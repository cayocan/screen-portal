# Screen Portal

**Virtual monitors for Meta Quest 3 — as many as you want, sharp enough to code on for hours.**

Screen Portal replaces physical monitors with virtual screens floating around you. Create any number of monitors,
pick each one's resolution and aspect ratio (ultrawide and portrait included), place and size them freely in 3D,
curve them, and keep using your real mouse and keyboard: the cursor goes where the screen *is* in space, not where
Windows thinks it is.

Two non-negotiable requirements drive every decision: it must be **light** (no stutter, no wasted battery) and the
**text must be crisp**.

> **Status:** active development. Phases 0–3 are done (video pipeline, clean skeleton, free multi-monitor,
> mouse-and-keyboard-first interaction). Next up: the UI/UX pass based on the [design system](docs/DesignSystem/).
> See [docs/STATUS.md](docs/STATUS.md) for the detailed state.

---

## Table of contents

1. [How it works](#how-it-works)
2. [Features](#features)
3. [Architecture](#architecture)
4. [Repository layout](#repository-layout)
5. [Requirements](#requirements)
6. [Getting started](#getting-started)
7. [Testing](#testing)
8. [Tooling scripts](#tooling-scripts)
9. [Keyboard shortcuts](#keyboard-shortcuts)
10. [Performance budget](#performance-budget)
11. [Engineering rules](#engineering-rules)
12. [Roadmap](#roadmap)
13. [Documentation index](#documentation-index)

---

## How it works

The product is **two programs working together**:

| | **Quest app** (what the user sees) | **Windows host** (runs in the background) |
|---|---|---|
| **What it is** | Native standalone headset app (APK). No cable, no Quest Link, no SteamVR | Lightweight tray app that starts with Windows |
| **Responsible for** | Drawing the screens in 3D, passthrough, menus, hands/controllers, **decoding video**, drawing the cursor, **applying screen manipulation** | Creating virtual monitors, **capturing and encoding** each one, streaming over Wi-Fi, audio, **spatial cursor routing**, mouse capture for manipulation, **global hotkeys** |
| **Where the user's apps run** | Nowhere — the Quest only *displays* | On the PC, as usual. Keyboard and mouse stay plugged into the PC |

**Daily flow:** the host starts with Windows → the user opens the app on the Quest → the app finds the PC on the
network by itself (mDNS) and connects → the screens come back exactly as they were in the last session.

**Why not render everything on the PC (Link/SteamVR model)?** That model renders the whole VR scene on the PC GPU,
requires Link/Air Link and usually degrades text and passthrough. Here the PC only *compresses screens*; the Quest
does the rest with native compositor layers, which keep text sharp because the image is never resampled through the
3D scene.

---

## Features

The MVP (v1.0) is built around **three pillars** that must reach the quality bar before launch
([brief §4](docs/brief.md)):

### A. Sharp, free screens
- **N virtual monitors**, decoupled from physical monitors (virtual display driver, [ADR 0007](docs/adr/0007-virtual-display-driver.md)).
- **Resolution chosen at creation** — aspect ratio and landscape/portrait orientation. Resizing afterwards is a
  proportional zoom: **the resolution never changes**. Changing it is an explicit *Recreate* action (with undo).
- Free placement at any distance and angle, on an invisible **Cylinder or Sphere layout surface** whose snapping
  always helps and never traps ([ADR 0005](docs/adr/0005-layout-surface-cylinder-sphere.md)).
- **Flat or curved screens** with adjustable curvature; every curved screen is a cylinder compositor layer
  ([ADR 0011](docs/adr/0011-every-curved-screen-is-a-cylinder-layer.md)).
- **Per-screen frame rate:** Auto (the screen you look at runs at 60 fps, the rest drop), Video, Fixed 30/15 and Eco,
  under a shared bandwidth budget.
- **PC audio on the Quest** and **Quest microphone on the PC** ([ADR 0015](docs/adr/0015-audio-steam-streaming-drivers.md)).
- **Last session autosave** — the workspace comes back when the app restarts ([ADR 0014](docs/adr/0014-last-session-autosave.md)).
- The Windows display arrangement follows the layout in the headset ([ADR 0016](docs/adr/0016-windows-arrangement-follows-quest.md)).

### B. Passthrough desk zone — "you draw it, the app remembers"
A user-drawn region of the desk shows the real world (keyboard, mouse, hands), works with **any keyboard**, and is
anchored to the desk and restored automatically ([ADR 0003](docs/adr/0003-passthrough-zone-user-defined.md)).
*Planned for Phase 4.*

### C. Mouse and keyboard first
- **Spatial cursor:** the mouse travels to where a screen is in 3D (left, right, above, diagonal, across gaps).
- **Cursor drawn locally in the headset** as a compositor layer, independent of video latency; outside the screens
  it becomes a **3D world cursor** that operates the whole app UI ([ADR 0017](docs/adr/0017-world-cursor-and-edit-mode.md)).
- **Edit mode** with a gizmo: move X/Y/depth, rotate, scale — with mouse, controllers or hands.
- **Global keyboard shortcuts** for everything (see [below](#keyboard-shortcuts)).
- Hands and controllers are an extra adapter, never required. Manipulation clicks never leak to Windows, and normal
  clicks are never swallowed.

Post-MVP ideas live in the backlog ([brief §4.3](docs/brief.md)): readability assistant, look-and-jump, focus mode,
HEVC + FEC, multiple PCs, Mac host, etc.

---

## Architecture

**Hybrid Unity + native.** Unity handles the "brain" and the interaction; native code handles the video path.
**A video frame never crosses C#** on the headset — `MediaCodec` decodes straight into the Surface of an
`OVROverlay` compositor layer (zero-copy). See [ADR 0001](docs/adr/0001-hybrid-unity-native-video-path.md).

```
┌──────────────────────────── PC (Windows) ─────────────────────────────┐
│  Virtual display driver (SudoVDA) ──► N virtual monitors              │
│                                                                       │
│  Host (.NET 10)                                                       │
│   ├─ Session / control channel (TCP 9200, FlatBuffers) + mDNS         │
│   ├─ StreamPipeline × N  [WGC capture ► HW H.264 ► packetize ► UDP]   │
│   ├─ Frame-rate policy + bandwidth budget                             │
│   ├─ Spatial cursor router (low-level mouse hook + SpatialMap)        │
│   ├─ Cursor feed (position/shape → headset)                           │
│   ├─ Virtual monitor manager · Windows arrangement                    │
│   ├─ Audio (WASAPI loopback → Opus) · mic playback                    │
│   └─ Hotkey service (global shortcuts → commands)                     │
└───────────────────────────────┬───────────────────────────────────────┘
                                │ LAN: TCP (control) + UDP (video × N, audio)
┌───────────────────────────────▼──────── Quest 3 ──────────────────────┐
│  Native .aar (Kotlin + C++/NDK)                                       │
│   ├─ Receiver × N (depacketize, minimal jitter buffer)                │
│   └─ Decoder × N  (MediaCodec ► compositor layer Surface)             │
│                         ▲ zero-copy                                   │
│  Unity 6.3 LTS + Meta XR SDK                                          │
│   ├─ Domain (pure C#): Workspace, Screen, Layout, Commands, Spatial   │
│   ├─ Application: WorkspaceStore, CommandBus, connection, cursor…     │
│   ├─ Presentation: screen views (OVROverlay), cursor, gizmo, menus    │
│   ├─ Infrastructure: native video bridge, control client, storage     │
│   └─ Composition: VContainer lifetime scopes (DI)                     │
└───────────────────────────────────────────────────────────────────────┘
```

### Technology stack

| Layer | Technology |
|---|---|
| Headset app | Unity 6.3 LTS (6000.3.19f1), URP, OpenXR + Meta XR Core SDK, Meta Interaction SDK, IL2CPP, ARM64, VContainer |
| Headset video | Android library (`.aar`) in Kotlin + C++ (NDK r27c): UDP receive, `MediaCodec` hardware decode |
| Screen rendering | Compositor layers (`OVROverlay` quad/cylinder) with external surfaces, as Underlay layers ([ADR 0010](docs/adr/0010-screens-as-underlay-layers.md)) |
| UI text | TextMeshPro (SDF), eye buffer at render scale 1.0 ([ADR 0013](docs/adr/0013-ui-text-textmeshpro-render-scale.md)) |
| PC host | C# / .NET 10 (`net10.0-windows10.0.22621.0`) |
| Capture | Windows Graphics Capture |
| Virtual monitors | SudoVDA (IddCx), each monitor created with an exact resolution, never mode-switched live |
| Encode | Hardware H.264 via Media Foundation (NVENC / AMF / QuickSync), low latency, no B-frames |
| Audio | WASAPI loopback → Opus → UDP; Quest mic → PC |
| Protocol | TCP control channel with versioned **FlatBuffers** messages; UDP per video stream; mDNS discovery; code-based pairing ([ADR 0006](docs/adr/0006-control-protocol-pairing-mdns.md)) |
| Input (PC) | `WH_MOUSE_LL` hook + `SetCursorPos` (spatial cursor), `RegisterHotKey` (global shortcuts), `SetDisplayConfig` (arrangement) |

### Design patterns
- **Clean / Hexagonal architecture** (ports & adapters). Assembly definitions are one-directional:
  `Presentation / Infrastructure → Application → Domain`. The Domain never references UnityEngine, Windows or Android APIs.
- **Unidirectional data flow + Command:** the workspace is immutable; every state change is a Command dispatched
  through the `CommandBus` into the `WorkspaceStore`, which provides undo/redo ([ADR 0004](docs/adr/0004-immutable-workspace-and-shared-nunit-tests.md)).
  Views observe state and never mutate it.
- **Declarative screen sync** between headset and host via screen leases ([ADR 0009](docs/adr/0009-screen-leases-declarative-sync.md)).
- **State machine** for the connection lifecycle: discovery → pairing → streaming ⇄ unstable → reconnect.
- **Strategy + Factory** for pluggable backends (capture, encoder, display driver).
- **Pipes & filters / producer-consumer** for the video pipeline, with pooled buffers.
- **ADRs** for every structural decision ([docs/adr/](docs/adr/)).

---

## Repository layout

The Unity project lives at the **repository root** (not under `headset-unity/` as the brief originally said — see
[ADR 0000](docs/adr/0000-unity-project-at-repo-root.md)).

```
/                         Unity 6.3 LTS project (Assets/, Packages/, ProjectSettings/)
├── Assets/_Project/
│   ├── Domain/           pure C# rules: Commands, Layout, Screens, Spatial
│   ├── Application/      use cases: Audio, Connection, Cursor, Manipulation, Menu, Screens, Session
│   ├── Presentation/     views: screens, cursor, manipulation gizmo, menus, connection status
│   ├── Infrastructure/   Unity and shared adapters (native bridge, control client, persistence)
│   ├── Composition/      VContainer lifetime scopes
│   ├── Editor/           editor automation (file-trigger command watcher, build helpers)
│   ├── Scenes/Main.unity the app scene
│   ├── Spikes/Phase0/    Phase 0 video and passthrough spike scenes
│   ├── Tests/            EditMode NUnit tests (shared with tests-dotnet)
│   └── ThirdParty/       FlatBuffers runtime
├── host/                 PC companion (.NET 10) — Host.sln
│   ├── Host.Domain/      pure rules (SpatialMap, layout math) — no Windows APIs
│   ├── Host.Capture/     Windows Graphics Capture
│   ├── Host.Encode/      hardware H.264 encoding
│   ├── Host.Transport/   TCP/UDP, control protocol, hotkey map, mDNS
│   ├── Host.Shared/      shared primitives
│   ├── Host.App/         composition root + CLI (ScreenPortalHost), audio, input, displays
│   └── Host.Tests/       xUnit tests
├── headset-native/       Android library (.aar): Kotlin + C++ NDK (Gradle project)
├── protocol/             control.fbs + wire-format specs (video, audio, mouse state, control v2)
├── tests-dotnet/         UnityPureCode.Tests — runs Domain/Application tests with plain `dotnet test`
├── tools/                build, deploy and measurement scripts (PowerShell) + pinned flatc
└── docs/                 brief, status, ADRs, specs, plans, measurements, design system
```

---

## Requirements

**To use it (MVP target):**
- Meta Quest 3 / 3S on Horizon OS v74+.
- Windows 10/11 PC with a hardware H.264 encoder (NVIDIA NVENC, AMD AMF or Intel QuickSync).
- Wi-Fi 6/6E with the headset close to the router and the PC on **wired Ethernet**.

**To develop:**

| Tool | Version / notes |
|---|---|
| Unity Editor | **6000.3.19f1** (Unity 6.3 LTS) with the Android Build Support module (SDK, NDK r27c, OpenJDK) |
| Unity packages | Meta XR Core / Interaction OVR / MR Utility Kit **207.0.0** (always the same exact version), `com.unity.ai.assistant`, `com.unity.pipeline`, VContainer 1.19.0 |
| .NET SDK | 10 |
| Virtual display driver | SudoVDA installed on the PC ([ADR 0007](docs/adr/0007-virtual-display-driver.md)) |
| Audio drivers | Steam Streaming Speakers/Microphone (temporary, until Phase 5 — [ADR 0015](docs/adr/0015-audio-steam-streaming-drivers.md)) |
| Optional | `unity` CLI, Unity MCP, `ffmpeg`/`ffprobe` (stream verification), Meta XR Simulator |

> The XR Interaction Toolkit and Unity's VR/MR templates are intentionally **not** used — all interaction comes from
> the Meta XR SDK.

---

## Getting started

All commands run from the repository root in PowerShell. Scripts document their parameters in their header.

### 1. Host (PC)

```powershell
dotnet build host/Host.sln
dotnet run --project host/Host.App -- serve      # control on tcp/9200 + mDNS + pairing + video
```

`serve` is the default mode. Other verbs exist for diagnostics and spikes: `stream`, `receive`, `display-spike`,
`audio-test`, `cursor-probe`. Without a headset, `tools/host-loopback-test.ps1` captures the primary monitor,
encodes it, sends it to a local receiver and validates the stream with `ffprobe`.

### 2. Native library (headset video path)

```powershell
tools/build-native.ps1          # builds the .aar with Unity's bundled JDK/SDK/NDK and copies it to Assets/Plugins/Android/
```

### 3. Unity app

Open the project root in Unity 6000.3.19f1 (packages resolve from the Meta scoped registry). With the Editor open,
the automation script drives it through a file trigger:

```powershell
tools/unity-cmd.ps1 -Command resolve-packages
tools/unity-cmd.ps1 -Command build-main-scene
tools/unity-cmd.ps1 -Command build-apk
```

Available commands: `refresh`, `resolve-packages`, `test-editmode`, `build-main-scene`, `boot-main`, `build-scenes`,
`boot-video`, `boot-passthrough`, `build-apk`. If the `ScreenPortal.Editor` assembly fails to compile, the trigger
keeps running the old version — focus the Unity window. With the Editor closed, use batch mode
(`Unity.exe -batchmode -quit -projectPath . -runTests` / `-executeMethod`).

### 4. Deploy to the Quest

Wi-Fi adb is the default (the headset can stay on its charger):

```powershell
tools/quest.ps1                                   # shows which connection will be used + battery
tools/quest.ps1 -Install Builds/Phase0.apk -Launch
tools/quest.ps1 -EnableWifi                       # once, over USB, after the Quest reboots
```

The helper uses USB when the cable is connected, otherwise Wi-Fi (`SP_QUEST_IP` overrides the default IP). A sleeping
headset does not answer adb — wake it up first.

### 5. Connect

Run the host, open the app on the Quest. The headset discovers the PC via mDNS, asks for a pairing code the first
time, and reconnects automatically afterwards.

### Where to test

Anything conclusive in the **Meta XR Simulator** (scene logic, UI, composition, interaction, flows that don't depend
on hardware) is tested there. Only what is conclusive solely on the device goes to the **headset**: decoder and real
latency, compositor-layer sharpness, passthrough, Quest Wi-Fi, mDNS/NSD, Android permissions, performance/thermals.

---

## Testing

| Suite | Command | Notes |
|---|---|---|
| Unity Domain + Application (fast, no Editor) | `dotnet test tests-dotnet/UnityPureCode.Tests` | Same NUnit sources as the Unity Test Runner, runs in seconds |
| Unity EditMode (Editor open) | `tools/unity-cmd.ps1 -Command test-editmode` | Full Unity Test Runner |
| Host | `dotnet test host/Host.Tests` | xUnit — SpatialMap, protocol, hotkeys, pipelines |
| Host video, no headset | `tools/host-loopback-test.ps1` | Capture → encode → UDP loopback → `ffprobe` |
| End-to-end video on the Quest | `tools/phase0-streams-test.ps1 -Streams 4` | N simultaneous streams; supports mixed rates (`-Mix "60x1,15x3"`) |

Every domain change ships with tests. Every cursor bug becomes a test case before it is fixed. Verification reports
for each sub-phase live in [docs/measurements/](docs/measurements/).

---

## Tooling scripts

| Script | Purpose |
|---|---|
| [tools/unity-cmd.ps1](tools/unity-cmd.ps1) | Drives the open Unity Editor (refresh, tests, scene build, APK build) without MCP |
| [tools/build-native.ps1](tools/build-native.ps1) | Builds `headset-native` into the `.aar` |
| [tools/gen-protocol.ps1](tools/gen-protocol.ps1) | Regenerates the C# for `protocol/control.fbs` with the pinned `tools/flatc` (output is committed) |
| [tools/quest.ps1](tools/quest.ps1) | adb connection helper (USB or Wi-Fi), install and launch |
| [tools/host-loopback-test.ps1](tools/host-loopback-test.ps1) | Host video test on the PC alone |
| [tools/phase0-streams-test.ps1](tools/phase0-streams-test.ps1) | End-to-end N-stream test on the Quest |
| [tools/phase0-collect.ps1](tools/phase0-collect.ps1) | Collects stats from logcat into CSV under `docs/measurements/` |
| [tools/install-vdd.ps1](tools/install-vdd.ps1) | Legacy installer for the original Virtual Display Driver (replaced by SudoVDA) |

---

## Keyboard shortcuts

Global shortcuts registered by the host (defaults, from `Host.Transport/Control/HotkeyMap.cs`). They act on the
screen under the cursor.

| Action | Shortcut |
|---|---|
| Move screen | `Ctrl+Alt+←/→/↑/↓` |
| Bigger / smaller (zoom, resolution unchanged) | `Ctrl+Alt+Shift+↑` / `Ctrl+Alt+Shift+↓` |
| Closer / farther | `Ctrl+Alt+PageUp` / `Ctrl+Alt+PageDown` |
| Toggle curvature · less / more | `Ctrl+Alt+C` · `Ctrl+Alt+[` / `Ctrl+Alt+]` |
| Cycle frame-rate mode | `Ctrl+Alt+F` |
| Undo / redo | `Ctrl+Alt+Z` / `Ctrl+Alt+Y` |
| Recenter | `Ctrl+Alt+R` |
| Toggle layout surface (Cylinder ↔ Sphere) | `Ctrl+Alt+L` |
| Align to posture | `Ctrl+Alt+T` |
| Toggle grid snapping | `Ctrl+Alt+G` |
| Toggle edit mode | `Ctrl+Alt+E` |
| Find lost cursor | `Ctrl+Alt+Home` |
| Open the app menu | `Ctrl+Alt+M` |
| Detach / reattach window (mirror, currently disabled) | `Ctrl+Alt+D` / `Ctrl+Alt+Shift+D` |

Holding `Shift` while dragging disables any snapping.

---

## Performance budget

The technical Definition of Done ([brief §11](docs/brief.md)):

| Metric | Target |
|---|---|
| App frame rate | Stable 72 Hz ([ADR 0002](docs/adr/0002-app-refresh-rate-72hz.md)) |
| GC allocation per frame | **0 bytes** in steady state |
| Glass-to-glass latency (Wi-Fi 6) | **< 50 ms** target, < 80 ms acceptable — measured ~33 ms for one screen in Phase 0 |
| Focused screen | 1440p @ 60 fps |
| Unfocused screens (Auto) | 10–15 fps, reduced bitrate |
| Text | 12 pt legible at ~1 m virtual distance |
| Local cursor | < 20 ms perceived delay, no jumps between screens |
| Mouse hook | Microsecond processing per event; never blocks the hook thread |
| A/V sync | < 45 ms drift |

Measured so far: 5 screens at 60 fps including 4K, 4 × 60 fps decode with no drops
([ADR 0008](docs/adr/0008-decode-capacity.md)), ~100 → ~70 Mbps with 5 screens thanks to the frame-rate policy.

---

## Engineering rules

The full list is in [CLAUDE.md](CLAUDE.md) and [brief §13](docs/brief.md). The essentials:

- **Domain never references UnityEngine** (nor Windows/Android APIs); dependencies flow inward only.
- **Every workspace state change is a Command** via the `CommandBus`. Views never mutate state.
- **Zero allocation** in `Update`/`LateUpdate` and in the video path: no LINQ, closures, string concatenation or
  `new` in loops — use pools, `Span`, `NativeArray`.
- **Video frames never cross C#** on the headset.
- No singletons, no `FindObjectOfType`; dependency injection through VContainer.
- Network messages only through schemas in [protocol/](protocol/); no ad-hoc JSON on the hot path.
- `SpatialMap` is pure code in `Host.Domain`; the mouse-hook callback never does heavy work.
- **`ResizeScreen` never changes resolution**; the host never switches the mode of a live virtual monitor — only
  `RecreateScreen` does.
- **Mouse-first:** every action must be possible with mouse + keyboard alone.
- **Freedom principle:** snaps, grids and layout rules help, never trap; every magnet has a threshold and turns off
  with `Shift`.
- UI follows the [design system](docs/DesignSystem/): values only from `tokens.json` via `DesignTokens`, sizes in
  degrees of visual angle, interface text in Brazilian Portuguese.
- **Scope is closed** for the MVP: changes require a brief update plus an ADR; new ideas go to the backlog.
- Every structural decision gets an ADR in [docs/adr/](docs/adr/). Small commits, one purpose each.

---

## Roadmap

| Phase | Content | State |
|---|---|---|
| **0** — Feasibility spike | WGC → HW H.264 → UDP → `.aar` → MediaCodec → OVROverlay; latency and decoder capacity | ✅ ~33 ms, 60 fps, 12 pt legible |
| **1** — Clean skeleton | Asmdefs, immutable Workspace, CommandBus, undo/redo, VContainer; FlatBuffers protocol, pairing, mDNS, connection state machine | ✅ |
| **2** — Free multi-monitor | Virtual display driver, layout surface + manipulation, N screens, recreate, curved screens, per-screen fps + budget, audio | ✅ (2A–2G) |
| **3** — Mouse and keyboard first | Windows arrangement follows the Quest, hotkeys, headset-drawn cursor, 3D world cursor, edit mode with gizmo, menu by mouse | ✅ (3A–3C; 3D window mirror built and disabled) |
| **UI/UX** | Apply the design system to the app and host | ▶ next |
| **3b** — Layout profiles | Named profiles anchored to the desk, quick switching | planned |
| **4** — Passthrough zone | Drawing, editing, anchors, robustness | planned |
| **5** — Polish to the quality bar | Focus policy + motion detection, adaptive bitrate, stability, onboarding and installer, own audio driver | planned |

The MVP ships only when every acceptance criterion in [brief §4.2](docs/brief.md) passes.

---

## Documentation index

| Document | Contents |
|---|---|
| [docs/brief.md](docs/brief.md) | **Product source of truth** (Portuguese): vision, scope, architecture, patterns, risks, roadmap |
| [docs/STATUS.md](docs/STATUS.md) | Current phase, results and carried-over items |
| [docs/adr/](docs/adr/) | Architecture Decision Records 0000–0018 |
| [docs/specs/](docs/specs/) · [docs/plans/](docs/plans/) | Design spec and implementation plan for each sub-phase |
| [docs/measurements/](docs/measurements/) | Verification reports and measurements (latency, decode, bandwidth) |
| [docs/DesignSystem/](docs/DesignSystem/) | Tokens, components, previews and implementation guide for the UI |
| [docs/phase0-runbook.md](docs/phase0-runbook.md) | Phase 0 runbook |
| [protocol/](protocol/) | `control.fbs` and wire-format specs: video packet, audio, mouse state, control v2 |
| [CLAUDE.md](CLAUDE.md) | Project rules for AI-assisted development |

Most internal docs are written in Brazilian Portuguese.

---

## License

No license has been chosen yet. All rights reserved by the author until one is added.
