using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace AICA.Core.LLM
{
    /// <summary>
    /// Native MCP tool definition from tools/list.
    /// </summary>
    public sealed class McpToolDefinition
    {
        public string Name { get; }
        public string Description { get; }
        public JsonElement InputSchema { get; }

        public McpToolDefinition(string name, string description, JsonElement inputSchema)
        {
            Name = name;
            Description = description;
            InputSchema = inputSchema;
        }
    }

    /// <summary>
    /// Result of an MCP tool call.
    /// </summary>
    public sealed class McpToolResult
    {
        public bool Success { get; }
        public string Content { get; }
        public string Error { get; }

        private McpToolResult(bool success, string content, string error)
        {
            Success = success;
            Content = content;
            Error = error;
        }

        public static McpToolResult Ok(string content) => new McpToolResult(true, content, null);
        public static McpToolResult Fail(string error) => new McpToolResult(false, null, error);
    }

    /// <summary>
    /// MCP JSON-RPC 2.0 client over stdin/stdout streams.
    /// Handles request/response matching via concurrent dictionary of TaskCompletionSource.
    /// Thread-safe: writes serialized via SemaphoreSlim, reads on background task.
    /// </summary>
    public sealed class McpClient : IDisposable
    {
        private readonly StreamWriter _writer;
        private readonly Stream _inputStream; // raw byte stream — avoids StreamReader byte/char mismatch
        private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);
        private int _nextId;
        private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending =
            new ConcurrentDictionary<int, TaskCompletionSource<JsonElement>>();
        private readonly CancellationTokenSource _readCts = new CancellationTokenSource();
        private readonly Task _readLoop;
        private volatile bool _disposed;

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = true
        };

        /// <summary>
        /// Create McpClient from process stdin/stdout streams.
        /// Starts background read loop immediately.
        /// Uses raw byte stream for reading to handle Content-Length (bytes) correctly
        /// with UTF-8 multi-byte characters.
        /// </summary>
        public McpClient(Stream processStdin, Stream processStdout)
        {
            if (processStdin == null) throw new ArgumentNullException(nameof(processStdin));
            if (processStdout == null) throw new ArgumentNullException(nameof(processStdout));

            _writer = new StreamWriter(processStdin, new UTF8Encoding(false)) { AutoFlush = true };
            _inputStream = processStdout;
            _readLoop = Task.Run(() => ReadLoopAsync(_readCts.Token));
        }

        /// <summary>
        /// Send MCP initialize handshake.
        /// </summary>
        public async Task InitializeAsync(CancellationToken ct)
        {
            var initParams = new Dictionary<string, object>
            {
                ["protocolVersion"] = "2024-11-05",
                ["capabilities"] = new Dictionary<string, object>(),
                ["clientInfo"] = new Dictionary<string, object>
                {
                    ["name"] = "AICA",
                    ["version"] = "1.3"
                }
            };

            await SendRequestAsync("initialize", initParams, ct).ConfigureAwait(false);

            // Send initialized notification (no id, no response expected)
            var notification = new Dictionary<string, object>
            {
                ["jsonrpc"] = "2.0",
                ["method"] = "notifications/initialized"
            };
            await WriteMessageAsync(notification, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Call an MCP tool and return the result.
        /// </summary>
        public async Task<McpToolResult> CallToolAsync(string toolName, Dictionary<string, object> arguments, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(toolName))
                throw new ArgumentException("Tool name required", nameof(toolName));

            try
            {
                var callParams = new Dictionary<string, object>
                {
                    ["name"] = toolName,
                    ["arguments"] = arguments ?? new Dictionary<string, object>()
                };

                var result = await SendRequestAsync("tools/call", callParams, ct).ConfigureAwait(false);

                // Extract text from result.content[0].text
                var content = ExtractContentText(result);
                return McpToolResult.Ok(content);
            }
            catch (McpException ex)
            {
                return McpToolResult.Fail(ex.Message);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return McpToolResult.Fail($"MCP call failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Retrieve native tool definitions from the MCP server via tools/list.
        /// </summary>
        public async Task<List<McpToolDefinition>> ListToolsAsync(CancellationToken ct)
        {
            var result = await SendRequestAsync("tools/list", new Dictionary<string, object>(), ct).ConfigureAwait(false);
            var tools = new List<McpToolDefinition>();

            if (result.ValueKind == JsonValueKind.Undefined)
                return tools;

            // MCP tools/list returns { tools: [ { name, description, inputSchema }, ... ] }
            if (result.TryGetProperty("tools", out var toolsArray) &&
                toolsArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in toolsArray.EnumerateArray())
                {
                    var name = item.TryGetProperty("name", out var n) ? n.GetString() : null;
                    if (string.IsNullOrEmpty(name)) continue;

                    var description = item.TryGetProperty("description", out var d) ? d.GetString() : "";
                    var inputSchema = item.TryGetProperty("inputSchema", out var s) ? s.Clone() : default;

                    tools.Add(new McpToolDefinition(name, description, inputSchema));
                }
            }

            return tools;
        }

        /// <summary>
        /// List available MCP resources via resources/list.
        /// </summary>
        public async Task<List<McpResourceInfo>> ListResourcesAsync(CancellationToken ct)
        {
            var result = await SendRequestAsync("resources/list", new Dictionary<string, object>(), ct).ConfigureAwait(false);
            var resources = new List<McpResourceInfo>();

            if (result.ValueKind == JsonValueKind.Undefined)
                return resources;

            if (result.TryGetProperty("resources", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray())
                {
                    var uri = item.TryGetProperty("uri", out var u) ? u.GetString() : null;
                    if (string.IsNullOrEmpty(uri)) continue;
                    var name = item.TryGetProperty("name", out var n) ? n.GetString() : uri;
                    var desc = item.TryGetProperty("description", out var d) ? d.GetString() : "";
                    resources.Add(new McpResourceInfo(uri, name, desc));
                }
            }

            return resources;
        }

        /// <summary>
        /// Read a single MCP resource by URI via resources/read.
        /// </summary>
        public async Task<string> ReadResourceAsync(string uri, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(uri))
                throw new ArgumentException("Resource URI required", nameof(uri));

            var readParams = new Dictionary<string, object> { ["uri"] = uri };
            var result = await SendRequestAsync("resources/read", readParams, ct).ConfigureAwait(false);

            // MCP resources/read returns { contents: [ { uri, text, mimeType } ] }
            if (result.TryGetProperty("contents", out var contents) && contents.ValueKind == JsonValueKind.Array)
            {
                var sb = new StringBuilder();
                foreach (var item in contents.EnumerateArray())
                {
                    if (item.TryGetProperty("text", out var textEl))
                    {
                        if (sb.Length > 0) sb.Append('\n');
                        sb.Append(textEl.GetString());
                    }
                }
                return sb.ToString();
            }

            return string.Empty;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _readCts.Cancel();

            // Fail all pending requests
            foreach (var kvp in _pending)
            {
                if (_pending.TryRemove(kvp.Key, out var tcs))
                {
                    tcs.TrySetCanceled();
                }
            }

            try { _writer?.Dispose(); } catch { }
            try { _inputStream?.Dispose(); } catch { }
            _writeLock?.Dispose();
            _readCts?.Dispose();
        }

        private async Task<JsonElement> SendRequestAsync(string method, object parameters, CancellationToken ct)
        {
            var id = Interlocked.Increment(ref _nextId);
            var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);

            using (ct.Register(() => tcs.TrySetCanceled(ct)))
            {
                _pending[id] = tcs;

                var request = new Dictionary<string, object>
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = id,
                    ["method"] = method,
                    ["params"] = parameters
                };

                await WriteMessageAsync(request, ct).ConfigureAwait(false);

                try
                {
                    return await tcs.Task.ConfigureAwait(false);
                }
                finally
                {
                    _pending.TryRemove(id, out _);
                }
            }
        }

        private async Task WriteMessageAsync(object message, CancellationToken ct)
        {
            var json = JsonSerializer.Serialize(message, JsonOptions);

            await _writeLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                // Use Content-Length framing (standard MCP transport)
                var bytes = Encoding.UTF8.GetBytes(json);
                await _writer.WriteAsync($"Content-Length: {bytes.Length}\r\n\r\n").ConfigureAwait(false);
                await _writer.WriteAsync(json).ConfigureAwait(false);
                await _writer.FlushAsync().ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        private async Task ReadLoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var message = await ReadMessageAsync(ct).ConfigureAwait(false);
                    if (message == null) break; // stream ended

                    ProcessResponse(message);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AICA] McpClient read loop error: {ex.Message}");
            }
            finally
            {
                // Fail all remaining pending on stream close
                foreach (var kvp in _pending)
                {
                    if (_pending.TryRemove(kvp.Key, out var tcs))
                    {
                        tcs.TrySetException(new McpException("MCP server disconnected"));
                    }
                }
            }
        }

        /// <summary>
        /// Read a single line (up to \n) from the raw byte stream. Returns null on EOF.
        /// </summary>
        private async Task<string> ReadLineFromStreamAsync()
        {
            var bytes = new List<byte>(256);
            var buf = new byte[1];
            while (true)
            {
                var read = await _inputStream.ReadAsync(buf, 0, 1).ConfigureAwait(false);
                if (read == 0) return bytes.Count > 0 ? Encoding.UTF8.GetString(bytes.ToArray()) : null;
                if (buf[0] == (byte)'\n')
                {
                    // Strip trailing \r
                    if (bytes.Count > 0 && bytes[bytes.Count - 1] == (byte)'\r')
                        bytes.RemoveAt(bytes.Count - 1);
                    return Encoding.UTF8.GetString(bytes.ToArray());
                }
                bytes.Add(buf[0]);
            }
        }

        /// <summary>
        /// Read exactly <paramref name="count"/> bytes from the raw stream. Returns null on premature EOF.
        /// </summary>
        private async Task<byte[]> ReadExactBytesAsync(int count)
        {
            var buffer = new byte[count];
            var totalRead = 0;
            while (totalRead < count)
            {
                var read = await _inputStream.ReadAsync(buffer, totalRead, count - totalRead).ConfigureAwait(false);
                if (read == 0) return null;
                totalRead += read;
            }
            return buffer;
        }

        private async Task<string> ReadMessageAsync(CancellationToken ct)
        {
            // Read from raw byte stream to avoid StreamReader byte/char mismatch.
            // Content-Length specifies bytes; reading chars via StreamReader would over-read
            // when the response contains multi-byte UTF-8 characters (e.g., Chinese text).
            var headerLine = await ReadLineFromStreamAsync().ConfigureAwait(false);
            if (headerLine == null) return null;

            if (headerLine.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            {
                const int MaxMessageSize = 10 * 1024 * 1024; // 10 MB sanity bound
                var lengthStr = headerLine.Substring("Content-Length:".Length).Trim();
                if (!int.TryParse(lengthStr, out var contentLength) || contentLength <= 0 || contentLength > MaxMessageSize)
                    return null;

                // Read empty line separator
                await ReadLineFromStreamAsync().ConfigureAwait(false);

                // Read exactly contentLength BYTES, then decode to string
                var rawBytes = await ReadExactBytesAsync(contentLength).ConfigureAwait(false);
                if (rawBytes == null) return null;
                return Encoding.UTF8.GetString(rawBytes);
            }

            // Newline-delimited: the line itself is the JSON message
            return string.IsNullOrWhiteSpace(headerLine) ? null : headerLine;
        }

        private void ProcessResponse(string json)
        {
            try
            {
                using (var doc = JsonDocument.Parse(json))
                {
                    var root = doc.RootElement;

                    // Skip notifications (no id)
                    if (!root.TryGetProperty("id", out var idElement))
                        return;

                    var id = idElement.GetInt32();

                    if (!_pending.TryRemove(id, out var tcs))
                    {
                        System.Diagnostics.Debug.WriteLine($"[AICA] McpClient orphaned response id={id}");
                        return;
                    }

                    // Check for error
                    if (root.TryGetProperty("error", out var errorElement))
                    {
                        var message = "MCP error";
                        if (errorElement.TryGetProperty("message", out var msgElement))
                            message = msgElement.GetString();
                        tcs.TrySetException(new McpException(message));
                        return;
                    }

                    // Extract result
                    if (root.TryGetProperty("result", out var resultElement))
                    {
                        tcs.TrySetResult(resultElement.Clone());
                    }
                    else
                    {
                        tcs.TrySetResult(default);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AICA] McpClient parse error: {ex.Message}");
            }
        }

        private static string ExtractContentText(JsonElement result)
        {
            if (result.ValueKind == JsonValueKind.Undefined)
                return string.Empty;

            // Standard MCP: result.content is array of {type, text}
            if (result.TryGetProperty("content", out var contentArray) &&
                contentArray.ValueKind == JsonValueKind.Array)
            {
                var sb = new StringBuilder();
                foreach (var item in contentArray.EnumerateArray())
                {
                    if (item.TryGetProperty("text", out var textElement))
                    {
                        if (sb.Length > 0) sb.Append('\n');
                        sb.Append(textElement.GetString());
                    }
                }
                return sb.ToString();
            }

            // Fallback: serialize the entire result
            return result.GetRawText();
        }
    }

    /// <summary>
    /// MCP resource metadata from resources/list.
    /// </summary>
    public sealed class McpResourceInfo
    {
        public string Uri { get; }
        public string Name { get; }
        public string Description { get; }

        public McpResourceInfo(string uri, string name, string description)
        {
            Uri = uri;
            Name = name;
            Description = description;
        }
    }

    /// <summary>
    /// Exception for MCP protocol errors.
    /// </summary>
    public class McpException : Exception
    {
        public McpException(string message) : base(message) { }
        public McpException(string message, Exception inner) : base(message, inner) { }
    }
}
