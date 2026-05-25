param(
    [Parameter(Mandatory = $true)]
    [string] $Version,

    [switch] $ValidateOnly,

    [switch] $PreflightOnly,

    [switch] $SkipGitHubRelease
)

$ErrorActionPreference = 'Stop'

$RepoOwner = 'bloooowfish'
$RepoName = 'Where-Is-My-Head-Plugin'
$RemoteName = 'origin'
$WorkflowFile = 'release.yml'
$ReleaseVersionPattern = '^\d+\.\d+\.\d+\.\d+$'

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$IdentityScriptPath = Join-Path $RepoRoot 'tools\Verify-RepoIdentity.ps1'

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

function Invoke-ScalarCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string] $FilePath,

        [Parameter(Mandatory = $true)]
        [string] $FailureMessage,

        [Parameter(ValueFromRemainingArguments = $true)]
        [string[]] $Arguments
    )

    $output = & $FilePath @Arguments
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        Fail $FailureMessage
    }

    $value = ($output | Select-Object -First 1)
    if ($null -eq $value -or [string]::IsNullOrWhiteSpace([string] $value)) {
        Fail $FailureMessage
    }

    return ([string] $value).Trim()
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

function Assert-RepoIdentity {
    if (-not (Test-Path $IdentityScriptPath)) {
        Fail "Missing identity verification script: $IdentityScriptPath"
    }

    Invoke-Checked -FilePath 'powershell' -Arguments @(
        '-NoProfile',
        '-ExecutionPolicy',
        'Bypass',
        '-File',
        $IdentityScriptPath
    )
}

function Assert-CleanGitWorkingTree {
    $status = & git -C $RepoRoot status --porcelain
    if ($LASTEXITCODE -ne 0) {
        Fail 'Failed to read git status.'
    }

    if ($status) {
        Fail 'Release trigger requires a clean working tree.'
    }
}

function Assert-OnMainBranch {
    $branch = Invoke-ScalarCommand -FilePath 'git' -FailureMessage 'Failed to read current git branch.' -Arguments @(
        '-C',
        $RepoRoot,
        'rev-parse',
        '--abbrev-ref',
        'HEAD'
    )

    if ($branch -ne 'main') {
        Fail "Release must be triggered from main. Current branch: $branch"
    }
}

function Assert-BranchSynchronized {
    Invoke-Checked -FilePath 'git' -Arguments @('-C', $RepoRoot, 'fetch', $RemoteName, 'main')

    $head = Invoke-ScalarCommand -FilePath 'git' -FailureMessage 'Failed to read local HEAD.' -Arguments @(
        '-C',
        $RepoRoot,
        'rev-parse',
        'HEAD'
    )

    $remoteHead = Invoke-ScalarCommand -FilePath 'git' -FailureMessage "Failed to read $RemoteName/main." -Arguments @(
        '-C',
        $RepoRoot,
        'rev-parse',
        "$RemoteName/main"
    )

    if ($head -ne $remoteHead) {
        Fail "Release trigger requires local main to match $RemoteName/main."
    }
}

function Assert-TagAvailable {
    param([Parameter(Mandatory = $true)][string] $TagName)

    & git -C $RepoRoot rev-parse -q --verify "refs/tags/$TagName" *> $null
    if ($LASTEXITCODE -eq 0) {
        Fail "Release tag already exists locally: $TagName"
    }

    $remoteTag = & git -C $RepoRoot ls-remote --tags $RemoteName "refs/tags/$TagName"
    if ($LASTEXITCODE -ne 0) {
        Fail "Failed to check remote tag availability for $TagName."
    }

    if ($remoteTag) {
        Fail "Release tag already exists on $RemoteName`: $TagName"
    }
}

function Assert-GitHubCliIdentity {
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
        Fail 'GitHub CLI is required to trigger the release workflow.'
    }

    $login = Invoke-ScalarCommand -FilePath 'gh' -FailureMessage 'GitHub CLI is not authenticated.' -Arguments @(
        'api',
        'user',
        '--jq',
        '.login'
    )

    if ($login -ne $RepoOwner) {
        Fail "GitHub CLI is authenticated as '$login', but this repository must release as '$RepoOwner'."
    }
}

function Assert-GitHubReleaseAvailable {
    param([Parameter(Mandatory = $true)][string] $TagName)

    $repoFullName = Invoke-ScalarCommand -FilePath 'gh' -FailureMessage "Failed to verify GitHub repository $RepoOwner/$RepoName." -Arguments @(
        'api',
        "repos/$RepoOwner/$RepoName",
        '--jq',
        '.full_name'
    )

    if ($repoFullName -ne "$RepoOwner/$RepoName") {
        Fail "Failed to verify GitHub repository $RepoOwner/$RepoName."
    }

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

function Assert-WorkflowAvailable {
    Invoke-Checked -FilePath 'gh' -Arguments @(
        'workflow',
        'view',
        $WorkflowFile,
        '--repo',
        "$RepoOwner/$RepoName"
    )
}

function Invoke-ReleaseWorkflow {
    Invoke-Checked -FilePath 'gh' -Arguments @(
        'workflow',
        'run',
        $WorkflowFile,
        '--repo',
        "$RepoOwner/$RepoName",
        '--ref',
        'main',
        '-f',
        "version=$Version"
    )
}

Set-Location $RepoRoot
Assert-ReleaseVersion -Value $Version
Assert-RepoIdentity

if (-not $SkipGitHubRelease) {
    Assert-GitHubCliIdentity
}

if ($ValidateOnly) {
    Write-Host "Release validation passed for $Version."
    exit 0
}

if ($SkipGitHubRelease) {
    Fail 'SkipGitHubRelease is only supported with ValidateOnly.'
}

$tagName = "v$Version"

Assert-CleanGitWorkingTree
Assert-OnMainBranch
Assert-BranchSynchronized
Assert-TagAvailable -TagName $tagName
Assert-GitHubReleaseAvailable -TagName $tagName
Assert-WorkflowAvailable

if ($PreflightOnly) {
    Write-Host "Release workflow preflight passed for $Version."
    exit 0
}

Invoke-ReleaseWorkflow
Write-Host "Queued GitHub Actions release workflow for $Version."
Write-Host "View runs: https://github.com/$RepoOwner/$RepoName/actions/workflows/$WorkflowFile"
