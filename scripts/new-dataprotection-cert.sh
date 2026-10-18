#!/usr/bin/env bash
# Creates the certificate that encrypts auth-cookie keys at rest (DataProtection) for docker-compose.prod.yml.
# Usage: DATAPROTECTION_CERT_PASSWORD=... scripts/new-dataprotection-cert.sh [output.pfx]
# Keep the .pfx and its password with your backups: without them the encrypted keys cannot be read (everyone is signed out).
set -euo pipefail

OUT="${1:-secrets/dataprotection.pfx}"
: "${DATAPROTECTION_CERT_PASSWORD:?set DATAPROTECTION_CERT_PASSWORD}"
mkdir -p "$(dirname "$OUT")"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

# Stops Git Bash on Windows from turning "/CN=..." into a file path (no effect elsewhere).
MSYS2_ARG_CONV_EXCL="/CN=" openssl req -x509 -newkey rsa:3072 -sha256 -days 1825 -nodes -subj "/CN=OfficeAI DataProtection" \
  -keyout "$TMP/key.pem" -out "$TMP/cert.pem" -quiet
openssl pkcs12 -export -inkey "$TMP/key.pem" -in "$TMP/cert.pem" -out "$OUT" -passout env:DATAPROTECTION_CERT_PASSWORD
chmod 600 "$OUT"
echo "✓ certificate written to $OUT (valid 5 years)"
