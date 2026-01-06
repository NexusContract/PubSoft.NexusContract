using PubSoft.NexusContract.Abstractions.Attributes;
using PubSoft.NexusContract.Abstractions.Contracts;

namespace PubSoft.UnionPay.Contract.Query
{
    /// <summary>
    /// 银联交易查询请求
    /// </summary>
    [ApiOperation("unionpay.query.transaction", HttpVerb.POST, Version = "5.1.0")]
    public class QueryRequest : IApiRequest<QueryResponse>
    {
        /// <summary>
        /// 商户订单号
        /// </summary>
        [ApiField("order_id", IsRequired = true, Description = "原交易的商户订单号")]
        public string OrderId { get; set; }

        /// <summary>
        /// 交易日期
        /// </summary>
        [ApiField("txn_time", IsRequired = true, Description = "原交易时间，格式：yyyyMMddHHmmss")]
        public string TransactionTime { get; set; }
    }

    /// <summary>
    /// 银联交易查询响应
    /// </summary>
    public class QueryResponse
    {
        public string OrderId { get; set; }
        public string Status { get; set; }
        public string Message { get; set; }
        public long Amount { get; set; }
        public string PayTime { get; set; }
    }
}
