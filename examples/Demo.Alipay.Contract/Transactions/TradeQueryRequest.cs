// Copyright (c) 2025-2026 PubSoft (pubsoft@gmail.com). All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using NexusContract.Abstractions.Attributes;
using NexusContract.Abstractions.Contracts;

namespace Demo.Alipay.Contract.Transactions;

#nullable disable

/// <summary>
/// 统一收单交易查询接口
/// API: alipay.trade.query
/// 文档: https://opendocs.alipay.com/open/02ivbs
/// </summary>
[ApiOperation("alipay.trade.query", HttpVerb.POST)]
public class TradeQueryRequest : IApiRequest<TradeQueryResponse>
{
    /// <summary>商户订单号</summary>
    [ApiField("out_trade_no", IsRequired = false, Description = "商户订单号，和支付宝交易号不能同时为空")]
    public string OutTradeNo { get; set; }

    /// <summary>支付宝交易号</summary>
    [ApiField("trade_no", IsRequired = false, Description = "支付宝交易号，和商户订单号不能同时为空")]
    public string TradeNo { get; set; }

    /// <summary>银行间联模式下收单机构的pid</summary>
    [ApiField("org_pid", IsRequired = false)]
    public string OrgPid { get; set; }

    /// <summary>查询选项</summary>
    [ApiField("query_options", IsRequired = false)]
    public string[] QueryOptions { get; set; }
}

/// <summary>
/// 统一收单交易查询接口响应
/// </summary>
public class TradeQueryResponse
{
    /// <summary>支付宝交易号</summary>
    public string TradeNo { get; set; }

    /// <summary>商户订单号</summary>
    public string OutTradeNo { get; set; }

    /// <summary>买家支付宝账号</summary>
    public string BuyerLogonId { get; set; }

    /// <summary>买家支付宝用户ID</summary>
    public string BuyerUserId { get; set; }

    /// <summary>买家支付宝用户唯一标识</summary>
    public string BuyerOpenId { get; set; }

    /// <summary>交易状态：WAIT_BUYER_PAY/TRADE_CLOSED/TRADE_SUCCESS/TRADE_FINISHED</summary>
    public string TradeStatus { get; set; }

    /// <summary>交易金额</summary>
    public decimal TotalAmount { get; set; }

    /// <summary>实收金额</summary>
    public decimal ReceiptAmount { get; set; }

    /// <summary>买家实付金额</summary>
    public decimal BuyerPayAmount { get; set; }

    /// <summary>订单标题</summary>
    public string Subject { get; set; }

    /// <summary>订单描述</summary>
    public string Body { get; set; }

    /// <summary>交易支付时间</summary>
    public string GmtPayment { get; set; }

    /// <summary>本次交易打款给卖家的时间</summary>
    public string SendPayDate { get; set; }

    /// <summary>商户门店编号</summary>
    public string StoreId { get; set; }

    /// <summary>商户机具终端编号</summary>
    public string TerminalId { get; set; }

    /// <summary>交易额外信息</summary>
    public string ExtInfos { get; set; }
}
