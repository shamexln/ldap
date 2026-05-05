# ADR-0002:IdP 架构范式 —— Facade / Core / Source 三层

- **状态**: Proposed
- **决策日期**: 2026-05-04
- **最后更新**: 2026-05-05(§附录 B 状态卡片随 12 次后续 commit 持续刷新)
- **决策人**: ImprivataProxy 项目组
- **相关文档**: [adsync-vs-saml.md (ADR-0001)](./adr-0001-adsync-vs-saml.md) / [LDAP.md](./LDAP.md) / [plan.md](../plan.md)
- **实施记录**: [adr-0002-phase-ab-implementation.md](./adr-0002-phase-ab-implementation.md) —— Phase α + β + IdpCore 去 AD 化已落地,227/227 tests 绿

---

## 1. 背景

[ADR-0001](./adr-0001-adsync-vs-saml.md) 确定了身份源选型:**AD LDAP 同步 + 本地账户库**,显式放弃 SAML 路线,同时在 §7 留下接口抽象扩展口(`IRemotePasswordVerifier`, `IUserDirectorySync`, `Identity.Source` 枚举)。

[plan.md](../plan.md) 里的 [§项目结构](../plan.md#L688) 已经把代码大致分成 `Endpoints/ Authentication/ Accounts/ ActiveDirectory/ Tickets/ Admin/` 等目录,逻辑上接近"Facade + Core + Source"三层,但**没有把这个分层显式表达出来**。结果:

- 新人读代码需要自己揣摩"这段属于协议适配还是认证核心";
- ADR-0001 §7 的扩展口漂在空中,没有归位到具体目录;
- 未来加 SAML / OIDC Facade 时,边界模糊,容易再次耦合死;
- 测试面积偏组合爆炸(协议 × 协议,协议 × 源)。

本 ADR 把 plan.md 隐含的分层**正式化**,并定义各层的**接口契约**,作为项目的架构范式。

---

## 2. 核心洞察:Imprivata ProveID Web API 本来就是个 IdP

这是本 ADR 的立足点。

IdP(Identity Provider)的标准职责,Imprivata ProveID 全部具备:

| IdP 标准职责 | Imprivata ProveID 对应 | 本项目实现 |
|------------|----------------------|-----------|
| 接受认证请求 | `POST /AuthUser` | `AuthUserEndpoint.PostAsync` |
| 多因子认证 | PWD / UID / PIN / FP / PKI / KRB / ... | `AuthEngine` 按 modalityID 分派 |
| 会话状态机(多步认证) | `ServerState` | `AuthSessionStore` |
| 签发身份凭证 | AuthTicket (OStick) | `JwtTokenIssuer` 发 JWT |
| 验证凭证 | `Authorization: OStick ostick.ticket=<x>` | `OStickAuthenticationHandler` |
| 撤销凭证 | `CANCEL /AuthUser` | `TicketBlacklistService` |
| 属性发布 | XML `<UserIdentity>`、`<RemainingAuthPolicy>` | Imprivata Response builder |
| 服务发现 | `/Servers`, `/Domains`, `/Modalities` | 三个静态端点 |

**结论**:Imprivata ProveID 只是"用了专有 XML 协议 + OStick Ticket 的 IdP",核心抽象跟 SAML IdP / OIDC Provider 完全对应。本项目要做的事,本质是**"构建一个本地 IdP,前端说 Imprivata 协议,后端认 AD"**。

一旦接受这个视角,合理的架构分层就自然浮现。

---

## 3. 决策

**采用三层架构:Protocol Facade + IdP Core + Identity Sources**。

```
┌────────────────────────────────────────────────────────────────┐
│                     Protocol Facades                            │
│   Imprivata ProveID XML  │  Admin REST  │  (SAML, future)       │
│           ▲                      ▲                ▲             │
│           │   [IProtocolFacade]  │                │             │
├───────────┼──────────────────────┼────────────────┼─────────────┤
│           ▼                      ▼                ▼             │
│                          IdP Core                               │
│   AuthEngine  │  TokenIssuer  │  SessionStore  │  LockoutPolicy │
│   PwdAuth     │  UidAuth      │  PinAuth       │  AuditLog      │
│                                                                 │
│   依赖 [IRemotePasswordVerifier] [IUserDirectorySync]           │
├─────────────────────────────────────────────────────────────────┤
│                     Identity Sources                            │
│   Local DB (SQLite) │  Active Directory (LDAPS)  │  (future)    │
│   [IUserStore]        [IRemotePasswordVerifier,                │
│                        IUserDirectorySync]                      │
└─────────────────────────────────────────────────────────────────┘
```

### 分层契约

| 层 | 职责 | 和协议关联 | 和身份源关联 |
|----|------|:---------:|:-----------:|
| **Protocol Facades** | 翻译外部协议 ↔ IdP Core 调用 | **强** | 零 |
| **IdP Core** | 认证策略、会话、签票、审计 | 零 | 零 |
| **Identity Sources** | 存 / 验证凭证,拉用户元数据 | 零 | **强** |

**核心原则**:IdP Core **不出现**任何 XML 解析、LDAP 调用、HTTP 路由。Facade **不出现**任何密码哈希、锁定策略。Source **不出现**任何会话或签票逻辑。

---

## 4. 各层接口契约

### 4.1 Identity Sources 层

用户数据存储与密码验证的抽象。**所有源(Local / AD / 未来的 SAML)必须实现对应的契约**。

```csharp
// Sources/Contracts/IUserStore.cs — 本地账户库 CRUD
public interface IUserStore {
    Task<User?> FindAsync(string username, string domain, CancellationToken ct);
    Task<User?> FindByIdAsync(Guid userId, CancellationToken ct);
    Task<User?> FindByCardHashAsync(string cardUidHash, CancellationToken ct);

    Task UpsertFromDirectoryAsync(DirectoryUserDto dto, CancellationToken ct);
    Task DisableNotInAsync(IReadOnlySet<Guid> seenGuids, CancellationToken ct);

    Task UpdatePasswordHashAsync(Guid userId, string hash, CancellationToken ct);
    Task UpdateFailCountAsync(Guid userId, PwdOrPin which, int count, DateTime? lockUntil, CancellationToken ct);
}

// Sources/Contracts/IRemotePasswordVerifier.cs — "问外部系统密码对不对"
public interface IRemotePasswordVerifier {
    /// <summary>
    /// 返回密码对/错,或 Unreachable(外部系统不可达,caller 决定回退策略)。
    /// </summary>
    Task<RemoteVerifyResult> VerifyAsync(
        UserIdentity identity,
        string password,
        CancellationToken ct);
}

public enum RemoteVerifyOutcome { Valid, Invalid, Unreachable }
public record RemoteVerifyResult(RemoteVerifyOutcome Outcome, string? Diagnostic = null);

// Sources/Contracts/IUserDirectorySync.cs — "定期拉用户列表"
public interface IUserDirectorySync {
    Task<DirectorySyncResult> RunOnceAsync(CancellationToken ct);
}

public record DirectorySyncResult(
    int Added, int Updated, int Disabled, int Skipped,
    TimeSpan Duration, bool SuccessfullyCompleted);
```

**当前实现**:
- `Sources/Local/UserStore.cs` : `IUserStore`
- `Sources/ActiveDirectory/AdBindAuthenticator.cs` : `IRemotePasswordVerifier`
- `Sources/ActiveDirectory/AdSyncService.cs` : `IUserDirectorySync`(同时是 `BackgroundService`)

**未来实现(按需添加)**:
- `Sources/Saml/SamlEcpVerifier.cs` : `IRemotePasswordVerifier`
- `Sources/Scim/ScimSyncService.cs` : `IUserDirectorySync`

### 4.2 IdP Core 层

协议无关的认证逻辑。**不得依赖任何 Facade 或 Source 的具体类型**,只依赖 §4.1 的契约。

```csharp
// IdpCore/Authentication/IAuthenticator.cs — 每种 modality 一个
public interface IAuthenticator<TInput> {
    Task<AuthResult> AuthenticateAsync(
        TInput input,
        AuthRequestContext context,
        CancellationToken ct);
}

// 输入类型分别对应三种 modality:
public record PwdInput(string Username, string Domain, string Password);
public record UidInput(string UniqueId);
public record PinInput(string ServerState, string Pin);

// IdpCore/Authentication/AuthResult.cs — 统一输出
public record AuthResult(
    AuthOutcome Outcome,
    AuthContext? Context,          // 成功或多步挑战时有
    string? ServerState,           // 多步认证时由 Core 签发
    ModalityChallenge? NextChallenge,
    ImprivataReturnCode Rtc);      // 给 Facade 直接塞进响应用

public enum AuthOutcome {
    Success,
    InvalidCredentials,
    LockedOut,
    SessionExpired,
    MultiStepRequired,
    SourceUnreachable
}

// IdpCore/Tokens/ITokenIssuer.cs — 签/验/吊销凭证
public interface ITokenIssuer<TToken> {
    TToken Issue(AuthContext context);
    Task<AuthContext?> ValidateAsync(TToken token, CancellationToken ct);
    Task RevokeAsync(string jti, CancellationToken ct);
}

// IdpCore/Sessions/IAuthSessionStore.cs — 多步认证状态
public interface IAuthSessionStore {
    Task<string> CreateAsync(Guid userId, string pendingModality, CancellationToken ct);
    Task<AuthSession?> TakeAsync(string serverState, CancellationToken ct);  // 一次性
    Task CleanExpiredAsync(CancellationToken ct);
}

// IdpCore/Authentication/ILockoutPolicy.cs — 失败计数 + 锁定
public interface ILockoutPolicy {
    Task<LockoutStatus> CheckAsync(Guid userId, PwdOrPin which, CancellationToken ct);
    Task<LockoutStatus> OnSuccessAsync(Guid userId, PwdOrPin which, CancellationToken ct);
    Task<LockoutStatus> OnFailureAsync(Guid userId, PwdOrPin which, CancellationToken ct);
}

// IdpCore/Audit/IAuditSink.cs — 审计日志
public interface IAuditSink {
    Task LogAsync(AuditEvent evt, CancellationToken ct);
}
```

**当前实现**:
- `IdpCore/Authentication/PwdAuthenticator.cs` : `IAuthenticator<PwdInput>`
  依赖 `IUserStore`, `IPasswordHasher`, `IRemotePasswordVerifier`, `ILockoutPolicy`, `IAuditSink`
- `IdpCore/Authentication/UidAuthenticator.cs` : `IAuthenticator<UidInput>`
- `IdpCore/Authentication/PinAuthenticator.cs` : `IAuthenticator<PinInput>`
- `IdpCore/Tokens/JwtTokenIssuer.cs` : `ITokenIssuer<OStickTicket>`

**未来实现**:
- `IdpCore/Tokens/SamlAssertionIssuer.cs` : `ITokenIssuer<SamlAssertion>` — 给 SAML Facade 使用

### 4.3 Protocol Facades 层

外部协议到 IdP Core 的翻译器。**每个 Facade 是一个自成一套的子系统**,互相不依赖。

```csharp
// Facades/Contracts/IProtocolFacade.cs — 所有 facade 的注册入口
public interface IProtocolFacade {
    string Name { get; }               // "Imprivata", "Admin", "Saml", ...
    void RegisterServices(IServiceCollection services, IConfiguration config);
    void MapEndpoints(IEndpointRouteBuilder routes);
}

// 具体 Facade 不暴露接口,只按照 IProtocolFacade 自注册
```

**当前实现**:
- `Facades/Imprivata/ImprivataFacade.cs` : `IProtocolFacade`
  注册:`AuthUserEndpoint`, `ServersEndpoint`, `DomainsEndpoint`, `ModalitiesEndpoint`, `OStickAuthenticationHandler`
- `Facades/Admin/AdminFacade.cs` : `IProtocolFacade`
  注册:`UsersController`, `CardsController`, `SyncController`

**未来实现**:
- `Facades/Saml/SamlFacade.cs` —— 暴露 SAML SP/IdP 端点
- `Facades/Oidc/OidcFacade.cs` —— 暴露 OIDC `/.well-known` + `/authorize` + `/token`

---

## 5. 目录结构

### 5.1 目标结构

```
src/ImprivataProxy/
│
├── Facades/                              ← 协议适配层
│   ├── Contracts/
│   │   └── IProtocolFacade.cs
│   ├── Imprivata/                        ← [Endpoints/ 迁入]
│   │   ├── ImprivataFacade.cs
│   │   ├── AuthUserEndpoint.cs
│   │   ├── ServersEndpoint.cs
│   │   ├── DomainsEndpoint.cs
│   │   ├── ModalitiesEndpoint.cs
│   │   ├── NotImplementedEndpoint.cs
│   │   ├── ImprivataXml.cs
│   │   ├── ImprivataReturnCodes.cs
│   │   └── OStickAuthenticationHandler.cs
│   └── Admin/                            ← [Admin/ 迁入]
│       ├── AdminFacade.cs
│       ├── UsersController.cs
│       ├── CardsController.cs
│       └── SyncController.cs
│
├── IdpCore/                              ← 协议无关认证核心
│   ├── Authentication/
│   │   ├── Contracts/
│   │   │   ├── IAuthenticator.cs
│   │   │   ├── ILockoutPolicy.cs
│   │   │   └── IPasswordHasher.cs
│   │   ├── AuthEngine.cs
│   │   ├── PwdAuthenticator.cs           ← 依赖 IRemotePasswordVerifier
│   │   ├── UidAuthenticator.cs
│   │   ├── PinAuthenticator.cs
│   │   ├── LockoutPolicy.cs
│   │   └── PasswordHasher.cs             ← Argon2id
│   ├── Sessions/
│   │   ├── Contracts/
│   │   │   └── IAuthSessionStore.cs
│   │   └── AuthSessionStore.cs
│   ├── Tokens/
│   │   ├── Contracts/
│   │   │   └── ITokenIssuer.cs
│   │   ├── JwtTokenIssuer.cs             ← OStick ticket 是一种 JWT
│   │   └── TicketBlacklistService.cs
│   └── Audit/
│       ├── Contracts/
│       │   └── IAuditSink.cs
│       └── AuditLog.cs
│
├── Sources/                              ← 身份源
│   ├── Contracts/
│   │   ├── IUserStore.cs
│   │   ├── IRemotePasswordVerifier.cs
│   │   └── IUserDirectorySync.cs
│   ├── Local/                            ← [Accounts/ 迁入]
│   │   ├── AppDbContext.cs
│   │   ├── UserStore.cs                  : IUserStore
│   │   ├── Entities/
│   │   │   ├── User.cs
│   │   │   ├── UserCard.cs
│   │   │   ├── AuthSessionEntity.cs
│   │   │   ├── TicketBlacklistEntity.cs
│   │   │   └── AuditLogEntity.cs
│   │   └── Migrations/
│   └── ActiveDirectory/                  ← [ActiveDirectory/ 迁入]
│       ├── LdapClient.cs
│       ├── LdapConnectionFactory.cs
│       ├── AdSyncService.cs              : IUserDirectorySync, BackgroundService
│       ├── AdBindAuthenticator.cs        : IRemotePasswordVerifier
│       └── AdAttributeMapper.cs
│
├── Configuration/                        ← 保留不变
│   ├── ProxyConfig.cs
│   ├── AdConfig.cs
│   ├── TicketConfig.cs
│   ├── AuthPolicyConfig.cs
│   └── DatabaseConfig.cs
│
├── Shared/                               ← 公共工具
│   ├── Logging/
│   │   └── LogSanitizer.cs
│   └── Xml/
│       └── XmlHelper.cs
│
├── Middleware/                           ← 跨层中间件
│   ├── RequestLoggingMiddleware.cs
│   └── ResponseLoggingMiddleware.cs
│
└── Program.cs
```

### 5.2 Namespace 约定

| 目录 | Namespace |
|------|-----------|
| `Facades/Imprivata/` | `ImprivataProxy.Facades.Imprivata` |
| `Facades/Admin/` | `ImprivataProxy.Facades.Admin` |
| `IdpCore/Authentication/` | `ImprivataProxy.IdpCore.Authentication` |
| `IdpCore/Tokens/` | `ImprivataProxy.IdpCore.Tokens` |
| `Sources/Local/` | `ImprivataProxy.Sources.Local` |
| `Sources/ActiveDirectory/` | `ImprivataProxy.Sources.ActiveDirectory` |

Namespace 即目录结构,方便通过 `using` 语句一眼看出依赖关系是否"跨层"。

### 5.3 跨层依赖规则

```
Facades  ──allowed──▶  IdpCore  ──allowed──▶  Sources/Contracts
   │                       │
   └─not allowed──▶ Sources │
Facades  ──not allowed──▶ Sources 具体实现
IdpCore  ──not allowed──▶ Facades
Sources  ──not allowed──▶ IdpCore / Facades
```

**执行手段**:
- 约定 + Code Review
- 可选:用 `dotnet-nsdepcop` 或 `ArchUnitNET` 在 CI 里做分层规则校验

---

## 6. 迁移计划

本 ADR **不要求立刻大改**,按以下节奏落地即可:

### Phase α:接口契约先行(1 天,无破坏)

1. 新增 `Sources/Contracts/`, `IdpCore/*/Contracts/`, `Facades/Contracts/` 目录
2. 把 plan.md §7.1 / 7.2 的接口正式抽出(`IRemotePasswordVerifier`, `IUserDirectorySync` 等)
3. 现有代码不动,只做接口引出 + 现有类实现接口
4. DI 容器里开始按接口注入

**交付**:所有公开接口就位,现有实现不动

### Phase β:目录重组(半天,机械迁移)

1. `Endpoints/` → `Facades/Imprivata/`
2. `Admin/` → `Facades/Admin/`
3. `Authentication/` → `IdpCore/Authentication/`
4. `Accounts/` → `Sources/Local/`
5. `ActiveDirectory/` → `Sources/ActiveDirectory/`
6. `Tickets/` → `IdpCore/Tokens/`

全部是 `git mv` + 改 namespace,逻辑零改动。测试全绿作为迁移完成标志。

**交付**:目录结构落位,216 tests 仍 100% 通过

### Phase γ:强化分层约束(可选,半天)

1. 引入 ArchUnitNET 写单元测试,校验跨层引用
2. CI 里加一条 job 专门跑架构测试
3. 文档:给 `README.md` 补"架构分层"一节

**交付**:架构退化有自动化兜底

### 非强制

- 不要求在 Phase α/β 之前完成所有测试重组
- 不要求 Phase γ,只在项目规模进一步扩大时再做

---

## 7. 解锁的能力

### 7.1 多协议 IdP 并存

```
Web 浏览器用户   ──SAML/OIDC ──▶ Facades/Saml     ┐
Imprivata 客户端 ──XML ────────▶ Facades/Imprivata ├──▶ IdpCore ──▶ AD + Local DB
REST API 客户端  ──Bearer ─────▶ Facades/Oidc     ┘
移动端 App      ──OIDC ────────▶ 同上
```

同一批用户 / 卡号 / PIN,不同客户端走不同 Facade,共享 IdpCore。医疗场景的真实收益:

- 护士工位 Imprivata 刷卡
- 医生在家浏览器 SAML
- 移动 App OIDC
- 外部合作医院跨域 SSO

### 7.2 身份源换代零代价

客户从本地 AD 迁到 Entra ID:
- 新增 `Sources/EntraId/`(实现 `IRemotePasswordVerifier` + `IUserDirectorySync`)
- 配置换 Source
- IdpCore / Facades 零改动

### 7.3 协议升级零代价

Imprivata 出 v29 协议,改 `Facades/Imprivata/` 一个目录。IdpCore 不动。

### 7.4 自己当身份源

自己实现 `Facades/Scim/` 向外暴露 SCIM 2.0,**本项目就能给别的系统当身份源**。多医院架构里有用:

```
医院 A 的 ImprivataProxy ──SCIM──▶ 医院 B 的 ImprivataProxy
                                    (或任何支持 SCIM 的系统)
```

### 7.5 测试矩阵从乘法变加法

| 反模式(耦合架构) | 本 ADR |
|---|---|
| 协议 × 源 × modality = 爆炸 | 每层独立测 + 少量 Facade 集成测 |
| N Facade × M Source = N×M 测试 | N + M 测试 |

---

## 8. 反模式(显式禁止)

为防止架构退化,以下代码模式在 Code Review 中应当被拒绝:

### 8.1 Facade 直接访问 Source

```csharp
// ❌ BAD: Facades/Imprivata/AuthUserEndpoint.cs
var user = await _dbContext.Users.FirstOrDefaultAsync(...);  // 跨层!
var card = await _ldap.SearchAsync(...);                     // 跨层!

// ✅ GOOD: 通过 IdpCore/Authentication
var result = await _authEngine.AuthenticateAsync(input, ctx, ct);
```

### 8.2 IdpCore 依赖具体协议

```csharp
// ❌ BAD: IdpCore/Authentication/PwdAuthenticator.cs
var xml = XDocument.Parse(request);  // XML 是 Imprivata 的事
return new XElement("Response", ...);

// ✅ GOOD: PwdAuthenticator 只接受 PwdInput record,返回 AuthResult
```

### 8.3 Source 依赖 IdpCore 或 Facade

```csharp
// ❌ BAD: Sources/ActiveDirectory/AdSyncService.cs
_ticketIssuer.Issue(...);       // Source 不签票!
_xmlBuilder.BuildResponse();    // Source 不管协议!

// ✅ GOOD: Sources 只知道自己的存储 / 外部系统
```

### 8.4 配置跨层泄露

```csharp
// ❌ BAD: IdpCore/Authentication/PwdAuthenticator.cs
var adHost = _config["Ad:LdapUrl"];  // IdpCore 不该知道 LDAP!

// ✅ GOOD: IRemotePasswordVerifier 注入,LDAP URL 是 Source 的实现细节
```

### 8.5 Facade 之间互相依赖

```csharp
// ❌ BAD: Facades/Admin/SyncController.cs
_imprivataAuthEngine.SignTicket(...);  // Admin 直接调 Imprivata Facade!

// ✅ GOOD: 都通过 IdpCore 间接沟通
```

---

## 9. 风险与权衡

| 风险 | 评估 | 缓解 |
|------|------|------|
| 接口过度抽象导致 Core 臃肿 | 低 | 每个接口都有当下的具体需求驱动,不做投机性接口 |
| 目录重组引入 regression | 低 | 纯 `git mv`,测试全绿作为完成标志 |
| 团队不习惯分层,Code Review 漏放 | 中 | Phase γ 加 ArchUnit;新人 onboarding 文档明确 |
| Facade 重复代码(每个都要写 DI / routing) | 低 | `IProtocolFacade` + 通用基类可吸收 |
| 将来不做 SAML,抽象白做 | 低 | 即便永不加 SAML,分层对可维护性、可测试性仍有直接收益 |

**决策者权衡**:短期代价(目录重组 + 几个接口)**远小于**长期收益(多协议扩展 + 测试矩阵线性化 + 架构清晰度)。

---

## 10. 回顾触发条件

以下情况发生时,应当重新评估本 ADR:

- 项目收敛到只做 Imprivata 协议 + 本地 AD,永不扩展,分层可适度放松
- 多协议需求不出现,但 Facade 数量膨胀(多种 REST API),应考虑抽得更细
- 出现第四层需求(例如策略层 PDP/PEP、分析层),重新规划
- 团队规模增长到 10+ 人,需要更强的编译期分层约束(考虑多项目拆分)

---

## 11. 与已有 ADR 的关系

| ADR | 关系 |
|-----|------|
| [ADR-0001:AD LDAP 同步 vs SAML](./adr-0001-adsync-vs-saml.md) §7 | 提供扩展口 `IRemotePasswordVerifier`, `IUserDirectorySync` |
| **ADR-0002(本文)** | 把这些扩展口归位到 `Sources/Contracts/`,并扩展成完整三层契约 |
| 未来 ADR-0003 | 可能讨论"是否引入 SAML/OIDC Facade",本 ADR 提供的架构是前提 |

---

## 12. 图示(目标状态蓝图)

> ⚠️ 本节图是**按本 ADR 落地后的目标样子**画的,**不代表当前代码**。现有代码(`Endpoints/ Authentication/ Accounts/ ActiveDirectory/ Tickets/` 平铺结构)对应的实际流程见 [`diagrams/pipeline.puml`](./diagrams/pipeline.puml)。两份图并存,用于评审与重构比对。

### 12.1 认证流程总览(跨层 activity)

[`diagrams/adr-0002-authuser-flowchart.puml`](./diagrams/adr-0002-authuser-flowchart.puml)

一张 Activity diagram(PlantUML swimlane),分三泳道 **Facade | IdpCore | Sources**,覆盖 `POST /AuthUser` 的全部分派路径:PWD、UID(单步/挑战 PIN)、PIN(多步第 2 步)。**跨层切换点即泳道切换点**,可直观检查 §8 反模式是否违反。

### 12.2 三种场景 Sequence 图

[`diagrams/adr-0002-authuser-sequence.puml`](./diagrams/adr-0002-authuser-sequence.puml) 内含 3 个独立渲染块:

| `@startuml` 名 | 场景 | 关键契约 |
|----------------|------|---------|
| `adr0002-seq-pwd` | PWD 本地 hash + AD bind fallback | `IAuthenticator<PwdInput>`, `IRemotePasswordVerifier`, `IPasswordHasher` |
| `adr0002-seq-uid` | UID 单步 / 挑战 PIN 两分支 | `IAuthenticator<UidInput>`, `IAuthSessionStore` |
| `adr0002-seq-pin` | UID+PIN 多步第 2 步 | `IAuthenticator<PinInput>`, `IAuthSessionStore`, `ILockoutPolicy` |

每张图的 participant 全部用**接口名**(`IAuthenticator<T>`, `IUserStore`, `ITokenIssuer<OStickTicket>` 等),严格对齐 §4 契约,体现"依赖抽象而非实现"。

### 12.3 读图要点

1. **Facade 的职责**:解析 XML(ImprivataXmlParser)→ 构造 typed Input(PwdInput / UidInput / PinInput)→ 调 `IAuthenticator` → 拿到 `AuthResult` → 根据 `Outcome` 决定调 `ITokenIssuer` + `ImprivataXmlBuilder.Success/Pending/Failure`
2. **IdpCore 的职责**:消费 Input,编排 `IUserStore` / `ILockoutPolicy` / `IPasswordHasher` / `IAuthSessionStore` / `IAuditSink` / `IRemotePasswordVerifier`,最终返回 `AuthResult`。**不**解析 XML,**不**签 token
3. **Sources 的职责**:纯数据访问,`IUserStore` 查/写本地 DB,`IRemotePasswordVerifier` 对外验密码
4. **`AuthResult` 的角色**:IdpCore 到 Facade 的唯一返回契约。`Outcome` 决定响应形态,`Context` 装成功者身份,`ServerState`/`NextChallenge` 装多步状态,`Rtc` 装 Imprivata 返回码
5. **Token 签发在 Facade 而非 Core**:因为 token 格式是协议绑定的(Imprivata 要 OStick JWT,未来 SAML Facade 要 SAML Assertion)。Core 只说"谁通过了",Facade 决定"发什么票据"

---

## 13. 参考

- [Identity Provider (Wikipedia)](https://en.wikipedia.org/wiki/Identity_provider)
- [Hexagonal Architecture (Alistair Cockburn)](https://alistair.cockburn.us/hexagonal-architecture/)
- [Ports and Adapters pattern](https://en.wikipedia.org/wiki/Hexagonal_architecture_(software))
- [SAML 2.0 Core Spec](http://docs.oasis-open.org/security/saml/v2.0/saml-core-2.0-os.pdf)
- [OpenID Connect Core 1.0](https://openid.net/specs/openid-connect-core-1_0.html)
- [ArchUnitNET — 架构规则测试](https://github.com/TNG/ArchUnitNET)

---

## 附录 A:DI 注册示意(Program.cs)

采用本 ADR 后,`Program.cs` 的 DI 注册呈现清晰三层:

```csharp
var builder = WebApplication.CreateBuilder(args);

// ---- Sources ----
builder.Services.AddDbContext<AppDbContext>(...);
builder.Services.AddScoped<IUserStore, UserStore>();
builder.Services.AddSingleton<ILdapConnectionFactory, LdapConnectionFactory>();
builder.Services.AddScoped<IRemotePasswordVerifier, AdBindAuthenticator>();
builder.Services.AddHostedService<AdSyncService>();

// ---- IdpCore ----
builder.Services.AddSingleton<IPasswordHasher, Argon2Hasher>();
builder.Services.AddScoped<ILockoutPolicy, LockoutPolicy>();
builder.Services.AddScoped<IAuthSessionStore, AuthSessionStore>();
builder.Services.AddScoped<IAuthenticator<PwdInput>, PwdAuthenticator>();
builder.Services.AddScoped<IAuthenticator<UidInput>, UidAuthenticator>();
builder.Services.AddScoped<IAuthenticator<PinInput>, PinAuthenticator>();
builder.Services.AddSingleton<ITokenIssuer<OStickTicket>, JwtTokenIssuer>();
builder.Services.AddScoped<IAuditSink, AuditLog>();

// ---- Facades ----
var facades = new IProtocolFacade[] {
    new ImprivataFacade(),
    new AdminFacade(),
    // new SamlFacade(),   ← 将来取消注释即接入
};
foreach (var f in facades) f.RegisterServices(builder.Services, builder.Configuration);

var app = builder.Build();
foreach (var f in facades) f.MapEndpoints(app);
app.Run();
```

一眼看出:**三层清晰、Facade 可插拔、跨层不越界**。这就是本 ADR 的落地形态。

---

## 附录 B:实施状态(Phase α + β,保守版,2026-05-04)

本附录记录本 ADR 的 ADR-0002 §6 Phase α(保守版)+ Phase β 已在 2026-05-04 执行的成果与遗留。
**完整实施过程、原始 plan、命令序列、下一步路线图**见独立文件:
[**adr-0002-phase-ab-implementation.md**](./adr-0002-phase-ab-implementation.md)。

[项目根 CHANGELOG.md](../CHANGELOG.md) 也记录了本次变更的摘要。

### 状态卡片(2026-05-04 更新)

| 维度 | 状态 | 备注 |
|------|:----:|------|
| 三层目录结构(Facades / IdpCore / Sources / Shared) | ✅ | §5 落地,各层 `Contracts/` 子目录齐全 |
| Namespace 改名(67 源 + 31 测试) | ✅ | 无旧 namespace 残留 |
| `IRemotePasswordVerifier` 契约 + 实现 | ✅ | `LdapClient` 实现,DI 就位 |
| `IUserDirectorySync` 契约 + 实现 | ✅ | `AdSyncRunner` 实现,DI 就位 |
| `IUserStore` 扩展(Admin + Discovery 所需 10 方法) | ✅ | |
| Admin 控制器改走 `IUserStore`(§8.1 修) | ✅ | `UsersController` / `CardsController` / `DomainsEndpoint` |
| `IAuditSink` 重命名 `IAuditLogger` | ✅ | 含实现类 `EfAuditLogger` → `AuditLogSink` |
| `IAuditStore` + `IClientContextProvider` | ✅ | EfAuditLogger 的 DbContext/HTTP 依赖已抽离 |
| `IAuthSessionRepo` + `ITicketBlacklistRepo` | ✅ | §8.2 完整修复(AuthSessionStore / TicketBlacklistService 不再用 AppDbContext) |
| `ILockoutPolicy` 抽取 + `ILockoutRepo` | ✅ | 4 个 Record*Async 从 UserStore 搬到 policy + repo |
| `IProtocolFacade` 自注册 | ✅ | `ImprivataFacade` + `AdminFacade` 各自注册服务和路由 |
| `UserIdentity` + `IRemotePasswordVerifier` 中立化 | ✅ | 接口参数 `string DN` → `UserIdentity`(含 DN/UPN/GUID);PwdAuthenticator 从 `ILdapClient` 切到 `IRemotePasswordVerifier`;IdpCore 不再依赖 AD 特定接口 |
| 泛型 `IAuthenticator<TInput>` | ⏸️ Deferred | 见下方 "Deferred 项说明" |
| 泛型 `ITokenIssuer<TToken>` | ⏸️ Deferred | 同上 |
| §8.1 反模式 | ✅ | 零违规 + ArchUnit CI 保护 |
| §8.2 反模式 | ✅ | 零违规 + ArchUnit CI 保护 |
| §8.3 反模式 | ✅ | 零违规(PwdOrPin 搬到 Shared/Contracts)+ ArchUnit CI 保护 |
| §8.4 反模式 | ✅ | 零违规 + ArchUnit CI 保护 |
| IdpCore 不依赖 AD 特定 `ILdapClient` | ✅ | ArchUnit 规则 `IdpCore_Should_Not_Depend_On_ILdapClient` 持续保护 |
| Phase γ ArchUnitNET | ✅ | **11** 条架构规则,227/227 tests 全绿 |
| 编译 & 测试 | ✅ | 227/227,0 warning / 0 error |

### Deferred 项说明

两条泛型契约(§4.2)**有意延期**,**不视为欠债**:

- **`IAuthenticator<TInput>` + `PwdInput`/`UidInput`/`PinInput` records** —— 本项目 [plan.md §1.3](../plan.md) 明确锁死 3 种 modality(PWD / UID / UID+PIN),其他(FP / PKI / KRB / QnA / OTP)"不在本项目范围"。没有第 4 种 modality 要来,统一签名带来的只有可读性损失:
  - 命名抽象化(`IPwdAuthenticator` 一眼懂 → `IAuthenticator<PwdInput>` 要看 T 是啥)
  - 调用点代码量 ≈ +200%(构造 Input record + 构造 AuthRequestContext + 调用 ≈ 3 行,原 1 行)
- **`ITokenIssuer<TToken>` + 多 token 类型** —— 只有 OStick JWT 一种 token。[ADR-0001](./adr-0001-adsync-vs-saml.md) 显式放弃 SAML 路线,所以不会出现 SamlAssertion 这类并行 token 格式。

**回顾触发条件**:以下任一发生时,回头做这两项:
- 出现第 4 种 modality(FP / PKI / OTP 等进入本项目范围)
- 引入 SAML / OIDC Facade 且需要非 OStick 的 token 格式
- §4 强契约对齐成为合规 / 审计要求

非此情况下,**保持现状是经过权衡的正确选择**。

### §4 完整契约进度

**11 实现 / 2 有意延期 = 13 项(阶段达标)**。已实现的 11 个:
`IUserStore`, `IRemotePasswordVerifier`, `IUserDirectorySync`, `IAuthSessionStore` + `IAuthSessionRepo`, `ITicketBlacklist` + `ITicketBlacklistRepo`, `IAuditSink` + `IAuditStore`, `IClientContextProvider`, `IPasswordHasher`, `ILockoutPolicy` + `ILockoutRepo`, `IProtocolFacade`。

### 历史推进顺序(已完成)

按实际推进顺序记录,详见 [实施记录文档](./adr-0002-phase-ab-implementation.md):

1. ✅ Phase α + β 保守版(`IRemotePasswordVerifier` + `IUserDirectorySync` + 三层目录)
2. ✅ §5 Contracts/ 子目录对齐
3. ✅ §8.3 修复(`IAuditLogger` 搬到 `Shared/Contracts/`)
4. ✅ §8.2 初修(`IAuditStore` + `IClientContextProvider`)
5. ✅ §8.2 收尾(`IAuthSessionRepo` + `ITicketBlacklistRepo`)
6. ✅ `IAuditLogger` → `IAuditSink` 重命名
7. ✅ `IProtocolFacade` 自注册
8. ✅ `ILockoutPolicy` 抽出
9. ✅ Phase γ:ArchUnitNET 架构测试
