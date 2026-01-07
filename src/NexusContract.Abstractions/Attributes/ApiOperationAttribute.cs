// Copyright (c) 2025-2026 NexusContract. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;

namespace NexusContract.Abstractions.Attributes
{
    /// <summary>
    /// 【模式 P-102】ApiOperation Annotation（操作标注）
    /// 
    /// 核心职责：声明契约的交互语义（Intent），而非实现细节。
    /// 
    /// 设计原则：Intent, not Implementation
    /// =====================================
    /// 
    /// ✓ Intent（在 Attribute 中定义）：
    /// - Operation：操作标识（alipay.trade.pay）
    /// - HttpVerb：HTTP 动词（GET/POST/PUT）
    /// - Interaction：交互模式（同步 vs 异步）
    /// - Version：API 版本
    /// 
    /// ✗ Implementation（在 Provider 中定义）：
    /// - 签名算法（MD5 vs RSA vs RSA2）
    /// - 加密方法（AES vs RSA vs SM4）
    /// - 超时策略、重试策略、HTTP Header 管理
    /// 
    /// 好处：
    /// 1. 业务契约与技术适配器完全解耦
    /// 2. Provider 各自决定实现细节（同一契约可被多个 Provider 适配）
    /// 3. 模型清晰度：Attribute = 协议约束，Provider = 实现选择
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class ApiOperationAttribute : Attribute
    {
        /// <summary>
        /// 全局唯一的操作标识（例如：alipay.trade.pay, unionpay.query）
        /// 
        /// 这是 Contract Kernel 的主键：
        /// - 路由：Provider 通过 Operation 查找处理逻辑
        /// - 监控：Metrics、Tracing 的维度
        /// - 配置：超时、重试、限流等策略的绑定
        /// - 日志：操作的唯一标识
        /// </summary>
        public string Operation { get; }

        /// <summary>
        /// 交互模式：请求-响应 或 单向通知
        /// 
        /// RequestResponse：调用方等待同步响应（支付、查询）
        /// OneWay：调用方不等待响应或异步回调（通知、推送）
        /// </summary>
        public InteractionKind Interaction { get; }

        /// <summary>
        /// HTTP 交互动词（协议事实，不是实现细节）
        /// 
        /// 这是契约的必备属性：
        /// - GET：查询、获取资源
        /// - POST：支付、转账等有业务影响的操作
        /// - PUT/PATCH：更新
        /// - DELETE：删除
        /// </summary>
        public HttpVerb Verb { get; }

        /// <summary>
        /// 接口版本（可选）
        /// 用于 API 向后兼容性管理
        /// </summary>
        public string? Version { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="operation">操作标识（必须，全局唯一）</param>
        /// <param name="verb">HTTP 动词（默认 POST）</param>
        /// <param name="interaction">交互模式（默认请求-响应）</param>
        public ApiOperationAttribute(
            string operation,
            HttpVerb verb = HttpVerb.POST,
            InteractionKind interaction = InteractionKind.RequestResponse)
        {
            if (string.IsNullOrWhiteSpace(operation))
                throw new ArgumentException(
                    "ApiOperation: Operation cannot be null or empty.",
                    nameof(operation));

            Operation = operation;
            Verb = verb;
            Interaction = interaction;
        }
    }
}


