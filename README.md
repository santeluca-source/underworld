# UNDERWORLD
Gioco multiplayer open-world 3D per Windows (8–16 giocatori), stile Modern Low Poly. Testi del gioco in inglese.
Stato: Fasi 1–4 fatte (giocatore, Downtown, multiplayer con server dedicato, menu e HUD). Prossima: Fase 5 (salvataggi).
Documenti: `Docs/BRIEF.md` (brief), `Docs/ARCHITECTURE.md` (contratto tecnico), `Docs/ROADMAP.md` (fasi), `Docs/DECISIONS.md` (decisioni).

## Come si avvia il gioco
1. Crea la build: `powershell -NoProfile -ExecutionPolicy Bypass -File Tools/unity.ps1 -Action build`.
2. Avvia `UnityProject/Builds/Windows/UNDERWORLD.exe`.
3. Nel menu scrivi il nome e premi **Play**: il gioco avvia da solo un server Downtown sul PC e ti collega.
   - **Play with friends**: come Play, ma gli amici entrano con il tuo IP (porta 7777 UDP aperta sul router).
   - **Join**: scrivi IP e porta del server di un amico.
4. In gioco: WASD per muoverti, mouse per la camera, Shift per correre, Spazio per saltare, **Esc** per la pausa.

Server dedicato a mano: `UNDERWORLD.exe -server -area Downtown -port 7777 -batchmode -nographics`.
Client diretto: `UNDERWORLD.exe -connect 127.0.0.1:7777 -name Tom`.

## Come si eseguono i test
Tutti i comandi: `powershell -NoProfile -ExecutionPolicy Bypass -File <script> ...` dalla cartella del repository.

- Setup del progetto (scene, prefab, materiali; serve dopo un merge): `Tools/unity.ps1 -Action setup`.
- Compilazione: `Tools/unity.ps1 -Action compile`.
- Test EditMode: `Tools/unity.ps1 -Action editmode`.
- Scenari multiplayer (server e client veri): `Tools/test.ps1 -Scenario all`. Con `-SkipBuild` usa la build esistente; `-Repeat 2` ripete tutto per trovare i problemi rari. Un solo scenario: `Tools/test.ps1 -Scenario downtown_two_players -SkipBuild`.
- I client con grafica dei test si aprono in una finestra piccola sullo schermo 2.
- Log, risultati e screenshot dei test: `%TEMP%\underworld-tests\<data>\`. Screenshot di riferimento: `Docs/screens/a1-*.png`.

## Cartelle
- `UnityProject/` client e server Unity 6 (URP, Netcode for GameObjects, UI Toolkit)
- `Tools/` script di build e di test (`unity.ps1`, `test.ps1`, `scenarios/`)
- `Docs/` documenti e screenshot
- `GameServer/`, `Backend/`, `Database/`, `Infrastructure/` per le fasi future (backend ASP.NET Core + PostgreSQL)
