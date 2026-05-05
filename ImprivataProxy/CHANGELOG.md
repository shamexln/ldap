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

### Resolved (since first draft of this CHANGELOG)

后续 commits 已解决这些遗留项:

- **§8.2 违规** —— `EfAuditLogger` / `AuthSessionStore` / `TicketBlacklistService` 全部抽出 `IXxxRepo`(Sources)+ policy(IdpCore),`IClientContextProvider` 承担 HTTP 抽象
- **§8.3 违规** —— `IAuditLogger` 搬到 `Shared/Contracts/`;`PwdOrPin` 也搬到 `Shared/Contracts/`
- **§4 部分契约**:`IAuditSink` 重命名、`ILockoutPolicy` 抽出、`IProtocolFacade` 自注册、各层 `Contracts/` 子目录——全部完成
- **IdpCore 去 AD 化**:`IRemotePasswordVerifier` 签名从 `string distinguishedName` 升级到 `UserIdentity`(协议中立);`PwdAuthenticator` 不再依赖 `ILdapClient`,走 `IRemotePasswordVerifier`;`ILdapClient.BindAsUserAsync` 移除;`LdapClient.VerifyAsync` 真正 tri-state(Valid/Invalid/Unreachable);LDAP 不可达现在返回 `RtcSystemError` 不累计 lockout(修潜 UX bug);ArchUnit 加第 11 条规则 `IdpCore_Should_Not_Depend_On_ILdapClient`
- **Phase γ ArchUnitNET** —— 10 条架构规则已上,`§8` 在 CI 被持续保护

### Consciously Deferred (**not technical debt** — explicit design decisions)

- **泛型 `IAuthenticator<TInput>` + `PwdInput/UidInput/PinInput` records**
- **泛型 `ITokenIssuer<TToken>` + 多 token 类型**

这两项**不是待办**,而是经过评估的架构选择:

1. 本项目 [plan.md §1.3](plan.md) 明确锁定 3 种 modality(PWD / UID / UID+PIN),未来不会新增
2. [ADR-0001](designdoc/adr-0001-adsync-vs-saml.md) 显式放弃 SAML,只有 OStick JWT 一种 token
3. 泛型化**没有实际收益**(没有统一分派的多态需求),**却有可读性代价**(调用点代码量 +200%,命名抽象化)
4. §8 架构正确性由 [ArchUnit 测试](tests/ImprivataProxy.Tests/Architecture/LayeringTests.cs) 持续保护

**回顾触发条件**(未来满足任一则回头做):
- 出现第 4 种 modality(FP / PKI / OTP 等进入本项目范围)
- 引入 SAML / OIDC Facade 且需要非 OStick 的 token 格式

详细论证见 [ADR-0002 §附录 B](designdoc/adr-0002-idp-architecture.md)。

### Verification

- `dotnet clean && dotnet build` —— 0 warnings, 0 errors
- `dotnet test` —— **227 / 227 passed, 0 failed**(含 11 条 ArchUnit 架构规则)
- 反模式 grep(ADR-0002 §8):§8.1 / §8.2 / §8.3 / §8.4 全部零违规

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
