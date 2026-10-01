$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $projectRoot "src\Nyri.Win10\Nyri.Win10.csproj"
$output = Join-Path $projectRoot "dist\win-x64"
$localSdk = "D:\CODE PROECTS\NEW FOUNDS\ISLAND\.dotnet\dotnet.exe"
$dotnet = $null

if (Test-Path $localSdk) {
    $dotnet = $localSdk
} else {
    $candidate = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
    if ($candidate) {
        $sdks = & $candidate --list-sdks 2>$null
        if ($LASTEXITCODE -eq 0 -and $sdks) { $dotnet = $candidate }
    }
}

if (-not $dotnet) { throw ".NET 8 SDK not found." }

& $dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $output

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$exe = Join-Path $output "NYRI-WIN10.exe"
if (-not (Test-Path $exe)) { throw "Publish completed but EXE was not found." }

Write-Host "Built: $exe"