# ADR-0002 Phase α + β + 后续演进实施记录(归档)

- **起始**: 2026-05-04
- **最后更新**: 2026-05-08
- **当前状态**: ✅ ADR-0002 §3 / §5 / §8 全部达标;§4 契约 **11 实现 / 2 有意延期 = 13 项**(阶段达标);IdpCore 去 AD 化完成;Gateway 集成联调通过;双模式 LDAP 认证(Sync + OnDemand)落地
- **测试**: **224 / 224 passed**(213 单元+集成 + 11 ArchUnit 架构规则)
- **相关文档**: [adr-0002-idp-architecture.md](./adr-0002-idp-architecture.md) / [adr-0001-adsync-vs-saml.md](./adr-0001-adsync-vs-saml.md)
- **GitHub**: [github.com/shamexln/ldap](https://github.com/shamexln/ldap) `main` 分支

---

## 成就一览(16 个 commit 从 scaffold 到双模式 LDAP 认证)

### 初始建仓(Phase α + β 保守版)—— 4 commits

| Commit | 内容 | 交付 |
|--------|------|------|
| `b8e4309` | chore: project scaffolding | .gitignore + 项目文件 + Docker + README |
| `df1f101` | feat(ImprivataProxy): local IdP with 3-layer architecture per ADR-0002 | 67 src 文件,三层目录 + `IRemotePasswordVerifier` + `IUserDirectorySync` + Admin 改走 `IUserStore` |
| `c64d226` | test(ImprivataProxy): 216 unit + integration tests | 31 test 文件,216 tests 全绿 |
| `d752548` | docs(ImprivataProxy): design docs + ADRs + CHANGELOG + diagrams | ADR-0001/0002 + 5 puml + CHANGELOG + session_conversation |

**初始状态**:Phase α + β(保守版)完成,§5 各层 Contracts/ 仅 `Sources/`,§8.2/§8.3 有遗留。

### 后续推进(§5 + §8 收尾 + §4 契约深化 + IdpCore 去 AD 化 + Gateway 联调 + 双模式 LDAP)—— 13 commits

| # | Commit | 主题 | ADR 章节 |
|:-:|--------|------|---------|
| 1 | `9d4a730` | refactor: move interfaces to Contracts/ subdirs per ADR-0002 §5 | §5 各层 Contracts/ 对齐 |
| 2 | `9bb87ec` | refactor: fix §8.3 by relocating IAuditLogger to Shared/Contracts | §8.3 |
| 3 | `fd1e155` | refactor: fix §8.2 by extracting IAuditStore + IClientContextProvider | §8.2 初步(audit) |
| 4 | `c48f325` | refactor: complete §8.2 — extract AuthSession + TicketBlacklist repos | §8.2 收尾(session + blacklist) |
| 5 | `ccb1b58` | refactor: rename IAuditLogger → IAuditSink (ADR-0002 §4.2) | §4.2 重命名 |
| 6 | `d2a0e08` | refactor: introduce IProtocolFacade self-registration (ADR-0002 §4.3) | §4.3 自注册 |
| 7 | `1f81ea0` | refactor: extract ILockoutPolicy (ADR-0002 §4.2) | §4.2 policy + ILockoutRepo,`PwdOrPin` 搬 Shared |
| 8 | `11a6ff5` | test: add ArchUnitNET layering tests (ADR-0002 Phase γ) | **Phase γ** 10 条架构规则 |
| 9 | `6b1adb5` | docs: defer §4 generic interfaces — explicit design decision, not tech debt | §4.2 泛型 IAuthenticator/ITokenIssuer 显式 Deferred |
| 10 | `4fcfe0a` | docs: annotate adr-0002-phase-ab-implementation.md with commit hashes | 实施记录加 38 处 commit hash 回链 |
| 11 | `b7ae53c` | refactor: de-AD-ify IRemotePasswordVerifier — introduce UserIdentity, wire PwdAuthenticator through the verifier | §4.1 去 AD 化:UserIdentity 中立模型 + PwdAuthenticator 不再依赖 ILdapClient + ArchUnit 新规则 |
| 12 | `19febd2` | feat: Gateway integration — match real Imprivata ProveID response formats | Gateway 联调:Domains 格式 + 空 domain + reverse mapping + Credential Failure disp="2" |
| 13 | *(wip)* | feat: dual-mode LDAP authentication (Sync + OnDemand) | 双模式 LDAP:OnDemand 自注册 + 条件 DI + filter escaping |

### §4 完整契约进度

**11 实现 / 2 有意延期 = 13 项(阶段达标)**。

已实现的 11 个(按落地 commit 追溯):

| 契约 | 来源 commit |
|------|:-----------:|
| `IUserStore`(+扩展 10 方法) | `df1f101` |
| `IRemotePasswordVerifier` | `df1f101` |
| `IUserDirectorySync` | `df1f101` |
| `IPasswordHasher` | `df1f101` |
| `IAuthSessionStore` | `df1f101` |
| `ITicketBlacklist` | `df1f101` |
| `IAuditSink`(+ `IAuditStore`)| `fd1e155` + `ccb1b58` |
| `IClientContextProvider` | `fd1e155` |
| `IProtocolFacade` | `d2a0e08` |
| `ILockoutPolicy`(+ `ILockoutRepo`)| `1f81ea0` |
| `IAuthSessionRepo` + `ITicketBlacklistRepo` | `c48f325` |

有意延期的 2 个:泛型 `IAuthenticator<TInput>` / `ITokenIssuer<TToken>` ——
详见文末 "有意延期 ⏸️" 小节和 [ADR-0002 §附录 B](./adr-0002-idp-architecture.md)。

### §8 反模式收敛(ArchUnit 持续保护)

| 维度 | 来源 commit | 结果 |
|------|:-----------:|:----:|
| §8.1 Facade → Sources 具体类 | `df1f101`(Admin 改走 IUserStore)| ✅ 零违规 |
| §8.2 IdpCore → HTTP/DB | `fd1e155` + `c48f325` | ✅ 零违规 |
| §8.3 Sources → IdpCore | `9bb87ec` + `1f81ea0`(PwdOrPin 搬家) | ✅ 零违规 |
| §8.4 Config 泄露 | (初始即无)| ✅ 零违规 |
| **CI 保护** | `11a6ff5` | ✅ 10 条 ArchUnit 规则,226/226 tests 全绿 |

---

## 以下为执行前的原始 plan(历史存档)

> 以下内容是 Claude 进入 plan 模式时产出的执行方案原文,用于对照实施结果、追溯决策依据。

### Context

用户指示按 ADR-0002 完整方案修改代码:

- **Phase β**:目录重组到 `Facades / IdpCore / Sources`
- **Phase α**:添加 ADR-0002 §4 定义的新抽象接口,现有类实现之
- **Admin 清理**:`UsersController` / `CardsController` 不再直接用 `AppDbContext`,改走 `IUserStore`(扩展 ~10 个方法)

**硬约束**:216 tests 全程保持绿;现有逻辑零行为变动(只抽象化,不重写业务规则)。

### 总体策略

分 4 个 Stage 执行,每个 Stage 后 `dotnet build && dotnet test` 验证:

| Stage | 动作 | 风险 | 时长估 |
|-------|------|------|-------|
| 1 | Phase β 目录重组 + namespace 改名 | 低(机械) | 40 min |
| 2 | Phase α 新增 `Sources/Contracts/`,**附加**实现 | 中 | 30 min |
| 3 | 扩展 `IUserStore` + Admin 控制器改走 Store | 中-高(新方法) | 30 min |
| 4 | 反模式 grep 校验 + 清理残留 | 低 | 10 min |

---

### Stage 1:Phase β 目录重组

#### 1.1 目标结构

```
src/ImprivataProxy/
├── Program.cs
├── appsettings*.json
│
├── Facades/
│   ├── Imprivata/                           ← Endpoints/ + OStick*.cs
│   └── Admin/                               ← 原 Admin/
│
├── IdpCore/
│   ├── Authentication/                      ← 原 Authentication/(去 session)
│   ├── Sessions/                            ← AuthSessionStore
│   ├── Tokens/                              ← 原 Tickets/(去 OStick*)
│   └── Audit/                               ← 从 Accounts/ 抽 IAuditLogger+EfAuditLogger
│
├── Sources/
│   ├── Local/                               ← 原 Accounts/ 余下(含 Entities/)
│   └── ActiveDirectory/                     ← 原 ActiveDirectory/
│
├── Configuration/                           ← 不变
├── Middleware/                              ← 不变
└── Shared/
    ├── Logging/                             ← 原 Logging/
    └── Xml/                                 ← 原 Xml/
```

#### 1.2 Namespace 映射表

| 旧 | 新 |
|---|---|
| `ImprivataProxy.Endpoints` | `ImprivataProxy.Facades.Imprivata` |
| `ImprivataProxy.Admin` | `ImprivataProxy.Facades.Admin` |
| `ImprivataProxy.Authentication`(除 session) | `ImprivataProxy.IdpCore.Authentication` |
| `ImprivataProxy.Authentication.AuthSessionStore` 等 | `ImprivataProxy.IdpCore.Sessions` |
| `ImprivataProxy.Tickets`(除 OStick) | `ImprivataProxy.IdpCore.Tokens` |
| `ImprivataProxy.Tickets.OStick*` | `ImprivataProxy.Facades.Imprivata` |
| `ImprivataProxy.Accounts`(IAuditLogger, EfAuditLogger) | `ImprivataProxy.IdpCore.Audit` |
| `ImprivataProxy.Accounts`(其余) | `ImprivataProxy.Sources.Local` |
| `ImprivataProxy.Accounts.Entities` | `ImprivataProxy.Sources.Local.Entities` |
| `ImprivataProxy.ActiveDirectory` | `ImprivataProxy.Sources.ActiveDirectory` |
| `ImprivataProxy.Logging` | `ImprivataProxy.Shared.Logging` |
| `ImprivataProxy.Xml` | `ImprivataProxy.Shared.Xml` |

---

### Stage 2:Phase α 新增契约接口

ADR-0002 §6 Phase α 定义"现有代码不动,只做接口引出 + 现有类实现接口"。本 Stage 遵此最小扰动原则。

#### 2.1 新增 `Sources/Contracts/IRemotePasswordVerifier.cs`

```csharp
namespace ImprivataProxy.Sources.Contracts;

public interface IRemotePasswordVerifier
{
    Task<RemoteVerifyResult> VerifyAsync(
        string distinguishedName, string password, CancellationToken ct);
}

public enum RemoteVerifyOutcome { Valid, Invalid, Unreachable }
public record RemoteVerifyResult(RemoteVerifyOutcome Outcome, string? Diagnostic = null);
```

#### 2.2 新增 `Sources/Contracts/IUserDirectorySync.cs`

```csharp
namespace ImprivataProxy.Sources.Contracts;

public interface IUserDirectorySync
{
    Task<SyncResult> RunOnceAsync(CancellationToken ct);
}
```

#### 2.3 LdapClient 实现 IRemotePasswordVerifier

```csharp
public class LdapClient : ILdapClient, IRemotePasswordVerifier
{
    public async Task<RemoteVerifyResult> VerifyAsync(string dn, string password, CancellationToken ct)
    {
        try
        {
            var ok = await BindAsUserAsync(dn, password, ct);
            return new RemoteVerifyResult(ok ? RemoteVerifyOutcome.Valid : RemoteVerifyOutcome.Invalid);
        }
        catch (Exception ex)
        {
            return new RemoteVerifyResult(RemoteVerifyOutcome.Unreachable, ex.Message);
        }
    }
}
```

#### 2.4 AdSyncRunner 实现 IUserDirectorySync

方法签名 `RunOnceAsync(CancellationToken ct)` 已匹配,只需加接口。

#### 2.5 DI 注册

```csharp
builder.Services.AddSingleton<IRemotePasswordVerifier>(
    sp => (IRemotePasswordVerifier)sp.GetRequiredService<ILdapClient>());
builder.Services.AddScoped<IUserDirectorySync>(
    sp => sp.GetRequiredService<AdSyncRunner>());
```

#### 2.6 PwdAuthenticator 继续用 ILdapClient

**不改**。`IRemotePasswordVerifier` 作为"未来可切换点"就位,但不强制立即迁移。符合 ADR-0002 §6 Phase α "现有代码不动"。

#### 2.7 不做的 α 内容(保留给未来)

| 接口 | 为何推迟 |
|------|---------|
| `IAuditSink` 重命名 `IAuditLogger` | 重命名涉及所有调用点,风险不匹配 α "非破坏" |
| 泛型 `IAuthenticator<TInput>` | 需引入 `PwdInput/UidInput/PinInput` records 并改 Authenticator 签名 |
| 泛型 `ITokenIssuer<TToken>` | 同上,`Issue(User)` 变 `Issue(AuthContext)` |
| `ILockoutPolicy` | 锁定计数目前在 `UserStore`,抽取要改多处 |

---

### Stage 3:Admin 控制器改走 IUserStore

#### 3.1 IUserStore 扩展方法

```csharp
// UsersController 需要
Task<IReadOnlyList<User>> ListUsersAsync(string? search, bool? enabled, int take, CancellationToken ct);
Task<User?> FindByIdWithCardsAsync(string userId, CancellationToken ct);
Task<bool> SetUserEnabledAsync(string userId, bool enabled, CancellationToken ct);
Task<bool> SetPinHashAsync(string userId, string? pinHash, CancellationToken ct);
Task<bool> UnlockUserAsync(string userId, CancellationToken ct);

// CardsController 需要
Task<UserCard?> FindCardByHashAsync(string cardUidHash, CancellationToken ct);
Task CreateCardAsync(UserCard card, CancellationToken ct);
Task<UserCard?> FindCardByIdWithUserAsync(string cardId, CancellationToken ct);
Task<bool> RevokeCardAsync(string cardId, CancellationToken ct);

// DomainsEndpoint 需要(§8.1 违规修复)
Task<IReadOnlyList<string>> GetDistinctEnabledDomainsAsync(CancellationToken ct);
```

#### 3.2 控制器改写

构造器参数 `AppDbContext db` → `IUserStore users`,HTTP 行为零变化。

---

### Stage 4:反模式 grep 校验 + 清理

```bash
# §8.1 Facade 不得访问 Sources 具体类型
grep -r "AppDbContext\|LdapClient\|EfAuditLogger" src/ImprivataProxy/Facades/     # 期望:空

# §8.2 IdpCore 不得依赖协议/XML
grep -r "XDocument\|XElement\|HttpContext" src/ImprivataProxy/IdpCore/            # 期望:空

# §8.3 Sources 不得依赖 IdpCore / Facades
grep -r "ImprivataProxy\.IdpCore\|ImprivataProxy\.Facades" src/ImprivataProxy/Sources/  # 期望:空

# §8.4 配置跨层泄露
grep -r "IConfiguration\|builder.Configuration" src/ImprivataProxy/IdpCore/      # 期望:空

# 残留旧 namespace
grep -rE "ImprivataProxy\.(Endpoints|Authentication|Tickets|Accounts|ActiveDirectory|Admin|Logging|Xml)\b" src tests  # 期望:空

# 最终验证
dotnet clean && dotnet build && dotnet test     # Passed: 216
```

---

### 关键文件与位置

#### 新增接口文件(Stage 2)

| 新文件 | 位置 |
|--------|------|
| `IRemotePasswordVerifier.cs` + `RemoteVerifyResult` | `src/ImprivataProxy/Sources/Contracts/` |
| `IUserDirectorySync.cs` | `src/ImprivataProxy/Sources/Contracts/` |

#### 修改文件(Stage 2)

| 文件 | 改动 |
|------|------|
| `Sources/ActiveDirectory/LdapClient.cs` | 追加 `IRemotePasswordVerifier` 实现 |
| `Sources/ActiveDirectory/AdSyncRunner.cs` | 加 `IUserDirectorySync` 接口 |
| `Program.cs` | +2 DI 注册 |

#### 扩展文件(Stage 3)

| 文件 | 改动 |
|------|------|
| `Sources/Local/IUserStore.cs` | +10 方法签名 |
| `Sources/Local/UserStore.cs` | +10 方法实现 |
| `Facades/Admin/UsersController.cs` | AppDbContext → IUserStore |
| `Facades/Admin/CardsController.cs` | 同上 |
| `Facades/Imprivata/DomainsEndpoint.cs` | 同上 |

---

### 风险清单(执行前估计)

| 风险 | 影响 | 缓解 |
|------|------|------|
| Phase β 的 using 漏改 | 编译失败 | 每步 `dotnet build` 立即暴露 |
| Admin 控制器测试依赖具体 EF 查询形状 | 测试失败 | 保持方法行为等价 |
| `partial class Program` + WebApplicationFactory | 集成测试打不起来 | Program.cs 位置不动 |
| `IAuditLogger` 属于 Accounts 还是 IdpCore/Audit | 边界争议 | ADR-0002 §5 划归 `IdpCore/Audit/` |
| **`EfAuditLogger` 仍依赖 `AppDbContext`** | **§8.1 违规?** | **IdpCore → Sources 允许,但 IdpCore 依赖具体类违规。本次暂忍,未来 α 加 `IAuditStore` 抽象** |
| OStick*.cs 归 Facade 还是 IdpCore | 边界争议 | OStick 是 Imprivata 协议的 ticket scheme → Facade |
| Migrations/ 目录(EF) | 可能存在,未清点 | Stage 1 前检查 |

### Out of scope(明确不做)

- ❌ `IAuditSink` 重命名(未来 PR)
- ❌ 泛型 `IAuthenticator<PwdInput/UidInput/PinInput>` + `AuthRequestContext` + 新 `AuthResult` shape
- ❌ 泛型 `ITokenIssuer<TToken>`
- ❌ `ILockoutPolicy` 从 UserStore 抽出
- ❌ `IProtocolFacade` 自注册机制
- ❌ ArchUnit 架构测试(Phase γ)
- ❌ 更新 ADR-0002 状态为 Accepted(仍 Proposed)
- ❌ Git commit(用户决定何时提交)

---

## 执行过程(事后)

实际分 4 个 Stage 完成,每个 Stage 后运行 `dotnet build && dotnet test`:

| Stage | 操作数 | 构建结果 | 测试结果 |
|-------|-------:|:-------:|:-------:|
| 基线 | — | — | 216 ✅ |
| Stage 1(目录重组) | 67 `mv` + 11 `sed` + 9 Edit 手修 | ✅ | 216 ✅ |
| Stage 2(新接口) | 2 新文件 + 3 Edit + Program.cs +5 行 | ✅ | 216 ✅ |
| Stage 3(IUserStore 扩展 + Admin 改造) | `IUserStore` +10 方法 + 4 文件重写 + 3 测试 fixture 修 | ✅ | 216 ✅ |
| Stage 4(反模式 grep + Migrations 字符串) | 4 grep + 1 sed | ✅ | 216 ✅ |

**合计**:约 100 次工具调用,~1 小时。

---

## 后续推进明细(按 commit 时间顺序)

每一条对应仓库 main 分支上的一个独立 commit,便于按 hash 查 diff / 回溯。

### 1. `9d4a730` — §5 各层 `Contracts/` 子目录对齐

**做了什么**:
- 8 个 IdpCore 接口文件物理移动到 `<parent>/Contracts/` 子目录(namespace 不变)
  - `IdpCore/Authentication/{IPwdAuthenticator, IUidAuthenticator, IPinAuthenticator, IPasswordHasher}.cs` → `Contracts/`
  - `IdpCore/Sessions/IAuthSessionStore.cs` → `Contracts/`
  - `IdpCore/Tokens/{ITicketIssuer, ITicketBlacklist, ISigningKeyProvider}.cs` → `Contracts/`
- `IUserStore.cs` 从 `Sources/Local/` → `Sources/Contracts/`(namespace 改 `Sources.Local` → `Sources.Contracts`)
- 7 个调用点追加 `using ImprivataProxy.Sources.Contracts;`

**ADR 意图**:§5 目录结构落地到完整形态("每层有 Contracts/ 子目录")。

**验证**:dotnet build clean,216/216 tests 绿。

### 2. `9bb87ec` — §8.3 修复:`IAuditLogger` 搬到 `Shared/Contracts/`

**背景**:`Sources/ActiveDirectory/{AdSyncRunner, AdSyncService}.cs` 引用 `IdpCore.Audit`,违反 §8.3(Sources 不得依赖 IdpCore)。

**做了什么**:
- `mv IdpCore/Audit/IAuditLogger.cs Shared/Contracts/IAuditLogger.cs`
- namespace 从 `ImprivataProxy.IdpCore.Audit` 改为 `ImprivataProxy.Shared.Contracts`
- 全项目 sed 替换 `using ImprivataProxy.IdpCore.Audit;` → `using ImprivataProxy.Shared.Contracts;`(12 源 + 2 测试 + Program.cs)
- `EfAuditLogger` 和其他直接引用 `EfAuditLogger` 类的地方**补回** `using ImprivataProxy.IdpCore.Audit;`(实现类仍在 IdpCore)

**验证**:§8.3 grep 空;226/216 tests 绿。

### 3. `fd1e155` — §8.2 修复(初步):`IAuditStore` + `IClientContextProvider`

**背景**:`IdpCore/Audit/EfAuditLogger` 同时吃 `AppDbContext`(§8.2-具体 Source 类)+ `IHttpContextAccessor`(§8.2-HTTP),需要拆。

**做了什么**:
- **新建 4 文件**:
  - `Sources/Contracts/IAuditStore.cs`(`AppendAsync(AuditLogEntry, ct)`)
  - `Sources/Local/EfAuditStore.cs`(EF 实现)
  - `Shared/Contracts/IClientContextProvider.cs`(`GetClientIp()`)
  - `Shared/Http/HttpClientContextProvider.cs`(ASP.NET Core 实现,X-Forwarded-For 解析)
- **重写** `EfAuditLogger`:构造器从 `(AppDbContext, IHttpContextAccessor?)` 变 `(IAuditStore, IClientContextProvider?)`;无 DbContext / HTTP 依赖
- Program.cs + 2 DI 注册
- 8 处 `new EfAuditLogger(Ctx.Db)` 测试 fixture 改为 `new EfAuditLogger(new EfAuditStore(Ctx.Db))`

**验证**:§8.2 grep 在 IdpCore/Audit 下空;216/216 tests 绿。

### 4. `c48f325` — §8.2 修复(收尾):`IAuthSessionRepo` + `ITicketBlacklistRepo`

**背景**:Stage 3 grep 发现 `IdpCore/Sessions/AuthSessionStore.cs` 和 `IdpCore/Tokens/TicketBlacklistService.cs` 也直接用 `AppDbContext`。同 `EfAuditLogger` 一样模式。

**做了什么**(应用"policy / store"模板):
- **新建 4 文件**:
  - `Sources/Contracts/IAuthSessionRepo.cs`(Add/Find/Remove/DeleteExpired/SaveChanges)
  - `Sources/Local/EfAuthSessionRepo.cs`(EF 实现)
  - `Sources/Contracts/ITicketBlacklistRepo.cs`(Exists/Add/DeleteExpired/SaveChanges)
  - `Sources/Local/EfTicketBlacklistRepo.cs`(EF 实现)
- **重写** `AuthSessionStore`:policy 保留(serverState 生成 / TTL 算术 / 清理 cadence),持久化委派给 repo
- **重写** `TicketBlacklistService`:policy 保留(dedup / 5-min GC),持久化委派给 repo
- Program.cs + 2 DI 注册
- 13 处测试 fixture 更新

**验证**:§8.2 grep 在 IdpCore 下全空;216/216 tests 绿。

### 5. `ccb1b58` — §4.2 重命名:`IAuditLogger` → `IAuditSink`,`EfAuditLogger` → `AuditLogSink`

**背景**:ADR-0002 §4.2 契约名是 `IAuditSink`。类名 `EfAuditLogger` 在第 3 次 commit 后已无 EF 依赖,`Ef` 前缀变成误导。

**做了什么**:
- `mv Shared/Contracts/IAuditLogger.cs IAuditSink.cs`(+ 类型名 `IAuditLogger` → `IAuditSink`)
- `mv IdpCore/Audit/EfAuditLogger.cs AuditLogSink.cs`(+ 类型名 `EfAuditLogger` → `AuditLogSink`)
- 全项目 sed:`IAuditLogger` → `IAuditSink`,`EfAuditLogger` → `AuditLogSink`
- 20 文件受影响,纯标识符替换,零行为变动

**验证**:216/216 tests 绿。

### 6. `d2a0e08` — §4.3 `IProtocolFacade` 自注册

**背景**:Program.cs 硬编码各协议的 DI 注册 + 路由映射。违反 "Facade 可插拔" 理念。

**做了什么**:
- **新建 3 文件**:
  - `Facades/Contracts/IProtocolFacade.cs`(`Name` + `RegisterServices` + `MapEndpoints`)
  - `Facades/Imprivata/ImprivataFacade.cs`(OStick scheme + AuthUser/Servers/Domains/Modalities + 501 通配)
  - `Facades/Admin/AdminFacade.cs`(Admin scheme + Controllers)
- **重写** Program.cs:基础设施(DbContext/Sources/IdpCore)保留;Facade 相关代码变成 `foreach (var f in facades) f.RegisterServices(...)` + `foreach (...) f.MapEndpoints(...)`
- Program.cs 瘦身 ~40 行

**解锁**:未来加 `SamlFacade` / `OidcFacade` 只需一行 `facades` 数组改动。

**验证**:216/216 tests 绿。

### 7. `1f81ea0` — §4.2 抽出 `ILockoutPolicy` + `ILockoutRepo`

**背景**:锁定计数(`RecordPwdFailureAsync` 等 4 个方法)内嵌在 `UserStore`,违背 ADR-0002 §4.2 的 "policy 独立" 设想。

**做了什么**:
- **新建 4 文件**:
  - `IdpCore/Authentication/Contracts/ILockoutPolicy.cs`(`CheckAsync/OnSuccessAsync/OnFailureAsync` + `PwdOrPin` enum + `LockoutStatus` record)
  - `IdpCore/Authentication/LockoutPolicy.cs`(读 `AuthPolicyConfig`,组合 repo)
  - `Sources/Contracts/ILockoutRepo.cs`(`ReadAsync/WriteAsync` + `LockoutState` record)
  - `Sources/Local/EfLockoutRepo.cs`(EF 读写 User.Pwd/PinFailCount / LockedUntil)
- **重构** `PwdAuthenticator` / `PinAuthenticator`:注入 `ILockoutPolicy`,替换 3 处内联 `IsCurrentlyLocked` 检查和 4 处 `_users.Record*` 调用
- **从 `IUserStore` 删除 4 方法**:`RecordPwdSuccessAsync` / `RecordPwdFailureAsync` / `RecordPinSuccessAsync` / `RecordPinFailureAsync`
- Program.cs + 2 DI 注册
- 2 个认证器测试 fixture 更新,注入新 `LockoutPolicy`

**注**:`PwdOrPin` 枚举起初放在 `IdpCore.Authentication`,导致 `Sources.Contracts.ILockoutRepo` 反向依赖 IdpCore(隐性 §8.3 违规)。commit 8 的 ArchUnit 触发检测后,在 commit 8 修复(搬到 `Shared/Contracts/`)—— 实际这个 fix 被一并塞进了 commit 8。

**验证**:216/216 tests 绿。

### 8. `11a6ff5` — Phase γ:ArchUnitNET 10 条架构规则

**背景**:§8 反模式已人工清零,但没有 CI 保护 —— 将来有人写违规代码照样能过。

**做了什么**:
- `dotnet add package TngTech.ArchUnitNET.xUnit`(0.13.3)
- **新建** `tests/ImprivataProxy.Tests/Architecture/LayeringTests.cs`,10 条规则:
  - §8.1 × 3 条:Facades 不得依赖 `AppDbContext` / Ef*/UserStore / LdapClient
  - §8.2 × 3 条:IdpCore 不得依赖 `AppDbContext` / `IHttpContextAccessor` / `System.Xml.Linq`
  - §8.3 × 2 条:Sources 不得依赖 IdpCore / Facades
  - §8.4 × 2 条:IdpCore 和 Sources 都不得依赖 `IConfiguration`
- **顺手修** commit 7 遗留的 §8.3 隐性违规:`PwdOrPin` 从 `IdpCore.Authentication` 搬到 `Shared/Contracts/PwdOrPin.cs`(3 文件使用方 using 更新)

**验证**:226/226 tests 全绿(216 + 10 新)。架构 suite 运行 ~22 ms。

### 9. `6b1adb5` — §4.2 显式决策:泛型 `IAuthenticator<T>` / `ITokenIssuer<T>` Deferred

**背景**:§4 还剩 2 项泛型契约未做。用户评估后认定**没有实际收益**(本项目锁死 3 种 modality + 1 种 token 类型),决定不做,但要从"TODO"状态降级为"有意延期"。

**做了什么**(文档修订,零代码改动):
- [adr-0002-idp-architecture.md](./adr-0002-idp-architecture.md) §附录 B 状态卡片刷新 + 新增 "Deferred 项说明"
- 本文档("下一步" 改为 "后续推进状态") + 新增 "有意延期" 小节
- [CHANGELOG.md](../CHANGELOG.md):把原先 "Deferred (known technical debt)" 块拆成 `Resolved` 和 `Consciously Deferred`

### 10. `4fcfe0a` — 实施记录加 38 处 commit hash 回链

**背景**:本文档"后续推进状态"列了 7 条概要,但缺 commit hash。未来读者想查"某项改动是哪个 commit"要去翻 git log。

**做了什么**:
- 头部状态行刷新(226/226 + 11 实现/2 有意延期)
- 新增 "成就一览":4 初始 commits 表 + 9 后续 commits 表 + §4 契约逐条 → commit hash + §8 反模式 → commit hash
- "后续推进状态" 改写 "后续推进明细" —— 9 个带 hash 标题的 block
- 删除陈旧的 "⚠️ 已知遗留" 表和 "§4 5/11" 旧数据

文档从 352 行 → 502 行,新增 38 处 commit hash 回链。零代码改动。

---

### 11. `b7ae53c` — §4.1 IdpCore 去 AD 化:UserIdentity + IRemotePasswordVerifier 真正接通

**背景**:外部评审指出,虽然 `IRemotePasswordVerifier` 说是"通用抽象",但接口签名 `VerifyAsync(string distinguishedName, ...)` 是 LDAP 专属语义,且 `PwdAuthenticator` 实际走的是 `ILdapClient.BindAsUserAsync`,`IRemotePasswordVerifier` 在 DI 容器里无人注入——形同"死代码"。结论:当前契约形状和实现落地仍是 **AD/Ldap-first**。

**做了什么**:

**新抽象**:
- 新建 `Shared/Contracts/UserIdentity.cs` — 协议中立身份 record:`Username` + `Domain` + `DistinguishedName?` + `UserPrincipalName?` + `ObjectGuid?`。每种 verifier 按自己理解的字段解读(LDAP → DN;SAML ECP → UPN;...)

**契约升级**:
- `IRemotePasswordVerifier.VerifyAsync` 签名 `string distinguishedName` → `UserIdentity identity`
- 文档注释强调各实现自己选字段,PwdAuthenticator 协议无关

**LDAP 适配器清洁化**:
- `ILdapClient.BindAsUserAsync` 删除(AD 特定,本不该在公共接口)
- `LdapClient.VerifyAsync` 重写:直接打开 LDAP 连接 + bind,按异常类型分 `Valid` / `Invalid`(LDAP 49) / `Unreachable`(其他);tri-state 真实生效(之前 `Unreachable` 分支是死代码)
- `ILdapClient` 瘦身到只剩 `SearchAllUsersAsync`

**IdpCore 切换**:
- `PwdAuthenticator` 字段 `ILdapClient _ldap` → `IRemotePasswordVerifier _remote`
- 构造 `UserIdentity` 从 User 实体,调 `_remote.VerifyAsync`
- **顺手修 UX bug**:LDAP 宕机原本被吞掉日志后作为"invalid credentials"返回 + 累计 lockout。现在返回 `Unreachable` → `RtcSystemError`,不累计 lockout。用户稍后重试即可

**测试适配**:
- `PwdAuthenticatorTests.FakeLdap` → `FakeVerifier`(实现 `IRemotePasswordVerifier`),字段 `Results: Dictionary<(dn,pwd), RemoteVerifyOutcome>` 替 `BindResults`
- `FakeLdapClient`(Helpers) 瘦身 + 加 `IRemotePasswordVerifier` 双接口
- `IntegrationAppFactory` DI 现 `RemoveAll<ILdapClient>` + `RemoveAll<IRemotePasswordVerifier>` 然后双注册同一 fake

**ArchUnit 新规则(第 11 条)**:
- `IdpCore_Should_Not_Depend_On_ILdapClient` —— 锁死 IdpCore 不得直接引用 AD 特定的 `ILdapClient`

**验证**:227/227 tests 绿(原 226 + 新 ArchUnit 1 条)。

**结构效果**:
- `IRemotePasswordVerifier` 从"DI 容器里站着但没人拿"变成 **PwdAuthenticator 的唯一验证通道**
- 未来加 `SamlEcpVerifier : IRemotePasswordVerifier` → 在 Program.cs 换一行 DI,`PwdAuthenticator` 零改动
- IdpCore 目录下 grep `ILdapClient` 零命中,ArchUnit 持续保护

---

### 12. `19febd2` — Gateway 集成联调:Draeger Gateway → ImprivataProxy 端到端

**背景**:Draeger Gateway(医疗设备 CMS 认证网关)通过 Imprivata ProveID Web API 协议对接,实际发来 `GET /sso/ProveIDWeb/v28/Domains` + `POST /sso/ProveIDWeb/v28/AuthUser`。首次联调发现我们的响应格式和真实 Imprivata 有差异,Gateway 不认。

**做了什么**:

**12. Domains 端点格式修复**:
- 原始响应 `<Domain name="..." type="AD"/>` 被 Gateway 忽略
- 重写为真实 Imprivata 格式:`<Domain id="..."><UserDirType>AD</UserDirType><UseSSL>false</UseSSL><Name meaning="DNS">onesign.online</Name><Name meaning="NetBIOS">ONESIGN</Name><SPN>...</SPN></Domain>`
- 使用 reverse DomainMapping 对外展示 `onesign.online`(而非内部 `ad.vista.com`)
- 加入 `OneSignLocal` 域条目(OneSign 类型)
- 使用确定性 GUID(MD5 from domain name)作为 domain id

**13. 空 domain 处理 + DefaultDomain**:
- Gateway 发送的 AuthUser 请求中 `<Domain>` 为空(尽管 Domains 响应里有域信息)
- 添加 `ProxyConfig.DefaultDomain`(appsettings.json `"DefaultDomain": "ad.vista.com"`)
- 当请求 domain 为空时 fallback 到 `DefaultDomain`
- 添加完整的 domain 解析日志:empty → DefaultDomain / mapped / as-is

**14. UID/PIN reverse domain mapping**:
- `HandleUidAsync` 和 `HandlePinAsync` 也需要 reverse domain(从 `ad.vista.com` → `onesign.online`)
- 提取 `GetReverseDomain(AuthResult, ProxyConfig)` 辅助方法
- 统一 3 种 modality 的 domain 显示逻辑

**15. PWD Credential Failure 格式(disp="2")**:
- 真实 Imprivata 密码错误返回 `disp="2"` + 用户 Principal 信息 + ModalityEnrollment
- 我们原来返回 `disp="4"` + 无用户信息(Gateway 可能无法区分"密码错误"和"用户不存在")
- 新增 `ReturnCodes.DispCredentialFailure = 2`
- 新增 `AuthResult.CredentialFailure(User, Rtc, Reason)` 变体
- `PwdAuthenticator` 的 `RemoteVerifyOutcome.Invalid` 分支改返回 `CredentialFailure`
- `ImprivataXmlBuilder.CredentialFailure()` 生成带 Principal + ModalityEnrollment 但无 AuthTicket/userPolicy 的响应
- `AuthUserEndpoint.ToXmlResult` 新增 `CredentialFailure` 分支

**验证结果**(实机联调):
- Draeger Gateway → `GET /Domains` → 正确解析域列表
- 设备端 PWD 登录(shaolei/Draeger123)→ 认证成功,Gateway 回传 session
- 密码错误 → `disp="2"` + 用户信息(待验证)
- 用户不存在 → `disp="4"`(不泄漏信息)

---

### 13. *(wip)* — 双模式 LDAP 认证(Sync + OnDemand)

**背景**:现有系统要求部署服务账户定期全量同步 AD 用户才能登录。新增 **OnDemand 模式**:无需服务账户,用户首次密码登录时系统用其凭据直接 UPN bind AD → 成功后自动创建本地记录 → Admin 再分配卡/PIN。两种模式通过 `Ad:Mode` 配置切换,默认 `"Sync"`,向后兼容。

**做了什么**:

**13a. 配置扩展**:
- `AdConfig.Mode` 字段:`"Sync"`(默认)或 `"OnDemand"`
- `appsettings.json` 添加 `"Mode": "Sync"` 到 `Ad` 节

**13b. `ILdapClient.BindAndSearchSelfAsync`**:
- 新增接口方法:用户凭据 UPN bind(`username@domain`) → 搜索自身属性 → 返回 `OnDemandLoginResult`
- `OnDemandLoginResult` sealed record:三态 `Valid`(含 `AdUserDto`)/ `Invalid` / `Unreachable`
- `LdapClient` 实现:UPN bind → `(&(objectClass=user)(sAMAccountName={escaped}))` → `TryMapEntry`
- **LDAP filter escaping**(`EscapeLdapFilter`):防注入,处理 `\*()` 和 NUL 5 个特殊字符

**13c. `PwdAuthenticator` OnDemand 分支**:
- 在 `user is null` 处检测 OnDemand 模式
- `BindAndSearchSelfAsync`(通过 `IOnDemandLoginProvider` 接口) → 三态处理:
  - `Valid` → `UpsertFromAdAsync` + 签发 ticket(path=`"ondemand_first_login"`)
  - `Invalid` → 返回 `RtcInvalidCredentials`
  - `Unreachable` → 返回 `RtcSystemError`(不累计 lockout)
- 新注入:`IOnDemandLoginProvider` + `IOptions<AdConfig>`
- **注意**:密码不缓存本地哈希,每次认证均通过 AD LDAP bind 实时验证

**13d. 条件 DI + SyncController 适配**:
- `Program.cs`:仅 `Mode=Sync` 时注册 `AdSyncService` + `AddHostedService`
- `SyncController`:nullable `AdSyncService?` 注入,OnDemand 模式返回 404

**验证结果**:
- `dotnet build` —— 0 warnings, 0 errors
- OnDemand 分支逻辑覆盖 Valid / Invalid / Unreachable 三态
- Sync 模式行为不变(回归安全)

---

## 有意延期 ⏸️(**不是下一步,仅在触发条件出现时考虑**)

以下 2 项**不是技术债**,而是经过评估的架构选择:

- **泛型 `IAuthenticator<TInput>`** + `PwdInput` / `UidInput` / `PinInput` records
- **泛型 `ITokenIssuer<TToken>`** + 多 token 类型

**触发条件**(任一出现时才回头做):
- 出现第 4 种 modality(FP / PKI / OTP 等进入本项目范围);或
- 引入 SAML / OIDC Facade 且需要非 OStick 的 token 格式

**决策依据**见 [ADR-0002 §附录 B "Deferred 项说明"](./adr-0002-idp-architecture.md)。简要:

- [plan.md §1.3](../plan.md) 锁死 3 种 modality,没有第 4 种要来
- [ADR-0001](./adr-0001-adsync-vs-saml.md) 放弃 SAML 路线,没有第 2 种 token 格式要来
- 强行做只会换来可读性损失(调用点代码量 +200%,命名抽象化),理论收益为零
- 决策在 commit `6b1adb5` 显式落档
