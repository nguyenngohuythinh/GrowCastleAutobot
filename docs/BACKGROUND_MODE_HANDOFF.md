# Background mode handoff

Updated: 2026-11-23 (Asia/Bangkok)

## Objective and current state

Keep GrowCastleAutobot farming on LDPlayer 9.5.45.0 (Android 1600×900, DPI 240) while another app covers it, without physical mouse/keyboard input or foreground activation. Minimized LDPlayer need not work; failures should pause with useful logs.

- Current branch: `feature/true-background-mode`, tracking `origin/feature/true-background-mode`.
- Last committed implementation: `08691da` (`Implement safe true background automation`); base `origin/main` at audit: `60c97ae`.
- Working tree at audit start: clean. After TASK 02: only the two new `docs/BACKGROUND_MODE_*.md` files are intended source changes; no application code changed. Build and CLI state are under ignored `artifacts/`.
- Current task: TASK 07 — documentation and PR, DONE. No next task is selected.

## Completed and pending

The earlier implementation added instance-pinned capture and input transports, frame normalization, bounded capture retries, pause on invalid/unknown state, UI error display, documentation, and 41 regression assertions. PR: https://github.com/nguyenngohuythinh/GrowCastleAutobot/pull/1 . It is not merged.

TASK 01 confirmed the committed branch contents and reran baseline checks. The audit did not find a proven new code defect. Full covered farming, feature regression and at least two complete replay cycles remain unverified. The previous live capture diagnostic covered 20 changing frames and one battle entry, then stopped safely when LDPlayer became minimized.

TASK 02 confirmed SDK 8.0.319 and built an isolated Release copy at `D:\GrowCastleAutobot\artifacts\task02-build\gca.exe`. It passed with 0 errors and 7 warnings; regression passed 41 assertions. Do not call the live acceptance checks passed on this basis.

TASK 03 ran covered LDPlayer-1 capture outside the sandbox after sandboxed Win32 lookup returned `root=0`. It captured 20/20 frames (19 changing comparisons; 31.9 ms average, 60.8 ms maximum) without observing foreground activation. The saved `artifacts/run/artifacts/live-0.png` visibly contains the Grow Castle menu; a new read-only `--recognize-frame` diagnostic confirmed `blank=False`, `sky=True`, `menu=True` through the bot's recognition methods. Ordinary outer PrintWindow was black at 1025×589; full-content outer and inner render showed the game. No production capture code changed. Other renderer/GPU and game states remain unverified.

TASK 04 added `--live-input` to the regression program. The first run stopped before input because LDPlayer was foreground. After the user covered it, production `PerformReplay` changed the real game from menu to battle while the foreground HWND and cursor coordinates remained unchanged. The isolated Release build passed with 0 errors and 7 warnings; the full 41-assertion suite passed. Right click, wheel, keys, Home/Back, foreground mode end-to-end, and multi-instance routing were not all observed in the game; the existing receiver and source routing tests cover their transport behavior.

TASK 05 audited error paths and strengthened the injected capture-failure worker test. It confirmed exactly two retries, one categorized `Capture failed` pause event, and prompt Stop; regression now passes 43 assertions. Isolated Release build passed with 0 errors and 7 warnings. No production recovery code changed. The earlier live minimize incident remains evidence for `Window invalid`; TASK 05 did not introduce another live failure. Hung PrintWindow driver calls and renderer-specific behavior remain limitations.

TASK 06 produced `docs/BACKGROUND_MODE_QA.md`. Automated build/regression passed (0 errors, 7 warnings; 43/43 assertions). Two production Replay cycles completed with VS Code covering LDPlayer (42.9 s and 85.9 s), without observed foreground activation. Chrome-covered capture passed 20/20 frames with 19 changes, mean 32.7 ms, max 54.7 ms. The matrix explicitly leaves untested visible/foreground/optional-feature and mixed-DPI scenarios NOT VERIFIED or PARTIAL. No production source change or commit in TASK 06.

TASK 07 updated usage docs and the existing PR #1 with the observed results. Final isolated Release build passed with 0 errors and 7 warnings; regression passed 43/43 assertions. Remaining limits are in `docs/BACKGROUND_MODE_QA.md`. Commit hashes appear below after recording the task deliverables.

## Build and tests

Use the workspace SDK if `dotnet` is unavailable:

```powershell
$env:DOTNET_CLI_HOME = 'D:\GrowCastleAutobot\.dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
.\.dotnet\dotnet.exe build GrowCastleAutobot\gca\gca.csproj --no-restore -c Release
.\.dotnet\dotnet.exe run --project GrowCastleAutobot\gca.Tests --no-restore
```

TASK 01 result: build passed (0 errors, 7 warnings); regression passed (41 assertions). TASK 02 result: isolated build passed (0 errors, 7 warnings); regression passed (41 assertions). TASK 03 result: isolated build passed (0 errors, 7 warnings); regression passed (41 assertions), covered live capture and menu recognition passed. TASK 04 result: isolated build passed (0 errors, 7 warnings), regression passed (41 assertions), covered Replay input passed. TASK 05 result: isolated build passed (0 errors, 7 warnings), regression passed (43 assertions). TASK 06 result: isolated build passed (0 errors, 7 warnings), regression passed (43 assertions), two covered Replay cycles and Chrome capture passed.

For TASK 02's isolated build, set `DOTNET_CLI_HOME` under ignored `artifacts/` and build with `-o D:\GrowCastleAutobot\artifacts\task02-build`. Launch from that directory so relative `clickerSettings.json` remains separate:

```powershell
Push-Location 'D:\GrowCastleAutobot\artifacts\task02-build'
.\gca.exe
Pop-Location
```

The executable has ProductVersion `Version: 23.11.2026 02:35`; its `gca.dll` SHA-256 is `8DC26277B1B13A4A879508E0FA3157B73261D9EBCDBE313F7AC53B574B3B0825`. Keep the neighboring DLLs and runtime files with the executable. No installed/stable copy was overwritten.

## Resume instructions

1. Read this file, `docs/BACKGROUND_MODE_TASKS.md`, and `GrowCastleAutobot/BACKGROUND_MODE.md`.
2. Check `git status --short --branch` before editing. Preserve the two TASK 01 documentation files and any later user changes.
3. Perform only the TASK ID named by the user. TASK 01–07 are done within their documented limits. Read `docs/BACKGROUND_MODE_QA.md` before claiming any remaining manual scenario passed.
4. Update both tracking files for that task, including changed files, build/test outcomes, commit and remaining limits. Do not merge into main.
