#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd -- "${script_dir}/.." && pwd)"

kernel_image="${KERNEL_IMAGE:-${repo_root}/build/out/kernel/bzImage}"
initramfs_image="${INITRAMFS:-${repo_root}/build/out/initramfs.cpio.gz}"
serial_log="${SERIAL_LOG:-${repo_root}/out.txt}"
memory="${MEMORY:-512M}"

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

mkdir -p "$(dirname -- "${serial_log}")"
rm -f "${serial_log}"
echo "Serial console will be written to ${serial_log}"

exec qemu-system-x86_64 \
  -kernel "${kernel_image}" \
  -initrd "${initramfs_image}" \
  -m "${memory}" \
  -append "console=ttyS0 console=tty0 rdinit=/init panic=-1" \
  -display gtk \
  -serial "file:${serial_log}" \
  -no-reboot
