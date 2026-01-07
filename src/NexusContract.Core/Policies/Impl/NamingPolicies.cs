using System.Text.RegularExpressions;
using NexusContract.Abstractions.Policies;

namespace NexusContract.Core.Policies.Impl
{
    /// <summary>
    /// Snake Case 命名策略 (例如: OrderId -> order_id)
    /// </summary>
    public class SnakeCaseNamingPolicy : INamingPolicy
    {
        public string ConvertName(string propertyName)
        {
            if (string.IsNullOrEmpty(propertyName))
                return propertyName;

            // PascalCase/camelCase -> snake_case
            return Regex.Replace(propertyName, "(?<!^)([A-Z])", "_$1").ToLowerInvariant();
        }
    }

    /// <summary>
    /// Camel Case 命名策略 (例如: OrderId -> orderId)
    /// </summary>
    public class CamelCaseNamingPolicy : INamingPolicy
    {
        public string ConvertName(string propertyName)
        {
            if (string.IsNullOrEmpty(propertyName) || char.IsLower(propertyName[0]))
                return propertyName;

            return char.ToLowerInvariant(propertyName[0]) + propertyName.Substring(1);
        }
    }

    /// <summary>
    /// Pascal Case 命名策略 (保持原样，例如: OrderId -> OrderId)
    /// </summary>
    public class PascalCaseNamingPolicy : INamingPolicy
    {
        public string ConvertName(string propertyName)
        {
            return propertyName; // 保持不变
        }
    }
}


