# Decisioni

Ogni voce: decisione, motivo, alternative. Le decisioni aperte del brief restano aperte finché l'utente non sceglie.

## 2026-11-26

### D1 — Motore: Unity 6 (6000.3.29f1) + URP 17.3, backend Mono
- Motivo: scelto dall'utente; è installato. IL2CPP e il modulo "Dedicated Server" non sono installati: il server è lo stesso exe in `-batchmode -nographics`.
- Alternativa: IL2CPP più avanti per prestazioni e protezione del codice.

### D2 — Rete: Netcode for GameObjects 2.14 + Unity Transport 2.7, server dedicato per area
- Motivo: pacchetti ufficiali, inclusi nell'editor, adatti a 8–16 giocatori.
- Niente modalità host: anche in locale il server è un processo separato. Così il client non ha mai l'autorità sull'economia.

### D3 — Movimento: autorità del proprietario + controllo del server
- Motivo: Netcode for GameObjects non ha la predizione del client. Con l'autorità solo al server, ogni passo avrebbe il ritardo della rete.
- Il server controlla velocità e salti di posizione e corregge. Da rivedere in Fase 12.

### D4 — Salvataggi: file JSON dietro `IPlayerStore`
- Motivo: sul PC non ci sono .NET SDK, PostgreSQL e Docker. L'interfaccia permette di passare al backend senza cambiare il gioco.
- Prossimo passo: installare .NET 8 SDK e PostgreSQL (serve il permesso dell'utente), poi `HttpPlayerStore`.

### D5 — Identità provvisoria: GUID + token sul client, hash sul server
- Motivo: impedisce di entrare con l'identità di un altro senza un servizio di account.
- Aperta: autenticazione definitiva (backend).

### D6 — Aree come server separati, collegati da trasferimenti
- Motivo: richiesto dal brief (istanze distinte). Il trasferimento salva, rilascia il lucchetto e collega il client al server di destinazione con un biglietto.
- "Gioca" avvia i server delle aree sul PC in automatico.

### D7 — Mondo costruito a runtime con un seme fisso
- Motivo: stesso mondo su server e client senza mandarlo in rete; scene piccole nel repo; mesh unite per materiale (prestazioni).
- La vecchia scena `Downtown_Test` (salvata nel file, 329.000 righe) va sostituita.

### D8 — UI Toolkit con stile "vetro"
- Motivo: UI fatta a codice e fogli di stile; il glassmorphism è la preferenza dell'utente (regole nel vault). Il blur non esiste in UI Toolkit: si usa il ripiego (fondo più scuro).

### D9 — Chat vocale: prossimità con "premi per parlare", fatta in casa
- Motivo: Vivox richiede un progetto Unity Gaming Services sull'account dell'utente.
- Audio a 16 kHz, compressione µ-law, inviato tramite il server ai giocatori vicini. Da sostituire con Vivox o Opus.

### D10 — Gioco su internet: IP diretto
- Motivo: Relay e Lobby richiedono l'account Unity dell'utente. Per gli amici serve aprire le porte (7777–7780 UDP) sul router.

### D11 — Arte: kit Kenney (CC0) dal vault + forme fatte a codice
- Car Kit, Factory Kit, Nature Kit, già scaricati dall'utente. Licenza CC0. Crediti a Kenney nel gioco.

### D12 — Lingua del gioco: inglese
- Richiesta dell'utente (2026-11-27): tutti i testi per il giocatore sono in inglese (menu, HUD, finestre, cartelli nel mondo, motivi di rifiuto, avvisi, nomi delle aree).
- Documenti e commenti del codice restano in italiano (li legge l'utente). Formato dei soldi: `$12,500`.

## Decisioni ancora aperte (dal brief)
- Hosting dei server, matchmaking, provider voice definitivo, autenticazione definitiva.
- Dimensioni delle mappe (ora: circa 300 × 300 m per area).
- Modello di sincronizzazione dei veicoli (ora: autorità del guidatore + controllo del server).
- Monetizzazione, numero massimo di istanze, distribuzione.
