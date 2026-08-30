param(
    [string]$Package = (
        Join-Path $PSScriptRoot "..\artifacts\stream-deck\dev.witqq.win-net-switch.streamDeckPlugin")
)

$ErrorActionPreference = "Stop"
if (-not (Test-Path -LiteralPath $Package -PathType Leaf)) {
    throw "Stream Deck plugin package was not found: $Package"
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($Package)
try {
    $entries = @($archive.Entries | ForEach-Object FullName)
    $prefix = "dev.witqq.win-net-switch.sdPlugin/"
    $requiredEntries = @(
        "${prefix}manifest.json",
        "${prefix}bin/plugin.js",
        "${prefix}ui/toggle-adapter.html",
        "${prefix}ui/sdpi-components.js"
    )
    foreach ($requiredEntry in $requiredEntries) {
        if ($entries -notcontains $requiredEntry) {
            throw "Stream Deck plugin package is missing: $requiredEntry"
        }
    }

    $forbiddenExtensions = @(".exe", ".dll", ".msi", ".ps1", ".bat", ".cmd")
    $forbiddenEntries = @($entries | Where-Object {
        $forbiddenExtensions -contains [IO.Path]::GetExtension($_).ToLowerInvariant()
    })
    if ($forbiddenEntries.Count -ne 0) {
        throw "Stream Deck plugin package contains forbidden companion files: " +
            ($forbiddenEntries -join ", ")
    }

    Write-Host "Stream Deck plugin package verified: $($entries.Count) files, no companion executable."
}
finally {
    $archive.Dispose()
}
