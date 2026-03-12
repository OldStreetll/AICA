using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AICA.Core.Agent;
using AICA.Core.Tools;
using Moq;
using Xunit;

namespace AICA.Core.Tests.Agent
{
    public class MiddlewareTests
    {
        private Mock<IAgentContext> CreateMockContext()
        {
            var context = new Mock<IAgentContext>();
            context.Setup(c => c.WorkingDirectory).Returns("/workspace");
            context.Setup(c => c.IsPathAccessible(It.IsAny<string>())).Returns(true);
            context.Setup(c => c.FileExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            context.Setup(c => c.ReadFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("file content");
            return context;
        }

        private Mock<IUIContext> CreateMockUIContext()
        {
            return new Mock<IUIContext>();
        }

        [Fact]
        public async Task LoggingMiddleware_LogsToolExecution()
        {
            // Arrange
            var logs = new List<string>();
            var middleware = new LoggingMiddleware(logs);
            var pipeline = new ToolExecutionPipeline();
            pipeline.Use(middleware);

            var tool = new ReadFileTool();
            var context = CreateMockContext();
            var uiContext = CreateMockUIContext();
            var call = new ToolCall
            {
                Id = "1",
                Name = "read_file",
                Arguments = new Dictionary<string, object> { ["path"] = "test.txt" }
            };

            // Act
            await pipeline.ExecuteAsync(call, tool, context.Object, uiContext.Object);

            // Assert
            Assert.Contains(logs, log => log.Contains("read_file"));
            Assert.Contains(logs, log => log.Contains("started"));
            Assert.Contains(logs, log => log.Contains("completed"));
        }

        [Fact]
        public async Task TimeoutMiddleware_EnforcesTimeout()
        {
            // Arrange
            var middleware = new TimeoutMiddleware(TimeSpan.FromMilliseconds(100));
            var pipeline = new ToolExecutionPipeline();
            pipeline.Use(middleware);

            var tool = new SlowTool(TimeSpan.FromSeconds(5));
            var context = CreateMockContext();
            var uiContext = CreateMockUIContext();
            var call = new ToolCall
            {
                Id = "1",
                Name = "slow_tool",
                Arguments = new Dictionary<string, object>()
            };

            // Act & Assert
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            {
                await pipeline.ExecuteAsync(call, tool, context.Object, uiContext.Object);
            });
        }

        [Fact]
        public async Task PermissionCheckMiddleware_BlocksUnauthorizedTools()
        {
            // Arrange
            var middleware = new PermissionCheckMiddleware(new HashSet<string> { "read_file" });
            var pipeline = new ToolExecutionPipeline();
            pipeline.Use(middleware);

            var tool = new WriteFileTool();
            var context = CreateMockContext();
            var uiContext = CreateMockUIContext();
            var call = new ToolCall
            {
                Id = "1",
                Name = "write_to_file",
                Arguments = new Dictionary<string, object>
                {
                    ["path"] = "test.txt",
                    ["content"] = "content"
                }
            };

            // Act
            var result = await pipeline.ExecuteAsync(call, tool, context.Object, uiContext.Object);

            // Assert
            Assert.False(result.Success);
            Assert.Contains("permission denied", result.Error.ToLower());
        }

        [Fact]
        public async Task PermissionCheckMiddleware_AllowsAuthorizedTools()
        {
            // Arrange
            var middleware = new PermissionCheckMiddleware(new HashSet<string> { "read_file" });
            var pipeline = new ToolExecutionPipeline();
            pipeline.Use(middleware);

            var tool = new ReadFileTool();
            var context = CreateMockContext();
            var uiContext = CreateMockUIContext();
            var call = new ToolCall
            {
                Id = "1",
                Name = "read_file",
                Arguments = new Dictionary<string, object> { ["path"] = "test.txt" }
            };

            // Act
            var result = await pipeline.ExecuteAsync(call, tool, context.Object, uiContext.Object);

            // Assert
            Assert.True(result.Success);
        }

        [Fact]
        public async Task MonitoringMiddleware_TracksExecutionMetrics()
        {
            // Arrange
            var metrics = new Dictionary<string, int>();
            var middleware = new MonitoringMiddleware(metrics);
            var pipeline = new ToolExecutionPipeline();
            pipeline.Use(middleware);

            var tool = new ReadFileTool();
            var context = CreateMockContext();
            var uiContext = CreateMockUIContext();
            var call = new ToolCall
            {
                Id = "1",
                Name = "read_file",
                Arguments = new Dictionary<string, object> { ["path"] = "test.txt" }
            };

            // Act
            await pipeline.ExecuteAsync(call, tool, context.Object, uiContext.Object);

            // Assert
            Assert.True(metrics.ContainsKey("read_file"));
            Assert.Equal(1, metrics["read_file"]);
        }

        [Fact]
        public async Task MultipleMiddleware_ExecuteInOrder()
        {
            // Arrange
            var executionOrder = new List<string>();
            var middleware1 = new OrderTrackingMiddleware("first", executionOrder);
            var middleware2 = new OrderTrackingMiddleware("second", executionOrder);
            var middleware3 = new OrderTrackingMiddleware("third", executionOrder);

            var pipeline = new ToolExecutionPipeline();
            pipeline.Use(middleware1);
            pipeline.Use(middleware2);
            pipeline.Use(middleware3);

            var tool = new ReadFileTool();
            var context = CreateMockContext();
            var uiContext = CreateMockUIContext();
            var call = new ToolCall
            {
                Id = "1",
                Name = "read_file",
                Arguments = new Dictionary<string, object> { ["path"] = "test.txt" }
            };

            // Act
            await pipeline.ExecuteAsync(call, tool, context.Object, uiContext.Object);

            // Assert
            Assert.Equal(3, executionOrder.Count);
            Assert.Equal("first", executionOrder[0]);
            Assert.Equal("second", executionOrder[1]);
            Assert.Equal("third", executionOrder[2]);
        }

        [Fact]
        public async Task ErrorHandlingMiddleware_CatchesAndClassifiesExceptions()
        {
            // Arrange
            var middleware = new ErrorHandlingMiddleware();
            var pipeline = new ToolExecutionPipeline();
            pipeline.Use(middleware);

            var tool = new FailingTool();
            var context = CreateMockContext();
            var uiContext = CreateMockUIContext();
            var call = new ToolCall
            {
                Id = "1",
                Name = "failing_tool",
                Arguments = new Dictionary<string, object>()
            };

            // Act
            var result = await pipeline.ExecuteAsync(call, tool, context.Object, uiContext.Object);

            // Assert
            Assert.False(result.Success);
            Assert.Contains("execution failed", result.Error.ToLower());
        }

        // Helper middleware implementations for testing

        private class LoggingMiddleware : IToolExecutionMiddleware
        {
            private readonly List<string> _logs;

            public LoggingMiddleware(List<string> logs)
            {
                _logs = logs;
            }

            public async Task<ToolResult> ProcessAsync(ToolExecutionContext context, CancellationToken ct)
            {
                _logs.Add($"Tool {context.Call.Name} started");
                var result = await context.Next(ct);
                _logs.Add($"Tool {context.Call.Name} completed");
                return result;
            }
        }

        private class TimeoutMiddleware : IToolExecutionMiddleware
        {
            private readonly TimeSpan _timeout;

            public TimeoutMiddleware(TimeSpan timeout)
            {
                _timeout = timeout;
            }

            public async Task<ToolResult> ProcessAsync(ToolExecutionContext context, CancellationToken ct)
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(_timeout);
                return await context.Next(cts.Token);
            }
        }

        private class PermissionCheckMiddleware : IToolExecutionMiddleware
        {
            private readonly HashSet<string> _allowedTools;

            public PermissionCheckMiddleware(HashSet<string> allowedTools)
            {
                _allowedTools = allowedTools;
            }

            public Task<ToolResult> ProcessAsync(ToolExecutionContext context, CancellationToken ct)
            {
                if (!_allowedTools.Contains(context.Call.Name))
                {
                    return Task.FromResult(ToolResult.Fail($"Permission denied for tool: {context.Call.Name}"));
                }
                return context.Next(ct);
            }
        }

        private class MonitoringMiddleware : IToolExecutionMiddleware
        {
            private readonly Dictionary<string, int> _metrics;

            public MonitoringMiddleware(Dictionary<string, int> metrics)
            {
                _metrics = metrics;
            }

            public async Task<ToolResult> ProcessAsync(ToolExecutionContext context, CancellationToken ct)
            {
                if (!_metrics.ContainsKey(context.Call.Name))
                    _metrics[context.Call.Name] = 0;
                _metrics[context.Call.Name]++;

                return await context.Next(ct);
            }
        }

        private class OrderTrackingMiddleware : IToolExecutionMiddleware
        {
            private readonly string _name;
            private readonly List<string> _executionOrder;

            public OrderTrackingMiddleware(string name, List<string> executionOrder)
            {
                _name = name;
                _executionOrder = executionOrder;
            }

            public async Task<ToolResult> ProcessAsync(ToolExecutionContext context, CancellationToken ct)
            {
                _executionOrder.Add(_name);
                return await context.Next(ct);
            }
        }

        private class ErrorHandlingMiddleware : IToolExecutionMiddleware
        {
            public async Task<ToolResult> ProcessAsync(ToolExecutionContext context, CancellationToken ct)
            {
                try
                {
                    return await context.Next(ct);
                }
                catch (Exception ex)
                {
                    return ToolResult.Fail($"Execution failed: {ex.Message}");
                }
            }
        }

        // Helper tools for testing

        private class SlowTool : IAgentTool
        {
            private readonly TimeSpan _delay;

            public SlowTool(TimeSpan delay)
            {
                _delay = delay;
            }

            public string Name => "slow_tool";
            public string Description => "A tool that takes a long time";

            public ToolDefinition GetDefinition()
            {
                return new ToolDefinition
                {
                    Name = Name,
                    Description = Description,
                    Parameters = new ToolParameters { Type = "object", Properties = new Dictionary<string, ToolParameterProperty>() }
                };
            }

            public async Task<ToolResult> ExecuteAsync(ToolCall call, IAgentContext context, IUIContext uiContext, CancellationToken ct)
            {
                await Task.Delay(_delay, ct);
                return ToolResult.Ok("Done");
            }

            public Task HandlePartialAsync(ToolCall call, IUIContext ui, CancellationToken ct)
            {
                return Task.CompletedTask;
            }

            public ToolMetadata GetMetadata()
            {
                return new ToolMetadata
                {
                    Name = Name,
                    Description = Description,
                    Category = ToolCategory.Analysis
                };
            }
        }

        private class FailingTool : IAgentTool
        {
            public string Name => "failing_tool";
            public string Description => "A tool that always fails";

            public ToolDefinition GetDefinition()
            {
                return new ToolDefinition
                {
                    Name = Name,
                    Description = Description,
                    Parameters = new ToolParameters { Type = "object", Properties = new Dictionary<string, ToolParameterProperty>() }
                };
            }

            public Task<ToolResult> ExecuteAsync(ToolCall call, IAgentContext context, IUIContext uiContext, CancellationToken ct)
            {
                throw new InvalidOperationException("Tool execution failed");
            }

            public Task HandlePartialAsync(ToolCall call, IUIContext ui, CancellationToken ct)
            {
                return Task.CompletedTask;
            }

            public ToolMetadata GetMetadata()
            {
                return new ToolMetadata
                {
                    Name = Name,
                    Description = Description,
                    Category = ToolCategory.Analysis
                };
            }
        }
    }
}
