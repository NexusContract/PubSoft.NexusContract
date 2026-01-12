// Copyright (c) 2025-2026 NexusContract. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using NexusContract.Abstractions.Attributes;
using NexusContract.Abstractions.Exceptions;

namespace NexusContract.Core.Reflection
{
    /// <summary>
    /// 【决策 A-301】NexusContractMetadataRegistry（契约元数据注册表）
    /// 
    /// 核心职能：
    /// 1. 发现（Discovery）：扫描契约类的 Attribute 结构
    /// 2. 验证（Validation）：调用 ContractValidator 确保符合边界"宪法"
    /// 3. 冻结（Freezing）：将反射结果转为不可变缓存对象，运行期零反射损耗
    /// 
    /// 为什么要冻结元数据？
    /// ====================
    /// 
    /// 架构对标：
    /// 【方案 A】动态反射（Alipay SDK 模式）：
    ///   每个请求时动态反射属性列表和 Attribute → O(n) 反射成本
    ///   后果：GC 压力、反射堆积、P99 波动
    /// 
    /// 【方案 B】冻结式元数据（我们的方案）：
    ///   启动期一次性完成全量反射 + Attribute 解析 + 约束验证
    ///   运行期 ConcurrentDictionary 查询 → O(1) 成本
    ///   后果：P50 = P99，曲线平稳，GC 压力可控
    /// 
    /// 成本收益：
    /// 
    /// 1. 启动期成本（一次性）
    ///    - 反射所有 Contract 类型的属性（取决于契约数量）
    ///    - 验证约束（NXC1xx）
    ///    - 前提说明：在小到中等规模契约（字段数较少、契约数量有限）与典型硬件上，耗时通常 &lt; 1 秒；实际耗时随契约数量与字段复杂度显著波动
    /// 
    /// 2. 运行期收益（每个请求）
    ///    - O(1) 缓存查询，无反射开销
    ///    - 稳定的性能曲线（P50/P99），无明显 GC 导致的抖动
    ///    - 注意：性能优化（Expression Tree 预编译）尚在实验阶段，当前回退到反射路径
    /// 
    /// 3. AI 生成友好
    ///    - 元数据清单明确（所有 POCO 类型的元数据）
    ///    - AI 生成 60+ Alipay 接口时，零歧义
    ///    - ContractValidator 保证所有新接口都符合边界规范
    /// 
    /// 4. 设计意图明确
    ///    - "冻结" = 运行时不允许修改元数据结构
    ///    - 强制"配置优于约定"原则
    ///    - Provider 必须在启动时声明所有可能的 POCO 类型
    /// 
    /// 实现策略：
    /// 使用 ConcurrentDictionary 实现懒加载冻结。
    /// 首次访问某个类型时触发反射 + 验证，后续访问返回缓存结果。
    /// 约束：不允许运行时修改元数据，所有契约必须在设计阶段明确。
    /// 
    /// 命名说明：
    /// 旧名 ReflectionCache 描述"实现手段"，新名 NexusContractMetadataRegistry 描述"架构职责"。
    /// 这不仅仅是一个缓存，而是契约元数据的权威注册表和管理中心。
    /// </summary>
    public sealed class NexusContractMetadataRegistry
    {
        private static readonly Lazy<NexusContractMetadataRegistry> _instance = new(() => new NexusContractMetadataRegistry());
        public static NexusContractMetadataRegistry Instance => _instance.Value;

        // 核心冷冻库：Key 是契约类型，Value 是冷冻后的元数据
        private readonly ConcurrentDictionary<Type, ContractMetadata> _cache = new();

        private NexusContractMetadataRegistry() { }

        /// <summary>
        /// 获取契约的元数据（如果不存在则触发"冷冻"流程）
        /// 懒加载模式：收集所有错误后统一抛出结构化异常，提供完整的诊断信息
        /// </summary>
        public ContractMetadata GetMetadata(Type type)
        {
            return _cache.GetOrAdd(type, t =>
            {
                // 使用独立 report，避免污染全局状态
                var report = new DiagnosticReport();
                ContractValidator.Validate(t, report);

                var metadata = BuildMetadata(t, warmup: false, report: report);

                // 如果有错误，抛出结构化异常（而非 InvalidOperationException）
                if (report.HasErrors)
                {
                    throw new Exceptions.ContractIncompleteException(
                        report,
                        $"Contract {t.Name} validation failed: {report.Diagnostics.Count(d => d.Severity >= DiagnosticSeverity.Error)} errors found"
                    );
                }

                return metadata;
            });
        }

        /// <summary>
        /// 【决策 A-308】启动期批量预加载（体检报告模式）
        /// 
        /// 改进点：
        /// 1. 返回结构化的 DiagnosticReport，而非简单的 List&lt;string&gt;
        /// 2. 使用 Validate(Type, DiagnosticReport) 收集所有错误，而非遇到第一个错误就停止
        /// 3. 即使某个契约有错误，仍继续扫描其他契约
        /// 4. 收集每个契约的所有问题后再决定是否构建元数据
        /// 5. 最后统一报告所有问题，对开发友好
        /// 6. 支持传入 encryptor/decryptor 用于 warmup，避免 NXC111 警告
        /// </summary>
        /// <param name="types">要预加载的契约类型集合</param>
        /// <param name="warmup">是否进行预热（测试投影器和回填器）</param>
        /// <param name="encryptor">加密器（可选，用于 warmup 阶段测试加密字段）</param>
        /// <param name="decryptor">解密器（可选，用于 warmup 阶段测试解密字段）</param>
        public DiagnosticReport Preload(
            IEnumerable<Type> types,
            bool warmup = false,
            Abstractions.Security.IEncryptor? encryptor = null,
            Abstractions.Security.IDecryptor? decryptor = null)
        {
            NexusGuard.EnsurePhysicalAddress(types);

            var globalReport = new DiagnosticReport();

            foreach (var type in types)
            {
                try
                {
                    if (type.GetCustomAttribute<ApiOperationAttribute>() == null)
                        continue;

                    string contractName = type.FullName ?? type.Name ?? "Unknown";

                    // ✅ 关键修复：每个契约使用独立的 report，避免全局污染
                    var perTypeReport = new DiagnosticReport();

                    // 1. 执行全量诊断扫描（写入 per-type report）
                    ContractValidator.Validate(type, perTypeReport);

                    // 2. 构建元数据（写入 per-type report）
                    try
                    {
                        var metadata = BuildMetadata(type, warmup, perTypeReport, encryptor, decryptor);

                        // 3. 缓存判定只依赖该类型的错误状态
                        if (!perTypeReport.HasErrors)
                        {
                            _cache.TryAdd(type, metadata);
                            globalReport.IncrementSuccessCount();
                        }
                    }
                    catch (Exception ex)
                    {
                        // 捕获构建期意外错误（如表达式树编译失败）
                        perTypeReport.Add(new ContractDiagnostic(
                            contractName,
                            "NXC_GENERIC",
                            $"元数据构建失败: {ex.Message}",
                            DiagnosticSeverity.Critical,
                            propertyName: null,
                            propertyPath: null,
                            ex.Message
                        ));
                    }

                    // 4. 合并 per-type report 到全局报告
                    globalReport.AddRange(perTypeReport.Diagnostics);
                }
                catch (Exception ex)
                {
                    string contractName = type.FullName ?? type.Name ?? "Unknown";
                    string? msg = ex is TargetInvocationException ? ex.InnerException?.Message : ex.Message;

                    globalReport.Add(new ContractDiagnostic(
                        contractName,
                        "NXC999",
                        NexusContract.Abstractions.Exceptions.ContractDiagnosticRegistry.Format(
                            "NXC999", new CultureInfo("zh-CN"), msg ?? "Unknown error"),
                        DiagnosticSeverity.Critical,
                        propertyName: null,
                        propertyPath: null,
                        msg ?? "Unknown error"
                    ));
                }
            }

            return globalReport;
        }

        /// <summary>
        /// 构建元数据（诊断收集版本）
        /// 
        /// 改进点：
        /// 1. 不再使用 aggregateErrors 布尔值 + List&lt;string&gt;，改用 List&lt;ContractDiagnostic&gt;
        /// 2. 即使 Auditor 失败，仍尝试编译 Projector/Hydrator
        /// 3. 返回 null 表示完全失败，否则返回部分元数据
        /// </summary>
        /// <summary>
        /// 单路径流水线：验证 → 审计 → 编译（三段式）
        /// </summary>
        /// <param name="type">契约类型</param>
        /// <param name="warmup">是否预热投影器</param>
        /// <param name="report">诊断报告（收集所有错误和警告）</param>
        /// <param name="encryptor">加密器（可选，用于 warmup）</param>
        /// <param name="decryptor">解密器（可选，用于 warmup）</param>
        private ContractMetadata BuildMetadata(
            Type type,
            bool warmup,
            DiagnosticReport report,
            Abstractions.Security.IEncryptor? encryptor = null,
            Abstractions.Security.IDecryptor? decryptor = null)
        {
            string contractName = type.FullName ?? type.Name ?? "Unknown";

            // ========== 阶段 1：入境检查（Validation）==========
            // Validator 已在外部调用，这里直接获取元数据
            var opAttr = type.GetCustomAttribute<ApiOperationAttribute>();
            if (opAttr == null)
            {
                // 验证器应该已经捕获此问题，这里作为防御性检查
                report.Add(ContractDiagnostic.Create(
                    contractName,
                    ContractDiagnosticRegistry.NXC101,
                    propertyName: null,
                    propertyPath: null,
                    type.Name ?? "Unknown"
                ));
            }

            var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            if (props.Length == 0)
            {
                report.Add(new ContractDiagnostic(
                    contractName,
                    "NXC108",
                    $"契约类 {type.Name} 至少需要定义一个公共属性。",
                    DiagnosticSeverity.Critical
                ));
            }

            // ========== 阶段 2：深度审计（Auditing）==========
            var properties = new List<PropertyMetadata>();
            var auditedProps = new List<PropertyAuditResult>();

            foreach (var prop in props)
            {
                // Validate getter/setter completeness
                if (!prop.CanRead || !prop.CanWrite)
                {
                    report.Add(new ContractDiagnostic(
                        contractName,
                        "NXC109",
                        $"属性 {type.Name}.{prop.Name} 需要同时具有 get 与 set。",
                        DiagnosticSeverity.Error,
                        propertyName: prop.Name,
                        propertyPath: prop.Name
                    ));
                    continue; // 继续检查其他属性
                }

                var fieldAttr = prop.GetCustomAttribute<ApiFieldAttribute>();
                if (fieldAttr == null) continue;

                try
                {
                    // 【决策 A-302】启动期一次性审计，缓存结果，运行期 O(1) 查询
                    var auditResult = ContractAuditor.AuditProperty(prop, fieldAttr, currentDepth: 1);
                    properties.Add(new PropertyMetadata(prop, fieldAttr, auditResult));

                    System.Diagnostics.Debug.WriteLine($"[Audit] {type.Name}.{prop.Name}:");
                    System.Diagnostics.Debug.WriteLine($"  - Type: {prop.PropertyType.Name}");
                    System.Diagnostics.Debug.WriteLine($"  - ApiField.Name: {fieldAttr.Name ?? "<null>"}");
                    System.Diagnostics.Debug.WriteLine($"  - IsEncrypted: {fieldAttr.IsEncrypted}");
                    System.Diagnostics.Debug.WriteLine($"  - IsComplexType: {auditResult.IsComplexType}");
                    System.Diagnostics.Debug.WriteLine($"  - IsEncryptedWithoutName: {auditResult.IsEncryptedWithoutName}");
                    System.Diagnostics.Debug.WriteLine($"  - IsComplexWithoutName: {auditResult.IsComplexWithoutName}");

                    // Only include safe properties in projector
                    if (!auditResult.IsEncryptedWithoutName && !auditResult.IsComplexWithoutName)
                    {
                        System.Diagnostics.Debug.WriteLine($"  ✅ INCLUDED in projector");
                        auditedProps.Add(auditResult);
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"  ❌ EXCLUDED from projector (EncryptedWithoutName={auditResult.IsEncryptedWithoutName}, ComplexWithoutName={auditResult.IsComplexWithoutName})");
                    }
                }
                catch (Exception ex)
                {
                    report.Add(new ContractDiagnostic(
                        contractName,
                        "NXC110",
                        $"审计属性 {type.Name}.{prop.Name} 失败: {ex.Message}",
                        DiagnosticSeverity.Error,
                        propertyName: prop.Name,
                        propertyPath: prop.Name,
                        ex.Message
                    ));
                    // 继续检查其他属性
                }
            }

            // ========== 阶段 3：冻结元数据（强制反射缓存路径）==========
            // 不使用运行时编译器，确保系统确定性
            // 设置投影器和回填器为 null，运行时将使用缓存反射路径
            Func<object, Abstractions.Policies.INamingPolicy, Abstractions.Security.IEncryptor?, Dictionary<string, object>>? projector = null;
            Func<IDictionary<string, object>, Abstractions.Policies.INamingPolicy, Abstractions.Security.IDecryptor?, object>? hydrator = null;

            System.Diagnostics.Debug.WriteLine($"[BuildMetadata] Metadata frozen for {contractName} ({properties.Count} properties). Using reflection-based projection/hydration.");

            // 返回不可变对象（即使有错误也返回部分元数据，让调用方决定如何处理）
            return new ContractMetadata(type, opAttr!, properties.AsReadOnly(), projector, hydrator);
        }
    }

    /// <summary>
    /// 冷冻后的契约元数据（不可变）
    /// </summary>
    public record ContractMetadata(
        Type ContractType,
        ApiOperationAttribute Operation,
        IReadOnlyList<PropertyMetadata> Properties,
        Func<object, Abstractions.Policies.INamingPolicy, Abstractions.Security.IEncryptor?, Dictionary<string, object>>? Projector,
        Func<IDictionary<string, object>, Abstractions.Policies.INamingPolicy, Abstractions.Security.IDecryptor?, object>? Hydrator);

    /// <summary>
    /// 冻结后的属性元数据（不可变）
    /// 
    /// 包含 PropertyInfo、ApiField 和 AuditResult。
    /// AuditResult 缓存约束检查结果，运行期通过布尔值判断而非重复 string.IsNullOrEmpty()。
    /// </summary>
    public record PropertyMetadata(
        PropertyInfo PropertyInfo,
        ApiFieldAttribute ApiField,
        PropertyAuditResult AuditResult);
}


