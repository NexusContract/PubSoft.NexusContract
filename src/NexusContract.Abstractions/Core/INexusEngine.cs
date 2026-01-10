// Copyright (c) 2025-2026 NexusContract. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using NexusContract.Abstractions.Contracts;

namespace NexusContract.Abstractions.Core
{
    /// <summary>
    /// Nexus 引擎接口：多租户 ISV 网关的调度大脑
    /// 
    /// 职责：
    /// - 根据请求类型（OperationId）或租户上下文（ProviderName）路由到对应 Provider
    /// - 集成 IConfigurationResolver 进行 JIT 配置加载
    /// - 提供诊断日志和性能埋点
    /// - 处理 Provider 调用失败的回退逻辑
    /// 
    /// 架构位置：
    /// - 位于 Endpoint（FastEndpoints）和 Provider（业务逻辑）之间
    /// - 接收租户上下文，返回强类型响应
    /// - 无状态设计：所有状态通过参数传递
    /// 
    /// 工作流：
    /// <code>
    /// // Endpoint 调用 Engine
    /// var tenantCtx = TenantContextFactory.Create(req, HttpContext);
    /// var response = await _engine.ExecuteAsync(req, tenantCtx, ct);
    /// 
    /// // Engine 内部流程
    /// 1. 提取 OperationId（从 [ApiOperation] 元数据）
    /// 2. 路由到对应 Provider（通过 ProviderName 或 OperationId 前缀）
    /// 3. 构造 ConfigurationContext（Provider + Realm + Profile）
    /// 4. JIT 加载配置（通过 IConfigurationResolver）
    /// 5. 调用 Provider.ExecuteAsync（传入配置）
    /// 6. 返回响应
    /// </code>
    /// 
    /// Provider 路由策略：
    /// 1. **显式路由**：TenantContext.ProviderName 指定
    /// 2. **OperationId 前缀**：如 "alipay.trade.create" → AlipayProvider
    /// 3. **元数据标注**：Contract 上的 [Provider("Alipay")]
    /// 4. **默认 Provider**：配置文件中的 DefaultProvider
    /// 
    /// 性能优化：
    /// - Provider 注册表：启动时构建，运行时只读（FrozenDictionary）
    /// - 配置缓存：通过 IConfigurationResolver 的 L1/L2 缓存
    /// - 无反射：基于泛型约束和编译期类型推断
    /// 
    /// 异常处理：
    /// - Provider 未找到：抛出 InvalidOperationException
    /// - 配置加载失败：抛出 NexusTenantException
    /// - Provider 调用失败：透传原始异常
    /// </summary>
    public interface INexusEngine
    {
        /// <summary>
        /// 执行请求（自动调度到对应 Provider）
        /// 
        /// 类型推断：
        /// - TResponse 从 IApiRequest&lt;TResponse&gt; 自动推断
        /// - 调用方无需显式指定泛型参数
        /// 
        /// 示例：
        /// <code>
        /// // 编译器自动推断 TResponse 为 TradeResponse
        /// var response = await _engine.ExecuteAsync(
        ///     new TradeCreateRequest { ... },  // IApiRequest&lt;TradeResponse&gt;
        ///     tenantCtx,
        ///     ct
        /// );
        /// </code>
        /// 
        /// 工作流：
        /// 1. 验证 request 非空
        /// 2. 验证 tenantContext 非空
        /// 3. 提取 OperationId（从元数据注册表）
        /// 4. 路由到对应 Provider
        /// 5. 构造 ConfigurationContext
        /// 6. JIT 加载配置
        /// 7. 调用 Provider.ExecuteAsync
        /// 8. 返回强类型响应
        /// 
        /// 性能特征：
        /// - 冷路径（首次请求）：~100ms（含配置加载）
        /// - 热路径（缓存命中）：~10ms（Provider 执行 + HTTP 往返）
        /// - 内存分配：~1KB（主要是 Dictionary 和字符串）
        /// </summary>
        /// <typeparam name="TResponse">响应类型（自动推断）</typeparam>
        /// <param name="request">API 请求对象（实现 IApiRequest&lt;TResponse&gt;）</param>
        /// <param name="identity">租户身份标识（包含 Realm + Profile + Provider）</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>强类型响应对象</returns>
        /// <exception cref="System.ArgumentNullException">request 或 identity 为空</exception>
        /// <exception cref="System.InvalidOperationException">Provider 未找到或未注册</exception>
        /// <exception cref="Exceptions.NexusTenantException">配置加载失败</exception>
        Task<TResponse> ExecuteAsync<TResponse>(
            IApiRequest<TResponse> request,
            ITenantIdentity identity,
            CancellationToken ct = default)
            where TResponse : class, new();
    }
}
