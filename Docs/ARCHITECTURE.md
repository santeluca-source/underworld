# Architettura di UNDERWORLD

Questo documento è il contratto tecnico del progetto. Ogni modulo lo segue.
Se una scelta cambia, aggiorna questo file e `Docs/DECISIONS.md`.

## 1. In breve

- Un solo eseguibile: `UNDERWORLD.exe`. Gli argomenti decidono se è client o server.
- Ogni area del mondo gira in un **server separato** (massimo 16 giocatori per server).
- Il **server decide** tutto ciò che conta: soldi, oggetti, proprietà, veicoli posseduti, missioni, polizia, clan.
- Il **client** mostra il mondo, legge l'input e manda **richieste**. Non decide mai un valore economico.
- I salvataggi sono file JSON sul PC del server, dietro l'interfaccia `IPlayerStore`.
  Più avanti un backend (ASP.NET Core + PostgreSQL) implementerà la stessa interfaccia.
- "Gioca" dal menu avvia da solo i server delle aree sul PC (processi nascosti) e collega il giocatore.

## 2. Modi di avvio (argomenti della riga di comando)

Tutti gli argomenti si leggono con `Underworld.Core.LaunchArgs.Current`. Non leggere `Environment.GetCommandLineArgs()` altrove.

| Argomento | Chi | Significato |
|---|---|---|
| `-server` | server | Avvia un server senza giocatore locale. Usalo con `-batchmode -nographics`. |
| `-area <AreaId>` | server | Area del server: `Downtown`, `Industrial`, `Suburbs`, `Outskirts`, `TestGround`. |
| `-port <n>` | server | Porta UDP. Default: `AreaInfo.DefaultPort(area)` (7777–7780, TestGround 7790). |
| `-listen <ip>` | server | Indirizzo di ascolto. Default `127.0.0.1` (solo questo PC). `0.0.0.0` per gli amici. |
| `-datadir <dir>` | server | Cartella dei dati del server. Default `Application.persistentDataPath/ServerData`. |
| `-areas <file>` | server | JSON con l'indirizzo di ogni area (per i trasferimenti). |
| `-parentPid <pid>` | server | Se il processo padre muore, il server salva e si chiude. |
| `-testmode` | server | Abilita i comandi di test (`ServerTestHooks`). Mai nelle partite vere. |
| `-connect <ip:porta>` | client | Si collega subito, senza menu. |
| `-name <nome>` | client | Nome del giocatore. |
| `-profile <dir>` | client | Cartella dell'identità del client (più client sullo stesso PC). |
| `-bot <tipo>` | client | Input automatico (`idle`, `walk`, `wander`...). |
| `-autotest <scenario>` | client e server | Esegue uno scenario di test automatico. |
| `-autotest-out <file>` | client e server | Dove scrivere il risultato JSON dello scenario. |
| `-autoshot <file.png>` | client | Screenshot automatico dopo 3 secondi, poi chiude (se non c'è `-autotest`). |

Esempi:
```
UNDERWORLD.exe -server -area Downtown -port 7777 -batchmode -nographics
UNDERWORLD.exe -connect 127.0.0.1:7777 -name Bot1 -profile C:\tmp\bot1 -bot wander -batchmode -nographics
```

## 3. Scene

| Scena | Indice build | Contenuto |
|---|---|---|
| `Bootstrap` | 0 | `NetworkManager` + `UnityTransport` + `GameRoot` (DontDestroyOnLoad). Legge gli argomenti e decide cosa fare. |
| `MainMenu` | 1 | Solo UI del menu. |
| `Area_Downtown`, `Area_Industrial`, `Area_Suburbs`, `Area_Outskirts` | 2+ | Luce, cielo e un `AreaBuilder` che costruisce l'area a runtime. |
| `Area_TestGround` | ultima | Piano piatto e leggero per i test automatici. |

- Il server carica la scena dell'area con `NetworkManager.SceneManager.LoadScene` (scene management attivo). I client la seguono da soli.
- Il nome della scena viene sempre da `AreaInfo.SceneName(area)`.
- Le scene si creano con passi di setup (`IProjectSetupStep`) e si registrano con `ctx.AddBuildScene(path, priorità)`.

## 4. Rete

- **Netcode for GameObjects 2.14** + **Unity Transport 2.7**. Tick rate 30. Massimo 16 giocatori per server.
- Niente modalità host: anche "Gioca" usa un server separato. Il client non è mai autorità.
- **Approvazione della connessione**: `NetworkConfig.ConnectionData` contiene un JSON `ConnectionPayload`:
  ```json
  { "v": 1, "playerId": "guid", "token": "base64", "name": "Nome", "ticket": null }
  ```
  Il server rifiuta con un motivo in italiano (versione diversa, server pieno, nome non valido, già collegato...). Il client mostra il motivo.
- **Prefab del giocatore** `Player`: `NetworkObject`, `NetworkTransform` con `AuthorityMode = Owner`, `ThirdPersonController` attivo solo per il proprietario, `PlayerVisual`, `PlayerNetwork`. Lo crea il server sullo spawn dell'area.
- **Movimento**: il proprietario muove il suo personaggio (risposta immediata). Il server lo controlla con `MovementValidator`: velocità massima (a piedi 8,5 m/s; in auto quella del veicolo + margine) e salti di posizione. Se un controllo fallisce, il server rimette il giocatore nell'ultima posizione valida.
- **RPC** (client → server): `[Rpc(SendTo.Server)]`, nome del metodo che finisce con `Rpc`.
  - Il mittente si legge da `RpcParams.Receive.SenderClientId`. Mai fidarsi di un id passato come parametro.
  - Il client manda solo intenzioni e id (es. "compra l'oggetto `car.sedan`"). Il prezzo lo legge il server dal suo catalogo.
  - Il server controlla sempre: distanza (con la posizione che conosce lui), tempi di attesa, limite di frequenza (`RateLimiter`), proprietà, soldi.
- **Stato visibile solo al proprietario** (es. soldi): `NetworkVariable` con `ReadPermission = Owner`, scrittura solo server.
- **Eventi server → client**: `[Rpc(SendTo.Owner)]` o `SendTo.SpecifiedInParams`.

## 5. Identità e sessione

- Il client crea al primo avvio `identity.json` (in `-profile` o in `persistentDataPath/Client`): `playerId` (GUID) e `token` (32 byte casuali, base64).
- Il server salva solo `SHA256(playerId + ":" + token)` e lo confronta a tempo costante.
- Nome visibile: da 3 a 16 caratteri (lettere, numeri, spazio, `_`, `-`).
- **Lucchetto di sessione**: file `locks/<playerId>.lock` (area, pid del server, ora dell'ultimo battito). Un giocatore può essere su un solo server. Un lucchetto senza battito da più di 15 secondi è scaduto.
- Questo sistema è provvisorio. Il backend porterà un'autenticazione vera (decisione aperta).

## 6. Salvataggi

- Interfaccia `IPlayerStore` (server). Implementazione attuale: `JsonFilePlayerStore`.
- Cartella dati del server (`-datadir`):
  ```
  players/<playerId>.json      un file per giocatore
  clans.json                   clan (modulo Social)
  transactions/<aaaa-mm-gg>.jsonl   registro di ogni movimento di denaro
  locks/<playerId>.lock        lucchetti di sessione
  ```
- Scrittura atomica: scrivi `file.tmp`, poi sostituisci il file vero (`File.Replace` o move).
- Schema `PlayerSave` con `schemaVersion`. Campi base: profilo (nome, data, hash del token, aspetto), portafoglio, inventario, posizione (area + punto), sezione `modules`.
- **Dati dei moduli**: ogni modulo salva i suoi dati in `modules["<chiave>"]` (JSON libero, Newtonsoft). Aiuti: `GetModule<T>(chiave)` e `SetModule<T>(chiave, valore)`. Così un modulo nuovo non cambia lo schema base.
- Quando si salva: dopo ogni cambio importante (con un ritardo di 2 s), ogni 60 s, all'uscita del giocatore, alla chiusura del server, prima di un trasferimento.

## 7. Economia

- Importi in `long` (unità intere di dollari). Mai `float` per i soldi.
- Solo `EconomyService` (server) cambia il saldo: `Credit(player, importo, motivo)` e `TryDebit(player, importo, motivo)`.
- Ogni movimento va nel registro `transactions/*.jsonl` (ora, area, giocatore, importo, saldo dopo, motivo).
- Prezzi e ricompense: nei cataloghi del server (classi statiche per modulo, es. `VehicleCatalog`). Il client riceve una copia solo per mostrare i prezzi.
- Soldi iniziali: 5.000 $. Bilanciamento: simulazione in `Tools/econ-sim` (giocatore "goloso", regola dal vault).

## 8. Interazioni

- `Interactable` (componente non di rete) con `Id` stabile, testo del suggerimento, raggio, tipo. Lo crea il costruttore dell'area.
- Il client mostra "E — testo" per l'oggetto più vicino e manda `InteractRpc(id)`.
- Il server trova lo stesso `Interactable` nella sua copia del mondo, controlla la distanza e chiama il gestore del tipo (`IInteractionHandler`, uno per modulo).
- Interazioni "tieni premuto" (rapine, trapano): il server misura il tempo, non il client.

## 9. Mondo e aree

- Ogni area ha un costruttore (`IAreaBuilder`) che crea il mondo **a runtime** con un seme fisso. Server e client ottengono lo stesso mondo senza mandarlo in rete.
- Le mesh statiche si uniscono per materiale (`MeshCombiner`) per avere poche draw call. Collider semplici (box) a parte.
- Punti speciali: `AreaAnchor` (spawn, arrivi, uscite, negozi, banca, stazione di polizia...). Id nel formato `area.tipo.nome`.
- NavMesh (per la polizia): costruita a runtime solo sul server (`NavMeshSurface`).
- Arte: modelli **Kenney** (CC0) presi dal vault (Car Kit, Factory Kit, Nature Kit) in `Assets/Art/Kenney/`, più forme low poly fatte a codice. Stile "Modern Low Poly": colori pieni, pochi dettagli, luce calda.

## 10. Interfaccia (UI)

- **UI Toolkit** a runtime (`UIDocument` + `PanelSettings` + fogli USS). Testi in italiano.
- Stile "vetro" (preferenza dell'utente, regole del vault): pannelli scuri semitrasparenti, bordo 1 px bianco al 15–20 %, angoli arrotondati, ombra morbida, massimo 1–2 livelli di vetro per schermata, contrasto del testo almeno 4,5:1. Il blur dietro il vetro non c'è in UI Toolkit: si usa il fondo più scuro (regola di ripiego).
- Regole del vault da rispettare: niente gradienti di sfondo, niente sottotitoli tutto maiuscolo, niente punto finale nei titoli, poche pillole e pochi bordi; entrate ease-out 150–250 ms, uscite più corte del 20 %, pulsanti a scala 0,97 quando premuti, niente rimbalzi.
- La logica non chiama la UI: scrive su `HudBus` (soldi, suggerimento, avvisi, stelle ricercato, obiettivo, area).
- Finestre aperte → `InputGate.Block(this)`: il personaggio si ferma e il mouse si libera.

## 11. Codice

- Assembly: `Underworld.Runtime` (Assets/Scripts/Runtime), `Underworld.Editor` (Assets/Scripts/Editor), `Underworld.Tests.EditMode` (Assets/Tests/EditMode).
- Namespace = cartella: `Underworld.Core`, `.Net`, `.Player`, `.World`, `.UI`, `.Server`, `.Economy`, `.Testing`, poi `.Vehicles`, `.Properties`, `.Missions`, `.Police`, `.Social`, `.Areas`.
- Nomi del codice in inglese. Commenti e testi per il giocatore in italiano.
- Codice solo server: dentro `if (IsServer)` o in classi del namespace `Underworld.Server`. Ogni metodo che cambia lo stato controlla di girare sul server.
- Niente `Find` o LINQ nei cicli di ogni frame. Niente stato statico che non si azzera (usa `RuntimeInitializeOnLoadMethod`).
- Numeri casuali del mondo: `System.Random(seme)`. Numeri casuali di gioco: solo sul server.

## 12. Test

Regola: **non dichiarare funzionante una cosa senza un test che l'ha provata.**

1. **Test EditMode** (logica pura: catalogo, salvataggi, economia, parsing): `Assets/Tests/EditMode/`. Comando: `Tools/unity.ps1 -Action editmode`.
2. **Scenari automatici** (rete vera, processi veri): un server e uno o più client con `-autotest`. Comando: `Tools/test.ps1 -Scenario <nome>|all`.
   - Ogni scenario ha un file `Tools/scenarios/<nome>.json` (quali server, quali client, tempo massimo) e una classe `[AutoTestScenario("<nome>")]` in `Runtime/Testing/Scenarios/`.
   - Ogni processo scrive un risultato JSON. Lo scenario passa solo se tutti i processi passano e nessun log contiene eccezioni.
   - Porte e cartelle dati dei test sono sempre temporanee (`-PortBase`, `-datadir`, `-profile`): i test non toccano i salvataggi veri.
3. **Build**: `Tools/unity.ps1 -Action build` → `UnityProject/Builds/Windows/UNDERWORLD.exe`.

Un solo Unity alla volta per ogni copia del progetto.

## 13. Come si aggiunge un modulo (punti di estensione)

Per lavorare in parallelo senza conflitti, un modulo aggiunge file suoi e non modifica i file degli altri.

| Serve | Come |
|---|---|
| Asset, prefab, scene | Una classe `IProjectSetupStep` in `Editor/Steps/<Modulo>/`. Il runner la trova da solo. |
| Prefab di rete | `ctx.RegisterNetworkPrefab(prefab)` nel passo di setup (lista unica: `Assets/DefaultNetworkPrefabs.asset`). |
| Componenti sul giocatore | Un `IPlayerPrefabContributor` (editor) aggiunge i suoi componenti al prefab `Player`. |
| Servizi del server | Una classe `IServerModule` (avvio, arrivo e uscita dei giocatori, chiusura). |
| Dati salvati | `PlayerSave.modules["<chiave>"]` con `GetModule/SetModule`. |
| Prezzi | Un catalogo statico del modulo. |
| Interazioni | Un `IInteractionHandler` per il suo tipo di `Interactable`. |
| HUD | Eventi di `HudBus`, oppure un pannello UI del modulo registrato nella UI. |
| Test | `[AutoTestScenario]` + `Tools/scenarios/<nome>.json` + test EditMode. |

## 14. Stato delle fasi

Vedi `Docs/ROADMAP.md`.

## Note UI

Traccia UI (ramo `feat/a1-ui`). Codice in `Runtime/UI/`, stili e asset in `Assets/UI/`, setup in `Editor/Steps/UI/`.

### Come nasce la UI
- `UiRoot` nasce da solo (`RuntimeInitializeOnLoadMethod`, prima della prima scena) e resta tra le scene (`DontDestroyOnLoad`).
- Non nasce con `-server` né in batchmode senza grafica (`UiRoot.ShouldCreate()`).
- `UIDocument` + `PanelSettings` in `Assets/UI/Resources/Underworld/UiPanelSettings.asset` (creato da `UiAssetsStep`): scala con lo schermo, 1920×1080, match 0,5.
- Tema `Assets/UI/Theme/UnderworldTheme.tss`: tema runtime standard di Unity + `Tokens.uss` (unico posto con colori, spazi 4/8/12/16/24/32, testo 14/16/20/28/40/64, raggi) + `Base.uss` + `Screens.uss`.
- Carattere Inter (SIL OFL 1.1, licenza in `Assets/UI/Fonts/OFL.txt`). `InterTabular-SemiBold` ha le cifre a larghezza fissa (soldi).

### Schermate
- `UiScreenMachine` + `UiModeRules` (logica pura, test `UiScreenMachineTests`): la schermata dipende dalla scena attiva e da `GameServices.Launcher.State`.
  - `InGame` → HUD (sempre). Scena `MainMenu` → menu. Avvio/connessione/trasferimento/errore fuori dal menu → caricamento a schermo pieno. Scena `Area_*` senza launcher → HUD.
- Menu, caricamento e pausa chiamano `InputGate.Block(UiRoot)`. Ogni finestra aperta chiama `InputGate.Block(finestra)`.
- Mouse: con menu, pausa o finestra aperta `UiRoot.LateUpdate` lo libera ogni frame. La camera del gioco deve bloccarlo solo se `InputGate.GameplayBlocked` è falso.
- Esc (o Start del gamepad): chiude la finestra in cima, poi apre/chiude la pausa; nel menu torna alla vista principale.
- Pausa: il mondo non si ferma (gioco in rete). Pulsanti: Riprendi, Torna al menu (`Launcher.Disconnect`), Esci dal gioco (`Launcher.QuitGame`).
- Menu senza launcher (`GameServices.Launcher == null`): i pulsanti di gioco mostrano "Partita non disponibile", nessuna eccezione.
- Nome del giocatore: `PlayerPrefs["underworld.playerName"]`, stesse regole del server (`PlayerNameRules`). Indirizzo e porta di "Unisciti": `underworld.joinAddress`, `underworld.joinPort` (default 7777). Solo IPv4 o `localhost`.

### Come un modulo aggiunge UI (senza toccare i file della UI)
Tutto passa da `UiRegistry` e `UiWindows` (namespace `Underworld.UI`). Si può registrare prima che la UI esista; sul server non costa niente.

```csharp
// 1) Finestra del modulo: una classe che eredita UiWindow (vetro, titolo, X, contenuto che scorre, piè di pagina).
public sealed class GarageWindow : UiWindow
{
    public const string Id = "vehicles.garage";
    public GarageWindow() : base("Garage", 520f)
    {
        Content.Add(UiBuild.Text("Nessun veicolo", UiClasses.Body, UiClasses.Muted));
        AddFooterButton("Chiudi", Close);
        AddFooterButton("Prendi l'auto", OnTake, primary: true);
    }
    protected override void OnOpened() { /* aggiorna i dati */ }
    void OnTake() { /* manda la richiesta al server */ }
}

// 2) Registrazione (una volta, es. all'avvio del modulo sul client).
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
static void RegisterUi()
{
    UiRegistry.RegisterWindow(GarageWindow.Id, () => new GarageWindow());
    UiRegistry.RegisterHudWidget("vehicles.speed", HudSlot.BottomRight, () => new Label("0 km/h"), order: 10);
    // Fogli USS del modulo (facoltativo): UiRegistry.RegisterStyleSheet(Resources.Load<StyleSheet>("Vehicles/Vehicles"));
}

// 3) Apertura (es. dal gestore dell'interazione): UiWindows.Open(GarageWindow.Id); Toggle, Close, IsOpen.
```

- Proprietà di `UiWindow`: `CloseOnEscape`, `BlocksGameplay`, `ShowScrim` (default tutte true), eventi `Opened`/`Closed`.
- Zone dell'HUD (`HudSlot`): `TopLeft` (sotto l'obiettivo), `TopRight` (sotto soldi, stelle, avvisi), `BottomLeft`, `BottomRight`, `BottomCenter` (sopra il suggerimento E).
- Classi utili (`UiClasses`): `uw-glass`, `uw-btn` + `uw-btn--primary|ghost|quiet`, `uw-heading`, `uw-body`, `uw-muted`, `uw-small`, `uw-tabular`, `uw-text-shadow`. Fabbriche in `UiBuild` (Button, Field, Text, Glass, IconButton). Animazioni in `UiAnim` (In, Out, Pulse, StaggerChildren: tempi del vault).
- Regola: al massimo 1–2 pannelli di vetro per schermata; nell'HUD niente vetro dietro ogni elemento.

### HUD (solo da HudBus)
- Soldi in alto a destra: cifre fisse, formato `12.500 $` (`UiFormat`), il numero rotola al cambio, pulse breve, riga `+2.250 $`.
- Stelle ricercato 0–5 (nascoste a 0). Nome dell'area al centro quando cambia (2,8 s). Obiettivo in alto a sinistra (nascosto con null). Suggerimento in basso: tasto `E` + testo (se il testo inizia con "E —" il prefisso si toglie). Avvisi: pila di 3, spariscono da soli (4–6 s).

### Sfondo del menu
- Scena `MainMenu` (priorità di build 10, passo `MainMenuSceneStep`): camera + `MenuBackdrop`, città notturna low poly fatta a codice (seme fisso, finestre accese negli shader, lampioni, luci rosse sulle torri, auto che passano), camera che gira piano.
- 4 shader HLSL per URP 17.3 in `Assets/UI/Backdrop/Shaders` (`Underworld/UI/Skyline*`). `UiAssetsStep` controlla che compilino (`ShaderUtil.ShaderHasError`) e crea i materiali. Provati nel player Windows (screenshot).
- Il preset `StreetDay` (vista da strada di giorno) serve solo all'anteprima dell'HUD.

### Anteprima e screenshot (senza rete)
```
UNDERWORLD.exe -uipreview <modo> [-autoshot file.png] [-autoshot-delay s] -screen-width 1920 -screen-height 1080 -screen-fullscreen 0 -popupwindow -logFile log.txt
```
- Modi: `menu`, `menu-join`, `menu-busy`, `menu-error`, `menu-offline`, `credits`, `hud`, `pause`, `window`.
- Usa `PreviewLauncher` (finto, nessuna rete) e dati finti su `HudBus`. Carica la scena `MainMenu`. Con `-autoshot` salva e chiude.
- Attenzione: se una scena contiene anche `Dev/AutoScreenshot`, anche quello reagisce a `-autoshot` (dopo 3 s chiude il gioco).

### Test
- EditMode `UiFormatTests` (soldi, nome, indirizzo) e `UiScreenMachineTests` (schermate, pausa, Esc, avvisi).

### Da fare / non verificato
- Clic, tastiera ed Esc nel player non sono provati con input automatico: sono provati la logica (test) e l'aspetto (screenshot).
- Il flusso con il launcher vero (rete) non è provato in questo ramo: manca l'implementazione di `IGameLauncher`.
- Navigazione con il gamepad tra i pulsanti: solo il fuoco iniziale nella pausa.
