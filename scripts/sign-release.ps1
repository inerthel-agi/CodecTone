<#
.SYNOPSIS
Signs the release executables with a code-signing certificate, when one is configured.

.DESCRIPTION
Does nothing (exit 0) unless one of these environment variables is set:
  CODECTONE_SIGN_THUMBPRINT  SHA-1 thumbprint of a certificate in the current user's or machine's store (preferred).
  CODECTONE_SIGN_PFX         Path to a .pfx file; its password is read from CODECTONE_SIGN_PFX_PASSWORD.
The password is never printed. Files are timestamped so signatures stay valid after the certificate expires.
#>
param(
    [string[]]$Files = @('release\final\CodecTone.exe', 'release\final\CodecTone-Standalone.exe', 'release\cli\CodecTone.Cli.exe'),
    [string]$TimestampUrl = 'http://timestamp.digicert.com'
)
$ErrorActionPreference = 'Stop'

$thumbprint = $env:CODECTONE_SIGN_THUMBPRINT
$pfx = $env:CODECTONE_SIGN_PFX
if (-not $thumbprint -and -not $pfx) {
    Write-Host 'Signing skipped: set CODECTONE_SIGN_THUMBPRINT or CODECTONE_SIGN_PFX to sign the release.'
    exit 0
}

$signtool = (Get-Command signtool.exe -ErrorAction SilentlyContinue).Source
if (-not $signtool) {
    $signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $signtool) { throw 'signtool.exe was not found. Install the Windows SDK signing tools.' }

$identity = if ($thumbprint) { @('/sha1', $thumbprint) } else {
    if (-not $env:CODECTONE_SIGN_PFX_PASSWORD) { throw 'CODECTONE_SIGN_PFX_PASSWORD is not set.' }
    @('/f', $pfx, '/p', $env:CODECTONE_SIGN_PFX_PASSWORD)
}

foreach ($file in $Files) {
    if (-not (Test-Path $file)) { throw "Missing file to sign: $file" }
    & $signtool sign @identity /fd SHA256 /tr $TimestampUrl /td SHA256 $file | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Signing failed for $file" }
    & $signtool verify /pa $file | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Signature verification failed for $file" }
    Write-Host "Signed: $file"
}
