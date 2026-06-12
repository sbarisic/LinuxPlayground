#!/usr/bin/env bash
set -euo pipefail

required_commands=(
  qemu-system-x86_64
  gcc
  make
  bc
  bison
  flex
  perl
  openssl
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

if ((${#missing[@]} > 0)); then
  echo "Missing required commands:"
  printf '  %s\n' "${missing[@]}"
  echo
  echo "On Ubuntu/WSL, install the expected packages with:"
  echo "  sudo apt-get update"
  echo "  sudo apt-get install -y qemu-system-x86 build-essential bc bison flex libssl-dev libelf-dev cpio gzip xz-utils curl tar git"
  exit 1
fi

echo "All required commands are available."
