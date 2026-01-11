// Copyright (c) 2025-2026 NexusContract. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using NexusContract.Abstractions.Exceptions;
using NexusContract.Abstractions.Policies;
using NexusContract.Abstractions.Security;
using NexusContract.Core.Exceptions;
using NexusContract.Core.Reflection;

namespace NexusContract.Core.Diagnostics
{
    /// <summary>
    /// 契约启动健康检查服务
    /// 
    /// 设计目标：
    /// 1. 一次性扫描所有契约，收集全量问题
    /// 2. 最后统一抛出 ContractIncompleteException（Fail-Fast）
    /// 3. 避免"修一个跑一次"的低效循环
    /// 
    /// 使用方式：
    /// <code>
    /// // 方式1：从程序集扫描
    /// var report = ContractStartupHealthCheck.Run(typeof(MyRequest).Assembly);
    /// 
    /// // 方式2：指定类型列表
    /// var report = ContractStartupHealthCheck.Run(new[] { typeof(Request1), typeof(Request2) });
    /// 
    /// // 方式3：带配置参数
    /// var report = ContractStartupHealthCheck.Run(
    ///     assemblies: new[] { typeof(MyRequest).Assembly },
    ///     warmup: true,
    ///     throwOnError: true,
    ///     encryptor: myEncryptor
    /// );
    /// </code>
    /// </summary>
    public static class ContractStartupHealthCheck
    {
        /// <summary>
        /// 执行启动健康检查（从程序集扫描）
        /// </summary>
        /// <param name="assemblies">要扫描的程序集</param>
        /// <param name="warmup">是否预热投影器/水化器（推荐生产启用）</param>
        /// <param name="throwOnError">是否在发现错误时抛出异常（默认 true）</param>
        /// <param name="namingPolicy">命名策略（用于投影器）</param>
        /// <param name="encryptor">加密器（用于 warmup 测试）</param>
        /// <param name="decryptor">解密器（用于 warmup 测试）</param>
        /// <returns>诊断报告（包含所有问题）</returns>
        /// <exception cref="ContractIncompleteException">如果 throwOnError=true 且存在错误</exception>
        public static DiagnosticReport Run(
            Assembly[] assemblies,
            bool warmup = false,
            bool throwOnError = true,
            INamingPolicy? namingPolicy = null,
            IEncryptor? encryptor = null,
            IDecryptor? decryptor = null)
        {
            NexusGuard.EnsureMinCount(assemblies);

            // 1. 扫描所有契约类型
            var contractTypes = ScanContractTypes(assemblies);

            // 2. 执行健康检查
            return Run(contractTypes, warmup, throwOnError, namingPolicy, encryptor, decryptor);
        }

        /// <summary>
        /// 执行启动健康检查（指定类型列表）
        /// </summary>
        public static DiagnosticReport Run(
            IEnumerable<Type> contractTypes,
            bool warmup = false,
            bool throwOnError = true,
            INamingPolicy? namingPolicy = null,
            IEncryptor? encryptor = null,
            IDecryptor? decryptor = null)
        {
            NexusGuard.EnsurePhysicalAddress(contractTypes);

            var typeList = contractTypes.ToList();
            if (typeList.Count == 0)
            {
                // 空契约集合，返回空报告
                return new DiagnosticReport();
            }

            Console.WriteLine($"🔍 Starting contract health check for {typeList.Count} contracts...");
            Console.WriteLine();

            // 执行 Preload（已修改为 per-type report）
            var report = NexusContractMetadataRegistry.Instance.Preload(
                typeList,
                warmup,
                encryptor,
                decryptor);

            // 输出摘要
            Console.WriteLine(report.GenerateSummary(includeDetails: false));

            // 如果有错误且需要抛出异常
            if (throwOnError && report.HasErrors)
            {
                Console.WriteLine();
                Console.WriteLine("❌ Contract validation failed. See detailed report above.");
                Console.WriteLine("💡 Tip: Call report.PrintToConsole(includeDetails: true) for full details.");
                Console.WriteLine();

                throw new Exceptions.ContractIncompleteException(report);
            }

            return report;
        }

        /// <summary>
        /// 从程序集扫描所有契约类型（带 [ApiOperation] 特性的类）
        /// </summary>
        private static List<Type> ScanContractTypes(Assembly[] assemblies)
        {
            var contractTypes = new List<Type>();

            foreach (var assembly in assemblies)
            {
                try
                {
                    var types = assembly.GetTypes()
                        .Where(t => t.GetCustomAttribute<NexusContract.Abstractions.Attributes.ApiOperationAttribute>() != null)
                        .ToList();

                    contractTypes.AddRange(types);
                    Console.WriteLine($"  📦 Found {types.Count} contracts in {assembly.GetName().Name}");
                }
                catch (ReflectionTypeLoadException ex)
                {
                    // 处理加载失败的类型
                    Console.WriteLine($"  ⚠️  Warning: Failed to load some types from {assembly.GetName().Name}");
                    var loadedTypes = ex.Types.Where(t => t != null).ToList();
                    var contracts = loadedTypes
                        .Where(t => t!.GetCustomAttribute<NexusContract.Abstractions.Attributes.ApiOperationAttribute>() != null)
                        .ToList();
                    if (contracts.Any())
                    {
                        contractTypes.AddRange(contracts!);
                        Console.WriteLine($"  📦 Found {contracts.Count} contracts (partial load)");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ❌ Error scanning {assembly.GetName().Name}: {ex.Message}");
                }
            }

            return contractTypes;
        }

        /// <summary>
        /// 生成 JSON 格式的诊断报告（用于 CI/CD 集成）
        /// </summary>
        public static string GenerateJsonReport(
            DiagnosticReport report,
            string? appId = null,
            string? environment = null)
        {
            var meta = new
            {
                appId = appId ?? "Unknown",
                environment = environment ?? "Development",
                timestamp = DateTime.UtcNow.ToString("o"),
                frameworkVersion = $"NexusContract v{typeof(ContractStartupHealthCheck).Assembly.GetName().Version}"
            };

            var summary = new
            {
                status = report.HasErrors ? "Failed" : (report.Diagnostics.Any() ? "Warning" : "Passed"),
                totalContractsScanned = report.SuccessCount + report.FailedCount,
                totalErrors = report.Diagnostics.Count(d => d.Severity >= DiagnosticSeverity.Error),
                blockerCount = report.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Critical),
                warningCount = report.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning)
            };

            var diagnosticsByContract = report.Diagnostics
                .GroupBy(d => d.ContractName)
                .Select(g => new
                {
                    contractType = g.Key,
                    failures = g.Select(d => new
                    {
                        severity = d.Severity.ToString(),
                        errorCode = d.ErrorCode,
                        message = d.Message.Split('\n')[0], // 只取第一行
                        location = !string.IsNullOrEmpty(d.PropertyPath) ? d.PropertyPath : d.PropertyName,
                        details = new
                        {
                            fullMessage = d.Message,
                            contextArgs = d.ContextArgs
                        }
                    }).ToList()
                })
                .ToList();

            var jsonReport = new
            {
                schema = "http://nexuscontract.pubsoft/schemas/startup-report.json",
                meta,
                summary,
                diagnostics = diagnosticsByContract
            };

            return System.Text.Json.JsonSerializer.Serialize(jsonReport, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
        }
    }
}
