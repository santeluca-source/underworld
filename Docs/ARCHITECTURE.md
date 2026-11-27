# Architettura di UNDERWORLD

Questo documento è il contratto tecnico del progetto. Ogni modulo lo segue.
Se una scelta cambia, aggiorna questo file e `Docs/DECISIONS.md`.
Ultimo aggiornamento: 2026-11-27, integrazione delle tracce NET, WORLD e UI (Fasi 3 e 4).

## 1. In breve

- Un solo eseguibile: `UNDERWORLD.exe`. Gli argomenti decidono se è client o server.
- Ogni area del mondo gira in un **server separato** (massimo 16 giocatori per server).
- Il **server decide** tutto ciò che conta: soldi, oggetti, proprietà, veicoli posseduti, missioni, polizia, clan.
- Il **client** mostra il mondo, legge l'input e manda **richieste**. Non decide mai un valore economico.
- I salvataggi sono file JSON sul PC del server, dietro l'interfaccia `IPlayerStore` (Fase 5).
  Più avanti un backend (ASP.NET Core + PostgreSQL) implementerà la stessa interfaccia.
- "Play" dal menu avvia da solo il server dell'area sul PC (processo nascosto) e collega il giocatore.
- **Lingua**: tutti i testi per il giocatore sono in inglese (US); codice con nomi in inglese; commenti e documenti in italiano (decisione D12).

## 2. Modi di avvio (argomenti della riga di comando)

Tutti gli argomenti si leggono con `Underworld.Core.LaunchArgs.Current`. Non leggere `Environment.GetCommandLineArgs()` altrove.

| Argomento | Chi | Significato |
|---|---|---|
| `-server` | server | Avvia un server senza giocatore locale. Usalo con `-batchmode -nographics`. |
| `-area <AreaId>` | server | Area del server: `Downtown` (default), `Industrial`, `Suburbs`, `Outskirts`, `TestGround`. |
| `-port <n>` | server | Porta UDP. Default: `AreaInfo.DefaultPort(area)` (7777–7780, TestGround 7790). |
| `-listen <ip>` | server | Indirizzo di ascolto. Default `127.0.0.1` (solo questo PC). `0.0.0.0` per gli amici. |
| `-datadir <dir>` | server | Cartella dei dati del server. Default `Application.persistentDataPath/ServerData`. |
| `-maxplayers <n>` | server | Cambia il limite di 16 (per i test). |
| `-areas <file>` | server | JSON con l'indirizzo di ogni area (per i trasferimenti, Fase 11). |
| `-parentPid <pid>` | server | Se il processo padre muore, il server si chiude (controllo ogni 0,5 s). |
| `-testmode` | server | Abilita i comandi di test (`ServerTestHooks`). Mai nelle partite vere. |
| `-connect <ip:porta>` | client | Si collega subito, senza menu. |
| `-name <nome>` | client | Nome del giocatore. |
| `-profile <dir>` | client | Cartella dell'identità del client (più client sullo stesso PC). |
| `-bot <tipo>` | client | Input automatico (`idle`, `walk`, `wander`). |
| `-localport`, `-localdatadir`, `-localarea` | client | Porta, cartella e area del server figlio di "Play" (test). |
| `-autotest <scenario>` | client e server | Esegue uno scenario di test automatico. |
| `-autotest-out <file>` | client e server | Dove scrivere il risultato JSON dello scenario. |
| `-autotest-timeout <s>`, `-autotest-release <file>` | client e server | Tempo massimo dello scenario; file che libera un client in attesa. |
| `-shots <dir>` | client | Cartella degli screenshot degli scenari. |
| `-autoshot <file.png>` [`-autoshot-delay s`] | client | Screenshot del gioco vero dopo 3 s, poi chiude. Ignorato con `-autotest`, `-uipreview`, `-preview`. |
| `-uipreview <modo>` | client | Anteprima della UI senza rete (sezione 10). |
| `-preview -preview-out <dir>` | client | Anteprima del mondo con screenshot (sezione 9). |

Argomenti di Unity utili: `-screen-width 960 -screen-height 540 -screen-fullscreen 0 -monitor 2 -logFile <file>`.

Esempi:
```
UNDERWORLD.exe -server -area Downtown -port 7777 -batchmode -nographics
UNDERWORLD.exe -connect 127.0.0.1:7777 -name Bot1 -profile C:\tmp\bot1 -bot wander -batchmode -nographics
```

## 3. Scene

| Scena | Priorità di build | Contenuto |
|---|---|---|
| `Bootstrap` | 0 | `GameRoot` + `NetworkManager` + `UnderworldTransport` + `GameLauncher` (DontDestroyOnLoad). Legge gli argomenti e decide cosa fare. |
| `MainMenu` | 10 | Camera + `MenuBackdrop` (città notturna fatta a codice). Il menu lo disegna `UiRoot`. |
| `Area_Downtown` | 100 | Luce, cielo, camera e un `AreaBuilder` che costruisce l'area a runtime. |
| `Area_TestGround` | 900 | Piano piatto e leggero per i test automatici. |

- Le scene si creano con passi di setup (`IProjectSetupStep`) e si registrano con `ctx.AddBuildScene(path, priorità)`; l'ordine della build segue la priorità. Dopo un merge: `Tools/unity.ps1 -Action setup`.
- Il server carica la scena dell'area con `NetworkManager.SceneManager.LoadScene` (scene management attivo). I client la seguono da soli.
- Il nome della scena viene sempre da `AreaInfo.SceneName(area)` (`Area_<AreaId>`). Il nome per il giocatore da `AreaInfo.DisplayName(area)`.
- Le aree future (`Area_Industrial`, `Area_Suburbs`, `Area_Outskirts`) useranno priorità tra 100 e 900.

## 4. Rete

### Avvio (scena Bootstrap)
- `GameRoot` (Runtime/Net) vive sullo stesso oggetto di `NetworkManager`, `UnderworldTransport` e `GameLauncher`. Una seconda copia si spegne da sola in `Awake`. `GameServices.Launcher` punta al `GameLauncher`.
- Rete: **Netcode for GameObjects 2.14** + **Unity Transport 2.7**, tick 30, approvazione accesa, gestione delle scene accesa, lista prefab `Assets/DefaultNetworkPrefabs.asset`, nessun `PlayerPrefab` (lo spawn lo fa il server).
- `-server` → `ServerRuntime`; `-connect ip:porta` → `GameLauncher.Join`; altrimenti scena `MainMenu`. `-autotest` → `AutoTestRunner`.
- `UnderworldTransport`: con un server su 127.0.0.1 il client apre la porta solo su 127.0.0.1 (niente avviso del firewall nei test).
- Niente modalità host: anche "Play" usa un server separato. Il client non è mai autorità.

### Server (`ServerRuntime`)
- Righe di log con `[Server]`. Pronto: riga `[Server] Pronto` e file `<datadir>/server.ready` (JSON con pid, area, porta).
- Chiusura pulita: evento `ServerRuntime.ServerStopping` (Fase 5: salvataggi qui), poi `IServerModule.OnServerStopping`, poi giocatori scollegati con il motivo `server_closing`, poi uscita.
- Motivi di chiusura: processo padre chiuso (`-parentPid`), file `<datadir>/server.stop` (test e launcher), uscita dell'applicazione.
- Area non giocabile = errore fatale: se `AreaBuilder` non ha costruito il mondo (eccezione nel costruttore, ArtLibrary mancante) o non c'è nessun punto `Spawn`, il server scrive l'errore, **non** scrive `server.ready` e si chiude con codice 1 (`ServerRuntime.IsAreaPlayable`). Il mondo a metà viene tolto e le sue risorse liberate.
- Spawn: punto `AreaAnchor` di tipo `Spawn` più libero (`SpawnSelector`), dopo la sincronizzazione della scena del client (`OnSynchronizeComplete`). In Downtown sono `downtown.spawn.0..7` intorno alla fontana della piazza.

### Approvazione della connessione
- `NetworkConfig.ConnectionData` contiene un JSON `ConnectionPayload`:
  ```json
  { "v": 1, "playerId": "guid", "token": "base64", "name": "Name", "ticket": null }
  ```
- Ordine dei controlli (`ConnectionApprovalRules`, logica pura): dimensione (max 2 KB), JSON, versione, identità (GUID + token di 32 byte), nome, server pieno, stesso playerId già dentro, poi i validatori dei moduli (`ServerApproval.AddValidator(IConnectionValidator)`).
- Motivi nel formato `codice|testo` (`DisconnectReasons`): il codice per il programma e i test, il testo **in inglese** per il giocatore (es. `server_full|Server is full (16/16). Try again later.`). Il client mostra solo il testo (`GameLauncher.StatusText`, `LastReasonCode`, `LastReasonText`).
- Punti di estensione per la Fase 5: `ServerPlayerRegistry.PlayerJoined/PlayerLeft` con `ServerPlayerEntry` (clientId, playerId, nome, ticket, personaggio); `IServerModule` (creato da solo all'avvio del server).

### Launcher (`GameLauncher`, interfaccia `IGameLauncher` in `GameServices.Launcher`)
- Stati: `Idle`, `StartingServer`, `Connecting`, `InGame`, `Transferring`, `Failed`. `StatusText` è sempre in inglese, pronto per la UI.
- `PlayLocal`: avvia lo stesso exe come server figlio (`-server -area Downtown -port <p> -listen 127.0.0.1 -parentPid <pid> -datadir <dir> -batchmode -nographics -logFile <dir>/server.log`), aspetta `server.ready`, poi si collega con nuovi tentativi (attese 0,5 → 3 s) fino a 25 s. Si riprova solo senza risposta, con `server_starting` o con `already_connected` (sessione vecchia dopo un crash: l'attesa si allunga fino a 35 s, il server la chiude dopo il timeout del transport); un rifiuto vero ferma subito. Il testo del motivo mandato dal server è ripulito (niente `<` `>`, al massimo 200 caratteri) e le etichette di stato non usano il rich text. Gli argomenti del server figlio sono sempre tra virgolette con le regole di Windows (`QuoteArg`).
- `HostForFriends`: come PlayLocal ma `-listen 0.0.0.0`. In gioco l'indirizzo `ip:porta` per gli amici si vede in un toast all'arrivo e sempre nel menu di pausa (`HudBus.HostAddress`). Non è nei test automatici (aprirebbe l'avviso del firewall).
- `Disconnect` ("Main menu" nella pausa, "Cancel" nel menu): ferma il client e torna al menu. Se Netcode stava ancora caricando l'area, il launcher torna al menu quando l'area si attiva (`GameLauncher.ShouldLeaveArea`); un'area senza partita con il launcher presente mostra l'attesa, non l'HUD. Il server figlio resta acceso e il prossimo PlayLocal lo riusa. Il server figlio si chiude quando il launcher sparisce: uscita dal gioco (`OnApplicationQuit`), fine del Play nell'editor (`ExitingPlayMode`) con `server.stop` e poi Kill dopo 3 s; `-parentPid` resta come rete di sicurezza (nell'editor il padre è Unity, che resta aperto). Se cambiano modo o porta, il vecchio server riceve `server.stop` (poi Kill dopo 5 s). Prima di avviare un server nuovo, un server rimasto da prima (pid e porta letti dal suo `server.ready`, stesso nome dell'exe) viene chiuso.
- Una disconnessione durante la partita mostra il titolo "Disconnected" (`IGameLauncher.DisconnectedInGame`), non "Connection failed".
- Nell'editor PlayLocal usa `Builds/Windows/UNDERWORLD.exe`.

### Regole delle RPC
- **RPC** (client → server): `[Rpc(SendTo.Server)]`, nome del metodo che finisce con `Rpc`.
  - Il mittente si legge da `RpcParams.Receive.SenderClientId`. Mai fidarsi di un id passato come parametro.
  - Il client manda solo intenzioni e id (es. "compra l'oggetto `car.sedan`"). Il prezzo lo legge il server dal suo catalogo.
  - Il server controlla sempre: distanza (con la posizione che conosce lui), tempi di attesa, limite di frequenza (`RateLimiter`, secchio di gettoni per client e chiave), proprietà, soldi.
- **Stato visibile solo al proprietario** (es. soldi): `NetworkVariable` con `ReadPermission = Owner`, scrittura solo server.
- **Eventi server → client**: `[Rpc(SendTo.Owner)]` o `SendTo.SpecifiedInParams`.

## 5. Giocatore

- Prefab `Assets/Prefabs/Player.prefab` creato da `PlayerPrefabStep` (ordine 250): `NetworkObject`, `NetworkTransform` con autorità del proprietario (posizione xyz + rotazione y, interpolazione), `ThirdPersonController`, `PlayerVisual`, `PlayerNetwork`, `MovementValidator`. Altri moduli aggiungono componenti con `IPlayerPrefabContributor` (Editor/Steps/Player).
- Solo il proprietario ha `CharacterController` e `ThirdPersonController` accesi; le copie remote e quelle del server usano un `CapsuleCollider` semplice.
- `PlayerNetwork`: nome in una `NetworkVariable` scritta dal server; targhetta col nome sopra gli altri giocatori; `SpawnPosition` (scelta dal server); collega la camera della scena (`ThirdPersonCamera.AttachToMainCamera`, la crea se manca).
- **Regola**: `Physics.autoSyncTransforms` è spento. Dopo ogni spostamento diretto del transform di un personaggio chiama `ThirdPersonController.SyncCharacterController(cc)` (o spegni il controller durante lo spostamento). Senza questo il personaggio può tornare in (0,0,0) (bug risolto il 2026-11-27, test `PlayerSpawnSyncTests`).
- Comandi: `IPlayerInput` (`KeyboardMouseInput` per le persone, `BotInput` per bot e test). Con `InputGate.GameplayBlocked` il controller ignora i comandi e la camera libera il mouse.
- Camera (`ThirdPersonCamera`): segue il personaggio locale, blocca il mouse solo se `InputGate.GameplayBlocked` è falso, **non usa Esc** (Esc è della UI). `SetView(yaw, pitch)` gira la camera (test, filmati).
- `MovementValidator` (server): controlla ogni posizione ricevuta dal proprietario.
  - Posizione finita (niente NaN o infinito) e dentro ±10 km: altrimenti violazione; un valore così non diventa mai l'ultima posizione valida.
  - Credito di movimento (`MovementBudget`): a piedi 8,5 m/s, salita 6 m/s, discesa 55 m/s (al massimo 18 m in un colpo), 1,5 s di credito per la rete a scatti.
  - Volo e caduta (`AirborneTracker`, spherecast del server sotto i piedi): al massimo 2,5 m sopra l'ultimo suolo; dopo 1 s in aria deve scendere (gravità minima 8 m/s²); niente discese più veloci di una caduta vera; sotto `minY` (-100 m) = fuori mappa.
  - Violazione → log, `CorrectPositionRpc` all'ultima posizione valida (o all'ultimo suolo per il volo). La RPC è `[Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]` e il proprietario controlla anche `SenderClientId == ServerClientId`: un client non può spostare un altro giocatore.
  - Durante una correzione le posizioni ricevute non contano: per il gioco del server vale `AuthoritativePosition` (il NetworkTransform continua a mostrare agli altri quella del client finché non torna). Chi non torna entro circa 1,8 s (3 × 0,6 s) o fa 4 violazioni in 30 s (finestra scorrevole, non si azzera tornando al punto) → disconnessione `movement_invalid`.
  - API (solo server): `SetSpeedAllowance(chiave, orizzontale, salita, discesa, allowFlight)` / `ClearSpeedAllowance` (veicoli, scale), `ServerTeleport`.

## 6. Identità e sessione

- Il client crea al primo avvio `identity.json` (in `-profile` o in `persistentDataPath/Client`): `playerId` (GUID) e `token` (32 byte casuali, base64).
- Nome visibile: da 3 a 16 caratteri (lettere, numeri, spazio, `_`, `-`). Stessa regola nel server (`Net/PlayerNameRules`) e nella UI (`UI/UiFormat` → `PlayerNameRules`).
- Fase 5 (da fare): il server salva solo `SHA256(playerId + ":" + token)` e lo confronta a tempo costante. **Lucchetto di sessione**: file `locks/<playerId>.lock` (area, pid del server, ora dell'ultimo battito); un lucchetto senza battito da più di 15 s è scaduto.
- Questo sistema è provvisorio. Il backend porterà un'autenticazione vera (decisione aperta).

## 7. Salvataggi (Fase 5)

- Interfaccia `IPlayerStore` (server). Implementazione prevista: `JsonFilePlayerStore`. Oggi il server non salva niente.
- Cartella dati del server (`-datadir`):
  ```
  players/<playerId>.json      un file per giocatore
  clans.json                   clan (modulo Social)
  transactions/<aaaa-mm-gg>.jsonl   registro di ogni movimento di denaro
  locks/<playerId>.lock        lucchetti di sessione
  ```
- Scrittura atomica: scrivi `file.tmp`, poi sostituisci il file vero (`File.Replace` o move).
- Schema `PlayerSave` con `schemaVersion`. Campi base: profilo (nome, data, hash del token, aspetto), portafoglio, inventario, posizione (area + punto), sezione `modules`.
- **Dati dei moduli**: ogni modulo salva i suoi dati in `modules["<chiave>"]` (JSON libero, Newtonsoft). Aiuti: `GetModule<T>(chiave)` e `SetModule<T>(chiave, valore)`.
- Quando si salva: dopo ogni cambio importante (con un ritardo di 2 s), ogni 60 s, all'uscita del giocatore, alla chiusura del server (`ServerRuntime.ServerStopping`), prima di un trasferimento.

## 8. Economia (Fase 6)

- Importi in `long` (unità intere di dollari). Mai `float` per i soldi.
- Solo `EconomyService` (server) cambia il saldo: `Credit(player, importo, motivo)` e `TryDebit(player, importo, motivo)`.
- Ogni movimento va nel registro `transactions/*.jsonl` (ora, area, giocatore, importo, saldo dopo, motivo).
- Prezzi e ricompense: nei cataloghi del server (classi statiche per modulo, es. `VehicleCatalog`). Il client riceve una copia solo per mostrare i prezzi.
- Soldi iniziali: $5,000. Formato per il giocatore: `UiFormat.Money(12500)` → `$12,500`, `MoneyDelta(-300)` → `-$300`.
- Bilanciamento: simulazione in `Tools/econ-sim` (giocatore "goloso", regola dal vault).

## 9. Mondo e aree

Codice in `Runtime/World/`, setup in `Editor/Steps/World/`.

**Arte**
- Kit Kenney (CC0) in `Assets/Art/Kenney/` (CarKit, FactoryKit, NatureKit), crediti in `Assets/Art/Kenney/CREDITS.md` e nella finestra Credits del gioco.
- `KenneyImport` (AssetPostprocessor) e `WorldArtStep` (setup, ordine 150) creano materiali URP in `Assets/Art/World/Materials` (niente rosa).
- `Resources/World/ArtLibrary.asset`: un elemento per modello con id stabile (es. `car.sedan`), bounds misurati e scala alla misura reale (auto ~4,3 m).
- Stile "Modern Low Poly": colori pieni, pochi dettagli, luce calda.

**Costruzione**
- Ogni area ha un costruttore: `[AreaBuilderFor(AreaId.X, Seed = n)] public sealed class XBuilder : IAreaBuilder`. Il registro (`AreaBuilderRegistry`) lo trova per riflessione: chi aggiunge un'area non modifica i file di WORLD.
- Il mondo nasce **a runtime** con un seme fisso: server e client ottengono lo stesso mondo senza mandarlo in rete (stesso hash, controllato dai test).
- `BuildContext`: `Box`, `Prism`, `Model(id, ...)`, `Collider`, `Anchor(nome, tipo, pos, yaw, raggio, data)`, `Light`, `Text`, `PreviewShot`; in `CityKit`: `Sign`, `Gateway`, `Room`, `Bench`... Il generatore casuale è `ctx.Rng`: non usare `UnityEngine.Random`.
- **Scritte nel mondo**: in inglese, maiuscole, con `PixelFont` (A–Z, 0–9, `> < ^ - . $` e spazio). Una lettera senza glifo non si vede: aggiungi il glifo prima di usarla.
- `MeshCombiner` unisce le mesh per materiale in pezzi di ~64 m (indici a 32 bit). Collider semplici (box) a parte.
- `AreaBuilder` (nella scena) costruisce in `Awake`, poi lancia l'evento statico `AreaBuilder.WorldBuilt`. Per chi arriva dopo: `AreaBuilder.WhenBuilt(callback)`.
- Server headless (`-server` o `-nographics`): solo collider e punti speciali. NavMesh solo sul server, dopo il mondo.
- Log: `[World] Built Downtown in X ms: renderers=..., colliders=..., anchors=..., hash=...` e `[World] NavMesh Downtown in X ms`.

**Punti speciali** (`AreaAnchor`, `Runtime/Core/AreaAnchor.cs`)
- Id nel formato `<area>.<tipo>.<nome>` (es. `downtown.bank.vault`). Id duplicato = eccezione. Ricerca: `AreaAnchors.Find(id)`, `OfKind(kind)`.

**Downtown** (`Areas/DowntownBuilder.cs`, seme 1337, ~300 × 300 m)
- Piazza con fontana al centro, bacheca MISSIONS, BANK con VAULT, POLICE, DEALERSHIP, MARKET, FASHION, PARKING, fermata BUS, grattacielo UNDERWORLD.
- Uscite visibili (portali con scritta): INDUSTRIAL a est, SUBURBS a nord, OUTSKIRTS a sud; `Data` = AreaId di arrivo. Muri invisibili sul bordo, aperti alle uscite.
- 42 punti speciali: spawn.0..7, arrival.from_*, exit.*, police.station/spawn.0..3/release, bank.lobby/vault, dealership.main/display.0..3/exit, shop.general, robbery.store0, clothing.main, garage.public/spawn.0..3, property.apt0..3, mission.board, hideout.drop.

**Anteprima del mondo**
- `UNDERWORLD.exe -preview -preview-out <cartella> [-area Downtown] [-preview-shots a,b] [-preview-navmesh] -screen-width 1280 -screen-height 720 -screen-fullscreen 0 -monitor 2 -logFile <file>`
- Salva un PNG per ogni `PreviewShot` dell'area e scrive nel log batch, triangoli e FPS, poi chiude.

## 10. Interfaccia (UI)

Codice in `Runtime/UI/`, stili e asset in `Assets/UI/`, setup in `Editor/Steps/UI/`.

**Base**
- **UI Toolkit** a runtime. `UiRoot` nasce da solo (`RuntimeInitializeOnLoadMethod`) e resta tra le scene. Non nasce con `-server` né in batchmode senza grafica (`UiRoot.ShouldCreate()`).
- `PanelSettings` in `Assets/UI/Resources/Underworld/UiPanelSettings.asset`: scala con lo schermo, 1920×1080, match 0,5.
- Tema `Assets/UI/Theme/UnderworldTheme.tss` + `Tokens.uss` (unico posto con colori, spazi 4/8/12/16/24/32, testo 14/16/20/28/40/64, raggi) + `Base.uss` + `Screens.uss`.
- Carattere Inter (SIL OFL 1.1, licenza in `Assets/UI/Fonts/OFL.txt`). `InterTabular-SemiBold` ha le cifre a larghezza fissa (soldi).
- Stile "vetro" (regole del vault): pannelli scuri semitrasparenti, bordo 1 px bianco al 15–20 %, angoli arrotondati, massimo 1–2 livelli di vetro per schermata, contrasto del testo almeno 4,5:1. Il progetto è in spazio colore Linear: le alfa dei token di vetro e velo sono già corrette per la miscela lineare (`Tokens.uss`); il testo che informa usa almeno 16 punti di riferimento (a 1280x720 la scala è 0,67). Niente gradienti di sfondo, niente sottotitoli tutto maiuscolo, niente punto finale nei titoli; entrate ease-out 150–250 ms, uscite più corte del 20 %, pulsanti a scala 0,97 quando premuti.

**Schermate** (`UiScreenMachine` + `UiModeRules`, logica pura)
- `InGame` → HUD. Scena `MainMenu` → menu. Avvio/connessione/trasferimento/errore fuori dal menu → caricamento a schermo pieno. Scena `Area_*` senza launcher → HUD.
- Menu: campo "Your name", Play, Play with friends, Join (IP e porta), Credits, Quit; viste di avvio, errore (Retry/Back) e "Game not available" se `GameServices.Launcher` è null. Cancel chiama `Launcher.Disconnect()`.
- Menu, caricamento e pausa chiamano `InputGate.Block(UiRoot)`; ogni finestra aperta `InputGate.Block(finestra)`. Con menu, pausa o finestra aperta `UiRoot.LateUpdate` libera il mouse.
- **Esc** (o Start del gamepad) lo gestisce solo `UiRoot`: chiude la finestra in cima, poi apre/chiude la pausa; nel menu torna alla vista principale.
- Pausa ("Paused"): il mondo non si ferma (gioco in rete). Pulsanti: Resume, Main menu (`Launcher.Disconnect`), Quit game (`Launcher.QuitGame`).
- Nome del giocatore: `PlayerPrefs["underworld.playerName"]`. Indirizzo e porta di Join: `underworld.joinAddress`, `underworld.joinPort` (default 7777). Solo IPv4 o `localhost`.

**HUD** (solo da `HudBus`: la logica non chiama la UI)
- Soldi in alto a destra (`$` + cifre fisse, il numero rotola al cambio, riga `+$2,250`). Stelle ricercato 0–5 (nascoste a 0). Nome dell'area al centro quando cambia (2,8 s). Obiettivo in alto a sinistra (nascosto con null). Suggerimento in basso: tasto `E` + testo. Avvisi: pila di 3, spariscono da soli (4–6 s).

**Come un modulo aggiunge UI** (senza toccare i file della UI)
```csharp
public sealed class GarageWindow : UiWindow
{
    public const string Id = "vehicles.garage";
    public GarageWindow() : base("Garage", 520f)
    {
        Content.Add(UiBuild.Text("No vehicles yet", UiClasses.Body, UiClasses.Muted));
        AddFooterButton("Close", Close);
        AddFooterButton("Take the car", OnTake, primary: true);
    }
    protected internal override void OnOpened() { /* aggiorna i dati */ }
    void OnTake() { /* manda la richiesta al server */ }
}

[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
static void RegisterUi()
{
    UiRegistry.RegisterWindow(GarageWindow.Id, () => new GarageWindow());
    UiRegistry.RegisterHudWidget("vehicles.speed", HudSlot.BottomRight, () => new Label("0 mph"), order: 10);
}
// Apertura: UiWindows.Open(GarageWindow.Id); Toggle, Close, IsOpen.
```
- `UiWindow`: `CloseOnEscape`, `BlocksGameplay`, `ShowScrim` (default true), eventi `Opened`/`Closed`.
- Zone dell'HUD (`HudSlot`): `TopLeft`, `TopRight`, `BottomLeft`, `BottomRight`, `BottomCenter`.
- Classi (`UiClasses`): `uw-glass`, `uw-btn` + `uw-btn--primary|ghost|quiet`, `uw-heading`, `uw-body`, `uw-muted`, `uw-small`, `uw-tabular`, `uw-text-shadow`. Fabbriche in `UiBuild`; animazioni in `UiAnim`.

**Sfondo del menu**
- `MenuBackdrop`: città notturna low poly fatta a codice (seme fisso), 4 shader HLSL per URP 17.3 in `Assets/UI/Backdrop/Shaders`. `UiAssetsStep` controlla che compilino e crea i materiali.

**Anteprima della UI** (senza rete)
```
UNDERWORLD.exe -uipreview <modo> [-autoshot file.png] [-autoshot-delay s] -screen-width 1920 -screen-height 1080 -screen-fullscreen 0 -monitor 2 -logFile log.txt
```
- Modi: `menu`, `menu-join`, `menu-busy`, `menu-error`, `menu-offline`, `credits`, `hud`, `pause`, `window`. Usa `PreviewLauncher` (finto) e dati finti su `HudBus`.

## 11. Interazioni (Fase 7+)

- `Interactable` (componente non di rete) con `Id` stabile, testo del suggerimento (in inglese), raggio, tipo. Lo crea il costruttore dell'area.
- Il client mostra "E + testo" per l'oggetto più vicino e manda `InteractRpc(id)`.
- Il server trova lo stesso `Interactable` nella sua copia del mondo, controlla la distanza e chiama il gestore del tipo (`IInteractionHandler`, uno per modulo).
- Interazioni "tieni premuto" (rapine, trapano): il server misura il tempo, non il client.

## 12. Codice

- Assembly: `Underworld.Runtime` (Assets/Scripts/Runtime), `Underworld.Editor` (Assets/Scripts/Editor), `Underworld.Tests.EditMode` (Assets/Tests/EditMode).
- Namespace = cartella: `Underworld.Core`, `.Net`, `.Player`, `.World`, `.UI`, `.Dev`, `.Testing`, poi `.Server`, `.Economy`, `.Vehicles`, `.Properties`, `.Missions`, `.Police`, `.Social`, `.Areas`.
- Nomi del codice in inglese. Commenti e documenti in italiano. **Testi per il giocatore in inglese (US)**; log e nomi dei controlli dei test possono restare in italiano.
- Codice solo server: dentro `if (IsServer)` o in classi del namespace `Underworld.Server`. Ogni metodo che cambia lo stato controlla di girare sul server.
- Niente `Find` o LINQ nei cicli di ogni frame. Niente stato statico che non si azzera (usa `RuntimeInitializeOnLoadMethod`).
- Numeri casuali del mondo: `System.Random(seme)`. Numeri casuali di gioco: solo sul server.
- Strumenti di sviluppo in `Runtime/Dev`: solo `AutoScreenshot` (il vecchio `DevHud` IMGUI e la scena `Downtown_Test` sono stati tolti).

## 13. Test

Regola: **non dichiarare funzionante una cosa senza un test che l'ha provata.**

1. **Test EditMode** (logica pura): `Assets/Tests/EditMode/`. Comando: `Tools/unity.ps1 -Action editmode`. Gruppi: rete (`NetApprovalTests`, `NetRulesTests`, `PlayerMovementTests`, `PlayerSpawnSyncTests`), mondo (`WorldTests`: determinismo, headless senza renderer, id, punti richiesti, uscite, punti calpestabili), UI (`UiFormatTests`, `UiScreenMachineTests`).
2. **Scenari automatici** (rete vera, processi veri): `Tools/test.ps1 -Scenario <nome>|all [-PortBase 7700] [-SkipBuild] [-TimeoutSec n] [-Exe percorso] [-Repeat n]` (PowerShell 5.1). Senza `-SkipBuild` fa prima la build.
   - Ogni scenario ha un file `Tools/scenarios/<nome>.json` (server, client, tempo massimo) e classi `[AutoTestScenario("<scenario>.<ruolo>")]` + `IAutoTestScenario` in `Runtime/Testing/Scenarios/`.
   - `AutoTestContext`: `Check`, `WaitUntil`, `Wait`, `WaitForLocalPlayer`, `WaitForRelease`, `Log`, `SetData`, `LocalPlayer`, `Bot`, `Launcher`, `Server`.
   - Ogni processo scrive un risultato JSON (`-autotest-out`) e `<out>.ingame` quando il personaggio è pronto. Lo scenario passa solo se tutti i processi passano e nessun log contiene eccezioni.
   - Porte (`PortBase + 10 × indice`) e cartelle (`%TEMP%\underworld-tests\<data>\<scenario>`) sempre temporanee: i test non toccano i salvataggi veri. I client con grafica partono in finestra 960×540 sullo **schermo 2** (`-monitor 2`).
   - Scenari: `connect_two_clients`, `reject_bad_name`, `server_full`, `speed_hack_corrected`, `local_play_launcher`, `bot_wander`, `downtown_two_players` (server Downtown + A con grafica + B bot: spawn, si vedono, A va verso la bacheca, screenshot, FPS/batch, Esc → pausa; il server misura memoria e CPU), `menu_play_local` (menu vero → Play → server figlio Downtown → InGame con HUD; screenshot).
   - Il trucco di velocità (`ThirdPersonController.DebugSpeedMultiplier`) è `internal` e lo usa solo `speed_hack_corrected`.
3. **Build**: `Tools/unity.ps1 -Action build` → `UnityProject/Builds/Windows/UNDERWORLD.exe`.

Un solo Unity alla volta per ogni copia del progetto. Chiudi solo i processi che hai avviato tu, per PID.

## 14. Come si aggiunge un modulo (punti di estensione)

Per lavorare in parallelo senza conflitti, un modulo aggiunge file suoi e non modifica i file degli altri.

| Serve | Come |
|---|---|
| Asset, prefab, scene | Una classe `IProjectSetupStep` in `Editor/Steps/<Modulo>/`. Il runner la trova da solo. |
| Prefab di rete | `ctx.RegisterNetworkPrefab(prefab)` nel passo di setup (lista unica: `Assets/DefaultNetworkPrefabs.asset`). |
| Componenti sul giocatore | Un `IPlayerPrefabContributor` (editor) aggiunge i suoi componenti al prefab `Player`. |
| Servizi del server | Una classe `IServerModule` (avvio, arrivo e uscita dei giocatori, chiusura). |
| Controlli all'ingresso | `ServerApproval.AddValidator(IConnectionValidator)`. |
| Nuova area | Un `IAreaBuilder` con `[AreaBuilderFor]` + una scena `Area_<Id>` creata da un passo di setup. |
| Punti nel mondo | `ctx.Anchor(...)` nel costruttore dell'area; lettura con `AreaAnchors.Find`. Mondo pronto: `AreaBuilder.WhenBuilt`. |
| Dati salvati | `PlayerSave.modules["<chiave>"]` con `GetModule/SetModule`. |
| Prezzi | Un catalogo statico del modulo. |
| Interazioni | Un `IInteractionHandler` per il suo tipo di `Interactable`. |
| HUD e finestre | Eventi di `HudBus`; `UiRegistry.RegisterWindow` / `RegisterHudWidget` / `RegisterStyleSheet`. |
| Test | `[AutoTestScenario]` + `Tools/scenarios/<nome>.json` + test EditMode. |

## 15. Problemi noti

- Il controllo di velocità lascia passare circa 1 s di trucco (credito di 14 m) prima di correggere. Da stringere in Fase 12 se serve.
- Durante una correzione (al massimo circa 1,8 s) gli altri client vedono ancora la posizione mandata dal trucco: il server non blocca il NetworkTransform del proprietario. I moduli del server devono usare `MovementValidator.AuthoritativePosition`.
- Il server non salva ancora niente e i soldi dell'HUD sono $0 (Fasi 5 e 6).
- Il tunnel dentro le uscite è quasi nero (solo aspetto). Le piattaforme dell'autosalone sono vuote (le auto arriveranno dal modulo veicoli tramite `downtown.dealership.display.0..3`).
- Navigazione con il gamepad tra i pulsanti della UI: solo il fuoco iniziale nella pausa.
- Clic veri del mouse e digitazione nel campo del nome non sono provati da input automatico: lo scenario `menu_play_local` scrive nel campo e preme Play con un evento UI (`NavigationSubmitEvent`); Esc è provato con eventi veri dell'Input System.

## 16. Stato delle fasi

Vedi `Docs/ROADMAP.md`.
