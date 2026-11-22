# Background mode task tracker

Updated: 2026-11-23 (Asia/Bangkok)

| TASK ID | Status | Scope and evidence |
| --- | --- | --- |
| 01 — Audit current implementation | DONE | Audited branch, commit, working tree, background transport, bot integration, documentation and regression tests. See findings below. |
| 02 — Build & verify | DONE | .NET 8.0.319 verified; isolated Release build and 41 regression assertions passed. Exact executable and launch guidance below. |
| 03 — Background capture | DONE | Covered LDPlayer capture and real game recognition passed on the available renderer; details and limits below. |
| 04 — Background input | DONE | Covered production Replay click changed game state without changing foreground/cursor; other input types retain documented limits. |
| 05 — Error handling & safety | DONE | Bounded capture failure, categorized pause, Stop, stale/invalid/unknown guards verified; limits below. |
| 06 — Regression & manual QA | DONE | Automated checks, two covered Replay cycles, Chrome capture and a candid acceptance matrix recorded in `docs/BACKGROUND_MODE_QA.md`. |
| 07 — Documentation & PR | DONE | Updated usage and QA docs, final build/test passed, and existing PR #1 was updated. Commit details below. |

## TASK 01 findings

- Branch: `feature/true-background-mode`, tracking `origin/feature/true-background-mode`. HEAD and remote tracking ref were `08691da` at audit start; `origin/main` was `60c97ae`. Working tree was clean at audit start.
- Existing implementation is in `GrowCastleAutobot/gca/Classes/Automation/BackgroundTransport.cs` and `GrowCastleAutobot/gca/Autobot/BackgroundMode.cs`, integrated with initialization, input, capture, worker pause and UI. Existing documentation: `GrowCastleAutobot/BACKGROUND_MODE.md`; existing regression suite: `GrowCastleAutobot/gca.Tests/Program.cs`.
- The selected LDPlayer is matched by title and HWND against `ldconsole list2`; render HWND is validated against the root process. Background capture normalizes to 1520×865 and rejects blank frames. Input uses window messages and guards global input methods. The worker pauses after bounded capture retries or unknown state; minimize is rejected.
- Audit build: `.dotnet/dotnet.exe build GrowCastleAutobot/gca/gca.csproj --no-restore -c Release` passed, 0 errors, 7 unused-field/event warnings. Audit regression: `.dotnet/dotnet.exe run --project GrowCastleAutobot/gca.Tests --no-restore` passed, 41 assertions. `DOTNET_CLI_HOME` was set to a workspace-local directory because the default home was sandbox-denied.
- Confirmed repository/test gap: the 41 assertions exercise deterministic logic and a hidden Win32 receiver, not two completed Grow Castle replay cycles. The prior live diagnostic captured 20/20 frames while VS Code covered LDPlayer and entered one battle, but stopped when LDPlayer was minimized. The documented acceptance matrix is still pending; do not call it PASS.
- Known limitation: a PrintWindow call hung in the driver cannot be forcibly canceled. The code bounds the caller wait and refuses overlapping capture until the worker returns; the documentation instructs restarting GCA if it never returns. PrintWindow behavior on another renderer/GPU is unverified.
- No additional code defect was established by this audit. Source and deterministic tests alone cannot prove game-level background input or every farm feature.

Files changed for TASK 01: this tracker and `docs/BACKGROUND_MODE_HANDOFF.md`. No application code changed. Commit: none for this audit. No live LDPlayer test was run during TASK 01.

TASK 01 next step at that time: wait for the user to name TASK 02–07. TASK 02 was subsequently selected and completed below.

## TASK 02 results

- Branch and committed source stayed at `08691da`; the two TASK 01 tracker files were present and uncommitted before this task. No application source was edited.
- Environment: Windows 10.0.19045, SDK 8.0.319, `net8.0-windows`, WindowsDesktop runtime 8.0.22. Used the repository-local `.dotnet/dotnet.exe` and an ignored `artifacts/dotnet-home` for CLI state.
- Build: `.dotnet/dotnet.exe build GrowCastleAutobot/gca/gca.csproj --no-restore -c Release -o D:\GrowCastleAutobot\artifacts\task02-build` passed, 0 errors, 7 unused-field warnings. The build output is isolated and ignored by Git.
- Regression: `.dotnet/dotnet.exe run --project GrowCastleAutobot/gca.Tests --no-restore` passed, 41/41 assertions. These tests do not replace real LDPlayer acceptance checks.
- New executable: `D:\GrowCastleAutobot\artifacts\task02-build\gca.exe` (177,664 bytes; ProductVersion `Version: 23.11.2026 02:35`). Its companion `gca.dll` contains the application code. SHA-256: exe `603A7DEF2E609744DCB5A84DFCB1507F5998F46BA1678C4E9A952419353EBCB9`; dll `8DC26277B1B13A4A879508E0FA3157B73261D9EBCDBE313F7AC53B574B3B0825`.
- Launch this isolated build with its own working directory: `Push-Location 'D:\GrowCastleAutobot\artifacts\task02-build'; .\gca.exe; Pop-Location`. Keep all files in that build directory together and leave the user's installed/stable GCA directory untouched. The executable path and ProductVersion distinguish this build; the DLL hash verifies the code. `clickerSettings.json` is read/written relative to the working directory.
- Limits: this task did not launch the GUI or test LDPlayer, so runtime compatibility and full background behavior remain unverified. No stable GCA files were overwritten.

Files changed for TASK 02: this tracker and `docs/BACKGROUND_MODE_HANDOFF.md`; isolated ignored build/test artifacts. Commit: none. Next step: wait for the user to select another TASK ID.

## TASK 03 results

- Reviewed `WindowCapture`, `GameCoordinates`, DPI handling, `FrameHealth`, and the `CaptureBackground` retry path. No production capture code change was justified by the available evidence.
- Initially the sandboxed live diagnostic could see the `dnplayer` process but resolved `root=0 render=0`, then raised `Window invalid`. With the user leaving LDPlayer-1 open and covered, the same read-only diagnostic outside the sandbox succeeded. This was a test-access limitation, not evidence of a production capture defect.
- On the covered LDPlayer-1, `--live-capture` produced 20/20 valid 1520×865 frames; 19 successive frame hashes changed. Mean capture time 31.9 ms, maximum 60.8 ms. The diagnostic observed no LDPlayer foreground activation. `artifacts/run/artifacts/live-0.png` and `live-19.png` are saved locally (ignored by Git).
- Raw diagnostic at window size 1025×589, render 985×554: ordinary outer `PrintWindow` (flag 0) returned a black game area; outer full-content capture (flag 2) and inner render capture showed the game. This confirms the configured full-content path and inner capture on this renderer, including scaling to the canonical canvas.
- Added read-only `--recognize-frame <absolute PNG path>` to `gca.Tests/Program.cs`. Against the covered live frame, the bot's own `CheckSky(false)` and `CheckGCMenu(false)` returned `True`, with `blank=False`. This proves recognition of this captured menu frame, not all game states.
- Build to isolated `artifacts/task03-build`: passed, 0 errors, 7 warnings. Regression: 41/41 assertions passed. The recognition diagnostic also passed. No live input or replay was sent in TASK 03.
- Limits: different renderer/GPU, monitor DPI transitions, prolonged capture, and recognition of battle/reward/CAPTCHA states remain unverified. A hung PrintWindow worker remains subject to the documented 2-second caller timeout and cannot be killed forcibly.

Files changed for TASK 03: `GrowCastleAutobot/gca.Tests/Program.cs`, this tracker, and `docs/BACKGROUND_MODE_HANDOFF.md`. Commit: none. Next step: wait for the user to select another TASK ID.

## TASK 04 results

- Audited `LeftClickBackground`, `RightClickBackground`, `MoveBackground`, `WheelBackground`, both `SendKey` overloads, global mouse/keyboard APIs, and focus calls. Production background actions route through the instance-pinned `BackgroundInput`; foreground paths use their existing implementations and global helpers reject background mode. LDPlayer Home/Back uses the selected `list2` index through `ldconsole`.
- Added a focused `--live-input <title>` diagnostic in `gca.Tests/Program.cs`. It requires the selected LDPlayer to be open and covered, verifies the real Grow Castle menu, invokes production `PerformReplay`, then checks that the game leaves the menu while the foreground HWND and cursor coordinates remain unchanged.
- First live invocation stopped **before input** because LDPlayer was foreground. After the user covered it, `--live-input LDPlayer-1` passed: `PASS covered replay input: menu changed, foreground and cursor unchanged.` This is evidence that the selected instance processed this Replay click while covered.
- Existing hidden Win32 receiver regression checks left/right down/up, mapped mouse coordinates, wheel signed delta and screen coordinates, key down/up without focus, and cancellation before input. Full regression passed 41/41 assertions. Isolated Release build passed, 0 errors and 7 warnings.
- No production input code change or ADB backend was justified. Foreground mode was not run end to end in TASK 04; source review found its input path unchanged. Right click, wheel, special keys and Home/Back were validated at message/route level, not by observing each Grow Castle action. Multi-instance live isolation remains unverified.

Files changed for TASK 04: `GrowCastleAutobot/gca.Tests/Program.cs`, this tracker, and `docs/BACKGROUND_MODE_HANDOFF.md`. Commit: none. Next step: wait for the user to select another TASK ID.

## TASK 05 results

- Reviewed `WindowTarget.Validate`, `WindowCapture` timeout/blank rejection, `FrameHealth`, `CaptureBackground`, `PauseAutomation`, worker catches, and guards around ESC, reset, restart and LDPlayer reboot. Invalid/minimized windows reject capture before recognition; unknown state raises a categorized error before the old ESC/restart sequence. The background worker logs the category and pauses; Stop cancels waits.
- Strengthened the existing injected-failure worker test: the backend is called exactly three times (initial attempt plus two retries), exactly one `Capture failed:` event contains the pause instruction, then Stop wakes the paused worker. No production recovery code was changed.
- Regression: 43/43 assertions passed. Existing checks cover invalid HWND, blank frame, stale frame after input, static idle frame, global input guards, unknown-state ESC/restart/reset guards, and fresh I/O session after Stop. Isolated Release build to `artifacts/task05-build`: 0 errors, 7 warnings.
- The prior live minimized LDPlayer run reported `Window invalid` and stopped without restoring the window. TASK 05 did not deliberately minimize or fault the running game. PrintWindow driver hangs cannot be forcibly killed; the caller has a 2-second deadline and the session refuses overlapping capture until the worker returns. Stale-frame detection is a heuristic and other GPU/renderer failure modes remain unverified.

Files changed for TASK 05: `GrowCastleAutobot/gca.Tests/Program.cs`, this tracker, and `docs/BACKGROUND_MODE_HANDOFF.md`. Commit: none. Next step: wait for the user to select another TASK ID.

## TASK 06 results

- Isolated Release build to `artifacts/task06-build`: passed with 0 errors, 7 warnings. Regression: 43/43 assertions passed.
- With the user's confirmation that VS Code fully covered LDPlayer-1, `--live-replay` completed two production Replay cycles in 85.9 seconds (first at 42.9 seconds). The diagnostic did not observe LDPlayer foreground; cursor samples stayed constant during each Replay call. No CAPTCHA or unknown state interrupted the run.
- With the user's confirmation that Chrome fully covered LDPlayer-1, `--live-capture` captured 20/20 frames, 19 changes, mean 32.7 ms, max 54.7 ms, with no observed LDPlayer activation.
- `docs/BACKGROUND_MODE_QA.md` contains the scenario-by-scenario acceptance matrix, exact evidence, NOT VERIFIED cases, and manual Windows instructions. Visible baseline farming, Chrome farming beyond capture, foreground mode, all optional features, mixed-DPI monitors, UI hotkeys and multi-instance behavior remain NOT VERIFIED or PARTIAL as indicated there.

Files changed for TASK 06: `docs/BACKGROUND_MODE_QA.md`, this tracker and `docs/BACKGROUND_MODE_HANDOFF.md` (alongside the prior uncommitted test changes). Commit: none. Next step: wait for the user to select another TASK ID.

## TASK 07 results

- Updated `GrowCastleAutobot/BACKGROUND_MODE.md` with the observed two covered Replay cycles, Chrome capture, current diagnostic commands, and a link to the candid QA matrix. Updated `README.md` to link the matrix.
- Final isolated Release build to `artifacts/task07-build`: passed, 0 errors and 7 warnings. Final regression: 43/43 assertions passed. `git diff --check` found no whitespace errors.
- Existing PR: https://github.com/nguyenngohuythinh/GrowCastleAutobot/pull/1 (base `main`, head `feature/true-background-mode`, open and unmerged at TASK 07 review). Updated its validation summary; no duplicate PR or merge.
- Remaining limits: see `docs/BACKGROUND_MODE_QA.md` for NOT VERIFIED/PARTIAL cases. Two covered Replay cycles and Chrome capture are observed, but not all farm features or a long WPF worker session.

Files changed for TASK 07: `GrowCastleAutobot/BACKGROUND_MODE.md`, `README.md`, this tracker and `docs/BACKGROUND_MODE_HANDOFF.md`, plus committing the previously uncommitted TASK 03–06 test and QA files. Commit hash is recorded in the handoff after the commit. No further task is selected.
