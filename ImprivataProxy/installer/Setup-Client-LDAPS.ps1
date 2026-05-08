<#
.SYNOPSIS
    在 ImprivataProxy 主机上配置 LDAPS 证书信任。

.DESCRIPTION
    此脚本在安装 ImprivataProxy 的主机上执行以下操作：
    1. 导入域控的自签名证书到受信任根存储
    2. 验证 LDAPS 连接
    3. 验证 ImprivataProxy 服务状态

.PARAMETER CertFile
    从域控导出的证书文件路径（.cer 文件）。

.PARAMETER LdapHost
    域控的 FQDN（与证书中的 DnsName 一致）。

.PARAMETER LdapPort
    LDAPS 端口，默认 636。

.EXAMPLE
    .\Setup-Client-LDAPS.ps1 -CertFile "C:\sso.ad.vista.com.cer" -LdapHost "sso.ad.vista.com"
#>
param(
    [Parameter(Mandatory)]
    [string]$CertFile,

    [Parameter(Mandatory)]
    [string]$LdapHost,

    [int]$LdapPort = 636
)

$ErrorActionPreference = "Stop"

Write-Host "============================================" -ForegroundColor Cyan
Write-Host " ImprivataProxy 主机 LDAPS 配置脚本" -ForegroundColor Cyan
Write-Host " 证书文件: $CertFile" -ForegroundColor Cyan
Write-Host " LDAP 主机: ${LdapHost}:${LdapPort}" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

# ================================================================
# Step 0: 验证 DNS 能解析目标主机
# ================================================================
Write-Host "[0/3] 检查 DNS 解析..." -ForegroundColor Yellow
try {
    $resolved = [System.Net.Dns]::GetHostAddresses($LdapHost)
    Write-Host "  $LdapHost -> $($resolved[0])" -ForegroundColor Green
} catch {
    Write-Host "  错误: 无法解析主机名 $LdapHost" -ForegroundColor Red
    Write-Host ""
    Write-Host "  解决方法 (二选一):" -ForegroundColor Yellow
    Write-Host "    1. 将 DNS 指向域控: Set-DnsClientServerAddress -InterfaceAlias 'Ethernet0' -ServerAddresses '<域控IP>','8.8.8.8'" -ForegroundColor White
    Write-Host "    2. 添加 hosts 记录: Add-Content C:\Windows\System32\drivers\etc\hosts '<域控IP> $LdapHost'" -ForegroundColor White
    Write-Host ""
    exit 1
}

# ================================================================
# Step 1: 验证证书文件存在
# ================================================================
Write-Host "[1/3] 检查证书文件..." -ForegroundColor Yellow
if (-not (Test-Path $CertFile)) {
    Write-Host "  错误: 证书文件不存在: $CertFile" -ForegroundColor Red
    Write-Host "  请先从域控复制证书文件到本机" -ForegroundColor Yellow
    exit 1
}
Write-Host "  文件存在: $CertFile" -ForegroundColor Green

# ================================================================
# Step 2: 导入证书到受信任根存储
# ================================================================
Write-Host "[2/3] 导入证书到受信任根存储..." -ForegroundColor Yellow

# 先删除旧的同名证书
$oldCerts = Get-ChildItem Cert:\LocalMachine\Root | Where-Object { $_.Subject -match $LdapHost }
if ($oldCerts) {
    $oldCerts | Remove-Item -Force
    Write-Host "  已删除旧证书" -ForegroundColor DarkGray
}

$imported = Import-Certificate -FilePath $CertFile -CertStoreLocation "Cert:\LocalMachine\Root"
Write-Host "  已导入证书: $($imported.Subject)" -ForegroundColor Green
Write-Host "  指纹: $($imported.Thumbprint)" -ForegroundColor Green

# ================================================================
# Step 3: 验证 LDAPS 连接
# ================================================================
Write-Host "[3/3] 验证 LDAPS 连接..." -ForegroundColor Yellow

try {
    $tcp = New-Object System.Net.Sockets.TcpClient($LdapHost, $LdapPort)
    $ssl = New-Object System.Net.Security.SslStream($tcp.GetStream(), $false, {$true})
    $ssl.AuthenticateAsClient($LdapHost)
    Write-Host ""
    Write-Host "============================================" -ForegroundColor Green
    Write-Host " LDAPS 连接成功!" -ForegroundColor Green
    Write-Host " TLS 版本: $($ssl.SslProtocol)" -ForegroundColor Green
    Write-Host " 服务器证书: $($ssl.RemoteCertificate.Subject)" -ForegroundColor Green
    Write-Host "============================================" -ForegroundColor Green
    $ssl.Dispose()
    $tcp.Dispose()
} catch {
    Write-Host ""
    Write-Host "============================================" -ForegroundColor Red
    Write-Host " LDAPS 连接失败!" -ForegroundColor Red
    Write-Host " 错误: $_" -ForegroundColor Red
    Write-Host "============================================" -ForegroundColor Red
    Write-Host ""
    Write-Host "排查步骤:" -ForegroundColor Yellow
    Write-Host "  1. 确认域控 LDAPS 已启用 (在域控上运行 Setup-DC-LDAPS.ps1)" -ForegroundColor White
    Write-Host "  2. 确认网络连通: Test-NetConnection $LdapHost -Port $LdapPort" -ForegroundColor White
    Write-Host "  3. 确认防火墙未阻止 $LdapPort 端口" -ForegroundColor White
    exit 1
}

Write-Host ""
Write-Host "ImprivataProxy appsettings.json 中应配置:" -ForegroundColor Cyan
Write-Host "  `"LdapUrl`": `"ldaps://${LdapHost}:${LdapPort}`"" -ForegroundColor White
Write-Host ""

# 检查 ImprivataProxy 服务是否存在
$svc = Get-Service -Name "ImprivataProxy" -ErrorAction SilentlyContinue
if ($svc) {
    Write-Host "ImprivataProxy 服务状态: $($svc.Status)" -ForegroundColor Cyan
    if ($svc.Status -eq "Running") {
        Write-Host "建议重启服务以加载新配置: Restart-Service ImprivataProxy" -ForegroundColor Yellow
    }
} else {
    Write-Host "提示: ImprivataProxy 服务尚未安装，请先运行 MSI 安装包" -ForegroundColor DarkGray
}
