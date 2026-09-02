# VL.Rive — runtime update & state notes

Living reference for the rive-runtime submodule migration (now **complete**) and
the durable build/deploy knowledge that came out of it. Last updated
**2026-09-02**.

## TL;DR — runtime update is done

The `submodules/rive-runtime` pin has been migrated off the stale vvvv fork onto
plain upstream `rive-app/rive-runtime` and bumped to its **August 2026 head,
`61f00897`**. All changes are committed and pushed to **`main`**; the build is
green (native ClangCL + managed) and `.riv` files render correctly in vvvv.
There is no open migration work.

| Item | State |
|------|-------|
| Submodule pin | **`61f00897`** (upstream `rive-app/rive-runtime`, 2026-08) |
| `.gitmodules` url | `https://github.com/rive-app/rive-runtime.git` (was the vvvv fork) |
| Native + managed build | Green (ClangCL native, ClangSharp regen clean, managed OK) |
| vvvv runtime render | Confirmed working |

### Pin history
- `121dd6de` (2025-11) — old fork pin, could not resolve against the fork url.
- `9498c4c0` (2026-06-13) — first upstream bump, on branch
  `update-rive-runtime-2026-06`, merged to `main`.
- **`61f00897` (2026-08)** — second bump, straight to upstream head. **Current.**

The rive-runtime monorepo has **no release tags** — it's rolling `main`, so
"latest stable" = latest `main` that builds and renders.

## FrameDescriptor ABI — the thing to watch on every bump

`src/Interop/FrameDescriptor.cs` is a **hand-written** mirror (NOT generated into
`build/generated/`) of `rive::gpu::RenderContext::FrameDescriptor`. It's passed
to native by pointer (`RiveRenderContext.cs` → `BeginFrame`), so field order and
types must match the C++ struct **byte-for-byte**. Every runtime bump must diff
this struct. Two bumps' worth of drift, already reconciled:

- **→ `9498c4c0`:** struct gained three mid-struct fields —
  `DitherMode ditherMode`, `uint32_t virtualTileWidth`,
  `uint32_t virtualTileHeight`.
- **→ `61f00897`:** head inserted `TriangulationThresholds triangulationThresholds`
  mid-struct (after `ditherMode`, before `virtualTileWidth`). That struct holds a
  `size_t maxVerbs` → mirrored in C# as `nuint MaxVerbs`, which forces 8-byte
  alignment on the whole FrameDescriptor. **Defaults MUST be non-zero**
  (MinArea = 512*512, MaxVerbs = 256, FrameBudgetMs = 2) or triangulation is
  silently disabled.

Symptom of a misaligned FrameDescriptor: garbled rendering / wrong clear color /
wrong dither — **not** a crash. Other API deltas across these bumps were
source-compatible (e.g. `File::import` gained a trailing
`ScriptingVM* vm = nullptr`; `File::viewModel(size_t)` gained `const`). Luau
scripting stays gated behind premake `--with_rive_scripting`, which the build
does not pass (`--with_rive_text --with_rive_layout`), so no new link dependency.

## Fork patches — intentionally dropped
The old vvvv fork carried two local patches; both were checked and **not**
reapplied when moving to upstream:
- **`srgb`** (`65fb60be`) — D3D11 sRGB render-target acceptance. Inert on the PLS
  atomics path anyway (color is written through the UAV, which can't be `_SRGB`);
  the sRGB→linear fix lives **downstream in the PixelGrid shaders**, not here.
- **`arm64-hack`** (`24a31509`) — obsolete; `build/premake5.lua` already
  parametrizes arch via `platforms:x64 / platforms:ARM64` filters.

## Build prerequisites — Windows toolchain
Environment requirements, not code:
- **Visual Studio with the C++ workload** (MSVC toolset + Windows SDK w/ D3D11).
- **"C++ Clang tools for Windows"** — **required**. Every Rive C++ project builds
  with the **ClangCL** platform toolset (Rive's PLS renderer needs Clang on
  Windows; MSVC alone won't do). Missing it → `error MSB8020: ClangCL build
  tools ... not found`. Install via VS Installer → Individual components →
  **C++ Clang Compiler for Windows** + **MSBuild support for LLVM (clang-cl)
  toolset**. Verified with Clang 22.1.3.
- **Prerelease-only VS** (e.g. VS 2026 Insiders): needs the `build/Build.cs`
  fixes in commit `13b4aec` (VSWhere `-prerelease` for premake and MSBuild steps
  + a PATH preservation fix).

## Building

Full native + managed (regenerates bindings):
```
build.cmd
```
Chains: GenerateInteropSolution (premake vs2022 `--with_rive_text
--with_rive_layout`) → BuildRiveNative (MSBuild x64) → GenerateInteropCode
(ClangSharp `@generate.rsp`) → BuildRiveManaged. Build.cs auto-downloads
premake5, python, w64devkit.

**Managed-only changes** (anything under `src/*.cs` that doesn't touch the native
shim) don't need the native step:
```
dotnet build src/VL.Rive.csproj -c Release
```
Output: `lib/net8.0/VL.Rive.dll`.

Regen sanity check: after ClangSharp runs, `git diff build/generated/` should be
empty or trivial. A meaningful `Methods.cs` diff means the shim hpp surface
drifted — reconcile before committing.

## Deploy to vvvv (for this machine's FutureOfProcurement project)
vvvv loads the pack from a project-local nugets folder, **not** `%LOCALAPPDATA%`
or `~/.nuget`. Copy the built DLL to:
```
C:\FutureOfProcurement\Gamma\nugets\VL.Rive.0.0.15-pre\lib\net8.0\VL.Rive.dll
```
Close vvvv first (it locks the DLL). Back up the existing one before overwriting.

## Post-update RiveRenderer features (on top of the runtime migration)
- `05f5936` — optional `update` pin (forces a fresh setup-pose artboard) + fail-safe
  artboard/scene name lookup (unknown name logs a warning, keeps last valid content
  instead of throwing/freezing vvvv).
- `6118efb` — linear-animation scrubbing (`externalTimeControl` + `progress`).
- `f9c43bc` — named **Text Runs** input (`Spread<RiveTextRun>`), direct text-run
  control independent of data binding. Requires the `.riv` exported **"with all
  names"** or by-name lookups fail silently.
- `3fed6c4` — view model now binds at the **artboard** level, not the scene, so it
  applies even when a specific timeline is selected via Scene Name (the base
  `Scene::bindViewModelInstance` is empty — scene-level binding was a silent no-op
  for linear animations).
- `55870ac` — under `externalTimeControl`, the artboard advances by the real frame
  delta (not 0), so nested/instanced list artboards run on their own clock and play
  intro animations when instanced, while the parent timeline stays scrubbed.

## Rollback (runtime pin)
```
git revert 3103a46          # the 61f00897 bump commit, or
git -C submodules/rive-runtime checkout 9498c4c0   # step back to the June pin
```
Both `9498c4c0` and `121dd6de` are still valid upstream commits.

## Reference — key commits
- Current pin: `61f00897` (upstream head, 2026-08)
- Prior pin: `9498c4c0` (2026-06-13) · Original fork pin: `121dd6de` (2025-11-18)
- Dropped fork patches: srgb `65fb60be` · arm64-hack `24a31509`
