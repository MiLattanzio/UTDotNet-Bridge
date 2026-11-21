# UTDotNet Bridge

Experimental .NET 10 libraries for Ubuntu Touch, with an opt-in native Maliit keyboard bridge for Avalonia 12.1.6.

## Packages

| Package | Purpose |
| --- | --- |
| UTDotNet.Core | Conservative platform detection, phone scaling and input contracts. |
| UTDotNet.Diagnostics | Bounded logging, environment reports and optional DBus inspection. |
| UTDotNet.UbuntuTouch | Managed Maliit peer transport and bidirectional input callbacks, without Qt. |
| UTDotNet.Avalonia | TextBox integration, composition/commit, focus lifecycle and keyboard inset helpers. |

For an Avalonia application, install **UTDotNet.Avalonia**; its dependencies bring in the other three packages. This preview requires .NET 10. Applications still choose their Avalonia desktop backend, theme and native dependencies.

```sh
dotnet add package UTDotNet.Avalonia --version 0.1.2-preview.1
```

## Avalonia integration

On the Avalonia UI thread, create a bridge for the window/application group and retain the bridge and each registration for the view lifetime:

```csharp
using UTDotNet.Avalonia;
using UTDotNet.Core;
using UTDotNet.Diagnostics;

MaliitInputBridge? bridge = null;
IDisposable? registration = null;
if (new PlatformDetector().Detect().MobileEnvironment == LinuxMobileEnvironment.UbuntuTouch)
{
    bridge = new MaliitInputBridge(new DiagnosticLog());
    registration = bridge.Register(textBox, new InputRequest { InputType = InputType.Email });
}

// When the view is torn down, on the UI thread:
// registration?.Dispose();
// bridge?.Dispose();
```

Configure `PasswordChar`, `AcceptsReturn`, validation and layout in the application. Use `MaliitLayout.BottomInset` and `InputAreaChanged` to scroll fields above the keyboard. The bridge dismisses the keyboard when its window deactivates and retains composed text; a later tap starts a fresh input session.

The libraries do not set application-wide display scaling. The demo uses Lomiri grid size divided by eight, 16-unit text and controls at least 48 logical units high for comfortable phone use. On Ubuntu Touch/X11, configure `X11PlatformOptions.OverlayPopups = true` for in-window dropdowns.

## Validation and limits

Validated on a Fairphone 5 running Ubuntu Touch 24.04-2.2 through X11/XWayland, including native input in a confined Click, physical rotation, suspend/resume and keyboard dismissal on the lock screen. Native Wayland is blocked by the compositor protocol version on that image. Clipboard interoperability, touch selection handles, multiple keyboard languages and long sessions need further validation. Per-span preedit styling and hardware-key redirection are not implemented.

Show/hide acknowledgements are reported as `RequestedUnconfirmed`, not proof of keyboard visibility. The optional DBus inventory uses external `gdbus`, which can be absent or denied by confinement; the managed Maliit connection is independent. This is a preview tested on one device, with no compatibility promise for every Ubuntu Touch image.

See the [sample](https://github.com/MiLattanzio/UTDotNet-Bridge/tree/nuget-v0.1.2-preview.1/samples/UTDotNet.InputDemo), [input integration](https://github.com/MiLattanzio/UTDotNet-Bridge/blob/nuget-v0.1.2-preview.1/docs/input-method.md), [device validation](https://github.com/MiLattanzio/UTDotNet-Bridge/blob/nuget-v0.1.2-preview.1/docs/device-testing.md) and [deployment guide](https://github.com/MiLattanzio/UTDotNet-Bridge/blob/nuget-v0.1.2-preview.1/docs/deployment.md).

MIT licensed. Source: [MiLattanzio/UTDotNet-Bridge](https://github.com/MiLattanzio/UTDotNet-Bridge).
