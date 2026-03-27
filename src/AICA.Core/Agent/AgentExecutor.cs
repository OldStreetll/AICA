using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
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
    /// Core Agent executor that manages the Agent loop
    /// </summary>
    public class AgentExecutor : IAgentExecutor
    {
        private readonly ILLMClient _llmClient;
        private readonly ToolDispatcher _toolDispatcher;
        private readonly ILogger<AgentExecutor> _logger;
        private readonly int _maxIterations;
        private readonly int _maxTokenBudget;
        private readonly string _customInstructions;
        private readonly Kernel _kernel;
        private TaskState _taskState;

        /// <summary>
        /// Current task state (readable for UI layer)
        /// </summary>
        public TaskState CurrentTaskState => _taskState;

        /// <summary>
        /// Last condense summary produced during execution. UI layer can persist this
        /// to ConversationRecord.ContextSummary for smarter session resume.
        /// </summary>
        public string LastCondenseSummary { get; private set; }

        /// <summary>
        /// Number of messages in conversation history at the time of last condense.
        /// UI layer can persist this to ConversationRecord.SummaryUpToIndex.
        /// </summary>
        public int CondenseUpToMessageCount { get; private set; }

        public AgentExecutor(
            ILLMClient llmClient,
            ToolDispatcher toolDispatcher,
            ILogger<AgentExecutor> logger = null,
            int maxIterations = 50,
            int maxTokenBudget = 32000,
            string customInstructions = null,
            Kernel kernel = null)
        {
            _llmClient = llmClient ?? throw new ArgumentNullException(nameof(llmClient));
            _toolDispatcher = toolDispatcher ?? throw new ArgumentNullException(nameof(toolDispatcher));
            _logger = logger;
            _maxIterations = maxIterations;
            _maxTokenBudget = maxTokenBudget;
            _customInstructions = customInstructions;
            _kernel = kernel;
        }

        /// <summary>
        /// The SK Kernel instance (if configured). Available for future SK-based strategies.
        /// </summary>
        public Kernel Kernel => _kernel;

        /// <summary>
        /// Execute a user request through the Agent loop
        /// </summary>
        /// <param name="userRequest">The current user message</param>
        /// <param name="context">Agent context for workspace operations</param>
        /// <param name="uiContext">UI context for user interactions</param>
        /// <param name="previousMessages">Previous conversation history (excluding system prompt)</param>
        /// <param name="ct">Cancellation token</param>
        public async IAsyncEnumerable<AgentStep> ExecuteAsync(
            string userRequest,
            IAgentContext context,
            IUIContext uiContext,
            List<ChatMessage> previousMessages = null,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            // Wait for MCP native definitions before building tool list (eliminates race condition)
            await _toolDispatcher.WaitForMcpUpgradeAsync(5000).ConfigureAwait(false);

            // Build system prompt with tool definitions
            var allToolDefinitions = _toolDispatcher.GetToolDefinitions().ToList();
            var complexity = TaskComplexityAnalyzer.AnalyzeComplexity(userRequest);
            var gitNexusAvailable = allToolDefinitions.Any(t => t.Name.StartsWith("gitnexus_", StringComparison.OrdinalIgnoreCase));
            var toolDefinitions = DynamicToolSelector.SelectTools(userRequest, complexity, allToolDefinitions, gitNexusAvailable);
            // 1.2c: Detect project language and classify intent early (needed for prompt building)
            var language = ProjectLanguageDetector.DetectLanguage(context?.WorkingDirectory);
            var intent = DynamicToolSelector.ClassifyIntent(userRequest);

            // 3.4 H2: Create state machine for complex tasks
            var stateMachine = TaskStateMachine.TryCreate(userRequest, complexity);
            if (stateMachine != null)
            {
                _taskState.CurrentPhase = stateMachine.CurrentPhaseName;
                System.Diagnostics.Debug.WriteLine($"[AICA] H2: State machine activated — template={stateMachine.Template.Name}, phase={_taskState.CurrentPhase}");
            }

            var builder = new SystemPromptBuilder()
                .AddTools(toolDefinitions)
                .AddToolDescriptions()
                .AddComplexityGuidance(complexity)
                .AddWorkspaceContext(
                    context?.WorkingDirectory ?? Environment.CurrentDirectory,
                    context?.SourceRoots)
                .AddBugFixGuidance(intent, language)  // 1.3a: Bug fix guidance
                .AddQtTemplateGuidance(userRequest)  // 3.2: F5 Qt template for UI engineers
                .AddGitNexusGuidance(ResolveGitNexusRepoName(context?.WorkingDirectory)); // 2.1: GitNexus few-shot [P1/P2/P3]

            // 3.8: Load cross-session memory bank
            if (context?.WorkingDirectory != null)
            {
                var memoryContent = await Storage.MemoryBank.LoadAsync(context.WorkingDirectory, ct).ConfigureAwait(false);
                if (memoryContent != null)
                {
                    builder.AddMemoryContext(memoryContent);
                    System.Diagnostics.Debug.WriteLine($"[AICA] Loaded memory bank ({memoryContent.Length} chars)");
                }
            }

            // 3.7: Load saved progress for resume context
            if (context?.WorkingDirectory != null)
            {
                var savedProgress = await Storage.TaskProgressStore.LoadAsync(context.WorkingDirectory, ct).ConfigureAwait(false);
                if (savedProgress != null)
                {
                    builder.AddResumeContext(savedProgress);
                    System.Diagnostics.Debug.WriteLine($"[AICA] Loaded resume context: {savedProgress.EditedFiles?.Count ?? 0} edited files");
                }
            }

            // Load and integrate rules from files
            if (context?.WorkingDirectory != null)
            {
                var ruleContext = new RuleContext(context.WorkingDirectory)
                {
                    CandidatePaths = ExtractPathCandidates(userRequest)
                };

                // C14: Force-activate C++ rules in C++ projects
                if (language == ProjectLanguage.CppC)
                {
                    ruleContext.CandidatePaths.Add("**/*.cpp");
                    ruleContext.CandidatePaths.Add("**/*.h");
                    ruleContext.CandidatePaths.Add("**/*.c");
                }

                await builder.AddRulesFromFilesAsync(
                    context.WorkingDirectory,
                    ruleContext,
                    ct);
            }

            // Add custom instructions
            builder.AddCustomInstructions(_customInstructions);
            // Add project knowledge context only for knowledge-hungry intents
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

            var isComplexRequest = complexity == TaskComplexity.Complex;
            if (isComplexRequest)
            {
                System.Diagnostics.Debug.WriteLine($"[AICA] Complex request detected, will inject planning message");
            }

            // Build conversation history: system prompt + previous messages
            var conversationHistory = new List<ChatMessage>();

            // Always start with system prompt
            conversationHistory.Add(ChatMessage.System(systemPrompt));

            // Add previous conversation history (if any)
            // Note: previousMessages should already include the current user message
            if (previousMessages != null && previousMessages.Count > 0)
            {
                // Filter out old system prompts from previous messages
                var filteredPrevious = previousMessages
                    .Where(m => m.Role != ChatRole.System)
                    .ToList();

                // H6: Inject task boundary marker for cross-task isolation [C81/D-02]
                // This tells the LLM and condense logic that a new task begins here
                if (filteredPrevious.Count > 0)
                {
                    conversationHistory.Add(ChatMessage.System(
                        "[TASK_BOUNDARY] 以下是新的用户请求。" +
                        "此标记之前的内容来自本会话的历史任务。请专注于新请求，不要继续之前任务的工作。"));
                }

                conversationHistory.AddRange(filteredPrevious);
            }
            else
            {
                // If no previous messages, add the current user request
                conversationHistory.Add(ChatMessage.User(userRequest));
            }

            // Inject planning directive as a user message AFTER the user's request
            // This is the last thing the LLM sees, making it much harder to ignore
            // than burying it in a long system prompt.
            if (isComplexRequest)
            {
                conversationHistory.Add(ChatMessage.User(
                    "[System: Task Planning Required] This is a complex multi-step task. " +
                    "Before doing anything else, you MUST first call the `update_plan` tool to create a plan with 3+ concrete steps (all status 'pending'). " +
                    "Do NOT call any other tool before `update_plan`. " +
                    "After creating the plan, execute each step and call `update_plan` again to update step status (in_progress → completed/failed) as you progress."));
            }

            _taskState = new TaskState
            {
                MaxConsecutiveMistakes = 3,
                MaxRecoveryPrompts = 2
            };

            // Track tool call signatures to detect duplicates
            var executedToolSignatures = new HashSet<string>(StringComparer.Ordinal);

            // SEC-01: Permanent blacklist for security-denied paths (survives dedup set changes)
            var securityBlacklist = new HashSet<string>(StringComparer.Ordinal);

            // H4: Telemetry builder — collects per-tool metrics during execution
            var telemetryBuilder = new SessionRecordBuilder
            {
                Complexity = complexity.ToString(),
                Intent = intent,
                UserMessageTokens = userRequest?.Length ?? 0
            };

            while (_taskState.Iteration < _maxIterations && !ct.IsCancellationRequested && !_taskState.Abort && !_taskState.IsCompleted)
            {
                _taskState.Iteration++;
                _taskState.PhaseIterationCount++;
                _taskState.ApiRequestCount++;
                var iteration = _taskState.Iteration;
                _logger?.LogDebug("Agent iteration {Iteration}", iteration);

                // 3.4 H2: Check state machine directive
                if (stateMachine != null)
                {
                    var directive = stateMachine.GetDirective(_taskState, null);
                    if (directive != null)
                    {
                        _taskState.CurrentPhase = stateMachine.CurrentPhaseName;
                        _taskState.PhaseIterationCount = 0;
                        if (directive.Mode == PhaseDirectiveMode.Force)
                        {
                            conversationHistory.Add(ChatMessage.User(directive.Message));
                        }
                    }
                }
                System.Diagnostics.Debug.WriteLine($"[AICA] Agent iteration {iteration}");

                // ── Micro-compact old tool results to save context space ──
                conversationHistory = ResponseQualityFilter.MicroCompactToolResults(conversationHistory, keepRecent: 4);

                // Truncate conversation history if it exceeds token budget
                // Reserve ~15% of budget for system prompt, use 85% for conversation
                int conversationBudget = (int)(_maxTokenBudget * 0.85);
                int currentTokens;
                if (conversationBudget > 0)
                {
                    var truncateResult = ContextManager.TruncateConversationWithStats(
                        conversationHistory, conversationBudget);
                    conversationHistory = truncateResult.Messages;
                    currentTokens = truncateResult.TotalTokens;
                }
                else
                {
                    currentTokens = conversationHistory.Sum(m => Context.ContextManager.EstimateTokens(m.Content));
                }

                // ── Safety boundaries: only intervene in truly dangerous situations ──
                double tokenUsageRatio = (double)currentTokens / Math.Max(1, conversationBudget);

                // ── Two-level proactive condense ──
                // Level 1 (70%): hint the LLM to call condense
                // Level 2 (80%+): if LLM ignored the hint, auto-condense from conversation history
                // Note: auto-condense must run BEFORE safety boundary check so it gets a chance
                //       even when token usage jumps from 70s% to 90%+ in a single iteration
                if (tokenUsageRatio > 0.70 && !_taskState.HasAutoCondensed)
                {
                    int compressibleMessages = conversationHistory
                        .Count(m => m.Role != ChatRole.System);

                    if (compressibleMessages >= 5)
                    {
                        if (!_taskState.HasCondenseHinted)
                        {
                            // Level 1: hint
                            System.Diagnostics.Debug.WriteLine(
                                $"[AICA] Token usage at {tokenUsageRatio:P0}, injecting condense hint");

                            conversationHistory.Add(ChatMessage.System(
                                $"[CONTEXT_PRESSURE] Token usage is at {tokenUsageRatio:P0}. " +
                                "You should call the `condense` tool to summarize previous work and free up context space. " +
                                "Include all key findings, files read/modified, and current progress in your summary."));

                            _taskState.HasCondenseHinted = true;
                            _taskState.CondenseHintIteration = _taskState.Iteration;
                        }
                        else if (tokenUsageRatio > 0.80
                                 && _taskState.Iteration > _taskState.CondenseHintIteration + 1)
                        {
                            // Level 2: LLM ignored the hint for at least 1 iteration, auto-condense
                            System.Diagnostics.Debug.WriteLine(
                                $"[AICA] Token usage at {tokenUsageRatio:P0}, LLM ignored condense hint, performing auto-condense");

                            var summary = TokenBudgetManager.BuildAutoCondenseSummary(conversationHistory);

                            LastCondenseSummary = summary;
                            CondenseUpToMessageCount = conversationHistory.Count;

                            var condensed = new List<ChatMessage>();
                            if (conversationHistory.Count > 0)
                                condensed.Add(conversationHistory[0]); // system prompt

                            condensed.Add(ChatMessage.System(
                                "[Conversation auto-condensed] The following is a summary of all previous work:\n" + summary));

                            // Preserve the last user message (current request), not the first one.
                            // This prevents the LLM from replaying old tasks after auto-condensation.
                            for (int i = conversationHistory.Count - 1; i >= 0; i--)
                            {
                                if (conversationHistory[i].Role == ChatRole.User)
                                {
                                    condensed.Add(conversationHistory[i]);
                                    break;
                                }
                            }

                            // Post-condense instruction: guide LLM to answer the current request (P1-012, P1-013)
                            condensed.Add(ChatMessage.System(
                                "[Post-condense instruction] The conversation was condensed to save context space. " +
                                "The 'Tool Call History' section above contains FACTUAL data about every tool call made in this conversation. " +
                                "You MUST answer the user's LATEST message based on the summary above. " +
                                "When the user asks about previous work (e.g., 'what files did I read?', '之前读取了哪些文件'), " +
                                "your answer MUST be based EXCLUSIVELY on the Tool Call History section. " +
                                "Do NOT claim tools were not used if they appear in the history. " +
                                "Do NOT start a new task, replay old tasks, or explore new files unless the user asks."));

                            conversationHistory = condensed;
                            _taskState.HasAutoCondensed = true;

                            // Recalculate tokens after condense
                            currentTokens = conversationHistory.Sum(m => Context.ContextManager.EstimateTokens(m.Content));
                            tokenUsageRatio = (double)currentTokens / Math.Max(1, conversationBudget);

                            yield return AgentStep.TextChunk("\n\n📝 *Context window pressure detected, conversation auto-condensed.*\n\n");

                            System.Diagnostics.Debug.WriteLine(
                                $"[AICA] Auto-condense complete, token usage now {tokenUsageRatio:P0}");
                        }
                    }
                }

                // ── Message count-based proactive condense ──
                // Delegates to TokenBudgetManager for threshold constants and history building.
                if (!_taskState.HasAutoCondensed
                    && conversationHistory.Count >= TokenBudgetManager.ComputeCondenseMessageThreshold(_maxTokenBudget))
                {
                    int compressibleMessages = conversationHistory
                        .Count(m => m.Role != ChatRole.System);

                    if (compressibleMessages >= TokenBudgetManager.ComputeCondenseCompressibleThreshold(_maxTokenBudget))
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[AICA] Message count at {conversationHistory.Count}, performing proactive condense");

                        var summary = TokenBudgetManager.BuildAutoCondenseSummary(conversationHistory);
                        LastCondenseSummary = summary;
                        CondenseUpToMessageCount = conversationHistory.Count;

                        conversationHistory = TokenBudgetManager.BuildCondensedHistory(conversationHistory, summary);
                        _taskState.HasAutoCondensed = true;

                        currentTokens = conversationHistory.Sum(m => Context.ContextManager.EstimateTokens(m.Content));
                        tokenUsageRatio = (double)currentTokens / Math.Max(1, conversationBudget);

                        System.Diagnostics.Debug.WriteLine(
                            $"[AICA] Proactive condense complete, messages reduced to {conversationHistory.Count}, token usage {tokenUsageRatio:P0}");
                    }
                }

                // H7: Iteration budget awareness — intermediate checkpoints [C80/D-01]
                int budgetPercent = (_taskState.Iteration * 100) / _maxIterations;

                if (budgetPercent >= 80 && !_taskState.BudgetWarning80Sent)
                {
                    _taskState.BudgetWarning80Sent = true;
                    conversationHistory.Add(ChatMessage.System(
                        $"[BUDGET_WARNING_80] 你已使用 {_taskState.Iteration}/{_maxIterations} 次迭代（{budgetPercent}%）。" +
                        "你必须立即停止搜索，整合已有信息并调用 attempt_completion 提交最终答案。"));
                    System.Diagnostics.Debug.WriteLine(
                        $"[AICA] H7: 80% budget warning sent ({_taskState.Iteration}/{_maxIterations})");
                }
                else if (budgetPercent >= 60 && !_taskState.BudgetWarning60Sent)
                {
                    _taskState.BudgetWarning60Sent = true;
                    conversationHistory.Add(ChatMessage.System(
                        $"[BUDGET_WARNING_60] 你已使用 {_taskState.Iteration}/{_maxIterations} 次迭代（{budgetPercent}%）。" +
                        "请开始整合已收集的信息，准备给出最终答案。避免不必要的额外搜索。"));
                    System.Diagnostics.Debug.WriteLine(
                        $"[AICA] H7: 60% budget warning sent ({_taskState.Iteration}/{_maxIterations})");
                }

                // Only force completion in extreme edge cases
                bool forceCompletion = false;

                // Edge case 1: Approaching absolute iteration limit (last 2 iterations)
                if (_taskState.Iteration >= _maxIterations - 2)
                {
                    forceCompletion = true;
                    System.Diagnostics.Debug.WriteLine($"[AICA] Safety boundary: approaching max iterations ({_taskState.Iteration}/{_maxIterations})");
                }

                // Edge case 2: Context window critically full (>90%)
                if (tokenUsageRatio > 0.90)
                {
                    forceCompletion = true;
                    System.Diagnostics.Debug.WriteLine($"[AICA] Safety boundary: context window critically full ({tokenUsageRatio:P0})");
                }

                if (forceCompletion)
                {
                    string reason = _taskState.Iteration >= _maxIterations - 2
                        ? $"approaching iteration limit ({_taskState.Iteration}/{_maxIterations})"
                        : $"context window critically full ({tokenUsageRatio:P0})";

                    System.Diagnostics.Debug.WriteLine($"[AICA] Safety boundary triggered: {reason}");

                    bool alreadySummarizing = conversationHistory.Any(m =>
                        m.Role == LLM.ChatRole.System && m.Content != null &&
                        m.Content.Contains("[SAFETY_BOUNDARY]"));

                    if (!alreadySummarizing)
                    {
                        yield return AgentStep.TextChunk($"\n\n⚠️ *Safety boundary triggered ({reason}), completing task...*\n\n");

                        conversationHistory.Add(ChatMessage.System(
                            $"[SAFETY_BOUNDARY] You are approaching system limits ({reason}). " +
                            "You MUST call `attempt_completion` NOW to summarize your findings. " +
                            "Provide a comprehensive summary based on ALL information gathered so far."));
                    }
                }

                // Get LLM response with auto-retry
                string assistantResponse = null;
                var toolCalls = new List<ToolCall>();
                var pendingTextChunks = new List<string>();
                string streamError = null;
                bool wasCancelled = false;
                bool wasTruncated = false;
                const int maxRetries = 2;
                int[] retryDelaysMs = { 1000, 3000 };

                // Retry state — yield return is forbidden inside catch, so use flags
                string retryNotice = null;
                bool shouldRetry = false;
                int retryDelayMs = 0;

                for (int attempt = 0; attempt <= maxRetries; attempt++)
                {
                    // Reset per-attempt state
                    assistantResponse = null;
                    toolCalls.Clear();
                    pendingTextChunks.Clear();
                    streamError = null;
                    wasCancelled = false;
                    wasTruncated = false;
                    retryNotice = null;
                    shouldRetry = false;

                    try
                    {
                        // When forceCompletion is active, keep only attempt_completion tool to allow proper task termination
                        var effectiveTools = forceCompletion
                            ? toolDefinitions.Where(t => t.Name == "attempt_completion").ToList()
                            : toolDefinitions;
                        await foreach (var chunk in _llmClient.StreamChatAsync(conversationHistory, effectiveTools, ct).ConfigureAwait(false))
                        {
                            if (chunk.Type == LLMChunkType.Text)
                            {
                                assistantResponse = (assistantResponse ?? string.Empty) + chunk.Text;
                                pendingTextChunks.Add(chunk.Text);
                            }
                            else if (chunk.Type == LLMChunkType.ToolCall)
                            {
                                toolCalls.Add(chunk.ToolCall);
                            }
                            else if (chunk.Type == LLMChunkType.Done
                                && (chunk.FinishReason == "length" || chunk.FinishReason == "max_tokens"))
                            {
                                wasTruncated = true;
                                System.Diagnostics.Debug.WriteLine($"[AICA] Detected truncation: finish_reason={chunk.FinishReason}");
                            }
                        }
                        break; // success — exit retry loop
                    }
                    catch (OperationCanceledException)
                    {
                        wasCancelled = true;
                        break; // never retry cancellation
                    }
                    catch (LLM.LLMException llmEx) when (llmEx.IsContextExceeded)
                    {
                        System.Diagnostics.Debug.WriteLine($"[AICA] Context window exceeded, truncating history (attempt {attempt + 1})");
                        int reducedBudget = conversationBudget / 2;
                        conversationHistory = ContextManager.TruncateConversation(conversationHistory, reducedBudget);

                        if (attempt < maxRetries)
                        {
                            retryNotice = "\n⚠️ 上下文窗口已满，正在自动裁剪历史对话...\n";
                            shouldRetry = true;
                        }
                        else
                        {
                            streamError = "上下文窗口超出限制，自动裁剪后仍无法发送请求。请开始新对话。";
                        }
                    }
                    catch (LLM.LLMException llmEx) when (llmEx.IsTransient && attempt < maxRetries)
                    {
                        System.Diagnostics.Debug.WriteLine($"[AICA] Transient LLM error (attempt {attempt + 1}): {llmEx.Message}");
                        retryNotice = $"\n⏳ 请求失败，正在重试 ({attempt + 1}/{maxRetries})...\n";
                        retryDelayMs = retryDelaysMs[attempt];
                        shouldRetry = true;
                    }
                    catch (Exception ex) when (attempt < maxRetries && IsTransientException(ex))
                    {
                        System.Diagnostics.Debug.WriteLine($"[AICA] Transient error (attempt {attempt + 1}): {ex.Message}");
                        retryNotice = $"\n⏳ 网络错误，正在重试 ({attempt + 1}/{maxRetries})...\n";
                        retryDelayMs = retryDelaysMs[attempt];
                        shouldRetry = true;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[AICA] LLM stream error: {ex.Message}");
                        streamError = ex.Message;
                        break;
                    }

                    // Yield retry notice outside catch block (C# forbids yield in catch)
                    if (retryNotice != null)
                        yield return AgentStep.TextChunk(retryNotice);

                    if (shouldRetry)
                    {
                        if (retryDelayMs > 0)
                            await Task.Delay(retryDelayMs, ct).ConfigureAwait(false);
                        continue;
                    }
                    break;
                }

                // Handle errors first
                if (wasCancelled)
                {
                    yield return AgentStep.WithError("Operation cancelled.");
                    yield break;
                }
                if (streamError != null)
                {
                    yield return AgentStep.WithError($"LLM communication error: {streamError}");
                    yield break;
                }

                // ── Resolve ALL tool calls (API + text-based) BEFORE any decisions ──
                // Skip text-based parsing in force-completion mode (LLM should only produce text)
                if (!forceCompletion && toolCalls.Count == 0 && !string.IsNullOrEmpty(assistantResponse))
                {
                    var parsedCalls = ToolCallProcessor.TryParseTextToolCalls(assistantResponse);
                    if (parsedCalls.Count > 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"[AICA] Parsed {parsedCalls.Count} text-based tool call(s) from response");
                        toolCalls.AddRange(parsedCalls);

                        // Remove the text-based tool call syntax from assistantResponse to avoid leaking it
                        assistantResponse = ToolCallProcessor.RemoveTextToolCallSyntax(assistantResponse);
                    }
                }

                bool hasToolCalls = toolCalls.Count > 0;

                // ── GUARD FIRST: conversational messages yield text normally and exit ──
                // Must run BEFORE suppression to avoid replacing greetings with indicators.
                if (_taskState.Iteration == 1 && !hasToolCalls && IsLikelyConversational(userRequest))
                {
                    System.Diagnostics.Debug.WriteLine($"[AICA] Conversational message on iteration 1, yielding text and completing (toolCalls={toolCalls.Count})");

                    // BF-02 fix: Apply quality filters even on conversational path.
                    // Delegates to ResponseQualityFilter.ApplyAllFilters() per R1 principle.
                    var filteredResponse = Prompt.ResponseQualityFilter.ApplyAllFilters(assistantResponse ?? string.Empty);
                    if (filteredResponse != assistantResponse)
                    {
                        System.Diagnostics.Debug.WriteLine($"[AICA] Conversational response filtered ({(assistantResponse?.Length ?? 0) - filteredResponse.Length} chars removed)");
                    }

                    yield return AgentStep.TextChunk(filteredResponse);

                    var convMsg = ChatMessage.Assistant(filteredResponse);
                    conversationHistory.Add(convMsg);
                    yield return AgentStep.Complete(filteredResponse);
                    yield break;
                }

                // ── Extract <thinking> tags before any suppression logic ──
                string pendingThinking = null;
                if (!string.IsNullOrEmpty(assistantResponse))
                {
                    var (thinking, userFacing) = ResponseQualityFilter.ExtractThinking(assistantResponse);
                    if (!string.IsNullOrEmpty(thinking))
                    {
                        System.Diagnostics.Debug.WriteLine($"[AICA] Extracted thinking ({thinking.Length} chars)");
                        pendingThinking = thinking;
                        assistantResponse = userFacing;
                        pendingTextChunks.Clear();
                        if (!string.IsNullOrEmpty(userFacing))
                            pendingTextChunks.Add(userFacing);
                    }
                }

                // Yield thinking chunk (outside try-catch, safe for yield)
                if (!string.IsNullOrEmpty(pendingThinking))
                {
                    yield return AgentStep.ThinkingChunk(pendingThinking);
                }

                // ── Diagnostic: detect tool call intent without actual tool_calls (P1-POCO-011) ──
                if (!hasToolCalls && !string.IsNullOrWhiteSpace(assistantResponse))
                {
                    var responseText = assistantResponse;
                    if (responseText.Contains("ask_followup_question") || responseText.Contains("let me use")
                        || responseText.Contains("让我使用") || responseText.Contains("我需要询问"))
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[AICA] WARNING: LLM intended to call a tool but no tool_calls in response. " +
                            $"Check: 1) Function calling enabled? 2) Model supports tool use? " +
                            $"Content preview: {responseText.Substring(0, System.Math.Min(200, responseText.Length))}");
                    }
                }

                // ── Pre-tool text suppression ──
                // When the model calls tools, ALL accompanying text is captured as thinking content
                // for the collapsible thinking block. This text represents the LLM's reasoning
                // for this iteration (e.g., "The file wasn't found, let me check the directory...").
                // Exception: ask_followup_question needs its pre-text as the question context.
                bool suppressText = false;
                string pendingThinkingFromSuppression = null;

                if (hasToolCalls && !string.IsNullOrWhiteSpace(assistantResponse))
                {
                    bool hasOnlyFollowup = toolCalls.All(tc => tc.Name == "ask_followup_question");
                    bool hasOnlyCompletion = toolCalls.All(tc => tc.Name == "attempt_completion");

                    if (hasOnlyCompletion)
                    {
                        // attempt_completion's accompanying text is the LLM's conclusion/summary,
                        // not thinking. Yield it as TextChunk so it appears as conclusion (part 3).
                        suppressText = false;
                    }
                    else if (!hasOnlyFollowup)
                    {
                        // Capture ALL text as thinking content for this iteration's thinking block
                        pendingThinkingFromSuppression = assistantResponse;
                        suppressText = true;
                    }
                }

                // When no tool calls but tools have been used before (iteration > 1),
                // only suppress if the text is clearly tool-planning narration,
                // not a valid answer or summary of tool results.
                if (!suppressText && !hasToolCalls && _taskState.HasEverUsedTools
                    && _taskState.Iteration > 1 && !string.IsNullOrWhiteSpace(assistantResponse))
                {
                    bool looksLikeToolPlanning = IsToolPlanningText(assistantResponse);

                    if (looksLikeToolPlanning)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[AICA] Suppressing tool-planning narration ({assistantResponse.Length} chars), nudging to call attempt_completion");

                        // P1-017: detect repeated narration stall
                        var fingerprint = assistantResponse.Length > 100
                            ? assistantResponse.Substring(0, 100).Trim().ToLowerInvariant()
                            : assistantResponse.Trim().ToLowerInvariant();

                        if (fingerprint == _taskState.LastNarrativeFingerprint)
                        {
                            _taskState.RepeatedNarrativeCount++;
                            System.Diagnostics.Debug.WriteLine(
                                $"[AICA] Repeated narration detected ({_taskState.RepeatedNarrativeCount})");
                        }
                        else
                        {
                            _taskState.LastNarrativeFingerprint = fingerprint;
                            _taskState.RepeatedNarrativeCount = 1;
                        }

                        if (_taskState.RepeatedNarrativeCount >= 2)
                        {
                            System.Diagnostics.Debug.WriteLine("[AICA] Narrative stall detected, force-completing");
                            yield return AgentStep.TextChunk(assistantResponse);
                            yield return AgentStep.TextChunk(
                                "\n\n---\n⚠️ **提示**: AI 反复描述要执行的操作但未实际调用工具。任务已停止。\n" +
                                "可能原因：工具调用被截断或上下文不足。请尝试重新提问。");
                            yield return AgentStep.Complete(assistantResponse);
                            yield break;
                        }

                        pendingThinkingFromSuppression = assistantResponse;
                        suppressText = true;
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[AICA] Post-tool response looks like valid answer ({assistantResponse.Length} chars), allowing through");
                    }
                }

                // Suppress standalone meta-reasoning text only when tool calls are present [C79/D-04]
                // When no tool calls, the text IS the final answer — do not suppress
                if (!suppressText && hasToolCalls && !string.IsNullOrWhiteSpace(assistantResponse)
                    && ResponseQualityFilter.IsInternalReasoning(assistantResponse))
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[AICA] Suppressing meta-reasoning text ({assistantResponse?.Length ?? 0} chars)");
                    pendingThinkingFromSuppression = assistantResponse;
                    suppressText = true;
                }

                // Yield suppressed text as ThinkingChunk (only if no <thinking> tag was already extracted)
                if (!string.IsNullOrEmpty(pendingThinkingFromSuppression) && string.IsNullOrEmpty(pendingThinking))
                {
                    yield return AgentStep.ThinkingChunk(pendingThinkingFromSuppression);
                }

                if (suppressText)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[AICA] Suppressed text as thinking ({assistantResponse?.Length ?? 0} chars)");
                    assistantResponse = null;
                }
                else
                {
                    // ── Apply response quality filters before yielding ──
                    if (pendingTextChunks.Count > 0)
                    {
                        pendingTextChunks[0] = ResponseQualityFilter.StripForbiddenOpeners(pendingTextChunks[0]);
                        var lastIdx = pendingTextChunks.Count - 1;
                        pendingTextChunks[lastIdx] = ResponseQualityFilter.StripTrailingOffers(pendingTextChunks[lastIdx]);
                    }

                    foreach (var text in pendingTextChunks)
                    {
                        yield return AgentStep.TextChunk(text);
                    }
                }

                // Build assistant message for conversation history
                var assistantMsg = ChatMessage.Assistant(assistantResponse ?? string.Empty);
                if (toolCalls.Count > 0)
                {
                    assistantMsg.ToolCalls = new System.Collections.Generic.List<ToolCallMessage>();
                    foreach (var tc in toolCalls)
                    {
                        assistantMsg.ToolCalls.Add(new ToolCallMessage
                        {
                            Id = tc.Id,
                            Type = "function",
                            Function = new FunctionCall
                            {
                                Name = tc.Name,
                                Arguments = tc.Arguments != null 
                                    ? System.Text.Json.JsonSerializer.Serialize(tc.Arguments) 
                                    : "{}"
                            }
                        });
                    }
                }
                conversationHistory.Add(assistantMsg);

                // ── No tool calls at all ──
                if (toolCalls.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[AICA] No tool calls. Iteration={_taskState.Iteration}, wasTruncated={wasTruncated}, forceCompletion={forceCompletion}");

                    // Force-completion mode: text-only response IS the final answer
                    if (forceCompletion && !string.IsNullOrWhiteSpace(assistantResponse))
                    {
                        System.Diagnostics.Debug.WriteLine($"[AICA] Force-completion: yielding text response as Complete ({assistantResponse.Length} chars)");
                        // Note: pendingTextChunks were already yielded in the suppression block above,
                        // so we only yield the Complete step here (not the text chunks again).
                        yield return AgentStep.Complete(assistantResponse);
                        yield break;
                    }

                    if (wasTruncated)
                    {
                        conversationHistory.Add(ChatMessage.User("[System: Your response was cut off due to token limit. Please continue from where you left off.]"));
                        continue;
                    }

                    // ── Hallucination detection ──
                    // Detect if the model claims to have executed a tool but didn't actually call it
                    // [D-05] Only check when LLM has previously used tools — if it never called a tool,
                    // Chinese words like "调用了" / "结果是" in technical answers are not hallucinations
                    if (_taskState.HasEverUsedTools
                        && !string.IsNullOrWhiteSpace(assistantResponse) && ResponseProcessor.DetectToolExecutionClaim(assistantResponse))
                    {
                        _taskState.HallucinationCount++;
                        System.Diagnostics.Debug.WriteLine(
                            $"[AICA] Detected tool execution hallucination ({_taskState.HallucinationCount}/3) - model claimed to execute a tool but didn't call it");

                        // If hallucination persists after retries, stop and warn the user
                        // rather than looping indefinitely (may indicate function calling is disabled)
                        if (_taskState.HallucinationCount >= 3)
                        {
                            System.Diagnostics.Debug.WriteLine($"[AICA] Hallucination persisted after 3 retries, aborting with warning");
                            yield return AgentStep.TextChunk(
                                "\n\n⚠️ **提示**: AI 多次描述了要执行的操作但未实际调用工具。\n" +
                                "可能原因：\n" +
                                "1. LLM 服务器未启用 function calling（需要 `--enable-auto-tool-choice`）\n" +
                                "2. 模型不支持 OpenAI 格式的工具调用\n" +
                                "3. 在选项中检查 'Enable Tool Calling' 是否已启用\n" +
                                "请继续\n");
                            yield break;
                        }

                        // Yield the hallucinated text first (so user can see what happened)
                        // Already yielded via pendingTextChunks above

                        // Add a strong correction message
                        conversationHistory.Add(ChatMessage.User(
                            "⚠️ CRITICAL ERROR: You described executing a tool but did NOT actually call it. " +
                            "You MUST use the proper tool calling format to execute tools. " +
                            "Do NOT just describe what you would do - ACTUALLY CALL THE TOOL using the function calling mechanism. " +
                            "Please retry the operation by making the actual tool call now."));

                        System.Diagnostics.Debug.WriteLine($"[AICA] Added hallucination correction message");
                        continue;
                    }

                    // ── Conflict detection: modification request but already compliant ──
                    if (CompletionHandler.DetectModificationConflict(userRequest, assistantResponse, conversationHistory, _taskState))
                    {
                        System.Diagnostics.Debug.WriteLine($"[AICA] Detected modification conflict - user requested modification but code is already compliant");

                        conversationHistory.Add(ChatMessage.User(
                            "⚠️ CRITICAL: You discovered that the requested files are already in the desired state, " +
                            "but the user explicitly asked you to modify them. This is a conflict scenario. " +
                            "You MUST NOT directly complete the task or end with a text-only response. " +
                            "Instead, you MUST call the `ask_followup_question` tool to ask the user what they want to do. " +
                            "Provide clear options such as:\n" +
                            "- 'Keep the current implementation as is'\n" +
                            "- 'Modify the files anyway according to the original request'\n" +
                            "- 'Check other related files for similar issues'\n" +
                            "Explain your findings clearly and wait for the user's decision before proceeding."));

                        System.Diagnostics.Debug.WriteLine($"[AICA] Added conflict resolution constraint message");
                        continue;
                    }

                    // First iteration with no tools: genuine text response
                    // (knowledge questions, explanations, conversational replies)
                    if (_taskState.Iteration == 1)
                    {
                        yield return AgentStep.Complete(assistantResponse);
                        yield break;
                    }

                    // Nudge LLM to use tools
                    bool tooManyNoTool = _taskState.RecordNoToolsUsed();
                    if (tooManyNoTool)
                    {
                        yield return AgentStep.Complete(assistantResponse);
                        yield break;
                    }

                    conversationHistory.Add(ChatMessage.User(
                        "You did not use any tools in your response. " +
                        "If you have completed the task (created/edited files or finished the work), you MUST call the `attempt_completion` tool now. " +
                        "If the task is not complete, continue using tools to finish it. " +
                        "Do NOT just write a summary - call the appropriate tool."));
                    System.Diagnostics.Debug.WriteLine($"[AICA] No tools, nudging to use attempt_completion (count={_taskState.ConsecutiveNoToolCount})");
                    continue;
                }

                // Tools were used, reset no-tool counter and mark that tools have been used
                _taskState.ResetNoToolCount();
                _taskState.HasEverUsedTools = true;

                // ── Conflict detection: prevent direct completion when modification was requested but nothing needs changing ──
                if (toolCalls.Any(tc => tc.Name == "attempt_completion") &&
                    CompletionHandler.DetectModificationConflict(userRequest, assistantResponse, conversationHistory, _taskState))
                {
                    System.Diagnostics.Debug.WriteLine("[AICA] Preventing direct attempt_completion because modification conflict requires ask_followup_question");

                    conversationHistory.Add(ChatMessage.User(
                        "⚠️ CRITICAL: You concluded that the requested files are already in the desired state, " +
                        "but the user explicitly asked for a modification. This is a conflict scenario. " +
                        "You MUST NOT call `attempt_completion` yet. Instead, you MUST call the `ask_followup_question` tool " +
                        "to ask the user how to proceed. Provide clear options such as keeping the current implementation, " +
                        "modifying anyway, or checking related files."));
                    continue;
                }

                // ── Parameter augmentation ──
                // Auto-add missing parameters when user intent is clear but LLM omitted them
                ToolCallProcessor.AugmentToolCallParameters(toolCalls, userRequest);

                System.Diagnostics.Debug.WriteLine($"[AICA] Executing {toolCalls.Count} tool calls");

                // Execute tool calls
                foreach (var toolCall in toolCalls)
                {
                    // SEC-01: Check security blacklist BEFORE dedup — blocks path variants
                    if (ToolCallProcessor.IsPathBlacklisted(toolCall, securityBlacklist, context?.WorkingDirectory))
                    {
                        System.Diagnostics.Debug.WriteLine($"[AICA] SEC-01: Blocked security-blacklisted path for tool '{toolCall.Name}'");
                        var blockedResult = ToolResult.SecurityDenied("此路径已被安全策略永久拒绝访问。");
                        yield return AgentStep.WithToolResult(toolCall, blockedResult);
                        conversationHistory.Add(ChatMessage.ToolResult(toolCall.Id, blockedResult.Error));
                        continue;
                    }

                    // ── Duplicate tool call detection ──
                    // Skip tools that were already called with identical arguments
                    // (exempt attempt_completion, condense, and read_file after edit)
                    var toolSignature = ToolCallProcessor.GetToolCallSignature(toolCall);

                    // Allow read_file to be called again if the file was edited,
                    // or if the file was auto-truncated and this is a targeted follow-up with offset/limit
                    bool allowDuplicate = false;
                    if (toolCall.Name == "read_file")
                    {
                        var readPath = ToolCallProcessor.ExtractPathFromToolCall(toolCall);
                        if (readPath != null)
                        {
                            var normalizedPath = readPath.TrimEnd('/', '\\');
                            if (_taskState.EditedFiles.Contains(normalizedPath))
                            {
                                allowDuplicate = true;
                            }
                            else if (_taskState.TruncatedFiles.Contains(normalizedPath))
                            {
                                var hasOffsetOrLimit = (toolCall.Arguments != null) &&
                                    (toolCall.Arguments.ContainsKey("offset") || toolCall.Arguments.ContainsKey("limit"));
                                if (hasOffsetOrLimit) allowDuplicate = true;
                            }
                        }
                    }

                    if (toolCall.Name != "attempt_completion" && toolCall.Name != "condense"
                        && toolCall.Name != "update_plan"
                        && !allowDuplicate
                        && !executedToolSignatures.Add(toolSignature))
                    {
                        System.Diagnostics.Debug.WriteLine($"[AICA] Skipping duplicate tool call: {toolCall.Name}");
                        _taskState.TotalToolCallCount++; // Count duplicates too for force-completion
                        var dupResult = ToolResult.Fail(
                            $"Duplicate call: {toolCall.Name} was already called with these arguments. " +
                            $"The result is in your conversation history — use it directly. " +
                            $"Do NOT retry this call.");
                        yield return AgentStep.ActionStart(ToolCallProcessor.BuildActionDescription(toolCall));
                        yield return AgentStep.ToolStart(toolCall);
                        yield return AgentStep.WithToolResult(toolCall, dupResult);
                        conversationHistory.Add(ChatMessage.ToolResult(toolCall.Id, $"Error: {dupResult.Error}"));
                        continue;
                    }

                    yield return AgentStep.ActionStart(ToolCallProcessor.BuildActionDescription(toolCall));
                    yield return AgentStep.ToolStart(toolCall);

                    _taskState.LastToolName = toolCall.Name;
                    _taskState.TotalToolCallCount++;

                    ToolResult result;
                    try
                    {
                        // Use ConfigureAwait(false) to avoid deadlock when resumed on UI thread
                        using (var toolCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                        {
                            toolCts.CancelAfter(TimeSpan.FromSeconds(60)); // 60s timeout per tool
                            result = await _toolDispatcher.ExecuteAsync(toolCall, context, uiContext, toolCts.Token).ConfigureAwait(false);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[AICA] Tool {toolCall.Name} exception: {ex.Message}");
                        result = ToolResult.Fail($"Tool execution error: {ex.Message}");
                    }

                    // H4: Record tool call for telemetry
                    telemetryBuilder.RecordToolCall(toolCall.Name, result.Success);
                    if (!result.Success && toolCall.Name == "edit")
                        telemetryBuilder.RecordEditFailureReason(result.Error);

                    // Track mistakes vs successes
                    if (result.Success)
                    {
                        _taskState.ResetFailureCounts();
                        // Track file edits by path
                        if (toolCall.Name == "edit")
                        {
                            var editPath = ToolCallProcessor.ExtractPathFromToolCall(toolCall);
                            if (!string.IsNullOrEmpty(editPath))
                                _taskState.EditedFiles.Add(editPath.TrimEnd('/', '\\'));
                        }

                        // Track auto-truncated files so subsequent offset reads are not blocked by dedup
                        if (toolCall.Name == "read_file" && result.Content != null && result.Content.StartsWith("[AUTO_TRUNCATED]"))
                        {
                            var readPath = ToolCallProcessor.ExtractPathFromToolCall(toolCall);
                            if (!string.IsNullOrEmpty(readPath))
                                _taskState.TruncatedFiles.Add(readPath.TrimEnd('/', '\\'));
                        }
                    }
                    else
                    {
                        if (_taskState.RecordToolFailure(ToolFailureKind.Blocking))
                        {
                            System.Diagnostics.Debug.WriteLine($"[AICA] Consecutive mistake threshold reached ({_taskState.ConsecutiveBlockingFailureCount})");
                        }

                        // R5: Failed tool calls should not block retry with identical arguments.
                        // BUT: Security denials are permanent — use ToolCallProcessor.ShouldAllowRetry().
                        if (ToolCallProcessor.ShouldAllowRetry(result))
                        {
                            executedToolSignatures.Remove(toolSignature);
                            System.Diagnostics.Debug.WriteLine($"[AICA] Tool '{toolCall.Name}' failed, removed from dedup set (allows retry)");
                        }
                        else
                        {
                            // SEC-01: Add to permanent security blacklist (blocks path variants)
                            ToolCallProcessor.AddToSecurityBlacklist(toolCall, securityBlacklist, context?.WorkingDirectory);
                            System.Diagnostics.Debug.WriteLine($"[AICA] Tool '{toolCall.Name}' security denied, keeping in dedup set (no retry)");
                        }
                    }

                    // Log tool result summary for debugging (visible in VS Output → Debug)
                    if (result.Success && result.Content != null)
                    {
                        var contentForLog = result.Content;
                        var statsIdx = contentForLog.LastIndexOf("[TOOL_EXACT_STATS:");
                        if (statsIdx >= 0)
                        {
                            System.Diagnostics.Debug.WriteLine($"[AICA] Tool '{toolCall.Name}' → {contentForLog.Substring(statsIdx).TrimEnd()}");
                        }
                        else
                        {
                            var preview = contentForLog.Length > 120 ? contentForLog.Substring(0, 120) + "..." : contentForLog;
                            System.Diagnostics.Debug.WriteLine($"[AICA] Tool '{toolCall.Name}' → ({contentForLog.Length} chars) {preview}");
                        }
                    }
                    else if (!result.Success)
                    {
                        System.Diagnostics.Debug.WriteLine($"[AICA] Tool '{toolCall.Name}' FAILED → {result.Error}");
                    }

                    yield return AgentStep.WithToolResult(toolCall, result);

                    // Yield plan update step when update_plan succeeds with steps
                    if (toolCall.Name == "update_plan" && result.Success && context.CurrentPlan?.Steps?.Count > 0)
                    {
                        _taskState.HasActivePlan = true;
                        yield return AgentStep.PlanUpdated(context.CurrentPlan);
                    }

                    // Handle attempt_completion result — ends the agent loop
                    if (toolCall.Name == "attempt_completion" && result.Success
                        && result.Content != null && result.Content.StartsWith("TASK_COMPLETED:"))
                    {
                        _taskState.IsCompleted = true;
                        // Pass the full serialized result to the UI layer for parsing
                        yield return AgentStep.Complete(result.Content);
                        yield break;
                    }

                    // Handle condense result — replace middle of conversation with summary
                    if (toolCall.Name == "condense" && result.Success
                        && result.Content != null && result.Content.StartsWith("CONDENSE:"))
                    {
                        var summary = result.Content.Substring("CONDENSE:".Length);

                        // P1-013 fix: Augment LLM summary with programmatic tool call history
                        // The LLM's summary may omit tool call details. Programmatic extraction ensures
                        // tool call records survive condense regardless of LLM summary quality.
                        var toolHistory = TokenBudgetManager.ExtractToolCallHistory(conversationHistory);
                        if (!string.IsNullOrEmpty(toolHistory))
                        {
                            summary = summary + "\n\n" + toolHistory;
                        }

                        System.Diagnostics.Debug.WriteLine($"[AICA] Condensing conversation with summary ({summary.Length} chars)");

                        // Record condense info for UI layer to persist
                        LastCondenseSummary = summary;
                        CondenseUpToMessageCount = conversationHistory.Count;

                        // Keep system prompt (index 0), the condensed summary, and the
                        // LATEST user message (the one that triggered this iteration).
                        // Previously we kept the FIRST user message (index 1), which caused
                        // the LLM to replay old tasks after condensation.
                        var condensed = new List<ChatMessage>();
                        if (conversationHistory.Count > 0)
                            condensed.Add(conversationHistory[0]); // system prompt

                        condensed.Add(ChatMessage.System(
                            "[Conversation condensed] The following is a summary of all previous work:\n" + summary));

                        // Find and preserve the last user message (current request)
                        bool foundUserMessage = false;
                        for (int i = conversationHistory.Count - 1; i >= 0; i--)
                        {
                            if (conversationHistory[i].Role == ChatRole.User)
                            {
                                condensed.Add(conversationHistory[i]);
                                foundUserMessage = true;
                                break;
                            }
                        }

                        // Safety guard: if no user message found, inject one to prevent
                        // LLM API errors (APIs require conversation to end on a user turn)
                        if (!foundUserMessage)
                        {
                            condensed.Add(ChatMessage.User(
                                "Context was condensed. Please continue with the current task based on the summary above."));
                        }

                        // Post-condense instruction: guide LLM to answer the current request (P1-012, P1-013, P1-POCO-012/013)
                        condensed.Add(ChatMessage.System(
                            "[Post-condense instruction] The conversation was condensed to save context space. " +
                            "The 'Tool Call History' section above is AUTO-EXTRACTED and FACTUAL — it lists every tool call made. " +
                            "You MUST answer the user's LATEST message based on the summary above. " +
                            "When the user asks about previous work (e.g., 'what files did I read?', '之前读取了哪些文件'): " +
                            "1) Your answer MUST be based EXCLUSIVELY on the Tool Call History section. " +
                            "2) If read_file appears with N paths, tell the user ALL N paths — do NOT say 'only 1 file'. " +
                            "3) Do NOT reclassify tool names — read_file stays read_file, NOT list_code_definition_names. " +
                            "4) Do NOT claim tools were not used if they appear in the history. " +
                            "Do NOT start a new task, replay old tasks, or explore new files unless the user asks."));

                        conversationHistory = condensed;
                        yield return AgentStep.TextChunk("\n\n📝 *Conversation condensed to save context space.*\n\n");
                        continue; // restart the loop with condensed history
                    }

                    // Add tool result to conversation
                    var resultContent = result.Success ? result.Content : $"Error: {result.Error}";
                    // Smart truncation: adapt to remaining budget and content type
                    if (resultContent != null)
                    {
                        int remainingBudget = Math.Max(1000, conversationBudget - conversationHistory.Sum(m => Context.ContextManager.EstimateTokens(m.Content)));
                        resultContent = Context.ContextManager.SmartTruncateToolResult(resultContent, toolCall.Name, remainingBudget);
                    }
                    conversationHistory.Add(ChatMessage.ToolResult(toolCall.Id, resultContent));

                    // If the user explicitly rejected an edit, stop retrying the same edit path.
                    // Treat this as valid user feedback, not as an execution error.
                    if (toolCall.Name == "edit" && result.Success &&
                        result.Content != null && result.Content.Contains("EDIT CANCELLED BY USER - NO CHANGES WERE APPLIED"))
                    {
                        _taskState.DidRejectTool = true;
                        _taskState.UserCancellationCount++;
                    }

                    // Track other user-initiated cancellations
                    if (!result.Success && result.Error != null &&
                        (result.Error.Contains("cancelled by user") || result.Error.Contains("User cancelled")))
                    {
                        _taskState.UserCancellationCount++;
                    }

                    // If user has cancelled too many times, stop and ask what to do
                    if (_taskState.UserCancellationCount >= TaskState.MaxUserCancellations)
                    {
                        System.Diagnostics.Debug.WriteLine($"[AICA] User cancelled {_taskState.UserCancellationCount} times, stopping to ask for guidance");

                        conversationHistory.Add(ChatMessage.User(
                            "⚠️ The user has cancelled or rejected multiple operations (" + _taskState.UserCancellationCount + " times). " +
                            "This indicates they may not agree with your current approach. " +
                            "You MUST immediately call the `ask_followup_question` tool to ask the user what they want to do next. " +
                            "Summarize what you have accomplished so far and provide options such as:\n" +
                            "- Continue with a different approach\n" +
                            "- End the task and keep current progress\n" +
                            "- Explain what specific changes they want\n" +
                            "Do NOT continue with more tool calls until the user responds."));

                        // Reset counter so if user chooses to continue, they get another window
                        _taskState.UserCancellationCount = 0;
                        break; // break out of tool execution loop, let the Agent respond
                    }
                }

                // ── Check if task should be completed ──
                // If files were edited/created but attempt_completion was not called, remind the AI
                bool hadFileOperations = toolCalls.Any(tc => tc.Name == "edit");
                bool hadAttemptCompletion = toolCalls.Any(tc => tc.Name == "attempt_completion");

                if (hadFileOperations && !hadAttemptCompletion)
                {
                    System.Diagnostics.Debug.WriteLine("[AICA] File operations detected but no attempt_completion. Will remind AI in next iteration.");
                    // Don't add reminder immediately - let AI respond first, then check if it calls attempt_completion
                }

                // After tool execution, if the original response was truncated (finish_reason=length),
                // the LLM's text was cut off mid-sentence. Ask it to continue its output.
                if (wasTruncated)
                {
                    System.Diagnostics.Debug.WriteLine("[AICA] Response was truncated after tool calls, asking LLM to continue its output");
                    conversationHistory.Add(ChatMessage.User("[System: Your previous text response was cut off due to token limit. The tool calls have been executed and their results are above. Please continue your response from where it was cut off.]"));
                }

                // Check if consecutive mistake threshold was reached this iteration
                if (_taskState.ConsecutiveBlockingFailureCount >= _taskState.MaxConsecutiveMistakes)
                {
                    if (_taskState.CanPromptRecovery())
                    {
                        // Try recovery: ask the Agent to use ask_followup_question instead of terminating
                        _taskState.RecordRecoveryPrompt();
                        _taskState.ResetBlockingFailuresForRecovery();

                        System.Diagnostics.Debug.WriteLine($"[AICA] Consecutive error threshold reached, injecting recovery prompt (attempt {_taskState.RecoveryPromptCount}/{_taskState.MaxRecoveryPrompts})");

                        yield return AgentStep.TextChunk($"\n\n⚠️ *遇到连续错误，正在尝试恢复 ({_taskState.RecoveryPromptCount}/{_taskState.MaxRecoveryPrompts})...*\n\n");

                        if (_taskState.HasActivePlan)
                        {
                            // Plan-aware recovery: ask LLM to update the plan instead of asking user
                            conversationHistory.Add(ChatMessage.User(
                                "⚠️ You have encountered multiple consecutive errors with your current approach. " +
                                "STOP trying the same approach. You have an active task plan — call `update_plan` to " +
                                "mark the failing step as 'failed' and adjust the remaining steps with an alternative strategy. " +
                                "Consider:\n" +
                                "- Marking the current step as 'failed'\n" +
                                "- Adding a new step with a different approach\n" +
                                "- Skipping to the next feasible step\n" +
                                "Do NOT retry the failed operation. Call `update_plan` now to adjust your strategy."));
                        }
                        else
                        {
                            conversationHistory.Add(ChatMessage.User(
                                "⚠️ You have encountered multiple consecutive errors with your current approach. " +
                                "STOP trying the same approach. Instead, you MUST call the `ask_followup_question` tool " +
                                "to ask the user for guidance. Summarize what you were trying to do, what errors occurred, " +
                                "and provide options such as:\n" +
                                "- Try a different approach\n" +
                                "- Skip this step and move on\n" +
                                "- End the task with current progress\n" +
                                "Do NOT retry the failed operation. Call `ask_followup_question` now."));
                        }
                        continue;
                    }
                    else
                    {
                        // Recovery attempts exhausted — terminate with stats
                        yield return AgentStep.WithError(
                            $"Agent 遇到 {_taskState.ConsecutiveBlockingFailureCount} 次连续错误，且恢复尝试已用尽。\n" +
                            $"共执行了 {_taskState.TotalToolCallCount} 次工具调用。\n" +
                            $"建议：请检查上方的工具执行日志，确认已完成的工作。如需继续，请发送新的指令。");
                        yield break;
                    }
                }
            }

            // H4: Determine outcome and write telemetry
            string telemetryOutcome;
            if (_taskState.IsCompleted) telemetryOutcome = "completed";
            else if (_taskState.Abort) telemetryOutcome = "aborted";
            else if (ct.IsCancellationRequested) telemetryOutcome = "user_cancelled";
            else if (_taskState.Iteration >= _maxIterations) telemetryOutcome = "timeout";
            else telemetryOutcome = "unknown";

            var sessionRecord = telemetryBuilder.Build(_taskState, telemetryOutcome);
            _ = Task.Run(async () =>
            {
                try
                {
                    var writer = new AgentTelemetryWriter();
                    await writer.WriteAsync(sessionRecord).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[AICA] H4 telemetry fire-and-forget failed: {ex.Message}");
                }
            });

            // 3.7: Save progress for checkpoint resume
            if (context?.WorkingDirectory != null && _taskState.DidEditFile)
            {
                var progress = new Storage.TaskProgress
                {
                    OriginalUserRequest = userRequest,
                    EditedFiles = _taskState.EditedFiles.ToList(),
                    CurrentPhase = _taskState.CurrentPhase,
                    PlanState = _taskState.HasActivePlan ? "active" : null
                };
                _ = Storage.TaskProgressStore.SaveAsync(context.WorkingDirectory, progress);
            }

            if (_taskState.Iteration >= _maxIterations)
            {
                yield return AgentStep.WithError(
                    $"已达到最大迭代次数 ({_taskState.Iteration}/{_maxIterations})。\n" +
                    $"共执行了 {_taskState.TotalToolCallCount} 次工具调用，发送了 {_taskState.ApiRequestCount} 次API请求。\n" +
                    $"建议：请检查上方的工具执行日志，确认已完成的工作。如需继续，请发送新的指令。");
            }
        }

        /// <summary>
        /// Build a summary from conversation history for auto-condense.
        /// Extracts key information without relying on LLM.
        /// </summary>
        // BuildAutoCondenseSummary, ExtractToolCallHistory, TryParseJsonArgs
        // extracted to TokenBudgetManager.cs

        // AugmentToolCallParameters extracted to ToolCallProcessor.cs

        // TryParseTextToolCalls, ExtractBalancedJsonBlocks, RemoveTextToolCallSyntax,
        // DetectToolExecutionClaim, DetectModificationConflict, SanitizeParameterValue,
        // GetToolCallSignature, ExtractPathFromToolCall, IsLikelyConversational,
        // IsToolPlanningText, IsLikelyConversationalImpl, BuildActionDescription,
        // GetFirstArgValue, Shorten
        // extracted to ToolCallProcessor.cs, ResponseProcessor.cs, CompletionHandler.cs, PlanManager.cs

        // All methods below this line have been extracted to separate files.
        // See: ToolCallProcessor.cs, ResponseProcessor.cs, CompletionHandler.cs, PlanManager.cs

        #region Delegating methods (backward compatibility)

        internal static bool IsLikelyConversational(string message) => ResponseProcessor.IsLikelyConversational(message);
        internal static bool IsToolPlanningText(string text) => ResponseProcessor.IsToolPlanningText(text);

        #endregion

        // === OLD METHODS REMOVED (extracted to ToolCallProcessor, ResponseProcessor, CompletionHandler, PlanManager) ===
        // TryParseTextToolCalls → ToolCallProcessor.TryParseTextToolCalls
        // ExtractBalancedJsonBlocks → ToolCallProcessor.ExtractBalancedJsonBlocks
        // RemoveTextToolCallSyntax → ToolCallProcessor.RemoveTextToolCallSyntax
        // SanitizeParameterValue → ToolCallProcessor.SanitizeParameterValue
        // GetToolCallSignature → ToolCallProcessor.GetToolCallSignature
        // ExtractPathFromToolCall → ToolCallProcessor.ExtractPathFromToolCall
        // BuildActionDescription → ToolCallProcessor.BuildActionDescription
        // GetFirstArgValue → ToolCallProcessor.GetFirstArgValue
        // Shorten → ToolCallProcessor.Shorten
        // DetectToolExecutionClaim → ResponseProcessor.DetectToolExecutionClaim
        // IsToolPlanningText → ResponseProcessor.IsToolPlanningText
        // IsLikelyConversationalImpl → ResponseProcessor.IsLikelyConversational
        // DetectModificationConflict → CompletionHandler.DetectModificationConflict
        // ==================================================================================

        public void Abort()
        {
            _llmClient.Abort();
        }

        private static bool IsTransientException(Exception ex)
        {
            if (ex is System.Net.Http.HttpRequestException) return true;
            if (ex is TaskCanceledException tce && tce.CancellationToken == default) return true;
            if (ex is System.IO.IOException) return true;
            if (ex.InnerException is System.Net.Sockets.SocketException) return true;
            if (ex.InnerException is System.IO.IOException) return true;
            return false;
        }

        private List<string> ExtractPathCandidates(string text)
        {
            if (string.IsNullOrEmpty(text))
                return new List<string>();

            var pathMatcher = new AICA.Core.Rules.Parsers.PathMatcher();
            return pathMatcher.ExtractPathCandidates(text);
        }

        /// <summary>
        /// Resolve GitNexus repo name from working directory by finding the git root
        /// and using its directory name. Returns null if no .git found.
        /// </summary>
        private static string ResolveGitNexusRepoName(string workingDirectory)
        {
            if (string.IsNullOrEmpty(workingDirectory))
                return null;

            var dir = workingDirectory;
            for (int i = 0; i < 10 && !string.IsNullOrEmpty(dir); i++)
            {
                if (System.IO.Directory.Exists(System.IO.Path.Combine(dir, ".git")))
                {
                    var repoName = System.IO.Path.GetFileName(dir);
                    System.Diagnostics.Debug.WriteLine($"[AICA] GitNexus repo name resolved: {repoName} (from {workingDirectory})");
                    return repoName;
                }
                dir = System.IO.Path.GetDirectoryName(dir);
            }
            return null;
        }
    }

    // AgentStep, AgentStepType, and IAgentExecutor extracted to separate files:
    // - AgentStep.cs
    // - IAgentExecutor.cs
}
