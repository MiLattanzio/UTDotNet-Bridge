# Native input integration

The opt-in Maliit bridge is implemented and exercised on the Fairphone 5 developer deployment. It uses public Avalonia APIs and a managed D-Bus peer; no Qt dependency, framework fork or custom keyboard is introduced. Desktop use retains Avalonia's normal IME.

## Verified protocol

Discovery reads `org.maliit.Server.Address.address` at `/org/maliit/server/address` on `org.maliit.server`. Peer calls use `com.meego.inputmethod.uiserver1` at `/com/meego/inputmethod/uiserver1`. The callback interface is `com.meego.inputmethod.inputcontext1` at `/com/meego/inputmethod/inputcontext` (no final `1` in the path).

Runtime introspection through GLib's peer connection confirmed the installed server interface. The earlier `gdbus --address` experiment failed because it attempted bus initialization; it did not establish an unavailable peer. The implemented client maintains callbacks for the whole focus session.

Widget keys and editing semantics were checked against [MInputContext](https://github.com/maliit/framework/blob/master/input-context/minputcontext.cpp), [server XML](https://github.com/maliit/framework/blob/master/dbus_interfaces/minputmethodserver1interface.xml) and [callback XML](https://github.com/maliit/framework/blob/master/dbus_interfaces/minputmethodcontext1interface.xml). Hint/key values were checked against [Qt 5.15 qnamespace.h](https://github.com/qt/qtbase/blob/5.15/src/corelib/global/qnamespace.h). Source inspection is supplemented by actual device tests, not assumed equivalent to every shipped image.

## Editing and lifecycle

- Implements activate, widget update, show, hide and reset; commit replacement/cursor, preedit, selection, editing keys, area, hide, activation loss and disconnect callbacks.
- Commits use TextInput and normal deletion/selection, preserving undo. Preedit uses the public text client. Commits never append directly to TextBox.Text.
- A focus change invalidates the old connection. Pending composition is finalized in its original field. Explicit hide finalizes preedit, resets the server and updates state before hiding.
- Failed connections restore the framework IME. A later tap creates a new peer; no infinite reconnect loop runs. Dialog fields unregister on detachment.
- Window deactivation now finalizes pending composition, sends `hideInputMethod`, waits for its acknowledgement and then releases the peer. Disconnecting alone did not hide the keyboard on the FP5 lock screen. `DeactivateAsync()` exposes the same UI-thread operation to hosts. Reopening waits for any outstanding dismissal so an old hide cannot close a newly opened context; shutdown uses a one-second cancellation budget and logs failures without claiming visibility.
- Focus loss is resolved after the current focus transfer. Switching directly to another registered field retains the existing handoff behavior; leaving text input hides the keyboard. Detached callbacks cannot edit the old field while its terminal hide is in progress.
- Passwords omit surrounding text/offsets, disable prediction/capitalization and log no text or lengths. Ordinary text content is also redacted. Live protocol memory necessarily contains the text being edited.
- Area events guide layout. Acknowledgements return RequestedUnconfirmed and visibility stays Unknown. Device test results establish actual keyboard appearance separately with compositor captures.

## Limits

The Maliit bridge itself does not synchronize Avalonia's clipboard with Ubuntu Touch's native clipboard. NuGet 0.1.6-preview.1 provides a separate, opt-in Content Hub integration, described below and exercised in Click 0.1.6. See the [device record](device-testing.md#native-clipboard--click-016) for current interoperability results and the earlier selection limitations.

## Bounded clipboard writes (0.1.5-preview.1)

The phone follow-up found that Cut invoked Avalonia's clipboard write but never deleted the selection. In [Avalonia 12.1.6's X11 implementation](https://github.com/AvaloniaUI/Avalonia/blob/12.1.6/src/Avalonia.X11/Selections/Clipboard/X11ClipboardImpl.cs), a small plain-text write acquires clipboard ownership, then awaits the clipboard manager's `SAVE_TARGETS` response without a timeout. `TextBox.Cut()` waits for that operation before deleting. Copy/paste within the process can therefore work while Cut remains pending.

`ClipboardTextWriter.TryWriteAsync` bounds the acknowledgement wait to 500 ms. If it expires, the public `IClipboard.TryGetInProcessDataAsync()` must confirm ownership of the exact object submitted by this write; merely reading the same text is insufficient. The ownership query is also bounded. Missing ownership, cancellation or failure preserves the source text. The underlying platform write cannot be cancelled; late faults are observed without retaining an editor or scheduling a later deletion. Local ownership does not establish clipboard persistence after app exit or exchange with native Qt applications.

`TextClipboardBridge` opts a TextBox into these Copy/Cut operations. Hosts must enable it only for the affected Ubuntu Touch X11 path and dispose it with the field. Cut uses the public `SelectedText` editing property, preserving undo, and cancels its deletion if text, selection, sensitivity, editability, focus or attachment changes while copying. Password/sensitive fields cannot be copied or cut through the adapter. Paste retains Avalonia's normal behavior. These opt-in helpers are included in **UTDotNet.Avalonia 0.1.5-preview.1**.

The demo retains Avalonia's original text menus on Ubuntu Touch. When a menu opens for its current field, its items receive minimum dimensions of 64×48 units and a 16-unit font; the flyout opens transiently. No replacement context flyout is assigned, preserving the framework's selection behavior. This applies to input fields, dialog fields, notes and the report. A separate bridge fix rechecks actual field focus before a deferred hide, so focus returned by a closing menu is not mistaken for a real blur. Window deactivation still explicitly hides the native keyboard.

When a pointer press targets a field with pending native preedit, the Maliit bridge now commits that composition and requests a peer reset before TextBox's pointer handler runs. TextBox selects its committed Text, so leaving the word only in the preedit presenter prevented touch selection. The existing reset generation guard handles stale callbacks; the new headless regression test first reproduced the missing committed text, then passed with pointer finalization.

## Native Content Hub clipboard (0.1.6-preview.1)

These APIs are included in **NuGet 0.1.6-preview.1** and exercised in installed Click 0.1.6. Install `UTDotNet.Avalonia` at that version for the full integration, or `UTDotNet.UbuntuTouch` for the standalone clipboard provider.

`ContentHubClipboard` implements `ITextClipboard` through the session-bus service/interface `com.lomiri.content.dbus.Service`, object `/`. It uses `CreatePaste(ssayas) → b` and `GetLatestPasteData(s) → ay`. The empty surface ID selects the service's normal caller-PID focus check, which Lomiri recognizes for the confined X11 demo. It does not impersonate a Mir surface or request broader AppArmor policy. The managed client needs neither Qt nor an external command.

The [service implementation](https://gitlab.com/ubports/development/core/lomiri-content-hub/-/blob/f9f0adf3e03fb1ba95a1d4473d7727fd57fcce3b/src/com/lomiri/content/detail/service.cpp) and [MIME serialization format](https://gitlab.com/ubports/development/core/lomiri-content-hub/-/blob/f9f0adf3e03fb1ba95a1d4473d7727fd57fcce3b/src/com/lomiri/content/utils.cpp) are the protocol references. The phone runs Content Hub `2.2.3+0~20260916155039.1+ubports24.04.2~1.gbpe8c8d6`; runtime introspection and actual confined calls supplement the source inspection.

On the Avalonia UI thread, opt each Ubuntu Touch field into the native clipboard and keep the registration for its lifetime:

```csharp
using UTDotNet.Avalonia;
using UTDotNet.Core;
using UTDotNet.Diagnostics;
using UTDotNet.UbuntuTouch;

var log = new DiagnosticLog();
TextClipboardBridge? editing = null;
if (new PlatformDetector().Detect().MobileEnvironment == LinuxMobileEnvironment.UbuntuTouch)
{
    var nativeClipboard = new ContentHubClipboard(log);
    editing = new TextClipboardBridge(textBox, log, nativeClipboard);
    // For an explicit Copy button: await nativeClipboard.WriteTextAsync(text, cancellationToken).
}
window.Closed += (_, _) => editing?.Dispose();
```

`textBox` and `window` above are existing controls. One `ContentHubClipboard` can be shared across fields; it opens and disposes a connection per explicit operation. A two-second deadline covers connection and request, and cancellation/timeout closes that transport. There is no background polling or clipboard-content logging. Writes return true only after Content Hub accepts them. Reads return null for unavailable, invalid or non-text data, with no stale X11 fallback. The codec handles UTF-8 plain text, including accents, emoji and line breaks; it validates the little-endian layout, at most 16 MIME entries and the 4 MiB payload bound. Other text encodings, images, files and rich-text preservation are outside this implementation.

The three-argument `TextClipboardBridge` handles Copy, Cut **and Paste**. Cut and Paste use Avalonia's public editing path, preserving undo and field constraints. Late results cannot edit a changed, detached, disposed or inactive editor; deactivation followed by reactivation also invalidates an outstanding operation. Passwords/sensitive fields cannot Copy/Cut, but can Paste. The two-argument constructor retains the bounded X11 workaround and framework Paste for existing consumers.

The demo enables Content Hub by default on Ubuntu Touch, including dialog fields, notes, report selection and the Copy diagnostic report button. `--no-native-clipboard` selects the earlier backend for comparison. Font, scaling, original menus and touch target sizes remain unchanged. Content Hub stores accepted pastes independently of the X11 owner; persistence across process exit, reboot or service restart has not been tested here.

## Other limits

Individual preedit spans use Avalonia's default styling; the public client lacks equivalent per-span formatting. Preedit rectangle queries report unsupported, as the inspected Qt implementation also does. Hardware-key redirection and dynamic mutation of registered hints are not implemented. Applications configure password/multiline behavior, input hints and value validation.

`MaliitHandshakeStream` addresses early callbacks during Tmds 0.94.1 setup through its public custom-stream API. The on-device failure was traced to [DBusConnection.GetConnection](https://github.com/tmds/Tmds.DBus/blob/rel/0.94.1/src/Tmds.DBus.Protocol/DBusConnection.cs); the bounded workaround has dedicated tests. Re-evaluate it when upgrading Tmds.

Native Wayland remains blocked by xdg_wm_base v1 versus required v3, and the observed compositor registry lacks text-input-v3. XWayland/Maliit input has also been exercised in the standard Click AppArmor profile on this FP5; this does not fix native Wayland or establish support for other devices.

For landscape layout, reserve the full measured keyboard intersection. An earlier 200-unit minimum content height left controls beneath the keyboard on the FP5. The demo now hides its title/status and global export button when less than 360 logical units remain, preserves the 16-unit font and 48-unit input height, and scrolls the active field into view. Export remains available on the System tab. It recalculates the intersection on window resize as well as area callbacks.
