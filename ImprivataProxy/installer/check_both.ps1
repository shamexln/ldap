$scripts = @(
    'D:\project\ldap\ImprivataProxy\installer\Setup-DC-LDAPS.ps1',
    'D:\project\ldap\ImprivataProxy\installer\Setup-Client-LDAPS.ps1'
)

foreach ($script in $scripts) {
    $tokens = $null
    $errors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile(
        $script, [ref]$tokens, [ref]$errors)

    Write-Host "=== $script ===" -ForegroundColor Cyan
    if ($errors.Count -gt 0) {
        Write-Host "PARSE ERRORS:" -ForegroundColor Red
        foreach ($err in $errors) {
            Write-Host "  Line $($err.Extent.StartLineNumber): $($err.Message)"
        }
    }
    else {
        Write-Host "No parse errors - file is valid" -ForegroundColor Green
    }
    Write-Host ""
}
