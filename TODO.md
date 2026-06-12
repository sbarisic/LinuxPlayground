# TODO: Custom .NET-Based Linux Userspace OS

## Goal

Create a toy operating system distribution using the Linux kernel, but with a userspace written from scratch.

This is not intended to be a normal GNU/Linux distribution. The kernel, drivers, syscalls, process model, filesystems, and hardware support will come from Linux, but the visible operating system layer should be custom.

The userspace should be primarily written in .NET/C#.

## Core Design Decisions

* Use the Linux kernel.
* Build the kernel ourselves.
* Build our own root filesystem.
* Do not start from Debian, Alpine, Arch, BusyBox, systemd, OpenRC, or another existing userspace.
* Use a custom `/init`.
* Use .NET for the main userspace services.
* Prefer .NET Native AOT for early boot and core system services.
* Use normal self-contained .NET apps later where Native AOT is too restrictive.
* Use Unix domain sockets for service-to-service and app-to-service IPC.
* Use shared memory for high-bandwidth IPC such as graphics buffers.
* Keep Linux details internal where possible.
* Expose a custom OS API to apps instead of exposing raw `/dev`, `/proc`, `/sys`, and Linux-specific details.

## Intended Architecture

```text
bootloader
  ↓
custom-built Linux kernel
  ↓
initramfs / rootfs
  ↓
/init
  ↓
ServiceManager
  ↓
core .NET services
  ↓
custom shell / GUI / app runtime
```

Possible process tree:

```text
/init
  └─ ServiceManager
      ├─ devd
      ├─ netd
      ├─ storaged
      ├─ inputd
      ├─ gfxd
      ├─ busd
      └─ Shell
```

## Proposed Repository Layout

```text
/
├── TODO.md
├── README.md
├── LICENSE
├── LinuxPlayground.sln
├── build/
│   ├── build-kernel.sh
│   ├── build-dotnet.sh
│   ├── build-rootfs.sh
│   ├── build-initramfs.sh
│   ├── install-prereqs.sh
│   ├── check-prereqs.sh
│   ├── run-qemu.sh
│   └── clean.sh
├── kernel/
│   ├── config/
│   │   └── x86_64.config
│   └── patches/
├── rootfs/
│   ├── layout/
│   │   ├── dev/
│   │   ├── proc/
│   │   ├── sys/
│   │   ├── run/
│   │   ├── tmp/
│   │   ├── system/
│   │   └── Apps/
│   └── initramfs/
├── src/
│   ├── Apps/
│   │   └── HWorld/
│   ├── Init/
│   ├── MyOs.Core/
│   ├── MyOs.Ipc/
│   ├── MyOs.Sdk/
│   ├── MyOs.ServiceManager/
│   ├── MyOs.Shell/
│   ├── MyOs.Bus/
│   ├── MyOs.DeviceManager/
│   ├── MyOs.Network/
│   ├── MyOs.Storage/
│   ├── MyOs.Input/
│   └── MyOs.Graphics/
├── docs/
│   ├── architecture.md
│   ├── boot.md
│   ├── ipc.md
│   ├── services.md
│   ├── graphics.md
│   └── app-model.md
└── tools/
```

## Phase 0: Repository Bootstrap

* [x] Add `README.md`.
* [x] Add `TODO.md`.
* [x] Add license file.
* [ ] Decide project name.
* [ ] Decide userspace license.
* [x] Add `.gitignore`.
* [x] Add basic directory structure.
* [x] Add build scripts directory.
* [ ] Add docs directory.
* [x] Add initial .NET solution file.
* [x] Add initial C# projects.
* [x] Add QEMU run script.

## Phase 1: Boot a Custom Linux Kernel

* [x] Download or vendor Linux kernel source.
* [x] Create minimal x86_64 kernel config.
* [x] Enable initramfs support.
* [x] Enable devtmpfs.
* [x] Enable procfs.
* [x] Enable sysfs.
* [x] Enable tmpfs.
* [x] Enable serial console.
* [x] Enable VGA text console.
* [ ] Enable framebuffer or DRM later.
* [ ] Enable ext4 or another root filesystem later.
* [x] Build the kernel.
* [x] Boot kernel in QEMU.
* [ ] Confirm kernel panic happens because no init exists.
* [x] Add minimal initramfs with `/init`.
* [x] Boot successfully into our init.

## Phase 2: Minimal `/init`

The first `/init` can be native C, Rust, Zig, or .NET Native AOT. It should stay small and boring.

Required behavior:

* [x] Open `/dev/console`.
* [x] Redirect stdin/stdout/stderr to `/dev/console`.
* [x] Mount `devtmpfs` on `/dev`.
* [x] Mount `proc` on `/proc`.
* [x] Mount `sysfs` on `/sys`.
* [x] Mount `tmpfs` on `/run`.
* [x] Mount `tmpfs` on `/tmp`.
* [x] Print boot status to console.
* [x] Start `/system/ServiceManager`.
* [x] Reap zombie processes.
* [x] Reap orphaned child processes while `ServiceManager` is alive.
* [x] Restart `ServiceManager` if it exits unexpectedly.
* [ ] Handle shutdown/reboot commands later.

Do not make `/init` into the whole OS.

## Phase 3: .NET Build Strategy

* [x] Create .NET solution.
* [x] Create common runtime library project: `MyOs.Core`.
* [x] Add shared system path constants.
* [ ] Create service framework library: `MyOs.Services`.
* [x] Create IPC library: `MyOs.Ipc`.
* [x] Create shared SDK project: `MyOs.Sdk`.
* [x] Put native Linux/syscall helper area under `MyOs.Sdk`.
* [x] Create service manager project: `MyOs.ServiceManager`.
* [x] Create shell project: `MyOs.Shell`.
* [x] Create first managed app project: `HWorld`.
* [x] Configure Native AOT publishing for early services.
* [x] Configure self-contained publishing for managed apps.
* [ ] Decide target runtime IDs:

  * [x] `linux-x64`
  * [ ] `linux-arm64` later
* [x] Create build script that publishes all .NET services into rootfs.
* [x] Copy published binaries to `/system`.
* [x] Copy published app bundles to `/Apps`.

Early `.csproj` settings for Native AOT services:

```xml
<PropertyGroup>
  <OutputType>Exe</OutputType>
  <TargetFramework>net10.0</TargetFramework>
  <RuntimeIdentifier>linux-x64</RuntimeIdentifier>
  <SelfContained>true</SelfContained>
  <PublishAot>true</PublishAot>
  <StaticExecutable>true</StaticExecutable>
  <LinkerFlavor>lld</LinkerFlavor>
  <InvariantGlobalization>true</InvariantGlobalization>
</PropertyGroup>
```

## Phase 4: Service Manager

Create `ServiceManager`, the main .NET service manager.

Responsibilities:

* [x] Start as a Native AOT executable from `/system/ServiceManager`.
* [x] Print boot status to console.
* [x] Confirm `/dev`, `/proc`, `/sys`, `/run`, and `/tmp` are mounted.
* [x] Start `/system/Shell`.
* [x] Restart `Shell` if it exits unexpectedly.
* [ ] Read service definitions.
* [ ] Start configured services.
* [x] Track child process IDs.
* [ ] Restart critical services.
* [ ] Capture stdout/stderr.
* [x] Provide service status API.
* [x] Provide start/stop/restart API.
* [ ] Handle ordered shutdown.
* [ ] Support dependencies between services later.
* [ ] Support service manifests later.

Example service manifest:

```json
{
  "name": "Shell",
  "exec": "/system/Shell",
  "restart": "always",
  "critical": true
}
```

## Phase 5: IPC Layer

Use Unix domain sockets as the first IPC mechanism.

* [x] Implement socket server helper in C#.
* [x] Implement socket client helper in C#.
* [x] Use JSON-lines protocol at first.
* [x] Add request/response IDs.
* [x] Add error response format.
* [ ] Add service discovery later.
* [ ] Add binary protocol later if needed.
* [ ] Add peer credential checking.
* [ ] Add permission model later.
* [ ] Add file descriptor passing later.
* [ ] Add shared memory helper later.

Initial sockets:

```text
/run/myos/service.sock
/run/myos/bus.sock
/run/myos/input.sock
/run/myos/gfx.sock
/run/myos/net.sock
/run/myos/storage.sock
```

Shared path constants:

* [x] Put `/run/myos/service.sock` behind `MyOs.Core.SystemPaths`.
* [x] Keep `IpcPaths.ServiceManagerSocket` as the compatibility entrypoint for IPC callers.

## Phase 6: Shell

Create a minimal custom shell, not Bash.

Responsibilities:

* [x] Run on `/dev/console`.
* [x] Print prompt.
* [x] Parse simple commands.
* [x] Talk to `ServiceManager` over IPC.
* [ ] Implement built-in commands:

  * [x] `help`
  * [x] `clear`
  * [x] `echo`
  * [ ] `status`
  * [x] `apps`
  * [x] `run <app>`
  * [x] `services`
  * [x] `start <service>`
  * [x] `stop <service>`
  * [x] `restart <service>`
  * [x] `mounts`
  * [ ] `devices`
  * [x] `pid`
  * [x] `uptime`
  * [x] `reboot`
  * [x] `poweroff`
* [ ] Add command history later.
* [ ] Add line editing later.
* [ ] Add scripting later only if useful.

## Phase 7: Device Manager

Create `devd`.

Responsibilities:

* [ ] Enumerate `/sys`.
* [ ] Watch kernel uevents.
* [ ] Track devices.
* [ ] Expose device list over IPC.
* [ ] Expose block devices.
* [ ] Expose input devices.
* [ ] Expose network devices.
* [ ] Expose display/GPU devices.
* [ ] Handle firmware loading if needed.
* [ ] Handle permissions for `/dev` nodes later.

Implementation notes:

* May require native interop for netlink.
* May require parsing `/sys`.
* Keep the high-level logic in C#.
* Put ugly Linux ABI code behind SDK native helpers instead of exposing it to apps.

## Phase 8: Storage Service

Create `storaged`.

Responsibilities:

* [ ] List block devices.
* [ ] Detect partitions.
* [ ] Mount filesystems.
* [ ] Unmount filesystems.
* [ ] Track mounted volumes.
* [ ] Expose storage API to apps.
* [ ] Support initramfs-only mode first.
* [ ] Support real root filesystem later.
* [ ] Support ext4 first.
* [ ] Support FAT32 later.
* [ ] Add read-only safety mode.

Likely required Linux calls:

* `mount`
* `umount2`
* block-device ioctls
* `/proc/mounts`
* `/sys/class/block`

## Phase 9: Network Service

Create `netd`.

Responsibilities:

* [ ] Bring up loopback.
* [ ] List network interfaces.
* [ ] Configure static IPv4.
* [ ] Implement or integrate minimal DHCP client.
* [ ] Configure DNS.
* [ ] Expose network status over IPC.
* [ ] Add TCP/IP diagnostic commands.
* [ ] Add simple HTTP test client.
* [ ] Add firewalling later.
* [ ] Add Wi-Fi later if desired.

Implementation notes:

* Use netlink where possible.
* Start with loopback and manual static IP.
* DHCP can be implemented later.
* Avoid depending on NetworkManager, systemd-networkd, or dhcpcd.

## Phase 10: Input Service

Create `inputd`.

Responsibilities:

* [ ] Read `/dev/input/event*`.
* [ ] Decode keyboard events.
* [ ] Decode mouse events.
* [ ] Expose input events over IPC.
* [ ] Support keyboard layout later.
* [ ] Support key repeat later.
* [ ] Support gamepads later.
* [ ] Support permissions later.

Implementation notes:

* Apps should not read `/dev/input` directly.
* `inputd` should become the single trusted input broker.

## Phase 11: Graphics Service

Create `gfxd`.

Start simple, then evolve.

Stage 1:

* [ ] Use Linux framebuffer or DRM dumb buffer.
* [ ] Draw directly to screen.
* [ ] Clear screen.
* [ ] Draw rectangles.
* [ ] Draw text.
* [ ] Display mouse cursor.

Stage 2:

* [ ] Own `/dev/dri/card0`.
* [ ] Use DRM/KMS.
* [ ] Allocate buffers.
* [ ] Present frames.
* [ ] Implement a simple compositor.

Stage 3:

* [ ] Add app windows.
* [ ] Use Unix socket control protocol.
* [ ] Use shared memory for app framebuffers.
* [ ] Add damage tracking.
* [ ] Add input routing.
* [ ] Add basic window manager.

Stage 4:

* [ ] Add GPU acceleration later.
* [ ] Investigate GBM/EGL.
* [ ] Investigate Vulkan.
* [ ] Investigate Mesa integration.

Do not make every app talk to DRM/KMS directly. `gfxd` should own display hardware.

## Phase 12: App Model

Define a custom app model.

Ideas:

```text
/Apps/Calculator.app/
/Apps/Settings.app/
/Apps/Terminal.app/
```

Example app layout:

```text
Calculator.app/
├── manifest.json
├── bin/
│   └── Calculator.dll
└── resources/
```

Manifest fields:

* [ ] App name.
* [ ] App ID.
* [ ] Version.
* [ ] Executable path.
* [ ] Required permissions.
* [ ] Required services.
* [ ] Icon path.
* [ ] Windowing mode.

Early app assumptions:

* [x] Apps live under `/Apps/<Name>.app/`.
* [x] Apps are managed .NET code for now.
* [x] `manifest.json` does not need a `runtime` field yet.
* [x] The first app can use `bin/<AppName>` as its entry point.
* [x] Add first hello-world app bundle: `/Apps/HWorld.app/`.
* [x] Add shell app launcher for `run HWorld`.

Possible permissions:

* [ ] `graphics.window`
* [ ] `input.keyboard`
* [ ] `input.pointer`
* [ ] `network.client`
* [ ] `storage.read`
* [ ] `storage.write`
* [ ] `system.status`
* [ ] `service.control`

## Phase 13: MyOS SDK

Create a C# SDK for apps.

Example API shape:

```csharp
MyOs.Log.Info("App started");

var window = MyOs.Graphics.CreateWindow(800, 600, "Demo");
window.DrawText(20, 20, "Hello from MyOS");
window.Present();

var status = await MyOs.Network.GetStatusAsync();
```

SDK responsibilities:

* [x] Hide raw socket IPC.
* [x] Provide typed service clients.
* [x] Provide a native Linux/syscall helper area for internal SDK use.
* [ ] Provide logging API.
* [ ] Provide graphics API.
* [ ] Provide input API.
* [ ] Provide storage API.
* [x] Provide basic service discovery.
* [ ] Provide app manifest helpers.
* [ ] Provide permission helpers.

Native helper responsibilities:

* [x] Start with an internal namespace for Linux-specific helpers.
* [ ] Wrap `mount` and `umount2` when storage needs them.
* [ ] Wrap `ioctl` when device, input, storage, or graphics code needs it.
* [ ] Wrap `reboot` when shutdown control moves beyond shell stubs.
* [ ] Keep raw ABI details out of app-facing APIs.

## Phase 14: Build System

Create scripts to build the whole OS image.

Required scripts:

* [x] `build/build-kernel.sh`
* [x] `build/build-dotnet.sh`
* [ ] `build/build-rootfs.sh`
* [x] `build/build-initramfs.sh`
* [x] `build/run-qemu.sh`
* [ ] `build/clean.sh`

Build flow:

```text
build kernel
  ↓
publish .NET services
  ↓
create rootfs directory
  ↓
copy /init
  ↓
copy /system services
  ↓
copy /Apps bundles
  ↓
copy managed app runtime dependencies
  ↓
create initramfs
  ↓
boot in QEMU
```

## Phase 15: QEMU Target

Initial QEMU target:

* [x] x86_64.
* [x] Serial console.
* [x] VGA console.
* [x] Initramfs boot.
* [x] No disk initially.
* [ ] Add virtual disk later.
* [ ] Add virtio devices later.
* [ ] Add networking later.
* [ ] Add framebuffer/graphics later.

Example boot target:

```text
qemu-system-x86_64
  -kernel kernel/bzImage
  -initrd build/initramfs.cpio.gz
  -append "console=ttyS0 rdinit=/init"
  -nographic
```

## Phase 16: Documentation

Write docs as the project evolves.

* [ ] `docs/architecture.md`
* [ ] `docs/boot.md`
* [ ] `docs/init.md`
* [ ] `docs/services.md`
* [ ] `docs/ipc.md`
* [ ] `docs/device-manager.md`
* [ ] `docs/networking.md`
* [ ] `docs/storage.md`
* [ ] `docs/graphics.md`
* [ ] `docs/app-model.md`
* [ ] `docs/building.md`
* [ ] `docs/running-qemu.md`
* [ ] `docs/licensing.md`

## Phase 17: Licensing

Initial plan:

* [ ] Linux kernel remains GPL-2.0-only.
* [ ] Kernel patches should be GPL-2.0-compatible.
* [ ] Custom userspace can use MIT or Apache-2.0.
* [ ] Track third-party licenses.
* [ ] Add `THIRD_PARTY_NOTICES.md`.
* [ ] Do not imply the entire OS is one single license.
* [ ] Clearly document that the OS image combines multiple components with separate licenses.

Suggested userspace license:

```text
MIT
```

Suggested kernel patch license:

```text
GPL-2.0-only
```

## Early Milestones

### Milestone 1: First Boot

* [x] Linux kernel boots in QEMU.
* [x] `/init` runs.
* [x] Console output works.
* [x] System does not immediately panic.

### Milestone 2: Mounted Runtime Environment

* [x] `/dev` mounted.
* [x] `/proc` mounted.
* [x] `/sys` mounted.
* [x] `/run` mounted.
* [x] `/tmp` mounted.

### Milestone 3: First .NET Service

* [x] Native AOT `ServiceManager` runs.
* [x] `/init` starts `ServiceManager`.
* [x] `ServiceManager` prints to console.
* [x] `ServiceManager` stays alive.

### Milestone 4: Shell

* [x] Custom shell starts.
* [x] User can type commands.
* [x] Shell can query service status.
* [ ] Shell can reboot/poweroff.

### Milestone 5: Basic IPC

* [x] Unix domain socket server works.
* [x] Unix domain socket client works.
* [x] Request/response protocol works.
* [ ] Multiple services can communicate.

### Milestone 6: First App Bundle

* [x] `/Apps/HWorld.app` is packaged.
* [x] Shell can list app bundles.
* [x] Shell can run `HWorld`.
* [x] Managed app can use the SDK.

### Milestone 7: Basic Devices

* [ ] Device manager can list `/sys`.
* [ ] Input service can read keyboard.
* [ ] Storage service can list block devices.
* [ ] Network service can list network interfaces.

### Milestone 8: Basic Graphics

* [ ] Display output works.
* [ ] Draw pixels.
* [ ] Draw text.
* [ ] Show simple GUI shell.

## Non-Goals For Now

* [ ] Do not build a general-purpose GNU/Linux distribution.
* [ ] Do not use systemd.
* [ ] Do not use BusyBox as the main userspace.
* [ ] Do not use an existing distro rootfs.
* [ ] Do not build a full POSIX shell initially.
* [ ] Do not support real hardware initially.
* [ ] Do not support secure multi-user operation initially.
* [ ] Do not support package management initially.
* [ ] Do not support GPU acceleration initially.
* [ ] Do not support Wayland/Xorg initially.
* [ ] Do not support containers initially.
* [ ] Do not support no-MMU systems.
* [ ] Do not try to replace the Linux kernel.

## Open Questions

* [ ] Project name?
* [ ] Should `/init` be native C/Rust/Zig or .NET Native AOT?
* [ ] Should `ServiceManager` be PID 1 eventually, or stay as a child of a tiny init?
* [ ] Should IPC start as JSON-lines or binary from the beginning?
* [ ] Should service manifests be JSON, TOML, YAML, or custom?
* [x] Should apps be Native AOT only at first? No; start with managed .NET apps.
* [ ] Should the first graphics target be framebuffer or DRM/KMS?
* [ ] Should the root filesystem initially be initramfs-only?
* [ ] Should the first real disk filesystem be ext4?
* [ ] Should the OS expose Linux paths to apps or hide them completely?
* [ ] Should the app model be capability-based from the start?
* [ ] Should the shell be text-only first or GUI-first?

## Development Rule

Keep the first version extremely small.

The first successful system should only do this:

```text
boot Linux
run /init
mount basic filesystems
start .NET service manager
start custom shell
list and run first app bundle
accept simple commands
reboot cleanly
```

Everything else comes after that.
