# WinASM65 — Les cibles

Ce document decrit l'etat de depart du catalogue de cibles, et pose les
distinctions qui conditionnent toutes les taches ulterieures. Il accompagne
`docs/pipeline.md`.

---

## 1. Le point structurant : trois familles de cibles

Confondre ces familles produit un plan faux. C'est l'inventaire verifie qui
impose la suite du chantier.

| Famille | Cibles actuelles | Ce qu'est le fichier | Relocation par l'OS |
|---|---|---|---|
| **Executable plat** | NES, Commodore (PRG), Apple II (A2BIN), Atari 8 (XEX), Atari 2600 (ROM), BBC, Electron, Oric, Lynx, X16 | binaire 6502 | **non** — memoire en ROM |
| **Executable segmente** | GEOS (prevu, T8/T9) | records VLIR + image D64 | **oui** — le kernal relocalise |
| **Fichier texte** | DOS 3.2/3.3, ProDOS, Waterloo BASIC, BBC BASIC (T10/T15) ; GECOS hors perimetre, son BASIC n'est pas un BASIC 6502 | **texte tokenise** | non au chargement ; stub runtime si partage |

### Consequence majeure

**DOS 3.2/3.3 et ProDOS ne sont pas des cibles d'executable.** Ces systemes
n'executent pas de binaire : ils executent un interpreteur BASIC qui lit un
fichier texte. Un « executable DOS 3.3 » n'existe pas.

Produire un fichier chargeable par Applesoft suppose d'ecrire un **encodeur
de tokens** : table des 256 tokens, mode binaire `$96`, adressage `$xxxx` et
zero-page, formes abregees. Cela ne depend **ni des relocations, ni du
linker, ni du format d'objet**. C'est une tache distincte (T10), et c'est
pourquoi elle est parallelisable avec le coeur des relocations.

---

## 2. Le catalogue

`SystemCatalog` (`src/Targets/SystemCatalog.cs:8`) associe un identifiant a un
`ResolvedTarget` : CPU, format, adresse de chargement, symboles materiels.

`TryGet` retourne une **copie** (`:26`), jamais l'instance du dictionnaire.
C'est indispensable : `TargetResolver` modifie ensuite l'objet (format,
adresse), et une mutation d'une entree du catalogue contaminerait les
resolutions suivantes.

Les identifiants sont normalises (`:47`) : casse ignoree, `-` et `_`
supprimes. `apple-2e` et `apple2e` designent donc la meme cible.

### Catalogue actuel

| Id | CPU | Format | Load | Symboles |
|---|---|---|---|---|
| `raw` | 6502 | `bin` | — | non |
| `nes` | 6502 | `ines` | — | PPU, OAM, canaux audio, joystick |
| `famicom` | 6502 | `ines` | — | idem `nes` |
| `c64` | 6502 | `prg` | `$0801` | VIC, SID, CIA1, CIA2, ecran, memoire de couleurs |
| `c64c` | 6502 | `prg` | `$0801` | idem `c64` (meme carte E/S, SID 8580) |
| `c128` | 6502 | `prg` | `$1C01` | idem `c64` + MMU, VDC |
| `vic20` | 6502 | `prg` | `$1001` | VIC, VIA1, VIA2 |
| `plus4` | 6502 | `prg` | `$1001` | TED, ACIA, port utilisateur (aucun VIC) |
| `c16` | 6502 | `prg` | `$1001` | idem `plus4` |
| `pet2001` | 6502 | `prg` | `$0401` | PIA1, PIA2, VIA |
| `pet2001n` | 6502 | `prg` | `$0401` | idem `pet2001` |
| `cbm2-80` | 6502 | `prg` | — | CRTC, ACIA, CIA, TPI |
| `cbm2-40` | 6502 | `prg` | — | VIC-II a la place du CRTC |
| `x16` | 65c02 | `prg` | `$0801` | — |
| `apple2` | 6502 | `a2bin` | `$0800` | KBD, TXTCLR |
| `apple2e` | 65c02 | `a2bin` | `$0800` | KBD, TXTCLR |
| `atari8` | 6502 | `xex` | `$0600` | DMACTL, COLBK, CONSOL |
| `atari800` | 6502 | `xex` | `$0600` | idem `atari8` |
| `atari2600` | 6502 | `rom` | — | VSYNC, WSYNC, COLUBK… (rom 4096) |
| `vcs` | 6502 | `rom` | — | idem `atari2600` |
| `bbc` | 6502 | `bbc` | `$0E00` | OSWRCH, OSWORD, CRTC, ULA, les deux VIA |
| `bbcmicro` | 6502 | `bbc` | `$0E00` | idem `bbc` |
| `tube` | 6502 | `tube` | — | HIMEM, le systeme du second processeur |
| `bbc2p` | 6502 | `tube` | — | idem `tube` |
| `tube65c02` | 65c02 | `tube` | — | idem `tube` |
| `electron` | 6502 | `bin` | `$0E00` | — |
| `oric` | 6502 | `bin` | `$0500` | — |
| `lynx` | 65c02 | `bin` | — | non |

`WinASM65 -t list` affiche ce catalogue, avec les memes informations que
`Describe()` (`:30`).

### Portee et borne

Le catalogue est explicitement **borne**. Au-dela, une cible est un ajout
conscient, pas une consequence. Deux precisions :

- Les variantes materielles sont des **options** de cible (tache T14), pas
  des systemes distincts : PAL/NTSC, SID 6581 vs RDFI 8580, VIC-II vs VDC
  vs TED. Pas une cible par combinaison.
- Le second processeur 6502 du BBC Micro (Tube) est une cible **distincte**
  (tache T13) : modele memoire separe, table de symboles materielle
  differente, mode de chargement propre.

---

## 3. Resolution de la cible

`TargetResolver.Resolve` (`src/Targets/TargetResolver.cs:9`) applique une
priorite stricte, du plus faible au plus fort :

```
1. valeurs par defaut (raw / 6502 / bin)
2. preset du catalogue, via -t <systeme> ou Target.System
3. section Target du JSON de configuration
4. surcharge -cpu
5. surcharge -format
```

La surcharge CLI l'emporte donc toujours sur la configuration, qui l'emporte
sur le preset. Un systeme inconnu leve une `ArgumentException` nommant
l'identifiant et renvoyant vers `-t list` (`:20`).

`TargetConf` (`src/Segments/SegmentModels.cs:46`) expose `System`, `Cpu`,
`Format`, `LoadAddress`, `RunAddress`, `RomSize`, `DefineHardwareSymbols`,
`Ines` et `Regions` (section 5). Les adresses sont des **chaines**, parsees par
`NumericLiteral` pour accepter `$0801` comme `2049`.

### Symbole materiels

Quand `DefineHardwareSymbols` est vrai, la table de la cible est injectee
comme symboles predefinis dans la portee globale, donc utilisables dans le
source :

```
lda PPUCTRL    ->  $AD $00 $20
```

C'est ce que verifie `Assembler_PredefinedSymbolsAreVisibleToSource`. Ces
symboles sont indistinguables des symboles de l'utilisateur : rien ne les
marque comme materiels, ce qui sera un point a trancher pour le format
d'objet (T3), ou la distinction source/import compte.

---

## 4. Les formats de sortie

Chaque format est une classe `IExecutableFormat` enregistree dans
`ExecutablePublisher` (`src/Targets/ExecutablePublisher.cs:16`) sous un nom
court. Un nom inconnu produit un **diagnostic** listant les formats
acceptes, pas une exception.

| Nom | Classe | Structure ecrite |
|---|---|---|
| `bin` | `RawBinaryFormat` | charge utile brute |
| `ines` | `InesFormat` | 16 octets d'en-tete, puis PRG (16 Ko/banque) puis CHR (8 Ko/banque) |
| `prg` | `CommodorePrgFormat` | adresse de chargement 16 bits LE, puis charge |
| `a2bin` | `Apple2BinaryFormat` | adresse 16 bits LE, longueur 16 bits LE, puis charge |
| `xex` | `AtariXexFormat` | `$FF $FF`, start 16 bits LE, end 16 bits LE, puis charge |
| `rom` | `PaddedRomFormat` | charge completee par des octets nuls jusqu'a `RomSize` |
| `o65` | `O65Format` | en-tete plat de 12 octets, puis charge |
| `ihex` | `IntelHexFormat` | enregistrements Intel HEX |
| `srec` | `MotorolaSrecFormat` | enregistrements Motorola S-record |
| `bbc` | `BbcFormat` | fichier BBC avec un code header, ou un `$FF` de texte |
| `tube` | `TubeFormat` | la charge seule, ce qu'attend OSLOAD |

### `bbc` en particulier (T13)

**Ce que le plan demandait n'existait pas.** Le plan prevoyait un « fichier a
en-tete binaire (`&FF` + longueur 16 bits) ou a octet de type + 2 octets
d'adresse d'execution ». Aucun octet de type de ce genre n'existe sur la BBC,
et le marqueur `&4F &4F` que la premiere version de `BbcFormat` ecrivait
n'apparait nulle part dans le systeme d'exploitation. Un fichier produit ainsi
est un fichier que la machine refuse de charger, silencieusement.

Le seul format auto-decrivant que la BBC accepte est le **code header**, et il
existe precisement pour le cas ou le systeme de fichiers ne peut pas porter les
adresses :

```
+0   4C lo hi          branchement inconditionnel vers le code
+3   EA EA EA          remplissage : un client ne fait confiance qu'a trois octets
+6   60 | cpu          type : bit 6 « contient du code », bit 5 « adresse presente », nibble bas = processeur
+7   octet             decalage du marqueur de copyright
+8   titre \0
     00
     "(C) auteur" \0
     adresse de chargement, 32 bits
     <le code>
```

Trois consequences, toutes testees :

- **Pas de longueur.** Le type ne l'encode pas et n'a pas a l'encoder : un
  client qui veut la longueur la demande au systeme de fichiers. Le bit 5 ne
  dit pas « combien d'octets » mais « y a-t-il une adresse ».
- **L'octet 7 est le seul moyen de distinguer le fichier du code nu.** Le
  client cherche un octet nul suivi de `"(C)"`. Un marqueur deplace rend le
  fichier invisible sans aucun message.
- **L'adresse fait 32 bits** meme sur un 6502 16 bits : les octets hauts
  distinguent la memoire du second processeur de celle du processeur
  principal. C'est le seul endroit ou cela s'ecrit.

Un code qui commence deja par un branchement est reconnu et n'est pas decale :
ecrire un deuxieme point d'entree decalerait le code de la taille de l'en-tete
tout en produisant un fichier parfaitement valide, ou le client entrerait au
milieu du code.

Le genre se choisit par configuration, `BbcFileType` : `code` (defaut),
`text` (un `$FF` suivi du texte, rien d'autre), `flat` (aucun en-tete).

### `tube` en particulier (T13)

Meme correction : il n'existe pas de service `OSLOAD` sur la BBC. Le seul
`OSLOAD` du monde Acorn est celui de l'Atom, et il charge un fichier **avec**
en-tete vers une adresse fournie par l'appelant.

Ce que le second processeur attend reellement, c'est un bloc dont la longueur
et l'adresse sont connues par ailleurs : un loader qui recoit un bloc les a
deja. `TubeFormat` ecrit donc la charge et rien d'autre, ce qui est aussi ce
qui distingue cette cible : ce n'est pas une autre maniere d'ecrire un fichier,
c'est une autre maniere de le placer.

La cible `tube` ne declare que **ce qui est deja pris** dans l'espace
d'adressage, parce que c'est ce qu'un programme doit savoir pour ne pas ecraser
le systeme qui le fait tourner : `HIMEM` a `$8000`, le systeme du second
processeur a `$F800`, la page 2 des indirections du systeme a `$0200`. Aucun
E/S n'est nomme : les registres du tube sont ceux du processeur principal, et
un programme du second processeur qui les nommerait irait lire du materiel
absent de son espace d'adressage.

**Le processeur est expose dans les deux variantes.** Le guide de l'utilisateur
dit 6502B, le manuel de service dit 65C02, et les cartes survivantes portent
des 65C02. Les deux sources sont serieuses et disent des choses differentes :
le catalogue propose donc `tube` et `tube65c02` plutot que d'en designer un
comme vrai.

### Priorite de l'adresse

Les formats qui ont besoin d'une adresse appliquent tous la meme regle :

```
1. target.OriginAddress   — adresse reellement obtenue a l'assemblage
2. target.LoadAddress     — valeur de la cible ou de la configuration
3. constante de repli     — propre au format
```

`OriginAddress` l'emporte parce qu'il reflete la realite de l'assemblage :
si le source porte un `.org` different de l'adresse de chargement prevue,
c'est le `.org` qui fait foi. `PrgFormat_PrefersOriginAddressOverLoadAddress`
et `PrgFormat_FallsBackToLoadAddress` verifient les deux branches.

Les constantes de repli, quant a elles, sont les adresses de chargement
historiques de la machine : `$0801` pour `prg`, `$0800` pour `a2bin`,
`$0600` pour `xex`. Elles ne s'appliquent que si la cible ne fournit rien.

### `ines` en particulier

`InesFormat` (`:17`) detecte un en-tete deja present et ne le reconstitue
pas. C'est ce qui permet a `example_bomberman-nes` de fonctionner : la sortie
combinee commence par `NES_Header.bin`, qui contient deja l'en-tete, et le
format le laisse passer intact.

Le mapper est reparti sur deux octets : bits 4-7 de l'octet 6 pour les
bits 0-3, bits 4-7 de l'octet 7 pour les bits 4-7 (`:41`, `:58`). Le
miroir vertical et la batterie sont codes dans l'octet 6. Une charge plus
grande que PRG+CHR produit un diagnostic plutot qu'une troncature
silencieuse (`:25-31`).

### `xex` en particulier

`AtariXexFormat` refuse une charge qui deborde `$FFFF` (`:17`) plutot que de
la tronquer. Si `RunAddress` est defini, un enregistrement de demarrage est
ajoute apres la charge, aux vecteurs `$02E0`/`$02E1` (`:32`).

### `ihex` et `srec` en particulier

Ces deux formats sont **independants de la machine** : ils transportent une
adresse, donc la meme charge peut viser plusieurs systemes. C'est ce qui les
rend utiles pour un binaire partage entre cibles.

`IntelHexFormat` emet un enregistrement de type 03 (adresse de depart), des
enregistrements de donnees de 16 octets, un enregistrement de type 04
(adresse etendue) des que la charge franchit une frontiere de 64 Ko, et un
enregistrement de type 01 (fin). Le champ « nombre d'octets » compte les
**seuls octets de donnees**, conformement a la specification.

`MotorolaSrecFormat` utilise des adresses sur 24 bits et n'a donc pas cette
limite.

---

## 5. Memoire multi-regions (T6)

La configuration peut decrire des **regions nommees** : adresse, taille,
banque, type `ro`/`rw`/`bss`, et le stockage `load` quand il differe de
l'execution.

```json
{
  "Target": {
    "System": "nes",
    "Regions": [
      { "Name": "PRG0",   "Address": "$C000", "Size": "$3000", "Bank": 0, "Type": "ro" },
      { "Name": "PRG1",   "Address": "$F000", "Size": "$0FFA", "Bank": 1, "Type": "ro" },
      { "Name": "VECTORS", "Address": "$FFFA", "Size": "$6",    "Type": "ro" },
      { "Name": "OAM",    "Address": "$0200", "Size": "$0100", "Type": "rw" },
      { "Name": "VARS",   "Address": "$0300", "Size": "$0180", "Type": "bss" }
    ]
  }
}
```

| Champ | Obligatoire | Sens |
|---|---|---|
| `Name` | non | nom de la region, repris dans les diagnostics et dans les symboles derives |
| `Address` | **oui** | base de la region, c'est-a-dire l'adresse **d'execution** : c'est elle que le `.org` est valide contre |
| `Size` | non | taille en octets ; absente, la region va jusqu'a `$FFFF` |
| `Bank` | non | numero de banque, pour une memoire banquee |
| `Type` | non | `ro` (defaut), `rw` ou `bss` |
| `Load` | non | adresse de **stockage**, quand elle differe de l'execution |

`Address` et `Size` sont des **chaines**, comme les autres adresses de la
configuration : `"$C000"` et `"49152"` sont acceptes.

### Une region ne place rien

Une region est **declarative** : elle dit ce que la cible attend. Elle ne
place aucun octet, ne remplace pas le `.org` et ne modifie pas la sortie. Une
configuration **sans** section `Regions` a une carte vide, qui ne valide rien :
c'est le comportement d'avant T6, et il est verifie par
`ConfigurationSansRegions_LaLigneDeCommandeSeComporteCommeAvant`.
`example_bomberman-nes` produit le meme octet pour un octet avec et sans
`Regions`.

Ce qui change, c'est qu'un `.org` **ne peut plus atterrir hors de toute carte
en silence**.

### Validation croisee des `.org`

Un `.org` est verifie contre la fenetre d'execution des regions declarees :

- dans une region `ro` ou `rw` : accepte, sans rien changer d'autre ;
- dans **aucune** region : erreur, et la region **attendue** est nommee ;
- dans une region **`bss`** : erreur, puisque cet espace est reserve et
  n'occupe aucun octet du fichier.

**Quelle region est attendue.** La regle est la seule qui s'énonce sans
deviner : les regions sont triees par adresse de depart, et la region attendue
est la derniere dont le debut est inferieur ou egal a l'adresse. Donc une
adresse inferieure a toutes les regions attend la premiere, et une adresse
tombee dans un **trou** entre deux regions attend celle que le trou suit.

```
Line 3 - File BMAN_BANK1.NAS - Type $C000 lands outside every declared region.
Expected region: PRG1 $F000-$FFF9 (ro). Declared regions: PRG1 $F000-$FFF9 (ro).
```

Le message nomme aussi **toutes** les regions declarees, parce que la region
attendue n'est qu'une probabilite : la liste est la partie sure du message.

### Placement croise `load` / `run`

`Address` est l'adresse d'execution, `Load` l'adresse de stockage. La
difference est ce qui rendra au linker (T4) et au kernal GEOS (T9) la
possibilite de relocaliser : `MemoryMap.TryFindByLoad` retrouve la region d'un
adresse de stockage.

### `bss` reserve sans produire d'octet

Une region `bss` declare de l'espace reserve : `TotalBssSize` et
`ReservedAt` y repondent, et la sortie du fichier est inchangee. Cote source,
chaque region produit des symboles `NOM_START`, `NOM_LOAD`, `NOM_RUN`, et
`NOM_END` / `NOM_SIZE` si une taille est declaree, ce qui permet d'ecrire :

```
.memarea VARS_START
vars .res VARS_SIZE
```

sans ecrire l'adresse en dur. Ces symboles s'ajoutent aux symboles materiels
et n'existent que si des regions sont declarees.

### Ce que T6 ne fait pas

- **Le depassement d'une region.** Seul le `.org` est verifie. Detecter qu'une
  emission *deborde* la fin d'une region demanderait un point d'observation
  dans l'emetteur : c'est `Core/AssemblerEngine.cs` et
  `Output/BinaryEmitter.cs`, les deux fichiers que T1/T2 ont le plus modifies,
  reserves ici.
- **Le catalogue.** `SystemCatalog` ne porte pas encore de regions par
  preset : `TargetResolver` et `ResolvedTarget` sont reserves a T3/T14, donc
  une region ne se declare que dans le JSON, jamais dans un preset de cible.

---

## 6. Les variantes materielles comme options (T14)

Ce qui varie d'une machine a l'autre n'est pas toujours la meme chose. Le
plan en faisait un seul cas ; il y en a trois, et les traiter de la meme facon
produirait soit des cibles en double, soit des tables qui mentent.

| Cas | Ce qui change | Exemple |
|---|---|---|
| **revision de puce** | aucune adresse ; la revision elle seule | 6581 contre 8580 |
| **norme de television** | le rythme, et parfois un bit dans un registre | NTSC 262 lignes, PAL 312 sur le NES |
| **puce de video** | les adresses, entierement | le Plus/4 n'a pas de VIC-II du tout |

### Ce que le plan affirmait, et qui est faux

- **« Le 8580 du C64C change les espaces memoire visibles. »** Non. Le C64C a
  bien un VIC-II a `$D000` et bien sa memoire de couleurs a `$D800` : elle est
  soit une 2114 discret, soit integree au Super-PLA. Ce qui change est la
  revision de la puce de son, et elle ne deplace aucune adresse. Le C64C est donc
  un C64 dont le nom change, pas une carte qui bouge.
- **« L'adresse de la table de sprites du NES est `$0200` en NTSC et `$0300`
  en PAL. »** Non. L'OAM du PPU est interne, il n'a pas d'adresse du tout ;
  `$0200` et `$0300` sont deux pages tampons que des logiciels choisissent
  librement. Le catalogue donne les deux nombres qui sont reels et mesurables,
  `FRAME_LINES` et `VISIBLE_LINES`.
- **« Le PET a un 6847. »** Non. Aucun appareil Commodore n'a de 6847. Le 2001
  et le 2001-N n'ont meme pas de controleur video : leur temporisation est en
  logique discrete. Et ils n'ont pas d'ACIA non plus — le 6551 du PET est
  l'addition du SuperPET, a `$EFF0`.
- **« Le C128D a un PEKKA a `$D800`. »** Introuvable dans les sources
  consultees, qui placent le 8563 a `$D600` sur la carte 310379, commune au
  C128 et au C128D. Le C128D est donc expose sans symbole de PEKKA plutot
  qu'avec une adresse inventee.

### Le modele

`HardwareOptions` porte quatre options : `Model`, `VideoStandard`,
`VideoChip`, `SoundChip`. Elles se choisissent par configuration :

```json
{ "Target": { "System": "c128",
               "Hardware": { "Model": "c128", "VideoChip": "vdc", "SoundChip": "8580" } } }
```

`HardwareProfile.Build` transforme ces options en table de symboles : **une
table par famille**, puis les differences appliquees. Le catalogue ne detient
plus aucune table de C64, de VIC-20 ou de NES : une seconde table pour la meme
machine serait un second endroit ou oublier la meme correction.

### Les cibles nommees ne font que preselectionner

```
c64, c64c, c64-pal, c64-ntsc
c128, c128-vdc, c128d
vic20, vic20-pal, vic20-ntsc
plus4, c16
pet2001, pet2001n
cbm2-40, cbm2-80
nes, nes-pal, nes-ntsc
```

Une cible nommee ne differes d'une autre que par ce qu'elle preselectionne :
meme format, meme adresse de chargement, memes registres. C'est verifie.

`-t list` affiche `hw=...` pour chaque variante. C'est la seule trace de la
machine visee dans la sortie : un build PAL et un build NTSC produisent les
memes octets, donc rien d'autre ne dirait lequel a ete fait.

### Les combinaisons impossibles sont refusees

Un CRTC sur un PET 2001, un VDC sur un C64, un SID sur un Plus/4 : chacun de
ces cas produirait une table dont les adresses repondent par de la memoire
vive. `HardwareRules.Reject` refuse la combinaison en nommant la raison, et
`TargetResolver` leve.

### L'adresse de chargement d'une machine bankee

Un PRG commence par deux octets : l'adresse ou la machine pose le code. Le
repli historique est `$0801`, celui du C64. Le garder pour une machine qui
banque sa memoire — le CBM-II, dont la cible ne declare aucune adresse —
produirait une image que rien ne signale comme fausse. Le format refuse donc
d'ecrire un PRG pour une cible qui a des options materielles et aucune adresse,
et demande une adresse ou un `.org`.

---

## 7. Un symbole non defini, en mode fichier unique (T13)

Le moteur de traitement des symboles laisse en place toute expression qu'il
n'a pas reussie a resoudre, et l'espace qu'il reserve est **un zero**. Dans un
build multi-fichiers c'est correct : un nom utilise par un fichier peut etre
defini par un fichier lu plus tard, et l'orchestrateur verifie les restes une
fois tous les fichiers de symboles charges
(`MultiSegmentOrchestrator.cs:121`).

Un assemblage **seul**, lui, n'a pas de fichier plus tard. Le zero restait en
place, le fichier s'ecrivait, le code de sortie etait zero, et l'image etait
fausse sur la machine. C'est exactement l'« erreur de resolution differee » que
le plan signale comme risque.

La correction est un choix explicite, `AssemblerOptions.ReportUndefinedSymbols` :

- **faux** par defaut, parce que le meme moteur sert aux deux roles ;
- **vrai** pour l'assemblage autonome de la ligne de commande.

Un nom declare par `.import` n'est jamais refuse : c'est le linker qui le
resout, et le refuser ici rendrait un module impossible a assembler seul.

Le message cite le nom **et la ligne**, la ligne etant reprise de la table de
relocations qui porte deja cette provenance. Une erreur qui ne nomme que le
symbole envoie le lecteur chercher dans tout le fichier.

---

## 8. Le mode direct reste la contrat

Toute evolution doit preserver :

- chaque fichier s'assemble seul et produit un blob **deja place a son
  adresse reelle** ;
- l'ordre de `Output.Files` ne fait que **confirmer** le placement, il ne
  le determine pas ;
- la sortie est **gravable sans linkeur**.

Le format d'objet `.w65` et le linker (T3, T4) sont **ajoutes a cote**. Le
linker produit le mode A, il ne le remplace pas.
