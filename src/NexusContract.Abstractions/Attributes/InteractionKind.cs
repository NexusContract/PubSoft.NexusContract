namespace NexusContract.Abstractions.Attributes
{
    /// <summary>
    /// 交互模式枚举
    /// 定义 Contract 的交互语义（请求-响应 vs 单向通知）
    /// </summary>
    public enum InteractionKind
    {
        /// <summary>
        /// 请求-响应模式（同步）
        /// 调用方发起请求，等待并获取响应
        /// 典型场景：支付、查询、转账等需要即时反馈的操作
        /// </summary>
        RequestResponse = 1,

        /// <summary>
        /// 单向模式（异步 / 通知）
        /// 调用方不等待响应，或通过回调获取结果
        /// 典型场景：回调、通知、对账推送等无需同步响应的操作
        /// </summary>
        OneWay = 2
    }
}


