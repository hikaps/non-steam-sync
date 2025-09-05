Param(
    [string]$Configuration = 'Release',
    [string]$PlayniteVersion = '10.32'
)

$ErrorActionPreference = 'Stop'

$proj = Join-Path $PSScriptRoot '..\src\SteamShortcutsImporter\SteamShortcutsImporter.csproj'
dotnet build $proj -c $Configuration

$outDir = Join-Path $PSScriptRoot "..\src\SteamShortcutsImporter\bin\$Configuration\net462"
if (!(Test-Path $outDir)) { throw "Build output not found: $outDir" }

Copy-Item (Join-Path $PSScriptRoot '..\src\SteamShortcutsImporter\extension.yaml') $outDir -Force
if (Test-Path (Join-Path $PSScriptRoot '..\src\SteamShortcutsImporter\icon.png')) {
    Copy-Item (Join-Path $PSScriptRoot '..\src\SteamShortcutsImporter\icon.png') $outDir -Force
}

$tmp = Join-Path $env:TEMP "Playnite-$PlayniteVersion"
if (!(Test-Path (Join-Path $tmp 'Toolbox.exe'))) {
    $zipUrl = "https://github.com/JosefNemec/Playnite/releases/download/$PlayniteVersion/Playnite-$PlayniteVersion.zip"
    $zipPath = Join-Path $env:TEMP "Playnite-$PlayniteVersion.zip"
    Invoke-WebRequest -Uri $zipUrl -OutFile $zipPath
    New-Item -ItemType Directory -Force -Path $tmp | Out-Null
    Expand-Archive -Path $zipPath -DestinationPath $tmp -Force
}

$toolbox = Join-Path $tmp 'Toolbox.exe'
if (!(Test-Path $toolbox)) { throw "Toolbox.exe not found: $toolbox" }

Push-Location $outDir
try {
    if (Test-Path 'SteamShortcutsImporter.pext') { Remove-Item 'SteamShortcutsImporter.pext' -Force }
    & $toolbox pack $outDir $PSScriptRoot
}
finally {
    Pop-Location
}

Write-Host "Packed: " (Join-Path $PSScriptRoot 'SteamShortcutsImporter.pext')

