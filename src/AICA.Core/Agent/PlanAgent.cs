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

                // If no tool calls → LLM has finished exploring, this is the plan
                if (toolCalls.Count == 0)
                {
                    if (responseText.Length < 20)
                        return PlanResult.Fail("Plan too short");

                    return PlanResult.Ok(responseText);
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
                            result = await _toolDispatcher.ExecuteAsync(toolCall, context, null, toolCts.Token)
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

            // Max iterations reached — use whatever text we have
            var lastAssistant = history.LastOrDefault(m => m.Role == ChatRole.Assistant);
            if (lastAssistant != null && !string.IsNullOrEmpty(lastAssistant.Content) && lastAssistant.Content.Length > 20)
                return PlanResult.Ok(lastAssistant.Content);

            return PlanResult.Fail("Planning did not produce a result within iteration limit");
        }
    }
}
