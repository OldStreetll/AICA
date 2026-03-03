using System;
using AICA.Core.Security;

namespace AICA.Tests
{
    /// <summary>
    /// Simple test program to verify AutoApproveManager functionality
    /// Run this as a console application to test the auto-approve logic
    /// </summary>
    class TestAutoApproveManager
    {
        static void Main(string[] args)
        {
            Console.WriteLine("=== AutoApproveManager Test Suite ===\n");

            int passed = 0;
            int failed = 0;

            // Test 1: Read operations auto-approval
            Console.WriteLine("Test 1: Read operations auto-approval (enabled)");
            var options1 = new AutoApproveOptions { AutoApproveFileRead = true };
            var manager1 = new AutoApproveManager(options1);

            if (manager1.ShouldAutoApprove("read_file", "test.txt") &&
                manager1.ShouldAutoApprove("list_dir", "/path") &&
                manager1.ShouldAutoApprove("grep_search", "pattern") &&
                manager1.ShouldAutoApprove("find_by_name", "*.cs"))
            {
                Console.WriteLine("✓ PASSED\n");
                passed++;
            }
            else
            {
                Console.WriteLine("✗ FAILED\n");
                failed++;
            }

            // Test 2: Read operations disabled
            Console.WriteLine("Test 2: Read operations auto-approval (disabled)");
            var options2 = new AutoApproveOptions { AutoApproveFileRead = false };
            var manager2 = new AutoApproveManager(options2);

            if (!manager2.ShouldAutoApprove("read_file", "test.txt") &&
                !manager2.ShouldAutoApprove("list_dir", "/path"))
            {
                Console.WriteLine("✓ PASSED\n");
                passed++;
            }
            else
            {
                Console.WriteLine("✗ FAILED\n");
                failed++;
            }

            // Test 3: File creation auto-approval
            Console.WriteLine("Test 3: File creation auto-approval (enabled)");
            var options3 = new AutoApproveOptions { AutoApproveFileCreate = true };
            var manager3 = new AutoApproveManager(options3);

            if (manager3.ShouldAutoApprove("Create File", "newfile.txt"))
            {
                Console.WriteLine("✓ PASSED\n");
                passed++;
            }
            else
            {
                Console.WriteLine("✗ FAILED\n");
                failed++;
            }

            // Test 4: File edit auto-approval
            Console.WriteLine("Test 4: File edit auto-approval (enabled)");
            var options4 = new AutoApproveOptions { AutoApproveFileEdit = true };
            var manager4 = new AutoApproveManager(options4);

            if (manager4.ShouldAutoApprove("Edit File", "existing.txt"))
            {
                Console.WriteLine("✓ PASSED\n");
                passed++;
            }
            else
            {
                Console.WriteLine("✗ FAILED\n");
                failed++;
            }

            // Test 5: Safe commands auto-approval
            Console.WriteLine("Test 5: Safe commands auto-approval (enabled)");
            var options5 = new AutoApproveOptions { AutoApproveSafeCommands = true };
            var manager5 = new AutoApproveManager(options5);

            if (manager5.ShouldAutoApprove("Run Command", "dotnet build") &&
                manager5.ShouldAutoApprove("Run Command", "npm install") &&
                manager5.ShouldAutoApprove("Run Command", "git status"))
            {
                Console.WriteLine("✓ PASSED\n");
                passed++;
            }
            else
            {
                Console.WriteLine("✗ FAILED\n");
                failed++;
            }

            // Test 6: Unsafe commands should not be auto-approved
            Console.WriteLine("Test 6: Unsafe commands should NOT be auto-approved");
            var options6 = new AutoApproveOptions { AutoApproveSafeCommands = true };
            var manager6 = new AutoApproveManager(options6);

            if (!manager6.ShouldAutoApprove("Run Command", "rm -rf /") &&
                !manager6.ShouldAutoApprove("Run Command", "format C:"))
            {
                Console.WriteLine("✓ PASSED\n");
                passed++;
            }
            else
            {
                Console.WriteLine("✗ FAILED\n");
                failed++;
            }

            // Test 7: Custom rule
            Console.WriteLine("Test 7: Custom rule");
            var options7 = new AutoApproveOptions();
            var manager7 = new AutoApproveManager(options7);
            manager7.AddRule(new AutoApproveRule
            {
                OperationType = "Custom Operation",
                Condition = (op, details) => details.Contains("test")
            });

            if (manager7.ShouldAutoApprove("Custom Operation", "test file") &&
                !manager7.ShouldAutoApprove("Custom Operation", "production file"))
            {
                Console.WriteLine("✓ PASSED\n");
                passed++;
            }
            else
            {
                Console.WriteLine("✗ FAILED\n");
                failed++;
            }

            // Test 8: File deletion should never be auto-approved by default
            Console.WriteLine("Test 8: File deletion should NOT be auto-approved");
            var options8 = new AutoApproveOptions { AutoApproveFileDelete = false };
            var manager8 = new AutoApproveManager(options8);

            if (!manager8.ShouldAutoApprove("Delete File", "somefile.txt"))
            {
                Console.WriteLine("✓ PASSED\n");
                passed++;
            }
            else
            {
                Console.WriteLine("✗ FAILED\n");
                failed++;
            }

            // Test 9: Case insensitive operation names
            Console.WriteLine("Test 9: Case insensitive operation names");
            var options9 = new AutoApproveOptions { AutoApproveFileRead = true };
            var manager9 = new AutoApproveManager(options9);

            if (manager9.ShouldAutoApprove("READ_FILE", "test.txt") &&
                manager9.ShouldAutoApprove("Read_File", "test.txt") &&
                manager9.ShouldAutoApprove("read_file", "test.txt"))
            {
                Console.WriteLine("✓ PASSED\n");
                passed++;
            }
            else
            {
                Console.WriteLine("✗ FAILED\n");
                failed++;
            }

            // Summary
            Console.WriteLine("=== Test Summary ===");
            Console.WriteLine($"Total: {passed + failed}");
            Console.WriteLine($"Passed: {passed}");
            Console.WriteLine($"Failed: {failed}");
            Console.WriteLine($"Success Rate: {(passed * 100.0 / (passed + failed)):F1}%");

            if (failed == 0)
            {
                Console.WriteLine("\n✓ All tests passed!");
            }
            else
            {
                Console.WriteLine($"\n✗ {failed} test(s) failed!");
            }
        }
    }
}
