# Passaggio di consegne per applicazioni Avalonia su Ubuntu Touch

> Questa consegna conserva la base verificata con **0.1.5-preview.1**. Per una nuova integrazione con gli appunti nativi usare **0.1.6-preview.1** e seguire il [README aggiornato](../README.md#use-the-native-ubuntu-touch-clipboard). Le limitazioni Qt indicate sotto si riferiscono alla versione precedente.

Questo documento è destinato a un'altra istanza di Codex incaricata di creare un'applicazione Ubuntu Touch usando **UTDotNet.Avalonia da nuget.org**. Contiene una base compilabile, le regole di integrazione e il percorso di distribuzione. Il progetto dell'app deve essere autonomo: usa i pacchetti pubblicati e consulta il repository della libreria come riferimento.

## Istruzione per l'istanza che riceve il lavoro

Realizza l'app richiesta dall'utente partendo dalla configurazione seguente. Mantieni il normale input Avalonia sul desktop e attiva Maliit solo quando viene rilevato Ubuntu Touch. Integra scala del telefono, tastiera, scorrimento, ciclo di vita e packaging Click. Prima di dichiarare l'app pronta, compila e pubblica per l'architettura scelta, poi distingui i risultati sul computer da quelli verificati sul telefono. Se il dispositivo non è disponibile, consegna l'artefatto e la procedura di prova indicando quali verifiche restano da eseguire.

## Versioni e perimetro

Base verificata il **21 novembre 2026**:

| Componente | Versione o scelta |
| --- | --- |
| Pacchetto principale | `UTDotNet.Avalonia` **`0.1.5-preview.1`** |
| Framework dell'app | `net10.0`, SDK .NET 10 |
| Avalonia e pacchetti backend/tema | **`12.1.6`**, tutti allineati |
| Percorso sul telefono | X11 tramite XWayland, shell Lomiri, tastiera nativa Maliit |
| Dispositivo di riferimento | Fairphone 5, Ubuntu Touch **24.04-2.2**, ARM64 |
| Pubblicazione .NET | `linux-arm64`, self-contained, senza trimming, AOT o single-file |
| Click di riferimento | framework `ubuntu-touch-24.04-2.x`, policy AppArmor `2404.2` |

La versione è presente nell'[indice pubblico NuGet](https://api.nuget.org/v3-flatcontainer/utdotnet.avalonia/index.json) e nella [pagina del pacchetto](https://www.nuget.org/packages/UTDotNet.Avalonia/0.1.5-preview.1). Blocca questa combinazione per riprodurre l'integrazione; un aggiornamento richiede nuove verifiche. Usa un runtime .NET 10 con aggiornamenti di manutenzione disponibili al momento della distribuzione.

Un solo riferimento a `UTDotNet.Avalonia` porta anche `UTDotNet.Core`, `UTDotNet.Diagnostics` e `UTDotNet.UbuntuTouch`. Il pacchetto fornisce integrazione di input, diagnostica e helper di layout; l'app fornisce backend Avalonia, tema, interfaccia, validazione, dipendenze Linux e file Click. `PhoneTextMenu`, `MainWindow` della demo, script Click e opzioni CLI della demo non sono API distribuite nel NuGet.

La verifica sul Fairphone riguarda questa immagine e questo dispositivo. Wayland nativo è bloccato lì da `xdg_wm_base` v1 contro v3 richiesta, e il compositor osservato non espone text-input-v3. Parti da `UsePlatformDetect()` e dal percorso X11; la presenza di una sessione Mir/Wayland non identifica da sola il backend della finestra. Un handle con descrittore `XID` identifica il percorso X11. Vedi il [registro delle prove](https://github.com/MiLattanzio/UTDotNet-Bridge/blob/nuget-v0.1.5-preview.1/docs/device-testing.md) e la [documentazione Linux di Avalonia](https://docs.avaloniaui.net/docs/platform-specific-guides/linux).

## Creare un progetto indipendente

Crea una directory `TouchApp` fuori dal repository della libreria. I nomi e l'identità Click qui sotto sono segnaposto da adattare all'app reale. I blocchi indicati con un nome di file sono completi; non servono template Avalonia o file XAML.

`TouchApp.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
    <PublishTrimmed>false</PublishTrimmed>
    <PublishSingleFile>false</PublishSingleFile>
    <PublishAot>false</PublishAot>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="UTDotNet.Avalonia" Version="0.1.5-preview.1" />
    <PackageReference Include="Avalonia.Desktop" Version="12.1.6" />
    <PackageReference Include="Avalonia.Themes.Fluent" Version="12.1.6" />
  </ItemGroup>
</Project>
```

`NuGet.Config` per questo nuovo progetto, con il solo feed pubblico:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
```

`global.json`:

```json
{
  "sdk": { "version": "10.0.100", "rollForward": "latestFeature" }
}
```

Per un'app esistente, aggiungi `dotnet add package UTDotNet.Avalonia --version 0.1.5-preview.1`, allinea i pacchetti Avalonia e integra i comportamenti sotto nel suo avvio e nelle sue viste. Se usa Central Package Management, dichiara le versioni in `Directory.Packages.props` secondo le convenzioni del progetto; conserva i feed necessari all'app.

## Avvio e scala prima dell'inizializzazione

`Program.cs`:

```csharp
using System.Globalization;
using Avalonia;
using UTDotNet.Core;
using UTDotNet.Diagnostics;

namespace TouchApp;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        ConfigurePhoneScale();
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>().UsePlatformDetect();
        if (new PlatformDetector().Detect().MobileEnvironment == LinuxMobileEnvironment.UbuntuTouch)
            builder = builder.With(new X11PlatformOptions { OverlayPopups = true });
        return builder;
    }

    private static void ConfigurePhoneScale()
    {
        var environment = new PlatformDetector().Detect().MobileEnvironment;
        if (environment != LinuxMobileEnvironment.UbuntuTouch ||
            Environment.GetEnvironmentVariable("AVALONIA_SCREEN_SCALE_FACTORS") is not null ||
            Environment.GetEnvironmentVariable("AVALONIA_GLOBAL_SCALE_FACTOR") is not null)
            return;

        var grid = Environment.GetEnvironmentVariable("GRID_UNIT_PX");
        if (grid is null)
        {
            // Utile in una shell di sviluppo; nel Click l'ambiente arriva da Lomiri.
            var result = new CommandRunner(TimeSpan.FromSeconds(2))
                .RunAsync("device-info", ["get", "GridUnit"], CancellationToken.None)
                .GetAwaiter().GetResult();
            if (result.Succeeded) grid = result.Output.Trim();
        }
        if (PhoneDisplayScale.FromGridUnit(grid, environment) is { } scale)
            Environment.SetEnvironmentVariable("AVALONIA_SCREEN_SCALE_FACTORS",
                scale.ToString(CultureInfo.InvariantCulture));
    }
}
```

`PhoneDisplayScale.FromGridUnit` calcola una scala assoluta `GRID_UNIT_PX / 8`: sul Fairphone il valore 24 produce 3. Non moltiplicarla di nuovo per la scala del monitor e non fissare 3 su tutti i telefoni. La libreria non imposta la scala globale da sola. `OverlayPopups = true` ospita i menu nella finestra, evitando i problemi di focus osservati con popup X11 separati sotto Lomiri.

`App.cs`:

```csharp
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;

namespace TouchApp;

public sealed class App : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }
}
```

## Finestra con input nativo e spazio per la tastiera

`MainWindow.cs`:

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using UTDotNet.Avalonia;
using UTDotNet.Core;
using UTDotNet.Diagnostics;
using UTDotNet.UbuntuTouch;

namespace TouchApp;

public sealed class MainWindow : Window
{
    private readonly DiagnosticLog _log = new();
    private readonly List<TextInputAdapter> _adapters = [];
    private readonly List<IDisposable> _registrations = [];
    private readonly List<TextClipboardBridge> _clipboards = [];
    private readonly List<TextBox> _fields = [];
    private readonly MaliitInputBridge? _bridge;
    private readonly Border _root;
    private InputArea _keyboardArea;
    private TextBox? _focused;
    private bool _closed;

    public MainWindow()
    {
        Title = "TouchApp";
        Width = 400;
        Height = 720;
        FontSize = 16;
        var phone = new PlatformDetector().Detect().MobileEnvironment == LinuxMobileEnvironment.UbuntuTouch;
        if (phone)
        {
            WindowState = WindowState.Maximized;
            _bridge = new MaliitInputBridge(_log);
            _bridge.InputAreaChanged += OnInputAreaChanged;
        }

        var form = new StackPanel { Spacing = 12 };
        AddField(form, "text", "Testo", new() { AutoCorrect = true, AutoCapitalization = true });
        AddField(form, "email", "Email", new() { InputType = InputType.Email });
        AddField(form, "password", "Password", new() { InputType = InputType.Password });
        AddField(form, "number", "Numero", new() { InputType = InputType.Numeric });
        AddField(form, "phone", "Telefono", new() { InputType = InputType.Phone });
        AddField(form, "url", "Indirizzo web", new() { InputType = InputType.Url });
        AddField(form, "search", "Ricerca", new() { InputType = InputType.Search });
        AddField(form, "notes", "Note", new() { Multiline = true, AutoCorrect = true });
        _root = new Border
        {
            Padding = new Thickness(16),
            Child = new ScrollViewer { Content = form }
        };
        Content = _root;

        Opened += (_, _) =>
        {
            if (phone && TryGetPlatformHandle()?.HandleDescriptor == "XID")
                foreach (var field in _fields)
                    _clipboards.Add(new TextClipboardBridge(field, _log));
        };
        SizeChanged += (_, _) => ApplyKeyboardInset();
        PositionChanged += (_, _) => ApplyKeyboardInset();
        Closed += (_, _) =>
        {
            _closed = true;
            if (_bridge is not null) _bridge.InputAreaChanged -= OnInputAreaChanged;
            foreach (var clipboard in _clipboards) clipboard.Dispose();
            foreach (var registration in _registrations) registration.Dispose();
            _bridge?.Dispose();
            foreach (var adapter in _adapters) adapter.Dispose();
        };
    }

    private void AddField(StackPanel form, string id, string label, InputRequest request)
    {
        var field = new TextBox
        {
            PlaceholderText = label,
            MinHeight = request.Multiline ? 112 : 48,
            AcceptsReturn = request.Multiline,
            TextWrapping = request.Multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            PasswordChar = request.InputType == InputType.Password ? '●' : '\0'
        };
        _fields.Add(field);
        _adapters.Add(new TextInputAdapter(field, id, request, _log));
        if (_bridge is not null) _registrations.Add(_bridge.Register(field, request));
        field.GotFocus += (_, _) =>
        {
            _focused = field;
            ScrollToFocusedField();
        };
        form.Children.Add(new TextBlock { Text = label });
        form.Children.Add(field);
    }

    private void OnInputAreaChanged(Window window, InputArea area)
    {
        if (window != this || _closed) return;
        _keyboardArea = area;
        ApplyKeyboardInset();
    }

    private void ApplyKeyboardInset()
    {
        if (_closed) return;
        var inset = 0d;
        if (_keyboardArea.Width > 0 && _keyboardArea.Height > 0)
        {
            var viewport = new PixelRect(this.PointToScreen(default),
                PixelSize.FromSize(ClientSize, RenderScaling));
            inset = MaliitLayout.BottomInset(_keyboardArea, viewport, RenderScaling);
        }
        _root.Margin = new Thickness(0, 0, 0, inset);
        ScrollToFocusedField();
    }

    private void ScrollToFocusedField() => Dispatcher.UIThread.Post(() =>
    {
        if (!_closed && _focused is { IsFocused: true } field) field.BringIntoView();
    }, DispatcherPriority.Loaded);
}
```

L'area Maliit è espressa in pixel di schermo: usa origine globale, dimensione del client convertita in pixel e `RenderScaling`, come nell'esempio. `BottomInset` restituisce unità logiche e considera solo l'intersezione: evita di sottrarre due volte la tastiera quando il compositor ha già ridimensionato la finestra. Ricalcola anche dopo la rotazione; mantieni uno `ScrollViewer` realmente vincolato dall'altezza disponibile. In orizzontale riduci elementi accessori anziché imporre un'altezza minima che copra i campi.

Il campione mantiene i campi per tutta la vita della finestra. Per pagine dinamiche, libera adapter, registrazioni e clipboard quando la pagina viene rimossa e ricreali quando ritorna. Non registrare due volte lo stesso `TextBox`. Per finestre e dialoghi appartenenti allo stesso gruppo, condividi un bridge, applica l'inset alla finestra indicata dall'evento e libera il bridge solo alla chiusura del gruppo; il dialogo libera soltanto le proprie risorse.

## Regole delle API da rispettare

| API | Responsabilità e uso |
| --- | --- |
| `PlatformDetector().Detect()` | Controllare `MobileEnvironment == LinuxMobileEnvironment.UbuntuTouch`; Linux, ARM64 o Lomiri da soli non bastano. |
| `TextInputAdapter(field, id, request, log)` | Applica gli hint Avalonia e osserva l'input. Da solo non apre la tastiera Maliit. Usare identificatori costanti, senza testo dell'utente. |
| `MaliitInputBridge(log)` e `Register(field, request)` | Attivano l'integrazione nativa al focus/tap. Conservare e smaltire ogni `IDisposable`; tutte le operazioni sulla UI avvengono sul thread Avalonia. |
| `InputRequest` | Tipi `Text`, `Password`, `Email`, `Numeric`, `Phone`, `Url`, `Search`; multilinea e correzione sono hint. L'app valida i dati. |
| `TextClipboardBridge(field, log)` | Opt-in per Copy/Cut sul percorso Ubuntu Touch/X11, uno per campo. Dispose con il campo. Paste resta quello di Avalonia. |
| `ClipboardTextWriter.TryWriteAsync(clipboard, text, log, token)` | Scrittura delimitata nel tempo per azioni applicative, per esempio copia di un report. Controllare il `bool` prima di mostrare successo. |
| `InputAreaChanged` e `MaliitLayout.BottomInset` | Riservare spazio misurato e portare il campo attivo in vista. |
| `await bridge.DeactivateAsync()` | Per un'eventuale transizione applicativa in background; mantiene le registrazioni. La disattivazione della finestra è già gestita dal bridge. |

Per password servono sia `InputType.Password` sia `TextBox.PasswordChar`; per multilinea servono sia `Multiline = true` sia `AcceptsReturn = true`. Non usare `UpdateRequest` dell'adapter per presumere di aggiornare la registrazione nativa: modifica gli hint del framework, mentre il cambio dinamico degli hint nativi non è supportato in questa preview. Preferisci richieste stabili per campo.

Non concatenare manualmente i commit Maliit a `TextBox.Text` e non installare un secondo IME sullo stesso campo: il bridge gestisce composizione, selezione, undo e sospensione temporanea dell'IME del framework. Le API native `ShowKeyboardAsync`, `HideKeyboardAsync` e `SetInputTypeAsync` non sostituiscono registrazione e focus. `IsAvailable` può essere falso prima del primo focus; `RequestedUnconfirmed` e `KeyboardVisibility.Unknown` non provano che la tastiera sia visibile.

Il workaround Copy/Cut verifica l'effettiva proprietà dei dati dopo un'attesa limitata e protegge da eliminazioni ritardate se l'editor cambia. Non risolve lo scambio con applicazioni Qt native, né garantisce persistenza degli appunti dopo l'uscita. Con password o campi sensibili impedisce Copy/Cut.

Per l'interfaccia finale mantieni testo intorno a 16 unità, campi, pulsanti e voci dei menu alti almeno 48 unità. Il campione sopra dimensiona i campi; i menu di testo richiedono anche l'adattamento applicativo mostrato in [PhoneTextMenu.cs](https://github.com/MiLattanzio/UTDotNet-Bridge/blob/nuget-v0.1.5-preview.1/samples/UTDotNet.InputDemo/PhoneTextMenu.cs). Quel file conserva i flyout originali, dimensiona solo quello del campo attivo a 64×48 e lo apre in modalità transient. Se lo riusi, adatta il namespace, conserva la licenza MIT e smaltisci l'istanza con il campo. Non considerarlo incluso nel NuGet.

## Compilare ed eseguire sul computer

Dalla directory `TouchApp`, con SDK .NET 10 e le [dipendenze Linux di Avalonia](https://docs.avaloniaui.net/docs/platform-specific-guides/linux) disponibili:

```sh
dotnet restore TouchApp.csproj --configfile NuGet.Config
dotnet build TouchApp.csproj -c Release --no-restore
dotnet run --project TouchApp.csproj -c Release --no-build
```

Conserva `packages.lock.json` nel progetto; usa `--locked-mode` nei restore riproducibili dopo aver stabilito le dipendenze. Un restore per un RID può modificare il lock: controlla il diff. Un'app GUI richiede una sessione grafica; la compilazione senza display non verifica la tastiera.

Il codice di questo documento è stato estratto in un progetto temporaneo esterno al repository e verificato con SDK 10.0.401: restore da nuget.org con cache vuota, build Release senza errori o avvisi e publish self-contained `linux-arm64` riusciti. I quattro riferimenti UTDotNet sono pacchetti pubblici 0.1.5-preview.1, senza `ProjectReference`. Il campione non è stato eseguito sul telefono né confezionato in un nuovo Click durante questa verifica; le prove del dispositivo citate riguardano la demo originale.

Le opzioni `--diagnostics`, `--smoke`, `--trace`, `--no-maliit`, `--wayland` e `--screenshot` appartengono alla demo originale. Questo esempio non le implementa. Un eventuale smoke con focus programmatico non equivale a una prova touch o a una cattura della tastiera del compositor.

## Pubblicare per ARM64 e preparare il Click

```sh
dotnet publish TouchApp.csproj -c Release -r linux-arm64 \
  --self-contained true -p:PublishTrimmed=false -p:PublishSingleFile=false \
  -p:PublishAot=false -o artifacts/linux-arm64
```

L'output include il runtime .NET: sul telefono non serve installare l'SDK o `dotnet`. Restano necessarie le librerie native del sistema. Il dispositivo di riferimento ha glibc 2.39, ICU 74.2, OpenSSL 3.0.13 e XWayland; verifica l'immagine target prima di estendere la compatibilità. Un `.nupkg` non è un pacchetto installabile da Lomiri.

Prepara questa struttura in `packaging/click/`, con un'icona reale `icon.svg`; i nomi di manifest, hook, desktop entry ed eseguibile devono essere coerenti. La struttura segue il [formato Click UBports](https://docs.ubports.com/en/latest/appdev/platform/click.html); framework e policy qui indicati riproducono il [deployment verificato](https://github.com/MiLattanzio/UTDotNet-Bridge/blob/nuget-v0.1.5-preview.1/docs/deployment.md).

`packaging/click/manifest.json`:

```json
{
  "name": "touchapp.example",
  "title": "TouchApp",
  "version": "0.1.0",
  "description": "Applicazione Avalonia per Ubuntu Touch",
  "maintainer": "Nome Cognome <email@example.com>",
  "architecture": "arm64",
  "framework": "ubuntu-touch-24.04-2.x",
  "hooks": {
    "touchapp": {
      "apparmor": "touchapp.apparmor",
      "desktop": "touchapp.desktop"
    }
  }
}
```

`packaging/click/touchapp.apparmor`:

```json
{
  "policy_version": 2404.2,
  "policy_groups": []
}
```

Questo è il profilo iniziale per l'esempio di input; l'app reale dichiara i gruppi necessari alle proprie funzionalità. Sul Fairphone input nativo e runtime funzionano con il template standard `ubuntu-sdk`, senza permessi aggiuntivi. Non disabilitare AppArmor o modificare la rootfs per replicare questo percorso.

`packaging/click/touchapp.desktop`:

```ini
[Desktop Entry]
Name=TouchApp
Comment=Applicazione Avalonia per Ubuntu Touch
Exec=./launch.sh
Icon=icon.svg
Terminal=false
Type=Application
X-Ubuntu-Touch=true
X-Ubuntu-XMir-Enable=true
```

`packaging/click/launch.sh`:

```sh
#!/bin/sh
set -eu
cd "${0%/*}"
export DOTNET_EnableDiagnostics=0
export RESOURCE_NAME="${APP_ID:-touchapp.example_touchapp}"
exec ./app/TouchApp "$@"
```

Il launcher usa built-in della shell: `dirname` e `mkdir` esterni sono stati negati dal profilo confinato della demo. `RESOURCE_NAME` evita il tentativo di Avalonia 12.1.6 di leggere il nome del processo da `/proc`. Lomiri fornisce display, directory temporanee, runtime directory e scala; non inserire UID o `DISPLAY` del Fairphone nel Click. Per salvare dati usa le [directory scrivibili applicative UBports](https://docs.ubports.com/en/latest/appdev/guides/writeable-dirs.html), non la directory di installazione.

Per costruire, usa .NET SDK, Docker con accesso all'immagine Clickable appropriata e `desktop-file-validate` sul computer. Da Bash nella directory dell'app, dopo il publish:

```bash
set -euo pipefail
mkdir -p artifacts/click
stage_dir="$(mktemp -d "$PWD/artifacts/click/stage.XXXXXX")"
mkdir -p "$stage_dir/app"
cp -a artifacts/linux-arm64/. "$stage_dir/app/"
cp packaging/click/* "$stage_dir/"
chmod +x "$stage_dir/launch.sh" "$stage_dir/app/TouchApp"
desktop-file-validate "$stage_dir/touchapp.desktop"
image="${CLICK_BUILD_IMAGE:-clickable/amd64-ut24.04-2.x-arm64:latest}"
docker run --rm --user "$(id -u):$(id -g)" \
  --mount "type=bind,src=$PWD/artifacts/click,dst=/work" \
  --workdir /work --entrypoint click "$image" build --no-validate "${stage_dir##*/}"
sha256sum artifacts/click/touchapp.example_0.1.0_arm64.click
docker run --rm --user "$(id -u):$(id -g)" \
  --mount "type=bind,src=$PWD/artifacts/click,dst=/work,readonly" \
  --workdir /work --entrypoint click-review "$image" touchapp.example_0.1.0_arm64.click \
  2>&1 | tee artifacts/click/review.txt
```

Adatta il nome del `.click` se cambi manifest. Per riproducibilità registra il digest dell'immagine effettivamente usata; quello della demo è nel documento di deployment. Il build separa costruzione e revisione: `--no-validate` non è un esito positivo della review. La versione osservata di `click-reviewers-tools 0.85~noble` non conosce la policy `2404.2` nella propria tabella statica e termina con codice 2 per `security:policy_version_exists` e `security:policy_groups_exists`. Conserva l'errore e controlla separatamente eventuali altri problemi; il successo sul telefono non equivale all'approvazione OpenStore. Un'icona relativa può produrre anche un warning non fatale del validatore desktop.

## Installare e provare sul telefono

Con modalità sviluppatore e ADB già autorizzato, seleziona il dispositivo con `adb devices` e verifica `uname -m`, `/etc/os-release`, UID e ambiente della sessione utente. Esegui l'app come utente della sessione, non come root. I seguenti comandi installano il Click sul dispositivo selezionato:

```sh
adb -s SERIAL push artifacts/click/touchapp.example_0.1.0_arm64.click /home/phablet/
adb -s SERIAL shell 'gdbus call --system --dest com.lomiri.click --object-path /com/lomiri/click --method com.lomiri.click.Install /home/phablet/touchapp.example_0.1.0_arm64.click'
```

Sostituisci `SERIAL` con il seriale osservato; apri poi **TouchApp** dal launcher Lomiri. Per avvio CLI usa `lomiri-app-launch touchapp.example_touchapp_0.1.0` con `XDG_RUNTIME_DIR` e `DBUS_SESSION_BUS_ADDRESS` della sessione effettiva. Il formato dell'ID è `<nome-pacchetto>_<hook>_<versione>`.

Per un lancio di sviluppo dalla cartella pubblicata, trasferisci l'intera directory e rileva anche `DISPLAY`, `XDG_SESSION_TYPE`, `XDG_CURRENT_DESKTOP`, `GRID_UNIT_PX` e l'ambiente Maliit della sessione. Le shell ADB possono non ereditarli. I valori UID 32011, display `:0` e seriale riportati nella documentazione della demo appartengono solo al Fairphone osservato. Il lancio manuale non verifica il confinamento Click.

## Verifiche da consegnare insieme all'app

| Verifica | Risultato da registrare |
| --- | --- |
| Restore e build | Versioni effettive, dipendenze da NuGet, esito della compilazione. |
| Publish ARM64 | Artefatto completo e architettura, senza dipendenze dal repository della libreria. |
| Avvio desktop | Finestra, tema, input normale, assenza di attivazione Maliit sul desktop. |
| Avvio Click | Installazione, apertura dal launcher, profilo AppArmor effettivo. |
| Tastiera | Tap, composizione senza spazio finale, commit, backspace, cambio campo, hide e riapertura. |
| Tipi di campo | Password mascherata, modalità numerica/email, Return in multilinea; validazione dell'app separata dagli hint. |
| Layout | Campo inferiore visibile sopra la tastiera, scorrimento, portrait/landscape, menu utilizzabili al tocco. |
| Selezione e appunti | Selezione di una parola ancora in composizione, Cut/Paste, cambio editor durante la copia, scambio con app esterne come prova separata. |
| Ciclo di vita | Dialoghi, cambio app, blocco schermo, sospensione/ripresa, testo conservato e tastiera riapribile. |
| Packaging | Hash del `.click`, output completo della review e problemi ancora aperti. |

Il repository della libreria riporta 70 test automatici superati e prove fisiche su Fairphone 5, incluse selezione/Cut/Paste nel Click 0.1.5. Questi risultati non certificano automaticamente la nuova app. Restano aperti scambio clipboard con Qt nativo, recupero del menu dopo trascinamento delle maniglie di selezione, verifiche estese multilingua e sessioni lunghe. Non sono implementati styling dei singoli span di preedit e reindirizzamento dei tasti hardware. La pubblicazione OpenStore non è stata verificata.

## Diagnosi rapida

| Sintomo | Controllo utile |
| --- | --- |
| La tastiera non appare | Verificare rilevamento Ubuntu Touch, registrazione e focus del campo, session bus e log Maliit. `TextInputAdapter` da solo non attiva il bridge. |
| `IsAvailable` è falso all'avvio | È normale prima di una sessione nativa connessa; ritentare con un tap sul campo. |
| `SessionBusDiagnostics` riporta Unknown nel Click | L'eseguibile esterno `gdbus` può essere negato. La connessione gestita Maliit è indipendente. |
| I menu non si aprono o perdono focus | Verificare `OverlayPopups = true` su Ubuntu Touch e usare i menu di testo originali. |
| Cut non elimina il testo | Verificare versione 0.1.5-preview.1 e attivazione di `TextClipboardBridge` su Ubuntu Touch/X11. Non eliminare manualmente il testo per aggirare un esito negativo. |
| Interfaccia piccola o ingrandita due volte | Applicare grid/8 prima di Avalonia e rispettare gli override espliciti. |
| Campo coperto in landscape | Ricalcolare l'intersezione su resize, verificare coordinate globali e scala, ridurre elementi accessori. |
| Errore di accesso a `/proc` all'avvio | Verificare `RESOURCE_NAME` nel launcher confinato. |
| Errore `xdg_wm_base (>=3)` | È il blocco Wayland noto sull'immagine testata; usare il percorso X11 verificato. |

Usa `DiagnosticLog` con identificatori stabili: `Snapshot()` restituisce gli eventi recenti e `EntryAdded` permette di mostrarli o esportarli. Non aggiungere log del testo digitato, password o contenuti clipboard. Per approfondire: [integrazione input](https://github.com/MiLattanzio/UTDotNet-Bridge/blob/nuget-v0.1.5-preview.1/docs/input-method.md), [demo completa](https://github.com/MiLattanzio/UTDotNet-Bridge/tree/nuget-v0.1.5-preview.1/samples/UTDotNet.InputDemo), [packaging originale](https://github.com/MiLattanzio/UTDotNet-Bridge/tree/nuget-v0.1.5-preview.1/packaging/click) e [script di build Click](https://github.com/MiLattanzio/UTDotNet-Bridge/blob/nuget-v0.1.5-preview.1/scripts/build-click.sh). I riferimenti sono fissati al tag della release NuGet per evitare di mescolare API successive.
