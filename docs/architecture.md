# Architecture

Implemented modules: Core, Diagnostics, UbuntuTouch, Avalonia and InputDemo. UbuntuTouch references Diagnostics/Core and Tmds.DBus.Protocol 0.94.1; Avalonia references the native module. One test project covers shared and framework boundaries.

`PlatformProbe` collects allowlisted environment and small metadata files. `PlatformDetector.Detect(snapshot)` contains pure conservative decision logic. A desktop Lomiri session or Maliit bus name alone does not identify Ubuntu Touch. Session type and application backend remain separate facts.

`SessionBusDiagnostics` uses optional `gdbus` through `ICommandRunner` for bounded, read-only inventory. Native input uses in-process D-Bus and does not shell out. Diagnostics cap output at 64 KiB per stream, each process at three seconds and event history at 300 entries.

`MaliitSession` owns one peer per focus session. It discovers the local Unix endpoint, installs the callback object, activates context, sends widget state and requests the keyboard. Outbound calls are serialized with four-second deadlines; cancellation closes the connection. Callback payloads are applied on the UI thread before acknowledgement. A callback must never await another call to that peer.

Maliit sends initialization calls before Tmds 0.94.1 finishes its Hello probe. `MaliitHandshakeStream`, installed through the public connection-stream extension, buffers early calls/signals until the handler exists. It passes the Hello reply/error first, then releases callbacks in order. Limits are 32 deferred frames and 64 KiB total. Normal traffic passes through unchanged; this protocol needs no file-descriptor passing. Tests cover ordering, partial reads, bounds and disposal. This scoped workaround should be re-evaluated when updating Tmds.

`MaliitInputBridge` registers specific TextBoxes. Each focus session has its own connection, cancellation and reset generation. Old callbacks cannot edit a newly focused field. The bridge disables the platform IME on the active registered control, uses its public text client and restores the original setting on release. `TextInputAdapter` remains an independent observer and hint mapper.

Commit, selection replacement and editing keys use Avalonia routed editing events. Preedit stays outside committed Text. Pending composition is finalized on hide/focus loss; an explicit application replacement of Text cancels it. Reset generations suppress stale edits. Password surrounding text, cursor/selection offsets and prediction are removed at the transport boundary and from diagnostics.

The last measured global keyboard area is retained across fields and owned dialogs because a new context may not receive unchanged geometry. Layout uses physical intersection divided by rendering scale; a compositor-resized window is not shrunk twice. Window closure releases subscriptions; bridge disposal clears the retained area. Geometry and successful requests do not establish boolean visibility.

The demo sets an absolute display scale from Lomiri grid/8 before backend initialization, respecting explicit Avalonia overrides. It initializes the phone window from available screen bounds rather than centering a desktop-sized window off screen. Controls have minimum 48-unit touch targets and scrollable content.
