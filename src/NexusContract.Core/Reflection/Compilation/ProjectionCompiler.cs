// Copyright (c) 2025-2026 NexusContract. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using NexusContract.Abstractions.Attributes;
using NexusContract.Abstractions.Policies;
using NexusContract.Abstractions.Security;

namespace NexusContract.Core.Reflection.Compilation
{
    /// <summary>
    /// 投影器编译器（Expression Tree → Compiled Delegate）
    /// 
    /// 职责：
    /// 将契约对象的属性投影为字典（用于序列化和传输）。
    /// 
    /// 性能：
    /// 表达式树预编译后，性能提升约 10 倍（相比反射）。
    /// 
    /// 限制：
    /// 仅支持扁平 POCO（无嵌套对象、无集合）。
    /// 复杂场景会在 BuildMetadata 阶段被排除，fallback 到反射路径。
    /// </summary>
    internal sealed class ProjectionCompiler
    {
        /// <summary>
        /// 编译投影器委托
        /// </summary>
        /// <param name="contractType">契约类型</param>
        /// <param name="auditResults">属性审计结果（已过滤复杂类型）</param>
        /// <returns>投影委托 (object, INamingPolicy, IEncryptor?) => Dictionary</returns>
        public static Func<object, INamingPolicy, IEncryptor?, Dictionary<string, object>>? Compile(
            Type contractType,
            PropertyAuditResult[] auditResults)
        {
            if (contractType == null) throw new ArgumentNullException(nameof(contractType));
            if (auditResults == null) throw new ArgumentNullException(nameof(auditResults));

            // 空属性列表，返回空字典生成器
            if (auditResults.Length == 0)
            {
                return (_, _, _) => new Dictionary<string, object>();
            }

            try
            {
                return BuildProjectorExpression(contractType, auditResults);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ProjectionCompiler] Failed to compile projector for {contractType.Name}: {ex.Message}");
                return null; // Fallback to reflection
            }
        }

        /// <summary>
        /// 构建表达式树投影器
        /// </summary>
        private static Func<object, INamingPolicy, IEncryptor?, Dictionary<string, object>> BuildProjectorExpression(
            Type contractType,
            PropertyAuditResult[] auditResults)
        {
            var param = Expression.Parameter(typeof(object), "o");
            var namingPolicyParam = Expression.Parameter(typeof(INamingPolicy), "namingPolicy");
            var encryptorParam = Expression.Parameter(typeof(IEncryptor), "encryptor");

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
            var convertNameMethod = typeof(INamingPolicy).GetMethod("ConvertName")!;
            var encryptMethod = typeof(IEncryptor).GetMethod("Encrypt")!;

            foreach (var audit in auditResults)
            {
                var prop = audit.PropertyInfo;
                var apiField = audit.ApiField;

                // 判断字段名：优先使用显式 Name，否则调用 NamingPolicy.ConvertName
                Expression keyExpr;
                if (!string.IsNullOrEmpty(apiField.Name))
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
                    var encryptorNotNull = Expression.NotEqual(encryptorParam, Expression.Constant(null, typeof(IEncryptor)));

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
                    string errorMessage = $"Encryption required but encryptor is null. Type: {contractType.Name}, Property: {prop.Name}";
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
            var lambda = Expression.Lambda<Func<object, INamingPolicy, IEncryptor?, Dictionary<string, object>>>(
                body,
                param,
                namingPolicyParam,
                encryptorParam
            );
            return lambda.Compile();
        }
    }
}
