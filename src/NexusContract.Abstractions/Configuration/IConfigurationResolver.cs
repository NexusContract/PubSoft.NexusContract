// Copyright (c) 2025-2026 NexusContract. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using NexusContract.Abstractions.Exceptions;

namespace NexusContract.Abstractions.Configuration
{
    /// <summary>
    /// 配置解析器接口：通过显式的 ProfileId 将业务身份映射为物理配置（JIT 模式）
    /// 
    /// 职责：
    /// - 根据业务身份（ProviderName + ProfileId）查询物理配置（O(1) 精确匹配）
    /// - 支持多层缓存（L1 内存 + L2 Redis）
    /// - 支持配置热更新（无需重启服务）
    /// - 提供配置失效通知机制
    /// 
    /// 宪法约束（月月红 003 - 物理槽位隔离）：
    /// - ProfileId 必须显式传入，禁止自动回填或默认值
    /// - Redis Key 格式严格为 `config:{provider}:{profileId}`
    /// - 所有查询都是 O(1) 精确匹配，无模糊搜索
    /// - 业务身份信息仅保留在审计日志中，不参与寻址
    /// 
    /// 典型实现：
    /// 1. **InMemoryConfigResolver**：纯内存实现（用于测试）
    /// 2. **HybridConfigResolver**：L1/L2 缓存 + Redis 回源
    /// 3. **RedisConfigResolver**：Redis 单层缓存
    /// 
    /// 缓存策略建议：
    /// - L1 内存缓存：滑动 24h + 绝对 30 天，优先级 NeverRemove
    /// - L2 Redis 缓存：永久存储（无 TTL）
    /// - 配置更新时通过 Pub/Sub 主动刷新
    /// 
    /// 性能特征：
    /// - 查询延迟：L1 命中 &lt;1μs，L2 命中 ~1ms，冷启动 ~5ms
    /// - 吞吐量：QPS 百万级（受限于 CPU，而非缓存）
    /// - 脱网运行：Redis 故障时可运行 30 天（L1 缓存）
    /// 
    /// 使用示例：
    /// <code>
    /// // 显式传递 profileId（从 URL 路由提取）
    /// var settings = await _configResolver.ResolveAsync("Alipay", profileId, ct);
    /// 
    /// // 使用配置
    /// var signedRequest = SignRequest(request, settings.PrivateKey);
    /// </code>
    /// </summary>
    public interface IConfigurationResolver
    {
        /// <summary>
        /// JIT 解析配置（O(1) 精确匹配，支持 L1/L2 缓存）
        /// 
        /// 工作流：
        /// 1. 查询 L1 内存缓存（Redis Key: `config:{provider}:{profileId}`）→ 命中则返回
        /// 2. 查询 L2 Redis 缓存 → 命中则回填 L1 并返回
        /// 3. 未找到配置 → 抛出 ContractIncompleteException（NXC201）
        /// 
        /// 必需约束：
        /// - `profileId` 必须非空且明确（禁止 null/empty）
        /// - 缺失 `profileId` 时应在调用端就拒绝（不应传入此方法）
        /// - Redis Key 格式必须严格为 `config:{provider}:{profileId}`
        /// 
        /// 异常处理：
        /// - 配置未找到：抛出 ContractIncompleteException（HTTP 404，NXC201）
        /// - 参数无效：抛出 ArgumentException（HTTP 400，NXC201）
        /// </summary>
        /// <param name="providerName">Provider 标识（例如 "Alipay", "WeChat"）</param>
        /// <param name="profileId">档案标识（显式必填，禁止 null/empty）</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>Provider 物理配置（含私钥）</returns>
        /// <exception cref="ContractIncompleteException">配置未找到（NXC201）或参数无效</exception>
        Task<IProviderConfiguration> ResolveAsync(
            string providerName,
            string profileId,
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
        /// </summary>
        /// <param name="providerName">Provider 标识</param>
        /// <param name="profileId">档案标识</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>刷新任务</returns>
        Task RefreshAsync(
            string providerName,
            string profileId,
            CancellationToken ct = default);

        /// <summary>
        /// 预热配置缓存（可选方法）
        /// 
        /// 使用场景：
        /// - 服务启动时批量加载热点商户配置
        /// - 降低冷启动延迟
        /// 
        /// 实现建议：
        /// - 从 Redis 批量扫描所有配置键
        /// - 批量加载到 L1 内存缓存
        /// - 异步执行，不阻塞启动流程
        /// </summary>
        /// <param name="ct">取消令牌</param>
        /// <returns>预热任务</returns>
        Task WarmupAsync(CancellationToken ct = default);
    }
}
