param(
    [ValidateSet("win-x64", "win-arm64")]
    [string] $Runtime = "win-x64",
    [ValidateSet("app", "setup")]
    [string] $Target = "app"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$projectName = if ($Target -eq "setup") { "SimplyDraw.Setup" } else { "SimplyDraw.App" }
$exeName = if ($Target -eq "setup") { "SimplyDrawSetup.exe" } else { "SimplyDraw.exe" }
$project = Join-Path $root "src\$projectName\$projectName.csproj"
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

Write-Host "Built $out\$exeName"
