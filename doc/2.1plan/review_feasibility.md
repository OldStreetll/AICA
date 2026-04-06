# AICA v2.1 Unified Plan - Feasibility & Expected Impact Analysis

> **Reviewer**: Claude Opus 4.6 (automated review)
> **Date**: 2026-04-06
> **Scope**: 15 tasks (M1/M3/SK/H1/OH2/H3/H2/OH5/S3/PA1/OH3/S1/S4/S2/T2) + 3 cross-cutting items (T1/RC1/AS1)
> **Method**: Plan document analysis + source code verification against `/mnt/d/project/AICA/src/`

---

## Executive Summary

The plan is **architecturally sound and well-structured**, with accurate code location references and reasonable technical approaches. However, the **16-week timeline for a single developer is aggressive** — a more realistic estimate is **20-24 weeks** when accounting for cross-cutting overhead, integration testing, VS UI work, and inevitable debugging. The "weak model + strong system" strategy is the right paradigm, but several tasks **overestimate the improvement achievable with MiniMax-M2.5** as the consumer of system enhancements.

**Overall Verdict**: 70% of tasks are well-scoped and feasible as described. 20% need scope adjustment. 10% carry significant risk of delivering less value than claimed.

---

## Per-Task Analysis

### M1: Prune Timing Adjustment (2-3 days) — Phase 1

**Feasibility: HIGH** ✅

| Aspect | Assessment |
|--------|-----------|
| Code location | **VERIFIED.** `AgentExecutor.cs` (1,100 lines). `PruneOldToolOutputs` exists at line ~890-947. Compression logic at lines 180-233. |
| Work estimate | **Reasonable.** This is ~15 lines of call-order rearrangement. 2-3 days includes testing. |
| Technical risk | **LOW.** The function already exists; this is reordering, not new logic. |
| Hidden complexity | Possible interaction with `TokenBudgetManager.cs` (678 lines) thresholds — need to verify that Prune-then-check doesn't cause oscillation (prune frees tokens → below threshold → next turn overflows again). |

**Expected Impact: MODERATE** ⚠️

- The claim that Prune can avoid some Compaction calls is plausible but the **"Prune 后仍需 Compaction >90% 则失效"** threshold is correctly identified.
- Actual benefit depends heavily on how much token budget tool outputs consume vs. conversation history. If tool outputs are typically <20% of context, Prune savings will be marginal.
- **Recommendation**: Add telemetry for current tool-output-vs-total-context ratio BEFORE implementing, to validate the assumption.

---

### M3: Auto-Format After Edit (3-5 days) — Phase 1

**Feasibility: HIGH** ✅

| Aspect | Assessment |
|--------|-----------|
| Code location | **VERIFIED.** `EditFileTool.cs` (1,115 lines). Post-edit logic exists around AppendDiagnosticsAsync (line 401). |
| Work estimate | **Reasonable.** DTE.ExecuteCommand("Edit.FormatDocument") is a well-known VS API. Config switch is trivial. |
| Technical risk | **LOW-MEDIUM.** Risk: DTE format command is synchronous and may block on large files. Need timeout protection. Also, the DTE command requires the document to be open in the editor — if EditFileTool modifies via file system writes (not DTE text buffer), Format.Document won't work. |
| Hidden complexity | **IMPORTANT**: Need to verify EditFileTool's write mechanism. If it writes to disk directly (File.WriteAllText), the DTE format command requires reopening the document buffer first. This could add 1-2 days. |

**Expected Impact: MODERATE** ✅

- MiniMax-M2.5 likely produces inconsistent formatting, so auto-format is genuinely useful.
- The 90% "diff为空" failure signal is reasonable.
- This is a **quality-of-life improvement for the user**, not an Agent capability improvement.

---

### SK: Skills + Task Templates (5-8 days) — Phase 1

**Feasibility: MEDIUM-HIGH** ⚠️

| Aspect | Assessment |
|--------|-----------|
| Code locations | **VERIFIED.** Rule.cs (105 lines) — **currently has NO `Description` field** (plan correctly identifies this as a new addition). RuleLoader.cs (202 lines) already has frontmatter parsing. RuleEvaluator.cs (103 lines) already has paths glob matching. SystemPromptBuilder.cs (472 lines) has AddBugFixGuidance (line 76) and AddQtTemplateGuidance (line 88). DynamicToolSelector.cs (181 lines) has ClassifyIntent (line 153). |
| Work estimate | **Slightly underestimated.** 5-8 days for 8 steps is tight. Steps 1-4 (infrastructure) are ~3-4 days. Steps 5-6 (externalize + templates) are ~2-3 days. Step 7 (success criteria) is ~0.5 day. Step 8 (DynamicToolSelector change + baseline data collection) needs **1-2 weeks of baseline data** before execution, which creates a scheduling dependency not reflected in the plan. |
| Technical risk | **MEDIUM.** The SkillTool (~60-80 lines) is straightforward. The risk is in DynamicToolSelector modification — removing tool filtering is a behavioral change that could increase tool hallucination. The plan correctly identifies this risk and the mitigation (baseline → compare → rollback), but the **baseline collection period is not in the timeline**. |

**Expected Impact: HIGH for templates, UNCERTAIN for DynamicToolSelector change** ⚠️

- Task templates providing structured workflows to MiniMax is the **highest-value item in Phase 1**. Weak models benefit enormously from structured guidance.
- **Concern**: The plan assumes MiniMax will reliably invoke SkillTool when appropriate. This depends on the model's ability to recognize when to use a meta-tool — weak models often struggle with this. Consider auto-injection based on ClassifyIntent instead of relying on LLM-initiated SkillTool calls.
- DynamicToolSelector's tool filtering removal: the plan correctly notes the risk but **does not account for the 1-2 week baseline collection in the timeline**.

---

### H1: Tool Output Persistence (10-15 days) — Phase 2

**Feasibility: HIGH** ✅

| Aspect | Assessment |
|--------|-----------|
| Code locations | **VERIFIED.** EditFileTool.cs, ReadFileTool.cs, GrepSearchTool.cs, ListDirTool.cs, RunCommandTool.cs all exist in `/src/AICA.Core/Tools/`. |
| Work estimate | **Reasonable.** New ToolOutputPersistenceManager (~200 lines) + touching 6-8 tool files + cleanup mechanism + config. 10-15 days is appropriate. |
| Technical risk | **LOW.** File-based persistence is straightforward. The main risk is ensuring atomic writes on Windows (NTFS) and handling concurrent access. |
| Hidden complexity | The "逐个工具接入" step depends on each tool having a different truncation implementation. Need to audit each tool's current truncation pattern — they may not all be uniform. |

**Expected Impact: MODERATE** ⚠️

- The critical assumption is **"Agent 会回头查看被截断的完整输出"**. This depends entirely on MiniMax-M2.5's ability to:
  1. Notice the truncation hint in the tool result
  2. Decide to use read_file with offset/limit
  3. Actually follow through

- **With weak models, this chain of reasoning is unreliable.** The plan correctly identifies "truncation_file_read_count ≈ 0" as a failure signal.
- **Recommendation**: Instead of relying on the model to read back, consider a hybrid approach — automatically inject the most relevant portion of truncated output based on context (e.g., error messages from build output).

---

### OH2: Structured Memory Upgrade (5-8 days) — Phase 3

**Feasibility: MEDIUM** ⚠️

| Aspect | Assessment |
|--------|-----------|
| Code location | **VERIFIED.** MemoryBank.cs (102 lines). Confirmed: 4000 char hard truncation at line 43. Current implementation is simple file concatenation. |
| Work estimate | **Slightly underestimated.** The plan calls for: YAML frontmatter parsing, 4-category classification, relevance scoring with Chinese+English tokenization, dynamic token budgeting. This is closer to **8-12 days** for robust implementation. |
| Technical risk | **MEDIUM-HIGH.** Chinese text tokenization "按字分词 + 基础停用词表" is a known hard problem. Even the "simple first version" needs: character segmentation, stop word filtering, mixed CJK/ASCII handling, Unicode normalization. Testing coverage for CJK relevance scoring is non-trivial. |
| Hidden complexity | The relevance scoring (description 2x, body 1x) approach is essentially a bag-of-words model. For short queries, this may produce poor results. Consider adding: exact phrase matching bonus, recency decay factor. |

**Expected Impact: MODERATE-HIGH** ✅

- Replacing 4000-char hard truncation with relevance-based selection is a **clear improvement**.
- The claimed "节省 ~300-500 tokens/请求" is plausible if current memory injection is bloated.
- **Concern**: The "用户反馈'需要的记忆没被注入'" failure signal requires manual tracking — add a mechanism for the user to flag missed memories.

---

### H3: Permission Feedback Injection + Persistence (10 days) — Phase 3

**Feasibility: MEDIUM** ⚠️

| Aspect | Assessment |
|--------|-----------|
| Code locations | **VERIFIED.** ToolExecutionPipeline.cs (212 lines, middleware pattern). SafetyGuard.cs (453 lines, v2.3 permission system with PermissionRule/PermissionRuleEngine). |
| Work estimate | Part 1 (feedback injection, 5 days): **Reasonable** if the VS UI feedback dialog is simple. Part 2 (persistence, 5 days): **Reasonable** — JSON read/write + SafetyGuard integration. |
| Technical risk | **MEDIUM.** Part 1 requires VS UI work (dialog box for feedback input). The plan describes this as "弹出可选反馈输入框" — this is VSIX UI work that can be unpredictable. Part 2 has a subtle risk: loading permissions.json at startup and integrating with the existing PermissionRuleEngine (which uses glob→regex, line-based rules) without breaking the current system. |
| Hidden complexity | The "始终允许/始终拒绝" UI in VS toolbar/dialog needs careful UX — avoid creating a security footgun where users auto-approve dangerous operations. |

**Expected Impact: MODERATE** ✅

- Feedback injection is genuinely useful — telling the model WHY a tool was rejected (instead of just "denied") reduces retry loops.
- Permission persistence reduces friction for repeated sessions.
- **For MiniMax-M2.5**: The model should be able to parse "Permission denied. User feedback: {text}" — this is a pattern within most models' capability.

---

### H2: File Snapshot & Rollback (10-15 days) — Phase 4

**Feasibility: HIGH** ✅

| Aspect | Assessment |
|--------|-----------|
| Code location | **VERIFIED.** EditFileTool.cs (1,115 lines) is the integration point. |
| Work estimate | Core snapshot logic (5 days) + rollback API (3 days) + VS UI (5-7 days) = **13-15 days is realistic**. The VS UI portion (toolbar button, step visualization) is the highest-effort component. |
| Technical risk | **LOW-MEDIUM.** File copy is simple. Risks: handling file locks (VS may have files open), large solution snapshot sizes (2MB limit helps), session ID management across VS restarts. |
| Hidden complexity | The plan says "方案 A: 文件复制到 ~/.AICA/snapshots/" — this avoids git but means **no incremental/diff-based storage**. For active sessions with many edits, snapshot directory could grow quickly. The 7-day retention helps but could still be 100MB+ for active use. |

**Expected Impact: HIGH** ✅

- This is a **genuine safety net** that directly addresses MiniMax-M2.5's tendency to make wrong edits.
- Users will use this — it provides confidence to let the AI try more aggressive changes.
- The "回滚功能使用频率接近 0" failure signal is appropriate.

---

### OH5: SubAgent Generalization + ReviewAgent (5-8 days) — Phase 4

**Feasibility: MEDIUM** ⚠️

| Aspect | Assessment |
|--------|-----------|
| Code location | **VERIFIED.** PlanAgent.cs (254 lines) already implements a SubAgent-like pattern: independent token budget (16K), read-only tools, max iterations (10), timeout (60s). |
| Work estimate | **Underestimated.** Extracting a SubAgent base class from PlanAgent (3 days) + implementing ReviewAgent (2-3 days) + testing both configurations (2 days) + refactoring PlanAgent to use SubAgent (2 days) = **8-10 days more realistic**. The refactoring from concrete class to base class + configuration is always more work than expected. |
| Technical risk | **MEDIUM.** The SubAgent base class design is clean. Risk: PlanAgent has specific behaviors (tool result truncation at 3000 chars, finalization logic) that may not generalize cleanly. ReviewAgent's "检查清单式" approach depends on MiniMax being able to follow a structured checklist in a single iteration with 4K token budget. |

**Expected Impact: UNCERTAIN** ⚠️⚠️

- **This is the highest-risk item for expected value.** The plan honestly acknowledges "审查意见的用户采纳率 <20% 则简化或移除".
- **Critical concern**: MiniMax-M2.5 with a 4K token budget and NO tools (pure reasoning) doing code review. The model needs to:
  1. Parse a diff
  2. Apply 5 checklist dimensions
  3. Produce actionable feedback
  4. All in one iteration with 4K tokens

- Weak models typically produce generic, unhelpful reviews ("looks good", "consider error handling") under these constraints.
- **Recommendation**: Start with a single checklist dimension (e.g., consistency — "did the edit change a function signature without updating the header?") as a proof-of-concept before investing in all 5 dimensions.

---

### S3: Header Sync Detection (3-5 days) — Phase 4

**Feasibility: HIGH** ✅

| Aspect | Assessment |
|--------|-----------|
| Code location | **VERIFIED.** EditFileTool.cs (integration point), ProjectKnowledgeStore has SymbolRecord with FilePath/Signature/Namespace/Name fields (confirmed in KnowledgeContextProvider.cs usage). ISymbolParser.cs and SymbolParser.cs exist for parsing. |
| Work estimate | **Reasonable.** HeaderSyncDetector (~100 lines) + EditFileTool integration (~20 lines) + testing. 3-5 days is accurate. |
| Technical risk | **LOW-MEDIUM.** Depends on SymbolParser being able to reliably extract function signatures from both .cpp and .h files. If tree-sitter parsing is already working (TreeSitterSymbolParser.cs exists), this is straightforward. |

**Expected Impact: HIGH** ✅

- This is a **C/C++ killer feature**. Header/source sync is one of the most common errors, and MiniMax-M2.5 almost certainly misses this regularly.
- The system-level detection (no model reasoning needed) makes this reliable regardless of model quality.
- "警告后实际修改率 <30%" failure signal — this may be too generous. If MiniMax sees a clear "⚠️ Header sync needed: update foo.h:42" message, it should act on it >50% of the time.

---

### PA1: PlanAgent Output Optimization (2-3 days) — Phase 5

**Feasibility: HIGH** ✅

| Aspect | Assessment |
|--------|-----------|
| Code location | **VERIFIED.** PlanAgent.cs (254 lines), PlanPromptBuilder.cs exists, AgentExecutor.cs has injection points. |
| Work estimate | **Reasonable.** Prompt rewrite (1 day) + injection logic in AgentExecutor (1 day) + testing (0.5-1 day). |
| Technical risk | **LOW.** This is primarily prompt engineering + a small code change for repeated injection. |

**Expected Impact: MODERATE-HIGH** ✅

- "粗粒度目标" instead of "修改 foo.cpp 第42行" is the **correct approach for weak models**. Specific line references become stale immediately.
- "每轮重复注入" addresses "lost in the middle" — this is a known effective technique.
- **Concern**: Repeated injection consumes context budget. Need to balance injection frequency with token cost.

---

### OH3: Hooks System (5-8 days) — Phase 5

**Feasibility: MEDIUM** ⚠️

| Aspect | Assessment |
|--------|-----------|
| Code location | **VERIFIED.** ToolExecutionPipeline.cs (212 lines) uses middleware pattern — natural Hook insertion point. |
| Work estimate | **Underestimated.** Hook config loader (1-2 days) + CommandHookExecutor with shell execution (2-3 days) + AgentHookExecutor with ReviewAgent integration (2-3 days) + JSON config schema + testing + error handling = **8-12 days more realistic**. |
| Technical risk | **MEDIUM-HIGH.** Shell command execution (cmd.exe/powershell) in a VSIX extension has security implications. Need sandboxing, timeout handling, and careful path escaping. The "Agent Hook" calling ReviewAgent in parallel with user review is architecturally complex — race conditions between user confirmation and ReviewAgent completion. |
| Hidden complexity | The plan describes ReviewAgent running in parallel with user review, with results "追加显示" — this requires async UI updates in the VS diff dialog, which is non-trivial VSIX development. |

**Expected Impact: MODERATE** ⚠️

- Command Hooks (clang-format, audit logging) are genuinely useful and well-scoped.
- Agent Hooks (ReviewAgent integration) are the complex part with uncertain value (depends on OH5 ReviewAgent quality).
- The "用户从不配置自定义 Hook" failure signal suggests the team is aware this might be overengineered for a single-developer tool.

---

### S1: Symbol Retrieval Enhancement (5-8 days) — Phase 6

**Feasibility: MEDIUM** ⚠️

| Aspect | Assessment |
|--------|-----------|
| Code location | **VERIFIED.** KnowledgeContextProvider.cs (236 lines) has TF-IDF scoring (ComputeIdf at line 181). AgentExecutor.cs is the integration point. GitNexusProcessManager.cs exists. |
| Work estimate | **Reasonable for the core logic.** Async preheating + relationship graph integration + 3s timeout + fallback. 5-8 days covers it. |
| Technical risk | **MEDIUM.** GitNexus integration depends on the external process being responsive. The "3秒超时保护 + fallback" is correctly identified. Risk: if GitNexus is slow on large codebases, the 3s timeout fires frequently, and the feature provides no benefit. |

**Expected Impact: MODERATE** ⚠️

- Extending from TF-IDF Top-10 to relationship-graph Top-15-20 is a good idea in theory.
- **Concern**: The added symbols from relationship graph expansion may dilute the context with irrelevant information if the graph is too broad. d=1 (direct callers/callees only) would be safer than deeper traversal.
- The "关系图扩展的命中率 <10%" failure signal is appropriate.

---

### S4: GitNexus Proactive Trigger (4-6 days) — Phase 6

**Feasibility: HIGH** ✅

| Aspect | Assessment |
|--------|-----------|
| Code location | **VERIFIED.** EditFileTool.cs (integration point), DynamicToolSelector.cs has ClassifyIntent for condition matching. |
| Work estimate | **Reasonable.** Conditional trigger logic + GitNexus call + result formatting + timeout. 4-6 days. |
| Technical risk | **LOW-MEDIUM.** The constraints (d=1, 1000 tokens, 5s timeout) are well-chosen to limit blast radius. |

**Expected Impact: MODERATE-HIGH** ✅

- Impact warnings for public API changes are **highly valuable in C/C++ codebases**.
- The system-level detection (no model reasoning) makes this reliable.
- **Concern**: MiniMax-M2.5 may not effectively use the impact information even when provided. The model needs to translate "⚠️ Impact: 3 callers affected" into actual protective actions.

---

### S2: Auto Background Build (4-5 days) — Phase 6

**Feasibility: MEDIUM-HIGH** ✅

| Aspect | Assessment |
|--------|-----------|
| Code location | EditFileTool.cs (trigger), AgentExecutor.cs (injection). Need VSAgentContext for VS build API access. |
| Work estimate | **Reasonable.** VS incremental build API + BuildResultCache + injection logic. 4-5 days. |
| Technical risk | **MEDIUM.** VS build API behavior during active editing sessions can be unpredictable. Incremental builds may fail or take too long on large solutions. Need to handle "build already in progress" scenarios. |

**Expected Impact: HIGH** ✅

- Compiler error feedback is **objectively useful** — it's ground truth, not model inference.
- This is one of the highest-value items because it provides the model with **real signal** (compiler errors) instead of relying on its own reasoning.
- **Strong recommendation**: Prioritize this over some Phase 4-5 items.

---

### T2: Telemetry Session Summary (2-3 days) — Phase 6

**Feasibility: HIGH** ✅

| Aspect | Assessment |
|--------|-----------|
| Work estimate | **Reasonable.** JSON aggregation at session end. Straightforward. |
| Technical risk | **LOW.** |

**Expected Impact: LOW-MODERATE** ✅

- Useful for development iteration but doesn't directly improve Agent performance.
- Enables data-driven decisions for future improvements.

---

## Cross-Cutting Items Analysis

### T1: Structured Telemetry Logging (+0.5 day per component)

**Assessment: UNDERESTIMATED** ⚠️⚠️

- "+0.5天 per component" assumes telemetry is just adding log lines. In practice:
  - First component needs telemetry infrastructure setup (log format, file rotation, structured output) — add **2-3 days upfront**.
  - Subsequent components are indeed ~0.5 day each.
  - 15 components × 0.5 = 7.5 days + 2-3 days infrastructure = **~10 days total**, not "融入各任务".
- **Risk**: If telemetry infrastructure is deferred, each component implements its own format, requiring later unification.
- **Recommendation**: Implement telemetry infrastructure in Phase 1 alongside M1/M3 (add 2-3 days to Phase 1).

### RC1: ReviewAgent Checklist Design (included in OH5)

**Assessment: REASONABLE** ✅

- The 5-dimension checklist is well-thought-out.
- Including it in OH5's timeline makes sense since it's primarily prompt engineering.
- **Concern**: The checklist effectiveness depends entirely on MiniMax-M2.5's ability to follow structured instructions in a constrained context.

### AS1: Assumption Documentation (+0.25 day per component)

**Assessment: REASONABLE** ✅

- 0.25 day per component for documentation is appropriate.
- Total: ~4 days across all components, which is manageable.
- The assumption/failure-signal/validation pattern in the plan is **excellent engineering practice**.

---

## EditFileTool 7-Item Modification Conflict Risk

**Risk Level: MEDIUM-HIGH** ⚠️⚠️

EditFileTool.cs is 1,115 lines and will be touched by 7 different tasks across 5 phases:

| # | Task | Phase | Modification Point |
|---|------|-------|-------------------|
| 1 | H2 | 4 | Pre-edit: snapshot |
| 2 | M3 | 1 | Post-edit: format |
| 3 | S3 | 4 | Post-edit: header sync |
| 4 | S4 | 6 | Post-edit: impact analysis |
| 5 | H1 | 2 | Post-edit: truncation |
| 6 | S2 | 6 | Post-edit: async build |
| 7 | T1 | All | Throughout: telemetry |

**Concerns**:

1. **Ordering correctness**: The plan specifies M3 → S3 → S4 → H1 → S2 ordering, which is logically correct. BUT implementing these across 5 phases means each new addition must be inserted at the correct position in an increasingly complex post-edit pipeline.

2. **Error propagation**: If M3 (format) fails, should S3 (header sync) still run? The plan doesn't specify error handling between pipeline stages.

3. **Performance accumulation**: 6 post-edit operations (format + header sync + impact + truncation check + diagnostics + async build) could make each edit noticeably slow. Need to measure cumulative latency.

4. **Testability**: Testing all 7 modifications together requires integration tests that exercise the full pipeline. Each new addition needs tests for both isolated behavior AND interaction with existing modifications.

**Recommendation**: 
- Extract the post-edit pipeline into a dedicated `PostEditPipeline` class early (Phase 1, with M3), using a chain-of-responsibility pattern. This makes subsequent additions cleaner and testable.
- Define clear error handling: each stage should be independently failable (fail-open) except H2 snapshot (fail-close — don't edit without snapshot).

---

## 16-Week Single-Developer Timeline Assessment

**Verdict: OPTIMISTIC by ~4-8 weeks** ⚠️⚠️⚠️

### Calculation

| Category | Plan Estimate | Realistic Estimate | Delta |
|----------|--------------|-------------------|-------|
| Task sum (min) | ~72 days | ~85 days | +13 days |
| Task sum (max) | ~102 days | ~120 days | +18 days |
| T1 infrastructure | 0 (embedded) | 2-3 days | +3 days |
| T1 per-component | 7.5 days (embedded) | 10 days | +2.5 days |
| Integration testing | Not explicit | ~8-10 days | +10 days |
| VS UI work (H2, H3, OH3) | Partially accounted | +5-8 days | +8 days |
| Bug fixing / rework | Not accounted | ~10-15 days (10-15%) | +15 days |
| **Total** | **80 days (16 weeks)** | **105-130 days (21-26 weeks)** | **+25-50 days** |

### Specific Timeline Risks

1. **Phase 1 (Week 1-2)**: SK's 5-8 days + M1's 2-3 days + M3's 3-5 days = 10-16 days. Fits in 2 weeks only at the minimum estimate.

2. **Phase 2 (Week 3-5)**: H1's 10-15 days is a large, cross-cutting change. Touching 6-8 tool files sequentially is realistic at 15 days.

3. **Phase 3 (Week 6-8)**: OH2 + H3 = 15-18 days. OH2's Chinese tokenization alone could consume the buffer. H3's VS UI work is unpredictable.

4. **Phase 4 (Week 9-11)**: H2 + OH5 + S3 = 18-28 days in 15 calendar days. This is the **most over-packed phase**. H2's VS UI (rollback toolbar) + OH5's refactoring + S3 = realistically 4 weeks.

5. **Phase 5 (Week 12-13)**: PA1 + OH3 = 7-11 days. OH3's shell execution + async UI = likely 10-12 days.

6. **Phase 6 (Week 14-16)**: S1 + S4 + S2 + T2 = 15-22 days in 15 calendar days. Tight but possible since items are more independent.

### Recommendation

- **Add 2-week buffer** after Phase 3 and Phase 5 for integration testing and stabilization.
- **Realistic target**: 22-24 weeks (5.5-6 months).
- The plan's Phase-based independence is a **major strength** — it allows stopping at any phase boundary with delivered value.

---

## MiniMax-M2.5 Capability Constraints

### What the Plan Gets Right

1. **System-level compensation** (S3 header sync, S4 impact, S2 build errors) — these don't depend on model intelligence.
2. **Structured templates** (SK) — weak models benefit most from explicit structure.
3. **CheckList-based review** (OH5 ReviewAgent) — pattern matching over open reasoning.
4. **"不反馈给 LLM 做自动纠错"** — correct decision. Self-correction loops with weak models diverge.

### What the Plan May Overestimate

| Assumption | Risk |
|-----------|------|
| MiniMax will invoke SkillTool appropriately | Weak models struggle with meta-tools. Auto-injection is safer. |
| MiniMax will read back truncated outputs (H1) | Multi-step reasoning chains are unreliable with weak models. |
| MiniMax will act on impact warnings (S4) | The model may see "⚠️ Impact: 3 callers" and ignore it. |
| ReviewAgent (4K budget, no tools) produces useful reviews | Generic reviews are the likely outcome. |
| MiniMax follows coarse-grained plans better (PA1) | True in theory, but "better" may still mean <50% step completion. |

### Recommendations

1. **Prefer auto-injection over LLM-initiated retrieval** wherever possible (SK, H1).
2. **Make system-level features (S3, S4, S2) the highest priority** — they're model-independent.
3. **Budget for A/B testing**: Many improvements need before/after comparison to validate. The telemetry infrastructure should be Phase 1 priority.
4. **Set realistic baselines**: Measure current task completion rates before implementing, to quantify actual improvement.

---

## Additional Risks Not Identified in the Plan

1. **VS2022 VSIX API stability**: The plan assumes DTE APIs work reliably for format, build, and diagnostics. VS API behavior can be version-dependent — test on the specific VS2022 version deployed.

2. **Disk space in offline environment**: Snapshots (~/.AICA/snapshots/), truncations (~/.AICA/truncations/), telemetry (~/.AICA/telemetry/), and memory (~/.aica/memory/) all write to user profile. In a 涉密离线环境, disk quotas may be restrictive.

3. **No rollback plan for individual features**: The plan has per-task failure signals but no mechanism to disable individual features without code changes. Consider feature flags from the start.

4. **Test coverage gap**: The plan doesn't mention test strategy. Current tests exist (AICA.Core.Tests/) but 7 modifications to EditFileTool need comprehensive integration tests. Budget 1-2 days per phase for test writing.

5. **ConversationCompactor interaction**: M1 changes Prune→Compaction ordering, but ConversationCompactor.cs (196 lines, separate file) may have assumptions about when it's called. Verify ConversationCompactor doesn't depend on Prune NOT having run.

---

## Priority Reordering Recommendation

Based on feasibility and expected impact, the optimal priority order differs from the plan:

| Rank | Task | Reason |
|------|------|--------|
| 1 | M1 | Quick win, low risk, enables M1+H1 synergy |
| 2 | M3 | Quick win, direct user benefit |
| 3 | SK (templates only) | High impact for weak model, defer DynamicToolSelector change |
| 4 | S2 | **Move up**: Compiler feedback is highest-value model-independent signal |
| 5 | S3 | Model-independent, C/C++ killer feature |
| 6 | H1 | Important infrastructure, but model may not use it well |
| 7 | H2 | Safety net, user confidence |
| 8 | PA1 | Low cost, moderate impact |
| 9 | OH2 | Important but complex |
| 10 | S4 | Good value, depends on SK |
| 11 | H3 | Moderate value, VS UI risk |
| 12 | OH5 | Uncertain value with weak model |
| 13 | S1 | Moderate value, GitNexus dependency |
| 14 | OH3 | High complexity, uncertain adoption |
| 15 | T2 | Nice to have |

**Key change**: Move S2 (auto build) and S3 (header sync) earlier — they provide **model-independent ground truth** that's more valuable than model-dependent features.

---

## Conclusion

The AICA v2.1 Unified Plan is a **well-researched, architecturally coherent document** with excellent practices (assumption tracking, failure signals, telemetry-driven validation). The code location references are **verified accurate** against the current codebase.

**Key adjustments needed**:
1. **Timeline**: Plan for 22-24 weeks, not 16. Add integration buffers.
2. **EditFileTool**: Extract post-edit pipeline early to manage 7-modification complexity.
3. **MiniMax expectations**: Prefer system-level features over model-dependent ones. Auto-inject instead of relying on LLM retrieval.
4. **Cross-cutting overhead**: T1 telemetry needs upfront infrastructure investment (~3 days in Phase 1).
5. **Feature flags**: Add a simple enable/disable mechanism for each feature to support incremental rollout and rollback.

The plan's greatest strength is its **phase independence** — each phase delivers standalone value. This is the correct architecture for a single-developer project with uncertain model capabilities.

===ANALYSIS_COMPLETE===
