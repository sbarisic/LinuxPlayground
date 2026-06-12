# LinuxPlayground

A toy operating system distribution using the Linux kernel with a custom userspace.

The first development target is intentionally small: build a Linux kernel, build an
initramfs with a tiny custom `/init`, boot it in QEMU, mount the basic runtime
filesystems, start the C# `ServiceManager`, and launch a tiny custom shell.

## First Boot Skeleton

Run these commands from WSL Ubuntu at the repository root:

```sh
bash build/install-prereqs.sh
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

`build/build-initramfs.sh` also publishes the .NET `ServiceManager` and `Shell`
as Native AOT executables and copies them into `/system` inside the initramfs.
The WSL toolchain uses .NET 10 and LLVM `lld` for those static native publishes.

`build/run-qemu.sh` opens QEMU with a VGA window for the interactive shell and
writes the serial console to `out.txt`. The kernel command line enables both
`ttyS0` and `tty0`, with VGA selected as `/dev/console` for user interaction.

Expected QEMU serial output includes:

```text
LinuxPlayground init starting
[init] mounted devtmpfs on /dev
[init] mounted proc on /proc
[init] mounted sysfs on /sys
[init] mounted tmpfs on /run
[init] mounted tmpfs on /tmp
[init] starting /system/ServiceManager
[ServiceManager] starting pid=...
[ServiceManager] confirmed /dev mounted
[ServiceManager] confirmed /proc mounted
[ServiceManager] confirmed /sys mounted
[ServiceManager] confirmed /run mounted
[ServiceManager] confirmed /tmp mounted
[ServiceManager] idle
[ServiceManager] starting /system/Shell
[Shell] starting pid=...
myos>
```

Stop QEMU manually with a terminal interrupt for now.

## Current Scope

The tiny C `/init` in `src/Init/init.c` stays deliberately small. It proves the
boot path, mounts the basic runtime filesystems, starts `/system/ServiceManager`,
and restarts it if it exits.

The C# `ServiceManager` is intentionally tiny for now. It prints its PID,
confirms the runtime mounts from `/proc/mounts`, and supervises `/system/Shell`.

The custom shell currently supports:

```text
help
clear
echo <text>
mounts
pid
uptime
reboot
poweroff
```

`reboot` and `poweroff` are stubs until `/init` grows a shutdown control path.
