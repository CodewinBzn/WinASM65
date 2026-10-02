# WinASM65 - Le moniteur 6502

## 1. Ce que c'est

Un moniteur assemble une source, la place dans une machine emulee, la relit, et
la deplace ailleurs sans reassembler. Le cas d'usage est le debogage : on ecrit
une routine, on la charge a une adresse ou le programme ne fonctionne pas, on lit
la memoire autour, on change d'adresse, on recommence.

```
WinASM65.Monitor.exe --mesen "C:\...\MesenCE\Mesen.exe" --rom jeu.nes
```

ou, si un pont ecoute deja :

```
WinASM65.Monitor.exe --port 45678
```

## 2. Ce que l'emulateur seul n'apporte pas

Un emulateur sait charger un fichier et l'executer. Il ne sait pas assembling
depuis une session, ni relinker a la volee, ni relire pour verifier.

La propriete qui compte est la **relecture**. `LOAD` ecrit, puis relit et compare
octet a octet. Un pont qui ecrit moins que ce qu'il annonce, ou qui ecrit ailleurs,
repondrait pourtant `OK` ; la comparaison est la seule preuve disponible que la
routine est reellement a l'adresse nommee. Elle est donc obligatoire, pas
decorative, et elle est verifiee par test.

`LOAD <unite> <adresse>` place l'unite **entiere** a l'adresse nommee. Pour une unite a
plusieurs blocs, c'est le premier bloc qui atterrit a cette adresse et les autres
gardent leur distance : poser une unite a $8000/$9000 a $3000 donne $3000/$4000,
et non deux blocs empiles. Les relocations de chaque bloc sont corrigees de la
meme quantite, donc un bloc qui lit sa propre adresse bouge juste.

## 3. Commandes

| Commande | Effet |
|---|---|
| `PING` | nom et version de l'hote |
| `READ <addr> <len>` | lecture, en hexadecimal |
| `WRITE <addr> <hex>` | ecriture ; les octets peuvent etre separes par des espaces |
| `DISASM <addr> <count>` | desassemblage depuis un instantane unique |
| `PAUSE` `RESUME` `STEP` `RESET` | controle d'execution |
| `BREAK SET\|REMOVE\|LIST\|CLEAR` | points d'arret, genre `read`/`write`/`exec` |
| `STATE SAVE` / `STATE LOAD <hex>` | instantane complet |
| `ASSEMBLE <source>` | assemble une fois et garde le resultat |
| `LOAD <unite> <adresse>` | relie, ecrit, relit |
| `UNITS` | ce qui a ete assemble |
| `HELP` | la liste ci-dessus |
| `QUIT` | sortir |

Une adresse s'ecrit `$1234`, `0x1234`, `d1234` (decimal) ou `1234` (hexadecimal,
comme partout en assembleur). Un verbe inconnu est **nomme**, jamais transmis tel
quel au pont : sinon l'utilisateur ne saurait plus quelle couche a repondu.

## 4. Architecture

```
        Program.exe            repl : lire une ligne, l'afficher
             |
       MonitorSession         un verbe -> des reponses. Ne lit pas la console.
        /          \
 UnitLibrary     IMemoryBackend     le contrat unique vers la machine
        |              |
        |        BridgeMemoryBackend  ---- socket TCP ---->  bridge.lua
        |                                          (dans MesenCE)
 RelocatableUnit
        |
   l'assembleur et le linker de WinASM65
```

Trois decisions, et pourquoi :

**La session ne parle pas a la console.** `MonitorSession.Execute` renvoie des
lignes. C'est ce qui permet de tester le REPL entierement, sans emulateur et sans
terminal, et ce qui empeche le shell et la socket de diverger : une ligne tapee et
une ligne recue ne peuvent pas signifier deux choses, parce qu'il n'y a qu'une
implementation.

**Les unites vivent a part de la machine.** Une unite ne depend d'aucune machine :
une fois assemblee, les modules qui en sortent sont les memes ou qu'elle soit
placee. `UnitLibrary` les garde, `RelocatableUnit` sait les relier. C'est ce qui
permet a la fois le TCP (`ProtocolServer`) et le REPL de placer les memes unites
sans deux implementations du meme comportement.

**Une seule porte d'acces a la memoire.** `IMemoryBackend` est le seul contrat
avec un emulateur. Un backend reel (le pont) et un backend de test (le faux)
l'implimentent, ce qui rend tout le moniteur testable sans lancer Mesen.

## 5. Le pont Lua

`Bridge/bridge.lua` s'execute dans MesenCE, ouvre un socket sur
`127.0.0.1:45678`, et repond en texte ligne a ligne.

Protocole : `OK ...` ou `ERR <raison>`. Rien ne reussit silencieusement, pour la
meme raison que la validation des operandes (P0) : un refus non nomme est un bug.

| Commande | Reponse |
|---|---|
| `PING` | `OK MesenCE 2.2.1 nesDebug` |
| `READ $<addr> <len>` | `OK <hex>` |
| `WRITE $<addr> <hex>` | `OK` |
| `STATE SAVE` / `STATE LOAD <hex>` | `OK <hex>` / `OK` |

### Ce que ce produit ne sait pas faire

MesenCE 2.2.1 expose `emu.pause` ? non. `emu.step` ? non. Un point d'arret ? non.
Le pont ne les simule pas : il repond `ERR` avec la raison, et le backend la
remonte telle quelle. Un backend qui ne ferait rien laisserait le moniteur afficher
un etat dans lequel la machine n'est pas -- c'est le seul mode de defaut contre
lequel tout le reste est construit.

### Bornes mesurees

Le pont est un script Lua synchrone : une requete non bornee gele l'emulateur, et
l'utilisateur croit qu'il a plante.

| Born | Valeur | Raison |
|---|---|---|
| lecture | 4096 octets | au-dela, Lua sature une passe |
| ecriture | 4096 octets | idem, le pont ecrit octet par octet |
| `LOAD` | 16 Ki | une ecriture de 64 KiB dans un seul message gele l'emulateur |
| instantane | 128 Ki | au-dela, la ligne de reponse devient absurde |
| delai de socket | 1 s | le pont est synchrone : il ne peut pas hostage son propre producteur |

Le pont decoupe aussi les lectures par blocs de 256 octets. Ce n'est pas une
 precaution : MesenCE tue un script qui reste trop longtemps dans une passe.

## 6. MesenCE 2.2.1 - pieges mesures

Ces trois-la ont ete payes, et aucun n'est une faute de Lua standard. Ils sont
dans le code la ou ils mordent, parce qu'ils se reproduisent tous par un echec
**silencieux** : code de sortie -1, aucune sortie, aucun rapport.

1. **`string.format("%04X", nil)` est une exception, pas un zero.** Un
   `emu.getState()` dont la forme differe ne donne donc pas un etat a zero : il
   tue le callback, et comme plus personne n'appelle `emu.stop(0)` ensuite,
   l'emulateur reste vivant. Le symptome n'est pas le code de sortie, c'est le
   delai : 3 min 21 s au lieu de 0,9 s.

2. **Un `[[` imbrique dans une chaine casse le script au chargement.** Un chemin
   Windows dans une chaine longue-bracket n'est pas un echappement Lua, et le
   parseur de MesenCE ne s'en remettait pas. La forme qui marche :
   `rapport:write([[le chemin]] .. "\n")` -- le long crochet *est* la chaine.

3. **`emu.memType` est renomme et specifique au systeme.** En 2.2.1 il n'y a
   plus de `cpu` ni de `cpuDebug` generiques : `nesDebug` (264),
   `nesInternalRam` (48), `nesPrgRom` (47), 264 valeurs au total, une par systeme.
   Le nom du type de memoire est donc une decision d'adaptateur par emulateur, et
   non une constante partagee.

MesenCE echoue aussi sur `bomber.nes` (`[CPU] Uninitialized memory read: $0008`) :
le jeu lit un miroir de PPUSTATUS avant que le PPU soit pret, et le mode
testrunner traite une lecture non initialisee comme une erreur fatale. Ce n'est
pas une defaute de la ROM.

## 7. Ce que la validation couvre

509 tests verts, dont 101 pour le moniteur.

| Couche | Ce qui est verifie |
|---|---|
| `ProtocolTests` | parsing, bornes, reponses du serveur |
| `BreakpointSetTests` | ajout, retrait, effacement |
| `MemoryBackendTests` | plage, ecriture atomique, instantane |
| `BridgeMemoryBackendTests` | le backend sur **socket TCP reel** contre un vrai serveur, y compris le decoupage en blocs de 256 octets |
| `RelocatableUnitTests` | assemblage, relocation, relecture apres `LOAD` |
| `MonitorSessionTests` | chaque verbe, et chaque faute de frappe |
| `MonitorProgramTests` | **l'executable lance**, ses arguments, son flux d'entree, sa sortie |

Les deux derniers niveaux existent parce que la bibliotheque peut etre parfaite et
l'outil ne pas fonctionner : `MonitorProgramTests` demarre le vrai binaire, ecrit
des commandes sur son entree standard, et verifie ce qu'il imprime. C'est la seule
maniere de couvrir le nom de l'hote annonce, l'analyse des arguments, l'attente du
pont, et le fait qu'une faute de frappe ne termine pas le processus.

Le choix de `BridgeMemoryBackendTests` est a signaler : il utilise un backend
faux, pas un emulateur. Ce n'est pas une limite, c'est le propos. Ce qui est
verifie la est justement ce que le pont *est* -- une ligne de texte, et deux bouts
qui s'entendent -- alors qu'un emulateur reel ajouterait la preuve que MesenCE
repond, ce qu'il fait, et qui ne change rien au fait que les octets ecrits soient
ceux que le linkeur a produits.

## 9. Limites nommees

- **`BREAK REMOVE` n'est pas implemente.** Un pont possede ses points d'arret ; la
  session le dit plutot que de pretendre.
- **`STATE SAVE` suppose un tableau contigu.** `emu.getState` ne garantit pas cela.
  Le pont refuse avec le type recu plutot que de repondre `OK` suivi d'un payload
  vide, qui ferait croire a un instantane sauvegarde.
- **`DISASM` n'est pas verifie contre un emulateur.** Il est verifie contre un
  backend qui repond, ce qui prouve le decodage et la lecture en un seul instant,
  pas que MesenCE lit au bon endroit. La lecture passe par
  `emu.read(..., nesDebug)`, dont la valeur est mesuree ailleurs.
- **Version de l'hote.** Le pont annonce la sienne dans `PING` et le backend la
  remonte sans la comparer a une version figee. Un test qui passe « sans nommer
  l'hote » ne prouve rien de reutilisable.

## 10. Licence

Le pont doit rester un script charge par l'emulateur, jamais lie. Un linkage
contaminerait WinASM65 par la GPL de l'emulateur. Aucun code de Mesen n'est
repris ici : seulement les noms d'API, qui sont une interface.