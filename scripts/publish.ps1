param(
    [ValidateSet("win-x64", "win-arm64")]
    [string]$Runtime = "win-x64",

    [string]$DotNet = "dotnet"
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $projectRoot "src\WinNetSwitch.App\WinNetSwitch.App.csproj"
$output = Join-Path $projectRoot "artifacts\publish\$Runtime"
$setupProject = Join-Path $projectRoot "src\WinNetSwitch.Setup\WinNetSwitch.Setup.csproj"
$setupOutput = Join-Path $projectRoot "artifacts\setup\$Runtime"

& $DotNet publish $project `
    --configuration Release `
    --runtime $Runtime `
    --self-contained true `
    --output $output `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$executable = Join-Path $output "WinNetSwitch.exe"
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw "Published executable was not created: $executable"
}

Write-Host "Published: $executable"

& $DotNet publish $setupProject `
    --configuration Release `
    --runtime $Runtime `
    --self-contained true `
    --output $setupOutput `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    "-p:PayloadPath=$executable"

if ($LASTEXITCODE -ne 0) {
    throw "Setup publish failed with exit code $LASTEXITCODE."
}

$setupExecutable = Join-Path $setupOutput "WinNetSwitch-Setup.exe"
if (-not (Test-Path -LiteralPath $setupExecutable -PathType Leaf)) {
    throw "Setup executable was not created: $setupExecutable"
}

Write-Host "Setup: $setupExecutable"
