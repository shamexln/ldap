# Changelog

本项目遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/) 规范,
版本号遵循 [Semantic Versioning](https://semver.org/lang/zh-CN/)。

---

## [Unreleased]

### Changed

- **架构**:项目按 [ADR-0002](designdoc/adr-0002-idp-architecture.md) §3 重组为三层结构 **Facades / IdpCore / Sources**,外加 `Shared/`
  - `Endpoints/` → `Facades/Imprivata/`(并入 `OStick*.cs`)
  - `Admin/` → `Facades/Admin/`
  - `Authentication/` → `IdpCore/Authentication/` + `IdpCore/Sessions/`(拆出会话状态)
  - `Tickets/` → `IdpCore/Tokens/`(去掉 `OStick*`,归 Facade)
  - `Accounts/` → `Sources/Local/` + `IdpCore/Audit/`(`IAuditLogger` 拆出)
  - `ActiveDirectory/` → `Sources/ActiveDirectory/`
  - `Logging/` → `Shared/Logging/`
  - `Xml/` → `Shared/Xml/`
- **Namespace**:67 源文件 + 31 测试文件的 `namespace` 和 `using` 声明同步更新
- **Admin 控制器**:`UsersController` / `CardsController` 不再注入 `AppDbContext`,改走 `IUserStore`([ADR-0002 §8.1 违规修复](designdoc/adr-0002-idp-architecture.md))
- **`DomainsEndpoint`**:改走 `IUserStore.GetDistinctEnabledDomainsAsync`,不再直接用 `AppDbContext`

### Added

- **`Sources/Contracts/IRemotePasswordVerifier`**:抽象"问外部身份源这个密码对不对"的能力
  - 三态结果 `RemoteVerifyOutcome { Valid, Invalid, Unreachable }`
  - `LdapClient` 适配实现;未来可平行加 SAML ECP / OIDC ROPC 实现
- **`Sources/Contracts/IUserDirectorySync`**:抽象"定期从外部身份目录拉用户"的能力
  - `AdSyncRunner` 实现之;未来可接 SCIM 2.0 / Microsoft Graph delta
- **`IUserStore` 扩展 10 方法**:
  - `ListUsersAsync`, `FindByIdWithCardsAsync`, `SetUserEnabledAsync`, `SetPinHashAsync`, `UnlockUserAsync`
  - `FindCardByHashAsync`, `CreateCardAsync`, `FindCardByIdWithUserAsync`, `RevokeCardAsync`
  - `GetDistinctEnabledDomainsAsync`
- **Program.cs DI 注册**:`IRemotePasswordVerifier` / `IUserDirectorySync` 通过适配器模式(`sp => (IX)sp.GetRequiredService<Concrete>()`)与既有实例共享
- **文档**:
  - [designdoc/adr-0001-adsync-vs-saml.md](designdoc/adr-0001-adsync-vs-saml.md) —— 身份源选型决策
  - [designdoc/adr-0002-idp-architecture.md](designdoc/adr-0002-idp-architecture.md) —— 三层架构决策
  - [designdoc/adr-0002-phase-ab-implementation.md](designdoc/adr-0002-phase-ab-implementation.md) —— Phase α + β 详细实施记录
  - [designdoc/diagrams/adr-0002-authuser-flowchart.puml](designdoc/diagrams/adr-0002-authuser-flowchart.puml) —— 流程图
  - [designdoc/diagrams/adr-0002-authuser-sequence.puml](designdoc/diagrams/adr-0002-authuser-sequence.puml) —— UML Sequence 图

### Fixed

- **[ADR-0002 §8.1](designdoc/adr-0002-idp-architecture.md)** Facade 直接访问 Sources 具体类型的违规:`UsersController` / `CardsController` / `DomainsEndpoint` 三处 `AppDbContext` 注入全部移除

### Deferred (known technical debt)

- **§8.2 违规**:[`IdpCore/Audit/EfAuditLogger`](src/ImprivataProxy/IdpCore/Audit/EfAuditLogger.cs) 仍依赖 `AppDbContext` + `IHttpContextAccessor`。需引入 `IAuditStore` + `IClientContextProvider` 抽象后解决
- **§8.3 违规**:[`Sources/ActiveDirectory/AdSyncRunner`](src/ImprivataProxy/Sources/ActiveDirectory/AdSyncRunner.cs) + `AdSyncService` `using ImprivataProxy.IdpCore.Audit`。可通过把 `IAuditLogger` 接口搬到 `Shared/Contracts/` 解决(方案 A,~10 分钟)
- **§4 完整契约** 未完成项(6/11):
  - 泛型 `IAuthenticator<TInput>` + `PwdInput/UidInput/PinInput` records + `AuthRequestContext`
  - 泛型 `ITokenIssuer<TToken>`
  - `IAuditSink` 重命名(现仍为 `IAuditLogger`)
  - `ILockoutPolicy` 从 `UserStore` 抽出
  - `IProtocolFacade` 自注册机制
  - 各层 `Contracts/` 子目录(目前仅 `Sources/Contracts/`)
- **Phase γ**:ArchUnitNET 架构测试未引入

### Verification

- `dotnet clean && dotnet build` —— 0 warnings, 0 errors
- `dotnet test` —— **216 / 216 passed, 0 failed**
- 反模式 grep(ADR-0002 §8):§8.1 零违规;§8.2/§8.3 已知遗留如上

### Refs

- ADR-0002 §6 Phase α(保守版)+ Phase β 全部覆盖
- 详细实施记录:[designdoc/adr-0002-phase-ab-implementation.md](designdoc/adr-0002-phase-ab-implementation.md)

---

## [0.1.0] - 初始版本

项目初始实现(Local IdP + AD 同步 + OStick JWT ticket),Endpoints / Authentication / Accounts /
ActiveDirectory / Tickets / Admin 平铺目录结构。216 tests 通过。

参见:
- [designdoc/LDAP.md](designdoc/LDAP.md) —— 设计文档
- [plan.md](plan.md) —— 原始实施计划
