param (
    [Parameter(Mandatory=$true)][string]$BuildOutputPath,
    [Parameter(Mandatory=$true)][string]$RootPath,
    [Parameter(Mandatory=$true)][string]$ArtifactsRootPath,
    [Parameter(Mandatory=$true)][string]$Version,
    [Parameter(Mandatory=$true)][string]$Platform
)

$AppPackageName = "TumblThree-v$Version-$Platform-Application"
$AppZipFile = Join-Path $ArtifactsRootPath "$AppPackageName.zip"
$StagingAppPath = Join-Path $ArtifactsRootPath "Application\TumblThree" # Staging path for files to be zipped

Write-Host "Target Zip File: $AppZipFile"
Write-Host "Staging Path: $StagingAppPath"

if (Test-Path $AppZipFile) {
    Remove-Item -Force $AppZipFile
    Write-Host "Removed existing package: $AppZipFile"
}
if (Test-Path (Join-Path $ArtifactsRootPath "Application")) {
    Remove-Item -Recurse -Force (Join-Path $ArtifactsRootPath "Application")
    Write-Host "Cleaned old staging application folder."
}

# Logic adapted from appveyor-postbuild.ps1
$harvestPath = $BuildOutputPath
$applicationArtifactsPath = $StagingAppPath

Write-Host "Harvest Path (Build Output): $harvestPath"

New-Item -ItemType Directory -Force -Path $applicationArtifactsPath | Out-Null
Write-Host "Created staging directory: $applicationArtifactsPath"

Write-Host "Copying application files from $harvestPath to $applicationArtifactsPath..."
Get-ChildItem -Path "$harvestPath\*" -Include *.exe,*.dll,*.config | Copy-Item -Destination $applicationArtifactsPath
Write-Host "Application files copied."

Write-Host "Copying LICENSE files..."
Copy-Item (Join-Path $RootPath "LICENSE") -Destination (Join-Path $applicationArtifactsPath "LICENSE.txt")
Copy-Item (Join-Path $RootPath "LICENSE-3RD-PARTY") -Destination (Join-Path $applicationArtifactsPath "LICENSE-3RD-PARTY.txt")
Write-Host "LICENSE files copied."

Write-Host "Copying translation folders..."
$translationFolders = Get-ChildItem -Directory $harvestPath | Where-Object { $_.Name.Length -eq 2 -and (Test-Path (Join-Path $_.FullName ($_.Name + ".dll"))) } # Ensure it's a culture folder
if ($translationFolders) {
    foreach ($tf in $translationFolders) {
        $tfTarget = Join-Path $applicationArtifactsPath $tf.Name
        Write-Host "Copying translation folder: $($tf.FullName) to $tfTarget"
        New-Item -ItemType Directory -Force -Path $tfTarget | Out-Null
        Get-ChildItem -Path $tf.FullName -Include *.dll | Copy-Item -Destination $tfTarget
    }
    Write-Host "Translation folders copied."
} else {
    Write-Host "No translation folders found to copy."
}

Write-Host "Zipping application files from $applicationArtifactsPath\*" -DestinationPath $AppZipFile -Force
Write-Host "Application packaged to: $AppZipFile"
