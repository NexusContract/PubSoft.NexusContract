# NexusContract 框架决策提取 - 中小文件组（80-200行）

> 从 80-200 行的 CS 和 MD 文件中提取框架核心设计决策

**生成时间**：2026-01-11  
**范围**：src/, examples/（排除 bin/, obj/, adr/, docs/adr/）  
**文件总数**：11 个关键文件

---

## 一、安全设计决策

### 【文件 1】AesSecurityProvider.cs (108 行)

**核心决策：**

1. **AES-256-CBC 硬件加速策略**
   - 选择 AES-256-CBC 而非流密码，原因：支持硬件加速（CPU AES-NI 指令集）
   - 加密耗时仅 ~5μs（2KB 密钥），远低于网络 IO 延迟（1ms Redis）
   - 性能计算：网络延迟 >> 加密延迟，加密成为"免费操作"

2. **随机 IV 防模式攻击**
   - 每次加密生成新的随机 IV（16 字节），防止密文模式识别
   - IV 与密文一同存储：`格式: v1:[IV(16字节)][密文]`
   - 版本前缀 `v1:` 便于未来算法升级（向后兼容性）

3. **版本化加密格式**
   - 前缀 `v1:` 标记加密方案版本，支持运行时多算法共存
   - 兼容性：升级到 `v2:` 时旧密文仍可解密

**关键代码片段：**
```csharp
// 加密格式设计：版本 + IV + 密文
byte[] result = new byte[aes.IV.Length + cipherBytes.Length];
Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
Buffer.BlockCopy(cipherBytes, 0, result, aes.IV.Length, cipherBytes.Length);
return VersionPrefix + Convert.ToBase64String(result);
```

**框架关系：**
- 用于 Redis L2 缓存中的 PrivateKey 加密（HybridConfigResolver）
- 配置文件中敏感字段加密（ProtectedPrivateKeyConverter）
- 支撑 ADR-008 Redis-First 存储策略

---

### 【文件 2】ProtectedPrivateKeyConverter.cs (68 行)

**核心决策：**

1. **JSON 序列化层透明加密**
   - 在 JsonConverter 层拦截 PrivateKey 字段的序列化/反序列化
   - Read（从 Redis）：密文 → 解密 → 明文
   - Write（到 Redis）：明文 → 加密 → 密文

2. **"明文驻留内存，密文驻留 Redis" 策略**
   - 应用内存中保存明文 ProviderSettings（避免每次签名都解密）
   - Redis 中存储密文（即使 Redis 泄露也无法直接使用）
   - 传输加密：Redis 连接使用 TLS

3. **加密开销仅在缓存操作时触发**
   - 热路径（L1 命中）：零加密开销
   - 冷启动（L2 → L1）：+5μs 加密/解密时间（可忽略）

**关键代码片段：**
```csharp
// Read：从 Redis 读出时解密
public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
{
    string? encryptedValue = reader.GetString();
    return _securityProvider.Decrypt(encryptedValue);
}

// Write：写入 Redis 时加密
public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
{
    string encryptedValue = _securityProvider.Encrypt(value);
    writer.WriteStringValue(encryptedValue);
}
```

**框架关系：**
- HybridConfigResolver 中的序列化 Hook（获取 JsonOptions）
- 支撑多租户配置的安全隔离

---

## 二、工厂模式与对象创建

### 【文件 3】TenantContextFactory.cs (185 行)

**核心决策：**

1. **协议相关 → 协议无关的转换边界**
   - **输入**：ASP.NET Core 的 HttpContext（协议特定，包含 HTTP 头、查询参数等）
   - **输出**：NexusContract 的 TenantContext（协议无关，跨平台）
   - **职责**：完全解耦业务逻辑与 HTTP 细节

2. **三层递归提取策略（优先级）**
   - **L1：HTTP 请求头**（最高优先级）- 标准化传输方式
     - `X-Tenant-Realm`, `X-Tenant-Profile`, `X-Provider-Name`
   - **L2：HTTP 查询参数**（中等优先级）- 备选传输方式
     - `?realm_id=xxx&profile_id=xxx&provider=xxx`
   - **L3：HTTP 请求体 JSON**（最低优先级）- 业务数据中提取
     - `{ "realm_id": "xxx", "profile_id": "xxx", "provider_name": "xxx" }`

3. **跨平台兼容别名映射**
   - 支持多个别名（大小写不敏感）：`realm_id`, `sys_id`, `sp_mch_id`（支付宝）等
   - 使用 `HashSet<string>` + `StringComparer.OrdinalIgnoreCase` 实现高效查询
   - 消除平台差异：ISV、服务商、特约商户等概念统一映射

4. **无状态设计（幂等性）**
   - 纯静态方法，每次请求重新提取（避免请求污染）
   - 支持异步：`EnableBuffering()` 支持多次读取请求体

**关键代码片段：**
```csharp
// 优先级递归：请求头 > 查询参数 > 请求体
realmId = ExtractFromHeaders(httpContext, RealmIdAliases, ...);
if (string.IsNullOrEmpty(realmId))
    realmId = ExtractFromQuery(httpContext, RealmIdAliases);
if (string.IsNullOrEmpty(realmId))
{
    var (bodyRealmId, _, _) = await ExtractFromJsonBodyAsync(httpContext);
    realmId ??= bodyRealmId;
}
```

**框架关系：**
- 在 NexusEndpoint 中自动调用（Zero-Code 承诺的基础）
- 解决 Hosting 层"污染"问题（ADR-005 允许的唯一依赖 ASP.NET Core 的地方）

---

### 【文件 4】NexusGatewayClientFactory.cs (118 行)

**核心决策：**

1. **FrozenDictionary + 点分标识符路由**
   - **为什么 FrozenDictionary？**
     - 启动期注册所有网关 URI → 编译成不可变集合
     - 运行时 O(1) 查询，无锁、无哈希碰撞风险
     - 内存布局紧凑，适合高频路由查询（每次 SendAsync 一次）
     - 符合"启动期锁定，运行期极低开销"设计哲学
   
   - **为什么点分标识符？**
     - 支付网关命名规范：`provider.endpoint.resource` 形式
     - 按第一部分路由（provider）最符合多网关架构
     - 示例：`allinpay.yunst.trade.pay` → 路由到 `allinpay` 网关
     - 保持扩展性：未来轻松加入新的支付方供应商

2. **Builder 模式支持灵活配置**
   - 推迟冻结时机：运行时动态注册网关
   - 启动期冻结：避免运行时意外修改

**关键代码片段：**
```csharp
// 点分标识符解析：取第一部分
string providerKey = operationKey.Split('.')[0];  // "allinpay.yunst" → "allinpay"

// FrozenDictionary 支持高频 O(1) 查询
if (!gatewayMap.TryGetValue(providerKey, out var gatewayUri))
    throw new KeyNotFoundException(...);
```

**框架关系：**
- 支撑多网关架构（支付宝、微信、银联等并存）
- 与 NexusGatewayClient 配合实现"近无开销"的网关路由

---

## 三、上下文管理与配置

### 【文件 5】ConfigurationContext.cs (177 行)

**核心决策：**

1. **三元组标识（Provider + Realm + Profile）**
   - **ProviderName**：渠道标识（"Alipay", "WeChat"）→ 路由 Redis 键
   - **RealmId**：域/归属权（Alipay: sys_id / WeChat: sp_mchid）
     - 业务含义：ISV 服务商系统 ID，标识逻辑隔离的业务空间
     - 防越权隔离：不同 Realm 的配置完全隔离
   - **ProfileId**：档案/执行单元（Alipay: app_id / WeChat: sub_mchid）
     - 业务含义：Realm 下的具体业务实例（子商户、设备）
     - 可选字段：某些场景可通过默认规则自动补全

2. **大小写不敏感 Hash 计算**
   - ProviderName 使用 `StringComparer.OrdinalIgnoreCase` 进行哈希
   - 确保 "Alipay" 和 "alipay" 映射到同一缓存键
   - 防止缓存命中率下降

3. **流式 API 支持链式调用**
   - `WithMetadata()`, `WithProfileId()` 返回 `this`
   - 支持 Builder 模式初始化

**关键代码片段：**
```csharp
// 三元组标识 + 大小写不敏感 Hash
public override int GetHashCode()
{
    hash = hash * 31 + StringComparer.OrdinalIgnoreCase.GetHashCode(ProviderName);
    hash = hash * 31 + RealmId?.GetHashCode() ?? 0;
    hash = hash * 31 + ProfileId?.GetHashCode() ?? 0;
    return hash;
}
```

**框架关系：**
- 从 TenantContext（租户身份）映射到 ConfigurationContext（配置查询凭证）
- IConfigurationResolver 的输入参数（决定配置的查询路径）
- 与多租户隔离架构深度绑定（ADR-009）

---

### 【文件 6】HybridConfigResolver.cs (734 行 - 超范围，但核心部分在范围内)

**核心决策：**

1. **Redis-First + 内存缓存的双层架构**
   - **L1（MemoryCache）**：进程内，12 小时 TTL + 30 天绝对过期
   - **L2（Redis）**：主数据源，永久保存 + RDB/AOF 持久化
   - **L4（可选）**：数据库，冷备份 + 审计日志
   
   - **为什么是 Redis-First？**（ADR-008）
     - ISV 配置极低频变更（通常以"年"为单位）
     - 读多写少，KV 结构，无复杂查询需求
     - 替代关系型数据库的合理选择

2. **滑动过期 + 永不剔除 + Pub/Sub 强一致性**
   - **SlidingExpiration（24h）**：只要有业务流量，缓存持续有效
   - **AbsoluteExpiration（30天）**：防止"僵尸配置"永久驻留
   - **Priority.NeverRemove**：防止内存压力时配置被意外剔除
   
   - **业务收益**（针对就餐支付高实时性）：
     - 消除"12小时卡点"回源（Redis 查询导致 1ms 延迟）
     - 系统可脱网运行（Redis 故障时依然可用 30 天）
     - L1 命中率：99.99%+（几乎所有请求命中内存）

3. **缓存击穿保护 + 负缓存防穿透**
   - `SemaphoreSlim` 限制并发回源 Redis（同一 cacheKey 仅一个线程）
   - 负缓存：配置不存在时缓存标记（1 分钟 TTL）
   - 防止恶意探测不存在的 RealmId 导致 Redis 雪崩

4. **权限校验层（IDOR 防护）**
   - `ValidateOwnershipAsync()` 验证 AppId 是否属于该 SysId
   - 使用 Redis Set 存储权限白名单（O(1) 查询）
   - 权限索引缓存 24 小时，冷启动自愈

5. **精细化缓存刷新策略**
   - **ConfigChange**（配置变更）：仅清理单个 ProfileId 缓存，不触碰 Map 权限索引
   - **MappingChange**（映射变更）：清理配置缓存 + Map 索引
   - **FullRefresh**（全量刷新）：清理 Realm 所有缓存 + Map 索引
   
   - **性能收益**：500 个 ProfileId 的 Realm，单个密钥轮换不再影响其他 499 个

6. **冷启动自愈（Pull 模式）**
   - L1 未命中时，通过 `ColdStartSyncAsync()` 从 Redis 拉取
   - **500ms 快速失败保护**：新商家冷启动失败可重试，防止线程池耗尽
   - 负缓存策略：空 Set 缓存 5 分钟

**关键代码片段：**
```csharp
// 双层缓存 + 缓存击穿保护
if (_memoryCache.TryGetValue(cacheKey, out object? cachedValue)) return cached;

SemaphoreSlim cacheLock = _locks.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));
await cacheLock.WaitAsync(ct);
try
{
    // 双重检查锁定
    if (_memoryCache.TryGetValue(cacheKey, out var cached2)) return cached2;
    
    // 尝试 Redis
    RedisValue l2Value = await _redisDb.StringGetAsync(cacheKey);
    if (l2Value.HasValue) return DeserializeConfig(l2Value);
    
    // 负缓存（防穿透）
    _memoryCache.Set(cacheKey, ConfigNotFoundMarker, 
        new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = NegativeCacheTtl });
}
finally { cacheLock.Release(); }
```

**框架关系：**
- IConfigurationResolver 的生产级实现
- 支撑多实例部署、配置跨实例共享
- 与 TenantConfigurationManager 配合实现 CRUD

---

### 【文件 7】InMemoryConfigResolver.cs (191 行)

**核心决策：**

1. **纯内存存储 + ConcurrentDictionary**
   - 无外部依赖（不需要数据库或 Redis）
   - 支持动态添加/更新/删除配置
   - 线程安全（ConcurrentDictionary）

2. **文件监控热更新（可选）**
   - 支持从 JSON 文件加载配置
   - FileSystemWatcher 监控变化（延迟 100ms 避免锁定）

3. **适用场景精准定位**
   - ✅ 单元测试（Mock 配置）
   - ✅ 开发环境（快速启动）
   - ❌ 生产环境（无持久化）
   - ❌ 多实例部署（配置不同步）

4. **DEBUG 模式区分**
```csharp
#if DEBUG
    return _cache.Values.ToList();  // DEBUG：返回完整配置（包括私钥）
#else
    return _cache.Values.Select(MaskSensitiveData).ToList();  // 生产：脱敏私钥
#endif
```

**框架关系：**
- IConfigurationResolver 的开发级实现
- 与 HybridConfigResolver 形成互补（生产 vs 开发）
- 支撑测试速度与生产可靠性的平衡

---

## 四、核心执行引擎

### 【文件 8】NexusGateway.cs (174 行)

**核心决策：**

1. **纯异步设计（无同步版本）**
   - 禁止 `.Wait()` 和 `.Result` 同步等待
   - 原因：避免线程池耗尽（高延迟场景：2s × 400 TPS = 800 线程压力）

2. **四阶段管道架构**
   - **验证**（Validation）：缓存后极快（NexusContractMetadataRegistry O(1)）
   - **投影**（Projection）：请求对象 → 字典（字段迭代 + 加密）
   - **执行**（Execution）：Provider 处理（线程于此释放回线程池）
   - **回填**（Hydration）：字典 → 响应对象（类型转换）

3. **ConfigureAwait(false) 的哲学**
   - 避免切换回 UI 线程（支付系统总是后端）
   - 继续使用线程池线程，无上下文切换开销
   - 性能优化：+10% 到 +30% 吞吐量（根据场景）

4. **异常转译体系**
   - ContractIncompleteException → 诊断异常（包含结构化数据）
   - 其他异常 → InvalidOperationException（防止信息泄露）
   - 异常包含诊断码（NXC101, NXC201 等）用于日志集成

**关键代码片段：**
```csharp
public async Task<TResponse> ExecuteAsync<TResponse>(
    IApiRequest<TResponse> request,
    Func<ExecutionContext, IDictionary<string, object>, Task<...>> executorAsync,
    CancellationToken ct = default)
{
    // 1. 验证契约（缓存后极快）
    ContractMetadata metadata = NexusContractMetadataRegistry.Instance.GetMetadata(requestType);
    
    // 2. 投影请求
    IDictionary<string, object> projectedRequest = _projectionEngine.Project(request);
    
    // 3. 异步执行（线程于此释放）
    var responseDict = await executorAsync(executionContext, projectedRequest)
        .ConfigureAwait(false);
    
    // 4. 回填响应
    TResponse response = _hydrationEngine.Hydrate<TResponse>(responseDict);
    return response;
}
```

**框架关系：**
- 【决策 A-501】支付网关的唯一门面
- 为每个微服务提供统一的编排入口
- 与 Provider 层配合实现"签名+加密+投影"的自动化

---

### 【文件 9】StartupHealthCheck.cs (133 行)

**核心决策：**

1. **Fail-Fast 设计 + 全量问题收集**
   - 一次性扫描所有契约，收集全量问题
   - 避免"修一个跑一次"的低效循环
   - 启动期失败 Fast-Fail（比运行时发现问题早）

2. **诊断报告结构化输出**
   - 按契约分组错误（便于定位）
   - 错误级别分层：Critical > Error > Warning
   - JSON 格式输出（便于 CI/CD 集成）

3. **可配置的预热测试**
   - `warmup=true` 时预编译投影器/水化器
   - 提前发现动态生成代码的问题
   - 生产环境推荐启用（+50-100ms 启动时间，换取运行时稳定性）

**框架关系：**
- 应用启动入口（Program.cs 中调用）
- 与 ContractValidator 配合实现启动期质量保证
- NexusContractMetadataRegistry.Preload() 的驱动器

---

## 五、客户端集成

### 【文件 10】NexusGatewayClient.cs (171 行)

**核心决策：**

1. **Primary Constructor 零冗余设计**
   - .NET 10 C# 13 一级构造函数
   - 减少 50%+ 样板代码
   - 代价：仅限 .NET 10+（2026 年已是合理约束）

2. **自动类型推断**
   - `SendAsync<TResponse>()` 自动推断响应类型
   - 编译器零猜测，开发者零烦恼
   - 泛型约束：`where TResponse : class, new()`

3. **异常统一化（NXC 诊断体系）**
   - 无论错误来自验证、序列化、HTTP 还是反序列化
   - 都统一为 `NexusCommunicationException`
   - 自动填充 NXC 诊断码（NXC101, NXC201 等）
   
   - **为什么？**
     - 调用者仅需 `catch` 一个异常类型
     - 结构化的 DiagnosticData 便于日志和监控
     - 内部异常存储在 InnerException 中供细粒度调试

4. **客户端不提供 Project() 方法**
   - Client 是 BFF 层通过 HTTP 调用的工具，不含本地投影逻辑
   - 投影是 Provider 和 Gateway 的职责
   - BFF 只需：构造契约 → SendAsync → 接收响应

**关键代码片段：**
```csharp
// 自动异常转换为 NexusCommunicationException
try { /* HTTP 操作 */ }
catch (ContractIncompleteException contractEx)
{
    throw NexusCommunicationException.FromContractIncomplete(contractEx);
}
catch (HttpRequestException httpEx)
{
    throw NexusCommunicationException.FromHttpError($"Network error: {httpEx.Message}", 500, httpEx);
}
```

**框架关系：**
- 与 NexusGatewayClientFactory 配合支持多网关
- 是远程 HttpApi 调用的标准工具

---

## 六、端点与配置管理

### 【文件 11】NexusEndpoint.cs (89 行)

**核心决策：**

1. **Zero-Code 承诺的实现**
   - 继承 NexusEndpoint<TRequest> 即可自动：
     - 提取租户上下文（TenantContextFactory）
     - 调用 NexusEngine 执行
     - 处理异常（rent 异常 → HTTP 403）

2. **自动路由生成**
   - OperationId `alipay.trade.create` → POST `/alipay/trade/create`
   - （当前版本）使用默认路由，后续版本从元数据自动生成

3. **双层泛型约束**
   - `NexusEndpoint<TRequest, TResponse>` - 完整型
   - `NexusEndpoint<TRequest>` - 简化型（自动推断 EmptyResponse）

**框架关系：**
- FastEndpoints 7.x 的自定义基类
- TenantContextFactory 的应用入口
- 与 NexusEngine 配合实现"一行代码"承诺

---

### 【文件 12】TenantConfigurationManager.cs (195 行)

**核心决策：**

1. **高层 API 隐藏实现细节**
   - 封装 HybridConfigResolver 的 CRUD 操作
   - 为运营后台和命令行工具提供统一接口

2. **三层模型的映射层（Map Layer）**
   - Redis Set 存储授权 ProfileId 集合
   - 关键：`nxc:map:{realm}:{provider}`
   - 支持 SISMEMBER（权限校验）+ SMEMBERS（配置发现）

3. **默认 ProfileId 支持**
   - 某些场景下 ProfileId 可自动补全
   - 映射层使用 `{mapKey}:default` 标记
   - 冷启动自愈时优先使用默认

4. **新商家上线隔离策略（ADR-016）**
   - 创建新商家后调用 `PreWarmGatewayAsync()`
   - 发送 MappingChange 消息清除网关 Map 缓存
   - 下次请求触发 ColdStartSyncAsync 回源 Redis
   - 隔离效果：管理端 0 影响，首次请求 +10-50ms

**关键代码片段：**
```csharp
// 原子性更新：配置 + 映射层 + 默认标记
var transaction = _redisDb.CreateTransaction();
await _resolver.SetConfigurationAsync(identity, configuration, ct);  // 写配置
await transaction.SetAddAsync(mapKey, profileId);  // 更新映射层
if (isDefault) await transaction.StringSetAsync(defaultMarker, profileId);  // 设置默认
await transaction.ExecuteAsync();
```

**框架关系：**
- HybridConfigResolver 的高层管理接口
- 应用于运营后台（租户管理界面）

---

## 七、关键设计模式总结

| 模式 | 文件 | 用途 |
|------|------|------|
| **Factory** | TenantContextFactory, NexusGatewayClientFactory | 对象创建与路由 |
| **Builder** | NexusGatewayClientFactory, HybridConfigResolver | 灵活配置 |
| **Strategy** | AesSecurityProvider, ProtectedPrivateKeyConverter | 加密策略 |
| **Repository** | InMemoryConfigResolver, HybridConfigResolver | 数据访问 |
| **Two-Level Cache** | HybridConfigResolver | 性能优化 |
| **Double-Check Lock** | HybridConfigResolver | 并发控制 |
| **Fail-Fast** | ContractStartupHealthCheck | 错误处理 |
| **Pipeline** | NexusGateway | 请求处理 |

---

## 八、跨文件架构关系

```
Input Flow:
  HTTP Request (FastEndpoints)
      ↓
  NexusEndpoint<TRequest>
      ↓
  TenantContextFactory.CreateAsync() [从HTTP头/查询参数/请求体提取]
      ↓
  ConfigurationContext [构建查询凭证]
      ↓
  HybridConfigResolver.ResolveAsync() [L1缓存 → L2(Redis)]
      ↓
  ProviderSettings [含加密的PrivateKey]
      ↓ (ProtectedPrivateKeyConverter.Read 解密)
      ↓
  Provider [使用明文PrivateKey进行签名]
      ↓
  NexusGateway.ExecuteAsync()
      ├─ ProjectionEngine [投影请求]
      ├─ Provider [签名+加密]
      └─ HydrationEngine [回填响应]
      ↓
  Output [HTTP Response]

Security Layers:
  - L1: AES256 加密（ProviderSettings.PrivateKey in Redis）
  - L2: IDOR 防护（ValidateOwnershipAsync）
  - L3: 400ms 快速失败超时（冷启动自愈）
  - L4: Pub/Sub Refreshing (缓存一致性)

Caching Strategy:
  L1 (In-Process): 12h 滑动 + 30天绝对 + NeverRemove
  L2 (Redis): 永久存储 + RDB/AOF
  L3 (Optional): 数据库备份

Configuration Isolation:
  Realm = Logical Space (服务商级别)
  Profile = Business Instance (子商户级别)
  Map Layer = Authorization + Discovery
```

---

## 九、性能关键指标

| 指标 | 值 | 备注 |
|------|-----|------|
| **L1 缓存命中率** | 99.99%+ | 滑动过期 + 大多数配置不变 |
| **加密耗时（2KB密钥）** | ~5μs | 硬件加速 (AES-NI) |
| **Redis 单次查询** | ~1ms | 网络延迟 + 反序列化 |
| **L1 → L2 回源（缓存击穿保护）** | 仅1个线程 | SemaphoreSlim 限流 |
| **冷启动超时保护** | 500ms | 新商家失败可重试 |
| **全量预热（WarmupAsync）** | 依配置数量 | 使用 SCAN 避免 KEYS * 阻塞 |

---

## 十、与 ADR 的对应关系

| ADR | 核心决策 | 对应文件 |
|-----|---------|---------|
| ADR-008 | Redis-First 存储策略 | HybridConfigResolver |
| ADR-009 | 三层数据模型（Mapping+Config+Backup） | HybridConfigResolver, TenantConfigurationManager |
| ADR-012 | IProvider 适配器模式 | NexusGateway |
| ADR-013 | Realm 与 Profile 抽象 | ConfigurationContext, TenantContextFactory |
| ADR-014 | 默认解析与自愈策略 | HybridConfigResolver.ResolveDefaultProfileAsync |
| ADR-015 | 懒加载与永久缓存 | HybridConfigResolver 的滑动过期策略 |
| ADR-016 | 新商家上线隔离 | TenantConfigurationManager.PreWarmGatewayAsync |

---

## 十一、建议与注意事项

### 🔴 高风险决策
1. **500ms 冷启动超时**：新商家可能首次请求超时，需要客户端重试机制
2. **Pub/Sub 消息丢失**：网络故障时缓存刷新延迟，30 天 TTL 兜底

### 🟡 需要验证
1. **99.99% L1 命中率假设**：需在生产环境实际监控
2. **SemaphoreSlim 竞争**：高并发场景（>10k QPS）需压测
3. **ConcurrentDictionary 内存占用**：大量配置（>100k）时评估内存压力

### 🟢 最佳实践
1. 监控 L1 缓存命中率 → 调整 TTL
2. 监控 Redis SMEMBERS 耗时 → 评估 ProfileId 数量上限
3. 定期验证权限索引准确性 → IDOR 防护生效

---

**生成工具**：GitHub Copilot  
**验证状态**：✅ 所有文件已读取并分析  
**最后更新**：2026-01-11
