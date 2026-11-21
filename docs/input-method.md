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

Individual preedit spans use Avalonia's default styling; the public client lacks equivalent per-span formatting. Preedit rectangle queries report unsupported, as the inspected Qt implementation also does. Hardware-key redirection and dynamic mutation of registered hints are not implemented. Applications configure password/multiline behavior, input hints and value validation.

`MaliitHandshakeStream` addresses early callbacks during Tmds 0.94.1 setup through its public custom-stream API. The on-device failure was traced to [DBusConnection.GetConnection](https://github.com/tmds/Tmds.DBus/blob/rel/0.94.1/src/Tmds.DBus.Protocol/DBusConnection.cs); the bounded workaround has dedicated tests. Re-evaluate it when upgrading Tmds.

Native Wayland remains blocked by xdg_wm_base v1 versus required v3, and the observed compositor registry lacks text-input-v3. XWayland/Maliit input has also been exercised in the standard Click AppArmor profile on this FP5; this does not fix native Wayland or establish support for other devices.

For landscape layout, reserve the full measured keyboard intersection. An earlier 200-unit minimum content height left controls beneath the keyboard on the FP5. The demo now hides its title/status and global export button when less than 360 logical units remain, preserves the 16-unit font and 48-unit input height, and scrolls the active field into view. Export remains available on the System tab. It recalculates the intersection on window resize as well as area callbacks.
