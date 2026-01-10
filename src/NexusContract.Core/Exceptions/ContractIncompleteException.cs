// Copyright (c) 2025-2026 NexusContract. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Linq;
using NexusContract.Core.Reflection;

namespace NexusContract.Core.Exceptions
{
    /// <summary>
    /// 启动期契约完整性异常（Fail-Fast + 结构化诊断）
    /// 
    /// 设计意图：
    /// - Fail-Fast：进程不启动/不对外服务
    /// - 但不等于"遇到第一个错误就停止"
    /// - 一次性携带全部契约问题，避免"修一个跑一次"
    /// 
    /// 使用场景：
    /// - 启动期 Preload 发现错误时抛出
    /// - 运行期懒加载发现错误时抛出（可选，取决于策略）
    /// </summary>
    public sealed class ContractIncompleteException : Exception
    {
        /// <summary>
        /// 完整的诊断报告（结构化，可机器解析）
        /// </summary>
        public DiagnosticReport Report { get; }

        /// <summary>
        /// 错误数量（Error + Critical）
        /// </summary>
        public int ErrorCount { get; }

        /// <summary>
        /// 致命错误数量（Critical）
        /// </summary>
        public int CriticalCount { get; }

        /// <summary>
        /// 失败的契约数量
        /// </summary>
        public int FailedContractCount { get; }

        /// <summary>
        /// 一行摘要（用于日志快速浏览）
        /// </summary>
        public string Summary { get; }

        public ContractIncompleteException(DiagnosticReport report)
            : base(GenerateMessage(report))
        {
            Report = report ?? throw new ArgumentNullException(nameof(report));
            ErrorCount = report.Diagnostics.Count(d => d.Severity >= DiagnosticSeverity.Error);
            CriticalCount = report.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Critical);
            FailedContractCount = report.FailedCount;
            Summary = $"Contract validation failed: {FailedContractCount} contracts, {ErrorCount} errors ({CriticalCount} critical)";
        }

        public ContractIncompleteException(DiagnosticReport report, string customMessage)
            : base(customMessage)
        {
            Report = report ?? throw new ArgumentNullException(nameof(report));
            ErrorCount = report.Diagnostics.Count(d => d.Severity >= DiagnosticSeverity.Error);
            CriticalCount = report.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Critical);
            FailedContractCount = report.FailedCount;
            Summary = customMessage;
        }

        private static string GenerateMessage(DiagnosticReport report)
        {
            if (report == null) return "Contract validation failed (no report available)";

            int errorCount = report.Diagnostics.Count(d => d.Severity >= DiagnosticSeverity.Error);
            int criticalCount = report.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Critical);
            int failedCount = report.FailedCount;

            string msg = $"❌ Contract validation failed:\n" +
                      $"  - Failed contracts: {failedCount}\n" +
                      $"  - Total errors: {errorCount} ({criticalCount} critical)\n\n";

            // 列出前 5 个失败的契约
            var failedContracts = report.GetFailedContracts().Take(5).ToList();
            if (failedContracts.Any())
            {
                msg += "Failed contracts:\n";
                foreach (string? contractName in failedContracts)
                {
                    var errors = report.Diagnostics
                        .Where(d => d.ContractName == contractName && d.Severity >= DiagnosticSeverity.Error)
                        .Take(2);
                    msg += $"  • {contractName}\n";
                    foreach (var error in errors)
                    {
                        msg += $"    - [{error.ErrorCode}] {error.Message.Split('\n')[0]}\n";
                    }
                }
                if (failedCount > 5)
                {
                    msg += $"  ... and {failedCount - 5} more contracts\n";
                }
            }

            msg += "\n💡 Run DiagnosticReport.PrintToConsole() to see full details.";
            return msg;
        }
    }
}
