using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AICA.Core.Context;
using AICA.Core.LLM;
using AICA.Core.Prompt;
using AICA.Core.Rules;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace AICA.Core.Agent
{
    /// <summary>
    /// Core Agent executor — trust-based design.
    /// Uses LLM finish_reason as the natural stop signal (like OpenCode).
    /// Doom loop detection prevents infinite tool call cycles.
    /// </summary>
    public class AgentExecutor : IAgentExecutor
    {
        private readonly ILLMClient _llmClient;
        private readonly ToolDispatcher _toolDispatcher;
        private readonly ILogger<AgentExecutor> _logger;
        private readonly Logging.TelemetryLogger _telemetryLogger;
        private readonly int _maxIterations;
        private readonly int _maxTokenBudget;
        private readonly string _customInstructions;
        private readonly Kernel _kernel;
        private TaskState _taskState;

        private static int DoomLoopThreshold => Config.AicaConfig.Current.Agent.DoomLoopThreshold;
        private static int MaxRetries => Config.AicaConfig.Current.Agent.MaxRetries;
        private static readonly int[] RetryDelaysMs = { 1000, 3000 };

        public TaskState CurrentTaskState => _taskState;
        public string LastCondenseSummary { get; private set; }
        public int CondenseUpToMessageCount { get; private set; }
        public Kernel Kernel => _kernel;

        public AgentExecutor(
            ILLMClient llmClient,
            ToolDispatcher toolDispatcher,
            ILogger<AgentExecutor> logger = null,
            Logging.TelemetryLogger telemetryLogger = null,
            int maxIterations = 50,
            int maxTokenBudget = 32000,
            string customInstructions = null,
            Kernel kernel = null)
        {
            _llmClient = llmClient ?? throw new ArgumentNullException(nameof(llmClient));
            _toolDispatcher = toolDispatcher ?? throw new ArgumentNullException(nameof(toolDispatcher));
            _logger = logger;
            _telemetryLogger = telemetryLogger;
            _maxIterations = maxIterations;
            _maxTokenBudget = maxTokenBudget;
            _customInstructions = customInstructions;
            _kernel = kernel;
        }

        public async IAsyncEnumerable<AgentStep> ExecuteAsync(
            string userRequest,
            IAgentContext context,
            IUIContext uiContext,
            List<ChatMessage> previousMessages = null,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            // ── Setup ──
            await _toolDispatcher.WaitForMcpUpgradeAsync(5000).ConfigureAwait(false);

            var allToolDefinitions = _toolDispatcher.GetToolDefinitions().ToList();
            var complexity = TaskComplexityAnalyzer.AnalyzeComplexity(userRequest);

            // ── Plan Agent for complex tasks ──
            string planContext = null;
            PlanProgressTracker planTracker = null;
            TaskPlan currentPlan = null;
            var recentToolSummaries = new List<ToolExecutionSummary>();

            if (complexity == TaskComplexity.Complex)
            {
                yield return AgentStep.TextChunk("🧠 *Planning...*\n\n");
                var planAgent = new PlanAgent(_llmClient, _toolDispatcher);
                var planResult = await planAgent.GeneratePlanAsync(
                    userRequest, context, allToolDefinitions, ct).ConfigureAwait(false);
                if (planResult.Success)
                {
                    planContext = planResult.PlanText;
                    yield return AgentStep.TextChunk($"📋 **Plan:**\n{planResult.PlanText}\n\n---\n\n");

                    // Parse plan text into structured TaskPlan for progress tracking
                    currentPlan = ParsePlanFromText(planResult.PlanText);
                    if (currentPlan != null)
                    {
                        planTracker = new PlanProgressTracker(_llmClient);
                        yield return AgentStep.PlanUpdated(currentPlan);
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[AICA] PlanAgent failed: {planResult.FailureReason}, falling back to direct execution");
                }
            }

            var gitNexusAvailable = allToolDefinitions.Any(t => t.Name.StartsWith("gitnexus_", StringComparison.OrdinalIgnoreCase));
            var toolDefinitions = DynamicToolSelector.SelectTools(userRequest, complexity, allToolDefinitions, gitNexusAvailable).ToList();
            var language = ProjectLanguageDetector.DetectLanguage(context?.WorkingDirectory);
            var intent = DynamicToolSelector.ClassifyIntent(userRequest);

            // ── Build system prompt ──
            var conversationHistory = BuildConversationHistory(
                userRequest, context, language, intent, complexity, toolDefinitions, previousMessages, ct);

            // Wait for async operations in prompt building
            var history = await conversationHistory.ConfigureAwait(false);

            // Inject plan context if planning phase succeeded
            if (!string.IsNullOrEmpty(planContext))
            {
                history.Add(ChatMessage.System(
                    "[Task Plan — generated by planning phase]\n" + planContext));
            }

            // ── State initialization ──
            _taskState = new TaskState();
            var executedToolSignatures = new HashSet<string>(StringComparer.Ordinal);
            var sessionStartUtc = DateTime.UtcNow;
            var securityBlacklist = new HashSet<string>(StringComparer.Ordinal);
            var recentToolSignatures = new List<string>();
            var telemetryBuilder = new SessionRecordBuilder
            {
                Complexity = complexity.ToString(),
                Intent = intent,
                UserMessageTokens = userRequest?.Length ?? 0
            };

            // v2.4: Account for tool definition token overhead in budget
            int toolDefinitionTokens = toolDefinitions.Sum(t =>
                Context.ContextManager.EstimateTokens(t.Description ?? "") +
                Context.ContextManager.EstimateTokens(
                    System.Text.Json.JsonSerializer.Serialize(t.Parameters ?? new ToolParameters())));
            System.Diagnostics.Debug.WriteLine(
                $"[AICA] Tool definitions: {toolDefinitions.Count} tools, ~{toolDefinitionTokens} tokens overhead");
            int conversationBudget = (int)(_maxTokenBudget * 0.85) - toolDefinitionTokens;
            bool budgetWarningSent = false;

            // ══════════════════════════════════════════════════════════
            // ══  MAIN AGENT LOOP — trust-based, finish_reason stop  ══
            // ══════════════════════════════════════════════════════════
            while (_taskState.Iteration < _maxIterations && !ct.IsCancellationRequested && !_taskState.IsCompleted)
            {
                _taskState.Iteration++;
                _taskState.ApiRequestCount++;
                _logger?.LogDebug("Agent iteration {Iteration}", _taskState.Iteration);

                // ── Max-steps injection (OpenCode style) ──
                // On the last iteration, inject an assistant message telling LLM to wrap up with text only
                if (_taskState.Iteration >= _maxIterations - 1 && !budgetWarningSent)
                {
                    budgetWarningSent = true;
                    history.Add(ChatMessage.Assistant(
                        "CRITICAL: Maximum steps reached. Respond with text only — " +
                        "summarize what has been accomplished, list remaining tasks, and provide recommendations."));
                    System.Diagnostics.Debug.WriteLine(
                        $"[AICA] Max-steps message injected ({_taskState.Iteration}/{_maxIterations})");
                }

                // ── v2.1 M1: Prune before condense (cheap before expensive) ──
                var estimatedTokens = history.Sum(m => Context.ContextManager.EstimateTokens(m.Content));
                var condenseThreshold = (int)(conversationBudget * 0.70);
                var reCondenseGap = TokenBudgetManager.ComputeReCondenseGap(_maxTokenBudget);
                bool compactionAvoided = false;

                if (Config.AicaConfig.Current.Features.PruneBeforeCompaction
                    && estimatedTokens >= condenseThreshold
                    && history.Count >= 6)
                {
                    var tokensBefore = estimatedTokens;
                    PruneOldToolOutputs(history, protectRecentTurns: 2, protectTokens: 40000, minPruneTokens: 20000);
                    estimatedTokens = history.Sum(m => Context.ContextManager.EstimateTokens(m.Content));
                    var pruneTokensFreed = tokensBefore - estimatedTokens;
                    compactionAvoided = estimatedTokens < condenseThreshold;
                    System.Diagnostics.Debug.WriteLine(
                        $"[AICA] Pre-condense prune: freed ~{pruneTokensFreed} tokens, compaction {(compactionAvoided ? "avoided" : "still needed")}");
                    _telemetryLogger?.LogEvent(_taskState.CurrentPhase ?? "agent", "prune_before_compaction",
                        new Dictionary<string, object>
                        {
                            ["prune_tokens_freed"] = pruneTokensFreed,
                            ["compaction_avoided"] = compactionAvoided
                        });
                }

                // ── Auto-condense based on estimated token usage (OpenCode-inspired) ──
                // Trigger when accumulated tokens reach 70% of conversation budget,
                // with re-condense gap protection to avoid excessive condensation.
                if (!compactionAvoided
                    && _taskState.CanCondenseAgain(history.Count, reCondenseGap)
                    && estimatedTokens >= condenseThreshold
                    && history.Count >= 6) // minimum message count safety floor
                {
                    // LLM-based condense (OpenCode style) with programmatic fallback
                    System.Diagnostics.Debug.WriteLine(
                        $"[AICA] Condense triggered: ~{estimatedTokens} tokens >= {condenseThreshold} threshold ({history.Count} messages)");
                    var summary = await ConversationCompactor.GenerateSummaryAsync(
                        _llmClient, history, ct).ConfigureAwait(false);
                    LastCondenseSummary = summary;
                    var msgBefore = history.Count;
                    CondenseUpToMessageCount = msgBefore;
                    history = ConversationCompactor.BuildCondensedHistory(history, summary);
                    _taskState.RecordCondense(history.Count);
                    telemetryBuilder.RecordCondenseEvent(msgBefore, history.Count, Context.ContextManager.EstimateTokens(summary), "proactive");
                    System.Diagnostics.Debug.WriteLine(
                        $"[AICA] LLM-based condense #{_taskState.CondenseCount} complete, messages {msgBefore} → {history.Count}");

                    // v2.1 M1: Context Reset — if condense still over threshold OR consecutive doom loops
                    var postCondenseTokens = history.Sum(m => Context.ContextManager.EstimateTokens(m.Content));
                    if (postCondenseTokens >= condenseThreshold || _taskState.ConsecutiveDoomLoopCount >= 3)
                    {
                        var resetReason = postCondenseTokens >= condenseThreshold
                            ? $"post-condense still over threshold ({postCondenseTokens}/{condenseThreshold})"
                            : $"consecutive doom loops ({_taskState.ConsecutiveDoomLoopCount})";
                        System.Diagnostics.Debug.WriteLine($"[AICA] Context reset triggered: {resetReason}");

                        history = ConversationCompactor.ResetToClean(history, _taskState, planContext);
                        executedToolSignatures.Clear();
                        recentToolSignatures.Clear();

                        _telemetryLogger?.LogEvent(_taskState.CurrentPhase ?? "agent", "context_reset",
                            new Dictionary<string, object>
                            {
                                ["reason"] = resetReason,
                                ["reset_count"] = _taskState.ResetCount,
                                ["iteration"] = _taskState.Iteration
                            });
                    }
                }

                // ── Call LLM with retry ──
                string assistantText = null;
                var toolCalls = new List<ToolCall>();
                string finishReason = null;
                bool wasCancelled = false;

                var streamResult = await StreamLLMWithRetry(
                    history, toolDefinitions, ct,
                    text => { /* text chunks handled below */ }).ConfigureAwait(false);

                assistantText = streamResult.Text;
                toolCalls = streamResult.ToolCalls;
                finishReason = streamResult.FinishReason;
                wasCancelled = streamResult.WasCancelled;

                // Calibrate token estimation from API usage data
                if (streamResult.Usage != null && streamResult.Usage.PromptTokens > 0)
                {
                    var estimatedPrompt = history.Sum(m => Context.ContextManager.EstimateTokens(m.Content));
                    Context.ContextManager.CalibrateFromUsage(estimatedPrompt, streamResult.Usage.PromptTokens);
                }

                if (streamResult.Error != null)
                {
                    yield return AgentStep.WithError($"LLM 通信错误: {streamResult.Error}");
                    yield break;
                }

                if (wasCancelled)
                {
                    yield return AgentStep.WithError("操作已取消。");
                    yield break;
                }

                // Context overflow — trigger emergency condense and retry
                if (streamResult.ContextOverflow)
                {
                    System.Diagnostics.Debug.WriteLine("[AICA] Context overflow, triggering emergency condense");
                    var emergencyMsgBefore = history.Count;
                    var overflowSummary = await ConversationCompactor.GenerateSummaryAsync(
                        _llmClient, history, ct).ConfigureAwait(false);
                    history = ConversationCompactor.BuildCondensedHistory(history, overflowSummary);
                    _taskState.RecordCondense(history.Count);
                    telemetryBuilder.RecordCondenseEvent(emergencyMsgBefore, history.Count, Context.ContextManager.EstimateTokens(overflowSummary), "emergency");
                    yield return AgentStep.TextChunk("\n\n📝 *Context overflow — conversation condensed.*\n\n");
                    continue; // Retry with condensed history
                }

                // ── Stream text to UI ──
                if (!string.IsNullOrWhiteSpace(assistantText))
                {
                    yield return AgentStep.TextChunk(assistantText);
                }

                // Record assistant message in history
                if (!string.IsNullOrEmpty(assistantText) || toolCalls.Count > 0)
                {
                    var assistantMsg = ChatMessage.Assistant(assistantText ?? string.Empty);
                    if (toolCalls.Count > 0)
                    {
                        assistantMsg.ToolCalls = toolCalls.Select(tc => new ToolCallMessage
                        {
                            Id = tc.Id,
                            Function = new FunctionCall { Name = tc.Name, Arguments = tc.Arguments != null ? JsonSerializer.Serialize(tc.Arguments) : "{}" }
                        }).ToList();
                    }
                    history.Add(assistantMsg);
                }

                // ── Natural stop: finish_reason="stop" with no tool calls ──
                if (toolCalls.Count == 0)
                {
                    if (finishReason == "length")
                    {
                        // Truncated — ask LLM to continue
                        history.Add(ChatMessage.User(
                            "[System: Your response was cut off. Please continue from where you left off.]"));
                        System.Diagnostics.Debug.WriteLine("[AICA] Response truncated, asking to continue");
                        continue;
                    }

                    // Model chose to stop (finish_reason="stop" or equivalent) — task is done
                    System.Diagnostics.Debug.WriteLine(
                        $"[AICA] Natural completion: finish_reason={finishReason}, no tool calls, iteration={_taskState.Iteration}");
                    _taskState.IsCompleted = true;
                    yield return AgentStep.Complete(assistantText ?? string.Empty);
                    break;
                }

                // ── Execute tool calls ──
                _taskState.HasEverUsedTools = true;
                ToolCallProcessor.AugmentToolCallParameters(toolCalls, userRequest);

                foreach (var toolCall in toolCalls)
                {
                    // Security blacklist check
                    if (ToolCallProcessor.IsPathBlacklisted(toolCall, securityBlacklist, context?.WorkingDirectory))
                    {
                        var blockedResult = ToolResult.SecurityDenied("此路径已被安全策略永久拒绝访问。");
                        yield return AgentStep.WithToolResult(toolCall, blockedResult);
                        history.Add(ChatMessage.ToolResult(toolCall.Id, blockedResult.Error));
                        continue;
                    }

                    // Doom loop detection — same tool + same args 3 times in a row
                    // OpenCode style: ask user instead of blocking outright
                    string currentSig = $"{toolCall.Name}|{JsonSerializer.Serialize(toolCall.Arguments ?? new Dictionary<string, object>())}";
                    recentToolSignatures.Add(currentSig);
                    if (recentToolSignatures.Count >= DoomLoopThreshold)
                    {
                        var tail = recentToolSignatures.Skip(recentToolSignatures.Count - DoomLoopThreshold).ToList();
                        if (tail.Distinct().Count() == 1)
                        {
                            _taskState.ConsecutiveDoomLoopCount++;
                            System.Diagnostics.Debug.WriteLine(
                                $"[AICA] Doom loop detected: {toolCall.Name} called {DoomLoopThreshold}x with identical args, " +
                                $"consecutive doom loops: {_taskState.ConsecutiveDoomLoopCount}, asking user");
                            // Instead of blocking, tell LLM to ask the user
                            var doomMsg = $"You have called {toolCall.Name} with identical arguments {DoomLoopThreshold} times. " +
                                          "You MUST call ask_followup_question to ask the user how to proceed. " +
                                          "Suggest options: try a different approach, skip this step, or end the task.";
                            yield return AgentStep.WithToolResult(toolCall, ToolResult.Fail(doomMsg));
                            history.Add(ChatMessage.ToolResult(toolCall.Id, $"Error: {doomMsg}"));
                            recentToolSignatures.Clear(); // Reset so user's choice gets a fresh window
                            continue;
                        }
                    }
                    // v2.1 M1: Reset consecutive doom loop counter on non-doom tool execution
                    _taskState.ConsecutiveDoomLoopCount = 0;

                    // Dedup detection
                    var toolSignature = ToolCallProcessor.GetToolCallSignature(toolCall);
                    bool allowDuplicate = ShouldAllowDuplicate(toolCall);

                    if (!allowDuplicate
                        && !executedToolSignatures.Add(toolSignature))
                    {
                        var dupResult = ToolResult.Fail(
                            $"Duplicate call: {toolCall.Name} was already called with these arguments. Use the result from your conversation history.");
                        yield return AgentStep.ActionStart(ToolCallProcessor.BuildActionDescription(toolCall));
                        yield return AgentStep.ToolStart(toolCall);
                        yield return AgentStep.WithToolResult(toolCall, dupResult);
                        history.Add(ChatMessage.ToolResult(toolCall.Id, $"Error: {dupResult.Error}"));
                        _taskState.TotalToolCallCount++;
                        continue;
                    }

                    // Execute tool
                    yield return AgentStep.ActionStart(ToolCallProcessor.BuildActionDescription(toolCall));
                    yield return AgentStep.ToolStart(toolCall);

                    _taskState.LastToolName = toolCall.Name;
                    _taskState.TotalToolCallCount++;

                    ToolResult result;
                    try
                    {
                        using (var toolCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                        {
                            toolCts.CancelAfter(TimeSpan.FromSeconds(60));
                            result = await _toolDispatcher.ExecuteAsync(toolCall, context, uiContext, toolCts.Token).ConfigureAwait(false);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[AICA] Tool {toolCall.Name} exception: {ex.Message}");
                        result = ToolResult.Fail($"Tool execution error: {ex.Message}");
                    }

                    // Telemetry
                    telemetryBuilder.RecordToolCall(toolCall.Name, result.Success);
                    if (!result.Success && toolCall.Name == "edit")
                        telemetryBuilder.RecordEditFailureReason(result.Error);
                    if (result.Metadata != null && result.Metadata.TryGetValue("fuzzy_match_level", out var matchLevel))
                        telemetryBuilder.RecordFuzzyMatchLevel(matchLevel);

                    // Track state
                    if (result.Success)
                    {
                        _taskState.ResetFailureCounts();
                        TrackSuccessfulTool(toolCall, result);
                    }
                    else
                    {
                        _taskState.RecordToolFailure(ToolFailureKind.Blocking);
                        HandleFailedTool(toolCall, result, executedToolSignatures, securityBlacklist, toolSignature, context);
                    }

                    // Debug logging
                    LogToolResult(toolCall, result);

                    yield return AgentStep.WithToolResult(toolCall, result);

                    // Collect tool summary for plan progress evaluation
                    if (planTracker != null)
                    {
                        recentToolSummaries.Add(new ToolExecutionSummary
                        {
                            ToolName = toolCall.Name,
                            Success = result.Success,
                            Summary = TruncateForSummary(
                                result.Success ? result.Content : result.Error, 200)
                        });
                    }

                    // Add tool result to conversation
                    var resultContent = result.Success ? result.Content : $"Error: {result.Error}";
                    if (resultContent != null)
                    {
                        int remainingBudget = Math.Max(1000,
                            conversationBudget - history.Sum(m => ContextManager.EstimateTokens(m.Content)));
                        resultContent = ContextManager.SmartTruncateToolResult(resultContent, toolCall.Name, remainingBudget);
                    }
                    history.Add(ChatMessage.ToolResult(toolCall.Id, resultContent));

                    // Track user cancellations
                    TrackUserCancellation(toolCall, result);
                    if (_taskState.UserCancellationCount >= TaskState.MaxUserCancellations)
                    {
                        _taskState.UserCancellationCount = 0;
                        history.Add(ChatMessage.User(
                            "用户已多次取消操作。请调用 ask_followup_question 询问用户希望如何继续。"));
                        break;
                    }
                }

                // Evaluate plan progress after tool execution round
                if (planTracker != null && currentPlan != null && recentToolSummaries.Count > 0)
                {
                    var updatedPlan = await planTracker.EvaluateProgressAsync(
                        currentPlan, recentToolSummaries, ct).ConfigureAwait(false);
                    if (updatedPlan != null)
                    {
                        currentPlan = updatedPlan;
                        yield return AgentStep.PlanUpdated(updatedPlan);
                    }
                    recentToolSummaries.Clear();
                }

                // Handle truncated response after tool execution
                if (finishReason == "length" && toolCalls.Count > 0)
                {
                    history.Add(ChatMessage.User(
                        "[System: Your previous text was cut off. Tool results are above. Please continue from where you left off.]"));
                }
            }

            // ── Post-loop: prune old tool outputs for next conversation (OpenCode style) ──
            // Runs AFTER the loop — LLM never sees pruned results during active work.
            // Only prunes when enough tokens can be freed (protectTokens=40K, minPruneTokens=20K).
            PruneOldToolOutputs(history, protectRecentTurns: 2, protectTokens: 40000, minPruneTokens: 20000);

            // ── Post-loop: telemetry + progress save ──
            WriteTelemetry(telemetryBuilder, ct);
            await SaveProgressIfNeeded(userRequest, context, ct).ConfigureAwait(false);

            // ── Session-end telemetry (v2.1 telemetry补线) ──
            var sessionElapsed = DateTime.UtcNow - sessionStartUtc;
            _telemetryLogger?.LogEvent(_taskState.CurrentPhase ?? "agent", "session_end",
                new System.Collections.Generic.Dictionary<string, object>
                {
                    ["iterations"] = _taskState.Iteration,
                    ["tool_calls"] = _taskState.TotalToolCallCount,
                    ["condense_count"] = _taskState.CondenseCount,
                    ["reset_count"] = _taskState.ResetCount,
                    ["duration_seconds"] = (int)sessionElapsed.TotalSeconds,
                    ["completed"] = _taskState.IsCompleted
                });

            // Iteration exhaustion fallback
            if (_taskState.Iteration >= _maxIterations && !_taskState.IsCompleted)
            {
                yield return AgentStep.WithError(
                    $"已达到最大迭代次数 ({_taskState.Iteration}/{_maxIterations})。\n" +
                    $"共执行了 {_taskState.TotalToolCallCount} 次工具调用。\n" +
                    $"建议：请检查上方的工具执行日志，确认已完成的工作。");
            }
        }

        public void Abort()
        {
            _llmClient.Abort();
        }

        // ══════════════════════════════════════════════════
        // ══  Private helper methods                      ══
        // ══════════════════════════════════════════════════

        private async Task<List<ChatMessage>> BuildConversationHistory(
            string userRequest, IAgentContext context,
            ProjectLanguage language, string intent,
            TaskComplexity complexity,
            List<ToolDefinition> toolDefinitions,
            List<ChatMessage> previousMessages,
            CancellationToken ct)
        {
            var builder = new SystemPromptBuilder()
                .AddTools(toolDefinitions)
                .AddComplexityGuidance(complexity)
                .AddWorkspaceContext(
                    context?.WorkingDirectory ?? Environment.CurrentDirectory,
                    context?.SourceRoots)
                .AddBugFixGuidance(intent, language)
                .AddQtTemplateGuidance(userRequest)
                .AddGitNexusGuidance(ResolveGitNexusRepoName(context?.WorkingDirectory))
                // v2.3: Inject project structure into prompt (reduces list_projects calls)
                .AddProjectStructure(context?.GetProjects());

            // Load MCP resources (GitNexus setup instructions + codebase context)
            // This is what OpenCode does — inject resource content so the LLM knows how to use MCP tools
            await InjectMcpResources(builder, context, ct).ConfigureAwait(false);

            // Load memory bank (v2.1 OH2: pass user query for relevance scoring)
            if (context?.WorkingDirectory != null)
            {
                var query = ExtractLatestUserMessage(previousMessages);
                var memoryResult = await Storage.MemoryBank.LoadWithMetricsAsync(
                    context.WorkingDirectory, query, ct).ConfigureAwait(false);
                if (memoryResult.Content != null)
                {
                    builder.AddMemoryContext(memoryResult.Content);

                    // v2.1 OH2: Formal telemetry for memory loading
                    _telemetryLogger?.LogEvent(_taskState.CurrentPhase ?? "agent", "memory_loaded",
                        new Dictionary<string, object>
                        {
                            { "memories_total", memoryResult.MemoriesTotal },
                            { "memories_injected", memoryResult.MemoriesInjected },
                            { "memory_tokens_used", memoryResult.MemoryTokensUsed }
                        });
                }
            }

            // Load resume context
            if (context?.WorkingDirectory != null)
            {
                var savedProgress = await Storage.TaskProgressStore.LoadAsync(context.WorkingDirectory, ct).ConfigureAwait(false);
                if (savedProgress != null)
                    builder.AddResumeContext(savedProgress);
            }

            // Load rules
            if (context?.WorkingDirectory != null)
            {
                var ruleContext = new RuleContext(context.WorkingDirectory)
                {
                    CandidatePaths = ExtractPathCandidates(userRequest)
                };
                if (language == ProjectLanguage.CppC)
                {
                    ruleContext.CandidatePaths.Add("**/*.cpp");
                    ruleContext.CandidatePaths.Add("**/*.h");
                    ruleContext.CandidatePaths.Add("**/*.c");
                }
                await builder.AddRulesFromFilesAsync(context.WorkingDirectory, ruleContext, ct);
            }

            builder.AddCustomInstructions(_customInstructions);

            // Knowledge context for relevant intents
            if (intent == "read" || intent == "analyze" || intent == "command" || intent == "bug_fix")
            {
                var knowledgeStore = Knowledge.ProjectKnowledgeStore.Instance;
                if (knowledgeStore.HasIndex)
                {
                    var provider = knowledgeStore.CreateProvider();
                    if (provider != null)
                    {
                        var knowledgeContext = provider.RetrieveContext(userRequest);
                        builder.AddKnowledgeContext(knowledgeContext);
                    }
                }
            }

            var systemPrompt = builder.Build();
            var history = new List<ChatMessage>();
            history.Add(ChatMessage.System(systemPrompt));

            if (previousMessages != null && previousMessages.Count > 0)
            {
                var filtered = previousMessages.Where(m => m.Role != ChatRole.System).ToList();
                if (filtered.Count > 0)
                {
                    history.Add(ChatMessage.System(
                        "[TASK_BOUNDARY] 以下是新的用户请求。请专注于新请求，不要继续之前任务的工作。"));
                }
                history.AddRange(filtered);
            }
            else
            {
                history.Add(ChatMessage.User(userRequest));
            }

            return history;
        }

        /// <summary>
        /// v2.1 OH2: Extract the latest user message text from conversation history.
        /// Used as query for memory relevance scoring.
        /// </summary>
        private static string ExtractLatestUserMessage(List<LLM.ChatMessage> messages)
        {
            if (messages == null || messages.Count == 0)
                return null;

            for (int i = messages.Count - 1; i >= 0; i--)
            {
                if (messages[i].Role == LLM.ChatRole.User && !string.IsNullOrEmpty(messages[i].Content))
                    return messages[i].Content;
            }

            return null;
        }

        private struct LLMStreamResult
        {
            public string Text;
            public List<ToolCall> ToolCalls;
            public string FinishReason;
            public bool WasCancelled;
            public bool ContextOverflow;
            public string Error;
            public LLM.UsageInfo Usage;
        }

        private static readonly Random _jitterRandom = new Random();

        private async Task<LLMStreamResult> StreamLLMWithRetry(
            List<ChatMessage> history, List<ToolDefinition> tools,
            CancellationToken ct, Action<string> onTextChunk)
        {
            var result = new LLMStreamResult { ToolCalls = new List<ToolCall>() };

            for (int attempt = 0; attempt <= MaxRetries; attempt++)
            {
                try
                {
                    string text = null;
                    var toolCalls = new List<ToolCall>();
                    string finishReason = null;

                    await foreach (var chunk in _llmClient.StreamChatAsync(history, tools, ct).ConfigureAwait(false))
                    {
                        if (chunk.Type == LLMChunkType.Text)
                        {
                            text = (text ?? string.Empty) + chunk.Text;
                            onTextChunk?.Invoke(chunk.Text);
                        }
                        else if (chunk.Type == LLMChunkType.ToolCall)
                        {
                            toolCalls.Add(chunk.ToolCall);
                        }
                        else if (chunk.Type == LLMChunkType.Done)
                        {
                            finishReason = chunk.FinishReason ?? "stop";
                            if (chunk.Usage != null)
                                result.Usage = chunk.Usage;
                        }
                    }

                    result.Text = text;
                    result.ToolCalls = toolCalls;
                    result.FinishReason = finishReason ?? "stop";
                    return result;
                }
                catch (OperationCanceledException)
                {
                    result.WasCancelled = true;
                    return result;
                }
                catch (Exception ex) when (IsContextOverflowException(ex))
                {
                    // Context overflow detected from API error — trigger condense
                    System.Diagnostics.Debug.WriteLine(
                        $"[AICA] Context overflow detected from API: {ex.Message}");
                    result.ContextOverflow = true;
                    return result;
                }
                catch (LLM.LLMException lex) when (attempt < MaxRetries)
                {
                    // v2.3: Structured error classification for differentiated recovery
                    var errorKind = lex.Classify();
                    System.Diagnostics.Debug.WriteLine(
                        $"[AICA] LLM error classified as {errorKind} (attempt {attempt + 1}/{MaxRetries}): {lex.Message}");

                    switch (errorKind)
                    {
                        case LLM.LLMErrorKind.ContextOverflow:
                            result.ContextOverflow = true;
                            return result;
                        case LLM.LLMErrorKind.RateLimited:
                            // Rate limited: longer backoff
                            int rlDelay = 2000 * (1 << attempt) + _jitterRandom.Next(0, 1000);
                            System.Diagnostics.Debug.WriteLine($"[AICA] Rate limited, waiting {rlDelay}ms");
                            await Task.Delay(rlDelay, ct).ConfigureAwait(false);
                            break;
                        case LLM.LLMErrorKind.Retryable:
                            int baseDelay2 = 1000 * (1 << attempt);
                            int jitter2 = _jitterRandom.Next(0, baseDelay2 / 2);
                            await Task.Delay(baseDelay2 + jitter2, ct).ConfigureAwait(false);
                            break;
                        case LLM.LLMErrorKind.AuthError:
                            result.Error = $"Authentication error: {lex.Message}. Check API key configuration.";
                            return result;
                        case LLM.LLMErrorKind.ModelNotFound:
                            result.Error = $"Model not found: {lex.Message}. Check model name in settings.";
                            return result;
                        default: // BadRequest, Fatal
                            result.Error = lex.Message;
                            return result;
                    }
                }
                catch (Exception ex) when (attempt < MaxRetries && IsTransientException(ex))
                {
                    // Non-LLMException transient errors (network, IO)
                    int baseDelay = 1000 * (1 << attempt);
                    int jitter = _jitterRandom.Next(0, baseDelay / 2);
                    int delay = baseDelay + jitter;
                    System.Diagnostics.Debug.WriteLine(
                        $"[AICA] Transient error (attempt {attempt + 1}/{MaxRetries}), retrying in {delay}ms: {ex.Message}");
                    await Task.Delay(delay, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    result.Error = ex.Message;
                    return result;
                }
            }

            result.Error = "Max retries exceeded";
            return result;
        }

        /// <summary>
        /// Allow duplicate read_file calls on files that were edited in this session
        /// (content changed, so re-reading with same path is legitimate).
        /// v2.1: TruncatedFiles branch removed — O12 fix includes offset/limit in signature,
        /// so different chunks already produce different signatures and bypass dedup naturally.
        /// </summary>
        private bool ShouldAllowDuplicate(ToolCall toolCall)
        {
            if (toolCall.Name != "read_file") return false;

            var readPath = ToolCallProcessor.ExtractPathFromToolCall(toolCall);
            if (readPath == null) return false;

            return _taskState.EditedFiles.Contains(readPath.TrimEnd('/', '\\'));
        }

        private void TrackSuccessfulTool(ToolCall toolCall, ToolResult result)
        {
            if (toolCall.Name == "edit")
            {
                var editPath = ToolCallProcessor.ExtractPathFromToolCall(toolCall);
                if (!string.IsNullOrEmpty(editPath))
                    _taskState.EditedFiles.Add(editPath.TrimEnd('/', '\\'));
            }

            // v2.1: TruncatedFiles tracking removed (O12 fix makes it unnecessary)
        }

        private void HandleFailedTool(ToolCall toolCall, ToolResult result,
            HashSet<string> executedToolSignatures, HashSet<string> securityBlacklist,
            string toolSignature, IAgentContext context)
        {
            if (ToolCallProcessor.ShouldAllowRetry(result))
            {
                executedToolSignatures.Remove(toolSignature);
            }
            else
            {
                ToolCallProcessor.AddToSecurityBlacklist(toolCall, securityBlacklist, context?.WorkingDirectory);
            }
        }

        private void TrackUserCancellation(ToolCall toolCall, ToolResult result)
        {
            if (toolCall.Name == "edit" && result.Success &&
                result.Content != null && result.Content.Contains("EDIT CANCELLED BY USER"))
            {
                _taskState.UserCancellationCount++;
            }
            if (!result.Success && result.Error != null &&
                (result.Error.Contains("cancelled by user") || result.Error.Contains("User cancelled")))
            {
                _taskState.UserCancellationCount++;
            }
        }

        private List<ChatMessage> HandleCondense(string content, List<ChatMessage> history)
        {
            var summary = content.Substring("CONDENSE:".Length);
            var toolHistory = TokenBudgetManager.ExtractToolCallHistory(history);
            if (!string.IsNullOrEmpty(toolHistory))
                summary = summary + "\n\n" + toolHistory;

            LastCondenseSummary = summary;
            CondenseUpToMessageCount = history.Count;

            var condensed = new List<ChatMessage>();
            if (history.Count > 0)
                condensed.Add(history[0]); // system prompt

            condensed.Add(ChatMessage.System(
                "[Conversation condensed] The following is a summary of all previous work:\n" + summary));

            // Preserve last user message
            for (int i = history.Count - 1; i >= 0; i--)
            {
                if (history[i].Role == ChatRole.User)
                {
                    condensed.Add(history[i]);
                    break;
                }
            }

            if (condensed.Count < 3)
            {
                condensed.Add(ChatMessage.User(
                    "Context was condensed. Please continue with the current task based on the summary above."));
            }

            _taskState.RecordCondense(condensed.Count);
            return condensed;
        }

        private void LogToolResult(ToolCall toolCall, ToolResult result)
        {
            if (result.Success && result.Content != null)
            {
                var statsIdx = result.Content.LastIndexOf("[TOOL_EXACT_STATS:");
                if (statsIdx >= 0)
                    System.Diagnostics.Debug.WriteLine($"[AICA] Tool '{toolCall.Name}' → {result.Content.Substring(statsIdx).TrimEnd()}");
                else
                {
                    var preview = result.Content.Length > 120 ? result.Content.Substring(0, 120) + "..." : result.Content;
                    System.Diagnostics.Debug.WriteLine($"[AICA] Tool '{toolCall.Name}' → ({result.Content.Length} chars) {preview}");
                }
            }
            else if (!result.Success)
            {
                System.Diagnostics.Debug.WriteLine($"[AICA] Tool '{toolCall.Name}' FAILED → {result.Error}");
            }
        }

        private void WriteTelemetry(SessionRecordBuilder telemetryBuilder, CancellationToken ct)
        {
            if (!Config.AicaConfig.Current.Telemetry.Enabled) return;
            string outcome;
            if (_taskState.IsCompleted) outcome = "completed";
            else if (_taskState.Abort) outcome = "aborted";
            else if (ct.IsCancellationRequested) outcome = "user_cancelled";
            else if (_taskState.Iteration >= _maxIterations) outcome = "timeout";
            else outcome = "unknown";

            var sessionRecord = telemetryBuilder.Build(_taskState, outcome);
            _ = Task.Run(async () =>
            {
                try
                {
                    var writer = new AgentTelemetryWriter();
                    await writer.WriteAsync(sessionRecord).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[AICA] Telemetry write failed: {ex.Message}");
                }
            });
        }

        private async Task SaveProgressIfNeeded(string userRequest, IAgentContext context, CancellationToken ct)
        {
            if (context?.WorkingDirectory != null && _taskState.DidEditFile)
            {
                var progress = new Storage.TaskProgress
                {
                    OriginalUserRequest = userRequest,
                    EditedFiles = _taskState.EditedFiles.ToList(),
                    CurrentPhase = _taskState.CurrentPhase,
                    PlanState = null
                };
                await Storage.TaskProgressStore.SaveAsync(context.WorkingDirectory, progress).ConfigureAwait(false);
            }
        }

        private static bool IsTransientException(Exception ex)
        {
            if (IsContextOverflowException(ex)) return false; // Handled separately
            if (ex is System.Net.Http.HttpRequestException) return true;
            if (ex is TaskCanceledException tce && tce.CancellationToken == default) return true;
            if (ex is System.IO.IOException) return true;
            if (ex.InnerException is System.Net.Sockets.SocketException) return true;
            if (ex.InnerException is System.IO.IOException) return true;
            return false;
        }

        /// <summary>
        /// Detect context window overflow from API error responses (OpenCode style).
        /// </summary>
        private static bool IsContextOverflowException(Exception ex)
        {
            var msg = ex.Message ?? string.Empty;
            if (ex.InnerException != null) msg += " " + ex.InnerException.Message;

            return msg.Contains("context_length_exceeded")
                || msg.Contains("maximum context length")
                || msg.Contains("token limit")
                || msg.Contains("too many tokens")
                || msg.Contains("context window")
                // v2.4: MiniMax-specific error patterns (Chinese + English variants)
                || msg.Contains("上下文长度")
                || msg.Contains("令牌限制")
                || msg.Contains("超出模型")
                || msg.Contains("exceeds the model")
                || msg.Contains("input is too long")
                || msg.Contains("max_tokens");
        }

        private List<string> ExtractPathCandidates(string text)
        {
            if (string.IsNullOrEmpty(text))
                return new List<string>();
            return new Rules.Parsers.PathMatcher().ExtractPathCandidates(text);
        }

        private static string ResolveGitNexusRepoName(string workingDirectory)
        {
            if (string.IsNullOrEmpty(workingDirectory))
                return null;

            var dir = workingDirectory;
            for (int i = 0; i < 10 && !string.IsNullOrEmpty(dir); i++)
            {
                if (System.IO.Directory.Exists(System.IO.Path.Combine(dir, ".git")))
                    return System.IO.Path.GetFileName(dir);
                dir = System.IO.Path.GetDirectoryName(dir);
            }
            return null;
        }

        /// <summary>
        /// Prune old tool outputs to free token space for the NEXT conversation (OpenCode style).
        /// Called AFTER the main loop — LLM never sees pruned results during active work.
        /// Uses token thresholds: first protectTokens of old outputs are untouched,
        /// only prunes beyond that, and only if total prunable exceeds minPruneTokens.
        /// </summary>
        private static void PruneOldToolOutputs(
            List<ChatMessage> history,
            int protectRecentTurns = 2,
            int protectTokens = 40000,
            int minPruneTokens = 20000)
        {
            // Find protection boundary (skip recent N user turns)
            int userMsgCount = 0;
            int protectBoundary = history.Count;
            for (int i = history.Count - 1; i >= 0; i--)
            {
                if (history[i].Role == ChatRole.User)
                {
                    userMsgCount++;
                    if (userMsgCount >= protectRecentTurns)
                    {
                        protectBoundary = i;
                        break;
                    }
                }
            }

            // Scan old tool outputs, accumulate token estimates
            int totalOldTokens = 0;
            int prunableTokens = 0;
            var toPrune = new List<int>();

            for (int i = 0; i < protectBoundary; i++)
            {
                if (history[i].Role == ChatRole.Tool && history[i].Content != null && history[i].Content.Length > 200)
                {
                    int estimatedTokens = Context.ContextManager.EstimateTokens(history[i].Content);
                    totalOldTokens += estimatedTokens;

                    // Only prune beyond the protection zone
                    if (totalOldTokens > protectTokens)
                    {
                        prunableTokens += estimatedTokens;
                        toPrune.Add(i);
                    }
                }
            }

            // Only execute if enough tokens would be freed
            if (prunableTokens >= minPruneTokens)
            {
                foreach (var idx in toPrune)
                {
                    int originalTokens = Context.ContextManager.EstimateTokens(history[idx].Content);
                    var timestamp = DateTime.Now.ToString("HH:mm");
                    history[idx] = ChatMessage.ToolResult(
                        history[idx].ToolCallId,
                        $"[compacted at {timestamp}, original ~{originalTokens} tokens — re-read the file if you need this content]");
                }
                System.Diagnostics.Debug.WriteLine(
                    $"[AICA] Post-loop pruning: {toPrune.Count} tool outputs pruned (~{prunableTokens} tokens freed, {totalOldTokens} total old tokens)");
            }
        }

        /// <summary>
        /// Read MCP resources (gitnexus://setup + gitnexus://repo/{name}/context) and inject into prompt.
        /// This is the mechanism that tells the LLM how and when to use GitNexus tools.
        /// </summary>
        private async Task InjectMcpResources(SystemPromptBuilder builder, IAgentContext context, CancellationToken ct)
        {
            try
            {
                var pm = GitNexusProcessManager.Instance;
                if (pm.State != GitNexusState.Ready || pm.Client == null)
                    return;

                var client = pm.Client;
                var sb = new System.Text.StringBuilder();

                // Read setup resource (AGENTS.md — tells LLM how to use GitNexus)
                try
                {
                    var setup = await client.ReadResourceAsync("gitnexus://setup", ct).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(setup))
                    {
                        sb.AppendLine(setup);
                        System.Diagnostics.Debug.WriteLine($"[AICA] MCP resource gitnexus://setup loaded ({setup.Length} chars)");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[AICA] MCP resource gitnexus://setup failed: {ex.Message}");
                }

                // Read repo context resource (codebase overview + available tools)
                var repoName = ResolveGitNexusRepoName(context?.WorkingDirectory);
                if (!string.IsNullOrEmpty(repoName))
                {
                    try
                    {
                        var repoContext = await client.ReadResourceAsync(
                            $"gitnexus://repo/{repoName}/context", ct).ConfigureAwait(false);
                        if (!string.IsNullOrWhiteSpace(repoContext))
                        {
                            sb.AppendLine(repoContext);
                            System.Diagnostics.Debug.WriteLine($"[AICA] MCP resource gitnexus://repo/{repoName}/context loaded ({repoContext.Length} chars)");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[AICA] MCP resource gitnexus://repo/{repoName}/context failed: {ex.Message}");
                    }
                }

                if (sb.Length > 0)
                {
                    // Map native MCP tool names to AICA-registered names (gitnexus_ prefix)
                    // The MCP resource (AGENTS.md) uses native names like `query`, `context`,
                    // but AICA registers them as `gitnexus_query`, `gitnexus_context`, etc.
                    // Without this mapping, the LLM can't connect resource guidance to actual tools.
                    var content = sb.ToString();
                    content = MapMcpToolNames(content);
                    builder.AddMcpResourceContext(content);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AICA] MCP resource injection failed (non-fatal): {ex.Message}");
            }
        }

        /// <summary>
        /// Map native MCP tool names in resource content to AICA-registered names.
        /// GitNexus AGENTS.md references tools as `query`, `context`, `impact`, etc.
        /// but AICA registers them as `gitnexus_query`, `gitnexus_context`, `gitnexus_impact`.
        /// </summary>
        private static string MapMcpToolNames(string content)
        {
            // Replace backtick-quoted tool names in markdown: `query` → `gitnexus_query`
            // Also handle plain references like "Use context()" → "Use gitnexus_context()"
            var mappings = new[]
            {
                ("query", "gitnexus_query"),
                ("context", "gitnexus_context"),
                ("impact", "gitnexus_impact"),
                ("detect_changes", "gitnexus_detect_changes"),
                ("rename", "gitnexus_rename"),
                ("cypher", "gitnexus_cypher"),
                ("list_repos", "gitnexus_list_repos"),
            };

            foreach (var (mcpName, aicaName) in mappings)
            {
                // `query` → `gitnexus_query` (backtick-quoted in markdown tables)
                content = content.Replace($"`{mcpName}`", $"`{aicaName}`");
                // query() → gitnexus_query() (function call references)
                content = content.Replace($"{mcpName}(", $"{aicaName}(");
                // "Use query " → "Use gitnexus_query " (space-delimited references in prose)
                content = content.Replace($"Use {mcpName} ", $"Use {aicaName} ");
            }

            return content;
        }

        // ResponseProcessor delegates removed — no longer needed in trust-based design

        /// <summary>
        /// Parse structured markdown plan text from PlanAgent into a TaskPlan.
        /// Extracts numbered steps from the "## Steps" section.
        /// </summary>
        private static TaskPlan ParsePlanFromText(string planText)
        {
            if (string.IsNullOrWhiteSpace(planText)) return null;

            var plan = new TaskPlan { Explanation = planText };
            var lines = planText.Split('\n');
            bool inSteps = false;

            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();

                if (line.StartsWith("## Steps", StringComparison.OrdinalIgnoreCase))
                {
                    inSteps = true;
                    continue;
                }

                if (inSteps)
                {
                    if (line.StartsWith("##"))
                        break;

                    var match = System.Text.RegularExpressions.Regex.Match(
                        line, @"^(?:\d+[\.\)]\s*|-\s+)(.+)$");
                    if (match.Success)
                    {
                        plan.Steps.Add(new PlanStep
                        {
                            Description = match.Groups[1].Value.Trim(),
                            Status = PlanStepStatus.Pending
                        });
                    }
                }
            }

            return plan.Steps.Count > 0 ? plan : null;
        }

        private static string TruncateForSummary(string text, int maxLen)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Length <= maxLen ? text : text.Substring(0, maxLen) + "...";
        }
    }
}
