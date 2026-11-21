# Ubuntu Touch investigation

Investigation started 2026-11-21. This document distinguishes source inspection from execution on a device.

## Source findings

- .NET 10 is the installed LTS (SDK 10.0.401, runtime 10.0.12). The [support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core) lists support through November 2028; deployment should use current servicing patches.
- Avalonia is pinned to 12.1.6, verified in NuGet. Its [desktop builder](https://github.com/AvaloniaUI/Avalonia/blob/12.1.6/src/Avalonia.Desktop/AppBuilderDesktopExtensions.cs) selects X11 on Linux. A Wayland session variable does not establish the application's backend.
- The [X11 DBus IME factory](https://github.com/AvaloniaUI/Avalonia/blob/12.1.6/src/Avalonia.FreeDesktop/DBusIme/X11DBusImeHelper.cs) recognizes IBus and Fcitx, not Maliit. Setting QT_IM_MODULE=maliit does not load Qt's plugin into Avalonia.
- Avalonia 12.1.6 also has a separate Wayland backend. Its [text input broker](https://github.com/AvaloniaUI/Avalonia/blob/12.1.6/src/Avalonia.Wayland/WindowImpl.TextInput.cs) uses zwp_text_input_v3 if the compositor advertises it. This must be checked on the target compositor.
- Maliit upstream [address discovery](https://github.com/maliit/framework/blob/master/connection/inputcontextdbusaddress.cpp) reads property `address` from interface `org.maliit.Server.Address`, object `/org/maliit/server/address`, service `org.maliit.server` on the session bus.
- Its [connection implementation](https://github.com/maliit/framework/blob/master/connection/dbusserverconnection.cpp) then uses a **peer DBus connection**, exports `/com/meego/inputmethod/inputcontext`, and calls `/com/meego/inputmethod/uiserver1`. Session bus discovery is not a complete input transport.
- The [server interface](https://github.com/maliit/framework/blob/master/dbus_interfaces/minputmethodserver1interface.xml) includes context activation, widget state, show/hide and reset. The [client interface](https://github.com/maliit/framework/blob/master/dbus_interfaces/minputmethodcontext1interface.xml) includes commit, preedit, key events, selection, activation loss and input method area. A one-shot show call cannot supply this lifecycle.

Upstream master links describe inspected source, not proof that every Ubuntu Touch image ships that revision. Device package versions and runtime introspection must be recorded separately.

## Device observations, 2026-11-21

Fairphone 5 (`FP5`), Ubuntu Touch 24.04-2.2 / build 3, channel `24.04-2.x/arm64/android9plus/stable`. The device reports Ubuntu 24.04.5 in os-release plus `VARIANT_ID=touch`. System-image configuration uses colon-separated keys, with `base: system-image.ubports.com`. Both formats are covered by detector tests.

Lomiri's environment reports `XDG_SESSION_TYPE=mir`, `XDG_CURRENT_DESKTOP=Lomiri`, `XDG_SESSION_DESKTOP=ubuntu-touch`, `WAYLAND_DISPLAY=wayland-0`, `QT_QPA_PLATFORM=mirserver`, `QT_IM_MODULE=maliit`. These belong to the shell, not necessarily the application.

Installed components: Maliit `2.3.0-10+0~20260609102150.8+ubports24.04.2~1.gbp6056d5`; Lomiri `0.5.0+0~20260910085057.5+ubports24.04.2~1.gbp0bd07c`; lomiri-keyboard `1.0.3+0~20260911162321.9+ubports24.04.2~1.gbp85e1a7`.

Runtime introspection confirmed the `org.maliit.Server.Address.address` property and an endpoint at `/run/user/32011/maliit-server`. GLib peer introspection also confirmed the server interface. The implemented native peer was then exercised with real OSK composition, commit, editing, mode changes and show/hide. The optional CLI's failed peer-introspection attempt and connection initialization workaround are documented in [input-method.md](input-method.md).

The full initial Wayland registry round trip advertised: android_wlegl v2, qt_windowmanager v1, wl_compositor v4, wl_subcompositor v1, wl_seat v6, wl_output v3, wl_data_device_manager v3, wl_shell v1, zxdg_shell_v6 v1, xdg_wm_base v1, wl_shm v1. No text-input manager appeared. The published Avalonia 12.1.6 demo failed at native Wayland initialization because it requires xdg_wm_base >=3. These are measured properties of this image, not all Ubuntu Touch devices.

The same self-contained .NET 10 ARM64 build **did launch using DISPLAY=:0**, produced an XID, passed its programmatic smoke checks and safely introspected Maliit. An Xwayland process was observed after launch. This established the developer reproduction path. A later Click build also launched under the standard AppArmor profile and exercised native input, physical rotation and deep suspend/resume; see the [validation record](device-testing.md). Packaged DBus inventory remains unavailable because its external helper is denied.

## Initial hypothesis

A mismatch between the selected Avalonia input transport and the system's Maliit transport can explain focus working while the OSK does not appear. A same-session comparison with `--no-maliit` now reproduces focus without keyboard activation using an injected touchscreen tap; enabling the bridge opens the OSK and delivers edits. Rendering, confinement, focus and lifecycle may contribute independently. No Uno application or backend has been measured, so no conclusion about Uno is warranted.

## Implementation boundary

The observer and explicit desktop fallback remain available. The opt-in native bridge owns registered fields while focused, restoring their original framework IME setting afterwards. Direct native requests return RequestedUnconfirmed; visibility remains Unknown. No global IME replacement or built-in keyboard is installed.

Lomiri reports `GRID_UNIT_PX=24`; the demo uses absolute Avalonia scale 3 and initializes the window from screen bounds. This fixes the initially tiny controls and an observed negative X11 window origin after desktop-size centering. DejaVu Sans is used because the shipped Ubuntu variable font rendered normal-weight text unexpectedly bold in this Avalonia/device combination.
