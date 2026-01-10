// Copyright (c) 2025-2026 NexusContract. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using NexusContract.Abstractions.Policies;
using NexusContract.Abstractions.Security;

namespace NexusContract.Core.Reflection.Compilation
{
    /// <summary>
    /// 默认表达式编译器实现
    /// 
    /// 聚合投影器和回填器编译逻辑，提供统一的编译入口。
    /// 
    /// 架构优势：
    /// - 分离元数据管理和表达式编译职责
    /// - 便于未来扩展（如 Source Generator、Roslyn CodeGen）
    /// - 降低单文件复杂度，提高可维护性
    /// </summary>
    internal sealed class ExpressionCompiler : IExpressionCompiler
    {
        /// <summary>
        /// 单例实例（编译器无状态，可全局共享）
        /// </summary>
        public static readonly ExpressionCompiler Instance = new ExpressionCompiler();

        private ExpressionCompiler() { }

        /// <summary>
        /// 编译投影器（Contract → Dictionary）
        /// </summary>
        public Func<object, INamingPolicy, IEncryptor?, Dictionary<string, object>>? CompileProjector(
            Type contractType,
            PropertyAuditResult[] auditResults)
        {
            return ProjectionCompiler.Compile(contractType, auditResults);
        }

        /// <summary>
        /// 编译回填器（Dictionary → Contract）
        /// </summary>
        public Func<IDictionary<string, object>, INamingPolicy, IDecryptor?, object>? CompileHydrator(
            Type contractType,
            PropertyAuditResult[] auditResults)
        {
            return HydrationCompiler.Compile(contractType, auditResults);
        }
    }
}
