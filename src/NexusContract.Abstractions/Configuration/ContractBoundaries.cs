namespace PubSoft.NexusContract.Abstractions.Configuration
{
    /// <summary>
    /// 契约系统的物理边界定义
    /// 
    /// 这是整个系统的"宪法"常数。所有边界值的单一来源。
    /// 修改此处，系统全链路的行为都会随之调整（Validator/Engine/Cache/Hydration）。
    /// </summary>
    public static class ContractBoundaries
    {
        /// <summary>
        /// 最大嵌套深度（物理红线）
        /// 
        /// 设计理由：
        /// - Layer 1: 契约请求对象本身
        /// - Layer 2: 嵌套的复杂属性
        /// - Layer 3: 嵌套属性的属性
        /// 
        /// 超过 3 层会：
        /// 1. 增加 StackOverflow 风险
        /// 2. 降低代码可维护性
        /// 3. 强制重构为多个操作
        /// </summary>
        public const int MaxNestingDepth = 3;

        /// <summary>
        /// 最大集合大小（防 OOM）
        /// 
        /// 设计理由：
        /// 防止三方 API 返回巨大列表导致内存溢出。
        /// 如果需要处理超大数据集，应使用分页或流式处理。
        /// </summary>
        public const int MaxCollectionSize = 1000;
    }
}

