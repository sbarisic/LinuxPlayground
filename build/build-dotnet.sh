#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd -- "${script_dir}/.." && pwd)"

system_dir="${repo_root}/build/out/system"
apps_dir="${repo_root}/build/out/Apps"

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

mkdir -p "${system_dir}" "${apps_dir}"

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
    -p:DebugType=none \
    -p:DebugSymbols=false \
    --output "${publish_dir}"

  cp "${publish_dir}/${name}" "${output_binary}"
  chmod 0755 "${output_binary}"

  echo "${name} written to ${output_binary}"
}

publish_managed_app() {
  local name="$1"
  local project="$2"
  local app_dir="${apps_dir}/${name}.app"
  local bin_dir="${app_dir}/bin"
  local resources_dir="${app_dir}/resources"

  rm -rf "${app_dir}"
  mkdir -p "${bin_dir}" "${resources_dir}"

  echo "Publishing ${name} app..."
  dotnet publish "${project}" \
    --configuration Release \
    --runtime linux-x64 \
    --self-contained true \
    -p:PublishAot=false \
    -p:PublishSingleFile=false \
    -p:DebugType=none \
    -p:DebugSymbols=false \
    --output "${bin_dir}"

  cat > "${app_dir}/manifest.json" <<EOF
{
  "id": "myos.hworld",
  "name": "HWorld",
  "version": "0.1.0",
  "entry": "bin/HWorld",
  "kind": "console",
  "permissions": []
}
EOF

  chmod 0755 "${bin_dir}/${name}"
  echo "${name} app written to ${app_dir}"
}

publish_native_aot "ServiceManager" "${repo_root}/src/MyOs.ServiceManager/MyOs.ServiceManager.csproj"
publish_native_aot "Shell" "${repo_root}/src/MyOs.Shell/MyOs.Shell.csproj"
publish_managed_app "HWorld" "${repo_root}/src/Apps/HWorld/HWorld.csproj"
