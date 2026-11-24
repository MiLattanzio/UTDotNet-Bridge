# Passaggio di consegne per integrare Ubuntu Touch nei sorgenti Avalonia

L'obiettivo è portare in Avalonia il supporto Ubuntu Touch sviluppato in UTDotNet, così che un'app possa usare tastiera Maliit, appunti nativi e informazioni sull'area occupata dalla tastiera tramite i normali servizi del framework. Il risultato deve eliminare le dipendenze `UTDotNet.*` e la registrazione manuale dei singoli campi. Il primo percorso da completare è **X11 tramite XWayland su Lomiri**, già verificato sul telefono.

Destinatario: lo sviluppatore o l'istanza Codex che lavorerà nel repository di Avalonia. Questo è il piano di trasferimento; l'integrazione nei sorgenti Avalonia resta da implementare. La precedente [consegna per applicazioni](codex-handoff.it.md) riguarda invece l'uso dei pacchetti bridge.

## Base da trasferire

Riferimento aggiornato al **24 novembre 2026**:

| Elemento | Base riproducibile |
| --- | --- |
| Repository sorgente | [UTDotNet-Bridge](https://github.com/MiLattanzio/UTDotNet-Bridge), directory locale `/home/mi/RiderProjects/UTDotNet-Bridge` |
| Base sorgente | Tag `nuget-v0.1.6-preview.1`, che include le modifiche Content Hub elencate sotto |
| Librerie | `0.1.6-preview.1`, con Content Hub |
| Demo e funzionalità più recenti | Click `0.1.6`, con clipboard Content Hub |
| Framework verificato | Avalonia `12.1.6`, .NET `10`, Tmds.DBus.Protocol `0.94.1` |
| Dispositivo delle prove registrate | Fairphone 5 ARM64, Ubuntu Touch `24.04-2.2`, build 3, Lomiri |
| Esecuzione verificata | X11/XWayland, Click con AppArmor standard e nessun gruppo aggiuntivo |
| Verifica automatica ripetuta per questa consegna | **93 test passati, 0 falliti, 0 saltati** |

**La release `nuget-v0.1.6-preview.1` contiene la base Content Hub completa.** Rispetto al precedente commit `f7690f6108db05bf8e185e9f7ae533bda0f81462`, i file principali aggiunti o modificati sono:

- Nuovi: [ITextClipboard.cs](../src/UTDotNet.Core/ITextClipboard.cs), [ContentHubClipboard.cs](../src/UTDotNet.UbuntuTouch/ContentHubClipboard.cs), [ContentHubTextCodec.cs](../src/UTDotNet.UbuntuTouch/ContentHubTextCodec.cs), [ContentHubClipboardTests.cs](../tests/UTDotNet.Tests/ContentHubClipboardTests.cs), [NativeClipboardBridgeTests.cs](../tests/UTDotNet.Tests/NativeClipboardBridgeTests.cs) e [NativeClipboardProbe.qml](../tests/device/NativeClipboardProbe.qml).
- Modificati: [TextClipboardBridge.cs](../src/UTDotNet.Avalonia/TextClipboardBridge.cs), [TextClipboardBridgeTests.cs](../tests/UTDotNet.Tests/TextClipboardBridgeTests.cs), [MainWindow.cs](../samples/UTDotNet.InputDemo/MainWindow.cs), [manifest.json](../packaging/click/manifest.json), README e documentazione di input, distribuzione e prove.
- La documentazione e gli artefatti di prova hanno funzioni diverse: [device-testing.md](device-testing.md) è il registro versionabile; screenshot e log sotto `artifacts/verification/` sono locali e ignorati da Git. Trasferirli separatamente se servono alla revisione.

Prima del porting acquisire `git status --short`, `git diff` e `git ls-files --others --exclude-standard`. Una patch prodotta dal solo `git diff` ometterebbe i nuovi file Content Hub. Conservare una copia completa della base di confronto senza sovrascrivere modifiche dell'utente. Per il codice riutilizzato mantenere l'attribuzione e la [licenza del progetto](../LICENSE).

## Risultato richiesto nelle applicazioni

L'app finale deve poter usare il normale avvio Avalonia, `TextBox`, `TextInputOptions` e `TopLevel.Clipboard`. Focus e tap aprono Maliit; Copy/Cut/Paste scambiano testo con le applicazioni native; il layout riceve l'occlusione della tastiera attraverso le API Avalonia.

Non devono più comparire nell'app `MaliitInputBridge.Register`, `TextInputAdapter`, `TextClipboardBridge`, `ClipboardTextWriter`, `ITextClipboard` o riferimenti ai quattro progetti/pacchetti UTDotNet. Anche un campo creato dinamicamente o dentro un dialogo deve funzionare senza configurazione del bridge.

Restano responsabilità dell'app contenuti, validazione, tema, dimensioni dei controlli, comportamento responsive e identità del pacchetto Click. Durante il lavoro un'opzione esplicita del backend può consentire il confronto; per il risultato finale predisporre il rilevamento Ubuntu Touch e un override documentato, senza introdurre un nuovo adattatore per ogni controllo.

## Mappa tra il bridge e Avalonia

I punti di ingresso seguenti sono stati verificati nei sorgenti ufficiali **12.1.6**. Se si lavora su un altro ramo, ritrovarne gli equivalenti prima di modificare il codice. I nomi delle nuove classi sono proposte, non API già disponibili.

| Implementazione da usare come riferimento | Destinazione e lavoro in Avalonia |
| --- | --- |
| [MaliitSession.cs](../src/UTDotNet.UbuntuTouch/MaliitSession.cs), [MaliitModels.cs](../src/UTDotNet.UbuntuTouch/MaliitModels.cs), [MaliitPeerConnectionOptions.cs](../src/UTDotNet.UbuntuTouch/MaliitPeerConnectionOptions.cs) | Trasporto interno, ad esempio `src/Avalonia.FreeDesktop/DBusIme/Maliit/`. Separare protocollo, stato e adattamento all'input Avalonia. |
| [MaliitInputBridge.cs](../src/UTDotNet.Avalonia/MaliitInputBridge.cs) | Nuova implementazione di `ITextInputMethodImpl` e `IX11InputMethodControl`, creata da `IX11InputMethodFactory`. Trasferire ciclo di vita e protezioni, eliminando il dizionario di `TextBox` registrate. |
| [MaliitTextEditor.cs](../src/UTDotNet.Avalonia/MaliitTextEditor.cs) | Traduzione di commit, preedit, replacement e tasti nel client IME e nel normale percorso di input. Eventuali capacità mancanti vanno aggiunte al contratto generico e implementate dal client `TextBox`. |
| [TextInputAdapter.cs](../src/UTDotNet.Avalonia/TextInputAdapter.cs), [Input.cs](../src/UTDotNet.Core/Input.cs) | Mappare direttamente `TextInputOptions` ai parametri Maliit. L'osservatore diagnostico e `InputRequest` non devono diventare prerequisiti dell'app. |
| [ContentHubClipboard.cs](../src/UTDotNet.UbuntuTouch/ContentHubClipboard.cs), [ContentHubTextCodec.cs](../src/UTDotNet.UbuntuTouch/ContentHubTextCodec.cs) | Backend interno Content Hub che implementa `IClipboardImpl`, selezionato nella costruzione dei servizi X11 su Ubuntu Touch. Il pubblico continua a essere `IClipboard`. |
| [TextClipboardBridge.cs](../src/UTDotNet.Avalonia/TextClipboardBridge.cs) | Protezioni di editing asincrono in `Avalonia.Controls/TextBox.cs`; eliminare l'intercettazione applicativa degli eventi clipboard. |
| [ClipboardTextWriter.cs](../src/UTDotNet.Avalonia/ClipboardTextWriter.cs) | Correzione separata dell'attesa `SAVE_TARGETS` in `X11ClipboardImpl`; utile per X11 ma non sostitutiva di Content Hub. |
| [MaliitLayout.cs](../src/UTDotNet.Avalonia/MaliitLayout.cs), gestione in [MainWindow.cs](../samples/UTDotNet.InputDemo/MainWindow.cs) | Esporre l'area tramite `IInputPane` del `TopLevel`, con trasformazione delle coordinate e aggiornamento a resize/rotazione. |
| [PlatformDetection.cs](../src/UTDotNet.Core/PlatformDetection.cs), [PhoneDisplayScale.cs](../src/UTDotNet.Core/PhoneDisplayScale.cs), [Program.cs](../samples/UTDotNet.InputDemo/Program.cs) | Rilevamento conservativo, scala Lomiri e scelta dei popup nel backend. Separare questi default dal protocollo Maliit. |

Punti di aggancio ufficiali:

- [X11DBusImeHelper.cs](https://github.com/AvaloniaUI/Avalonia/blob/12.1.6/src/Avalonia.FreeDesktop/DBusIme/X11DBusImeHelper.cs) sceglie attualmente IBus/Fcitx. Le interfacce factory/control sono insieme in [IX11InputMethod.cs](https://github.com/AvaloniaUI/Avalonia/blob/12.1.6/src/Avalonia.FreeDesktop/IX11InputMethod.cs).
- [X11Window.Ime.cs](https://github.com/AvaloniaUI/Avalonia/blob/12.1.6/src/Avalonia.X11/X11Window.Ime.cs) crea il client con l'XID e inoltra commit/tasti. [X11Window.cs](https://github.com/AvaloniaUI/Avalonia/blob/12.1.6/src/Avalonia.X11/X11Window.cs) notifica attivazione, coordinate e scala e restituisce le feature del `TopLevel`.
- [InputMethodManager.cs](https://github.com/AvaloniaUI/Avalonia/blob/12.1.6/src/Avalonia.Base/Input/TextInput/InputMethodManager.cs) contiene `TextInputMethodManager` e gestisce client, reset, focus e rettangolo del cursore. Usare questo percorso, evitando di disabilitare l'IME come fa oggi il bridge esterno.
- [X11Platform.cs](https://github.com/AvaloniaUI/Avalonia/blob/12.1.6/src/Avalonia.X11/X11Platform.cs) costruisce `X11ClipboardImpl`, lo avvolge in `Clipboard` e registra entrambi i servizi. Il contratto da implementare è [IClipboardImpl](https://github.com/AvaloniaUI/Avalonia/blob/12.1.6/src/Avalonia.Base/Input/Platform/IClipboardImpl.cs); [Clipboard.cs](https://github.com/AvaloniaUI/Avalonia/blob/12.1.6/src/Avalonia.Base/Input/Platform/Clipboard.cs) gestisce anche il percorso opzionale di ownership.
- [IInputPane.cs](https://github.com/AvaloniaUI/Avalonia/blob/12.1.6/src/Avalonia.Controls/Platform/IInputPane.cs) definisce `OccludedRect`, `StateChanged` e gli stati `Open`/`Closed`.

`Avalonia.FreeDesktop` usa già Tmds; nel tag verificato è anch'esso `0.94.1`. Adattare il codice ai target framework e alle convenzioni del repository Avalonia, senza imporre il `net10.0` esclusivo del bridge o importare i suoi progetti. Riferimenti: [progetto FreeDesktop](https://github.com/AvaloniaUI/Avalonia/blob/12.1.6/src/Avalonia.FreeDesktop/Avalonia.FreeDesktop.csproj) e [versioni dipendenze](https://github.com/AvaloniaUI/Avalonia/blob/12.1.6/Directory.Packages.props).

## Integrazione Maliit

### Trasporto e protocollo

Riutilizzare il trasporto gestito come base, mantenendo questi vincoli:

1. Il session bus serve a leggere `org.maliit.Server.Address.address`, oggetto `/org/maliit/server/address`, destinazione `org.maliit.server`. La conversazione di input prosegue su una **connessione D-Bus peer separata** all'endpoint Unix ottenuto. Accettare un singolo endpoint locale.
2. Il server espone `/com/meego/inputmethod/uiserver1`, interfaccia `com.meego.inputmethod.uiserver1`. Il client esporta `/com/meego/inputmethod/inputcontext`, interfaccia `com.meego.inputmethod.inputcontext1`. Non basta aggiungere una chiamata `showInputMethod` sul session bus.
3. All'apertura registrare il callback, attivare il contesto, inviare `updateWidgetInformation(a{sv}b)` e richiedere la tastiera. Conservare `showInputMethod`, `hideInputMethod`, `reset` e aggiornamenti dello stato.
4. Portare i callback `commitString(siii)`, `updatePreedit(sa(iii)iii)`, `keyEvent(iiisbiy)`, `setSelection(ii)`, `updateInputMethodArea(iiii)`, `imInitiatedHide`, `activationLostEvent`, `selection` e `preeditRectangle` secondo [MaliitSession.cs](../src/UTDotNet.UbuntuTouch/MaliitSession.cs). Le impostazioni attualmente solo riconosciute non costituiscono supporto al reindirizzamento della tastiera hardware.
5. Serializzare le chiamate in uscita; la base applica un limite di quattro secondi alle operazioni di connessione/chiamata. Cancellazione o timeout chiudono il trasporto interessato. Una connessione condivisa di Avalonia non va chiusa per annullare un singolo client.
6. Applicare il callback sul thread UI prima dell'acknowledgement. Dal callback non attendere una nuova chiamata sullo stesso peer: il server potrebbe essere in attesa della risposta e causare un deadlock.

Con Tmds `0.94.1`, Maliit può inviare callback prima che termini il probe `Hello`. Il workaround `MaliitHandshakeStream` trattiene quei frame fino alla registrazione del handler, lasciando passare prima la risposta/errore `Hello`: massimo 32 frame e 64 KiB. Mantenerne i test di ordine, letture parziali, limiti e disposal. Rivalutarlo quando cambia Tmds; non applicarlo indiscriminatamente al session bus. [Architettura e motivazione](architecture.md).

La base `DBusTextInputMethodBase` di Avalonia è un riferimento per factory e lifecycle, ma il suo modello di servizio sul bus non equivale al peer Maliit. Decidere se estrarre parti comuni o implementare direttamente le due interfacce; conservare le garanzie sopra in entrambi i casi.

### Client generico ed editing

[ITextInputMethodImpl](https://github.com/AvaloniaUI/Avalonia/blob/12.1.6/src/Avalonia.Base/Input/TextInput/ITextInputMethodImpl.cs) espone `SetClient`, `SetCursorRect`, `SetOptions` e `Reset`. Il backend Maliit deve usare questi ingressi e sottoscrivere `InputPaneActivationRequested`, `SurroundingTextChanged` e `SelectionChanged` sul client attivo. Una nuova attivazione del pannello deve riaprire la tastiera anche quando il campo ha già il focus.

Il porting non è una copia meccanica di `MaliitTextEditor`: [TextInputMethodClient](https://github.com/AvaloniaUI/Avalonia/blob/12.1.6/src/Avalonia.Base/Input/TextInput/TextInputMethodClient.cs) fornisce surrounding text, selezione e preedit, ma non un'operazione completa di replacement/commit con tutte le semantiche Maliit. L'evento `IX11InputMethodControl.Commit` trasporta solo una stringa.

Prima di completare il backend, definire il percorso generico per replacement relativo, cancellazione con commit vuoto e posizione del cursore dopo il commit. Verificare se basta la selezione del client più il percorso di input esistente; altrimenti aggiungere la minima capacità necessaria al contratto e al [TextBoxTextInputMethodClient](https://github.com/AvaloniaUI/Avalonia/blob/12.1.6/src/Avalonia.Controls/TextBoxTextInputMethodClient.cs). Non ricavare una `TextBox` concreta dal visual né modificare direttamente `Text` dal trasporto. Il client generico può esporre un paragrafo invece dell'intero documento: gli offset devono restare relativi al surrounding text fornito.

Preservare questi comportamenti:

- Preedit separato dal testo confermato; commit, replacement, backspace e cancellazione passano dall'editing normale e mantengono undo, selezione, `MaxLength`, sola lettura e regole multilinea.
- Reset, hide e cambio di focus finalizzano una composizione valida; una sostituzione esplicita del testo da parte dell'app annulla la vecchia composizione.
- Un pointer press deve finalizzare il preedit **prima** che inizi la selezione nel controllo. Questa correzione ha risolto la parola visibile ma non selezionabile. Trasferirla nel punto comune di reset/editing e provarla anche con gli altri IME.
- Il bridge traduce i codici Qt; `X11InputMethodForwardedKey.KeyVal` di Avalonia usa invece il percorso X11. Serve una conversione esplicita: non inoltrare i numeri Qt come keysym X11. Gli eventi signal-only non devono editare il testo.
- L'inoltro di un commit mediante `ScheduleInput` è accodato. Verificare che selezione e stato osservati dal successivo callback siano già aggiornati quando si risponde a Maliit; il semplice ritorno dell'evento `Commit` non lo dimostra.
- I rettangoli ricevuti da `SetCursorRect` sono già nel percorso di trasformazione del manager. Convertire una sola volta a pixel globali con posizione finestra e scala, verificando cursore in `ScrollViewer`, margini, dialoghi e rotazione.

### Focus e stato sensibile

Ogni contesto di focus deve avere cancellazione e generazione proprie. Un callback della sessione precedente o precedente a un reset non può modificare il nuovo editor. Sganciare gli eventi su cambio client, detach e disposal.

Quando una finestra perde attivazione, conservare la composizione, completare l'eventuale apertura già accodata, richiedere hide e poi rilasciare il peer. Il bridge limita questa fase a un secondo. Una successiva apertura deve attendere la chiusura precedente, anche passando tra finestre: un hide tardivo non deve nascondere la tastiera del nuovo dialogo. La chiusura di un menu che restituisce il focus al campo va distinta da un reale abbandono dell'input.

In `SetOptions` mappare contenuto, multilinea, suggerimenti, maiuscole e tipo di invio usando [MaliitModels.cs](../src/UTDotNet.UbuntuTouch/MaliitModels.cs). Per campi sensibili/password omettere dal protocollo surrounding text, offset e selezione, disattivando predizione e capitalizzazione. Escludere contenuti e dettagli sensibili anche dai log. Gestire gli aggiornamenti delle opzioni previsti dal framework; il bridge ha invece registrazioni con hint fissi.

La selezione automatica deve rispettare gli override IME esistenti e `none`. Aggiungere il riconoscimento esplicito `maliit` e un default limitato a Ubuntu Touch; la sola presenza di Maliit, ARM64, Lomiri o `WAYLAND_DISPLAY` non identifica il dispositivo o il backend. In caso di servizio assente conservare l'editing ordinario e la possibilità di riconnessione senza input duplicato.

## Clipboard Content Hub e correzioni comuni

### Backend nativo

Il percorso funzionante usa il servizio e l'interfaccia `com.lomiri.content.dbus.Service`, oggetto `/`, sul session bus:

| Operazione | Firma e significato nella base |
| --- | --- |
| Scrittura | `CreatePaste(ssayas) → b`: app ID, surface ID vuoto, buffer MIME, elenco `text/plain` |
| Lettura | `GetLatestPasteData(s) → ay`: surface ID vuoto; decodifica del buffer MIME |

Il surface ID vuoto usa il controllo del focus del processo chiamante da parte del servizio, esercitato anche con la demo X11 confinata. Usare l'identità dell'app (`APP_ID` nella base), senza impersonare una superficie Mir. Il client apre una connessione per operazione, con limite complessivo di due secondi, e la chiude su timeout/cancellazione. Non richiede Qt, processi esterni o polling. [Dettagli del protocollo e prove](input-method.md#native-content-hub-clipboard-016-preview1).

Il codec supporta plain text UTF-8, accenti, emoji e newline. Il formato verificato è little-endian, con limite di 4 MiB e massimo 16 voci MIME in lettura; controllare offset, dimensioni e UTF-8 prima di usare il payload. La clipboard UTDotNet è solo testuale: non trasferisce automaticamente l'intero contratto Avalonia.

Nel nuovo `IClipboardImpl` completare esplicitamente questi punti:

1. `TryGetDataAsync` deve produrre dati Avalonia con formati coerenti e lifetime corretto. Dati nativi assenti o invalidi non autorizzano a restituire una vecchia copia X11.
2. `SetDataAsync` deve completare con successo solo quando la scrittura è accettata. Il `false` del trasporto va tradotto in un errore gestito dal percorso clipboard Avalonia: una normale conclusione della `Task` farebbe proseguire Cut con la cancellazione. Verificare anche il trattamento dei timeout nelle eccezioni gestite dai controlli.
3. Implementare e provare `ClearAsync` contro il servizio reale. Il bridge non contiene questa operazione: non dichiararla completata con un no-op né assumere senza prova che scrivere una stringa vuota equivalga a cancellare tutti i formati.
4. Definire la politica per formati misti, non testuali e rich text, preservando il backend X11 negli ambienti non Ubuntu Touch. La prima consegna può documentare il supporto al solo testo, ma deve rifiutare in modo coerente operazioni non supportate.
5. Rispettare il contratto di disposal di [IClipboard](https://github.com/AvaloniaUI/Avalonia/blob/12.1.6/src/Avalonia.Base/Input/Platform/IClipboard.cs). Non implementare `IOwnedClipboardImpl` sulla base di una cache locale: dopo una copia in Morph il proprietario/dato corrente può essere cambiato. Senza una verifica affidabile `TryGetInProcessDataAsync` deve restare senza dato confermato.

La scelta del backend deve valere anche per `TopLevel.Clipboard` usato da pulsanti e codice applicativo. Intercettare soltanto il menu di `TextBox` lascerebbe il bridge necessario in altri punti.

### Editing asincrono

Portare le protezioni di `TextClipboardBridge` nel normale percorso di [TextBox.cs](https://github.com/AvaloniaUI/Avalonia/blob/12.1.6/src/Avalonia.Controls/TextBox.cs). Nella base verificata Cut attende la scrittura e poi elimina la selezione; Paste attende la lettura e poi applica il testo. Entrambe richiedono verifiche dopo l'attesa.

Salvare una revisione dell'editor prima dell'operazione e invalidarla su cambiamenti di testo, selezione, sensibilità, sola lettura, focus, attachment o attivazione finestra. Anche deattivazione seguita da riattivazione deve invalidare il risultato. Prima di una modifica tardiva controllare la stessa finestra e le capacità del campo. Mantenere gli eventi pubblici Copy/Cut/Paste e `Handled`, undo/redo, vincoli del campo e ritorno del focus dai menu. Verificare la compatibilità delle chiamate programmatiche esistenti invece di copiare indiscriminatamente i vincoli del bridge telefonico.

Cut cancella solo dopo scrittura accettata e se la selezione originaria è ancora valida. Un errore di lettura, testo assente o vuoto non deve cancellare la selezione durante Paste. Password e campi sensibili non esportano testo con Copy/Cut, anche tramite chiamata diretta; Paste resta consentito se il campo è modificabile.

### Attesa del clipboard manager X11

Il problema precedente è distinto dall'interoperabilità nativa: [X11ClipboardImpl.cs](https://github.com/AvaloniaUI/Avalonia/blob/12.1.6/src/Avalonia.X11/Selections/Clipboard/X11ClipboardImpl.cs) prende ownership e poi attende `SAVE_TARGETS` senza timeout. Cut può così restare sospeso pur avendo copiato il testo localmente.

Portare una correzione circoscritta a questa implementazione: attesa limitata, gestione di perdita di ownership, richieste successive, risposta tardiva e disposal. Il bridge usa 500 ms e accetta il risultato solo quando possiede ancora **l'esatto oggetto** inviato; uguaglianza del testo non è una prova sufficiente. Dentro il backend definire una generazione per richiesta e le semantiche di successo. Non dichiarare persistenza dopo l'uscita sulla base della sola ownership locale. Questa correzione non sostituisce Content Hub per lo scambio con Qt/Morph.

## Area della tastiera e comportamento del telefono

Usare `updateInputMethodArea` per esporre l'occlusione tramite `IInputPane`. Il rettangolo Maliit della base è in pixel globali: intersecarlo con il viewport della finestra, convertirlo nelle coordinate logiche attese dalla feature e ricalcolarlo quando cambiano posizione, dimensioni o scala. Il risultato non deve ridurre due volte una finestra già ridimensionata dal compositor. [MaliitLayout.cs](../src/UTDotNet.Avalonia/MaliitLayout.cs) fornisce il calcolo verificato dell'inset inferiore.

L'ultima area misurata deve sopravvivere al cambio campo e al passaggio a un dialogo: Maliit non sempre ripete una geometria invariata. Invalidarla su hide/deattivazione quando appropriato; non mantenerla come dato globale senza lifecycle. Usare il normale layout Avalonia per portare il campo in vista; mantenere la logica responsive specifica della demo nell'app.

Il bridge riporta visibilità `Unknown`; una risposta a show/hide non prova la presenza sullo schermo. `IInputPane` 12.1.6 dispone soltanto di `Open`/`Closed`: definire e testare una mappatura basata sui dati di occlusione e sulle notifiche del server, documentando il caso ancora sconosciuto. Non trasformare automaticamente un acknowledgement in `Open`.

Completare i default telefonici come interventi separati:

- **Scala:** `GRID_UNIT_PX / 8`, assoluta. Sul FP5 `24 → 3`. Il bridge accetta valori finiti tra 8 e 48; gli override Avalonia già impostati prevalgono. Integrare il default nel percorso di scaling, senza moltiplicare una scala già applicata. `device-info` è un fallback della demo per shell di sviluppo e non deve diventare un comando esterno obbligatorio nel framework.
- **Popup:** `X11PlatformOptions.OverlayPopups = true` ha risolto il focus dei dropdown su Lomiri. Valutare un default Ubuntu Touch che rispetti le scelte esplicite dell'app; provarlo con menu, ComboBox e dialoghi.
- **Identità sotto AppArmor:** il launcher usa `RESOURCE_NAME` ricavato da `APP_ID` per evitare il recupero del nome processo via procfs. Se si rende il framework autonomo da questo workaround, correggere il fallback di `WM_CLASS` in `X11Window.cs` rispettando prima l'identità esplicita. Non ampliare le policy per risolvere l'accesso a procfs.
- **UI:** 16 unità per il testo, campi alti almeno 48 e voci dei menu originali 64×48 sono scelte verificate della demo. Conservare menu e selezione nativi di Avalonia; eventuali default touch del tema richiedono una modifica dedicata, non stili applicati dal trasporto Maliit.

## Sequenza di implementazione

1. **Acquisire la base completa.** Aprire un checkout Avalonia, leggere le sue istruzioni locali, scegliere e registrare il commit di destinazione. Per una nuova branch usare `codex/ubuntu-touch-native-integration`. Riprodurre il bridge e mantenere la demo originale come confronto.
2. **Portare protocollo e test Maliit.** Isolare il peer e il workaround di handshake in FreeDesktop; adattare logging e framework target. Aggiungere i test mancanti sul protocollo reale con un peer simulato, oltre ai mock del bridge.
3. **Integrare factory e client IME.** Collegare focus, opzioni, reset, input, attivazione finestra e riapertura su tap. Risolvere replacement e ordine di commit/ack. Validare un'app senza registrazioni per campo, compresi dialoghi e controlli creati dopo l'avvio.
4. **Portare Content Hub.** Implementare `IClipboardImpl`, decidere clear/formati/lifetime, collegarlo ai servizi X11 e portare i test con session bus privato. Lo scambio deve funzionare anche tramite API diretta di `TopLevel.Clipboard`.
5. **Integrare le correzioni comuni.** Protezioni di Cut/Paste, reset prima della selezione e limite X11 `SAVE_TARGETS` in modifiche distinte e verificabili. Eseguire regressioni anche per i backend e gli IME esistenti.
6. **Esporre l'occlusione e i default Ubuntu Touch.** Completare `IInputPane`, scala, popup e identità confinata. Verificare rotazione, passaggi fra finestre e lock/unlock.
7. **Rimuovere il bridge dalla demo di accettazione.** Compilare usando solo gli artefatti Avalonia modificati, generare un Click ARM64 e ripetere le prove native. Preparare commit/PR con base, motivazione, test e limiti; l'eventuale accettazione upstream è distinta dal funzionamento nel fork.

La decomposizione suggerita permette di proporre separatamente correzioni generiche di clipboard/editing, backend Maliit, backend Content Hub e default Lomiri. Mantenere il baseline X11 standard per confrontare regressioni; non mescolare l'abilitazione Wayland con la prima integrazione.

## Verifiche da trasferire

Per la base UTDotNet, dalla radice del repository:

```sh
dotnet restore UTDotNet.sln
dotnet test UTDotNet.sln -c Release --no-restore --nologo --disable-build-servers -m:1
```

Il secondo comando ha ricompilato la soluzione e passato tutti i **93 test** il 23 novembre 2026. I test richiedono anche `dbus-daemon` e un ambiente che consenta l'apertura di socket locali per MSBuild e il bus privato. I test e la build di Avalonia modificato dovranno essere eseguiti nel repository di destinazione secondo le sue istruzioni.

| Suite di origine | Cosa preservare nel porting |
| --- | --- |
| [MaliitTests.cs](../tests/UTDotNet.Tests/MaliitTests.cs) | Commit/replacement/undo, preedit separato, eventi obsoleti, deattivazione, hide prima del nuovo show, area mantenuta nei dialoghi, protezione dei dati sensibili |
| [MaliitHandshakeTests.cs](../tests/UTDotNet.Tests/MaliitHandshakeTests.cs) | Callback anticipati, ordine dei frame, disposal e limiti del buffer |
| [MaliitClipboardTests.cs](../tests/UTDotNet.Tests/MaliitClipboardTests.cs) | Selezione del preedit, menu touch, editing dopo clipboard, password |
| [TextClipboardBridgeTests.cs](../tests/UTDotNet.Tests/TextClipboardBridgeTests.cs) | Ownership esatta, manager non responsivo, Cut tardivo, comandi diretti e ritorno focus dal menu |
| [ContentHubClipboardTests.cs](../tests/UTDotNet.Tests/ContentHubClipboardTests.cs) | Fixture nativa, Unicode, parser ostile, limiti, firme D-Bus effettive, identità, rifiuto, timeout e cancellazione |
| [NativeClipboardBridgeTests.cs](../tests/UTDotNet.Tests/NativeClipboardBridgeTests.cs) | Scambio nativo, undo, Paste tardivo, background/ritorno, nessun fallback X11 obsoleto, password e `MaxLength` |
| [CoreTests.cs](../tests/UTDotNet.Tests/CoreTests.cs), [AvaloniaTests.cs](../tests/UTDotNet.Tests/AvaloniaTests.cs) | Detection negativa su desktop, input ordinario, log redatti, detach, hint, layout e rotazione |

I test che verificano la disattivazione/ripristino dell'IME da parte del bridge vanno riscritti per il percorso integrato: lì l'IME del framework deve restare attivo. Non basta copiare test che continuano a istanziare gli adapter eliminati.

Aggiungere nel repository Avalonia test per priorità degli override IME, assenza/riavvio dei servizi, più finestre, opzioni dinamiche, client testuale diverso da `TextBox`, clear/formati clipboard, disposal dei dati e timeout X11 con più scritture. Eseguire le regressioni IBus/Fcitx/XIM e gli altri controlli richiesti dalla CI.

### Accettazione sul dispositivo

Le prove storiche del [registro](device-testing.md) riguardano il bridge, non certificano il futuro porting. Ripeterle con un'app collegata esclusivamente ad Avalonia modificato:

- Tap, digitazione, preedit, commit, backspace, hide e riapertura; password, numerico, email e multilinea.
- Selezione con pressione prolungata anche durante il preedit; trascinamento delle maniglie, menu originale, Cut/Paste, sostituzione e undo/redo.
- Copy/Paste in entrambe le direzioni con il [probe Qt](../tests/device/NativeClipboardProbe.qml) e Morph, usando testi diversi, accenti, emoji e più righe. Includere un pulsante che acceda direttamente a `TopLevel.Clipboard`.
- Cambio campo, dialogo, menu e rotazione con campo visibile sopra la tastiera; lock/unlock, background/ritorno e sospensione reale, senza tastiera residua sul lock screen né perdita della composizione.
- Avvio dal Click in AppArmor enforce, senza nuove policy. Registrare commit Avalonia, versioni dei servizi, hash del pacchetto e risultati; distinguere touch fisico, eventi iniettati, headless e semplici richieste al protocollo.

Per pubblicare ARM64 usare inizialmente il percorso già provato: self-contained, `linux-arm64`, senza trimming, AOT o single-file. Adattare [packaging e comandi](deployment.md) all'app di accettazione. Seriali, UID e display del FP5 sono valori osservati, da rilevare di nuovo. Una cattura del render dell'app non mostra la tastiera del compositor.

## Condizioni di completamento e limiti

Il bridge è eliminabile quando:

1. Il grafo delle dipendenze e l'output pubblicato dell'app di accettazione non contengono pacchetti o assembly `UTDotNet.*`, e il sorgente dell'app non crea adapter sostitutivi.
2. Il percorso IME integrato gestisce automaticamente campi, dialoghi e richieste di riapertura, mantenendo editing e lifecycle descritti sopra.
3. Il servizio clipboard di Avalonia scambia testo con Qt/Morph e gestisce errori senza perdita o sostituzione tardiva del testo.
4. Layout, rotazione e confinamento passano sul telefono, e i test di regressione dei backend esistenti passano sul computer.
5. La consegna comprende commit del fork, artefatti utilizzabili, istruzioni d'uso e un registro che separa risultati verificati e attività residue.

Restano fuori dalla prima base verificata: stile dei singoli span preedit, reindirizzamento completo dei tasti hardware, formati clipboard non testuali, persistenza degli appunti dopo uscita/riavvio, AOT/trimming e supporto generalizzato ad altri dispositivi. Lingue aggiuntive, composizione accentata tramite tastiera, modalità search/phone/URL, selezioni multilinea estese e sessioni lunghe richiedono prove dedicate; l'Unicode già provato negli appunti non dimostra la composizione IME in quelle lingue.

Wayland nativo resta un lavoro separato: sul FP5 osservato `xdg_wm_base` è v1 mentre il backend provato richiede almeno v3, e il registry non espone text-input-v3. Portare Maliit non risolve automaticamente avvio e protocolli Wayland; non abbassare una versione richiesta senza verificare le chiamate utilizzate. Il review tool Click usato nella base manca dei metadati policy `2404.2`: installazione e funzionamento confinato non equivalgono ad approvazione OpenStore.

## Istruzione pronta per chi riceve il lavoro

> Integra nei sorgenti Avalonia il supporto Ubuntu Touch descritto in questo documento. Usa il tag nuget-v0.1.6-preview.1 di UTDotNet-Bridge come implementazione di riferimento, con le modifiche Content Hub verificate nella demo Click 0.1.6. Parti dal percorso X11/XWayland e dai contratti del tag Avalonia 12.1.6, adattandoli al commit di destinazione che registrerai. Porta Maliit nel backend IME, Content Hub in IClipboardImpl, le protezioni di editing nei controlli e l'occlusione in IInputPane. Preserva desktop, password, undo, lifecycle e confinamento. Concludi con una demo che usa soltanto Avalonia modificato, senza dipendenze UTDotNet o registrazioni per campo, e con test automatici e prove sul telefono documentati separatamente. Se il telefono non è disponibile, completa il lavoro verificabile e consegna il pacchetto e la matrice da eseguire, lasciando esplicita l'accettazione dispositivo ancora pendente.
