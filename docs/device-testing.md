# Validation record

Sessions: 2026-11-21 and 2026-11-22. No result is inferred from compilation alone.

## Environment and scope

Fairphone 5 / FP5, AArch64, Ubuntu Touch 24.04-2.2 build 3 over Ubuntu 24.04.5. .NET 10.0.12 self-contained, Avalonia 12.1.6, Tmds.DBus.Protocol 0.94.1. Initial ADB directory deployment to `/home/phablet/utdotnet-phase1`, running as phablet through existing X11/XWayland `DISPLAY=:0`. Subsequent `utdotnet.inputdemo` Click 0.1.0 installed through `com.lomiri.click.Install`, launched with `lomiri-app-launch`, and verified under AppArmor enforcement. No OpenStore publication.

Device interaction below used injected touchscreen events through the already-accessible evdev interface. It exercised the real OSK and editing protocol; it was not a person typing on the physical glass. Evidence comes from redacted logs and captures of the system compositor `/run/mir_socket`. Headless tests and programmatic smoke tests have narrower scope.

## Automated and desktop results

| Check | Result | Scope |
|---|---|---|
| Release build / ARM64 publish | Pass | SDK 10.0.401; untrimmed self-contained directory, about 108 MiB |
| Automated suite | Pass, 93 tests | Earlier 70 checks plus native MIME/Unicode validation, an isolated session-bus protocol test, rejection/timeouts/cancellation, guarded native Paste, stale-selection/background-return protection, password Paste and field constraints. Native clipboard interoperability is a separate device result |
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
| Touch selection | Word selection pass, including pending preedit in the user's 0.1.5 test. The earlier 0.1.2 handle-drag menu problem did not recur in the 0.1.6 check: a drag extended selection 17–21 to 10–21, the original menu returned, and Copy succeeded. Cross-line dragging and other handle directions remain to be tested |
| Selection replacement / undo | Pass for physical replacement: user selected `mondo` in Multiline and replaced it with `sole` plus a space using the native OSK. Undo/redo remains a headless result, not a phone test |
| Clipboard within the demo | Copy/Paste pass in 0.1.2: `mondo` transferred from Standard text to Email. Cut initially failed, then passed the user's physical 0.1.5 test: `sole` was removed and pasted back. Logs confirm exact clipboard ownership and text lengths 4 → 0 → 4. Multiline transfer, paste over a selection and undo/redo additionally pass headless tests |
| Clipboard with native Qt apps (0.1.2) | Fail in this session: the native gallery did not offer Paste after the demo copied `mondo`; after native Copy of `luna`, the demo still pasted stale `mondo`. Native Copy/Paste of `luna` worked, confirmed by the user. Internal demo clipboard success does not establish system interoperability |
| Native clipboard (0.1.6) | Pass in both directions with the native Qt probe and confined Morph. UTF-8 accents, emoji and two lines reached the demo; Cut wrote accepted native data before removing the selection; a distinct Morph copy replaced earlier clipboard content. User confirmed physical Cut/Paste and comfortable menus. See the detailed record below |
| Search / phone / URL layouts | Implemented hints; device interaction not yet verified |
| Accents, alternate languages, long-press variants | Unverified |
| Physical rotation | Pass: user turned FP5; portrait 408×865 ↔ landscape 900×384, scale 3; text retained. Native landscape keyboard and full focused field visible after layout fix |
| Screen-off / wake with USB | App state and peer reopening pass; deep suspend failed with kernel `a600000.ssusb`, errno -16 (busy), success 0 / fail 2 |
| Deep suspend / resume without USB | Pass: user unplugged, powered screen off for at least 30 seconds, woke/unlocked and reconnected in portrait. Kernel success rose 0 → 17, failures stayed 2; same Click PID 10449. This is one user test interval containing multiple kernel sleep entries, not 17 independent tests |
| Composition and editing after resume | Pass in Click: `ciao` was still preedit before sleep, finalized and preserved after resume/context loss; retap reconnected; backspace, replacement character and space commit worked |
| Click confinement | Pass for installation, launch, scale, X11/Maliit input and lifecycle above; `/proc/10449/attr/current` returned `utdotnet.inputdemo_inputdemo_0.1.0 (enforce)`, no extra policy groups |
| Checks dropdowns (0.1.1) | Pass: user physically opened the first menu, selected Pass, reopened and selected Fail, and confirmed it works. Logs show matching open/result/close events without the earlier focus cycling; PID 11496 ran under `utdotnet.inputdemo_inputdemo_0.1.1 (enforce)` |
| Keyboard on lock screen (0.1.2) | Pass: user physically locked/woke/unlocked the FP5 and confirmed the app keyboard no longer covered the lock screen, `ciao` was preserved and a later field tap reopened input. Logs show deactivation at 01:32:55.994 UTC, composition finalized, hide requested at 01:32:56.014 and acknowledged at 01:32:56.017 before peer disposal; activation resumed at 01:33:02.105. Standard AppArmor remains enforced |
| Click review / OpenStore | Review tool blocked by missing static 2404.2 policy metadata (two errors); no other errors or warnings, some checks not applicable. Not published or OpenStore-approved |
| Long sessions | Unverified |
| Native Wayland on FP5 | Blocked before window creation: xdg_wm_base v1 versus required >=3; no text-input manager in observed registry |

The bridge is experimental. Graphics startup still emits gralloc/Zink/GLX warnings; successful basic interaction does not establish acceleration or broad GPU compatibility. Clipboard comparisons used Lomiri UI Toolkit Gallery in the earlier session, then a Qt probe and Morph in 0.1.6. No Uno application was tested.

## Selection and clipboard session — Click 0.1.2

The confined demo remained PID 5498 with profile `utdotnet.inputdemo_inputdemo_0.1.2 (enforce)`. The comparison app was Lomiri UI Toolkit Gallery 1.1, PID 7895 with profile `uitk-gallery.ubports_lomiri-ui-toolkit-gallery_1.1 (enforce)`. Only disposable test fields and known test strings were used.

1. Native keyboard input produced `ciao mondo ` in Standard text. Long-press selected `mondo` (offsets 5–10); dragging the left handle extended the selection to `ciao mondo` (0–10). Screenshots and redacted selection logs agree.
2. After the drag, the selection remained correct but the menu disappeared. Waiting, holding inside the selection and tapping a handle did not restore it. Tapping after the text to clear the selection, then holding `mondo` again, restored Cut/Copy/Paste. This menu recovery limitation was observed with injected touches; the later user check covered word selection and replacement, not this exact drag sequence.
3. Copy of `mondo` followed by Paste into empty Email produced exactly `mondo`. Choosing clipboard commands caused the field to lose focus and the Maliit context to hide; retapping reactivated input. The status label's earlier `OSK Unavailable` result was not a live visibility measurement.
4. In the native gallery, `luna ` was entered and `luna` selected. Its Paste action was disabled despite the earlier demo Copy. After native Copy, Paste in the demo's empty Multiline field produced the old `mondo`, not `luna`. As a control, the gallery's Standard field was cleared and Paste invoked: the user confirmed it restored `luna`. These observations establish the interoperability failure without assuming whether the missing integration belongs in the backend, shell or an adapter.
5. The user then physically selected `mondo` in the demo's Multiline field, replaced it with `sole` and a space, and confirmed comfortable operation. The initial positive response for a separate Cut/Paste was subsequently corrected: choosing Cut closes the menu but leaves the text. The corresponding logs show focus changes without a text deletion. Cut is therefore a failing device result, regardless of headless command success. The demo retained its 16-unit font, 48-unit minimum main controls and scale 3. Text menus and selection handles still use Avalonia's default sizing; the minimum for main controls is not a claim that every menu item meets that size.

`MaliitClipboardTests` uses the headless clipboard and fake Maliit peer to check clipboard editing, multiline transfer, selection replacement, undo/redo, continued native input and redacted diagnostics. It also checks that password Copy/Cut user shortcuts leave the clipboard unchanged and that the native state omits sensitive text/offsets. This does not verify native clipboard transport or physical password-menu behavior. Applications invoking `TextBox.Copy()` or `Cut()` directly must honor `CanCopy`/`CanCut`, as Avalonia's normal commands do.

### Cut follow-up

Click 0.1.3 added redacted clipboard action/error diagnostics and fixed a deferred blur handler that hid the keyboard even after focus had already returned to the field. The default-menu headless test reproduced that focus defect before the fix. PID 8708 ran with `utdotnet.inputdemo_inputdemo_0.1.3 (enforce)`. On-device logs confirmed three Cut requests without deletion or a reported write exception; the user confirmed that text remained and the menu was still uncomfortable. Increasing MenuItem minimum dimensions in the window stylesheet did not establish usable text-menu targets, so that styling attempt was replaced.

Click 0.1.4 introduced the bounded write/ownership check described in [input integration](input-method.md#bounded-clipboard-writes-015-preview1) and guards against late deletion. Its replacement three-button flyout was rejected by the user, who preferred the earlier menu and reported being unable to select. Logs showed native preedit updates and repeated field touches without committed Text. A headless test reproduced that gap: pressing a field with preedit left Text empty before the framework tried to select it.

Click 0.1.5 restores Avalonia's original text menus, applies 64×48-unit minimum item sizes directly to the active menu, and finalizes preedit before TextBox handles a pointer press. The bounded clipboard write and stale-editor protections remain. All 70 automated tests pass.

**Physical 0.1.5 result: pass.** The user was asked to type `sole` even without a trailing space, select it, Cut and Paste, and confirmed “Funziona tutto”. PID 9262 ran under `utdotnet.inputdemo_inputdemo_0.1.5 (enforce)`. The log shows preedit becoming committed text at 02:52:24 UTC, selection 0–4, Cut requested at 02:52:27.239, exact in-process ownership confirmed at 02:52:27.767 while the manager acknowledgement remained pending, and deletion at 02:52:27.773. Paste at 02:52:32.285 restored length 4. This confirms both the clipboard acknowledgement stall and successful guarded deletion. There was no terminal Maliit hide during these menu actions. No new compositor screenshot was available; the exact visible result comes from the user's confirmation, supported by the redacted log.

Built and installed artifact: `artifacts/click/utdotnet.inputdemo_0.1.5_arm64.click`, SHA-256 `9d47b26a78f854001711a85af49d4472c4722cf90f9045ce8d1d1d8e1c85019b`. This package retains the standard AppArmor profile. The Click review tool still reports the same two missing-policy-metadata errors; installation and runtime success do not turn that store review into a pass.

## Regressions found and corrected during device testing

- Desktop initial width was centered to a negative X11 origin, then resized by Lomiri without correcting position. Phone screen bounds now determine initial geometry.
- The installed Ubuntu variable font rendered normal text unexpectedly bold in this stack. The demo uses the available static DejaVu Sans font and explicit compact tab sizing.
- Tmds attempted to reply to Maliit's early initialization callbacks before ConnectAsync completed. A bounded custom stream defers those callbacks until the handler is installed.
- Backspace can move committed words back into preedit. Pending composition is now finalized on hide/focus loss, preventing lost text.
- Maliit can omit an unchanged keyboard-area notification for a new context. The last measured global area is reused and intersected with the active window, including dialogs.
- A 200-unit minimum content height left the landscape field underneath the OSK. The inset now reserves the entire intersection; the demo hides nonessential surrounding controls when height is limited, keeping text and touch targets at their original size.
- Confined startup denied external launcher utilities and Avalonia's procfs-derived process name. Shell builtins and the supported `RESOURCE_NAME` identity fixed startup without broader permissions.
- Checks dropdowns failed to open on the phone with independent X11 popup windows. Version 0.1.1 uses Avalonia's embedded popup option on Ubuntu Touch and 48-unit minimum menu items. The user verified Pass/Fail selection on the installed package; desktop backend configuration is unchanged.
- Closing the Maliit peer on window deactivation did not itself hide the native keyboard. Version 0.1.2 explicitly sends hide before disposing the peer, preserves preedit, and serializes a later reopening behind that hide. It uses existing window/focus events with no additional AppArmor rules. Clearing a zero keyboard area during window teardown no longer queries detached screen coordinates.
- Returning focus from a context menu could still trigger the deferred blur hide. The bridge now checks that the original field remains unfocused before dismissing; window deactivation still hides independently.
- Cut waited indefinitely for a clipboard-manager acknowledgement despite already owning the data. The opt-in text clipboard helpers confirm exact in-process ownership after a bounded wait and delete only an unchanged, editable selection in the active editor. Failed ownership checks and late completions cannot delete a different selection.
- Text held only in the preedit presenter was not selectable by TextBox. Pointer presses now finalize/reset native composition before framework selection handling. The original menu was restored at the user's request, with explicit minimum item dimensions instead of a replacement button layout.

## Native clipboard — Click 0.1.6

On 2026-11-22, `ContentHubClipboard` replaced the demo's X11 clipboard path on Ubuntu Touch. The installed demo ran as PID 6398 with `utdotnet.inputdemo_inputdemo_0.1.6 (enforce)`, unchanged policy groups and scale 3. The native service accepted this confined caller's operations; the earlier explicit Lomiri focus query for baseline PID 5859 also returned true. The new client calls the native service from its own process with an empty surface ID, using the service's caller-PID verification. It does not use an unconfined intermediary for the demo.

The native reference control is `tests/device/NativeClipboardProbe.qml`, run with the device's `qmlscene`, `QT_QPA_PLATFORM=ubuntumirclient` and `QT_IM_MODULE=maliit`. This probe runs unconfined and is a test control, not a shipped native application. The separate Morph check used PID 6697 under `morph-browser (enforce)`. Its test tab was `about:blank`; no search/navigation was submitted.

Observed with injected touch events, compositor captures and redacted logs:

1. Qt's Copy placed `luna caffè 🌙\nseconda riga` on the native clipboard. The demo's original Paste menu inserted all 26 UTF-16 code units into Multiline at 20:46:00 UTC. Both lines, the accent and emoji are visible in `qt-to-demo-multiline.png`; the emoji uses Avalonia's available monochrome glyph.
2. The demo selected offsets 5–10 (`caffè`). At 20:46:48.729 UTC, Content Hub accepted the write; the source length changed 26 → 21, then `ClipboardCut` confirmed removal. The keyboard remained visible. A newly opened empty Qt probe pasted `caffè`, proving the result was not its earlier local text.
3. Morph's native Paste inserted `caffè` into the selected separator in the disposable `about:blank` address field, producing `aboutcaffèblank`. Morph then copied that whole 15-character value. Paste into the demo's empty Email field received exactly the new value at 20:50:46.733 UTC, replacing the previous native clipboard value.
4. The user was asked to physically select, Cut and Paste in the installed 0.1.6, and confirmed: “Sì, funziona ed è comodo”. This is the user's usability result, separate from the injected-touch/log evidence above.
5. A left-handle drag in Multiline extended the selection from 17–21 (`riga`) to 10–21 (`econda riga`). The original menu reappeared; Copy at 20:52:54.186 UTC was accepted by Content Hub. The earlier menu-recovery problem was not reproduced in this sequence. No new handle/menu implementation was needed.

The build passes 93 tests. Native protocol tests use a private session bus and fake Content Hub to verify actual request signatures, identity arguments, successful/denied operations, timeouts and cancellation. Parser tests include a native-format fixture, Unicode, truncated/invalid buffers and size limits. UI tests cover native Copy/Cut/Paste, replacement/undo, no stale X11 fallback, changed/detached/disposed/read-only editors, background-return races, password command restrictions and MaxLength. Only the headless deactivation regression uses a pinned-framework reflection hook because the headless backend cannot emit that compositor event; production uses public APIs/events.

Installed artifact: `artifacts/click/utdotnet.inputdemo_0.1.6_arm64.click`, SHA-256 `b347c38ae1405dbdf875969fc4e6e5d8d012f60b1f8b4fbbacb5e82f8580dce8`. The review tool still lacks policy 2404.2 metadata and reports the same two policy errors; this is not an OpenStore approval. Native clipboard changes are included in **NuGet 0.1.6-preview.1**; the earlier 0.1.5-preview.1 packages do not contain them.

## Evidence and remaining work

`artifacts/verification/maliit/` contains baseline/native screenshots, numeric/password/email/multiline/dialog evidence, preserved composition, final layout, redacted device logs and desktop smoke output. These are local ignored artifacts; this document is the versionable record.

`artifacts/verification/lifecycle/` adds landscape before/after screenshots, preedit before suspension, portrait after resume, reopened keyboard and redacted Click logs. The original orientation preferences were restored (`orientation-lock=none`, `rotation-lock=true`); power timeouts and brightness were not changed.

`artifacts/verification/checks/click-0.1.1-menu.log` records the user's successful physical dropdown interaction. The compositor capture service returned `Failed to obtain buffer stream from screencast` during this follow-up, so this result relies on the user's confirmation and event logs, not a new screenshot. The automated test separately verifies touch opening, selection, minimum item height, reopening and outside dismissal.

`artifacts/verification/lock/` contains the 0.1.2 pre-lock composition capture and redacted lifecycle log. The user confirmed the physical lock-screen behavior and reopening after unlock. This follow-up corrected a gap in the earlier suspend/resume test: retained text after resume did not establish keyboard dismissal while the lock screen was visible. The bridge still reports keyboard visibility Unknown; the hide acknowledgement is not used as a visibility measurement.

`artifacts/verification/selection/` contains `long-press.png`, `expanded-selection.png`, `handle-retap.png`, `reselected-word.png`, `pasted-email.png`, `native-copy-selection.png`, `native-to-avalonia-result.png` and redacted logs. Captures stopped returning buffers during the native control Paste; the subsequent native control, physical replacement and Cut/Paste results rely on the user's confirmations, with demo logs as supporting evidence. Screenshot failure is not reported as a clipboard failure.

`click-0.1.3-selection.log` records Cut reaching the framework without deletion; `click-0.1.4-selection.log` records uncommitted preedit and unsuccessful selection attempts; `click-0.1.5-selection.log` records the final successful selection/Cut/Paste. Build logs retain the package hashes and unsuccessful review results. `artifacts/test-results/selection/selection.trx` records the final 70 passing automated tests.

`artifacts/verification/native-clipboard/` contains the native Qt, Morph and demo captures, the drag/menu check and `click-0.1.6.log`. `artifacts/native-clipboard-tests.log` records 93 passing tests; `artifacts/native-clipboard-click-build.log` retains packaging and review output.

Next work: broader cross-line/other-direction handle dragging, physical undo/password clipboard commands, accented keyboard composition, search/phone/URL modes and longer background sessions. Native clipboard persistence after process exit/reboot, non-text formats, other devices and large real-device payloads remain unverified. Packaged DBus inventory needs an in-process implementation because launching `gdbus` is denied. Resolve the Click review tool's policy table before claiming a complete store review. Keep the developer path and baseline switches for comparisons.
