using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AICA.Core.Agent;
using AICA.Core.Config;
using AICA.Core.Rules;

namespace AICA.Core.Tools
{
    /// <summary>
    /// Tool for loading skill content by name from .aica-rules/ files with type=skill.
    /// Backup mechanism for when passive injection doesn't match — LLM can explicitly
    /// request a skill (e.g., "use the bug-fix workflow").
    /// </summary>
    public class SkillTool : IAgentTool
    {
        public string Name => "use_skill";
        public string Description =>
            "Load a skill by name. Skills provide step-by-step workflows for common tasks " +
            "(bug-fix, feature-add, refactor, test-write). Use when you need structured guidance.";

        public ToolMetadata GetMetadata() => new ToolMetadata
        {
            Name = Name,
            Description = Description,
            Category = ToolCategory.Analysis,
            RequiresConfirmation = false,
            RequiresApproval = false,
            TimeoutSeconds = 5,
            IsModifying = false,
            Tags = new[] { "skill", "workflow", "template" }
        };

        public ToolDefinition GetDefinition() => new ToolDefinition
        {
            Name = Name,
            Description = Description,
            Parameters = new ToolParameters
            {
                Type = "object",
                Properties = new Dictionary<string, ToolParameterProperty>
                {
                    ["name"] = new ToolParameterProperty
                    {
                        Type = "string",
                        Description = "The skill name to load (e.g., 'bug-fix', 'feature-add', 'refactor', 'test-write')"
                    }
                },
                Required = new[] { "name" }
            }
        };

        public async Task<ToolResult> ExecuteAsync(
            ToolCall call, IAgentContext context, IUIContext uiContext, CancellationToken ct = default)
        {
            if (!AicaConfig.Current.Features.SkillsEnabled)
                return ToolResult.Fail("Skills are disabled (features.skillsEnabled=false).");

            try
            {
                var name = ToolParameterValidator.GetRequiredParameter<string>(call.Arguments, "name");
                var loader = new RuleLoader();
                var rules = await loader.LoadAllRulesAsync(context.WorkingDirectory, ct);

                // Filter to type=skill rules only
                var skills = rules.Where(r => r.Enabled && IsSkill(r)).ToList();

                // Match by frontmatter name (primary) or rule Id/Name (fallback)
                var match = skills.FirstOrDefault(r => SkillNameOf(r)
                    .Equals(name, StringComparison.OrdinalIgnoreCase));

                if (match != null)
                    return ToolResult.Ok(match.Content);

                // Not found — list available skills
                var available = skills.Select(r => SkillNameOf(r)).OrderBy(n => n);
                return ToolResult.Fail(
                    $"Skill '{name}' not found. Available skills: {string.Join(", ", available)}");
            }
            catch (ToolParameterException ex)
            {
                return ToolErrorHandler.HandleError(ToolErrorHandler.ParameterError(ex.Message));
            }
            catch (Exception ex)
            {
                var error = ToolErrorHandler.ClassifyException(ex, "use_skill");
                return ToolErrorHandler.HandleError(error);
            }
        }

        public Task HandlePartialAsync(ToolCall call, IUIContext ui, CancellationToken ct = default)
            => Task.CompletedTask;

        private static bool IsSkill(Rules.Models.Rule rule)
        {
            return rule.Metadata.Custom.TryGetValue("type", out var typeObj)
                   && "skill".Equals(typeObj?.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        private static string SkillNameOf(Rules.Models.Rule rule)
        {
            if (rule.Metadata.Custom.TryGetValue("name", out var nameObj)
                && nameObj is string customName && !string.IsNullOrEmpty(customName))
                return customName;
            return rule.Name;
        }
    }
}
