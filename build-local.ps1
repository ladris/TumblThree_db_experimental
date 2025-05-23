param (
    [string]$Platform = "x64",  # Or "x86"
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$PSScriptRoot = Split-Path -Parent -Path $MyInvocation.MyCommand.Definition
Push-Location $PSScriptRoot # Ensure commands run from repo root

$SolutionPath = Join-Path $PSScriptRoot "src\TumblThree\TumblThree.sln"
$VersionInfoFile = Join-Path $PSScriptRoot "src\TumblThree\SharedAssemblyInfo.cs"
$ScriptsPath = Join-Path $PSScriptRoot "scripts"

Write-Host "Determining application version..."
$AppVersion = & (Join-Path $ScriptsPath "get-version.ps1") -versionInfoFile $VersionInfoFile
if (-not $AppVersion -or $AppVersion -match "^\s*$") {
    Write-Error "Failed to determine application version. Check get-version.ps1 and SharedAssemblyInfo.cs."
    exit 1
}
Write-Host "AppVersion: $AppVersion"

Write-Host "Restoring NuGet packages for $SolutionPath..."
nuget.exe restore $SolutionPath

Write-Host "Building solution $SolutionPath (Configuration: $Configuration, Platform: $Platform)..."
msbuild.exe $SolutionPath "/p:Configuration=$Configuration" "/p:Platform=$Platform" "/t:Rebuild" "/verbosity:minimal"

Write-Host "Packaging application..."
$BuildOutputPath = Join-Path $PSScriptRoot "src\TumblThree\TumblThree.Presentation\bin\$Platform\$Configuration" # Corrected path
$ArtifactsDir = Join-Path $PSScriptRoot "artifacts"
New-Item -ItemType Directory -Force -Path $ArtifactsDir | Out-Null

& (Join-Path $ScriptsPath "create-package.ps1") -BuildOutputPath $BuildOutputPath -RootPath $PSScriptRoot -ArtifactsRootPath $ArtifactsDir -Version $AppVersion -Platform $Platform

Write-Host "Build and packaging complete. Artifacts in (or will be in): $ArtifactsDir"
Pop-Location
