using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using AICA.Core.Agent;
using Xunit;

namespace AICA.Core.Tests.Agent
{
    public class AgentTelemetryTests
    {
        #region SessionRecord

        [Fact]
        public void SessionRecord_Immutable_PropertiesMatchConstructor()
        {
            var toolCounts = new Dictionary<string, int> { { "read_file", 3 }, { "edit", 1 } };
            var failCounts = new Dictionary<string, int> { { "edit", 1 } };
            var failReasons = new[] { "file not found" };

            var record = new SessionRecord(
                sessionId: "test123",
                timestamp: new DateTime(2026, 3, 23, 10, 0, 0, DateTimeKind.Utc),
                complexity: "Medium",
                intent: "modify",
                userMessageTokens: 150,
                iterations: 5,
                toolCalls: 4,
                toolCallCounts: toolCounts,
                toolFailCounts: failCounts,
                editAttempts: 2,
                editSuccesses: 1,
                editFailures: 1,
                editFailureReasons: failReasons,
                outcome: "completed",
                durationMs: 12345);

            Assert.Equal("test123", record.SessionId);
            Assert.Equal("Medium", record.Complexity);
            Assert.Equal("modify", record.Intent);
            Assert.Equal(150, record.UserMessageTokens);
            Assert.Equal(5, record.Iterations);
            Assert.Equal(4, record.ToolCalls);
            Assert.Equal(3, record.ToolCallCounts["read_file"]);
            Assert.Equal(1, record.ToolFailCounts["edit"]);
            Assert.Equal(2, record.EditAttempts);
            Assert.Equal(1, record.EditSuccesses);
            Assert.Equal(1, record.EditFailures);
            Assert.Single(record.EditFailureReasons);
            Assert.Equal("completed", record.Outcome);
            Assert.Equal(12345, record.DurationMs);
        }

        [Fact]
        public void SessionRecord_NullCollections_DefaultToEmpty()
        {
            var record = new SessionRecord(
                "id", DateTime.UtcNow, "Simple", "read",
                100, 1, 1, null, null, 0, 0, 0, null, "completed", 100);

            Assert.NotNull(record.ToolCallCounts);
            Assert.Empty(record.ToolCallCounts);
            Assert.NotNull(record.ToolFailCounts);
            Assert.Empty(record.ToolFailCounts);
            Assert.NotNull(record.EditFailureReasons);
            Assert.Empty(record.EditFailureReasons);
        }

        [Fact]
        public void SessionRecord_Serialize_Roundtrip()
        {
            var record = new SessionRecord(
                "abc", DateTime.UtcNow, "Complex", "analyze",
                200, 10, 8,
                new Dictionary<string, int> { { "grep_search", 5 } },
                new Dictionary<string, int>(),
                0, 0, 0, Array.Empty<string>(),
                "completed", 5000);

            var json = JsonSerializer.Serialize(record);
            Assert.Contains("abc", json);
            Assert.Contains("Complex", json);
            Assert.Contains("grep_search", json);
        }

        #endregion

        #region SessionRecordBuilder

        [Fact]
        public void Builder_RecordToolCall_TracksCountsCorrectly()
        {
            var builder = new SessionRecordBuilder();
            builder.RecordToolCall("read_file", true);
            builder.RecordToolCall("read_file", true);
            builder.RecordToolCall("edit", true);
            builder.RecordToolCall("edit", false);

            var state = new TaskState { Iteration = 3, TotalToolCallCount = 4 };
            var record = builder.Build(state, "completed");

            Assert.Equal(2, record.ToolCallCounts["read_file"]);
            Assert.Equal(2, record.ToolCallCounts["edit"]);
            Assert.Equal(1, record.ToolFailCounts["edit"]);
            Assert.Equal(2, record.EditAttempts);
            Assert.Equal(1, record.EditSuccesses);
            Assert.Equal(1, record.EditFailures);
        }

        [Fact]
        public void Builder_Build_UsesTaskState()
        {
            var builder = new SessionRecordBuilder { Complexity = "Simple", Intent = "read" };
            var state = new TaskState { Iteration = 7, TotalToolCallCount = 12 };
            var record = builder.Build(state, "timeout");

            Assert.Equal(7, record.Iterations);
            Assert.Equal(12, record.ToolCalls);
            Assert.Equal("timeout", record.Outcome);
        }

        #endregion

        #region AgentTelemetryWriter

        [Fact]
        public async Task WriteAsync_CreatesDirectory_AppendsJSONL()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "aica_test_" + Guid.NewGuid().ToString("N"));
            try
            {
                var writer = new AgentTelemetryWriter(tempDir);
                var record = new SessionRecord(
                    "test1", new DateTime(2026, 3, 23, 0, 0, 0, DateTimeKind.Utc),
                    "Simple", "read", 50, 1, 1,
                    new Dictionary<string, int>(), new Dictionary<string, int>(),
                    0, 0, 0, Array.Empty<string>(), "completed", 100);

                await writer.WriteAsync(record);

                var filePath = Path.Combine(tempDir, "2026-03-23.jsonl");
                Assert.True(File.Exists(filePath));

                var lines = File.ReadAllLines(filePath);
                Assert.Single(lines);
                Assert.Contains("test1", lines[0]);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task WriteAsync_MultipleRecords_OnePerLine()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "aica_test_" + Guid.NewGuid().ToString("N"));
            try
            {
                var writer = new AgentTelemetryWriter(tempDir);
                var ts = new DateTime(2026, 3, 23, 0, 0, 0, DateTimeKind.Utc);

                for (int i = 0; i < 3; i++)
                {
                    var record = new SessionRecord(
                        $"session_{i}", ts, "Simple", "read", 50, 1, 1,
                        new Dictionary<string, int>(), new Dictionary<string, int>(),
                        0, 0, 0, Array.Empty<string>(), "completed", 100);
                    await writer.WriteAsync(record);
                }

                var filePath = Path.Combine(tempDir, "2026-03-23.jsonl");
                var lines = File.ReadAllLines(filePath);
                Assert.Equal(3, lines.Length);
                Assert.Contains("session_0", lines[0]);
                Assert.Contains("session_1", lines[1]);
                Assert.Contains("session_2", lines[2]);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task WriteAsync_NullRecord_NoException()
        {
            var writer = new AgentTelemetryWriter(Path.GetTempPath());
            await writer.WriteAsync(null); // Should not throw
        }

        #endregion
    }
}
