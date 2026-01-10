// Copyright (c) 2025-2026 NexusContract. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using NexusContract.Abstractions.Configuration;
using NexusContract.Abstractions.Contracts;

namespace NexusContract.Abstractions.Providers
{
    /// <summary>
    /// Provider 接口：无状态单例，动态配置
    /// 
    /// 职责：
    /// - 封装特定支付平台的协议转换（Alipay/WeChat/UnionPay）
    /// - 实现签名算法（RSA2/AEAD_AES_256_GCM/等）
    /// - 管理 HTTP 通信（请求构建、响应解析）
    /// - 处理平台特定的错误码映射
    /// 
    /// 设计原则：
    /// 1. **无状态单例**：Provider 不持有配置字段，配置通过方法参数传入
    /// 2. **动态配置**：支持运行时动态加载不同租户的配置（JIT 模式）
    /// 3. **协议隔离**：不依赖 FastEndpoints/YARP 等 HTTP 框架
    /// 4. **契约驱动**：基于 IApiRequest 和元数据注册表工作
    /// 
    /// 为什么不持有配置？
    /// - ISV 场景：一个 Provider 实例服务上百个商户
    /// - 配置热更新：无需重启服务即可更新商户配置
    /// - 内存效率：避免为每个租户创建 Provider 实例
    /// 
    /// 实现示例：
    /// <code>
    /// public class AlipayProvider : IProvider
    /// {
    ///     public string ProviderName => "Alipay";
    ///     
    ///     public async Task&lt;TResponse&gt; ExecuteAsync&lt;TResponse&gt;(
    ///         IApiRequest&lt;TResponse&gt; request,
    ///         ProviderSettings settings,
    ///         CancellationToken ct = default)
    ///     {
    ///         // 1. 构建请求 URL
    ///         var uri = BuildUrl(settings.GatewayUrl, request.GetOperationId());
    ///         
    ///         // 2. 投影请求（通过 NexusGateway）
    ///         var dict = _gateway.Project(request);
    ///         
    ///         // 3. 签名请求（使用 settings.PrivateKey）
    ///         var signedRequest = SignRequest(dict, settings);
    ///         
    ///         // 4. 发送 HTTP 请求
    ///         var responseDict = await SendAsync(uri, signedRequest, ct);
    ///         
    ///         // 5. 回填响应（通过 NexusGateway）
    ///         return _gateway.Hydrate&lt;TResponse&gt;(responseDict);
    ///     }
    /// }
    /// </code>
    /// </summary>
    public interface IProvider
    {
        /// <summary>
        /// Provider 标识（如 "Alipay", "WeChat", "UnionPay"）
        /// 用于 Engine 路由和诊断日志
        /// </summary>
        string ProviderName { get; }

        /// <summary>
        /// 执行请求（由 Engine 调度）
        /// 
        /// 工作流：
        /// 1. 验证配置完整性（必需的密钥、证书）
        /// 2. 投影请求对象（C# → Dictionary）
        /// 3. 签名请求（平台特定算法）
        /// 4. 发送 HTTP 请求（支持重试、超时）
        /// 5. 验证响应签名（防篡改）
        /// 6. 回填响应对象（Dictionary → C#）
        /// 
        /// 异常处理：
        /// - 配置无效：抛出 ArgumentException
        /// - 签名失败：抛出 SecurityException
        /// - 网络超时：抛出 TimeoutException
        /// - 平台错误：抛出 ContractIncompleteException（含错误码）
        /// </summary>
        /// <typeparam name="TResponse">响应类型（从 IApiRequest 推断）</typeparam>
        /// <param name="request">API 请求对象</param>
        /// <param name="configuration">Provider 物理配置（含密钥）</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>强类型响应对象</returns>
        Task<TResponse> ExecuteAsync<TResponse>(
            IApiRequest<TResponse> request,
            IProviderConfiguration configuration,
            CancellationToken ct = default)
            where TResponse : class, new();
    }
}
