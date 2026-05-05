# ImprivataProxy

> **Last updated**: 2026-05-05

Imprivata ProveID Web API 兼容的本地身份提供者。对外说 Imprivata XML,对后端用 Active Directory(LDAPS)+ 本地 SQLite 账户库。

- 完整设计:[designdoc/LDAP.md](designdoc/LDAP.md)
- 实施计划(各 phase 交付清单):[plan.md](plan.md)

---

## 1. 它是什么

- **对 Imprivata 客户端**:看起来就是一台 Imprivata 服务器(同样的 `/sso/ProveIDWeb/v28/*` XML 协议)
- **对运维**:一个独立的 ASP.NET Core 服务,自己管账户库、自己发 OStick Ticket(JWT),只跟 AD 做**读同步** + **密码验证**
- 支持三种认证场景:**PWD(用户名+密码)** / **UID(刷卡)** / **UID + PIN**
- 密码修改交给 AD 自己管——代理通过"首次 LDAP bind 成功时缓存 argon2 哈希 + 本地验证命中/失效时回退 bind"吸收 AD 侧变更

---

## 2. 运行

### 前置

- .NET 8 SDK
- 一个 AD 域 + LDAPS 可达
- AD 里的一个**只读服务账号**(建议 `svc-imprivata`,密码长期稳定)
- 可选:预置 RSA 2048 PEM 签名密钥(不给的话首次启动会自动生成)

### 必需环境变量

```bash
# 管理 API Basic Auth 密码(admin 用户)
export ADMIN_PASSWORD=<strong-password>

# AD 服务账号密码
export AD_SVC_PASSWORD=<svc-account-password>
```

### 启动

```bash
dotnet run --project src/ImprivataProxy

# 或 Windows 服务(自包含发布)
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o ./publish
sc.exe create ImprivataProxy binPath= "C:\path\to\ImprivataProxy.exe"
```

### 配置

`src/ImprivataProxy/appsettings.json` 主要段:

| 段 | 关键字段 |
|----|----------|
| `Proxy` | `ListenAddress`, `ListenPort` |
| `Database` | `ConnectionString`(默认 SQLite `./data/proxy.db`) |
| `Ad` | `LdapUrl`, `BaseDn`, `ServiceAccountDn`, `SyncIntervalMinutes` |
| `AuthPolicy` | `PwdMaxFails`, `PinMaxFails`, `PwdHashTtlDays`, `AuthSessionTtlSeconds` 等 |
| `Ticket` | `SigningKeyPath`, `TtlHours`, `Issuer` |
| `Admin` | `Username`(默认 `admin`),`PasswordEnvVar` |

---

## 3. 快速体验

### Imprivata 协议(客户端视角)

```bash
# 登录(PWD)
curl -X POST http://localhost/sso/ProveIDWeb/v28/AuthUser \
  -H "Content-Type: text/xml" \
  --data '<Request><ModalityAuthInput modalityID="PWD"><AuthRequest>
<PasswordVerificationRequest>
<UserIdentity><Username>alice</Username><Domain>CORP</Domain></UserIdentity>
<Password>p@ss</Password>
</PasswordVerificationRequest></AuthRequest></ModalityAuthInput>
<CreateAuthTicket>true</CreateAuthTicket></Request>'
# → <Response><AuthState disp="0"/>...<AuthTicket>eyJ...</AuthTicket></Response>

TICKET=eyJ...

# whoami
curl http://localhost/sso/ProveIDWeb/v28/AuthUser \
  -H "Authorization: OStick ostick.ticket=$TICKET"

# 登出(吊销 ticket)
curl -X CANCEL http://localhost/sso/ProveIDWeb/v28/AuthUser \
  -H "Authorization: OStick ostick.ticket=$TICKET"
```

### 管理 API

```bash
# 手动触发 AD 同步
curl -u admin:$ADMIN_PASSWORD -X POST http://localhost/admin/sync

# 列用户 / 搜索
curl -u admin:$ADMIN_PASSWORD 'http://localhost/admin/users?search=alice'

# 发卡
curl -u admin:$ADMIN_PASSWORD -X POST http://localhost/admin/cards \
  -H "Content-Type: application/json" \
  -d '{"userId":"<uuid>","cardUid":"1234567890","label":"main"}'

# 设 PIN
curl -u admin:$ADMIN_PASSWORD -X PUT http://localhost/admin/users/<uuid>/pin \
  -H "Content-Type: application/json" \
  -d '{"pin":"1234"}'

# 解锁账户(重置 PWD/PIN 失败计数)
curl -u admin:$ADMIN_PASSWORD -X POST http://localhost/admin/users/<uuid>/unlock

# 禁用账户
curl -u admin:$ADMIN_PASSWORD -X PATCH http://localhost/admin/users/<uuid> \
  -H "Content-Type: application/json" -d '{"enabled":false}'
```

---

## 4. Docker

```bash
# 1. 填必需变量
cp .env.example .env
$EDITOR .env            # AD_SVC_PASSWORD / ADMIN_PASSWORD / AD_* 连接信息

# 2. 构建 + 启动
docker compose up -d --build

# 3. 健康检查
curl http://localhost/health

# 4. 首次同步 AD(导入用户)
curl -u admin:$ADMIN_PASSWORD -X POST http://localhost/admin/sync
```

### 持久化卷(docker compose 自动创建)

| 卷名 | 挂载路径 | 作用 |
|------|---------|------|
| `proxy-data` | `/app/data` | SQLite DB — **必须**持久,否则账户/卡/PIN 全丢 |
| `proxy-certs` | `/app/certs` | JWT 签名密钥 PEM — 持久则已发 ticket 跨重启仍有效 |
| `proxy-logs` | `/app/logs` | Serilog 滚动日志 |

备份 DB:
```bash
docker run --rm \
  -v imprivataproxy_proxy-data:/data \
  -v $PWD:/backup \
  alpine tar czf /backup/proxy-db-$(date +%F).tar.gz -C /data .
```

### 镜像特性

- 基础镜像 `mcr.microsoft.com/dotnet/aspnet:8.0`(runtime-only,无 SDK)
- 非 root 用户 `app` (uid 1000) 运行
- 容器内监听 **8080**(非特权),compose 映射到宿主 `${PROXY_HOST_PORT:-80}`
- curl healthcheck 每 30s 打 `/health`
- 配置覆盖用 `__` 双下划线环境变量(例:`Ad__LdapUrl`)

### 企业 AD 自签 CA 证书

如果 AD 用内部 CA 签 LDAPS 证书,默认容器不信任。两条路子:

1. **运行时挂载 + 一次性注册**(compose 有示例注释):
   ```yaml
   volumes:
     - /etc/ssl/corp-ca.crt:/usr/local/share/ca-certificates/corp-ca.crt:ro
   entrypoint: /bin/sh -c 'update-ca-certificates && dotnet ImprivataProxy.dll'
   ```
2. **打进镜像**:
   ```dockerfile
   COPY corp-ca.crt /usr/local/share/ca-certificates/
   RUN update-ca-certificates
   ```

---

## 5. 开发与测试

```bash
# 全部测试(单元 + 集成)
dotnet test

# 仅单元
dotnet test --filter "FullyQualifiedName!~Integration"

# 仅集成
dotnet test --filter "FullyQualifiedName~Integration"

# 生成 EF 迁移
dotnet ef migrations add <Name> \
  --project src/ImprivataProxy --output-dir Accounts/Migrations
```

当前测试数:**216**(含 30 个 HTTP 级集成用例)。

---

## 6. 安全要点

- **所有密码/PIN 都 argon2id 哈希**,卡号 SHA-256 哈希
- **恒定时间比较**:Admin Basic Auth、argon2 verify 都用 `FixedTimeEquals`
- **LDAPS 强制**:配置 `ldaps://` scheme,`System.DirectoryServices.Protocols` 自动走 TLS
- **JWT 签名密钥**:PEM 文件,Linux 下权限自动设 0600;生产建议预置不要用自动生成
- **日志脱敏**:`Password / OldPassword / NewPassword / PIN / UniqueID / AuthTicket` XML 元素在日志里替换为 `***`;`Authorization` header 也替换为 `***`
- **管理 API 走独立 Basic Auth scheme**,不接受 OStick ticket(反之亦然)
- **AD 同步失败绝不扫尾禁用**——网络抖动不会把用户全禁

---

## 7. 未实现的 Imprivata 资源

以下端点被代理显式拒绝(HTTP 501 + Imprivata 风格 XML),没在路线图上:

```
/Password /Enrollment /Multi /VdiAccess /ConfigObject /SAMLArtifact /UserAppCreds
```

原因:PWD 修改由 AD 自行管理;其他资源不在当前业务范围内。如需启用,从 [designdoc/LDAP.md §1.5](designdoc/LDAP.md) 开始。
