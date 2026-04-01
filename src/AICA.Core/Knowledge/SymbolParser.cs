using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace AICA.Core.Knowledge
{
    /// <summary>
    /// Extracts symbol definitions from source file content using regex-based parsing.
    /// Supports C/C++ (.h, .hpp, .cpp) and C# (.cs) files.
    /// Fallback parser when VS2022 CodeModel is unavailable.
    /// </summary>
    public class RegexSymbolParser : ISymbolParser
    {
        /// <summary>Singleton instance for backward compatibility with static callers.</summary>
        public static readonly RegexSymbolParser Instance = new RegexSymbolParser();
        // C/C++ patterns
        private static readonly Regex CppClassStructRegex = new Regex(
            @"^\s*(?:template\s*<[^>]*>\s*)?(?:class|struct)\s+(?:\w+\s+)*(\w+)\s*(?::\s*(?:public|protected|private)\s+(\w[\w:]*))?\s*\{?",
            RegexOptions.Compiled);

        private static readonly Regex CppEnumRegex = new Regex(
            @"^\s*enum\s+(?:class\s+)?(\w+)",
            RegexOptions.Compiled);

        // Supports C++17 nested namespaces: namespace a::b::c
        private static readonly Regex CppNamespaceRegex = new Regex(
            @"^\s*namespace\s+([\w:]+(?:::[\w]+)*)",
            RegexOptions.Compiled);

        private static readonly Regex CppTypedefRegex = new Regex(
            @"^\s*typedef\s+.+\s+(\w+)\s*;",
            RegexOptions.Compiled);

        private static readonly Regex CppDefineRegex = new Regex(
            @"^\s*#define\s+(\w+)(?:\(|\s)",
            RegexOptions.Compiled);

        private static readonly Regex CppFunctionRegex = new Regex(
            @"^\s*(?:static\s+|virtual\s+|inline\s+|extern\s+)*(?:const\s+)?[\w:*&<>,\s]+\s+(\w+)\s*\([^;]*\)\s*(?:const)?\s*(?:override)?\s*(?:=\s*0)?\s*[;{]",
            RegexOptions.Compiled);

        // C# patterns
        private static readonly Regex CSharpTypeRegex = new Regex(
            @"^\s*(?:public|internal|protected|private)?\s*(?:static\s+|abstract\s+|sealed\s+|partial\s+)*(?:class|struct|interface|enum)\s+(\w+)(?:<[^>]+>)?\s*(?::\s*([\w\s,.<>]+))?\s*(?:where\s|{|$)",
            RegexOptions.Compiled);

        private static readonly Regex CSharpNamespaceRegex = new Regex(
            @"^\s*namespace\s+([\w.]+)",
            RegexOptions.Compiled);

        private static readonly Regex CSharpTypeKindRegex = new Regex(
            @"\b(class|struct|interface|enum)\b",
            RegexOptions.Compiled);

        /// <summary>
        /// Parse a file's content and extract symbols based on file extension.
        /// Instance method implementing ISymbolParser.
        /// </summary>
        IReadOnlyList<SymbolRecord> ISymbolParser.Parse(string filePath, string content)
            => Parse(filePath, content);

        /// <summary>
        /// Parse a file's content and extract symbols based on file extension.
        /// Static method for backward compatibility.
        /// </summary>
        public static IReadOnlyList<SymbolRecord> Parse(string filePath, string content)
        {
            if (string.IsNullOrEmpty(filePath) || string.IsNullOrEmpty(content))
                return Array.Empty<SymbolRecord>();

            var ext = System.IO.Path.GetExtension(filePath).ToLowerInvariant();
            switch (ext)
            {
                case ".h":
                case ".hpp":
                case ".hxx":
                case ".cpp":
                case ".cxx":
                case ".c":
                case ".cppm":
                    return ParseCpp(filePath, content);
                case ".cs":
                    return ParseCSharp(filePath, content);
                default:
                    return Array.Empty<SymbolRecord>();
            }
        }

        /// <summary>
        /// Extract symbol definitions from C/C++ source content.
        /// </summary>
        public static IReadOnlyList<SymbolRecord> ParseCpp(string filePath, string content)
        {
            var symbols = new List<SymbolRecord>();
            var lines = content.Split(new[] { '\n', '\r' }, StringSplitOptions.None);

            var namespaceStack = new Stack<string>();
            var braceDepth = 0;
            var namespaceBraceDepths = new Stack<int>();
            var inClassBody = false;
            var currentClassName = "";
            var classBraceDepth = 0;
            var methodCount = 0;
            var staticMethods = new List<string>();
            var baseClass = "";

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var trimmed = line.TrimStart();

                // Skip comments and preprocessor (except #define)
                if (trimmed.StartsWith("//") || trimmed.StartsWith("/*") || trimmed.StartsWith("*"))
                    continue;

                // Count braces for scope tracking
                foreach (var ch in line)
                {
                    if (ch == '{') braceDepth++;
                    else if (ch == '}')
                    {
                        braceDepth--;

                        // Check if we're exiting a namespace
                        if (namespaceBraceDepths.Count > 0 && braceDepth == namespaceBraceDepths.Peek())
                        {
                            namespaceBraceDepths.Pop();
                            if (namespaceStack.Count > 0) namespaceStack.Pop();
                        }

                        // Check if we're exiting a class body
                        if (inClassBody && braceDepth == classBraceDepth)
                        {
                            // Finalize class symbol with method count
                            inClassBody = false;
                        }
                    }
                }

                // Namespace
                var nsMatch = CppNamespaceRegex.Match(line);
                if (nsMatch.Success)
                {
                    namespaceStack.Push(nsMatch.Groups[1].Value);
                    namespaceBraceDepths.Push(braceDepth - 1);
                    continue;
                }

                // Class / Struct
                var csMatch = CppClassStructRegex.Match(line);
                if (csMatch.Success)
                {
                    var name = csMatch.Groups[1].Value;
                    baseClass = csMatch.Groups[2].Success ? csMatch.Groups[2].Value : "";

                    // Skip forward declarations (no '{' on same or next line)
                    if (!line.Contains("{") && i + 1 < lines.Length && !lines[i + 1].TrimStart().StartsWith("{"))
                    {
                        if (line.TrimEnd().EndsWith(";"))
                            continue; // forward declaration
                    }

                    var kind = trimmed.Contains("struct") ? SymbolKind.Struct : SymbolKind.Class;
                    var ns = namespaceStack.Count > 0 ? string.Join("::", namespaceStack.ToArray()) : "";

                    inClassBody = true;
                    classBraceDepth = braceDepth - 1;
                    currentClassName = name;
                    methodCount = 0;
                    staticMethods = new List<string>();

                    // Count methods in class body (look ahead)
                    var bodyBraces = 0;
                    var started = line.Contains("{");
                    for (var j = started ? i : i + 1; j < lines.Length; j++)
                    {
                        foreach (var ch in lines[j])
                        {
                            if (ch == '{') bodyBraces++;
                            else if (ch == '}') bodyBraces--;
                        }
                        if (started && bodyBraces <= 0) break;
                        if (!started && lines[j].TrimStart().StartsWith("{")) started = true;

                        var methodLine = lines[j].TrimStart();
                        if (IsMethodDeclaration(methodLine))
                        {
                            methodCount++;
                            if (methodLine.StartsWith("static "))
                            {
                                var funcMatch = CppFunctionRegex.Match(lines[j]);
                                if (funcMatch.Success)
                                    staticMethods.Add(funcMatch.Groups[1].Value);
                            }
                        }
                    }

                    var summary = FormatClassSummary(kind, name, baseClass, methodCount, staticMethods);
                    var keywords = GenerateKeywords(name, ns, baseClass, kind.ToString());

                    symbols.Add(new SymbolRecord(
                        id: $"{filePath}:{name}",
                        name: name,
                        kind: kind,
                        filePath: filePath,
                        ns: ns,
                        summary: summary,
                        keywords: keywords,
                        startLine: i + 1));
                    continue;
                }

                // Enum
                var enumMatch = CppEnumRegex.Match(line);
                if (enumMatch.Success)
                {
                    var name = enumMatch.Groups[1].Value;
                    var ns = namespaceStack.Count > 0 ? string.Join("::", namespaceStack.ToArray()) : "";
                    var keywords = GenerateKeywords(name, ns, "", "Enum");

                    symbols.Add(new SymbolRecord(
                        id: $"{filePath}:{name}",
                        name: name,
                        kind: SymbolKind.Enum,
                        filePath: filePath,
                        ns: ns,
                        summary: $"enum {name}",
                        keywords: keywords,
                        startLine: i + 1));
                    continue;
                }

                // Typedef
                var typedefMatch = CppTypedefRegex.Match(line);
                if (typedefMatch.Success)
                {
                    var name = typedefMatch.Groups[1].Value;
                    var ns = namespaceStack.Count > 0 ? string.Join("::", namespaceStack.ToArray()) : "";
                    var keywords = GenerateKeywords(name, ns, "", "Typedef");

                    symbols.Add(new SymbolRecord(
                        id: $"{filePath}:{name}",
                        name: name,
                        kind: SymbolKind.Typedef,
                        filePath: filePath,
                        ns: ns,
                        summary: line.Trim().TrimEnd(';'),
                        keywords: keywords,
                        startLine: i + 1));
                    continue;
                }

                // #define (only meaningful macros, skip include guards)
                var defineMatch = CppDefineRegex.Match(line);
                if (defineMatch.Success)
                {
                    var name = defineMatch.Groups[1].Value;
                    // Skip include guards and internal macros
                    if (name.EndsWith("_H") || name.EndsWith("_INCLUDED") || name.StartsWith("_"))
                        continue;

                    var keywords = GenerateKeywords(name, "", "", "Define");

                    symbols.Add(new SymbolRecord(
                        id: $"{filePath}:{name}",
                        name: name,
                        kind: SymbolKind.Define,
                        filePath: filePath,
                        ns: "",
                        summary: line.Trim(),
                        keywords: keywords,
                        startLine: i + 1));
                    continue;
                }

                // Top-level functions (not inside class body, depth 0 or 1)
                if (!inClassBody && braceDepth <= 1)
                {
                    var funcMatch = CppFunctionRegex.Match(line);
                    if (funcMatch.Success)
                    {
                        var name = funcMatch.Groups[1].Value;
                        // Skip common non-function patterns
                        if (name == "if" || name == "while" || name == "for" || name == "switch" || name == "return")
                            continue;

                        var ns = namespaceStack.Count > 0 ? string.Join("::", namespaceStack.ToArray()) : "";
                        var keywords = GenerateKeywords(name, ns, "", "Function");

                        symbols.Add(new SymbolRecord(
                            id: $"{filePath}:{name}",
                            name: name,
                            kind: SymbolKind.Function,
                            filePath: filePath,
                            ns: ns,
                            summary: line.Trim().TrimEnd('{').Trim(),
                            keywords: keywords,
                            startLine: i + 1));
                    }
                }
            }

            return symbols;
        }

        /// <summary>
        /// Extract symbol definitions from C# source content.
        /// </summary>
        public static IReadOnlyList<SymbolRecord> ParseCSharp(string filePath, string content)
        {
            var symbols = new List<SymbolRecord>();
            var lines = content.Split(new[] { '\n', '\r' }, StringSplitOptions.None);
            var currentNamespace = "";

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                // Namespace
                var nsMatch = CSharpNamespaceRegex.Match(line);
                if (nsMatch.Success)
                {
                    currentNamespace = nsMatch.Groups[1].Value;
                    continue;
                }

                // Type declarations
                var typeMatch = CSharpTypeRegex.Match(line);
                if (typeMatch.Success)
                {
                    var name = typeMatch.Groups[1].Value;
                    var baseTypes = typeMatch.Groups[2].Success ? typeMatch.Groups[2].Value.Trim() : "";

                    // Determine kind
                    var kindMatch = CSharpTypeKindRegex.Match(line);
                    var kindStr = kindMatch.Success ? kindMatch.Groups[1].Value : "class";
                    SymbolKind kind;
                    switch (kindStr)
                    {
                        case "struct": kind = SymbolKind.Struct; break;
                        case "enum": kind = SymbolKind.Enum; break;
                        case "interface": kind = SymbolKind.Class; break; // treat interface as class
                        default: kind = SymbolKind.Class; break;
                    }

                    var summary = $"{kindStr} {name}";
                    if (!string.IsNullOrEmpty(baseTypes))
                        summary += $" : {baseTypes}";

                    var keywords = GenerateKeywords(name, currentNamespace, baseTypes, kindStr);

                    symbols.Add(new SymbolRecord(
                        id: $"{filePath}:{name}",
                        name: name,
                        kind: kind,
                        filePath: filePath,
                        ns: currentNamespace,
                        summary: summary,
                        keywords: keywords,
                        startLine: i + 1));
                }
            }

            return symbols;
        }

        private static bool IsMethodDeclaration(string trimmedLine)
        {
            if (string.IsNullOrEmpty(trimmedLine)) return false;
            if (trimmedLine.StartsWith("//") || trimmedLine.StartsWith("/*") || trimmedLine.StartsWith("*"))
                return false;
            if (trimmedLine.StartsWith("#") || trimmedLine.StartsWith("using ") || trimmedLine.StartsWith("friend "))
                return false;

            // Must contain '(' for function signature
            if (!trimmedLine.Contains("(")) return false;

            // Skip control flow
            if (trimmedLine.StartsWith("if ") || trimmedLine.StartsWith("if(") ||
                trimmedLine.StartsWith("while ") || trimmedLine.StartsWith("while(") ||
                trimmedLine.StartsWith("for ") || trimmedLine.StartsWith("for(") ||
                trimmedLine.StartsWith("switch ") || trimmedLine.StartsWith("switch(") ||
                trimmedLine.StartsWith("return ") || trimmedLine.StartsWith("return("))
                return false;

            // Looks like a method: has return type and name before '('
            return trimmedLine.Contains(")") &&
                   (trimmedLine.EndsWith(";") || trimmedLine.EndsWith("{") ||
                    trimmedLine.EndsWith("= 0;") || trimmedLine.EndsWith("override;") ||
                    trimmedLine.Contains(") const") || trimmedLine.Contains(") override"));
        }

        private static string FormatClassSummary(
            SymbolKind kind, string name, string baseClass,
            int methodCount, List<string> staticMethods)
        {
            var summary = $"{kind.ToString().ToLowerInvariant()} {name}";
            if (!string.IsNullOrEmpty(baseClass))
                summary += $" : {baseClass}";
            summary += $" ({methodCount} methods)";
            if (staticMethods.Count > 0)
                summary += $" | Static: {string.Join(", ", staticMethods)}";
            return summary;
        }

        /// <summary>
        /// Generate TF-IDF keywords from symbol metadata.
        /// Splits camelCase and PascalCase names, includes namespace segments.
        /// </summary>
        public static IReadOnlyList<string> GenerateKeywords(
            string name, string ns, string baseType, string kindLabel)
        {
            var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Add the full name
            keywords.Add(name.ToLowerInvariant());

            // Split camelCase / PascalCase
            foreach (var part in SplitIdentifier(name))
                keywords.Add(part.ToLowerInvariant());

            // Namespace segments
            if (!string.IsNullOrEmpty(ns))
            {
                foreach (var seg in ns.Split(new[] { '.', ':' }, StringSplitOptions.RemoveEmptyEntries))
                    keywords.Add(seg.ToLowerInvariant());
            }

            // Base type
            if (!string.IsNullOrEmpty(baseType))
            {
                foreach (var part in baseType.Split(new[] { ',', ' ', '<', '>' }, StringSplitOptions.RemoveEmptyEntries))
                    keywords.Add(part.Trim().ToLowerInvariant());

                foreach (var part in SplitIdentifier(baseType))
                    keywords.Add(part.ToLowerInvariant());
            }

            // Kind label
            keywords.Add(kindLabel.ToLowerInvariant());

            return new List<string>(keywords);
        }

        /// <summary>
        /// Split a PascalCase or camelCase identifier into words.
        /// E.g., "MyLoggerFactory" → ["My", "Logger", "Factory"]
        /// </summary>
        public static IReadOnlyList<string> SplitIdentifier(string identifier)
        {
            if (string.IsNullOrEmpty(identifier)) return Array.Empty<string>();

            var parts = new List<string>();
            var current = new System.Text.StringBuilder();

            for (var i = 0; i < identifier.Length; i++)
            {
                var ch = identifier[i];

                if (ch == '_' || ch == ':' || ch == '.' || ch == ',' || ch == ' ')
                {
                    if (current.Length > 0)
                    {
                        parts.Add(current.ToString());
                        current.Clear();
                    }
                    continue;
                }

                if (char.IsUpper(ch) && current.Length > 0)
                {
                    var prev = identifier[i - 1];
                    var prevIsLower = char.IsLower(prev);
                    var prevIsDigit = char.IsDigit(prev);
                    var nextIsLower = (i + 1 < identifier.Length) && char.IsLower(identifier[i + 1]);
                    var prevIsUpper = char.IsUpper(prev);

                    if (prevIsLower || prevIsDigit || (prevIsUpper && nextIsLower))
                    {
                        parts.Add(current.ToString());
                        current.Clear();
                    }
                }
                // Split on digit→letter transition (non-uppercase handled above)
                else if (char.IsLetter(ch) && !char.IsUpper(ch) && current.Length > 0
                    && char.IsDigit(identifier[i - 1]))
                {
                    parts.Add(current.ToString());
                    current.Clear();
                }

                current.Append(ch);
            }

            if (current.Length > 0)
                parts.Add(current.ToString());

            return parts;
        }
    }
}
