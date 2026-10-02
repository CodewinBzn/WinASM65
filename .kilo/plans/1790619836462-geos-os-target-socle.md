# WinASM65 — Système de modules façon DLL, en préservant le brûlage direct

## Objectif

Permettre des routines partagées réutilisées à plusieurs adresses, sur le
modèle des DLL Windows, et des exécutables pour systèmes d'exploitation 6502
— **sans casser** le mode de brûlage direct existant.

## Décisions actées

1. **Deux modes coexistent.** Le mode actuel n'est ni remplacé ni modifié dans
   son comportement observable.
2. **La burntabilité directe est une contrainte dure.** Chaque fichier
   s'assemble seul et produit un blob déjà placé à son adresse réelle,
   gravable sans linkeur. Confirmé par `example_bomberman-nes/config.json` où
   chaque entrée a son propre `.org` (BMAN_BANK1 = $C000, vectors = $FFFA) et
   où l'ordre de `Output.Files` ne fait que confirmer le placement.
3. **Format d'objet propriétaire, inspiré DLL Windows — PAS du format O65.**
   Décision utilisateur : ne pas se rapprocher de ca65/ld65.
4. **Le linker est au build**, sauf là où le système sait relocaliser (voir 5).
5. **GEOS : produire la table de relocation que le kernal sait consommer**, de
   sorte que le chargement dynamique soit réel, comme `IMAGE_BASED_RELOCATIONS`
   de Windows. Décision utilisateur (option A).
6. **DOS 3.2/3.3 et ProDOS : cibles d'une troisième nature.** Ils n'exécutent
   pas de binaire. Une routine partagée y est chargée dynamiquement depuis un
   fichier texte, via un stub runtime **fourni par WinASM65** (décision
   utilisateur).
7. **Validation en deux temps : NES puis GEOS.**
8. **Documentation préalable**, dans `docs/`, plus un visualiseur HTML.
9. **La cible est configurable**, y compris en mémoire multi-régions.
10. **Dépendances d'archives transitives**, comme une DLL qui en charge une autre.

## Les trois familles de cibles

Point structurant issu de l'inventaire vérifié. Confondre ces familles produit
un plan faux.

| Famille | Cibles | Ce qu'est le fichier | Relocation par l'OS |
|---|---|---|---|
| **Exécutable plat** | NES, Commodore (PRG), Apple II (A2BIN), BBC (binaire) | binaire 6502 | non — mémoire en ROM |
| **Exécutable segmenté** | GEOS | records VLIR + image D64 | **oui** — le kernal relocalise |
| **Fichier texte** | DOS 3.2/3.3, ProDOS, Waterloo BASIC, GECOS, BBC BASIC | **texte tokenisé** | non au chargement ; stub runtime si DLL |

**Conséquence majeure :** DOS 3.2/3.3 et ProDOS ne sont pas des cibles
d'exécutable. Un binaire produit pour ces systèmes ne sera pas exécuté par
 Applesoft. Produire un « exécutable DOS 3.3 » suppose d'écrire un
**encodeur de tokens** (table des 256 tokens, mode binaire `$96`, adressage
`$xxxx` ou zéro-page, formes abrégées), ce qui ne dépend **ni des relocations,
ni du linker, ni du format d'objet**. Tâche distincte, après le socle.

## Mécanisme de partage pour les cibles texte

Puisque ces systèmes n'exécutent pas de binaire, le partage « façon DLL » y
fonctionne ainsi :

1. Le linker produit la routine comme un bloc de données.
2. Un **stub runtime en assembleur 6502, fourni par WinASM65**, est injecté
   dans le fichier texte.
3. À l'exécution, le stub décompresse le bloc en RAM, applique les
   relocations sur les sites qu'il a réservés, puis le code appelant fait
   `JSR` vers l'adresse obtenue.
4. Aucune information d'adresse absolue n'est figée dans le fichier texte :
   le stub est le seul à savoir où il a posé le code.

Le stub doit donc connaître, au moment de l'injection, la liste des sites à
relocaliser et leur largeur. C'est le même besoin que la table de relocations du
linker, présenté sous une forme exécutable.

## Conséquences du choix « DLL » plutôt que O65

| Concept DLL Windows | Équivalent WinASM65 |
|---|---|
| `__declspec(dllexport)` | directive d'export dans le source |
| `dllimport` | directive d'import, résolu par nom |
| Table d'exports / d'imports | idem dans le `.w65` |
| Image base, rebasing | adresse de charge + relocations |
| `LoadLibrary` | stub runtime (cibles texte) ou kernal GEOS |
| Dependency Walker | visualiseur HTML + erreur d'import non satisfait |

**Décision structurelle :** une DLL expose des **noms**, pas des adresses.
Le linker résout des **noms de symboles** entre modules. Pour chaque site
relocalisé, il conserve le **fichier et la ligne source**, afin de produire un
diagnostic qui nomme le symbole, le module qui l'importe et le module
exporteur attendu.

## Contraintes techniques établies par lecture du code

| Fichier | Contrainte |
|---|---|
| `src/Core/AssemblerEngine.cs:483` | `val = exprRes.Value.AsInteger` puis `EmitWord((ushort)val)`. L'adresse est aplatie en octets : aucune trace du site. **Bloquant pour toute relocation.** |
| `src/Expressions/ExpressionResult.cs:14` | `IsResolved` ne renvoie qu'un booléen. Impossible de distinguer `lda #$05` d'une référence à un symbole. **Bloquant pour T1.** |
| `src/Output/BinaryEmitter.cs:28` | Un seul `List<byte> _buffer`, un seul `CurrentAddress`/`OriginAddress`. Aucun concept de segment. |
| `src/Segments/MultiSegmentOrchestrator.cs` | Ne gère que les dépendances symboliques via `.symb`/`.Unsolved`/`.UnsolvedExpr` échangés sur disque. Aucun symbole exporté/importé formellement. |
| `src/Targets/O65Format.cs` | Fichier plat à adresse fixe : ni segments nommés, ni relocations, ni table d'exports. Ni un objet O65 ni une DLL. |

## Non-régression (contrainte contractuelle)

Le mode direct doit produire des octets **identiques** à l'existant. Test de
golden sur chaque format (`bin`, `prg`, `a2bin`, `xex`, `ines`, `rom`, `ihex`,
`srec`) avec un jeu de fixtures figé, comparé avant/après chaque tâche. Les 34
tests existants passent inchangés.

## Séquence

### Tâche P0 — Portail : validation de plage des opérandes

Tâche préalable, numérotée hors série `T` car elle est un **portail** et non un
membre de la séquence. Doit être terminée et validée **avant T0, T1 et T2**.

**Pourquoi un portail.** L'assembleur tronque aujourd'hui silencieusement toute
valeur d'opérande hors plage : `LDA #300` produit `A9 2C`, sans diagnostic et
sans échec du build. Cinq sites confirmés :

| Fichier | Ligne | Code |
|---|---|---|
| `src/Core/Value.cs` | 57-60 | `ToByte()` → `(byte)(AsInteger & 0xFF)` |
| `src/Core/Value.cs` | 62-65 | `ToUInt16()` → `(ushort)(AsInteger & 0xFFFF)` |
| `src/Core/AssemblerEngine.cs` | 519 | `EmitByte((byte)val)` si `info.Length == 2` |
| `src/Core/AssemblerEngine.cs` | 524 | `EmitWord((ushort)val)` |
| `src/Core/AssemblerEngine.cs` | 633, 637 | patch de **seconde passe** via `ToUInt16()`/`ToByte()` |

Le chemin zéro-page est déjà correct et ne doit pas être touché :
`TryOptimizeZeroPage` (`src/Cpu/Cpu6502.cs:252`) refuse déjà toute valeur hors de `0..255`.

**Lien direct avec le risque n°1 du chantier.** Le tableau des risques indique
que modifier l'émetteur sans l'info de type produit des relocations fausses
*silencieusement*. La troncature est la même famille de défaut : une valeur
aberrante est masquée au lieu d'être signalée. T2 enregistre des sites de
relocation de largeur 1 et 2 octets ; la règle de plage doit être définie **une
seule fois**, avant que ces sites n'existent. La corriger après T2 reviendrait à
faire corriger chaque site de relocation par l'agent qui l'écrit, sans garantie
d'homogénéité.

**Travail :**

1. `ErrorCodes` (`src/Core/Diagnostic.cs`) : ajouter
   `VALUE_OUT_OF_RANGE_BYTE` et `VALUE_OUT_OF_RANGE_WORD`, messages incluant la
   valeur fautive et la plage acceptable.
2. `Value` (`src/Core/Value.cs`) : ajouter `InByteRange(long)` et
   `InWordRange(long)`. **Ne pas modifier** `ToByte()`/`ToUInt16()`, qui sont des
   conversions par masque et peuvent avoir d'autres appelants.
3. Première passe (`AssemblerEngine.cs` 503-527) : valider avant émission.
   En cas de dépassement, `ReportError` puis émettre l'opcode et des octets de
   remplacement à zéro, comme la branche « unresolved » (lignes 533-545), pour
   garder adresses et listing cohérents. L'échec du build vient de
   `IDiagnosticReporter.HasErrors`, pas de l'émission.
4. Seconde passe (`PatchResolvedExpression`, 612-640) : **indispensable et non
   optionnelle.** Un opérande hors plage référencé par un symbole non résolu à la
   première passe ne passe jamais par le point 3 ; sans ce correctif le bug reste
   accessible via `LDA LABEL_EN_AVANCE`.
5. Auditer les autres appelants de `ToByte()`/`ToUInt16()`. L'inventaire ci-dessus
   est complet pour `Value.cs` et `AssemblerEngine.cs` mais **n'a pas pu être
   vérifié sur le reste de l'arbre** lors de la planification : `rg`/`grep` étaient
   indisponibles et `Select-String` a été refusé par les permissions. Commande
   utilisable : `Select-String -Path "WinASM65\src\*\*\*.cs" -Pattern "ToByte" -List`
   (sans pipe, les pipes PowerShell sont bloqués).

**Plages retenues** — signées à gauche pour que `LDA #-1` continue de produire
`0xFF` et `JMP $-1` de produire `0xFFFF`, comportement standard des assembleurs 6502 :

- 1 octet : `-128 .. 255`, puis émission par `(byte)(v & 0xFF)`
- 2 octets : `-32768 .. 65535`, puis émission par `(ushort)(v & 0xFFFF)`

**Tests** (nouveau `WinASM65.Tests/OperandRangeTests.cs`) : `InByteRange` /
`InWordRange` aux bornes et juste hors bornes ; `LDA #300` → erreur et aucun
octet de code ; `LDA #-1` → `A9 FF` sans diagnostic ; `LDA #255` → `A9 FF` ;
`JMP $12345` → erreur ; symbole en avant hors plage → erreur *(couvre la seconde
passe)* ; `LDA $10` → `A5 10` et non `AD 00 10` *(verrouille la non-régression
zéro-page)*.

**Portée de la non-régression.** Les fixtures du golden test octet-pour-octet
sont du code déjà valide, donc hors plage de ce changement : elles doivent rester
bit pour bit identiques. Seules les sources contenant une valeur hors plage
cesseront de s'assembler — c'est le comportement voulu, à signaler dans le README.

**Documenté dans T0.** `docs/pipeline.md` doit décrire P0 comme le point de
départ réel : la troncature y est décrite comme un défaut, pas comme un
comportement.

### Tâche 0 — Documentation
- `docs/pipeline.md` : classification des lignes (`ParseLine` et l'ordre des
  motifs), décodage CPU (13 opcodes par mnémonique), évaluation d'expression,
  émission, passe 2, dépendances symboliques, absence de segments et de
  relocations.
- `docs/format-module.md` : spécification du `.w65` (T3).
- `docs/targets.md` : les trois familles, catalogue des cibles, mémoire
  multi-régions.
- `docs/targets-text.md` : le mode fichier texte, l'encodeur de tokens, le
  stub runtime.
- `docs/viewer.md` : le visualiseur (T12).
Écrite avant T1 : ces documents décrivent le point de départ.

### Tâche 1 — Le type d'expression atteint l'émetteur
`ExpressionResult` expose les symboles *utilisés*. `Value` et
`ExpressionEvaluator` propagent l'information jusqu'à `HandleInstruction`,
**avec fichier et ligne source**. Test prouvant que `lda #$05` et `lda label`
sont distingués.

### Tâche 2 — Table de relocations dans l'émetteur
`BinaryEmitter` enregistre chaque site `(position, largeur, symbole, segment,
fichier, ligne)`. Exposée via `AssemblyResult`.
T1 et T2 sont indissociables : ne pas les séparer en deux PR.

### Tâche 3 — Format d'objet module (le « .w65 »)
En-tête, segments nommés, table d'exports (nom → segment/offset), table
d'imports (nom + module), table de relocations, provenance source par site.
Directives source d'export et d'import.
Décider ici le sort de `O65Format.cs` : le retirer, ou le conserver sous un nom
de format plat honnête, puisqu'il n'est ni un objet O65 ni une DLL.

### Tâche 4 — Linker
`WinASM65 link` : résout les imports par nom contre les exports, diagnostique
les imports non satisfaits en nommant symbole, module importeur et module
exporteur attendu, place les segments, applique les relocations, émet une image
plate. **Sa sortie est un binaire gravable** : il produit le mode A, il ne le
remplace pas.

### Tâche 5 — Bibliothèque et dépendances transitives
Archive de modules avec index de symboles. Une archive peut en référencer une
autre ; résolution transitive. Détecter et signaler les cycles et les symbols
exportés en double.

### Tâche 6 — Mémoire multi-régions configurable
Extension de `TargetConf` : régions nommées (adresse, taille, banque, type
`ro`/`rw`/`bss`), placement croisé `load`/`run`, validation croisée des `.org`.

### Tâche 7 — Validation linker sur NES
Reprise de `example_bomberman-nes` : extraire une routine partagée, lier à
deux adresses distinctes, vérifier l'exécution dans un émulateur NES.

### Tâche 8 — GEOS : format d'application segmenté
Records VLIR, entrée de répertoire D64 étendue (octets $15-$1D : type de
fichier, structure SEQ/VLIR, horodatage), bloc INFO d'un secteur (icône 63
octets en format sprite, adresses load/end/start, auteur, description), image
D64 complète (BAM, allocation de secteurs, interleaving, signature « GEOS
format » à l'offset $AD du secteur BAM, secteur bordure à $AB-$AC).
Inclut la tâche 6 pour les banques 0-15.

### Tâche 9 — Relocation par le kernal GEOS
Table de relocation au format que le kernal GEOS 2.0 consomme : en-tête
d'application portant l'adresse de base et l'offset de la table, et entrées de
relocation correspondantes. Le kernal relocalise au chargement, comme
`IMAGE_BASED_RELOCATIONS` de Windows.
Source de référence : le code du kernal reverse-ingénieré, publié sur GitHub
(mist64/geos), qui fournit les structures exactes.
**Distincte de la tâche 8** : T8 produit le conteneur, T9 produit les données
que le noyau consomme. Validation : l'application se charge dans VICE depuis un
fichier non pré-positionné et s'exécute correctement.

### Tâche 10 — Cibles texte : encodeur de tokens
Encodeur Applesoft (table des 256 tokens, mode binaire `$96`, adressage
`$xxxx` et zéro-page, formes abrégées), puis conteneur ProDOS (bloc de 512
octets, entrelacement par paires de secteurs, points d'entrée, segments).
Test de round-trip vers un interpréteur Applesoft.
Ne dépend ni de T1-T3 ni du linker. Prend du temps : le budget de tokens est
la partie la plus longue de la tâche.

### Tâche 11 — Cibles texte : stub runtime et partage façon DLL
Stub en assembleur 6502 **fourni par le dépôt** : décompression du bloc en
RAM, application des relocations, puis appel. Injecté par le linker dans le
fichier texte, avec la liste des sites à corriger.
Codé pour s'exécuter sur la machine cible : validation par exécution, pas par
golden test.

### Tâche 12 — Visualiseur de modules (HTML)
Outil hors ligne dans le navigateur : charge un `.w65` ou un binaire lié,
affiche segments, symboles exportés/importés, sites relocalisables et leur
source. Équivalent du Dependency Viewer de Windows. Lecture seule.

### Tâche 13 — BBC Micro : machine principale et second processeur
Format binaire BBC : fichier à en-tête binaire (`&FF` + longueur 16 bits) ou à
octet de type + 2 octets d'adresse d'exécution, plus le format binaire brut
`OSLOAD`.
Inclut le **second processeur 6502 (Tube)**, qui n'est pas la même cible :
modèle mémoire distinct (RAM et ROM séparées, $0000-$7FFF accessible en
toute logique, espace d'adressage propre), table de symboles matérielle
différente, et mode de chargement propre. Cible distincte dans
`SystemCatalog`, avec sa propre configuration de mémoire.
Les deux cibles profitent de T4 (linker) sans travail supplémentaire.

### Tâche 14 — Variantes matérielles des cibles existantes
Compléter le catalogue sans le multiplier déraisonnablement : ce qui varie est
modélisé comme des **options de cible**, pas comme des systèmes distincts.
- Régions TV : PAL / NTSC, avec leurs fréquences de raster et leurs adresses
  de table de sprites.
- C64 : SID (6581) vs RDFI (8580), présent sur C64C, qui change les espaces
  mémoire visibles.
- C128 : VIC-II en mode C64, VDC 40/80 colonnes, PEKKA en mode natif.
- Modèles régionaux : cibles `pet` (PET 2001 / 2001-N / CBM-II) et
  `plus4` (TED, au lieu de VIC).

Le catalogue expose des cibles nommées pour les combinaisons courantes
(`c64`, `c64c`, `c128-vdc`, `vic20-pal`, `vic20-ntsc`) qui ne font que
présélectionner ces options. Pas de duplication de code.

### Tâche 15 — BASIC 6502 restants
Même famille que T10 : fichiers texte tokenisés, même encodeur de tokens
générique, conteneurs différents.
- **Waterloo Structured BASIC** (Apple II, C64, PET) : le plus répandu après
  Applesoft, et le plus proche d'un BASIC « de Production ».
- **GECOS** (découvert avec GEOS, cible séparée).
- **Applesoft / DOS 3.2 vs 3.3** : un même encodeur, deux conteneurs (la
  différence 3.2/3.3 tient surtout au catalogue de fichiers et à l'ordre
  d'allocation des secteurs, déjà couvert par T10).
- **BBC BASIC** : tokens différents d'Applesoft, mais le conteneur est un
  fichier texte brut, pas un fichier à secteurs entrelacés — plus simple que
  T10.

## Hors périmètre

- Loader dynamique pour NES/BBC : impossible en ROM, écarté par le matériel.
- Format O65 conforme à cc65 : remplacé par le format propriétaire.
- Exécutables DOS 3.3 / ProDOS au sens « fichier binaire » : ces systèmes
  n'en ont pas. Écarté par construction, remplacé par T10.
- GECOS, Waterloo Structured BASIC et autres BASIC 6502 : initialement hors
  périmètre, **ajoutés en tâche 15** à la demande de l'utilisateur.

## Risques

- **Congruence T1/T2.** Modifier l'émetteur sans l'info de type produit des
  relocations fausses, **silencieusement**. Risque n°1 du chantier. P0 traite la
  même famille de défaut en amont : une valeur hors plage doit être refusée
  avant de devenir un site de relocation.
- **Volume de relocations.** Une DLL NES typique en compte des centaines
  (chaque `JSR` franchissant une frontière). Coût à anticiper dans le linker.
- **Erreur de résolution différée.** Le lien par nom peut rester silencieux
  jusqu'au runtime (GEOS, stub) ou produire une image non exécutable (NES).
  D'où l'importance du visualiseur de T12.
- **Cycles d'archives.** Les dépendances transitives peuvent créer des cycles.
  À détecter explicitement en T5.
- **Stub 6502 non testable par golden test.** T11 ne se valide que par
  exécution sur cible ou émulateur. Coût de mise au point plus élevé.
- **Budget de tokens Applesoft.** L'encodeur Applesoft (T10) est un sujet à
  part ; à ne pas sous-estimer ni fusionner avec d'autres tâches.
- **`.o` sur disque.** `MultiSegmentOrchestrator` réécrit des `.o`. Le format
  objet doit coexister avec ce mécanisme ou le remplacer proprement.
- **Dérive du périmètre.** « Tout supporter » sans borne devient
  intrainable : chaque BASIC dialectique, chaque variante de SID, chaque
  clone de C64 est une cible. Le périmètre est donc borné explicitement en
  fin de plan, et toute cible hors liste est un ajout, pas une conséquence.

## Validation

- Les 34 tests existants passent inchangés à chaque tâche.
- Golden test octet-pour-octet sur tous les formats du mode direct. Les fixtures
  étant du code valide, elles restent identiques après P0 ; seules les sources
  contenant une valeur hors plage cessent de s'assembler.
- P0 : un diagnostic est émis pour toute valeur hors plage, en première **et**
  en seconde passe, et le build échoue.
- Résolution d'imports : import non résolu → message nommant symbole et
  module attendu ; export en double → modules en conflit ; archive cyclique →
  cycle nommé.
- Linker : une routine partagée liée à deux adresses, exécution vérifiée dans
  un émulateur NES.
- GEOS : application chargée dans VICE depuis un fichier non pré-positionné,
  relocalisée par le kernal, exécutée et affichée correctement.
- Cibles texte : round-trip vers un interpréteur Applesoft ; partage d'une
  routine entre deux projets exécuté correctement.
- Visualiseur : un module réel correspond à ce que le linker a produit.

## Parallélisation

Le plan n'est pas linéaire. Certaines tâches sont strictement indépendantes et
peuvent être conduites par des agents distincts en parallèle, à condition que
leurs fichiers de travail soient disjoints.

**Vague 0 — P0, en solo, avant tout le reste**

- **P0** validation de plage des opérandes (`src/Core/Value.cs`,
  `src/Core/AssemblerEngine.cs`, `src/Core/Diagnostic.cs`, nouveau
  `WinASM65.Tests/OperandRangeTests.cs`).

  Un seul agent, aucun travail parallèle. P0 touche `Core/AssemblerEngine.cs` et
  `Core/Value.cs`, que l'agent B de la vague 1 modifie également : les lancer
  simultanément ferait deux versions divergentes de `HandleInstruction`, dont
  une seule serait testée. P0 doit être **fusionné** avant d'ouvrir la vague 1.

  P0 touche aussi `Diagnostic.cs`, que personne d'autre ne touche dans ce plan.

**Vague 1 — trois agents, aucun chevauchement**
- **A : T0** documentation (`docs/`). Aucun fichier source touché.
- **B : T1 + T2** cœur des relocations (`Expressions/`, `Output/BinaryEmitter.cs`,
  `Core/AssemblerEngine.cs`). Une seule PR, indissociables. Dépend de P0.
- **C : T10** encodeur de tokens (`TextFormat/`, nouveau). Indépendant du
  cœur : il relit le source Applesoft et n'utilise ni relocations ni linker.

**Vague 2 — après B**
- **D : T3** format `.w65` (`Targets/`, `AssemblyResult`).
- **E : T6** mémoire multi-régions (`Segments/SegmentModels.cs`,
  `SystemCatalog`). Disjoint de D sauf sur l'enregistrement des formats.

**Vague 3 — après D et E**
- **F : T4** linker (nouveau module).
- **G : T14** variantes matérielles (`SystemCatalog.cs`, libre depuis E).
- **H : T13** formats BBC (nouveaux fichiers ; la validation attend T4).

**Vague 4 — après F**
- T5 (archives), T12 (visualiseur), T7 (validation NES), T11 (stub runtime).

**Vague 5**
- T8 puis T9 (GEOS, conteneur puis relocation kernal — non séparables), puis
  T15 (BASIC restants, dépend de T10).

### Conditions du parallélisme

**0. Ne pas lancer plusieurs agents dans le même répertoire de travail.**
C'est la condition la plus importante et la plus facile à enfreindre. Deux
agents qui écrivent dans le même arbre se marchent dessus : le second écrase
les modifications du premier, ou fusionne des versions incohérentes, sans
qu'aucune erreur ne soit signalée. Chaque agent parallèle doit avoir son propre
*worktree* git. Si le gestionnaire d'agents du projet sait créer des worktrees,
c'est la voie à privilégier. Sinon, ne pas paralléliser : enchaîner les tâches
est plus sûr qu'un travail perdu.

**1. Isolation par worktree obligatoire.** Même remarque vue sous l'angle des
fichiers : `SystemCatalog.cs` est notamment touché par T6, T13 et T14 ; ces
tâches ne peuvent pas tourner simultanément dans le même arbre.

**2. Sortie de build partagée.** `bin/` et `obj/` sont communs à tous les
agents d'un même worktree. Une compilation concurrente échoue, et une
suppression de `bin/` par un agent casse la validation d'un autre. À isoler,
ou à sérialiser les étapes de build.

**3. `Remove-Item` est autorisé globalement.** Autorisé sans demande, il peut
supprimer les artefacts d'un autre agent. Les agents doivent limiter les
suppressions à leurs propres fichiers de sortie.

**4. Points de contrôle maintenus.** La non-régression des 34 tests et le golden
test octet-pour-octet restent vérifiés à chaque fin de tâche, y compris en
parallèle. Un agent qui valide seulement ses propres fichiers ne peut pas
constater qu'il a cassé le mode direct chez un autre.

### Ce qui ne peut pas être parallélisé

- **T1 et T2** : même PR, par construction.
- **P0 et T1/T2** : mêmes fichiers (`Core/AssemblerEngine.cs`, `Core/Value.cs`).
  P0 passe en vague 0, seul.
- **T8 et T9** : T9 dépend du conteneur produit par T8.
- **T15** : dépend de l'encodeur de T10.
- **T6 et T14** : même fichier (`SystemCatalog.cs`).

## Questions ouvertes

Aucune. Périmètre arrêté par l'utilisateur à « tout supporter », borné comme
suit — au-delà, une nouvelle cible est un ajout explicite, pas une conséquence
implicite :

- **Exécutables plats** : NES, Commodore (PRG), Apple II (A2BIN), BBC
  (principal + Tube), Atari 8, Atari 2600, Oric, Lynx, X16, plus les variantes
  matérielles de T14.
- **Exécutables segmentés** : GEOS (conteneur + relocation par le kernal).
- **Fichiers texte** : DOS 3.2, DOS 3.3, ProDOS, Waterloo BASIC, GECOS,
  BBC BASIC.

Deux précisions de périmètre, pour que la limite soit nette :

- Les variantes matérielles sont des **options** de cible (T14), pas des
  systèmes distincts. On ne crée pas une cible par combinaison PAL/NTSC/SID.
- Les BASIC 6502 couvrent les quatre principaux (Applesoft, Waterloo, GECOS,
  BBC BASIC). Les dialectes mineurs ou expérimentaux ne relèvent pas de ce
  plan.

**3. `Remove-Item` est autorisé globalement** — voir « Conditions du
parallélisation ». Sans worktree isolé, un agent peut supprimer les artefacts
d'un autre.

## Ce qui reste à faire

État au 2 octobre 2026, après `3aadc4f` poussé sur `master`.

### T9 — la validation VICE n'est pas faite

Le code est livré et poussé ; le critère du plan (« l'application se charge
dans VICE depuis un fichier non pré-positionné et s'exécute correctement ») reste
non satisfait. Ce n'est pas un défaut du produit : c'est un blocage de cette
build de VICE, mesuré, dont il faut trouver le contournement avant de pouvoir
conclure.

Trois faits établis, pour ne pas les re-mesurer :

1. `-autostart` tape toujours `LOAD"…",8,1` puis `RUN`, que le fichier soit un
   PRG nu ou le premier fichier d'un disque. Le fichier est chargé aux bonnes
   adresses — vérifié octet par octet en RAM — mais `RUN` n'exécute rien : une
   sonde `10 $FF` (jeton invalide, donc « ?SYNTAX ERROR IN 10 » attendu) ne
   produit aucun message. Ce n'est pas un jeton faux, c'est l'absence de
   programme du point de vue du BASIC.
2. Le moniteur binaire **arrête le CPU à la connexion**. L'horloge du CIA 1
   (`$DC00`) reste à `$7FFF` entre deux lectures espacées de 2 s. Un programme
   ne peut donc pas être observé en cours d'exécution ; on ne peut lire que ce
   qu'il a laissé. Corollaire : la frappe au tampon clavier (`$C6F`) est
   impossible, la machine ne vidant jamais le tampon.
3. Le seul témoin possible est donc l'image, ce qui suppose que le programme
   démarre — et il ne démarre pas.

Harnais écrit et réutilisable, dans `%TEMP%\kilo\vicerun\` (hors dépôt) :
`Probe.ps1` (sonde : BASIC ou machine nue, lecture de l'écran BASIC depuis la
RAM vidéo), `T9Shot.ps1` (témoin par la couleur du bord, vert si la table est
appliquée, rouge sinon, avec une image de contrôle qui doit peindre en rouge),
`T9.ps1` (lecture RAM par le moniteur, utile pour l'agencement mais pas pour
l'exécution). Les jetons BASIC V2 sont résolus : `LOAD` = `$93`, `SYS` = `$9E`,
`STOP` = `$90`, le numéro de ligne est petit-boutiste.

### Trou trouvé au passage, hors dépôt

`ExtractRecord.ps1` lisait le record GEOS comme une suite d'octets continue. Un
secteur de D64 n'en porte que 254, donc au-delà de 256 octets la fin du record
était remplacée par les octets de chaînage du secteur suivant — qui ressemblent à
`00 FF` et se lisaient comme des données plausibles. Corrigé en suivant la
chaîne. Le produit n'était pas en cause.

### Prérequis acquis

VICE 3.10 GTK3 dans `%TEMP%\kilo\vice\gh\GTK3VICE-3.10-win64\bin`, image GEOS
2.0r dans `%TEMP%\kilo\geos\geos.d64` (SHA-256
`1BABD118E2F68604DDA10561A68909B552F4960C586E7588A411BBC4E65A9A01`).
`c1541 -format <nom,id> <type> <image>` crée un disque ; `c1541` refuse un D64 au
format GEOS (« Empty image »), donc il ne peut pas servir de vérificateur de
contenu sur ces images.

### Non commit, à ne pas confondre avec T9

`WinASM65.Monitor/Bridge/bridge_mesen2.lua`,
`WinASM65.Monitor.Tests/MameCapabilityTests.cs`, `WinASM65.Monitor/Bridge/probe_mame.lua`
et le plan du moniteur : autre chantier, désormais commité par la session du
moniteur. `WinASM65.Monitor/Program.cs` appartient également au chantier du
moniteur (validation de `settings.json` pour Mesen2), pas à T9.

## Ordre de grandeur

L'ensemble représente environ 16 tâches, dont cinq (P0, T1, T2, T3, T4) sont
l'infrastructure commune dont tout le reste dépend. P0 est la plus petite et la
seule à ne dépendre d'aucune autre : c'est la porte d'entrée du chantier. Les formats d'OS représentent
la majorité du volume de travail mais sont, eux, largement parallélisables une
fois le socle en place. L'ordre naturel est donc : terminer T1 à T4, puis ouvrir
le plus d'agents possible sur les formats.
