using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AICA.Core.Rules.Parsers
{
    /// <summary>
    /// Parses YAML frontmatter from Markdown content.
    /// Implements fail-open strategy: invalid YAML preserves original content.
    /// </summary>
    public class YamlFrontmatterParser
    {
        private const string FrontmatterDelimiter = "---";

        /// <summary>
        /// Parse YAML frontmatter from content.
        /// </summary>
        public FrontmatterParseResult Parse(string content)
        {
            if (string.IsNullOrEmpty(content))
            {
                return new FrontmatterParseResult
                {
                    Data = new Dictionary<string, object>(),
                    Body = content,
                    HadFrontmatter = false
                };
            }

            // Check if content starts with frontmatter delimiter
            if (!content.StartsWith(FrontmatterDelimiter))
            {
                return new FrontmatterParseResult
                {
                    Data = new Dictionary<string, object>(),
                    Body = content,
                    HadFrontmatter = false
                };
            }

            // Find the closing delimiter
            var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            int closingDelimiterIndex = -1;

            for (int i = 1; i < lines.Length; i++)
            {
                if (lines[i].Trim() == FrontmatterDelimiter)
                {
                    closingDelimiterIndex = i;
                    break;
                }
            }

            // No closing delimiter found - treat as regular content
            if (closingDelimiterIndex == -1)
            {
                return new FrontmatterParseResult
                {
                    Data = new Dictionary<string, object>(),
                    Body = content,
                    HadFrontmatter = false
                };
            }

            // Extract frontmatter and body
            var frontmatterLines = lines.Skip(1).Take(closingDelimiterIndex - 1).ToList();
            var bodyLines = lines.Skip(closingDelimiterIndex + 1).ToList();

            var frontmatterText = string.Join("\n", frontmatterLines);
            var bodyText = string.Join("\n", bodyLines).TrimStart('\n', '\r');

            // Parse YAML
            try
            {
                var data = ParseYaml(frontmatterText);
                return new FrontmatterParseResult
                {
                    Data = data,
                    Body = bodyText,
                    HadFrontmatter = true
                };
            }
            catch (Exception ex)
            {
                // Fail-open: preserve original content on parse error
                return new FrontmatterParseResult
                {
                    Data = new Dictionary<string, object>(),
                    Body = content,
                    HadFrontmatter = true,
                    ParseError = ex.Message
                };
            }
        }

        /// <summary>
        /// Simple YAML parser for basic key-value pairs and lists.
        /// Supports: strings, numbers, booleans, lists, and nested objects.
        /// </summary>
        private Dictionary<string, object> ParseYaml(string yaml)
        {
            var result = new Dictionary<string, object>();

            if (string.IsNullOrWhiteSpace(yaml))
                return result;

            var lines = yaml.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var stack = new Stack<(int indent, string key, object value)>();

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#"))
                    continue;

                var indent = GetIndentation(line);
                var trimmedLine = line.Trim();

                // Pop stack items with greater or equal indentation
                while (stack.Count > 0 && stack.Peek().indent >= indent)
                {
                    var item = stack.Pop();
                    if (stack.Count > 0)
                    {
                        var parent = stack.Peek();
                        if (parent.value is Dictionary<string, object> dict)
                        {
                            dict[item.key] = item.value;
                        }
                    }
                    else
                    {
                        result[item.key] = item.value;
                    }
                }

                // Parse key-value pair
                if (trimmedLine.Contains(":"))
                {
                    var parts = trimmedLine.Split(new[] { ':' }, 2);
                    var key = parts[0].Trim();
                    var valueStr = parts.Length > 1 ? parts[1].Trim() : "";

                    object value = ParseValue(valueStr);

                    // Check if this is a list or nested object
                    if (string.IsNullOrEmpty(valueStr))
                    {
                        // Could be a list or nested object - will be determined by next lines
                        value = new Dictionary<string, object>();
                    }

                    stack.Push((indent, key, value));
                }
                else if (trimmedLine.StartsWith("- "))
                {
                    // List item
                    var itemValue = trimmedLine.Substring(2).Trim();
                    if (stack.Count > 0)
                    {
                        var parent = stack.Peek();
                        if (parent.value is List<object> parentList)
                        {
                            parentList.Add(ParseValue(itemValue));
                        }
                        else if (parent.value is Dictionary<string, object>)
                        {
                            // Convert to list
                            var newList = new List<object> { ParseValue(itemValue) };
                            stack.Pop();
                            stack.Push((parent.indent, parent.key, newList));
                        }
                    }
                }
            }

            // Flush remaining stack items
            while (stack.Count > 0)
            {
                var item = stack.Pop();
                result[item.key] = item.value;
            }

            return result;
        }

        private int GetIndentation(string line)
        {
            int count = 0;
            foreach (var ch in line)
            {
                if (ch == ' ')
                    count++;
                else if (ch == '\t')
                    count += 2;
                else
                    break;
            }
            return count;
        }

        private object ParseValue(string value)
        {
            if (string.IsNullOrEmpty(value))
                return null;

            // Boolean
            if (value.Equals("true", StringComparison.OrdinalIgnoreCase))
                return true;
            if (value.Equals("false", StringComparison.OrdinalIgnoreCase))
                return false;

            // Number
            if (int.TryParse(value, out var intVal))
                return intVal;
            if (double.TryParse(value, out var doubleVal))
                return doubleVal;

            // String (remove quotes if present)
            if ((value.StartsWith("\"") && value.EndsWith("\"")) ||
                (value.StartsWith("'") && value.EndsWith("'")))
            {
                return value.Substring(1, value.Length - 2);
            }

            return value;
        }
    }

    /// <summary>
    /// Result of parsing YAML frontmatter.
    /// </summary>
    public class FrontmatterParseResult
    {
        /// <summary>
        /// Parsed YAML data as key-value pairs.
        /// </summary>
        public Dictionary<string, object> Data { get; set; }

        /// <summary>
        /// Content body (after frontmatter).
        /// </summary>
        public string Body { get; set; }

        /// <summary>
        /// Whether frontmatter was present in the original content.
        /// </summary>
        public bool HadFrontmatter { get; set; }

        /// <summary>
        /// Error message if parsing failed (fail-open strategy).
        /// </summary>
        public string ParseError { get; set; }
    }
}
