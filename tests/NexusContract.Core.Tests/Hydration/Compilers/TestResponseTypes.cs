// Copyright (c) 2025-2026 NexusContract. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using NexusContract.Abstractions.Attributes;

namespace NexusContract.Core.Tests.Hydration.Compilers
{
    /// <summary>
    /// IL 编译器测试用 Response 类型集合
    /// 
    /// 说明：
    /// - ApiField 属性**不是必须的**，仅在以下情况使用：
    ///   1. 需要显式指定字段名（属性名与协议字段名不同）
    ///   2. 标记字段为加密（IsEncrypted = true）
    ///   3. 标记字段为必填（IsRequired = true）
    /// 
    /// - 默认情况下，属性名通过 INamingPolicy 转换为协议字段名
    ///   例如：PropertyName → property_name（snake_case）
    /// 
    /// - 加密字段时，ApiField Name 参数**必须显式指定**（宪法 R-201）
    /// </summary>
    /// 
    /// <summary>
    /// 简单响应类型（无 ApiField 标注）
    /// 
    /// 说明：属性名直接由 INamingPolicy 转换为协议字段名
    /// 适用场景：协议字段名与属性名遵循同一命名策略时使用
    /// </summary>
    public class SimpleResponse
    {
        // ApiField 不是必须的，默认由命名策略处理
        public long Id { get; set; }

        public decimal Amount { get; set; }

        public bool Success { get; set; }
    }

    /// <summary>
    /// 带加密字段的响应类型
    /// 
    /// 说明：加密字段**必须显式指定** ApiField(Name)
    /// 宪法 R-201：IsEncrypted = true 时强制指定 Name（Fail-Fast 原则）
    /// </summary>
    public class EncryptedResponse
    {
        public long Id { get; set; }

        // ✓ 正确：加密字段显式指定名称
        [ApiField("card_number", IsEncrypted = true)]
        public string CardNo { get; set; } = string.Empty;

        // ✓ 正确：普通字段无需 ApiField
        public decimal Amount { get; set; }
    }

    /// <summary>
    /// 显式字段名映射的响应类型
    /// 
    /// 说明：当属性名与协议字段名不同时，使用 ApiField(Name) 显式指定
    /// 适用场景：API 协议字段名为 CamelCase，而属性使用 PascalCase 时
    /// </summary>
    public class MappedResponse
    {
        // ✓ 正确：显式指定字段名映射
        [ApiField("order_id")]
        public long OrderId { get; set; }

        // ✓ 正确：可选的 IsRequired 标记
        [ApiField("user_name", IsRequired = true)]
        public string UserName { get; set; } = string.Empty;

        // ✓ 正确：普通字段，由命名策略处理
        public decimal TotalAmount { get; set; }
    }

    /// <summary>
    /// 必填字段响应类型
    /// 
    /// 说明：IsRequired = true 标记字段为必填
    /// 异常：如果必填字段在源数据中缺失，抛出 NXC301 异常
    /// </summary>
    public class RequiredFieldResponse
    {
        // ✓ 正确：标记必填字段
        [ApiField(IsRequired = true)]
        public long TransactionId { get; set; }

        // ✓ 正确：普通可选字段
        public string? Remark { get; set; }
    }
}
