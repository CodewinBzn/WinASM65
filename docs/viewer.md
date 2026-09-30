# WinASM65 — Le visualiseur de modules

Ce document specifie le visualiseur de modules : un outil **hors ligne**, qui
s'ouvre dans un navigateur, et qui montre ce qu'un `.w65` contient. C'est
l'equivalent du Dependency Viewer de Windows, et la tache T12 du plan.

**Implemente.** La commande `WinASM65 view -f <fichier> -o <sortie.html>`
produit un fichier HTML unique, sans ressource externe. Le test de la tache
est dans `WinASM65.Tests/ModuleViewerTests.cs`. Ce document a ete ecrit avant
l'implementation (T0) ; la section 8 dit ce qui a change et ce qui reste
ouvert.

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

## 4. Les entrees

L'outil accepte deux formes, parce qu'elles repondent a deux questions
differentes.

### 4.1 Un `.w65` ou un `.w65a`

Pour repondre a : *que contient ce module avant linkage ?* Segments non
places, exports, imports, relocations non resolues, provenance complete.
C'est la vue de developpement.

Sur une archive, l'outil ajoute ce que la vue d'un module seul ne peut pas
dire :

- l'**index de symboles**, construit avec le code du resolver, donc identique
  a ce que le linker verra ;
- pour chaque membre, si ses **imports sont satisfaits** et par qui — un import
  non satisfait est le cas critique du plan, mis en evidence ;
- pour chaque membre, si ses **exports sont cites** par un autre membre, et
  par qui — un export que personne ne cite est du code mort ;
- le diagnostic d'**export en double**, nomme, sans interrompre l'affichage.

Le test d'index duplique est parfois source d'erreurs : une vue qui ne
s'affiche pas ne sert a personne. La page est donc produite quand meme, avec
le conflit en evidence.

### 4.2 Un binaire deja lie

**Non implemente, et volontairement.** Un binaire lie ne porte ni symboles ni
table de relocations : les relocations ont ete appliquees, les tables
supprimees. Lire un `bin` ne permettrait d'afficher que des octets, ce que
`xxd` fait deja mieux.

La seule facon honnete d'afficher « ce que le linker a reellement produit »
serait que le linker ecrive a cote de l'image une carte de linkage
(segments poses, adresses finales des symboles). C'est un format de plus a
maintenir, avec un cout de synchronisation avec le linker, pour une
information que les tests de T7 et l'inspection de T8 donnent deja. Ce n'est
donc pas fait tant que personne n'en a besoin.

Ce que l'outil ne pretendra donc jamais : montrer un symbole dans une image
ou il n'en reste aucun.

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

---

## 8. Etat a la cloture de T12

Ce que la tache a livre :

- `WinASM65/src/Modules/ModuleViewer.cs`, la commande `view -f <fichier>
  -o <sortie.html>`, et l'aide en ligne ;
- deux entrees, `.w65` et `.w65a`, chacune avec ses sections ;
- l'analyse satisfaction / usage des symboles d'archive, faite avec le code du
  resolver ;
- `WinASM65.Tests/ModuleViewerTests.cs` : 9 tests.

Un piege rencontre en route, note parce qu'il se representera : le magic
d'archive est `W65A`, qui **commence par** le magic de module `W65`. Un test
dans le mauvais ordre fait passer une archive pour un module, et l'echec
apparait alors comme un segment corrompu dans un fichier abime. Le test
d'archive passe donc en premier, et il y a un test qui le verrouille.

Ce qui n'est pas fait, et pourquoi :

- **La vue d'un binaire lie** (section 4.2) : elle demanderait une carte de
  linkage que le linker n'ecrit pas encore. Ce n'est pas un oubli, c'est un
  format de plus sans consommateur.
- **L'execution du code 6502** : hors de portee, et sans objet ici.
- **Ce que le navigateur fait du HTML** : les tests portent sur le HTML
  produit, pas sur un rendu. Une page peut etre bien formee et s'afficher mal.
