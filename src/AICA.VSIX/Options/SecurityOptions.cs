using System.ComponentModel;
using System.Runtime.InteropServices;

namespace AICA.Options
{
    internal partial class OptionsProvider
    {
        [ComVisible(true)]
        public class SecurityOptionsPage : BaseOptionPage<SecurityOptions> { }
    }

    public class SecurityOptions : BaseOptionModel<SecurityOptions>
    {
        [Category("File Operations")]
        [DisplayName("Require Confirmation for File Edit")]
        [Description("Require user confirmation before editing existing files")]
        [DefaultValue(true)]
        public bool RequireConfirmForEdit { get; set; } = true;

        [Category("File Operations")]
        [DisplayName("Require Confirmation for File Create")]
        [Description("Require user confirmation before creating new files")]
        [DefaultValue(true)]
        public bool RequireConfirmForCreate { get; set; } = true;

        [Category("File Operations")]
        [DisplayName("Require Confirmation for File Delete")]
        [Description("Require user confirmation before deleting files")]
        [DefaultValue(true)]
        public bool RequireConfirmForDelete { get; set; } = true;

        [Category("Command Execution")]
        [DisplayName("Allow Command Execution")]
        [Description("Allow the Agent to execute terminal commands")]
        [DefaultValue(true)]
        public bool AllowCommandExecution { get; set; } = true;

        [Category("Command Execution")]
        [DisplayName("Auto-approve Safe Commands")]
        [Description("Automatically approve commands marked as safe by the model")]
        [DefaultValue(false)]
        public bool AutoApproveSafeCommands { get; set; } = false;

        [Category("Command Execution")]
        [DisplayName("Command Whitelist")]
        [Description("Commands that are always allowed (comma-separated, e.g., dotnet,npm,git)")]
        [DefaultValue("dotnet,npm,git,nuget")]
        public string CommandWhitelist { get; set; } = "dotnet,npm,git,nuget";

        [Category("Command Execution")]
        [DisplayName("Command Blacklist")]
        [Description("Commands that are never allowed (comma-separated, e.g., rm,del,format)")]
        [DefaultValue("rm,del,format,shutdown,restart")]
        public string CommandBlacklist { get; set; } = "rm,del,format,shutdown,restart";

        [Category("File Access")]
        [DisplayName("Respect .aicaignore")]
        [Description("Respect .aicaignore file patterns for file access")]
        [DefaultValue(true)]
        public bool RespectAicaIgnore { get; set; } = true;

        [Category("File Access")]
        [DisplayName("Protected Paths")]
        [Description("Paths that the Agent cannot access (comma-separated)")]
        [DefaultValue(".git,.vs,node_modules,bin,obj")]
        public string ProtectedPaths { get; set; } = ".git,.vs,node_modules,bin,obj";
    }
}
