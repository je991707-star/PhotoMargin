[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$fixtureRoot = Join-Path $repositoryRoot ('artifacts\publish-regression-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixtureRoot | Out-Null
$results = @()

# Invoke the real script in an isolated copy of the directory structure. Simulate
# build/failure outcomes only; never rename or delete the user's actual release.
function global:dotnet {
    if ($args[0] -eq 'restore' -and $global:photoMarginPublishRegressionMode -eq 'RestoreFailure') { $global:LASTEXITCODE = 23; return }
    if ($args[0] -eq 'publish') {
        if ($global:photoMarginPublishRegressionMode -eq 'PublishFailure') { $global:LASTEXITCODE = 24; return }
        if ($global:photoMarginPublishRegressionMode -ne 'MissingExecutable') {
            $outputIndex = [Array]::IndexOf($args, '--output')
            [IO.File]::WriteAllText((Join-Path $args[$outputIndex + 1] 'PhotoMargin.App.exe'), 'new-release-fixture')
        }
    }
    $global:LASTEXITCODE = 0
}
function global:Move-Item {
    param([string]$LiteralPath, [string]$Destination)
    if ($global:photoMarginPublishRegressionMode -eq 'PromotionFailure' -and $LiteralPath -like '*publish-staging-*\PhotoMargin-win-x64.zip') {
        throw 'Simulated archive promotion failure.'
    }
    Microsoft.PowerShell.Management\Move-Item -LiteralPath $LiteralPath -Destination $Destination -ErrorAction Stop
}
try {
    foreach ($case in @('RestoreFailure', 'PublishFailure', 'MissingExecutable', 'PromotionFailure', 'Success')) {
        $global:photoMarginPublishRegressionMode = $case
        $caseRoot = Join-Path $fixtureRoot $case
        $packaging = Join-Path $caseRoot 'packaging'; $docs = Join-Path $caseRoot 'docs'
        $artifacts = Join-Path $caseRoot 'artifacts'; $release = Join-Path $artifacts 'PhotoMargin-win-x64'
        New-Item -ItemType Directory -Path $packaging, $docs, $release | Out-Null
        Copy-Item -LiteralPath (Join-Path $repositoryRoot 'packaging\publish.ps1') -Destination $packaging
        [IO.File]::WriteAllText((Join-Path $caseRoot 'README.md'), 'Test readme')
        [IO.File]::WriteAllText((Join-Path $docs 'THIRD_PARTY_NOTICES.md'), 'Test notices')
        [IO.File]::WriteAllText((Join-Path $release 'previous.txt'), 'previous-release')
        $archive = Join-Path $artifacts 'PhotoMargin-win-x64.zip'
        [IO.File]::WriteAllText($archive, 'previous-archive')
        $failed = $false
        try { & (Join-Path $packaging 'publish.ps1') } catch { $failed = $true }
        if ($case -eq 'Success') {
            $passed = -not $failed -and (Test-Path -LiteralPath (Join-Path $release 'PhotoMargin.App.exe')) -and
                @(Get-ChildItem -LiteralPath $artifacts -Directory -Filter 'PhotoMargin-win-x64.previous-*').Count -eq 1 -and
                @(Get-ChildItem -LiteralPath $artifacts -File -Filter 'PhotoMargin-win-x64.previous-*.zip').Count -eq 1
        }
        else {
            $passed = $failed -and (Test-Path -LiteralPath (Join-Path $release 'previous.txt')) -and
                [IO.File]::ReadAllText($archive) -eq 'previous-archive'
        }
        $results += [PSCustomObject]@{ Case = $case; Passed = $passed }
        if (-not $passed) { throw "Publish regression failed: $case" }
    }
    $results | Format-Table -AutoSize
    $results | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $fixtureRoot 'results.json') -Encoding UTF8
    Write-Host "Fixtures/results: $fixtureRoot"
}
finally {
    Remove-Item Function:\dotnet
    Remove-Item Function:\Move-Item
}
