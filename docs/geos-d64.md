# GEOS : application segmentée sur D64

Ce document est normatif pour `WinASM65/src/Targets/D64Image.cs`. Les
offsets viennent du GEOS Programmer's Reference Guide, pas de déductions.

## Entrée de répertoire (32 octets)

| Offset | Taille | Champ |
|---|---|---|
| $00-$01 | 2 | piste/secteur du bloc d'entrées suivant, $00 pour le dernier |
| $02 | 1 | type de fichier C64 ; les trois bits bas doivent faire 0, 1 ou 2 |
| $03-$04 | 2 | piste/secteur du fichier, ou du secteur RECORD si le type GEOS n'est pas $00 |
| $05-$14 | 16 | nom, ASCII, paddé à $A0 |
| $15-$16 | 2 | piste/secteur du bloc INFO |
| $17 | 1 | structure GEOS : $00 séquentiel, $01 VLIR |
| $18 | 1 | type GEOS, $06 pour une application |
| $19-$1D | 5 | horodatage : année = valeur + 1900, mois, jour, heure, minute |
| $1E-$1F | 2 | taille en secteurs, octet bas d'abord |

Les trois bits bas du type C64 decides : 3 et au-delà sont des fichiers REL,
que GEOS n'accepte pas, donc l'écrivain refuse. Un REL écrit produirait une
entrée que GEOS refuse de reconnaître.

## Bloc INFO (un secteur)

| Offset | Taille | Champ |
|---|---|---|
| $00-$01 | 2 | $00 / $FF : un seul secteur, rien ne suit |
| $02-$04 | 3 | largeur d'icône $03, hauteur $15, puis $BF |
| $05-$43 | 63 | bitmap de l'icône, format sprite |
| $44 | 1 | type C64, comme dans l'entrée |
| $45 | 1 | type GEOS, comme dans l'entrée |
| $46 | 1 | structure, comme dans l'entrée |
| $47-$48 | 2 | adresse de chargement |
| $49-$4A | 2 | adresse de fin |
| $4B-$4C | 2 | adresse de démarrage |
| $4D-$60 | 20 | texte de classe, terminé par $00 |
| $61-$74 | 20 | auteur, terminé par $00 |
| $75-$88 | 20 | nom de l'application ayant créé un document |
| $89-$9F | 23 | libre pour les applications |
| $A0-$FF | 96 | description, terminée par $00 |

La hauteur d'icône est fixée à $15 parce que c'est elle qui donne les 63
octets de bitmap, la seule taille qui tienne dans le secteur. Une icône plus
grande est tronquée à 63 octets : l'écrire en entier déborderait sur le type
C64 à $44 et le fichier semblerait valide.

## Secteur RECORD (VLIR)

$00-$01 vaut $00 / $FF, puis des paires piste/secteur, une par record.
$00/$00 termine la liste, $00/$FF marque un record absent. Au plus 127
records, donc 127 paires.

## Secteur BAM, piste 18 secteur 0

| Offset | Taille | Champ |
|---|---|---|
| $00-$01 | 2 | piste/secteur du premier secteur de répertoire |
| $02 | 1 | $41, la 1541 |
| $03 | 1 | $2A, la version du DOS |
| $04-$8F | 140 | 35 pistes de 4 octets, à partir de la piste 1 |
| $90-$9F | 16 | nom du disque |
| $A0-$A1 | 2 | $A0 $A0 |
| $A2-$A3 | 2 | identifiant du disque |
| $A4 | 1 | $A0 |
| $A5-$A6 | 2 | "2A" |
| $A7-$AA | 4 | $A0 |
| $AB-$AC | 2 | piste/secteur du secteur bordure |
| $AD-$BC | 16 | signature GEOS, "GEOS format V1.0" |
| $BD-$FF | 67 | inutilisé |

Un groupe de quatre octets de BAM est un compte de secteurs libres sur ses
sept bits bas, puis les bitmaps de liberté des secteurs 0-7, 8-15 et 16-23.
Les bits 5 et 6 du quatrième octet portent les bits 7 et 8 du compte, ce qui
permet à une piste de dépasser 127 secteurs libres. Compte et bitmap doivent
concorder : s'ils mentent, DOS croit qu'il reste de la place et écrase un
fichier.

La signature est à `$AD` et le secteur bordure à `$AB-$AC`. GEOS décide
lui-même si un disque est un disque GEOS en cherchant `GEOS format` à `$AD`,
donc cette chaîne doit être exactement cela.

## Allocation

Seul le secteur 18/0 est réservé d'avance. Le reste de la piste 18 est laissé
libère à dessein, parce que le répertoire doit s'y trouver : le réserver en
entier ne laisserait nulle part où le mettre et il atterrirait sur une piste
de données, où DOS ne le trouverait pas.

La recherche du secteur suivant avance de l'entrelacement, 10 par défaut, puis
passe à la piste suivante. L'entrelacement ne change que la vitesse de
lecture, jamais la validité de l'image : le lecteur suit la chaîne telle
qu'elle est écrite.

Une chaîne de secteurs porte 254 octets utiles, les deux premiers gardant le
lien vers le suivant. Le dernier secteur d'une chaîne porte $00 puis le
nombre d'octets utilisés : c'est ce qui dit au lecteur où le fichier s'arrête,
et sans cela il lirait 254 octets de trop.

## Table de relocation

Le kernal GEOS ne corrige rien au chargement. `LOAD` ($C208) prend l'adresse de
chargement dans le bloc INFO ; `LOAD2` ($C211) accepte à la place l'adresse que
l'appelant fournit en $886C-$886D. Dans les deux cas les octets arrivent tels
quels. Une application qui n'est pas chargée à l'adresse pour laquelle elle a
été assemblée doit donc se corriger elle-même, et le travail de WinASM65
consiste à lui fournir la liste des endroits à corriger.

Le premier record est le programme entier, et il commence par le stub qui le
deplace, puis la table, puis le code :

```
record 0 :  [ stub ][ table ][ code ... ]
adresse de chargement : ^
```

Le kernal saute a l'adresse de chargement, qui est donc le stub ; le stub
corrige le code puis saute au vrai point d'entree. Tout ce dont l'application a
besoin est ainsi dans le seul record que le kernal charge, et une application
deplacee n'a besoin d'aucun installeur.

La table a l'interieur du record est :

```
$00-$03  "R65A", pour que la zone ne se confonde pas avec du remplissage
$04-$05  l'adresse pour laquelle l'image a ete assemblee
$06-$07  le nombre d'entrees
$08-$09  le point d'entree du programme
$0A-     entrees : adresse basse, adresse haute, largeur
```

Le bloc INFO resume la table dans sa zone libre, $89-$9F, qui appartient a
l'application :

```
$89-$8C  "R65A"
$8D-$8E  adresse de base
$8F-$90  adresse de la table
$91-$92  nombre d'entrees
```

Le resume est une description de la table, pas la table : ce que le stub lit est
la table elle-meme, celle du record. Le magic est ce qui rend l'extension
inoffensive : un kernal qui ignore cette zone charge le fichier exactement comme
avant, et le stub, lui, fait ce que le kernal ne fait pas.

Le stub (`src/Targets/geos-stub.asm`) ne contient aucune adresse. Il lit
l'adresse de chargement que le kernal laisse en $886C-$886D, et atteint la table
et le code par cette adresse plus un decalage fixe, ce qui revient a dire qu'il
se deplace avec le programme. Il n'utilise que des branches relatives : un
`JMP` vers une de ses propres etiquettes, ou une branche absolue, le fixerait a
la seule adresse pour laquelle il a ete assemble. Il utilise $70-$7F, la page
zero reservee a l'application sous GEOS, et il rend la main au programme avant
que celui-ci ne demarre.

Seules les references absolues figurent dans la table. Une branche relative est
deja correcte ou que le code bouge ou non, la page zero est la page zero a toute
adresse de chargement, et une valeur immediate n'est pas une adresse.

**`<` et `>` ne se valent pas.** Les deux designent une adresse, mais un seul est
deplacable par cette table :

- **bas** (`lda #<Table`) : l'entree le decrit, avec une largeur de 1. Ajouter le
  biais a un octet bas est une addition modulo 256, et l'addition commute avec la
  reduction : `(v + b) & $FF == (v & $FF) + b`. Le resultat est exact, donc la
  table le decrit et `Apply` le corrige.

- **haut** (`lda #>Table`) : `Build` **refuse**, en nommant le site. Le haut de
  `v + b` depend de la retenue sortie du bas, et la table ne transporte ni la
  valeur ni son bas — elle ne peut donc pas connaitre cette retenue. Une entree
  qui pretendait le decrire deplacerait le programme a une adresse decalee d'une
  page, sans qu'aucune trace ne le dise.

Le refus est dans `Build` et dans `Apply`, pas dans le linker : un programme qui
ne bougera jamais est parfaitement correct avec un `lda #>Table`, et le linker ne
sait pas si l'image bougera. C'est la cible qui le sait, donc c'est elle qui
refuse.

La parade reste la meme qu'avant : poser la table en RAM par une reference de deux
octets — c'est ce que fait le stub runtime de T11 — et ne garder que des references
16 bits dans le code relocalisable. Avec le refus en place, cet oubli devient un
diagnostic nomme au lieu d'un defaut silencieux.

**Pas de stub auto-positionne.** Un 6502 ne peut pas lire son propre compteur de
programme sans un `JSR` dont la cible est une adresse d'execution, et une
constante d'assemblage n'en est pas une : le `JSR` pousserait la bonne adresse
mais sauterait ailleurs. Un stub ecrit ainsi fonctionnerait a une seule adresse
de chargement et corromprait la memoire a toutes les autres. La table est donc
consommee par l'application, par un chargeur, ou par WinASM65 lui-meme.

Le test central n'est pas la structure de la table mais son effet : une image
chargee ailleurs puis corrigee par la table doit etre identique, octet pour
octet, a l'image que le linker produit en liant a cette adresse. C'est la
propriete qui compte, et une table qui laisserait un site de cote la
manquerait.

## Ce que ces tests ne prouvent pas

Les tests vérifient la structure du disque champ par champ, et l'équivalence
entre la table appliquée et un lien à l'adresse de chargement. Ils ne vérifient
pas que GEOS affiche l'icône, ni que le kernal charge l'application, ni que
l'application se corrige elle-même au démarrage : cela demande C64 ou VICE. Un
disque bien formé et une table juste ne suffisent pas à dire qu'elle démarre.

## Ligne de commande

```
WinASM65 geos module.w65... -o disk.d64 [-name N] [-disk N] [-id NN]
                            [-author A] [-description D] [-shift n] [-start n]
```

Les segments liés deviennent les records de l'application, dans l'ordre de
placement, donc le record 0 est celui que GEOS charge en premier. Le dernier
record est la table de relocation. `-start` donne le point d'entrée du
programme, qui est sinon le début du premier segment.
