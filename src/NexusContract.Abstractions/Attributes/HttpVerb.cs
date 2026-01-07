namespace NexusContract.Abstractions.Attributes
{
    /// <summary>
    /// HTTP 请求方法枚举
    /// 
    /// 这是协议事实，不是实现细节。
    /// 在定义契约时必须显式声明使用的 HTTP 动词。
    /// 例如：
    /// - 查询操作通常用 GET
    /// - 支付、转账通常用 POST
    /// - 更新通常用 PUT
    /// </summary>
    public enum HttpVerb
    {
        /// <summary>
        /// GET 请求（获取资源）
        /// 幂等、安全、可缓存
        /// </summary>
        GET = 0,

        /// <summary>
        /// POST 请求（创建资源、执行操作）
        /// 非幂等、不安全
        /// 支付、转账、订单创建等关键操作通常用 POST
        /// </summary>
        POST = 1,

        /// <summary>
        /// PUT 请求（完整替换资源）
        /// 幂等、不安全
        /// </summary>
        PUT = 2,

        /// <summary>
        /// DELETE 请求（删除资源）
        /// 幂等、不安全
        /// </summary>
        DELETE = 3,

        /// <summary>
        /// PATCH 请求（部分更新资源）
        /// 不幂等、不安全
        /// </summary>
        PATCH = 4,

        /// <summary>
        /// HEAD 请求（获取资源元信息，无响应体）
        /// 幂等、安全
        /// </summary>
        HEAD = 5,

        /// <summary>
        /// OPTIONS 请求（查询服务器能力）
        /// 幂等、安全
        /// </summary>
        OPTIONS = 6
    }
}


