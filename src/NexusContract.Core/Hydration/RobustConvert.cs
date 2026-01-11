// Copyright (c) 2025-2026 NexusContract. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using NexusContract.Abstractions.Exceptions;

namespace NexusContract.Core.Hydration
{
    /// <summary>
    /// 【决策 A-501】清爽反射版类型转换
    /// 
    /// 设计哲学：
    /// - 放弃 IL 编译的几百纳秒优化，换得完整的诊断能力
    /// - 网络延迟 100-500ms 的背景下，反射的 1-10μs 完全可以忽略
    /// - 每一行逻辑都可读、可调试、可测试
    /// 
    /// 诊断策略：
    /// 如果 'Amount' 字段期望 Decimal，却收到 'abc'：
    /// → [NXC302] 字段 'Amount' 类型转换失败：预期 Decimal，实际 String 值 'abc'
    /// 
    /// 这种"报错报得准"在处理支付异常和重试补偿时是救命的。
    /// </summary>
    public sealed class RobustConvert
    {
        /// <summary>
        /// 将值转换为目标类型，带完整的诊断信息
        /// </summary>
        public static object ConvertValue(
            object? sourceValue,
            Type targetType,
            string? fieldName = null,
            string? contractName = null)
        {
            NexusGuard.EnsurePhysicalAddress(targetType);

            // 处理 null
            if (sourceValue == null)
            {
                // Nullable 类型直接返回 null
                if (IsNullableType(targetType))
                    return null;

                // 非 Nullable 则报错
                throw new ContractIncompleteException(
                    contractName ?? "Unknown",
                    ContractDiagnosticRegistry.NXC301,
                    contractName ?? "Unknown",
                    fieldName ?? "Unknown"
                );
            }

            // 同类型直接返回
            if (sourceValue.GetType() == targetType)
                return sourceValue;

            // 字符串 → 特殊处理（支付场景中最常见）
            if (sourceValue is string stringValue)
                return ConvertFromString(stringValue, targetType, fieldName, contractName);

            // 数值类型 → 通用转换
            try
            {
                var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

                return underlyingType switch
                {
                    var t when t == typeof(int) => Convert.ToInt32(sourceValue),
                    var t when t == typeof(long) => Convert.ToInt64(sourceValue),
                    var t when t == typeof(decimal) => Convert.ToDecimal(sourceValue),
                    var t when t == typeof(double) => Convert.ToDouble(sourceValue),
                    var t when t == typeof(float) => Convert.ToSingle(sourceValue),
                    var t when t == typeof(bool) => Convert.ToBoolean(sourceValue),
                    var t when t == typeof(DateTime) => Convert.ToDateTime(sourceValue),
                    var t when t == typeof(Guid) => ConvertToGuid(sourceValue),
                    _ => Convert.ChangeType(sourceValue, underlyingType)
                };
            }
            catch (Exception ex)
            {
                // 捕获转换异常，提升诊断信息
                throw new ContractIncompleteException(
                    contractName ?? "Unknown",
                    ContractDiagnosticRegistry.NXC302,
                    fieldName ?? "Unknown",
                    $"Expected {targetType.Name}, got {sourceValue.GetType().Name} value '{sourceValue}'",
                    ex
                );
            }
        }

        /// <summary>
        /// 从字符串转换（支付场景中最常见）
        /// </summary>
        private static object ConvertFromString(
            string stringValue,
            Type targetType,
            string? fieldName = null,
            string? contractName = null)
        {
            var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

            // 字符串 → 字符串：直接返回
            if (underlyingType == typeof(string))
                return stringValue;

            try
            {
                return underlyingType switch
                {
                    var t when t == typeof(int) => int.Parse(stringValue),
                    var t when t == typeof(long) => long.Parse(stringValue),
                    var t when t == typeof(decimal) => decimal.Parse(stringValue),
                    var t when t == typeof(double) => double.Parse(stringValue),
                    var t when t == typeof(float) => float.Parse(stringValue),
                    var t when t == typeof(bool) => bool.Parse(stringValue),
                    var t when t == typeof(DateTime) => DateTime.Parse(stringValue),
                    var t when t == typeof(Guid) => Guid.Parse(stringValue),
                    _ => Convert.ChangeType(stringValue, underlyingType)
                };
            }
            catch (Exception ex)
            {
                throw new ContractIncompleteException(
                    contractName ?? "Unknown",
                    ContractDiagnosticRegistry.NXC302,
                    fieldName ?? "Unknown",
                    $"Cannot parse string '{stringValue}' as {underlyingType.Name}",
                    ex
                );
            }
        }

        /// <summary>
        /// Guid 特殊处理（支持两种格式）
        /// </summary>
        private static Guid ConvertToGuid(object value)
        {
            return value switch
            {
                Guid g => g,
                string s => Guid.Parse(s),
                byte[] b => new Guid(b),
                _ => throw new FormatException($"Cannot convert {value.GetType()} to Guid")
            };
        }

        /// <summary>
        /// 判断是否为 Nullable 类型
        /// </summary>
        private static bool IsNullableType(Type type)
        {
            return Nullable.GetUnderlyingType(type) != null || !type.IsValueType;
        }

        /// <summary>
        /// 批量转换（用于集合投影）
        /// </summary>
        public static List<object> ConvertList(
            IEnumerable<object?>? sourceList,
            Type elementType,
            string? fieldName = null,
            string? contractName = null)
        {
            var result = new List<object>();

            if (sourceList == null)
                return result;

            int index = 0;
            foreach (var item in sourceList)
            {
                try
                {
                    if (item == null)
                        continue;

                    var converted = ConvertValue(item, elementType, $"{fieldName}[{index}]", contractName);
                    result.Add(converted);
                }
                catch (Exception ex)
                {
                    throw new ContractIncompleteException(
                        contractName ?? "Unknown",
                        ContractDiagnosticRegistry.NXC302,
                        $"{fieldName}[{index}]",
                        $"List element conversion failed at index {index}: {ex.Message}",
                        ex
                    );
                }
                index++;
            }

            return result;
        }
    }
}
