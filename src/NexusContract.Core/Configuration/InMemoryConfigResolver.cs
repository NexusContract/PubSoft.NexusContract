// Copyright (c) 2025-2026 NexusContract. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NexusContract.Abstractions.Configuration;
using NexusContract.Abstractions.Contracts;
using NexusContract.Abstractions.Exceptions;
using NexusContract.Core.Configuration;

namespace NexusContract.Core.Configuration
{
    /// <summary>
    /// 内存配置解析器：用于测试和开发环境的简单实现
    /// 
    /// 特性：
    /// - 纯内存存储（ConcurrentDictionary）
    /// - 无外部依赖（不需要数据库或 Redis）
    /// - 支持动态添加/更新/删除配置
    /// - 线程安全（使用 ConcurrentDictionary 可减少并发问题，但请在高并发场景下进行验证）
    /// - 文件监控（可选，支持从 JSON 文件热加载）
    /// 
    /// 适用场景：
    /// - 单元测试（Mock 配置）
    /// - 集成测试（预设测试配置）
    /// - 开发环境（快速启动，无需数据库）
    /// - Demo 演示（简化部署）
    /// 
    /// 不适用场景：
    /// - 生产环境（无持久化，服务重启配置丢失）
    /// - 多实例部署（每个实例独立内存，配置不同步）
    /// - 大量租户（内存占用线性增长）
    /// 
    /// 性能特征：
    /// - 查询延迟：极快（纯内存访问）
    /// - 内存占用：~1KB/配置（假设密钥 2048 位）
    /// - 并发能力：受限于内存与实现细节（ConcurrentDictionary 提供高效并发读写性能，但仍建议在目标负载下进行验证）
    /// </summary>
    public sealed class InMemoryConfigResolver : IConfigurationResolver, IDisposable
    {
        private readonly ConcurrentDictionary<string, ProviderSettings> _cache;
        private readonly FileSystemWatcher? _fileWatcher;
        private readonly string? _configFilePath;

        /// <summary>
        /// 构造内存配置解析器
        /// </summary>
        public InMemoryConfigResolver()
        {
            _cache = new ConcurrentDictionary<string, ProviderSettings>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 构造内存配置解析器（预设配置）
        /// </summary>
        /// <param name="presetConfigurations">预设配置列表</param>
        public InMemoryConfigResolver(IEnumerable<ProviderSettings> presetConfigurations)
        {
            _cache = new ConcurrentDictionary<string, ProviderSettings>(StringComparer.OrdinalIgnoreCase);

            if (presetConfigurations != null)
            {
                foreach (var config in presetConfigurations)
                {
                    string key = BuildCacheKey(config.ProviderName, config.AppId);
                    _cache[key] = config;
                }
            }
        }

        /// <summary>
        /// 构造内存配置解析器（从文件加载并监控变化）
        /// </summary>
        /// <param name="configFilePath">配置文件路径（JSON 格式）</param>
        /// <param name="enableHotReload">是否启用热更新（默认 true）</param>
        /// <exception cref="FileNotFoundException">配置文件不存在</exception>
        /// <exception cref="JsonException">JSON 格式无效</exception>
        public InMemoryConfigResolver(string configFilePath, bool enableHotReload = true)
        {
            if (string.IsNullOrWhiteSpace(configFilePath))
                throw new ArgumentNullException(nameof(configFilePath));

            if (!File.Exists(configFilePath))
                throw new FileNotFoundException($"Configuration file not found: {configFilePath}");

            _cache = new ConcurrentDictionary<string, ProviderSettings>(StringComparer.OrdinalIgnoreCase);
            _configFilePath = configFilePath;

            // 初始加载
            LoadConfigurationsFromFile();

            // 启用文件监控（热更新）
            if (enableHotReload)
            {
                string? directory = Path.GetDirectoryName(configFilePath);
                string fileName = Path.GetFileName(configFilePath);

                if (!string.IsNullOrEmpty(directory))
                {
                    _fileWatcher = new FileSystemWatcher(directory, fileName)
                    {
                        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                        EnableRaisingEvents = true
                    };

                    _fileWatcher.Changed += OnConfigFileChanged;
                }
            }
        }

        /// <summary>
        /// JIT 解析配置（内存查询）
        /// </summary>
        public Task<IProviderConfiguration> ResolveAsync(
            string providerName,
            string profileId,
            CancellationToken ct = default)
        {
            // 防御性校验：确保物理地址完整（宪法 002/003）
            NexusGuard.EnsurePhysicalAddress(providerName, profileId, nameof(InMemoryConfigResolver));

            string key = BuildCacheKey(providerName, profileId);

            if (_cache.TryGetValue(key, out var settings))
            {
                return Task.FromResult<IProviderConfiguration>(settings);
            }

            // 配置未找到
            throw NexusTenantException.NotFound(
                $"{providerName}:{profileId}");
        }

        /// <summary>
        /// 刷新配置缓存（内存实现：删除指定配置）
        /// </summary>
        public Task RefreshAsync(
            string providerName,
            string profileId,
            CancellationToken ct = default)
        {
            // 防御性校验：确保物理地址完整（宪法 002/003）
            NexusGuard.EnsurePhysicalAddress(providerName, profileId, nameof(InMemoryConfigResolver));

            string key = BuildCacheKey(providerName, profileId);
            _cache.TryRemove(key, out _);

            return Task.CompletedTask;
        }

        /// <summary>
        /// 预热配置缓存（内存实现：无操作）
        /// </summary>
        public Task WarmupAsync(CancellationToken ct = default)
        {
            // 内存实现无需预热
            return Task.CompletedTask;
        }

        /// <summary>
        /// 添加或更新配置
        /// </summary>
        /// <param name="settings">Provider 配置</param>
        public void AddOrUpdate(ProviderSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            string key = BuildCacheKey(settings.ProviderName, settings.AppId);
            _cache[key] = settings;
        }

        /// <summary>
        /// 删除配置
        /// </summary>
        /// <param name="providerName">Provider 标识</param>
        /// <param name="profileId">档案标识</param>
        /// <returns>是否删除成功</returns>
        public bool Remove(string providerName, string profileId)
        {
            string key = BuildCacheKey(providerName, profileId);
            return _cache.TryRemove(key, out _);
        }

        /// <summary>
        /// 清空所有配置
        /// </summary>
        public void Clear()
        {
            _cache.Clear();
        }

        /// <summary>
        /// 获取所有配置（用于诊断）
        /// ⚠️ 生产环境：私钥已脱敏（前4位+***+后4位）
        /// ⚠️ DEBUG 模式：返回完整私钥
        /// </summary>
        public IReadOnlyCollection<ProviderSettings> GetAll()
        {
#if DEBUG
            // DEBUG 模式：返回完整配置（包括私钥）
            return _cache.Values.ToList();
#else
            // 生产模式：脱敏私钥
            return _cache.Values.Select(MaskSensitiveData).ToList();
#endif
        }

        /// <summary>
        /// 获取配置数量
        /// </summary>
        public int Count => _cache.Count;

        /// <summary>
        /// 构建缓存键（Provider:Profile）
        /// </summary>
        private static string BuildCacheKey(string providerName, string profileId)
        {
            // 格式: "Alipay:2021001234567890"
            return $"{providerName}:{profileId}";
        }

        /// <summary>
        /// 从文件加载配置
        /// </summary>
        private void LoadConfigurationsFromFile()
        {
            if (string.IsNullOrEmpty(_configFilePath))
                return;

            try
            {
                string json = File.ReadAllText(_configFilePath);
                var configs = JsonSerializer.Deserialize<List<ProviderSettings>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (configs != null)
                {
                    _cache.Clear();
                    foreach (var config in configs)
                    {
                        // 验证配置完整性
                        if (!config.Validate(out string? error))
                        {
                            throw new InvalidOperationException(
                                $"Invalid configuration in file: {error}");
                        }

                        string key = BuildCacheKey(config.ProviderName, config.AppId);
                        _cache[key] = config;
                    }
                }
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException(
                    $"Failed to parse configuration file: {_configFilePath}", ex);
            }
        }

        /// <summary>
        /// 文件变化事件处理（热更新）
        /// </summary>
        private void OnConfigFileChanged(object sender, FileSystemEventArgs e)
        {
            try
            {
                // 延迟加载（避免文件锁定）
                Thread.Sleep(100);
                LoadConfigurationsFromFile();
            }
            catch
            {
                // 静默失败（避免热更新影响服务稳定性）
            }
        }

        /// <summary>
        /// 脱敏敏感数据（用于诊断输出）
        /// </summary>
        private static ProviderSettings MaskSensitiveData(ProviderSettings original)
        {
            string maskedPrivateKey = MaskString(original.PrivateKey);
            string maskedPublicKey = MaskString(original.PublicKey);

            return new ProviderSettings(
                original.ProviderName,
                original.AppId,
                original.MerchantId,
                maskedPrivateKey,
                maskedPublicKey,
                original.GatewayUrl,
                original.IsSandbox,
                new Dictionary<string, object>(original.ExtendedSettings)
            );
        }

        /// <summary>
        /// 字符串脱敏（显示前4位+***+后4位）
        /// </summary>
        private static string MaskString(string input)
        {
            if (string.IsNullOrEmpty(input) || input.Length <= 8)
                return "***";

            return $"{input.Substring(0, 4)}***{input.Substring(input.Length - 4)}";
        }

        /// <summary>
        /// 释放资源（停止文件监控）
        /// </summary>
        public void Dispose()
        {
            _fileWatcher?.Dispose();
        }
    }
}
