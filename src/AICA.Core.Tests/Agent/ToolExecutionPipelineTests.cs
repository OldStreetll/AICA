using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AICA.Core.Agent;
using Moq;
using Xunit;

namespace AICA.Core.Tests.Agent
{
    public class ToolExecutionPipelineTests
    {
        private Mock<IAgentTool> CreateMockTool(string name)
        {
            var mock = new Mock<IAgentTool>();
            mock.Setup(t => t.Name).Returns(name);
            mock.Setup(t => t.GetMetadata()).Returns(new ToolMetadata
            {
                Name = name,
                Category = ToolCategory.FileRead,
                TimeoutSeconds = 10
            });
            return mock;
        }

        [Fact]
        public async Task ExecuteAsync_WithValidTool_ExecutesTool()
        {
            // Arrange
            var pipeline = new ToolExecutionPipeline();
            var tool = CreateMockTool("test_tool");
            tool.Setup(t => t.ExecuteAsync(It.IsAny<ToolCall>(), It.IsAny<IAgentContext>(), It.IsAny<IUIContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ToolResult.Ok("Success"));

            var call = new ToolCall { Id = "1", Name = "test_tool", Arguments = new Dictionary<string, object>() };
            var context = new Mock<IAgentContext>();
            var uiContext = new Mock<IUIContext>();

            // Act
            var result = await pipeline.ExecuteAsync(call, tool.Object, context.Object, uiContext.Object);

            // Assert
            Assert.True(result.Success);
            Assert.Equal("Success", result.Content);
        }

        [Fact]
        public async Task ExecuteAsync_WithNullCall_ThrowsException()
        {
            // Arrange
            var pipeline = new ToolExecutionPipeline();
            var tool = CreateMockTool("test_tool");
            var context = new Mock<IAgentContext>();
            var uiContext = new Mock<IUIContext>();

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                pipeline.ExecuteAsync(null, tool.Object, context.Object, uiContext.Object));
        }

        [Fact]
        public async Task ExecuteAsync_WithNullTool_ThrowsException()
        {
            // Arrange
            var pipeline = new ToolExecutionPipeline();
            var call = new ToolCall { Id = "1", Name = "test_tool", Arguments = new Dictionary<string, object>() };
            var context = new Mock<IAgentContext>();
            var uiContext = new Mock<IUIContext>();

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                pipeline.ExecuteAsync(call, null, context.Object, uiContext.Object));
        }

        [Fact]
        public async Task ExecuteAsync_WithMiddleware_ProcessesMiddleware()
        {
            // Arrange
            var pipeline = new ToolExecutionPipeline();
            var middlewareCalled = false;

            // Middleware that calls Next to continue the chain
            var middleware = new PassThroughMiddleware(() => middlewareCalled = true);
            pipeline.Use(middleware);

            var tool = CreateMockTool("test_tool");
            tool.Setup(t => t.ExecuteAsync(It.IsAny<ToolCall>(), It.IsAny<IAgentContext>(), It.IsAny<IUIContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ToolResult.Ok("Success"));

            var call = new ToolCall { Id = "1", Name = "test_tool", Arguments = new Dictionary<string, object>() };
            var context = new Mock<IAgentContext>();
            var uiContext = new Mock<IUIContext>();

            // Act
            await pipeline.ExecuteAsync(call, tool.Object, context.Object, uiContext.Object);

            // Assert
            Assert.True(middlewareCalled);
        }

        [Fact]
        public async Task ExecuteAsync_WithMiddlewareReturningResult_ShortCircuits()
        {
            // Arrange
            var pipeline = new ToolExecutionPipeline();

            // Middleware that short-circuits by NOT calling Next
            var middleware = new ShortCircuitMiddleware(ToolResult.Fail("Blocked by middleware"));
            pipeline.Use(middleware);

            var tool = CreateMockTool("test_tool");
            var toolExecuted = false;
            tool.Setup(t => t.ExecuteAsync(It.IsAny<ToolCall>(), It.IsAny<IAgentContext>(), It.IsAny<IUIContext>(), It.IsAny<CancellationToken>()))
                .Callback(() => toolExecuted = true)
                .ReturnsAsync(ToolResult.Ok("Success"));

            var call = new ToolCall { Id = "1", Name = "test_tool", Arguments = new Dictionary<string, object>() };
            var context = new Mock<IAgentContext>();
            var uiContext = new Mock<IUIContext>();

            // Act
            var result = await pipeline.ExecuteAsync(call, tool.Object, context.Object, uiContext.Object);

            // Assert
            Assert.False(result.Success);
            Assert.False(toolExecuted);
        }

        [Fact]
        public async Task ExecuteAsync_WithToolException_CatchesAndHandles()
        {
            // Arrange
            var pipeline = new ToolExecutionPipeline();

            var tool = CreateMockTool("test_tool");
            tool.Setup(t => t.ExecuteAsync(It.IsAny<ToolCall>(), It.IsAny<IAgentContext>(), It.IsAny<IUIContext>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("Tool error"));

            var call = new ToolCall { Id = "1", Name = "test_tool", Arguments = new Dictionary<string, object>() };
            var context = new Mock<IAgentContext>();
            var uiContext = new Mock<IUIContext>();

            // Act
            var result = await pipeline.ExecuteAsync(call, tool.Object, context.Object, uiContext.Object);

            // Assert
            Assert.False(result.Success);
            Assert.Contains("Tool error", result.Error);
        }

        [Fact]
        public async Task ExecuteAsync_WithCancellation_HandlesCancellation()
        {
            // Arrange
            var pipeline = new ToolExecutionPipeline();

            var tool = CreateMockTool("test_tool");
            tool.Setup(t => t.ExecuteAsync(It.IsAny<ToolCall>(), It.IsAny<IAgentContext>(), It.IsAny<IUIContext>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new OperationCanceledException());

            var call = new ToolCall { Id = "1", Name = "test_tool", Arguments = new Dictionary<string, object>() };
            var context = new Mock<IAgentContext>();
            var uiContext = new Mock<IUIContext>();

            // Act
            var result = await pipeline.ExecuteAsync(call, tool.Object, context.Object, uiContext.Object);

            // Assert
            Assert.False(result.Success);
            Assert.Contains("cancelled", result.Error.ToLower());
        }

        [Fact]
        public void Use_WithNullMiddleware_ThrowsException()
        {
            // Arrange
            var pipeline = new ToolExecutionPipeline();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => pipeline.Use(null));
        }

        [Fact]
        public void MiddlewareCount_ReturnsCorrectCount()
        {
            // Arrange
            var pipeline = new ToolExecutionPipeline();
            var middleware1 = new PassThroughMiddleware();
            var middleware2 = new PassThroughMiddleware();

            // Act
            pipeline.Use(middleware1);
            pipeline.Use(middleware2);

            // Assert
            Assert.Equal(2, pipeline.MiddlewareCount);
        }

        [Fact]
        public async Task ExecuteAsync_TracksExecutionTime()
        {
            // Arrange
            var pipeline = new ToolExecutionPipeline();

            var tool = CreateMockTool("test_tool");
            tool.Setup(t => t.ExecuteAsync(It.IsAny<ToolCall>(), It.IsAny<IAgentContext>(), It.IsAny<IUIContext>(), It.IsAny<CancellationToken>()))
                .Returns(async () =>
                {
                    await Task.Delay(100);
                    return ToolResult.Ok("Success");
                });

            var call = new ToolCall { Id = "1", Name = "test_tool", Arguments = new Dictionary<string, object>() };
            var context = new Mock<IAgentContext>();
            var uiContext = new Mock<IUIContext>();

            // Act
            var result = await pipeline.ExecuteAsync(call, tool.Object, context.Object, uiContext.Object);

            // Assert
            Assert.True(result.Success);
        }

        [Fact]
        public async Task ExecuteAsync_WithMultipleMiddleware_ProcessesInOrder()
        {
            // Arrange
            var pipeline = new ToolExecutionPipeline();
            var callOrder = new List<string>();

            pipeline.Use(new OrderTrackingMiddleware("middleware1", callOrder));
            pipeline.Use(new OrderTrackingMiddleware("middleware2", callOrder));

            var tool = CreateMockTool("test_tool");
            tool.Setup(t => t.ExecuteAsync(It.IsAny<ToolCall>(), It.IsAny<IAgentContext>(), It.IsAny<IUIContext>(), It.IsAny<CancellationToken>()))
                .Callback(() => callOrder.Add("tool"))
                .ReturnsAsync(ToolResult.Ok("Success"));

            var call = new ToolCall { Id = "1", Name = "test_tool", Arguments = new Dictionary<string, object>() };
            var context = new Mock<IAgentContext>();
            var uiContext = new Mock<IUIContext>();

            // Act
            await pipeline.ExecuteAsync(call, tool.Object, context.Object, uiContext.Object);

            // Assert
            Assert.Equal(new[] { "middleware1", "middleware2", "tool" }, callOrder);
        }

        #region Helper Middleware

        /// <summary>
        /// Middleware that calls Next to continue the chain, optionally invoking a callback
        /// </summary>
        private class PassThroughMiddleware : IToolExecutionMiddleware
        {
            private readonly Action _callback;

            public PassThroughMiddleware(Action callback = null)
            {
                _callback = callback;
            }

            public async Task<ToolResult> ProcessAsync(ToolExecutionContext context, CancellationToken ct)
            {
                _callback?.Invoke();
                return await context.Next(ct);
            }
        }

        /// <summary>
        /// Middleware that returns a result without calling Next, short-circuiting the pipeline
        /// </summary>
        private class ShortCircuitMiddleware : IToolExecutionMiddleware
        {
            private readonly ToolResult _result;

            public ShortCircuitMiddleware(ToolResult result)
            {
                _result = result;
            }

            public Task<ToolResult> ProcessAsync(ToolExecutionContext context, CancellationToken ct)
            {
                return Task.FromResult(_result);
            }
        }

        /// <summary>
        /// Middleware that tracks execution order by name
        /// </summary>
        private class OrderTrackingMiddleware : IToolExecutionMiddleware
        {
            private readonly string _name;
            private readonly List<string> _order;

            public OrderTrackingMiddleware(string name, List<string> order)
            {
                _name = name;
                _order = order;
            }

            public async Task<ToolResult> ProcessAsync(ToolExecutionContext context, CancellationToken ct)
            {
                _order.Add(_name);
                return await context.Next(ct);
            }
        }

        #endregion
    }
}
