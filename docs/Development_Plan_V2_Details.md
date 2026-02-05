# VS2022 AI 插件开发计划 - 详细规范补充

> 本文档是 Development_Plan_V2.md 的补充，包含详细的实现规范和代码示例

---

## 一、工具实现详细规范

### 1.1 read_file 工具

```csharp
public class ReadFileTool : IAgentTool
{
    public string Name => "read_file";
    
    public ToolDefinition GetDefinition() => new()
    {
        Name = "read_file",
        Description = "Read the contents of a file at the specified path.",
        Parameters = new[]
        {
            new ToolParameter("path", "string", "The path of the file to read", required: true)
        }
    };
    
    public async Task<ToolResult> ExecuteAsync(ToolCall call, ITaskContext ctx)
    {
        var path = call.GetParam<string>("path");
        
        // 1. 解析路径
        var absolutePath = ctx.PathResolver.Resolve(path);
        if (absolutePath == null)
            return ToolResult.Error($"Invalid path: {path}");
        
        // 2. 检查访问权限（.aicaignore）
        if (!ctx.FilePolicy.IsAccessible(path))
            return ToolResult.Error($"Access denied by .aicaignore: {path}");
        
        // 3. 检查文件存在
        if (!File.Exists(absolutePath))
            return ToolResult.Error($"File not found: {path}");
        
        // 4. 读取文件
        var content = await File.ReadAllTextAsync(absolutePath);
        
        // 5. 记录上下文
        ctx.FileTracker.TrackRead(path);
        
        return ToolResult.Success(content);
    }
}
```

### 1.2 edit 工具（差异化编辑）

```csharp
public class EditFileTool : IAgentTool
{
    public string Name => "edit";
    
    public ToolDefinition GetDefinition() => new()
    {
        Name = "edit",
        Description = @"Performs exact string replacements in files.
- old_string must be unique in the file (or use replace_all)
- Preserves exact indentation
- The edit will FAIL if old_string is not found or not unique",
        Parameters = new[]
        {
            new ToolParameter("file_path", "string", "The path to the file", required: true),
            new ToolParameter("old_string", "string", "The text to replace (must be unique)", required: true),
            new ToolParameter("new_string", "string", "The replacement text", required: true),
            new ToolParameter("replace_all", "boolean", "Replace all occurrences", required: false)
        }
    };
    
    public async Task HandlePartialAsync(ToolCall call, IUIContext ui)
    {
        // 流式预览：边生成边显示
        var filePath = call.GetParam<string>("file_path");
        var newString = call.GetParam<string>("new_string");
        
        if (!string.IsNullOrEmpty(filePath) && !string.IsNullOrEmpty(newString))
        {
            await ui.ShowPartialEditPreviewAsync(filePath, newString);
        }
    }
    
    public async Task<ToolResult> ExecuteAsync(ToolCall call, ITaskContext ctx)
    {
        var filePath = call.GetParam<string>("file_path");
        var oldString = call.GetParam<string>("old_string");
        var newString = call.GetParam<string>("new_string");
        var replaceAll = call.GetParam<bool>("replace_all", false);
        
        // 1. 解析路径
        var absolutePath = ctx.PathResolver.Resolve(filePath);
        
        // 2. 检查访问权限
        if (!ctx.FilePolicy.IsAccessible(filePath))
            return ToolResult.Error($"Access denied: {filePath}");
        
        // 3. 读取原文件
        if (!File.Exists(absolutePath))
            return ToolResult.Error($"File not found: {filePath}");
        var content = await File.ReadAllTextAsync(absolutePath);
        
        // 4. 检查唯一性
        var count = CountOccurrences(content, oldString);
        if (count == 0)
            return ToolResult.Error("old_string not found in file. Provide correct text to match.");
        if (count > 1 && !replaceAll)
            return ToolResult.Error($"old_string found {count} times. Provide more context to make it unique, or use replace_all=true.");
        
        // 5. 执行替换
        var newContent = replaceAll 
            ? content.Replace(oldString, newString)
            : ReplaceFirst(content, oldString, newString);
        
        // 6. 显示 Diff 预览
        await ctx.DiffView.ShowAsync(absolutePath, content, newContent);
        
        // 7. 等待用户确认
        var approved = await ctx.AskApprovalAsync("edit", $"Edit file: {filePath}");
        if (!approved)
        {
            await ctx.DiffView.RevertAsync();
            return ToolResult.Denied();
        }
        
        // 8. 保存文件
        await File.WriteAllTextAsync(absolutePath, newContent);
        await ctx.DiffView.ApplyAsync();
        
        return ToolResult.Success($"Successfully edited {filePath}");
    }
    
    private int CountOccurrences(string text, string pattern)
    {
        int count = 0, index = 0;
        while ((index = text.IndexOf(pattern, index)) != -1)
        {
            count++;
            index += pattern.Length;
        }
        return count;
    }
    
    private string ReplaceFirst(string text, string oldValue, string newValue)
    {
        var index = text.IndexOf(oldValue);
        if (index < 0) return text;
        return text.Substring(0, index) + newValue + text.Substring(index + oldValue.Length);
    }
}
```

### 1.3 execute_command 工具

```csharp
public class RunCommandTool : IAgentTool
{
    public string Name => "execute_command";
    
    public async Task<ToolResult> ExecuteAsync(ToolCall call, ITaskContext ctx)
    {
        var command = call.GetParam<string>("command");
        var cwd = call.GetParam<string>("cwd", ctx.WorkspaceRoot);
        var timeout = call.GetParam<int>("timeout", 60);
        
        // 1. 安全检查
        var safety = ctx.CommandClassifier.Classify(command);
        
        if (safety == CommandSafety.Forbidden)
        {
            return ToolResult.Error($"Command denied by security policy: {command}");
        }
        
        // 2. 需要确认的命令
        if (safety == CommandSafety.RequiresConfirmation)
        {
            var approved = await ctx.AskApprovalAsync("command", 
                $"Execute command?\n{command}");
            if (!approved) return ToolResult.Denied();
        }
        
        // 3. 执行命令
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeout));
        
        var psi = new ProcessStartInfo
        {
            FileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "cmd.exe" : "/bin/bash",
            Arguments = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? $"/c {command}" : $"-c \"{command}\"",
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        
        var output = new StringBuilder();
        using var process = new Process { StartInfo = psi };
        
        process.OutputDataReceived += (s, e) => {
            if (e.Data != null)
            {
                output.AppendLine(e.Data);
                ctx.UI.AppendCommandOutput(e.Data); // 实时输出
            }
        };
        process.ErrorDataReceived += (s, e) => {
            if (e.Data != null) output.AppendLine(e.Data);
        };
        
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            return ToolResult.Error($"Command timed out after {timeout}s");
        }
        
        return ToolResult.Success(output.ToString());
    }
}
```

---

## 二、命令安全分级配置

### 2.1 默认白名单（安全命令）

```json
{
  "allowPatterns": [
    "dotnet build *",
    "dotnet run *",
    "dotnet test *",
    "dotnet restore *",
    "dotnet clean *",
    "dotnet --version",
    "git status",
    "git log *",
    "git diff *",
    "git branch *",
    "git show *",
    "dir *",
    "ls *",
    "cat *",
    "type *",
    "echo *",
    "pwd",
    "cd *"
  ]
}
```

### 2.2 默认黑名单（禁止命令）

```json
{
  "denyPatterns": [
    "rm -rf *",
    "del /s /q *",
    "format *",
    "fdisk *",
    "mkfs *",
    "dd *",
    "shutdown *",
    "reboot *",
    "wget *",
    "curl *",
    "Invoke-WebRequest *",
    "powershell -enc *",
    "cmd /c start *"
  ]
}
```

### 2.3 危险字符检测

```csharp
public class CommandClassifier
{
    private static readonly HashSet<string> DangerousPatterns = new()
    {
        "`",      // 命令替换
        "$()",    // 命令替换
        "\n",     // 换行（命令分隔）
        "\r",     // 回车
        "|&",     // 管道重定向
        ">&",     // 输出重定向
    };
    
    public bool HasDangerousChars(string command)
    {
        // 检测引号外的危险字符
        bool inSingleQuote = false, inDoubleQuote = false;
        
        for (int i = 0; i < command.Length; i++)
        {
            char c = command[i];
            
            if (c == '\'' && !inDoubleQuote) inSingleQuote = !inSingleQuote;
            if (c == '"' && !inSingleQuote) inDoubleQuote = !inDoubleQuote;
            
            if (!inSingleQuote && !inDoubleQuote)
            {
                // 检测反引号
                if (c == '`') return true;
                
                // 检测换行
                if (c == '\n' || c == '\r') return true;
            }
        }
        
        return false;
    }
}
```

---

## 三、.aicaignore 文件规范

### 3.1 语法（与 .gitignore 相同）

```gitignore
# 忽略敏感配置
appsettings.Production.json
*.secrets.json
.env
.env.*

# 忽略密钥文件
*.pem
*.key
*.pfx
id_rsa*

# 忽略敏感目录
/secrets/
/credentials/
/.ssh/

# 忽略日志
*.log
/logs/

# 使用 !include 引入其他文件
!include .gitignore
```

### 3.2 实现

```csharp
public class FileAccessPolicy
{
    private readonly Ignore _ignoreRules;
    private readonly FileSystemWatcher _watcher;
    private readonly string _workspaceRoot;
    
    public async Task InitializeAsync()
    {
        var ignorePath = Path.Combine(_workspaceRoot, ".aicaignore");
        if (File.Exists(ignorePath))
        {
            var content = await File.ReadAllTextAsync(ignorePath);
            await ProcessIgnoreContentAsync(content);
        }
        
        // 监视文件变化
        _watcher = new FileSystemWatcher(_workspaceRoot, ".aicaignore");
        _watcher.Changed += async (s, e) => await ReloadAsync();
        _watcher.EnableRaisingEvents = true;
    }
    
    public bool IsAccessible(string relativePath)
    {
        return !_ignoreRules.IsIgnored(relativePath);
    }
}
```

---

## 四、上下文管理策略

### 4.1 Token 预算分配

```csharp
public class ContextWindow
{
    private readonly int _maxTokens;      // 模型最大 Token（如 32K）
    private readonly int _reservedOutput; // 预留输出（如 4K）
    
    public int AvailableTokens => _maxTokens - _reservedOutput;
    
    public ContextBudget AllocateBudget()
    {
        var available = AvailableTokens;
        
        return new ContextBudget
        {
            SystemPrompt = (int)(available * 0.15),   // 15% 系统提示
            ToolDefinitions = (int)(available * 0.10), // 10% 工具定义
            ConversationHistory = (int)(available * 0.60), // 60% 对话历史
            CurrentContext = (int)(available * 0.15)  // 15% 当前上下文
        };
    }
}
```

### 4.2 上下文截断策略

```csharp
public class ContextTruncation
{
    public List<Message> Truncate(List<Message> messages, int maxTokens)
    {
        // 策略：保留首尾，截断中间
        // 1. 始终保留第一条用户消息（任务描述）
        // 2. 始终保留最近 N 轮对话
        // 3. 中间部分按优先级裁剪
        
        var result = new List<Message>();
        var currentTokens = 0;
        
        // 保留第一条
        result.Add(messages[0]);
        currentTokens += CountTokens(messages[0]);
        
        // 从后向前添加
        for (int i = messages.Count - 1; i > 0; i--)
        {
            var tokens = CountTokens(messages[i]);
            if (currentTokens + tokens > maxTokens) break;
            
            result.Insert(1, messages[i]);
            currentTokens += tokens;
        }
        
        // 添加截断提示
        if (result.Count < messages.Count)
        {
            var notice = new Message
            {
                Role = "system",
                Content = "[NOTE] Some previous conversation history has been removed to fit context window."
            };
            result.Insert(1, notice);
        }
        
        return result;
    }
}
```

---

## 五、System Prompt 模板

```markdown
You are an AI coding assistant for Visual Studio 2022. You help users with coding tasks by reading, writing, and editing files, searching code, and executing commands.

## Tools Available

You have access to the following tools:

### read_file
Read the contents of a file.
Parameters:
- path (required): The file path to read

### edit
Edit a file by replacing specific text.
Parameters:
- file_path (required): The file to edit
- old_string (required): The exact text to replace (must be unique in file)
- new_string (required): The replacement text

### write_to_file
Create a new file with the specified content.
Parameters:
- path (required): The file path to create
- content (required): The file content

### search_files
Search for text in files using regex.
Parameters:
- path (required): Directory to search
- regex (required): Search pattern

### execute_command
Run a terminal command.
Parameters:
- command (required): The command to run

### ask_followup_question
Ask the user for clarification.
Parameters:
- question (required): The question to ask

### attempt_completion
Mark the task as complete.
Parameters:
- result (required): Summary of what was accomplished

## Rules

1. Always read files before editing to understand context
2. Use edit tool for precise changes, not full file rewrites
3. old_string must match exactly (including whitespace)
4. Dangerous commands require user approval
5. Some files may be protected by .aicaignore

## Current Workspace

Working directory: {workspace_root}
Open files: {open_files}
```

---

## 六、验收测试用例

### 6.1 Agent 功能测试

| 测试用例 | 输入 | 预期结果 |
|----------|------|----------|
| 创建控制器 | "创建一个 UserController" | 生成 UserController.cs 文件 |
| 修改代码 | "给 User 类添加 Email 属性" | 正确编辑 User.cs |
| 搜索代码 | "找到所有使用 HttpClient 的地方" | 返回匹配文件列表 |
| 执行命令 | "运行 dotnet build" | 执行成功，显示输出 |
| 安全拦截 | "执行 rm -rf /" | 被拦截，显示错误 |

### 6.2 性能测试

| 测试项 | 目标 | 测量方法 |
|--------|------|----------|
| 补全响应 | < 500ms | 从按键到显示的时间 |
| 编辑预览 | < 100ms | Diff 显示延迟 |
| 索引速度 | 1000 文件/分钟 | 大项目索引时间 |
| 内存占用 | < 500MB | 任务管理器监控 |

---

**文档版本**: v2.0 补充  
**创建日期**: 2026-02-04
