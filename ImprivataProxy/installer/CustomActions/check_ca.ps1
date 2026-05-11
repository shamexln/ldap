$dll = [System.Reflection.Assembly]::LoadFrom('D:\project\ldap\ImprivataProxy\installer\CustomActions\bin\Release\net472\DetectDomainCA.dll')
Write-Host "Types found:"
foreach ($t in $dll.GetTypes()) {
    Write-Host "  Type: $($t.FullName)"
    foreach ($m in $t.GetMethods([System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Static)) {
        $attrs = $m.GetCustomAttributes($true)
        foreach ($a in $attrs) {
            Write-Host "    Method: $($m.Name) has attribute: $($a.GetType().FullName)"
        }
    }
}

Write-Host ""
Write-Host "Checking WixToolset.Dtf.WindowsInstaller.dll in bin:"
$wiDll = "D:\project\ldap\ImprivataProxy\installer\CustomActions\bin\Release\net472\WixToolset.Dtf.WindowsInstaller.dll"
if (Test-Path $wiDll) {
    Write-Host "  EXISTS: $wiDll"
} else {
    Write-Host "  NOT FOUND: $wiDll"
}

Write-Host ""
Write-Host "Files in obj\Release\net472:"
Get-ChildItem 'D:\project\ldap\ImprivataProxy\installer\CustomActions\obj\Release\net472\' | ForEach-Object { Write-Host "  $($_.Name) ($($_.Length) bytes)" }
