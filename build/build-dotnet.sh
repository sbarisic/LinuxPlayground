#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd -- "${script_dir}/.." && pwd)"

system_dir="${repo_root}/build/out/system"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "dotnet was not found. Run: bash build/install-prereqs.sh" >&2
  exit 1
fi

if ! command -v clang >/dev/null 2>&1; then
  echo "clang was not found. Run: bash build/install-prereqs.sh" >&2
  exit 1
fi

if ! command -v ld.lld >/dev/null 2>&1; then
  echo "ld.lld was not found. Run: bash build/install-prereqs.sh" >&2
  exit 1
fi

if ! dotnet --list-sdks | grep -q '^10\.'; then
  echo ".NET 10 SDK was not found. Run: bash build/install-prereqs.sh" >&2
  exit 1
fi

mkdir -p "${system_dir}"

publish_native_aot() {
  local name="$1"
  local project="$2"
  local publish_dir="${repo_root}/build/out/dotnet/${name}"
  local output_binary="${system_dir}/${name}"

  mkdir -p "${publish_dir}"

  echo "Publishing ${name}..."
  dotnet publish "${project}" \
    --configuration Release \
    --runtime linux-x64 \
    --self-contained true \
    -p:PublishAot=true \
    -p:StaticExecutable=true \
    -p:LinkerFlavor=lld \
    -p:AssemblyName="${name}" \
    -p:DebugType=none \
    -p:DebugSymbols=false \
    --output "${publish_dir}"

  cp "${publish_dir}/${name}" "${output_binary}"
  chmod 0755 "${output_binary}"

  echo "${name} written to ${output_binary}"
}

publish_native_aot "ServiceManager" "${repo_root}/src/MyOs.ServiceManager/MyOs.ServiceManager.csproj"
publish_native_aot "Shell" "${repo_root}/src/MyOs.Shell/MyOs.Shell.csproj"
