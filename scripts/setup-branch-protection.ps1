<#
.SYNOPSIS
    Creates or updates the "main-protection" repository ruleset (ADR-0001, issue #3).

.DESCRIPTION
    Applies docs/adr/assets/main-ruleset.json to the repository through the GitHub REST API (gh api):
      - pull request required, 0 approval (single shared account), review threads resolved;
      - required checks: "Gitleaks (full history)", "TruffleHog (verified secrets)", "Forbidden files guard";
      - linear history, no force-push, no branch deletion;
      - repository admins may bypass only through a pull request (never by a direct push).
    The script is idempotent: it updates the ruleset if one with the same name exists, otherwise creates it.
    It only touches rulesets; no other repository setting is changed.

.PARAMETER Repo
    Target repository as "owner/name".

.PARAMETER RulesetPath
    Path to the ruleset JSON definition (versioned with the ADR).

.EXAMPLE
    ./scripts/setup-branch-protection.ps1 -WhatIf
    Shows what would be done without calling any write endpoint.

.EXAMPLE
    ./scripts/setup-branch-protection.ps1
    Creates or updates the ruleset on Yvahajatiana/counterparty-risk.

.NOTES
    Requires the GitHub CLI (gh) authenticated with an account that is admin of the repository.
#>
#Requires -Version 7.0
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [ValidatePattern('^[A-Za-z0-9-]+/[A-Za-z0-9._-]+$')]
    [string] $Repo = 'Yvahajatiana/counterparty-risk',

    [string] $RulesetPath = (Join-Path $PSScriptRoot '..' 'docs' 'adr' 'assets' 'main-ruleset.json')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-Gh {
    # Runs gh with an argument array (no string interpolation into a shell) and fails on non-zero exit
    param([Parameter(Mandatory = $true)] [string[]] $Arguments)
    $output = & gh @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "gh $($Arguments -join ' ') failed with exit code $LASTEXITCODE"
    }
    return $output
}

# Prerequisites
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw 'GitHub CLI (gh) not found. Install it with: winget install GitHub.cli'
}
& gh auth status *> $null
if ($LASTEXITCODE -ne 0) {
    throw 'gh is not authenticated. Run: gh auth login'
}

$RulesetPath = (Resolve-Path -LiteralPath $RulesetPath).Path
$ruleset = Get-Content -LiteralPath $RulesetPath -Raw | ConvertFrom-Json
$name = $ruleset.name
if ([string]::IsNullOrWhiteSpace($name)) {
    throw "Ruleset definition $RulesetPath has no 'name'."
}

$checks = $ruleset.rules |
    Where-Object { $_.type -eq 'required_status_checks' } |
    ForEach-Object { $_.parameters.required_status_checks.context }
Write-Host "Repository     : $Repo"
Write-Host "Ruleset        : $name ($RulesetPath)"
Write-Host "Required checks: $($checks -join ', ')"

# Look for an existing ruleset with the same name (idempotency)
$existing = Invoke-Gh @('api', "repos/$Repo/rulesets?per_page=100") | ConvertFrom-Json
$match = @($existing | Where-Object { $_.name -eq $name })
if ($match.Count -gt 1) {
    throw "Several rulesets named '$name' exist on $Repo; clean them up manually first."
}

if ($match.Count -eq 1) {
    $id = $match[0].id
    if ($PSCmdlet.ShouldProcess("$Repo ruleset $id", "Update ruleset '$name'")) {
        $result = Invoke-Gh @('api', '--method', 'PUT', "repos/$Repo/rulesets/$id", '--input', $RulesetPath) | ConvertFrom-Json
        Write-Host "Updated ruleset '$($result.name)' (id $($result.id), enforcement $($result.enforcement))."
    }
}
else {
    if ($PSCmdlet.ShouldProcess($Repo, "Create ruleset '$name'")) {
        $result = Invoke-Gh @('api', '--method', 'POST', "repos/$Repo/rulesets", '--input', $RulesetPath) | ConvertFrom-Json
        Write-Host "Created ruleset '$($result.name)' (id $($result.id), enforcement $($result.enforcement))."
    }
}

# Show the rules that now apply to the default branch
if (-not $WhatIfPreference) {
    $defaultBranch = (Invoke-Gh @('api', "repos/$Repo", '--jq', '.default_branch'))
    Write-Host "Rules active on '$defaultBranch':"
    Invoke-Gh @('api', "repos/$Repo/rules/branches/$defaultBranch", '--jq', '.[].type') |
        ForEach-Object { Write-Host "  - $_" }
}
