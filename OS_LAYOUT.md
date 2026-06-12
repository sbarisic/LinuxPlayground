# LinuxPlayground OS Layout

This document describes the intended virtual filesystem as seen by the OS and,
eventually, by applications and users. It is not a normal Linux distribution
layout. Linux provides the kernel, drivers, syscalls, and low-level virtual
filesystems, but LinuxPlayground should expose a smaller, opinionated operating
system surface.

The current boot milestone needs `/init`, `/dev`, `/proc`, `/sys`, `/run`,
`/tmp`, `/system/ServiceManager`, and `/system/Shell`. The rest of this layout
describes where the system should grow.

## Design Idea

From the user perspective, the OS should feel like one coherent environment,
not like a collection of Linux implementation details. Users should interact
with apps, settings, storage locations, devices, and services through
LinuxPlayground concepts. Paths such as `/dev/input/event0`, `/proc/mounts`,
and `/sys/class/block` are implementation details for trusted system services,
not the normal app or user API.

The filesystem has three broad layers:

1. Boot and kernel-facing internals.
2. OS-owned services, configuration, logs, and runtime state.
3. User-facing apps, data, settings, and mounted volumes.

The early system may run entirely from an initramfs. Later, the same namespace
can be backed by a real root filesystem plus mounted data volumes.

## High-Level Tree

```text
/
├── init
├── system/
├── apps/
├── users/
├── volumes/
├── config/
├── var/
├── run/
├── tmp/
├── dev/
├── proc/
└── sys/
```

## Root `/`

`/` is the complete OS namespace. It should stay small and readable. Top-level
entries should represent OS concepts, not inherited Unix tradition by default.

The root should not become a dumping ground for compatibility paths. If a path
exists only because Linux needs it, keep it documented as an internal node and
avoid making applications depend on it.

## Boot Node

### `/init`

The first userspace process launched by the kernel. It is PID 1 and is
responsible for creating the minimum runtime environment.

Responsibilities:

* Open and attach `/dev/console`.
* Mount `/dev`, `/proc`, `/sys`, `/run`, and `/tmp`.
* Print early boot status.
* Start `/system/ServiceManager`.
* Wait for the service manager and restart it if it exits.

`/init` should stay tiny. It is not the shell, the service manager, the device
manager, or the whole OS.

## OS-Owned Nodes

### `/system`

OS-owned runtime files. This is where the core userspace lives.

Expected contents:

```text
/system/
├── ServiceManager
├── Shell
├── logd
├── busd
├── devd
├── storaged
├── netd
├── inputd
├── gfxd
├── shell
├── lib/
├── services/
└── manifests/
```

Early builds place service executables directly in `/system`; currently these
are the Native AOT `ServiceManager` and `Shell`. As the OS grows,
`/system/services` and `/system/manifests` can hold service definitions,
dependency metadata, restart policy, and permissions.

User apps should not write here. Treat it as read-only at runtime once the
system is mature.

### `/config`

Persistent system configuration owned by the OS, not by individual users.

Examples:

```text
/config/
├── system.json
├── services/
├── network/
├── storage/
└── display/
```

This is for settings such as enabled services, default network configuration,
hostname, display mode, and storage policy. In early initramfs-only boots this
may not exist or may be read-only defaults.

### `/var`

Persistent system data that changes over time.

Expected contents:

```text
/var/
├── log/
├── crash/
├── spool/
└── state/
```

`logd` should eventually write persistent logs under `/var/log`. Crash reports,
service state snapshots, and other OS-maintained mutable data belong here.

If the machine is running initramfs-only, `/var` may be temporary or absent.

### `/run`

Runtime state for the current boot. This should be a `tmpfs`.

Expected contents:

```text
/run/
└── myos/
    ├── log.sock
    ├── service.sock
    ├── bus.sock
    ├── input.sock
    ├── gfx.sock
    ├── net.sock
    └── storage.sock
```

Use `/run` for Unix domain sockets, PID files if needed, locks, and temporary
service coordination. Nothing here survives a reboot.

### `/tmp`

Temporary scratch space for the current boot. This should also be a `tmpfs`.

Apps and services can use it for short-lived files that have no meaning after
reboot. Do not store logs, user documents, or durable state here.

## User-Facing Nodes

### `/apps`

Installed applications. Applications are packaged as directories, not scattered
across system paths.

Example:

```text
/apps/
├── Calculator.app/
│   ├── manifest.json
│   ├── app
│   ├── resources/
│   └── permissions.json
└── Terminal.app/
    ├── manifest.json
    ├── app
    ├── resources/
    └── permissions.json
```

The user should think of `/apps/*.app` as installable app bundles. The app
manifest describes its name, app ID, executable, runtime type, icon, requested
permissions, and required services.

Apps should use the MyOS SDK and service APIs instead of reading `/dev`,
`/proc`, or `/sys` directly.

### `/users`

User-owned data and settings. The first OS version may have one implicit user,
but the layout should not block multiple users later.

Example:

```text
/users/
└── default/
    ├── Desktop/
    ├── Documents/
    ├── Downloads/
    ├── Pictures/
    ├── Videos/
    ├── AppsData/
    └── Settings/
```

User-visible files belong here. Per-user app data should live under
`/users/<name>/AppsData/<app-id>/` rather than inside `/apps`.

### `/volumes`

Mounted storage volumes presented in a user-friendly way.

Example:

```text
/volumes/
├── System/
├── Data/
├── UsbDrive/
└── Cdrom/
```

`storaged` owns the mapping between kernel block devices and these names. Users
and apps should see volumes as named storage locations, not as raw devices like
`/dev/sda1`.

The early system can skip this until real disks are introduced.

## Linux Internal Nodes

These paths exist because the Linux kernel and low-level services need them.
They are not the primary application API.

### `/dev`

Device nodes from `devtmpfs`.

Examples:

```text
/dev/console
/dev/null
/dev/zero
/dev/input/event0
/dev/dri/card0
/dev/sda
```

Trusted services consume these nodes:

* `inputd` owns keyboard and pointer input.
* `gfxd` owns display devices.
* `storaged` owns block devices.
* `netd` owns network configuration through kernel APIs.

Normal apps should not directly open hardware device nodes. They should request
capabilities through the OS API.

### `/proc`

Linux process and kernel information from `procfs`.

Useful to OS internals for process inspection, mounts, memory information, and
some kernel status. The service manager and diagnostics tools may read it, but
apps should not treat it as stable public API.

### `/sys`

Linux device and driver model from `sysfs`.

`devd`, `storaged`, `inputd`, `netd`, and `gfxd` can inspect `/sys` to discover
devices and capabilities. Higher-level services should translate that raw view
into LinuxPlayground concepts such as devices, displays, network interfaces,
and volumes.

## Service Ownership

The filesystem is intentionally service-owned:

```text
/init          -> init
/system        -> build system and OS updates
/config        -> settings service / service manager
/var/log       -> logd
/run/myos      -> ServiceManager and service IPC endpoints
/apps          -> app installer / app manager
/users         -> shell, GUI, apps, storage service
/volumes       -> storaged
/dev           -> kernel devtmpfs, mediated by devd and service daemons
/proc          -> kernel procfs, internal diagnostics
/sys           -> kernel sysfs, internal device discovery
```

This ownership model keeps one service responsible for each part of the tree.
It also keeps the future permission model simpler: apps request access to OS
capabilities, and services decide which filesystem nodes or kernel interfaces
are actually touched.

## User Perspective

The shell or GUI should present simple concepts:

* Apps are in `Apps`.
* User files are in the user's home area.
* Drives and mounted media are named volumes.
* System status comes from services, not from manually reading `/proc`.
* Devices are listed through the OS device API, not by browsing `/dev`.
* Logs are available through a logs command or viewer, not by tailing random
  files.

The implementation can still use Linux heavily underneath. The important rule is
that Linux-specific paths should be treated as backend plumbing. LinuxPlayground
apps should be written against the custom SDK and service protocols so the OS
can change the backend without changing app behavior.

## Early Boot Layout

The current initramfs skeleton contains:

```text
/
├── init
├── dev/
├── proc/
├── sys/
├── run/
├── tmp/
└── system/
    ├── ServiceManager
    └── Shell
```

That is enough to prove:

* The kernel can load the initramfs.
* `/init` can run as PID 1.
* VGA console output and keyboard input work.
* Serial output can be captured separately for logs.
* Basic Linux virtual filesystems can be mounted.
* The C# Native AOT service manager can start from `/system/ServiceManager`.
* `/init` can supervise the service manager as a child process.
* `ServiceManager` can supervise an interactive shell as a child process.

Everything else should be added only when a real service or user workflow needs
it.
