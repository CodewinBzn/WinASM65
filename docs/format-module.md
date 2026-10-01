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
[ Table des symbols locaux   1.1+   ]
[ Charge utile des segments         ]
```

### 2.1 En-tete (16 octets)

| Offset | Taille | Champ |
|---|---|---|
| $00 | 4 | magic `W65` (0x57 0x36 0x35) |
| $04 | 2 | version du format, majeure |
| $06 | 2 | version du format, mineure |
| $08 | 4 | taille du bloc des tables |
| $0C | 4 | position de la charge utile |

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
| offset dans le fichier | 4 | position de la charge utile, **absolue**, mesuree depuis le debut du fichier |
| origine | 2 | adresse `.org` contre laquelle l'unite a ete ecrite |

L'offset est absolu, comme l'en-tete et comme toutes les autres positions du
fichier. La charge utile suit les tables, donc sa base n'est connue qu'une
fois les tables dimensionnees : l'ecrivain mesure les tables, puis les
reecrit avec les offsets definitifs. Un offset relatif a la charge utile
pointerait dans l'en-tete.

L'origine n'est **pas** un placement. C'est l'adresse de repli du linker,
utilisee quand rien d'autre ne contraint le segment ; la perdre a
l'ecriture le laissait placer n'importe ou.

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

Une implementation ne doit pas retenir un seul symbole par site : une
expression comme `lda BASE+DELTA` lit deux symboles, et un linker qui n'en
garde qu'un resolvra le site contre la mauvaise valeur.

### 2.6 Table des symbols locaux (version 1.1)

Meme forme qu'un export : un nom, un index de segment, un offset.

| Champ | Taille | Role |
|---|---|---|
| nom | longueur + octets | etiquette definie par le source et **non** exportee |
| index de segment | 4 | segment ou l'etiquette est posee |
| offset | 4 | position dans ce segment |

Elle est ecrite apres les relocations et n'est lue que si la version mineure
le dit. Voir « Une etiquette nommee par une relocation est resolue, exportee ou
non » plus bas pour ce qu'elle corrige.

### Types de relocation

| Type | Signification |
|---|---|
| `abs8` | adresse 16 bits, forme courte — le cas `lda label` |
| `abs16` | adresse 16 bits, forme longue |
| `zp8` | adresse zero-page, 1 octet |
| `rel8` | branchement conditionnel, 1 octet |
| `seg` | adresse de segment, pour le chargement dynamique |

Cette table est la specification de reference. Elle ne couvre pas encore deux
cas que la cible GEOS rend necessaires et que l'implementation de T1/T2 a
donc dut nommer :

| Type | Signification | Pourquoi la table ci-dessus ne suffit pas |
|---|---|---|
| `imm8` | valeur 1 octet tiree d'un symbole, `lda #MASK` | ni une adresse ni une branche : le linker ecrit une **valeur**, pas une adresse |
| `data8` / `data16` | champ de donnee, `.byte COUNT` / `.word TABLE` | une table de sauts est une suite d'adresses, mais rien dans la syntaxe ne dit que l'auteur y met des adresses |

Ces deux constats ne sont pas des corrections de code face a une doc
fautive : le code n'existait pas. Ce sont des trous de la specification,
signales ici parce que T3 et T4 dependront de la liste reelle des types.

Deux types sont venus de la validation T7, qui execute enfin le code lie
sur un NES emule :

| Type | Signification | Pourquoi il a fallu l'inventer |
|---|---|---|
| `low8` | octet bas de la valeur resolue, `lda #<LABEL` | la selection d'octet ne peut pas etre appliquee a l'assemblage : sur un symbole importe, la valeur n'est pas encore connue |
| `high8` | octet haut de la valeur resolue, `lda #>LABEL` | idem, et c'est le cas le plus courant : montage d'un pointeur sur deux octets |

### Le selecteur d'octet

`<` et `>` ne sont pas du sucre que l'evaluateur peut finir seul. L'evaluateur
les applique quand il peut, c'est-a-dire quand le symbole est deja defini. Un
symbole **importe** ne l'est pas : le resultat est « non resolu », et c'est
justement le cas qui passe par le linker. Sans transport de la selection,
l'adresse entiere arrivait dans un champ d'un octet et la lien echouait sur
`lda #>pointeur` avec « does not fit on one octet » — pour toute etiquette au
dessus de la page zero, donc presque toutes.

La selection est donc decidee par l'evaluateur (`ExpressionResult.Selector`),
conservee sur le chemin non resolu, et traduite en `low8` / `high8` par
`RelocationRecord.TypeFor`. Le linker l'applique apres le decalage d'adresse,
donc `lda #>pointeur` sur une image deplacee de $2000 lit bien le haut de
l'adresse decalee.

Deux limites, nommees plutot que laissees a decouvrir :

- sur un champ de **deux** octets, la selection est ignoree. Un `.word >SYM`
  est une autre erreur et il ne revient pas au linker de la deviner ;
- seul le **premier** selecteur compte, donc le plus exterieur. Un
  `#<(<SYM)` imbrique n'est pas un cas aoin on a un etat, et l'ecrire
  supposerait d'evaluer l'expression, ce qu'on ne peut pas faire ici.

### Une etiquette nommee par une relocation est resolue, exportee ou non

L'assembleur enregistre une relocation pour **toute** reference qui lit un nom,
y compris une etiquette que la meme unite definit trois lignes plus haut. Le
module ne transporte que ses exports, donc une telle reference arrivait au linker
nommant une etiquette qui etait pourtant dans le fichier, et le lien echouait
sur « which no linked module exports ». Exporter `Boucle` pour une boucle
privee n'achetait rien et LearnASM65 ne pouvait pas l'expliquer.

Le `.w65` est donc passe en **version 1.1** et porte une **table de symbols
locaux** : nom, segment, offset. Comme un export, jamais une adresse. Le linker
resout une relocation dans les exports d'abord, puis dans la table du module qui
possede la relocation — l'ordre ne compte que pour un nom qui est les deux, donc
un programme contradictoire, et resoudre un import comme un import est la lecture
qui colle au reste.

Trois consequences, toutes testees :

- une reference a une etiquette locale se resout, **et suit le decalage** : la
  valeur est l'adresse placee du segment plus l'offset, donc elle bouge avec
  l'image comme un export ;
- deux modules peuvent chacun avoir un `Boucle` prive : les tables sont par
  module, aucune collision ;
- quand le nom n'est **ni** exporte **ni** defini, le message nomme les deux
  recherches, pour ne pas renvoyer vers la seule table d'exports.

L'assembleur ne met dans cette table que des **etiquettes**. Une constante
(`Pointeur = $1234`) est une valeur et pas une adresse : la mettre ferait qu'une
relocation nommant un calcul semblerait viser une etiquette. Une etiquette
exportee n'y est pas non plus, elle est deja dans la table d'exports.

**Compatibilite.** La table de symbols est la derniere des tables et c'est la
version mineure qui dit si elle est la : un lecteur 1.0 s'arrete apres les
relocations et l'ignore, un fichier 1.0 se lit avec une table vide plutot qu'avec
du bruit lu dans les donnees du segment. Un module ecrit par l'assembleur actuel
se lie donc avec un linker plus ancien, moins les etiquettes locales — et une
reference a une etiquette locale dans un tel fichier echoue pour une raison
juste : le nom n'y est pas.

Une limite reste : `BuildModule` ne produit qu'un segment par unite, donc une
etiquette posee hors de ce segment — une unite avec plusieurs `.org` — n'a pas
d'offset a donner et n'est pas enregistree. La reference echoue alors en la
nommant, ce qui vaut mieux qu'une place au hasard.

### Etat de l'implementation apres T1/T2

`BinaryEmitter` tient desormais une table de relocations, exposee par
`AssemblyResult.Relocations`. `RelocationRecord` porte le segment, l'offset
dans le segment, la largeur, le type, **tous** les symboles lus, le fichier et
la ligne. `Seg` existe dans l'enumeration mais n'est jamais produit : il
suppose les segments nommes de `.w65`, qui arrivent en T3.


La distinction `abs8` / `abs16` est le point le plus subtil du format, et
celui que le plan designe comme **risque numero 1** du chantier. Aujourd'hui
`lda #$05` et `lda label` produisent le meme `ExpressionResult` resolu. Si le
type de relocation est choisi sans savoir si l'operande contenait un
symbole, la sortie est fausse **et silencieuse**. C'est pourquoi T1 (le type
d'expression atteint l'emetteur) et T2 (la table dans l'emetteur) sont
indissociables et doivent former une seule PR.

**Resolution.** T1 et T2 sont livres. `ExpressionResult` porte desormais le
role de l'expression (`Immediate`, `Address`, `RelativeBranch`, `Data`), la
liste des symboles reellement lus, et la provenance de chacun. Le role est
attribue par l'appelant a partir du mode d'adressage decode, jamais devine
par l'evaluateur : `lda #$05` et `lda label` ont la meme forme, et seul le
mode les distingue.

La largeur est decidee **apres** `Cpu6502.TryOptimizeZeroPage`, qui peut
reduire un operande absolu a un octet. Enregistrer la largeur avant cette
decision produit une relocation fausse de facon silencieuse.

---

## 2bis. Archives (`.w65a`)

Une archive regroupe plusieurs modules `.w65` et peut en referencer une
autre. `WinASM65 link` accepte une archive en entree et la developpe, de
sorte que le linker n'a jamais a connaitre les archives.

```
[ magic "W65A"  4 octets ]
[ version majeure 2 ][ version mineure 2 ]
[ nombre de membres  4 ]
[ taille des tables 4 ]
[ debut de la charge utile 4 ]  absolu
[ table des membres            ]  nom, offset absolu, longueur
[ table des references         ]  chemins d'archives
[ blobs des membres            ]  chacun un .w65 complet
```

**Pas d'index de symboles stocke.** L'index est l'union des tables d'export
des membres, calculee a l'ouverture. Un index stocke serait une seconde
copie de faits qui deja vivent dans les membres, et les deux pourraient
diverger ; rien ici ne peut contenir une copie perimee.

**References.** Un chemin relatif a l'archive qui le nomme, ce qui permet
de deplacer une bibliotheque avec ses dependants tant que leurs positions
relatives tiennent. La resolution est transitive. Un cycle est une
**erreur** : il n'empêche pas la resolution, puisque chaque archive n'est
developpee qu'une fois, mais il signale toujours une faute dans la
description du projet et se taire la cacherait. Le message nomme toute la
chaine, parce qu'au-dela de deux archives la paire qui boucle n'est pas
evidente.

**Ordre.** Une archive referencee est placee avant celle qui la reference,
donc avant son dependant dans l'ordre de liaison.

**Export en double.** Erreur, et non avertissement : rien dans la
resolution ne dit lequel des deux modules un importateur voulait, et en
choisir un silencieusement relierait l'appel a la mauvaise routine. Les
deux fournisseurs sont nommes, car « symbole en double X » seul laisse le
lecteur deviner quelle archive ouvrir.

```
WinASM65 archive base.w65 milieu.w65 -o lib.w65a -ref base.w65a
WinASM65 link lib.w65a main.w65 -o game.bin
```

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
