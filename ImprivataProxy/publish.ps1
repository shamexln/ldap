<#
.SYNOPSIS
    Build and package ImprivataProxy as a self-contained Windows MSI installer.

.DESCRIPTION
    This script:
    1. Builds the Vue 3 frontend (npm run build)
    2. Copies the frontend build output to wwwroot
    3. Publishes the .NET app as self-contained for win-x64
    4. Builds the WiX MSI installer

.PARAMETER SkipFrontend
    Skip the frontend build step (use existing wwwroot content).

.PARAMETER Configuration
    Build configuration. Default: Release.

.PARAMETER Version
    Product version for the MSI (e.g. "1.0.0"). Default: "1.0.0".

.EXAMPLE
    .\publish.ps1
    .\publish.ps1 -SkipFrontend -Version "1.2.0"
#>
param(
    [switch]$SkipFrontend,
    [string]$Configuration = "Release",
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$SrcDir = Join-Path $ScriptDir "src\ImprivataProxy"
$FrontendDir = Join-Path $ScriptDir "frontend"
$InstallerDir = Join-Path $ScriptDir "installer"
$PublishDir = Join-Path $InstallerDir "publish"
$OutputMsi = Join-Path $InstallerDir "ImprivataProxy_${Version}_x64_en-US.msi"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host " ImprivataProxy MSI Build" -ForegroundColor Cyan
Write-Host " Version: $Version" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# ================================================================
# Step 1: Build Vue frontend
# ================================================================
if (-not $SkipFrontend) {
    Write-Host "[1/4] Building Vue frontend..." -ForegroundColor Yellow

    if (-not (Test-Path (Join-Path $FrontendDir "node_modules"))) {
        Write-Host "  Installing npm dependencies..."
        Push-Location $FrontendDir
        npm install
        Pop-Location
    }

    Push-Location $FrontendDir
    npm run build
    Pop-Location

    # Vite already outputs directly to src/ImprivataProxy/wwwroot (configured in vite.config.ts)
    Write-Host "  Frontend build complete." -ForegroundColor Green
} else {
    Write-Host "[1/4] Skipping frontend build (using existing wwwroot)." -ForegroundColor DarkGray
}

# ================================================================
# Step 2: Publish .NET app (self-contained, win-x64)
# ================================================================
Write-Host "[2/4] Publishing .NET application (self-contained, win-x64)..." -ForegroundColor Yellow

if (Test-Path $PublishDir) {
    Remove-Item -Recurse -Force $PublishDir
}

dotnet publish $SrcDir `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained `
    --output $PublishDir `
    /p:Version=$Version `
    /p:PublishSingleFile=false

if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: dotnet publish failed!" -ForegroundColor Red
    exit 1
}

Write-Host "  Published to: $PublishDir" -ForegroundColor Green

# ================================================================
# Step 3: Verify publish output
# ================================================================
Write-Host "[3/4] Verifying publish output..." -ForegroundColor Yellow

$requiredFiles = @(
    "ImprivataProxy.exe",
    "ImprivataProxy.dll",
    "appsettings.json",
    "web.config"
)

foreach ($file in $requiredFiles) {
    $filePath = Join-Path $PublishDir $file
    if (-not (Test-Path $filePath)) {
        Write-Host "  WARNING: Expected file not found: $file" -ForegroundColor Red
    }
}

$fileCount = (Get-ChildItem -Recurse $PublishDir -File).Count
Write-Host "  Total files in publish output: $fileCount" -ForegroundColor Green

# ================================================================
# Step 3.5: Generate Files.wxs from publish output
# ================================================================
Write-Host "  Generating Files.wxs from publish output..." -ForegroundColor Yellow

$filesWxs = Join-Path $InstallerDir "Files.wxs"
$excludeFiles = @("ImprivataProxy.exe", "appsettings.json")

$xmlLines = @()
$xmlLines += '<?xml version="1.0" encoding="UTF-8"?>'
$xmlLines += '<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">'
$xmlLines += '  <Fragment>'
$xmlLines += '    <ComponentGroup Id="PublishFiles" Directory="INSTALLFOLDER">'

$componentIndex = 0
$allFiles = Get-ChildItem -Recurse $PublishDir -File | Where-Object {
    $excludeFiles -notcontains $_.Name
}

foreach ($file in $allFiles) {
    $relativePath = $file.FullName.Substring($PublishDir.Length + 1)
    $sourceAttr = "publish\$relativePath"
    $componentId = "comp_$componentIndex"
    $subDir = Split-Path $relativePath -Parent
    if ($subDir) {
        $xmlLines += "      <Component Id=`"$componentId`" Guid=`"*`" Subdirectory=`"$subDir`">"
    } else {
        $xmlLines += "      <Component Id=`"$componentId`" Guid=`"*`">"
    }
    $xmlLines += "        <File Source=`"$sourceAttr`" />"
    $xmlLines += "      </Component>"
    $componentIndex++
}

$xmlLines += '    </ComponentGroup>'
$xmlLines += '  </Fragment>'
$xmlLines += '</Wix>'

$xmlLines -join "`r`n" | Set-Content $filesWxs -Encoding UTF8
Write-Host "  Generated $componentIndex components in Files.wxs" -ForegroundColor Green

# ================================================================
# Step 3.6: Ensure PowerShell scripts use UTF-8 BOM encoding
#           (PowerShell 5.x misreads non-BOM UTF-8 with CJK characters)
# ================================================================
Write-Host "  Ensuring PS1 scripts are UTF-8 with BOM..." -ForegroundColor Yellow
$utf8Bom = New-Object System.Text.UTF8Encoding $true
$ps1Files = @(
    (Join-Path $InstallerDir "ApplyConfig.ps1"),
    (Join-Path $InstallerDir "Setup-DC-LDAPS.ps1"),
    (Join-Path $InstallerDir "Setup-Client-LDAPS.ps1")
)
foreach ($ps1 in $ps1Files) {
    if (Test-Path $ps1) {
        $content = [System.IO.File]::ReadAllText($ps1)
        [System.IO.File]::WriteAllText($ps1, $content, $utf8Bom)
    }
}
Write-Host "  Done." -ForegroundColor Green

# ================================================================
# Step 4: Build WiX MSI
# ================================================================
Write-Host "[4/4] Building WiX MSI installer..." -ForegroundColor Yellow

# Build the MSI using dotnet build (supports Files glob patterns)
dotnet build `
    (Join-Path $InstallerDir "ImprivataProxy.Installer.wixproj") `
    -c Release `
    -o $InstallerDir

if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: WiX build failed!" -ForegroundColor Red
    exit 1
}

# Find the generated MSI
$generatedMsi = Get-ChildItem -Path $InstallerDir -Filter "*.msi" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($generatedMsi) {
    $OutputMsi = $generatedMsi.FullName
}

# ================================================================
# Done
# ================================================================
Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host " BUILD SUCCESSFUL" -ForegroundColor Green
Write-Host " MSI: $OutputMsi" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "To install:" -ForegroundColor Cyan
Write-Host "  msiexec /i `"$OutputMsi`"" -ForegroundColor White
Write-Host ""
Write-Host "To install silently:" -ForegroundColor Cyan
Write-Host "  msiexec /i `"$OutputMsi`" /qn LISTEN_PORT=80 AD_SVC_PASSWORD=xxx ADMIN_PASSWORD=xxx LDAP_URL=ldap://dc:389 LDAP_BASE_DN=DC=example,DC=com LDAP_SERVICE_DN=CN=svc,CN=Users,DC=example,DC=com" -ForegroundColor White
