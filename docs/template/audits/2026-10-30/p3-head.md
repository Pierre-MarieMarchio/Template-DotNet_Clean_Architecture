# p3-head : persona .NET sur develop 935d552a (paquets 1.2.0-dev)

Projet : `Acme.Head` (défauts : api, auth local, PostgreSQL, examples all), sous
`/tmp/claude-1000/-home-pmmarchio-code-Template-DotNet-Clean-Architecture/608f3f2d-3c02-4530-9e46-6f8e7e0e445b/scratchpad/personas/p3-head/work/Acme.Head`
(git local, 1 commit par étape). Journaux : `.../personas/p3-head/log/*.log`. `coverage` non lancé (non vérifié).

## 1. cleanarch.json
Écrit à la génération : template (paquet + version), name, options (auth, database, examples, files, ci, sonar,
agentFiles), client, presentation (tableau `[{name, framework}]`). Lecteurs : launcher (`Taken from Acme.Head: ...`),
`Tools/Tasks.cs` (options, web-join, presentation-join), `NewFeature.cs`/`NewUseCase.cs` (nom), `ProjectFileTests`,
et `dotnet cleanarch` (qui plante dessus, voir 2). Enregistre désormais chaque ajout : `web` (scope, apps, packages) après
web-app Portal, `presentation[]` après Mcp (git diff dans le projet).
Non enregistrés : une feature (manifeste `.feature-manifest/` à part).

## 2. dotnet cleanarch
Découverte : aucune instruction. 0 occurrence de `dotnet cleanarch`/`CleanArch.Cli` dans le projet généré, le README
GitHub develop, `docs/template/using-the-template.md`, les messages de `dnx ... new`. `.config/dotnet-tools.json`
n'épingle que dotnet-ef et dotnet-sonarscanner. Seule trace : roadmap/ADR 0017-0019 (`docs/template/`) et le README du
nupkg ("run as `dotnet cleanarch`"). `dotnet cleanarch` : "Could not execute because the specified command or file was
not found". Installé en outil local : `dotnet tool install CleanArch.Cli --version 1.2.0-dev` (manifeste local, commande
`dotnet-cleanarch`).
Commandes : AUCUNE sous-commande. `--help`/`-h`/`-?` et `--version` seulement (`1.2.0-dev+935d552a...`).
Sans argument dans un projet : devrait afficher "<nom>, generated from <paquet> <version>, at <chemin>" ; sur le projet
généré par défaut il échoue : `cleanarch.json is not a project file: The JSON value could not be converted to
CleanArch.Cli.PresentationProject. Path: $.presentation` (exit 1). Cause : le modèle du CLI (`ProjectFile.cs`) attend
`Presentation` objet unique, le fichier écrit un tableau (docs et ProjectFileTests disent tableau). Avec `presentation`
retiré ou objet unique : affiche la ligne d'identité. Hors projet : "Not inside a CleanArch project: no cleanarch.json in
... or any folder above it." Toute autre commande (`add`, `new-feature`, `info`, `tasks`) : "Unrecognized command".
Donc aujourd'hui : un localisateur de projet, cassé sur un projet généré normal. ADR 0019/roadmap #163 annoncent
`bootstrap`, `add web-app|web-package|presentation`, `adopt` : rien n'existe.

## 3. Tâches courtes
(a) new-feature Projects Project (aggregate, etag), 0,65 s, 52 fichiers + manifeste + page. Imprime toujours 6 éditions
(AppDbContext, ApplicationModule, PersistenceModule, Program, HostComposition, ApplicationModuleTests) + TestDatabase,
migration non lancée, `openapi` non cité, 2 lignes de doc (README, api.md). Aucune option `--fields`; `Description` dans
23 fichiers / 47 lignes (grep). Fait à la main : 6 éditions + TestDatabase (48 lignes), migration-add AddProjects (3,8 s),
openapi, 2 lignes doc. Build 10,9 s 0 erreur du premier coup. `verify` 1 : rouge sur `dotnet format` (IMPORTS, ordre de
mes usings, fichier PersistenceModule.cs(1,1)); `format-fix` puis `verify` 2 : vert, 3773 tests, 3 min 27. Budget/Name
non réécrit (non fait, mesuré par grep).
(b) Web app : `dnx CleanArch.Create --yes -- web-app -n Portal --framework vite-react --yes` 2,7 s : déduit les options, crée
l'app, lance web-join lui-même, `cleanarch.json` enregistre l'app. Reste 10 éditions manuelles imprimées (compose include,
.env.example, .dockerignore, dependabot, codeql, 5 lignes de docs), plus `web-scaffold Portal` (11,8 s). Un seul test les
contrôle maintenant : `ContainerImagePinningTests.EveryWebApp_IsIncludedInTheRootComposeFile` rouge sans l'include; avec
include + .env seuls, 173/173 tests d'archi verts : dependabot x2, codeql, .dockerignore, 5 docs restent sans garde-fou.
MCP : `dnx ... -- presentation -n Mcp --framework mcp --yes` 26,5 s : join (~45 éditions "Applied"), scaffold, ajout sln,
format, `cleanarch.json` enregistre Mcp. Reste à la main : `dotnet ef migrations add AddAccessKeys --project ...Auth
--startup-project ...Auth --output-dir Migrations` (imprimée en haut, absente de "Next:"), `openapi` (v1.json modifié).
Piège migration-add : TOUJOURS là. `Tasks.cs migration-add AddAccessKeys` : migration VIDE (Up/Down vides) dans
Persistence, aucun avertissement (`Migrations/..._AddAccessKeys.cs`, supprimée ensuite). Après ef Auth + openapi :
`verify` vert, 3867 tests, 4 min 48; `verify` lance `has-pending-model-changes` pour Persistence ET Auth (`t3b-verify.log:132,134`).
Worker : `presentation -n Worker --framework worker` -> `--framework can't be 'worker'. Use one of blazor-server,
avalonia, mcp, wpf.` Non ajoutable.
(c) remove-feature Projects : intact (0 edited) mais enregistrée : rapport seul (18 registrations, 4 EF, 9 migrations,
10 usages, 169 "possibles" dont `<Project>`/`Project(` du .sln), `--apply` refusé. Après édition de Project.cs : "1 edited",
mêmes bloqueurs, `error: Projects was not removed, and nothing was deleted`. Seul un squelette neuf non enregistré
(Widgets) : `--apply` l'a supprimé. Inchangé.

## 4. Tableau final

| friction | 5f22faa5 | 935d552a | preuve |
|---|---|---|---|
| `dotnet cleanarch` introuvable, aucune piste | introuvable | paquet CleanArch.Cli existe mais aucune doc/message n'en parle; non épinglé dans dotnet-tools.json; install manuelle depuis le flux | grep 0 occurrence; `t2-cli.log`; `dotnet tool install CleanArch.Cli` |
| ce que fait `dotnet cleanarch` | n/a | squelette : --help, --version, identité du projet; AUCUNE commande utile; plante sur un cleanarch.json par défaut | `t2-cli.log`, `t3-cli-final.log` : `Path: $.presentation` |
| cleanarch.json enregistre-t-il génération et ajouts | non | corrigé : génération, web (apps), presentation | git diff cleanarch.json après web-app et Mcp |
| new-feature imprime 6 éditions + migration au lieu de les faire | oui | inchangé (même texte, `openapi` toujours non cité) | `t3a-newfeature.log` |
| placeholders Name/Description dans 22 fichiers, pas de --fields | 22-23 fichiers | inchangé, 23 fichiers / 47 lignes | grep Description sur les .cs générés; `--help` de NewFeature.cs |
| `new-feature --help` affiche l'aide de dotnet run | oui | inchangé | `t3a-help.log` |
| première app web : ~11 éditions manuelles, web-join inutilisable | 10 éditions, web-join "nothing to join" | amélioré : le launcher lance web-join, cleanarch.json enregistré, 1 des 10 éditions contrôlée par un test; 10 éditions encore imprimées à la main | `t3b-webapp.log`; test d'archi rouge/vert |
| édits web non contrôlés (dependabot, codeql, docs...) | 7 sur 10 | inchangé (archi verts sans elles) | `dotnet test --project Tests/Architecture` après include + .env |
| verify ne couvre pas le web (pnpm lint/test/build, schema.ts) | oui | inchangé | `t3b-verify.log` : 0 `pnpm` |
| host MCP : join automatique | oui, 83 fichiers | inchangé, plus enregistré dans cleanarch.json | `t3b-mcp.log` |
| migration Auth après MCP imprimée en haut, pas dans Next: | oui | inchangé | `t3b-mcp.log:7` |
| migration-add crée une migration vide dans le mauvais contexte | oui | inchangé, aucun avertissement, pas d'option de contexte | `t3b-migadd.log`, Up() vide |
| worker non ajoutable après coup | oui | inchangé, message d'erreur clair mais refus | `t3b-worker.log` |
| remove-feature inutilisable dès qu'une feature est éditée (ou enregistrée) | oui | inchangé; même un squelette intact devient rapport seul dès qu'on l'enregistre; 169 possibles, bruit | `t3c-remove-*.log` |
| `dnx --yes` consommé par dnx (`--yes` avant et après `--`) | oui | inchangé | `t3-dnxyes.log` |
| git non initialisé, `--setup` non cité en fin de `new` | oui | inchangé (`--setup` documenté seulement dans --help) | `t1-gen.log`, `t1-help.log` |
| double BOM dans README du MCP | oui | inchangé, verify vert | `xxd` : `efbbbfefbbbf` |
| coverage | plante (PAL_SEHException) | non lancé (consigne) | non vérifié |

## 5. Commandes de `dotnet cleanarch` (1.2.0-dev+935d552a)
- `dotnet cleanarch` : localise cleanarch.json (dossier courant ou parent) et imprime `<name>, generated from <package> <version>, at <path>`; hors projet : message "Not inside a CleanArch project"; plante sur un cleanarch.json généré par défaut (presentation tableau).
- `dotnet cleanarch --help | -h | -?` : description, usage `dotnet cleanarch [options]`, options seulement.
- `dotnet cleanarch --version` : `1.2.0-dev+935d552a9bdf9a08a42babdebee924d0514521c0`.
- Aucune sous-commande (add, bootstrap, adopt, new-feature : "Unrecognized command").

## 6. Non vérifié
`coverage`; `dotnet cleanarch` sous Windows; `--setup` du launcher; blazor-server/avalonia/wpf; swap Budget (non fait); échec de PendingModelChangesTests sans migration Auth (non reproduit, seul le passage vert est prouvé); `end-to-end`.
