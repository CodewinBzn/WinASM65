# Validation de plage des opérandes WinASM65

> **Plan autonome — absorbé.** À la décision de l'utilisateur, ce travail est
> intégré en **tâche P0 (portail)** du plan modulaire `.w65` :
> `.kilo/plans/1790619836462-geos-os-target-socle.md`, section « Séquence ».
> **La source de vérité est ce plan-là.** Ce fichier n'est conservé que comme
> spécification détaillée ; s'il diverge de P0, P0 fait foi. Ne pas implémenter
> les deux.

## Contexte et problème

L'assembleur tronque silencieusement toute valeur d'opérande hors plage. Aucun diagnostic
n'est émis, aucun code de sortie n'échoue : `LDA #300` produit `A9 2C` et se grave tel quel
sur la cartouche.

Les cinq sites de troncature, tous confirmés par lecture du code :

| Fichier | Ligne | Code | Problème |
|---|---|---|---|
| `WinASM65/src/Core/Value.cs` | 57-60 | `ToByte()` → `(byte)(AsInteger & 0xFF)` | masque au lieu de valider |
| `WinASM65/src/Core/Value.cs` | 62-65 | `ToUInt16()` → `(ushort)(AsInteger & 0xFFFF)` | masque au lieu de valider |
| `WinASM65/src/Core/AssemblerEngine.cs` | 519 | `EmitByte((byte)val)` si `info.Length == 2` | troncature silencieuse |
| `WinASM65/src/Core/AssemblerEngine.cs` | 524 | `EmitWord((ushort)val)` | troncature silencieuse |
| `WinASM65/src/Core/AssemblerEngine.cs` | 633, 637 | `PatchWord`/`PatchByte` via `ToUInt16()`/`ToByte()` | troncature en **seconde passe** |

Le chemin d'optimisation zéro-page est **déjà correct** et ne doit pas être touché :
`TryOptimizeZeroPage` (`WinASM65/src/Cpu/Cpu6502.cs:252`) refuse déjà toute valeur
hors `0..255`, donc `AssemblerEngine.cs:508-511` n'émet jamais de valeur tronquée.

### Contrainte à ne pas casser

La troncature est involontaire mais il est possible qu'un des 34 tests existants, ou un
fichier de `example_bomberman-nes/`, en dépende. La tâche 6 consiste à mesurer cela avant
de choisir definitively entre les tâches 3 et 4 (voir « Décision conditionnelle »).

## Plages valides

Les plages sont signées à gauche pour que `LDA #-1` continue de produire `0xFF` et
`JMP $-1` de produire `0xFFFF`. C'est le comportement standard des assembleurs 6502.

- Opérande 1 octet : `-128 .. 255`, puis émission par `(byte)(v & 0xFF)`
- Opérande 2 octets : `-32768 .. 65535`, puis émission par `(ushort)(v & 0xFFFF)`

## Décision de conception

**Ne pas changer la sémantique de `Value.ToByte()` et `ToUInt16()`.** Ces méthodes sont
des conversions par masque et d'autres appelants peuvent en dépendre. Les laisser intactes
et ajouter des méthodes de validation distinctes limite le risque de régression.

Émettre des octets de remplacement à zéro en cas d'erreur, comme le fait déjà la branche
« unresolved » de `AssemblerEngine.cs:533-545`, plutôt que de ne rien émettre. Cela garde
les adresses et le listing cohérents si plusieurs erreurs se cumulent. L'échec du build est
assuré par `IDiagnosticReporter.HasErrors` (`Diagnostic.cs:111`), pas par l'émission.

## Tâches

### 1. Constantes de diagnostic
Dans `WinASM65/src/Core/Diagnostic.cs`, ajouter à la classe `ErrorCodes` existante
(lignes 74-95), en suivant la convention de nommage en majuscules du fichier :
- `VALUE_OUT_OF_RANGE_BYTE`
- `VALUE_OUT_OF_RANGE_WORD`

Les messages doivent inclure la valeur fautive et la plage acceptable, pour que
`LDA #300` produise un diagnostic actionnable.

### 2. Aides de validation
Dans `WinASM65/src/Core/Value.cs`, ajouter sans modifier l'existant :
- `bool InByteRange(long v)` → `-128 <= v <= 255`
- `bool InWordRange(long v)` → `-32768 <= v <= 65535`

Des prédicats plutôt que des `TryToByte`/`TryToUInt16` : l'appelant a besoin de la plage
pour le message d'erreur, et l'émission reste faite par `EmitByte`/`EmitWord`.

### 3. Première passe
Dans `WinASM65/src/Core/AssemblerEngine.cs`, dans le bloc `exprRes.IsResolved` de
`HandleInstruction`, avant toute émission :
- si `info.Length == 2` et `!val.InByteRange()` → `ReportError(CurrentLocation, ...)` et
  émettre opcode + `0` de remplacement
- sinon si `info.Length != 2` et `!val.InWordRange()` → même traitement sur 2 octets

Le cast `(byte)val` ligne 519 et `(ushort)val` ligne 524 devient inoffensif une fois la
validation en place, mais le remplacer par un masquage explicite `& 0xFF` / `& 0xFFFF`
rend l'intention non ambiguë.

### 4. Seconde passe
Dans `PatchResolvedExpression` (`AssemblerEngine.cs:612-640`), appliquer la même validation
avant `PatchWord` (ligne 633) et `PatchByte` (ligne 637).

Cette étape est indispensable et non optionnelle : un opérande hors plage référencé par un
symbole non encore résolu à la première passe ne passerait jamais par le code de la
tâche 3. Sans ce correctif, le bug resterait accessible par `LDA FORWARD_LABEL`.

Noter que `_diagnostics.ReportError(CurrentLocation, ...)` y signale une localisation
approximative, car le patch s'exécute hors du flux ligne par ligne. C'est un défaut
préexistant de `UnresolvedExpr` (`SymbolTypes.cs:29-40`), hors périmètre : ne pas le
corriger ici, mais le signaler dans le message d'erreur si possible en changeant
`AssemblerEngine.cs:554` pour stocker la ligne d'origine dans `UnresolvedExpr`.

### 5. Audit des autres appelants
La recherche exhaustive des appels à `ToByte()`/`ToUInt16()` a été **bloquée par les
permissions** du dépôt lors de la planification : la commande `Select-String` a été refusée,
et `rg`/`grep` sont indisponibles (échec de téléchargement de ripgrep). L'inventaire
ci-dessus, issu de la lecture directe de `Value.cs` et `AssemblerEngine.cs`, est donc
**complet pour ces deux fichiers mais non vérifié pour le reste de l'arbre**.

Avant de considérer la tâche terminée, l'agent d'implémentation doit lister les autres
appelants et décider pour chacun :
- si le masquage y est intentionnel (conversion d'adresse, index, masque de bits) →
  ne rien changer
- si le masquage y est un chemin d'émission → appliquer la même validation

Outil suggéré pour l'audit, en évitant les pipes bloqués par les permissions :
`Select-String -Path "WinASM65\src\*\*\*.cs" -Pattern "ToByte" -List`

### 6. Mesurer l'impact avant de trancher
Ajouter les tests de la tâche 7, puis lancer la suite existante. Si des tests échouent
**uniquement** parce qu'ils dépendaient de la troncature, deux options :
- corriger le source du test pour utiliser une valeur dans la plage (préféré, le test
  testait autre chose)
- n'appliquer la validation qu'aux cibles concernées si un comportement doit rester

Ne pas assouplir la validation pour faire passer un test. Si un test échoue pour une autre
raison, c'est un problème distinct à traiter séparément.

## Tests à ajouter

Dans `WinASM65.Tests/TargetTests.cs` ou, de préférence, un nouveau fichier
`WinASM65.Tests/OperandRangeTests.cs` pour ne pas mélanger les sujets. Convention existante
à suivre : attributs `[TestClass]` / `[TestMethod]`, style `NomDuSujet_ComportementAttendu`.

Tests unitaires sur `Value` :
- `InByteRange_Accepte255EtMoins128`
- `InByteRange_Rejette256EtMoins129`
- `InWordRange_Accepte65535EtMoins32768`
- `InWordRange_Rejette65536EtMoins32769`

Tests d'intégration sur l'assembleur complet (assembler une chaîne, inspecter les octets
émis et les diagnostics) :
- `Immediate_HorsPlage_EmetUneErreur` — `LDA #300` → diagnostic, 0 octet de code produit
- `Immediate_Negatif_EmetFF` — `LDA #-1` → `A9 FF`, aucun diagnostic
- `Immediat_256_EstValide` — `LDA #255` → `A9 FF`, aucun diagnostic
- `Absolu_HorsPlage_EmetUneErreur` — `JMP $12345` → diagnostic
- `SymboleEnAvant_HorsPlage_EmetUneErreur` — `LDA LATER` puis `LATER equ 300` → diagnostic
  (couvre la seconde passe, tâche 4)
- `ZeroPage_Optimise_NEstPasAffecte` — `LDA $10` → `A5 10` et non `AD 00 10`, aucun
  diagnostic (verrouille la non-régression du chemin zéro-page)
- `OperandeDansLaPlage_NEmetPasDeDiagnostic` — contrôle général

Pour les tests d'intégration, préférer l'API publique d'assemblage déjà utilisée par
`WinASM65.Tests/AssemblyIntegrationTests.cs` plutôt que d'instancier `AssemblerEngine` en
interne, afin de rester cohérent avec l'approche existante.

## Risques

- **Rupture de compatibilité** : du code assembleur existant qui reposait sur la
  troncature cessera de s'assembler. C'est le comportement voulu, mais cela doit être
  signalé dans le README.
- **Passe 2 et localisation** : les diagnostics de la tâche 4 pointeront une ligne
  approximative. Préexistant, non corrigé ici.
- **Faux positifs** : un masque de bits légitime (`AND #$0F` sur une constante calculée
  plus large) pourrait maintenant être rejeté si la valeur intermédiaire dépasse la plage.
  C'est le comportement correct, mais cela peut révéler des sources qui comptaient sur la
  troncature.

## Validation

Depuis `C:\Projects\WinASM65` :

1. `dotnet build WinASM65.sln -c Release` → 0 erreur, 0 avertissement
2. `dotnet test WinASM65.Tests/WinASM65.Tests.csproj` → les 34 tests préexistants
   toujours verts, plus les nouveaux
3. Vérification manuelle du non-régression du mode direct : assembler
   `example_bomberman-nes/` avant et après le changement et comparer les octets produits.
   Le mode direct doit rester bit pour bit identique pour le code déjà valide.
4. Confirmer le code de sortie non nul sur une source contenant `LDA #300`

Note : la commande `dotnet` est autorisée par les permissions du dépôt ; en revanche les
pipes PowerShell (`|`) sont refusés, ce qui a bloqué plusieurs tentatives de recherche
textuelle pendant la planification.

## Hors périmètre

- Correction de la localisation imprécise des diagnostics de seconde passe
- Validation de plage pour les directives `.byte` / `.word` (`ErrorCodes.DATA_BYTE` et
  `DATA_WORD` suggèrent un chemin de données distinct qui n'a pas été audité) — à traiter
  comme tâche séparée si le même problème s'y reproduit
- Toute modification du plan modulaire `.w65` (`.kilo/plans/1790619836462-geos-os-target-socle.md`)
