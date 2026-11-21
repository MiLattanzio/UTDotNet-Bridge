# Validation record

Date: 2026-11-21. No result is inferred from compilation alone.

## Environment and scope

Fairphone 5 / FP5, AArch64, Ubuntu Touch 24.04-2.2 build 3 over Ubuntu 24.04.5. .NET 10.0.12 self-contained, Avalonia 12.1.6, Tmds.DBus.Protocol 0.94.1. Initial ADB directory deployment to `/home/phablet/utdotnet-phase1`, running as phablet through existing X11/XWayland `DISPLAY=:0`. Subsequent `utdotnet.inputdemo` Click 0.1.0 installed through `com.lomiri.click.Install`, launched with `lomiri-app-launch`, and verified under AppArmor enforcement. No OpenStore publication.

Device interaction below used injected touchscreen events through the already-accessible evdev interface. It exercised the real OSK and editing protocol; it was not a person typing on the physical glass. Evidence comes from redacted logs and captures of the system compositor `/run/mir_socket`. Headless tests and programmatic smoke tests have narrower scope.

## Automated and desktop results

| Check | Result | Scope |
|---|---|---|
| Release build / ARM64 publish | Pass | SDK 10.0.401; untrimmed self-contained directory, about 108 MiB |
| Automated suite | Pass, 50 tests | Platform detection, fallbacks, diagnostics/process cancellation, headless editing/undo, sensitive state, stale callbacks, composition finalization, area handoff, bounded peer initialization and focused-field visibility across rotation |
| Final desktop X11 smoke | Pass | Window, focus/text diagnostics, fallback OSK results and app render; no native desktop IME claim |
| Earlier explicit desktop Wayland smoke | Pass | Phase 1 real-window smoke; not repeated for native Maliit |
| Phone layout | Pass | Headless 360×640 and 900×384 with keyboard, 16-unit font and at least 48-unit fields; physical checks below |

Test results are retained under ignored `artifacts/test-results/`. The input service still reports keyboard visibility Unknown; compositor captures establish visible behavior independently.

## Real-device matrix

| Check | Result and evidence |
|---|---|
| Baseline, `--no-maliit` | Reproduced original failure: tapping Standard text gives focus but no OSK; `fp5-baseline.png` and log |
| Native field activation | Pass: same session/device opens native keyboard and accepts input |
| Composition and commit | Pass: typed `ciao`, observed native preedit callbacks, committed with space |
| Hide / switch while composing | Pass after fix: typed `ciao` without space, hid the keyboard or switched to password, text preserved in Standard text |
| Explicit hide / retap | Pass: keyboard disappears and reopens on already-focused field |
| Password | Pass: native input masked; callback/observer logs redact text, lengths and selection; no suggestion row observed |
| Numeric and backspace | Pass: numeric layout, `123` then backspace yields `12` |
| Email mode | Pass for layout: dedicated @ and domain key observed; full address entry not separately tested |
| Multiline | Pass: two lines entered through native Return |
| Field switching | Pass across standard, numeric, password, email and multiline contexts; old callbacks also rejected by headless test |
| Scrolling and occlusion | Pass in portrait: side-margin swipe scrolls the form and focused lower fields remain reachable above OSK; unchanged geometry is retained on handoff |
| Native dialog | Pass: native keyboard, `ok` committed in dialog, close returns focus/context to Standard text |
| Phone UI scale | Pass: grid 24 → absolute scale 3; 1224 physical pixels → 408 logical width, 48-unit controls, readable font, four tabs on one row |
| Selection replacement / undo | Automated headless pass only; native touch handles/clipboard unverified |
| Search / phone / URL layouts | Implemented hints; device interaction not yet verified |
| Accents, alternate languages, long-press variants | Unverified |
| Physical rotation | Pass: user turned FP5; portrait 408×865 ↔ landscape 900×384, scale 3; text retained. Native landscape keyboard and full focused field visible after layout fix |
| Screen-off / wake with USB | App state and peer reopening pass; deep suspend failed with kernel `a600000.ssusb`, errno -16 (busy), success 0 / fail 2 |
| Deep suspend / resume without USB | Pass: user unplugged, powered screen off for at least 30 seconds, woke/unlocked and reconnected in portrait. Kernel success rose 0 → 17, failures stayed 2; same Click PID 10449. This is one user test interval containing multiple kernel sleep entries, not 17 independent tests |
| Composition and editing after resume | Pass in Click: `ciao` was still preedit before sleep, finalized and preserved after resume/context loss; retap reconnected; backspace, replacement character and space commit worked |
| Click confinement | Pass for installation, launch, scale, X11/Maliit input and lifecycle above; `/proc/10449/attr/current` returned `utdotnet.inputdemo_inputdemo_0.1.0 (enforce)`, no extra policy groups |
| Click review / OpenStore | Review tool blocked by missing static 2404.2 policy metadata (two errors); no other errors or warnings, some checks not applicable. Not published or OpenStore-approved |
| Long sessions | Unverified |
| Native Wayland on FP5 | Blocked before window creation: xdg_wm_base v1 versus required >=3; no text-input manager in observed registry |

The bridge is experimental. Graphics startup still emits gralloc/Zink/GLX warnings; successful basic interaction does not establish acceleration or broad GPU compatibility. No Qt comparison application was required to obtain the successful native path, and no Uno application was tested.

## Regressions found and corrected during device testing

- Desktop initial width was centered to a negative X11 origin, then resized by Lomiri without correcting position. Phone screen bounds now determine initial geometry.
- The installed Ubuntu variable font rendered normal text unexpectedly bold in this stack. The demo uses the available static DejaVu Sans font and explicit compact tab sizing.
- Tmds attempted to reply to Maliit's early initialization callbacks before ConnectAsync completed. A bounded custom stream defers those callbacks until the handler is installed.
- Backspace can move committed words back into preedit. Pending composition is now finalized on hide/focus loss, preventing lost text.
- Maliit can omit an unchanged keyboard-area notification for a new context. The last measured global area is reused and intersected with the active window, including dialogs.
- A 200-unit minimum content height left the landscape field underneath the OSK. The inset now reserves the entire intersection; the demo hides nonessential surrounding controls when height is limited, keeping text and touch targets at their original size.
- Confined startup denied external launcher utilities and Avalonia's procfs-derived process name. Shell builtins and the supported `RESOURCE_NAME` identity fixed startup without broader permissions.

## Evidence and remaining work

`artifacts/verification/maliit/` contains baseline/native screenshots, numeric/password/email/multiline/dialog evidence, preserved composition, final layout, redacted device logs and desktop smoke output. These are local ignored artifacts; this document is the versionable record.

`artifacts/verification/lifecycle/` adds landscape before/after screenshots, preedit before suspension, portrait after resume, reopened keyboard and redacted Click logs. The original orientation preferences were restored (`orientation-lock=none`, `rotation-lock=true`); power timeouts and brightness were not changed.

Next device checks: touch selection and replacement, native clipboard, accented composition, search/phone/URL modes and longer background sessions. Packaged DBus inventory needs an in-process implementation because launching `gdbus` is denied. Resolve the Click review tool's policy table before claiming a complete store review. Keep the developer path and baseline switch for comparisons.
