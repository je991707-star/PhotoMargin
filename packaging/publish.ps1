[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
$publishDirectory = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot 'PhotoMargin-win-x64'))
$archivePath = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot 'PhotoMargin-win-x64.zip'))

if (-not $publishDirectory.StartsWith($artifactsRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Publish output escaped the repository artifacts directory.'
}

New-Item -ItemType Directory -Force -Path $artifactsRoot | Out-Null
if (Test-Path -LiteralPath $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}
if (Test-Path -LiteralPath $archivePath) {
    Remove-Item -LiteralPath $archivePath -Force
}

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
    --output $publishDirectory `
    --nologo

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

Copy-Item -LiteralPath (Join-Path $repositoryRoot 'README.md') -Destination $publishDirectory
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'docs\THIRD_PARTY_NOTICES.md') -Destination $publishDirectory
Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $archivePath -CompressionLevel Optimal

Write-Host "Published: $publishDirectory"
Write-Host "Archive:   $archivePath"
