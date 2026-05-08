$utf8Bom = New-Object System.Text.UTF8Encoding $true
$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
$files = @("Setup-DC-LDAPS.ps1", "Setup-Client-LDAPS.ps1", "ApplyConfig.ps1")
foreach ($f in $files) {
    $path = Join-Path $dir $f
    if (Test-Path $path) {
        $content = [System.IO.File]::ReadAllText($path)
        [System.IO.File]::WriteAllText($path, $content, $utf8Bom)
        Write-Host "UTF-8 BOM added: $f"
    }
}
