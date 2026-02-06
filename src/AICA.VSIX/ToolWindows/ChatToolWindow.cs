using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Shell;

namespace AICA.ToolWindows
{
    [Guid("F6A7B8C9-D0E1-2345-ABCD-6789ABCDEF01")]
    public class ChatToolWindow : ToolWindowPane
    {
        private ChatToolWindowControl _control;

        public ChatToolWindow() : base(null)
        {
            Caption = "AICA Chat";
            BitmapImageMoniker = KnownMonikers.StatusInformation;
        }

        protected override void Initialize()
        {
            base.Initialize();
            _control = new ChatToolWindowControl();
            Content = _control;
        }

        public async Task UpdateContentAsync(string content)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            _control?.UpdateContent(content);
        }

        public async Task AppendMessageAsync(string role, string content)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            _control?.AppendMessage(role, content);
        }

        public async Task SendProgrammaticMessageAsync(string userMessage)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (_control != null)
            {
                await _control.SendProgrammaticMessageAsync(userMessage);
            }
        }

        public void ClearConversation()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _control?.ClearConversation();
        }
    }
}
