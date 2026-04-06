namespace AICA.Core.Tools.Pipeline
{
    /// <summary>
    /// Records a symbol whose signature changed during an edit.
    /// Used by S3 HeaderSyncStep to detect .h/.hpp synchronization needs.
    /// </summary>
    public class SymbolChange
    {
        /// <summary>Fully qualified symbol name (e.g., "MyNamespace::MyClass::MyMethod")</summary>
        public string Name { get; set; }

        /// <summary>Symbol signature before the edit</summary>
        public string OldSignature { get; set; }

        /// <summary>Symbol signature after the edit</summary>
        public string NewSignature { get; set; }

        /// <summary>File path where the change occurred</summary>
        public string FilePath { get; set; }
    }
}
