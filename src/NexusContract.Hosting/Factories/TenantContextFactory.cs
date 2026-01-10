// Copyright (c) 2025-2026 NexusContract. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using NexusContract.Abstractions.Contracts;
using NexusContract.Abstractions.Exceptions;

namespace NexusContract.Hosting.Factories
{
    /// <summary>
    /// 租户上下文工厂：从 HTTP 请求中提取租户标识
    /// 
    /// 职责：处理"协议相关"到"协议无关"的转换
    /// - 输入：ASP.NET Core 的 HttpContext（协议相关）
    /// - 输出：NexusContract 的 TenantContext（协议无关）
    /// 
    /// 提取策略（优先级从高到低）：
    /// 1. HTTP 请求头（X-Tenant-Realm / X-Tenant-Profile / X-Provider-Name）
    /// 2. HTTP 查询参数（?realm_id=xxx&amp;profile_id=xxx&amp;provider=xxx）
    /// 3. HTTP 请求体 JSON（{ "realm_id": "xxx", "profile_id": "xxx", "provider_name": "xxx" }）
    /// 
    /// 支持的标识符名称（跨平台兼容）：
    /// - RealmId: realm_id, sys_id, sp_mch_id（支付宝服务商模式）
    /// - ProfileId: profile_id, app_id, sub_mch_id（微信特约商户模式）
    /// - ProviderName: provider_name, provider, channel（渠道标识）
    /// 
    /// 使用示例：
    /// <code>
    /// // 在 NexusEndpointBase 中自动调用
    /// var tenantContext = await TenantContextFactory.CreateAsync(HttpContext);
    /// </code>
    /// 
    /// 设计约束：
    /// - 依赖 ASP.NET Core（Hosting 层允许的"污染"）
    /// - 使用 System.Text.Json（禁止 Newtonsoft.Json）
    /// - 无状态静态方法（每次请求重新提取）
    /// </summary>
    public static class TenantContextFactory
    {
        // 支持的标识符名称（小写，用于不区分大小写匹配）
        private static readonly HashSet<string> RealmIdAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            "realm_id", "realmid", "sys_id", "sysid", "sp_mch_id", "spmchid"
        };

        private static readonly HashSet<string> ProfileIdAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            "profile_id", "profileid", "app_id", "appid", "sub_mch_id", "submchid"
        };

        private static readonly HashSet<string> ProviderNameAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            "provider_name", "providername", "provider", "channel"
        };

        /// <summary>
        /// 从 HttpContext 异步提取租户上下文
        /// </summary>
        /// <param name="httpContext">HTTP 上下文</param>
        /// <returns>租户上下文</returns>
        /// <exception cref="NexusTenantException">租户标识缺失或无效</exception>
        public static async Task<TenantContext> CreateAsync(HttpContext httpContext)
        {
            if (httpContext == null)
                throw new ArgumentNullException(nameof(httpContext));

            string? realmId = null;
            string? profileId = null;
            string? providerName = null;

            // 1. 尝试从请求头提取
            realmId = ExtractFromHeaders(httpContext, RealmIdAliases, "X-Tenant-Realm", "X-RealmId");
            profileId = ExtractFromHeaders(httpContext, ProfileIdAliases, "X-Tenant-Profile", "X-ProfileId");
            providerName = ExtractFromHeaders(httpContext, ProviderNameAliases, "X-Provider-Name", "X-Provider");

            // 2. 尝试从查询参数提取（优先级低于请求头）
            if (string.IsNullOrEmpty(realmId))
                realmId = ExtractFromQuery(httpContext, RealmIdAliases);
            if (string.IsNullOrEmpty(profileId))
                profileId = ExtractFromQuery(httpContext, ProfileIdAliases);
            if (string.IsNullOrEmpty(providerName))
                providerName = ExtractFromQuery(httpContext, ProviderNameAliases);

            // 3. 尝试从请求体 JSON 提取（优先级最低）
            if (string.IsNullOrEmpty(realmId) || string.IsNullOrEmpty(profileId))
            {
                var (bodyRealmId, bodyProfileId, bodyProviderName) = await ExtractFromJsonBodyAsync(httpContext);
                realmId ??= bodyRealmId;
                profileId ??= bodyProfileId;
                providerName ??= bodyProviderName;
            }

            // 验证必需字段
            if (string.IsNullOrEmpty(realmId))
                throw NexusTenantException.MissingIdentifier("RealmId (sys_id / sp_mch_id)");
            if (string.IsNullOrEmpty(profileId))
                throw NexusTenantException.MissingIdentifier("ProfileId (app_id / sub_mch_id)");

            return new TenantContext
            {
                RealmId = realmId,
                ProfileId = profileId,
                ProviderName = providerName
            };
        }

        /// <summary>
        /// 从请求头提取标识符
        /// </summary>
        private static string? ExtractFromHeaders(
            HttpContext context,
            HashSet<string> aliases,
            params string[] knownHeaders)
        {
            // 优先检查标准请求头
            foreach (var header in knownHeaders)
            {
                if (context.Request.Headers.TryGetValue(header, out var value) &&
                    !string.IsNullOrWhiteSpace(value))
                {
                    return value.ToString().Trim();
                }
            }

            // 回退：遍历所有请求头查找别名
            foreach (var kvp in context.Request.Headers)
            {
                if (aliases.Contains(kvp.Key) && !string.IsNullOrWhiteSpace(kvp.Value))
                {
                    return kvp.Value.ToString().Trim();
                }
            }

            return null;
        }

        /// <summary>
        /// 从查询参数提取标识符
        /// </summary>
        private static string? ExtractFromQuery(HttpContext context, HashSet<string> aliases)
        {
            foreach (var kvp in context.Request.Query)
            {
                if (aliases.Contains(kvp.Key) && !string.IsNullOrWhiteSpace(kvp.Value))
                {
                    return kvp.Value.ToString().Trim();
                }
            }

            return null;
        }

        /// <summary>
        /// 从请求体 JSON 异步提取标识符
        /// </summary>
        private static async Task<(string? realmId, string? profileId, string? providerName)> ExtractFromJsonBodyAsync(HttpContext context)
        {
            try
            {
                // 检查 Content-Type
                if (!context.Request.HasJsonContentType())
                    return (null, null, null);

                // 启用缓冲（允许多次读取）
                context.Request.EnableBuffering();
                context.Request.Body.Position = 0;

                // 使用 System.Text.Json 解析
                using var jsonDoc = await JsonDocument.ParseAsync(
                    context.Request.Body,
                    cancellationToken: context.RequestAborted);

                context.Request.Body.Position = 0; // 重置流位置供后续读取

                var root = jsonDoc.RootElement;

                var realmId = FindJsonValue(root, RealmIdAliases);
                var profileId = FindJsonValue(root, ProfileIdAliases);
                var providerName = FindJsonValue(root, ProviderNameAliases);

                return (realmId, profileId, providerName);
            }
            catch
            {
                // JSON 解析失败：忽略请求体提取
                return (null, null, null);
            }
        }

        /// <summary>
        /// 从 JSON 元素中查找匹配别名的值
        /// </summary>
        private static string? FindJsonValue(JsonElement element, HashSet<string> aliases)
        {
            if (element.ValueKind != JsonValueKind.Object)
                return null;

            foreach (var property in element.EnumerateObject())
            {
                if (aliases.Contains(property.Name) &&
                    property.Value.ValueKind == JsonValueKind.String)
                {
                    var value = property.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                        return value.Trim();
                }
            }

            return null;
        }
    }
}
