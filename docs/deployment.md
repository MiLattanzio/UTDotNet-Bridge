# ARM64 deployment investigation

## Verified execution path

On 2026-11-21 a self-contained `linux-arm64` publish (about 108 MiB, untrimmed, directory deployment) was copied with ADB to `/home/phablet/utdotnet-phase1` on a Fairphone 5. The .NET 10.0.12 runtime executed and the demo opened through `DISPLAY=:0`, showing an X11 native handle. An Xwayland process was observed after the run. The stock device had no `dotnet` executable on PATH.

The initial run was an **unconfined developer launch**. The subsequent Click installation described below also ran under the standard AppArmor profile. Neither establishes support for arbitrary ARM64 Ubuntu Touch images, rootfs versions or GPUs.

Observed device dependencies: glibc 2.39, ICU 74.2, OpenSSL 3.0.13, libwayland-client 1.22.0, EGL/GLES libraries, XWayland, gdbus. Startup produced gralloc/Zink/GLX warnings but the X11 smoke completed. Basic text rendering and touchscreen interaction were verified; acceleration and broader graphics behavior remain unverified. The Click contains the same self-contained directory layout; trimming/AOT and single-file extraction remain disabled.

## Reproduce on this device

Build using the README publish command, then, from the repository root:

```sh
adb -s 49f88e70 push artifacts/linux-arm64/. /home/phablet/utdotnet-phase1/
adb -s 49f88e70 shell 'cd /home/phablet/utdotnet-phase1 && chmod +x UTDotNet.InputDemo && XDG_RUNTIME_DIR=/run/user/32011 DBUS_SESSION_BUS_ADDRESS=unix:path=/run/user/32011/bus DISPLAY=:0 XDG_SESSION_TYPE=mir XDG_CURRENT_DESKTOP=Lomiri QT_IM_MODULE=maliit ./UTDotNet.InputDemo'
```

Serial, UID/runtime directory and display above are **observed values for this Fairphone only**. Determine them again for another device or login session. ADB shells did not inherit Lomiri's session variables. Do not launch the application as root. Add `--diagnostics` for a console report, `--smoke` for an automatically closing launch test, `--trace` for redacted events, or `--no-maliit` for the framework-only baseline. The demo reads `GRID_UNIT_PX` or `device-info get GridUnit` before backend initialization; on this device grid 24 gives absolute Avalonia scale 3. Explicit Avalonia scale overrides take precedence. No system package installation or rootfs modification is necessary for this developer test.

## Native Wayland blocker

`--wayland` currently fails before window creation: `Required wayland global xdg_wm_base (>=3) not found`. WAYLAND_DEBUG showed `xdg_wm_base` v1 and no text-input manager. Fixing/negotiating the shell protocol is a separate task and still would not create missing compositor text-input support. Do not lower the required version without auditing every request made by Avalonia's backend.

## Click build and installation

`packaging/click/` defines `utdotnet.inputdemo`, version `0.1.1`, architecture `arm64`, framework `ubuntu-touch-24.04-2.x`, policy version `2404.2`. The maintainer is Mi Lattanzio, as supplied for this build. The package is approximately 40.4 MiB and includes .NET; no system runtime installation is needed.

```sh
# Requires .NET SDK, Docker, Python 3 and desktop-file-validate.
# Uses the existing Clickable 24.04-2.x ARM64 build image.
scripts/build-click.sh
```

Output: `artifacts/click/utdotnet.inputdemo_0.1.1_arm64.click`. The script prints its SHA-256 and saves the review output to `artifacts/click/review.txt`. `CLICK_BUILD_IMAGE` selects a different compatible image. The tested image digest is `sha256:0a69f2c6c52a9d33516ce210eb5cb374ca93905985d62820d9be5a351647b5ba`.

**Validator limitation:** the bundled `click-reviewers-tools 0.85~noble` recognizes the framework mapping but its static `APPARMOR_POLICIES` table lacks `2404.2`. It reports `security:policy_version_exists` and `security:policy_groups_exists`, returning 2 after the package has been built. This is retained as a failed review, not silently treated as a pass. The actual policy templates are installed in both container and device. The phone's Click service generated and enforced the profile successfully. `desktop-file-validate` also emits a nonfatal warning about the package-relative SVG icon; the Click hook resolves it to an absolute installed path. OpenStore review/publication has not been performed.

Install and launch through Lomiri (substitute the observed serial and runtime directory for another device):

```sh
adb -s 49f88e70 push artifacts/click/utdotnet.inputdemo_0.1.1_arm64.click /home/phablet/
adb -s 49f88e70 shell 'gdbus call --system --dest com.lomiri.click --object-path /com/lomiri/click --method com.lomiri.click.Install /home/phablet/utdotnet.inputdemo_0.1.1_arm64.click'
adb -s 49f88e70 shell 'XDG_RUNTIME_DIR=/run/user/32011 DBUS_SESSION_BUS_ADDRESS=unix:path=/run/user/32011/bus lomiri-app-launch utdotnet.inputdemo_inputdemo_0.1.1'
```

The application also appears as **UTDotNet Input** in the launcher. The desktop entry follows the [UBports Click format](https://docs.ubports.com/en/latest/appdev/platform/click.html), with `X-Ubuntu-XMir-Enable=true` and no orientation restriction. DISPLAY, runtime directory, TMPDIR and grid scale come from Lomiri; no device UID or display number is embedded in the package.

The standard `ubuntu-sdk` AppArmor template is used with **no additional policy groups or custom permissions**. `/proc/<pid>/attr/current` confirmed `utdotnet.inputdemo_inputdemo_0.1.0 (enforce)`. Runtime/JIT loading, fonts, X11 window creation, the Maliit peer and bidirectional input were exercised under that profile. Two packaging adaptations were necessary:

- The shell launcher uses builtins only; external `dirname` and `mkdir` were denied. Lomiri already creates the package-specific temporary directory under `$XDG_RUNTIME_DIR/confined/`.
- The launcher sets `RESOURCE_NAME` from `APP_ID`. [Avalonia 12.1.6's X11 window](https://github.com/AvaloniaUI/Avalonia/blob/12.1.6/src/Avalonia.X11/X11Window.cs) otherwise calls `Process.GetCurrentProcess().ProcessName`, whose procfs access fails under this profile. Supplying the supported identity avoids that access without changing framework code or permissions.

The package enables the redacted `--trace` log in the app's systemd user unit and disables .NET diagnostic IPC. Typed text remains excluded. Optional DBus inventory still uses external `gdbus`, which is denied under confinement; the UI correctly reports Unknown. Maliit itself uses the managed connection and works independently. Future packaged inventory should use an in-process bus client. See [UBports writable directories](https://docs.ubports.com/en/latest/appdev/guides/writeable-dirs.html) and [policy groups](https://docs.ubports.com/en/latest/appdev/platform/apparmor.html).

### Checks menu correction (0.1.1)

The user reported that the Checks dropdowns did not open in 0.1.0; attempts coincided with repeated owner deactivation/reactivation. The demo now requests `X11PlatformOptions.OverlayPopups = true` only when Ubuntu Touch is detected. This [supported Avalonia option](https://github.com/AvaloniaUI/Avalonia/blob/12.1.6/src/Avalonia.X11/X11Platform.cs) hosts dropdowns in the application surface instead of separate native X11 popup windows. Desktop configuration is unchanged. ComboBox items have a minimum height of 48 logical units, and fixed test identifiers/result choices are logged on menu opening, closing and selection; notes are not logged.

Use the currently serviced .NET 10 runtime for distribution. The tested host/runtime patch is recorded for reproducibility, not recommended as the latest available patch. [.NET Ubuntu dependencies and supported distributions](https://learn.microsoft.com/en-us/dotnet/core/install/linux-ubuntu-install) describe Ubuntu; Ubuntu Touch itself is not thereby a certified .NET platform.

## Device interaction and capture

`scripts/device-tap.py` supports one tap, a bounded hold, or `--swipe-to X Y`. It uses the existing phablet/android_input permissions and refuses to overlap active contacts. Verify the touchscreen event device and axis ranges for each device; FP5 uses `/dev/input/event4`, X=0..1224, Y=0..2700. No root helper or permission changes are needed. Touches in text fields can select text; the form can be scrolled using its side margin.

Capture the **system compositor** with `mirscreencast -m /run/mir_socket -n 1 --stdout`. On this FP5 the output is raw RGBA 1224×2700. `/run/user/32011/mir_socket` captures a nested surface arrangement and does not reproduce the final keyboard/shell placement; do not use it as visibility evidence. Device status-bar coordinates differ from the app's X11 coordinates by the shell inset.
