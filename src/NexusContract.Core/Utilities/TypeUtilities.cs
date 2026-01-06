// Copyright (c) 2025-2026 PubSoft (pubsoft@gmail.com). All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections;

namespace PubSoft.NexusContract.Core.Utilities
{
    /// <summary>
    /// 类型判断工具类
    /// 
    /// 统一实现类型检查逻辑，消除代码重复。
    /// 之前 IsComplexType() 在 3 个文件中重复定义：
    /// - ContractValidator
    /// - ContractAuditor  
    /// - ResponseHydrationEngine
    /// </summary>
    public static class TypeUtilities
    {
        /// <summary>
        /// 判断是否为复杂类型（需要递归处理的自定义对象）
        /// 
        /// 排除规则：
        /// 1. 基元类型（int, bool, etc.）
        /// 2. 字符串（虽然是引用类型，但视为简单类型）
        /// 3. IDictionary（协议表示层，不视为契约对象）
        /// 4. System 命名空间类型（框架类，如 DateTime, Guid）
        /// 
        /// 包含规则：
        /// - 用户自定义的 POCO 类型
        /// - 需要递归投影/回填的嵌套对象
        /// </summary>
        /// <param name="type">要检查的类型</param>
        /// <returns>true 表示复杂类型，false 表示简单类型</returns>
        public static bool IsComplexType(Type type)
        {
            if (type == null || type.IsPrimitive || type == typeof(string))
                return false;

            // 排除 Dictionary（协议层，不是契约对象）
            if (typeof(IDictionary).IsAssignableFrom(type))
                return false;

            // 排除系统框架类（DateTime, Guid, TimeSpan 等）
            if (type.Namespace?.StartsWith("System") == true)
                return false;

            // 其余为复杂类型（用户自定义 POCO）
            return true;
        }

        /// <summary>
        /// 判断是否为集合类型（IEnumerable 但非字符串）
        /// </summary>
        /// <param name="type">要检查的类型</param>
        /// <returns>true 表示集合类型</returns>
        public static bool IsCollectionType(Type type)
        {
            return type != typeof(string) 
                && typeof(IEnumerable).IsAssignableFrom(type);
        }

        /// <summary>
        /// 判断类型是否可为 null
        /// </summary>
        /// <param name="type">要检查的类型</param>
        /// <returns>true 表示可为 null（引用类型或 Nullable&lt;T&gt;）</returns>
        public static bool IsNullable(Type type)
        {
            return !type.IsValueType || Nullable.GetUnderlyingType(type) != null;
        }
    }
}
