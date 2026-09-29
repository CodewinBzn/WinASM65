# WinASM65 — Le pipeline d'assemblage

Ce document décrit **l'état de départ**, c'est-à-dire le comportement actuel du
code avant toute evolution vers le format d'objet `.w65` et le linker. Il est
ecrit pour servir de reference lors des taches T1 a T4.

Toutes les references de lignes sont donnees pour le code actuel et peuvent
decaler apres evolution.

---

## 1. Vue d'ensemble

```
Fichier source
     |
     v
CommandLineApplication.Run          (src/CommandLineApplication.cs)
     |  resout la cible, cree le CPU, injecte les symboles materiels
     v
AssemblerFactory.Create             (src/Core/AssemblerFactory.cs)
     |
     v
AssemblerEngine.Assemble           (src/Core/AssemblerEngine.cs)
     |  boucle de lecture de fichier
     |    -> ParseLine            classification de la ligne
     |       -> decodage CPU       opcode + mode d'adressage
     |       -> evaluation         Tokenizer -> ExpressionEvaluator
     |       -> emission           BinaryEmitter
     |  ResolvePendingSymbols      passe 2
     v
AssemblyResult                     octets + diagnostics + OriginAddress
     |
     v
ExecutablePublisher.Publish         (src/Targets/ExecutablePublisher.cs)
     |
     v
ExecutableFile.WriteBytes/WriteText  ecriture sur disque
```

Le mode **direct** est le mode par defaut et le seul qui existe aujourd'hui.
Chaque fichier s'assemble seul et produit un blob **deja place a son adresse
reelle**, directement gravable sans linkeur.

---

## 2. Classification des lignes

`AssemblerEngine.ParseLine` (`src/Core/AssemblerEngine.cs:286`) teste une suite
de motifs **dans un ordre fixe**. L'ordre est significatif : le premier motif
qui correspond gagne, donc un motif trop permissif eclipserait tous les
suivants.

| Ordre | Motif | Role |
|---|---|---|
| 0 | etat conditionnel | si `.if` est inactif, la ligne est ignoree (sauf directives conditionnelles) |
| 1 | etat de repetition | si dans un `.rep`, la ligne est mise en tampon |
| 2 | `StartLocalScopeRegex` | `{` — ouvre une portee locale |
| 3 | `EndLocalScopeRegex` | `}` — ferme une portee locale |
| 4 | `MemReserveRegex` | `label .res n` / `label .RES n` |
| 5 | `DirectiveRegex` | toute ligne commencant par `.nom` |
| 6 | `LabelDeclareRegex` | `label:` |
| 7 | `ConstantRegex` | `label = expression` |
| 8 | `InstructionRegex` | `opcode operandes` (3 lettres) |
| 9 | `MacroCallRegex` | appel de macro |
| 10 | — | erreur de syntaxe |

Remarques :

- Le commentaire est retire **avant** toute classification, par
  `Regex.Replace(trimmed, ";(.)*", "")` (`:261`). Le `;` n'est donc pas
  utilisable a l'interieur d'une chaine.
- Le **test des scopes precede le test des directives** (ordre 2/3 avant 5).
  Une ligne `{` seule ne peut pas etre une directive.
- `MemReserveRegex` precede `DirectiveRegex` parce que `DirectiveRegex`
  n'est **pas ancre** (aucun `^` ni `$`, `:71`) et matche donc `.res` en
  milieu de ligne, y compris dans `label .res 4`. `MemReserveRegex` est
  ancre (`:74`) et correspond a la ligne entiere. Sans cet ordre, la ligne
  `label .res 4` partirait vers le dispatcher de directives au lieu de
  `HandleMemReserve`.
- Le `MacroCallRegex` est le dernier recours : il est volontairement tres
  permissif (`une suite alphanumerique`), d'ou son placement en fin de chaine.

### Memoire et provenance

Chaque ligne est traitee dans un `SourceFileState` qui porte le chemin du
fichier et le numero de ligne courant (`:695`). `CurrentLocation` (`:96`)
compose ces deux informations, et c'est la seule source de provenance de
l'assembleur. Le couple (fichier, ligne) est disponible au moment de
l'emission, ce qui satisfait l'exigence de la tache T1 — mais il n'est
**pas** transmis a l'emetteur (voir section 8).

---

## 3. Decodage CPU

`Cpu6502.ParseOperand` (`src/Cpu/Cpu6502.cs:152`) transforme
`mnemonic + operandes` en `InstructionInfo { Mode, Opcode, Length,
OperandExpression }`.

### La table d'opcodes

La table est un dictionnaire `mnemonic -> byte[13]`, un octet par mode
d'adressage, ou `0xFF` signifie « non supporte » (`:36`). Ordre des modes :

```
0 IMP  1 ACC  2 IMM  3 ABS  4 ABX  5 ABY
6 ZPG  7 ZPX  8 ZPY  9 IND 10 INX 11 INY 12 REL
```

C'est bien **13 opcodes par mnemonique**, comme le note le plan.

`Cpu65C02` derive de `Cpu6502` et remplace les propriétés virtuelles
(`OpcodeTable`, `RelativeBranchMnemonics`, `AccumulatorMnemonics`), ce qui
permet d'etendre le jeu d'instructions sans toucher au decodeur generique.

### Resolution du mode

L'ordre de reconnaissance des operandes determine le mode, et le premier motif
qui correspond gagne :

1. Operande vide → `Accumulator` si le mnemonique est dans
   `ASL/LSR/ROL/ROR`, sinon `Implicit` (`:158`)
2. Mnemonique de branchement relatif (`BCC BCS BEQ BMI BNE BPL BVC BVS`) →
   `Relative` (`:172`)
3. Les espaces sont supprimes, puis :
   - commence par `#` → `Immediate` (`:181`)
   - `(expr,x)` → `IndirectX` (`:189`)
   - `(expr),y` → `IndirectY` (`:195`)
   - `(expr)` → `Indirect` (`:203`)
   - finit par `,x` → `AbsoluteX` (`:209`)
   - finit par `,y` → `AbsoluteY` (`:217`)
   - **sinon → `Absolute` par defaut** (`:227`)

### Optimisation zero-page

`TryOptimizeZeroPage` (`:245`) reduit une adresse absolue qui tient sur un
octet vers le mode zero-page equivalent, raccourcissant l'instruction de
3 a 2 octets. Cela concerne `Absolute` → `ZeroPage`,
`AbsoluteX` → `ZeroPageX`, `AbsoluteY` → `ZeroPageY`, et uniquement si
l'opcode existe dans ce mode. Sans cette optimisation, l'assembleur
emetrait `STA $10` sur 3 octets au lieu de 2 — un cas ou le mode assembleur
et le mode « source explicite » different.

### Branchements relatifs

`TryCalculateRelativeOffset` (`:277`) calcule `cible - (adresse + 2)`, soit un
decalage relatif au PC **apres** l'instruction de 2 octets, et refuse tout
decalage hors de `[-128, +127]` via `ErrorCodes.REL_JUMP`.

---

## 4. Evaluation des expressions

### Tokenizer

`Tokenizer` (`src/Expressions/Tokenizer.cs`) est une **unique expression
regex** dont les groupes nommes sont convertis en `TokenType`. Les formes
numeriques reconnues :

| Forme | Exemple | Type |
|---|---|---|
| decimal | `42` | `DecimalNumber` |
| hexadecimal | `$FF` | `HexNumber` |
| binaire | `%1010` | `BinaryNumber` |
| caractere | `"A"` | `CharacterConstant` |
| booleens | `TRUE` / `FALSE` | `True` / `False` |
| identifiant | `label` | `Identifier` |

L'ordre des alternatives dans le motif est important : `OR|or|\|\|` precede
les operateurs, et `%` binaire precede `%` modulo. Le motif se termine par
`(?<invalid>[^\s]+)` qui absorbe tout residu.

### Evaluateur

`ExpressionEvaluator.Evaluate` (`src/Expressions/ExpressionEvaluator.cs:27`)
est un **shunting-yard** fortement type :

1. Tokenisation.
2. Parcours des tokens : les litteraux deviennent des valeurs, les
   identifiants sont resolus via `ISymbolResolver`, les operateurs sont
   empiles.
3. **Si au moins un symbole est indefini, l'evaluation s'arrete ici** et
   renvoie `ExpressionResult.WithUndefinedSymbols(...)` (`:91`). Aucun
   calcul n'est effectue — le resultat n'est pas partiellement correct.
4. Sinon, reduction de la pile d'operateurs.

L'unification est determinee par la position : un operateur `+`, `-`, `~`,
`!`, `<`, `>` est unaire s'il est en position 0 ou s'il precede un
operateur ou une parenthese fermante (`:128`).

Precedences, de la plus forte a la plus faible :

```
unaires 11 | * / % 10 | + - 9 | << >> 8 | < > <= >= 7
== != = 6 | & 5 | ^ 4 | | 3 | AND 2 | OR 1
```

`<` et `>` unaires sont les operateurs d'octet bas et haut du langage.

### Le resultat porte seulement un booleen

`ExpressionResult` (`src/Expressions/ExpressionResult.cs`) expose `Value` et
`UndefinedSymbols`, et `IsResolved` se reduit a
`UndefinedSymbols.Count == 0`.

**C'est la contrainte bloquante identifiee dans le plan.** `lda #$05` et
`lda label` produisent tous deux un `ExpressionResult` resolu, sans aucune
trace du fait que le second contient un symbole. Une fois aplatie en octets
par `HandleInstruction`, l'information est perdue de facon definitive. La
tache T1 consiste a propager les symboles *utilises* — avec fichier et ligne
source — jusqu'a l'emetteur.

---

## 5. Emission

### BinaryEmitter

`BinaryEmitter` (`src/Output/BinaryEmitter.cs`) tient :

- une `List<byte> _buffer` unique ;
- `CurrentAddress` — position de lecture courante ;
- `OriginAddress` — adresse de base du buffer.

`EmitByte` / `EmitWord` / `EmitBytes` ajoutent a la fin et avancent
`CurrentAddress`. `PatchByte` / `PatchWord` / `PatchBytes` ecrivent a une
position donnee. La position de patch est donc **relative a `OriginAddress`**
(voir `:561` : `opcodeAddress - _emitter.OriginAddress + 1`).

Il n'existe **aucun concept de segment**. Un seul buffer, une seule base,
un seul `CurrentAddress`. C'est la deuxieme contrainte bloquante du plan.

### Origine

L'origine est fixee par `.org`, ou a defaut par `AssemblerOptions.DefaultOrigin`
passe a l'assembleur (`:193`). `ApplyDefaultOrigin` initialise les deux
variables `CurrentAddress` et `OriginAddress`, mais **seulement si une
valeur est fournie** ; sans valeur, tout demarre a 0. Un `.org` ulterieur
ecrase cette valeur.

`ResolvedTarget.OriginAddress` est propage depuis `AssemblyResult` jusqu'aux
formats de sortie, ou il prime sur `LoadAddress` (voir section 7).

### Une instruction, deux chemins

`HandleInstruction` (`:439`) distingue le cas resolu du cas non resolu.

**Cas resolu** — l'expression est evaluee immediatement :

- mode `Relative` → `TryCalculateRelativeOffset`, puis opcode + 1 octet ;
- sinon, tentative d'optimisation zero-page ;
- sinon emission directe en 2 ou 3 octets selon `info.Length`.

**Cas non resolu** — des **placeholders** sont emis (0 pour l'octet ou le
mot), et l'emplacement est enregistre pour la passe 2 :

```csharp
ushort position = (ushort)(opcodeAddress - _emitter.OriginAddress + 1);
_scopeManager.AddUnresolvedExpression(position, unresExpr);
```

Chaque symbole indini est enregistre via `AddUnresolvedSymbol`, et
`NbrUndefinedSymb` compte les dependances.

Ce mecanisme de placeholders est ce qui permet a un `JSR` de franchir la
frontiere entre deux fichiers, mais il **confond l'operande de donnees et
l'operande d'adresse** : dans les deux cas on ne retient qu'une position et
une largeur. Rien ne dit que l'octet a cet endroit est une adresse.

---

## 6. Passe 2 et dependances symboliques

`ScopeManager` (`src/Symbols/ScopeManager.cs`) gere une pile de portees
lexicales. `{` et `}` empilent et depilent ; `TryResolveSymbol` remonte la
pile jusqu'a la portee globale (`:123`).

### Resolution par comptage de dependances

Le resolution n'est pas un parcours de graphe mais un **decompte** :

- `UnresolvedSymbol.NbrUndefinedSymb` compte le nombre de symboles encore
  indefinis dans son expression ;
- `UnresolvedExpr.NbrUndefinedSymb` fait de meme pour un site d'emission ;
- a chaque symbole defini, `ResolveSymbolDepsAndExprs` (`:230`) decremente
  ces compteurs ; quand un compteur atteint zero, l'expression est
  reevaluee et, si elle reussit, le site est patche.

Un site n'est donc patche que lorsque **toutes** ses dependances sont
resolues. C'est ce qui rend la resolution independante de l'ordre de
lecture des fichiers.

`HandleLabel` (`:369`) declenche `ResolveSymbols` a chaque definition de
label, ce qui resout au plus tot sans attendre la fin du fichier.

### Echange par fichiers

Quand un symbole reste indefini en fin de fichier, l'assembleur ecrit trois
fichiers (`:672`) :

| Fichier | Contenu |
|---|---|
| `<base>.symb` | table des symboles definis (JSON) |
| `<base>.Unsolved` | symboles non resolus, avec leurs dependances |
| `<base>.UnsolvedExpr` | sites d'emission non resolus |

Le nom de base est `sourceFile.Split('.')[0]`, donc un chemin contenant un
point est tronque a son premier point.

### Orchestration multi-fichiers

`MultiSegmentOrchestrator` (`src/Segments/MultiSegmentOrchestrator.cs`)
enchaîne les segments declares dans `config.json` :

1. Assemblage de chaque segment, qui produit ses `.o`, `.symb`, `.Unsolved`,
   `.UnsolvedExpr`.
2. Pour chaque segment, rechargement du `.o` dans l'emetteur, puis
   rechargement des `.Unsolved` et `.UnsolvedExpr` (`:77-99`).
3. Pour chaque **dependance declaree**, lecture du `.symb` correspondant et
   injection des symboles dans la portee globale, puis
   `ResolvePendingSymbols()` (`:101-118`).
4. Si des symboles ou des sites restent non resolus, erreur
   `UNDEFINED_SYMBOL`.
5. Suppression des `.Unsolved` / `.UnsolvedExpr` traites.

Les dependances sont **declarees a la main** dans le JSON. Il n'existe aucun
mecanisme d'export/import formel : rien ne dit qu'un symbole est exporte
plutot que simplement defini, ni quel module est cense l'exporter.

C'est la troisieme contrainte bloquante du plan, et la raison pour laquelle
le format d'objet `.w65` (T3) est necessaire.

---

## 7. Sortie

`ExecutablePublisher` (`src/Targets/ExecutablePublisher.cs`) associe un nom
de format a une implementation `IExecutableFormat` :

| Nom | Classe | Contenu |
|---|---|---|
| `bin` | `RawBinaryFormat` | octets bruts |
| `ines` | `InesFormat` | en-tete iNES + PRG + CHR |
| `prg` | `CommodorePrgFormat` | adresse de chargement 16 bits + charge |
| `xex` | `AtariXexFormat` | magic + adresse de depart + charge |
| `a2bin` | `Apple2BinaryFormat` | en-tete Apple II + segments |
| `rom` | `PaddedRomFormat` | remplissage a `RomSize` |
| `o65` | `O65Format` | en-tete plat de 12 octets |
| `ihex` | `IntelHexFormat` | enregistrements Intel HEX |
| `srec` | `MotorolaSrecFormat` | enregistrements Motorola S-record |

Un nom inconnu produit un diagnostic nommant les formats acceptes, plutot
qu'une exception.

### Origine du format

Chaque format qui a besoin d'une adresse applique la meme priorite :

```
1. target.OriginAddress   — adresse reellement obtenue a l'assemblage
2. target.LoadAddress     — valeur de la cible ou de la configuration
3. 0                       — defaut
```

`OriginAddress` l'emporte parce qu'il reflete l'assemblage reel : si le
source contenait un `.org` different de l'adresse de chargement prevue,
c'est `.org` qui fait foi. C'est le comportement verifie par
`PrgFormat_PrefersOriginAddressOverLoadAddress`.

### Combiner

Pour une sortie multi-fichiers, `IBinaryCombiner` concatene les `.o` selon
`Output.Files`, avec un `Size` optionnel servant au remplissage. L'ordre du
JSON ne fait que **confirmer** le placement : chaque fichier porte son
propre `.org`, l'image est donc deja correctement positionnee avant
concatenation.

---

## 8. Ce qui manque pour les relocations

Resume des trois points identifies dans le plan, avec leur localisation
exacte.

| Manque | Localisation | Consequence |
|---|---|---|
| Le type d'expression n'atteint pas l'emetteur | `ExpressionResult` n'expose que `IsResolved` (`ExpressionResult.cs:14`) | impossible de distinguer `lda #$05` de `lda label` |
| L'aplatissement des adresses | `HandleInstruction` : `val = exprRes.Value.AsInteger` puis `EmitWord((ushort)(val & 0xFFFF))` (`AssemblerEngine.cs:485`, `:538`) | aucune trace du site, aucune possibilite de relocaliser |
| Aucun segment | `BinaryEmitter` : un `List<byte>`, un `CurrentAddress` (`BinaryEmitter.cs:28`) | pas de placement multi-regions |
| Pas d'export/import | `MultiSegmentOrchestrator` : echanges par `.symb` / `.Unsolved` | dependances declarees a la main, pas de diagnostic nomme |

Le risque associe au premier point est le plus eleve du chantier : une
relocation produite sans le type d'expression est **fausse et silencieuse**.
C'est la raison pour laquelle T1 et T2 doivent former une seule PR.

---

## 9. Ce que le mode direct garantit

- Chaque fichier s'assemble independamment et produit un blob place a son
  adresse reelle.
- L'ordre de `Output.Files` ne determine pas le placement.
- La sortie est gravable sans linkeur.
- Les tests `DirectBurnGoldenTests` verrouillent les octets de sortie pour
  `bin`, `prg`, `a2bin`, `xex`, `ines`, `rom`, `ihex`, `srec`.

Toute evolution doit preserver ces proprietes. Le format `.w65` et le linker
sont **ajoutes** a cote, ils ne remplacent pas ce mode.
