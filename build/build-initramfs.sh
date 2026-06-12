#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd -- "${script_dir}/.." && pwd)"

out_root="${repo_root}/build/out"
initramfs_root="${out_root}/initramfs-root"
init_binary="${out_root}/init"
initramfs_image="${INITRAMFS:-${out_root}/initramfs.cpio.gz}"
initramfs_tmp="${initramfs_image}.tmp"

mkdir -p "${out_root}" "$(dirname -- "${initramfs_image}")"
rm -rf "${initramfs_root}"
rm -f "${initramfs_tmp}"
mkdir -p \
  "${initramfs_root}/dev" \
  "${initramfs_root}/proc" \
  "${initramfs_root}/sys" \
  "${initramfs_root}/run" \
  "${initramfs_root}/tmp" \
  "${initramfs_root}/system"

echo "Building static /init..."
gcc \
  -std=c11 \
  -Wall \
  -Wextra \
  -Werror \
  -O2 \
  -static \
  -o "${init_binary}" \
  "${repo_root}/src/Init/init.c"

cp "${init_binary}" "${initramfs_root}/init"
chmod 0755 "${initramfs_root}/init"
chmod 0755 "${initramfs_root}"
chmod 0755 "${initramfs_root}/dev" "${initramfs_root}/proc" "${initramfs_root}/sys" "${initramfs_root}/run" "${initramfs_root}/tmp" "${initramfs_root}/system"

echo "Creating initramfs..."
(
  cd "${initramfs_root}"
  find . -print0 | cpio --null --quiet -o --format=newc
) | gzip -9 > "${initramfs_tmp}"

mv "${initramfs_tmp}" "${initramfs_image}"

echo "Initramfs written to ${initramfs_image}"
if command -v file >/dev/null 2>&1; then
  if file "${init_binary}" | grep -q "statically linked"; then
    echo "/init is statically linked."
  else
    echo "warning: /init does not appear to be statically linked." >&2
  fi
fi
