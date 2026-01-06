// Copyright (c) 2025-2026 PubSoft (pubsoft@gmail.com). All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using PubSoft.NexusContract.Abstractions.Attributes;
using PubSoft.NexusContract.Abstractions.Contracts;

namespace Demo.Alipay.Contract.Transactions;

#nullable disable

/// <summary>
/// 统一收单交易创建接口
/// API: alipay.trade.create
/// 文档: https://opendocs.alipay.com/open/02ekfg
/// </summary>
[ApiOperation("alipay.trade.create", HttpVerb.POST)]
public class TradeCreateRequest : IApiRequest<TradeCreateResponse>
{
    /// <summary>商户订单号（64字符内）</summary>
    [ApiField("out_trade_no", IsRequired = true, Description = "商户订单号，由商家自定义")]
    public string OutTradeNo { get; set; }

    /// <summary>订单总金额（单位：元）</summary>
    [ApiField("total_amount", IsRequired = true, Description = "订单总金额，单位：元")]
    public decimal TotalAmount { get; set; }

    /// <summary>订单标题</summary>
    [ApiField("subject", IsRequired = true, Description = "订单标题")]
    public string Subject { get; set; }

    /// <summary>买家支付宝用户ID</summary>
    [ApiField("buyer_id", IsRequired = false)]
    public string BuyerId { get; set; }

    /// <summary>买家支付宝登录账号</summary>
    [ApiField("buyer_logon_id", IsRequired = false)]
    public string BuyerLogonId { get; set; }

    /// <summary>买家支付宝用户唯一标识</summary>
    [ApiField("buyer_open_id", IsRequired = false)]
    public string BuyerOpenId { get; set; }

    /// <summary>订单附加信息</summary>
    [ApiField("body", IsRequired = false)]
    public string Body { get; set; }

    /// <summary>产品码</summary>
    [ApiField("product_code", IsRequired = false)]
    public string ProductCode { get; set; } = "FACE_TO_FACE_PAYMENT";

    /// <summary>卖家支付宝用户ID</summary>
    [ApiField("seller_id", IsRequired = false)]
    public string SellerId { get; set; }

    /// <summary>订单相对超时时间</summary>
    [ApiField("timeout_express", IsRequired = false)]
    public string TimeoutExpress { get; set; }

    /// <summary>订单绝对超时时间</summary>
    [ApiField("time_expire", IsRequired = false)]
    public string TimeExpire { get; set; }

    /// <summary>通知地址</summary>
    [ApiField("notify_url", IsRequired = false)]
    public string NotifyUrl { get; set; }

    /// <summary>可打折金额</summary>
    [ApiField("discountable_amount", IsRequired = false)]
    public decimal? DiscountableAmount { get; set; }

    /// <summary>不可打折金额</summary>
    [ApiField("undiscountable_amount", IsRequired = false)]
    public decimal? UndiscountableAmount { get; set; }

    /// <summary>商户门店编号</summary>
    [ApiField("store_id", IsRequired = false)]
    public string StoreId { get; set; }

    /// <summary>商户操作员编号</summary>
    [ApiField("operator_id", IsRequired = false)]
    public string OperatorId { get; set; }

    /// <summary>商户机具终端编号</summary>
    [ApiField("terminal_id", IsRequired = false)]
    public string TerminalId { get; set; }
}

/// <summary>
/// 统一收单交易创建接口响应
/// </summary>
public class TradeCreateResponse
{
    /// <summary>商户订单号</summary>
    public string OutTradeNo { get; set; }

    /// <summary>支付宝交易号</summary>
    public string TradeNo { get; set; }
}
