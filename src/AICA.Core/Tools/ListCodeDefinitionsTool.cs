using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AICA.Core.Agent;

namespace AICA.Core.Tools
{
    /// <summary>
    /// Tool for listing code definitions (classes, methods, properties, etc.) in source files.
    /// Uses regex-based parsing to extract definitions from C#, Python, JavaScript/TypeScript, and Java files.
    /// </summary>
    public class ListCodeDefinitionsTool : IAgentTool
    {
        public string Name => "list_code_definition_names";
        public string Description => "List all code definitions (classes, methods, properties, etc.) in a file or directory. " +
            "Useful for understanding code structure without reading entire files.";

        private static readonly HashSet<string> SupportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".cs", ".py", ".js", ".ts", ".tsx", ".jsx", ".java",
            ".c", ".cpp", ".cc", ".cxx", ".h", ".hpp", ".hxx"
        };

        private static readonly HashSet<string> ExcludedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".git", ".vs", ".vscode", "bin", "obj", "node_modules", "packages",
            "__pycache__", ".idea", "dist", ".next", ".nuget", "TestResults"
        };

        public ToolDefinition GetDefinition()
        {
            return new ToolDefinition
            {
                Name = Name,
                Description = Description,
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolParameterProperty>
                    {
                        ["path"] = new ToolParameterProperty
                        {
                            Type = "string",
                            Description = "File or directory path (relative to workspace root). For directories, recursively scans all supported source files."
                        }
                    },
                    Required = new[] { "path" }
                }
            };
        }

        public Task<ToolResult> ExecuteAsync(ToolCall call, IAgentContext context, CancellationToken ct = default)
        {
            string relativePath = ".";
            if (call.Arguments.TryGetValue("path", out var pathObj) && pathObj != null)
            {
                relativePath = pathObj.ToString();
            }

            if (string.IsNullOrWhiteSpace(relativePath) || relativePath == "/" || relativePath == "\\")
                relativePath = ".";

            if (!context.IsPathAccessible(relativePath))
                return Task.FromResult(ToolResult.Fail($"Access denied: {relativePath}"));

            string fullPath;
            if (relativePath == "." || relativePath == "./")
                fullPath = context.WorkingDirectory;
            else if (Path.IsPathRooted(relativePath))
                fullPath = relativePath;
            else
                fullPath = Path.Combine(context.WorkingDirectory, relativePath);

            var sb = new StringBuilder();
            int fileCount = 0;
            const int maxFiles = 50;

            if (File.Exists(fullPath))
            {
                var defs = ParseFile(fullPath);
                if (defs != null)
                {
                    sb.AppendLine($"{relativePath}:");
                    foreach (var line in defs)
                        sb.AppendLine($"  {line}");
                }
                else
                {
                    return Task.FromResult(ToolResult.Fail($"Unsupported file type. Supported: {string.Join(", ", SupportedExtensions)}"));
                }
            }
            else if (Directory.Exists(fullPath))
            {
                ScanDirectory(fullPath, context.WorkingDirectory, sb, ref fileCount, maxFiles);
                if (fileCount == 0)
                    sb.AppendLine("No supported source files found.");
                if (fileCount >= maxFiles)
                    sb.AppendLine($"\n... (output truncated at {maxFiles} files)");
            }
            else
            {
                return Task.FromResult(ToolResult.Fail($"Path not found: {relativePath}"));
            }

            return Task.FromResult(ToolResult.Ok(sb.ToString()));
        }

        private void ScanDirectory(string dirPath, string workingDir, StringBuilder sb, ref int fileCount, int maxFiles)
        {
            if (fileCount >= maxFiles) return;

            // Process files in current directory
            try
            {
                var files = Directory.GetFiles(dirPath)
                    .Where(f => SupportedExtensions.Contains(Path.GetExtension(f)))
                    .OrderBy(f => f)
                    .ToArray();

                foreach (var file in files)
                {
                    if (fileCount >= maxFiles) return;

                    var defs = ParseFile(file);
                    if (defs != null && defs.Count > 0)
                    {
                        var rel = GetRelativePath(file, workingDir);
                        sb.AppendLine($"{rel}:");
                        foreach (var line in defs)
                            sb.AppendLine($"  {line}");
                        sb.AppendLine();
                        fileCount++;
                    }
                }

                // Recurse into subdirectories
                foreach (var subDir in Directory.GetDirectories(dirPath).OrderBy(d => d))
                {
                    if (fileCount >= maxFiles) return;
                    var dirName = Path.GetFileName(subDir);
                    if (ExcludedDirs.Contains(dirName)) continue;
                    ScanDirectory(subDir, workingDir, sb, ref fileCount, maxFiles);
                }
            }
            catch (UnauthorizedAccessException) { }
        }

        private List<string> ParseFile(string filePath)
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (!SupportedExtensions.Contains(ext))
                return null;

            string content;
            try
            {
                content = File.ReadAllText(filePath);
            }
            catch
            {
                return new List<string> { "[error reading file]" };
            }

            switch (ext)
            {
                case ".cs":
                    return ParseCSharp(content);
                case ".py":
                    return ParsePython(content);
                case ".js":
                case ".jsx":
                case ".ts":
                case ".tsx":
                    return ParseJavaScriptTypeScript(content);
                case ".java":
                    return ParseJava(content);
                case ".c":
                case ".cpp":
                case ".cc":
                case ".cxx":
                case ".h":
                case ".hpp":
                case ".hxx":
                    return ParseCpp(content);
                default:
                    return null;
            }
        }

        #region C# Parser

        private List<string> ParseCSharp(string content)
        {
            var results = new List<string>();

            // Namespace
            var nsPattern = new Regex(@"^\s*namespace\s+([\w.]+)", RegexOptions.Multiline);
            foreach (Match m in nsPattern.Matches(content))
                results.Add($"namespace {m.Groups[1].Value}");

            // Classes, structs, interfaces, enums, records
            var typePattern = new Regex(
                @"^\s*((?:public|private|protected|internal|static|abstract|sealed|partial)\s+)*" +
                @"(class|struct|interface|enum|record)\s+" +
                @"(\w+)(?:<[^>]+>)?(?:\s*:\s*([^\{]+?))?(?:\s*\{|\s*$)",
                RegexOptions.Multiline);
            foreach (Match m in typePattern.Matches(content))
            {
                var modifiers = (m.Groups[1].Value ?? "").Trim();
                var keyword = m.Groups[2].Value;
                var name = m.Groups[3].Value;
                var baseTypes = m.Groups[4].Success ? m.Groups[4].Value.Trim().TrimEnd('{').Trim() : "";
                var line = string.IsNullOrEmpty(modifiers) ? $"{keyword} {name}" : $"{modifiers}{keyword} {name}";
                if (!string.IsNullOrEmpty(baseTypes))
                    line += $" : {baseTypes}";
                results.Add(line);
            }

            // Methods (including async, generic, etc.)
            var methodPattern = new Regex(
                @"^\s*((?:public|private|protected|internal|static|virtual|override|abstract|async|new|sealed)\s+)+" +
                @"([\w<>\[\]?,\s]+?)\s+" +
                @"(\w+)\s*(?:<[^>]+>)?\s*\(([^)]*)\)",
                RegexOptions.Multiline);
            foreach (Match m in methodPattern.Matches(content))
            {
                var modifiers = m.Groups[1].Value.Trim();
                var returnType = m.Groups[2].Value.Trim();
                var name = m.Groups[3].Value;
                var parameters = SimplifyParameters(m.Groups[4].Value.Trim());

                // Skip things that look like control flow or type definitions
                if (IsControlFlowKeyword(name) || IsTypeKeyword(returnType))
                    continue;

                results.Add($"  {modifiers} {returnType} {name}({parameters})");
            }

            // Properties
            var propPattern = new Regex(
                @"^\s*((?:public|private|protected|internal|static|virtual|override|abstract|new)\s+)+" +
                @"([\w<>\[\]?,\s]+?)\s+" +
                @"(\w+)\s*\{\s*(get|set)",
                RegexOptions.Multiline);
            foreach (Match m in propPattern.Matches(content))
            {
                var modifiers = m.Groups[1].Value.Trim();
                var type = m.Groups[2].Value.Trim();
                var name = m.Groups[3].Value;
                if (!IsTypeKeyword(type))
                    results.Add($"  {modifiers} {type} {name} {{ get; set; }}");
            }

            return results;
        }

        #endregion

        #region Python Parser

        private List<string> ParsePython(string content)
        {
            var results = new List<string>();

            // Classes
            var classPattern = new Regex(@"^class\s+(\w+)(?:\(([^)]*)\))?\s*:", RegexOptions.Multiline);
            foreach (Match m in classPattern.Matches(content))
            {
                var name = m.Groups[1].Value;
                var bases = m.Groups[2].Success ? m.Groups[2].Value.Trim() : "";
                results.Add(string.IsNullOrEmpty(bases) ? $"class {name}" : $"class {name}({bases})");
            }

            // Functions/methods (with indentation tracking)
            var funcPattern = new Regex(@"^(\s*)def\s+(\w+)\s*\(([^)]*)\)(?:\s*->\s*(\S+))?\s*:", RegexOptions.Multiline);
            foreach (Match m in funcPattern.Matches(content))
            {
                var indent = m.Groups[1].Value;
                var name = m.Groups[2].Value;
                var parameters = SimplifyPythonParameters(m.Groups[3].Value.Trim());
                var returnType = m.Groups[4].Success ? $" -> {m.Groups[4].Value}" : "";

                var prefix = indent.Length > 0 ? "  " : "";
                results.Add($"{prefix}def {name}({parameters}){returnType}");
            }

            return results;
        }

        #endregion

        #region JavaScript/TypeScript Parser

        private List<string> ParseJavaScriptTypeScript(string content)
        {
            var results = new List<string>();

            // Classes
            var classPattern = new Regex(
                @"^\s*(?:export\s+)?(?:default\s+)?(?:abstract\s+)?class\s+(\w+)(?:\s+extends\s+(\w+))?(?:\s+implements\s+([^{]+))?\s*\{",
                RegexOptions.Multiline);
            foreach (Match m in classPattern.Matches(content))
            {
                var name = m.Groups[1].Value;
                var ext = m.Groups[2].Success ? $" extends {m.Groups[2].Value}" : "";
                var impl = m.Groups[3].Success ? $" implements {m.Groups[3].Value.Trim()}" : "";
                results.Add($"class {name}{ext}{impl}");
            }

            // Interfaces (TypeScript)
            var ifacePattern = new Regex(
                @"^\s*(?:export\s+)?interface\s+(\w+)(?:\s+extends\s+([^{]+))?\s*\{",
                RegexOptions.Multiline);
            foreach (Match m in ifacePattern.Matches(content))
            {
                var name = m.Groups[1].Value;
                var ext = m.Groups[2].Success ? $" extends {m.Groups[2].Value.Trim()}" : "";
                results.Add($"interface {name}{ext}");
            }

            // Type aliases (TypeScript)
            var typePattern = new Regex(@"^\s*(?:export\s+)?type\s+(\w+)(?:<[^>]+>)?\s*=", RegexOptions.Multiline);
            foreach (Match m in typePattern.Matches(content))
                results.Add($"type {m.Groups[1].Value}");

            // Functions (standalone)
            var funcPattern = new Regex(
                @"^\s*(?:export\s+)?(?:async\s+)?function\s+(\w+)\s*(?:<[^>]+>)?\s*\(([^)]*)\)",
                RegexOptions.Multiline);
            foreach (Match m in funcPattern.Matches(content))
            {
                var name = m.Groups[1].Value;
                var parameters = SimplifyParameters(m.Groups[2].Value.Trim());
                results.Add($"function {name}({parameters})");
            }

            // Arrow functions assigned to const/let/var
            var arrowPattern = new Regex(
                @"^\s*(?:export\s+)?(?:const|let|var)\s+(\w+)\s*(?::\s*\S+\s*)?=\s*(?:async\s+)?\(",
                RegexOptions.Multiline);
            foreach (Match m in arrowPattern.Matches(content))
                results.Add($"const {m.Groups[1].Value} = (...)");

            // Enums (TypeScript)
            var enumPattern = new Regex(@"^\s*(?:export\s+)?(?:const\s+)?enum\s+(\w+)\s*\{", RegexOptions.Multiline);
            foreach (Match m in enumPattern.Matches(content))
                results.Add($"enum {m.Groups[1].Value}");

            return results;
        }

        #endregion

        #region Java Parser

        private List<string> ParseJava(string content)
        {
            var results = new List<string>();

            // Package
            var pkgPattern = new Regex(@"^\s*package\s+([\w.]+)\s*;", RegexOptions.Multiline);
            var pkgMatch = pkgPattern.Match(content);
            if (pkgMatch.Success)
                results.Add($"package {pkgMatch.Groups[1].Value}");

            // Classes/interfaces/enums
            var typePattern = new Regex(
                @"^\s*((?:public|private|protected|static|abstract|final)\s+)*" +
                @"(class|interface|enum)\s+" +
                @"(\w+)(?:<[^>]+>)?(?:\s+extends\s+(\w+))?(?:\s+implements\s+([^{]+))?\s*\{",
                RegexOptions.Multiline);
            foreach (Match m in typePattern.Matches(content))
            {
                var modifiers = (m.Groups[1].Value ?? "").Trim();
                var keyword = m.Groups[2].Value;
                var name = m.Groups[3].Value;
                var line = string.IsNullOrEmpty(modifiers) ? $"{keyword} {name}" : $"{modifiers} {keyword} {name}";
                if (m.Groups[4].Success) line += $" extends {m.Groups[4].Value}";
                if (m.Groups[5].Success) line += $" implements {m.Groups[5].Value.Trim()}";
                results.Add(line);
            }

            // Methods
            var methodPattern = new Regex(
                @"^\s*((?:public|private|protected|static|final|abstract|synchronized|native)\s+)+" +
                @"(?:<[^>]+>\s+)?" +
                @"([\w<>\[\]?,\s]+?)\s+" +
                @"(\w+)\s*\(([^)]*)\)",
                RegexOptions.Multiline);
            foreach (Match m in methodPattern.Matches(content))
            {
                var modifiers = m.Groups[1].Value.Trim();
                var returnType = m.Groups[2].Value.Trim();
                var name = m.Groups[3].Value;
                var parameters = SimplifyParameters(m.Groups[4].Value.Trim());
                if (!IsControlFlowKeyword(name))
                    results.Add($"  {modifiers} {returnType} {name}({parameters})");
            }

            return results;
        }

        #endregion

        #region C/C++ Parser

        private List<string> ParseCpp(string content)
        {
            var results = new List<string>();

            // Remove single-line comments to avoid false matches
            var noComments = Regex.Replace(content, @"//[^\n]*", "");
            // Remove multi-line comments
            noComments = Regex.Replace(noComments, @"/\*.*?\*/", "", RegexOptions.Singleline);

            // Namespaces
            var nsPattern = new Regex(@"^\s*namespace\s+([\w:]+)", RegexOptions.Multiline);
            foreach (Match m in nsPattern.Matches(noComments))
                results.Add($"namespace {m.Groups[1].Value}");

            // Classes and structs (with optional inheritance)
            var classPattern = new Regex(
                @"^\s*(?:template\s*<[^>]*>\s*)?" +
                @"(class|struct)\s+" +
                @"(?:\w+\s+)?" +   // optional export macro like GuiExport
                @"(\w+)" +
                @"(?:\s*:\s*(?:public|protected|private)?\s*([\w:<>, ]+?))?" +
                @"\s*\{",
                RegexOptions.Multiline);
            foreach (Match m in classPattern.Matches(noComments))
            {
                var keyword = m.Groups[1].Value;
                var name = m.Groups[2].Value;
                var baseClass = m.Groups[3].Success ? m.Groups[3].Value.Trim() : "";
                var line = $"{keyword} {name}";
                if (!string.IsNullOrEmpty(baseClass))
                    line += $" : {baseClass}";
                results.Add(line);
            }

            // Enums (C/C++ style)
            var enumPattern = new Regex(
                @"^\s*(?:typedef\s+)?enum\s+(?:class\s+)?(\w+)?\s*\{",
                RegexOptions.Multiline);
            foreach (Match m in enumPattern.Matches(noComments))
            {
                var name = m.Groups[1].Success ? m.Groups[1].Value : "(anonymous)";
                results.Add($"enum {name}");
            }

            // Typedefs
            var typedefPattern = new Regex(
                @"^\s*typedef\s+.+?\s+(\w+)\s*;",
                RegexOptions.Multiline);
            foreach (Match m in typedefPattern.Matches(noComments))
                results.Add($"typedef {m.Groups[1].Value}");

            // Functions/methods: return_type name(params)
            // Match standalone function declarations/definitions
            var funcPattern = new Regex(
                @"^[ \t]*" +
                @"(?:(?:static|virtual|inline|explicit|extern|friend|const|constexpr|override|noexcept)\s+)*" +
                @"([\w:*&<>]+(?:\s*[*&])?\s+[*&]?)" +  // return type
                @"(\w+)" +                               // function name
                @"\s*\(([^)]*)\)" +                      // parameters
                @"(?:\s*(?:const|override|noexcept|final))*" +
                @"\s*[{;]",
                RegexOptions.Multiline);
            foreach (Match m in funcPattern.Matches(noComments))
            {
                var returnType = m.Groups[1].Value.Trim();
                var name = m.Groups[2].Value;
                var parameters = SimplifyParameters(m.Groups[3].Value.Trim());

                // Skip control flow, macros, and common non-function patterns
                if (IsControlFlowKeyword(name) || IsCppNonFunction(name, returnType))
                    continue;

                results.Add($"  {returnType} {name}({parameters})");
            }

            // #define macros (function-like and constant)
            var definePattern = new Regex(
                @"^\s*#\s*define\s+(\w+)(?:\([^)]*\))?",
                RegexOptions.Multiline);
            var macroCount = 0;
            foreach (Match m in definePattern.Matches(content)) // Use original content for macros
            {
                if (macroCount >= 20) { results.Add("  ... (more macros)"); break; }
                var name = m.Groups[1].Value;
                // Skip include guards and internal macros
                if (name.EndsWith("_H") || name.EndsWith("_H_") || name.StartsWith("_"))
                    continue;
                results.Add($"#define {name}");
                macroCount++;
            }

            return results;
        }

        private static bool IsCppNonFunction(string name, string returnType)
        {
            var skipNames = new HashSet<string> { "if", "else", "for", "while", "do", "switch",
                "return", "throw", "catch", "sizeof", "typeof", "decltype", "static_assert",
                "Q_OBJECT", "Q_PROPERTY", "Q_DECLARE_METATYPE", "SLOT", "SIGNAL",
                "Q_SIGNALS", "Q_SLOTS", "emit", "Q_EMIT" };
            var skipTypes = new HashSet<string> { "class", "struct", "enum", "namespace",
                "typedef", "using", "template", "#define", "#include", "#ifdef", "#ifndef" };
            return skipNames.Contains(name) || skipTypes.Contains(returnType);
        }

        #endregion

        #region Helpers

        private static string SimplifyParameters(string parameters)
        {
            if (string.IsNullOrWhiteSpace(parameters)) return "";
            // Keep parameter types and names, but limit length
            var simplified = Regex.Replace(parameters, @"\s+", " ").Trim();
            if (simplified.Length > 80)
                simplified = simplified.Substring(0, 77) + "...";
            return simplified;
        }

        private static string SimplifyPythonParameters(string parameters)
        {
            if (string.IsNullOrWhiteSpace(parameters)) return "";
            // Remove type annotations and default values for brevity
            var parts = parameters.Split(',');
            var simplified = string.Join(", ", parts.Select(p =>
            {
                var clean = p.Trim();
                // Remove default values
                var eqIdx = clean.IndexOf('=');
                if (eqIdx > 0) clean = clean.Substring(0, eqIdx).Trim();
                // Remove type annotation
                var colonIdx = clean.IndexOf(':');
                if (colonIdx > 0) clean = clean.Substring(0, colonIdx).Trim();
                return clean;
            }));
            if (simplified.Length > 80)
                simplified = simplified.Substring(0, 77) + "...";
            return simplified;
        }

        private static bool IsControlFlowKeyword(string name)
        {
            var keywords = new HashSet<string> { "if", "else", "for", "foreach", "while", "do", "switch", "case",
                "try", "catch", "finally", "using", "lock", "return", "throw", "new", "typeof", "sizeof", "nameof" };
            return keywords.Contains(name);
        }

        private static bool IsTypeKeyword(string type)
        {
            var keywords = new HashSet<string> { "class", "struct", "interface", "enum", "record", "namespace",
                "delegate", "event", "if", "else", "for", "while", "switch", "using", "try", "catch" };
            return keywords.Contains(type);
        }

        private static string GetRelativePath(string fullPath, string basePath)
        {
            if (!basePath.EndsWith(Path.DirectorySeparatorChar.ToString()))
                basePath += Path.DirectorySeparatorChar;

            if (fullPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
                return fullPath.Substring(basePath.Length).Replace('\\', '/');

            return fullPath;
        }

        #endregion

        public Task HandlePartialAsync(ToolCall call, IUIContext ui, CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }
    }
}
