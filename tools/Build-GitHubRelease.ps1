param(
    [Parameter(Mandatory = $true)]
    [string] $Version
)

$ErrorActionPreference = 'Stop'

$RepoOwner = 'bloooowfish'
$RepoName = 'Where-Is-My-Head-Plugin'
$ReleaseVersionPattern = '^\d+\.\d+\.\d+\.\d+$'

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$ProjectPath = Join-Path $RepoRoot 'WhereIsMyHead\WhereIsMyHead.csproj'
$BuiltPluginDir = Join-Path $RepoRoot 'WhereIsMyHead\bin\x64\Release\WhereIsMyHead'
$BuiltZipPath = Join-Path $BuiltPluginDir 'latest.zip'
$DistDir = Join-Path $RepoRoot 'dist'

function Fail {
    param([Parameter(Mandatory = $true)][string] $Message)
    Write-Error $Message
    exit 1
}

function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)]
        [string] $FilePath,

        [Parameter(ValueFromRemainingArguments = $true)]
        [string[]] $Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        Fail "Command failed with exit code $LASTEXITCODE`: $FilePath $($Arguments -join ' ')"
    }
}

function Invoke-CommandCapture {
    param(
        [Parameter(Mandatory = $true)]
        [string] $FilePath,

        [Parameter(ValueFromRemainingArguments = $true)]
        [string[]] $Arguments
    )

    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $output = & $FilePath @Arguments 2>&1
        [PSCustomObject]@{
            ExitCode = $LASTEXITCODE
            Output = ($output | Out-String)
        }
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
}

function Assert-ReleaseVersion {
    param([Parameter(Mandatory = $true)][string] $Value)

    if ($Value -notmatch $ReleaseVersionPattern) {
        Fail "Release version must be a four-part numeric version such as 7.5.0.0."
    }
}

function Assert-TagAvailable {
    param([Parameter(Mandatory = $true)][string] $TagName)

    & git rev-parse -q --verify "refs/tags/$TagName" *> $null
    if ($LASTEXITCODE -eq 0) {
        Fail "Release tag already exists locally: $TagName"
    }

    $remoteTag = & git ls-remote --tags origin "refs/tags/$TagName"
    if ($LASTEXITCODE -ne 0) {
        Fail "Failed to check remote tag availability for $TagName."
    }

    if ($remoteTag) {
        Fail "Release tag already exists on origin: $TagName"
    }
}

function Assert-GitHubReleaseAvailable {
    param([Parameter(Mandatory = $true)][string] $TagName)

    $releaseCheck = Invoke-CommandCapture -FilePath 'gh' -Arguments @(
        'api',
        "repos/$RepoOwner/$RepoName/releases/tags/$TagName"
    )

    if ($releaseCheck.ExitCode -eq 0) {
        Fail "GitHub release already exists: $TagName"
    }

    if ($releaseCheck.Output -notmatch 'HTTP 404|Not Found') {
        Fail "Failed to check GitHub release availability for $TagName. $($releaseCheck.Output)"
    }
}

function Set-ProjectVersion {
    param([Parameter(Mandatory = $true)][string] $Value)

    [xml] $project = (Get-Content -Raw $ProjectPath).TrimStart([char] 0xFEFF)
    $propertyGroup = $project.Project.PropertyGroup | Select-Object -First 1
    if ($null -eq $propertyGroup) {
        Fail "No PropertyGroup found in $ProjectPath"
    }

    $propertyGroup.Version = $Value
    $project.Save($ProjectPath)
}

function Copy-ReleaseArtifact {
    param([Parameter(Mandatory = $true)][string] $AssetName)

    if (-not (Test-Path $BuiltZipPath)) {
        Fail "Build did not produce expected plugin zip: $BuiltZipPath"
    }

    New-Item -ItemType Directory -Force $DistDir | Out-Null
    $artifactPath = Join-Path $DistDir $AssetName
    Copy-Item -LiteralPath $BuiltZipPath -Destination $artifactPath -Force
    return $artifactPath
}

Set-Location $RepoRoot
Assert-ReleaseVersion -Value $Version

$tagName = "v$Version"
$assetName = "WhereIsMyHead-$Version.zip"

Invoke-Checked -FilePath 'git' -Arguments @('checkout', 'main')
Invoke-Checked -FilePath 'git' -Arguments @('pull', '--ff-only', 'origin', 'main')
Assert-TagAvailable -TagName $tagName
Assert-GitHubReleaseAvailable -TagName $tagName
Set-ProjectVersion -Value $Version
Invoke-Checked -FilePath 'dotnet' -Arguments @('test', '.\WhereIsMyHead.Tests\WhereIsMyHead.Tests.csproj', '-c', 'Debug', '--no-restore')
Invoke-Checked -FilePath 'dotnet' -Arguments @('build', $ProjectPath, '-c', 'Release', '-p:Platform=x64')
$artifactPath = Copy-ReleaseArtifact -AssetName $assetName

Invoke-Checked -FilePath 'git' -Arguments @('config', 'user.name', $RepoOwner)
Invoke-Checked -FilePath 'git' -Arguments @('config', 'user.email', '285025450+bloooowfish@users.noreply.github.com')
Invoke-Checked -FilePath 'git' -Arguments @('add', '--', $ProjectPath)
Invoke-Checked -FilePath 'git' -Arguments @('commit', '-m', "release: $Version")
Invoke-Checked -FilePath 'git' -Arguments @('tag', $tagName)
Invoke-Checked -FilePath 'git' -Arguments @('push', 'origin', $tagName)
Invoke-Checked -FilePath 'gh' -Arguments @(
    'release',
    'create',
    $tagName,
    $artifactPath,
    '--repo',
    "$RepoOwner/$RepoName",
    '--title',
    "Where Is My Head $Version",
    '--notes',
    "Where Is My Head $Version"
)
Invoke-Checked -FilePath 'git' -Arguments @('push', 'origin', 'main')

Write-Host "Released Where Is My Head $Version from GitHub Actions."
Write-Host "Artifact: $artifactPath"
