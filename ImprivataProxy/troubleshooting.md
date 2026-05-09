# ImprivataProxy 故障排查指南

## 常见问题速查表

| 现象 | 可能原因 | 解决方法 |
|------|----------|----------|
| 服务无法启动 | 端口被占用 | `netstat -ano \| findstr :80` |
| 服务无法启动 | 配置文件错误 | 检查 `appsettings.json` JSON 格式 |
| 无法连接 AD | DNS 无法解析域控主机名 | 见"DNS 解析问题" |
| 无法连接 AD | LDAP 地址或端口错误 | `Test-NetConnection <ip> -Port 636` |
| 无法连接 AD | 密码错误 | 检查环境变量 `AD_SVC_PASSWORD` |
| LDAPS 连接失败 | 域控证书 Provider 不兼容 | 见"LDAPS 证书问题" |
| LDAPS 连接失败 | 证书未导入受信任根 | 见"LDAPS 证书问题" |
| 脚本运行报语法错误 | 文件编码问题 (PowerShell 5.x) | 见"PowerShell 脚本执行问题" |
| 脚本提示 "scripts is disabled" | 执行策略限制 | 见"PowerShell 脚本执行问题" |

---

## DNS 解析问题

### 现象

```
New-Object : Exception calling ".ctor" with "2" argument(s): "No such host is known"
```

或 `nslookup sso.ad.vista.com` 返回 "Non-existent domain"。

### 原因

ImprivataProxy 主机的 DNS 服务器（如 8.8.8.8）无法解析内网域名 `sso.ad.vista.com`。域控通常同时是内网 DNS 服务器。

### 诊断

```powershell
# 检查 DNS 配置
nslookup sso.ad.vista.com
ipconfig /all | findstr "DNS"
```

### 修复

**方法一：添加 hosts 记录**（推荐，不影响其他 DNS 解析）

```powershell
Add-Content C:\Windows\System32\drivers\etc\hosts "192.168.230.205 sso.ad.vista.com"
ipconfig /flushdns
```

**方法二：修改 DNS 服务器**

```powershell
Set-DnsClientServerAddress -InterfaceAlias "Ethernet0" -ServerAddresses "192.168.230.205","8.8.8.8"
```

### 验证

```powershell
ping sso.ad.vista.com
# 应返回: Reply from 192.168.230.205
```

### 注意

- hosts 文件中必须写**真实 IP**，不能用占位符如 `192.168.228.x`
- 修改后必须执行 `ipconfig /flushdns` 刷新 DNS 缓存
- 如果是新装机器，网卡名可能不是 `Ethernet0`，用 `Get-NetAdapter` 查看实际名称

---

## LDAPS 证书问题

### 现象

```
The LDAP server is unavailable.
```

或 LDAPS 连接在端口 636 上超时/拒绝。

### 常见原因及修复

#### 1. 域控证书 Provider 不正确

NTDS 只认特定的加密 Provider。使用错误的 Provider（如 "Microsoft Strong Cryptographic Provider"）会导致 NTDS 不加载证书。

**正确做法**：使用 `Setup-DC-LDAPS.ps1`，它指定了正确的 Provider：
```
-Provider "Microsoft RSA SChannel Cryptographic Provider"
```

#### 2. 证书未在受信任根存储

自签名证书必须同时存在于 `Cert:\LocalMachine\My` 和 `Cert:\LocalMachine\Root`。

**检查**：
```powershell
Get-ChildItem Cert:\LocalMachine\Root | Where-Object { $_.Subject -match "sso.ad.vista.com" }
```

#### 3. NTDS 未重启

生成证书后必须重启 NTDS 服务：
```powershell
Restart-Service NTDS -Force
```

#### 4. 客户端未导入域控证书

ImprivataProxy 主机必须信任域控的证书：
```powershell
Import-Certificate -FilePath "C:\sso.ad.vista.com.cer" -CertStoreLocation "Cert:\LocalMachine\Root"
```

#### 5. SkipCertValidation 与 SecureSocketLayer 顺序问题

代码中 `VerifyServerCertificate` 回调必须在设置 `SecureSocketLayer = true` **之前**设置，否则会报 "server unavailable"。

### 手动验证 LDAPS

```powershell
$tcp = New-Object System.Net.Sockets.TcpClient("sso.ad.vista.com", 636)
$ssl = New-Object System.Net.Security.SslStream($tcp.GetStream(), $false, {$true})
$ssl.AuthenticateAsClient("sso.ad.vista.com")
Write-Host "TLS: $($ssl.SslProtocol)  证书: $($ssl.RemoteCertificate.Subject)"
$ssl.Dispose(); $tcp.Dispose()
```

成功输出：
```
TLS: Tls12  证书: CN=sso.ad.vista.com
```

---

## PowerShell 脚本执行问题

### 现象一：执行策略限制

```
File C:\Setup-Client-LDAPS.ps1 cannot be loaded because running scripts is disabled on this system.
```

### 修复

```powershell
powershell -ExecutionPolicy Bypass -File "C:\Setup-Client-LDAPS.ps1" -CertFile "C:\sso.ad.vista.com.cer" -LdapHost "sso.ad.vista.com"
```

或临时解除限制：
```powershell
Set-ExecutionPolicy Bypass -Scope Process
```

### 现象二：中文脚本语法解析错误

```
Unexpected token '}' in expression or statement.
```

### 原因

PowerShell 5.x（Windows 自带版本）默认将无 BOM 的 UTF-8 文件当作 ANSI 编码读取。脚本中的中文注释被错误解析，导致语法错误。

### 检查版本

```powershell
$PSVersionTable.PSVersion
# Major=5 → 有此问题
# Major=7 → 无此问题
```

### 修复方法

1. **确保脚本文件为 UTF-8 with BOM 编码**（推荐）
   - 运行 `publish.ps1` 构建时会自动处理编码
   - 或用记事本打开脚本 → 另存为 → 编码选择"UTF-8 BOM"

2. **安装 PowerShell 7**
   ```powershell
   winget install Microsoft.PowerShell
   ```
   然后使用 `pwsh` 代替 `powershell` 运行脚本。

---

## LDAP 协议与端口对照

| 协议 | 端口 | 说明 |
|------|------|------|
| `ldap://` | 389 | 明文传输，不加密 |
| `ldaps://` | 636 | TLS 加密传输 |

**常见错误**：
- `ldaps://xxx:389` — 错误！LDAPS 使用端口 636，不是 389
- `ldap://xxx:636` — 错误！端口 636 需要 `ldaps://` 协议

**正确配置**：
```json
"LdapUrl": "ldaps://sso.ad.vista.com:636"
```

---

## 三者必须一致

配置 LDAPS 时，以下三个位置的主机名必须完全一致：

| 位置 | 值 | 配置方法 |
|------|-----|----------|
| 域控证书 DnsName | `sso.ad.vista.com` | `Setup-DC-LDAPS.ps1 -DnsName` |
| 客户端脚本 LdapHost | `sso.ad.vista.com` | `Setup-Client-LDAPS.ps1 -LdapHost` |
| appsettings.json LdapUrl | `ldaps://sso.ad.vista.com:636` | 手动编辑或安装时指定 |

任何一个不一致都会导致 TLS 证书验证失败（除非 `SkipCertValidation: true`）。

---

## LDAPS 部署脚本使用指南

### Setup-DC-LDAPS.ps1（在域控服务器上执行）

此脚本在域控上生成自签名证书并启用 LDAPS。

**前置条件**：
- 必须在域控服务器上以管理员身份运行
- 域控已安装 AD DS 角色

**基本用法**：
```powershell
powershell -ExecutionPolicy Bypass -File "D:\project\ldap\ImprivataProxy\installer\Setup-DC-LDAPS.ps1" -DnsName "ldaps.chrliege.be"
```

**参数说明**：

| 参数 | 必需 | 默认值 | 说明 |
|------|------|--------|------|
| `-DnsName` | 是 | — | 客户端连接时使用的 FQDN |
| `-ValidYears` | 否 | 5 | 证书有效期（年） |
| `-NoDcHostName` | 否 | — | 不将 DC 主机名加入证书 SAN |

**示例**：
```powershell
# 标准用法
.\Setup-DC-LDAPS.ps1 -DnsName "ldaps.chrliege.be"

# 指定有效期 10 年
.\Setup-DC-LDAPS.ps1 -DnsName "ldaps.chrliege.be" -ValidYears 10

# 不加 DC 主机名到 SAN
.\Setup-DC-LDAPS.ps1 -DnsName "ldaps.chrliege.be" -NoDcHostName
```

**执行步骤**（脚本自动完成）：
1. 检测 DC 主机名，构建证书 SAN 列表
2. 生成自签名证书（RSA 2048, SHA256, SChannel Provider）
3. 添加证书到受信任根存储
4. 导出 `.cer` 文件供客户端使用
5. 重启 NTDS 服务加载新证书
6. 验证 LDAPS 连接（127.0.0.1:636）

**输出文件**：脚本目录下生成 `<DnsName>.cer`，需拷贝到 ImprivataProxy 主机。

---

### Setup-Client-LDAPS.ps1（在 ImprivataProxy 主机上执行）

此脚本在 ImprivataProxy 主机上导入域控证书并验证 LDAPS 连通性。

**前置条件**：
- 已从域控拷贝 `.cer` 证书文件到本机
- 能通过网络访问域控的 636 端口
- DNS 能解析域控主机名（或已添加 hosts 记录）

**基本用法**：
```powershell
powershell -ExecutionPolicy Bypass -File "D:\project\ldap\ImprivataProxy\installer\Setup-Client-LDAPS.ps1" -CertFile "C:\ldaps.chrliege.be.cer" -LdapHost "ldaps.chrliege.be"
```

**参数说明**：

| 参数 | 必需 | 默认值 | 说明 |
|------|------|--------|------|
| `-CertFile` | 是 | — | 域控导出的 `.cer` 证书文件路径 |
| `-LdapHost` | 是 | — | 域控 FQDN（必须与证书 DnsName 匹配） |
| `-LdapPort` | 否 | 636 | LDAPS 端口 |

**示例**：
```powershell
# 标准用法
.\Setup-Client-LDAPS.ps1 -CertFile "C:\ldaps.chrliege.be.cer" -LdapHost "ldaps.chrliege.be"

# 指定非标准端口
.\Setup-Client-LDAPS.ps1 -CertFile "C:\ldaps.chrliege.be.cer" -LdapHost "ldaps.chrliege.be" -LdapPort 6636
```

**执行步骤**（脚本自动完成）：
1. 检查 DNS 解析目标主机名
2. 验证证书文件存在
3. 导入证书到受信任根存储
4. 验证 LDAPS 连接（TLS 握手）

---

## 前端管理界面登录失败

### 现象

前端页面输入 admin 密码后提示 "Invalid credentials"，或一直跳回登录页。

服务日志显示：
```
[ERR] Admin password env var 'ADMIN_PASSWORD' not set; rejecting all admin requests
AuthenticationScheme: Admin was challenged.
<< 401 for GET /admin/users
```

### 原因

ImprivataProxy 以 Windows 服务运行时，**不会继承**系统级环境变量。服务进程只能读取注册表中配置在该服务下的环境变量。

需要设置的环境变量：

| 环境变量 | 用途 |
|----------|------|
| `AD_SVC_PASSWORD` | LDAP 服务账号密码（AD 同步用） |
| `ADMIN_PASSWORD` | 前端管理界面登录密码 |

### 修复

```powershell
# 停止服务
Stop-Service ImprivataProxy

# 在注册表中设置服务环境变量（两个都要设置！）
$regPath = "HKLM:\SYSTEM\CurrentControlSet\Services\ImprivataProxy"
Set-ItemProperty -Path $regPath -Name "Environment" -Value @(
    "AD_SVC_PASSWORD=Draeger123!",
    "ADMIN_PASSWORD=admin123"
)

# 启动服务
Start-Service ImprivataProxy
```

### 验证

前端登录使用：
- 用户名：`admin`
- 密码：上面设置的 `ADMIN_PASSWORD` 值（如 `admin123`）

### 注意

- 只设置系统环境变量（`[Environment]::SetEnvironmentVariable(..., "Machine")`）对服务**无效**
- 必须通过注册表 `HKLM:\SYSTEM\CurrentControlSet\Services\<服务名>` 的 `Environment` 键值设置
- 修改后需要重启服务才生效
- 如果之后需要修改密码，重复上述步骤即可

---

## AD 同步凭据失败

### 现象

服务日志显示：
```
[WRN] LDAP bind failed for CN=xxx,DC=xxx: The supplied credential is invalid.
```

或：
```
Service account password env var 'AD_SVC_PASSWORD' not set
```

### 诊断

1. **确认密码正确**（在域控上测试）：
```powershell
$cred = New-Object System.Net.NetworkCredential("CN=Compte fourni,OU=Readers,OU=Users,OU=Specific,OU=CITADELLE,DC=CHRLIEGE,DC=BE", "Draeger123!")
$conn = New-Object System.DirectoryServices.Protocols.LdapConnection(New-Object System.DirectoryServices.Protocols.LdapDirectoryIdentifier("ldaps.chrliege.be", 636))
$conn.SessionOptions.SecureSocketLayer = $true
$conn.SessionOptions.VerifyServerCertificate = {$true}
$conn.AuthType = [System.DirectoryServices.Protocols.AuthType]::Basic
$conn.Bind($cred)
Write-Host "Bind 成功"
```

2. **确认环境变量已注入服务**（见上方"前端管理界面登录失败"的修复方法）

### 常见错误

- 密码末尾少了特殊字符（如 `Draeger123` vs `Draeger123!`）
- 只设置了系统环境变量，没有写入服务注册表
- Service Account DN 拼写错误（注意空格、大小写）

---

## 完整部署流程速查

```
┌─────────────────────────────────────┐
│         域控服务器 (DC)              │
│                                     │
│  1. 安装 AD DS 角色                 │
│  2. 提升为域控 (Install-ADDSForest) │
│  3. 创建 OU 和服务账号              │
│  4. 运行 Setup-DC-LDAPS.ps1        │
│  5. 拷贝 .cer 到客户端             │
└─────────────────────────────────────┘
                 │
                 │ 拷贝 .cer 文件
                 ▼
┌─────────────────────────────────────┐
│       ImprivataProxy 主机           │
│                                     │
│  1. 配置 DNS/hosts 解析域控名       │
│  2. 运行 Setup-Client-LDAPS.ps1    │
│  3. 安装 ImprivataProxy MSI        │
│     填写: LDAP URL / BaseDN /      │
│           ServiceAccountDN / Domain │
│  4. 设置服务环境变量（注册表）      │
│     AD_SVC_PASSWORD + ADMIN_PASSWORD│
│  5. 重启服务                        │
│  6. 访问 http://<IP>:80 登录管理页 │
└─────────────────────────────────────┘
```
