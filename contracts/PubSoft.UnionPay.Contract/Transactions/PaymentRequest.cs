using PubSoft.NexusContract.Abstractions.Attributes;
using PubSoft.NexusContract.Abstractions.Contracts;

namespace PubSoft.UnionPay.Contract.Transactions
{
    /// <summary>
    /// 银联支付请求契约（业务层）
    /// 仅依赖 Abstractions，完全解耦具体实现
    /// </summary>
    [ApiOperation("unionpay.trade.pay", HttpVerb.POST, Version = "5.1.0")]
    public class PaymentRequest : IApiRequest<PaymentResponse>
    {
        /// <summary>
        /// 商户订单号
        /// </summary>
        [ApiField(IsRequired = true, Description = "商户系统订单号，必须唯一")]
        public string MerchantOrderId { get; set; }

        /// <summary>
        /// 交易金额（单位：分）
        /// </summary>
        [ApiField("txn_amt", IsRequired = true, Description = "交易金额，单位：分")]
        public long Amount { get; set; }

        /// <summary>
        /// 交易货币代码
        /// </summary>
        [ApiField("currency_code", IsRequired = true, Description = "货币代码，156=人民币")]
        public string CurrencyCode { get; set; } = "156";

        /// <summary>
        /// 银行卡号（敏感信息，必须加密）
        /// </summary>
        [ApiField("card_no", IsEncrypted = true, IsRequired = true, Description = "支付银行卡号")]
        public string CardNumber { get; set; }

        /// <summary>
        /// CVV2 安全码（敏感信息，必须加密）
        /// </summary>
        [ApiField("cvv2", IsEncrypted = true, IsRequired = false, Description = "卡片验证码")]
        public string Cvv2 { get; set; }

        /// <summary>
        /// 卡有效期（敏感信息，必须加密）
        /// </summary>
        [ApiField("exp_date", IsEncrypted = true, IsRequired = false, Description = "卡有效期，格式：YYMM")]
        public string ExpiryDate { get; set; }

        /// <summary>
        /// 持卡人姓名
        /// </summary>
        [ApiField(Description = "持卡人姓名")]
        public string CardholderName { get; set; }

        /// <summary>
        /// 持卡人手机号
        /// </summary>
        [ApiField("mobile_no", IsRequired = false, Description = "持卡人手机号")]
        public string MobileNumber { get; set; }

        /// <summary>
        /// 用户 IP 地址
        /// </summary>
        [ApiField(Description = "用户终端IP地址")]
        public string UserIp { get; set; }

        /// <summary>
        /// 商品描述
        /// </summary>
        [ApiField("goods_desc", Description = "商品或订单描述")]
        public string GoodsDescription { get; set; }
    }

    /// <summary>
    /// 银联支付响应
    /// </summary>
    public class PaymentResponse
    {
        public string TransactionId { get; set; }
        public string OrderId { get; set; }
        public string Status { get; set; }
        public string Message { get; set; }
        public string TraceNo { get; set; }
    }
}
