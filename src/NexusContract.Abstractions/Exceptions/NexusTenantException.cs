// Copyright (c) 2025-2026 NexusContract. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;

namespace NexusContract.Abstractions.Exceptions
{
    /// <summary>
    /// Nexus 租户异常：租户上下文解析或验证失败
    /// 
    /// 典型场景：
    /// - HTTP Headers 中缺少租户标识（X-Realm-Id, X-Profile-Id）
    /// - 租户标识格式无效（非法字符、长度超限）
    /// - 配置解析器找不到对应的租户配置
    /// - 租户权限校验失败（如 IP 白名单、证书过期）
    /// 
    /// HTTP 状态码建议：403 Forbidden
    /// 错误代码：TENANT_INVALID, TENANT_NOT_FOUND, TENANT_UNAUTHORIZED
    /// </summary>
    public sealed class NexusTenantException : Exception
    {
        /// <summary>
        /// 错误代码（用于客户端识别）
        /// </summary>
        public string ErrorCode { get; }

        /// <summary>
        /// 租户标识（用于诊断）
        /// </summary>
        public string TenantIdentifier { get; }

        /// <summary>
        /// 构造租户异常（基础版本）
        /// </summary>
        /// <param name="message">错误消息</param>
        public NexusTenantException(string message)
            : base(message)
        {
            ErrorCode = "TENANT_INVALID";
            TenantIdentifier = string.Empty;
        }

        /// <summary>
        /// 构造租户异常（带内部异常）
        /// </summary>
        /// <param name="message">错误消息</param>
        /// <param name="innerException">内部异常</param>
        public NexusTenantException(string message, Exception innerException)
            : base(message, innerException)
        {
            ErrorCode = "TENANT_INVALID";
            TenantIdentifier = string.Empty;
        }

        /// <summary>
        /// 构造租户异常（完整版本）
        /// </summary>
        /// <param name="message">错误消息</param>
        /// <param name="errorCode">错误代码</param>
        /// <param name="tenantIdentifier">租户标识</param>
        public NexusTenantException(string message, string errorCode, string tenantIdentifier = null)
            : base(message)
        {
            ErrorCode = errorCode ?? "TENANT_INVALID";
            TenantIdentifier = tenantIdentifier ?? string.Empty;
        }

        /// <summary>
        /// 构造租户异常（完整版本 + 内部异常）
        /// </summary>
        /// <param name="message">错误消息</param>
        /// <param name="errorCode">错误代码</param>
        /// <param name="tenantIdentifier">租户标识</param>
        /// <param name="innerException">内部异常</param>
        public NexusTenantException(
            string message,
            string errorCode,
            string tenantIdentifier,
            Exception innerException)
            : base(message, innerException)
        {
            ErrorCode = errorCode ?? "TENANT_INVALID";
            TenantIdentifier = tenantIdentifier ?? string.Empty;
        }

        /// <summary>
        /// 工厂方法：租户未找到
        /// </summary>
        public static NexusTenantException NotFound(string tenantIdentifier)
        {
            return new NexusTenantException(
                $"Tenant not found: {tenantIdentifier}",
                "TENANT_NOT_FOUND",
                tenantIdentifier);
        }

        /// <summary>
        /// 工厂方法：租户未授权
        /// </summary>
        public static NexusTenantException Unauthorized(string tenantIdentifier, string reason = null)
        {
            string message = reason != null
                ? $"Tenant unauthorized: {tenantIdentifier}. Reason: {reason}"
                : $"Tenant unauthorized: {tenantIdentifier}";

            return new NexusTenantException(
                message,
                "TENANT_UNAUTHORIZED",
                tenantIdentifier);
        }

        /// <summary>
        /// 工厂方法：租户标识无效
        /// </summary>
        public static NexusTenantException InvalidIdentifier(string fieldName, string value)
        {
            return new NexusTenantException(
                $"Invalid tenant identifier: {fieldName}={value}",
                "TENANT_INVALID",
                $"{fieldName}:{value}");
        }

        /// <summary>
        /// 工厂方法：缺少租户标识
        /// </summary>
        public static NexusTenantException MissingIdentifier(string fieldName)
        {
            return new NexusTenantException(
                $"Missing required tenant identifier: {fieldName}",
                "TENANT_MISSING",
                fieldName);
        }
    }
}
