# Background mode

Enable **Background mode** and **Capture inner window** in Settings. Both options
are visible at the minimum GCA width and persist in `clickerSettings.json`.
Select the exact LDPlayer instance title (for example `LDPlayer-1`). Keep Android
at **1600 × 900**, DPI 240. Leave the emulator open; another app may cover it.
Minimizing is deliberately unsupported: the bot pauses without restoring it.

## Implementation and verified causes

- The old `SendKey` explicitly called `SetForegroundWindow`. The unknown-hint
  handler did too, and error reporting activated GCA. Background paths now avoid
  all of these, including global mouse/keyboard helpers and CAPTCHA test movement.
- Outer `PrintWindow(..., 0)` returned a black game area on the available LDPlayer.
  Inner render capture returned the game. The capture backend tries full-content
  outer capture if requested, then falls back to the inner client surface when
  the result is blank. There is no fallback to desktop capture or global input.
- Inner capture previously applied offsets only to individual pixel reads and
  CAPTCHA crops; unsafe region scanners read unshifted coordinates. Now all
  recognition uses one 1520 × 865, 32-bpp image, with the render viewport at
  `(3,34,1475,830)`. Input maps this same viewport to the current render client.
- Capture and input temporarily use physical-pixel DPI awareness. The diagnostic
  console reproduced virtualized bounds of 1216 × 692 at 125% scaling versus the
  actual 1520 × 865 window. This was a diagnostic observation, not proof that DPI
  caused every failure in the original WPF application.
- The old size-repair path restored/resized windows and the freeze path reset the
  emulator. Background mode validates the instance and pauses instead. Scheduled
  emulator cleanup is skipped; explicit/scheduled restarts pause for inspection.

`IGameCapture` / `WindowCapture` and `IGameInput` / `BackgroundInput` are small
transport boundaries; farm decisions and coordinates remain in the existing bot.
HWNDs are pinned to the selected process. LDPlayer console actions use the index
resolved from `list2`, matched by window title and HWND, never a hardcoded port or
serial. Home/Back retain LDPlayer's instance keyboard action. Win32 keys go to the
render window without activation; whether a given LDPlayer key binding responds
is renderer/version dependent and needs the acceptance checks below.

## Failure behavior

- Capture gets at most two retries, then pauses. `PrintWindow` has a 2-second
  caller deadline and at most one outstanding task per capture session. A hung
  driver call cannot be forcibly canceled; its late bitmap is disposed and that
  session refuses more captures. Stop returns promptly; restart GCA if the driver
  never returns.
- Mouse/key messages have a 500-ms deadline and are not automatically replayed
  after ambiguous delivery. Down/up messages stay paired across pause requests.
- Blank/invalid frames never reach recognition. Fifteen seconds of unchanged
  sampled pixels **after input** reports `Stale frame`; this is a heuristic, not
  proof of GPU failure. A static menu without input is allowed.
- Unknown state after the existing wait pauses before the old ten-ESC/restart
  path. Emulator reset/restart in Background mode also pauses instead of looping.
- GCA displays the error inline without activating either window. Correct the
  problem and resume; recognition restarts with a fresh capture. Stop still works.

The logs distinguish `Capture failed`, `Stale frame`, `Input failed`,
`Window invalid`, and `Game state unknown`.

For a scenario-by-scenario result table from LDPlayer 9.5.45.0, see
[the QA report](../docs/BACKGROUND_MODE_QA.md). PASS entries describe only the
specific behavior observed there; other farm features still need manual checks.

## Build and deterministic checks

From the repository root, using the pinned .NET SDK 8.0.319:

```powershell
dotnet build GrowCastleAutobot/gca/gca.csproj -c Release
dotnet run --project GrowCastleAutobot/gca.Tests
```

The console regression suite needs no external test framework. It checks viewport
mapping (including scaling and negative screen coordinates), exact color/offset
normalization, blank/undersized frames, stale-frame timing, JSON compatibility,
invalid windows, global input guards, and the real worker's fault-pause/Stop path.
Test artifacts/settings are isolated under `artifacts/run`.

Optional live diagnostics (these require an interactive Windows session):

```powershell
dotnet run --project GrowCastleAutobot/gca.Tests -- --capture LDPlayer-1
dotnet run --project GrowCastleAutobot/gca.Tests -- --live-capture LDPlayer-1
dotnet run --project GrowCastleAutobot/gca.Tests -- --recognize-frame C:\absolute\path\to\captured-menu.png
dotnet run --project GrowCastleAutobot/gca.Tests -- --live-input LDPlayer-1
dotnet run --project GrowCastleAutobot/gca.Tests -- --live-replay LDPlayer-1
```

The last command actually plays two replays using the production `PerformReplay`
method. It requires the game menu/battle, stops on unknown state or CAPTCHA, and
does not change your saved bot settings. It is a transport/replay integration
check, not a full validation of every farm feature or the complete WPF worker UI.

On the available LDPlayer 9.5.45 environment, covered capture succeeded under
VS Code (20/20 frames, 19 changing comparisons, 31.9 ms average) and Chrome
(20/20 frames, 19 changes, 32.7 ms average). A captured menu frame passed the
bot's `CheckSky` and `CheckGCMenu` checks. A production `PerformReplay` call
changed the game from menu to battle with unchanged foreground and cursor.
Subsequently, two full `PerformReplay` cycles completed under VS Code cover in
85.9 seconds without observed LDPlayer activation. The earlier live run had
stopped safely when LDPlayer was minimized. These results verify the tested
capture/replay paths, not every farm feature or a long unattended WPF session.

## Manual acceptance on LDPlayer 9.5.45.0

1. Start Grow Castle on the selected 1600 × 900 instance. Enable Background mode
   and Capture inner window. Enable Auto Replay, then Start. Observe at least
   three completed replay cycles while visible.
2. Maximize Chrome over the entire emulator for at least ten waves. Record wave
   completions and capture errors. Repeat with VS Code; type and move the mouse
   during battles and menu transitions. Neither LDPlayer nor GCA should activate.
3. Repeat with Capture inner window disabled. A blank outer frame should cause
   one logged switch to inner capture, not ESC or restart.
4. Move/resize the emulator and test a second monitor at different DPI. Check
   replay, hero skills and a region-based detection such as a reward button.
   Scaling can change exact pixel colors; a failed recognition should pause.
5. Minimize the emulator mid-battle. Expect `Window invalid` and a paused bot;
   no automatic restore. Restore manually, resume, then Stop.
6. Close the selected emulator or make the renderer return black frames. Expect
   bounded recovery and an inline error; no ESC loop, global input or reboot.
7. Pause/Resume and Stop using both buttons and configured hotkeys, including
   during a capture/input error. Stop must release a paused worker immediately.
8. Save/load JSON; verify both checkboxes. Check minimum GCA width (390).
9. With your existing builds/settings, smoke-test Auto Battle, Dragon/Dungeon,
   upgrades, skills, ads and CAPTCHA. Preserve CAPTCHA behavior; handle any
   challenge manually during diagnostic tests. Then smoke-test Foreground mode.
10. Test two simultaneously running instances with distinct titles: only the
    selected instance should respond, including Home/Back.

PrintWindow depends on LDPlayer's renderer. A successful Win32 message delivery
does not prove the game acted on it. If a different GPU/renderer fails these
checks, collect logs and captured PNGs before selecting an additional backend.
ADB was assessed but not added: the local LDPlayer ADB device was unavailable,
while inner Win32 capture worked. No Android resolution/security settings changed.

API references: [Microsoft PrintWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-printwindow),
[LDPlayer instance console](https://www.ldplayer.net/support/introduction-to-ldplayer-command-line-interface.html),
[Android screenshot commands](https://developer.android.com/tools/adb#screencap).
