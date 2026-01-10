// Copyright (c) 2025-2026 NexusContract. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NexusContract.Abstractions.Contracts;
using NexusContract.Abstractions.Exceptions;
using NexusContract.Abstractions.Policies;
using NexusContract.Abstractions.Security;
using NexusContract.Core.Hydration;
using NexusContract.Core.Projection;
using NexusContract.Core.Reflection;

namespace NexusContract.Core
{
    /// <summary>
    /// 执行上下文：包含操作元数据
    /// </summary>
    /// <param name="OperationId">操作标识（从 [ApiOperation] 属性提取），用于三方 API 选择证书、签名算法等动态参数</param>
    public sealed record ExecutionContext(string? OperationId = null);

    /// <summary>
    /// 【决策 A-501】NexusGateway（支付网关唯一门面）
    /// 
    /// 职责：自动编排出入链路（出向投影 + 回向回填）。
    /// 工作流：验证 → 投影 → async 执行 → 回填 → 异常转译。
    /// 
    /// 设计原则：
    /// - 每个微服务对接一个支付平台（遵循微服务单一职责）
    /// - Provider 层封装平台特定逻辑（签名、命名策略、加密）
    /// - Gateway 提供通用引擎（投影、回填、验证）
    /// 
    /// 纯异步设计（无同步版本）的理由：
    /// - 避免线程池耗尽（高延迟场景：若 2s 平均响应 × 400 TPS，同步需 800 线程）
    /// - 线程复用效率高、GC 压力低、代码流可控
    /// - 防止 AI 生成代码时意外产生死锁
    /// 
    /// 为什么用 ConfigureAwait(false)？
    /// - 避免切换回 UI 线程（支付系统总是后端，无 UI 线程）
    /// - 继续使用线程池线程，无上下文切换开销
    /// 
    /// 禁止项：
    /// - 不允许在 Provider 中用 .Wait() 或 .Result 来同步等待
    /// - 如果用户的 executorAsync 返回了 Task.FromResult()（假异步）
    ///   → 系统能正常工作，但性能损失
    ///   → ContractValidator 应该在审计时警告
    /// 
    /// 约束条件：
    /// - 没有 ExecuteSync() 方法（强制异步）
    /// - 没有 Task.Wait() 的使用（强制 await）
    /// - 所有 Provider 实现必须是真正的异步（不是 Task.FromResult() 伪装）
    /// </summary>
    public sealed class NexusGateway
    {
        private readonly ProjectionEngine _projectionEngine;
        private readonly ResponseHydrationEngine _hydrationEngine;

        /// <summary>
        /// 初始化网关
        /// </summary>
        /// <param name="namingPolicy">字段名转换策略（必需）</param>
        /// <param name="encryptor">加密器（可选）</param>
        /// <param name="decryptor">解密器（可选）</param>
        public NexusGateway(
            INamingPolicy namingPolicy,
            IEncryptor? encryptor = null,
            IDecryptor? decryptor = null)
        {
            if (namingPolicy == null)
                throw new ArgumentNullException(nameof(namingPolicy));

            _projectionEngine = new ProjectionEngine(namingPolicy, encryptor);
            _hydrationEngine = new ResponseHydrationEngine(namingPolicy, decryptor);
        }

        /// <summary>
        /// 唯一的、纯异步执行入口
        /// 
        /// 使用 IApiRequest&lt;TResponse&gt; 形式调用，编译器自动推断出 TResponse 类型。
        /// 调用者无需显式指定泛型参数。
        /// </summary>
        public async Task<TResponse> ExecuteAsync<TResponse>(
            IApiRequest<TResponse> request,
            Func<ExecutionContext, IDictionary<string, object>, Task<IDictionary<string, object>>> executorAsync,
            CancellationToken ct = default)
            where TResponse : class, new()
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (executorAsync == null)
                throw new ArgumentNullException(nameof(executorAsync));

            try
            {
                Type requestType = request.GetType();

                // 1. 验证契约（缓存后极快）
                ContractMetadata metadata = NexusContractMetadataRegistry.Instance.GetMetadata(requestType);
                string? operationId = metadata.Operation?.OperationId;

                // 2. 投影请求
                IDictionary<string, object> projectedRequest = _projectionEngine.Project<object>(request);

                // 3. 异步执行（线程于此释放回线程池）
                ExecutionContext executionContext = new ExecutionContext(operationId);
                IDictionary<string, object> responseDict = await executorAsync(executionContext, projectedRequest).ConfigureAwait(false);

                // 4. 回填响应
                TResponse response = _hydrationEngine.Hydrate<TResponse>(responseDict);

                return response;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (ContractIncompleteException ex)
            {
                ThrowDiagnosticException(ex);
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"[NexusGateway.ExecuteAsync] Unexpected error during request execution.",
                    ex);
            }
        }

        /// <summary>
        /// 仅投影（用于需要单向序列化的场景）
        /// </summary>
        public IDictionary<string, object> Project<TContract>(TContract contract)
            where TContract : notnull
        {
            if (contract == null)
                throw new ArgumentNullException(nameof(contract));

            try
            {
                NexusContractMetadataRegistry.Instance.GetMetadata(typeof(TContract));
                return _projectionEngine.Project<TContract>(contract);
            }
            catch (ContractIncompleteException ex)
            {
                ThrowDiagnosticException(ex);
                throw;
            }
        }

        /// <summary>
        /// 仅回填（用于需要单向反序列化的场景）
        /// </summary>
        public TResponse Hydrate<TResponse>(IDictionary<string, object> source)
            where TResponse : new()
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            try
            {
                return _hydrationEngine.Hydrate<TResponse>(source);
            }
            catch (ContractIncompleteException ex)
            {
                ThrowDiagnosticException(ex);
                throw;
            }
        }

        /// <summary>
        /// 诊断异常输出（用于日志系统集成）
        /// </summary>
        private static void ThrowDiagnosticException(ContractIncompleteException ex)
        {
            IDictionary<string, object> diagnosticData = ex.GetDiagnosticData();
            string category = ContractDiagnosticRegistry.GetCategory(ex.ErrorCode);
            string severity = ContractDiagnosticRegistry.GetSeverity(ex.ErrorCode);

            // 这里可以接入日志系统
            // logger.LogError(new Dictionary<string, object>
            // {
            //     { "category", category },
            //     { "severity", severity },
            //     { "diagnostic_data", diagnosticData }
            // });
        }
    }
}


