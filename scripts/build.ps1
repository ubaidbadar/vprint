param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $root 'src/VPrint/VPrint.csproj'
$checks = Join-Path $root 'tests/VPrint.Checks/VPrint.Checks.csproj'
$output = Join-Path $root "artifacts/$Runtime"

dotnet restore $project -r $Runtime
if ($LASTEXITCODE -ne 0) { throw "Restore failed." }

dotnet build $project -c Release -r $Runtime --no-restore
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

dotnet run --project $checks -c Release -r $Runtime
if ($LASTEXITCODE -ne 0) { throw "Smoke tests failed." }

dotnet publish $project -c Release -r $Runtime --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=embedded -p:PublishTrimmed=false -o $output
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

$exe = Join-Path $output 'VPrint.exe'
if (-not (Test-Path $exe)) { throw "Expected EXE was not produced: $exe" }
Write-Host "VPrint build ready: $exe"
