# UTDotNet Bridge

A reusable .NET/Avalonia integration library for Ubuntu Touch, with an **experimental native Maliit input bridge** and a diagnostic demo.

Native keyboard opening, composition/commit, backspace, password, numeric/email modes, multiline editing and hide/reopen have been exercised on a Fairphone 5 running Ubuntu Touch 24.04-2.2 through X11/XWayland. A Click build also runs with the standard AppArmor profile and native Maliit input. Tests use injected touchscreen events, physical orientation changes and system-compositor captures. Native Wayland remains blocked; see the [validation record](docs/device-testing.md) for precise scope.

## Install from NuGet

The published preview is **0.1.6-preview.1**, targeting **.NET 10** and tested with **Avalonia 12.1.6**. Run this in your Avalonia application's project directory:

```sh
dotnet add package UTDotNet.Avalonia --version 0.1.6-preview.1
```

Or add the reference to the application's `.csproj`:

```xml
<ItemGroup>
  <PackageReference Include="UTDotNet.Avalonia" Version="0.1.6-preview.1" />
</ItemGroup>
```

`UTDotNet.Avalonia` brings in the other three libraries as dependencies. For applications that only need a lower layer, install the corresponding package from this table using the same version:

| Package | Use it for |
| --- | --- |
| [UTDotNet.Core](https://www.nuget.org/packages/UTDotNet.Core/0.1.6-preview.1) | Platform detection, phone scaling and input/clipboard contracts. |
| [UTDotNet.Diagnostics](https://www.nuget.org/packages/UTDotNet.Diagnostics/0.1.6-preview.1) | Bounded logging, environment reports and optional DBus inspection. |
| [UTDotNet.UbuntuTouch](https://www.nuget.org/packages/UTDotNet.UbuntuTouch/0.1.6-preview.1) | Managed Maliit sessions and native Content Hub text clipboard. |
| [UTDotNet.Avalonia](https://www.nuget.org/packages/UTDotNet.Avalonia/0.1.6-preview.1) | Native keyboard integration and guarded Copy/Cut/Paste for Avalonia `TextBox` controls. |

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
using UTDotNet.UbuntuTouch;

public sealed class InputWindow : Window
{
    private readonly TextInputAdapter _adapter;
    private readonly MaliitInputBridge? _bridge;
    private readonly IDisposable? _registration;
    private TextClipboardBridge? _clipboard;

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
            Opened += (_, _) =>
            {
                if (TryGetPlatformHandle()?.HandleDescriptor == "XID")
                    _clipboard = new TextClipboardBridge(email, log, new ContentHubClipboard(log));
            };
        }

        Closed += (_, _) =>
        {
            _clipboard?.Dispose();
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

Touching a field finalizes pending native composition before selection, and a closing menu returning focus does not dismiss its input context.

### Use the native Ubuntu Touch clipboard

Starting with **0.1.6-preview.1**, pass a `ContentHubClipboard` as the third argument to `TextClipboardBridge`, as in the window above. This connects the field's existing **Copy, Cut and Paste** commands to Lomiri Content Hub, allowing text exchange with native Qt apps and Morph. Keep one `TextClipboardBridge` per field and dispose it with the field; one `ContentHubClipboard` can be shared by all fields. Enable this integration on Ubuntu Touch; ordinary desktop applications retain Avalonia's clipboard.

For an application action such as a Copy report button, use the same provider directly. Call it in response to an explicit user action while your app is active:

```csharp
using System.Threading;
using UTDotNet.Diagnostics;
using UTDotNet.UbuntuTouch;

// Inside your Ubuntu Touch Copy button handler:
var nativeClipboard = new ContentHubClipboard(new DiagnosticLog());
bool copied = await nativeClipboard.WriteTextAsync("Report text", CancellationToken.None);
// Show success only when copied is true; false means the service did not confirm the write.
```

`ReadTextAsync(cancellationToken)` returns the current plain text, or `null` when unavailable, invalid or non-text. Prefer `TextClipboardBridge` for editable fields: it waits for an accepted write before Cut deletes, preserves undo and field constraints, and rejects delayed Cut/Paste results if the text, selection or focus changed. Password/sensitive fields cannot Copy/Cut but can Paste. Failed native reads never fall back to an older X11 clipboard value.

Each operation has a two-second deadline and supports cancellation. The managed provider needs no Qt dependency, external helper or additional AppArmor policy group on the tested Click profile. It supports UTF-8 text, accents, emoji and line breaks, with a 4 MiB payload limit. Images, files and rich-text preservation are not supported; persistence across process exit, reboot or service restart has not been tested.

The existing two-argument `TextClipboardBridge` and `ClipboardTextWriter.TryWriteAsync` remain available for bounded X11 writes. They do not enable Content Hub interoperability. See the [native clipboard guide](docs/input-method.md#native-content-hub-clipboard-016-preview1) for the protocol and lifecycle details.

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

Requires .NET 10 SDK. Avalonia is pinned to 12.1.6; `global.json` accepts installed .NET 10 feature bands. The development host used SDK 10.0.401 and runtime 10.0.12. On Linux, the native clipboard protocol tests also require `dbus-daemon` and permission to open local sockets; they start an isolated test bus.

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

- `UTDotNet.Core`: conservative, injectable platform detection; input requests, results, the `ITextClipboard` contract and an explicit unsupported fallback. Recognizes actual Ubuntu Touch `VARIANT_ID=touch` and system-image metadata. Session and application display backend are kept separate.
- `UTDotNet.Diagnostics`: bounded event history, optional `ILogger`, safe asynchronous DBus inventory and introspection with timeout, cancellation and output limits.
- `UTDotNet.UbuntuTouch`: persistent managed Maliit peer, bidirectional callbacks, bounded initialization, cancellation and focus-session ownership; `ContentHubClipboard` for native plain-text exchange. No Qt dependency.
- `UTDotNet.Avalonia`: observer, opt-in `MaliitInputBridge`, `TextClipboardBridge`, `ClipboardTextWriter`, public text-client editing and measured keyboard-inset helper. Native Copy/Cut/Paste preserves editing constraints and undo behavior.
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

For moving this integration into Avalonia itself and removing the bridge dependency, see the [Avalonia source integration handoff (Italian)](docs/avalonia-upstream-handoff.it.md).

Build the installable ARM64 Click with `scripts/build-click.sh`; see the deployment document for prerequisites, installation commands and the bundled review tool's policy-metadata limitation. The tested package uses the standard AppArmor template with no extra policy groups. Rotation, a real kernel suspend/resume cycle, retained composition and input after resume have been verified on this FP5.

Click 0.1.5 passed the user's physical word-selection and Cut/Paste test, including selection of text still in composition. Its fixes remain included, with the original text menus and phone-sized targets in the demo; menu sizing remains application UI configuration. Earlier device checks also passed replacement with native keyboard input and copy/paste between fields. See the [test record](docs/device-testing.md#cut-follow-up).

**NuGet 0.1.6-preview.1 / Click 0.1.6:** `ContentHubClipboard` and the three-argument `TextClipboardBridge` provide native Copy/Cut/Paste. Bidirectional exchange passed on the FP5 with a native Qt control and Morph, including accented text, emoji and multiline paste. The user confirmed comfortable Cut/Paste; the original menu also returned after a selection-handle drag. All 93 tests pass. Follow the [native clipboard integration guide](docs/input-method.md#native-content-hub-clipboard-016-preview1). The demo enables the native path by default on Ubuntu Touch; `--no-native-clipboard` retains the earlier X11 path for comparison.

Remaining validation includes multiline selection handles, multiple keyboard languages, long sessions and OpenStore review. Packaged DBus inventory reports Unknown because external `gdbus` is denied; native Maliit uses an independent managed connection. Per-span preedit styling and hardware-key redirection are not implemented. This is a developer integration tested on one device, not a production support claim for all Ubuntu Touch images.

MIT license; see [LICENSE](LICENSE).
