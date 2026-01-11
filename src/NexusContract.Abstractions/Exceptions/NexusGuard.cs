// Copyright (c) 2025-2026 NexusContract. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace NexusContract.Abstractions.Exceptions;

/// <summary>
/// 物理寻址卫哨：强制执行宪法 002 和 003。
/// 
/// 设计原则（防腐政策）：
/// 1. [原位爆炸]：发现参数缺失立即抛出 NXC201，不准汇总或延迟
/// 2. [零分配]：禁止 new 任何对象，确保热路径无GC压力（宪法 007）
/// 3. [中央守门]：所有物理寻址失败都必须通过此卫哨（宪法 012）
/// 
/// 反对：复杂的 Fluent API、GuardContext、运行时正则匹配等八股文代码
/// 赞成：极简、直接、可内联的原始方法
/// 
/// === Phase 1A 扩展（2026-01-11）===
/// 新增多种验证场景支持：
/// - 非空验证（字符串、对象）
/// - 非空白字符串验证
/// - 集合最小元素数验证
/// - URI 绝对路径验证
/// - 字节长度/大小验证
/// - Base64 格式验证
/// 
/// 所有过载均使用统一的参数名验证，抛出 NXC201 或特定异常类型。
/// </summary>
public static class NexusGuard
{
    /// <summary>
    /// 确保参数非空（对象）。
    /// 
    /// 替换 `throw new ArgumentNullException(nameof(param))`。
    /// 使用 [CallerArgumentExpression] 自动捕获参数名，无需手动 `nameof(...)`。
    /// 
    /// 使用场景：
    /// - 构造函数参数验证
    /// - 方法入口参数验证
    /// - 属性初始化验证
    /// 
    /// 示例：
    /// ```csharp
    /// public SomeClass(IService service)
    /// {
    ///     NexusGuard.EnsurePhysicalAddress(service);  // 参数名自动捕获为 "service"
    ///     _service = service;
    /// }
    /// ```
    /// </summary>
    /// <param name="value">待验证的值</param>
    /// <param name="paramName">参数名称（由编译器通过 CallerArgumentExpression 自动填充）</param>
    /// <param name="caller">调用者身份（可选，用于诊断上下文）</param>
    /// <exception cref="ContractIncompleteException">NXC201 - 物理参数缺失</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void EnsurePhysicalAddress(
        object? value,
        [CallerArgumentExpression("value")] string paramName = "",
        string? caller = null)
    {
        if (value == null)
        {
            ThrowPhysicalAddressDeficit(paramName, caller);
        }
    }

    /// <summary>
    /// 确保字符串非空且非空白。
    /// 
    /// 替换 `throw new ArgumentException("...cannot be null or empty...")`。
    /// 
    /// 使用场景：
    /// - 字符串配置参数验证
    /// - 字符串标识符验证
    /// - API 密钥、Token 等敏感字符串验证
    /// </summary>
    /// <param name="value">待验证的字符串</param>
    /// <param name="paramName">参数名称（由编译器自动填充）</param>
    /// <param name="caller">调用者身份（可选）</param>
    /// <exception cref="ContractIncompleteException">NXC201 - 物理参数缺失</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void EnsureNonEmptyString(
        string? value,
        [CallerArgumentExpression("value")] string paramName = "",
        string? caller = null)
    {
        // .NET Standard 2.0 最稳定的写法，避免依赖 Linq 或扩展方法
        if (string.IsNullOrEmpty(value) || (value != null && value.Trim().Length == 0))
        {
            ThrowPhysicalAddressDeficit(paramName, caller);
        }
    }

    /// <summary>
    /// 确保集合非空且至少包含指定数量的元素。
    /// 
    /// 替换 `throw new ArgumentException("Must provide at least X items...")`。
    /// 
    /// 使用场景：
    /// - 程序集集合验证
    /// - 网关列表验证
    /// - 任何需要最小集合大小的场景
    /// </summary>
    /// <param name="collection">集合对象</param>
    /// <param name="minCount">最小元素数（默认 1）</param>
    /// <param name="paramName">参数名称（由编译器自动填充）</param>
    /// <param name="caller">调用者身份（可选）</param>
    /// <exception cref="ContractIncompleteException">NXC201 - 物理参数缺失</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void EnsureMinCount<T>(
        ICollection<T>? collection,
        int minCount = 1,
        [CallerArgumentExpression("collection")] string paramName = "",
        string? caller = null)
    {
        if (collection == null || collection.Count < minCount)
        {
            ThrowPhysicalAddressDeficit(paramName, caller, $"expected at least {minCount} item(s)");
        }
    }

    /// <summary>
    /// 确保 URI 是绝对路径。
    /// 
    /// 替换 `throw new ArgumentException("...must be an absolute URI...")`。
    /// 
    /// 使用场景：
    /// - 网关 URL 验证
    /// - HTTP 端点验证
    /// </summary>
    /// <param name="uri">待验证的 URI</param>
    /// <param name="paramName">参数名称（由编译器自动填充）</param>
    /// <param name="caller">调用者身份（可选）</param>
    /// <exception cref="ContractIncompleteException">NXC201 - 物理参数缺失</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void EnsureAbsoluteUri(
        Uri? uri,
        [CallerArgumentExpression("uri")] string paramName = "",
        string? caller = null)
    {
        if (uri == null || !uri.IsAbsoluteUri)
        {
            ThrowPhysicalAddressDeficit(paramName, caller, "must be an absolute URI");
        }
    }

    /// <summary>
    /// 确保字节数据长度符合预期。
    /// 
    /// 替换 `throw new ArgumentException("Master key must be 32 bytes...")`。
    /// 
    /// 使用场景：
    /// - 加密密钥长度验证（256-bit = 32 bytes）
    /// - 哈希值长度验证
    /// - 任何字节长度约束
    /// </summary>
    /// <param name="data">待验证的字节数据</param>
    /// <param name="expectedLength">期望长度（精确匹配）</param>
    /// <param name="paramName">参数名称（由编译器自动填充）</param>
    /// <param name="caller">调用者身份（可选）</param>
    /// <exception cref="ContractIncompleteException">NXC201 - 物理参数缺失</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void EnsureByteLength(
        byte[]? data,
        int expectedLength,
        [CallerArgumentExpression("data")] string paramName = "",
        string? caller = null)
    {
        if (data == null || data.Length != expectedLength)
        {
            ThrowPhysicalAddressDeficit(paramName, caller, $"must be exactly {expectedLength} bytes");
        }
    }

    /// <summary>
    /// 确保字符串是有效的 Base64 格式。
    /// 
    /// 替换 `throw new ArgumentException("...must be a valid Base64 string...")`。
    /// 
    /// 注意：在 .NET Standard 2.0 中，我们使用 try-catch 包装 Convert.FromBase64String。
    /// 此验证被设计为"冷路径"（Cold Path），不影响主流程性能。
    /// </summary>
    /// <param name="base64String">待验证的 Base64 字符串</param>
    /// <param name="paramName">参数名称（由编译器自动填充）</param>
    /// <param name="caller">调用者身份（可选）</param>
    /// <exception cref="ContractIncompleteException">NXC201 - 物理参数缺失或格式无效</exception>
    public static void EnsureValidBase64(
        string? base64String,
        [CallerArgumentExpression("base64String")] string paramName = "",
        string? caller = null)
    {
        // 第一步：检查非空
        if (string.IsNullOrWhiteSpace(base64String))
        {
            ThrowPhysicalAddressDeficit(paramName, caller);
        }

        // 第二步：验证 Base64 格式（冷路径，try-catch 不内联）
        // 注意：此路径分离出去，主流程（真正的参数使用）不会因 try-catch 而被阻止内联
        // base64String 已经过非空检查，可安全传入
        VerifyBase64Format(base64String!, paramName, caller);
    }

    /// <summary>
    /// 确保字典包含指定的键。
    /// 
    /// 替换 `throw new KeyNotFoundException(...)`。
    /// 
    /// 使用场景：
    /// - 网关字典查找验证
    /// - 配置字典访问验证
    /// </summary>
    /// <param name="dictionary">字典对象</param>
    /// <param name="key">待查找的键</param>
    /// <param name="dictName">字典参数名称（由编译器自动填充）</param>
    /// <param name="caller">调用者身份（可选）</param>
    /// <exception cref="ContractIncompleteException">NXC203 - 物理寻址键缺失</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void EnsureDictionaryKey<TKey>(
        IDictionary<TKey, object>? dictionary,
        TKey? key,
        [CallerArgumentExpression("dictionary")] string dictName = "",
        string? caller = null)
        where TKey : notnull
    {
        if (dictionary == null || key == null || !dictionary.ContainsKey(key))
        {
            ThrowPhysicalAddressDeficit(dictName, caller, $"key not found: {key}");
        }
    }

    /// <summary>
    /// 确保物理寻址参数完整。[LEGACY - 保留向后兼容]
    /// 
    /// 工作流：
    /// - 检查 Provider 非空 → 抛出 NXC201
    /// - 检查 ProfileId 非空 → 抛出 NXC201
    /// - 全部通过 → 继续执行
    /// 
    /// 宪法约束：
    /// - 宪法 002（URL 资源寻址）：Provider 来自 URL 或 Header
    /// - 宪法 003（物理槽位隔离）：ProfileId 来自 URL 路由（绝对权威）
    /// - 宪法 012（结构化诊断）：缺失参数必须返回 NXC201
    /// </summary>
    /// <param name="provider">Provider 标识（null/empty 则抛异常）</param>
    /// <param name="profileId">档案标识（null/empty 则抛异常）</param>
    /// <param name="caller">调用者身份（用于诊断日志）</param>
    /// <exception cref="ContractIncompleteException">NXC201 - 物理寻址参数缺失</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void EnsurePhysicalAddress(string? provider, string? profileId, string caller)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            throw new ContractIncompleteException(
                caller,
                ContractDiagnosticRegistry.NXC201,
                "Physical Address Error: Provider is missing.");
        }

        if (string.IsNullOrWhiteSpace(profileId))
        {
            throw new ContractIncompleteException(
                caller,
                ContractDiagnosticRegistry.NXC201,
                "Physical Address Error: ProfileId is missing.");
        }
    }

    /// <summary>
    /// 投掷物理寻址缺失异常（内部辅助方法）。
    /// 
    /// 注意：此方法标记为 [DoesNotReturn]，编译器知道执行此方法后永不返回。
    /// 这使编译器能够优化调用点的"不可到达代码"分析。
    /// </summary>
    /// <exception cref="ContractIncompleteException">NXC201 - 物理参数缺失</exception>
    [DoesNotReturn]
    private static void ThrowPhysicalAddressDeficit(string paramName, string? caller = null, string? detail = null)
    {
        string message = $"Physical Address Error: Parameter '{paramName}' is missing or invalid.";
        if (detail != null)
        {
            message += $" ({detail})";
        }

        throw new ContractIncompleteException(
            caller ?? "NexusGuard",
            ContractDiagnosticRegistry.NXC201,
            message);
    }

    /// <summary>
    /// 验证 Base64 格式（冷路径）。
    /// 
    /// 此方法被分离出来，以确保主流程的内联优化不被 try-catch 阻碍。
    /// 在 .NET Standard 2.0 中，try-catch 会阻止方法被内联，因此我们将其隔离。
    /// 
    /// 设计原则（宪法 007 - 性能）：
    /// - 热路径（参数检查）：可内联，无 try-catch
    /// - 冷路径（格式验证）：包含 try-catch，但无需内联
    /// </summary>
    private static void VerifyBase64Format(string base64String, string paramName, string? caller)
    {
        try
        {
            Convert.FromBase64String(base64String);
        }
        catch (FormatException)
        {
            ThrowPhysicalAddressDeficit(paramName, caller, "must be a valid Base64 string");
        }
    }
}
