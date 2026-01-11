// Copyright (c) 2025-2026 NexusContract. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using NexusContract.Abstractions.Attributes;
using NexusContract.Abstractions.Exceptions;
using NexusContract.Abstractions.Policies;
using NexusContract.Abstractions.Security;

namespace NexusContract.Core.Reflection.Compilation
{
    /// <summary>
    /// 回填器编译器（Expression Tree → Compiled Delegate）
    /// 
    /// 职责：
    /// 将字典数据回填到契约对象（用于反序列化和响应处理）。
    /// 
    /// 性能：
    /// 表达式树预编译后，避免运行时反射开销。
    /// 
    /// 限制：
    /// 仅支持扁平 POCO（无嵌套对象、无集合）。
    /// 检测到复杂类型时返回 null，触发 fallback 到反射路径。
    /// </summary>
    internal sealed class HydrationCompiler
    {
        /// <summary>
        /// 编译回填器委托
        /// </summary>
        /// <param name="contractType">契约类型</param>
        /// <param name="auditResults">属性审计结果</param>
        /// <returns>回填委托 (IDictionary, INamingPolicy, IDecryptor?) => object，失败返回 null</returns>
        public static Func<IDictionary<string, object>, INamingPolicy, IDecryptor?, object>? Compile(
            Type contractType,
            PropertyAuditResult[] auditResults)
        {
            NexusGuard.EnsurePhysicalAddress(contractType);
            NexusGuard.EnsurePhysicalAddress(auditResults);

            // 仅为简单POCO构建Hydrator（无复杂类型）
            // 复杂场景（嵌套对象、集合）需要fallback到反射路径
            if (auditResults.Any(a => a.IsComplexType))
            {
                return null; // Fallback到反射路径
            }

            try
            {
                return BuildHydratorExpression(contractType, auditResults);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[HydrationCompiler] Failed to compile hydrator for {contractType.Name}: {ex.Message}");
                return null; // Fallback to reflection
            }
        }

        /// <summary>
        /// 构建表达式树回填器
        /// </summary>
        private static Func<IDictionary<string, object>, INamingPolicy, IDecryptor?, object> BuildHydratorExpression(
            Type contractType,
            PropertyAuditResult[] auditResults)
        {
            var dictParam = Expression.Parameter(typeof(IDictionary<string, object>), "dict");
            var namingPolicyParam = Expression.Parameter(typeof(INamingPolicy), "namingPolicy");
            var decryptorParam = Expression.Parameter(typeof(IDecryptor), "decryptor");

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
                throw new ContractIncompleteException(
                    $"Cannot find TryGetValue method on IDictionary<string, object>",
                    "NXC102");
            }

            var convertNameMethod = typeof(INamingPolicy).GetMethod("ConvertName")!;
            var decryptMethod = typeof(IDecryptor).GetMethod("Decrypt")!;
            var changeTypeMethod = typeof(Convert).GetMethod("ChangeType", new[] { typeof(object), typeof(Type) })!;

            foreach (var audit in auditResults)
            {
                var prop = audit.PropertyInfo;
                var apiField = audit.ApiField;

                // 确定字段名：优先使用显式Name，否则调用NamingPolicy
                Expression keyExpr;
                if (!string.IsNullOrEmpty(apiField.Name))
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
                    var decryptorNotNull = Expression.NotEqual(decryptorParam, Expression.Constant(null, typeof(IDecryptor)));
                    var valueAsString = Expression.Convert(valueVar, typeof(string));
                    var decryptCall = Expression.Call(decryptorParam, decryptMethod, valueAsString);

                    // throw with detailed diagnostic info
                    string errorMessage = $"Decryption required but decryptor is null. Type: {contractType.Name}, Property: {prop.Name}";
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
                    // 字符串类型：确保类型匹配
                    if (finalValueExpr.Type == typeof(string))
                    {
                        convertedValue = finalValueExpr;
                    }
                    else
                    {
                        // 从 object 转换为 string
                        convertedValue = Expression.TypeAs(finalValueExpr, typeof(string));
                    }
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

            var lambda = Expression.Lambda<Func<IDictionary<string, object>, INamingPolicy, IDecryptor?, object>>(
                body,
                dictParam,
                namingPolicyParam,
                decryptorParam
            );
            return lambda.Compile();
        }
    }
}
