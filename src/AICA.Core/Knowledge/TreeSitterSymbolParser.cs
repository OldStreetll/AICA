using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using TreeSitter;

namespace AICA.Core.Knowledge
{
    /// <summary>
    /// v2.8: Extracts symbol definitions using tree-sitter for accurate AST-based parsing.
    /// Supports C and C++ files. Falls back gracefully if tree-sitter is unavailable.
    /// Runs on background thread (no UI thread requirement).
    /// </summary>
    public class TreeSitterSymbolParser : ISymbolParser
    {
        private static readonly HashSet<string> CppExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".cpp", ".cxx", ".cc", ".cppm", ".hpp", ".hxx", ".h"
        };

        private static readonly HashSet<string> CExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".c"
        };

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        public static readonly TreeSitterSymbolParser Instance = new TreeSitterSymbolParser();

        // Circuit breaker: disable after first native DLL load failure to avoid 4000+ repeated exceptions
        private volatile bool _disabled;
        private static bool _nativeDllsPreloaded;

        /// <summary>
        /// Whether tree-sitter has been disabled due to native DLL load failure.
        /// </summary>
        public bool IsDisabled => _disabled;

        /// <summary>
        /// Preload tree-sitter native DLLs from the extension's install directory.
        /// VS2022 P/Invoke doesn't search the extension dir by default.
        /// </summary>
        private void PreloadNativeDlls()
        {
            try
            {
                // Find the directory where TreeSitter.dll (managed) is located
                var assemblyDir = Path.GetDirectoryName(typeof(Language).Assembly.Location);
                if (string.IsNullOrEmpty(assemblyDir))
                {
                    // Fallback: use AICA.Core.dll location
                    assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                }

                if (string.IsNullOrEmpty(assemblyDir) || !Directory.Exists(assemblyDir))
                {
                    System.Diagnostics.Debug.WriteLine("[AICA] TreeSitter: cannot determine assembly directory for native DLL preload");
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"[AICA] TreeSitter: preloading native DLLs from {assemblyDir}");

                var dllNames = new[] { "tree-sitter.dll", "tree-sitter-c.dll", "tree-sitter-cpp.dll" };
                foreach (var dllName in dllNames)
                {
                    var dllPath = Path.Combine(assemblyDir, dllName);
                    if (File.Exists(dllPath))
                    {
                        var handle = LoadLibrary(dllPath);
                        System.Diagnostics.Debug.WriteLine(
                            $"[AICA] TreeSitter: preloaded {dllName} = {(handle != IntPtr.Zero ? "OK" : "FAILED")}");

                        if (handle == IntPtr.Zero)
                        {
                            _disabled = true;
                            System.Diagnostics.Debug.WriteLine(
                                $"[AICA] TreeSitter DISABLED: failed to preload {dllName} (Win32 error {Marshal.GetLastWin32Error()})");
                            return;
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[AICA] TreeSitter: {dllName} not found at {dllPath}");
                        _disabled = true;
                        return;
                    }
                }

                System.Diagnostics.Debug.WriteLine("[AICA] TreeSitter: all native DLLs preloaded successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AICA] TreeSitter preload error: {ex.Message}");
                _disabled = true;
            }
        }

        public IReadOnlyList<SymbolRecord> Parse(string filePath, string content)
        {
            if (_disabled)
                return Array.Empty<SymbolRecord>();

            if (string.IsNullOrEmpty(filePath) || string.IsNullOrEmpty(content))
                return Array.Empty<SymbolRecord>();

            // Preload native DLLs from the same directory as this assembly (VS extension dir)
            if (!_nativeDllsPreloaded)
            {
                _nativeDllsPreloaded = true;
                PreloadNativeDlls();
            }

            var ext = Path.GetExtension(filePath).ToLowerInvariant();

            string languageName;
            if (CppExtensions.Contains(ext))
                languageName = "Cpp";
            else if (CExtensions.Contains(ext))
                languageName = "C";
            else
                return Array.Empty<SymbolRecord>(); // Unsupported, let regex handle it

            try
            {
                using (var language = new Language(languageName))
                using (var parser = new Parser(language))
                using (var tree = parser.Parse(content))
                {
                    var symbols = new List<SymbolRecord>();
                    TraverseNode(tree.RootNode, filePath, "", symbols);
                    return symbols;
                }
            }
            catch (DllNotFoundException ex)
            {
                // Native DLL not found — disable tree-sitter permanently for this session
                _disabled = true;
                System.Diagnostics.Debug.WriteLine(
                    $"[AICA] TreeSitter DISABLED: native DLL not found ({ex.Message}). Falling back to regex for all files.");
                return Array.Empty<SymbolRecord>();
            }
            catch (Exception ex)
            {
                // Other errors (e.g., parse failure on one file) — disable if it looks like a load error
                if (ex.Message.Contains("Unable to load") || ex.Message.Contains("DllNotFoundException"))
                {
                    _disabled = true;
                    System.Diagnostics.Debug.WriteLine(
                        $"[AICA] TreeSitter DISABLED: load error ({ex.Message}). Falling back to regex for all files.");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[AICA] TreeSitter parse failed for {filePath}: {ex.Message}");
                }
                return Array.Empty<SymbolRecord>();
            }
        }

        private void TraverseNode(Node node, string filePath, string currentNamespace, List<SymbolRecord> symbols)
        {
            foreach (var child in node.NamedChildren)
            {
                try
                {
                    ProcessNode(child, filePath, currentNamespace, symbols);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[AICA] TreeSitter node error: {ex.Message}");
                }
            }
        }

        private void ProcessNode(Node node, string filePath, string currentNamespace, List<SymbolRecord> symbols)
        {
            var startLine = (int)node.StartPosition.Row + 1;
            var endLine = (int)node.EndPosition.Row + 1;

            switch (node.Type)
            {
                case "namespace_definition":
                    var nsName = GetChildText(node, "name") ?? GetChildText(node, "declarator");
                    if (string.IsNullOrEmpty(nsName)) nsName = "anonymous";
                    var fullNs = string.IsNullOrEmpty(currentNamespace) ? nsName : $"{currentNamespace}::{nsName}";

                    symbols.Add(CreateRecord(filePath, nsName, SymbolKind.Namespace, currentNamespace,
                        startLine, endLine));

                    // Recurse into namespace body
                    var nsBody = GetChildByType(node, "declaration_list");
                    if (nsBody != null)
                        TraverseNode(nsBody, filePath, fullNs, symbols);
                    break;

                case "class_specifier":
                case "struct_specifier":
                    var className = GetChildText(node, "name");
                    if (string.IsNullOrEmpty(className)) break;

                    var kind = node.Type == "struct_specifier" ? SymbolKind.Struct : SymbolKind.Class;
                    var baseClause = GetChildByType(node, "base_class_clause");
                    var baseTypes = baseClause != null ? ExtractBaseTypes(baseClause) : "";
                    var classBody = GetChildByType(node, "field_declaration_list");
                    var memberCount = classBody != null ? CountNamedChildren(classBody) : 0;

                    var summary = $"{kind.ToString().ToLowerInvariant()} {className}" +
                        (string.IsNullOrEmpty(baseTypes) ? "" : $" : {baseTypes}") +
                        $" ({memberCount} members)";

                    symbols.Add(CreateRecord(filePath, className, kind, currentNamespace,
                        startLine, endLine, summary: summary));

                    // Recurse into class body for member extraction
                    if (classBody != null)
                    {
                        var classNs = string.IsNullOrEmpty(currentNamespace)
                            ? className : $"{currentNamespace}::{className}";
                        TraverseNode(classBody, filePath, classNs, symbols);
                    }
                    break;

                case "enum_specifier":
                    var enumName = GetChildText(node, "name");
                    if (!string.IsNullOrEmpty(enumName))
                    {
                        symbols.Add(CreateRecord(filePath, enumName, SymbolKind.Enum, currentNamespace,
                            startLine, endLine));
                    }
                    break;

                case "function_definition":
                    var funcName = ExtractFunctionName(node);
                    if (!string.IsNullOrEmpty(funcName) && !IsControlKeyword(funcName))
                    {
                        var sig = ExtractFunctionSignature(node);
                        var access = ExtractAccessSpecifier(node);
                        symbols.Add(CreateRecord(filePath, funcName, SymbolKind.Function, currentNamespace,
                            startLine, endLine, signature: sig, access: access));
                    }
                    break;

                case "declaration":
                    // Could be a function declaration or variable declaration
                    var declName = ExtractDeclarationName(node);
                    if (!string.IsNullOrEmpty(declName))
                    {
                        if (HasChildOfType(node, "function_declarator"))
                        {
                            var declSig = ExtractFunctionSignature(node);
                            symbols.Add(CreateRecord(filePath, declName, SymbolKind.Function, currentNamespace,
                                startLine, endLine, signature: declSig));
                        }
                    }
                    break;

                case "field_declaration":
                    var fieldName = ExtractFieldName(node);
                    if (!string.IsNullOrEmpty(fieldName))
                    {
                        var fieldType = GetChildText(node, "type") ?? ExtractTypeText(node);
                        var fieldAccess = ExtractAccessSpecifier(node);
                        symbols.Add(CreateRecord(filePath, fieldName, SymbolKind.Variable, currentNamespace,
                            startLine, endLine,
                            summary: $"{fieldType ?? "?"} {fieldName}",
                            access: fieldAccess));
                    }
                    break;

                case "type_definition":
                    var tdName = ExtractTypedefName(node);
                    if (!string.IsNullOrEmpty(tdName))
                    {
                        symbols.Add(CreateRecord(filePath, tdName, SymbolKind.Typedef, currentNamespace,
                            startLine, endLine));
                    }
                    break;

                case "preproc_def":
                    var macroName = GetChildText(node, "name");
                    if (!string.IsNullOrEmpty(macroName) &&
                        !macroName.EndsWith("_H") && !macroName.EndsWith("_INCLUDED") && !macroName.StartsWith("_"))
                    {
                        symbols.Add(CreateRecord(filePath, macroName, SymbolKind.Define, "",
                            startLine, endLine));
                    }
                    break;

                default:
                    // Recurse into other compound nodes
                    if (node.NamedChildren.Count > 0)
                        TraverseNode(node, filePath, currentNamespace, symbols);
                    break;
            }
        }

        #region Node Helpers

        private static string GetChildText(Node node, string fieldName)
        {
            try
            {
                // Try field-based lookup first
                var fields = node.Fields;
                if (fields != null)
                {
                    foreach (var field in fields)
                    {
                        if (field.Key == fieldName)
                        {
                            // field.Value could be a Node directly
                            try { return field.Value.Text; } catch { }
                        }
                    }
                }

                // Fallback: search named children by type hint
                foreach (var child in node.NamedChildren)
                {
                    if (child.Type == fieldName || child.Type == "identifier" ||
                        child.Type == "type_identifier" || child.Type == "field_identifier")
                    {
                        if (child.Type == fieldName) return child.Text;
                    }
                }
            }
            catch { }
            return null;
        }

        private static Node GetChildByType(Node node, string typeName)
        {
            foreach (var child in node.NamedChildren)
            {
                if (child.Type == typeName) return child;
            }
            return null;
        }

        private static bool HasChildOfType(Node node, string typeName)
        {
            foreach (var child in node.NamedChildren)
            {
                if (child.Type == typeName) return true;
                // Check nested (e.g., declaration → declarator → function_declarator)
                foreach (var grandchild in child.NamedChildren)
                {
                    if (grandchild.Type == typeName) return true;
                }
            }
            return false;
        }

        private static int CountNamedChildren(Node node)
        {
            return node.NamedChildren.Count;
        }

        private static string ExtractFunctionName(Node node)
        {
            // function_definition → declarator → (function_declarator → declarator → identifier)
            var declarator = GetChildByType(node, "declarator")
                ?? GetChildByType(node, "function_declarator");
            if (declarator == null) return null;

            return ExtractNameFromDeclarator(declarator);
        }

        private static string ExtractNameFromDeclarator(Node declarator)
        {
            if (declarator.Type == "identifier")
                return declarator.Text;
            if (declarator.Type == "field_identifier")
                return declarator.Text;
            if (declarator.Type == "qualified_identifier" || declarator.Type == "scoped_identifier")
            {
                var name = GetChildByType(declarator, "name");
                return name?.Text ?? declarator.Text;
            }

            // Recurse: function_declarator → declarator → identifier
            foreach (var child in declarator.NamedChildren)
            {
                var result = ExtractNameFromDeclarator(child);
                if (result != null) return result;
            }
            return null;
        }

        private static string ExtractDeclarationName(Node node)
        {
            var declarator = GetChildByType(node, "declarator");
            if (declarator == null) return null;
            return ExtractNameFromDeclarator(declarator);
        }

        private static string ExtractFieldName(Node node)
        {
            var declarator = GetChildByType(node, "declarator");
            if (declarator != null)
                return ExtractNameFromDeclarator(declarator);

            // Direct field_identifier child
            var fieldId = GetChildByType(node, "field_identifier");
            return fieldId?.Text;
        }

        private static string ExtractTypedefName(Node node)
        {
            var declarator = GetChildByType(node, "type_declarator")
                ?? GetChildByType(node, "declarator");
            if (declarator != null)
                return ExtractNameFromDeclarator(declarator);
            return null;
        }

        private static string ExtractTypeText(Node node)
        {
            var typeNode = GetChildByType(node, "type_identifier")
                ?? GetChildByType(node, "primitive_type");
            return typeNode?.Text;
        }

        private static string ExtractFunctionSignature(Node node)
        {
            try
            {
                // Try to get a clean one-line signature from the node text
                var text = node.Text;
                if (string.IsNullOrEmpty(text)) return null;

                // Take text up to the opening brace or semicolon
                var braceIdx = text.IndexOf('{');
                var semiIdx = text.IndexOf(';');
                var endIdx = braceIdx >= 0 ? braceIdx : (semiIdx >= 0 ? semiIdx : text.Length);
                var sig = text.Substring(0, Math.Min(endIdx, 200)).Trim();

                // Collapse whitespace
                sig = System.Text.RegularExpressions.Regex.Replace(sig, @"\s+", " ");
                return sig;
            }
            catch
            {
                return null;
            }
        }

        private static string ExtractBaseTypes(Node baseClause)
        {
            var bases = new List<string>();
            foreach (var child in baseClause.NamedChildren)
            {
                if (child.Type == "base_class_specifier" || child.Type == "type_identifier")
                    bases.Add(child.Text);
            }
            return string.Join(", ", bases);
        }

        private static string ExtractAccessSpecifier(Node node)
        {
            var access = GetChildByType(node, "access_specifier");
            if (access != null) return access.Text;

            // Check parent for access context
            var parent = node.Parent;
            if (parent != null && parent.Type == "access_specifier")
                return parent.Text;

            return "";
        }

        private static bool IsControlKeyword(string name)
        {
            return name == "if" || name == "while" || name == "for" ||
                   name == "switch" || name == "return" || name == "catch";
        }

        #endregion

        private SymbolRecord CreateRecord(
            string filePath, string name, SymbolKind kind, string ns,
            int startLine, int endLine,
            string summary = null, string signature = null, string access = null)
        {
            if (string.IsNullOrEmpty(summary))
                summary = $"{kind.ToString().ToLowerInvariant()} {name}";

            var keywords = RegexSymbolParser.GenerateKeywords(name, ns, null, kind.ToString().ToLowerInvariant());

            return new SymbolRecord(
                id: $"{filePath}:{name}:{startLine}",
                name: name,
                kind: kind,
                filePath: filePath,
                ns: ns,
                summary: summary,
                keywords: keywords,
                startLine: startLine,
                endLine: endLine,
                signature: signature,
                accessModifier: access);
        }
    }
}
