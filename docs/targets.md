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
| **Fichier texte** | DOS 3.2/3.3, ProDOS, Waterloo BASIC, GECOS, BBC BASIC (prevus, T10/T15) | **texte tokenise** | non au chargement ; stub runtime si partage |

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
| `c64` | 6502 | `prg` | `$0801` | VIC, SID, CIA1, CIA2 |
| `c128` | 6502 | `prg` | `$1C01` | idem `c64` |
| `vic20` | 6502 | `prg` | `$1001` | VIC, VIA1, VIA2 |
| `pet` | 6502 | `prg` | `$0401` | — |
| `plus4` | 6502 | `prg` | `$1001` | — |
| `c16` | 6502 | `prg` | `$1001` | — |
| `x16` | 65c02 | `prg` | `$0801` | — |
| `apple2` | 6502 | `a2bin` | `$0800` | KBD, TXTCLR |
| `apple2e` | 65c02 | `a2bin` | `$0800` | KBD, TXTCLR |
| `atari8` | 6502 | `xex` | `$0600` | DMACTL, COLBK, CONSOL |
| `atari800` | 6502 | `xex` | `$0600` | idem `atari8` |
| `atari2600` | 6502 | `rom` | — | VSYNC, WSYNC, COLUBK… (rom 4096) |
| `vcs` | 6502 | `rom` | — | idem `atari2600` |
| `bbc` | 6502 | `bbc` | `$0E00` | OSWRCH, OSWORD, CRTC, ULA, les deux VIA |
| `bbcmicro` | 6502 | `bbc` | `$0E00` | idem `bbc` |
| `tube` | 6502 | `tube` | `$2000` | bornes de sa carte memoire |
| `bbc2p` | 6502 | `tube` | `$2000` | idem `tube` |
| `tube6502` | 6502 | `tube` | `$2000` | idem `tube` |
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
| `bbc` | `BbcFormat` | fichier BBC avec en-tete : executable, binaire ou texte |
| `tube` | `TubeFormat` | la charge seule, ce qu'attend OSLOAD |

### `bbc` en particulier (T13)

La BBC deduit la nature d'un fichier d'un seul octet, puis de la longueur
que cet octet encode. Les trois formes :

| Type | Octet 0 | Longueur | Donnees | Adresse |
|---|---|---|---|---|
| texte | `$FF` | aucune | a partir de l'octet 1 | aucune |
| binaire | `$00`-`$7F` | `T * 256 + octet 1` | a partir de l'octet **6** | `$4F $4F` puis 2 octets, a l'octet 4 |
| executable | `$80`-`$FE` | `(T AND $3F) * 256 + octet 1` | a partir de l'octet **2** | `$4F $4F` puis 2 octets, au debut des donnees |

Deux consequences qu'il faut avoir a l'esprit :

- **Le bit 6 ne porte pas de longueur.** Un executable s'arrete donc a 16 Ko
  la ou un binaire va a 32. Depasser la limite donne un diagnostic, jamais une
  troncature : un fichier tronque reste un fichier valide pour la machine, et
  l'erreur n'apparaitrait qu'a l'execution.
- **La longueur annoncee est celle du fichier**, en-tete et adresse compris.
  Ecrire `octet 1 = longueur de la charge` produit un fichier dont les
  $4F $4F d'adresse sont pris pour des instructions.

`$4F $4F` est le marqueur « les deux octets qui suivent sont une adresse ». Il
sert d'en-tete dans le binaire et de prefixe dans l'executable. Inserer une
adresse maladroitement est un piege : un source qui ecrit lui-meme `$4F $4F` en tete
resserait decale de quatre octets. `BbcFormat` reconnait ce cas et garde
l'adresse du source au lieu d'en inserer une seconde.

Le genre se choisit par configuration, `BbcFileType` : `exec` (defaut),
`binary`, `text`. Un genre inconnu retombe sur `exec` plutot que d'echouer :
c'est le seul qui reste plausible pour du code.

### `tube` en particulier (T13)

OSLOAD ne lit aucun en-tete. Un octet de plus serait execute comme une
instruction, donc `TubeFormat` ecrit la charge et rien d'autre. C'est aussi
pourquoi l'adresse de chargement n'est pas une donnee du fichier mais une
regle de la machine, portee par la cible.

La cible `tube` ne declare que **les bornes de sa carte memoire**
(`TUBERAM`...`TUBEROMEND`), et deliberement pas les registres du tube : ce
sont des registres du processeur principal, et un programme du second
processeur qui les nommerait irait lire du materiel qu'il ne voit pas. C'est
la difference qui en fait deux cibles et non une seule avec deux adresses.

**Une adresse reste ouverte.** Les sources du second processeur 6502 sont
assemblees pour `$0200`, la ou est la RAM, alors qu'OSLOAD charge a `$2000`.
Les deux valeurs sont reelles et la documentation diverge. La cible prend
`$2000`, celle du service, et `LoadAddress` la change : le choix appartient au
programme, pas a l'ecrivain.

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

## 6. Un symbole non defini, en mode fichier unique (T13)

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

## 7. Le mode direct reste la contrat

Toute evolution doit preserver :

- chaque fichier s'assemble seul et produit un blob **deja place a son
  adresse reelle** ;
- l'ordre de `Output.Files` ne fait que **confirmer** le placement, il ne
  le determine pas ;
- la sortie est **gravable sans linkeur**.

Le format d'objet `.w65` et le linker (T3, T4) sont **ajoutes a cote**. Le
linker produit le mode A, il ne le remplace pas.
