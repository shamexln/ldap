# ImprivataProxy - 安装与使用说明 (IFU)

## 系统要求

- Windows 10 / Windows Server 2016 或更高版本（64位）
- 至少 200MB 可用磁盘空间
- 网络访问权限（HTTP 端口，默认 80）
- Active Directory 服务账户（用于 LDAP 同步）
- 管理员权限（安装和运行 Windows 服务需要）

## 安装步骤

### 方式一：图形界面安装

1. 双击 `ImprivataProxy-x.x.x.msi` 安装包
2. 按照安装向导依次完成以下配置：
   - **安装目录**：默认为 `C:\Program Files\ImprivataProxy\`
   - **LDAP 配置**：
     - LDAP Server URL（例如 `ldap://192.168.1.100:389`）
     - Base DN（例如 `DC=example,DC=com`）
     - Service Account DN（例如 `CN=svc_imprivata,CN=Users,DC=example,DC=com`）
     - Service Account Password
   - **服务配置**：
     - HTTP 监听端口（默认 `80`）
     - 管理面板密码（Admin Password）
3. 点击 **Install** 开始安装
4. 安装完成后点击 **Finish**

### 方式二：静默安装（命令行）

以管理员身份运行 PowerShell 或 CMD：

```powershell
msiexec /i "ImprivataProxy-1.0.0.msi" /qn ^
    LISTEN_PORT=80 ^
    LDAP_URL="ldap://192.168.1.100:389" ^
    LDAP_BASE_DN="DC=example,DC=com" ^
    LDAP_SERVICE_DN="CN=svc_imprivata,CN=Users,DC=example,DC=com" ^
    AD_SVC_PASSWORD="your_ad_password" ^
    ADMIN_PASSWORD="your_admin_password"
```

## 安装后目录结构

```
C:\Program Files\ImprivataProxy\
├── ImprivataProxy.exe          # 服务主程序
├── appsettings.json            # 配置文件（可手动编辑）
├── web.config                  # IIS 集成配置
├── wwwroot\                    # 管理界面前端文件
├── data\                       # SQLite 数据库存储
│   └── proxy.db
├── logs\                       # 日志文件（按天滚动）
│   └── proxy-20260507.log
├── certs\                      # 证书文件
│   └── ticket-signing.pem
└── [.NET 运行时文件]
```

## 启动服务

### 自动启动

安装完成后，服务默认设置为**自动启动**，Windows 重启后会自动运行。

### 手动启动/停止

**方式一：Windows 服务管理器**

1. 按 `Win + R`，输入 `services.msc`，回车
2. 找到 **Imprivata Proxy Service**
3. 右键选择 **启动** / **停止** / **重启**

**方式二：命令行（管理员）**

```powershell
# 启动服务
sc start ImprivataProxy

# 停止服务
sc stop ImprivataProxy

# 查看服务状态
sc query ImprivataProxy

# 重启服务
sc stop ImprivataProxy && sc start ImprivataProxy
```

**方式三：PowerShell（管理员）**

```powershell
# 启动
Start-Service ImprivataProxy

# 停止
Stop-Service ImprivataProxy

# 重启
Restart-Service ImprivataProxy

# 查看状态
Get-Service ImprivataProxy
```

## 验证安装

安装并启动服务后，执行以下检查：

1. **检查服务状态**：
   ```powershell
   sc query ImprivataProxy
   ```
   应显示 `STATE: RUNNING`

2. **检查健康端点**：
   ```powershell
   curl http://localhost/health
   ```
   应返回：`{"status":"healthy","timestamp":"..."}`

3. **访问管理界面**：
   打开浏览器访问 `http://localhost`，使用安装时设置的管理员密码登录

## 配置修改

安装后如需修改配置，编辑以下文件：

```
C:\Program Files\ImprivataProxy\appsettings.json
```

修改后需重启服务生效：

```powershell
Restart-Service ImprivataProxy
```

### 常用配置项

| 配置项 | 路径 | 说明 |
|--------|------|------|
| 监听端口 | `Kestrel.Endpoints.Http.Url` | HTTP 监听地址和端口 |
| LDAP 地址 | `Ad.LdapUrl` | AD 服务器地址 |
| LDAP Base DN | `Ad.BaseDn` | 搜索根目录 |
| 同步间隔 | `Ad.SyncIntervalMinutes` | AD 用户同步间隔（分钟） |
| UID 模式 | `Ad.UidMode` | 身份识别模式：`Badge`（通过 Badge 号码实时查询 AD）或 `CardHash`（本地卡哈希查找） |
| Badge 属性 | `Ad.BadgeAttribute` | AD 中存储 Badge 号码的属性名（默认 `employeeNumber`） |
| 授权组 | `Ad.RequiredGroups` | 用户必须属于其中至少一个组才允许登录（数组，为空则不限制） |
| 密码锁定次数 | `AuthPolicy.PwdMaxFails` | 连续失败后锁定账户 |
| 锁定时间 | `AuthPolicy.PwdLockoutMinutes` | 锁定持续时间（分钟） |

### 环境变量

以下敏感信息通过服务环境变量配置（安装时已设置）：

| 变量名 | 说明 |
|--------|------|
| `AD_SVC_PASSWORD` | AD 服务账户密码 |
| `ADMIN_PASSWORD` | 管理面板登录密码 |

如需修改环境变量：

```powershell
# 修改注册表中的服务环境变量
$regPath = "HKLM:\SYSTEM\CurrentControlSet\Services\ImprivataProxy"
$env = @("AD_SVC_PASSWORD=new_password", "ADMIN_PASSWORD=new_admin_pwd", "ASPNETCORE_ENVIRONMENT=Production")
Set-ItemProperty -Path $regPath -Name "Environment" -Value $env -Type MultiString

# 重启服务使修改生效
Restart-Service ImprivataProxy
```

## 日志查看

日志文件位于安装目录下的 `logs\` 文件夹，按天滚动：

```powershell
# 查看今天的日志
Get-Content "C:\Program Files\ImprivataProxy\logs\proxy-20260507.log" -Tail 50

# 实时跟踪日志
Get-Content "C:\Program Files\ImprivataProxy\logs\proxy-20260507.log" -Wait
```

## 卸载

### 方式一：控制面板

1. 打开 **设置 > 应用 > 已安装的应用**
2. 找到 **Imprivata Proxy Service**
3. 点击 **卸载**

### 方式二：命令行

```powershell
msiexec /x "ImprivataProxy-1.0.0.msi" /qn
```

> **注意**：卸载后 `data\` 和 `logs\` 目录会保留，不会删除用户数据。如需完全清理，请手动删除安装目录。

## Active Directory 域控初始化

在安装 ImprivataProxy 之前，需要在域控制器上完成以下准备工作。安装包中提供了一键初始化脚本 `Setup-AD-TestUsers.ps1`。

### 前置条件

- 域控服务器（Windows Server 2016 或更高）具有管理员权限
- 已安装 Active Directory Domain Services (AD DS) 角色
- 已安装 ActiveDirectory PowerShell 模块（域控默认已安装）

### 需要准备的内容

| 项目 | 说明 |
|------|------|
| 服务账号 | 用于 ImprivataProxy 连接 LDAP 查询用户信息 |
| 用户的 `employeeNumber` 属性 | 存储 Badge（徽章）号码，作为刷卡识别的主键 |
| 授权安全组 | 用户必须属于至少一个授权组才允许登录 |

### 执行初始化脚本

在域控服务器上以管理员身份运行 PowerShell：

```powershell
powershell -ExecutionPolicy Bypass -File Setup-AD-TestUsers.ps1
```

脚本自动执行以下操作：

1. **创建测试用户**（位于 Base DN 搜索范围内）
   - `tester1`（Badge: `9021054`）
   - `tester2`（Badge: `9999999`）

2. **创建服务账号** `svc_draeger`（位于 Readers OU）
   - 密码永不过期
   - 无需管理员权限，普通域用户即可读取其他用户属性

3. **创建授权安全组**
   - `PRM_Infirmier_Moniteur`
   - `PRM_Aide_Soignant`
   - `PRM_Assistant_Logistique`

4. **分配组成员**（将测试用户加入所有授权组）

### 手动设置用户 Badge 号码

如果已有现成域用户，只需为其填写 `employeeNumber` 属性：

```powershell
Set-ADUser -Identity "existing_user" -EmployeeNumber "1234567"
```

也可以在"Active Directory 用户和计算机"图形界面中：用户属性 → 组织 → 员工编号。

### AD 属性说明

ImprivataProxy 通过 Badge 号码查询用户后，将读取以下标准 AD 属性（Windows Server 2016+ 默认自带，无需扩展 Schema）：

| 属性 | 说明 |
|------|------|
| `employeeNumber` | Badge 号码（可通过 `appsettings.json` 的 `BadgeAttribute` 配置映射到其他属性） |
| `displayName` | 显示名称 |
| `givenName` | 名 |
| `sn` | 姓 |
| `sAMAccountName` | 登录名 |
| `userPrincipalName` | UPN 登录名 |
| `memberOf` | 组成员关系（用于授权判断） |

## LDAPS 证书配置（加密通信）

ImprivataProxy 与 Active Directory 之间的通信建议使用 LDAPS（端口 636）加密传输。配置分两步：先在域控上生成并启用证书，再在 ImprivataProxy 主机上导入证书建立信任。

### 前置条件

- 域控服务器具有管理员权限
- ImprivataProxy 主机具有管理员权限
- 两台机器网络互通（端口 636）

### 第一步：域控服务器配置

在域控服务器上以管理员身份运行 PowerShell，执行 `Setup-DC-LDAPS.ps1`：

```powershell
.\Setup-DC-LDAPS.ps1 -DnsName "sso.ad.vista.com"
```

参数说明：
| 参数 | 说明 | 示例 |
|------|------|------|
| `-DnsName` | 域控的 FQDN，必须与客户端连接时使用的主机名一致 | `sso.ad.vista.com` |
| `-ValidYears` | 证书有效期（年），默认 5 | `-ValidYears 10` |

脚本自动执行：
1. 删除旧的同名证书
2. 使用 NTDS 兼容的 Provider 生成自签名证书
3. 将证书添加到受信任根存储（自签名证书必须）
4. 重启 NTDS 服务加载证书
5. 验证 LDAPS 是否正常工作
6. 导出 `.cer` 证书文件（供客户端使用）

成功后输出：
```
============================================
 LDAPS 配置成功!
 TLS: Tls12
 证书: CN=sso.ad.vista.com
============================================

下一步:
  1. 将 sso.ad.vista.com.cer 复制到 ImprivataProxy 主机
  2. 在 ImprivataProxy 主机上运行 Setup-Client-LDAPS.ps1
```

### 第二步：复制证书文件

将域控上生成的 `.cer` 文件复制到 ImprivataProxy 主机。文件位置为脚本运行目录下的 `<DnsName>.cer`（例如 `sso.ad.vista.com.cer`）。

### 第三步：ImprivataProxy 主机配置

> **前置条件**：ImprivataProxy 主机必须能解析域控主机名。如果 DNS 未指向域控，需先添加 hosts 记录（参见"故障排查"章节）。

在 ImprivataProxy 主机上以管理员身份运行 PowerShell，执行 `Setup-Client-LDAPS.ps1`：

```powershell
.\Setup-Client-LDAPS.ps1 -CertFile "C:\sso.ad.vista.com.cer" -LdapHost "sso.ad.vista.com"
```

> **注意**：如果提示"running scripts is disabled"，请使用以下方式运行：
> ```powershell
> powershell -ExecutionPolicy Bypass -File ".\Setup-Client-LDAPS.ps1" -CertFile "C:\sso.ad.vista.com.cer" -LdapHost "sso.ad.vista.com"
> ```

参数说明：
| 参数 | 说明 | 示例 |
|------|------|------|
| `-CertFile` | 从域控复制过来的 `.cer` 证书文件路径 | `C:\sso.ad.vista.com.cer` |
| `-LdapHost` | 域控的 FQDN（与证书一致） | `sso.ad.vista.com` |
| `-LdapPort` | LDAPS 端口，默认 636 | `-LdapPort 636` |

脚本自动执行：
1. 导入证书到本机受信任根存储
2. 验证 LDAPS TLS 连接是否成功

成功后输出：
```
============================================
 LDAPS 连接成功!
 TLS 版本: Tls12
 服务器证书: CN=sso.ad.vista.com
============================================

ImprivataProxy appsettings.json 中应配置:
  "LdapUrl": "ldaps://sso.ad.vista.com:636"
```

### 第四步：修改 ImprivataProxy 配置

确认 LDAPS 连接成功后，编辑 `appsettings.json`，确保 LDAP 地址使用 LDAPS：

```json
"Ad": {
    "LdapUrl": "ldaps://sso.ad.vista.com:636",
    "BaseDn": "DC=ad,DC=vista,DC=com",
    "ServiceAccountDn": "CN=Administrator,CN=Users,DC=ad,DC=vista,DC=com",
    ...
}
```

修改后重启服务：

```powershell
Restart-Service ImprivataProxy
```

### 注意事项

- 证书的 `DnsName` 必须与 `appsettings.json` 中 `LdapUrl` 的主机名完全一致
- 自签名证书过期后需重新运行以上流程
- 如使用企业 CA 或公共 CA 签发的正式证书，无需运行这些脚本，只需确保证书链可信即可
- 配置项 `SkipCertValidation` 仅用于测试环境，生产环境应设为 `false`

## 故障排查

| 现象 | 可能原因 | 解决方法 |
|------|----------|----------|
| 服务无法启动 | 端口被占用 | 检查端口占用：`netstat -ano \| findstr :80` |
| 服务无法启动 | 配置文件错误 | 检查 `appsettings.json` 格式是否为合法 JSON |
| 无法连接 AD | LDAP 地址错误 | 确认 LDAP URL 可达：`Test-NetConnection <ip> -Port 636` |
| 无法连接 AD | 密码错误 | 检查环境变量 `AD_SVC_PASSWORD` 是否正确 |
| 无法连接 AD | DNS 无法解析域控主机名 | 添加 hosts 记录或将 DNS 指向域控（见下方说明） |
| LDAPS 脚本报语法错误 | 文件编码不正确（PowerShell 5.x） | 用 `powershell -ExecutionPolicy Bypass -File` 运行，或确保文件为 UTF-8 BOM 编码 |
| 脚本提示 "scripts is disabled" | PowerShell 执行策略限制 | 使用 `powershell -ExecutionPolicy Bypass -File "脚本路径"` 运行 |
| 管理界面无法登录 | 密码错误 | 检查环境变量 `ADMIN_PASSWORD` |
| 页面无法访问 | 防火墙阻止 | 检查 Windows 防火墙是否允许配置的端口 |

### DNS 解析问题详解

如果 ImprivataProxy 主机无法解析域控主机名（例如 `sso.ad.vista.com`），通常是因为机器的 DNS 服务器未指向域控（域控通常也是 DNS 服务器）。

**诊断：**

```powershell
nslookup sso.ad.vista.com
```

如果返回 "Non-existent domain"，说明 DNS 无法解析。

**修复方法（二选一）：**

方法一：添加 hosts 记录（推荐，简单直接）

```powershell
# 将域控 IP 加入 hosts 文件（以 192.168.230.205 为例）
Add-Content C:\Windows\System32\drivers\etc\hosts "192.168.230.205 sso.ad.vista.com"
ipconfig /flushdns
```

方法二：修改 DNS 服务器指向域控

```powershell
# 将主 DNS 改为域控 IP（以 Ethernet0 网卡为例）
Set-DnsClientServerAddress -InterfaceAlias "Ethernet0" -ServerAddresses "192.168.230.205","8.8.8.8"
```

**验证：**

```powershell
ping sso.ad.vista.com
# 应显示 Reply from 192.168.230.205
```
