# Persona p2-ours-2 : développeur .NET qui découvre le template (angle nuget.org, dotnet new, --help)

Version testée : CleanArch.WebApi.Templates / CleanArch.Create 1.1.0-dev (flux local). Environnement : WSL2, .NET SDK 10.0.301, Docker, Node 24, pnpm 11.28.2.
Projets générés sous le dossier work : un projet par configuration et par voie (dotnet new, launcher non interactif, launcher guidé).

## Limite d'environnement (à ne pas compter comme friction du template)
/tmp est un tmpfs de 16 Go partagé avec les autres agents ; il a été plein (0 Mo libre) pendant la majeure partie de l'exécution. Conséquences : trois essais de `test` sur la config riche ont avorté ou été relancés, `dnx` a échoué une fois à écrire dans /tmp, et j'ai dû supprimer mes projets déjà testés (docs archivées hors tmpfs) pour continuer. Le chemin « launcher guidé avec setup complet » n'a donc pas pu être vérifié jusqu'au bout.

## Journal horodaté (extraits fidèles)
12:21:54 nuget page fetched (1.0.0 only, README shows --web react/angular, no --presentation); GitHub README shows more options
12:21:36 env check: dotnet 10.0.301, docker, node 24, pnpm present
12:21:48 nuget page fetched via WebFetch: shows only 1.0.0, quick start dnx CleanArch.Create; options list --web none|placeholder|react|angular, no --presentation (1.0.0 page; version artefact). GitHub README (develop) lists more options.
12:21:54 dotnet new install CleanArch.WebApi.Templates::1.1.0-dev -> OK <1s; prints "The colon separator :: has been deprecated in favor of @" (audit syntax); noisy workload-pack lines on fresh DOTNET_CLI_HOME (env artefact). 4 templates installed (webapi, presentation, webapp, webpackage).
12:21:59 dotnet new list cleanarch / --help: option help is complete, with choices and descriptions.
12:22:08 dotnet new cleanarch-webapi -n Acme.Shop -o DnDefault: OK 9s. Post-action message: "Bootstrap the project before anything else ... run dotnet run Tools/Tasks.cs bootstrap and commit what it writes". Folder is not a git repo, no hint to git init (grep 'git init' in generated docs: no hit).
12:22:28 bootstrap default: OK, 1m58s (6m18 CPU). Ends "Formatting stable, OpenAPI reference written, tools restored and each context's initial migration ... Commit this before anything else".
12:24:33 test --no-integration: OK 1m04s: 209+160+337+47+... all passed.
12:25:44 docker compose up -d --build --wait (images cached): OK 35s; API 8080 healthy. GET / -> 401 problem+json auth.required; /scalar/v1 200; mailpit 200; POST register 202; /me no token 401.
12:28:57 test min failed: tmpfs /tmp 100% full (16G shared by agents), 'No space left on device' - environment limit; freed DnDefault bin/obj
12:30:17 min gen 5s, bootstrap 1m03, test --no-integration 1m09 (retry after ENOSPC) 798 tests pass
12:30:43 gen rich OK 10s (api+worker+blazor+mcp accepted, README says 'a project holds the API or the worker')
12:33:59 bootstrap rich OK 3m11 (pnpm store warning: env)
12:40:33 test rich 1st run: ENOSPC (/tmp tmpfs full, shared), partial total 3245 pass; freed bin/obj; retry
12:47:09 test rich 2nd run ENOSPC again (other agents fill /tmp 16G); removed DnDefault/DnMin dirs (docs tarred), 3rd attempt
12:53:39 test rich OK 6m19 (-m:2, slow because shared disk) 3358 tests, 0 failed
12:53:39 rich compose up -d --build --wait: OK 2m05 (images partly cached). 8 services healthy; GET /health/ready 200, scalar 200, portal :8090 200, mcp :8091/mcp GET 405 (streamable http, expected), web :5173 200 serves Vite page, :5173/api/health/ready 401 (relay to API, protected route), mailpit 200, register 202, worker logs sweeps. compose down -v OK.
12:59:25 launcher guided via 'script -qc' with piped Enters: TUI drives fine; wizard 10 questions + setup steps (First commit, Bootstrap, Bootstrap commit, Copy .env) + summary + confirm. First commit skipped: git user.name unset (clear message). Bootstrap failed in ~20s: dotnet format crash stack only (disk /tmp full: probable cause, root cause not shown by launcher)
13:01:10 bootstrap LGuided manual retry: ENOSPC at restore => the guided failure was disk (env), launcher showed only a dotnet format stack tail
13:04:38 launcher non-interactive: default 10s, min 6s, rich 10s; prints 'The same with dotnet new: ...'; no git init in ni mode; launcher auto-lowers examples/files under --auth none (message explains); no-tty without --yes: clear error; dotnet new --auth none alone: 'Missing mandatory option(s) ... --auth-none-needs-examples-none' (obscure) and empty dir left; sqlite+worker same style. Output identical to dotnet new except sln GUIDs
13:05:12 new-feature Orders Order on un-bootstrapped default: OK 5s, prints 6 edits, migration advice, docs rows; explains pre-bootstrap case
13:05:12 launcher guidé n°2 (setup par défaut) : mêmes résultats que n°1 : « ▲ First commit skipped / git doesn't know who you are yet: user.name and user.email aren't set. », puis « ▲ Bootstrap failed ... 'dotnet format .../Acme.Shop.sln' exited with 1. » (disque plein à ce moment). Variables GIT_AUTHOR_* / GIT_COMMITTER_* ignorées par le test d'identité du launcher.
13:06-13:09 attente d'espace disque (4 Mo à 90 Mo libres) ; abandon des essais de build restants.

## Mesures

| Étape | Durée | Résultat | Étapes manuelles | Prérequis découverts |
|---|---|---|---|---|
| Lire la page nuget.org (WebFetch, résumé) | ~1 min | OK : quick start `dnx CleanArch.Create`, install `dotnet new install`, prérequis .NET 10.0.100 + Docker Compose v2 | 0 | la page ne montre que 1.0.0 et ses options (react, angular), sans `--presentation` ni `--setup` : normal pour 1.0.0 |
| `dotnet new install` | <1 s | OK, 4 gabarits listés (webapi, presentation, webapp, webpackage) | 1 | réseau/flux |
| `dotnet new list` / `--help` | instantané | OK, aide complète (valeurs, défauts, descriptions) | 0 | |
| DEFAUT `dotnet new cleanarch-webapi` | 9 s | OK | 1 | |
| DEFAUT bootstrap | 1 min 58 | OK (cache NuGet vide) | 1 | réseau ; pas Docker |
| DEFAUT `test --no-integration` | 1 min 04 | OK, 3257 tests (somme des lignes « total: »), 0 échec | 1 | |
| DEFAUT `docker compose up -d --build --wait` | 35 s (images en cache) | OK, 4 services sains ; register 202, Scalar 200, Mailpit 200, `/me` sans jeton 401 | 2 (cp .env, up) | Docker, ports 8080/8025/5432/9000 libres |
| MINIMALE (`--auth none --examples none --files false`) génération | 5 s | OK | 1 | |
| MINIMALE bootstrap | 1 min 03 | OK | 1 | |
| MINIMALE `test --no-integration` | 1 min 09 (2e essai ; 1er essai : disque plein) | OK, 798 tests, 0 échec | 1 | |
| RICHE (`--web vite-react` + 4 `--presentation`) génération | 10 s | OK ; accepte api + worker ensemble | 1 | |
| RICHE bootstrap | 3 min 11 | OK (blazor, mcp, worker, vite, pnpm, client TS) | 1 | Node + pnpm, réseau |
| RICHE `test --no-integration -m:2` | 6 min 19 (après 2 essais avortés pour disque plein) | OK, 3358 tests, 0 échec | 1 | |
| RICHE compose up --build --wait | 2 min 05 | OK, 8 services ; /health/ready 200, Scalar 200, portail :8090 200, MCP :8091/mcp GET 405, web :5173 200 (page Vite), :5173/api/... 401, Mailpit 200, register 202, journaux du worker | 2 | Docker, ports 8090/8091/5173 |
| Launcher non interactif défaut / minimale / riche | 10 s / 6 s / 10 s | OK, affiche l'équivalent `dotnet new`; arbre identique à `dotnet new` (seuls les GUID du .sln diffèrent) | 1 | |
| Launcher guidé (via `script -qc` + Entrée) | ~27 s | questionnaire jusqu'au bout, mais bootstrap en échec (disque plein) | | |
| `new-feature Orders Order` (sans bootstrap) | 5 s | OK, six éditions imprimées, conseil migration | 1 | |

Nombre d'étapes manuelles pour une appli qui répond (défaut) : generate, cd, bootstrap, cp .env, compose up = 5 (+ git init non documenté).

## Où mettre le premier code métier
Dans `Src/Domain`, `Src/Application`, `Src/Infrastructure/...Persistence` et `Src/Presentation` par la commande `dotnet run Tools/Tasks.cs new-feature <Features> <Feature> --access owner|shared`, puis les éditions imprimées. La doc l'apprend : `docs/getting-started/first-run.md` (section « Add a feature »), `docs/guides/adding-a-feature.md` (forme, « What you write by hand » par couche), `docs/architecture/feature-shapes.md`. Oui, la doc suffit à répondre, y compris avant bootstrap (la sortie de `new-feature` dit quoi faire).

## Frictions, du pire au moindre

1. [important] Échec de bootstrap dans le launcher : la cause réelle est masquée. Le launcher affiche « ▲ Bootstrap failed / dotnet run Tools/Tasks.cs bootstrap exited with code 1 » puis la queue d'une pile `Microsoft.CodeAnalysis.Tools.CodeFormatter.OpenMSBuildWorkspaceAsync ... 'dotnet format ...sln' exited with 1`. La cause (ENOSPC) n'apparaît qu'en relançant `dotnet run Tools/Tasks.cs bootstrap` à la main : « NuGet.targets(198,5): error : No space left on device ». Ici le déclencheur est l'environnement, mais le message ne dit ni où est le journal complet ni quoi faire (sinon relancer bootstrap seul). Preuve : /home/pmmarchio/.cache-p2-ours-2/guided.clean lignes « Bootstrap failed », et ../boot-lguided.log.
2. [important, à confirmer par l'équipe] Créé par `dotnet new`, le dossier n'est pas un dépôt git (« fatal: not a git repository »), alors que le message de fin demande « commit what it writes » et que README/first-run répètent « commit ». Aucun `git init` dans la doc générée (grep « git init » : aucune occurrence). Seul le launcher fait le `git init` (« First commit ... after a git init if the folder is not a repo »). Le chemin `dotnet new` laisse le novice deviner.
3. [mineur] Combinaison refusée via `dotnet new` : `dotnet new cleanarch-webapi -n X --auth none` répond « Missing mandatory option(s) for the template '.NET Clean Architecture API': '--auth-none-needs-examples-none', '--auth-none-needs-files-false'. » (code 102). Le message est un identifiant interne, sans la phrase « ajoute --examples none --files false » ; un dossier vide est laissé (Bad1). Même style pour `--database sqlite --presentation worker` : « '--worker-needs-server-database' ». Contrepoints : `--help` et la page nuget disent la règle ; le launcher corrige tout seul (« Example features: none (the only option when Authentication is none) »).
4. [mineur] README du dépôt : « a project holds the API or the worker ». Or `--presentation api --presentation worker` est accepté et génère les deux hôtes ; l'overview dit « A worker beside it runs the background jobs, which the API leaves to it ». Le sens de la phrase (qui exécute les jobs) est ambigu pour un lecteur qui croit à un refus.
5. [mineur] Le launcher teste `git config user.name` et ignore `GIT_AUTHOR_*`/`GIT_COMMITTER_*` : « First commit skipped / git doesn't know who you are yet ». Le message dit bien quoi faire (les commandes `git config --global`).
6. [mineur] Les trois fonctionnalités d'exemple (TodoLists, Reminders, Notes) n'ont pas de page sous docs/features (accounts, authentication, background-jobs, email, files, observability, presentation, web-frontend seulement), alors que le README promet « one page per feature » et que les pages d'architecture les citent (ex. docs/getting-started/overview.md, docs/architecture/feature-shapes.md). Preuve : `ls docs/features` et docs/README.md sans ligne pour elles, sur la config riche (examples all).
7. [info] Coût disque et temps : après bootstrap + tests, défaut ~1,1 Go, minimale ~0,76 Go, riche ~1,5 Go hors node_modules ; bootstrap 1 à 3 min ; test --no-integration 1 à 6 min. À signaler dans les prérequis (le template dit réseau et Docker, pas l'espace disque).
8. [info] `dotnet new install ...::1.1.0-dev` affiche « The colon separator "::" has been deprecated in favor of "@" » : artefact de la consigne d'audit, pas du template. Les lignes « Writing workload pack installation record » viennent de mon DOTNET_CLI_HOME neuf (environnement).
9. [info] pnpm : « [WARN] The store at /home/pmmarchio/.local/share/pnpm/store/v11 is not used because packages cannot be hard linked ... Using the store at /tmp/.pnpm-store/v11 » : conséquence de /tmp en tmpfs (environnement). Non vérifié : `corepack enable` (pnpm déjà présent).
10. [info] Sur le défaut, `GET /` répond 401 problem+json `auth.required` : normal (default-deny) mais la doc première exécution passe directement à register/confirm/login ; pour voir « l'appli répond » sans compte, `/health/ready` (200) n'est cité que dans la variante sans authentification.

## Points forts prouvés
- Parcours de bout en bout sans erreur de template : défaut, minimale et riche génèrent, bootstrap, passent leurs tests unitaires (209+160+337+47+... = 3257 / 798 / 3358, 0 échec) et la pile Compose devient saine (défaut 4 services, riche 8) avec requêtes HTTP réelles.
- Le launcher non interactif et `dotnet new` produisent le même arbre (`diff -rq` : seul le .sln diffère, par ses GUID), et le launcher imprime la commande `dotnet new` équivalente.
- `--help` détaillé : chaque choix a sa description ; une valeur invalide (`--web react`) liste toutes les valeurs valides avec leur description.
- Sans terminal et sans `--yes`, le launcher dit : « There's no terminal to ask questions in, and these weren't given: --presentation, --web, ... Pass them as options, or add --yes ».
- Le guidé se pilote avec `script -qc` et des Entrée par pipe : 10 questions, récapitulatif, confirmation, étapes de setup proposées (First commit, Bootstrap, Bootstrap commit, Copy .env) ; il affiche la commande « Next time, skip the questions with dnx CleanArch.Create new -n Acme.Shop -o ... --yes --setup ».
- `new-feature` fonctionne avant bootstrap et explique le cas (« Before bootstrap has run, there is nothing to add to: the initial migration bootstrap creates takes the feature in »).

## Non vérifié
- Tests d'intégration (Testcontainers), `verify`, `coverage`, `end-to-end` : non lancés (durée et disque).
- Launcher guidé jusqu'au setup complet réussi (commit, bootstrap, commit) : limite d'environnement (disque plein), pas friction.
- Appel d'un outil MCP, connexion au portail, appel authentifié via le relais web : non faits. `corepack enable` : non testé.
- Temps à froid de la construction des images Docker (images déjà en cache).
- Page nuget.org lue via WebFetch (résumé, pas texte brut) ; elle montre 1.0.0 seulement.
