#!/usr/bin/env bash
set -euo pipefail

required_commands=(
  qemu-system-x86_64
  gcc
  make
  bc
  bison
  flex
  clang
  ld.lld
  perl
  openssl
  dotnet
  cpio
  gzip
  xz
  curl
  tar
  git
)

missing=()
for command_name in "${required_commands[@]}"; do
  if ! command -v "${command_name}" >/dev/null 2>&1; then
    missing+=("${command_name}")
  fi
done

missing_packages=()
if ! dotnet --list-sdks 2>/dev/null | grep -q '^10\.'; then
  missing_packages+=(dotnet-sdk-10.0)
fi

if ! printf '#include <libelf.h>\n#include <gelf.h>\nint main(void) { return 0; }\n' | gcc -x c - -lelf -o /tmp/linuxplayground-check-libelf >/dev/null 2>&1; then
  missing_packages+=(libelf-dev)
fi
rm -f /tmp/linuxplayground-check-libelf

if ! printf '#include <openssl/opensslv.h>\nint main(void) { return 0; }\n' | gcc -x c - -o /tmp/linuxplayground-check-openssl >/dev/null 2>&1; then
  missing_packages+=(libssl-dev)
fi
rm -f /tmp/linuxplayground-check-openssl

if ! printf '#include <zlib.h>\nint main(void) { return 0; }\n' | gcc -x c - -lz -o /tmp/linuxplayground-check-zlib >/dev/null 2>&1; then
  missing_packages+=(zlib1g-dev)
fi
rm -f /tmp/linuxplayground-check-zlib

if ((${#missing[@]} > 0 || ${#missing_packages[@]} > 0)); then
  echo "Missing required commands:"
  if ((${#missing[@]} > 0)); then
    printf '  %s\n' "${missing[@]}"
  else
    echo "  none"
  fi
  echo
  echo "Missing required packages:"
  if ((${#missing_packages[@]} > 0)); then
    printf '  %s\n' "${missing_packages[@]}"
  else
    echo "  none"
  fi
  echo
  echo "On Ubuntu/WSL, install the expected packages with:"
  echo "  bash build/install-prereqs.sh"
  echo
  echo "Or run apt manually:"
  echo "  sudo apt-get update"
  echo "  sudo apt-get install -y qemu-system-x86 build-essential bc bison flex clang lld libssl-dev libelf-dev zlib1g-dev dotnet-sdk-10.0 cpio gzip xz-utils curl tar git"
  exit 1
fi

echo "All required commands are available."
