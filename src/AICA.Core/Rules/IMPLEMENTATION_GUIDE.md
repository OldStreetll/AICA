# AIHelper Rules System - Implementation Guide

## Architecture Overview

The AIHelper rules system is a modular, extensible framework for loading and evaluating project-specific rules. It follows a fail-open design philosophy where invalid rules don't break the system.

### Components

```
┌─────────────────────────────────────────────────────────────┐
│                    SystemPromptBuilder                       │
│  (Integrates rules into system prompt)                       │
└────────────────────┬────────────────────────────────────────┘
                     │
        ┌────────────┴────────────┐
        │                         │
┌───────▼──────────┐    ┌────────▼──────────┐
│   RuleLoader     │    │  RuleEvaluator    │
│ (Load from disk) │    │ (Evaluate rules)  │
└────────┬─────────┘    └────────┬──────────┘
         │                       │
         │      ┌────────────────┘
         │      │
    ┌────▼──────▼──────────┐
    │  Rule Models         │
    │  - Rule              │
    │  - RuleMetadata      │
    │  - RuleSource        │
    └─────────────────────┘
         │
    ┌────▼──────────────────┐
    │  Parsers              │
    │  - YamlFrontmatter    │
    │  - PathMatcher        │
    └──────────────────────┘
```

### Data Flow

```
1. User Request
   ↓
2. AgentExecutor.ExecuteAsync()
   ↓
3. Extract candidate paths from request
   ↓
4. Create RuleContext with paths
   ↓
5. RuleLoader.LoadAllRulesAsync()
   ├─ Load local rules (.aica-rules/)
   └─ Load global rules (~/.aica/rules/)
   ↓
6. RuleEvaluator.EvaluateRules()
   ├─ Check enabled status
   ├─ Match paths
   └─ Sort by priority
   ↓
7. SystemPromptBuilder.AddRulesFromFilesAsync()
   ├─ Merge activated rules
   └─ Add to system prompt
   ↓
8. System Prompt with Rules
   ↓
9. LLM receives enhanced prompt
```

## Core Components

### 1. Rule Models

**Rule.cs**
- Represents a single rule with metadata
- Properties: Id, Name, Content, Metadata, Source, Priority, Enabled, FilePath, LoadedAt
- Immutable design (no mutations)

**RuleMetadata.cs**
- Extracted from YAML frontmatter
- Properties: Paths (glob patterns), Custom (additional metadata)

**RuleSource enum**
- Builtin (0) - Default rules
- Remote (5) - Future remote rules
- Global (10) - User's global rules
- Workspace (20) - Project-specific rules

### 2. Parsers

**YamlFrontmatterParser.cs**
- Parses YAML frontmatter from Markdown files
- Fail-open strategy: invalid YAML preserves original content
- Supports: strings, numbers, booleans, lists, nested objects
- Returns: FrontmatterParseResult with data, body, error info

**PathMatcher.cs**
- Matches file paths against glob patterns
- Supports: `**`, `*`, `?` wildcards
- Case-insensitive matching
- Extracts path candidates from text using heuristics

### 3. Rule Loading

**RuleLoader.cs**
- Loads rules from filesystem
- Methods:
  - `LoadLocalRulesAsync()` - Load from `.aica-rules/`
  - `LoadGlobalRulesAsync()` - Load from `~/.aica/rules/`
  - `LoadAllRulesAsync()` - Load both local and global
- Recursive directory traversal
- Extracts metadata from YAML frontmatter
- Handles errors gracefully

### 4. Rule Evaluation

**RuleEvaluator.cs**
- Evaluates rule activation conditions
- Methods:
  - `EvaluateRule()` - Check if single rule should activate
  - `EvaluateRules()` - Evaluate all rules, return activated
  - `MergeRules()` - Merge by priority, keep highest version
- Considers: enabled status, path conditions, priority

**RuleContext.cs**
- Context for rule evaluation
- Properties: WorkingDirectory, CandidatePaths, Custom
- Passed to evaluator for condition checking

### 5. System Prompt Integration

**SystemPromptBuilder.cs**
- Enhanced with `AddRulesFromFilesAsync()` method
- Loads rules asynchronously
- Evaluates rules based on context
- Integrates activated rules into system prompt
- Maintains backward compatibility

## Design Patterns

### 1. Fail-Open Strategy

Invalid rules don't break the system:
- YAML parsing errors → preserve original content
- File read errors → log and continue
- Invalid paths → skip rule
- Missing directories → return empty list

### 2. Immutability

All data models are immutable:
- No property setters (except initialization)
- New objects created for modifications
- Prevents hidden side effects

### 3. Dependency Injection

Components accept dependencies:
- ILogger for logging
- RuleContext for evaluation context
- Enables testing with mocks

### 4. Priority-Based Merging

Rules are merged by priority:
- Higher priority overrides lower
- Same ID → keep highest priority version
- Enables rule inheritance and overrides

### 5. Lazy Evaluation

Rules are evaluated only when needed:
- Loaded on-demand during agent execution
- Evaluated based on current context
- Reduces unnecessary processing

## Testing Strategy

### Unit Tests

**YamlFrontmatterParserTests.cs**
- Valid/invalid YAML parsing
- Frontmatter extraction
- Fail-open behavior
- Edge cases (empty, multiple dashes, etc.)

**PathMatcherTests.cs**
- Glob pattern matching
- Case-insensitive matching
- Path extraction from text
- URL exclusion

**RuleEvaluatorTests.cs**
- Rule activation conditions
- Priority sorting
- Rule merging
- Path matching integration

**RuleLoaderTests.cs**
- File loading from directories
- Recursive directory traversal
- Metadata extraction
- Error handling

### Integration Tests

- End-to-end rule loading and evaluation
- System prompt integration
- Agent executor integration
- Real file system operations

### Test Coverage

Target: 80%+ code coverage
- All public methods tested
- Happy paths and error cases
- Boundary conditions
- Integration scenarios

## Performance Considerations

### Optimization Strategies

1. **Lazy Loading**: Rules loaded only when needed
2. **Caching**: Consider caching parsed rules
3. **Parallel Loading**: Load local and global rules in parallel
4. **Efficient Matching**: Compiled regex patterns for path matching

### Performance Targets

- Rule loading: < 100ms
- Rule evaluation: < 50ms
- Path matching: < 10ms per rule
- System prompt generation: < 200ms

## Security Considerations

### Input Validation

- Validate file paths before reading
- Sanitize YAML content
- Prevent directory traversal attacks
- Validate glob patterns

### Error Handling

- Never expose internal errors to user
- Log detailed errors for debugging
- Fail gracefully on invalid input
- Maintain system stability

## Future Enhancements

### Planned Features

1. **Remote Rules**: Load rules from remote sources
2. **Rule Caching**: Cache parsed rules for performance
3. **Rule Versioning**: Support rule versions
4. **Conditional Rules**: More complex activation conditions
5. **Rule Composition**: Combine rules into groups
6. **Hot Reload**: Reload rules without restart

### Extension Points

- Custom rule sources (database, API, etc.)
- Custom path matchers
- Custom metadata extractors
- Custom evaluation strategies

## Troubleshooting Guide

### Common Issues

**Rules not loading**
- Check `.aica-rules/` directory exists
- Verify file permissions
- Check for YAML parsing errors
- Review logs for error messages

**Rules not activating**
- Verify `enabled: true`
- Check path patterns
- Test glob patterns
- Review rule priority

**Performance issues**
- Profile rule loading time
- Check for large rule files
- Optimize path matching
- Consider caching

## API Documentation

### RuleLoader

```csharp
public class RuleLoader
{
    public async Task<List<Rule>> LoadLocalRulesAsync(
        string workspacePath,
        CancellationToken ct = default);

    public async Task<List<Rule>> LoadGlobalRulesAsync(
        CancellationToken ct = default);

    public async Task<List<Rule>> LoadAllRulesAsync(
        string workspacePath,
        CancellationToken ct = default);
}
```

### RuleEvaluator

```csharp
public class RuleEvaluator
{
    public bool EvaluateRule(Rule rule, RuleContext context);

    public List<Rule> EvaluateRules(
        List<Rule> rules,
        RuleContext context);

    public List<Rule> MergeRules(List<Rule> rules);
}
```

### SystemPromptBuilder

```csharp
public class SystemPromptBuilder
{
    public async Task<SystemPromptBuilder> AddRulesFromFilesAsync(
        string workspacePath,
        RuleContext context = null,
        ILogger<SystemPromptBuilder> logger = null,
        CancellationToken ct = default);
}
```

## References

- [Rule File Format](../.aica-rules/README.md)
- [Sample Rules](../.aica-rules/)
- [Test Files](../../tests/Rules/)
