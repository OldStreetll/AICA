using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AICA.Core.Agent
{
    /// <summary>
    /// Records a single condensation event during agent execution.
    /// </summary>
    public class CondenseEvent
    {
        public DateTime Timestamp { get; }
        public int MessagesBefore { get; }
        public int MessagesAfter { get; }
        public int SummaryTokenEstimate { get; }
        public string TriggerReason { get; }

        public CondenseEvent(int messagesBefore, int messagesAfter, int summaryTokenEstimate, string triggerReason)
        {
            Timestamp = DateTime.UtcNow;
            MessagesBefore = messagesBefore;
            MessagesAfter = messagesAfter;
            SummaryTokenEstimate = summaryTokenEstimate;
            TriggerReason = triggerReason;
        }
    }

    /// <summary>
    /// H4: Immutable session-level telemetry record.
    /// One record per agent execution, written to JSONL file.
    /// </summary>
    public class SessionRecord
    {
        public string SessionId { get; }
        public DateTime Timestamp { get; }
        public string Complexity { get; }
        public string Intent { get; }
        public int UserMessageTokens { get; }
        public int Iterations { get; }
        public int ToolCalls { get; }
        public IReadOnlyDictionary<string, int> ToolCallCounts { get; }
        public IReadOnlyDictionary<string, int> ToolFailCounts { get; }
        public int EditAttempts { get; }
        public int EditSuccesses { get; }
        public int EditFailures { get; }
        public IReadOnlyList<string> EditFailureReasons { get; }
        public IReadOnlyList<CondenseEvent> CondenseEvents { get; }
        public int TotalPromptTokens { get; }
        public int TotalCompletionTokens { get; }
        public IReadOnlyDictionary<string, int> FuzzyMatchDistribution { get; }
        public string Outcome { get; }
        public int DurationMs { get; }

        public SessionRecord(
            string sessionId,
            DateTime timestamp,
            string complexity,
            string intent,
            int userMessageTokens,
            int iterations,
            int toolCalls,
            IReadOnlyDictionary<string, int> toolCallCounts,
            IReadOnlyDictionary<string, int> toolFailCounts,
            int editAttempts,
            int editSuccesses,
            int editFailures,
            IReadOnlyList<string> editFailureReasons,
            IReadOnlyList<CondenseEvent> condenseEvents,
            int totalPromptTokens,
            int totalCompletionTokens,
            IReadOnlyDictionary<string, int> fuzzyMatchDistribution,
            string outcome,
            int durationMs)
        {
            SessionId = sessionId;
            Timestamp = timestamp;
            Complexity = complexity;
            Intent = intent;
            UserMessageTokens = userMessageTokens;
            Iterations = iterations;
            ToolCalls = toolCalls;
            ToolCallCounts = toolCallCounts ?? new Dictionary<string, int>();
            ToolFailCounts = toolFailCounts ?? new Dictionary<string, int>();
            EditAttempts = editAttempts;
            EditSuccesses = editSuccesses;
            EditFailures = editFailures;
            EditFailureReasons = editFailureReasons ?? Array.Empty<string>();
            CondenseEvents = condenseEvents ?? Array.Empty<CondenseEvent>();
            TotalPromptTokens = totalPromptTokens;
            TotalCompletionTokens = totalCompletionTokens;
            FuzzyMatchDistribution = fuzzyMatchDistribution ?? new Dictionary<string, int>();
            Outcome = outcome;
            DurationMs = durationMs;
        }
    }

    /// <summary>
    /// H4: Mutable builder for collecting telemetry data during agent execution.
    /// </summary>
    public class SessionRecordBuilder
    {
        private readonly Dictionary<string, int> _toolCallCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _toolFailCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _editFailureReasons = new List<string>();
        private readonly List<CondenseEvent> _condenseEvents = new List<CondenseEvent>();
        private readonly Dictionary<string, int> _fuzzyMatchDistribution = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private int _totalPromptTokens;
        private int _totalCompletionTokens;

        public string SessionId { get; set; } = Guid.NewGuid().ToString("N");
        public DateTime StartTime { get; set; } = DateTime.UtcNow;
        public string Complexity { get; set; } = "Simple";
        public string Intent { get; set; } = "unknown";
        public int UserMessageTokens { get; set; }
        public int EditAttempts { get; set; }
        public int EditSuccesses { get; set; }
        public int EditFailures { get; set; }

        public void RecordCondenseEvent(int messagesBefore, int messagesAfter, int summaryTokenEstimate, string triggerReason)
        {
            _condenseEvents.Add(new CondenseEvent(messagesBefore, messagesAfter, summaryTokenEstimate, triggerReason));
        }

        public void RecordTokenUsage(int promptTokens, int completionTokens)
        {
            _totalPromptTokens += promptTokens;
            _totalCompletionTokens += completionTokens;
        }

        public void RecordFuzzyMatchLevel(string level)
        {
            if (string.IsNullOrEmpty(level)) return;
            if (!_fuzzyMatchDistribution.ContainsKey(level))
                _fuzzyMatchDistribution[level] = 0;
            _fuzzyMatchDistribution[level]++;
        }

        public void RecordToolCall(string toolName, bool success)
        {
            if (string.IsNullOrEmpty(toolName)) return;

            if (!_toolCallCounts.ContainsKey(toolName))
                _toolCallCounts[toolName] = 0;
            _toolCallCounts[toolName]++;

            if (!success)
            {
                if (!_toolFailCounts.ContainsKey(toolName))
                    _toolFailCounts[toolName] = 0;
                _toolFailCounts[toolName]++;
            }

            if (toolName.Equals("edit", StringComparison.OrdinalIgnoreCase))
            {
                EditAttempts++;
                if (success) EditSuccesses++;
                else EditFailures++;
            }
        }

        public void RecordEditFailureReason(string reason)
        {
            if (!string.IsNullOrEmpty(reason))
                _editFailureReasons.Add(reason);
        }

        public SessionRecord Build(TaskState state, string outcome)
        {
            return new SessionRecord(
                sessionId: SessionId,
                timestamp: StartTime,
                complexity: Complexity,
                intent: Intent,
                userMessageTokens: UserMessageTokens,
                iterations: state?.Iteration ?? 0,
                toolCalls: state?.TotalToolCallCount ?? 0,
                toolCallCounts: new Dictionary<string, int>(_toolCallCounts),
                toolFailCounts: new Dictionary<string, int>(_toolFailCounts),
                editAttempts: EditAttempts,
                editSuccesses: EditSuccesses,
                editFailures: EditFailures,
                editFailureReasons: _editFailureReasons.ToArray(),
                condenseEvents: _condenseEvents.ToArray(),
                totalPromptTokens: _totalPromptTokens,
                totalCompletionTokens: _totalCompletionTokens,
                fuzzyMatchDistribution: new Dictionary<string, int>(_fuzzyMatchDistribution),
                outcome: outcome,
                durationMs: (int)(DateTime.UtcNow - StartTime).TotalMilliseconds);
        }
    }

    /// <summary>
    /// H4: Writes SessionRecord to JSONL files in ~/.AICA/telemetry/.
    /// Thread-safe, creates directory if needed, appends one JSON line per record.
    /// </summary>
    public class AgentTelemetryWriter
    {
        private readonly string _baseDirectory;
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public AgentTelemetryWriter(string baseDirectory = null)
        {
            _baseDirectory = baseDirectory
                ?? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".AICA",
                    "telemetry");
        }

        /// <summary>
        /// Base directory for telemetry files (exposed for testing).
        /// </summary>
        public string BaseDirectory => _baseDirectory;

        public async Task WriteAsync(SessionRecord record)
        {
            if (record == null) return;

            try
            {
                Directory.CreateDirectory(_baseDirectory);

                var fileName = $"{record.Timestamp:yyyy-MM-dd}.jsonl";
                var filePath = Path.Combine(_baseDirectory, fileName);
                var json = JsonSerializer.Serialize(record, JsonOptions);

                // Append with newline — thread-safe via FileShare.ReadWrite
                using (var stream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                using (var writer = new StreamWriter(stream))
                {
                    await writer.WriteLineAsync(json).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                // Telemetry failures should never crash the agent
                System.Diagnostics.Debug.WriteLine($"[AICA] Telemetry write failed: {ex.Message}");
            }
        }
    }
}
