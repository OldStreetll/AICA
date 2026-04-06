namespace AICA.Core.Tools.Pipeline
{
    /// <summary>
    /// Identifies when an edit step runs relative to the file write.
    /// </summary>
    public enum EditPhase
    {
        /// <summary>Before the edit is applied (e.g., file snapshot for rollback)</summary>
        PreEdit,

        /// <summary>After the edit is applied (e.g., format, diagnostics, build)</summary>
        PostEdit
    }
}
