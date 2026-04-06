using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AICA.Core.LLM
{
    public enum ContentPartType
    {
        Text,
        Image,
        Code
    }

    public interface IContentPart
    {
        ContentPartType Type { get; }
    }

    public class TextPart : IContentPart
    {
        public ContentPartType Type => ContentPartType.Text;
        public string Text { get; }

        public TextPart(string text)
        {
            Text = text ?? string.Empty;
        }
    }

    public class ImagePart : IContentPart
    {
        public const int MaxBase64Bytes = 2 * 1024 * 1024;
        public const int MaxDimension = 2048;

        public ContentPartType Type => ContentPartType.Image;
        public string Base64Data { get; }
        public string MediaType { get; }

        public ImagePart(string base64Data, string mediaType)
        {
            if (string.IsNullOrEmpty(base64Data))
                throw new ArgumentException("Base64 data cannot be empty.", nameof(base64Data));
            if (string.IsNullOrEmpty(mediaType))
                throw new ArgumentException("Media type cannot be empty.", nameof(mediaType));

            // Validate base64 size (each base64 char encodes 6 bits → 4 chars = 3 bytes)
            int estimatedBytes = (base64Data.Length * 3) / 4;
            if (estimatedBytes > MaxBase64Bytes)
                throw new ArgumentException(
                    $"Image exceeds maximum size of {MaxBase64Bytes / 1024 / 1024}MB (estimated {estimatedBytes / 1024 / 1024}MB).",
                    nameof(base64Data));

            Base64Data = base64Data;
            MediaType = mediaType;
        }

        public string ToDataUrl() => $"data:{MediaType};base64,{Base64Data}";
    }

    public class CodePart : IContentPart
    {
        public ContentPartType Type => ContentPartType.Code;
        public string Code { get; }
        public string FilePath { get; }
        public int StartLine { get; }
        public int StartColumn { get; }
        public int EndLine { get; }
        public int EndColumn { get; }
        public string Language { get; }
        public string ProjectName { get; }

        public CodePart(string code, string filePath, int startLine, int startColumn,
            int endLine, int endColumn, string language, string projectName = null)
        {
            Code = code ?? throw new ArgumentNullException(nameof(code));
            FilePath = filePath ?? string.Empty;
            StartLine = startLine;
            StartColumn = startColumn;
            EndLine = endLine;
            EndColumn = endColumn;
            Language = language ?? string.Empty;
            ProjectName = projectName ?? string.Empty;
        }

        public string ToStructuredText()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"File: {FilePath} (L{StartLine}:C{StartColumn} - L{EndLine}:C{EndColumn})");
            if (!string.IsNullOrEmpty(ProjectName))
                sb.AppendLine($"Project: {ProjectName}");
            sb.AppendLine($"```{Language}");
            sb.AppendLine(Code);
            sb.AppendLine("```");
            return sb.ToString();
        }
    }

    internal static class ContentPartHelpers
    {
        public static string ConcatTextAndCodeParts(List<IContentPart> parts)
        {
            if (parts == null || parts.Count == 0)
                return string.Empty;

            var sb = new StringBuilder();
            foreach (var part in parts)
            {
                switch (part)
                {
                    case TextPart text:
                        sb.Append(text.Text);
                        break;
                    case CodePart code:
                        sb.Append(code.ToStructuredText());
                        break;
                    // ImagePart is intentionally excluded from text concatenation
                }
            }
            return sb.ToString();
        }
    }
}
