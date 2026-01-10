// Copyright (c) 2025-2026 NexusContract. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using NexusContract.Abstractions.Policies;
using NexusContract.Abstractions.Security;

namespace NexusContract.Core.Reflection.Compilation
{
    /// <summary>
    /// 表达式树编译器抽象接口
    /// 
    /// 职责边界：
    /// - 元数据注册表（MetadataRegistry）：管理"存什么"
    /// - 表达式编译器（IExpressionCompiler）：管理"怎么编译"
    /// 
    /// 设计意图：
    /// 将 Expression Tree 构建逻辑从元数据注册表中剥离，实现关注点分离。
    /// 未来可支持多种编译策略（如 Source Generator、Roslyn CodeGen）。
    /// </summary>
    public interface IExpressionCompiler
    {
        /// <summary>
        /// 编译投影器（Contract → Dictionary）
        /// </summary>
        /// <param name="contractType">契约类型</param>
        /// <param name="auditResults">属性审计结果</param>
        /// <returns>投影委托，失败返回 null</returns>
        Func<object, INamingPolicy, IEncryptor?, Dictionary<string, object>>? CompileProjector(
            Type contractType,
            PropertyAuditResult[] auditResults);

        /// <summary>
        /// 编译回填器（Dictionary → Contract）
        /// </summary>
        /// <param name="contractType">契约类型</param>
        /// <param name="auditResults">属性审计结果</param>
        /// <returns>回填委托，失败返回 null</returns>
        Func<IDictionary<string, object>, INamingPolicy, IDecryptor?, object>? CompileHydrator(
            Type contractType,
            PropertyAuditResult[] auditResults);
    }
}
