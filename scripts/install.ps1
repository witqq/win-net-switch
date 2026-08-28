param(
    [string]$Source,

    [switch]$Start
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Source)) {
    $Source = Join-Path $projectRoot "artifacts\publish\win-x64\WinNetSwitch.exe"
}

if (-not (Test-Path -LiteralPath $Source -PathType Leaf)) {
    throw "Published executable was not found: $Source. Run scripts\publish.ps1 first."
}

$installDirectory = Join-Path $env:LOCALAPPDATA "Programs\WinNetSwitch"
$installedExecutable = Join-Path $installDirectory "WinNetSwitch.exe"
$temporaryExecutable = Join-Path $installDirectory "WinNetSwitch.exe.new"
$startMenuDirectory = [Environment]::GetFolderPath("Programs")
$shortcutPath = Join-Path $startMenuDirectory "WinNetSwitch.lnk"

New-Item -ItemType Directory -Path $installDirectory -Force | Out-Null
try {
    Copy-Item -LiteralPath $Source -Destination $temporaryExecutable -Force
    Move-Item -LiteralPath $temporaryExecutable -Destination $installedExecutable -Force
}
finally {
    if (Test-Path -LiteralPath $temporaryExecutable -PathType Leaf) {
        Remove-Item -LiteralPath $temporaryExecutable -Force
    }
}

$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $installedExecutable
$shortcut.WorkingDirectory = $installDirectory
$shortcut.IconLocation = "$installedExecutable,0"
$shortcut.Description = "Switch physical network adapters"
$shortcut.Save()

Write-Host "Installed: $installedExecutable"
Write-Host "Start menu shortcut: $shortcutPath"

if ($Start) {
    Start-Process -FilePath $installedExecutable
}
