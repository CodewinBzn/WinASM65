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

## Ce que ces tests ne prouvent pas

Les tests vérifient la structure du disque champ par champ. Ils ne vérifient
pas que GEOS affiche l'icône, ni que le kernal charge l'application : cela
demande C64 ou VICE. Un disque bien formé ne suffit pas à dire qu'il démarre.

## Ligne de commande

```
WinASM65 geos module.w65... -o disk.d64 [-name N] [-disk N] [-id NN]
                            [-author A] [-description D] [-shift n]
```

Les segments liés deviennent les records de l'application, dans l'ordre de
placement, donc le record 0 est celui que GEOS charge en premier.
