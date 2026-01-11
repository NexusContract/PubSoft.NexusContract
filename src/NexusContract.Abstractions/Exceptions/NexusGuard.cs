// Copyright (c) 2025-2026 NexusContract. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

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
/// </summary>
public static class NexusGuard
{
    /// <summary>
    /// 确保物理寻址参数完整。
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
}
