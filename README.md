# UTDotNet Bridge

A reusable .NET/Avalonia integration library for Ubuntu Touch, with an **experimental native Maliit input bridge** and a diagnostic demo.

Native keyboard opening, composition/commit, backspace, password, numeric/email modes, multiline editing and hide/reopen have been exercised on a Fairphone 5 running Ubuntu Touch 24.04-2.2 through X11/XWayland. A Click build also runs with the standard AppArmor profile and native Maliit input. Tests use injected touchscreen events, physical orientation changes and system-compositor captures. Native Wayland remains blocked; see the [validation record](docs/device-testing.md) for precise scope.

## Install from NuGet

The published preview is **0.1.2-preview.1**, targeting **.NET 10** and tested with **Avalonia 12.1.6**. Run this in your Avalonia application's project directory:

```sh
dotnet add package UTDotNet.Avalonia --version 0.1.2-preview.1
```

Or add the reference to the application's `.csproj`:

```xml
<ItemGroup>
  <PackageReference Include="UTDotNet.Avalonia" Version="0.1.2-preview.1" />
</ItemGroup>
```

`UTDotNet.Avalonia` brings in the other three libraries as dependencies. For applications that only need a lower layer, install the corresponding package from this table using the same version:

| Package | Use it for |
| --- | --- |
| [UTDotNet.Core](https://www.nuget.org/packages/UTDotNet.Core/0.1.2-preview.1) | Platform detection, phone display scaling and shared input contracts. |
| [UTDotNet.Diagnostics](https://www.nuget.org/packages/UTDotNet.Diagnostics/0.1.2-preview.1) | Bounded logging, environment reports and optional DBus inspection. |
| [UTDotNet.UbuntuTouch](https://www.nuget.org/packages/UTDotNet.UbuntuTouch/0.1.2-preview.1) | Managed Maliit sessions and callbacks for a custom UI integration. |
| [UTDotNet.Avalonia](https://www.nuget.org/packages/UTDotNet.Avalonia/0.1.2-preview.1) | Native keyboard integration for Avalonia `TextBox` controls. |

Keep the application's Avalonia backend and theme packages, such as `Avalonia.Desktop` and `Avalonia.Themes.Fluent`, aligned with the tested Avalonia version. The application supplies its display backend, theme, native Linux dependencies and deployment configuration. These NuGet packages contain libraries; the installable Ubuntu Touch demo is a separate [Click package](docs/deployment.md).

## Use the packages in an Avalonia application

This minimal code-only window can be assigned to `desktop.MainWindow` by your existing Avalonia `App`. Keep the application's normal theme setup. `TextInputAdapter` applies input hints and observes diagnostics; `MaliitInputBridge` enables native Maliit input only after Ubuntu Touch is detected.

```csharp
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using UTDotNet.Core;
using UTDotNet.Diagnostics;
using UTDotNet.Avalonia;

public sealed class InputWindow : Window
{
    private readonly TextInputAdapter _adapter;
    private readonly MaliitInputBridge? _bridge;
    private readonly IDisposable? _registration;

    public InputWindow()
    {
        Title = "UTDotNet input";
        Width = 400;
        Height = 720;
        FontSize = 16;

        var email = new TextBox
        {
            PlaceholderText = "Email address",
            MinHeight = 48,
            VerticalAlignment = VerticalAlignment.Top
        };
        Content = new ScrollViewer { Content = email, Margin = new Thickness(16) };

        var log = new DiagnosticLog();
        var request = new InputRequest { InputType = InputType.Email };
        _adapter = new TextInputAdapter(email, "email", request, log);

        if (new PlatformDetector().Detect().MobileEnvironment == LinuxMobileEnvironment.UbuntuTouch)
        {
            _bridge = new MaliitInputBridge(log);
            _registration = _bridge.Register(email, request);
        }

        Closed += (_, _) =>
        {
            _registration?.Dispose();
            _bridge?.Dispose();
            _adapter.Dispose();
        };
    }
}
```

Construct, register and dispose these objects on Avalonia's UI thread. Keep each registration for the field's lifetime. For multiple fields or owned dialogs, share one bridge across the window group and dispose it when the owning group closes. Diagnostic identifiers such as `"email"` should be fixed names. The input observer does not log typed text.

Set the field's `InputRequest.InputType` to `Text`, `Password`, `Email`, `Numeric`, `Phone`, `Url` or `Search`. For passwords, also set the `TextBox.PasswordChar`; for multiline editing, set both `TextBox.AcceptsReturn` and `InputRequest.Multiline`. The application supplies validation. Register field-specific requests; changing an active native registration's input type is not supported in this preview.

The bridge temporarily disables the framework IME on its active registered field and restores it afterwards. Window deactivation requests keyboard dismissal and preserves composition; a later field tap can reopen the keyboard. An application with its own background transition can also `await bridge.DeactivateAsync()` on the UI thread while retaining its registrations.

### Phone layout and startup

In your existing `Program.BuildAvaloniaApp`, enable embedded X11 popups on Ubuntu Touch so dropdown menus remain usable under Lomiri. This uses the application's `Avalonia.Desktop` package and existing `App` class:

```csharp
using Avalonia;
using UTDotNet.Core;

public static class Program
{
    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>().UsePlatformDetect();
        if (new PlatformDetector().Detect().MobileEnvironment == LinuxMobileEnvironment.UbuntuTouch)
            builder = builder.With(new X11PlatformOptions { OverlayPopups = true });
        return builder;
    }
}
```

Apply phone scaling **before Avalonia initializes**. `PhoneDisplayScale.FromGridUnit` converts Lomiri's `GRID_UNIT_PX` into an absolute Avalonia scale (grid / 8). The [demo startup](samples/UTDotNet.InputDemo/Program.cs) sets `AVALONIA_SCREEN_SCALE_FACTORS` and respects existing scale overrides; it also handles development shells that do not inherit the grid value. The library leaves application-wide scaling to the host.

Keep text around 16 logical units and touch targets at least 48 units high, including dropdown items. For forms with fields near the bottom, handle `InputAreaChanged`, calculate the overlap with `MaliitLayout.BottomInset`, reserve that space and bring the focused field into view. Recalculate when the window size changes. The [demo window](samples/UTDotNet.InputDemo/MainWindow.cs) includes keyboard insets, dialogs and rotation handling; the minimal window above only places a field at the top.

`IPlatformInputService.IsAvailable` describes a connected native input session; it can be false before a field gains focus. It does not describe ordinary desktop text input. Show/hide acknowledgements return `RequestedUnconfirmed`, with `KeyboardVisibility.Unknown`; they do not prove the keyboard is visible. See [input integration](docs/input-method.md) for protocol behavior and limitations.

## Build and run

Requires .NET 10 SDK. Avalonia is pinned to 12.1.6; `global.json` accepts installed .NET 10 feature bands. The development host used SDK 10.0.401 and runtime 10.0.12.

```sh
dotnet restore UTDotNet.sln
dotnet build UTDotNet.sln -c Release --no-restore
dotnet test UTDotNet.sln -c Release --no-build
dotnet run --project samples/UTDotNet.InputDemo -c Release --no-build
```

Linux needs a working display server and Avalonia's native dependencies (including fontconfig, X11 libraries for the default backend, EGL/GLES for Wayland). `gdbus`, usually supplied by `libglib2.0-bin`, is optional: missing or denied access is displayed as a diagnostic result. See [Avalonia's Linux guide](https://docs.avaloniaui.net/docs/platform-specific-guides/linux).

```sh
# Explicit Wayland, with no automatic fallback:
dotnet run --project samples/UTDotNet.InputDemo -c Release --no-build -- --wayland
# Console diagnostics without creating a window:
dotnet run --project samples/UTDotNet.InputDemo -c Release --no-build -- --diagnostics
# Brief real-window smoke test; closes automatically:
dotnet run --project samples/UTDotNet.InputDemo -c Release --no-build -- --smoke --screenshot /tmp/utdotnet.png
```

`--no-maliit` disables native input for comparison; `--trace` prints redacted events. `--smoke` checks programmatic focus, text notifications and OSK request results. It does not simulate a touch test or confirm keyboard visibility. Screenshot output is an application render, not a capture of the compositor or keyboard.

## What is included

- `UTDotNet.Core`: conservative, injectable platform detection; input requests, results and an explicit unsupported fallback. Recognizes actual Ubuntu Touch `VARIANT_ID=touch` and system-image metadata. Session and application display backend are kept separate.
- `UTDotNet.Diagnostics`: bounded event history, optional `ILogger`, safe asynchronous DBus inventory and introspection with timeout, cancellation and output limits.
- `UTDotNet.UbuntuTouch`: persistent managed Maliit peer, bidirectional callbacks, bounded initialization, cancellation and focus-session ownership. No Qt dependency.
- `UTDotNet.Avalonia`: observer, opt-in `MaliitInputBridge`, public text-client editing and measured keyboard-inset helper. Commits use Avalonia editing events, preserving selection and undo.
- `UTDotNet.InputDemo`: text, password, number, email, multiline, search, phone and URL fields; a native dialog; OSK request buttons; environment, live events and a manual test matrix. Copy report includes matrix results and notes. Results are kept in memory until copied. Phone scaling follows Lomiri grid/8, controls are at least 48 logical units high, and fields scroll above the reported keyboard area.
- `UTDotNet.Tests`: platform/input tests, mocked DBus command responses, real process failure/timeout/cancellation tests and Avalonia headless editing/lifecycle tests.

No text content is logged by the input observer. Password diagnostics also omit lengths, selection offsets and editing key names. Diagnostic notes are user-authored and included verbatim in copied reports.

For development against the source, reference `src/UTDotNet.Avalonia/UTDotNet.Avalonia.csproj` in place of the NuGet reference. See [NuGet release packaging](docs/nuget-release.md) for local package verification and publishing with GitHub OIDC.

## Device deployment and remaining work

```sh
dotnet publish samples/UTDotNet.InputDemo -c Release -r linux-arm64 \
  --self-contained true -p:PublishTrimmed=false -p:PublishSingleFile=false \
  -o artifacts/linux-arm64
```

See [deployment and confinement](docs/deployment.md), [device record and test matrix](docs/device-testing.md), [platform investigation](docs/ubuntu-touch-investigation.md), [input integration](docs/input-method.md) and [architecture](docs/architecture.md).

Build the installable ARM64 Click with `scripts/build-click.sh`; see the deployment document for prerequisites, installation commands and the bundled review tool's policy-metadata limitation. The tested package uses the standard AppArmor template with no extra policy groups. Rotation, a real kernel suspend/resume cycle, retained composition and input after resume have been verified on this FP5.

Click 0.1.5 passed the user's physical word-selection and Cut/Paste test, including selection of text still in composition. It keeps the original text menus with phone-sized targets. The source adds bounded clipboard writes with exact ownership verification and guards against delayed deletion; these changes are not yet included in the published NuGet 0.1.2-preview.1 packages. Earlier device checks also passed replacement with native keyboard input and copy/paste between fields. See the [test record](docs/device-testing.md#cut-follow-up).

Clipboard exchange with the native Lomiri UI Toolkit Gallery failed in both directions in 0.1.2 and remains unresolved. Handle dragging extended the selection, but its context menu did not reliably return afterward; recovery after dragging was not rechecked in 0.1.5.

Remaining validation includes multiline selection handles, multiple keyboard languages, long sessions and OpenStore review. Packaged DBus inventory reports Unknown because external `gdbus` is denied; native Maliit uses an independent managed connection. Per-span preedit styling and hardware-key redirection are not implemented. This is a developer integration tested on one device, not a production support claim for all Ubuntu Touch images.

MIT license; see [LICENSE](LICENSE).
