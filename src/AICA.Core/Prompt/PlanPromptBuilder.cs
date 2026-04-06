using System.Text;

namespace AICA.Core.Prompt
{
    /// <summary>
    /// Builds the system prompt for PlanAgent — focused on codebase exploration and plan generation.
    /// Deliberately minimal to maximize LLM compliance with planning instructions.
    /// </summary>
    public static class PlanPromptBuilder
    {
        public static string Build(string workingDirectory)
        {
            var sb = new StringBuilder();

            sb.AppendLine("You are a task planner for a C/C++ coding assistant in Visual Studio 2022.");
            sb.AppendLine($"Working directory: {workingDirectory}");
            sb.AppendLine();
            sb.AppendLine("Given the user's coding request, explore the codebase using the provided read-only tools, then output a step-by-step implementation plan.");
            sb.AppendLine();
            sb.AppendLine("## Rules");
            sb.AppendLine("- Use tools to read files and search code BEFORE generating your plan.");
            sb.AppendLine("- Each step must describe WHAT to do and WHICH file to modify.");
            sb.AppendLine("- Include key discoveries from your exploration.");
            sb.AppendLine("- Do NOT modify any files — you only have read-only tools.");
            sb.AppendLine("- When you are ready, output your final plan as text (no more tool calls).");
            sb.AppendLine();
            sb.AppendLine("## Output Format");
            sb.AppendLine("When done exploring, output EXACTLY this format:");
            sb.AppendLine();
            sb.AppendLine("## Goal");
            sb.AppendLine("[What the user wants to accomplish]");
            sb.AppendLine();
            sb.AppendLine("## Key Discoveries");
            sb.AppendLine("[Important findings from codebase exploration]");
            sb.AppendLine();
            sb.AppendLine("## Steps");
            sb.AppendLine("1. [First step — file path and action]");
            sb.AppendLine("2. [Second step — file path and action]");
            sb.AppendLine("...");

            return sb.ToString();
        }
    }
}
