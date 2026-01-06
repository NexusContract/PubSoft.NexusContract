# Examples - NexusContract示例

本目录包含NexusContract框架的完整集成示例。

## 项目结构

### Demo.Alipay.Contract

**纯数据模型层** - Contract定义

- `Transactions/TradePayRequest.cs` - 支付宝支付接口契约
- 仅依赖 `NexusContract.Abstractions`
- 可被多个Endpoint/Provider复用

### Demo.Alipay.HttpApi

**应用层** - FastEndpoints集成示例

- `Endpoints/TradePayEndpoint.cs` - 支付宝支付端点实现
- `Program.cs` - 完整的FastEndpoints配置示例
- 依赖 `NexusContract.Core` + `NexusContract.Providers.Alipay` + `Demo.Alipay.Contract`

## 快速开始

### 1. 运行Demo

```bash
cd Demo.Alipay.HttpApi
dotnet run
```

### 2. 测试API

```bash
curl -X POST http://localhost:5000/api/alipay/alipay.trade.pay \
  -H "Content-Type: application/json" \
  -d '{
    "merchantOrderNo": "2024001",
    "totalAmount": 100.00,
    "subject": "测试订单",
    "scene": "bar_code",
    "authCode": "285015833990941919"
  }'
```

## 架构说明

```
Demo.Alipay.Contract (契约层)
  ↓
Demo.Alipay.HttpApi (应用层)
  ├→ Endpoint (继承 NexusProxyEndpoint<TradePayRequest>)
  └→ Program.cs (FastEndpoints集成)
  ↓
NexusContract.Providers.Alipay (Provider核心)
  └→ AlipayProvider (执行HTTP、签名、验证)
  ↓
NexusContract.Core (框架自动化)
  └→ NexusGateway (四阶段管道)
```

## 扩展新的支付宝API

### 步骤1：在Demo.Alipay.Contract中添加新Contract

```csharp
[ApiOperation("alipay.trade.refund", HttpVerb.POST)]
public class TradeRefundRequest : IApiRequest<TradeRefundResponse>
{
    [ApiField("out_request_no", IsRequired = true)]
    public string RefundRequestNo { get; set; }
}
```

### 步骤2：在Demo.Alipay.HttpApi中添加新Endpoint

```csharp
public class TradeRefundEndpoint : AlipayProxyEndpoint<TradeRefundRequest>
{
    private readonly AlipayProvider _alipayProvider;
    
    public TradeRefundEndpoint(AlipayProvider provider, NexusGateway gateway)
        : base(gateway) => _alipayProvider = provider;
    
    public async Task<TradeRefundResponse> HandleAsync(TradeRefundRequest req)
        => await _alipayProvider.ExecuteAsync(req);
}
```

完成！无需修改Provider。

## 关键特点

✅ **Contract和Provider完全解耦** - 可以为同一个Contract写多个Provider实现

✅ **零配置路由** - 路由来自[ApiOperation]属性

✅ **单泛型Endpoint** - NexusProxyEndpoint<TRequest>自动推断TResponse

✅ **四阶段自动化** - 验证→投影→执行→回填全自动

## 参考资源

- [NexusContract框架文档](../docs/IMPLEMENTATION.md)
- [支付宝Provider文档](../src/Providers/NexusContract.Providers.Alipay/README.md)
- [支付宝开放文档](https://opendocs.alipay.com/)
