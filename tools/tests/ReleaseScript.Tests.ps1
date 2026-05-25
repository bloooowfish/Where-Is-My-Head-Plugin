$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$releaseScript = Join-Path $repoRoot 'tools\Release.ps1'
$githubBuildScript = Join-Path $repoRoot 'tools\Build-GitHubRelease.ps1'
$identityScript = Join-Path $repoRoot 'tools\Verify-RepoIdentity.ps1'
$releaseWorkflow = Join-Path $repoRoot '.github\workflows\release.yml'
$readme = Join-Path $repoRoot 'README.md'
$gitignore = Join-Path $repoRoot '.gitignore'

function Invoke-ReleaseScript {
    param([Parameter(Mandatory = $true)][string[]] $Arguments)

    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $output = & powershell -NoProfile -ExecutionPolicy Bypass -File $releaseScript @Arguments 2>&1
        [PSCustomObject]@{
            ExitCode = $LASTEXITCODE
            Output = ($output | Out-String).Trim()
        }
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
}

function Assert-Equal {
    param(
        [Parameter(Mandatory = $true)][object] $Actual,
        [Parameter(Mandatory = $true)][object] $Expected,
        [Parameter(Mandatory = $true)][string] $Message
    )

    if ($Actual -ne $Expected) {
        throw "$Message Expected=[$Expected] Actual=[$Actual]"
    }
}

function Assert-Match {
    param(
        [Parameter(Mandatory = $true)][string] $Actual,
        [Parameter(Mandatory = $true)][string] $Pattern,
        [Parameter(Mandatory = $true)][string] $Message
    )

    if ($Actual -notmatch $Pattern) {
        throw "$Message Pattern=[$Pattern] Actual=[$Actual]"
    }
}

function Assert-NotMatch {
    param(
        [Parameter(Mandatory = $true)][string] $Actual,
        [Parameter(Mandatory = $true)][string] $Pattern,
        [Parameter(Mandatory = $true)][string] $Message
    )

    if ($Actual -match $Pattern) {
        throw "$Message Pattern=[$Pattern] Actual=[$Actual]"
    }
}

foreach ($path in @($releaseScript, $githubBuildScript, $identityScript, $releaseWorkflow, $readme, $gitignore)) {
    if (-not (Test-Path $path)) {
        throw "Missing release policy file: $path"
    }
}

$valid = Invoke-ReleaseScript -Arguments @('-Version', '7.5.9999.9999', '-ValidateOnly', '-SkipGitHubRelease')
Assert-Equal -Actual $valid.ExitCode -Expected 0 -Message 'ValidateOnly should accept four-part release versions.'
Assert-Match -Actual $valid.Output -Pattern '7\.5\.9999\.9999' -Message 'ValidateOnly output should mention the accepted version.'

$invalid = Invoke-ReleaseScript -Arguments @('-Version', '7.5', '-ValidateOnly', '-SkipGitHubRelease')
Assert-Equal -Actual $invalid.ExitCode -Expected 1 -Message 'ValidateOnly should reject non-four-part release versions.'
Assert-Match -Actual $invalid.Output -Pattern 'four-part' -Message 'Invalid version output should explain the required format.'

$skipRelease = Invoke-ReleaseScript -Arguments @('-Version', '7.5.9999.9999', '-SkipGitHubRelease')
Assert-Equal -Actual $skipRelease.ExitCode -Expected 1 -Message 'Real releases should reject SkipGitHubRelease.'
Assert-Match -Actual $skipRelease.Output -Pattern 'only supported with ValidateOnly' -Message 'SkipGitHubRelease failure should explain the supported mode.'

$releaseScriptText = Get-Content -Raw $releaseScript
Assert-Match -Actual $releaseScriptText -Pattern '\$RepoName = ''Where-Is-My-Head-Plugin''' -Message 'Release trigger should target the WhereIsMyHead repository.'
Assert-Match -Actual $releaseScriptText -Pattern 'Assert-OnMainBranch' -Message 'Release script should guard against non-main releases.'
Assert-Match -Actual $releaseScriptText -Pattern 'Assert-BranchSynchronized' -Message 'Release script should require local main to match origin/main.'
Assert-Match -Actual $releaseScriptText -Pattern 'Assert-TagAvailable' -Message 'Release script should reject duplicate tags before publishing.'
Assert-Match -Actual $releaseScriptText -Pattern 'Assert-GitHubReleaseAvailable' -Message 'Release script should reject duplicate GitHub releases before publishing.'
Assert-Match -Actual $releaseScriptText -Pattern 'Release workflow preflight passed' -Message 'Release script should support a no-mutation workflow preflight mode.'
Assert-Match -Actual $releaseScriptText -Pattern 'workflow\s*'',\s*''run' -Message 'Release script should trigger the GitHub Actions workflow.'
Assert-Match -Actual $releaseScriptText -Pattern '\$MasterRepoName = ''MyPluginMaster''' -Message 'Release script should know the master repository to update after release.'
Assert-Match -Actual $releaseScriptText -Pattern '\$MasterWorkflowFile = ''update-repo\.yml''' -Message 'Release script should target the master update workflow.'
Assert-Match -Actual $releaseScriptText -Pattern 'Invoke-WorkflowAndWait' -Message 'Release script should wait for release and master workflows to complete.'
Assert-Match -Actual $releaseScriptText -Pattern 'gh run list' -Message 'Release script should discover the queued workflow run.'
Assert-Match -Actual $releaseScriptText -Pattern 'displayTitle' -Message 'Release script should match workflow runs by display title correlation.'
Assert-Match -Actual $releaseScriptText -Pattern '\[Guid\]::NewGuid' -Message 'Release script should generate unique workflow correlation ids.'
Assert-Match -Actual $releaseScriptText -Pattern 'correlation_id=' -Message 'Release script should pass correlation ids into workflow_dispatch inputs.'
Assert-Match -Actual $releaseScriptText -Pattern 'run'',\s*''watch' -Message 'Release script should watch workflow completion before continuing.'
Assert-Match -Actual $releaseScriptText -Pattern 'Release and master repository update completed' -Message 'Release script should report completion after both workflows finish.'
Assert-Match -Actual $releaseScriptText -Pattern 'function Invoke-ScalarCommand' -Message 'Release script should use a scalar native-command helper.'
Assert-Match -Actual $releaseScriptText -Pattern 'function Invoke-CommandCapture' -Message 'Release script should capture expected native-command failures without terminating.'
Assert-NotMatch -Actual $releaseScriptText -Pattern 'dotnet\s*'',\s*''build' -Message 'Release trigger script should not build locally.'
Assert-NotMatch -Actual $releaseScriptText -Pattern 'Set-ProjectVersion' -Message 'Release trigger script should not mutate local version metadata.'
Assert-NotMatch -Actual $releaseScriptText -Pattern 'rev-parse\s+HEAD\s*\|' -Message 'Release script should not pipe git rev-parse HEAD before checking native exit codes.'
Assert-NotMatch -Actual $releaseScriptText -Pattern 'gh api user --jq \.login.*\|' -Message 'Release script should not pipe gh identity checks before checking native exit codes.'

$githubBuildScriptText = Get-Content -Raw $githubBuildScript
Assert-Match -Actual $githubBuildScriptText -Pattern '\$RepoName = ''Where-Is-My-Head-Plugin''' -Message 'GitHub build script should target the WhereIsMyHead repository.'
Assert-Match -Actual $githubBuildScriptText -Pattern "WhereIsMyHead\\WhereIsMyHead\.csproj" -Message 'GitHub build script should update the WhereIsMyHead project version.'
Assert-Match -Actual $githubBuildScriptText -Pattern 'WhereIsMyHead-\$Version\.zip' -Message 'GitHub build script should publish the expected asset name.'
Assert-Match -Actual $githubBuildScriptText -Pattern 'Set-ProjectVersion' -Message 'GitHub build script should update the project version in CI.'
Assert-Match -Actual $githubBuildScriptText -Pattern "FilePath 'dotnet'.+?'build'" -Message 'GitHub build script should build the plugin in CI.'
Assert-Match -Actual $githubBuildScriptText -Pattern 'release'',\s*''create' -Message 'GitHub build script should create the GitHub release asset in CI.'
Assert-Match -Actual $githubBuildScriptText -Pattern "'checkout', 'main'" -Message 'GitHub build script should commit from the main branch, not detached HEAD.'
Assert-Match -Actual $githubBuildScriptText -Pattern 'Assert-TagAvailable' -Message 'GitHub build script should re-check tag availability in CI.'
Assert-Match -Actual $githubBuildScriptText -Pattern 'Assert-GitHubReleaseAvailable' -Message 'GitHub build script should re-check release availability in CI.'
Assert-NotMatch -Actual $githubBuildScriptText -Pattern 'RepoJsonPath|Write-RepoJson|Get-GitHubZipDownloadCount|DownloadCount|ConvertTo-Json' -Message 'GitHub build script should not generate plugin-store repo.json; MyPluginMaster owns repo metadata.'
Assert-NotMatch -Actual $githubBuildScriptText -Pattern 'git'',\s*''add'',\s*''--'',\s*\$ProjectPath,\s*\$RepoJsonPath' -Message 'GitHub build script should only commit project version metadata.'

$identityScriptText = Get-Content -Raw $identityScript
Assert-Match -Actual $identityScriptText -Pattern "github-bf:bloooowfish/Where-Is-My-Head-Plugin\.git" -Message 'Identity script should require the subaccount SSH alias remote.'
Assert-Match -Actual $identityScriptText -Pattern '285025450\+bloooowfish@users\.noreply\.github\.com' -Message 'Identity script should require the subaccount noreply address.'

Assert-Equal -Actual (Test-Path (Join-Path $repoRoot 'repo.json')) -Expected $false -Message 'Plugin repository should not keep a standalone repo.json; MyPluginMaster owns the custom repository manifest.'

$readmeText = Get-Content -Raw $readme
Assert-Match -Actual $readmeText -Pattern 'https://raw\.githubusercontent\.com/bloooowfish/MyPluginMaster/refs/heads/main/repo\.json' -Message 'README should publish the master custom repository URL.'
Assert-NotMatch -Actual $readmeText -Pattern 'https://raw\.githubusercontent\.com/bloooowfish/Where-Is-My-Head-Plugin/.+repo\.json' -Message 'README should not publish a standalone plugin repository URL.'

$releaseWorkflowText = Get-Content -Raw $releaseWorkflow
Assert-Match -Actual $releaseWorkflowText -Pattern 'workflow_dispatch' -Message 'Release workflow should be manually triggerable.'
Assert-Match -Actual $releaseWorkflowText -Pattern 'run-name: Release \$\{\{ inputs\.version \}\} \$\{\{ inputs\.correlation_id \}\}' -Message 'Release workflow should expose correlation ids in the run name.'
Assert-Match -Actual $releaseWorkflowText -Pattern 'correlation_id:' -Message 'Release workflow should accept an orchestration correlation id.'
Assert-Match -Actual $releaseWorkflowText -Pattern 'ref:\s*main' -Message 'Release workflow should check out main for release commits.'
Assert-Match -Actual $releaseWorkflowText -Pattern 'goatcorp\.github\.io/dalamud-distrib/stg/latest\.zip' -Message 'Release workflow should install Dalamud dev files on the runner.'
Assert-Match -Actual $releaseWorkflowText -Pattern 'Build-GitHubRelease\.ps1' -Message 'Release workflow should delegate build and publish work to the CI script.'
Assert-NotMatch -Actual $releaseWorkflowText -Pattern 'MASTER_REPO_DISPATCH_TOKEN' -Message 'Release workflow should not require cross-repository dispatch secrets.'
Assert-NotMatch -Actual $releaseWorkflowText -Pattern 'repos/bloooowfish/MyPluginMaster/dispatches' -Message 'Release workflow should not notify MyPluginMaster directly.'
Assert-NotMatch -Actual $releaseWorkflowText -Pattern 'event_type=plugin-release' -Message 'Release workflow should not use repository_dispatch events.'

$gitignoreText = Get-Content -Raw $gitignore
Assert-Match -Actual $gitignoreText -Pattern 'dist/' -Message 'Release build output directory should be ignored.'
Assert-Match -Actual $gitignoreText -Pattern 'release-bf\.local\.cmd' -Message 'Local release helper should be ignored.'

Write-Host 'Release script tests passed.'
