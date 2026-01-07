// Copyright (c) 2025-2026 PubSoft (pubsoft@gmail.com). All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using PubSoft.NexusContract.Abstractions;
using PubSoft.NexusContract.Abstractions.Attributes;
using PubSoft.NexusContract.Abstractions.Configuration;
using PubSoft.NexusContract.Abstractions.Exceptions;
using PubSoft.NexusContract.Core.Utilities;

namespace PubSoft.NexusContract.Core.Reflection
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
            if (contractType == null) throw new ArgumentNullException(nameof(contractType));
            if (report == null) throw new ArgumentNullException(nameof(report));

            var contractName = contractType.Name;

            // 1. NXC101: 验证 Operation 意图声明
            var opAttr = contractType.GetCustomAttribute<ApiOperationAttribute>();
            if (opAttr == null)
            {
                report.Add(ContractDiagnostic.Create(contractName, "NXC101", contextArgs: contractName));
                // 如果没有 Operation 标注，后续验证失去基准，直接跳过该契约
                return;
            }

            // 2. NXC102: Operation 标识不能为空
            if (string.IsNullOrWhiteSpace(opAttr.Operation))
            {
                report.Add(ContractDiagnostic.Create(contractName, "NXC102", contextArgs: contractName));
            }

            // 3. NXC103: 验证 OneWay 语义闭环
            var responseType = GetResponseType(contractType);
            if (opAttr.Interaction == InteractionKind.OneWay && responseType != typeof(EmptyResponse))
            {
                report.Add(ContractDiagnostic.Create(contractName, "NXC103", 
                    contextArgs: new object[] { opAttr.Operation, responseType?.Name ?? "null" }));
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
            if (contractType == null)
                throw new ArgumentNullException(nameof(contractType));

            string contractName = contractType.FullName ?? contractType.Name ?? "Unknown";

            // NXC101: 验证 Operation 意图声明
            var opAttr = contractType.GetCustomAttribute<ApiOperationAttribute>();
            if (opAttr == null)
                throw new ContractIncompleteException(
                    contractName,
                    ContractDiagnosticRegistry.NXC101,
                    contractType.Name ?? "Unknown"
                );

            // NXC102: Operation 标识不能为空
            if (string.IsNullOrWhiteSpace(opAttr.Operation))
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
                    opAttr.Operation,
                    responseType?.Name ?? "null"
                );

            // 递归验证字段与深度
            ValidateFieldsRecursiveFailFast(contractType, 1, new HashSet<Type>(), "");
        }

        /// <summary>
        /// 递归验证字段和深度约束（诊断模式，带路径追踪）
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
                var fieldAttr = prop.GetCustomAttribute<ApiFieldAttribute>();
                if (fieldAttr == null) continue;

                string memberPath = string.IsNullOrEmpty(currentPath) ? prop.Name : $"{currentPath}.{prop.Name}";

                // NXC106: 安全红线：加密字段必须锁定路径
                if (fieldAttr.IsEncrypted && string.IsNullOrEmpty(fieldAttr.Name))
                {
                    report.Add(ContractDiagnostic.Create(rootContractName, "NXC106", 
                        propertyName: prop.Name,
                        propertyPath: memberPath,
                        contextArgs: new object[] { type.Name, prop.Name }));
                }

                Type propType = GetEffectiveType(prop.PropertyType);

                // 跳过基元类型
                if (propType.IsPrimitive || propType == typeof(string) || typeof(IDictionary).IsAssignableFrom(propType))
                    continue;

                // NXC107: 嵌套对象路径锁定检查
                if (TypeUtilities.IsComplexType(propType))
                {
                    if (currentDepth > 1 && string.IsNullOrEmpty(fieldAttr.Name))
                    {
                        report.Add(ContractDiagnostic.Create(rootContractName, "NXC107", 
                            propertyName: prop.Name,
                            propertyPath: memberPath,
                            contextArgs: new object[] { type.Name, prop.Name, currentDepth }));
                    }

                    // 继续递归探测（传递当前路径）
                    var childVisited = new HashSet<Type>(visited);
                    ValidateFieldsRecursive(propType, currentDepth + 1, childVisited, memberPath, report, rootContractName);
                }
            }
        }

        /// <summary>
        /// Fail-Fast 递归验证（用于运行时懒加载）
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
                var fieldAttr = prop.GetCustomAttribute<ApiFieldAttribute>();
                if (fieldAttr == null) continue;

                // NXC106: 安全红线：加密字段必须锁定路径
                if (fieldAttr.IsEncrypted && string.IsNullOrEmpty(fieldAttr.Name))
                    throw new ContractIncompleteException(
                        typeName,
                        ContractDiagnosticRegistry.NXC106,
                        type.Name ?? "Unknown",
                        prop.Name ?? "Unknown"
                    );

                Type propType = GetEffectiveType(prop.PropertyType);

                // 跳过基元类型和字符串
                if (propType.IsPrimitive || propType == typeof(string))
                    continue;

                // 跳过 IDictionary（直接透传）
                if (typeof(System.Collections.IDictionary).IsAssignableFrom(propType))
                    continue;

                // NXC107: 复杂对象检查
                if (TypeUtilities.IsComplexType(propType))
                {
                    if (currentDepth > 1 && string.IsNullOrEmpty(fieldAttr.Name))
                        throw new ContractIncompleteException(
                            typeName,
                            ContractDiagnosticRegistry.NXC107,
                            type.Name ?? "Unknown",
                            prop.Name ?? "Unknown",
                            currentDepth
                        );

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
