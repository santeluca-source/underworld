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
  Il server rifiuta con un motivo in inglese, da mostrare al giocatore (versione diversa, server pieno, nome non valido, già collegato...). Il client mostra il motivo.
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

- **UI Toolkit** a runtime (`UIDocument` + `PanelSettings` + fogli USS). **Testi del gioco in inglese** (menu, HUD, finestre, cartelli, messaggi). Soldi nel formato inglese: `$12,500`.
- Stile "vetro" (preferenza dell'utente, regole del vault): pannelli scuri semitrasparenti, bordo 1 px bianco al 15–20 %, angoli arrotondati, ombra morbida, massimo 1–2 livelli di vetro per schermata, contrasto del testo almeno 4,5:1. Il blur dietro il vetro non c'è in UI Toolkit: si usa il fondo più scuro (regola di ripiego).
- Regole del vault da rispettare: niente gradienti di sfondo, niente sottotitoli tutto maiuscolo, niente punto finale nei titoli, poche pillole e pochi bordi; entrate ease-out 150–250 ms, uscite più corte del 20 %, pulsanti a scala 0,97 quando premuti, niente rimbalzi.
- La logica non chiama la UI: scrive su `HudBus` (soldi, suggerimento, avvisi, stelle ricercato, obiettivo, area).
- Finestre aperte → `InputGate.Block(this)`: il personaggio si ferma e il mouse si libera.

## 11. Codice

- Assembly: `Underworld.Runtime` (Assets/Scripts/Runtime), `Underworld.Editor` (Assets/Scripts/Editor), `Underworld.Tests.EditMode` (Assets/Tests/EditMode).
- Namespace = cartella: `Underworld.Core`, `.Net`, `.Player`, `.World`, `.UI`, `.Server`, `.Economy`, `.Testing`, poi `.Vehicles`, `.Properties`, `.Missions`, `.Police`, `.Social`, `.Areas`.
- Nomi del codice in inglese. Commenti e documenti in italiano. **Testi per il giocatore in inglese** (UI, cartelli nel mondo, motivi di rifiuto, avvisi).
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

## Note NET

Note della traccia NET (rete, giocatore, test automatici = Fase 4). Scritte il 2026-11-26.

### Avvio (scena Bootstrap)
- `GameRoot` (Runtime/Net) è sullo stesso oggetto di `NetworkManager`, `UnderworldTransport` e `GameLauncher`. DontDestroyOnLoad; una seconda copia si spegne da sola in `Awake`.
- Rete: tick 30, approvazione accesa, gestione delle scene accesa, lista prefab `Assets/DefaultNetworkPrefabs.asset`, nessun `PlayerPrefab` (lo spawn lo fa il server).
- `-server` → `ServerRuntime`; `-connect ip:porta` → `GameLauncher.Join`; altrimenti scena `MainMenu` se è nella build (se manca: avviso e resta). `-autotest` → `AutoTestRunner`.
- `UnderworldTransport`: con un server su 127.0.0.1 il client apre la porta solo su 127.0.0.1 (niente avviso del firewall nei test).

### Server (`ServerRuntime`)
- Righe di log con `[Server]`. Pronto: riga `[Server] Pronto` e file `<datadir>/server.ready` (JSON con pid, area, porta).
- Chiusura pulita: evento `ServerRuntime.ServerStopping` (Fase 5: salvataggi qui), poi `IServerModule.OnServerStopping`, poi giocatori scollegati con il motivo `server_closing`, poi uscita.
- Motivi di chiusura: processo padre chiuso (`-parentPid`, controllo ogni 0,5 s), file `<datadir>/server.stop` (test e launcher), uscita dell'applicazione.
- `-maxplayers <n>` cambia il limite di 16 (per i test). Spawn: `AreaAnchor` di tipo `Spawn`, il più libero (`SpawnSelector`), dopo la sincronizzazione della scena del client (`OnSynchronizeComplete`).

### Approvazione e identità (sezione 5, prima parte)
- Ordine dei controlli (`ConnectionApprovalRules`, logica pura): dimensione (max 2 KB), JSON, versione, identità (GUID + token di 32 byte), nome, server pieno, stesso playerId già dentro, poi i validatori dei moduli.
- Motivi nel formato `codice|testo` (`DisconnectReasons`): il codice per il programma e i test, il testo in italiano per il giocatore. Il client mostra solo il testo (`GameLauncher.StatusText`, `LastReasonCode`, `LastReasonText`).
- Punti di estensione per la Fase 5: `ServerApproval.AddValidator(IConnectionValidator)` (hash del token, lucchetto di sessione, ban); `ServerPlayerRegistry.PlayerJoined/PlayerLeft` con `ServerPlayerEntry` (clientId, playerId, nome, ticket, personaggio); `IServerModule` (creato da solo all'avvio del server).
- Il server non salva ancora niente: hash del token, lucchetti e salvataggi sono della Fase 5.

### Launcher (`GameLauncher`, in `GameServices.Launcher`)
- `PlayLocal`: avvia lo stesso exe come server figlio (`-server -area Downtown -port <p> -listen 127.0.0.1 -parentPid <pid> -datadir <dir> -batchmode -nographics -logFile <dir>/server.log`), aspetta `server.ready`, poi si collega con nuovi tentativi (attese 0,5 → 3 s) fino a 25 s. Si riprova solo senza risposta o con `server_starting`; un rifiuto vero ferma subito.
- `HostForFriends`: come PlayLocal ma `-listen 0.0.0.0`; il testo di stato mostra IP di rete e porta. Non è nei test automatici (aprirebbe l'avviso del firewall).
- Dopo "Esci dalla partita" il server figlio resta acceso e il prossimo PlayLocal lo riusa; si chiude quando il gioco si chiude (`-parentPid`). Se cambiano modo o porta, il vecchio server riceve `server.stop` (poi Kill dopo 5 s).
- Argomenti per i test: `-localport`, `-localdatadir`, `-localarea`. Nell'editor PlayLocal usa `Builds/Windows/UNDERWORLD.exe` (se manca: messaggio in italiano).

### Giocatore
- Prefab `Assets/Prefabs/Player.prefab` creato da `PlayerPrefabStep` (ordine 250). Altri moduli aggiungono componenti con `IPlayerPrefabContributor` (Editor/Steps/Player), senza toccare il passo.
- `NetworkTransform` con autorità del proprietario (posizione xyz + rotazione y, interpolazione). Solo il proprietario ha `CharacterController` e `ThirdPersonController` accesi; le copie remote e quelle del server usano un `CapsuleCollider` semplice.
- `PlayerNetwork`: nome in una `NetworkVariable` scritta dal server; targhetta col nome per gli altri giocatori; collega la camera della scena (o la crea). Sul proprietario controlla nei primi frame che la posizione sia quella di spawn del server (vedi "Problemi noti").
- Comandi: `IPlayerInput` (`KeyboardMouseInput` per le persone, `BotInput` per bot e test). Con `InputGate.GameplayBlocked` il controller ignora i comandi e la camera libera il mouse. La camera non gestisce più Esc (lo usa il menu di pausa della UI).
- `MovementValidator` (server): credito di movimento (`MovementBudget`), a piedi 8,5 m/s, salita 6 m/s, discesa 55 m/s, 1,5 s di credito per la rete a scatti. Violazione → log, contatore, `CorrectPositionRpc` (SendTo.Owner) all'ultima posizione valida; 5 correzioni ignorate → disconnessione `movement_invalid`. API: `SetSpeedAllowance(chiave, velocità)` / `ClearSpeedAllowance` (veicoli), `ServerTeleport`.
- `RateLimiter` (secchio di gettoni per client e chiave) pronto per le RPC dei moduli.

### Test automatici (sezione 12)
- `Tools/test.ps1 -Scenario <nome>|all [-PortBase 7700] [-SkipBuild] [-TimeoutSec n] [-Exe percorso] [-Repeat n]` (PowerShell 5.1). Senza `-SkipBuild` fa prima la build.
- Ogni scenario usa 10 porte (`PortBase + 10 × indice`), cartelle temporanee in `%TEMP%\underworld-tests\<data>\<scenario>` (log, risultati, profili, dati del server). I log restano per controllarli.
- Scenari: `connect_two_clients`, `reject_bad_name`, `server_full`, `speed_hack_corrected`, `local_play_launcher`, `bot_wander` (4 bot insieme, nessuna correzione).
- Nel gioco: `[AutoTestScenario("<scenario>.<ruolo>")]` + `IAutoTestScenario` in `Runtime/Testing/Scenarios/`. `AutoTestContext`: `Check`, `WaitUntil`, `Wait`, `WaitForLocalPlayer`, `WaitForRelease`, `Log`, `SetData`, `LocalPlayer`, `Bot`, `Launcher`, `Server`.
- Risultato JSON in `-autotest-out`; file `<out>.ingame` quando il personaggio è pronto; `-autotest-timeout` (default 120 s). Client: esce con 0/1. Server: resta acceso fino a `server.stop`, poi esce con 1 se il suo scenario è fallito.
- Il trucco di velocità (`ThirdPersonController.DebugSpeedMultiplier`) è `internal` e lo usa solo lo scenario `speed_hack_corrected`.

### Problemi noti
- RISOLTO (2026-11-27): personaggio locale in (0,0,0) dopo lo spawn (raro, circa 1 su 15 in `bot_wander`). Causa provata: Netcode crea il prefab in (0,0,0) e poi sposta il transform; il progetto ha `Physics.autoSyncTransforms` spento, quindi la capsula del `CharacterController` resta in (0,0,0) fino al prossimo passo di fisica. Se il primo `Move` arriva prima di un FixedUpdate (frame veloci in batchmode), il personaggio torna in (0,0,0). Il `NetworkTransform` non c'entra. Correzione: `ThirdPersonController.SyncCharacterController(cc)` (spegne e riaccende il controller) allo spawn e prima di accendere i comandi. Regola: dopo ogni spostamento diretto del transform di un personaggio, risincronizza il `CharacterController` (o spegnilo durante lo spostamento). Test: `PlayerSpawnSyncTests` (EditMode, riproduce la causa) e i controlli "partenza dal punto di spawn" / "nessun salto di posizione" di `bot_wander`; `Tools/test.ps1 -Repeat N` ripete gli scenari per i problemi rari.
- Il blocco di tutti i processi visto insieme al caso (0,0,0) il 2026-11-26 alle 21:36 coincide con l'intervallo in cui uno script esterno chiudeva tutti gli `UNDERWORLD.exe` del PC (21:00-21:40): i log finiscono nello stesso secondo. Non si è più visto.
- Il controllo di velocità lascia passare circa 1 s di trucco (credito di 14 m) prima di correggere. Da stringere in Fase 12 se serve.
- `AreaAnchor` è stato spostato nel file `Runtime/Core/AreaAnchor.cs` (stessi membri): nel file `AreaAnchors.cs` Unity non lo salvava nelle scene ("script mancante").
