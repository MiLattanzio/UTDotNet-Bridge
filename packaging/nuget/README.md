# UTDotNet Bridge

Experimental .NET 10 libraries for Ubuntu Touch, with an opt-in native Maliit keyboard bridge and Content Hub clipboard for Avalonia 12.1.6.

## Packages

| Package | Purpose |
| --- | --- |
| UTDotNet.Core | Conservative platform detection, phone scaling and input/clipboard contracts. |
| UTDotNet.Diagnostics | Bounded logging, environment reports and optional DBus inspection. |
| UTDotNet.UbuntuTouch | Managed Maliit peer transport and Content Hub text clipboard, without Qt. |
| UTDotNet.Avalonia | TextBox integration, composition/commit, focus lifecycle, guarded Copy/Cut/Paste and keyboard inset helpers. |

For an Avalonia application, install **UTDotNet.Avalonia**; its dependencies bring in the other three packages. This preview requires .NET 10. Applications still choose their Avalonia desktop backend, theme and native dependencies.

```sh
dotnet add package UTDotNet.Avalonia --version 0.1.6-preview.1
```

## Avalonia integration

On the Avalonia UI thread, while constructing your view, create a bridge for the window/application group and retain each registration for the view lifetime. In this fragment, `window` and `textBox` are your Window and TextBox:

```csharp
using UTDotNet.Avalonia;
using UTDotNet.Core;
using UTDotNet.Diagnostics;
using UTDotNet.UbuntuTouch;

MaliitInputBridge? bridge = null;
IDisposable? registration = null;
TextClipboardBridge? clipboard = null;
var log = new DiagnosticLog();
if (new PlatformDetector().Detect().MobileEnvironment == LinuxMobileEnvironment.UbuntuTouch)
{
    bridge = new MaliitInputBridge(log);
    registration = bridge.Register(textBox, new InputRequest { InputType = InputType.Email });
    window.Opened += (_, _) =>
    {
        if (window.TryGetPlatformHandle()?.HandleDescriptor == "XID")
            clipboard = new TextClipboardBridge(textBox, log, new ContentHubClipboard(log));
    };
}

window.Closed += (_, _) =>
{
    clipboard?.Dispose();
    registration?.Dispose();
    bridge?.Dispose();
};
```

Configure `PasswordChar`, `AcceptsReturn`, validation and layout in the application. Use `MaliitLayout.BottomInset` and `InputAreaChanged` to scroll fields above the keyboard. The bridge dismisses the keyboard when its window deactivates and retains composed text; a later tap starts a fresh input session.

Touching a field finalizes pending composition before selection, and focus returned by a closing menu preserves the input context.

## Native clipboard

In **0.1.6-preview.1**, the three-argument `TextClipboardBridge` above routes the existing Copy/Cut/Paste commands through `ContentHubClipboard`. Retain and dispose one bridge per field; the provider can be shared across fields. Cut deletes only after an accepted write. Delayed Cut/Paste results cannot edit an editor whose text, selection or focus changed. The helper preserves undo and field constraints; passwords/sensitive fields cannot Copy/Cut but can Paste.

For explicit application actions, call `await nativeClipboard.WriteTextAsync(text, cancellationToken)` and report success only if it returns `true`. `await nativeClipboard.ReadTextAsync(cancellationToken)` returns plain text or `null` when unavailable, invalid or non-text. Use these operations from the active Ubuntu Touch app in response to the user; the service checks caller focus. Failed native reads do not return stale X11 data.

Operations have a two-second deadline, support cancellation and use a managed session-bus connection without Qt, external commands or additional AppArmor policy groups on the tested profile. UTF-8 plain text supports accents, emoji and line breaks, with a 4 MiB payload limit. Rich text, images and files are not supported. Persistence after process exit, reboot or service restart remains untested.

The existing two-argument `TextClipboardBridge` retains bounded X11 Copy/Cut and Avalonia Paste. `ClipboardTextWriter.TryWriteAsync` remains its application-level counterpart; these older helpers do not enable native Content Hub interoperability.

The libraries do not set application-wide display scaling. The demo uses Lomiri grid size divided by eight, 16-unit text and controls at least 48 logical units high for comfortable phone use. On Ubuntu Touch/X11, configure `X11PlatformOptions.OverlayPopups = true` for in-window dropdowns.

## Validation and limits

Validated with 93 automated tests and on a Fairphone 5 running Ubuntu Touch 24.04-2.2 through X11/XWayland. Confined Click 0.1.6 passed bidirectional exchange with a native Qt control and Morph, including accented text, emoji and multiline paste. The user confirmed physical Cut/Paste and comfortable menus. A selection-handle drag also restored the original menu and Copy worked; cross-line dragging and other handle directions remain to be tested. Earlier checks covered selection of pending composition, native input, rotation, suspend/resume and keyboard dismissal on the lock screen. Native Wayland is blocked by the compositor protocol version on that image. Multiple keyboard languages, long sessions, per-span preedit styling and hardware-key redirection remain outside the validated scope.

Show/hide acknowledgements are reported as `RequestedUnconfirmed`, not proof of keyboard visibility. The optional DBus inventory uses external `gdbus`, which can be absent or denied by confinement; the managed Maliit connection is independent. This is a preview tested on one device, with no compatibility promise for every Ubuntu Touch image.

See the [sample](https://github.com/MiLattanzio/UTDotNet-Bridge/tree/nuget-v0.1.6-preview.1/samples/UTDotNet.InputDemo), [input integration](https://github.com/MiLattanzio/UTDotNet-Bridge/blob/nuget-v0.1.6-preview.1/docs/input-method.md), [device validation](https://github.com/MiLattanzio/UTDotNet-Bridge/blob/nuget-v0.1.6-preview.1/docs/device-testing.md) and [deployment guide](https://github.com/MiLattanzio/UTDotNet-Bridge/blob/nuget-v0.1.6-preview.1/docs/deployment.md).

MIT licensed. Source: [MiLattanzio/UTDotNet-Bridge](https://github.com/MiLattanzio/UTDotNet-Bridge).
