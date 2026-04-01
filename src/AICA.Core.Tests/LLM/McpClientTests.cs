using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AICA.Core.LLM;
using Xunit;

namespace AICA.Core.Tests.LLM
{
    public class McpClientTests
    {
        /// <summary>
        /// Helper: create a pair of streams simulating process stdin/stdout.
        /// Write mock responses to serverWriter; client reads from clientStdout.
        /// </summary>
        private static (Stream clientStdin, Stream clientStdout, StreamWriter serverWriter) CreateMockStreams()
        {
            var clientStdin = new MemoryStream();
            var pipe = new MemoryStream();
            // We need a duplex pipe. Use a simple approach: write to pipe, client reads from pipe.
            // For testing, we use a blocking approach with a producer-consumer stream.
            var serverToClient = new BlockingStream();
            return (clientStdin, serverToClient, new StreamWriter(serverToClient.WriteEnd, new UTF8Encoding(false)) { AutoFlush = true });
        }

        [Fact]
        public async Task CallToolAsync_ValidResponse_ReturnsContent()
        {
            // Arrange: simulate MCP server responding to a tools/call request
            var (clientStdin, clientStdout, serverWriter) = CreateMockStreams();

            using (var client = new McpClient(clientStdin, clientStdout))
            {
                // Server responds after a short delay
                var responseTask = Task.Run(async () =>
                {
                    await Task.Delay(50);
                    var response = JsonSerializer.Serialize(new
                    {
                        jsonrpc = "2.0",
                        id = 1,
                        result = new
                        {
                            content = new[]
                            {
                                new { type = "text", text = "CChannel has 5 callers and 3 callees." }
                            }
                        }
                    });
                    var bytes = Encoding.UTF8.GetBytes(response);
                    serverWriter.Write($"Content-Length: {bytes.Length}\r\n\r\n");
                    serverWriter.Write(response);
                    serverWriter.Flush();
                });

                // Act
                using (var cts = new CancellationTokenSource(5000))
                {
                    var result = await client.CallToolAsync("context", new Dictionary<string, object>
                    {
                        ["name"] = "CChannel"
                    }, cts.Token);

                    // Assert
                    Assert.True(result.Success);
                    Assert.Contains("CChannel has 5 callers", result.Content);
                }
            }
        }

        [Fact]
        public async Task CallToolAsync_ErrorResponse_ReturnsFail()
        {
            var (clientStdin, clientStdout, serverWriter) = CreateMockStreams();

            using (var client = new McpClient(clientStdin, clientStdout))
            {
                var responseTask = Task.Run(async () =>
                {
                    await Task.Delay(50);
                    var response = JsonSerializer.Serialize(new
                    {
                        jsonrpc = "2.0",
                        id = 1,
                        error = new { code = -32601, message = "Tool not found: unknown_tool" }
                    });
                    var bytes = Encoding.UTF8.GetBytes(response);
                    serverWriter.Write($"Content-Length: {bytes.Length}\r\n\r\n");
                    serverWriter.Write(response);
                    serverWriter.Flush();
                });

                using (var cts = new CancellationTokenSource(5000))
                {
                    var result = await client.CallToolAsync("unknown_tool", new Dictionary<string, object>(), cts.Token);

                    Assert.False(result.Success);
                    Assert.Contains("Tool not found", result.Error);
                }
            }
        }

        [Fact]
        public async Task CallToolAsync_Timeout_ThrowsOperationCanceled()
        {
            var clientStdin = new MemoryStream();
            var clientStdout = new BlockingStream(); // Never writes — will timeout

            using (var client = new McpClient(clientStdin, clientStdout))
            using (var cts = new CancellationTokenSource(200))
            {
                // TaskCanceledException inherits from OperationCanceledException
                await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    () => client.CallToolAsync("context", new Dictionary<string, object>(), cts.Token));
            }
        }

        [Fact]
        public async Task CallToolAsync_MultipleContentItems_ConcatenatesText()
        {
            var (clientStdin, clientStdout, serverWriter) = CreateMockStreams();

            using (var client = new McpClient(clientStdin, clientStdout))
            {
                var responseTask = Task.Run(async () =>
                {
                    await Task.Delay(50);
                    var response = JsonSerializer.Serialize(new
                    {
                        jsonrpc = "2.0",
                        id = 1,
                        result = new
                        {
                            content = new[]
                            {
                                new { type = "text", text = "Part 1" },
                                new { type = "text", text = "Part 2" }
                            }
                        }
                    });
                    var bytes = Encoding.UTF8.GetBytes(response);
                    serverWriter.Write($"Content-Length: {bytes.Length}\r\n\r\n");
                    serverWriter.Write(response);
                    serverWriter.Flush();
                });

                using (var cts = new CancellationTokenSource(5000))
                {
                    var result = await client.CallToolAsync("query", new Dictionary<string, object>
                    {
                        ["query"] = "test"
                    }, cts.Token);

                    Assert.True(result.Success);
                    Assert.Contains("Part 1", result.Content);
                    Assert.Contains("Part 2", result.Content);
                }
            }
        }

        [Fact]
        public void McpToolResult_Ok_HasCorrectProperties()
        {
            var result = McpToolResult.Ok("test content");
            Assert.True(result.Success);
            Assert.Equal("test content", result.Content);
            Assert.Null(result.Error);
        }

        [Fact]
        public void McpToolResult_Fail_HasCorrectProperties()
        {
            var result = McpToolResult.Fail("something went wrong");
            Assert.False(result.Success);
            Assert.Null(result.Content);
            Assert.Equal("something went wrong", result.Error);
        }

        [Fact]
        public async Task Dispose_CancelsPendingRequests()
        {
            var clientStdin = new MemoryStream();
            var clientStdout = new BlockingStream();

            var client = new McpClient(clientStdin, clientStdout);
            var callTask = client.CallToolAsync("context", new Dictionary<string, object>(), CancellationToken.None);

            // Dispose while request is pending
            client.Dispose();

            // The task should complete with cancellation (TaskCanceledException inherits OperationCanceledException)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => callTask);
        }
    }

    /// <summary>
    /// A stream that blocks on reads until data is written (simple producer-consumer for testing).
    /// </summary>
    internal class BlockingStream : Stream
    {
        private readonly System.Collections.Concurrent.BlockingCollection<byte[]> _buffer =
            new System.Collections.Concurrent.BlockingCollection<byte[]>();
        private byte[] _current;
        private int _currentOffset;

        /// <summary>
        /// Write end — a separate stream that feeds data into this BlockingStream.
        /// </summary>
        public Stream WriteEnd { get; }

        public BlockingStream()
        {
            WriteEnd = new WriteEndStream(this);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_current != null && _currentOffset < _current.Length)
            {
                var toCopy = Math.Min(count, _current.Length - _currentOffset);
                Buffer.BlockCopy(_current, _currentOffset, buffer, offset, toCopy);
                _currentOffset += toCopy;
                if (_currentOffset >= _current.Length) _current = null;
                return toCopy;
            }

            try
            {
                _current = _buffer.Take();
                _currentOffset = 0;
                return Read(buffer, offset, count);
            }
            catch (InvalidOperationException)
            {
                return 0; // collection completed
            }
        }

        internal void AddData(byte[] data)
        {
            _buffer.Add(data);
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private class WriteEndStream : Stream
        {
            private readonly BlockingStream _parent;
            public WriteEndStream(BlockingStream parent) { _parent = parent; }
            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Flush() { }
            public override void Write(byte[] buffer, int offset, int count)
            {
                var data = new byte[count];
                Buffer.BlockCopy(buffer, offset, data, 0, count);
                _parent.AddData(data);
            }
        }
    }
}
