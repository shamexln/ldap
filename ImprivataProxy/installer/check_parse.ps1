$tokens = $null
$errors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile(
    'D:\project\ldap\ImprivataProxy\installer\Setup-Client-LDAPS.ps1',
    [ref]$tokens, [ref]$errors)

if ($errors.Count -gt 0) {
    Write-Host "PARSE ERRORS:" -ForegroundColor Red
    foreach ($err in $errors) {
        Write-Host "  Line $($err.Extent.StartLineNumber): $($err.Message)"
    }
} else {
    Write-Host "No parse errors - file is valid" -ForegroundColor Green
}
