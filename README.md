# UTDotNet Bridge

A reusable .NET/Avalonia integration library for Ubuntu Touch, with an **experimental native Maliit input bridge** and a diagnostic demo.

Native keyboard opening, composition/commit, backspace, password, numeric/email modes, multiline editing and hide/reopen have been exercised on a Fairphone 5 running Ubuntu Touch 24.04-2.2 through X11/XWayland. A Click build also runs with the standard AppArmor profile and native Maliit input. Tests use injected touchscreen events, physical orientation changes and system-compositor captures. Native Wayland remains blocked; see the [validation record](docs/device-testing.md) for precise scope.

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

## Use the adapter in another application

Install the preview package (which brings in the other three libraries):

```sh
dotnet add package UTDotNet.Avalonia --version 0.1.2-preview.1
```

Alternatively, reference `src/UTDotNet.Avalonia/UTDotNet.Avalonia.csproj` during development. See [NuGet release packaging](docs/nuget-release.md) for building packages locally and publishing with GitHub OIDC.

```csharp
var log = new DiagnosticLog();
var adapter = new TextInputAdapter(textBox, "email",
    new InputRequest { InputType = InputType.Email }, log);
// On Ubuntu Touch only, opt the field into native input:
MaliitInputBridge? bridge = null;
IDisposable? registration = null;
if (new PlatformDetector().Detect().MobileEnvironment == LinuxMobileEnvironment.UbuntuTouch)
{
    bridge = new MaliitInputBridge(log);
    registration = bridge.Register(textBox, adapter.Request);
}
// Keep these objects for the view lifetime; dispose on the UI thread:
registration?.Dispose();
bridge?.Dispose();
adapter.Dispose();
```

Namespaces: `UTDotNet.Core`, `UTDotNet.Diagnostics`, `UTDotNet.Avalonia`. Calls belong on Avalonia's UI thread. Identifiers should be fixed field names, not user-entered text. The application remains responsible for `PasswordChar`, `AcceptsReturn`, validation and layout; the adapter supplies hints and observes events. Use one bridge per application/window group and retain each registration. The bridge temporarily disables the framework IME only on its active registered field and restores it afterwards. Use `MaliitLayout.BottomInset` with `InputAreaChanged` for keyboard overlap; the demo shows how. Applications own display scaling and validation.

`IPlatformInputService.IsAvailable` refers to direct keyboard control, not the availability of ordinary text input. The desktop fallback returns `Unsupported`. The native bridge returns `RequestedUnconfirmed` for show/hide acknowledgements; visibility remains `Unknown`. Register field-specific hints: active type mutation is unsupported, and show requests with different hints are explicitly rejected.

## Device deployment and remaining work

```sh
dotnet publish samples/UTDotNet.InputDemo -c Release -r linux-arm64 \
  --self-contained true -p:PublishTrimmed=false -p:PublishSingleFile=false \
  -o artifacts/linux-arm64
```

See [deployment and confinement](docs/deployment.md), [device record and test matrix](docs/device-testing.md), [platform investigation](docs/ubuntu-touch-investigation.md), [input integration](docs/input-method.md) and [architecture](docs/architecture.md).

Build the installable ARM64 Click with `scripts/build-click.sh`; see the deployment document for prerequisites, installation commands and the bundled review tool's policy-metadata limitation. The tested package uses the standard AppArmor template with no extra policy groups. Rotation, a real kernel suspend/resume cycle, retained composition and input after resume have been verified on this FP5.

Remaining validation includes clipboard interoperability, touch selection handles, multiple keyboard languages, long sessions and OpenStore review. Packaged DBus inventory reports Unknown because external `gdbus` is denied; native Maliit uses an independent managed connection. Per-span preedit styling and hardware-key redirection are not implemented. This is a developer integration tested on one device, not a production support claim for all Ubuntu Touch images.

MIT license; see [LICENSE](LICENSE).
