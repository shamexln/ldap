<#
.SYNOPSIS
    Applies installer configuration to appsettings.json.
    Called by the MSI installer custom action after file deployment.
#>
param(
    [string]$ListenPort = "80",
    [string]$LdapUrl = "",
    [string]$BaseDn = "",
    [string]$ServiceDn = ""
)

$ErrorActionPreference = "Stop"
$InstallDir = $PSScriptRoot
$settingsPath = Join-Path $InstallDir "appsettings.json"

if (-not (Test-Path $settingsPath)) {
    Write-Error "appsettings.json not found at: $settingsPath"
    exit 1
}

$json = Get-Content $settingsPath -Raw | ConvertFrom-Json

# Apply listen port
if ($ListenPort) {
    $json.Proxy.ListenPort = [int]$ListenPort
    $json.Kestrel.Endpoints.Http.Url = "http://0.0.0.0:$ListenPort"
}

# Apply LDAP configuration
if ($LdapUrl) {
    $json.Ad.LdapUrl = $LdapUrl
}
if ($BaseDn) {
    $json.Ad.BaseDn = $BaseDn
}
if ($ServiceDn) {
    $json.Ad.ServiceAccountDn = $ServiceDn
}

# Update relative paths to absolute paths based on install directory
$dataPath = Join-Path $InstallDir "data\proxy.db"
$certsPath = Join-Path $InstallDir "certs\ticket-signing.pem"
$logsPath = Join-Path $InstallDir "logs\proxy-.log"
$json.Database.ConnectionString = "Data Source=$dataPath"
$json.Ticket.SigningKeyPath = $certsPath

# Update log path
$json.Serilog.WriteTo[1].Args.path = $logsPath

# Write back
$json | ConvertTo-Json -Depth 10 | Set-Content $settingsPath -Encoding UTF8
