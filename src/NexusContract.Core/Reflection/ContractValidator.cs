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
    /// 契约执法官（Contract Validator）
    /// 
    /// "最高法院"角色：在系统启动（冷冻元数据）时，像"X光"一样扫描每一个契约类，
    /// 任何违反边界的行为都会被瞬间卡死。它不负责业务逻辑，唯一职责是守护"没有魔法，只有边界"的共识。
    /// 
    /// 双重模式：
    /// - Validate() (执法官模式): 遇到第一个违宪行为立即抛出 ContractIncompleteException，用于运行时 Fail-Fast。
    /// - ValidateWithReport() (体检医生模式): 扫描所有违宪行为并生成一份完整的诊断报告，用于启动期 Preload。
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
        /// 对契约进行全量边界校验
        /// </summary>
        public static void Validate(Type contractType)
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

            // 3. 递归验证字段与深度
            ValidateFieldsRecursive(contractType, 1, new HashSet<Type>(), "");
        }

        /// <summary>
        /// 【决策 A-307】无损扫描模式：收集所有错误而不抛异常
        /// 
        /// 设计理念：
        /// Validate() 是"执法官"，遇到违宪立即逮捕（抛异常）。
        /// ValidateWithReport() 是"体检医生"，扫描完整个身体后才给出报告。
        /// 
        /// 使用场景：
        /// - Preload 启动期批量扫描：一次性发现所有契约的结构性问题
        /// - CI/CD 质量门禁：在构建管道中输出完整的诊断报告
        /// 
        /// 实现策略：
        /// 1. 将所有 throw 替换为 diagnostics.Add()
        /// 2. 即使某一步失败，仍继续后续检查
        /// 3. 兄弟节点独立检查（属性 A 失败不影响属性 B）
        /// </summary>
        public static List<ContractDiagnostic> ValidateWithReport(Type contractType)
        {
            if (contractType == null)
                throw new ArgumentNullException(nameof(contractType));

            var diagnostics = new List<ContractDiagnostic>();
            string contractName = contractType.FullName ?? contractType.Name ?? "Unknown";

            // NXC101: 验证 Operation 意图声明
            var opAttr = contractType.GetCustomAttribute<ApiOperationAttribute>();
            if (opAttr == null)
            {
                diagnostics.Add(ContractDiagnostic.Create(
                    contractName,
                    ContractDiagnosticRegistry.NXC101,
                    propertyName: null,
                    propertyPath: null,
                    contractType.Name ?? "Unknown"
                ));
                // Critical: stop further checks for this type
                return diagnostics;
            }

            // NXC102
            if (string.IsNullOrWhiteSpace(opAttr.Operation))
            {
                diagnostics.Add(ContractDiagnostic.Create(
                    contractName,
                    ContractDiagnosticRegistry.NXC102,
                    propertyName: null,
                    propertyPath: null,
                    contractType.Name ?? "Unknown"
                ));
            }

            // NXC103
            var responseType = GetResponseType(contractType);
            if (opAttr.Interaction == InteractionKind.OneWay && responseType != typeof(EmptyResponse))
            {
                diagnostics.Add(ContractDiagnostic.Create(
                    contractName,
                    ContractDiagnosticRegistry.NXC103,
                    propertyName: null,
                    propertyPath: null,
                    opAttr.Operation,
                    responseType?.Name ?? "null"
                ));
            }

            ValidateFieldsRecursiveWithReport(contractType, 1, new HashSet<Type>(), "", diagnostics);

            return diagnostics;
        }

        /// <summary>
        /// 递归验证字段和深度约束（Fail-Fast 版本）
        /// </summary>
        private static void ValidateFieldsRecursive(
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

                // NXC106: 安全红线：加密字段必须锁定路径 (Path Pinning)
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

                // NXC107: 复杂对象或列表检查：嵌套深度 > 1 时，必须显式锁定 Name
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
                    ValidateFieldsRecursive(propType, currentDepth + 1, childVisited, childPath);
                }
            }
        }

        private static void ValidateFieldsRecursiveWithReport(
            Type type,
            int currentDepth,
            HashSet<Type> visited,
            string path,
            List<ContractDiagnostic> diagnostics)
        {
            string typeName = type.FullName ?? type.Name ?? "Unknown";

            if (currentDepth > MaxDepth)
            {
                string pathInfo = !string.IsNullOrEmpty(path) ? path : type.Name ?? "Unknown";
                diagnostics.Add(ContractDiagnostic.Create(
                    typeName,
                    ContractDiagnosticRegistry.NXC104,
                    propertyName: null,
                    propertyPath: pathInfo,
                    MaxDepth,
                    pathInfo,
                    type.Name ?? "Unknown"
                ));
                return;
            }

            if (visited.Contains(type))
            {
                string pathInfo = !string.IsNullOrEmpty(path) ? path : type.Name ?? "Unknown";
                diagnostics.Add(ContractDiagnostic.Create(
                    typeName,
                    ContractDiagnosticRegistry.NXC105,
                    propertyName: null,
                    propertyPath: pathInfo,
                    pathInfo,
                    type.Name ?? "Unknown"
                ));
                return;
            }

            visited.Add(type);

            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var prop in properties)
            {
                var fieldAttr = prop.GetCustomAttribute<ApiFieldAttribute>();
                if (fieldAttr == null) continue;

                if (fieldAttr.IsEncrypted && string.IsNullOrEmpty(fieldAttr.Name))
                {
                    diagnostics.Add(ContractDiagnostic.Create(
                        typeName,
                        ContractDiagnosticRegistry.NXC106,
                        propertyName: prop.Name,
                        propertyPath: !string.IsNullOrEmpty(path) ? $"{path}.{prop.Name}" : prop.Name,
                        type.Name ?? "Unknown",
                        prop.Name ?? "Unknown"
                    ));
                }

                Type propType = GetEffectiveType(prop.PropertyType);
                if (propType.IsPrimitive || propType == typeof(string))
                    continue;
                if (typeof(System.Collections.IDictionary).IsAssignableFrom(propType))
                    continue;

                if (TypeUtilities.IsComplexType(propType))
                {
                    if (currentDepth > 1 && string.IsNullOrEmpty(fieldAttr.Name))
                    {
                        diagnostics.Add(ContractDiagnostic.Create(
                            typeName,
                            ContractDiagnosticRegistry.NXC107,
                            propertyName: prop.Name,
                            propertyPath: !string.IsNullOrEmpty(path) ? $"{path}.{prop.Name}" : prop.Name,
                            type.Name ?? "Unknown",
                            prop.Name ?? "Unknown",
                            currentDepth
                        ));
                    }

                    var childVisited = new HashSet<Type>(visited);
                    string childPath = !string.IsNullOrEmpty(path)
                        ? $"{path} → {prop.Name}"
                        : $"{(type.Name ?? "Unknown")}.{prop.Name ?? "Unknown"}";

                    ValidateFieldsRecursiveWithReport(propType, currentDepth + 1, childVisited, childPath, diagnostics);
                }
            }
        }

        // ValidateFieldsRecursiveWithReport is implemented above; duplicate removed.

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
