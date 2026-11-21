# GTag Mod Checker (BepInEx 5-plugin for Gorilla Tag på PC)

GTag Mod Checker er en **passiv** mod checker. Den ser på de andre spillerne i lobbyen og forteller om noen ser ut til å bruke mods eller juks. Du trykker **X** på venstre kontroller, og da dukker det opp en svart meny med rosa glød ved venstre hånd. Du velger spillere ved å peke på dem med en rosa laser eller trykke på navnet i lista. Tidligere versjoner viste et tekstpanel foran deg, omtrent slik:

```
GTAG MOD CHECKER   (X = lukk)
3 players checked, 1 flagged

SPOOFER  CHEATING  PC
  ALLOWED MODS: 0
  CHEATS: 1  Cosmetic spoofer 'cosmetx'

CLEANMONKE  NOT DETECTED  STEAM
  ALLOWED MODS: 0
  CHEATS: 0
```

Den **bare leser**. Den endrer ingenting i spillet, sender ingenting over nettverket og kaller ingen RPC-er.

---

## 1. Installer BepInEx 5
1. Last ned **BepInEx 5** for Windows x64 (`BepInEx_win_x64_5.4.x.zip`) fra den offisielle siden: https://github.com/BepInEx/BepInEx/releases
2. Finn Gorilla Tag-mappa. I Steam: høyreklikk Gorilla Tag → *Administrer* → *Bla gjennom lokale filer*.
3. Pakk ut zip-fila **rett i Gorilla Tag-mappa**, slik at `BepInEx`-mappa og `winhttp.dll` ligger ved siden av `Gorilla Tag.exe`.
4. Start Gorilla Tag én gang og avslutt. Da lager BepInEx mappene `BepInEx/plugins` og `BepInEx/config`.

## 2. Bygg pluginen
1. Installer **.NET SDK 8**: https://dotnet.microsoft.com/download
2. Åpne `GTagModChecker.csproj` og sjekk at `GamePath` peker til Gorilla Tag-mappa. Standard er `C:\Program Files (x86)\Steam\steamapps\common\Gorilla Tag`.
   Du kan også oppgi stien når du bygger:
   ```
   dotnet build -c Release -p:GamePath="D:\SteamLibrary\steamapps\common\Gorilla Tag"
   ```
3. Åpne en terminal i denne mappa og kjør:
   ```
   dotnet build -c Release
   ```
   DLL-en kopieres automatisk til `Gorilla Tag\BepInEx\plugins\GTagModChecker\GTagModChecker.dll`.

> DLL-en er **ikke** et program, så du kan ikke dobbeltklikke på den. BepInEx laster den inn når spillet starter.
> Pluginen trenger ingen ekstra DLL-er. Spillets egne filer og BepInEx blir **ikke** pakket med.

## 3. Bruk
1. Start Gorilla Tag på **PC**: Steam, eller Meta-appen med Link/Air Link.
2. Gå inn i en **privat lobby** (se punkt 5).
3. Trykk **X** på venstre kontroller. Menyen dukker opp over venstre hånd og følger hånden. Den er svart med rosa kant og rosa glød, og knappene er rosa med svart tekst.
4. **Trykk på knappene med høyre pekefinger.** En liten rosa prikk viser hvor fingertuppen er. Når du er over en knapp, blir prikken hvit og større, og knappen lyser lysere rosa. Skyv fingeren inn i knappen, så kjenner du et lite «klikk» i høyre kontroller. Ta fingeren ut av knappen før du trykker på den igjen.
5. Trykk **X** igjen for å legge bort menyen. Mens den er åpen, oppdateres innholdet hvert sekund.

### Venstre panel (følger venstre hånd)
Øverst står **GTAG MOD CHECKER** og `by Seb`. Langs **høyre kant** er det en kolonne med rosa fane-knapper:
- **LOBBY** (åpen som standard):
  - En statuslinje, for eksempel `ROOM ABC123 · 9 PLAYERS · 1 FLAGGED`. Er du ikke i et rom, står det `NOT IN A ROOM`.
  - En rad per spiller med navnet og en farget status i en svart boks: grønn **NOT DETECTED**, gul **SUSPICIOUS** eller rød **CHEATING**.
  - Lista viser seks spillere per side. Er det flere, blar du med **<** og **>**.
  - **REFRESH LIST** leser lobbyen på nytt med en gang.
- **NAMETAGS**: knapper for navnelappene over hodet til andre spillere (se under).
- **CHECKER**: forklaring av fargene, om X er satt til ett eller to trykk, antall spillere og flaggede, hvor mange linjer modlista har, og om bevegelses- og nettverkssjekkene er på.

### Velge en spiller med laseren
Mens menyen er åpen, går det en **rosa laser** ut fra høyre hånd.
- **Pek** på hodet til en spiller. Laseren låser seg på hodet og blir lysere.
- **Trykk på høyre avtrekker** (trigger) for å velge spilleren. Hvis avtrekkeren ikke kan leses, velges spilleren etter at du har pekt på den i 0,6 sekunder.
- Du kan også trykke på navnet i LOBBY-lista.

Laseren bruker ingen raycast og ingen collidere. Den måler bare vinkelen mellom laseren og hodet til hver spiller.

### Spillerpanelet (til høyre for venstre panel)
- **Øverst:** en firkant i spillerens farge, og ved siden av den:
  - `NAVN`
  - plattform og FPS, for eksempel `STEAM / 72FPS`
  - farge på Gorilla Tag sin 0–9-skala, for eksempel `COLOR 9 0 0`
  - `JOINED ON ...`
  - Under står status og score.
- **Faner side om side:** `PROFILE`, `ALLOWED MODS` og `CHEATS`.
- **PROFILE** (standard):
  - **VOLUME** med **<** og **>** og **MUTE**. Disse endrer bare hvor høyt *du* hører stemmen til spilleren, på din egen PC. Ingenting sendes, og ingen andre merker noe. Finnes ikke stemmen, står det `VOLUME: N/A`.
  - **REPORT** lagrer en rapport **bare lokalt** i loggen, med alle funnene og begrunnelsene, og viser `Reported locally`. Ingenting sendes til spillet eller til andre spillere.
  - **CLONE COSMETICS (OFF)** er grå og gjør ingenting. Den viser bare `Not available`.
  - Nederst står `PC INFO`, `GAMEMODE` (fra rommets innstillinger) og `PLAYFABID` (spillerens Photon-ID, som er PlayFab-ID-en i Gorilla Tag).
- **ALLOWED MODS:** lovlige mods som er funnet hos spilleren.
- **CHEATS:** juksefunn i rødt, og usikre funn i gult med `?`. Fanen er tom når ingenting er funnet.
- **X** oppe i hjørnet lukker spillerpanelet.

Verdier som ikke kan leses, vises som `NOT FOUND`. De blir aldri funnet på. `JOINED ON` og `PC INFO` er alltid `NOT FOUND`, fordi spillet ikke deler dem.

Plattform hentes fra spillerens properties. Finnes den ikke der, brukes kosmetikk-lista i riggen: `S. FIRST LOGIN` betyr Steam, og `FIRST LOGIN` betyr PC. Ellers står det `NOT FOUND`.

### Navnelapper (NAMETAGS)
Over hodet til hver spiller vises en lapp som **bare du ser**. Den snur seg alltid mot deg. Knappene i NAMETAGS-fanen er rosa når de er på og mørke når de er av, og valgene lagres i config-fila:
- **NAMETAGS: ON/OFF** slår alle lappene av eller på.
- **FONT: DEFAULT/BOLD** bytter mellom vanlig og fet skrift.
- **SELF TAGS** viser en lapp over ditt eget hode også.
- **HIDE SPEC TAG (N/A)** gjør ingenting i denne versjonen, fordi den ikke har noen spectator-lapp.
- **PLATFORMS** viser for eksempel `STEAM` eller `PC`.
- **FPS / HZ** viser for eksempel `72hz`.
- **VERDICT** viser NOT DETECTED, SUSPICIOUS eller CHEATING i farge.

Menyen har **ingen knapper som gjør noe med andre spillere eller lobbyen**. Det finnes ingen kick, crash, lobby hop, disconnect, rapport til spillet eller endring av kosmetikk. «Kick All» kan bare dukke opp som et *funn* fra modlista.

### Menystil: PANEL eller DISC
Du kan bytte utseende inne i spillet:
- **PANEL** (standard): det rosa/svarte panelet ved venstre hånd. Trykk fanen **CHECKER** og så **MENU STYLE: PANEL** for å bytte til DISC.
- **DISC**: en gjennomsiktig, glødende ring (fiolett som standard). Trykk boksen **MENU STYLE: DISC** for å bytte tilbake til PANEL.
Valget lagres i config (`[Menu] MenuStyle`), og **X** åpner alltid den stilen som er valgt.

**Innstillings-discen** dukker opp ca. 0,45 m foran hodet ditt, snudd mot deg, hver gang du åpner den. Den har bokser i to kolonner (liten tekst øverst, verdi under):
MENU STYLE, THEME (VIOLET/PINK), PLAYERS, NAMETAGS, TAG FPS, TAG PLATFORM, TAG VERDICT, CLIENT CHECKER (automatisk sjekk), SELF TAGS og LASER.
**PAGE 1/2** viser side 2 (TAG FONT, FINGER DOT, DEBUG TOUCH, OPEN WITH X, FLAGGED, MOD LIST, MOVEMENT, NETWORK). **CLOSE** lukker.

**PLAYERS** viser alle i rommet som bokser rundt ringen. Øverst i hver boks står statusen i farge: grønn NOT DETECTED, gul SUSPICIOUS, rød CHEATING.
Trykk en spiller, eller pek på spilleren med laseren og trykk avtrekkeren, for å åpne **spiller-discen**:
- En ring (ca. 0,9 m) svever rundt kroppen til spilleren, følger dem hver frame og er alltid snudd mot deg.
- Boksene viser STATUS, SCORE (lokal poengsum fra checkeren), FPS, PLATFORM, MODS, CHEATS og CREATED (alltid NOT FOUND, fordi kontoens opprettelsesdato ikke kan leses).
- Trykk **MODS** eller **CHEATS** for å vise lista i en boks under ringen. Trykk igjen for å skjule den.
- **REPORT/SELECT** skriver bare til din egen logg. **MUTE** demper spilleren bare hos deg. **BACK** går tilbake til spillerlista.
- Navnet står under ringen, i spillerens farge.

Du kan trykke på alle disc-boksene med fingertuppen (fingerprikken vises fortsatt). Du kan også peke på en boks med den fiolette laseren og trykke avtrekkeren.
Ringen er en vanlig mesh med 64 segmenter, uten teksturer og uten collidere. Loggen viser hvilken gjennomsiktig shader som brukes (`Gjennomsiktig shader: ...`).
Hvis ringen ser helt solid ut i spillet, sett `[Menu] TransparentShader = Sprites/Default`.

Andre innstillinger under `[Menu]`: `DiscTheme`, `DiscDistance` (0.45), `DiscScale` (1) og `PlayerDiscScale` (1).

### Innstillinger
Innstillingene ligger i `BepInEx/config/com.seb.gtagmodchecker.cfg`. Fila lages første gang spillet starter.

| Innstilling | Standard | Hva den gjør |
|---|---|---|
| `TriggerMode` | `SinglePress` | `SinglePress` = ett trykk på X, `DoublePress` = to raske trykk |
| `DoublePressWindow` | `0.4` | Sekunder mellom de to trykkene |
| `KeyboardKey` | `X` | Tast for testing på PC |
| `Haptics` | `true` | Vibrasjon når menyen åpnes, når du trykker på knapper og når laseren velger noen |
| `HandheldMode` | `true` | `true` = den rosa menyen i hånden, `false` = det gamle flytende tekstpanelet |
| `TabletAttach` | `FloatAboveHand` | `FloatAboveHand` = svever over venstre hånd og vender mot deg. `HandLocked` = låst til håndens vinkel |
| `TabletOffsetX/Y/Z` | `0 / 0.16 / 0` | Hvor menyen sitter i forhold til hånden (meter) |
| `TabletRotX/Y/Z` | `0 / 90 / 0` | Vinkelen i `HandLocked`-modus |
| `TabletScale` | `1` | Størrelsen på menyen |
| `PressRadius` | `0.006` | Margin (meter) rundt hver knapp. Fingertuppen treffer hvis den er innenfor knappen pluss denne marginen. Gamle `0.015` oppdateres automatisk |
| `PressDepth` | `0.025` | Hvor langt foran eller bak menyen (meter) fingertuppen kan være og fortsatt trykke |
| `PressCooldown` | `0.35` | Sekunder mellom trykk |
| `ShowFingerDot` | `true` | Rosa/hvit prikk der fingertuppen er |
| `DebugTouch` | `true` | Skriver nærmeste knapp, avstand og `inRect` til loggen én gang i sekundet mens menyen er åpen |
| `FingerTipOffset` | `0` | Brukes bare hvis spillets fingertupp ikke finnes: flytter trykkpunktet fremover fra høyre hånd, for eksempel `0.08` |
| **[Laser]** `LaserEnabled` | `true` | Slår laseren av eller på |
| `LaserSelect` | `Auto` | `Auto` = avtrekker hvis den kan leses, ellers pek og vent. `Trigger` = bare avtrekker. `Dwell` = bare pek og vent |
| `LaserRotX/Y/Z` | `90 / 0 / 0` | Retningen til laseren i forhold til høyre hånd. Peker den feil vei, prøv for eksempel `0 / 0 / 0` |
| `LaserConeAngle` | `7` | Hvor nøyaktig du må peke (grader) |
| `LaserMaxDistance` | `40` | Hvor langt unna (meter) laseren kan velge spillere |
| `LaserDwellTime` | `0.6` | Sekunder du må peke for å velge uten avtrekker |
| `LaserWidth` | `0.006` | Tykkelsen på laseren (meter) |
| **[Nametags]** `Nametags` | `true` | Navnelapper av eller på |
| `Font` | `Default` | `Default` eller `Bold` |
| `SelfTags` | `false` | Lapp over ditt eget hode |
| `HideSpecTag` | `false` | Har ingen effekt i denne versjonen |
| `Platforms` / `FpsHz` / `Verdict` | `true` | Hvilke linjer som vises på lappene |
| `Height` / `Size` | `0.45` / `1` | Hvor høyt over hodet lappen sitter (meter), og hvor stor den er |
| `AutoCheck` / `AutoCheckInterval` | `true` / `10` | Sjekker lobbyen i bakgrunnen og skriver flaggede spillere i loggen |
| `MovementChecks` / `NetworkChecks` | `true` | Slå av/på bevegelses- og nettverkssjekker |
| `MaxSpeed` | `22` | Maks fart (m/s), målt som snitt over et halvt sekund. Gamle `20` oppdateres automatisk |
| `SpeedSustainSamples` | `6` | Hvor mange målinger på rad (10 per sekund) farten må være over `MaxSpeed` før det teller |
| `TeleportDistance` / `MaxArmReach` | `12` / `2.0` | Grensene for teleport og armlengde |

**Tips:**
- Treffer du knappene dårlig, se på prikken: sitter den ikke på fingertuppen, les `Fingertupp-kilde` i loggen. Du kan også øke `PressRadius` til `0.012` eller `PressDepth` til `0.04`.
- Sitter menyen for høyt eller for lavt, endre `TabletOffsetY`.
- Peker laseren feil vei, endre `LaserRotX/Y/Z`.

### Modlista
Modlista ligger i `BepInEx/config/GTagModChecker_mods.json`. Fila lages automatisk og **lastes på nytt mens spillet kjører** hver gang du lagrer den.
- Hver linje i lista har et mønster (`pattern`), en matchtype (`exact`/`contains`/`prefix`/`suffix`/`wildcard`) og en kategori:
  - `cheat` gir CHEATING
  - `unsure` gir SUSPICIOUS
  - `mod` er lovlig og vises under ALLOWED MODS
- Lista som følger med inneholder **eksempler** (`example_*`) og mønstre fra den offentlige lista i [GorillaCaster](https://github.com/cs14gaming/GorillaCaster/blob/main/src/CheatDatabase.cs) (MIT). Mønstrene er **ikke** uavhengig verifisert, så navngitte menyer og korte ord er satt til `unsure`.
- Med `LogUnknownProperties` skrives ukjente nøkler til loggen. Bruk dem til å utvide lista.

## 4. Feilsøking
Åpne `Gorilla Tag\BepInEx\LogOutput.log` og søk etter **`GTag Mod Checker`**.
- `[GTag Mod Checker] started, trigger=..., XR device valid=...`: pluginen kjører.
  - Står det ingenting, er ikke BepInEx installert riktig, eller DLL-en ligger ikke i `BepInEx/plugins`.
- `Venstre kontroller funnet: ...`: kontrolleren ble funnet. Den letes etter på nytt hver ramme, så den kan dukke opp litt senere.
- `Fingertupp-kilde: GorillaTagger.rightHandTriggerCollider (avstand fra rightHandTransform: 0.0xx m)` viser hvor trykkpunktet hentes fra. Står det `rightHandTransform+offset`, ble ikke spillets fingertupp funnet, og da må du sette `FingerTipOffset`.
- `Meny: trykk row0` / `ptab_1` / `report` vises når du trykker på en knapp.
- `Touch-debug: ... nærmest=..., plan=... m, utenfor rekt=... m, inRect=...` kommer én gang i sekundet når `DebugTouch = true`. `plan` er avstanden fra menyens flate: den må være mindre enn `PressDepth`. `utenfor rekt` er avstanden utenfor knappens kant: den må være mindre enn `PressRadius`.
- `Meny: valgte NAVN (actor:N) via laser (trigger)` vises når laseren velger noen. `Høyre avtrekker: indexFloat=...` viser om avtrekkeren kan leses.
- `Fant ikke leftHandTransform`: menyen vises da foran kameraet i stedet.
- `X pressed (source=...)` vises for hvert trykk. Mangler linjen, blir ikke knappen lest. Prøv tastaturet.
- `Rig-lesing: fant ikke VRRig ...`: en Gorilla Tag-oppdatering har endret navn i spillet. Sjekk av mods (properties) og nettverk virker fortsatt, men bevegelsessjekkene er av.
- Med BepInEx-konsollen (`BepInEx/config/BepInEx.cfg` → `[Logging.Console] Enabled = true`) ser du loggen live.

## 5. Viktig
- Virker **bare på PC-versjonen** (Steam eller Meta PC via Link/Air Link). Det går **ikke** på Quest uten PC (standalone).
- **Bruk private lobbyer.** Gorilla Tag tillater mods bare i private/modded lobbyer, og du kan bli **bannet** hvis du bruker mods i offentlige lobbyer. Det gjelder også en mod checker.
- En mod checker kan ta feil. «CHEATING» er en indikasjon, ikke et bevis. Ikke heng ut folk basert på dette alene.

## 6. Slik virker deteksjonen (til skoleprosjektet)
En mod checker kan **ikke** se filene eller minnet til andre spillere. Den ser bare det spillet sender over nettverket (Photon PUN2). Vi bruker tre typer signaler:

1. **Custom properties (hovedmetoden):** hver spiller har en liste med nøkler og verdier, for eksempel `didTutorial`. Mange mods og juks legger til egne nøkler. Vi sammenligner nøklene mot modlista:
   - treff i `cheat` gir høy tillitsgrad
   - treff i `unsure` gir middels tillitsgrad
   - kjente, normale nøkler står på en **allowlist** og ignoreres
2. **Bevegelse:** 10 ganger i sekundet leser vi posisjonen til hodet og hendene til de andre spillerne (fra spillets `VRRig`) og ser etter:
   - **fart** over `MaxSpeed`, målt som snitt over et halvt sekund og bare hvis den holder seg høy i minst 6 målinger på rad. Korte topper (armsving, lagg som tar igjen posisjonen) teller ikke. Store enkelthopp (respawn eller portal) pauser fartssjekken i 1,5 sekunder, men mange store hopp tett på hverandre teller som juks
   - **teleport** (hopper mer enn `TeleportDistance` på en gang)
   - **lange armer** (hånden for langt fra hodet)
   - **unormal størrelse**
   - Flyging sjekkes **ikke**, fordi vi ikke kan vite om en annen spiller holder seg fast i noe.
3. **Nettverk:** vi teller hvor mange RPC-er, objekter som opprettes eller slettes, ownership-overtakelser og property-endringer hver spiller sender per sekund. Spam over grensen er mistenkelig.

Hvert treff får en **tillitsgrad** (0–1) og en begrunnelse. Gradene slås sammen til én score: `score = 1 - (1-c1)(1-c2)...`.
- Under 0,40: **NOT DETECTED**
- Fra 0,40: **SUSPICIOUS**
- Fra 0,85: **CHEATING**

For å unngå falske treff må bevegelses- og nettverksbrudd skje **minst 3 ganger på 10 sekunder**, og de første **5 sekundene** etter at noen har joinet ignoreres.

## Filer
- `GTagModChecker.csproj`: prosjektet (net472, refererer spillets egne DLL-er via `GamePath`, kopierer til `BepInEx/plugins`)
- `src/Plugin/Plugin.cs`: BepInEx-plugin og innstillinger
- `src/Plugin/CheckerBehaviour.cs`: hovedløkka (X-knapp, sjekk, panel)
- `src/Plugin/PhotonFeed.cs`: leser spillere, properties og nettverkshendelser (kun lesing)
- `src/Plugin/RigReader.cs`: leser rig-posisjoner via refleksjon (ingen avhengighet til Assembly-CSharp)
- `src/Plugin/InputReader.cs`: X-knapp, høyre avtrekker og tastatur
- `src/Plugin/Ui/CheckerMenu.cs`: menyen i hånden (venstre panel med faner, spillerpanel, rosa laser)
- `src/Plugin/Ui/DiscMenu.cs`: DISC-stilen (innstillings-disc, spillerring og spiller-disc som følger spilleren, fiolett laser)
- `src/Plugin/Ui/UiKit.cs`: byggeklosser (knapper, bokser, ring-mesh, gjennomsiktige materialer), trykk med pekefingeren og laser-treff (ren matte, ingen collidere)
- `src/Plugin/Ui/Nametags.cs`: lokale navnelapper over hodene
- `src/Plugin/Ui/PlayerText.cs`: plattform, farge (0–9) og FPS-tekst
- `src/Plugin/VoiceControl.cs`: lokalt volum og mute for stemmen til en spiller (bare på din PC)
- `src/Plugin/GtRefs.cs`: finner hendene, din egen rig og vibrasjon i Gorilla Tag via refleksjon
- `src/Plugin/VRPanel.cs`: det gamle flytende panelet (brukes når `HandheldMode = false`)
- `src/Core/*`: selve mod checker-motoren (samme som i det generelle biblioteket, med innebygd JSON-leser)
- `mods.json`: standard modliste (bygges inn i DLL-en)
