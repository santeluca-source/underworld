# UNDERWORLD — Passaggio di consegne (da Claude a ChatGPT / Codex)

Scritto da Claude il 2026-11-27 alle 19:35. Repo: `C:\Users\sante\Desktop\UNDERWORLD`, GitHub privato `santeluca-source/underworld`.

---

## 0. Prompt pronto da incollare in ChatGPT

> Sei l'architetto del gioco UNDERWORLD (Unity 6, multiplayer, Windows). Il progetto è già avanti: fasi 1–4 finite e testate, fasi 5–6 in corso. Leggi questo documento di passaggio (Docs/HANDOFF.md) e poi Docs/ARCHITECTURE.md, Docs/DECISIONS.md, Docs/ROADMAP.md, AGENTS.md. Rispetta le regole della sezione 2 (soprattutto: il server decide soldi e oggetti, tutto il testo del gioco in inglese, niente "funziona" senza test). Per prima cosa controlla lo stato della sezione 6 (lavoro in corso), poi proponimi il piano per finire le fasi 5–6 con i compiti per Codex.

---

## 1. Cos'è e chi decide

- **Gioco**: UNDERWORLD, sandbox open-world multiplayer 3D per Windows, terza persona, stile "Modern Low Poly", 8–16 giocatori per area. Ispirato al genere di ROB RUN ma **originale** (niente nomi, mappe o grafica copiati).
- **Brief originale dell'utente**: `Docs/BRIEF.md` (aree Downtown, Industrial District, Suburbs, Outskirts; economia persistente; case; veicoli e garage; personalizzazione; clan; polizia NPC; missioni cooperative; voice chat).
- **L'utente** non è un programmatore. Parla italiano. Vuole risposte brevi e chiare. Prova il gioco e dà giudizi.
- **Lingua**: testi del gioco in **inglese (US)** (decisione D12). Documenti, commenti del codice e messaggi all'utente in **italiano**.

## 2. Regole obbligatorie (AGENTS.md)

1. Il **server decide** soldi, oggetti, proprietà, veicoli, missioni. Il client manda solo intenzioni e id. Il server controlla mittente (`RpcParams`), distanza (`MovementValidator.AuthoritativePosition`), frequenza (`RateLimiter`), proprietà, soldi.
2. Soldi in `long`, mai `float`.
3. Piccoli incrementi. Un sistema alla volta. Usa i **punti di estensione** (sezione 14 di ARCHITECTURE.md): `IServerModule`, `IConnectionValidator`, `IPlayerPrefabContributor`, `IProjectSetupStep`, `UiRegistry`/`UiWindow`, `HudBus`, `AreaBuilder.WhenBuilt`, `AreaAnchors`.
4. **Non dichiarare funzionante niente senza un test**: test EditMode per la logica, scenari automatici (server e client veri) per la rete.
5. Dopo uno spostamento diretto del transform di un giocatore chiama `ThirdPersonController.SyncCharacterController(cc)` (`Physics.autoSyncTransforms` è spento; senza questo il personaggio torna in (0,0,0)).
6. Chiudi solo i processi che hai avviato tu, **per PID**. Mai per nome.
7. Server di test solo su `127.0.0.1` (con `0.0.0.0` Windows apre l'avviso del firewall).
8. Finestre di gioco dei test con `-monitor 2` (l'utente lavora sullo schermo 1).
9. Un solo Unity alla volta per ogni copia del progetto (Unity blocca la cartella).
10. Commit spessi. Messaggi di commit in italiano.

## 3. Ambiente sul PC dell'utente

| Cosa | Dove / stato |
|---|---|
| Unity 6000.3.29f1 (Mono, niente IL2CPP né modulo Dedicated Server) | `C:\Users\sante\Documents\Codex\UnityEditors\6000.3.29f1\Editor\Unity.exe` |
| Progetto Unity | `UnityProject/` nel repo |
| Pacchetti | URP 17.3, Netcode for GameObjects 2.14, Unity Transport 2.7, Input System 1.20, AI Navigation 2.0.16, Newtonsoft 3.2.2, UI Toolkit |
| PC | i5-12400F (12 thread), 32 GB RAM, RTX 5060 Ti, 2 schermi 1920×1080 (schermo 2 a destra) |
| Altri strumenti | git, GitHub CLI, Node.js, .NET runtime, Blender 4.5. **Mancano** .NET 8 SDK, PostgreSQL, Docker (servono per il backend: chiedere all'utente prima di installare). |
| Smart App Control di Windows | **disattivato** dall'utente (bloccava le DLL di Unity) |
| Registro del gioco | `HKCU\Software\Underworld\UNDERWORLD`, `UnitySelectMonitor=1` → le finestre si aprono sullo schermo 2 |
| Copia per l'utente | `UnityProject\Builds\Play\UNDERWORLD-Play.exe -monitor 1` (exe rinominato: le build dei test non la toccano) |
| Copie di lavoro vecchie (worktree) | `C:\Users\sante\Desktop\UNDERWORLD-wt\` (net, world, ui): già unite in main, si possono rimuovere con `git worktree remove` |
| Vault Obsidian dell'utente | `C:\Users\sante\Desktop\Project` (nota `Progetti/UNDERWORLD.md`, Diario giornaliero). Regola dell'utente: annotare ogni lavoro nel Diario. |

## 4. Come si lavora (comandi)

```powershell
# Unity in batchmode (riassume gli errori, esce con 0 se ok)
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/unity.ps1 -Action compile
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/unity.ps1 -Action setup     # esegue tutti gli IProjectSetupStep (scene, prefab, asset)
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/unity.ps1 -Action editmode  # test EditMode
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/unity.ps1 -Action build     # UnityProject/Builds/Windows/UNDERWORLD.exe

# Scenari multiplayer automatici (processi veri: server headless + client)
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/test.ps1 -Scenario all
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/test.ps1 -Scenario connect_two_clients -SkipBuild -Repeat 5
```

- Ciclo tipico: modifica → `compile` → `setup` → `editmode` → `build` → `test.ps1 -Scenario all` (due volte, per i problemi rari).
- Uno scenario = `Tools/scenarios/<nome>.json` (quali server e client) + classi `[AutoTestScenario("<scenario>.<ruolo>")]` in `Runtime/Testing/Scenarios/`. Ogni processo scrive un JSON di risultato; lo scenario passa solo se tutti passano e nessun log ha eccezioni. Porte e cartelle dati sempre temporanee.
- **Codex nel cloud non ha Unity né Windows**: non può compilare né testare. Per i test serve Codex CLI su questo PC, oppure l'utente lancia i comandi sopra.

## 5. Stato del progetto (2026-11-27 19:35)

**Fatto e verificato** (main `0f3252d`, su GitHub; verifica indipendente: 135/135 test EditMode, tutti gli scenari PASS):

| Fase | Cosa c'è |
|---|---|
| 1–2 | Repo, Unity 6 + URP, personaggio in terza persona (WASD, Shift, Spazio, mouse), animazione fatta a codice |
| 3 | **Downtown costruita a runtime** con seme fisso (stesso mondo su server e client, stesso hash): kit Kenney CC0 (auto, fabbrica, natura) dal vault, mesh unite per materiale, 42 punti speciali (`AreaAnchor`: spawn, uscite verso le altre aree, banca + caveau, polizia, autosalone, negozio, vestiti, parcheggio, appartamenti, bacheca missioni, nascondiglio). Cartelli in inglese con `PixelFont`. NavMesh solo sul server. Anteprima: `-preview`. |
| 4 | **Server dedicato** per area (stesso exe con `-server -batchmode -nographics`), scena Bootstrap, `GameLauncher` (Play = avvia il server sul PC; Play with friends; Join IP), approvazione con payload JSON (identità GUID + token, nome, versione, posti), spawn sui punti di Downtown, `NetworkTransform` con autorità del proprietario + `MovementValidator` sul server (velocità, volo, cadute, NaN, correzioni solo dal server, espulsione), **UI Toolkit in stile vetro** (menu con skyline notturno, HUD, pausa con Esc, finestre registrabili), 8 scenari multiplayer, revisione a 4 lenti con 9 difetti corretti. |

Misure: 60 FPS (vsync), 154–164 batch, Downtown costruita in ~0,2 s; server headless ~200 MB e ~6 % di un core con 2 giocatori.

## 6. Lavoro in corso: fasi 5–6 (workflow di Claude avviato alle 19:20)

Quando Claude ha scritto questo documento, il primo costruttore (persistenza) stava lavorando su main **senza commit** (cartelle nuove `Runtime/Server`, `Runtime/Economy`, `Runtime/Inventory` e modifiche in `Runtime/Net`). Se i crediti di Claude finiscono, il lavoro resta a metà.

**Prima di continuare:**
1. Assicurati che il workflow di Claude sia fermo (l'app Claude chiusa o il workflow fermato), altrimenti due sviluppatori scrivono su main insieme.
2. `git log 0f3252d..HEAD` e `git status`: vedi cosa è stato fatto. Se c'è lavoro non salvato, fai un commit "WIP" e controlla che compili.
3. Riparti dai compiti qui sotto, in ordine, saltando quello che è già fatto e testato.

### 6.1 Persistenza, identità, economia, inventario (Fase 5 + base della Fase 6)
- `ServerContext` (solo server, creato da un `IServerModule`) con i servizi; `PlayerSession` per giocatore. Agganci: `ServerPlayerRegistry.PlayerJoined/PlayerLeft`, `ServerRuntime.ServerStopping`.
- `IPlayerStore` + `JsonFilePlayerStore` in `<datadir>/players/<playerId>.json`. `playerId` validato come GUID prima di usarlo in un percorso. Scrittura atomica (tmp → replace) con `.bak`. JSON rotto: prova il `.bak`; se anche quello è rotto, sposta in `players/corrupt/` e **rifiuta** l'accesso (mai sovrascrivere i dati con un salvataggio vuoto).
- `PlayerSave` v1: playerId, createdUtc, lastSeenUtc, name, tokenHash, cash, inventory (id → quantità), lastArea, lastPosition, `modules` (`Dictionary<string, JToken>` con `GetModule<T>` / `SetModule<T>`). Salvataggio: 2 s dopo un cambio, ogni 60 s, all'uscita, a `ServerStopping` (sincrono), `SaveNow(session)` per i trasferimenti.
- Identità: un `IConnectionValidator` carica o crea l'account; salva solo `SHA256(playerId + ":" + token)` e confronta a tempo costante. Nuovo account: $5,000. Token sbagliato: `identity_invalid|Your player profile could not be verified.`
- Lucchetto tra server: `<datadir>/locks/<playerId>.lock` (area, pid, porta, battito ogni 5 s; scaduto dopo 15 s). Lucchetto vivo su un altro server → `already_connected`. Stesso server, stesso id, token valido → vince la sessione nuova: salva e scollega la vecchia con `session_replaced|You joined from another window.`
- `EconomyService`: `GetCash`, `Credit`, `TryDebit` (importo > 0, aritmetica controllata, tetto 1.000.000.000.000). Ogni movimento: una riga in `<datadir>/transactions/<aaaa-mm-gg>.jsonl`. `PlayerWallet` sul prefab: `NetworkVariable<long>` leggibile solo dal proprietario → `HudBus.SetCash`.
- `ItemCatalog` con registrazione dai moduli (id, nome e descrizione in inglese, prezzo, pila massima, categoria, negozi, usabile). `InventoryService` (Count, CanAdd, TryAdd, TryRemove). `PlayerInventory` sul prefab (lettura solo proprietario). `UseItemRpc` → registro `IItemUseHandler`. Finestra Inventory sul tasto **I**.
- `ServerTestHooks` attivi **solo** con `-testmode` (soldi, oggetti, stato), con uno scenario che prova che un server normale li rifiuta.
- Test: EditMode (salvataggio atomico, .bak, file rotto, GUID, moduli, hash, economia, registro, inventario). Scenari: `persistence_roundtrip`, `identity_spoof_rejected`, `session_replaced`, `server_restart_persistence`, `lock_two_servers`, `testhooks_disabled`.

### 6.2 Interazioni, negozio, primo lavoro, Energy Drink
- Interazioni (ARCHITECTURE sezione 11): `Interactable` (id stabile, testo inglese, raggio, tipo) piazzato sui punti speciali dopo `AreaBuilder.WhenBuilt`, uguale su server e client. Client: oggetto più vicino → suggerimento "E + testo" su `HudBus`; E → `InteractRpc(id)`. Server: limite di frequenza, distanza da `AuthoritativePosition`, gestore del tipo (`IInteractionHandler`). Interazioni "tieni premuto" misurate dal server, con barra di avanzamento.
- Negozio MARKET (`downtown.shop.general`): finestra con oggetti, prezzi, quantità; `BuyItemRpc(shopId, itemId, qty)` controllata dal server (mai soldi o oggetto persi).
- Oggetto `item.energy_drink` (Energy Drink, $40, pila 5): per 30 s corri il 25 % più veloce (`MovementValidator.SetSpeedAllowance` + velocità del controller; bere di nuovo rinnova, non somma).
- Bacheca MISSIONS (`downtown.mission.board`): finestra con un registro di lavori. Primo lavoro: **Courier** (consegna a un punto a più di 60 m; obiettivo su HUD, segnale visibile solo al giocatore; arrivo verificato dal server; paga $150 + $1 al metro, massimo $600; attesa 20 s; Cancel). Suggerimento una tantum per i nuovi: "Check the MISSIONS board to earn money".
- Scenari: `shop_purchase`, `shop_rejects`, `interact_spoof_rejected`, `courier_job_paid`, `energy_drink_speed` + screenshot `Docs/screens/a2-*.png`.

### 6.3 Personalizzazione e negozio di vestiti
- `Appearance` (struttura di rete: colore pelle, capelli o cappello, giacca, pantaloni, colore accento); `AppearanceCatalog` in inglese; opzioni premium che richiedono un vestito comprato (`clothing.*`, $250–$2,000).
- `PlayerAppearance` sul prefab: `NetworkVariable<Appearance>` (tutti leggono, scrive il server), applicata al personaggio fatto a codice; salvata nel modulo `appearance`.
- Menu: pulsante e schermata **Character** con anteprima 3D che gira; scelta salvata in locale e chiesta al server all'ingresso (`RequestAppearanceRpc`, il server controlla tutto).
- Negozio FASHION (`downtown.clothing.main`): comprare vestiti premium e cambiare aspetto.
- Scenari: `appearance_sync`, `premium_requires_purchase` + screenshot.

### 6.4 Chiusura delle fasi 5–6
Revisione a 4 lenti (sicurezza dei salvataggi; trucchi su soldi e oggetti, per esempio duplicazioni; rete e ciclo di vita; UI e inglese), poi verifica dei difetti e correzioni. Tutti gli scenari due volte verdi. Aggiornare ARCHITECTURE, ROADMAP, DECISIONS.

## 7. Fasi successive (piano di Claude, da rivedere con l'utente)

Ordine consigliato: 7–8 → 11 → 9 → 10 → 12. Ogni fase: specifica → sviluppo con test → revisione → correzioni.

**Fase 7 — Veicoli.** Modelli Kenney Car Kit (sedan, sedan-sports, hatchback-sports, suv, suv-luxury, van, taxi, truck, police). Fisica arcade a raggi con Rigidbody (più stabile in rete delle WheelCollider). Il guidatore diventa proprietario del `NetworkObject` (`ChangeOwnership`), sincronizzazione con autorità del proprietario, e il server alza il limite di velocità con `SetSpeedAllowance`. Salire e scendere con **F** (guidatore + passeggeri). `VehicleCatalog` (esempio: hatchback $8,000, sedan $12,000, van $15,000, suv $18,000, sports $45,000). Autosalone: auto in mostra sui punti `downtown.dealership.display.0..3`, finestra di acquisto, auto consegnata su `downtown.dealership.exit`. Auto possedute salvate nel modulo `vehicles`. Garage pubblico (`downtown.garage.public`): ritira o deposita, una sola auto fuori per giocatore, consegna su `garage.spawn.N`. Auto parcheggiate da rubare con il Lockpick (reato per la Fase 9). Repair Kit. Scenari: acquisto, guida vista dagli altri, garage, auto di altri chiusa.

**Fase 8 — Proprietà.** `PropertyCatalog` (appartamenti di Downtown `downtown.property.apt0..3` $60,000–$120,000; case in Suburbs; magazzini in Industrial; nascondiglio in Outskirts). Acquisto alla porta ("For sale $X"). Proprietà salvate nel modulo `properties` più un registro del server per area (solo il server di quell'area vende quella casa). Porta aperta solo al proprietario (poi anche al clan). Spawn a casa a scelta. Garage privato. Cassaforte: soldi depositati protetti dalle multe dell'arresto.

**Fase 11 — Altre aree e trasferimenti.** Costruttori `IndustrialBuilder` (Factory Kit: capannoni, container, gru, nastri, banchine, ferrovia), `SuburbsBuilder` (case con giardino in vendita, distributore, parco), `OutskirtsBuilder` (colline, strade di campagna, fattoria, nascondiglio; Nature Kit), circa 300 × 300 m con uscite e punti di arrivo. File `areas.json` (area → indirizzo:porta) per i server (`-areas`) e il launcher; "Play" avvia i 4 server. Trasferimento: il server vede il giocatore nell'uscita → `SaveNow` + rilascio del lucchetto + biglietto monouso (60 s) → RPC al proprietario con area, indirizzo, porta, biglietto → il client si collega al server di arrivo con il biglietto nel payload → arrivo su `<area>.arrival.from_<origine>`. Auto posseduta guidata: ricompare all'arrivo. Missioni attive: finiscono, gli oggetti portati restano. Scenario: `transfer_preserves_state`.

**Fase 9 — Missioni e polizia.** `WantedService` (stelle 0–5 per giocatore). Reati: rapina al negozio (tieni premuto su `downtown.robbery.store0`, $300–800, 2 stelle), furto d'auto (1 stella), colpo in banca (3–4 stelle). `PoliceDirector` sul server: agenti a piedi e auto della polizia guidate dalla NavMesh, vicino ai ricercati, con vista diretta. Arresto se un agente è a meno di 2 m per 2,5 s (o l'auto è bloccata): multa del 10 % dei soldi in tasca (minimo $100), perdita del bottino, uscita su `downtown.police.release`. Fuga: le stelle calano fuori vista. Missioni cooperative: chi è alla bacheca entro 20 s partecipa; obiettivi GoTo/Hold/Collect/Deliver/Survive; ricompensa divisa; statistiche nel modulo `missions`. Missione "Bank Job": serve il Thermal Drill; trapano sul caveau (60 s, più veloce in più persone); allarme → stelle per tutti; ognuno porta 1 sacco (`item.loot_bag`) a `downtown.hideout.drop`; $2,500 a sacco divisi. Ricettatore per vendere il bottino (Industrial).

**Fase 10 — Social.** Clan: creazione $10,000, nome 3–16 caratteri, sigla 2–4 unica, invito, ingresso, uscita, espulsione, ruoli, sigla sulla targhetta, `clans.json` condiviso tra server con lucchetto. Chat di testo: generale per server e del clan, massimo 200 caratteri, niente rich text, limite di frequenza, silenzia. **Voce di prossimità** (decisione D9): premi **V** per parlare, 16 kHz mono, µ-law, pacchetti di 20 ms con i messaggi di Netcode, il server li inoltra a chi è entro 30 m, riproduzione con AudioSource in streaming; test con un tono sintetico. Più avanti: Vivox (serve l'account Unity dell'utente).

**Fase 12 — Ottimizzazione.** 16 bot per server (cammino + lavori), misure del server (tick, CPU, memoria, banda), soglie del `NetworkTransform`, gestione dell'interesse se serve, IL2CPP, revisione finale di sicurezza, **simulazione dell'economia** con un giocatore "goloso" (`Tools/econ-sim`, lezione del vault: il primo tentativo di un altro gioco finiva in 3 minuti).

**Backend (dopo, con il permesso dell'utente per installare .NET 8 SDK e PostgreSQL).** ASP.NET Core 8 + PostgreSQL con le tabelle del brief (Accounts, PlayerProfiles, PlayerWallets, InventoryItems, Vehicles, Properties, Clans, ClanMembers, Missions, MissionProgress, Transactions, PlayerSettings). `HttpPlayerStore` implementa `IPlayerStore`: il gioco non cambia. Autenticazione vera al posto di GUID + token.

## 8. Problemi noti

- Il controllo della velocità lascia passare circa 1 s di trucco (~14 m) prima di correggere; durante una correzione (≤ 1,8 s) gli altri vedono ancora la posizione del trucco. I moduli del server devono usare `MovementValidator.AuthoritativePosition`.
- Il tunnel dentro i portali di uscita è quasi nero; le piattaforme dell'autosalone sono vuote (le auto arrivano con la Fase 7).
- La UI col gamepad è minima. I clic veri del mouse non sono provati da test automatici.
- I limiti di utilizzo dei servizi AI fermano i lavori lunghi: fare commit spessi e prompt di ripresa ("riparti dal commit X").
- Le attività programmate dell'utente (21:53 biblioteca, 22:11 revisione della sera) possono fallire se i crediti finiscono: l'utente ha scelto di continuare comunque.

## 9. Decisioni aperte (chiedere all'utente)

Hosting dei server, matchmaking, voice definitiva (Vivox?), autenticazione definitiva, dimensione delle mappe (ora ~300 × 300 m), modello di sincronizzazione dei veicoli, monetizzazione, numero massimo di istanze, distribuzione, installazione di .NET SDK / PostgreSQL / account Unity Gaming Services.

## 10. Metodo che ha funzionato

1. Un compito per sviluppatore, con file e cartelle suoi (punti di estensione), così non ci sono conflitti.
2. Ogni compito finisce con build, test EditMode e scenari multiplayer **due volte**.
3. Revisione con 4 lenti diverse (sicurezza, ciclo di vita, UI/UX, mondo/test/prestazioni); ogni difetto grave verificato da uno scettico; poi un correttore. Nella fase 3–4 ha trovato 9 difetti veri, 2 critici.
4. Screenshot veri guardati a occhio (`-autoshot`, `-preview`, `-uipreview`) prima di dire "fatto".
5. Annotare nel Diario del vault cosa è stato fatto, deciso e non verificato.
