# Crediti dell'arte

I modelli in questa cartella sono di **Kenney** (www.kenney.nl).
Licenza: **Creative Commons Zero (CC0)**, http://creativecommons.org/publicdomain/zero/1.0/
Si possono usare anche in progetti commerciali. Il credito non è obbligatorio, ma lo diamo volentieri.

| Kit | Cartella | Versione | Licenza |
|---|---|---|---|
| Car Kit | `CarKit/` | 3.1 | CC0 (`CarKit/License.txt`) |
| Factory Kit | `FactoryKit/` | vedi `FactoryKit/License.txt` | CC0 |
| Nature Kit | `NatureKit/` | vedi `NatureKit/License.txt` | CC0 |

Origine: copiati dal vault dell'utente (`Assets/Modelli 3D/kenney/`), formato FBX, con le texture `Textures/colormap.png`.

Come si usano nel gioco:
- L'importazione la regola `Editor/Steps/World/KenneyImport.cs` (niente animazioni, camere o luci; mesh leggibili; materiali URP Lit).
- Il codice mette i modelli nel mondo solo tramite la libreria `Assets/Resources/World/ArtLibrary.asset`
  (passo di setup "Arte del mondo"), che conosce misure e scala reale di ogni modello.
- Nei titoli di coda del gioco va scritto: "Modelli 3D: Kenney (www.kenney.nl), licenza CC0".
