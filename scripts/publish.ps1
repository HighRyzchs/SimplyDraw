param(
    [ValidateSet("win-x64", "win-arm64")]
    [string] $Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\SimplyDraw.App\SimplyDraw.App.csproj"
$out = Join-Path $root "publish\$Runtime"

dotnet publish $project `
    -c Release `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -o $out

Write-Host "Built $out\SimplyDraw.exe"
