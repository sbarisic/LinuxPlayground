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
MEMORY=512M
```

You can override any of those values in the environment before running the
scripts.

The kernel source is intentionally extracted and built under `KERNEL_WORK_DIR`
instead of the repository. When running from WSL, do not build the Linux source
under `/mnt/c`: the Linux kernel contains source files that differ only by case,
and Windows-backed filesystems can collapse those names.

If you previously tried to build under `kernel/source` on `/mnt/c`, ignore that
tree or delete it. The build script now uses the WSL-native cache by default.

`build/build-initramfs.sh` also publishes the .NET `ServiceManager`, `Shell`,
and `devd` as Native AOT executables and copies them into `/system` inside the
initramfs. It generates the first service manifests under `/system/services`,
publishes the managed `HWorld` app bundle into `/Apps/HWorld.app`, and copies
the runtime libraries needed by the self-contained managed app. The WSL
toolchain uses .NET 10 and LLVM `lld` for the static native service publishes.

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
[ServiceManager] loaded 2 service manifest(s) from /system/services
[ServiceManager] listening on /run/myos/service.sock
[ServiceManager] idle
[ServiceManager] starting /system/devd
[devd] starting pid=...
[devd] listening on /run/myos/device.sock
[ServiceManager] starting /system/Shell
[Shell] starting pid=...
myos>
```

Use `reboot` or `poweroff` inside the shell for clean shutdown control.

## Current Scope

The tiny C `/init` in `src/Init/init.c` stays deliberately small. It proves the
boot path, mounts the basic runtime filesystems, starts `/system/ServiceManager`,
reaps any child process that exits, restarts `ServiceManager` if it exits, and
performs the final reboot or poweroff syscall when ServiceManager signals PID 1.
A tiny static `/system/initctl` helper lets managed services send that signal
without relying on dynamic native loading from Native AOT.

The C# `ServiceManager` is intentionally tiny for now. It prints its PID,
confirms the runtime mounts from `/proc/mounts`, loads JSON service manifests
from `/system/services`, supervises `devd` and `Shell`, launches app bundles,
and exposes the first SDK-backed IPC endpoint at `/run/myos/service.sock`.

The first device manager, `devd`, enumerates `/sys/class/*/*` and exposes a
read-only device list at `/run/myos/device.sock`.

Shared C# paths live in `MyOs.Core.SystemPaths`, so service, app, IPC, serial,
and Linux runtime paths have one source of truth before the layout evolves.

The custom shell currently supports:

```text
help
clear
echo <text>
status
services
apps
run <app>
start <service>
stop <service>
restart <service>
mounts
devices
pid
uptime
reboot
poweroff
```

`status`, `services`, `start`, `stop`, `restart`, `apps`, `run`, `reboot`, and
`poweroff` use the shared C# SDK to talk to `ServiceManager`. `devices` talks to
`devd` through the SDK. `Shell` remains protected from manual `stop`, but
ServiceManager can stop it during ordered shutdown. `apps` lists bundles under
`/Apps`, and `run HWorld` asks ServiceManager to launch the first managed .NET
app bundle as a ServiceManager child process.
