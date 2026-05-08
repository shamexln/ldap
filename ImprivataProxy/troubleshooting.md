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
