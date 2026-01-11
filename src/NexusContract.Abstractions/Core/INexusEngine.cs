// Copyright (c) 2025-2026 NexusContract. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using NexusContract.Abstractions.Contracts;
using NexusContract.Abstractions.Exceptions;

namespace NexusContract.Abstractions.Core
{
    /// <summary>
    /// Nexus 引擎接口：多租户 ISV 网关的调度大脑
    /// 
    /// 职责：
    /// - 根据请求类型（OperationId）和显式 ProfileId 路由到对应 Provider
    /// - 集成 IConfigurationResolver 进行 JIT 配置加载
    /// - 提供诊断日志和 NXC 错误码
    /// - Provider 无状态单例模式（配置通过参数注入）
    /// 
    /// 宪法约束（月月红 004）：
    /// - BFF 负责身份转换（业务用户 → ProfileId）
    /// - Gate（此引擎）仅负责合约执行
    /// - ProfileId 必须显式传入（禁止隐式解析）
    /// 
    /// 工作流：
    /// <code>
    /// // BFF：转换业务身份为 ProfileId
    /// var profileId = merchantId.ToString("N");
    /// 
    /// // Gate（Engine）：执行合约
    /// var response = await _engine.ExecuteAsync(
    ///     request,          // IApiRequest&lt;TResponse&gt;
    ///     "Alipay",         // providerName（显式）
    ///     profileId,        // ProfileId（从 URL/BFF 明确传入）
    ///     ct
    /// );
    /// </code>
    /// 
    /// Provider 路由策略：
    /// 1. **显式 ProviderName**：调用端指定
    /// 2. **OperationId 前缀**：如 "alipay.trade.create" 推断 "Alipay"（备选）
    /// 3. **元数据标注**：Contract 上的 [NexusContract(...)]
    /// 
    /// 性能特征：
    /// - 热路径：~5-10ms（L1 缓存命中 + Provider 执行）
    /// - 冷启动：~50-100ms（Redis 查询 + L1 填充）
    /// - 脱网运行：Redis 故障时可用 30 天（L1 缓存）
    /// 
    /// 错误码：
    /// - NXC101：Request 验证失败
    /// - NXC201：ProfileId 缺失或无效
    /// - NXC301：Transport 错误
    /// - NXC401：Response 反序列化失败
    /// </summary>
    public interface INexusEngine
    {
        /// <summary>
        /// 执行请求（自动调度到对应 Provider）
        /// 
        /// 签名特点：
        /// - 显式要求 providerName 和 profileId
        /// - 禁止隐式解析或默认值
        /// - TResponse 自动从 IApiRequest&lt;TResponse&gt; 推断
        /// 
        /// 示例：
        /// <code>
        /// // 从 URL 路由显式提取 profileId
        /// var profileId = route["merchantId"];
        /// 
        /// // 显式传递给 Engine
        /// var response = await _engine.ExecuteAsync(
        ///     new TradeCreateRequest { /* ... */ },
        ///     "Alipay",     // providerName
        ///     profileId,    // 显式 ProfileId（不允许 null）
        ///     ct
        /// );
        /// </code>
        /// 
        /// 执行步骤：
        /// 1. 验证 request、providerName、profileId 非空（否则抛 NXC201）
        /// 2. 验证 profileId 格式合法
        /// 3. JIT 加载配置（通过 IConfigurationResolver）
        /// 4. 路由到对应 Provider（通过 providerName）
        /// 5. 四阶段管道：Validate → Project → Execute → Hydrate
        /// 6. 返回强类型响应
        /// 
        /// 异常处理：
        /// - 缺失 profileId：抛 NexusTenantException（NXC201）
        /// - Provider 未找到：抛 InvalidOperationException（NXC99x）
        /// - 配置加载失败：抛 NexusTenantException（NXC201）
        /// </summary>
        /// <typeparam name="TResponse">响应类型（自动推断）</typeparam>
        /// <param name="request">API 请求对象（实现 IApiRequest&lt;TResponse&gt;）</param>
        /// <param name="providerName">Provider 标识（例如 "Alipay", "WeChat"）——显式必填</param>
        /// <param name="profileId">档案标识（从 URL 路由或 BFF 层显式传入）——禁止 null/empty</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>强类型响应对象</returns>
        /// <exception cref="System.ArgumentNullException">request、providerName 或 profileId 为空</exception>
        /// <exception cref="System.ArgumentException">profileId 格式无效</exception>
        /// <exception cref="System.InvalidOperationException">Provider 未注册</exception>
        /// <exception cref="NexusTenantException">配置加载失败（NXC201）或其他租户错误</exception>
        Task<TResponse> ExecuteAsync<TResponse>(
            IApiRequest<TResponse> request,
            string providerName,
            string profileId,
            CancellationToken ct = default)
            where TResponse : class, new();
    }
}
