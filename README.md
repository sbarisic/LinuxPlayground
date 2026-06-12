# LinuxPlayground

A toy operating system distribution using the Linux kernel with a custom userspace.

The first development target is intentionally small: build a Linux kernel, build an
initramfs with a tiny custom `/init`, boot it in QEMU, print mount status to the
serial console, and idle as PID 1.

## First Boot Skeleton

Run these commands from WSL Ubuntu at the repository root:

```sh
bash build/check-prereqs.sh
bash build/build-kernel.sh
bash build/build-initramfs.sh
bash build/run-qemu.sh
```

The scripts use these defaults:

```sh
LINUX_VERSION=6.6.87
JOBS=$(nproc)
KERNEL_IMAGE=build/out/kernel/bzImage
INITRAMFS=build/out/initramfs.cpio.gz
KERNEL_WORK_DIR=$HOME/.cache/linuxplayground/kernel
```

You can override any of those values in the environment before running the
scripts.

The kernel source is intentionally extracted and built under `KERNEL_WORK_DIR`
instead of the repository. When running from WSL, do not build the Linux source
under `/mnt/c`: the Linux kernel contains source files that differ only by case,
and Windows-backed filesystems can collapse those names.

If you previously tried to build under `kernel/source` on `/mnt/c`, ignore that
tree or delete it. The build script now uses the WSL-native cache by default.

Expected QEMU serial output includes:

```text
LinuxPlayground init starting
[init] mounted devtmpfs on /dev
[init] mounted proc on /proc
[init] mounted sysfs on /sys
[init] mounted tmpfs on /run
[init] mounted tmpfs on /tmp
[init] init idle
```

Stop QEMU manually with a terminal interrupt for now.

## Current Scope

The tiny C `/init` in `src/Init/init.c` is temporary. It only proves the boot path
and mounts the basic runtime filesystems. The .NET service manager starts after
this boot loop is stable.
