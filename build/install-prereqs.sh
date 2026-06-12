#!/usr/bin/env bash
set -euo pipefail

if ! command -v apt-get >/dev/null 2>&1; then
  echo "apt-get was not found. This installer expects Ubuntu, Debian, or WSL Ubuntu." >&2
  exit 1
fi

packages=(
  qemu-system-x86
  build-essential
  bc
  bison
  flex
  clang
  lld
  libc-bin
  libssl-dev
  libelf-dev
  zlib1g-dev
  dotnet-sdk-10.0
  cpio
  gzip
  xz-utils
  curl
  tar
  git
)

if [[ "${EUID}" -eq 0 ]]; then
  sudo_cmd=()
else
  if ! command -v sudo >/dev/null 2>&1; then
    echo "sudo was not found. Run this script as root, or install sudo first." >&2
    exit 1
  fi
  sudo_cmd=(sudo)
fi

echo "Updating apt package indexes..."
"${sudo_cmd[@]}" env DEBIAN_FRONTEND=noninteractive apt-get update

echo "Installing LinuxPlayground build prerequisites..."
"${sudo_cmd[@]}" env DEBIAN_FRONTEND=noninteractive apt-get install -y "${packages[@]}"

echo "Prerequisites installed. Verifying..."
bash "$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)/check-prereqs.sh"
