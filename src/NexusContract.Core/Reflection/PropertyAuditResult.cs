using System;
using System.Reflection;
using NexusContract.Abstractions.Exceptions;

namespace NexusContract.Core.Reflection
{
    /// <summary>
    /// 【决策 A-302】PropertyAuditResult（属性审计结果缓存）
    /// 
    /// 设计意图：启动期缓存约束检查结果（IsEncryptedWithoutName、IsComplexWithoutName），
    /// 运行期直接读布尔值，避免重复的 string.IsNullOrEmpty() 操作。
    /// 性能收益：启动期承担一次性成本，运行期 O(1) 查询，消除反射热点。
    /// </summary>
    public sealed class PropertyAuditResult
    {
        /// <summary>
        /// 属性的反射信息
        /// </summary>
        public PropertyInfo PropertyInfo { get; }

        /// <summary>
        /// ApiField 属性标注
        /// </summary>
        public Abstractions.Attributes.ApiFieldAttribute ApiField { get; }

        /// <summary>
        /// 【规则 R-201】缓存的审计结果：加密字段是否缺少显式名称
        /// 
        /// 设计意图：
        /// - 为 true：IsEncrypted=true 但 Name 为空
        /// - 为 false：要么不加密，要么显式指定了名称
        /// 
        /// 运行期检查流程（在 ProjectionEngine 中）：
        /// if (auditResult.IsEncryptedWithoutName)
        ///     throw NXC106 异常
        /// 
        /// 而不是每次都：
        /// if (fieldAttr.IsEncrypted &amp;&amp; string.IsNullOrEmpty(fieldAttr.Name))
        ///     throw ...（这样会导致重复的反射检查）
        /// </summary>
        public bool IsEncryptedWithoutName { get; }

        /// <summary>
        /// 【规则 R-207】缓存的审计结果：嵌套深度 > 1 的复杂字段是否缺少显式名称
        /// 
        /// 设计意图：
        /// - 为 true：字段是复杂对象或列表，且深度 > 1，但未显式命名
        /// - 为 false：要么是基元类型，要么显式命名
        /// 
        /// 这是【NXC107】约束的缓存形式
        /// </summary>
        public bool IsComplexWithoutName { get; }

        /// <summary>
        /// 是否为复杂类型（对象或列表）
        /// </summary>
        public bool IsComplexType { get; }

        public PropertyAuditResult(
            PropertyInfo propertyInfo,
            Abstractions.Attributes.ApiFieldAttribute apiField,
            bool isEncryptedWithoutName,
            bool isComplexWithoutName,
            bool isComplexType)
        {
            NexusGuard.EnsurePhysicalAddress(propertyInfo);
            NexusGuard.EnsurePhysicalAddress(apiField);
            
            PropertyInfo = propertyInfo;
            ApiField = apiField;
            IsEncryptedWithoutName = isEncryptedWithoutName;
            IsComplexWithoutName = isComplexWithoutName;
            IsComplexType = isComplexType;
        }
    }
}


