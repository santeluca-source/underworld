# UNDERWORLD — Brief del progetto (testo dell'utente)

> Nota: nel brief originale i ruoli erano ChatGPT (architetto) e Codex (sviluppatore).
> Dal 2026-11-26 l'utente ha deciso: sviluppa Claude con i suoi agenti. Il resto del brief vale così com'è.

**Progetto:** UNDERWORLD · **Tipo:** Multiplayer 3D Open-World Sandbox · **Piattaforma:** Windows PC · **Visibilità:** privato · **Metodo:** incrementale, modulare, guidato dai test

## 1. Visione
Gioco multiplayer open-world ispirato alla struttura generale di ROB RUN, con identità, ambientazione, sistemi e progressione propri.
Un mondo persistente dove i giocatori possono:
- esplorare diverse aree;
- guadagnare e gestire denaro;
- acquistare proprietà;
- possedere garage e veicoli;
- personalizzare il personaggio;
- creare clan e gruppi;
- collaborare in missioni;
- interagire con la polizia NPC;
- comunicare con la voice chat.

Non un semplice prototipo: le fondamenta di un gioco completo, costruite per passi.

## 2. Caratteristiche principali
| Sistema | Specifiche |
|---|---|
| Multiplayer | 8–16 giocatori per istanza |
| Visuale | Terza persona |
| Piattaforma | Windows |
| Grafica | Modern Low Poly |
| Mappe | Multiple e collegate |
| Personaggi | Personalizzazione |
| Economia | Persistente |
| Proprietà | Case acquistabili |
| Veicoli | Acquisto, possesso e garage |
| Clan | Gruppi persistenti |
| NPC | Polizia |
| Missioni | Cooperative |
| Comunicazione | Voice chat |

## 3. Mondo di gioco
Prime quattro aree: Downtown, Industrial District, Suburbs, Outskirts.
Mappe o istanze distinte, collegate da un sistema di trasferimento dei giocatori.
Il trasferimento conserva: identità, inventario, denaro, veicoli posseduti, proprietà, progressione, stato delle missioni compatibile.

## 4. Tecnologie
Client: Unity 6 LTS, C#, URP, Input System. Multiplayer: Netcode for GameObjects, Unity Transport, server autoritativo.
Backend: ASP.NET Core, PostgreSQL, API per autenticazione e persistenza. Voice: provider dedicato (candidato Vivox). Target: Windows.
Non introdurre dipendenze definitive senza verificarne compatibilità e supporto.

## 5. Architettura
- Client Unity: rendering, input, camera, animazioni, UI, presentazione degli eventi.
- Game Server (autoritativo): movimento e stato multiplayer, inventario, economia, veicoli, proprietà, NPC, missioni, interazioni.
- Backend: account, autenticazione, profili, salvataggi, transazioni, clan, persistenza.
- Database PostgreSQL: Accounts, PlayerProfiles, PlayerWallets, InventoryItems, Vehicles, Properties, Clans, ClanMembers, Missions, MissionProgress, Transactions, PlayerSettings.
- La sicurezza economica è fondamentale: il client non assegna mai denaro, oggetti o proprietà.

## 7. Roadmap
1 Foundation · 2 Player Controller · 3 Primo ambiente (Downtown) · 4 Multiplayer · 5 Identità e persistenza · 6 Economia · 7 Veicoli · 8 Proprietà · 9 Missioni e polizia · 10 Social (clan, voice) · 11 Espansione (altre aree, trasferimenti) · 12 Ottimizzazione (8–16 giocatori, prestazioni, rete, sicurezza, stabilità).

## 8. Regole obbligatorie
Vedi `AGENTS.md` (stesse regole del brief).

## 10. Decisioni ancora aperte
Hosting, matchmaking, provider voice, autenticazione definitiva, dimensioni delle mappe, sincronizzazione dei veicoli, monetizzazione, numero massimo di istanze, distribuzione.
Proporre alternative e motivare prima di vincolare l'architettura.
