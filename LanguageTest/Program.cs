// 测试语言切换功能
using System.Globalization;
using NexusContract.Core.Reflection;

var report = new DiagnosticReport();

// 添加一些测试诊断
report.Add(new ContractDiagnostic(
    "TestContract",
    "NXC101",
    "测试契约",
    DiagnosticSeverity.Error,
    "TestProperty",
    "TestProperty",
    "TestContract"
));

report.Add(new ContractDiagnostic(
    "AnotherContract",
    "NXC106",
    "另一个测试",
    DiagnosticSeverity.Critical,
    "EncryptedField",
    "EncryptedField",
    "AnotherContract", "EncryptedField"
));

Console.WriteLine("=== 中文报告 ===");
report.PrintChineseToConsole();

Console.WriteLine("\n=== 英文报告 ===");
report.PrintEnglishToConsole();
