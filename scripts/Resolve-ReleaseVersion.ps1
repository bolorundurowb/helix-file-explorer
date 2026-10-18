#requires -Version 7.0
<#
.SYNOPSIS
Shared release-version decision logic for Helix Explorer's tag releases.

.DESCRIPTION
Used by .github/workflows/release-on-tag.yml so the tag caller applies one set
of version-parsing, make_latest, and props-sync rules instead of duplicating
them. release-installers.yml (the reusable publisher) does not call this
script: its tag/build-commit checks are a publish-time concern, not a
version-classification one.

A tag that is already a non-draft release is a no-op. That check uses the
published release, not a commit diff, so a re-run of the same tag does not
publish twice and a failed publish (still a draft, or no release at all) can
be retried.

Prints exactly one JSON object to stdout and makes no other side effects, so a
caller step parses the decision with `| ConvertFrom-Json`, and the same
invocation serves as a local dry run against the real repository.

Run -Mode Tag after `actions/checkout` with `fetch-depth: 0`. -Mode TagPropsSync
only needs Directory.Build.props on the commit it is comparing against.

.EXAMPLE
./Resolve-ReleaseVersion.ps1 -Mode Tag -Repo bolorundurowb/helix-file-explorer -TagName v1.4.4-preview.2

.EXAMPLE
./Resolve-ReleaseVersion.ps1 -Mode TagPropsSync -TagName v1.4.4 -PropsPath Directory.Build.props
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('Tag', 'TagPropsSync')]
    [string]$Mode,

    [string]$Repo = $env:GITHUB_REPOSITORY,

    [string]$PropsPath = 'Directory.Build.props',

    # Required for both modes: the pushed tag name, e.g. v1.4.4-preview.2.
    [string]$TagName
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# ---------------------------------------------------------------------------
# SemVer parsing
# ---------------------------------------------------------------------------

# Leading zeros are rejected on purpose: Helix Explorer has never produced a
# version like "01.4.3", and silently coercing one would hide a typo in
# Directory.Build.props instead of failing the run.
$script:VersionPattern = '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-preview\.([1-9]\d*))?$'

function ConvertFrom-HelixVersion {
    param([Parameter(Mandatory)][string]$Value)

    $trimmed = $Value.Trim().TrimStart('v', 'V')
    if ($trimmed -notmatch $script:VersionPattern) {
        throw "'$Value' is not a valid Helix Explorer version (expected MAJOR.MINOR.PATCH or MAJOR.MINOR.PATCH-preview.N, no leading zeros)."
    }

    [pscustomobject]@{
        Major    = [int]$Matches[1]
        Minor    = [int]$Matches[2]
        Patch    = [int]$Matches[3]
        Preview  = if ($Matches[4]) { [int]$Matches[4] } else { $null }
        IsStable = -not $Matches[4]
        Raw      = $trimmed
    }
}

function Get-HelixVersionTag {
    param([Parameter(Mandatory)][psobject]$Version)
    "v$($Version.Raw)"
}

function Compare-HelixVersion {
    # Returns -1/0/1. A stable release always outranks a preview on the same
    # MAJOR.MINOR.PATCH base, since shipping stable supersedes that preview.
    param(
        [Parameter(Mandatory)][psobject]$Left,
        [Parameter(Mandatory)][psobject]$Right
    )

    foreach ($part in 'Major', 'Minor', 'Patch') {
        if ($Left.$part -ne $Right.$part) {
            return [math]::Sign($Left.$part - $Right.$part)
        }
    }

    if ($Left.IsStable -and $Right.IsStable) { return 0 }
    if ($Left.IsStable) { return 1 }
    if ($Right.IsStable) { return -1 }
    return [math]::Sign($Left.Preview - $Right.Preview)
}

# ---------------------------------------------------------------------------
# GitHub release data
# ---------------------------------------------------------------------------

function Get-HelixRepoSlug {
    param([string]$Explicit)

    if ($Explicit) { return $Explicit }

    $url = git remote get-url origin 2>$null
    if ($url -and $url -match '[:/](?<owner>[^/]+)/(?<repo>[^/.]+?)(\.git)?$') {
        return "$($Matches.owner)/$($Matches.repo)"
    }

    throw "Could not determine the repository slug; pass -Repo owner/name or set GITHUB_REPOSITORY."
}

function Get-AllReleases {
    # `gh release list` caps at 30 by default. `--paginate --jq '.[]'` emits one
    # release object per line across every page, so pages never need to be
    # stitched back together.
    param([Parameter(Mandatory)][string]$Repo)

    $lines = gh api "repos/$Repo/releases" --paginate --jq '.[]' 2>$null
    if (-not $lines) { return @() }

    return @($lines | ForEach-Object { $_ | ConvertFrom-Json -Depth 10 })
}

# ---------------------------------------------------------------------------
# make_latest resolution
# ---------------------------------------------------------------------------

function Get-MakeLatest {
    # A hotfix tag published after a newer stable tag must never become
    # "latest", or the in-app updater would offer a downgrade. Previews are
    # never latest.
    param(
        [Parameter(Mandatory)][psobject]$Candidate,
        [Parameter(Mandatory)][array]$Releases
    )

    if (-not $Candidate.IsStable) { return $false }

    $currentLatest = $null
    foreach ($release in $Releases) {
        if ($release.draft -or $release.prerelease) { continue }

        $parsed = ConvertFrom-HelixVersion $release.tag_name
        if (-not $currentLatest -or (Compare-HelixVersion $parsed $currentLatest) -gt 0) {
            $currentLatest = $parsed
        }
    }

    if (-not $currentLatest) { return $true }
    return (Compare-HelixVersion $Candidate $currentLatest) -gt 0
}

# ---------------------------------------------------------------------------
# Directory.Build.props
# ---------------------------------------------------------------------------

function Read-PropsVersion {
    param([Parameter(Mandatory)][string]$Path)

    [xml]$props = Get-Content $Path
    $value = $props.SelectSingleNode('/Project/PropertyGroup/Version')?.InnerText
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "$Path must define Project/PropertyGroup/Version."
    }
    return $value.Trim()
}

# ---------------------------------------------------------------------------
# Entry points
# ---------------------------------------------------------------------------

function Invoke-TagDecision {
    param(
        [Parameter(Mandatory)][string]$Repo,
        [Parameter(Mandatory)][string]$TagName
    )

    $version = ConvertFrom-HelixVersion $TagName
    $canonicalTag = Get-HelixVersionTag -Version $version
    if ($canonicalTag -ne $TagName) {
        throw "Tag '$TagName' is not in canonical form; expected '$canonicalTag'."
    }

    $releases = Get-AllReleases -Repo $Repo
    $existing = $releases | Where-Object { -not $_.draft -and $_.tag_name -eq $TagName }

    if ($existing) {
        return [pscustomobject]@{ noop = $true }
    }

    return [pscustomobject]@{
        noop       = $false
        version    = $version.Raw
        prerelease = -not $version.IsStable
        makeLatest = (Get-MakeLatest -Candidate $version -Releases $releases)
    }
}

function Invoke-TagPropsSyncDecision {
    # Called after the tag has already published. Write the tag version into
    # Directory.Build.props only when the file is strictly behind it. A stable
    # tag outranks a preview on the same MAJOR.MINOR.PATCH, so a later stable
    # tag still syncs over a preview left in the file.
    param(
        [Parameter(Mandatory)][string]$TagName,
        [Parameter(Mandatory)][string]$PropsPath
    )

    $tagVersion = ConvertFrom-HelixVersion $TagName
    $propsVersion = ConvertFrom-HelixVersion (Read-PropsVersion -Path $PropsPath)

    # A tag on a commit master does not contain would otherwise point master's
    # props at code master never built; the caller only reaches this decision
    # after release-installers.yml already finished a successful publish from
    # that exact tag, so this strictly orders "ahead" by version, not by reachability.
    return [pscustomobject]@{
        needsSync = (Compare-HelixVersion $tagVersion $propsVersion) -gt 0
        version   = $tagVersion.Raw
    }
}

switch ($Mode) {
    'Tag' {
        if (-not $TagName) { throw "-TagName is required for -Mode Tag." }
        $repoSlug = Get-HelixRepoSlug -Explicit $Repo
        Invoke-TagDecision -Repo $repoSlug -TagName $TagName |
            ConvertTo-Json -Depth 5 -Compress
    }
    'TagPropsSync' {
        if (-not $TagName) { throw "-TagName is required for -Mode TagPropsSync." }
        Invoke-TagPropsSyncDecision -TagName $TagName -PropsPath $PropsPath |
            ConvertTo-Json -Depth 5 -Compress
    }
}
