# rive-runtime submodule update — handoff

Status as of **2026-08-19**. Work done on macOS; the remaining steps need a **Windows** machine.

## TL;DR

The `submodules/rive-runtime` pin was bumped from `121dd6de` (2025-11) to
upstream **`9498c4c0`** (2026-06-13). All source-level changes are committed on
branch **`update-rive-runtime-2026-06`** (commit `825785d`). What's left is to
**build the native shim + regenerate bindings on Windows and confirm it runs.**

## What was done (committed)

| File | Change |
|------|--------|
| `submodules/rive-runtime` | Pointer `121dd6de` → `9498c4c0` (plain `rive-app/rive-runtime` main) |
| `.gitmodules` | `url` repointed `vvvv/rive-runtime` → `rive-app/rive-runtime` |
| `src/Interop/FrameDescriptor.cs` | Added `DitherMode` enum + 3 fields to mirror the native struct |

### Why the URL was repointed
The vvvv fork's `main` is stuck at Jun 2025. Neither the old pin (`121dd6de`,
Nov 2025) nor the new one lives on the fork, so `git submodule update --init`
against the fork URL could not resolve the pin — this was already latently
broken before this update. Upstream `rive-app/rive-runtime` contains both, so
pointing there fixes it.

### Why FrameDescriptor.cs changed
`src/Interop/FrameDescriptor.cs` is a **hand-written** mirror (NOT in
`build/generated/`) of `rive::gpu::RenderContext::FrameDescriptor`. It's passed
to native by pointer (see `RiveRenderContext.cs` → `BeginFrame`), so its field
order and types must match the C++ struct byte-for-byte. Between the two pins
the native struct gained three fields mid-struct:
`DitherMode ditherMode`, `uint32_t virtualTileWidth`, `uint32_t virtualTileHeight`.
Those were added to the C# mirror in the same position.

## Fork patches — intentionally dropped
The vvvv fork carried two local patches; both were checked and **not** reapplied:
- **`srgb` branch** (`65fb60be`, "Adds SRGB texture formats") — D3D11 sRGB
  render-target acceptance in `renderer/src/d3d11/render_context_d3d_impl.cpp`.
  The current pin already dropped it; we pinned plain upstream. It still
  cherry-picks cleanly if you decide you need it:
  `git -C submodules/rive-runtime cherry-pick 65fb60be` (then you'd have to push
  a fork branch and pin to it, since it's not an upstream commit).
- **`arm64-hack` branch** (`24a31509`) — flips every premake
  `architecture('x64')` → `ARM64`. Obsolete: VL.Rive's own `build/premake5.lua`
  already parametrizes arch via `platforms:x64 / platforms:ARM64` filters.

## API-break analysis (why this bump is low-risk)
Diffed every Rive symbol the shim (`src/Interop/RiveSharpInterop.cpp/.hpp`)
uses, pin → target:
- **Only real break:** `FrameDescriptor` (handled above).
- `File::import` gained a trailing `ScriptingVM* vm = nullptr` — source
  compatible; the shim's 3-arg call still binds.
- Luau scripting is gated behind premake `--with_rive_scripting`, which the
  build does **not** pass (it uses `--with_rive_text --with_rive_layout`), so no
  new link dependency and no `build/premake5.lua` change.
- D3D render-context, `RiveRenderer`, `Scene`, `Artboard`, `ViewModel*Runtime`,
  and `File` symbols the shim calls are all unchanged.
- `build/generate.rsp` untouched — ClangSharp only reflects the extern-C shim
  (which didn't change), not Rive internals.

## Next steps — on Windows

1. **Sync the submodule to the committed pin:**
   ```
   git checkout update-rive-runtime-2026-06
   git submodule update --init --recursive
   ```
   (LFS: the repo uses Git LFS — make sure `git lfs pull` has run for the submodule.)

2. **Build native + managed (this also regenerates bindings):**
   ```
   build.cmd
   ```
   The Nuke `Compile` target chains: GenerateInteropSolution (premake vs2022
   `--with_rive_text --with_rive_layout`) → BuildRiveNative (MSBuild x64) →
   GenerateInteropCode (ClangSharp `@generate.rsp`) → BuildRiveManaged.
   Requires: Visual Studio 2017+ (VSWhere finds 17.0), Windows SDK w/ D3D11.
   Build.cs auto-downloads premake5, python3.13, w64devkit.

3. **If the native shim fails to compile**, the most likely culprit is a Rive
   API drift not caught by the macOS header analysis. Check the error against
   `src/Interop/RiveSharpInterop.cpp`. If a specific symbol regressed, try an
   earlier upstream commit (bisect between `121dd6de` and `9498c4c0`) rather
   than going straight to head (`34f6df4`, 2026-08).

4. **Regen sanity check:** after ClangSharp runs, `git diff build/generated/`
   should be empty or trivial. If `Methods.cs` changed meaningfully, the shim
   hpp surface drifted — reconcile before committing.

5. **Runtime check:** load a `.riv` in vvvv and confirm it renders. The
   FrameDescriptor layout is the thing to watch — if fields are misaligned,
   you'll get garbled rendering or wrong clear color / dither, not a crash.

6. If all good, commit any regenerated files, then merge
   `update-rive-runtime-2026-06` → `main` (or open a PR).

## Rollback
```
git checkout main            # abandon the branch, or
git revert 825785d           # if already merged
```
The old pin `121dd6de` is still a valid upstream commit.

## Reference — key commits
- Old pin: `121dd6de` (2025-11-18)
- New pin: `9498c4c0` (2026-06-13) ← current
- Upstream head at time of update: `34f6df4` (2026-08-18)
- Fork srgb patch: `65fb60be` · Fork arm64 hack: `24a31509`
