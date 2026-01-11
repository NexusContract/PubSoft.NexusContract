// Copyright (c) 2025-2026 NexusContract. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using NexusContract.Abstractions.Configuration;
using NexusContract.Abstractions.Contracts;
using NexusContract.Abstractions.Core;
using NexusContract.Abstractions.Exceptions;
using NexusContract.Abstractions.Providers;

namespace NexusContract.Core.Engine
{
    /// <summary>
    /// NexusEngine：ISV 多租户调度引擎
    /// 
    /// 核心职责：
    /// 1. JIT 配置加载：通过 IConfigurationResolver 动态加载租户配置
    /// 2. Provider 路由：根据配置中的实现标签选择 Provider
    /// 3. 请求执行：调用 Provider.ExecuteAsync 执行业务逻辑
    /// 4. 异常处理：统一异常转换（ContractIncompleteException, NexusGatewayException）
    /// 
    /// 路由策略（配置驱动，优先级从高到低）：
    /// 1. 配置实现标签：settings.ExtendedSettings["ImplementationName"]（如 "Alipay.Cert" / "Alipay.RSA"）
    /// 2. 基础渠道名称：identity.ProviderName（"Alipay" / "WeChat"）
    /// 
    /// 设计理念：
    /// - 先加载配置，再决定路由（配置决定执行路径）
    /// - 支持同一渠道多版本实现（如 RSA vs 证书版本）
    /// - 支持按租户、按环境的灵活路由（无需修改代码）
    /// 
    /// 性能特征：
    /// - 冷路径：~100ms（首次配置加载 + Provider 初始化）
    /// - 热路径：~10ms（L1 缓存命中 + Provider 执行）
    /// - 并发能力：无状态设计，支持高并发（受限于 Provider 实现）
    /// 
    /// 使用示例：
    /// <code>
    /// // 注册 Provider 和 ConfigResolver
    /// var engine = new NexusEngine(configResolver);
    /// engine.RegisterProvider("Alipay", new AlipayProvider());
    /// engine.RegisterProvider("WeChat", new WeChatProvider());
    /// 
    /// // 执行请求
    /// var tenantContext = new TenantContext("2088123456789012", "2021001234567890")
    ///     .WithProvider("Alipay");
    /// 
    /// var response = await engine.ExecuteAsync(payRequest, tenantContext);
    /// </code>
    /// 
    /// 设计约束：
    /// - Provider 必须无状态（配置通过方法参数传递）
    /// - 线程安全（使用 ConcurrentDictionary 减少并发问题，但在高并发场景下应进行验证）
    /// - 支持运行时动态注册 Provider
    /// </summary>
    /// <remarks>
    /// 构造 NexusEngine
    /// </remarks>
    /// <param name="configResolver">配置解析器</param>
    public sealed class NexusEngine : INexusEngine
    {
        private readonly IConfigurationResolver _configResolver;
        private readonly ConcurrentDictionary<string, IProvider> _providerRegistry = new ConcurrentDictionary<string, IProvider>(StringComparer.OrdinalIgnoreCase);

        public NexusEngine(IConfigurationResolver configResolver)
        {
            NexusGuard.EnsurePhysicalAddress(configResolver);
            _configResolver = configResolver;
        }

        /// <summary>
        /// 注册 Provider
        /// </summary>
        /// <param name="providerName">Provider 标识（如 "Alipay"）</param>
        /// <param name="provider">Provider 实例</param>
        public void RegisterProvider(string providerName, IProvider provider)
        {
            NexusGuard.EnsureNonEmptyString(providerName);
            NexusGuard.EnsurePhysicalAddress(provider);

            _providerRegistry[providerName] = provider;
        }

        /// <summary>
        /// 注销 Provider
        /// </summary>
        /// <param name="providerName">Provider 标识</param>
        /// <returns>是否注销成功</returns>
        public bool UnregisterProvider(string providerName)
        {
            if (string.IsNullOrWhiteSpace(providerName))
                return false;

            return _providerRegistry.TryRemove(providerName, out _);
        }

        /// <summary>
        /// 执行多租户请求（显式参数）
        /// </summary>
        /// <typeparam name="TResponse">响应类型</typeparam>
        /// <param name="request">API 请求</param>
        /// <param name="providerName">提供商名称</param>
        /// <param name="profileId">配置文件 ID（必填，禁止自动补全）</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>API 响应</returns>
        public async Task<TResponse> ExecuteAsync<TResponse>(
            IApiRequest<TResponse> request,
            string providerName,
            string profileId,
            CancellationToken ct = default)
            where TResponse : class, new()
        {
            NexusGuard.EnsurePhysicalAddress(request);
            NexusGuard.EnsureNonEmptyString(providerName);
            NexusGuard.EnsureNonEmptyString(profileId);

            try
            {
                // 1. JIT 配置加载（先加载配置，配置决定后续路由）
                var configuration = await _configResolver.ResolveAsync(providerName, profileId, ct)
                    .ConfigureAwait(false);

                // 2. Provider 路由（基于配置的实现标签动态决策）
                string implementationKey = ResolveImplementationName(providerName, configuration);
                var provider = GetProvider(implementationKey);

                // 3. 执行请求
                var response = await provider.ExecuteAsync<TResponse>(request, configuration, ct)
                    .ConfigureAwait(false);

                return response;
            }
            catch (Exception ex) when (!(ex is ArgumentNullException))
            {
                // 未知异常：包装为 ContractIncompleteException
                throw new ContractIncompleteException(
                    nameof(NexusEngine),
                    ContractDiagnosticRegistry.NXC201,
                    $"Request execution failed for profileId '{profileId}' on provider '{providerName}': {ex.Message}",
                    ex);
            }
        }

        /// <summary>
        /// 解析实现名称（配置驱动路由）
        /// 
        /// 策略：
        /// 1. 优先读取配置中的 ImplementationName（如 "Alipay.Cert"）
        /// 2. 回退到方法参数中的 providerName（如 "Alipay"）
        /// 
        /// 使用场景：
        /// - 同一渠道多版本并存：
        ///   * Alipay.RSA（RSA 签名，普通商户）
        ///   * Alipay.Cert（证书签名，ISV 服务商）
        /// - 灰度切换：
        ///   * 通过配置修改 ImplementationName 实现平滑切换
        /// - 环境隔离：
        ///   * Alipay.Sandbox（沙箱环境）
        ///   * Alipay.Production（生产环境）
        /// </summary>
        /// <param name="providerName">提供商名称</param>
        /// <param name="configuration">已加载的配置</param>
        /// <returns>Provider 实现名称</returns>
        private string ResolveImplementationName(
            string providerName,
            IProviderConfiguration configuration)
        {
            // 策略 1: 配置中的具体实现标识（优先级最高）
            // 例如：ExtendedSettings["ImplementationName"] = "Alipay.Cert"
            string specificTag = configuration.GetExtendedSetting<string>("ImplementationName");
            if (!string.IsNullOrWhiteSpace(specificTag))
            {
                return specificTag;
            }

            // 策略 2: 回退到基础渠道名称
            // 例如：providerName = "Alipay"
            if (!string.IsNullOrWhiteSpace(providerName))
            {
                return providerName;
            }

            // 所有策略失败：视为缺失实现名（物理寻址缺失），由 NexusGuard 抛出 NXC
            NexusGuard.EnsureNonEmptyString(providerName);
            throw new InvalidOperationException("Cannot resolve ImplementationName from configuration or providerName");
        }

        /// <summary>
        /// 获取 Provider 实例
        /// </summary>
        private IProvider GetProvider(string providerName)
        {
            if (_providerRegistry.TryGetValue(providerName, out var provider))
            {
                return provider;
            }

            throw new InvalidOperationException(
                $"Provider '{providerName}' is not registered. Available providers: {string.Join(", ", _providerRegistry.Keys)}");
        }

        /// <summary>
        /// 获取已注册的 Provider 数量
        /// </summary>
        public int RegisteredProviderCount => _providerRegistry.Count;

        /// <summary>
        /// 检查 Provider 是否已注册
        /// </summary>
        public bool IsProviderRegistered(string providerName)
        {
            return !string.IsNullOrWhiteSpace(providerName) &&
                   _providerRegistry.ContainsKey(providerName);
        }
    }
}
