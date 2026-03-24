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
        private readonly StreamReader _reader;
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
        /// </summary>
        public McpClient(Stream processStdin, Stream processStdout)
        {
            if (processStdin == null) throw new ArgumentNullException(nameof(processStdin));
            if (processStdout == null) throw new ArgumentNullException(nameof(processStdout));

            _writer = new StreamWriter(processStdin, new UTF8Encoding(false)) { AutoFlush = true };
            _reader = new StreamReader(processStdout, Encoding.UTF8);
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
            try { _reader?.Dispose(); } catch { }
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

        private async Task<string> ReadMessageAsync(CancellationToken ct)
        {
            // Try Content-Length framing first, fall back to newline-delimited
            var headerLine = await _reader.ReadLineAsync().ConfigureAwait(false);
            if (headerLine == null) return null;

            if (headerLine.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            {
                const int MaxMessageSize = 10 * 1024 * 1024; // 10 MB sanity bound
                var lengthStr = headerLine.Substring("Content-Length:".Length).Trim();
                if (!int.TryParse(lengthStr, out var contentLength) || contentLength <= 0 || contentLength > MaxMessageSize)
                    return null;

                // Read empty line separator
                await _reader.ReadLineAsync().ConfigureAwait(false);

                // Read exact content
                var buffer = new char[contentLength];
                var totalRead = 0;
                while (totalRead < contentLength)
                {
                    var read = await _reader.ReadAsync(buffer, totalRead, contentLength - totalRead).ConfigureAwait(false);
                    if (read == 0) return null;
                    totalRead += read;
                }
                return new string(buffer, 0, totalRead);
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
    /// Exception for MCP protocol errors.
    /// </summary>
    public class McpException : Exception
    {
        public McpException(string message) : base(message) { }
        public McpException(string message, Exception inner) : base(message, inner) { }
    }
}
