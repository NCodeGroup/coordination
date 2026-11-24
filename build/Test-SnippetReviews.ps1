<#
.SYNOPSIS
    Checks that scanoss.json records each reviewed snippet match by itself, so no match is silenced in bulk.

.DESCRIPTION
    Reads the SCANOSS settings file and accepts only settings.bom.include entries with the keys path, purl and comment.
    Each entry names one existing file, without wildcards, a purl that starts with pkg: and a comment that gives the
    reviewer's reason. It exits 1 and prints each problem when a key, an entry or a value is outside that shape.

.PARAMETER Path
    The settings file: scanoss.json at the repository root by default.

.EXAMPLE
    ./build/Test-SnippetReviews.ps1
#>
[CmdletBinding()]
param(
    [string]$Path
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $Path) { $Path = Join-Path $root 'scanoss.json' }
if (-not [IO.Path]::IsPathRooted($Path)) { $Path = Join-Path (Get-Location) $Path }

$document = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json -AsHashtable
$problems = [System.Collections.Generic.List[string]]::new()
$count = 0

function Test-Keys {
    param($Table, [string[]]$Allowed, [string]$Name)
    foreach ($key in $Table.Keys) {
        if ($key -notin $Allowed) { $problems.Add("${Name}: key '$key' is not allowed.") }
    }
}

if ($document -isnot [System.Collections.IDictionary]) {
    $problems.Add('The file is not a JSON object.')
}
else {
    Test-Keys $document @('settings') 'top level'
    $settings = $document['settings']
    if ($null -ne $settings -and $settings -isnot [System.Collections.IDictionary]) {
        $problems.Add('settings is not an object.')
    }
    elseif ($null -ne $settings) {
        Test-Keys $settings @('bom') 'settings'
        $bom = $settings['bom']
        if ($null -ne $bom -and $bom -isnot [System.Collections.IDictionary]) {
            $problems.Add('bom is not an object.')
        }
        elseif ($null -ne $bom) {
            Test-Keys $bom @('include') 'bom'
            $include = $bom['include']
            if ($null -ne $include -and $include -isnot [System.Collections.IList]) {
                $problems.Add('include is not a list.')
            }
            elseif ($null -ne $include) {
                $count = @($include).Count
                for ($i = 0; $i -lt $count; $i++) {
                    $entry = $include[$i]
                    $name = "include[$i]"
                    if ($entry -isnot [System.Collections.IDictionary]) {
                        $problems.Add("$name is not an object.")
                        continue
                    }
                    Test-Keys $entry @('path', 'purl', 'comment') $name

                    $entryPath = $entry['path']
                    if ($entryPath -isnot [string] -or [string]::IsNullOrWhiteSpace($entryPath)) {
                        $problems.Add("${name}: path is missing.")
                    }
                    elseif ($entryPath.Contains('*') -or $entryPath.Contains('?')) {
                        $problems.Add("${name}: path '$entryPath' holds a wildcard.")
                    }
                    elseif (-not (Test-Path -LiteralPath (Join-Path $root $entryPath) -PathType Leaf)) {
                        $problems.Add("${name}: path '$entryPath' names no file.")
                    }

                    $purl = $entry['purl']
                    if ($purl -isnot [string] -or -not $purl.StartsWith('pkg:', [StringComparison]::Ordinal)) {
                        $problems.Add("${name}: purl does not start with pkg:.")
                    }

                    $comment = $entry['comment']
                    if ($comment -isnot [string] -or [string]::IsNullOrWhiteSpace($comment)) {
                        $problems.Add("${name}: comment is missing.")
                    }
                }
            }
        }
    }
}

if ($problems.Count -gt 0) {
    foreach ($problem in $problems) { Write-Host $problem }
    exit 1
}
Write-Host "Snippet reviews: $count entries, each for one file."
exit 0
