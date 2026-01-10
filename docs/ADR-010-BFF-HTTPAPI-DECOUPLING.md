# ADR-010: BFF/HttpApi 职责解耦与 URL 资源寻址

**状态**: ✅ 已采纳  
**日期**: 2026-01-11  
**决策者**: 架构组  
**相关**: ADR-009 (三层模型 + 缓存优化)

---

## 目录

1. [背景与问题](#1-背景与问题)
2. [决策](#2-决策)
3. [理由](#3-理由)
4. [实现细节](#4-实现细节)
5. [影响与风险](#5-影响与风险)
6. [迁移路径](#6-迁移路径)

---

## 1. 背景与问题

### 1.1 当前架构的职责混淆

在 ADR-009 定义的三层模型中（Map / Inst / Pool），所有层级的查询职责都集中在 **HttpApi（网关层）**：

```
当前架构：
┌─────────────────────────────────────┐
│ BFF 层                               │
│ - 业务路由决策                       │
│ - 用户交互逻辑                       │
└─────────────────────────────────────┘
         ↓ HTTP (携带 Header)
┌─────────────────────────────────────┐
│ HttpApi 层（职责过重）               │
│ - Map 层查询（授权校验）   ← 问题点   │
│ - Inst 层查询（业务参数）            │
│ - Pool 层查询（密钥资源）            │
│ - 签名、加密、协议转换               │
└─────────────────────────────────────┘
```

**核心矛盾**：
- **Map 层（授权映射）** 是业务决策逻辑，应该由 BFF 负责
- **HttpApi** 作为执行层，应该只处理"如何执行"，而非"能否执行"
- ProfileId 通过 Header 传递，确定性不足（需要运行时解析）

### 1.2 "老商家保护"的物理隔离诉求

在高并发场景下，新商家上线或配置变更时，如果 Map 层的配置噪声（非法 AppId、权限校验失败）影响到 HttpApi 的内存状态，可能导致：

1. **资源争抢**：新商家的 Map 查询占用 Redis 连接，老商家请求排队
2. **缓存污染**：Map 层的频繁变更导致 HttpApi 缓存抖动
3. **职责不清**：HttpApi 需要同时维护三层缓存，复杂度高

**核心诉求**：将 Map 层（授权决策）物理隔离到 BFF，让 HttpApi 成为纯粹的"执行引擎"。

---

## 2. 决策

### 2.1 Map 层职责上移到 BFF

**核心决策**：`nxc:map`（授权映射层）仅由 **BFF 层** 维护和加载。HttpApi 视为"受信任执行环境"，仅通过 BFF 传递的 `ProfileId` 索引 `nxc:inst` 和 `nxc:pool`。

**新架构**：

```
┌─────────────────────────────────────┐
│ BFF 层（决策者）                     │
│ - 查询 nxc:map（授权校验）           │
│ - 选择 ProfileId（业务决策）         │
│ - 拼接 URL：/api/{provider}/{profileId}/{op} │
└─────────────────────────────────────┘
         ↓ HTTP (URL 携带 ProfileId)
┌─────────────────────────────────────┐
│ HttpApi 层（执行者）                 │
│ - 从 URL 提取 ProfileId              │
│ - 查询 nxc:inst（业务参数）          │
│ - 查询 nxc:pool（密钥资源）          │
│ - 签名、加密、协议转换               │
└─────────────────────────────────────┘
```

**物理分布**：

| 层级 | 存储位置 | 物理驻留点 | 变更频率 | 订阅消息 |
|-----|---------|-----------|---------|---------|
| **Layer 1: Map** | Redis Set | **BFF 内存 (L1)** | 中（授权变更） | MappingChange |
| **Layer 2: Inst** | Redis Hash | **HttpApi 内存 (L1)** | 低（Token 更新） | ConfigChange |
| **Layer 3: Pool** | Redis String | **HttpApi 内存 (L1)** | 极低（密钥轮换） | ConfigChange |

### 2.2 URL 资源寻址模式

**设计决策**：将 ProfileId 编码到 URL 路径中，实现 RESTful 风格的资源寻址（Resource Addressing）。

**格式对比**：

```
传统模式（Header 传参）：
POST /api/alipay/trade/create
Header: X-Tenant-Profile: 2088123456789012

资源寻址模式（推荐）：
POST /api/alipay/2088123456789012/trade/create
              ↑
       ProfileId 直接编码到 URL
```

**核心优势**：
- ✅ **确定性**：BFF 已完成决策，URL 携带"答案"，HttpApi 无需猜测
- ✅ **可观测性**：URL 路径即业务身份，日志、监控一目了然
- ✅ **冷启动优化**：跳过 Map 查询，直接根据 ProfileId 查询 Inst/Pool

### 2.3 复合标识符（Composite Key）策略

**设计背景**：在生产环境中，NexusContract 采用了扁平化复合键设计，这一选择基于多年的高并发实践与可观测性经验评估，旨在降低查找链路复杂度并提高隔离性。

基于我们的生产实践，扁平化复合键通常能在高并发场景下缩短逻辑路径并改善稳定性（详见“生产验证”部分）。

**格式规范**：

| 场景 | ProfileId 格式 | 示例 | 说明 |
|-----|--------------|------|------|
| **直连模式** | `{appid}` | `2088123456789012` | 单一应用ID |
| **服务商模式** | `{spid}-{sub_appid}` | `2088001-2088002` | 服务商-子商户 |
| **微信特约商户** | `{mchid}-{sub_mchid}` | `1900000001-1900000002` | 特约商户模式 |

**示例**：

```
# 直连模式
ProfileId: "2088123456789012"
URL: "/api/alipay/2088123456789012/trade/create"
RedisKey: "nxc:inst:realm123:Alipay:2088123456789012"

# 服务商模式（复合键）
ProfileId: "2088001234567890-2088123456789012"  ← 服务商ID-子商户ID
URL: "/api/alipay/2088001234567890-2088123456789012/trade/create"
RedisKey: "nxc:inst:realm456:Alipay:2088001234567890-2088123456789012"
```

**决策理由**：

#### A. 寻址复杂度降低到 O(1)

如果采用"优雅"的嵌套结构（先查服务商SP，再在SP下找子商户AppId），网关在执行支付时需要进行两次逻辑判断或两次缓存查询。

**复杂度对比**：

| 设计模式 | 寻址路径 | 内存查询次数 | 复杂度 | 高并发稳定性 |
|---------|---------|------------|--------|------------|
| **嵌套结构**（"优雅"） | 查 SP → 查 AppId | 2 次 | O(2) | ⚠️ 逻辑链路长 |
| **扁平化复合键**（"粗暴"） | 查 `spid-appid` | 1 次 | **O(1)** | ✅ 逻辑路径短 |

```csharp
// ✅ 扁平化：一次内存查找直接命中
var config = _memoryCache.Get("nxc:inst:realm:Alipay:2088001-2088002");

// ❌ 嵌套（反例）：需要两次查找
var spConfig = _memoryCache.Get("nxc:sp:2088001");
var appConfig = spConfig.SubMerchants.Get("2088002");
```

**架构价值**：在高并发瞬间，**逻辑路径越短，系统越稳**。

#### B. 变更颗粒度的极致隔离（爆炸半径最小化）

**扁平化设计的物理隔离效果**：

```
配置变更：
- 修改对象：服务商A(2088001) → 子商户B(2088002)
- 消息载荷：ProfileId = "2088001-2088002"
- Redis Key: "nxc:inst:realm:Alipay:2088001-2088002"

隔离保障（说明）：
- 在我们的生产观测中，子商户 C（2088003）的缓存 `nxc:inst:realm:Alipay:2088001-2088003` 未受影响；请在你的部署中验证投递与缓存策略。
- 直连商家 D（2088004）的缓存 `nxc:inst:realm:Alipay:2088004` 在观测中未受影响，但仍建议监控。
- 服务商 A 的其他子商户通常保持物理隔离，但隔离效果依赖于消息投递可靠性与部署配置。
```

**对比嵌套结构的风险**：

| 设计模式 | 变更对象 | 影响范围 | 风险评估 |
|---------|---------|---------|---------|
| **嵌套结构** | 修改 SP 下的某个子商户 | 可能触碰整个 SP 的缓存对象 | 🔴 高风险 |
| **扁平化复合键** | 修改 `spid-appid` | 仅该复合键的缓存 | 🟢 极低风险 |

**隔离效果（说明）**：在我们的生产经验中，此类变更通常只影响指定的复合键缓存，未观察到跨子商户的内存干扰；请在你的环境中验证消息投递与缓存策略以满足隔离需求。

#### C. 消息通知的简单幂等

**消息载荷格式**：

```json
{
  "type": "ConfigChange",
  "realmId": "realm456",
  "providerName": "Alipay",
  "profileId": "2088001234567890-2088123456789012",
  "timestamp": 1736510400
}
```

**幂等性保障**：
- 同一个 ProfileId 的多次推送，自动覆盖旧值
- 无需关心推送顺序（最后一次推送生效）
- 消息丢失后重推，不会产生副作用

**自愈成本**：如果新商家的配置配错了，手动测试发现不行，你只需要针对这个复合 ID 重新推一次。这个"点对点"的修复动作，对老商家来说完全是透明的。

#### D. "多年稳定"是最高等级的验证

**在支付架构中，"稳定运行多年"这五个字的分量超过任何花哨的新技术。**

**生产验证（摘要）**：以下为我们在生产环境的观测与经验（基于过去运行数据与事件分析，实际结果请根据具体部署验证）：

| 维度 | 观测 | 说明 |
|-----|---------|------|
| **隔离性** | 通常仅影响变更的 ProfileId | 复合键有助于隔离变更影响，但隔离效果依赖于消息投递与缓存配置 |
| **性能** | O(1) 内存查找 | 扁平化设计减少查找链路，从而降低延迟（具体延迟依赖运行环境） |
| **容错性** | 在观测中老商家未受显著影响 | 结合 NeverRemove 与滑动过期可在一定条件下提供脱网能力（依赖内存、访问模式与策略） |
| **可维护性** | 监控与重试流程清晰 | URL 路径有助于日志和问题定位 |

**职责边界**：

| 层级 | 职责 | 操作 |
|-----|------|------|
| **BFF 层** | 决策者 | 根据业务场景拼接复合键（因为它懂业务） |
| **HttpApi 层** | 执行者 | 消费复合键查询缓存（因为它只懂查内存） |
| **消息中间件** | 传递者 | 传递复合键进行精准打击（因为它只管通知） |

**这种"粗暴"是建立在对业务深刻理解基础上的"降维打击"。既然它已经稳定支撑了这么久，我们就把它作为"老商家保护协议"的基础设施，一直跑下去。**

### 2.4 分层消息通知机制

**核心原则**：不同层级订阅不同类型的消息，实现精准缓存清理。

**消息类型定义**：

| 消息类型 | 订阅者 | 载荷内容 | 触发动作 | 影响范围 |
|---------|-------|---------|---------|---------|
| **MappingChange** | BFF | `{realmId, provider}` | 清除 Map 缓存 | 仅 BFF 层，授权逻辑更新 |
| **ConfigChange** | HttpApi | `{provider, profileId}` | 清除 Inst 缓存 | 仅该 ProfileId，精准清理 |

**消息载荷示例**：

```json
// MappingChange（BFF 订阅）
{
  "type": "MappingChange",
  "realmId": "realm123",
  "providerName": "Alipay",
  "timestamp": 1736510400
}

// ConfigChange（HttpApi 订阅）
{
  "type": "ConfigChange",
  "realmId": "realm123",
  "providerName": "Alipay",
  "profileId": "2088123456789012",
  "timestamp": 1736510400
}
```

**新商家上线隔离性验证**：

| 变更操作 | 消息载荷 | 影响范围 | 风险评估 |
| --- | --- | --- | --- |
| **新增商家/AppId** | `MappingChange` | BFF 清除 Map 缓存 | **极低**。老商家的 URL 路径不受影响，不产生回源。 |
| **更新某商家 Token** | `ConfigChange` + ProfileId | HttpApi 清除该 ID 缓存 | **极低**。仅该商家下一笔请求需回源，其余 499 个商家 0 影响。 |
| **删除非法 AppId** | `MappingChange` | BFF 清除 Map 缓存 | **极低**。BFF 立即拦截非法路径，请求根本进不到 HttpApi。 |

### 2.5 BFF 层作为信任边界

**安全决策**：必须在 BFF 层维持内存 Map（HashSet），只有当 URL 中的 ProfileId 存在于该 Realm 的合法列表中，才允许透传到 HttpApi。

**BFF Middleware 伪代码**：

```csharp
public async Task InvokeAsync(HttpContext context)
{
    // 1. 提取租户标识
    var realmId = context.Request.Headers["X-Tenant-Realm"];
    var provider = context.Request.RouteValues["provider"];
    var profileId = context.Request.RouteValues["profileId"];

    // 2. 查询 BFF 内存的 Map 缓存
    var mapKey = $"{realmId}:{provider}";
    var allowedProfiles = await _mapCache.GetOrCreateAsync(mapKey, async () =>
    {
        // 冷启动：从 Redis 拉取
        var profiles = await _redis.SetMembersAsync($"nxc:map:{realmId}:{provider}");
        return new HashSet<string>(profiles.Select(p => p.ToString()));
    });

    // 3. 授权校验
    if (!allowedProfiles.Contains(profileId))
    {
        context.Response.StatusCode = 403;
        await context.Response.WriteAsync("Unauthorized ProfileId");
        return;
    }

    // 4. 透传到 HttpApi
    var httpApiUrl = $"https://httpapi/api/{provider}/{profileId}/{operation}";
    await _httpClient.PostAsync(httpApiUrl, context.Request.Body);
}
```

**安全保障**：
- ✅ 防止攻击者遍历 ProfileId 探测配置
- ✅ BFF 作为"信任边界"，HttpApi 视为受信任环境
- ✅ 非法请求在 BFF 层被拦截，不会打到 HttpApi

---

## 3. 理由

### 3.1 职责的彻底清算：BFF vs. HttpApi

在这种设计下，两者的界限变得无比清晰：

| 维度 | BFF 层（决策者） | HttpApi 层（执行者） |
|-----|----------------|-------------------|
| **职责** | 处理业务逻辑，验证"谁能用什么" | 只管干活，不管"为什么" |
| **操作** | 查询 `nxc:map`，判断 RealmId 是否合法 | 只查 `nxc:inst` 和 `nxc:pool` |
| **输出** | 向下转发时，URL 已带上确定的 ProfileId | 根据 ProfileId 抓取 Token、密钥进行签名 |
| **信任模型** | 不信任外部请求，需要鉴权 | 信任 BFF 传来的请求（已鉴权） |

### 3.2 为什么"Map 不放 HttpApi"更符合"老商家保护"原则？

这个调整进一步降低了 HttpApi（支付路径核心）的**击穿风险**：

1. **HttpApi 逻辑纯净化**：不再需要处理复杂的 `nxc:map` 逻辑（比如默认 AppId 解析、权限白名单比对）。这意味着 HttpApi 的内存更干净，执行路径更短。

2. **隔离风险扩散**：如果一个新商家的 `Map` 映射配置错了（比如配置了几千个非法 AppId），受影响的只是 BFF 层的内存或查询。**HttpApi 层的 L1 缓存（Inst/Pool）由于不感知 Map，完全不会受到这种配置噪声的干扰。**

3. **确定性下发**：BFF 已经把"选择题"做完了，HttpApi 拿到的直接是"答案"。这消灭了 HttpApi 在执行支付瞬间去解析"我该用哪个 AppId"的不确定性。

### 3.3 冷启动优势

如果网关重启（内存全空），URL 带 ProfileId 的优势立刻显现：

```csharp
// 1. 请求到达：/api/alipay/2088123456789012/trade/create
var profileId = RouteData.Values["profileId"];  // 从 URL 直接提取

// 2. 直接查询，无需先问"这个 Realm 有哪些 AppId"
var inst = await _redis.HashGetAllAsync($"nxc:inst:{realm}:Alipay:{profileId}");
var pool = await _redis.StringGetAsync($"nxc:pool:Alipay:{profileId}");

// 3. 防击穿：SemaphoreSlim 确保 1000 并发只有 1 次 Redis 查询
```

**对比传统 Header 模式**：

| 步骤 | URL 资源寻址 | Header 传参 | 额外开销 |
|-----|------------|-----------|---------|
| 1. 提取标识 | 从 URL 直接获取 | 从 Header 解析 | +1 次字符串拆分 |
| 2. 权限校验 | BFF 已完成 | HttpApi 需要查 Map | +1 次 Redis 往返 |
| 3. 查询配置 | 直接查 Inst/Pool | 查 Map → 查 Inst/Pool | +1 次 Redis 往返 |

**优势**：
- ✅ 跳过 Map 层查询，减少 1 次 Redis 往返
- ✅ SemaphoreSlim 防击穿，1000 并发只 1 次 Redis 查询
- ✅ HttpApi 职责纯净，仅处理执行逻辑

### 3.4 BFF 层可以接受"个别抖动"

相比于 HttpApi，BFF 处理的是：
- **业务路由决策**：扫码后进入哪个页面、选择哪个支付方式
- **低频授权校验**：验证该商户是否有权发起这笔交易

在这种场景下，如果因为新商家上线导致 BFF 层产生了一次 Redis 回源（10-50ms）：

1. **用户感知低**：扫码后的页面跳转多出几十毫秒，用户完全无感
2. **非核心路径**：这不涉及高频的支付签名和资金扣划，不会造成像 HttpApi 那样线程堆积引发的"支付卡死"
3. **天然隔离**：BFF 的并发模型通常比 HttpApi 更轻量，单个商家的回源不会像网关层那样产生剧烈的资源争抢

**结论**：BFF 采用"订阅-清除"模式，允许新商家上线时的首笔请求产生轻微 Redis 回源开销。

---

## 4. 实现细节

### 4.1 BFF 层实现：Map 管理与授权拦截

#### 4.1.1 内存缓存策略

**缓存策略（简要）**：订阅-清除（Cache-Aside）。实现详情见 `HybridConfigResolver.cs`（仓库内现有实现）。

要点：
- 订阅 `nxc:config:mapping`，收到消息仅清除对应 `mapKey` 的本地缓存（不携带全量载荷）
- Cache-Aside：首次或缓存缺失时从 Redis 拉取（`SMEMBERS`），滑动过期 24 小时，优先级设为 NeverRemove

示例（极简）：
```csharp
// Cache-Aside：从 cache 取，否则从 Redis 读取并设置 cache
var profiles = cache.GetOrCreate(mapKey, () => redis.SetMembersAsync($"nxc:map:{realm}:{provider}"));
```

（实现参考：`src/NexusContract.Hosting/Configuration/HybridConfigResolver.cs`）

**关键特性**：
- **订阅-清除**：收到 `MappingChange` 消息时，仅清除缓存，不携带载荷
- **冷启动拉取**：首次请求或缓存被清除后，从 Redis 拉取
- **滑动过期**：24 小时无访问自动过期，活跃商家始终热数据
- **NeverRemove**：避免被内存压力驱逐

#### 4.1.2 授权拦截 Middleware（简要）

授权中间件行为（伪代码）：
- 从 URL/Headers 提取 `realmId`/`provider`/`profileId`
- 调用 `MapCacheManager` 验证 `profileId` 是否被允许
- 不允许则返回 403；允许则调用 `await _next(context)`

示例（极简）：
```csharp
if (!await mapCache.IsAllowed(realmId, provider, profileId))
    return 403;
await _next(context);
```

（完整实现参考 `src/*/Middleware` 中的授权中间件）

### 4.2 HttpApi 层实现：简化为执行引擎

#### 4.2.1 移除 Map 查询逻辑

**变更前**（HybridConfigResolver.cs）：

```csharp
// ❌ HttpApi 需要查询 Map 层
var mapKey = BuildMapKey(realmId, providerName);
var profileIds = await _cacheService.GetOrCreateAsync(mapKey, async () =>
{
    return await _redisDb.SetMembersAsync(mapKey);
});

if (!profileIds.Contains(profileId))
{
    throw NexusTenantException.UnauthorizedProfile(realmId, profileId);
}
```

**变更后**：

```csharp
// ✅ HttpApi 直接使用 BFF 传来的 ProfileId
// 不再查询 Map 层，信任 BFF 已完成鉴权
var profileId = _tenantContext.ProfileId;  // 从 URL 路由参数提取
```

#### 4.2.2 订阅 ConfigChange 消息

```csharp
public class ConfigCacheManager
{
    private readonly IMemoryCache _cache;
    private readonly ISubscriber _redisSub;

    public ConfigCacheManager(IMemoryCache cache, IConnectionMultiplexer redis)
    {
        _cache = cache;
        _redisSub = redis.GetSubscriber();

        // 订阅 ConfigChange 消息
        _redisSub.Subscribe("nxc:config:change", (channel, message) =>
        {
            var msg = JsonSerializer.Deserialize<ConfigChangeMessage>(message);
            
            // 精准清除该 ProfileId 的 Inst 缓存
            var instKey = $"nxc:inst:{msg.RealmId}:{msg.ProviderName}:{msg.ProfileId}";
            _cache.Remove(instKey);
        });
    }
}
```

### 4.3 消息发布端实现（简要）

创建/发布流程要点：
- 保存 Inst/Pool/Map 到 Redis
- 发布 `MappingChange`（无载荷，仅通知 BFF 清除 map 缓存）
- 发布 `ConfigChange`（携带 `profileId`，HttpApi 精准清除该 ID 缓存）

极简示例：
```csharp
await redisSub.PublishAsync("nxc:config:mapping", msg);
await redisSub.PublishAsync("nxc:config:change", msgWithProfileId);
```

（完整实现位于 `TenantConfigurationManager`）
### 4.4 URL 路由配置

#### 4.4.1 FastEndpoints 路由调整（简要）

路由转换要点：将 operation 名称转换为路径并在开头添加 `{profileId}`，例如：
```
POST /api/alipay/{profileId}/trade/create
```

示例（极简）：
```csharp
Post("{profileId}/trade/create");
var profileId = Route<string>("profileId");
```

在 Handler 中以 `profileId` 构建 `TenantContext` 并执行请求。

---

## 5. 影响与风险

### 5.1 架构收益

| 维度 | 改进效果 | 说明 |
|-----|---------|------|
| **职责清晰性** | 🟢 显著提升 | BFF 负责授权，HttpApi 负责执行，边界清晰 |
| **隔离性** | 🟢 显著提升 | Map 层配置噪声不影响 HttpApi 的 Inst/Pool 缓存 |
| **确定性** | 🟢 显著提升 | URL 携带 ProfileId，消除运行时解析的不确定性 |
| **性能** | 🟢 轻微提升 | 冷启动减少 1 次 Redis 查询（跳过 Map） |
| **可观测性** | 🟢 显著提升 | URL 路径即业务身份，监控、日志更直观 |
| **安全性** | 🟢 显著提升 | BFF 作为信任边界，防止探测攻击 |

### 5.2 风险与应对

#### 风险 #1：BFF 层的回源抖动

**风险**：新商家上线时，BFF 首次请求需要回源 Redis（10-50ms）。

**应对**：
- ✅ BFF 处理的是低频业务路由，用户无感知
- ✅ 使用滑动过期，活跃商家始终热数据
- ✅ SemaphoreSlim 防止缓存击穿

#### 风险 #2：URL 暴露 ProfileId 的安全性

**风险**：攻击者可能遍历 ProfileId 探测系统。

**应对**：
- ✅ BFF Middleware 强制鉴权，非法 ProfileId 在 BFF 层被拦截
- ✅ HttpApi 信任 BFF，不再进行权限校验
- ✅ 审计日志记录所有鉴权失败的尝试

#### 风险 #3：消息丢失导致缓存不一致

**风险**：Pub/Sub 消息丢失率约 0.01%，可能导致缓存未及时清除。

**应对**：
- ✅ 滑动过期兜底（24 小时无访问自动过期）
- ✅ 绝对过期兜底（默认 30 天，可根据需求调整）
- ✅ 管理端提供"手动刷新"按钮

### 5.3 性能对比

| 场景 | 传统架构（Header传参） | 新架构（URL资源寻址） | 性能提升 |
|-----|---------------------|-------------------|---------|
| **BFF 鉴权** | 查询 Map（本地） | 查询 Map（本地） | 持平 |
| **HttpApi 冷启动** | 查 Map + Inst + Pool | 查 Inst + Pool | 减少 1 次 Redis 查询 |
| **HttpApi 热路径** | 纯内存（3层缓存） | 纯内存（2层缓存） | 轻微提升（内存更少） |
| **新商家上线** | HttpApi 回源 Map | BFF 回源 Map | 隔离性提升 |

---

## 6. 迁移路径

### 6.1 Phase 1：渐进式迁移（向后兼容）

**目标**：同时支持 URL 和 Header 两种模式，优先使用 URL。

```csharp
// 支持双模式：优先 URL，回退 Header
var profileId = RouteData.Values["profileId"]?.ToString() 
                ?? HttpContext.Request.Headers["X-Tenant-Profile"].ToString();

if (string.IsNullOrEmpty(profileId))
{
    throw new ArgumentException("ProfileId is required (URL or Header)");
}
```

**时间线**：2 周
**验证**：灰度 10% 流量测试，监控错误率

### 6.2 Phase 2：BFF 层实现 Map 管理

**目标**：在 BFF 层实现 MapCacheManager 和授权拦截 Middleware。

**变更清单**：
- ✅ 创建 `MapCacheManager` 类
- ✅ 实现 `TenantAuthorizationMiddleware`
- ✅ 订阅 `nxc:config:mapping` 消息
- ✅ 配置 ASP.NET Core Pipeline

**时间线**：1 周
**验证**：单元测试 + 集成测试

### 6.3 Phase 3：HttpApi 简化（移除 Map 查询）

**目标**：从 HttpApi 移除 Map 层查询逻辑。

**变更清单**：
- ✅ 修改 `HybridConfigResolver.cs`，移除 `ValidateOwnershipAsync`
- ✅ 订阅 `nxc:config:change` 消息（仅 ConfigChange，不再订阅 MappingChange）
- ✅ 调整 `TenantConfigurationManager.PreWarmGatewayAsync`，发布 ConfigChange 消息

**时间线**：1 周
**验证**：回归测试 + 性能测试

### 6.4 Phase 4：URL 路由全面推广

**目标**：所有新接口强制使用 URL 资源寻址模式。

**时间线**：1 周
**验证**：生产环境灰度 100% 流量

---

## 7. 监控与告警

### 7.1 监控指标（简要）

关键指标：

- BFF：`bff.map.cache.hit` / `bff.map.cache.miss` / `bff.auth.success` / `bff.auth.failure` / `bff.auth.latency_ms`
- HttpApi：`httpapi.inst.cache.hit` / `httpapi.inst.cache.miss` / `httpapi.latency_ms`

（在代码中以现有 Metrics 库计数与直方图方式上报）

### 7.2 告警规则

| 指标 | 阈值 | 告警级别 | 说明 |
|-----|------|---------|------|
| `bff.map.cache.miss` | > 100 次/分钟 | ⚠️ Warning | BFF Map 缓存频繁回源，检查缓存策略 |
| `bff.auth.failure` | > 50 次/分钟 | 🚨 Critical | 大量鉴权失败，疑似攻击 |
| `httpapi.inst.cache.miss` | > 10 次/分钟 | ⚠️ Warning | HttpApi Inst 缓存回源，检查消息推送 |
| `httpapi.latency_ms` (P99) | > 100ms | 🚨 Critical | 执行延迟飙升，立即排查 |

---

## 8. 总结

### 8.1 核心价值

通过 BFF/HttpApi 职责解耦与 URL 资源寻址，我们构建了一个极其稳固的架构三角形：

```
      URL携带ProfileId
         (确定性)
           ↑
          / \
         /   \
        /     \
   BFF鉴权   消息通知
  (隔离性)   (灵活性)
```

**三位一体**：
1. **URL** → 确定性（不需要猜 Profile）
2. **BFF** → 隔离性（非法请求进不去）
3. **消息** → 灵活性（实时生效，精准清理）

### 8.2 适用场景

**本架构特别适合**：
- ✅ ISV 多租户场景（新商家有测试流程）
- ✅ 就餐/零售高峰期（对老商家稳定性要求极高）
- ✅ 微服务架构（BFF 和 HttpApi 物理分离）
- ✅ 有完善的配置变更验证机制

**需要谨慎评估**：
- ⚠️ 单体应用（BFF 和 HttpApi 在同一进程）
- ⚠️ 自助注册场景（无人工介入，需增强冷启动容错）

### 8.3 架构哲学

**这是向业务妥协、向实战靠拢的设计：**

- **"粗暴"的扁平化复合键** → 经过多年验证的工业级选择
- **BFF 接受"个别抖动"** → 性能与复杂度的再平衡
- **HttpApi 坚如磐石** → 支付核心路径绝不能抖

**核心权衡**：
- **牺牲 BFF 的极致性能** → 换取 HttpApi 的职责纯净
- **增加 BFF 的鉴权步骤** → 换取系统的确定性和安全性
- **接受 URL 暴露 ProfileId** → 换取寻址的简单和确定性

**这是经过线上事故验证的实战方案：职责清晰，边界分明，隔离有效。**

---

**批准**: ✅ 架构组一致通过  
**影响范围**: 🔴 高（架构级变更，需分阶段迁移）  
**架构定位**: BFF/HttpApi 职责解耦 + URL 资源寻址 + 复合标识符策略  
**优先级 P0**：
- Phase 1：渐进式迁移（向后兼容）
- Phase 2：BFF 层实现 Map 管理
- Phase 3：HttpApi 简化（移除 Map 查询）

**下一步**: 实现 Phase 1 向后兼容模式、编写单元测试、灰度验证
