# ImprivataProxy - Installation and Usage Guide (IFU)

## System Requirements

- Windows 10 / Windows Server 2016 or later (64-bit)
- At least 200MB free disk space
- Network access (HTTP port, default 80)
- Active Directory service account (for LDAP synchronization)
- Administrator privileges (required for installing and running Windows services)

## Installation

### Option 1: GUI Installation

1. Double-click the `ImprivataProxy-x.x.x.msi` installer
2. Follow the installation wizard to complete the configuration:
   - **Installation directory**: Default is `C:\Program Files\ImprivataProxy\`
   - **LDAP Configuration**:
     - LDAP Server URL (e.g., `ldap://192.168.1.100:389`)
     - Base DN (e.g., `DC=example,DC=com`)
     - Service Account DN (e.g., `CN=svc_imprivata,CN=Users,DC=example,DC=com`)
     - Service Account Password
   - **Service Configuration**:
     - HTTP listen port (default `80`)
     - Admin panel password
3. Click **Install** to begin installation
4. Click **Finish** when complete

### Option 2: Silent Installation (Command Line)

Run PowerShell or CMD as Administrator:

```powershell
msiexec /i "ImprivataProxy-1.0.0.msi" /qn ^
    LISTEN_PORT=80 ^
    LDAP_URL="ldap://192.168.1.100:389" ^
    LDAP_BASE_DN="DC=example,DC=com" ^
    LDAP_SERVICE_DN="CN=svc_imprivata,CN=Users,DC=example,DC=com" ^
    AD_SVC_PASSWORD="your_ad_password" ^
    ADMIN_PASSWORD="your_admin_password"
```

## Directory Structure After Installation

```
C:\Program Files\ImprivataProxy\
├── ImprivataProxy.exe          # Service executable
├── appsettings.json            # Configuration file (manually editable)
├── web.config                  # IIS integration configuration
├── wwwroot\                    # Admin UI frontend files
├── data\                       # SQLite database storage
│   └── proxy.db
├── logs\                       # Log files (daily rolling)
│   └── proxy-20260507.log
├── certs\                      # Certificate files
│   └── ticket-signing.pem
└── [.NET runtime files]
```

## Starting the Service

### Automatic Startup

After installation, the service is configured to **start automatically** and will run after Windows restarts.

### Manual Start/Stop

**Option 1: Windows Services Manager**

1. Press `Win + R`, type `services.msc`, press Enter
2. Locate **Imprivata Proxy Service**
3. Right-click and select **Start** / **Stop** / **Restart**

**Option 2: Command Line (Administrator)**

```powershell
# Start service
sc start ImprivataProxy

# Stop service
sc stop ImprivataProxy

# Query service status
sc query ImprivataProxy

# Restart service
sc stop ImprivataProxy && sc start ImprivataProxy
```

**Option 3: PowerShell (Administrator)**

```powershell
# Start
Start-Service ImprivataProxy

# Stop
Stop-Service ImprivataProxy

# Restart
Restart-Service ImprivataProxy

# Check status
Get-Service ImprivataProxy
```

## Verifying Installation

After installing and starting the service, perform the following checks:

1. **Check service status**:
   ```powershell
   sc query ImprivataProxy
   ```
   Should display `STATE: RUNNING`

2. **Check health endpoint**:
   ```powershell
   curl http://localhost/health
   ```
   Should return: `{"status":"healthy","timestamp":"..."}`

3. **Access admin UI**:
   Open a browser and navigate to `http://localhost`, log in with the admin password set during installation

## Configuration

To modify configuration after installation, edit the following file:

```
C:\Program Files\ImprivataProxy\appsettings.json
```

Restart the service after making changes:

```powershell
Restart-Service ImprivataProxy
```

### Common Configuration Options

| Option | Path | Description |
|--------|------|-------------|
| Listen port | `Kestrel.Endpoints.Http.Url` | HTTP listen address and port |
| LDAP address | `Ad.LdapUrl` | AD server address |
| LDAP Base DN | `Ad.BaseDn` | Search root directory |
| Sync interval | `Ad.SyncIntervalMinutes` | AD user sync interval (minutes) |
| Password lockout attempts | `AuthPolicy.PwdMaxFails` | Lock account after consecutive failures |
| Lockout duration | `AuthPolicy.PwdLockoutMinutes` | Lockout duration (minutes) |

### Environment Variables

The following sensitive values are configured as service environment variables (set during installation):

| Variable | Description |
|----------|-------------|
| `AD_SVC_PASSWORD` | AD service account password |
| `ADMIN_PASSWORD` | Admin panel login password |

To modify environment variables:

```powershell
# Modify service environment variables in the registry
$regPath = "HKLM:\SYSTEM\CurrentControlSet\Services\ImprivataProxy"
$env = @("AD_SVC_PASSWORD=new_password", "ADMIN_PASSWORD=new_admin_pwd", "ASPNETCORE_ENVIRONMENT=Production")
Set-ItemProperty -Path $regPath -Name "Environment" -Value $env -Type MultiString

# Restart service for changes to take effect
Restart-Service ImprivataProxy
```

## Viewing Logs

Log files are located in the `logs\` folder under the installation directory, with daily rolling:

```powershell
# View today's log
Get-Content "C:\Program Files\ImprivataProxy\logs\proxy-20260507.log" -Tail 50

# Follow log in real-time
Get-Content "C:\Program Files\ImprivataProxy\logs\proxy-20260507.log" -Wait
```

## Uninstallation

### Option 1: Control Panel

1. Open **Settings > Apps > Installed apps**
2. Find **Imprivata Proxy Service**
3. Click **Uninstall**

### Option 2: Command Line

```powershell
msiexec /x "ImprivataProxy-1.0.0.msi" /qn
```

> **Note**: After uninstallation, the `data\` and `logs\` directories are preserved and user data is not deleted. To completely clean up, manually delete the installation directory.

## LDAPS Certificate Configuration (Encrypted Communication)

Communication between ImprivataProxy and Active Directory should use LDAPS (port 636) for encrypted transport. Configuration involves two steps: first generate and enable the certificate on the domain controller, then import the certificate on the ImprivataProxy host to establish trust.

### Prerequisites

- Administrator privileges on the domain controller
- Administrator privileges on the ImprivataProxy host
- Network connectivity between both machines (port 636)

### Step 1: Domain Controller Configuration

Run PowerShell as Administrator on the domain controller and execute `Setup-DC-LDAPS.ps1`:

```powershell
.\Setup-DC-LDAPS.ps1 -DnsName "sso.ad.vista.com"
```

Parameters:
| Parameter | Description | Example |
|-----------|-------------|---------|
| `-DnsName` | FQDN of the domain controller, must match the hostname used by the client | `sso.ad.vista.com` |
| `-ValidYears` | Certificate validity period (years), default 5 | `-ValidYears 10` |

The script automatically performs:
1. Removes old certificates with the same name
2. Generates a self-signed certificate using an NTDS-compatible Provider
3. Adds the certificate to the Trusted Root store (required for self-signed certificates)
4. Restarts the NTDS service to load the certificate
5. Verifies that LDAPS is working
6. Exports the `.cer` certificate file (for client use)

Successful output:
```
============================================
 LDAPS configuration successful!
 TLS: Tls12
 Certificate: CN=sso.ad.vista.com
============================================

Next steps:
  1. Copy sso.ad.vista.com.cer to the ImprivataProxy host
  2. Run Setup-Client-LDAPS.ps1 on the ImprivataProxy host
```

### Step 2: Copy Certificate File

Copy the `.cer` file generated on the domain controller to the ImprivataProxy host. The file is located in the script's execution directory as `<DnsName>.cer` (e.g., `sso.ad.vista.com.cer`).

### Step 3: ImprivataProxy Host Configuration

> **Prerequisite**: The ImprivataProxy host must be able to resolve the domain controller hostname. If DNS does not point to the DC, add a hosts record first (see "Troubleshooting" section).

Run PowerShell as Administrator on the ImprivataProxy host and execute `Setup-Client-LDAPS.ps1`:

```powershell
.\Setup-Client-LDAPS.ps1 -CertFile "C:\sso.ad.vista.com.cer" -LdapHost "sso.ad.vista.com"
```

> **Note**: If you get "running scripts is disabled", use:
> ```powershell
> powershell -ExecutionPolicy Bypass -File ".\Setup-Client-LDAPS.ps1" -CertFile "C:\sso.ad.vista.com.cer" -LdapHost "sso.ad.vista.com"
> ```

Parameters:
| Parameter | Description | Example |
|-----------|-------------|---------|
| `-CertFile` | Path to the `.cer` certificate file copied from the domain controller | `C:\sso.ad.vista.com.cer` |
| `-LdapHost` | FQDN of the domain controller (must match certificate) | `sso.ad.vista.com` |
| `-LdapPort` | LDAPS port, default 636 | `-LdapPort 636` |

The script automatically performs:
1. Imports the certificate to the local Trusted Root store
2. Verifies the LDAPS TLS connection

Successful output:
```
============================================
 LDAPS connection successful!
 TLS version: Tls12
 Server certificate: CN=sso.ad.vista.com
============================================

ImprivataProxy appsettings.json should be configured as:
  "LdapUrl": "ldaps://sso.ad.vista.com:636"
```

### Step 4: Update ImprivataProxy Configuration

After confirming the LDAPS connection is successful, edit `appsettings.json` to use LDAPS:

```json
"Ad": {
    "LdapUrl": "ldaps://sso.ad.vista.com:636",
    "BaseDn": "DC=ad,DC=vista,DC=com",
    "ServiceAccountDn": "CN=Administrator,CN=Users,DC=ad,DC=vista,DC=com",
    ...
}
```

Restart the service after making changes:

```powershell
Restart-Service ImprivataProxy
```

### Important Notes

- The certificate's `DnsName` must exactly match the hostname in `LdapUrl` in `appsettings.json`
- When the self-signed certificate expires, re-run the above process
- If using certificates issued by an enterprise CA or public CA, these scripts are not needed — just ensure the certificate chain is trusted
- The `SkipCertValidation` setting is for test environments only; set to `false` in production

## Troubleshooting

| Symptom | Possible Cause | Solution |
|---------|----------------|----------|
| Service fails to start | Port in use | Check port usage: `netstat -ano \| findstr :80` |
| Service fails to start | Invalid configuration | Verify `appsettings.json` is valid JSON |
| Cannot connect to AD | Incorrect LDAP address | Verify LDAP URL is reachable: `Test-NetConnection <ip> -Port 636` |
| Cannot connect to AD | Incorrect password | Check environment variable `AD_SVC_PASSWORD` |
| Cannot connect to AD | DNS cannot resolve DC hostname | Add hosts record or point DNS to domain controller (see below) |
| LDAPS script syntax error | Incorrect file encoding (PowerShell 5.x) | Run with `powershell -ExecutionPolicy Bypass -File`, or ensure file is UTF-8 BOM encoded |
| Script reports "scripts is disabled" | PowerShell execution policy restriction | Use `powershell -ExecutionPolicy Bypass -File "script_path"` |
| Cannot log in to admin UI | Incorrect password | Check environment variable `ADMIN_PASSWORD` |
| Page not accessible | Firewall blocking | Verify Windows Firewall allows the configured port |

### DNS Resolution Issues

If the ImprivataProxy host cannot resolve the domain controller hostname (e.g., `sso.ad.vista.com`), it is usually because the machine's DNS server does not point to the domain controller (the DC is typically also the DNS server).

**Diagnosis:**

```powershell
nslookup sso.ad.vista.com
```

If it returns "Non-existent domain", DNS resolution is failing.

**Fix (choose one):**

Option 1: Add hosts record (recommended, simple and direct)

```powershell
# Add the DC IP to the hosts file (using 192.168.230.205 as example)
Add-Content C:\Windows\System32\drivers\etc\hosts "192.168.230.205 sso.ad.vista.com"
ipconfig /flushdns
```

Option 2: Change DNS server to point to the domain controller

```powershell
# Set primary DNS to DC IP (using Ethernet0 adapter as example)
Set-DnsClientServerAddress -InterfaceAlias "Ethernet0" -ServerAddresses "192.168.230.205","8.8.8.8"
```

**Verify:**

```powershell
ping sso.ad.vista.com
# Should show: Reply from 192.168.230.205
```
