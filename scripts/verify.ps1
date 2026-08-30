param(
    [switch]$SkipSmoke,

    [string]$DotNet = "dotnet",

    [string]$Npm = "npm"
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $projectRoot "WinNetSwitch.slnx"
$tests = Join-Path $projectRoot "tests\WinNetSwitch.Tests\WinNetSwitch.Tests.csproj"
$publishScript = Join-Path $PSScriptRoot "publish.ps1"
$publishedExecutable = Join-Path $projectRoot "artifacts\publish\win-x64\WinNetSwitch.exe"
$setupExecutable = Join-Path $projectRoot "artifacts\setup\win-x64\WinNetSwitch-Setup.exe"
$pluginRoot = Join-Path $projectRoot "stream-deck-plugin"
$pluginPackageVerificationScript = Join-Path $PSScriptRoot "test-stream-deck-package.ps1"

function Invoke-PublishedAppMode {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Argument,

        [Parameter(Mandatory = $true)]
        [string]$Description
    )

    $process = Start-Process `
        -FilePath $publishedExecutable `
        -ArgumentList $Argument `
        -Wait `
        -PassThru
    if ($process.ExitCode -ne 0) {
        throw "$Description failed with exit code $($process.ExitCode)."
    }
}

Push-Location $projectRoot
try {
    & $DotNet restore $solution
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE." }

    & $DotNet build $solution --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE." }

    & $DotNet run --project $tests --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Test runner failed with exit code $LASTEXITCODE." }

    Push-Location $pluginRoot
    try {
        & $Npm ci
        if ($LASTEXITCODE -ne 0) { throw "npm ci failed with exit code $LASTEXITCODE." }

        & $Npm run typecheck
        if ($LASTEXITCODE -ne 0) { throw "Plugin typecheck failed with exit code $LASTEXITCODE." }

        & $Npm test
        if ($LASTEXITCODE -ne 0) { throw "Plugin tests failed with exit code $LASTEXITCODE." }

        & $Npm run package
        if ($LASTEXITCODE -ne 0) { throw "Plugin packaging failed with exit code $LASTEXITCODE." }

        & $Npm run validate
        if ($LASTEXITCODE -ne 0) { throw "Plugin validation failed with exit code $LASTEXITCODE." }

        & $pluginPackageVerificationScript
    }
    finally {
        Pop-Location
    }

    & $publishScript -Runtime win-x64 -DotNet $DotNet

    if (-not $SkipSmoke) {
        $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
        $principal = [Security.Principal.WindowsPrincipal]::new($identity)
        $isAdministrator = $principal.IsInRole(
            [Security.Principal.WindowsBuiltInRole]::Administrator)
        if (-not $isAdministrator) {
            throw "Smoke test requires an elevated PowerShell window. Re-run as administrator or use -SkipSmoke."
        }

        Invoke-PublishedAppMode `
            -Argument "--logging-self-test" `
            -Description "Diagnostic logging self-test"
        Write-Host "Diagnostic logging self-test passed."

        Invoke-PublishedAppMode `
            -Argument "--probe-adapters" `
            -Description "Production adapter enumeration probe"
        Write-Host "Production adapter enumeration probe passed without changing adapter state."

        Invoke-PublishedAppMode `
            -Argument "--smoke-test" `
            -Description "Native tray smoke test"
        Write-Host "Native tray smoke test passed without invoking the production network service."

        Invoke-PublishedAppMode `
            -Argument "--ipc-smoke-test" `
            -Description "Local control pipe smoke test"
        Write-Host "Local control pipe smoke test passed with a fake network service."

        $setupProcess = Start-Process `
            -FilePath $setupExecutable `
            -ArgumentList "--self-test" `
            -Wait `
            -PassThru
        if ($setupProcess.ExitCode -ne 0) {
            throw "Setup payload self-test failed with exit code $($setupProcess.ExitCode)."
        }

        Write-Host "Setup payload self-test passed."
    }
}
finally {
    Pop-Location
}
