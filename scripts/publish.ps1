$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "dist\win-x64"

dotnet publish "$root\src\Nyri.Win10\Nyri.Win10.csproj" `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -p:DebugType=None `
  -p:DebugSymbols=false `
  -o $out

$exe = Join-Path $out "NYRI-WIN10.exe"
$hash = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLower()
"$hash  NYRI-WIN10.exe" | Set-Content (Join-Path $out "SHA256SUMS.txt")
Write-Host "Published: $exe"