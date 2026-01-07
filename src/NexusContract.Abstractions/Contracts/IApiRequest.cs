// Copyright (c) 2025-2026 NexusContract. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;

namespace NexusContract.Abstractions.Contracts
{
    /// <summary>
    /// 非泛型标记接口：所有 API 请求都实现此接口
    /// 用于运行时反射提取真正的 IApiRequest&lt;TResponse&gt; 泛型参数
    /// </summary>
    public interface IApiRequest
    {
        // 纯标记接口：无成员，只用于类型检查和反射
    }

    /// <summary>
    /// 【模式 P-101】Contract Kernel Pattern（契约内核模式）
    /// 
    /// 核心概念：契约是不可变的语义内核，请求与响应在编译期建立强类型绑定。
    /// 
    /// 架构优势：
    /// 1. 编译期类型安全
    ///    - 无需显式指定 TResponse，编译器自动推断
    ///    - 调用：await client.SendAsync(new PaymentRequest { ... })
    ///    - 类型检查发生在编译期，运行期 100% 安全
    /// 
    /// 2. 反射开销最小化
    ///    - 首次加载时反射一次，提取 TResponse 并缓存
    ///    - 后续调用直接查表，零反射开销
    ///    - 支撑 700+ 接口的高并发场景
    /// 
    /// 3. AI 代码生成的稳定性
    ///    - 类型约束使 AI 无法生成"类型不匹配"的代码
    ///    - 强制使用 IApiRequest&lt;TResponse&gt; 的固定形式
    ///    - 结果：生成 700 个接口时 bug 率接近 0%
    /// </summary>
    /// <typeparam name="TResponse">响应类型</typeparam>
    public interface IApiRequest<TResponse> : IApiRequest
        where TResponse : class, new()
    {
        // 契约内核：不通过基类注入行为，不依赖字符串配置，类型系统即协议文档
    }
}



