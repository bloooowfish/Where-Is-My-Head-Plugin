$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$releaseConfig = Join-Path $repoRoot 'tools\release.config.psd1'
$releaseTools = Join-Path $repoRoot 'tools\release-tools'
$releaseWorkflow = Join-Path $repoRoot '.github\workflows\release.yml'
$readme = Join-Path $repoRoot 'README.md'
$gitignore = Join-Path $repoRoot '.gitignore'

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

foreach ($path in @($releaseConfig, $releaseTools, $releaseWorkflow, $readme, $gitignore)) {
    if (-not (Test-Path $path)) {
        throw "Missing release policy path: $path"
    }
}

foreach ($removed in @(
    'tools\Release.ps1',
    'tools\Build-GitHubRelease.ps1',
    'tools\Verify-RepoIdentity.ps1'
)) {
    Assert-Equal -Actual (Test-Path (Join-Path $repoRoot $removed)) -Expected $false -Message "Duplicated release script should be removed: $removed"
}

$configText = Get-Content -Raw $releaseConfig
Assert-Match -Actual $configText -Pattern "RepoName = 'Where-Is-My-Head-Plugin'" -Message 'Release config should target the WhereIsMyHead repository.'
Assert-Match -Actual $configText -Pattern "ProjectPath = 'WhereIsMyHead\\WhereIsMyHead\.csproj'" -Message 'Release config should point at the plugin project.'
Assert-Match -Actual $configText -Pattern "TestProjectPath = 'WhereIsMyHead\.Tests\\WhereIsMyHead\.Tests\.csproj'" -Message 'Release config should point at plugin tests.'
Assert-Match -Actual $configText -Pattern "ReleaseAssetName = 'WhereIsMyHead-\{0\}\.zip'" -Message 'Release config should keep the expected asset name.'
Assert-Match -Actual $configText -Pattern '285025450\+bloooowfish@users\.noreply\.github\.com' -Message 'Release config should require subaccount noreply identity.'
Assert-Match -Actual $configText -Pattern 'github-bf:bloooowfish/Where-Is-My-Head-Plugin\.git' -Message 'Release config should accept the subaccount SSH alias remote.'
Assert-Match -Actual $configText -Pattern 'git@github-bf:bloooowfish/Where-Is-My-Head-Plugin\.git' -Message 'Release config should accept the existing scp-style subaccount remote.'

$workflowText = Get-Content -Raw $releaseWorkflow
Assert-Match -Actual $workflowText -Pattern 'workflow_dispatch' -Message 'Release workflow should be manually triggerable.'
Assert-Match -Actual $workflowText -Pattern 'run-name: Release \$\{\{ inputs\.version \}\} \$\{\{ inputs\.correlation_id \}\}' -Message 'Release workflow should expose correlation ids in the run name.'
Assert-Match -Actual $workflowText -Pattern 'fetch-depth:\s*0' -Message 'Release workflow should preserve full checkout history.'
Assert-Match -Actual $workflowText -Pattern 'submodules:\s*true' -Message 'Release workflow should checkout shared release-tools submodule.'
Assert-Match -Actual $workflowText -Pattern 'ref:\s*main' -Message 'Release workflow should check out main for release commits.'
Assert-Match -Actual $workflowText -Pattern 'goatcorp\.github\.io/dalamud-distrib/stg/latest\.zip' -Message 'Release workflow should install Dalamud dev files on the runner.'
Assert-Match -Actual $workflowText -Pattern 'tools\\release-tools\\Build-GitHubRelease\.ps1' -Message 'Release workflow should delegate build and publish work to shared release tools.'
Assert-Match -Actual $workflowText -Pattern 'tools\\release\.config\.psd1' -Message 'Release workflow should pass the repo release config.'
Assert-NotMatch -Actual $workflowText -Pattern '-File\s+tools\\Build-GitHubRelease\.ps1' -Message 'Release workflow should not call the old repo-local build script.'
Assert-NotMatch -Actual $workflowText -Pattern 'MASTER_REPO_DISPATCH_TOKEN' -Message 'Release workflow should not require cross-repository dispatch secrets.'
Assert-NotMatch -Actual $workflowText -Pattern 'repos/bloooowfish/MyPluginMaster/dispatches' -Message 'Release workflow should not notify MyPluginMaster directly.'
Assert-NotMatch -Actual $workflowText -Pattern 'event_type=plugin-release' -Message 'Release workflow should not use repository_dispatch events.'

Assert-Equal -Actual (Test-Path (Join-Path $repoRoot 'repo.json')) -Expected $false -Message 'Plugin repository should not keep a standalone repo.json; MyPluginMaster owns the custom repository manifest.'

$readmeText = Get-Content -Raw $readme
Assert-Match -Actual $readmeText -Pattern 'https://raw\.githubusercontent\.com/bloooowfish/MyPluginMaster/refs/heads/main/repo\.json' -Message 'README should publish the master custom repository URL.'
Assert-NotMatch -Actual $readmeText -Pattern 'https://raw\.githubusercontent\.com/bloooowfish/Where-Is-My-Head-Plugin/.+repo\.json' -Message 'README should not publish a standalone plugin repository URL.'

$gitignoreText = Get-Content -Raw $gitignore
Assert-Match -Actual $gitignoreText -Pattern 'dist/' -Message 'Release build output directory should be ignored.'
Assert-Match -Actual $gitignoreText -Pattern 'release-bf\.local\.cmd' -Message 'Local release helper should remain ignored.'

Write-Host 'Release script tests passed.'
