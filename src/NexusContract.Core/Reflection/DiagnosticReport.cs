// Copyright (c) 2025-2026 NexusContract. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using NexusContract.Abstractions.Exceptions;

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
            NexusGuard.EnsurePhysicalAddress(diagnostic);
            _diagnostics.Add(diagnostic);
        }

        public void AddRange(IEnumerable<ContractDiagnostic> diagnostics)
        {
            NexusGuard.EnsurePhysicalAddress(diagnostics);
            _diagnostics.AddRange(diagnostics);
        }

        /// <summary>
        /// 合并另一个报告到当前报告
        /// </summary>
        public void Merge(DiagnosticReport other)
        {
            NexusGuard.EnsurePhysicalAddress(other);
            _diagnostics.AddRange(other.Diagnostics);
            SuccessCount += other.SuccessCount;
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

        public string GenerateSummary(bool includeDetails = true, CultureInfo? culture = null)
        {
            // 确定目标文化：显式指定 > 当前 UI 文化 > 默认 zh-CN
            var targetCulture = culture ?? CultureInfo.CurrentUICulture;
            bool isChinese = targetCulture.Name.StartsWith("zh");

            var sb = new StringBuilder();
            sb.AppendLine("╔════════════════════════════════════════════════════════════════════════╗");
            if (isChinese)
            {
                sb.AppendLine("║            NexusContract 契约体检报告 (Diagnostic Report)             ║");
            }
            else
            {
                sb.AppendLine("║          NexusContract Contract Diagnostic Report                    ║");
            }
            sb.AppendLine("╚════════════════════════════════════════════════════════════════════════╝");
            sb.AppendLine();

            // 语言切换链接
            if (isChinese)
            {
                sb.AppendLine("🌐 Language / 语言: [English](en-US) | **中文**");
            }
            else
            {
                sb.AppendLine("🌐 Language: [中文](zh-CN) | **English**");
            }
            sb.AppendLine();

            if (isChinese)
            {
                sb.AppendLine("📊 统计摘要 (Statistics):");
                sb.AppendLine($"  ✅ 成功缓存: {SuccessCount} 个契约");
                sb.AppendLine($"  ❌ 失败数量: {FailedCount} 个契约");
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("📊 Statistics:");
                sb.AppendLine($"  ✅ Successful: {SuccessCount} contracts");
                sb.AppendLine($"  ❌ Failed: {FailedCount} contracts");
                sb.AppendLine();
            }

            var severityStats = GetSeverityStats();
            if (severityStats.Any())
            {
                if (isChinese)
                {
                    sb.AppendLine("🔍 严重度分布 (Severity Distribution):");
                }
                else
                {
                    sb.AppendLine("🔍 Severity Distribution:");
                }
                foreach (var (severity, count) in severityStats.OrderByDescending(kv => kv.Key))
                {
                    string icon = severity switch
                    {
                        DiagnosticSeverity.Critical => "🔴",
                        DiagnosticSeverity.Error => "🟠",
                        DiagnosticSeverity.Warning => "🟡",
                        _ => "⚪"
                    };
                    string severityText = isChinese ? GetSeverityTextZh(severity) : severity.ToString();
                    string unit = isChinese ? "项" : "items";
                    sb.AppendLine($"  {icon} {severityText,-10}: {count,3} {unit}");
                }
                sb.AppendLine();
            }

            var errorCodeStats = GetErrorCodeStats();
            if (errorCodeStats.Any())
            {
                if (isChinese)
                {
                    sb.AppendLine("🏆 高频错误码 Top 5 (Top Error Codes):");
                }
                else
                {
                    sb.AppendLine("🏆 Top Error Codes:");
                }
                foreach (var (errorCode, count) in errorCodeStats.Take(5))
                {
                    string timesText = isChinese ? "次" : "times";
                    sb.AppendLine($"  [{errorCode}]: {count} {timesText}");
                }
                sb.AppendLine();
            }

            if (includeDetails && _diagnostics.Any())
            {
                if (isChinese)
                {
                    sb.AppendLine("📋 详细诊断 (Detailed Diagnostics):");
                }
                else
                {
                    sb.AppendLine("📋 Detailed Diagnostics:");
                }
                sb.AppendLine(new string('─', 76));

                var groupedByContract = _diagnostics
                    .GroupBy(d => d.ContractName)
                    .OrderBy(g => g.Key);

                foreach (var contractGroup in groupedByContract)
                {
                    if (isChinese)
                    {
                        sb.AppendLine($"\n📦 契约: {contractGroup.Key}");
                    }
                    else
                    {
                        sb.AppendLine($"\n📦 Contract: {contractGroup.Key}");
                    }
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

                        // 使用 ContractDiagnosticRegistry.Format 生成本地化消息
                        string localizedMessage = NexusContract.Abstractions.Exceptions.ContractDiagnosticRegistry.Format(
                            diagnostic.ErrorCode, targetCulture, diagnostic.ContextArgs);
                        string firstLine = localizedMessage.Split('\n')[0];
                        if (firstLine.Length > 200)
                        {
                            firstLine = firstLine.Substring(0, 197) + "...";
                        }
                        sb.AppendLine($"     {firstLine}");
                    }
                }
                sb.AppendLine();
            }

            if (HasCriticalErrors)
            {
                if (isChinese)
                {
                    sb.AppendLine("⚠️  行动建议 (Action Required):");
                    sb.AppendLine("   检测到致命错误 (Critical Errors)，必须修改代码后才能正常运行。");
                    sb.AppendLine("   请根据上述诊断信息逐一修复，确保所有契约符合 NexusContract 边界规范。");
                }
                else
                {
                    sb.AppendLine("⚠️  Action Required:");
                    sb.AppendLine("   Critical errors detected, code modification required to run properly.");
                    sb.AppendLine("   Please fix all issues according to the diagnostic information above.");
                }
            }
            else if (HasErrors)
            {
                if (isChinese)
                {
                    sb.AppendLine("⚠️  行动建议 (Action Suggested):");
                    sb.AppendLine("   检测到错误 (Errors)，部分契约可能在运行时失败。");
                    sb.AppendLine("   建议优先修复，以确保系统稳定性。");
                }
                else
                {
                    sb.AppendLine("⚠️  Action Suggested:");
                    sb.AppendLine("   Errors detected, some contracts may fail at runtime.");
                    sb.AppendLine("   Recommended to fix for system stability.");
                }
            }
            else if (_diagnostics.Any())
            {
                if (isChinese)
                {
                    sb.AppendLine("✅ 状态良好 (Good Status):");
                    sb.AppendLine("   仅检测到警告 (Warnings)，不影响核心功能。");
                    sb.AppendLine("   建议在后续迭代中优化。");
                }
                else
                {
                    sb.AppendLine("✅ Good Status:");
                    sb.AppendLine("   Only warnings detected, core functionality unaffected.");
                    sb.AppendLine("   Consider optimization in future iterations.");
                }
            }
            else
            {
                if (isChinese)
                {
                    sb.AppendLine("✅ Passed:");
                    sb.AppendLine("   All contracts comply with NexusContract specifications (no violations detected).");
                }
                else
                {
                    sb.AppendLine("✅ Passed:");
                    sb.AppendLine("   All contracts comply with NexusContract specifications (no violations detected).");
                }
            }

            sb.AppendLine();
            sb.AppendLine("╚════════════════════════════════════════════════════════════════════════╝");

            return sb.ToString();
        }

        public void PrintToConsole(bool includeDetails = true, CultureInfo? culture = null)
        {
            Console.WriteLine(GenerateSummary(includeDetails, culture));
        }

        /// <summary>
        /// 生成中文诊断报告
        /// </summary>
        public string GenerateChineseSummary(bool includeDetails = true)
        {
            return GenerateSummary(includeDetails, new CultureInfo("zh-CN"));
        }

        /// <summary>
        /// 生成英文诊断报告
        /// </summary>
        public string GenerateEnglishSummary(bool includeDetails = true)
        {
            return GenerateSummary(includeDetails, new CultureInfo("en-US"));
        }

        /// <summary>
        /// 打印中文诊断报告到控制台
        /// </summary>
        public void PrintChineseToConsole(bool includeDetails = true)
        {
            PrintToConsole(includeDetails, new CultureInfo("zh-CN"));
        }

        /// <summary>
        /// 打印英文诊断报告到控制台
        /// </summary>
        public void PrintEnglishToConsole(bool includeDetails = true)
        {
            PrintToConsole(includeDetails, new CultureInfo("en-US"));
        }

        private static string GetSeverityTextZh(DiagnosticSeverity severity)
        {
            return severity switch
            {
                DiagnosticSeverity.Critical => "致命",
                DiagnosticSeverity.Error => "错误",
                DiagnosticSeverity.Warning => "警告",
                _ => "未知"
            };
        }
    }
}


