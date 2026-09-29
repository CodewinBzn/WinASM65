# WinASM65 — Le visualiseur de modules

Ce document specifie le visualiseur de modules : un outil **hors ligne**, qui
s'ouvre dans un navigateur, et qui montre ce qu'un `.w65` contient. C'est
l'equivalent du Dependency Viewer de Windows, et la tache T12 du plan.

**Non implemente.** Ce document fixe le cahier des charges.

---

## 1. A quoi cela sert

La resolution par nom a un defaut structurel : elle peut rester **silencieuse
jusqu'a l'execution**. Un import non resolu ne provoque pas forcement une
erreur a la compilation ; il produit une image qui plante a l'execution, ou
une routineappelee a zero, ou pire, une image qui « marche presque ».

Sur un systeme sans debugger — GEOS dans VICE, une ROM dans un emulateur NES —
ce diagnostic tardif est un vrai cout. Le visualiseur le rend **visible avant
que l'image ne parte sur la cible**.

Il sert aussi a repondre a la question que personne ne repond en relisant un
binaire : *qu'est-ce qui a ete lie ici, et depuis ou ?*

---

## 2. Contraintes

| Contrainte | Raison |
|---|---|
| **Hors ligne** | aucun envoi de donnees, aucune dependance reseau |
| **Lecture seule** | l'outil n'ecrit jamais un `.w65` ni un binaire |
| **Un seul fichier HTML** | ouvrable par double-clic, sans serveur |
| **Aucun build** | pas de chaine d'outils a installer |

Le mode « un seul fichier HTML » est ce qui rend l'outil utilisable dans le
contexte du plan : on inspecte un module sur une machine de developpement
sans installer quoi que ce soit, ni installer quoi que ce soit sur la
machine qui possede la copie de sauvegarde du projet.

---

## 3. Ce que l'outil affiche

### 3.1 Segments

Pour chaque segment : nom, taille, type (`ro` / `rw` / `bss`), banque,
alignement, et **adresse de placement** si le fichier est un binaire deja
lie.

Un segment `bss` n'a pas de contenu : il affiche sa taille reservee, et
signale qu'il n'occupe pas de place dans le fichier. Le distinguer
visuellement evite de chercher des octets qui n'existent pas.

### 3.2 Symboles exportes et importes

Les exports : nom, segment, offset. Les imports : nom, module attendu.

La vue doit faire resortir visuellement :

- un export **utilise** par un autre module, avec le module qui l'utilise ;
- un export **jamais utilise** — code mort, ou symbole prevu pour un usage
  futur ;
- un import **satisfait** et par qui ;
- un import **non satisfait** — le cas critique, mis en evidence.

### 3.3 Sites relocalisables

Le coeur de l'outil. Pour chaque site : segment, offset, largeur, type
(`abs8`, `abs16`, `zp8`, `rel8`, `seg`), symbole vise, et **le fichier source
et la ligne** d'ou il vient.

La provenance est ce qui distingue cet outil d'un simple « hex viewer »
structure. C'est elle qui permet de repondre a « pourquoi y a-t-il une
relocation ici ? » sans ouvrir l'editeur et chercher au hasard.

### 3.4 Diagnostic des conflits

| Cas | Affichage |
|---|---|
| export en double | les modules en conflit, nommes, avec le symbole |
| import non satisfait | le symbole, le module importeur, le module attendu |
| cycle d'archives | le cycle nomme, dans l'ordre |

Ce sont les trois diagnostics que le plan exige. Un visualiseur qui ne les
produit pas n'a pas d'interet propre : ils sont deja produits a la
compilation, avec moins de contexte.

---

## 4. Les deux entrees

L'outil accepte deux formes, parce qu'elles repondent a deux questions
differentes.

### 4.1 Un `.w65`

Pour repondre a : *que contient ce module avant linkage ?* Segments non
places, exports, imports, relocations non resolues, provenance complete.
C'est la vue de developpement.

### 4.2 Un binaire deja lie

Pour repondre a : *qu'est-ce qui a ete reellement produit ?* Les relocations
sont alors appliquees, donc les sites ne sont plus relocalisables. L'outil
peut montrer le resultat de l'operation — par exemple la difference entre
l'adresse assemblee et l'adresse finale d'un site.

C'est ce qui permet de verifier le resultat d'un linker sans le debuguer sur
la cible.

---

## 5. Contraintes de conception issues du format

Trois proprietes du format imposent des choix d'interface, et elles ne sont
pas évidentes.

**Le binaire peut contenir plusieurs segments non contigus.** L'affichage ne
doit donc pas supposer qu'un fichier est un espace d'adresses continu. Un
`bss` suivi d'un `ro` n'est pas un bloc.

**Les segments sont nommes, pas adresses.** Deux segments peuvent porter le
meme nom dans deux modules distincts. L'identite d'un export est donc le
couple (module, nom), jamais le nom seul. Une vue qui montre
`DrawTile` sans dire d'ou il vient est **faux**, pas seulement incomplet.

**Le placement n'est pas connu a l'assemblage.** Un `.w65` ne dit pas ou ses
segments seront poses. L'outil doit rendre cela explicite plutot que de
laisser croire a une adresse finale qui n'existe pas encore.

---

## 6. Ce que l'outil ne fait pas

- Il ne **lie pas**. Le linkage est la tache du linker, avec ses diagnostics
  et ses codes de sortie.
- Il ne **corrige rien**. Lecture seule : un fichier ouvert dans le
  visualiseur ne peut pas etre modifie, pour eviter qu'une correction
  manuelle produise un `.w65` invalide.
- Il n'**execute rien**. Il n'y a pas de 6502 embarque : il montre des
  donnees, il ne les fait pas tourner.

---

## 7. Position dans le plan

Le visualiseur est en **vague 4**, apres le linker (T4). Il depend du
format `.w65` (T3) et du linker, pas l'inverse — ce qui le rend
**parallement realisable** avec les archives (T5) et le stub runtime (T11).

Sa validation, selon le plan : **un module reel correspond a ce que le linker
a produit.** Autrement dit, ce que l'outil affiche sur un binaire lie doit
etre coherent avec la sortie effective du linker, et non avec une
representation parallele qui pourrait diverger.

C'est la meme discipline que pour le stub de T11 : un outil dont la
validite tient a sa coherence avec un autre composant doit etre verifie
contre ce composant, pas contre lui-meme.
