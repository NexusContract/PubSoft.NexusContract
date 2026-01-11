// Copyright (c) 2025-2026 NexusContract. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using NexusContract.Abstractions.Exceptions;

namespace NexusContract.Abstractions.Attributes
{
    /// <summary>
    /// 【规则 R-201】ApiField Annotation（字段标注）
    /// 
    /// 职责：描述字段如何从强类型对象投影到协议格式（Dictionary）。
    /// 
    /// 核心约束：Fail-Fast 原则
    /// 如果 IsEncrypted = true，则**必须**显式指定 Name 参数。
    /// 
    /// ✗ 错误：[ApiField(IsEncrypted = true)]
    ///   启动期 NXC106 异常
    /// 
    /// ✓ 正确：[ApiField("card_no", IsEncrypted = true)]
    /// 
    /// 理由：
    /// 1. 加密字段在协议层是二进制密文，投影引擎无法猜测其名称
    /// 2. 强制显式命名防止开发者遗忘加密字段的名称映射
    /// 3. Fail-Fast 验证（启动期）捕获错误，而非运行期
    /// 4. 支付系统的"确定性"需求：编译期和启动期都要验证
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class ApiFieldAttribute : Attribute
    {
        /// <summary>
        /// 字段在协议中的显式名称（可选，若为空则由 INamingPolicy 决定）
        /// </summary>
        public string? Name { get; }

        /// <summary>
        /// 是否需要加密传输
        /// </summary>
        public bool IsEncrypted { get; set; }

        /// <summary>
        /// 是否为必填字段
        /// </summary>
        public bool IsRequired { get; set; }

        /// <summary>
        /// 字段描述（用于文档生成）
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// 默认构造函数（使用命名策略推断字段名）
        /// </summary>
        public ApiFieldAttribute()
        {
            Name = null;
        }

        /// <summary>
        /// 显式指定字段名的构造函数
        /// </summary>
        /// <param name="name">协议中的字段名</param>
        public ApiFieldAttribute(string name)
        {
            NexusGuard.EnsureNonEmptyString(name);
            Name = name;
        }


    }
}


