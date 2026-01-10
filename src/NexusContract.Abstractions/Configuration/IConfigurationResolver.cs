// Copyright (c) 2025-2026 NexusContract. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using NexusContract.Abstractions.Contracts;
using NexusContract.Abstractions.Exceptions;

namespace NexusContract.Abstractions.Configuration
{
    /// <summary>
    /// 配置解析器接口：将业务身份映射为物理配置（JIT 模式）
    /// 
    /// 职责：
    /// - 根据业务身份（ProviderName + RealmId + ProfileId）查询物理配置
    /// - 支持多层缓存（L1 内存 + L2 Redis）
    /// - 支持配置热更新（无需重启服务）
    /// - 提供配置失效通知机制
    /// 
    /// 典型实现：
    /// 1. **InMemoryConfigResolver**：纯内存实现（用于测试）
    /// 2. **HybridConfigResolver**：L1/L2 缓存 + 数据库回源
    /// 3. **RedisConfigResolver**：Redis 单层缓存
    /// 4. **DatabaseConfigResolver**：直接查询数据库（无缓存）
    /// 
    /// 缓存策略建议：
    /// - L1 内存缓存：TTL 5 分钟，最多 1000 个配置
    /// - L2 Redis 缓存：TTL 30 分钟
    /// - 配置更新时主动刷新缓存（Pub/Sub）
    /// 
    /// 性能考量：
    /// - 冷启动：首次请求需要查询数据库（~100ms）
    /// - 热路径：L1 缓存命中（<1ms）
    /// - L2 缓存命中：Redis 查询（~5ms）
    /// - 并发场景：使用 SemaphoreSlim 防止缓存击穿
    /// 
    /// 使用示例：
    /// <code>
    /// // 在 Provider 中使用
    /// var configCtx = new ConfigurationContext("Alipay", tenantCtx.RealmId)
    /// {
    ///     ProfileId = tenantCtx.ProfileId
    /// };
    /// 
    /// var settings = await _configResolver.ResolveAsync(configCtx, ct);
    /// 
    /// // 使用配置
    /// var signedRequest = SignRequest(request, settings.PrivateKey);
    /// </code>
    /// </summary>
    public interface IConfigurationResolver
    {
        /// <summary>
        /// JIT 解析配置（支持 L1/L2 缓存）
        /// 
        /// 工作流：
        /// 1. 查询 L1 内存缓存 → 命中则返回
        /// 2. 查询 L2 Redis 缓存 → 命中则更新 L1 并返回
        /// 3. 查询数据库（ITenantRepository）→ 更新 L1/L2 并返回
        /// 4. 未找到配置 → 抛出 NexusTenantException.NotFound
        /// 
        /// 并发控制：
        /// - 使用 SemaphoreSlim 防止缓存击穿（同一配置并发查询）
        /// - 首个请求查询数据库，后续请求等待结果
        /// 
        /// 异常处理：
        /// - 配置未找到：抛出 NexusTenantException.NotFound
        /// - 配置无效：抛出 NexusTenantException.InvalidIdentifier
        /// - 数据库异常：抛出原始异常（由调用方处理）
        /// </summary>
        /// <param name="identity">租户身份标识（包含 Provider + Realm + Profile）</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>Provider 物理配置（含私钥）</returns>
        /// <exception cref="NexusTenantException">配置未找到或无效</exception>
        Task<IProviderConfiguration> ResolveAsync(
            ITenantIdentity identity,
            CancellationToken ct = default);

        /// <summary>
        /// 刷新配置缓存（可选方法）
        /// 
        /// 使用场景：
        /// - 管理后台更新商户配置后主动刷新
        /// - Redis Pub/Sub 收到配置变更通知
        /// - 定时任务批量刷新即将过期的配置
        /// 
        /// 注意：不是所有实现都支持此方法
        /// - InMemoryConfigResolver：支持（清除内存缓存）
        /// - HybridConfigResolver：支持（清除 L1/L2 缓存）
        /// - DatabaseConfigResolver：不支持（无缓存）
        /// </summary>
        /// <param name="identity">租户身份标识</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>刷新任务</returns>
        Task RefreshAsync(
            ITenantIdentity identity,
            CancellationToken ct = default);

        /// <summary>
        /// 预热配置缓存（可选方法）
        /// 
        /// 使用场景：
        /// - 服务启动时批量加载热点商户配置
        /// - 降低冷启动延迟
        /// 
        /// 实现建议：
        /// - 从数据库查询所有活跃商户配置
        /// - 批量写入 L1/L2 缓存
        /// - 异步执行，不阻塞启动流程
        /// </summary>
        /// <param name="ct">取消令牌</param>
        /// <returns>预热任务</returns>
        Task WarmupAsync(CancellationToken ct = default);
    }
}
