using Markdig;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AICA.Options;
using AICA.Core.LLM;
using AICA.Core.Agent;
using AICA.Core.Tools;
using AICA.Agent;
using EnvDTE80;

namespace AICA.ToolWindows
{
    public partial class ChatToolWindowControl : UserControl
    {
        private readonly MarkdownPipeline _markdownPipeline;
        private readonly List<ConversationMessage> _conversation = new List<ConversationMessage>();
        private readonly List<ChatMessage> _llmHistory = new List<ChatMessage>();
        private bool _isBrowserReady;
        private bool _isSending;
        private OpenAIClient _llmClient;
        private CancellationTokenSource _currentCts;
        private bool _agentMode = true; // Default to Agent mode
        private AgentExecutor _agentExecutor;
        private ToolDispatcher _toolDispatcher;
        private VSAgentContext _agentContext;
        private VSUIContext _uiContext;

        public ChatToolWindowControl()
        {
            InitializeComponent();

            _markdownPipeline = new MarkdownPipelineBuilder()
                .UseAdvancedExtensions()
                .Build();

            ChatBrowser.LoadCompleted += ChatBrowser_LoadCompleted;
            ChatBrowser.NavigateToString(BuildPageHtml(string.Empty));
        }

        private void ChatBrowser_LoadCompleted(object sender, System.Windows.Navigation.NavigationEventArgs e)
        {
            _isBrowserReady = true;
        }

        public void UpdateContent(string content)
        {
            var html = Markdig.Markdown.ToHtml(content ?? string.Empty, _markdownPipeline);
            UpdateBrowserContent($"<div class=\"message assistant\"><div class=\"role\">AI</div><div class=\"content\">{html}</div></div>");
        }

        public void AppendMessage(string role, string content)
        {
            _conversation.Add(new ConversationMessage { Role = role, Content = content });
            RenderConversation();
        }

        /// <summary>
        /// Send a message programmatically (from right-click commands) and trigger LLM response
        /// </summary>
        public async System.Threading.Tasks.Task SendProgrammaticMessageAsync(string userMessage)
        {
            if (_isSending || string.IsNullOrWhiteSpace(userMessage)) return;

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            InputTextBox.Text = userMessage;
            await SendMessageAsync();
        }

        public void ClearConversation()
        {
            _conversation.Clear();
            _llmHistory.Clear();
            _llmClient?.Dispose();
            _llmClient = null;
            _agentExecutor = null;
            _toolDispatcher = null;
            _agentContext = null;
            _uiContext = null;
            UpdateBrowserContent(string.Empty);
        }

        private void InitializeAgentComponents(GeneralOptions options)
        {
            // Initialize tool dispatcher with available tools
            _toolDispatcher = new ToolDispatcher();
            _toolDispatcher.RegisterTool(new ReadFileTool());
            _toolDispatcher.RegisterTool(new WriteFileTool());
            _toolDispatcher.RegisterTool(new EditFileTool());
            _toolDispatcher.RegisterTool(new ListDirTool());
            _toolDispatcher.RegisterTool(new GrepSearchTool());
            _toolDispatcher.RegisterTool(new FindByNameTool());
            _toolDispatcher.RegisterTool(new RunCommandTool());

            // Initialize LLM client
            var clientOptions = new LLMClientOptions
            {
                ApiEndpoint = options.ApiEndpoint,
                ApiKey = options.ApiKey,
                Model = options.ModelName,
                MaxTokens = options.MaxTokens,
                Temperature = options.Temperature,
                TimeoutSeconds = options.RequestTimeoutSeconds,
                Stream = true
            };
            _llmClient = new OpenAIClient(clientOptions);

            // Initialize VS-specific contexts
            ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                var dte = await AsyncServiceProvider.GlobalProvider.GetServiceAsync(typeof(EnvDTE.DTE)) as DTE2;
                
                _agentContext = new VSAgentContext(
                    dte,
                    confirmationHandler: async (op, details, ct) =>
                    {
                        return await _uiContext.ShowConfirmationAsync(op, details, ct);
                    });

                _uiContext = new VSUIContext(
                    streamingContentUpdater: content =>
                    {
                        ThreadHelper.JoinableTaskFactory.Run(async () =>
                        {
                            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                            RenderConversation(content);
                        });
                    });
            });

            // Initialize Agent executor
            _agentExecutor = new AgentExecutor(_llmClient, _toolDispatcher);
        }

        private void RenderConversation(string streamingContent = null)
        {
            var bodyBuilder = new StringBuilder();
            
            foreach (var message in _conversation)
            {
                var roleClass = message.Role == "user" ? "user" : "assistant";
                var roleName = message.Role == "user" ? "You" : "AI";
                var html = Markdig.Markdown.ToHtml(message.Content ?? string.Empty, _markdownPipeline);
                bodyBuilder.AppendLine($"<div class=\"message {roleClass}\"><div class=\"role\">{roleName}</div><div class=\"content\">{html}</div></div>");
            }

            if (!string.IsNullOrEmpty(streamingContent))
            {
                var streamingHtml = Markdig.Markdown.ToHtml(streamingContent, _markdownPipeline);
                bodyBuilder.AppendLine($"<div class=\"message assistant streaming\"><div class=\"role\">AI</div><div class=\"content\">{streamingHtml}</div></div>");
            }

            UpdateBrowserContent(bodyBuilder.ToString());
        }

        private void UpdateBrowserContent(string innerHtml)
        {
            if (!_isBrowserReady || ChatBrowser.Document == null)
            {
                ChatBrowser.NavigateToString(BuildPageHtml(innerHtml));
                return;
            }

            try
            {
                dynamic doc = ChatBrowser.Document;
                dynamic log = doc?.getElementById("chat-log");
                if (log != null)
                {
                    log.innerHTML = innerHtml;
                    dynamic window = doc?.parentWindow;
                    window?.scrollTo(0, doc?.body?.scrollHeight ?? 0);
                    return;
                }
            }
            catch { }

            ChatBrowser.NavigateToString(BuildPageHtml(innerHtml));
        }

        private async void SendButton_Click(object sender, RoutedEventArgs e)
        {
            await SendMessageAsync();
        }

        private async void InputTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
            {
                e.Handled = true;
                await SendMessageAsync();
            }
        }

        private async System.Threading.Tasks.Task SendMessageAsync()
        {
            if (_isSending) return;

            var userMessage = InputTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(userMessage)) return;

            _isSending = true;
            InputTextBox.IsEnabled = false;
            SendButton.IsEnabled = false;
            _currentCts = new CancellationTokenSource();

            try
            {
                InputTextBox.Text = string.Empty;
                AppendMessage("user", userMessage);

                var options = await GeneralOptions.GetLiveInstanceAsync();
                
                if (string.IsNullOrEmpty(options.ApiEndpoint))
                {
                    AppendMessage("assistant", "⚠️ Please configure the LLM API endpoint in Tools > Options > AICA > General");
                    return;
                }

                // Initialize components if needed
                if (_llmClient == null)
                {
                    InitializeAgentComponents(options);
                }

                // Use Agent mode only if tool calling is enabled
                if (_agentMode && _agentExecutor != null && options.EnableToolCalling)
                {
                    await ExecuteAgentModeAsync(userMessage);
                }
                else
                {
                    await ExecuteChatModeAsync(userMessage);
                }
            }
            catch (OperationCanceledException)
            {
                AppendMessage("assistant", "🛑 Request cancelled.");
            }
            catch (LLMException ex)
            {
                AppendMessage("assistant", $"❌ LLM Error: {ex.Message}");
            }
            catch (Exception ex)
            {
                AppendMessage("assistant", $"❌ Error: {ex.Message}");
            }
            finally
            {
                _isSending = false;
                _currentCts?.Dispose();
                _currentCts = null;
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                InputTextBox.IsEnabled = true;
                SendButton.IsEnabled = true;
                InputTextBox.Focus();
            }
        }

        private async System.Threading.Tasks.Task ExecuteAgentModeAsync(string userMessage)
        {
            var responseBuilder = new StringBuilder();
            var toolOutputBuilder = new StringBuilder();
            var hasToolCalls = false;

            await foreach (var step in _agentExecutor.ExecuteAsync(userMessage, _agentContext, _uiContext, _currentCts.Token))
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                switch (step.Type)
                {
                    case AgentStepType.TextChunk:
                        responseBuilder.Append(step.Text);
                        RenderConversation(responseBuilder.ToString() + toolOutputBuilder.ToString());
                        break;

                    case AgentStepType.ToolStart:
                        hasToolCalls = true;
                        toolOutputBuilder.AppendLine();
                        toolOutputBuilder.AppendLine($"🔧 **Calling tool:** `{step.ToolCall.Name}`");
                        RenderConversation(responseBuilder.ToString() + toolOutputBuilder.ToString());
                        break;

                    case AgentStepType.ToolResult:
                        var status = step.Result.Success ? "✅" : "❌";
                        var resultPreview = TruncateForDisplay(step.Result.Success ? step.Result.Content : step.Result.Error, 200);
                        toolOutputBuilder.AppendLine($"{status} **Result:** {resultPreview}");
                        toolOutputBuilder.AppendLine();
                        RenderConversation(responseBuilder.ToString() + toolOutputBuilder.ToString());
                        break;

                    case AgentStepType.Complete:
                        var finalContent = responseBuilder.ToString() + toolOutputBuilder.ToString();
                        
                        // Add diagnostic hint if no tools were called but response suggests tool usage was intended
                        if (!hasToolCalls && ContainsToolIntentLanguage(responseBuilder.ToString()))
                        {
                            finalContent += "\n\n---\n⚠️ **提示**: AI 描述了要执行的操作但未实际调用工具。\n" +
                                "可能原因：\n" +
                                "1. LLM 服务器未启用 function calling（需要 `--enable-auto-tool-choice`）\n" +
                                "2. 模型不支持 OpenAI 格式的工具调用\n" +
                                "3. 在选项中检查 'Enable Tool Calling' 是否已启用";
                        }
                        
                        if (!string.IsNullOrWhiteSpace(finalContent))
                        {
                            _conversation.Add(new ConversationMessage { Role = "assistant", Content = finalContent });
                        }
                        RenderConversation();
                        break;

                    case AgentStepType.Error:
                        AppendMessage("assistant", $"❌ Agent Error: {step.ErrorMessage}");
                        break;
                }
            }
        }

        private async System.Threading.Tasks.Task ExecuteChatModeAsync(string userMessage)
        {
            // Add system prompt if this is the first message
            if (_llmHistory.Count == 0)
            {
                _llmHistory.Add(ChatMessage.System(
                    "You are AICA, an AI coding assistant for Visual Studio. " +
                    "Help the user with programming tasks, code explanations, debugging, and more. " +
                    "Be concise but thorough. Use markdown for code formatting."));
            }

            _llmHistory.Add(ChatMessage.User(userMessage));

            var responseBuilder = new StringBuilder();
            
            await foreach (var chunk in _llmClient.StreamChatAsync(_llmHistory, null, _currentCts.Token))
            {
                if (chunk.Type == LLMChunkType.Text && !string.IsNullOrEmpty(chunk.Text))
                {
                    responseBuilder.Append(chunk.Text);
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    RenderConversation(responseBuilder.ToString());
                }
                else if (chunk.Type == LLMChunkType.Done)
                {
                    break;
                }
            }

            var finalResponse = responseBuilder.ToString();
            if (!string.IsNullOrEmpty(finalResponse))
            {
                _llmHistory.Add(ChatMessage.Assistant(finalResponse));
                _conversation.Add(new ConversationMessage { Role = "assistant", Content = finalResponse });
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                RenderConversation();
            }
            else
            {
                AppendMessage("assistant", "⚠️ No response received from the LLM.");
            }
        }

        private string TruncateForDisplay(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text)) return "(empty)";
            if (text.Length <= maxLength) return text.Replace("\n", " ").Replace("\r", "");
            return text.Substring(0, maxLength).Replace("\n", " ").Replace("\r", "") + "...";
        }

        private bool ContainsToolIntentLanguage(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            
            // Detect phrases that indicate the AI intended to use tools but didn't
            var intentPhrases = new[]
            {
                "让我", "我将", "我来", "我会", "让我们",
                "查看", "读取", "列出", "打开", "检查",
                "let me", "i will", "i'll", "let's",
                "read the file", "list the", "check the"
            };
            
            var lowerText = text.ToLowerInvariant();
            foreach (var phrase in intentPhrases)
            {
                if (lowerText.Contains(phrase.ToLowerInvariant()))
                    return true;
            }
            return false;
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            ClearConversation();
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            VS.Commands.ExecuteAsync("Tools.Options").FireAndForget();
        }

        private string BuildPageHtml(string innerContent)
        {
            return $@"<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"" />
    <style>
        :root {{ color-scheme: light dark; }}
        body {{
            margin: 0; padding: 0;
            font-family: 'Segoe UI', 'Helvetica Neue', Arial, sans-serif;
            font-size: 14px; line-height: 1.5;
            background: #1e1e1e; color: #d4d4d4;
        }}
        .container {{ padding: 12px 16px 20px 16px; max-width: 1100px; margin: 0 auto; }}
        @media (prefers-color-scheme: light) {{
            body {{ background: #ffffff; color: #1e1e1e; }}
            pre code {{ background: #f6f8fa; color: #1e1e1e; }}
            .message {{ background: #f5f7fb; border-color: #d0d7de; }}
            .message.user {{ background: #e8f1ff; border-color: #b7cff9; }}
        }}
        .message {{
            margin: 0 0 12px 0; padding: 10px 12px;
            border-radius: 8px; border: 1px solid #3c3c3c;
            background: #252526; box-shadow: 0 1px 2px rgba(0,0,0,0.35);
        }}
        .message.user {{ background: #0e3a5c; border-color: #2d5f8a; }}
        .message.streaming {{ opacity: 0.85; }}
        .role {{
            font-size: 11px; letter-spacing: 0.03em;
            text-transform: uppercase; color: #9ca3af; margin-bottom: 6px;
        }}
        .content p {{ margin: 0 0 0.75em 0; }}
        pre {{ overflow-x: auto; }}
        pre code {{
            display: block; padding: 12px; border-radius: 6px;
            background: #1e1e1e; color: #d4d4d4;
            font-family: Consolas, 'Courier New', monospace; font-size: 13px;
        }}
        code {{
            font-family: Consolas, 'Courier New', monospace;
            background: rgba(255,255,255,0.08); padding: 0 3px; border-radius: 3px;
        }}
        a {{ color: #4aa3ff; text-decoration: none; }}
        a:hover {{ text-decoration: underline; }}
        h1, h2, h3, h4, h5, h6 {{ margin-top: 1.4em; margin-bottom: 0.6em; }}
    </style>
</head>
<body>
<div id=""chat-log"" class=""container"">
{innerContent}
</div>
</body>
</html>";
        }

        private class ConversationMessage
        {
            public string Role { get; set; }
            public string Content { get; set; }
        }
    }
}
