# WinASM65 — Le format de module `.w65`

Ce document est la specification du format d'objet **proprietaire** que le
plan introduit en tache T3. Il est inspire du modele des DLL Windows, et
**deliberement pas** du format O65 de cc65 : le plan a tranche que WinASM65
ne se rapproche pas de ca65/ld65, parce que la motivation est le partage de
routines entre projets, pas la compatibilite d'outils.

Ce document est normatif pour T3. Il decrit ce que le format doit contenir
et pourquoi, pas ce que le code sait faire aujourd'hui — l'etat actuel est
decrit dans `docs/pipeline.md`, section 8.

---

## 1. Pourquoi un format proprietaire

Le partage « facon DLL » a une propriete structurante : **une DLL expose des
noms, pas des adresses**. Le linker resout des **noms de symboles** entre
modules. Un `.w65` doit donc pouvoir distinguer :

- un symbole **defini** dans ce module, qui peut etre exporte ;
- un symbole **reference** par ce module, qui doit etre resolu ailleurs.

Cette distinction n'existe pas aujourd'hui. `MultiSegmentOrchestrator`
echange des `.symb` / `.Unsolved` bruts, et les dependances sont declarees a
la main dans le JSON. Rien ne dit qu'un symbole est exporte plutot que
simplement defini, ni quel module est cense l'exporter.

| Concept DLL Windows | Equivalent WinASM65 |
|---|---|
| `__declspec(dllexport)` | directive d'export dans le source |
| `dllimport` | directive d'import, resolu par nom |
| Table d'exports / d'imports | idem dans le `.w65` |
| Image base, rebasing | adresse de charge + relocations |
| `LoadLibrary` | stub runtime (cibles texte) ou kernal GEOS |
| Dependency Walker | visualiseur HTML + erreur d'import non satisfait |

---

## 2. Structure du fichier

Un `.w65` est un binaire. Toutes les tables sont precedees de leur taille,
pour qu'un lecteur puisse se passer d'en deviner la structure. L'ordre des
sections est impose.

```
[ En-tete        16 octets        ]
[ Table des segments                ]
[ Table des exports                 ]
[ Table des imports                 ]
[ Table des relocations             ]
[ Charge utile des segments         ]
```

### 2.1 En-tete (16 octets)

| Offset | Taille | Champ |
|---|---|---|
| $00 | 4 | magic `W65` (0x57 0x36 0x35) |
| $04 | 2 | version du format, majeure |
| $06 | 2 | version du format, mineure |
| $08 | 4 | position de la table des segments (octets) |
| $0C | 4 | taille totale du fichier |

Le magic vaut `W65`, et non `O65`, pour eviter toute confusion avec le
format O65 de cc65. Un fichier commencant par `o65` **n'est pas** un module
WinASM65.

### 2.2 Table des segments

Les segments sont **nommes**. Un nommer est ce qui permet a une meme routine
d'etre placee a plusieurs adresses : le linker duplique le segment, il ne le
deplace pas.

| Champ | Taille | Role |
|---|---|---|
| nom | longueur + octets | identifiant du segment, reference par les exports et les relocations |
| taille | 4 | longueur en octets |
| alignement | 4 | alignement souhaite |
| type | 1 | `ro` (lecture seule), `rw` (lecture-ecriture), `bss` (reserve) |
| banque | 1 | index de banque, ou $FF si non applicable |
| offset dans le fichier | 4 | position de la charge utile |

Un segment `bss` n'occupe pas de place dans le fichier : il est reserve
seulement. C'est ce qui permet de decrire une zone de variables sans
l'ecrire.

Les types `ro`/`rw`/`bss` et la banque sont la brique de base de la memoire
multi-regions (tache T6). Les definir dans le format plutot que dans la
seule configuration permet a un **module** de porter sa propre contrainte,
independant de la cible ou il est lie.

### 2.3 Table des exports

Un export associe un **nom** a un **segment** et un **offset**. Il ne
contient jamais d'adresse absolue : l'adresse depend du placement, qui
n'est pas connu a l'assemblage.

| Champ | Taille | Role |
|---|---|---|
| nom | longueur + octets | symboleVisible de l'exterieur |
| index de segment | 4 | segment ou vit le symbole |
| offset dans le segment | 4 | position relative au debut du segment |

### 2.4 Table des imports

Un import associe un nom au **module attendu**. C'est ce qui permet un
diagnostic utile : « le symbole `DrawTile` est importe par `sprite.asm` mais
aucun export ne le fournit », ou « `DrawTile` est fourni par `graphics.w65`
mais `sprite.asm` l'importe de `tiles.w65` ».

| Champ | Taille | Role |
|---|---|---|
| nom | longueur + octets | symbole recherche |
| module attendu | longueur + octets | nom du module qui doit l'exporter |

Le module attendu est **indicatif** : il permet un diagnostic precis, mais
la resolution finale se fait par nom, comme Windows. Un module peut exporter
un symbole sans que l'importateur ait nomme le bon module, et le linker
signale alors la divergence plutot que d'echouer silencieusement.

### 2.5 Table des relocations

C'est le coeur du format, et la piece qui manque aujourd'hui.

| Champ | Taille | Role |
|---|---|---|
| offset dans le segment | 4 | ou ecrire la valeur corrigee |
| largeur | 1 | 1 (octet) ou 2 (mot) |
| type | 1 |voir ci-dessous |
| index de symbole | 4 | symbole vise, ou symbole importe correspondant |
| fichier source | longueur + octets | **provenance** |
| ligne source | 4 | **provenance** |

Les deux derniers champs sont ce qui distingue une relocation exploitable
d'une relocation aveugle. Le plan exige que le diagnostic nomme **le
symbole, le module qui l'importe et le module exporteur attendu** ; sans la
provenance, un echec de resolution ne peut produire qu'un numero de ligne
dans un fichier inconnu, ce qui n'aide personne a corriger quoi que ce soit.

### Types de relocation

| Type | Signification |
|---|---|
| `abs8` | adresse 16 bits, forme courte — le cas `lda label` |
| `abs16` | adresse 16 bits, forme longue |
| `zp8` | adresse zero-page, 1 octet |
| `rel8` | branchement conditionnel, 1 octet |
| `seg` | adresse de segment, pour le chargement dynamique |

La distinction `abs8` / `abs16` est le point le plus subtil du format, et
celui que le plan designe comme **risque numero 1** du chantier. Aujourd'hui
`lda #$05` et `lda label` produisent le meme `ExpressionResult` resolu. Si le
type de relocation est choisi sans savoir si l'operande contenait un
symbole, la sortie est fausse **et silencieuse**. C'est pourquoi T1 (le type
d'expression atteint l'emetteur) et T2 (la table dans l'emetteur) sont
indissociables et doivent former une seule PR.

---

## 3. Directives source

Le source doit pouvoir declarer ses exports et ses imports.

```
        .export DrawTile, LoadCharset
        .import  PutPixel, gfx_module
```

`.export` marque des symboles comme visibles de l'exterieur ; `.import`
declare une dependance dont l'assembleur n'a pas besoin de connaitre la
definition pour produire le code, mais dont le linker a besoin pour
resoudre.

L'assembleur emet alors un `.w65` dans lequel ces symboles apparaissent
exclusivement dans la table d'exports ou la table d'imports. Un symbole a la
fois defini et exporte apparait dans les deux segments concernes : dans
l'export pour les autres, et comme un symbol local pour ses propres
references.

Ces directives sont absentes du code actuel. Les 16 directrices enregistrees
sont `.org`, `.memarea`, `.incbin`, `.include`, `.byte`, `.word`, `.macro`,
`.endmacro`, `.if`, `.ifdef`, `.ifndef`, `.else`, `.endif`, `.rep`,
`.endrep`, `.end`.

---

## 4. Interaction avec le mode direct

Le format `.w65` est **ajoute a cote** du mode direct, jamais a la place.

- Le mode direct ne produit pas de `.w65`. Il continue d'ecrire un blob
  place a son adresse reelle, gravable sans linkeur.
- Le mode module produit un `.w65` qui n'est **pas** directement gravable :
  il contient des segments non places et des relocations non resolues.
- `WinASM65 link` consomme des `.w65` et produit un **binaire gravable**,
  qui est le mode A. Il ne le remplace pas.

C'est la decision structurante du plan : « le linker est au build, sauf la ou
le systeme sait relocaliser ». Pour la NES, ou le code vit en ROM, le linker
sert a la construction. Pour GEOS, ou le kernal relocalise au chargement,
le linker produit la table que le kernal consommera.

### Coexistence avec MultiSegmentOrchestrator

`MultiSegmentOrchestrator` reecrit des `.o` sur disque. Le plan signale
explicitement ce point comme un risque. Le format `.w65` doit soit
coexister avec ce mecanisme, soit le remplacer proprement — et dans le
deuxieme cas, la suppression doit etre un acte delibere, pas une
consequence de l'introduction du nouveau format.

---

## 5. Diagnostic : l'exigence de nommage

Le format existe d'abord pour rendre un diagnostic possible. Un import non
satisfait doit nommer :

1. le **symbole** recherche ;
2. le **module qui l'importe** ;
3. le **module exporteur attendu**, d'apres la table d'imports ;
4. le **fichier source et la ligne** du site, d'apres la table de
   relocations.

C'est la meme exigence que pour les exports en double (les modules en
conflit) et les cycles d'archives (le cycle nomme). Sans provenance dans le
format, ces trois diagnostics sont impossibles a produire correctement.

---

## 6. Ce que le format ne fait pas

- Il ne dit pas **ou** un module sera place. Le placement est l'affaire du
  linker et de la cible.
- Il ne contient pas de **chemin d'execution**. C'est le role du fichier de
  configuration, ou du point d'entree du conteneur (secteur INFO de GEOS,
  vecteurs d'un conteneur BBC ou ProDOS).
- Il ne remplace pas l'**encodeur de tokens** des cibles texte. Ce
  encodeur-nedepend ni des relocations, ni du linker, ni du format — c'est
  pourquoi il est parallelisable avec le coeur (voir `docs/targets-text.md`).
