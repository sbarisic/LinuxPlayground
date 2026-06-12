#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd -- "${script_dir}/.." && pwd)"

kernel_image="${KERNEL_IMAGE:-${repo_root}/build/out/kernel/bzImage}"
initramfs_image="${INITRAMFS:-${repo_root}/build/out/initramfs.cpio.gz}"

if [[ ! -f "${kernel_image}" ]]; then
  echo "Kernel image not found: ${kernel_image}" >&2
  echo "Run: bash build/build-kernel.sh" >&2
  exit 1
fi

if [[ ! -f "${initramfs_image}" ]]; then
  echo "Initramfs not found: ${initramfs_image}" >&2
  echo "Run: bash build/build-initramfs.sh" >&2
  exit 1
fi

exec qemu-system-x86_64 \
  -kernel "${kernel_image}" \
  -initrd "${initramfs_image}" \
  -append "console=ttyS0 rdinit=/init panic=-1" \
  -nographic \
  -no-reboot
