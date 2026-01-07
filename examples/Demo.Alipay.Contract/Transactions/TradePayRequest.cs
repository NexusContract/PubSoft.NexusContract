// Copyright (c) 2025-2026 PubSoft (pubsoft@gmail.com). All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using PubSoft.NexusContract.Abstractions.Attributes;
using PubSoft.NexusContract.Abstractions.Contracts;

namespace Demo.Alipay.Contract.Transactions
{
#nullable disable
    /// <summary>
    /// 支付宝统一收单交易支付接口请求
    /// 文档: https://opendocs.alipay.com/open/02ijkv
    /// </summary>
    [ApiOperation("alipay.trade.pay", HttpVerb.POST)]
    public class TradePayRequest : IApiRequest<TradePayResponse>
    {
        /// <summary>商户订单号（64字符内）</summary>
        [ApiField("out_trade_no", IsRequired = true, Description = "商户订单号")]
        public string MerchantOrderNo { get; set; }

        /// <summary>订单总金额（单位：元）</summary>
        [ApiField("total_amount", IsRequired = true, Description = "订单总金额，单位：元")]
        public decimal TotalAmount { get; set; }

        /// <summary>订单标题</summary>
        [ApiField("subject", IsRequired = true, Description = "订单标题")]
        public string Subject { get; set; }

        /// <summary>场景码：bar_code（条码）qr_code（二维码）</summary>
        [ApiField("scene", IsRequired = true, Description = "场景码")]
        public string Scene { get; set; }

        /// <summary>买家支付宝账户</summary>
        [ApiField("buyer_id", IsRequired = false)]
        public string BuyerId { get; set; }

        /// <summary>条码/二维码内容</summary>
        [ApiField("auth_code", IsRequired = false)]
        public string AuthCode { get; set; }

        /// <summary>销售产品码</summary>
        [ApiField("product_code", IsRequired = false)]
        public string ProductCode { get; set; } = "FACE_TO_FACE_PAYMENT";

        /// <summary>订单描述</summary>
        [ApiField("body", IsRequired = false)]
        public string Body { get; set; }
    }

    /// <summary>
    /// 支付宝统一收单交易支付接口响应
    /// </summary>
    public class TradePayResponse
    {
        /// <summary>支付宝交易号</summary>
        public string TradeNo { get; set; }

        /// <summary>商户订单号</summary>
        public string OutTradeNo { get; set; }

        /// <summary>买家支付宝账户</summary>
        public string BuyerId { get; set; }

        /// <summary>交易状态</summary>
        public string TradeStatus { get; set; }

        /// <summary>实付金额</summary>
        public decimal ReceiptAmount { get; set; }

        /// <summary>支付时间</summary>
        public string GmtPayment { get; set; }
    }
}
