<#
.SYNOPSIS
    Interactive tool to create test users, service account, and authorization groups on a Domain Controller.

.DESCRIPTION
    Provides an interactive menu to:
    1. Create test users (tester1, tester2)
    2. Create service account (svc_draeger for LDAP bind)
    3. Create authorization security groups
    4. Add test users to all groups
    A. Run all steps sequentially

.NOTES
    Requires ActiveDirectory PowerShell module (installed by default on DCs).
    Must be run as Administrator on the Domain Controller.

.EXAMPLE
    .\Setup-AD-TestUsers.ps1
#>

$ErrorActionPreference = "Stop"
Import-Module ActiveDirectory

$Password = ConvertTo-SecureString "Draeger123" -AsPlainText -Force

# ================================================================
# Function definitions
# ================================================================

function Show-Banner {
    Write-Host ""
    Write-Host "============================================" -ForegroundColor Cyan
    Write-Host " AD Test Environment Setup" -ForegroundColor Cyan
    Write-Host " Domain:   CHRLIEGE.BE" -ForegroundColor Cyan
    Write-Host " Password: Draeger123 (all accounts)" -ForegroundColor Cyan
    Write-Host "============================================" -ForegroundColor Cyan
}

function Show-Menu {
    Write-Host ""
    Write-Host "  [1] Create test users (tester1, tester2, tester3)" -ForegroundColor White
    Write-Host "  [2] Create service account (svc_draeger)" -ForegroundColor White
    Write-Host "  [3] Create authorization groups" -ForegroundColor White
    Write-Host "  [4] Add test users to all groups" -ForegroundColor White
    Write-Host "  [A] Run ALL steps (1-4)" -ForegroundColor Yellow
    Write-Host "  [Q] Exit" -ForegroundColor DarkGray
    Write-Host ""
}

function Step-CreateTestUsers {
    Write-Host ""
    Write-Host "[1] Creating test users..." -ForegroundColor Yellow
    try {
        New-ADUser -Name "tester1" `
            -SamAccountName "tester1" `
            -UserPrincipalName "tester1@CHRLIEGE.BE" `
            -EmployeeNumber "9021054" `
            -GivenName "Test" `
            -Surname "User1" `
            -DisplayName "Test User1" `
            -AccountPassword $Password `
            -Enabled $true `
            -Path "OU=SSO,OU=Users,OU=CITADELLE,DC=CHRLIEGE,DC=BE"
        Write-Host "  tester1 (badge: 9021054) created" -ForegroundColor Green
    }
    catch {
        if ($_.Exception.Message -match "already exists") {
            Write-Host "  tester1 already exists, skipped" -ForegroundColor DarkGray
        } else {
            Write-Host "  ERROR creating tester1: $_" -ForegroundColor Red
        }
    }

    try {
        New-ADUser -Name "tester2" `
            -SamAccountName "tester2" `
            -UserPrincipalName "tester2@CHRLIEGE.BE" `
            -EmployeeNumber "9999999" `
            -GivenName "Test" `
            -Surname "User2" `
            -DisplayName "Test User2" `
            -AccountPassword $Password `
            -Enabled $true `
            -Path "OU=SSO,OU=Users,OU=CITADELLE,DC=CHRLIEGE,DC=BE"
        Write-Host "  tester2 (badge: 9999999) created" -ForegroundColor Green
    }
    catch {
        if ($_.Exception.Message -match "already exists") {
            Write-Host "  tester2 already exists, skipped" -ForegroundColor DarkGray
        } else {
            Write-Host "  ERROR creating tester2: $_" -ForegroundColor Red
        }
    }

    try {
        New-ADUser -Name "tester3" `
            -SamAccountName "tester3" `
            -UserPrincipalName "tester3@CHRLIEGE.BE" `
            -EmployeeNumber "040574B" `
            -GivenName "Test" `
            -Surname "User3" `
            -DisplayName "Test User3" `
            -AccountPassword $Password `
            -Enabled $true `
            -Path "OU=SSO,OU=Users,OU=CITADELLE,DC=CHRLIEGE,DC=BE"
        Write-Host "  tester3 (badge: 040574B) created" -ForegroundColor Green
    }
    catch {
        if ($_.Exception.Message -match "already exists") {
            Write-Host "  tester3 already exists, skipped" -ForegroundColor DarkGray
        } else {
            Write-Host "  ERROR creating tester3: $_" -ForegroundColor Red
        }
    }
}

function Step-CreateServiceAccount {
    Write-Host ""
    Write-Host "[2] Creating service account..." -ForegroundColor Yellow
    try {
        New-ADUser -Name "svc_draeger" `
            -SamAccountName "svc_draeger" `
            -UserPrincipalName "svc_draeger@CHRLIEGE.BE" `
            -AccountPassword $Password `
            -Enabled $true `
            -PasswordNeverExpires $true `
            -CannotChangePassword $true `
            -Path "OU=Readers,OU=Users,OU=Specific,OU=CITADELLE,DC=CHRLIEGE,DC=BE"
        Write-Host "  svc_draeger created" -ForegroundColor Green
    }
    catch {
        if ($_.Exception.Message -match "already exists") {
            Write-Host "  svc_draeger already exists, skipped" -ForegroundColor DarkGray
        } else {
            Write-Host "  ERROR creating svc_draeger: $_" -ForegroundColor Red
        }
    }
    Write-Host ""
    Write-Host "  Service Account DN:" -ForegroundColor Cyan
    Write-Host "  CN=svc_draeger,OU=Readers,OU=Users,OU=Specific,OU=CITADELLE,DC=CHRLIEGE,DC=BE" -ForegroundColor White
}

function Step-CreateGroups {
    Write-Host ""
    Write-Host "[3] Creating authorization groups..." -ForegroundColor Yellow

    $groups = @("PRM_Infirmier_Moniteur", "PRM_Aide_Soignant", "PRM_Assistant_Logistique")
    foreach ($g in $groups) {
        try {
            New-ADGroup -Name $g -GroupScope Global -GroupCategory Security
            Write-Host "  $g created" -ForegroundColor Green
        }
        catch {
            if ($_.Exception.Message -match "already exists") {
                Write-Host "  $g already exists, skipped" -ForegroundColor DarkGray
            } else {
                Write-Host "  ERROR creating ${g}: $_" -ForegroundColor Red
            }
        }
    }
}

function Step-AssignGroupMembers {
    Write-Host ""
    Write-Host "[4] Adding test users to groups..." -ForegroundColor Yellow

    $groups = @("PRM_Infirmier_Moniteur", "PRM_Aide_Soignant", "PRM_Assistant_Logistique")
    foreach ($g in $groups) {
        try {
            Add-ADGroupMember -Identity $g -Members "tester1","tester2","tester3" -ErrorAction Stop
            Write-Host "  tester1, tester2, tester3 added to $g" -ForegroundColor Green
        }
        catch {
            if ($_.Exception.Message -match "already a member") {
                Write-Host "  Users already in $g, skipped" -ForegroundColor DarkGray
            } else {
                Write-Host "  ERROR adding members to ${g}: $_" -ForegroundColor Red
            }
        }
    }
}

function Step-RunAll {
    Step-CreateTestUsers
    Step-CreateServiceAccount
    Step-CreateGroups
    Step-AssignGroupMembers
    Write-Host ""
    Write-Host "All steps completed." -ForegroundColor Green
    Write-Host ""
    Write-Host "Summary:" -ForegroundColor Cyan
    Write-Host "  tester1 -> badge 9021054" -ForegroundColor White
    Write-Host "  tester2 -> badge 9999999" -ForegroundColor White
    Write-Host "  tester3 -> badge 040574B" -ForegroundColor White
    Write-Host "  svc_draeger -> LDAP bind account" -ForegroundColor White
    Write-Host "  All passwords: Draeger123" -ForegroundColor White
}

# ================================================================
# Main loop
# ================================================================
Show-Banner

while ($true) {
    Show-Menu
    $choice = Read-Host "Select an option"

    switch ($choice.ToUpper()) {
        "1" { Step-CreateTestUsers }
        "2" { Step-CreateServiceAccount }
        "3" { Step-CreateGroups }
        "4" { Step-AssignGroupMembers }
        "A" { Step-RunAll }
        "Q" {
            Write-Host ""
            Write-Host "Exiting." -ForegroundColor DarkGray
            return
        }
        default {
            Write-Host "  Invalid option. Please enter 1-4, A, or Q." -ForegroundColor Red
        }
    }
}
