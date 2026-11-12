<#
.SYNOPSIS
    Prepares a DTC library PC to run the MISA kiosk app for visitors.

.DESCRIPTION
    Run once on each library PC, in an administrator PowerShell window, after copying the
    published kiosk app to the PC (KioskApp README, "Deploying"). It:

      1. creates a standard (not administrator) Windows account for visitors,
      2. starts the kiosk app whenever that account signs in, and starts it again within a
         minute if it is closed or killed (Task Scheduler),
      3. turns off Task Manager, "Lock", "Change password", Run, the Control Panel/Settings,
         the Windows-key shortcuts and the command prompt for that account only,
      4. limits who can read the kiosk's settings and key folders,
      5. optionally keeps the PC awake (so it never shows as Offline),
      6. optionally sets up automatic sign-in with Microsoft's Sysinternals Autologon.

    Nothing changes when you add -WhatIf: it only lists what it would do. Try that first.
    Staff keep using their own administrator account for maintenance.

.EXAMPLE
    # See what would change:
    .\Setup-KioskPC.ps1 -WhatIf

.EXAMPLE
    # Do it (asks for the visitor account's password):
    .\Setup-KioskPC.ps1 -KioskExe 'C:\ComputerTracker\ComputerTrackerKiosk.exe' -KeepAwake

.EXAMPLE
    # The account must have signed in once before its restrictions (step 3) can be set.
    # Sign in as the visitor account once, sign out, then:
    .\Setup-KioskPC.ps1 -RestrictionsOnly
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    # The Windows account visitors use. It signs in automatically (step 6 / Autologon).
    [string]$KioskUser = 'MISAVisitor',

    # The published kiosk app (see the KioskApp README).
    [string]$KioskExe = 'C:\ComputerTracker\ComputerTrackerKiosk.exe',

    # Only (re)apply step 3, e.g. after the account's first sign-in.
    [switch]$RestrictionsOnly,

    # Never sleep while plugged in (the tracker shows a sleeping PC as Offline).
    [switch]$KeepAwake,

    # Path to Autologon64.exe from Microsoft Sysinternals (download it yourself from
    # https://learn.microsoft.com/sysinternals/downloads/autologon). It stores the password
    # encrypted, unlike the plain-text registry method. Leave out to set it up by hand.
    [string]$AutologonExe
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Step([string]$text) { Write-Host "`n== $text" -ForegroundColor Cyan }

# Well-known group SIDs, so this works on Windows in any language.
$UsersGroupSid = 'S-1-5-32-545'
$AdminsGroupSid = 'S-1-5-32-544'

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) { throw 'Run this in PowerShell opened with "Run as administrator".' }

$appFolder = Split-Path -Parent $KioskExe
$settingsFile = Join-Path $appFolder 'kiosk.settings.json'
$dataFolder = Join-Path $env:ProgramData 'ComputerTracker'

if (-not $RestrictionsOnly) {
    # ---- 1. The visitor account -------------------------------------------------------
    Write-Step "1. Visitor account '$KioskUser' (standard user)"
    $account = Get-LocalUser -Name $KioskUser -ErrorAction SilentlyContinue
    if ($null -eq $account) {
        if ($PSCmdlet.ShouldProcess($KioskUser, 'Create a standard local account')) {
            $password = Read-Host -AsSecureString "Password for $KioskUser (staff only; it signs in automatically)"
            $account = New-LocalUser -Name $KioskUser -Password $password -FullName 'Library visitor' `
                -Description 'MISA kiosk: visitors use this account' -PasswordNeverExpires -UserMayNotChangePassword
            Add-LocalGroupMember -SID $UsersGroupSid -Member $KioskUser
        }
    } else {
        Write-Host "   Exists already."
    }
    $admins = Get-LocalGroupMember -SID $AdminsGroupSid | Where-Object { $_.Name -like "*\$KioskUser" }
    if ($admins) { Write-Warning "$KioskUser is an administrator. Remove it from Administrators: visitors must not have admin rights." }

    # ---- 2. Start the kiosk app at sign-in, and again if it is closed -----------------
    Write-Step '2. Start the kiosk app at sign-in (and restart it within a minute if closed)'
    if (-not (Test-Path $KioskExe)) { Write-Warning "Not found yet: $KioskExe (copy the published app there first)." }
    if ($PSCmdlet.ShouldProcess('Task Scheduler', "Task 'MISA Kiosk' for $KioskUser")) {
        $action = New-ScheduledTaskAction -Execute $KioskExe -WorkingDirectory $appFolder
        $trigger = New-ScheduledTaskTrigger -AtLogOn -User "$env:COMPUTERNAME\$KioskUser"
        # Every minute while signed in: starts the app if it isn't running (a running one is left alone).
        $trigger.Repetition = (New-ScheduledTaskTrigger -Once -At (Get-Date) -RepetitionInterval (New-TimeSpan -Minutes 1)).Repetition
        $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
            -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew -StartWhenAvailable
        $principal = New-ScheduledTaskPrincipal -UserId "$env:COMPUTERNAME\$KioskUser" -LogonType Interactive -RunLevel Limited
        Register-ScheduledTask -TaskName 'MISA Kiosk' -Action $action -Trigger $trigger -Settings $settings `
            -Principal $principal -Description 'Starts the MISA kiosk app for visitors and keeps it running.' -Force | Out-Null
    }

    # ---- 4. Who can read the kiosk's files --------------------------------------------
    Write-Step '4. Limit who can read the kiosk settings and its key'
    # The app runs as the visitor account, so that account must be able to read them (and save a
    # new PIN). Other standard accounts on the PC can't. See DEPLOYMENT.md: "What this can't hide".
    if ($PSCmdlet.ShouldProcess($dataFolder, 'Allow only SYSTEM, Administrators and the kiosk account')) {
        New-Item -ItemType Directory -Force -Path $dataFolder | Out-Null
        & icacls.exe $dataFolder /inheritance:r /grant:r "*S-1-5-18:(OI)(CI)F" "*${AdminsGroupSid}:(OI)(CI)F" "${KioskUser}:(OI)(CI)M" | Out-Null
    }
    if (Test-Path $settingsFile) {
        if ($PSCmdlet.ShouldProcess($settingsFile, 'Allow only SYSTEM, Administrators and the kiosk account')) {
            & icacls.exe $settingsFile /inheritance:r /grant:r "*S-1-5-18:F" "*${AdminsGroupSid}:F" "${KioskUser}:M" | Out-Null
        }
    } else {
        Write-Warning "Not found yet: $settingsFile (copy the app first, then run this again)."
    }

    # ---- 5. Keep awake ------------------------------------------------------------------
    if ($KeepAwake) {
        Write-Step '5. Never sleep while plugged in'
        if ($PSCmdlet.ShouldProcess('Power settings', 'Sleep after: never (plugged in)')) {
            & powercfg.exe /change standby-timeout-ac 0
            & powercfg.exe /change hibernate-timeout-ac 0
        }
    }

    # ---- 6. Automatic sign-in -------------------------------------------------------------
    Write-Step "6. Automatic sign-in as $KioskUser"
    if ($AutologonExe) {
        if (-not (Test-Path $AutologonExe)) { throw "Not found: $AutologonExe" }
        if ($PSCmdlet.ShouldProcess($KioskUser, 'Sign in automatically (Sysinternals Autologon)')) {
            $plain = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR(
                (Read-Host -AsSecureString "Password of $KioskUser again, for automatic sign-in")))
            & $AutologonExe $KioskUser $env:COMPUTERNAME $plain /accepteula
            $plain = $null
        }
    } else {
        Write-Host "   Not done. Run Sysinternals Autologon (Microsoft) once: Autologon64.exe, enter $KioskUser and its password."
    }
}

# ---- 3. Restrictions for the visitor account only -----------------------------------------
Write-Step "3. Restrictions for $KioskUser (Task Manager, Lock, Run, Control Panel, Windows keys, command prompt)"
$profilePath = $null
$sid = (Get-LocalUser -Name $KioskUser -ErrorAction SilentlyContinue).SID.Value
if ($sid) {
    $profilePath = (Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\$sid" -ErrorAction SilentlyContinue).ProfileImagePath
}
if (-not $profilePath -or -not (Test-Path (Join-Path $profilePath 'NTUSER.DAT'))) {
    Write-Warning "$KioskUser has not signed in yet, so its settings don't exist. Sign in as $KioskUser once, sign out, then run: .\Setup-KioskPC.ps1 -RestrictionsOnly"
} elseif ($PSCmdlet.ShouldProcess($KioskUser, 'Turn off Task Manager, Lock, Change password, Run, Control Panel, Windows keys, command prompt')) {
    $hive = 'HKU\MISAKiosk'
    & reg.exe load $hive (Join-Path $profilePath 'NTUSER.DAT') | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not open $KioskUser's settings. Make sure $KioskUser is signed out, then try again." }
    try {
        $system = 'HKU\MISAKiosk\Software\Microsoft\Windows\CurrentVersion\Policies\System'
        $explorer = 'HKU\MISAKiosk\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer'
        $windowsSystem = 'HKU\MISAKiosk\Software\Policies\Microsoft\Windows\System'
        $values = @(
            @($system, 'DisableTaskMgr', 1),          # Task Manager (Ctrl+Shift+Esc, Ctrl+Alt+Del menu)
            @($system, 'DisableLockWorkstation', 1),  # "Lock"
            @($system, 'DisableChangePassword', 1),   # "Change a password"
            @($system, 'DisableRegistryTools', 1),    # Registry Editor
            @($explorer, 'NoRun', 1),                 # Win+R
            @($explorer, 'NoControlPanel', 1),        # Control Panel and Settings
            @($explorer, 'NoWinKeys', 1),             # Windows-key shortcuts
            @($windowsSystem, 'DisableCMD', 1)        # command prompt
        )
        foreach ($v in $values) {
            & reg.exe add $v[0] /v $v[1] /t REG_DWORD /d $v[2] /f | Out-Null
        }
    } finally {
        [GC]::Collect()
        & reg.exe unload $hive | Out-Null
    }
}

Write-Step 'Done. Still to do by hand:'
Write-Host "   - kiosk.settings.json: apiUrl, kioskMode true, a staffPin of 8+ characters, visitorUrl."
Write-Host "   - Pair the PC (dashboard: Computers -> Details -> Create pairing code), sign in as $KioskUser, test."
Write-Host "   - Check BIOS/UEFI: password-protect it and turn off booting from USB, or a visitor can bypass Windows."
