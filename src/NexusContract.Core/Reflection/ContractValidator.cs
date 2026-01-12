// Copyright (c) 2025-2026 NexusContract. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NexusContract.Abstractions;
using NexusContract.Abstractions.Attributes;
using NexusContract.Abstractions.Configuration;
using NexusContract.Abstractions.Exceptions;
using NexusContract.Core.Utilities;

namespace NexusContract.Core.Reflection
{
    /// <summary>
    /// 【决策 A-307】全量诊断执法官 (Full-Scan Contract Validator)
    /// 
    /// 重构说明：
    /// 从 Fail-Fast 模式切换为 Reporting 模式。不再直接 throw，而是将所有违宪行为
    /// 记录到 DiagnosticReport 中，实现一次运行、全量体检。
    /// 
    /// 双重模式：
    /// - Validate(Type, DiagnosticReport) (体检医生模式): 扫描所有违宪行为并写入诊断报告，用于启动期 Preload。
    /// - ValidateFailFast(Type) (执法官模式): 遇到第一个违宪行为立即抛出 ContractIncompleteException，用于运行时动态加载。
    /// 
    /// 验证规则（1xx 静态结构）：
    /// 1. NXC101: 类必须标记 [ApiOperation]，Operation 不能为空
    /// 2. NXC102: Operation 标识不能为空
    /// 3. NXC103: OneWay 交互必须使用 EmptyResponse
    /// 4. NXC104: 嵌套深度不超过 3 层（防 StackOverflow，强制重构）
    /// 5. NXC105: 检测循环引用（防止无限递归）
    /// 6. NXC106: 加密字段必须显式锁定 Name（禁止 NamingPolicy 猜测）
    /// 7. NXC107: 嵌套对象必须显式路径锁定（第 2 层及以上需要 [ApiField] 的 Name）
    /// 
    /// 三级阶梯策略（约定优于配置 vs 显式主权）：
    /// ============================================
    /// 【第 1 层 - 约定】普通字段自动推导：
    ///   - 规则：无 [ApiField] 的字段 → 自动用 INamingPolicy 转换名称（如 OutTradeNo → out_trade_no）
    ///   - 减负：开发者无需为每个字段加注解，框架内化 80% 的工作
    ///   - 宪法：遵循 CoC（约定优于配置）原则
    /// 
    /// 【第 2 层 - 强制】特定条件必须显式标注：
    ///   - 【强制一】加密字段 ([Encrypt] = true) 必须有 [ApiField(Name = "...")] 
    ///     理由：加密意味着"历史属性"，属性重构会导致旧数据解密失败、三方对接崩坏 (宪法 011)
    ///   - 【强制二】复杂对象在第 2+ 层必须有 [ApiField(Name = "...")] 
    ///     理由：嵌套递归需要明确的路径边界，防止"反射黑洞"和深度爆炸 (宪法 001)
    ///   - 【强制三】非标命名字段（如 sys_auth_token_v2_tmp）必须显式映射
    ///     理由：宪法 009 协议主权，明确三方字段的意图
    /// 
    /// 【第 3 层 - 防御】启动期命名映射表：
    ///   - 生成全量的"约定推导映射"
    ///   - 防止不同 Provider 使用不同命名规范导致的歧义
    ///   - 宪法 012 诊断主权：任何冲突都应被明确检测和报告
    /// </summary>
    public static class ContractValidator
    {
        // 从全局边界配置引用（单一来源）
        private static readonly int MaxDepth = ContractBoundaries.MaxNestingDepth;

        /// <summary>
        /// 【决策 A-307】全量诊断模式：对契约进行深度扫描并收集所有诊断信息
        /// </summary>
        public static void Validate(Type contractType, DiagnosticReport report)
        {
            NexusGuard.EnsurePhysicalAddress(contractType);
            NexusGuard.EnsurePhysicalAddress(report);

            string contractName = contractType.Name;

            // 1. NXC101: 验证 Operation 意图声明
            var opAttr = contractType.GetCustomAttribute<ApiOperationAttribute>();
            if (opAttr == null)
            {
                report.Add(ContractDiagnostic.Create(contractName, "NXC101", contextArgs: contractName));
                // 如果没有 Operation 标注，后续验证失去基准，直接跳过该契约
                return;
            }

            // 2. NXC102: OperationId 标识不能为空
            if (string.IsNullOrWhiteSpace(opAttr.OperationId))
            {
                report.Add(ContractDiagnostic.Create(contractName, "NXC102", contextArgs: contractName));
            }

            // 3. NXC103: 验证 OneWay 语义闭环
            var responseType = GetResponseType(contractType);
            if (opAttr.Interaction == InteractionKind.OneWay && responseType != typeof(EmptyResponse))
            {
                report.Add(ContractDiagnostic.Create(contractName, "NXC103",
                    contextArgs: new object[] { opAttr.OperationId, responseType?.Name ?? "null" }));
            }

            // 4. 开启递归路径探测
            ValidateFieldsRecursive(contractType, 1, new HashSet<Type>(), "", report, contractName);
        }

        /// <summary>
        /// Fail-Fast 模式：对契约进行全量边界校验，遇到第一个错误立即抛异常
        /// 用于运行时动态加载场景（如 GetMetadata 懒加载）
        /// </summary>
        public static void ValidateFailFast(Type contractType)
        {
            NexusGuard.EnsurePhysicalAddress(contractType);

            string contractName = contractType.FullName ?? contractType.Name ?? "Unknown";

            // NXC101: 验证 Operation 意图声明
            var opAttr = contractType.GetCustomAttribute<ApiOperationAttribute>();
            if (opAttr == null)
                throw new ContractIncompleteException(
                    contractName,
                    ContractDiagnosticRegistry.NXC101,
                    contractType.Name ?? "Unknown"
                );

            // NXC102: OperationId 标识不能为空
            if (string.IsNullOrWhiteSpace(opAttr.OperationId))
                throw new ContractIncompleteException(
                    contractName,
                    ContractDiagnosticRegistry.NXC102,
                    contractType.Name ?? "Unknown"
                );

            // NXC103: 验证 OneWay 语义闭环
            var responseType = GetResponseType(contractType);
            if (opAttr.Interaction == InteractionKind.OneWay && responseType != typeof(EmptyResponse))
                throw new ContractIncompleteException(
                    contractName,
                    ContractDiagnosticRegistry.NXC103,
                    opAttr.OperationId,
                    responseType?.Name ?? "null"
                );

            // 递归验证字段与深度
            ValidateFieldsRecursiveFailFast(contractType, 1, new HashSet<Type>(), "");
        }

        /// <summary>
        /// 递归验证字段和深度约束（诊断模式，带路径追踪）
        /// 
        /// 三级阶梯策略（约定优于配置 vs 显式主权）：
        /// 1. 基础层（约定）：无 [ApiField] 的普通字段 → 自动推导（INamingPolicy），无需强制
        /// 2. 约束层（强制）：有 [ApiField] 但条件不满足：
        ///    - 加密字段必须显式锁定 Name（NXC106）
        ///    - 复杂对象在 2+ 层必须显式锁定 Name（NXC107）
        /// 3. 防御层（推导表）：启动时生成全量命名映射，防止 Provider 间歧义
        /// </summary>
        private static void ValidateFieldsRecursive(
            Type type,
            int currentDepth,
            HashSet<Type> visited,
            string currentPath,
            DiagnosticReport report,
            string rootContractName)
        {
            // NXC104: 深度红线校验
            if (currentDepth > MaxDepth)
            {
                report.Add(ContractDiagnostic.Create(rootContractName, "NXC104",
                    propertyPath: currentPath,
                    contextArgs: new object[] { MaxDepth, currentPath, type.Name }));
                return; // 触碰深度硬边界，停止该分支向下探测
            }

            // NXC105: 循环引用检测
            if (visited.Contains(type))
            {
                report.Add(ContractDiagnostic.Create(rootContractName, "NXC105",
                    propertyPath: currentPath,
                    contextArgs: new object[] { currentPath, type.Name }));
                return;
            }

            visited.Add(type);

            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var prop in properties)
            {
                Type propType = GetEffectiveType(prop.PropertyType);

                // 基元类型和字符串：无需 [ApiField]，自动推导
                if (propType.IsPrimitive || propType == typeof(string) || typeof(IDictionary).IsAssignableFrom(propType))
                    continue;

                // 复杂对象：需要特殊处理
                bool isComplexType = TypeUtilities.IsComplexType(propType);
                var fieldAttr = prop.GetCustomAttribute<ApiFieldAttribute>();

                string memberPath = string.IsNullOrEmpty(currentPath) ? prop.Name : $"{currentPath}.{prop.Name}";

                // 【强制一】加密字段必须显式锁定 Name（宪法 011 - 加密数据的"历史属性"）
                if (fieldAttr?.IsEncrypted == true && string.IsNullOrEmpty(fieldAttr.Name))
                {
                    report.Add(ContractDiagnostic.Create(rootContractName, "NXC106",
                        propertyName: prop.Name,
                        propertyPath: memberPath,
                        contextArgs: new object[] { type.Name, prop.Name }));
                }

                // 【强制二】复杂对象在第 2+ 层必须显式锁定 Name（宪法 001 - 递归边界）
                if (isComplexType && fieldAttr != null && currentDepth > 1 && string.IsNullOrEmpty(fieldAttr.Name))
                {
                    report.Add(ContractDiagnostic.Create(rootContractName, "NXC107",
                        propertyName: prop.Name,
                        propertyPath: memberPath,
                        contextArgs: new object[] { type.Name, prop.Name, currentDepth }));
                }

                // 只有复杂对象需要继续递归探测
                if (isComplexType)
                {
                    var childVisited = new HashSet<Type>(visited);
                    ValidateFieldsRecursive(propType, currentDepth + 1, childVisited, memberPath, report, rootContractName);
                }
            }
        }

        /// <summary>
        /// Fail-Fast 递归验证（用于运行时懒加载）
        /// 
        /// 同样遵循三级阶梯策略：只对加密字段和复杂对象进行强制检查
        /// </summary>
        private static void ValidateFieldsRecursiveFailFast(
            Type type,
            int currentDepth,
            HashSet<Type> visited,
            string path)
        {
            string typeName = type.FullName ?? type.Name ?? "Unknown";

            // NXC104: 深度红线校验
            if (currentDepth > MaxDepth)
            {
                string pathInfo = !string.IsNullOrEmpty(path) ? path : type.Name ?? "Unknown";
                throw new ContractIncompleteException(
                    typeName,
                    ContractDiagnosticRegistry.NXC104,
                    MaxDepth,
                    pathInfo,
                    type.Name ?? "Unknown"
                );
            }

            // NXC105: 循环引用检测
            if (visited.Contains(type))
            {
                string pathInfo = !string.IsNullOrEmpty(path) ? path : type.Name ?? "Unknown";
                throw new ContractIncompleteException(
                    typeName,
                    ContractDiagnosticRegistry.NXC105,
                    pathInfo,
                    type.Name ?? "Unknown"
                );
            }

            visited.Add(type);

            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var prop in properties)
            {
                Type propType = GetEffectiveType(prop.PropertyType);

                // 基元类型、字符串和字典：自动推导，无需 [ApiField]
                if (propType.IsPrimitive || propType == typeof(string))
                    continue;

                if (typeof(System.Collections.IDictionary).IsAssignableFrom(propType))
                    continue;

                // 只对复杂对象进行强制检查
                bool isComplexType = TypeUtilities.IsComplexType(propType);
                var fieldAttr = prop.GetCustomAttribute<ApiFieldAttribute>();

                // 【强制一】加密字段必须显式锁定 Name
                if (fieldAttr?.IsEncrypted == true && string.IsNullOrEmpty(fieldAttr.Name))
                    throw new ContractIncompleteException(
                        typeName,
                        ContractDiagnosticRegistry.NXC106,
                        type.Name ?? "Unknown",
                        prop.Name ?? "Unknown"
                    );

                // 【强制二】复杂对象在第 2+ 层必须显式锁定 Name
                if (isComplexType && fieldAttr != null && currentDepth > 1 && string.IsNullOrEmpty(fieldAttr.Name))
                    throw new ContractIncompleteException(
                        typeName,
                        ContractDiagnosticRegistry.NXC107,
                        type.Name ?? "Unknown",
                        prop.Name ?? "Unknown",
                        currentDepth
                    );

                // 继续递归（仅限复杂对象）
                if (isComplexType)
                {
                    var childVisited = new HashSet<Type>(visited);
                    string childPath = !string.IsNullOrEmpty(path)
                            ? $"{path} → {prop.Name}"
                            : $"{(type.Name ?? "Unknown")}.{prop.Name ?? "Unknown"}";
                    ValidateFieldsRecursiveFailFast(propType, currentDepth + 1, childVisited, childPath);
                }
            }
        }

        /// <summary>
        /// 获取有效的类型（处理 List{T} 等泛型）
        /// </summary>
        private static Type GetEffectiveType(Type type)
        {
            // 如果是 List<T> 或其他 IEnumerable<T>，提取 T
            if (type.IsGenericType && typeof(IEnumerable).IsAssignableFrom(type))
            {
                var args = type.GetGenericArguments();
                return args.Length > 0 ? args[0] : type;
            }
            return type;
        }

        /// <summary>
        /// 从 IApiRequest{T} 中提取响应类型 T
        /// </summary>
        private static Type? GetResponseType(Type type)
        {
            var iface = type.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType &&
                    i.GetGenericTypeDefinition().Name == "IApiRequest`1");
            return iface?.GetGenericArguments()[0];
        }
    }
}


