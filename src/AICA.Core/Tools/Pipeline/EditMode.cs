namespace AICA.Core.Tools.Pipeline
{
    /// <summary>
    /// The editing mode that produced this edit context.
    /// </summary>
    public enum EditMode
    {
        /// <summary>Single old_string/new_string replacement</summary>
        Single,

        /// <summary>Multiple edits on the same file (one diff preview)</summary>
        MultiEdit,

        /// <summary>Edits across multiple files (per-file diff preview)</summary>
        MultiFile
    }
}
