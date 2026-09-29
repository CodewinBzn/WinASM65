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
| `bbc` | 6502 | `bin` | `$0E00` | OSWRCH, OSBYTE, OSWORD |
| `bbcmicro` | 6502 | `bin` | `$0E00` | idem `bbc` |
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
`Format`, `LoadAddress`, `RunAddress`, `RomSize`, `DefineHardwareSymbols` et
`Ines`. Les adresses sont des **chaines**, parsees par `NumericLiteral` pour
accepter `$0801` comme `2049`.

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

## 5. Memoire multi-regions (non implemente)

Le plan prevoit (tache T6) d'etendre la configuration pour decrire des
**regions nommees** : adresse, taille, banque, type `ro`/`rw`/`bss`, avec
placement croise `load`/`run` et validation croise des `.org`.

L'etat actuel ne permet **rien** de tout cela :

- `BinaryEmitter` (`src/Output/BinaryEmitter.cs:28`) tient une seule
  `List<byte>`, un seul `CurrentAddress`, un seul `OriginAddress`.
- `TargetConf` n'a aucun champ de region ou de banque.
- Un `.org` est un repositionnement absolu du curseur, sans notion de
  contiguite, de taille, ni de verification de depassement.

Un `.memarea` existe (directive enregistree dans `AssemblerEngine.cs:136`),
mais il ne sert qu'a la directive `.res` : il tient un curseur de zone
memoriel, il ne decrit pas une region verifiable.

La consequence : today, une cible multi-banques comme la NES n'est decrite
que par la **convention** des `.org` dans le source. Rien dans la
configuration ne dit qu'une banque fait 16 Ko, ni ne refuse un `.org` qui
debordonne. `example_bomberman-nes` fonctionne parce que `BMAN_BANK1.NAS` et
`BMAN_BANK2.NAS` portent leurs `.org` et que le JSON de sortie les confirme
par une taille — la verification est faite par l'oeil, pas par le code.

---

## 6. Le mode direct reste la contrat

Toute evolution doit preserver :

- chaque fichier s'assemble seul et produit un blob **deja place a son
  adresse reelle** ;
- l'ordre de `Output.Files` ne fait que **confirmer** le placement, il ne
  le determine pas ;
- la sortie est **gravable sans linkeur**.

Le format d'objet `.w65` et le linker (T3, T4) sont **ajoutes a cote**. Le
linker produit le mode A, il ne le remplace pas.
