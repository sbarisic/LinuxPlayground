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
myosd service manager
  ↓
core .NET services
  ↓
custom shell / GUI / app runtime
```

Possible process tree:

```text
/init
  └─ myosd
      ├─ logd
      ├─ devd
      ├─ netd
      ├─ storaged
      ├─ inputd
      ├─ gfxd
      ├─ busd
      └─ shell
```

## Proposed Repository Layout

```text
/
├── TODO.md
├── README.md
├── LICENSE
├── build/
│   ├── build-kernel.sh
│   ├── build-rootfs.sh
│   ├── build-initramfs.sh
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
│   │   └── apps/
│   └── initramfs/
├── src/
│   ├── Init/
│   ├── MyOs.ServiceManager/
│   ├── MyOs.Logging/
│   ├── MyOs.Shell/
│   ├── MyOs.Bus/
│   ├── MyOs.DeviceManager/
│   ├── MyOs.Network/
│   ├── MyOs.Storage/
│   ├── MyOs.Input/
│   ├── MyOs.Graphics/
│   └── MyOs.Sdk/
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

* [ ] Add `README.md`.
* [ ] Add `TODO.md`.
* [ ] Add license file.
* [ ] Decide project name.
* [ ] Decide userspace license.
* [ ] Add `.gitignore`.
* [ ] Add basic directory structure.
* [ ] Add build scripts directory.
* [ ] Add docs directory.
* [ ] Add initial .NET solution file.
* [ ] Add initial C# projects.
* [ ] Add QEMU run script.

## Phase 1: Boot a Custom Linux Kernel

* [ ] Download or vendor Linux kernel source.
* [ ] Create minimal x86_64 kernel config.
* [ ] Enable initramfs support.
* [ ] Enable devtmpfs.
* [ ] Enable procfs.
* [ ] Enable sysfs.
* [ ] Enable tmpfs.
* [ ] Enable serial console.
* [ ] Enable framebuffer or DRM later.
* [ ] Enable ext4 or another root filesystem later.
* [ ] Build the kernel.
* [ ] Boot kernel in QEMU.
* [ ] Confirm kernel panic happens because no init exists.
* [ ] Add minimal initramfs with `/init`.
* [ ] Boot successfully into our init.

## Phase 2: Minimal `/init`

The first `/init` can be native C, Rust, Zig, or .NET Native AOT. It should stay small and boring.

Required behavior:

* [ ] Open `/dev/console`.
* [ ] Redirect stdin/stdout/stderr to `/dev/console`.
* [ ] Mount `devtmpfs` on `/dev`.
* [ ] Mount `proc` on `/proc`.
* [ ] Mount `sysfs` on `/sys`.
* [ ] Mount `tmpfs` on `/run`.
* [ ] Mount `tmpfs` on `/tmp`.
* [ ] Print boot status to console.
* [ ] Start `/system/myosd`.
* [ ] Reap zombie processes.
* [ ] Restart `myosd` if it exits unexpectedly.
* [ ] Handle shutdown/reboot commands later.

Do not make `/init` into the whole OS.

## Phase 3: .NET Build Strategy

* [ ] Create .NET solution.
* [ ] Create common runtime library project: `MyOs.Core`.
* [ ] Create Linux interop library project: `MyOs.Linux`.
* [ ] Create service framework library: `MyOs.Services`.
* [ ] Create IPC library: `MyOs.Ipc`.
* [ ] Create service manager project: `MyOs.ServiceManager`.
* [ ] Configure Native AOT publishing for early services.
* [ ] Configure self-contained publishing for non-critical services.
* [ ] Decide target runtime IDs:

  * [ ] `linux-x64`
  * [ ] `linux-arm64` later
* [ ] Create build script that publishes all .NET services into rootfs.
* [ ] Copy published binaries to `/system`.

Early `.csproj` settings for Native AOT services:

```xml
<PropertyGroup>
  <OutputType>Exe</OutputType>
  <TargetFramework>net10.0</TargetFramework>
  <RuntimeIdentifier>linux-x64</RuntimeIdentifier>
  <SelfContained>true</SelfContained>
  <PublishAot>true</PublishAot>
  <InvariantGlobalization>true</InvariantGlobalization>
</PropertyGroup>
```

## Phase 4: Service Manager

Create `myosd`, the main .NET service manager.

Responsibilities:

* [ ] Read service definitions.
* [ ] Start configured services.
* [ ] Track child process IDs.
* [ ] Restart critical services.
* [ ] Capture stdout/stderr.
* [ ] Forward logs to `logd`.
* [ ] Provide service status API.
* [ ] Provide start/stop/restart API.
* [ ] Handle ordered shutdown.
* [ ] Support dependencies between services later.
* [ ] Support service manifests later.

Example service manifest:

```json
{
  "name": "logd",
  "exec": "/system/logd",
  "restart": "always",
  "critical": true
}
```

## Phase 5: Logging Service

Create `logd`.

Responsibilities:

* [ ] Listen on `/run/myos/log.sock`.
* [ ] Accept log messages from services and apps.
* [ ] Write logs to `/var/log/system.log`.
* [ ] Mirror important logs to `/dev/console`.
* [ ] Add timestamps.
* [ ] Add service names.
* [ ] Add severity levels.
* [ ] Support log query API later.
* [ ] Support ring-buffer logging later.
* [ ] Support crash logs later.

Simple protocol idea:

```json
{"level":"info","service":"myosd","message":"started"}
```

## Phase 6: IPC Layer

Use Unix domain sockets as the first IPC mechanism.

* [ ] Implement socket server helper in C#.
* [ ] Implement socket client helper in C#.
* [ ] Use JSON-lines protocol at first.
* [ ] Add request/response IDs.
* [ ] Add error response format.
* [ ] Add service discovery later.
* [ ] Add binary protocol later if needed.
* [ ] Add peer credential checking.
* [ ] Add permission model later.
* [ ] Add file descriptor passing later.
* [ ] Add shared memory helper later.

Initial sockets:

```text
/run/myos/log.sock
/run/myos/service.sock
/run/myos/bus.sock
/run/myos/input.sock
/run/myos/gfx.sock
/run/myos/net.sock
/run/myos/storage.sock
```

## Phase 7: Shell

Create a minimal custom shell, not Bash.

Responsibilities:

* [ ] Run on `/dev/console`.
* [ ] Print prompt.
* [ ] Parse simple commands.
* [ ] Talk to `myosd` over IPC.
* [ ] Talk to `logd` over IPC.
* [ ] Implement built-in commands:

  * [ ] `help`
  * [ ] `clear`
  * [ ] `echo`
  * [ ] `status`
  * [ ] `services`
  * [ ] `start <service>`
  * [ ] `stop <service>`
  * [ ] `restart <service>`
  * [ ] `logs`
  * [ ] `mounts`
  * [ ] `devices`
  * [ ] `reboot`
  * [ ] `poweroff`
* [ ] Add command history later.
* [ ] Add line editing later.
* [ ] Add scripting later only if useful.

## Phase 8: Device Manager

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
* Put ugly Linux ABI code in `MyOs.Linux` or a small native helper library.

## Phase 9: Storage Service

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

## Phase 10: Network Service

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

## Phase 11: Input Service

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

## Phase 12: Graphics Service

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

## Phase 13: App Model

Define a custom app model.

Ideas:

```text
/apps/Calculator.app/
/apps/Settings.app/
/apps/Terminal.app/
```

Example app layout:

```text
Calculator.app/
├── manifest.json
├── app
├── resources/
└── permissions.json
```

Manifest fields:

* [ ] App name.
* [ ] App ID.
* [ ] Version.
* [ ] Executable path.
* [ ] Runtime type:

  * [ ] Native AOT
  * [ ] self-contained .NET
  * [ ] managed DLL later
* [ ] Required permissions.
* [ ] Required services.
* [ ] Icon path.
* [ ] Windowing mode.

Possible permissions:

* [ ] `graphics.window`
* [ ] `input.keyboard`
* [ ] `input.pointer`
* [ ] `network.client`
* [ ] `storage.read`
* [ ] `storage.write`
* [ ] `system.status`
* [ ] `service.control`

## Phase 14: MyOS SDK

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

* [ ] Hide raw socket IPC.
* [ ] Provide typed service clients.
* [ ] Provide logging API.
* [ ] Provide graphics API.
* [ ] Provide input API.
* [ ] Provide storage API.
* [ ] Provide service discovery.
* [ ] Provide app manifest helpers.
* [ ] Provide permission helpers.

## Phase 15: Build System

Create scripts to build the whole OS image.

Required scripts:

* [ ] `build/build-kernel.sh`
* [ ] `build/build-dotnet.sh`
* [ ] `build/build-rootfs.sh`
* [ ] `build/build-initramfs.sh`
* [ ] `build/run-qemu.sh`
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
create initramfs
  ↓
boot in QEMU
```

## Phase 16: QEMU Target

Initial QEMU target:

* [ ] x86_64.
* [ ] Serial console.
* [ ] Initramfs boot.
* [ ] No disk initially.
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

## Phase 17: Documentation

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

## Phase 18: Licensing

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

* [ ] Linux kernel boots in QEMU.
* [ ] `/init` runs.
* [ ] Console output works.
* [ ] System does not immediately panic.

### Milestone 2: Mounted Runtime Environment

* [ ] `/dev` mounted.
* [ ] `/proc` mounted.
* [ ] `/sys` mounted.
* [ ] `/run` mounted.
* [ ] `/tmp` mounted.

### Milestone 3: First .NET Service

* [ ] Native AOT `myosd` runs.
* [ ] `/init` starts `myosd`.
* [ ] `myosd` prints to console.
* [ ] `myosd` stays alive.

### Milestone 4: Logging

* [ ] `logd` starts.
* [ ] Other services can send log messages.
* [ ] Logs appear on console.
* [ ] Logs are saved to file.

### Milestone 5: Shell

* [ ] Custom shell starts.
* [ ] User can type commands.
* [ ] Shell can query service status.
* [ ] Shell can reboot/poweroff.

### Milestone 6: Basic IPC

* [ ] Unix domain socket server works.
* [ ] Unix domain socket client works.
* [ ] Request/response protocol works.
* [ ] Multiple services can communicate.

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
* [ ] Should `myosd` be PID 1 eventually, or stay as a child of a tiny init?
* [ ] Should IPC start as JSON-lines or binary from the beginning?
* [ ] Should service manifests be JSON, TOML, YAML, or custom?
* [ ] Should apps be Native AOT only at first?
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
start logging service
start custom shell
accept simple commands
reboot cleanly
```

Everything else comes after that.
