using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AICA.Core.Rules
{
    /// <summary>
    /// 规则目录初始化器
    /// 负责创建和初始化 .aica-rules 目录
    /// </summary>
    public class RulesDirectoryInitializer
    {
        private static readonly object _lockObject = new object();
        private static readonly string RulesDirectoryName = ".aica-rules";
        private static readonly string GeneralRuleFileName = "general.md";

        /// <summary>
        /// 初始化规则目录和初始文件
        /// </summary>
        public async Task<InitializationResult> InitializeAsync(string solutionPath)
        {
            if (string.IsNullOrWhiteSpace(solutionPath))
            {
                return new InitializationResult
                {
                    Success = false,
                    Error = "Solution path cannot be null or empty"
                };
            }

            try
            {
                // 第一步：确保目录存在
                var directoryResult = await EnsureRulesDirectoryAsync(solutionPath);
                if (!directoryResult.Success)
                {
                    return directoryResult;
                }

                // 第二步：创建初始文件
                var filesResult = await CreateInitialFilesAsync(directoryResult.RulesPath);
                if (!filesResult.Success)
                {
                    return filesResult;
                }

                return new InitializationResult
                {
                    Success = true,
                    RulesPath = directoryResult.RulesPath
                };
            }
            catch (Exception ex)
            {
                return new InitializationResult
                {
                    Success = false,
                    Error = $"Failed to initialize rules directory: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// 确保规则目录存在
        /// </summary>
        public async Task<InitializationResult> EnsureRulesDirectoryAsync(string solutionPath)
        {
            if (string.IsNullOrWhiteSpace(solutionPath))
            {
                return new InitializationResult
                {
                    Success = false,
                    Error = "Solution path cannot be null or empty"
                };
            }

            try
            {
                // 验证解决方案路径是否存在
                if (!Directory.Exists(solutionPath))
                {
                    return new InitializationResult
                    {
                        Success = false,
                        Error = $"Solution path does not exist: {solutionPath}"
                    };
                }

                var rulesPath = Path.Combine(solutionPath, RulesDirectoryName);

                // 使用锁防止并发问题
                lock (_lockObject)
                {
                    // 检查目录是否已存在
                    if (!Directory.Exists(rulesPath))
                    {
                        // 创建目录
                        Directory.CreateDirectory(rulesPath);
                    }
                }

                return new InitializationResult
                {
                    Success = true,
                    RulesPath = rulesPath
                };
            }
            catch (UnauthorizedAccessException ex)
            {
                return new InitializationResult
                {
                    Success = false,
                    Error = $"Access denied: {ex.Message}"
                };
            }
            catch (IOException ex)
            {
                return new InitializationResult
                {
                    Success = false,
                    Error = $"IO error: {ex.Message}"
                };
            }
            catch (Exception ex)
            {
                return new InitializationResult
                {
                    Success = false,
                    Error = $"Failed to ensure rules directory: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// 创建初始规则文件
        /// </summary>
        public async Task<InitializationResult> CreateInitialFilesAsync(string rulesPath)
        {
            if (string.IsNullOrWhiteSpace(rulesPath))
            {
                return new InitializationResult
                {
                    Success = false,
                    Error = "Rules path cannot be null or empty"
                };
            }

            try
            {
                // 验证规则目录是否存在
                if (!Directory.Exists(rulesPath))
                {
                    return new InitializationResult
                    {
                        Success = false,
                        Error = $"Rules directory does not exist: {rulesPath}"
                    };
                }

                // 检测 C/C++ 项目并复制规范文件
                var projectDir = Path.GetDirectoryName(rulesPath) ?? rulesPath;
                System.Diagnostics.Debug.WriteLine($"[AICA] Checking if C++ project: {projectDir}");
                var isCpp = IsCppProject(projectDir);
                System.Diagnostics.Debug.WriteLine($"[AICA] IsCppProject result: {isCpp}");
                if (isCpp)
                {
                    foreach (var (fileName, fileContent) in CppRuleTemplates.GetAll())
                    {
                        var filePath = Path.Combine(rulesPath, fileName);
                        if (!File.Exists(filePath))
                        {
                            await WriteFileAsync(filePath, fileContent);
                            System.Diagnostics.Debug.WriteLine($"[AICA] Created C++ rule: {fileName}");
                        }
                    }
                }

                return new InitializationResult
                {
                    Success = true,
                    RulesPath = rulesPath
                };
            }
            catch (UnauthorizedAccessException ex)
            {
                return new InitializationResult
                {
                    Success = false,
                    Error = $"Access denied: {ex.Message}"
                };
            }
            catch (IOException ex)
            {
                return new InitializationResult
                {
                    Success = false,
                    Error = $"IO error: {ex.Message}"
                };
            }
            catch (Exception ex)
            {
                return new InitializationResult
                {
                    Success = false,
                    Error = $"Failed to create initial files: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// 生成通用规则文件内容
        /// </summary>
        /// <summary>
        /// Write content to a file asynchronously (.NET Standard 2.0 compatible).
        /// </summary>
        private static async Task WriteFileAsync(string filePath, string content)
        {
            using (var fileStream = File.Create(filePath))
            using (var writer = new StreamWriter(fileStream, System.Text.Encoding.UTF8))
            {
                await writer.WriteAsync(content);
            }
        }

        /// <summary>
        /// Detect if the project directory contains C/C++ source files.
        /// Uses a quick scan (first 200 files) to avoid slow enumeration on large projects.
        /// </summary>
        private static bool IsCppProject(string projectDir)
        {
            try
            {
                var cppExtensions = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
                {
                    ".cpp", ".c", ".h", ".cc", ".cxx", ".hpp", ".hxx"
                };

                int cppCount = 0;
                int totalCount = 0;
                const int scanLimit = 200;

                foreach (var file in Directory.EnumerateFiles(projectDir, "*.*", SearchOption.AllDirectories))
                {
                    var ext = Path.GetExtension(file);
                    if (string.IsNullOrEmpty(ext)) continue;

                    // Skip build/dependency directories
                    if (file.IndexOf(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                        file.IndexOf(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                        file.IndexOf(Path.DirectorySeparatorChar + "node_modules" + Path.DirectorySeparatorChar, System.StringComparison.OrdinalIgnoreCase) >= 0)
                        continue;

                    totalCount++;
                    if (cppExtensions.Contains(ext))
                        cppCount++;

                    if (totalCount >= scanLimit) break;
                }

                // Threshold 15%: large C++ projects have many config/build/doc files
                // (poco: 55/200 = 27.5%, but smaller projects may be lower)
                var result = totalCount > 0 && (double)cppCount / totalCount > 0.15;
                System.Diagnostics.Debug.WriteLine($"[AICA] IsCppProject scan: {cppCount}/{totalCount} cpp files, result={result}");
                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AICA] IsCppProject exception: {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        private static string GenerateGeneralRuleContent()
        {
            return @"---
priority: 5
enabled: true
---

# AICA 通用开发规则

这是一个通用的开发规则，适用于所有代码文件。

## 代码质量标准

- 编写清晰易读的代码
- 遵循 DRY（Don't Repeat Yourself）原则
- 保持函数简洁（< 50 行）
- 使用有意义的变量名

## 注释规范

- 为复杂逻辑添加注释
- 使用 XML 文档注释（C#）
- 避免过度注释

## 测试要求

- 新功能必须有单元测试
- 目标覆盖率 80%+
- 测试边界情况和异常路径

## 错误处理

- 显式处理所有异常
- 提供有意义的错误消息
- 记录错误信息用于调试
";
        }
    }

    /// <summary>
    /// 初始化结果
    /// </summary>
    public class InitializationResult
    {
        /// <summary>
        /// 操作是否成功
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 规则目录路径
        /// </summary>
        public string RulesPath { get; set; }

        /// <summary>
        /// 错误消息（如果失败）
        /// </summary>
        public string Error { get; set; }
    }
}
