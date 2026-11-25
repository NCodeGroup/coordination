<#
.SYNOPSIS
    Checks that every NuGet package the solution references, transitive and development-only ones included, has a
    license ORIGINALITY.md permits.

.DESCRIPTION
    Restores the repository's dotnet tools and the solution's packages, then runs nuget-license over the solution with
    the allow list. It exits non-zero when a package has a license the list doesn't name, or none.

.PARAMETER AllowedLicenses
    The allow list file, a JSON array of SPDX license identifiers: build/allowed-licenses.json by default.

.EXAMPLE
    ./build/Test-Licenses.ps1
#>
[CmdletBinding()]
param(
    [string]$AllowedLicenses
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $AllowedLicenses) { $AllowedLicenses = Join-Path $root 'build/allowed-licenses.json' }
if (-not [IO.Path]::IsPathRooted($AllowedLicenses)) { $AllowedLicenses = Join-Path (Get-Location) $AllowedLicenses }

Push-Location $root
try {
    dotnet tool restore
    if ($LASTEXITCODE) { exit $LASTEXITCODE }
    dotnet restore NCode.Coordination.slnx --nologo
    if ($LASTEXITCODE) { exit $LASTEXITCODE }
    dotnet tool run nuget-license -i NCode.Coordination.slnx -t -a $AllowedLicenses -err
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
