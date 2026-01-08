// Copyright (c) 2025-2026 NexusContract. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NexusContract.Core.Reflection
{
    /// <summary>
    /// 【决策 A-306】契约体检报告
    /// </summary>
    public sealed class DiagnosticReport
    {
        private readonly List<ContractDiagnostic> _diagnostics = new();

        public IReadOnlyList<ContractDiagnostic> Diagnostics => _diagnostics.AsReadOnly();
        public bool HasErrors => _diagnostics.Any(d => d.Severity >= DiagnosticSeverity.Error);
        public bool HasCriticalErrors => _diagnostics.Any(d => d.Severity == DiagnosticSeverity.Critical);
        public int SuccessCount { get; private set; }
        public int FailedCount => GetFailedContracts().Count();

        public void Add(ContractDiagnostic diagnostic)
        {
            if (diagnostic == null) throw new ArgumentNullException(nameof(diagnostic));
            _diagnostics.Add(diagnostic);
        }

        public void AddRange(IEnumerable<ContractDiagnostic> diagnostics)
        {
            if (diagnostics == null) throw new ArgumentNullException(nameof(diagnostics));
            _diagnostics.AddRange(diagnostics);
        }

        public void IncrementSuccessCount()
        {
            SuccessCount++;
        }

        public Dictionary<DiagnosticSeverity, int> GetSeverityStats()
        {
            return _diagnostics
                .GroupBy(d => d.Severity)
                .ToDictionary(g => g.Key, g => g.Count());
        }

        public Dictionary<string, int> GetErrorCodeStats()
        {
            return _diagnostics
                .GroupBy(d => d.ErrorCode)
                .OrderByDescending(g => g.Count())
                .ToDictionary(g => g.Key, g => g.Count());
        }

        public IEnumerable<string> GetFailedContracts()
        {
            return _diagnostics
                .Where(d => d.Severity >= DiagnosticSeverity.Error)
                .Select(d => d.ContractName)
                .Distinct()
                .OrderBy(name => name);
        }

        public string GenerateSummary(bool includeDetails = true)
        {
            var sb = new StringBuilder();
            sb.AppendLine("╔════════════════════════════════════════════════════════════════════════╗");
            sb.AppendLine("║            NexusContract 契约体检报告 (Diagnostic Report)             ║");
            sb.AppendLine("╚════════════════════════════════════════════════════════════════════════╝");
            sb.AppendLine();

            sb.AppendLine("📊 统计摘要 (Statistics):");
            sb.AppendLine($"  ✅ 成功缓存: {SuccessCount} 个契约");
            sb.AppendLine($"  ❌ 失败数量: {FailedCount} 个契约");
            sb.AppendLine();

            var severityStats = GetSeverityStats();
            if (severityStats.Any())
            {
                sb.AppendLine("🔍 严重度分布 (Severity Distribution):");
                foreach (var (severity, count) in severityStats.OrderByDescending(kv => kv.Key))
                {
                    string icon = severity switch
                    {
                        DiagnosticSeverity.Critical => "🔴",
                        DiagnosticSeverity.Error => "🟠",
                        DiagnosticSeverity.Warning => "🟡",
                        _ => "⚪"
                    };
                    sb.AppendLine($"  {icon} {severity,-10}: {count,3} 项");
                }
                sb.AppendLine();
            }

            var errorCodeStats = GetErrorCodeStats();
            if (errorCodeStats.Any())
            {
                sb.AppendLine("🏆 高频错误码 Top 5 (Top Error Codes):");
                foreach (var (errorCode, count) in errorCodeStats.Take(5))
                {
                    sb.AppendLine($"  [{errorCode}]: {count} 次");
                }
                sb.AppendLine();
            }

            if (includeDetails && _diagnostics.Any())
            {
                sb.AppendLine("📋 详细诊断 (Detailed Diagnostics):");
                sb.AppendLine(new string('─', 76));

                var groupedByContract = _diagnostics
                    .GroupBy(d => d.ContractName)
                    .OrderBy(g => g.Key);

                foreach (var contractGroup in groupedByContract)
                {
                    sb.AppendLine($"\n📦 契约: {contractGroup.Key}");
                    foreach (var diagnostic in contractGroup.OrderByDescending(d => d.Severity))
                    {
                        string icon = diagnostic.Severity switch
                        {
                            DiagnosticSeverity.Critical => "🔴",
                            DiagnosticSeverity.Error => "🟠",
                            DiagnosticSeverity.Warning => "🟡",
                            _ => "⚪"
                        };

                        string location = !string.IsNullOrEmpty(diagnostic.PropertyPath)
                            ? $" → {diagnostic.PropertyPath}"
                            : !string.IsNullOrEmpty(diagnostic.PropertyName)
                                ? $".{diagnostic.PropertyName}"
                                : "";

                        sb.AppendLine($"  {icon} [{diagnostic.ErrorCode}]{location}");
                        string message = diagnostic.Message.Split('\n')[0];
                        if (message.Length > 200)
                        {
                            message = message.Substring(0, 197) + "...";
                        }
                        sb.AppendLine($"     {message}");
                    }
                }
                sb.AppendLine();
            }

            if (HasCriticalErrors)
            {
                sb.AppendLine("⚠️  行动建议 (Action Required):");
                sb.AppendLine("   检测到致命错误 (Critical Errors)，必须修改代码后才能正常运行。");
                sb.AppendLine("   请根据上述诊断信息逐一修复，确保所有契约符合 NexusContract 边界规范。");
            }
            else if (HasErrors)
            {
                sb.AppendLine("⚠️  行动建议 (Action Suggested):");
                sb.AppendLine("   检测到错误 (Errors)，部分契约可能在运行时失败。");
                sb.AppendLine("   建议优先修复，以确保系统稳定性。");
            }
            else if (_diagnostics.Any())
            {
                sb.AppendLine("✅ 状态良好 (Good Status):");
                sb.AppendLine("   仅检测到警告 (Warnings)，不影响核心功能。");
                sb.AppendLine("   建议在后续迭代中优化。");
            }
            else
            {
                sb.AppendLine("✅ 完美！(Perfect!):");
                sb.AppendLine("   所有契约均符合 NexusContract 规范，零违宪。");
            }

            sb.AppendLine();
            sb.AppendLine("╚════════════════════════════════════════════════════════════════════════╝");

            return sb.ToString();
        }

        public void PrintToConsole(bool includeDetails = true)
        {
            Console.WriteLine(GenerateSummary(includeDetails));
        }
    }
}


