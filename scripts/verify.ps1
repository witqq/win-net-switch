param(
    [switch]$SkipSmoke,

    [string]$DotNet = "dotnet"
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $projectRoot "WinNetSwitch.slnx"
$tests = Join-Path $projectRoot "tests\WinNetSwitch.Tests\WinNetSwitch.Tests.csproj"
$publishScript = Join-Path $PSScriptRoot "publish.ps1"
$publishedExecutable = Join-Path $projectRoot "artifacts\publish\win-x64\WinNetSwitch.exe"

Push-Location $projectRoot
try {
    & $DotNet restore $solution
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE." }

    & $DotNet build $solution --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE." }

    & $DotNet run --project $tests --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Test runner failed with exit code $LASTEXITCODE." }

    & $publishScript -Runtime win-x64 -DotNet $DotNet

    if (-not $SkipSmoke) {
        $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
        $principal = [Security.Principal.WindowsPrincipal]::new($identity)
        $isAdministrator = $principal.IsInRole(
            [Security.Principal.WindowsBuiltInRole]::Administrator)
        if (-not $isAdministrator) {
            throw "Smoke test requires an elevated PowerShell window. Re-run as administrator or use -SkipSmoke."
        }

        & $publishedExecutable --probe-adapters
        if ($LASTEXITCODE -ne 0) {
            throw "Production adapter enumeration probe failed with exit code $LASTEXITCODE."
        }

        Write-Host "Production adapter enumeration probe passed without changing adapter state."

        & $publishedExecutable --smoke-test
        if ($LASTEXITCODE -ne 0) {
            throw "Native tray smoke test failed with exit code $LASTEXITCODE."
        }

        Write-Host "Native tray smoke test passed without invoking the production network service."
    }
}
finally {
    Pop-Location
}
