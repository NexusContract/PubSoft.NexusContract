namespace PubSoft.NexusContract.Abstractions.Policies
{
    /// <summary>
    /// Policy over Convention: 策略优于约定
    /// 命名策略接口，不同的 Provider 可以持有不同的策略（如 snake_case, camelCase, PascalCase）
    /// </summary>
    public interface INamingPolicy
    {
        /// <summary>
        /// 将属性名转换为协议字段名
        /// </summary>
        /// <param name="propertyName">C# 属性名</param>
        /// <returns>协议字段名</returns>
        string ConvertName(string propertyName);
    }
}
