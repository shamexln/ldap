# ADR-0002 Phase α + β 实施记录(归档)

- **日期**: 2026-05-04
- **执行**: 从 Claude Code 会话级 plan 文件 `~/.claude/plans/imprivata-uml-sequence-jazzy-bengio.md` 固化而来
- **结果**: ✅ 编译通过,216 / 216 tests 全绿
- **相关文档**: [adr-0002-idp-architecture.md](./adr-0002-idp-architecture.md) / [adr-0001-adsync-vs-saml.md](./adr-0001-adsync-vs-saml.md)

---

## 实施结果摘要(执行后补充)

### ✅ 已完成

| 项 | 覆盖章节 |
|----|---------|
| 目录重组到 `Facades / IdpCore / Sources / Shared` 三层 | ADR-0002 §5 主干 + Phase β |
| Namespace 按三层语义改名(67 源文件 + 31 测试文件) | §5 |
| `Sources/Contracts/IRemotePasswordVerifier` 新接口 | §4.1 |
| `Sources/Contracts/IUserDirectorySync` 新接口 | §4.1 |
| `LdapClient` 实现 `IRemotePasswordVerifier`(加 `VerifyAsync`) | Phase α 非破坏 |
| `AdSyncRunner` 实现 `IUserDirectorySync`(同签名已匹配) | Phase α 非破坏 |
| Program.cs DI 按接口注入(适配器模式) | §6 Phase α |
| `IUserStore` 扩展 10 方法 | §4.1 + §8.1 修 |
| Admin `UsersController` / `CardsController` 改走 `IUserStore`(不再直接用 `AppDbContext`) | §8.1 修 |
| `DomainsEndpoint` 改走 `IUserStore.GetDistinctEnabledDomainsAsync` | §8.1 修 |
| EF Migrations 元数据字符串同步到新 namespace | 清理 |

### ⚠️ 已知遗留(本次 scope 外)

| 项 | 原因 |
|----|------|
| `EfAuditLogger` 仍吃 `AppDbContext` + `IHttpContextAccessor` | §8.1/§8.2 违规,plan 风险表标"暂忍",与 `IAuditSink`/`IAuditStore` 抽象一并下次做 |
| `AdSyncRunner`/`AdSyncService` 仍 `using ImprivataProxy.IdpCore.Audit` | §8.3 违规。plan 漏写,实施漏修。未来可把 `IAuditLogger` 接口搬到 `Shared/Contracts/`(方案 A) |
| 泛型 `IAuthenticator<TInput>` 未引入 | §4.2 激进契约,用户选"保守版"跳过 |
| 泛型 `ITokenIssuer<TToken>` 未引入 | 同上 |
| `IAuditSink` 重命名 | 同上 |
| `ILockoutPolicy` 抽出 | 同上(锁定逻辑仍内嵌在 `UserStore`) |
| `IProtocolFacade` 自注册机制 | §4.3 未实施,Program.cs 仍手工 MapGet |
| ADR-0002 §5 每层 `Contracts/` 子目录 | 只建了 `Sources/Contracts/`;IdpCore/Facades 的接口仍散在各自目录 |
| Git commit | 工作树保留未提交 |

### §4 完整契约进度

按 ADR-0002 §4 清单衡量:**5 / 11(45%)**。未完成部分属于"激进 α / 完整 §4"变体,需独立 PR。

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

## 下一步(若希望推进到 §4 完整契约)

按优先级排序:

1. **§8.3 违规修复**(最小代价):把 `IAuditLogger.cs` 从 `IdpCore/Audit/` 搬到 `Shared/Contracts/`,仅约 10 处 using 更新。10 分钟
2. **§8.2 违规修复**(中等代价):抽出 `IAuditStore`(Sources 层)+ `IClientContextProvider`(Facade 层),重写 `EfAuditLogger` 为纯编排。1-2 小时
3. **`IAuditSink` 重命名**(中等代价):`IAuditLogger` → `IAuditSink`(+ 实现类 `EfAuditSink`),全项目约 30 处调用点改。30 分钟
4. **`ILockoutPolicy` 抽出**(中等代价):把锁定计数逻辑从 `UserStore.RecordPwd/PinFailureAsync` 提取成独立接口。1 小时
5. **泛型 `IAuthenticator<TInput>` / `ITokenIssuer<TToken>`**(大代价):引入 `PwdInput/UidInput/PinInput` records + 改签名 + 大量 mock 更新。2-4 小时
6. **`IProtocolFacade` 自注册机制**(中等代价):改 Program.cs,让 `ImprivataFacade` / `AdminFacade` 类各自实现 `IProtocolFacade`,Program.cs 变成 `foreach (f in facades) f.MapEndpoints(app);` 模式。1 小时
7. **ArchUnit 架构测试**(中等代价):加 `ArchUnitNET` NuGet + 写分层规则测试,在 CI 阻止回退。1-2 小时

逐项推进,每项独立成 PR 便于评审。
