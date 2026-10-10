# Release signing

Stable releases require Windows signing and macOS Developer ID signing plus notarization. Missing credentials or failed
signature verification stop the release before publication. Prereleases retain unsigned Windows downloads and ad-hoc
macOS signatures by default; set the repository variable `SIGN_PRERELEASES` to `true` to sign and notarize previews too.

Configure these GitHub Actions repository secrets before publishing a stable release. Keep private keys and passwords
out of the repository.

| Secret                        | Value                                                                                    |
|-------------------------------|------------------------------------------------------------------------------------------|
| `WINDOWS_SIGNING_CERTIFICATE` | Base64-encoded PFX containing a trusted code-signing certificate and its private key     |
| `WINDOWS_SIGNING_PASSWORD`    | Password for that PFX                                                                    |
| `MACOS_SIGNING_CERTIFICATE`   | Base64-encoded P12 containing a Developer ID Application certificate and its private key |
| `MACOS_SIGNING_PASSWORD`      | Password for that P12                                                                    |
| `APPLE_NOTARY_KEY`            | Base64-encoded App Store Connect API private key (`.p8`) authorized for notarization     |
| `APPLE_NOTARY_KEY_ID`         | API key ID                                                                               |
| `APPLE_NOTARY_ISSUER_ID`      | API issuer ID                                                                            |

Windows uses the Windows SDK's SignTool, SHA-256 signatures, RFC 3161 timestamps, and Authenticode verification. The
downloadable launcher and the update's launcher executable and assembly are signed before packaging. This pipeline
supports an exportable PFX; certificates requiring hardware or cloud signing need a provider-specific signing
integration instead.
See [Microsoft's SignTool documentation](https://learn.microsoft.com/en-us/windows/win32/seccrypto/signtool).

macOS imports the Developer ID identity into a temporary keychain. Packaging signs native files and both app bundles
with hardened runtime, using
the [documented .NET entitlements](https://learn.microsoft.com/en-us/dotnet/core/install/macos-notarization-issues). It
submits each app to Apple, requires an accepted result, staples and validates its ticket, and checks Gatekeeper
assessment before packaging. The DMG is also signed, notarized, and stapled. Update archives include the signed, stapled
update app.
See [Apple's notarization workflow](https://developer.apple.com/documentation/security/customizing-the-notarization-workflow).

Certificates, imported Windows identities, and the temporary macOS keychain are removed after use. The release workflow
runs the existing packaged installation, startup, and update tests against the resulting artifacts before publication.

After adding credentials, validate a signed prerelease with `SIGN_PRERELEASES=true` before shipping a stable version.
Inspect the Windows publisher and signatures, macOS Gatekeeper behavior after a browser download, and an update from a
previously installed launcher on each platform. Local builds and unsigned preview releases cannot establish that the
production credentials or Apple's notarization service work.
