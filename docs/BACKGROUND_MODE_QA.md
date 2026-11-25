# Background mode QA report

Date: 2026-11-23 (Asia/Bangkok). Branch: `feature/true-background-mode`, implementation based on `08691da` plus TASK 03–05 diagnostic tests. LDPlayer-1 on Windows 10 / LDPlayer 9.5.45.0, Android 1600×900 DPI 240. This matrix distinguishes observed results from checks still requiring manual QA.

| Scenario | Result | Evidence and limit |
| --- | --- | --- |
| Release build | PASS | TASK 06 isolated build to `artifacts/task06-build`: 0 errors, 7 existing unused-field warnings. |
| Automated regression | PASS | 43/43 assertions, including coordinate normalization, blank/stale/invalid frame, bounded capture retry, pause/Stop and Win32 input receiver. |
| LDPlayer visible capture/farm | NOT VERIFIED | TASK 06 did not run a visible baseline farm session. |
| VS Code completely covers LDPlayer | PASS (Replay scope) | User confirmed VS Code covered it. `--live-replay LDPlayer-1` completed two production `PerformReplay` cycles at 42.9 s and 85.9 s. The diagnostic never observed LDPlayer as foreground. |
| Chrome completely covers LDPlayer | PASS (capture scope) | User confirmed Chrome covered it. `--live-capture LDPlayer-1` captured 20/20 frames, 19 changing comparisons, mean 32.7 ms, max 54.7 ms; no LDPlayer activation. Farm actions under Chrome were not tested. |
| User typing/moving mouse during farm | PARTIAL | User was invited to continue using the desktop; Replay cursor samples were unchanged within each call, and differed between calls (`1741,721` then `1652,464`). The diagnostic did not monitor keystrokes or continuous pointer activity. |
| At least two complete Replay cycles under cover | PASS | Two completed cycles observed in one run while VS Code covered LDPlayer. This proves Replay integration, not all bot features or unattended long runs. |
| LDPlayer minimized | PASS (earlier live safety observation) | Earlier live run reported `Window invalid` and stopped without restoring LDPlayer. Not repeated in TASK 06. |
| Capture failure and bounded retry | PASS (injected) | Fake backend failed exactly 3 attempts, yielded one categorized pause event; real renderer failure was not induced. |
| Pause/Stop after capture fault | PASS (automated) | Worker paused and Stop woke it promptly; Resume and UI hotkeys were not manually tested. |
| Stale frame | PASS (deterministic) | FrameHealth throws after unchanged samples for 15 s following input; live GPU freeze was not induced. |
| Foreground mode regression | NOT VERIFIED | Source path remained separate; no live foreground farm test. |
| Other farm features / two LDPlayer instances | NOT VERIFIED | Auto Battle, Dungeon/Dragon, upgrades, skills, ads, CAPTCHA and selected-instance isolation need targeted manual checks. |
| Moved/resized window / mixed-DPI monitors | PARTIAL | Covered live capture worked at a resized outer 1025×589 / render 985×554 in TASK 03; moving between DPI monitors was not tested. |

## How to repeat on Windows

Keep LDPlayer-1 open (not minimized), set Android to 1600×900 DPI 240, and start Grow Castle. Run from the repository root with `.dotnet/dotnet.exe` and `DOTNET_CLI_HOME` under `artifacts/` if needed. Cover LDPlayer completely with the named application before each diagnostic:

```powershell
.\.dotnet\dotnet.exe run --project GrowCastleAutobot\gca.Tests --no-build -- --live-capture LDPlayer-1
.\.dotnet\dotnet.exe run --project GrowCastleAutobot\gca.Tests --no-build -- --live-replay LDPlayer-1
```

The replay diagnostic sends game input and may stop on CAPTCHA, unknown state, minimize, foreground LDPlayer, or its four-minute deadline. It does not validate the full WPF UI. For remaining manual checks, follow `GrowCastleAutobot/BACKGROUND_MODE.md` under “Manual acceptance”: verify visible baseline and foreground mode, Chrome farming, Pause/Resume and hotkeys, failure UI, multi-instance routing, mixed DPI, and each optional farm feature. Record observed wave counts, foreground changes and logs before changing a matrix cell to PASS.
