<#
.SYNOPSIS
    在域控服务器上配置 LDAPS (端口 636) 所需的自签名证书。

.DESCRIPTION
    此脚本在域控上执行以下操作：
    1. 删除旧的同名证书（如有）
    2. 使用正确的 Provider 生成自签名证书
    3. 将证书添加到受信任根存储（自签名证书必须）
    4. 重启 NTDS 服务加载证书
    5. 验证 LDAPS 是否工作

.PARAMETER DnsName
    域控的 FQDN，必须与客户端连接时使用的主机名一致。

.PARAMETER ValidYears
    证书有效期（年）。默认 5 年。

.EXAMPLE
    .\Setup-DC-LDAPS.ps1 -DnsName "dc01.hospital.local"
    .\Setup-DC-LDAPS.ps1 -DnsName "sso.ad.vista.com" -ValidYears 10
#>
param(
    [Parameter(Mandatory)]
    [string]$DnsName,

    [int]$ValidYears = 5
)

$ErrorActionPreference = "Stop"

Write-Host "============================================" -ForegroundColor Cyan
Write-Host " 域控 LDAPS 证书配置脚本" -ForegroundColor Cyan
Write-Host " DnsName: $DnsName" -ForegroundColor Cyan
Write-Host " 有效期: $ValidYears 年" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

# ================================================================
# Step 1: 删除旧证书
# ================================================================
Write-Host "[1/5] 检查并删除旧证书..." -ForegroundColor Yellow
$oldCerts = Get-ChildItem Cert:\LocalMachine\My | Where-Object { $_.Subject -match $DnsName }
if ($oldCerts) {
    $oldCerts | Remove-Item -Force
    Write-Host "  已删除 $($oldCerts.Count) 个旧证书" -ForegroundColor Green
} else {
    Write-Host "  无旧证书" -ForegroundColor DarkGray
}

# 同时从 Root 存储删除旧的
$oldRootCerts = Get-ChildItem Cert:\LocalMachine\Root | Where-Object { $_.Subject -match $DnsName }
if ($oldRootCerts) {
    $oldRootCerts | Remove-Item -Force
}

# ================================================================
# Step 2: 生成新证书（使用 NTDS 兼容的 Provider）
# ================================================================
Write-Host "[2/5] 生成新自签名证书..." -ForegroundColor Yellow
$cert = New-SelfSignedCertificate `
    -DnsName $DnsName `
    -CertStoreLocation "Cert:\LocalMachine\My" `
    -KeySpec KeyExchange `
    -KeyLength 2048 `
    -KeyAlgorithm RSA `
    -HashAlgorithm SHA256 `
    -Provider "Microsoft RSA SChannel Cryptographic Provider" `
    -KeyUsage DigitalSignature, KeyEncipherment `
    -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.1") `
    -NotAfter (Get-Date).AddYears($ValidYears)

Write-Host "  证书指纹: $($cert.Thumbprint)" -ForegroundColor Green
Write-Host "  有效期至: $($cert.NotAfter)" -ForegroundColor Green

# ================================================================
# Step 3: 添加到受信任根存储
# ================================================================
Write-Host "[3/5] 添加证书到受信任根存储..." -ForegroundColor Yellow
$rootStore = New-Object System.Security.Cryptography.X509Certificates.X509Store("Root", "LocalMachine")
$rootStore.Open("ReadWrite")
$rootStore.Add($cert)
$rootStore.Close()
Write-Host "  已添加到 Cert:\LocalMachine\Root" -ForegroundColor Green

# ================================================================
# Step 4: 导出证书供客户端使用
# ================================================================
Write-Host "[4/5] 导出证书文件（供客户端导入）..." -ForegroundColor Yellow
$exportPath = Join-Path $PSScriptRoot "$DnsName.cer"
if (-not $PSScriptRoot) { $exportPath = "C:\$DnsName.cer" }
Export-Certificate -Cert $cert -FilePath $exportPath | Out-Null
Write-Host "  已导出到: $exportPath" -ForegroundColor Green
Write-Host "  请将此文件复制到 ImprivataProxy 主机" -ForegroundColor Yellow

# ================================================================
# Step 5: 重启 NTDS 并验证
# ================================================================
Write-Host "[5/5] 重启 NTDS 服务并验证 LDAPS..." -ForegroundColor Yellow
Restart-Service NTDS -Force
Write-Host "  NTDS 已重启，等待 5 秒..." -ForegroundColor DarkGray
Start-Sleep -Seconds 5

# 验证
try {
    $tcp = New-Object System.Net.Sockets.TcpClient("127.0.0.1", 636)
    $ssl = New-Object System.Net.Security.SslStream($tcp.GetStream(), $false, {$true})
    $ssl.AuthenticateAsClient($DnsName)
    Write-Host ""
    Write-Host "============================================" -ForegroundColor Green
    Write-Host " LDAPS 配置成功!" -ForegroundColor Green
    Write-Host " TLS: $($ssl.SslProtocol)" -ForegroundColor Green
    Write-Host " 证书: $($ssl.RemoteCertificate.Subject)" -ForegroundColor Green
    Write-Host "============================================" -ForegroundColor Green
    $ssl.Dispose()
    $tcp.Dispose()
} catch {
    Write-Host ""
    Write-Host "============================================" -ForegroundColor Red
    Write-Host " LDAPS 验证失败!" -ForegroundColor Red
    Write-Host " 错误: $_" -ForegroundColor Red
    Write-Host " 建议: 尝试重启整台域控 (Restart-Computer)" -ForegroundColor Yellow
    Write-Host "============================================" -ForegroundColor Red
}

Write-Host ""
Write-Host "下一步:" -ForegroundColor Cyan
Write-Host "  1. 将 $exportPath 复制到 ImprivataProxy 主机" -ForegroundColor White
Write-Host "  2. 在 ImprivataProxy 主机上运行 Setup-Client-LDAPS.ps1" -ForegroundColor White
