using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using AICA.Core.Agent;
using Xunit;

namespace AICA.Core.Tests.LLM
{
    public class ToolDefinitionSerializationTests
    {
        private static readonly JsonSerializerOptions SerializeOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        [Fact]
        public void ToolParameterProperty_NullFields_AreOmittedFromJson()
        {
            // A property with no Items/Properties/Required should serialize cleanly
            var prop = new ToolParameterProperty
            {
                Type = "string",
                Description = "A simple string"
            };

            var json = JsonSerializer.Serialize(prop, SerializeOptions);
            var doc = JsonDocument.Parse(json);

            Assert.True(doc.RootElement.TryGetProperty("type", out _));
            Assert.True(doc.RootElement.TryGetProperty("description", out _));
            Assert.False(doc.RootElement.TryGetProperty("items", out _));
            Assert.False(doc.RootElement.TryGetProperty("properties", out _));
            Assert.False(doc.RootElement.TryGetProperty("required", out _));
        }

        [Fact]
        public void NestedArraySchema_SerializesCorrectly()
        {
            // Simulate the edits parameter schema: array of objects with old_string/new_string
            var editsParam = new ToolParameterProperty
            {
                Type = "array",
                Description = "Array of edits",
                Items = new ToolParameterProperty
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolParameterProperty>
                    {
                        ["old_string"] = new ToolParameterProperty
                        {
                            Type = "string",
                            Description = "Text to replace"
                        },
                        ["new_string"] = new ToolParameterProperty
                        {
                            Type = "string",
                            Description = "Replacement text"
                        }
                    },
                    Required = new[] { "old_string", "new_string" }
                }
            };

            var json = JsonSerializer.Serialize(editsParam, SerializeOptions);
            var doc = JsonDocument.Parse(json);

            // Verify top level
            Assert.Equal("array", doc.RootElement.GetProperty("type").GetString());

            // Verify items
            var items = doc.RootElement.GetProperty("items");
            Assert.Equal("object", items.GetProperty("type").GetString());

            // Verify nested properties
            var props = items.GetProperty("properties");
            Assert.True(props.TryGetProperty("old_string", out var oldStr));
            Assert.Equal("string", oldStr.GetProperty("type").GetString());
            Assert.True(props.TryGetProperty("new_string", out var newStr));
            Assert.Equal("string", newStr.GetProperty("type").GetString());

            // Verify required array
            var required = items.GetProperty("required");
            var requiredValues = required.EnumerateArray().Select(e => e.GetString()).ToList();
            Assert.Contains("old_string", requiredValues);
            Assert.Contains("new_string", requiredValues);
        }

        [Fact]
        public void ThreeLevelNestedSchema_SerializesCorrectly()
        {
            // Simulate the files parameter: array → object(file_path, edits) → edits array → object(old/new)
            var filesParam = new ToolParameterProperty
            {
                Type = "array",
                Description = "Array of file edits",
                Items = new ToolParameterProperty
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolParameterProperty>
                    {
                        ["file_path"] = new ToolParameterProperty
                        {
                            Type = "string",
                            Description = "Path to the file"
                        },
                        ["edits"] = new ToolParameterProperty
                        {
                            Type = "array",
                            Description = "Edits for this file",
                            Items = new ToolParameterProperty
                            {
                                Type = "object",
                                Properties = new Dictionary<string, ToolParameterProperty>
                                {
                                    ["old_string"] = new ToolParameterProperty { Type = "string", Description = "Text to replace" },
                                    ["new_string"] = new ToolParameterProperty { Type = "string", Description = "Replacement" }
                                },
                                Required = new[] { "old_string", "new_string" }
                            }
                        }
                    },
                    Required = new[] { "file_path", "edits" }
                }
            };

            var json = JsonSerializer.Serialize(filesParam, SerializeOptions);
            var doc = JsonDocument.Parse(json);

            // Level 1: files array
            Assert.Equal("array", doc.RootElement.GetProperty("type").GetString());

            // Level 2: file entry object
            var fileEntry = doc.RootElement.GetProperty("items");
            Assert.Equal("object", fileEntry.GetProperty("type").GetString());
            Assert.True(fileEntry.GetProperty("properties").TryGetProperty("file_path", out _));

            // Level 3: edits array within file entry
            var editsInFile = fileEntry.GetProperty("properties").GetProperty("edits");
            Assert.Equal("array", editsInFile.GetProperty("type").GetString());

            // Level 3: edit entry object
            var editEntry = editsInFile.GetProperty("items");
            Assert.Equal("object", editEntry.GetProperty("type").GetString());
            Assert.True(editEntry.GetProperty("properties").TryGetProperty("old_string", out _));
            Assert.True(editEntry.GetProperty("properties").TryGetProperty("new_string", out _));

            var editRequired = editEntry.GetProperty("required")
                .EnumerateArray().Select(e => e.GetString()).ToList();
            Assert.Contains("old_string", editRequired);
            Assert.Contains("new_string", editRequired);
        }
    }
}
