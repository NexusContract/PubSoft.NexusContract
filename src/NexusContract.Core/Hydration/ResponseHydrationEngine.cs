// Copyright (c) 2025-2026 NexusContract. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
#nullable enable
using NexusContract.Abstractions.Configuration;
using NexusContract.Abstractions.Exceptions;
using NexusContract.Abstractions.Policies;
using NexusContract.Abstractions.Security;
using NexusContract.Core.Reflection;
using NexusContract.Core.Utilities;

namespace NexusContract.Core.Hydration
{
    /// <summary>
    /// 响应回填引擎（Response Hydration Engine）
    /// 
    /// 核心公式：Dictionary (协议表示) --[ResponseHydrationEngine]--> 强类型 Response
    /// 
    /// 职责：
    /// 1. 反向投影：将三方返回的 Dictionary 填充到强类型对象
    /// 2. 对称解密：自动解密 IsEncrypted=true 的字段
    /// 3. 脏数据清洗：通过强力类型转换处理 API 返回的"类型混乱"
    /// 4. 精准定位：出错时提供完整路径和诊断码（NXC3xx）
    /// 
    /// 设计原则：
    /// - 与 ProjectionEngine 完全对称（出入互为镜像）
    /// - 物理边界感知（MaxNestingDepth, MaxCollectionSize）
    /// - 强力容错能力（自动类型转换、解密、递归）
    /// </summary>
    public sealed class ResponseHydrationEngine
    {
        private readonly INamingPolicy _namingPolicy;
        private readonly IDecryptor? _decryptor;

        public ResponseHydrationEngine(INamingPolicy namingPolicy, IDecryptor? decryptor = null)
        {
            NexusGuard.EnsurePhysicalAddress(namingPolicy);
            _namingPolicy = namingPolicy;
            _decryptor = decryptor;
        }

        /// <summary>
        /// 将 Dictionary 回填到强类型 Response
        /// 
        /// 设计哲学：清爽反射回填，带完整诊断
        /// 性能：1-10ms（反射），在网络延迟 100-500ms 背景下完全可以忽略
        /// </summary>
        public T Hydrate<T>(IDictionary<string, object> source) where T : new()
        {
            NexusGuard.EnsurePhysicalAddress(source);

            try
            {
                return (T)HydrateInternal(typeof(T), source, 0);
            }
            catch (ContractIncompleteException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new ContractIncompleteException(
                    typeof(T).FullName ?? typeof(T).Name ?? "Unknown",
                    ContractDiagnosticRegistry.NXC301,
                    typeof(T).Name ?? "Unknown",
                    $"Hydration failed: {ex.Message}",
                    ex
                );
            }
        }

        /// <summary>
        /// 内部递归回填方法
        /// </summary>
        private object HydrateInternal(Type type, IDictionary<string, object> source, int depth)
        {
            // 物理红线：防御性深度检查
            if (depth > ContractBoundaries.MaxNestingDepth)
            {
                string typeName = type.FullName ?? type.Name ?? "Unknown";
                throw new ContractIncompleteException(
                    typeName,
                    ContractDiagnosticRegistry.NXC203,
                    ContractBoundaries.MaxNestingDepth
                );
            }

            var metadata = NexusContractMetadataRegistry.Instance.GetMetadata(type);
            object instance = Activator.CreateInstance(type)!;
            string typeFullName = type.FullName ?? type.Name ?? "Unknown";
            string typeName2 = type.Name ?? "Unknown";

            foreach (var pm in metadata.Properties)
            {
                // 确定字段名：Path Pinning 优先
                // 优先使用显式指定的 Name，只有当未指定时才使用命名策略
                string fieldName = !string.IsNullOrEmpty(pm.ApiField.Name)
                    ? pm.ApiField.Name
                    : _namingPolicy.ConvertName(pm.PropertyInfo.Name);

                // 提取源数据
                if (!source.TryGetValue(fieldName, out object? rawValue) || rawValue == null)
                {
                    // NXC301: 必需字段缺失检查
                    if (pm.ApiField.IsRequired && !TypeUtilities.IsNullable(pm.PropertyInfo.PropertyType))
                    {
                        throw new ContractIncompleteException(
                            typeFullName,
                            ContractDiagnosticRegistry.NXC301,
                            typeName2,
                            pm.PropertyInfo.Name ?? "Unknown",
                            fieldName
                        );
                    }
                    // 可选字段：优雅跳过
                    continue;
                }

                // 执行值转换（含解密、递归、类型转换）
                object finalValue = TransformValue(rawValue, pm, depth);
                pm.PropertyInfo.SetValue(instance, finalValue);
            }

            return instance;
        }

        /// <summary>
        /// 值转换处理（对称解密 + 递归 + 类型转换）
        /// 
        /// 宪法 007 实现：使用智能缓存反射转换，避免重复反射调用
        /// 性能：<200ns（通过缓存优化，vs. ~100-500μs 重复反射路径）
        /// </summary>
        private object TransformValue(object rawValue, PropertyMetadata pm, int depth)
        {
            var targetType = pm.PropertyInfo.PropertyType;

            // A. 对称解密
            if (pm.ApiField.IsEncrypted && rawValue is string encryptedStr)
            {
                if (_decryptor == null)
                {
                    string declaringTypeName = pm.PropertyInfo.DeclaringType?.FullName ?? pm.PropertyInfo.DeclaringType?.Name ?? "Unknown";
                    string declaringTypeShortName = pm.PropertyInfo.DeclaringType?.Name ?? "Unknown";
                    throw new ContractIncompleteException(
                        declaringTypeName,
                        ContractDiagnosticRegistry.NXC202,
                        declaringTypeShortName,
                        pm.PropertyInfo.Name ?? "Unknown"
                    );
                }

                rawValue = _decryptor.Decrypt(encryptedStr);
            }

            // B. 递归回填复杂对象
            if (TypeUtilities.IsComplexType(targetType) && rawValue is IDictionary<string, object> nestedDict)
            {
                return HydrateInternal(targetType, nestedDict, depth + 1);
            }

            // C. 集合处理
            if (TypeUtilities.IsCollectionType(targetType) && targetType != typeof(string) && rawValue is IEnumerable rawList)
            {
                return HydrateCollection(rawList, targetType, pm, depth);
            }

            // D. 强力类型转换（使用编译委托）
            return RobustConvert(rawValue, targetType, pm);
        }

        /// <summary>
        /// 集合回填（递归处理每个元素）
        /// </summary>
        private object HydrateCollection(IEnumerable rawList, Type targetType, PropertyMetadata pm, int depth)
        {
            // 获取集合元素类型
            Type? elementType = null;
            if (targetType.IsGenericType)
            {
                elementType = targetType.GetGenericArguments().FirstOrDefault();
            }
            else if (typeof(IEnumerable).IsAssignableFrom(targetType) && targetType != typeof(string))
            {
                elementType = typeof(object);
            }

            if (elementType == null)
                elementType = typeof(object);

            var resultList = new List<object>();
            int itemCount = 0;

            foreach (object? item in rawList)
            {
                // NXC303: 集合大小限制
                if (++itemCount > ContractBoundaries.MaxCollectionSize)
                {
                    string declaringTypeName = pm.PropertyInfo.DeclaringType?.FullName ?? pm.PropertyInfo.DeclaringType?.Name ?? "Unknown";
                    string declaringTypeShortName = pm.PropertyInfo.DeclaringType?.Name ?? "Unknown";
                    throw new ContractIncompleteException(
                        declaringTypeName,
                        ContractDiagnosticRegistry.NXC303,
                        declaringTypeShortName,
                        pm.PropertyInfo.Name ?? "Unknown",
                        ContractBoundaries.MaxCollectionSize
                    );
                }

                if (item == null)
                {
                    resultList.Add(null!);
                    continue;
                }

                // 对集合元素进行转换
                object elementValue = item switch
                {
                    // 加密字符串解密
                    string s when pm.ApiField.IsEncrypted => DecryptValue(s, pm),

                    // 复杂对象递归回填
                    IDictionary<string, object> dict when TypeUtilities.IsComplexType(elementType)
                        => HydrateInternal(elementType, dict, depth + 1),

                    // 基础类型转换
                    _ => RobustConvert(item, elementType, pm)
                };

                resultList.Add(elementValue);
            }

            // 转换为目标集合类型
            return ConvertToCollection(resultList, targetType, elementType);
        }

        /// <summary>
        /// 强力类型转换器（终结三方 API 的类型混乱）
        /// 
        /// 设计哲学：清爽反射版本，带完整的诊断能力
        /// 性能：1-10μs（反射），在网络延迟 100-500ms 背景下完全可以忽略
        /// </summary>
        private object RobustConvert(object value, Type targetType, PropertyMetadata pm)
        {
            try
            {
                return Hydration.RobustConvert.ConvertValue(
                    value,
                    targetType,
                    pm.PropertyInfo.Name,
                    pm.PropertyInfo.DeclaringType?.Name ?? "Unknown"
                );
            }
            catch (ContractIncompleteException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // NXC302: 类型转换失败，精准告知现场
                string declaringTypeName = pm.PropertyInfo.DeclaringType?.FullName ?? pm.PropertyInfo.DeclaringType?.Name ?? "Unknown";
                string declaringTypeShortName = pm.PropertyInfo.DeclaringType?.Name ?? "Unknown";
                throw new ContractIncompleteException(
                    declaringTypeName,
                    ContractDiagnosticRegistry.NXC302,
                    pm.PropertyInfo.Name ?? "Unknown",
                    $"Failed to convert to {targetType.Name}: {ex.Message}",
                    ex
                );
            }
        }

        /// <summary>
        /// 解密字符串（带异常处理）
        /// </summary>
        private string DecryptValue(string encryptedValue, PropertyMetadata pm)
        {
            if (_decryptor == null)
            {
                string declaringTypeName = pm.PropertyInfo.DeclaringType?.FullName ?? pm.PropertyInfo.DeclaringType?.Name ?? "Unknown";
                string declaringTypeShortName = pm.PropertyInfo.DeclaringType?.Name ?? "Unknown";
                throw new ContractIncompleteException(
                    declaringTypeName,
                    ContractDiagnosticRegistry.NXC202,
                    declaringTypeShortName,
                    pm.PropertyInfo.Name ?? "Unknown"
                );
            }

            return _decryptor.Decrypt(encryptedValue);
        }

        /// <summary>
        /// 将 List 转换为目标集合类型
        /// </summary>
        private object ConvertToCollection(List<object> items, Type targetType, Type elementType)
        {
            // 如果目标是 IEnumerable<T> 或 List<T>，直接转换
            if (targetType.IsGenericType)
            {
                var genericDef = targetType.GetGenericTypeDefinition();

                // List<T>
                if (genericDef == typeof(List<>))
                {
                    var listType = typeof(List<>).MakeGenericType(elementType);
                    var list = Activator.CreateInstance(listType) as IList;
                    foreach (object item in items)
                        list?.Add(item);
                    return list!;
                }

                // IEnumerable<T>, ICollection<T> 等返回 List<T>
                if (typeof(IEnumerable).IsAssignableFrom(targetType))
                {
                    var listType = typeof(List<>).MakeGenericType(elementType);
                    var list = Activator.CreateInstance(listType) as IList;
                    foreach (object item in items)
                        list?.Add(item);
                    return list!;
                }
            }

            // 非泛型集合，返回 List<object>
            return items;
        }
    }
}


