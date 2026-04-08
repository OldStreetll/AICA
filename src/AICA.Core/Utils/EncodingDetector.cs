using System;
using System.IO;
using System.Text;

namespace AICA.Core.Utils
{
    /// <summary>
    /// Detects file encoding and preserves it across read/write cycles.
    /// Strategy: BOM detection → UTF-8 strict decode → system ANSI fallback.
    /// </summary>
    public static class EncodingDetector
    {
        /// <summary>
        /// Detection result containing encoding info and whether BOM was present.
        /// </summary>
        public class EncodingInfo
        {
            public Encoding Encoding { get; set; }
            public bool HasBom { get; set; }

            /// <summary>
            /// Returns the encoding configured for write-back (with/without BOM).
            /// </summary>
            public Encoding GetWriteEncoding()
            {
                // UTF-8: respect original BOM presence
                if (Encoding.CodePage == 65001)
                    return new UTF8Encoding(HasBom);

                // Other encodings: return as-is
                return Encoding;
            }
        }

        /// <summary>
        /// Detect file encoding from raw bytes.
        /// 1. Check BOM (UTF-8/UTF-16 LE/BE)
        /// 2. No BOM → try UTF-8 strict decode
        /// 3. UTF-8 fails → system default ANSI (GBK/CP936 on Chinese Windows)
        ///
        /// NOTE: Encoding.Default returns system ANSI codepage on .NET Framework 4.8.
        /// On .NET 6+ it returns UTF-8. If migrating, change fallback to
        /// Encoding.GetEncoding(936) or register CodePagesEncodingProvider.
        /// </summary>
        public static EncodingInfo DetectEncoding(byte[] rawBytes)
        {
            if (rawBytes == null || rawBytes.Length == 0)
                return new EncodingInfo { Encoding = Encoding.UTF8, HasBom = false };

            // 1. BOM detection
            if (rawBytes.Length >= 3
                && rawBytes[0] == 0xEF && rawBytes[1] == 0xBB && rawBytes[2] == 0xBF)
                return new EncodingInfo { Encoding = Encoding.UTF8, HasBom = true };

            if (rawBytes.Length >= 2 && rawBytes[0] == 0xFF && rawBytes[1] == 0xFE)
                return new EncodingInfo { Encoding = Encoding.Unicode, HasBom = true };

            if (rawBytes.Length >= 2 && rawBytes[0] == 0xFE && rawBytes[1] == 0xFF)
                return new EncodingInfo { Encoding = Encoding.BigEndianUnicode, HasBom = true };

            // 2. Try UTF-8 strict decode
            try
            {
                var utf8Strict = new UTF8Encoding(false, true);
                utf8Strict.GetString(rawBytes);
                return new EncodingInfo { Encoding = Encoding.UTF8, HasBom = false };
            }
            catch (DecoderFallbackException)
            {
                // ignored — not valid UTF-8
            }

            // 3. Fallback to system default ANSI (GBK on Chinese Windows)
            return new EncodingInfo { Encoding = Encoding.Default, HasBom = false };
        }

        /// <summary>
        /// Read file with encoding detection. Returns content and encoding info.
        /// </summary>
        public static void ReadWithEncoding(string filePath, out string content, out EncodingInfo encodingInfo)
        {
            var rawBytes = File.ReadAllBytes(filePath);
            encodingInfo = DetectEncoding(rawBytes);

            // Skip BOM bytes when decoding
            var preamble = encodingInfo.Encoding.GetPreamble();
            int offset = 0;
            if (encodingInfo.HasBom && preamble.Length > 0 && rawBytes.Length >= preamble.Length)
            {
                bool match = true;
                for (int i = 0; i < preamble.Length; i++)
                {
                    if (rawBytes[i] != preamble[i]) { match = false; break; }
                }
                if (match) offset = preamble.Length;
            }

            content = encodingInfo.Encoding.GetString(rawBytes, offset, rawBytes.Length - offset);
        }
    }
}
