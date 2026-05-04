# ADR-0001:身份源选型 —— AD LDAP 同步 vs SAML 属性富化

- **状态**: Accepted
- **日期**: 2026-05-03
- **决策人**: ImprivataProxy 项目组
- **相关文档**: [LDAP.md](./LDAP.md) / [plan.md](../plan.md)

---

## 1. 背景

[LDAP.md](./LDAP.md) 最初设计的 ImprivataProxy 是一个 **YARP 反向代理 + SAML 属性富化** 的中间件:

- Imprivata 客户端的 REST 请求被代理透传到**上游 Imprivata 服务器**
- 认证由上游 Imprivata 完成(PWD / UID / UID+PIN / FP / PKI / KRB / QnA / OTP)
- 代理本身作为 **SAML SP** 连接外部 IdP(ADFS / Azure AD),**每次登录实时查 IdP 拿属性断言**,注入 `groups` / `department` 到请求/响应中

业务方向调整后,新 [plan.md](../plan.md) 做了两件事:

1. **不再有上游 Imprivata 服务器** —— 代理自己充当 Imprivata 协议后端
2. **SAML 全部移除** —— 身份源只用 **AD(LDAP/LDAPS)**,周期性同步到本地 SQLite

这份文档记录这一架构收敛的权衡和决策,并留档给未来可能再次切换身份源的场景作为参考。

---

## 2. 核心澄清:原设计里的 SAML 只做**属性富化**,不是认证主角

这是最容易被误解的一点。

原 [LDAP.md:11](./LDAP.md#L11) 写得很清楚:

> 代理查询 SAML IdP 并注入额外属性到请求中

SAML 在原设计里**不负责验证密码、卡号或 PIN** —— 那些都由上游 Imprivata 服务器负责。SAML 只在登录成功后,用 `memberOf`、`department` 这些属性富化请求体。换句话说,SAML 是条**旁路**,不是**主干**。

原流程(简化):

```
Imprivata Client ──XML──▶ Proxy
                           │
                           ├──(转发认证)──▶ Imprivata 服务器 ← 真正认证在这里
                           │
                           └──(查属性)────▶ SAML IdP         ← 属性富化
                           │
                           ◀── 合并响应 ──
```

去掉上游 Imprivata 后,主认证的坑必须有东西填。SAML 原则上**填不了**这个坑(见 §4)。于是引入了 AD LDAP。

---

## 3. 本质区别对照表

| 维度 | **SAML**(原方案的 SAML 旁路) | **AD LDAP 同步**(新方案) |
|------|------------------------------|--------------------------|
| 解决什么问题 | 登录时实时获取 IdP 签发的属性断言 | 周期性把 AD 用户元数据拉本地,登录时本地查 |
| 协议 | HTTP POST/Redirect + XML 签名断言 | LDAPS 二进制(ASN.1 BER + TLS) |
| 通讯方向 | **浏览器驱动**,用户被重定向到 IdP 登录页 | **服务器到服务器**,代理后台主动拉 |
| 触发时机 | 每次认证流程中(或首次登录时) | 定时(默认 30 min)+ 手工触发 |
| 数据新鲜度 | 登录瞬间 = 100% 实时 | 最差滞后 1 个 sync 间隔 |
| 凭证归谁管 | **IdP 全权**(密码、MFA、生物识别在 IdP) | AD 管密码;卡号/PIN 代理管 |
| 信任模型 | **证书+签名**(SP 验证 IdP 签名的断言) | **账号+TLS**(服务账号 bind + DC 证书) |
| 用户体验 | 浏览器跳转,看到 IdP 登录页 | 用户完全感知不到 AD |
| 离线容忍 | ❌ IdP 必须在线 | ✅ 本地 argon2 命中即可 |
| 适合的客户端 | 浏览器 / 支持 SAML 的 SPA / WebView | 任何后端客户端,**包括嵌入式 / 无 GUI** |
| 协议复杂度 | 高(SP metadata / cert trust / binding / replay / NotOnOrAfter) | 低(bind + search) |
| MFA | IdP 天然支持 | 自己实现(如 UID+PIN) |
| 撤销延迟 | **立即**(IdP 拒绝下次登录) | 最差 1 个 sync 间隔 |
| 运维 | 需要 SAML 库、证书轮转、metadata 更新 | 需要 AD 管理员 + 服务账号 + DC 证书 |

### 流量对照

**SAML 方式(属性富化):**
```
┌──────────┐        ┌──────────┐
│ Client   │─XML───▶│  Proxy   │──SAML──▶┌─────┐
└──────────┘        │          │◀─assert─│ IdP │
                    │          │         └─────┘
                    │          │──XML───▶┌──────────┐
                    │          │◀──XML───│Imprivata │
                    └──────────┘         └──────────┘
                每次登录:代理 → IdP 实时拿断言
```

**AD LDAP 同步(新方案):**
```
┌──────────┐        ┌──────────┐
│ Client   │─XML───▶│  Proxy   │
└──────────┘        │  ┌────┐  │──LDAPS──▶┌─────┐
                    │  │本地│  │          │ AD  │
                    │  │ DB │  │◀─────────└─────┘
                    │  └────┘  │  (每 30 min 全量同步)
                    │          │──LDAPS──▶ PWD bind fallback 按需
                    └──────────┘
                登录时:直接查本地 DB,必要时 bind AD
```

---

## 4. 关键业务场景:刷卡 / PIN —— SAML 装不下

原设计能工作**正是因为**有 Imprivata 上游兜底 UID/PIN。去掉上游后,SAML 单独无法覆盖刷卡 / PIN,根因如下:

| SAML 假设 | 刷卡 / PIN 现实 |
|-----------|--------------|
| 用户有**浏览器**,能做 HTTP Redirect | 护士在病床边刷卡,**没浏览器** |
| IdP 是**认证主体**,用它自己的方式验密码/MFA | 卡号是 Imprivata 业务概念,**AD / Azure AD / ADFS 都没有这个 schema** |
| SP 把用户**重定向**到 IdP 登录页 | Imprivata 协议是**胖客户端 XML POST**,没有重定向能力 |
| 凭证由用户**在 IdP 侧输入** | 卡号是刷卡器硬件读出来的,**SAML 协议里没有传输它的位置** |

**本质矛盾**:SAML 是"前端 + 联邦"协议;刷卡/PIN 是"后端 + 本地验证"场景。

如果硬要在 SAML 框架里塞刷卡/PIN,有 3 条路都不好:

| 路线 | 说明 | 问题 |
|------|------|------|
| A. 把卡号当密码给 IdP 验 | SAML ECP 或 OIDC ROPC 代送卡号 | ECP 多数 IdP 不开;ROPC 已被 OAuth 2.1 **废弃** |
| B. SAML 先 PWD,之后本地 UID/PIN | 首次浏览器 SAML 登录注册卡+PIN,后续纯本地 | 实际等于"SAML 引导 + 本地 DB",跟 plan.md 几乎一样,只是 PWD 真相源换了 |
| C. SAML 只管 PWD,UID/PIN 纯本地 | 原 LDAP.md 架构,只不过 PWD 没 Imprivata 上游兜底 | PWD 验证的坑仍然悬空,要么 ECP 要么 ROPC,都不理想 |

---

## 5. 备选混合方案:SAML + 本地 DB

从技术上讲,PWD 的 "argon2 透明缓存 + 远程 fallback" 机制**和验证协议无关**。可以抽象成:

```csharp
interface IRemotePasswordVerifier {
    Task<bool> VerifyAsync(string userIdentifier, string password, CancellationToken ct);
}

class LdapBindVerifier    : IRemotePasswordVerifier { /* LDAP simple bind */ }
class SamlEcpVerifier     : IRemotePasswordVerifier { /* SAML ECP */ }
class OidcRopcVerifier    : IRemotePasswordVerifier { /* OIDC ROPC,不推荐 */ }
```

UID / PIN **完全与协议无关**,纯本地。

用户列表同步换成 **SCIM 2.0**([RFC 7644](https://www.rfc-editor.org/rfc/rfc7644)),现代 IdP(Azure AD / Okta / Keycloak)都支持。

### 混合架构

```
            ┌──────────┐
            │ SAML IdP │──── SCIM ───┐ 周期拉用户
            │ (Azure   │──── ECP ────┤ 按需验 PWD
            │  AD /    │
            │  Okta /  │
            │  Keycloak)
            └──────────┘             │
                                     ▼
            ┌───────────────────────────┐
            │     ImprivataProxy        │
            │                           │
            │  SamlSyncService (SCIM)   │ ← 替代 AdSyncService
            │  AuthEngine               │
            │   ├─ PwdAuth ── SAML ECP  │ ← 替代 AD bind
            │   ├─ UidAuth ── 本地 SHA-256
            │   └─ PinAuth ── 本地 argon2
            │  本地 SQLite              │
            │  TicketIssuer (JWT)       │
            └───────────────────────────┘
```

**90% 代码与 plan.md 相同**,只替换两个模块:

| plan.md | SAML 混合 |
|---------|----------|
| `AdSyncService` + LDAPS | `ScimSyncService` + SCIM API |
| `AdBindAuthenticator` + LDAP simple bind | `SamlEcpAuthenticator` + SAML ECP |

### 何时值得走 SAML 混合

| 信号 | 含义 |
|------|------|
| 公司没有本地 AD | 身份源在 Entra ID / Okta / Keycloak / Ping |
| 需要**外部合作医生**临时接入 | 跨组织联邦,SAML 原生支持 |
| 已经投资了 SAML/OIDC 体系 | 想统一身份栈 |
| 多医院多域场景 | 集中身份管理 |
| 云原生部署 | SaaS IdP 优于自建 AD |

### 何时仍选 plan.md(AD)

| 信号 | 含义 |
|------|------|
| 公司主要身份源是本地 AD | 多数医疗 / 企业场景 |
| IdP 不支持 ECP | ECP 要走复杂审批 |
| 医疗设备本地部署 | 弱网常见,AD 本地性更好 |
| 团队对 SAML ECP 不熟悉 | 学习曲线 + 运维负担 |
| 严审计 / 离线可用需求 | AD 成熟度更高 |

---

## 6. 决策

**本项目采用 AD LDAP 同步方案(plan.md 路线)。**

### 理由

1. **业务场景贴合**:医疗现场、刷卡、弱网、胖客户端,全部是 AD 的主场
2. **部署目标契合**:客户环境普遍有本地 AD,很少直接使用云 IdP
3. **协议简单**:LDAP simple bind + paged search,成熟且跨平台
4. **代码面最小**:一条真相源,无需同时维护 SAML 协议栈
5. **离线容忍**:本地 argon2 缓存让日常登录不依赖网络
6. **合规友好**:ISO27001 / 等保 / SOC2 审计对 AD 服务账号模式熟悉

### 显式放弃

- ❌ SAML SP 实现([src/ImprivataProxy/Saml/](../src/ImprivataProxy/Saml/) 整个目录删除,见 plan.md [§项目结构改动](../plan.md#L668))
- ❌ ITfoxtec.Identity.Saml2 NuGet 依赖
- ❌ Keycloak / OIDC 集成
- ❌ 跨域联邦能力(合作医生临时接入需走单独 Phase)

---

## 7. 留给未来的扩展口

尽管当前不实现 SAML,plan.md 的实现应**天然支持未来切换**或**并存**:

### 7.1 抽象 `IRemotePasswordVerifier`

`PwdAuthenticator` 依赖这个接口而不是直接调 AD:

```csharp
// Authentication/IRemotePasswordVerifier.cs
public interface IRemotePasswordVerifier {
    Task<VerifyResult> VerifyAsync(UserIdentity id, string password, CancellationToken ct);
}

// ActiveDirectory/AdBindAuthenticator.cs : IRemotePasswordVerifier
// (Saml/SamlEcpAuthenticator.cs : IRemotePasswordVerifier) ← 未来新增
```

切换 / 并存只需改 DI 注册,核心 `PwdAuthenticator` 零改动。

### 7.2 抽象 `IUserDirectorySync`

`BackgroundService` 依赖这个接口:

```csharp
public interface IUserDirectorySync {
    Task<SyncResult> RunOnceAsync(CancellationToken ct);
}

// ActiveDirectory/AdSyncService.cs : IUserDirectorySync
// (Scim/ScimSyncService.cs : IUserDirectorySync) ← 未来新增
```

### 7.3 配置层的 `Source` 枚举

```json
"Identity": {
  "Source": "Ad",           // "Ad" | "Saml" | "Hybrid"
  "Ad": { ... },
  "Saml": { ... }
}
```

`Hybrid` 场景(例如:主身份走 AD,合作医生走 SAML)在代码层面可通过多个 `IRemotePasswordVerifier` 实例 + 基于 `users.domain` 或 `user.attributes.external_idp` 的路由实现。

---

## 8. 回顾触发条件

以下情况发生时,应当重新评估本决策:

- 客户基础明显转向云 IdP(Entra ID / Okta / Google Workspace)成为主体
- 公司收购或合并,需要承接不同身份栈的用户
- 出现跨医院联邦的业务需求,且不能靠 AD 信任关系满足
- 医疗行业监管要求外部医生必须走 IdP 审计链
- .NET 生态出现显著简化 SAML ECP 集成的新库

重新评估时,§5 的混合方案是第一备选。

---

## 9. 参考

- [LDAP.md](./LDAP.md) —— 原反向代理 + SAML 富化设计(将在本决策后按 plan.md 重写)
- [plan.md](../plan.md) —— 新架构实施计划
- [SAML 2.0 ECP Profile (OASIS)](https://docs.oasis-open.org/security/saml/Post2.0/saml-ecp/v2.0/saml-ecp-v2.0.html)
- [SCIM 2.0 (RFC 7644)](https://www.rfc-editor.org/rfc/rfc7644)
- [Microsoft:Deprecation of ROPC in Azure AD](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth-ropc)
- [OWASP:LDAP Authentication](https://owasp.org/www-community/controls/LDAP_Authentication)
