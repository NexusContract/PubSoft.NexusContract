namespace PubSoft.NexusContract.Abstractions
{
    /// <summary>
    /// 空响应类型
    /// 用于 OneWay 交互模式，表示调用方不期望同步响应
    /// 
    /// 设计约束：当 [ApiOperation] 的 Interaction = OneWay 时，
    /// IApiRequest&lt;TResponse&gt; 中的 TResponse 必须且仅能是 EmptyResponse
    /// </summary>
    public sealed class EmptyResponse
    {
        /// <summary>
        /// 唯一实例（单例模式）
        /// </summary>
        public static readonly EmptyResponse Instance = new();

        private EmptyResponse() { }
    }
}
