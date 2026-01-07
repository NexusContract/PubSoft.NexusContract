// Copyright (c) 2025-2026 PubSoft (pubsoft@gmail.com). All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using PubSoft.NexusContract.Abstractions.Configuration;
using PubSoft.NexusContract.Abstractions.Exceptions;
using PubSoft.NexusContract.Abstractions.Policies;
using PubSoft.NexusContract.Abstractions.Security;
using PubSoft.NexusContract.Core.Reflection;

namespace PubSoft.NexusContract.Core.Projection
{
    /// <summary>
    /// 【决策 A-401】ProjectionEngine（运行时投影引擎）
    /// 
    /// 核心公式：强类型 POCO --[投影]--> Dictionary（中间表示）
    /// 
    /// 约束：
    /// - 递归深度最大 3 层（防循环引用、AI 过度嵌套）
    /// - 复杂对象/列表必须显式 Name（路径锁定）
    /// - 整树使用一致的 NamingPolicy（策略单调性）
    /// 
    /// 深度限制 3 的理由：Alipay/WeChat/UnionPay 标准接口最深 3 层。
    /// 超过 3 层通常表示架构设计问题，防止 AI 自主设计过深结构。
    /// </summary>
    public sealed class ProjectionEngine(INamingPolicy namingPolicy, IEncryptor? encryptor = null)
    {
        private readonly INamingPolicy _namingPolicy = namingPolicy ?? throw new ArgumentNullException(nameof(namingPolicy));
        private readonly IEncryptor? _encryptor = encryptor;

        // 从全局边界配置引用（单一来源）
        private static readonly int MaxRecursionDepth = ContractBoundaries.MaxNestingDepth;

        /// <summary>
        /// 将 Contract 投影为 Protocol Representation (Dictionary)
        /// 性能优化：优先使用预编译的 Expression Tree Projector（零反射开销）
        /// Fallback：递归投影支持嵌套对象和列表（深度限制 3 层）
        /// </summary>
        public IDictionary<string, object> Project<TResponse>(object contract)
        {
            if (contract == null)
                throw new ArgumentNullException(nameof(contract));

            var contractType = contract.GetType();
            var metadata = NexusContractMetadataRegistry.Instance.GetMetadata(contractType);

            // 【性能觉醒】优先使用预编译的 Projector（Expression Tree → Delegate）
            // 性能提升：从反射级（~100ns）→ 原生委托级（~10ns），约 10 倍提升
            if (metadata.Projector != null)
            {
                return metadata.Projector(contract, _namingPolicy, _encryptor);
            }

            // Fallback：仅在 Projector 编译失败时使用反射路径
            return ProjectInternal(contract, 0);
        }

        /// <summary>
        /// 内部递归投影方法
        /// </summary>
        private IDictionary<string, object> ProjectInternal(object contract, int depth)
        {
            if (contract == null)
                return new Dictionary<string, object>();

            // 防御性检查：深度限制（Fail-Safe）
            if (depth > MaxRecursionDepth)
            {
                string typeName = contract.GetType().FullName ?? contract.GetType().Name ?? "Unknown";
                throw new ContractIncompleteException(
                    typeName,
                    ContractDiagnosticRegistry.NXC203,
                    MaxRecursionDepth
                );
            }

            var contractType = contract.GetType();
            string contractTypeName = contractType.FullName ?? contractType.Name ?? "Unknown";
            var metadata = NexusContractMetadataRegistry.Instance.GetMetadata(contractType);
            var result = new Dictionary<string, object>();

            foreach (var pm in metadata.Properties)
            {
                object? propertyValue = pm.PropertyInfo.GetValue(contract);

                // 跳过 null 值
                if (propertyValue == null)
                {
                    if (pm.ApiField.IsRequired)
                    {
                        throw new ContractIncompleteException(
                            contractTypeName,
                            ContractDiagnosticRegistry.NXC201,
                            contractType.Name ?? "Unknown",
                            pm.PropertyInfo.Name ?? "Unknown"
                        );
                    }
                    continue;
                }

                string fieldName = !string.IsNullOrWhiteSpace(pm.ApiField.Name)
                    ? pm.ApiField.Name
                    : _namingPolicy.ConvertName(pm.PropertyInfo.Name);

                // 核心逻辑：根据类型选择投影方式
                object finalValue = propertyValue switch
                {
                    // 字符串：可能需要加密
                    string s => pm.ApiField.IsEncrypted ? EncryptValue(s) : s,

                    // IDictionary：按原样返回（或递归处理）
                    IDictionary dict => dict,

                    // List<T>：递归投影集合
                    IList list => ProjectCollection(list, pm.ApiField.IsEncrypted, depth),

                    // 基础值类型
                    ValueType v => v,

                    // 复杂对象：递归投影
                    object o => ProjectInternal(o, depth + 1),

                    _ => propertyValue
                };

                result[fieldName] = finalValue;
            }

            return result;
        }

        /// <summary>
        /// 投影集合（List&lt;T&gt;）
        /// 遍历列表中的每个元素，根据元素类型选择投影方式
        /// </summary>
        private IList ProjectCollection(IList list, bool isEncrypted, int depth)
        {
            var result = new List<object>();

            foreach (object? item in list)
            {
                if (item == null)
                    continue;

                object projectedItem = item switch
                {
                    // 列表中的字符串
                    string s => isEncrypted ? EncryptValue(s) : s,

                    // 列表中的基础类型
                    ValueType v => v,

                    // 列表中的复杂对象（递归，深度继续递增）
                    object o => ProjectInternal(o, depth + 1),

                    _ => item
                };

                result.Add(projectedItem);
            }

            return result;
        }

        /// <summary>
        /// 加密值
        /// </summary>
        private string EncryptValue(string value)
        {
            if (_encryptor == null)
            {
                // 获取当前属性名（仅用于错误报告）
                throw new ContractIncompleteException(
                    "ProjectionEngine",
                    ContractDiagnosticRegistry.NXC202,
                    "Unknown", // 在运行时由调用方提供
                    "Unknown"
                );
            }

            return _encryptor.Encrypt(value);
        }
    }
}
