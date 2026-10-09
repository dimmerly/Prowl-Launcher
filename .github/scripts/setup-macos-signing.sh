#!/bin/bash
set -euo pipefail

: "${MACOS_SIGNING_CERTIFICATE:?Set the base64 Developer ID Application P12 certificate}"
: "${MACOS_SIGNING_PASSWORD:?Set the certificate password}"
: "${APPLE_NOTARY_KEY:?Set the base64 App Store Connect API private key}"
: "${APPLE_NOTARY_KEY_ID:?Set the App Store Connect API key ID}"
: "${APPLE_NOTARY_ISSUER_ID:?Set the App Store Connect API issuer ID}"

work=$(mktemp -d "$RUNNER_TEMP/prowl-signing.XXXXXX")
keychain="$work/signing.keychain-db"
keychain_password=$(openssl rand -hex 32)
echo "::add-mask::$keychain_password"
echo "PROWL_SIGNING_WORK=$work" >> "$GITHUB_ENV"
echo "PROWL_MAC_KEYCHAIN=$keychain" >> "$GITHUB_ENV"

security create-keychain -p "$keychain_password" "$keychain"
security set-keychain-settings -lut 21600 "$keychain"
security unlock-keychain -p "$keychain_password" "$keychain"
# Keep the system trust roots and the runner's login keychain available.
security list-keychains -d user -s "$keychain" "$HOME/Library/Keychains/login.keychain-db"
printf '%s' "$MACOS_SIGNING_CERTIFICATE" | base64 --decode > "$work/certificate.p12"
security import "$work/certificate.p12" -P "$MACOS_SIGNING_PASSWORD" -k "$keychain" -t cert -f pkcs12 -T /usr/bin/codesign -T /usr/bin/security
security set-key-partition-list -S apple-tool:,apple:,codesign: -s -k "$keychain_password" "$keychain"
identity=$(security find-identity -v -p codesigning "$keychain" | awk '/Developer ID Application/{print $2; exit}')
if [ -z "$identity" ]; then
  echo 'The keychain contains no valid Developer ID Application signing identity.' >&2
  exit 1
fi
echo "PROWL_MAC_SIGNING_IDENTITY=$identity" >> "$GITHUB_ENV"

printf '%s' "$APPLE_NOTARY_KEY" | base64 --decode > "$work/notary.p8"
xcrun notarytool store-credentials prowl-notary --key "$work/notary.p8" --key-id "$APPLE_NOTARY_KEY_ID" --issuer "$APPLE_NOTARY_ISSUER_ID" --keychain "$keychain"
echo 'PROWL_MAC_NOTARY_PROFILE=prowl-notary' >> "$GITHUB_ENV"
rm -f "$work/certificate.p12" "$work/notary.p8"
