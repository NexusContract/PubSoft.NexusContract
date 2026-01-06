// Copyright (c) 2025-2026 PubSoft (pubsoft@gmail.com). All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using PubSoft.NexusContract.Abstractions.Attributes;
using PubSoft.NexusContract.Abstractions.Contracts;

namespace Demo.Alipay.Contract.Transactions;

#nullable disable

/// <summary>
/// 统一收单交易退款接口
/// API: alipay.trade.refund
/// 文档: https://opendocs.alipay.com/open/02e7gq
/// </summary>
[ApiOperation("alipay.trade.refund", HttpVerb.POST)]
public class TradeRefundRequest : IApiRequest<TradeRefundResponse>
{
    /// <summary>商户订单号</summary>
    [ApiField("out_trade_no", IsRequired = false, Description = "商户订单号，和支付宝交易号不能同时为空")]
    public string OutTradeNo { get; set; }

    /// <summary>支付宝交易号</summary>
    [ApiField("trade_no", IsRequired = false, Description = "支付宝交易号，和商户订单号不能同时为空")]
    public string TradeNo { get; set; }

    /// <summary>退款金额（单位：元）</summary>
    [ApiField("refund_amount", IsRequired = true, Description = "退款金额，不能大于订单金额")]
    public decimal RefundAmount { get; set; }

    /// <summary>退款请求号</summary>
    [ApiField("out_request_no", IsRequired = false, Description = "退款请求号，标识一次退款请求")]
    public string OutRequestNo { get; set; }

    /// <summary>退款原因说明</summary>
    [ApiField("refund_reason", IsRequired = false)]
    public string RefundReason { get; set; }

    /// <summary>订单退款币种信息</summary>
    [ApiField("refund_currency", IsRequired = false)]
    public string RefundCurrency { get; set; }

    /// <summary>商户门店编号</summary>
    [ApiField("store_id", IsRequired = false)]
    public string StoreId { get; set; }

    /// <summary>商户的操作员编号</summary>
    [ApiField("operator_id", IsRequired = false)]
    public string OperatorId { get; set; }

    /// <summary>商户的终端编号</summary>
    [ApiField("terminal_id", IsRequired = false)]
    public string TerminalId { get; set; }

    /// <summary>查询选项</summary>
    [ApiField("query_options", IsRequired = false)]
    public string[] QueryOptions { get; set; }

    /// <summary>银行间联模式下收单机构的pid</summary>
    [ApiField("org_pid", IsRequired = false)]
    public string OrgPid { get; set; }
}

/// <summary>
/// 统一收单交易退款接口响应
/// </summary>
public class TradeRefundResponse
{
    /// <summary>支付宝交易号</summary>
    public string TradeNo { get; set; }

    /// <summary>商户订单号</summary>
    public string OutTradeNo { get; set; }

    /// <summary>用户的登录id</summary>
    public string BuyerLogonId { get; set; }

    /// <summary>买家支付宝用户ID</summary>
    public string BuyerUserId { get; set; }

    /// <summary>买家支付宝用户唯一标识</summary>
    public string BuyerOpenId { get; set; }

    /// <summary>本次退款是否发生了资金变化</summary>
    public string FundChange { get; set; }

    /// <summary>退款总金额（累计已退款金额）</summary>
    public decimal RefundFee { get; set; }

    /// <summary>退款支付时间</summary>
    public string GmtRefundPay { get; set; }

    /// <summary>交易在支付时候的门店名称</summary>
    public string StoreName { get; set; }

    /// <summary>退款币种信息</summary>
    public string RefundCurrency { get; set; }

    /// <summary>本次商户实际退回金额</summary>
    public decimal SendBackFee { get; set; }
}
