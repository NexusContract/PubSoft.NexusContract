# NexusContract 框架决策 - 快速参考表

## 中小文件决策速查（80-200行）

### 📋 文件清单与行数

| # | 文件名 | 行数 | 类别 | 核心决策 |
|---|--------|------|------|---------|
| 1 | AesSecurityProvider.cs | 108 | 🔐 Security | AES-256-CBC 硬件加速 + 随机IV |
| 2 | ProtectedPrivateKeyConverter.cs | 68 | 🔐 Security | JSON 层透明加密（明文内存 + 密文Redis） |
| 3 | TenantContextFactory.cs | 185 | 🏭 Factory | 三层递归提取（头→参数→body） |
| 4 | NexusGatewayClientFactory.cs | 118 | 🏭 Factory | FrozenDictionary + 点分标识符路由 |
| 5 | ConfigurationContext.cs | 177 | 📦 Context | Provider + Realm + Profile 三元组 |
| 6 | HybridConfigResolver.cs | 734* | 💾 Cache | Redis-First + L1(12h) + L2永久 |
| 7 | InMemoryConfigResolver.cs | 191 | 💾 Cache | 纯内存（开发环境）+ 文件热更新 |
| 8 | NexusGateway.cs | 174 | ⚙️ Engine | 纯异步四阶段管道 + ConfigureAwait(false) |
| 9 | StartupHealthCheck.cs | 133 | ✅ Validation | Fail-Fast + 全量问题收集 |
| 10 | NexusGatewayClient.cs | 171 | 🌐 Client | Primary Constructor + 异常统一化(NXC) |
| 11 | NexusEndpoint.cs | 89 | 🔌 Endpoint | Zero-Code 承诺（自动提取租户+执行） |
| 12 | TenantConfigurationManager.cs | 195 | 🛠️ Management | Map层(映射+授权) + 新商家隔离 |

*超范围但核心内容完整

---

## 🎯 按职责分类

### 🔐 安全设计（2个文件）
```
AesSecurityProvider (108)
  ├─ AES-256-CBC + AES-NI 硬件加速
  ├─ 随机 IV（每次不同）
  └─ 版本前缀 v1:（向后兼容）

ProtectedPrivateKeyConverter (68)
  ├─ Read: 密文→解密→明文
  ├─ Write: 明文→加密→密文
  └─ 明文驻留内存，密文驻留Redis
```

### 🏭 工厂模式（2个文件）
```
TenantContextFactory (185)
  ├─ L1: HTTP 头（最高优先级）
  ├─ L2: 查询参数
  └─ L3: 请求体 JSON
  └─ 跨平台别名映射（realm_id/sys_id/sp_mch_id）

NexusGatewayClientFactory (118)
  ├─ FrozenDictionary（O(1) 查询）
  ├─ 点分标识符路由（provider.endpoint.resource）
  └─ Builder 模式灵活配置
```

### 📦 上下文与配置（3个文件）
```
ConfigurationContext (177)
  ├─ 三元组标识：Provider + Realm + Profile
  ├─ 大小写不敏感Hash
  └─ 流式API链式调用

HybridConfigResolver (734)
  ├─ L1: MemoryCache(12h滑动 + 30天绝对 + NeverRemove)
  ├─ L2: Redis(永久 + RDB/AOF)
  ├─ 缓存击穿保护（SemaphoreSlim）
  ├─ 负缓存防穿透（1min）
  ├─ 精细化刷新（ConfigChange/MappingChange/FullRefresh）
  └─ 冷启动自愈（500ms超时 + Pull模式）

InMemoryConfigResolver (191)
  ├─ 纯内存 + ConcurrentDictionary
  ├─ 文件监控热更新
  └─ DEBUG模式区分（完整私钥 vs 脱敏）
```

### ⚙️ 核心引擎（2个文件）
```
NexusGateway (174)
  ├─ 纯异步四阶段：验证→投影→执行→回填
  ├─ ConfigureAwait(false)（性能+10-30%）
  └─ 异常转译（ContractIncompleteException→诊断码）

StartupHealthCheck (133)
  ├─ Fail-Fast + 全量问题收集
  ├─ 按契约分组错误
  └─ JSON诊断报告（CI/CD集成）
```

### 🌐 客户端与端点（3个文件）
```
NexusGatewayClient (171)
  ├─ Primary Constructor（无样板代码）
  ├─ 自动类型推断
  └─ 异常统一化（→NexusCommunicationException + NXC诊断码）

NexusEndpoint (89)
  ├─ Zero-Code承诺
  ├─ 自动路由生成（OperationId→/provider/operation）
  └─ 自动租户提取+异常处理

TenantConfigurationManager (195)
  ├─ CRUD高层API
  ├─ Map层（Redis Set）
  ├─ 默认ProfileId支持
  └─ 新商家隔离（Pub/Sub通知→冷启动自愈）
```

---

## 💡 核心决策对比表

### 安全加密

| 决策 | 选择 | 原因 | 代价 |
|------|------|------|------|
| 算法 | AES-256-CBC | 硬件加速(AES-NI) | ~5μs耗时(可接受) |
| IV生成 | 随机生成 | 防模式攻击 | +16字节存储 |
| 存储位置 | 密文→Redis, 明文→内存 | 平衡安全+性能 | 需管理明文生命周期 |
| 版本化 | v1: 前缀 | 向后兼容性 | 序列化开销 |

### 配置缓存

| 层级 | 存储 | TTL | 特性 | 场景 |
|------|------|-----|------|------|
| L1 | MemoryCache | 12h滑动+30天绝对 | SlidingExpiration+NeverRemove | 进程内 |
| L2 | Redis | 永久 | RDB/AOF持久化 | 多实例共享 |
| L3 | Database | 可选 | 冷备份+审计 | 法规合规 |

### 工厂路由

| 参数 | TenantContextFactory | NexusGatewayClientFactory |
|------|----------------------|--------------------------|
| 输入 | HttpContext | operationKey (string) |
| 优先级 | 头 > 参数 > body | 无（直接查找） |
| 数据结构 | 别名HashSet | FrozenDictionary |
| 复杂度 | O(n×m) 别名查询 | O(1) 不可变查询 |

---

## ⚡ 性能数据

### 加密性能

```
加密耗时：~5μs（2KB密钥）
Redis延迟：~1ms
比值：Redis延迟 >> 加密耗时（200倍）
→ 加密成为"免费操作"
```

### 缓存命中率

```
L1命中率：99.99%+（滑动过期+大多数不变）
平均响应：纯内存操作（<1μs）
冷启动回源：Redis (~1ms) + 反序列化
```

### 冷启动保护

```
超时时间：500ms（new tenant protection）
影响范围：仅首次请求
自愈机制：ColdStartSyncAsync + Pull模式
备用方案：30天TTL绝对过期兜底
```

---

## 🔗 ADR 映射

| ADR | 标题 | 核心文件 | 关键特性 |
|-----|------|---------|---------|
| ADR-008 | Redis-First存储 | HybridConfigResolver | L1+L2双层 |
| ADR-009 | 三层数据模型 | HybridConfigResolver, TenantConfigurationManager | Map+Config+Backup |
| ADR-012 | IProvider适配器 | NexusGateway | 四阶段管道 |
| ADR-013 | Realm+Profile | ConfigurationContext, TenantContextFactory | 三元组隔离 |
| ADR-014 | 默认解析+自愈 | HybridConfigResolver | ResolveDefaultProfile |
| ADR-015 | 懒加载+永久缓存 | HybridConfigResolver | SlidingExpiration |
| ADR-016 | 新商家隔离 | TenantConfigurationManager | PreWarmGateway |

---

## ⚠️ 风险与注意事项

### 🔴 高风险

```
1. 冷启动500ms超时
   - 风险：新商家首次请求可能失败
   - 缓解：客户端重试机制必须启用
   
2. Pub/Sub消息丢失
   - 风险：缓存刷新延迟
   - 缓解：30天TTL绝对过期兜底
   
3. 内存成本（NeverRemove）
   - 风险：配置数量极多时内存溢出
   - 缓解：监控L1命中率 + 按需调整优先级
```

### 🟡 需验证项

```
1. L1命中率99.99%假设（需生产验证）
2. SemaphoreSlim在超高并发时的竞争（>10kQPS需压测）
3. Redis SMEMBERS耗时（ProfileId数量>1000？）
4. ProtectedPrivateKeyConverter在高频反序列化下的性能
5. 冷启动自愈在网络不稳定时的表现
```

### 🟢 最佳实践

```
✅ 启用启动期健康检查（warmup=true）
✅ 监控缓存命中率 + Redis查询延迟
✅ 配置Pub/Sub消息重试机制
✅ 定期审计权限索引准确性
✅ 压测新商家冷启动流程（500ms超时）
```

---

## 📊 决策复杂度矩阵

```
                复杂度
                  ▲
                  │  HybridConfigResolver(★★★★★)
                  │     ▲
                  │     │ TenantContextFactory(★★★)
                  │     │ TenantConfigurationManager(★★★)
                  │     │
                  │ ConfigurationContext(★★)
                  │ NexusGateway(★★)
                  │ AesSecurityProvider(★★)
                  │
         NexusEndpoint(★)
         NexusGatewayClient(★)
                  └──────────────────────────── 影响范围
                       ★ = 1星（简单）
```

---

## 🎓 学习路径建议

### 初级（理解基础）
1. ConfigurationContext（三元组模型）
2. AesSecurityProvider（对称加密基础）
3. NexusGatewayClient（HTTP调用）

### 中级（掌握集成）
1. TenantContextFactory（多源提取）
2. NexusGateway（四阶段管道）
3. HybridConfigResolver（缓存策略）

### 高级（优化与故障排除）
1. HybridConfigResolver 的缓存击穿防护
2. TenantConfigurationManager 的权限隔离
3. StartupHealthCheck 的诊断体系

---

**快速查询指南**：
- 安全问题？→ AesSecurityProvider + ProtectedPrivateKeyConverter
- 性能问题？→ HybridConfigResolver 的缓存策略
- 多租户问题？→ ConfigurationContext + TenantConfigurationManager
- 网关集成？→ NexusGatewayClient + NexusGatewayClientFactory
- 启动异常？→ StartupHealthCheck 的诊断报告
