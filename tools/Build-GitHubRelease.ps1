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
$BuiltManifestPath = Join-Path $BuiltPluginDir 'WhereIsMyHead.json'
$BuiltZipPath = Join-Path $BuiltPluginDir 'latest.zip'
$DistDir = Join-Path $RepoRoot 'dist'
$RepoJsonPath = Join-Path $RepoRoot 'repo.json'

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
        Fail "Release version must be a four-part numeric version such as 0.1.0.0."
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

function Read-JsonFile {
    param([Parameter(Mandatory = $true)][string] $Path)

    if (-not (Test-Path $Path)) {
        Fail "Missing JSON file: $Path"
    }

    Get-Content -Raw $Path | ConvertFrom-Json
}

function Get-GitHubZipDownloadCount {
    $counts = & gh api "repos/$RepoOwner/$RepoName/releases" --paginate --jq '.[].assets[]? | select(.name | endswith(".zip")) | .download_count'
    if ($LASTEXITCODE -ne 0) {
        Fail 'Failed to read GitHub release download counts.'
    }

    $total = 0
    foreach ($count in $counts) {
        if (-not [string]::IsNullOrWhiteSpace($count)) {
            $total += [int] $count
        }
    }

    return $total
}

function Write-RepoJson {
    param(
        [Parameter(Mandatory = $true)]
        [object] $Manifest,

        [Parameter(Mandatory = $true)]
        [int] $DownloadCount,

        [Parameter(Mandatory = $true)]
        [string] $AssetName
    )

    $tagName = "v$Version"
    $downloadUrl = "https://github.com/$RepoOwner/$RepoName/releases/download/$tagName/$AssetName"
    $now = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds().ToString()

    $entry = [ordered]@{
        Author = $Manifest.Author
        Name = $Manifest.Name
        InternalName = $Manifest.InternalName
        AssemblyVersion = $Version
        TestingAssemblyVersion = $null
        Description = $Manifest.Description
        Punchline = $Manifest.Punchline
        ApplicableVersion = $Manifest.ApplicableVersion
        Tags = @($Manifest.Tags)
        RepoUrl = "https://github.com/$RepoOwner/$RepoName"
        DalamudApiLevel = $Manifest.DalamudApiLevel
        TestingDalamudApiLevel = $null
        IsHide = $false
        IsTestingExclusive = $false
        DownloadCount = $DownloadCount
        DownloadLinkInstall = $downloadUrl
        DownloadLinkTesting = $null
        DownloadLinkUpdate = $downloadUrl
        LastUpdate = $now
    }

    if ($null -eq $entry.InternalName -or [string]::IsNullOrWhiteSpace([string] $entry.InternalName)) {
        $entry.InternalName = 'WhereIsMyHead'
    }

    if ($null -eq $entry.ApplicableVersion -or [string]::IsNullOrWhiteSpace([string] $entry.ApplicableVersion)) {
        $entry.ApplicableVersion = 'any'
    }

    if ($null -eq $entry.DalamudApiLevel) {
        $entry.DalamudApiLevel = 15
    }

    $json = ConvertTo-Json -InputObject @($entry) -Depth 8
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($RepoJsonPath, $json + [Environment]::NewLine, $utf8NoBom)
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
$manifest = Read-JsonFile -Path $BuiltManifestPath
$downloadCount = Get-GitHubZipDownloadCount
Write-RepoJson -Manifest $manifest -DownloadCount $downloadCount -AssetName $assetName

Invoke-Checked -FilePath 'git' -Arguments @('config', 'user.name', $RepoOwner)
Invoke-Checked -FilePath 'git' -Arguments @('config', 'user.email', '285025450+bloooowfish@users.noreply.github.com')
Invoke-Checked -FilePath 'git' -Arguments @('add', '--', $ProjectPath, $RepoJsonPath)
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
Write-Host "DownloadCount snapshot: $downloadCount"
