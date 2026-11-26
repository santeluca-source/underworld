# Regole per chi lavora sul codice (Claude e i suoi agenti)

Il gioco lo sviluppa Claude con agenti. Il contratto tecnico è `Docs/ARCHITECTURE.md`: leggilo prima di scrivere codice.

## Regole del brief (obbligatorie)
1. Analizza il repository prima di modificare file. Non sovrascrivere codice senza averlo letto.
2. Piccoli incrementi. Un sistema complesso alla volta per ogni agente.
3. Client, server e backend separati. Il client non decide mai denaro, oggetti o proprietà.
4. Codice leggibile e commentato (commenti in italiano, nomi in inglese).
5. Dopo ogni modifica: compila, esegui i test, prova la build. Non dichiarare funzionante ciò che non hai provato.
6. Niente dipendenze inutili. Ogni scelta di architettura va in `Docs/DECISIONS.md`.
7. Segnala problemi e decisioni. Aggiorna la documentazione quando cambia l'architettura.
8. Non andare avanti con errori critici aperti.
9. Gioco originale: niente nomi, mappe, personaggi o grafica copiati da altri giochi.

## Strumenti
- `Tools/unity.ps1 -Action compile|setup|build|editmode|method` — Unity in batchmode, con riassunto degli errori.
- `Tools/test.ps1 -Scenario <nome>|all` — scenari multiplayer automatici (server + client veri).
- Unity: `C:\Users\sante\Documents\Codex\UnityEditors\6000.3.29f1\Editor\Unity.exe`.
- **Un solo Unity alla volta per ogni copia del progetto.** Per lavori in parallelo usa una copia (git worktree) con la sua cartella `Library`.

## Cosa non fare
- Non scrivere nel vault Obsidian né nel Diario: lo fa il coordinatore.
- Non fare push e non creare repository: lo fa il coordinatore.
- Non cambiare le impostazioni di sicurezza del PC.
- Non scaricare file da internet senza il permesso dell'utente (i pacchetti Unity del manifest sono già permessi).
