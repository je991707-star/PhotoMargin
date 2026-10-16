[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
$publishDirectory = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot 'PhotoMargin-win-x64'))
$archivePath = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot 'PhotoMargin-win-x64.zip'))

function Assert-ArtifactPath([string]$candidate) {
    $resolved = [System.IO.Path]::GetFullPath($candidate)
    if (-not $resolved.StartsWith($artifactsRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Publish output escaped the repository artifacts directory: $resolved"
    }
}

New-Item -ItemType Directory -Force -Path $artifactsRoot | Out-Null
$publishId = [Guid]::NewGuid().ToString('N')
$stagingRoot = Join-Path $artifactsRoot "publish-staging-$publishId"
$stagingDirectory = Join-Path $stagingRoot 'PhotoMargin-win-x64'
$stagingArchive = Join-Path $stagingRoot 'PhotoMargin-win-x64.zip'
$previousDirectory = Join-Path $artifactsRoot "PhotoMargin-win-x64.previous-$publishId"
$previousArchive = Join-Path $artifactsRoot "PhotoMargin-win-x64.previous-$publishId.zip"
foreach ($candidate in @($publishDirectory, $archivePath, $stagingRoot, $stagingDirectory, $stagingArchive, $previousDirectory, $previousArchive)) {
    Assert-ArtifactPath $candidate
}
New-Item -ItemType Directory -Path $stagingDirectory | Out-Null

$projectPath = Join-Path $repositoryRoot 'src\PhotoMargin.App\PhotoMargin.App.csproj'
dotnet restore $projectPath --runtime win-x64 --nologo
if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore failed with exit code $LASTEXITCODE."
}

dotnet publish $projectPath `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --no-restore `
    --output $stagingDirectory `
    --nologo

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path -LiteralPath (Join-Path $stagingDirectory 'PhotoMargin.App.exe'))) {
    throw 'Publish did not produce PhotoMargin.App.exe; previous release was preserved.'
}
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'README.md') -Destination $stagingDirectory
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'docs\THIRD_PARTY_NOTICES.md') -Destination $stagingDirectory
Compress-Archive -Path (Join-Path $stagingDirectory '*') -DestinationPath $stagingArchive -CompressionLevel Optimal

# Only promote a completely built and archived release. Keep the previous release
# recoverable, and restore it if either promotion fails. No recursive deletion.
$directoryBackedUp = $false; $archiveBackedUp = $false; $directoryPromoted = $false
try {
    if (Test-Path -LiteralPath $publishDirectory) { Move-Item -LiteralPath $publishDirectory -Destination $previousDirectory; $directoryBackedUp = $true }
    if (Test-Path -LiteralPath $archivePath) { Move-Item -LiteralPath $archivePath -Destination $previousArchive; $archiveBackedUp = $true }
    Move-Item -LiteralPath $stagingDirectory -Destination $publishDirectory; $directoryPromoted = $true
    Move-Item -LiteralPath $stagingArchive -Destination $archivePath
}
catch {
    if ($directoryPromoted) { Move-Item -LiteralPath $publishDirectory -Destination $stagingDirectory }
    if ($directoryBackedUp) { Move-Item -LiteralPath $previousDirectory -Destination $publishDirectory }
    if ($archiveBackedUp) { Move-Item -LiteralPath $previousArchive -Destination $archivePath }
    throw
}

Write-Host "Published: $publishDirectory"
Write-Host "Archive:   $archivePath"
