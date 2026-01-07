using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using PubSoft.NexusContract.Abstractions.Attributes;
using PubSoft.NexusContract.Core.Utilities;

namespace PubSoft.NexusContract.Core.Reflection
{
    /// <summary>
    /// 【决策 A-303】ContractAuditor（契约审计员）
    /// 
    /// 职责分工：
    /// - ContractValidator：启动期拓扑验证（约束检查） → Fail-Fast 异常
    /// - ContractAuditor：启动期属性级审计 → 结果缓存到 PropertyAuditResult
    /// - ProjectionEngine：运行期读取缓存 → O(1) 布尔值检查
    /// 
    /// 设计意图：Attribute 纯数据化，验证逻辑集中到 Core 层启动期执行，
    /// 避免每次投影时都重复 string.IsNullOrEmpty() 检查。
    /// </summary>
    public static class ContractAuditor
    {
        private static readonly int MaxDepth = Abstractions.Configuration.ContractBoundaries.MaxNestingDepth;

        /// <summary>
        /// 对单个属性进行审计，生成缓存化的审计结果
        /// </summary>
        /// <param name="propertyInfo">属性反射信息</param>
        /// <param name="apiFieldAttribute">API 字段标注</param>
        /// <param name="currentDepth">当前嵌套深度（用于判断复杂字段是否需要显式命名）</param>
        /// <returns>冻结的审计结果（包含所有必要的验证结论）</returns>
        public static PropertyAuditResult AuditProperty(
            PropertyInfo propertyInfo,
            ApiFieldAttribute apiFieldAttribute,
            int currentDepth)
        {
            if (propertyInfo == null)
                throw new ArgumentNullException(nameof(propertyInfo));
            if (apiFieldAttribute == null)
                throw new ArgumentNullException(nameof(apiFieldAttribute));

            // 计算是否为复杂类型
            bool isComplexType = TypeUtilities.IsComplexType(propertyInfo.PropertyType);

            // 【规则 R-201】检查：加密字段必须显式命名
            // 结论缓存为 IsEncryptedWithoutName
            bool isEncryptedWithoutName = apiFieldAttribute.IsEncrypted && 
                                          string.IsNullOrWhiteSpace(apiFieldAttribute.Name);

            // 【规则 R-207】检查：嵌套深度 > MaxDepth 的复杂字段必须显式命名
            // 结论缓存为 IsComplexWithoutName
            bool isComplexWithoutName = isComplexType && 
                                        currentDepth > MaxDepth && 
                                        string.IsNullOrWhiteSpace(apiFieldAttribute.Name);

            return new PropertyAuditResult(
                propertyInfo,
                apiFieldAttribute,
                isEncryptedWithoutName,
                isComplexWithoutName,
                isComplexType
            );
        }

        /// <summary>
        /// 审计所有属性（在 NexusContractMetadataRegistry 的 BuildMetadata 中调用）
        /// </summary>
        public static List<PropertyAuditResult> AuditAllProperties(
            Type contractType,
            int currentDepth)
        {
            var results = new List<PropertyAuditResult>();

            var properties = contractType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var prop in properties)
            {
                var fieldAttr = prop.GetCustomAttribute<ApiFieldAttribute>();
                if (fieldAttr == null) continue;

                var auditResult = AuditProperty(prop, fieldAttr, currentDepth);
                results.Add(auditResult);
            }

            return results;
        }
    }
}
