#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd -- "${script_dir}/.." && pwd)"

linux_version="${LINUX_VERSION:-6.6.87}"
jobs="${JOBS:-$(nproc)}"

cache_dir="${repo_root}/build/cache"
out_dir="${repo_root}/build/out/kernel"
work_dir="${KERNEL_WORK_DIR:-${HOME}/.cache/linuxplayground/kernel}"
source_parent="${work_dir}/source"
source_dir="${source_parent}/linux-${linux_version}"
tarball="${cache_dir}/linux-${linux_version}.tar.xz"
kernel_url="https://cdn.kernel.org/pub/linux/kernel/v6.x/linux-${linux_version}.tar.xz"
config_fragment="${repo_root}/kernel/config/x86_64.config"
kernel_image="${KERNEL_IMAGE:-${out_dir}/bzImage}"

mkdir -p "${cache_dir}" "${out_dir}" "${source_parent}"

cleanup_items=()
cleanup() {
  local item
  for item in "${cleanup_items[@]}"; do
    if [[ -n "${item}" && -e "${item}" ]]; then
      rm -rf "${item}"
    fi
  done
}
trap cleanup EXIT

source_is_complete() {
  [[ -f "${source_dir}/Makefile" ]] &&
    [[ -x "${source_dir}/scripts/kconfig/merge_config.sh" ]] &&
    [[ -f "${source_dir}/arch/x86/Makefile" ]] &&
    [[ -f "${source_dir}/net/netfilter/xt_TCPMSS.c" ]] &&
    [[ -f "${source_dir}/net/netfilter/xt_tcpmss.c" ]]
}

tarball_is_valid() {
  tar -tf "${tarball}" "linux-${linux_version}/Makefile" >/dev/null 2>&1
}

download_tarball() {
  local tmp_tarball="${tarball}.tmp"

  rm -f "${tmp_tarball}"
  cleanup_items+=("${tmp_tarball}")
  echo "Downloading Linux ${linux_version}..."
  curl -L --fail --show-error --output "${tmp_tarball}" "${kernel_url}"

  if ! tar -tf "${tmp_tarball}" "linux-${linux_version}/Makefile" >/dev/null 2>&1; then
    rm -f "${tmp_tarball}"
    echo "Downloaded tarball is not a valid Linux ${linux_version} source archive." >&2
    exit 1
  fi

  mv "${tmp_tarball}" "${tarball}"
}

case_test_dir="$(mktemp -d "${source_parent}/case-test.XXXXXX")"
cleanup_items+=("${case_test_dir}")
touch "${case_test_dir}/case" "${case_test_dir}/CASE"
case_test_count="$(find "${case_test_dir}" -maxdepth 1 -type f | wc -l)"
rm -rf "${case_test_dir}"

if [[ "${case_test_count}" != "2" ]]; then
  echo "Kernel source directory is not case-sensitive: ${source_parent}" >&2
  echo "Linux kernel source must not be extracted under /mnt/c or another case-insensitive filesystem." >&2
  echo "Use WSL's native filesystem, or set KERNEL_WORK_DIR to a case-sensitive path." >&2
  exit 1
fi

if [[ ! -f "${tarball}" ]]; then
  download_tarball
elif ! tarball_is_valid; then
  echo "Cached Linux ${linux_version} tarball is incomplete or corrupt; downloading again."
  rm -f "${tarball}"
  download_tarball
fi

if [[ -d "${source_dir}" ]] && ! source_is_complete; then
  echo "Existing Linux ${linux_version} source tree is incomplete; extracting a fresh copy."
  rm -rf "${source_dir}"
fi

if [[ ! -d "${source_dir}" ]]; then
  extract_dir="$(mktemp -d "${source_parent}/extract.XXXXXX")"
  cleanup_items+=("${extract_dir}")
  echo "Extracting Linux ${linux_version} to ${source_dir}..."
  tar -C "${extract_dir}" -xf "${tarball}"

  if [[ ! -d "${extract_dir}/linux-${linux_version}" ]]; then
    rm -rf "${extract_dir}"
    echo "Linux ${linux_version} archive did not contain the expected source directory." >&2
    exit 1
  fi

  mv "${extract_dir}/linux-${linux_version}" "${source_dir}"
  rm -rf "${extract_dir}"
fi

if ! source_is_complete; then
  echo "Linux ${linux_version} source tree is missing expected files." >&2
  echo "Check that KERNEL_WORK_DIR is on a case-sensitive filesystem." >&2
  exit 1
fi

echo "Configuring Linux ${linux_version}..."
make -C "${source_dir}" ARCH=x86_64 x86_64_defconfig
"${source_dir}/scripts/kconfig/merge_config.sh" \
  -m \
  -O "${source_dir}" \
  "${source_dir}/.config" \
  "${config_fragment}"
make -C "${source_dir}" ARCH=x86_64 olddefconfig

echo "Building Linux ${linux_version} with ${jobs} job(s)..."
make -C "${source_dir}" ARCH=x86_64 -j"${jobs}" bzImage

cp "${source_dir}/arch/x86/boot/bzImage" "${kernel_image}"
echo "Kernel image written to ${kernel_image}"
