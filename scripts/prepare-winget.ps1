<#
.SYNOPSIS
Writes winget manifests for a published release of CodecTone.

.DESCRIPTION
Fills packaging\winget\templates with the version and the SHA-256 of the standalone executable and writes the
result to packaging\winget\out\<version>. Upload CodecTone-Standalone.exe to the GitHub release v<version> first:
the manifest points to that download URL. Submitting to microsoft/winget-pkgs is a separate, manual step.
#>
param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [string]$Executable = 'release\final\CodecTone-Standalone.exe'
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path $Executable)) { throw "Build the release first: $Executable was not found." }

$sha256 = (Get-FileHash $Executable -Algorithm SHA256).Hash
$output = Join-Path 'packaging\winget\out' $Version
New-Item -ItemType Directory -Force $output | Out-Null
foreach ($template in Get-ChildItem 'packaging\winget\templates\*.yaml') {
    $text = (Get-Content $template.FullName -Raw).Replace('{{VERSION}}', $Version).Replace('{{SHA256}}', $sha256)
    Set-Content -Path (Join-Path $output $template.Name) -Value $text -Encoding utf8 -NoNewline
}
Write-Host "Manifests written to $output (SHA-256 $sha256)."
Write-Host "Validate with: winget validate --manifest $output"
