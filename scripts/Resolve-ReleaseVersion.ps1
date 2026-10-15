#requires -Version 7.0
<#
.SYNOPSIS
Shared release-version decision logic for Helix Explorer's release automation.

.DESCRIPTION
Used by .github/workflows/release-on-master.yml and release-on-tag.yml so both
callers apply one set of version-parsing, preview-increment, make_latest, and
idempotency rules instead of duplicating them. release-installers.yml (the
reusable publisher) does not call this script: its tag/build-commit checks are
a publish-time concern, not a version-classification one.

Release intent is derived from what is already published, never from a commit
diff: comparing against a push's "before" SHA breaks on multi-commit pushes,
force-pushes, and new branches, and would let a failed publish strand a
version forever (it would look "unchanged" on retry and never ship).

Prints exactly one JSON object to stdout and makes no other side effects, so a
caller step parses the decision with `| ConvertFrom-Json`, and the same
invocation serves as a local dry run against the real repository.

Run after `actions/checkout` with `fetch-depth: 0` and an explicit
`git fetch --tags --force`: every mode here reads real tags and releases
rather than trusting what the triggering push happened to touch.

.EXAMPLE
./Resolve-ReleaseVersion.ps1 -Mode Master -Repo bolorundurowb/helix-file-explorer -Sha (git rev-parse HEAD)

.EXAMPLE
./Resolve-ReleaseVersion.ps1 -Mode Tag -Repo bolorundurowb/helix-file-explorer -TagName v1.4.4-preview.2

.EXAMPLE
./Resolve-ReleaseVersion.ps1 -Mode TagPropsSync -TagName v1.4.4 -PropsPath Directory.Build.props
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('Master', 'Tag', 'TagPropsSync')]
    [string]$Mode,

    [string]$Repo = $env:GITHUB_REPOSITORY,

    # Commit under evaluation for -Mode Master: the pushed/dispatched master SHA.
    [string]$Sha,

    [string]$PropsPath = 'Directory.Build.props',

    # Required for -Mode Tag and -Mode TagPropsSync: the pushed tag name, e.g. v1.4.4-preview.2.
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
# GitHub release/tag data
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
    # `gh release list` caps at 30 by default, which a preview-per-merge cadence
    # outgrows quickly. `--paginate --jq '.[]'` emits one release object per
    # line across every page, so pages never need to be stitched back together.
    param([Parameter(Mandatory)][string]$Repo)

    $lines = gh api "repos/$Repo/releases" --paginate --jq '.[]' 2>$null
    if (-not $lines) { return @() }

    return @($lines | ForEach-Object { $_ | ConvertFrom-Json -Depth 10 })
}

function Resolve-ReleaseCommit {
    # A release's target_commitish is often just the branch name "master", not
    # a SHA, so the commit must be resolved from the tag itself.
    param([Parameter(Mandatory)][string]$TagName)

    $commit = git rev-parse "$TagName^{commit}" 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $commit) { return $null }
    return $commit.Trim()
}

function Test-CommitIsAncestorOfAnyRelease {
    # Checking every non-draft release's commit, not just the highest version,
    # gives the same answer in the normal case and still holds if a lower
    # version is ever released after a higher one (e.g. a hotfix tag).
    param(
        [Parameter(Mandatory)][string]$Sha,
        [Parameter(Mandatory)][array]$Releases
    )

    foreach ($release in $Releases) {
        if ($release.draft) { continue }

        $commit = Resolve-ReleaseCommit -TagName $release.tag_name
        if (-not $commit) { continue }

        git merge-base --is-ancestor $Sha $commit 2>$null
        if ($LASTEXITCODE -eq 0) { return $true }
    }

    return $false
}

# ---------------------------------------------------------------------------
# Preview number assignment
# ---------------------------------------------------------------------------

function Get-ExistingPreviewNumbers {
    # Preview numbers come from observed state, not a stored counter. A
    # dangling tag with no release still reserves its number, so both sources
    # are unioned.
    param(
        [Parameter(Mandatory)][psobject]$Base,
        [Parameter(Mandatory)][array]$Releases
    )

    $prefix = "v$($Base.Major).$($Base.Minor).$($Base.Patch)-preview."
    $numbers = [System.Collections.Generic.HashSet[int]]::new()

    foreach ($tag in (git tag --list "$prefix*")) {
        $suffix = $tag.Substring($prefix.Length)
        if ($suffix -match '^([1-9]\d*)$') { [void]$numbers.Add([int]$Matches[1]) }
    }

    foreach ($release in $Releases) {
        $name = [string]$release.tag_name
        if ($name.StartsWith($prefix)) {
            $suffix = $name.Substring($prefix.Length)
            if ($suffix -match '^([1-9]\d*)$') { [void]$numbers.Add([int]$Matches[1]) }
        }
    }

    # HashSet is IEnumerable, so a bare `return $numbers` would have PowerShell
    # unroll it into zero-or-more pipeline objects (an empty set vanishes into
    # $null) instead of passing the set itself. The leading comma suppresses that.
    return , $numbers
}

function Test-StableTagOccupied {
    param([Parameter(Mandatory)][psobject]$Base)
    return [bool](git tag --list "v$($Base.Major).$($Base.Minor).$($Base.Patch)")
}

function Get-NextPreviewVersion {
    param(
        [Parameter(Mandatory)][psobject]$Current,
        [Parameter(Mandatory)][array]$Releases
    )

    $base = if ($Current.IsStable) {
        [pscustomobject]@{ Major = $Current.Major; Minor = $Current.Minor; Patch = $Current.Patch + 1 }
    }
    else {
        [pscustomobject]@{ Major = $Current.Major; Minor = $Current.Minor; Patch = $Current.Patch }
    }

    # The stable tag for this patch may already be occupied (e.g. someone
    # tagged ahead manually); keep advancing until an unclaimed patch is found.
    while (Test-StableTagOccupied -Base $base) {
        $base.Patch++
    }

    $numbers = Get-ExistingPreviewNumbers -Base $base -Releases $Releases
    $next = 1
    while ($numbers.Contains($next)) { $next++ }

    return ConvertFrom-HelixVersion "$($base.Major).$($base.Minor).$($base.Patch)-preview.$next"
}

# ---------------------------------------------------------------------------
# make_latest resolution
# ---------------------------------------------------------------------------

function Get-MakeLatest {
    # A hotfix tag published after a newer stable tag must never become
    # "latest", or the in-app updater would offer a downgrade. Previews are
    # never latest. The same comparison covers a manual downgrade written
    # into Directory.Build.props: it still releases, just not as latest.
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

function Invoke-MasterDecision {
    param(
        [Parameter(Mandatory)][string]$Repo,
        [Parameter(Mandatory)][string]$Sha,
        [Parameter(Mandatory)][string]$PropsPath
    )

    $releases = Get-AllReleases -Repo $Repo

    if (Test-CommitIsAncestorOfAnyRelease -Sha $Sha -Releases $releases) {
        return [pscustomobject]@{ noop = $true }
    }

    $propsVersion = ConvertFrom-HelixVersion (Read-PropsVersion -Path $PropsPath)
    $propsTag = Get-HelixVersionTag -Version $propsVersion
    $propsReleased = $releases | Where-Object { -not $_.draft -and $_.tag_name -eq $propsTag }

    if (-not $propsReleased) {
        # Covers a deliberate bump and a retry after a failed or half-finished
        # run: a failed publish never reaches a non-draft release, so its
        # version is retried here instead of being skipped.
        $target = $propsVersion
        $needsBump = $false
    }
    else {
        $target = Get-NextPreviewVersion -Current $propsVersion -Releases $releases
        $needsBump = $true
    }

    return [pscustomobject]@{
        noop        = $false
        version     = $target.Raw
        prerelease  = -not $target.IsStable
        makeLatest  = (Get-MakeLatest -Candidate $target -Releases $releases)
        needsBump   = $needsBump
    }
}

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
    # Only called for a stable tag's release, after it has already published
    # successfully. A preview tag never syncs master's props: that would point
    # master's props at a preview, which Invoke-MasterDecision would then treat
    # as "already released" and immediately mint yet another preview from it.
    param(
        [Parameter(Mandatory)][string]$TagName,
        [Parameter(Mandatory)][string]$PropsPath
    )

    $tagVersion = ConvertFrom-HelixVersion $TagName
    if (-not $tagVersion.IsStable) {
        throw "Invoke-TagPropsSyncDecision is only valid for a stable tag; got '$TagName'."
    }

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
    'Master' {
        if (-not $Sha) { throw "-Sha is required for -Mode Master." }
        $repoSlug = Get-HelixRepoSlug -Explicit $Repo
        Invoke-MasterDecision -Repo $repoSlug -Sha $Sha -PropsPath $PropsPath |
            ConvertTo-Json -Depth 5 -Compress
    }
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
