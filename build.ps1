param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$OutputDirectory = "dist",
    [switch]$SelfContained
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$output = Join-Path $root $OutputDirectory

dotnet run --project (Join-Path $root "tests/Sts2ModManager.Core.Tests/Sts2ModManager.Core.Tests.csproj") --configuration $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "Core tests failed."
}

$selfContainedValue = if ($SelfContained) { "true" } else { "false" }
dotnet publish (Join-Path $root "src/Sts2ModManager.App/Sts2ModManager.App.csproj") `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained $selfContainedValue `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    --output $output

if ($LASTEXITCODE -ne 0) {
    throw "Application publish failed."
}

$bootstrapStaging = Join-Path $output "_bootstrap-staging"
$packageOutput = Join-Path $output "ModTheSpire2"
$bootstrapProject = Join-Path $root "src/ModTheSpire2.Bootstrap/ModTheSpire2.Bootstrap.csproj"
$pckBuilderProject = Join-Path $root "tools/Sts2PckBuilder/Sts2PckBuilder.csproj"
$pckSource = Join-Path $root "src/ModTheSpire2.Bootstrap/PckContent"

if (Test-Path -LiteralPath $bootstrapStaging) {
    Remove-Item -LiteralPath $bootstrapStaging -Recurse -Force
}
New-Item -ItemType Directory -Path $bootstrapStaging | Out-Null

dotnet publish $bootstrapProject `
    --configuration $Configuration `
    --framework net8.0 `
    --self-contained false `
    -p:DebugType=None `
    --output $bootstrapStaging

if ($LASTEXITCODE -ne 0) {
    throw "Bootstrap mod publish failed."
}

if (Test-Path -LiteralPath $packageOutput) {
    Remove-Item -LiteralPath $packageOutput -Recurse -Force
}
New-Item -ItemType Directory -Path $packageOutput | Out-Null
Copy-Item -LiteralPath (Join-Path $bootstrapStaging "ModTheSpire2.dll") -Destination $packageOutput
Copy-Item -LiteralPath (Join-Path $root "src/ModTheSpire2.Bootstrap/ModTheSpire2.json") -Destination $packageOutput

$pckPath = Join-Path $packageOutput "ModTheSpire2.pck"
dotnet run --project $pckBuilderProject --configuration $Configuration -- $pckSource $pckPath
if ($LASTEXITCODE -ne 0) {
    throw "PCK build failed."
}

Remove-Item -LiteralPath $bootstrapStaging -Recurse -Force
Write-Host "Published launcher to $output"
Write-Host "Published native StS2 package to $packageOutput"
