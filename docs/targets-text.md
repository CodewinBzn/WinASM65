# WinASM65 — Les cibles texte

Ce document decrit la troisieme famille de cibles : les systemes qui
**n'executent pas de binaire**. Il accompagne `docs/targets.md`, qui en
donne la classification, et `docs/format-module.md`, qui decrit le format
d'objet.

**Aucun de ce qui est decrit ici n'est implemente.** L'encodeur de tokens est
la tache T10, le stub runtime la tache T11, les autres BASIC la tache T15.
Ce document fixe le cahier des charges et les contraintes, avant que le
travail ne commence.

---

## 1. Le point de depart : ces systemes n'ont pas d'executable

DOS 3.2, DOS 3.3, ProDOS, Applesoft, GECOS et BBC BASIC n'executent pas de
binaire 6502. Ils executent un **interpreteur BASIC** qui lit un fichier
**texte tokenise**.

La consequence est directe et structurante : **produire un « executable
DOS 3.3 » n'a pas de sens.** Il n'existe pas de chargeur binaire dans ces
systemes, donc pas d'adresse a laquelle on pourrait graver un blob.

Comparer ces cibles aux systemes de la premiere famille produit un plan
faux. Une routine partagee ne peut pas y etre placee dans une ROM, parce
qu'il n'y a pas de ROM.

---

## 2. Ce que l'encodeur de tokens a besoin de faire

Produire un fichier chargeable par Applesoft suppose de reecrire le source
en **tokens**, pas en octets de code. Leencodeur doit maitriser :

- la **table des 256 tokens**, ou chaque valeur de $80 a $FF est un mot-cle
  du langage (`END` = $80, `GOTO` = $AB, `PRINT` = $BA, …) ;
- le **mode binaire `$96`** : un ligne commencant par ce token est un
  bloc de donnees, pas du texte ;
- l'**adressage** : `$xxxx` pour une adresse absolue, un **numero de
  variable** pour le zero-page ;
- les **formes abregees** : `?` pour `PRINT`, `(N)` pour `GOTO`,
  `:` comme separateur d'instruction.

La conversion n'est pas lineaire. Pour `? "HELLO"` — la forme abregee de
`PRINT "HELLO"` — l'encodeur emet le mot-cle `$BA`, puis la chaine
`$48 $45 $4C $4C $4F` precedee de sa longueur en un octet, puis le
terminateur de fin de ligne. Le mot-cle lui-meme n'a pas d'octet de
longueur : c'est ce qui distingue un mot-cle d'une chaine.

C'est un sujet a part entiere. Le plan le dit explicitement : « ne pas
sous-estimer ni fusionner avec d'autres taches », et son budget de tokens est
la partie la plus longue du chantier.

### Ce dont l'encodeur ne depend pas

C'est ce qui rend T10 **parallelisable** avec le coeur des relocations
(T1/T2) :

| L'encodeur de tokens ne depend pas de | Pourquoi |
|---|---|
| des **relocations** | il ne manipule pas d'adresses de code, mais des tokens de langage |
| du **linker** | il lit un source BASIC, pas des modules |
| du **format d'objet `.w65`** | il n'y a pas de placement de segments |
| du **mode direct** | il produit un fichier texte, pas un binaire |

Il ne lit donc **aucun des fichiers** que le coeur des relocations modifie.
C'est la condition qui permet de lancer les deux en parallele.

---

## 3. Le partage « facon DLL » dans un fichier texte

Puisque ces systemes n'executent pas de binaire, le partage n'y fonctionne
pas comme dans une DLL classique. Le mecanisme est le suivant, et il est
identique pour tous ces dialectes :

1. Le linker produit la routine partagee comme un **bloc de donnees**.
2. Un **stub runtime en assembleur 6502, fourni par WinASM65** dans le
   depot, est injecte dans le fichier texte.
3. A l'execution, le stub **decompresse le bloc en RAM**, puis **applique
   les relocations** sur les sites qu'il a reserves.
4. Le code appelant fait `JSR` vers l'adresse obtenue.

L'invariant central : **aucune information d'adresse absolue n'est figee
dans le fichier texte.** Le stub est le seul a savoir ou il a pose le code.
C'est ce qui permet au meme bloc d'etre charge a des adresses differenties
sans regenerer le fichier.

### Ce que le stub doit savoir

Au moment de l'injection, le stub doit connaitre :

- la **liste des sites a relocaliser** ;
- la **largeur** de chacun (1 ou 2 octets).

C'est exactement le besoin de la table de relocations du linker, presente
sous une forme **executable**. Le lien entre T4 (linker) et T11 (stub) est
donc direct : le linker produit la table, le stub la consomme sous forme de
donnees executables.

### Le stub est du code cible

Point de validation important, et different de tout le reste du chantier :
**le stub ne se valide pas par un test golden.** Il s'execute sur une
machine 6502 reelle ou dans un emulateur. Un test qui comparerait des octets
prouverait que le stub n'a pas change, pas qu'il fonctionne.

La consequence est un cout de mise au point plus eleve, et c'est
explicitement liste comme risque dans le plan.

---

## 4. Conteneurs

Meme encodeur de tokens, conteneurs differents.

| Conteneur | Systeme | Description |
|---|---|---|
| Apple II | DOS 3.2, DOS 3.3, Applesoft | catalogue de fichiers + secteur de donnees, allocation de secteurs ordonnee |
| ProDOS | ProDOS | blocs de 512 octets, **entrelacement par paires de secteurs** (ordre `0, 8, 1, 9, 2, 10, …`), points d'entree, segments |
| Texte brut | BBC BASIC | un fichier texte, sans secteurs — le plus simple |

### DOS 3.2 contre DOS 3.3

La difference tient surtout au **catalogue de fichiers** et a l'**ordre
d'allocation des secteurs**, pas a l'encodeur. Le meme encodeur de tokens
sert aux deux ; ce sont les conteneurs qui different. C'est couvert par T10
sans travail supplementaire.

### ProDOS

Le conteneur ProDOS est le plus structure : blocs de 512 octets,
entrelacement par paires de secteurs, points d'entree explicites, et une
 notion de segments dans le load file. C'est aussi le seul des trois a avoir
un format de segments explicite, ce qui le rapproche du modele `.w65` — mais
le load file ProDOS ne contient pas de table de relocations.

### BBC BASIC

Les tokens sont differents de ceux d'Applesoft, mais le conteneur est un
**fichier texte brut**, sans secteurs entrelaces. C'est le cas le plus
simple de la famille, et il releve de T15 plutot que de T10.

---

## 5. Les quatre BASIC 6502

| BASIC | Perimetre | Conteneur |
|---|---|---|
| **Applesoft** | le plus repandu ; tache T10 | Apple II (DOS 3.2/3.3) |
| **Waterloo Structured BASIC** | Apple II, C64, PET ; le plus widespread apres Applesoft, et le plus proche d'un BASIC « de Production » | variable selon la machine |
| **GECOS** | cible distincte, decouverte avec GEOS | a definir |
| **BBC BASIC** | tokens differents, conteneur texte | texte brut |

Le plan borne explicitement ce perimetre : **les quatre BASIC principaux**.
Les dialectes mineurs ou experimentaux n'en font pas partie, et une nouvelle
cible est un ajout explicite, pas une consequence.

Waterloo Structured BASIC merite une mention : il est « de Production », ce
qui signifie qu'il a des fonctions, des types et une structure de donnees
qu'Applesoft n'a pas. Son encodeur n'est donc pas une simple variation de
celui d'Applesoft.

---

## 6. Validation

Le plan fixe une validation en **deux temps** : **NES puis GEOS**. Pour les
cibles texte, la validation est specifiquement :

- **round-trip** vers un interpreteur Applesoft : le fichier encode doit
  produire exactement le comportement attendu ;
- **partage d'une routine entre deux projets** execute correctement.

Le round-trip est la seule preuve qu'un encodeur est correct. Comparer
l'encodeur a ses propres specifications ne prouve rien sur l'interpreteur
 reel.

---

## 7. Resume des independances

Carte de travail pour l'execution en parallele :

| Tache | Touche | Depend de | Parallelisable avec |
|---|---|---|---|
| T1+T2 | `Expressions/`, `Output/BinaryEmitter.cs`, `Core/AssemblerEngine.cs` | rien | T10 (aucun fichier commun) |
| T3 | `Targets/`, `AssemblyResult` | T1+T2 | T6 (fichiers disjoints) |
| T6 | `Segments/SegmentModels.cs`, `SystemCatalog` | rien | T3 |
| T10 | `TextFormat/` (nouveau) | rien | T1+T2, T3, T6 |
| T11 | stub 6502 | T4 | — |
| T15 | encodeur + conteneurs | T10 | — |

Le point de vigilance reste le suivant, et il est independant du contenu :
deux agents dans le meme repertoire de travail s'ecrasent mutuellement sans
signaler d'erreur. Chaque agent parallel doit avoir son **propre worktree
git**.
