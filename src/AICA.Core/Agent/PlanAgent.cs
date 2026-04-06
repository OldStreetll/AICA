using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AICA.Core.Context;
using AICA.Core.LLM;
using AICA.Core.Prompt;

namespace AICA.Core.Agent
{
    /// <summary>
    /// Result from PlanAgent's planning phase.
    /// </summary>
    public class PlanResult
    {
        public bool Success { get; set; }
        public string PlanText { get; set; }
        public string FailureReason { get; set; }

        public static PlanResult Ok(string planText) =>
            new PlanResult { Success = true, PlanText = planText };

        public static PlanResult Fail(string reason) =>
            new PlanResult { Success = false, FailureReason = reason };
    }

    /// <summary>
    /// No-op IUIContext for PlanAgent — planning phase has no UI interaction.
    /// </summary>
    internal class NullUIContext : IUIContext
    {
        public static readonly NullUIContext Instance = new NullUIContext();
        public Task ShowMessageAsync(string message, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateStreamingContentAsync(string content, CancellationToken ct = default) => Task.CompletedTask;
        public Task ShowProgressAsync(string message, int? percentComplete = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task HideProgressAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> ShowConfirmationAsync(string title, string message, CancellationToken ct = default) => Task.FromResult(true);
        public Task<DiffPreviewResult> ShowDiffPreviewAsync(string filePath, string originalContent, string newContent, CancellationToken ct = default)
            => Task.FromResult(DiffPreviewResult.Approved(newContent));
        public Task<FollowupQuestionResult> ShowFollowupQuestionAsync(string question, System.Collections.Generic.List<QuestionOption> options, bool allowCustomInput = false, CancellationToken ct = default)
            => Task.FromResult<FollowupQuestionResult>(null);
    }

    /// <summary>
    /// Lightweight planning agent that explores the codebase with read-only tools
    /// and generates a structured implementation plan for complex tasks.
    /// Runs as a mini agent loop inside AgentExecutor, with independent token budget.
    /// </summary>
    public class PlanAgent
    {
        private readonly ILLMClient _llmClient;
        private readonly ToolDispatcher _toolDispatcher;

        private const int MaxIterations = 10;
        private const int TimeoutSeconds = 60;
        private const int MaxPlanTokenBudget = 16000;

        private static readonly HashSet<string> ReadOnlyToolNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "read_file", "grep_search", "list_dir", "glob"
        };

        public PlanAgent(ILLMClient llmClient, ToolDispatcher toolDispatcher)
        {
            _llmClient = llmClient ?? throw new ArgumentNullException(nameof(llmClient));
            _toolDispatcher = toolDispatcher ?? throw new ArgumentNullException(nameof(toolDispatcher));
        }

        /// <summary>
        /// Generate a task plan by exploring the codebase with read-only tools.
        /// Returns PlanResult.Success with markdown plan, or PlanResult.Fail on any error.
        /// All failures are non-fatal — caller should fall through to normal execution.
        /// </summary>
        public async Task<PlanResult> GeneratePlanAsync(
            string userRequest,
            IAgentContext context,
            IEnumerable<ToolDefinition> allToolDefinitions,
            CancellationToken ct)
        {
            try
            {
                var readOnlyTools = allToolDefinitions
                    .Where(t => ReadOnlyToolNames.Contains(t.Name))
                    .ToList();

                if (readOnlyTools.Count == 0)
                    return PlanResult.Fail("No read-only tools available");

                var systemPrompt = PlanPromptBuilder.Build(context.WorkingDirectory);
                var history = new List<ChatMessage>
                {
                    ChatMessage.System(systemPrompt),
                    ChatMessage.User(userRequest)
                };

                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    cts.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));
                    return await RunPlanLoopAsync(history, readOnlyTools, context, cts.Token)
                        .ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                return PlanResult.Fail("Planning timed out");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AICA] PlanAgent failed: {ex.Message}");
                return PlanResult.Fail($"Planning error: {ex.Message}");
            }
        }

        private async Task<PlanResult> RunPlanLoopAsync(
            List<ChatMessage> history,
            List<ToolDefinition> readOnlyTools,
            IAgentContext context,
            CancellationToken ct)
        {
            for (int iteration = 0; iteration < MaxIterations; iteration++)
            {
                ct.ThrowIfCancellationRequested();

                // Estimate tokens and truncate if needed
                int totalTokens = history.Sum(m => ContextManager.EstimateTokens(m.Content));
                if (totalTokens > MaxPlanTokenBudget)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[AICA] PlanAgent token budget exceeded ({totalTokens}), stopping exploration");
                    break;
                }

                // Stream LLM response
                var assistantText = new StringBuilder();
                var toolCalls = new List<ToolCall>();

                await foreach (var chunk in _llmClient.StreamChatAsync(history, readOnlyTools, ct)
                    .ConfigureAwait(false))
                {
                    if (chunk.Type == LLMChunkType.Text && chunk.Text != null)
                        assistantText.Append(chunk.Text);
                    else if (chunk.Type == LLMChunkType.ToolCall && chunk.ToolCall != null)
                        toolCalls.Add(chunk.ToolCall);
                }

                var responseText = assistantText.ToString().Trim();

                // If no tool calls → LLM has finished exploring, proceed to finalize
                if (toolCalls.Count == 0)
                {
                    history.Add(ChatMessage.Assistant(responseText));
                    break;
                }

                // LLM wants to use tools — execute read-only tools and continue
                var assistantMsg = ChatMessage.Assistant(responseText);
                assistantMsg.ToolCalls = toolCalls.Select(tc => new ToolCallMessage
                {
                    Id = tc.Id,
                    Function = new FunctionCall
                    {
                        Name = tc.Name,
                        Arguments = System.Text.Json.JsonSerializer.Serialize(tc.Arguments)
                    }
                }).ToList();
                history.Add(assistantMsg);

                foreach (var toolCall in toolCalls)
                {
                    ct.ThrowIfCancellationRequested();

                    // Safety: only execute read-only tools
                    if (!ReadOnlyToolNames.Contains(toolCall.Name))
                    {
                        history.Add(ChatMessage.ToolResult(toolCall.Id,
                            $"Error: Tool '{toolCall.Name}' is not available in planning mode. Only read-only tools can be used."));
                        continue;
                    }

                    ToolResult result;
                    try
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[AICA] PlanAgent executing tool: {toolCall.Name} args={System.Text.Json.JsonSerializer.Serialize(toolCall.Arguments)}");
                        using (var toolCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                        {
                            toolCts.CancelAfter(TimeSpan.FromSeconds(15));
                            result = await _toolDispatcher.ExecuteAsync(toolCall, context, NullUIContext.Instance, toolCts.Token)
                                .ConfigureAwait(false);
                        }
                        System.Diagnostics.Debug.WriteLine(
                            $"[AICA] PlanAgent tool result: {toolCall.Name} Success={result.Success} ContentLen={result.Content?.Length ?? 0} Error={result.Error}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[AICA] PlanAgent tool exception: {toolCall.Name} {ex.GetType().Name}: {ex.Message}");
                        result = ToolResult.Fail($"Tool error: {ex.Message}");
                    }

                    // Truncate large tool results to preserve token budget
                    var content = result.Success ? result.Content : $"Error: {result.Error}";
                    if (content != null && content.Length > 3000)
                        content = content.Substring(0, 3000) + "\n... (truncated for planning)";

                    history.Add(ChatMessage.ToolResult(toolCall.Id, content));
                }
            }

            // ── Finalize: one more LLM call without tools to generate structured plan ──
            return await FinalizePlanAsync(history, ct).ConfigureAwait(false);
        }

        private const string FinalizePlanPrompt =
            "Based on everything you have explored above, now output your FINAL implementation plan. " +
            "Use EXACTLY this format and nothing else:\n\n" +
            "## Goal\n[What the user wants to accomplish]\n\n" +
            "## Key Discoveries\n[Important findings from your exploration]\n\n" +
            "## Steps\n1. [First step — file path and action]\n2. [Second step — file path and action]\n...\n\n" +
            "Output the plan NOW. Do not call any tools. Do not explain what you will do — just output the plan.";

        private async Task<PlanResult> FinalizePlanAsync(List<ChatMessage> history, CancellationToken ct)
        {
            try
            {
                // Add finalize instruction and call LLM without tools
                history.Add(ChatMessage.User(FinalizePlanPrompt));

                var planText = new StringBuilder();
                await foreach (var chunk in _llmClient.StreamChatAsync(history, null, ct).ConfigureAwait(false))
                {
                    if (chunk.Type == LLMChunkType.Text && chunk.Text != null)
                        planText.Append(chunk.Text);
                }

                var result = planText.ToString().Trim();
                if (result.Length < 30)
                    return PlanResult.Fail("Finalized plan too short");

                System.Diagnostics.Debug.WriteLine(
                    $"[AICA] PlanAgent finalized plan: {result.Length} chars");
                return PlanResult.Ok(result);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[AICA] PlanAgent finalize failed: {ex.Message}");
                return PlanResult.Fail($"Finalize error: {ex.Message}");
            }
        }
    }
}
