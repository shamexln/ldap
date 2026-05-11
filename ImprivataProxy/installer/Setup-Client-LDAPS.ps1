<#
.SYNOPSIS
    Interactive tool to configure LDAPS certificate trust on the ImprivataProxy host.

.DESCRIPTION
    Provides an interactive menu to:
    1. Verify DNS resolution
    2. Check certificate file
    3. Import certificate into Trusted Root store
    4. Verify LDAPS connection
    5. Check ImprivataProxy service status
    6. Run all steps sequentially

.PARAMETER CertFile
    Path to the certificate file (.cer) exported from the domain controller.

.PARAMETER LdapHost
    FQDN of the domain controller (must match the DnsName in the certificate).

.PARAMETER LdapPort
    LDAPS port, default 636.

.EXAMPLE
    .\Setup-Client-LDAPS.ps1 -CertFile "C:\ldaps.chrliege.be.cer" -LdapHost "ldaps.chrliege.be"
#>
param(
    [Parameter(Mandatory)]
    [string]$CertFile,

    [Parameter(Mandatory)]
    [string]$LdapHost,

    [int]$LdapPort = 636
)

$ErrorActionPreference = "Stop"

# ================================================================
# Function definitions
# ================================================================

function Show-Banner {
    Write-Host ""
    Write-Host "============================================" -ForegroundColor Cyan
    Write-Host " ImprivataProxy Host LDAPS Configuration" -ForegroundColor Cyan
    Write-Host " Certificate: $CertFile" -ForegroundColor Cyan
    Write-Host " LDAP Host:   ${LdapHost}:${LdapPort}" -ForegroundColor Cyan
    Write-Host "============================================" -ForegroundColor Cyan
}

function Show-Menu {
    Write-Host ""
    Write-Host "  [1] Verify DNS resolution" -ForegroundColor White
    Write-Host "  [2] Check certificate file" -ForegroundColor White
    Write-Host "  [3] Import certificate into Trusted Root store" -ForegroundColor White
    Write-Host "  [4] Verify LDAPS connection" -ForegroundColor White
    Write-Host "  [5] Check ImprivataProxy service status" -ForegroundColor White
    Write-Host "  [A] Run ALL steps (1-5)" -ForegroundColor Yellow
    Write-Host "  [Q] Exit" -ForegroundColor DarkGray
    Write-Host ""
}

function Step-VerifyDns {
    Write-Host ""
    Write-Host "[1] Checking DNS resolution for $LdapHost..." -ForegroundColor Yellow
    try {
        $resolved = [System.Net.Dns]::GetHostAddresses($LdapHost)
        Write-Host "  $LdapHost -> $($resolved[0])" -ForegroundColor Green
    }
    catch {
        Write-Host "  ERROR: Cannot resolve hostname $LdapHost" -ForegroundColor Red
        Write-Host ""
        Write-Host "  Solutions (choose one):" -ForegroundColor Yellow
        Write-Host "    1. Point DNS to DC:" -ForegroundColor White
        Write-Host "       Set-DnsClientServerAddress -InterfaceAlias Ethernet0 -ServerAddresses DC_IP,8.8.8.8" -ForegroundColor DarkGray
        Write-Host "    2. Add hosts entry:" -ForegroundColor White
        Write-Host "       Add-Content C:\Windows\System32\drivers\etc\hosts 'DC_IP $LdapHost'" -ForegroundColor DarkGray
    }
}

function Step-CheckCertFile {
    Write-Host ""
    Write-Host "[2] Checking certificate file..." -ForegroundColor Yellow
    if (-not (Test-Path $CertFile)) {
        Write-Host "  ERROR: Certificate file not found: $CertFile" -ForegroundColor Red
        Write-Host "  Please copy the certificate file from the DC first." -ForegroundColor Yellow
        return $false
    }
    $certInfo = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($CertFile)
    Write-Host "  File exists: $CertFile" -ForegroundColor Green
    Write-Host "  Subject:     $($certInfo.Subject)" -ForegroundColor Green
    Write-Host "  Expires:     $($certInfo.NotAfter)" -ForegroundColor Green
    Write-Host "  Thumbprint:  $($certInfo.Thumbprint)" -ForegroundColor Green
    return $true
}

function Step-ImportCertificate {
    Write-Host ""
    Write-Host "[3] Importing certificate into Trusted Root store..." -ForegroundColor Yellow
    if (-not (Test-Path $CertFile)) {
        Write-Host "  ERROR: Certificate file not found: $CertFile" -ForegroundColor Red
        Write-Host "  Run step [2] to verify the file first." -ForegroundColor Yellow
        return
    }

    # Remove old certificates with the same subject
    $oldCerts = Get-ChildItem Cert:\LocalMachine\Root | Where-Object { $_.Subject -match $LdapHost }
    if ($oldCerts) {
        $oldCerts | Remove-Item -Force
        Write-Host "  Removed $($oldCerts.Count) old certificate(s)" -ForegroundColor DarkGray
    }

    $imported = Import-Certificate -FilePath $CertFile -CertStoreLocation "Cert:\LocalMachine\Root"
    Write-Host "  Imported: $($imported.Subject)" -ForegroundColor Green
    Write-Host "  Thumbprint: $($imported.Thumbprint)" -ForegroundColor Green
}

function Step-VerifyLdaps {
    Write-Host ""
    Write-Host "[4] Verifying LDAPS connection to ${LdapHost}:${LdapPort}..." -ForegroundColor Yellow
    try {
        $tcp = New-Object System.Net.Sockets.TcpClient($LdapHost, $LdapPort)
        $callback = [System.Net.Security.RemoteCertificateValidationCallback]{ $true }
        $ssl = New-Object System.Net.Security.SslStream($tcp.GetStream(), $false, $callback)
        $ssl.AuthenticateAsClient($LdapHost)
        Write-Host ""
        Write-Host "  LDAPS connection successful!" -ForegroundColor Green
        Write-Host "  TLS version: $($ssl.SslProtocol)" -ForegroundColor Green
        Write-Host "  Server cert: $($ssl.RemoteCertificate.Subject)" -ForegroundColor Green
        $ssl.Dispose()
        $tcp.Dispose()
    }
    catch {
        Write-Host ""
        Write-Host "  LDAPS connection FAILED!" -ForegroundColor Red
        Write-Host "  Error: $_" -ForegroundColor Red
        Write-Host ""
        Write-Host "  Troubleshooting:" -ForegroundColor Yellow
        Write-Host "    1. Ensure LDAPS is enabled on DC (run Setup-DC-LDAPS.ps1 on the DC)" -ForegroundColor White
        Write-Host "    2. Check connectivity: Test-NetConnection $LdapHost -Port $LdapPort" -ForegroundColor White
        Write-Host "    3. Ensure firewall is not blocking port $LdapPort" -ForegroundColor White
    }
}

function Step-CheckService {
    Write-Host ""
    Write-Host "[5] Checking ImprivataProxy service..." -ForegroundColor Yellow
    $svc = Get-Service -Name "ImprivataProxy" -ErrorAction SilentlyContinue
    if ($svc) {
        Write-Host "  Service found: ImprivataProxy" -ForegroundColor Green
        Write-Host "  Status: $($svc.Status)" -ForegroundColor Green
        if ($svc.Status -eq "Running") {
            Write-Host "  Tip: Restart service to load new config:" -ForegroundColor Yellow
            Write-Host "       Restart-Service ImprivataProxy" -ForegroundColor DarkGray
        }
    }
    else {
        Write-Host "  ImprivataProxy service not installed." -ForegroundColor DarkGray
        Write-Host "  Please run the MSI installer first." -ForegroundColor Yellow
    }

    Write-Host ""
    Write-Host "  Recommended appsettings.json:" -ForegroundColor Cyan
    Write-Host "    ""LdapUrl"": ""ldaps://${LdapHost}:${LdapPort}""" -ForegroundColor White
}

function Step-RunAll {
    Step-VerifyDns
    Step-CheckCertFile
    Step-ImportCertificate
    Step-VerifyLdaps
    Step-CheckService
    Write-Host ""
    Write-Host "All steps completed." -ForegroundColor Green
}

# ================================================================
# Main loop
# ================================================================
Show-Banner

while ($true) {
    Show-Menu
    $choice = Read-Host "Select an option"

    switch ($choice.ToUpper()) {
        "1" { Step-VerifyDns }
        "2" { Step-CheckCertFile | Out-Null }
        "3" { Step-ImportCertificate }
        "4" { Step-VerifyLdaps }
        "5" { Step-CheckService }
        "A" { Step-RunAll }
        "Q" {
            Write-Host ""
            Write-Host "Exiting." -ForegroundColor DarkGray
            return
        }
        default {
            Write-Host "  Invalid option. Please enter 1-5, A, or Q." -ForegroundColor Red
        }
    }
}
