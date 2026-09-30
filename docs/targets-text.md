# WinASM65 — Les cibles texte

Ce document decrit la troisieme famille de cibles : les systemes qui
**n'executent pas de binaire**. Il accompagne `docs/targets.md`, qui en
donne la classification, et `docs/format-module.md`, qui decrit le format
d'objet.

**L'encodeur de tokens et les conteneurs sont en place ; le stub runtime ne
l'est pas.** L'encodeur est la tache T10, les conteneurs et les autres BASIC
la tache T15, le stub runtime la tache T11. Ce qui suit fixe ce que chaque
systeme oblige a faire, et ce qui reste a valider sur une machine.

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

Le verbe qui les atteint est `WinASM65 basic` :

```
WinASM65 basic -f programme.bas -o programme.bin -dialect applesoft
WinASM65 basic -f programme.bas -o programme.po  -format prodos
WinASM65 basic -f programme.bas -o programme.dsk -format dos33 -list programme.lst
```

`-dialect` prend `applesoft`, `waterloo` ou `bbc` — `applesoft` par defaut.
`-format` prend `bin`, `prodos`, `dos32` ou `dos33` — `bin` par defaut, ce
qui ecrit le programme tel qu'il se tient en memoire. `-list` relit le fichier
ecrit et le rend en texte : c'est l'aller-retour que le format verifie, donc
c'est aussi la seule preuve que le fichier est bon.

Une nuance que la liste montre et que la source ne montre pas : la liste rend
**les octets du programme, pas le source d'origine**. Une chaine Applesoft y
revient sous forme de sa longueur et de ses caracteres, une reference BBC sous
forme de ses trois octets, parce que c'est ainsi que le fichier les porte. Le
tour est exact dans les deux sens, ce qui est la seule propriete qui compte,
et il ne produit pas un fichier qu'une personne pourrait reediter.

Le lecteur de source (`TextFormat/BasicSource.cs`) applique trois regles qui
viennent des machines plutot que du gout : il coupe les lignes sur le seul
retour a la ligne — `$0B` et `$0C` sont des caracteres legitimes dans un VDU
et dans une chaine —, il numerote un programme dont aucune ligne n'est
numerotee, de 10 en 10, et il refuse un programme dont certaines lignes le sont
et d'autres non. Un numero qui revient en arriere est signale et les lignes
sont laissees en place : l'ordre du fichier est l'ordre d'execution.

| Conteneur | Systeme | Description |
|---|---|---|
| Apple II | DOS 3.2, DOS 3.3, Applesoft | catalogue de fichiers + liste piste secteur, allocation par paires en 3.2 et secteur par secteur en 3.3 |
| ProDOS | ProDOS | blocs de 512 octets, **entrelacement par paires de secteurs** (ordre `0, 8, 1, 9, 2, 10, …`), points d'entree, segments |
| BASIC V2 | Waterloo Structured BASIC | chaine d'enregistrements lies a `$0801`, plus douze mots derriere `$FF $FF` |
| Texte brut | BBC BASIC | un fichier texte, sans secteurs — le plus simple |

### DOS 3.2 contre DOS 3.3

La difference tient surtout au **catalogue de fichiers** et a l'**ordre
d'allocation des secteurs**, pas a l'encodeur. Le meme encodeur de tokens
serve aux deux ; ce sont les conteneurs qui different.

Implemente dans `src/Targets/AppleDosImage.cs`, d'apres le Disk II Read/Write
Track-Sector, le *DOS Internals* de All About DOS et les notes de format de
CiderPress2 :

- **Volume** : 35 pistes. DOS 3.3 a 16 secteurs de 256 octets par piste
  (143 360 octets), DOS 3.2 n'en a que 13 (116 480 octets). La table des
  secteurs est le secteur 0 de la piste 17.
- **Table des secteurs** : `$00` vaut 4 sur un volume 3.3 et 2 sur un volume
  3.2, `$03` dit la version du DOS qui a formate le disque, `$34` et `$35`
  disent la geometrie, et la carte des secteurs libres commence a `$38`, quatre
  octets par piste, un bit par secteur — **un bit a 1 veut dire libre**.
- **Catalogue** : une chaine de secteurs sur la piste 17, sept entrees de
  35 octets par secteur. Une entree nomme le fichier (30 caracteres en ASCII
  haut, complete par des espaces), son type, le nombre de secteurs qu'il
  prend, et le premier secteur de sa **liste piste secteur**. Le catalogue de
  DOS 3.2 fait trois secteurs, donc 21 fichiers ; celui de DOS 3.3 prend toute
  la piste.
- **Liste piste secteur** : une chaine de secteurs qui contient jusqu'a 122
  paires. C'est elle qui transforme un fichier en secteurs, et elle commence a
  `$0C` du secteur.
- **Allocation** : la difference la plus reelle entre les deux. DOS 3.2 alloue
  **par paires de secteurs**, parce qu'un lecteur de l'epoque ne pouvait
  sauter un secteur qu'en ecrivant celui d'apres : un fichier d'un nombre impair
  de sectors prend une paire qu'il n'utilise pas, le compte du catalogue est
  donc pair, et le premier secteur de chaque paire est pair. DOS 3.3 alloue un
  secteur libre a la fois.
- **Entrelacement** : DOS l'applique en parlant au lecteur, et le volume ne le
  dit nulle part. L'image est donc ecrite **en ordre de secteurs DOS**, ce qui
  la rend comparable octet pour octet avec ce que DOS lit sur un emulateur qui
  n'applique rien ; une image physique a besoin de la table, qui est un option
  du conteneur et pas une propriete du format. C'est la meme decision que
  celle du conteneur ProDOS, et pour la meme raison.

Les deux versions sont accessibles par `-format dos32` et `-format dos33`. Un
fichier de programme y est de type A, precede de sa longueur en deux octets ;
un fichier binaire de type B, precede de son adresse de chargement. Ce sont
les deux formes de fichier que DOS rend a l'interpreteur : DOS donne un bloc,
pas un programme.

### ProDOS

Le conteneur ProDOS est le plus structure : blocs de 512 octets,
entrelacement par paires de secteurs, points d'entree explicites, et une
 notion de segments dans le load file. C'est aussi le seul des trois a avoir
un format de segments explicite, ce qui le rapproche du modele `.w65` — mais
le load file ProDOS ne contient pas de table de relocations.

Implemente dans `src/Targets/ProDosImage.cs`, d'apres le ProDOS 8 Technical
Reference (annexe B) :

- **Volume** : 280 blocs de 512 octets par defaut (143K). Deux blocs reserves,
  quatre blocs de repertoire, trente deux blocs de bitmap, puis les donnees.
  Le bitmap est alloue meme si personne ne le lit : sans lui ProDOS distribuerait
  son propre systeme de fichiers comme espace libre.
- **Repertoire** : liste chainee de blocs, chacun commencant par le bloc
  precedent puis le suivant. L'entete de volume est la **premiere entree du bloc
  2**, de $27 octets, comme les autres ; il y en a 13 par bloc.
- **Fichier** : un seul fork de donnees. Un fichier d'un bloc ou moins est un
  *seedling*, stocke de facon contigue ; au-dela c'est un *sapling*, avec un
  bloc d'index devant. Un *tree* (au-dela de 128K) est refuse plutot
  qu'ecrit a moitie.
- **Load file** : en-tete (type, version, version minimale, point d'entree sur
  deux octets), puis un record par segment (numero, longueur, adresse de
  charge, nom), puis des records de donnees de 512 octets au plus — un compte
  de zero veut dire « bloc plein ».
- **Entrelacement** : l'ordre `0, 8, 1, 9, 2, 10, …` se repetant tous les seize
  blocs. Il se lit « quel bloc logique est ici » : le bloc logique 0 est le
  premier ecrit, le bloc logique 8 le deuxieme. ProDOS numerote ses blocs en
  logique, et une image `.po` non entrelacee les stocke dans cet ordre-la ;
  l'entrelacement est donc une propriete de cet ecrivain, pas du systeme de
  fichiers.

Le format suit champ par champ, mais **rien ici n'a ete valide sur machine** :
les tests relisent l'image comme le ferait ProDOS (chaine du repertoire, clef,
index, bitmap) sans prouver qu'un emulateur ProDOS l'accepterait. C'est le meme
cas que T9 et T11.

### BBC BASIC

Les tokens sont differents de ceux d'Applesoft, mais le conteneur est un
**fichier texte brut**, sans secteurs entrelaces. C'est le cas le plus
simple de la famille, et il releve de T15 plutot que de T10.

BBC BASIC V est implemente dans `src/TextFormat/BbcBasicDialect.cs`, d'apres
les sources RISC OS Open (via la derivation de Matt Godbolt) :

- **Ligne** : `0D`, numero sur deux octets, longueur de tout l'enregistrement,
  puis les tokens. Longueur maximale 251. Le programme se termine par `0D FF` —
  d'ou la limite de 65279 pour un numero de ligne, puisque `$FF` dans l'octet
  haut marque la fin.
- **Tokens a partir de `$7F`**, et non de `$80`. Trois valeurs sont des
  echappements : `$C6`, `$C7` et `$C8` sont chacune suivies d'un second octet
  qui nomme le vrai token (fonctions, commandes, enonces etendus : `CASE`,
  `RENUMBER`, `SUM`...). `$8D` n'est pas un mot-cle : c'est une reference de
  ligne.
- **Chaines avec leurs guillemets** : pas de longueur devant, un guillemet
  ecrit deux fois n'en occupe qu'un.
- **Reference de ligne en trois octets** : les deux bits hauts de chaque octet du
  numero sont packs dans un premier octet, combine puis `EOR $54` ; les six
  bits restants de chaque octet sont stockes avec le bit 6 mis. Aucun octet
  d'une reference ne peut donc etre lu comme un token, ce qui permet a
  l'interpreteur de chercher `ELSE` sur une ligne sans tomber sur un `GOTO`.
  L'octet extended et la longueur viennent de la meme source, et les deux
  exemples de la derivation (139 et 204) sont des tests.

Le programme existe aussi **en texte** (`BbcBasicText.Render`) : la machine
tokenise un fichier texte au chargement, et c'est la seule forme ou un
commentaire et un mot-cle sont distingues par la personne et non par
l'interpreteur.

Comme pour ProDOS, **rien ici n'a ete execute sur un BBC** : la validation reste
structurelle (aller-retour octet pour octet, table confrontee a sa source).

---

## 5. Les quatre BASIC 6502

| BASIC | Perimetre | Conteneur | Etat |
|---|---|---|---|
| **Applesoft** | le plus repandu ; tache T10 | Apple II (DOS 3.2/3.3) | fait |
| **Waterloo Structured BASIC** | C64, VIC-20, PET ; le plus widespread apres Applesoft | BASIC V2 de Commodore | fait pour les machines Commodore |
| **GECOS** | cible distincte, decouverte avec GEOS | — | **hors perimetre**, voir plus bas |
| **BBC BASIC** | tokens differents, conteneur texte | texte brut | fait |

L'encodeur est generique : un `BasicDialect` porte la table de tokens, la facon
dont une chaine s'ecrit, la facon dont une reference de ligne s'ecrit, et la
mise en page des enregistrements (`Frame`, `TryReadRecord`). Applesoft, BBC BASIC
et Waterloo partagent le meme encodeur, ce que prouve le fait que l'ajout des
suivants n'a change aucun octet de la sortie Applesoft.

Le plan borne explicitement ce perimetre : **les quatre BASIC principaux**.
Les dialectes mineurs ou experimentaux n'en font pas partie, et une nouvelle
cible est un ajout explicite, pas une consequence.

### Waterloo Structured BASIC

Waterloo Structured BASIC n'est pas un BASIC : c'est un **cartouch** qui se
pose sur le BASIC que la machine a deja. Sa seule trace dans un programme
sauvegarde est la dozenaine de mots qu'il ajoute, ecrits apres le marqueur
`$FF $FF`. Un programme Waterloo est donc un programme du BASIC de l'hote avec
quelques mots dans un espace que l'hote laisse libre.

Sur les machines Commodore — PET, 8032, VIC-20, C64 — l'hote est **Commodore
BASIC V2**, dont le programme est deja la chaine d'enregistrements lies que ce
depot ecrit pour tous les BASIC herites de Microsoft. Le dialecte
(`src/TextFormat/WaterlooDialect.cs`) est donc la table de l'hote, de `$80` a
`$CB`, plus les douze mots du cartouch, de `$F3` a `$FE` : `IF` structure,
`CALL`, `LOOP`, `ENDLOOP`, `UNTIL`, `WHILE`, `ELSEIF`, `ELSE`, `ENDIF`, `PROC`,
`ENDPROC`, `QUIT`. `RENUMBER`, `DELETE` et `AUTO` ne sont pas tokenises : ils
n'existent qu'en mode direct.

Trois limites sont inscrites dans le code plutot que passees sous silence,
parce qu'une table de tokens ne peut pas les exprimer :

- Le cartouch **re-tokenise un mot selon sa position**. Un `IF` qui ouvre une
  ligne est celui du cartouch, `$F3`, et non le `$8B` de l'hote ; le choix
  depend de la position du mot, pas du mot. L'IF de l'hote est donc hors table :
  un mot ne peut avoir qu'un octet, et c'est celui d'un programme structure
  qui compte. Un fichier qui porte encore `$8B` est relu comme `\x8B`, ce que
  l'encodeur reecrit a l'identique — l'aller-retour reste exact.
- La version **Apple II** de Waterloo existe, mais aucune source consultee ne
  decrit sa mise en page de fichier ni sa table de tokens. Elle n'est donc pas
  implementee : inventer une table pour elle serait fabriquer un format.
- Rien ici n'a ete execute sur un Commodore. La validation est structurelle :
  la table est confrontee a sa source, la chaine de lignes est relue octet par
  octet, et l'aller-retour est exact.

### GECOS : pourquoi il n'y a rien a ecrire

GECOS est le systeme de General Electric pour ses machines — PDP-10 et
600/6000 — et non une cible 6502. Le plan le mentionne « decouvert avec GEOS »,
C'est-a-dire avec la confusion avec GEOS, le systeme d'exploitation de
Commodore qui n'a rien a voir avec lui. C'est meme la seule entree de la liste
des « BASIC 6502 » qui ne soit pas un BASIC 6502.

GECOS a bien un sous-systeme BASIC — un compilateur et un executeur algebrique,
documente dans le manuel de programmation GECOS III — mais un programme y est
un fichier de lignes numerotees accumulees dans le fichier `SY**` de
l'utilisateur, en texte, sans table de tokens. Il n'y a donc **aucun format de
programme tokenise** a ecrire, et l'encodeur de tokens de ce depot n'a rien a
y faire.

C'est une sortie de perimetre explicite et documentee, pas un oubli : la ligne
reste dans la liste des quatre BASIC du plan, avec la raison pour laquelle elle
est vide. La transformer en cible demanderait un autre systeme de fichiers que
celui de ce depot — GECOS est un systeme de lots ou l'on ecrit des
fichiers, pas une image de disque que l'on fabrique.

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
