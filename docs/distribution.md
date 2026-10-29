# Distribution

How to publish a signed CodecTone release and list it in winget. Nothing in this repository signs or publishes on
its own: each step below needs the maintainer's certificate or accounts.

## Continuous integration

`.github/workflows/ci.yml` builds and tests every push to `main` and `feat/**` and every pull request on
`windows-latest`. It uploads the test results and the layout captures rendered by `WorkspaceTests` (every page in
every theme) as artifacts. FFmpeg is not installed on the runner, so FFmpeg integration tests report as skipped.

## Code signing

Unsigned executables trigger a SmartScreen warning. Signing needs a code-signing certificate from a certificate
authority (OV or EV). It is not included in the repository.

1. Install the certificate in the Windows certificate store, or keep the `.pfx` file outside the repository.
2. Set one of these environment variables before running `build_executable.bat`:
   - `CODECTONE_SIGN_THUMBPRINT`: the certificate's SHA-1 thumbprint (preferred: no password on the command line).
   - `CODECTONE_SIGN_PFX` and `CODECTONE_SIGN_PFX_PASSWORD`: the `.pfx` path and its password.
3. Run `build_executable.bat`. `scripts/sign-release.ps1` signs `CodecTone.exe`, `CodecTone-Standalone.exe` and
   `CodecTone.Cli.exe` with SHA-256 and a timestamp, then verifies each signature. Without the variables it prints
   "Signing skipped" and the build continues.

`signtool.exe` comes from the Windows SDK.

## winget

The manifests use the `portable` installer type and point to `CodecTone-Standalone.exe` on the GitHub release.

1. Create the GitHub release `v<version>` and upload `release\final\CodecTone-Standalone.exe`.
2. Run `powershell -File scripts\prepare-winget.ps1 -Version <version>`. It writes the three manifests to
   `packaging\winget\out\<version>` with the file's SHA-256.
3. Check them with `winget validate --manifest packaging\winget\out\<version>`.
4. Submit them to `microsoft/winget-pkgs`, for example with `wingetcreate submit`. This opens a public pull
   request under your GitHub account.

Upload the exact file that was hashed: any rebuild changes the SHA-256.
