<#
.SYNOPSIS
    Interactive tool to configure LDAPS (port 636) on a Domain Controller.

.DESCRIPTION
    Provides an interactive menu to:
    1. Check existing certificates
    2. Generate a new self-signed certificate (NTDS-compatible)
    3. Add certificate to Trusted Root store
    4. Export certificate for client import
    5. Restart NTDS service
    6. Verify LDAPS connectivity
    7. Run all steps sequentially

.PARAMETER DnsName
    FQDN used by clients to connect (e.g. "ldaps.chrliege.be").

.PARAMETER ValidYears
    Certificate validity in years. Default: 5.

.PARAMETER NoDcHostName
    Skip adding the DC's real hostname to the certificate SAN.

.EXAMPLE
    .\Setup-DC-LDAPS.ps1 -DnsName "ldaps.chrliege.be"
#>
param(
    [Parameter(Mandatory)]
    [string]$DnsName,

    [int]$ValidYears = 5,

    [switch]$NoDcHostName
)

$ErrorActionPreference = "Stop"

# ================================================================
# Detect DC hostname
# ================================================================
$dcFqdn = "$($env:COMPUTERNAME).$($env:USERDNSDOMAIN)".ToLower()
if (-not $env:USERDNSDOMAIN) {
    $domainReg = (Get-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters" -Name Domain -ErrorAction SilentlyContinue).Domain
    if ($domainReg) {
        $dcFqdn = "$($env:COMPUTERNAME).$domainReg".ToLower()
    }
    else {
        $dcFqdn = ""
    }
}

# Build DNS name list for the certificate
$dnsNames = @($DnsName)
if ((-not $NoDcHostName) -and $dcFqdn -and ($dcFqdn -ne $DnsName.ToLower())) {
    $dnsNames = @($dcFqdn, $DnsName)
}

# Shared state
$script:cert = $null
$script:exportPath = $null

# ================================================================
# Function definitions
# ================================================================

function Show-Banner {
    Write-Host ""
    Write-Host "============================================" -ForegroundColor Cyan
    Write-Host " DC LDAPS Certificate Configuration" -ForegroundColor Cyan
    Write-Host " DnsName:      $DnsName" -ForegroundColor Cyan
    Write-Host " DC Hostname:  $dcFqdn" -ForegroundColor Cyan
    Write-Host " Cert SANs:    $($dnsNames -join ', ')" -ForegroundColor Cyan
    Write-Host " Valid for:    $ValidYears years" -ForegroundColor Cyan
    Write-Host "============================================" -ForegroundColor Cyan
}

function Show-Menu {
    Write-Host ""
    Write-Host "  [1] Check existing certificates" -ForegroundColor White
    Write-Host "  [2] Generate new self-signed certificate" -ForegroundColor White
    Write-Host "  [3] Add certificate to Trusted Root store" -ForegroundColor White
    Write-Host "  [4] Export certificate for client import" -ForegroundColor White
    Write-Host "  [5] Restart NTDS service" -ForegroundColor White
    Write-Host "  [6] Verify LDAPS connectivity" -ForegroundColor White
    Write-Host "  [A] Run ALL steps (1-6)" -ForegroundColor Yellow
    Write-Host "  [Q] Exit" -ForegroundColor DarkGray
    Write-Host ""
}

function Step-CheckCertificates {
    Write-Host ""
    Write-Host "[1] Checking existing certificates..." -ForegroundColor Yellow
    $oldCerts = Get-ChildItem Cert:\LocalMachine\My | Where-Object { $_.Subject -match $DnsName }
    if ($oldCerts) {
        Write-Host "  Personal store: Found $($oldCerts.Count) certificate(s)" -ForegroundColor Green
        $oldCerts | ForEach-Object {
            Write-Host "    - $($_.Subject) | Expires: $($_.NotAfter) | Thumbprint: $($_.Thumbprint)" -ForegroundColor DarkGray
        }
    }
    else {
        Write-Host "  Personal store: No certificates matching '$DnsName'" -ForegroundColor DarkGray
    }

    $oldRootCerts = Get-ChildItem Cert:\LocalMachine\Root | Where-Object { $_.Subject -match $DnsName }
    if ($oldRootCerts) {
        Write-Host "  Root store: Found $($oldRootCerts.Count) certificate(s)" -ForegroundColor Green
    }
    else {
        Write-Host "  Root store: No certificates matching '$DnsName'" -ForegroundColor DarkGray
    }
    Write-Host "  Done." -ForegroundColor Green
}

function Step-GenerateCertificate {
    Write-Host ""
    Write-Host "[2] Generating new self-signed certificate..." -ForegroundColor Yellow
    Write-Host "  DNS names: $($dnsNames -join ', ')" -ForegroundColor DarkGray
    $script:cert = New-SelfSignedCertificate `
        -DnsName $dnsNames `
        -CertStoreLocation "Cert:\LocalMachine\My" `
        -KeySpec KeyExchange `
        -KeyLength 2048 `
        -KeyAlgorithm RSA `
        -HashAlgorithm SHA256 `
        -Provider "Microsoft RSA SChannel Cryptographic Provider" `
        -KeyUsage DigitalSignature, KeyEncipherment `
        -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.1") `
        -NotAfter (Get-Date).AddYears($ValidYears)

    Write-Host "  Thumbprint: $($script:cert.Thumbprint)" -ForegroundColor Green
    Write-Host "  Subject:    $($script:cert.Subject)" -ForegroundColor Green
    Write-Host "  Valid until: $($script:cert.NotAfter)" -ForegroundColor Green
}

function Step-AddToRootStore {
    Write-Host ""
    Write-Host "[3] Adding certificate to Trusted Root store..." -ForegroundColor Yellow
    if (-not $script:cert) {
        # Try to find the most recent matching cert
        $script:cert = Get-ChildItem Cert:\LocalMachine\My | Where-Object { $_.Subject -match $DnsName } |
            Sort-Object NotAfter -Descending | Select-Object -First 1
        if (-not $script:cert) {
            Write-Host "  ERROR: No certificate found. Run step [2] first." -ForegroundColor Red
            return
        }
        Write-Host "  Using existing cert: $($script:cert.Thumbprint)" -ForegroundColor DarkGray
    }
    $rootStore = New-Object System.Security.Cryptography.X509Certificates.X509Store("Root", "LocalMachine")
    $rootStore.Open("ReadWrite")
    $rootStore.Add($script:cert)
    $rootStore.Close()
    Write-Host "  Added to Cert:\LocalMachine\Root" -ForegroundColor Green
}

function Step-ExportCertificate {
    Write-Host ""
    Write-Host "[4] Exporting certificate file..." -ForegroundColor Yellow
    if (-not $script:cert) {
        $script:cert = Get-ChildItem Cert:\LocalMachine\My | Where-Object { $_.Subject -match $DnsName } |
            Sort-Object NotAfter -Descending | Select-Object -First 1
        if (-not $script:cert) {
            Write-Host "  ERROR: No certificate found. Run step [2] first." -ForegroundColor Red
            return
        }
    }
    $script:exportPath = Join-Path $PSScriptRoot "$DnsName.cer"
    if (-not $PSScriptRoot) {
        $script:exportPath = "C:\$DnsName.cer"
    }
    Export-Certificate -Cert $script:cert -FilePath $script:exportPath | Out-Null
    Write-Host "  Exported to: $($script:exportPath)" -ForegroundColor Green
    Write-Host "  Please copy this file to the ImprivataProxy host" -ForegroundColor Yellow
}

function Step-RestartNTDS {
    Write-Host ""
    Write-Host "[5] Restarting NTDS service..." -ForegroundColor Yellow
    Restart-Service NTDS -Force
    Write-Host "  NTDS restarted, waiting 5 seconds..." -ForegroundColor DarkGray
    Start-Sleep -Seconds 5
    Write-Host "  Done." -ForegroundColor Green
}

function Step-VerifyLDAPS {
    Write-Host ""
    Write-Host "[6] Verifying LDAPS connectivity (localhost:636)..." -ForegroundColor Yellow
    try {
        $tcp = New-Object System.Net.Sockets.TcpClient("127.0.0.1", 636)
        $callback = [System.Net.Security.RemoteCertificateValidationCallback]{ $true }
        $ssl = New-Object System.Net.Security.SslStream($tcp.GetStream(), $false, $callback)
        $ssl.AuthenticateAsClient($DnsName)
        Write-Host ""
        Write-Host "  LDAPS connection successful!" -ForegroundColor Green
        Write-Host "  TLS version: $($ssl.SslProtocol)" -ForegroundColor Green
        Write-Host "  Certificate: $($ssl.RemoteCertificate.Subject)" -ForegroundColor Green
        $ssl.Dispose()
        $tcp.Dispose()
    }
    catch {
        Write-Host ""
        Write-Host "  LDAPS verification FAILED!" -ForegroundColor Red
        Write-Host "  Error: $_" -ForegroundColor Red
        Write-Host ""
        Write-Host "  Troubleshooting:" -ForegroundColor Yellow
        Write-Host "    1. Try restarting the entire DC: Restart-Computer" -ForegroundColor White
        Write-Host "    2. Verify certificate: certutil -store My $DnsName" -ForegroundColor White
        Write-Host "    3. Check DC hostname matches cert SAN: $dcFqdn" -ForegroundColor White
    }
}

function Step-RunAll {
    Step-CheckCertificates
    Step-GenerateCertificate
    Step-AddToRootStore
    Step-ExportCertificate
    Step-RestartNTDS
    Step-VerifyLDAPS
    Write-Host ""
    Write-Host "All steps completed." -ForegroundColor Green
    if ($script:exportPath) {
        Write-Host ""
        Write-Host "Next steps:" -ForegroundColor Cyan
        Write-Host "  1. Copy $($script:exportPath) to the ImprivataProxy host" -ForegroundColor White
        Write-Host "  2. Run Setup-Client-LDAPS.ps1 on the ImprivataProxy host" -ForegroundColor White
    }
}

# ================================================================
# Main loop
# ================================================================
Show-Banner

while ($true) {
    Show-Menu
    $choice = Read-Host "Select an option"

    switch ($choice.ToUpper()) {
        "1" { Step-CheckCertificates }
        "2" { Step-GenerateCertificate }
        "3" { Step-AddToRootStore }
        "4" { Step-ExportCertificate }
        "5" { Step-RestartNTDS }
        "6" { Step-VerifyLDAPS }
        "A" { Step-RunAll }
        "Q" {
            Write-Host ""
            Write-Host "Exiting." -ForegroundColor DarkGray
            return
        }
        default {
            Write-Host "  Invalid option. Please enter 1-6, A, or Q." -ForegroundColor Red
        }
    }
}
