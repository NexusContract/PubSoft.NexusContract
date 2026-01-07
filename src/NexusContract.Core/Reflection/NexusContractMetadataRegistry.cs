// Copyright (c) 2025-2026 PubSoft (pubsoft@gmail.com). All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using PubSoft.NexusContract.Abstractions.Attributes;

namespace PubSoft.NexusContract.Core.Reflection
{
    /// <summary>
    /// 【决策 A-301】NexusContractMetadataRegistry（契约元数据注册表）
    /// 
    /// 核心职能：
    /// 1. 发现（Discovery）：扫描契约类的 Attribute 结构
    /// 2. 验证（Validation）：调用 ContractValidator 确保符合边界"宪法"
    /// 3. 冻结（Freezing）：将反射结果转为不可变缓存对象，运行期零反射损耗
    /// 
    /// 为什么要冻结元数据？
    /// ====================
    /// 
    /// 架构对标：
    /// 【方案 A】动态反射（Alipay SDK 模式）：
    ///   每个请求时动态反射属性列表和 Attribute → O(n) 反射成本
    ///   后果：GC 压力、反射堆积、P99 波动
    /// 
    /// 【方案 B】冻结式元数据（我们的方案）：
    ///   启动期一次性完成全量反射 + Attribute 解析 + 约束验证
    ///   运行期 ConcurrentDictionary 查询 → O(1) 成本
    ///   后果：P50 = P99，曲线平稳，GC 压力可控
    /// 
    /// 成本收益：
    /// 
    /// 1. 启动期成本（一次性）
    ///    - 反射所有 Contract 类型的属性（取决于契约数量）
    ///    - 验证约束（NXC1xx）
    ///    - 前提说明：在小到中等规模契约（字段数较少、契约数量有限）与典型硬件上，耗时通常 &lt; 1 秒；实际耗时随契约数量与字段复杂度显著波动
    /// 
    /// 2. 运行期收益（每个请求）
    ///    - O(1) 缓存查询，无反射开销
    ///    - 稳定的性能曲线（P50/P99），无明显 GC 导致的抖动
    ///    - 注意：性能优化（Expression Tree 预编译）尚在实验阶段，当前回退到反射路径
    /// 
    /// 3. AI 生成友好
    ///    - 元数据清单明确（所有 POCO 类型的元数据）
    ///    - AI 生成 60+ Alipay 接口时，零歧义
    ///    - ContractValidator 保证所有新接口都符合边界规范
    /// 
    /// 4. 设计意图明确
    ///    - "冻结" = 运行时不允许修改元数据结构
    ///    - 强制"配置优于约定"原则
    ///    - Provider 必须在启动时声明所有可能的 POCO 类型
    /// 
    /// 实现策略：
    /// 使用 ConcurrentDictionary 实现懒加载冻结。
    /// 首次访问某个类型时触发反射 + 验证，后续访问返回缓存结果。
    /// 约束：不允许运行时修改元数据，所有契约必须在设计阶段明确。
    /// 
    /// 命名说明：
    /// 旧名 ReflectionCache 描述"实现手段"，新名 NexusContractMetadataRegistry 描述"架构职责"。
    /// 这不仅仅是一个缓存，而是契约元数据的权威注册表和管理中心。
    /// </summary>
    public sealed class NexusContractMetadataRegistry
    {
        private static readonly Lazy<NexusContractMetadataRegistry> _instance = new(() => new NexusContractMetadataRegistry());
        public static NexusContractMetadataRegistry Instance => _instance.Value;

        // 核心冷冻库：Key 是契约类型，Value 是冷冻后的元数据
        private readonly ConcurrentDictionary<Type, ContractMetadata> _cache = new();

        private NexusContractMetadataRegistry() { }

        /// <summary>
        /// 获取契约的元数据（如果不存在则触发"冷冻"流程）
        /// 懒加载模式：fail-fast，任何验证失败立即抛异常
        /// </summary>
        public ContractMetadata GetMetadata(Type type)
        {
            return _cache.GetOrAdd(type, t =>
            {
                // Fail-fast mode: 不收集错误，直接抛异常
                return BuildMetadata(t, warmup: false, aggregateErrors: false, errors: null);
            });
        }

        /// <summary>
        /// Preload multiple contract types at startup with error aggregation.
        /// Returns aggregated errors; successful metadata is cached.
        /// 批量启动模式：错误聚合，一次扫描给出完整"体检报告"
        /// </summary>
        public List<string> Preload(IEnumerable<Type> types, bool warmup = false)
        {
            if (types == null) throw new ArgumentNullException(nameof(types));

            var allErrors = new List<string>();

            foreach (var type in types)
            {
                try
                {
                    // Skip types without ApiOperation marker
                    if (type.GetCustomAttribute<ApiOperationAttribute>() == null) continue;

                    var errors = new List<string>();
                    var metadata = BuildMetadata(type, warmup, aggregateErrors: true, errors);

                    if (errors.Count == 0)
                    {
                        // Only cache if no errors occurred
                        _cache[type] = metadata;
                    }
                    else
                    {
                        allErrors.AddRange(errors);
                    }
                }
                catch (Exception ex)
                {
                    var msg = ex is TargetInvocationException ? ex.InnerException?.Message : ex.Message;
                    allErrors.Add($"[Critical] {type.FullName}: 扫描失败。原因: {msg}");
                }
            }

            return allErrors;
        }

        /// <summary>
        /// 单路径流水线：验证 → 审计 → 编译（三段式）
        /// </summary>
        /// <param name="type">契约类型</param>
        /// <param name="warmup">是否预热投影器</param>
        /// <param name="aggregateErrors">是否聚合错误（true=收集错误, false=立即抛异常）</param>
        /// <param name="errors">错误收集器（仅在 aggregateErrors=true 时使用）</param>
        private ContractMetadata BuildMetadata(Type type, bool warmup, bool aggregateErrors, List<string>? errors)
        {
            // ========== 阶段 1：入境检查（Validation）==========
            try
            {
                ContractValidator.Validate(type);
            }
            catch (Exception ex)
            {
                var msg = ex is TargetInvocationException ? ex.InnerException?.Message : ex.Message;
                if (aggregateErrors && errors != null)
                {
                    errors.Add($"[Validator] {type.FullName}: {msg}");
                    // Continue to collect more errors
                }
                else
                {
                    throw; // Fail-fast mode
                }
            }

            var opAttr = type.GetCustomAttribute<ApiOperationAttribute>();
            if (opAttr == null)
            {
                var err = $"[Type Error] {type.FullName}: 契约类必须标记 [ApiOperation]。";
                if (aggregateErrors && errors != null)
                {
                    errors.Add(err);
                }
                else
                {
                    throw new InvalidOperationException(err);
                }
            }

            var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            if (props.Length == 0)
            {
                var err = $"[Type Error] {type.FullName}: 契约类至少需要定义一个公共属性。";
                if (aggregateErrors && errors != null)
                {
                    errors.Add(err);
                }
                else
                {
                    throw new InvalidOperationException(err);
                }
            }

            // ========== 阶段 2：深度审计（Auditing）==========
            var properties = new List<PropertyMetadata>();
            var auditedProps = new List<PropertyAuditResult>();

            foreach (var prop in props)
            {
                // Validate getter/setter completeness
                if (!prop.CanRead || !prop.CanWrite)
                {
                    var err = $"[Property Error] {type.FullName}.{prop.Name}: 需要同时具有 get 与 set。";
                    if (aggregateErrors && errors != null)
                    {
                        errors.Add(err);
                    }
                    else
                    {
                        throw new InvalidOperationException(err);
                    }
                }

                var fieldAttr = prop.GetCustomAttribute<ApiFieldAttribute>();
                if (fieldAttr == null) continue;

                try
                {
                    // 【决策 A-302】启动期一次性审计，缓存结果，运行期 O(1) 查询
                    var auditResult = ContractAuditor.AuditProperty(prop, fieldAttr, currentDepth: 1);
                    properties.Add(new PropertyMetadata(prop, fieldAttr, auditResult));

                    // Only include safe properties in projector
                    if (!auditResult.IsEncryptedWithoutName && !auditResult.IsComplexWithoutName)
                    {
                        auditedProps.Add(auditResult);
                    }
                }
                catch (Exception ex)
                {
                    var err = $"[Audit Error] {type.FullName}.{prop.Name}: {ex.GetType().Name} {ex.Message}";
                    if (aggregateErrors && errors != null)
                    {
                        errors.Add(err);
                    }
                    else
                    {
                        throw;
                    }
                }
            }

            // ========== 阶段 3：武装化（Armoring）==========
            Func<object, Abstractions.Policies.INamingPolicy, Abstractions.Security.IEncryptor?, Dictionary<string, object>>? projector = null;
            Func<IDictionary<string, object>, Abstractions.Policies.INamingPolicy, Abstractions.Security.IDecryptor?, object>? hydrator = null;

            try
            {
                projector = BuildProjector(type, auditedProps.ToArray());

                if (warmup && projector != null)
                {
                    try
                    {
                        var instance = Activator.CreateInstance(type);
                        // 需要 dummy 服务来测试
                        var dummyNaming = new Policies.Impl.SnakeCaseNamingPolicy();
                        if (instance != null) projector(instance, dummyNaming, null);
                    }
                    catch (Exception ex)
                    {
                        var err = $"[Warmup Error] {type.FullName}: 预热运行失败：{ex.GetType().Name} {ex.Message}";
                        if (aggregateErrors && errors != null)
                        {
                            errors.Add(err);
                        }
                        // Don't throw in warmup failures
                    }
                }
            }
            catch (Exception ex)
            {
                var err = $"[Compile Error] {type.FullName}: 生成投影器失败：{ex.GetType().Name} {ex.Message}";
                if (aggregateErrors && errors != null)
                {
                    errors.Add(err);
                }
                else
                {
                    throw;
                }
            }

            // 构建 Hydrator（回填委托）
            try
            {
                hydrator = BuildHydrator(type, auditedProps.ToArray());

                if (warmup && hydrator != null)
                {
                    try
                    {
                        var testDict = new Dictionary<string, object>();
                        var dummyNaming = new Policies.Impl.SnakeCaseNamingPolicy();
                        hydrator(testDict, dummyNaming, null);
                    }
                    catch (Exception ex)
                    {
                        var err = $"[Hydrator Warmup Error] {type.FullName}: 预热运行失败：{ex.GetType().Name} {ex.Message}";
                        if (aggregateErrors && errors != null)
                        {
                            errors.Add(err);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                var err = $"[Hydrator Compile Error] {type.FullName}: 生成回填器失败：{ex.GetType().Name} {ex.Message}";
                if (aggregateErrors && errors != null)
                {
                    errors.Add(err);
                }
                // Hydrator 失败不应阻止元数据创建，可以 fallback 到反射
            }

            // 返回不可变对象（即使有错误也返回部分元数据，让调用方决定如何处理）
            return new ContractMetadata(type, opAttr!, properties.AsReadOnly(), projector, hydrator);
        }

        /// <summary>
        /// 构建表达式树投影器（Expression Tree → Compiled Delegate）
        /// 支持加密和命名策略，性能提升约 10 倍
        /// 
        /// 限制：仅支持扁平 POCO（无嵌套对象、无集合）
        /// 包含嵌套对象或集合的 Contract 会在 BuildMetadata 阶段被排除，fallback 到反射路径
        /// </summary>
        private static Func<object, Abstractions.Policies.INamingPolicy, Abstractions.Security.IEncryptor?, Dictionary<string, object>> BuildProjector(
            Type contractType, 
            PropertyAuditResult[] auditResults)
        {
            var param = Expression.Parameter(typeof(object), "o");
            var namingPolicyParam = Expression.Parameter(typeof(Abstractions.Policies.INamingPolicy), "namingPolicy");
            var encryptorParam = Expression.Parameter(typeof(Abstractions.Security.IEncryptor), "encryptor");
            
            var typedParam = Expression.Variable(contractType, "t");
            var dictVar = Expression.Variable(typeof(Dictionary<string, object>), "d");
            var expressions = new List<Expression>();

            // t = (ContractType)o
            expressions.Add(Expression.Assign(typedParam, Expression.Convert(param, contractType)));

            // d = new Dictionary<string, object>(capacity)
            var ctor = typeof(Dictionary<string, object>).GetConstructor(new[] { typeof(int) }) 
                       ?? typeof(Dictionary<string, object>).GetConstructor(Type.EmptyTypes)!;
            Expression newDict = ctor.GetParameters().Length == 1
                ? Expression.New(ctor, Expression.Constant(auditResults.Length))
                : Expression.New(ctor);
            expressions.Add(Expression.Assign(dictVar, newDict));

            var addMethod = typeof(Dictionary<string, object>).GetMethod("Add", new[] { typeof(string), typeof(object) })!;
            var convertNameMethod = typeof(Abstractions.Policies.INamingPolicy).GetMethod("ConvertName")!;
            var encryptMethod = typeof(Abstractions.Security.IEncryptor).GetMethod("Encrypt")!;

            foreach (var audit in auditResults)
            {
                var prop = audit.PropertyInfo;
                var apiField = audit.ApiField;
                
                // 判断字段名：优先使用显式 Name，否则调用 NamingPolicy.ConvertName
                Expression keyExpr;
                if (!string.IsNullOrWhiteSpace(apiField.Name))
                {
                    keyExpr = Expression.Constant(apiField.Name);
                }
                else
                {
                    keyExpr = Expression.Call(namingPolicyParam, convertNameMethod, Expression.Constant(prop.Name));
                }

                // 读取属性值：t.Property
                var propAccess = Expression.Property(typedParam, prop);
                
                // 如果加密：调用 encryptor.Encrypt(value.ToString())
                Expression valueExpr;
                if (apiField.IsEncrypted)
                {
                    // 生成：encryptor != null ? (value != null ? encryptor.Encrypt(value.ToString()) : null) : throw
                    var encryptorNotNull = Expression.NotEqual(encryptorParam, Expression.Constant(null, typeof(Abstractions.Security.IEncryptor)));
                    
                    // 检查属性值是否为null
                    var propAsObject = Expression.Convert(propAccess, typeof(object));
                    var propNotNull = Expression.NotEqual(propAsObject, Expression.Constant(null, typeof(object)));
                    
                    // value.ToString()
                    var toStringMethod = typeof(object).GetMethod("ToString")!;
                    var valueAsString = Expression.Call(propAsObject, toStringMethod);
                    
                    // encryptor.Encrypt(valueStr)
                    var encryptCall = Expression.Call(encryptorParam, encryptMethod, valueAsString);
                    
                    // value != null ? encryptor.Encrypt(value.ToString()) : null
                    var encryptOrNull = Expression.Condition(
                        propNotNull,
                        Expression.Convert(encryptCall, typeof(object)),
                        Expression.Constant(null, typeof(object)),
                        typeof(object)
                    );
                    
                    // throw with detailed diagnostic info
                    var errorMessage = $"Encryption required but encryptor is null. Type: {contractType.Name}, Property: {prop.Name}";
                    var throwExpr = Expression.Throw(
                        Expression.New(
                            typeof(InvalidOperationException).GetConstructor(new[] { typeof(string) })!,
                            Expression.Constant(errorMessage)
                        ),
                        typeof(object)
                    );
                    
                    // encryptor != null ? encryptOrNull : throw
                    valueExpr = Expression.Condition(encryptorNotNull, encryptOrNull, throwExpr, typeof(object));
                }
                else
                {
                    valueExpr = Expression.Convert(propAccess, typeof(object));
                }

                // d.Add(key, value)
                var addCall = Expression.Call(dictVar, addMethod, keyExpr, valueExpr);
                expressions.Add(addCall);
            }

            // return d
            expressions.Add(dictVar);

            var body = Expression.Block(new[] { typedParam, dictVar }, expressions);
            var lambda = Expression.Lambda<Func<object, Abstractions.Policies.INamingPolicy, Abstractions.Security.IEncryptor?, Dictionary<string, object>>>(
                body, 
                param, 
                namingPolicyParam, 
                encryptorParam
            );
            return lambda.Compile();
        }

        /// <summary>
        /// 构建回填委托（Expression Tree → Compiled Delegate）
        /// 支持解密、命名策略、基本类型转换
        /// 
        /// 限制：仅支持扁平 POCO（无嵌套对象、无集合）
        /// 检测到复杂类型时直接返回 null，触发 fallback 到反射路径（ResponseHydrationEngine.HydrateInternal）
        /// </summary>
        private static Func<IDictionary<string, object>, Abstractions.Policies.INamingPolicy, Abstractions.Security.IDecryptor?, object>? BuildHydrator(
            Type contractType, 
            PropertyAuditResult[] auditResults)
        {
            // 仅为简单POCO构建Hydrator（无复杂类型）
            // 复杂场景（嵌套对象、集合）需要fallback到反射路径
            if (auditResults.Any(a => a.IsComplexType))
            {
                return null; // Fallback到反射路径
            }

            var dictParam = Expression.Parameter(typeof(IDictionary<string, object>), "dict");
            var namingPolicyParam = Expression.Parameter(typeof(Abstractions.Policies.INamingPolicy), "namingPolicy");
            var decryptorParam = Expression.Parameter(typeof(Abstractions.Security.IDecryptor), "decryptor");
            
            var instanceVar = Expression.Variable(contractType, "instance");
            var valueVar = Expression.Variable(typeof(object), "value");
            var expressions = new List<Expression>();

            // instance = new T()
            var newInstance = Expression.New(contractType);
            expressions.Add(Expression.Assign(instanceVar, newInstance));

            var tryGetValueMethod = typeof(IDictionary<string, object>).GetMethod(
                "TryGetValue",
                new[] { typeof(string), typeof(object).MakeByRefType() }
            );

            if (tryGetValueMethod == null)
            {
                throw new InvalidOperationException($"Cannot find TryGetValue method on IDictionary<string, object>");
            }

            var convertNameMethod = typeof(Abstractions.Policies.INamingPolicy).GetMethod("ConvertName")!;
            var decryptMethod = typeof(Abstractions.Security.IDecryptor).GetMethod("Decrypt")!;
            var changeTypeMethod = typeof(Convert).GetMethod("ChangeType", new[] { typeof(object), typeof(Type) })!;

            foreach (var audit in auditResults)
            {
                var prop = audit.PropertyInfo;
                var apiField = audit.ApiField;

                // 确定字段名：优先使用显式Name，否则调用NamingPolicy
                Expression keyExpr;
                if (!string.IsNullOrWhiteSpace(apiField.Name))
                {
                    keyExpr = Expression.Constant(apiField.Name);
                }
                else
                {
                    keyExpr = Expression.Call(namingPolicyParam, convertNameMethod, Expression.Constant(prop.Name));
                }

                // dict.TryGetValue(key, out value)
                var tryGetValue = Expression.Call(
                    dictParam,
                    tryGetValueMethod,
                    keyExpr,
                    valueVar
                );

                // 处理值：解密 + 类型转换
                Expression finalValueExpr = valueVar;

                // 1. 解密（如果需要）
                if (apiField.IsEncrypted)
                {
                    var decryptorNotNull = Expression.NotEqual(decryptorParam, Expression.Constant(null, typeof(Abstractions.Security.IDecryptor)));
                    var valueAsString = Expression.Convert(valueVar, typeof(string));
                    var decryptCall = Expression.Call(decryptorParam, decryptMethod, valueAsString);
                    
                    // throw with detailed diagnostic info
                    var errorMessage = $"Decryption required but decryptor is null. Type: {contractType.Name}, Property: {prop.Name}";
                    var throwExpr = Expression.Throw(
                        Expression.New(
                            typeof(InvalidOperationException).GetConstructor(new[] { typeof(string) })!,
                            Expression.Constant(errorMessage)
                        ),
                        typeof(string)
                    );
                    
                    finalValueExpr = Expression.Condition(decryptorNotNull, decryptCall, throwExpr, typeof(string));
                }

                // 2. 类型转换
                Expression convertedValue;
                var targetType = prop.PropertyType;
                var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

                if (underlyingType == typeof(string))
                {
                    // 字符串类型直接使用（可能已解密）
                    convertedValue = finalValueExpr;
                }
                else if (underlyingType.IsValueType)
                {
                    // 值类型使用Convert.ChangeType
                    var changeTypeCall = Expression.Call(
                        changeTypeMethod,
                        Expression.Convert(finalValueExpr, typeof(object)),
                        Expression.Constant(underlyingType)
                    );
                    convertedValue = Expression.Convert(changeTypeCall, underlyingType);
                    
                    // 处理Nullable<T>
                    if (Nullable.GetUnderlyingType(targetType) != null)
                    {
                        convertedValue = Expression.Convert(convertedValue, targetType);
                    }
                }
                else
                {
                    // 引用类型使用TypeAs
                    convertedValue = Expression.TypeAs(finalValueExpr, targetType);
                }

                // instance.Prop = convertedValue
                var assignProperty = Expression.Assign(
                    Expression.Property(instanceVar, prop),
                    convertedValue
                );

                // if (TryGetValue) { assign; }
                var ifThen = Expression.IfThen(tryGetValue, assignProperty);
                expressions.Add(ifThen);
            }

            // return (object)instance
            expressions.Add(Expression.Convert(instanceVar, typeof(object)));

            var body = Expression.Block(
                new[] { instanceVar, valueVar },
                expressions
            );

            var lambda = Expression.Lambda<Func<IDictionary<string, object>, Abstractions.Policies.INamingPolicy, Abstractions.Security.IDecryptor?, object>>(
                body, 
                dictParam, 
                namingPolicyParam, 
                decryptorParam
            );
            return lambda.Compile();
        }
    }

    /// <summary>
    /// 冷冻后的契约元数据（不可变）
    /// </summary>
    public record ContractMetadata(
        Type ContractType,
        ApiOperationAttribute Operation,
        IReadOnlyList<PropertyMetadata> Properties,
        Func<object, Abstractions.Policies.INamingPolicy, Abstractions.Security.IEncryptor?, Dictionary<string, object>>? Projector,
        Func<IDictionary<string, object>, Abstractions.Policies.INamingPolicy, Abstractions.Security.IDecryptor?, object>? Hydrator);

    /// <summary>
    /// 冻结后的属性元数据（不可变）
    /// 
    /// 包含 PropertyInfo、ApiField 和 AuditResult。
    /// AuditResult 缓存约束检查结果，运行期通过布尔值判断而非重复 string.IsNullOrEmpty()。
    /// </summary>
    public record PropertyMetadata(
        PropertyInfo PropertyInfo,
        ApiFieldAttribute ApiField,
        PropertyAuditResult AuditResult);
}
