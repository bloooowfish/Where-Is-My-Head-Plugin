$ErrorActionPreference = 'Stop'

function Fail([string] $Message) {
    Write-Error $Message
    exit 1
}

$repoRoot = (& git rev-parse --show-toplevel).Trim()
if ([string]::IsNullOrWhiteSpace($repoRoot)) {
    Fail 'Unable to resolve git repository root.'
}

Push-Location $repoRoot
try {
    $expectedName = 'bloooowfish'
    $expectedEmail = '285025450+bloooowfish@users.noreply.github.com'
    $expectedOrigins = @(
        'github-bf:bloooowfish/Where-Is-My-Head-Plugin.git',
        'git@github-bf:bloooowfish/Where-Is-My-Head-Plugin.git'
    )

    $name = (& git config --local user.name).Trim()
    if ($name -ne $expectedName) {
        Fail "Unexpected local git user.name: '$name'. Expected '$expectedName'."
    }

    $email = (& git config --local user.email).Trim()
    if ($email -ne $expectedEmail) {
        Fail "Unexpected local git user.email: '$email'. Expected '$expectedEmail'."
    }

    $origin = (& git remote get-url origin).Trim()
    if ($origin -notin $expectedOrigins) {
        Fail "Unexpected origin remote: '$origin'. Expected one of: $($expectedOrigins -join ', ')."
    }

    $remotes = & git remote -v
    if ($remotes -match 'https?://') {
        Fail "HTTPS remote detected:`n$($remotes -join [Environment]::NewLine)"
    }

    $forbidden = @(
        ('Ayu' + 'mudayo'),
        ('yu' + 'memi'),
        ('github' + '-main'),
        ('github.com/' + 'local'),
        ('MA' + 'RU')
    )
    $pattern = ($forbidden | ForEach-Object { [regex]::Escape($_) }) -join '|'

    $rg = Get-Command rg -ErrorAction SilentlyContinue
    if ($null -eq $rg) {
        Fail 'ripgrep (rg) is required for identity scanning.'
    }

    & $rg.Source -n -i $pattern . `
        -g '!*bin*' `
        -g '!*obj*' `
        -g '!Reference/**' `
        -g '!.vs/**'
    $scanExitCode = $LASTEXITCODE
    if ($scanExitCode -eq 0) {
        Fail 'Forbidden main-account identity string found in repository source.'
    }

    if ($scanExitCode -ne 1) {
        Fail "Identity scan failed with rg exit code $scanExitCode."
    }

    Write-Host "Repo identity verified:"
    Write-Host "  user.name  = $name"
    Write-Host "  user.email = $email"
    Write-Host "  origin     = $origin"
    Write-Host "  remotes    = SSH-only"
    Write-Host "  scan       = no forbidden identity strings"
}
finally {
    Pop-Location
}
