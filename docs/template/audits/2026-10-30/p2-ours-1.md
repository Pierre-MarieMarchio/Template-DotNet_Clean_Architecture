# Persona p2-ours-1 : developpeur .NET, premier usage, angle README GitHub + launcher

Version testee : CleanArch.WebApi.Templates / CleanArch.Create 1.1.0-dev (flux local, suffixe -dev = artefact d'audit, pas une friction).
Machine : WSL2, .NET SDK 10.0.301, Docker 29.6.2, Node 24.18, pnpm 11.28.2 (sans jq). Home .NET isole (premier lancement sur home neuf : bruit "Welcome to .NET" et workloads MAUI = limite d'environnement).
Sources utilisees : README GitHub, docs/template/using-the-template.md (gh api GET), `--help`, messages du terminal, fichiers des projets generes.

Projets generes (un par methode x configuration quand utile) :
| Projet | Methode | Configuration |
|---|---|---|
| Acme.Guided | launcher guide (pty) | defauts |
| Acme.NonInt | launcher `-- new --yes` | defauts |
| Acme.DnDefault | dotnet new | defauts |
| Acme.LMin / Acme.DnMin | launcher non interactif / dotnet new | --auth none --examples none --files false |
| Acme.LRich / Acme.DnRich | launcher non interactif / dotnet new | --web vite-react + presentation api, blazor-server, worker, mcp |
| Acme.GRich | launcher guide (pty), setup complet | riche |
Mutualisation declaree : build/tests/appli ont ete menes sur Acme.Guided (defaut), Acme.LMin (minimale), Acme.LRich (riche). Les arbres Acme.NonInt == Acme.DnDefault et Acme.DnMin == Acme.LMin (hors GUID du .sln) ont ete compares par script : launcher et dotnet new generent le meme projet.

# 1. Journal horodate complet

12:05 README lu via gh api (quick start: dnx CleanArch.Create; prerequis annonces: SDK 10.0.100+, Docker Compose v2)
12:05:36 `dnx CleanArch.Create@1.1.0-dev --help` -> affiche l'aide de dnx (pas celle de l'outil). Bruit "Welcome to .NET 10" + workloads MAUI "Writing workload pack installation record..." (premier lancement sur home neuf: limite d'env).
12:05:46 `dnx CleanArch.Create@1.1.0-dev -y -- --help` -> aide de l'outil OK (1.3s). Il faut `--` pour atteindre --help de l'outil; le README ne le dit pas.
12:05:50 lu docs/template/using-the-template.md sur GitHub (gh api): decrit le launcher, options, refus. Constat: section "From nuget.org" dupliquee dans ce doc (Install and uninstall); table README sans --database.
12:06:06 2a (launcher guide, defauts) `dnx CleanArch.Create@1.1.0-dev -y` sous pty (script -qc, entrees par pipe: nom puis Entree x30). TUI pilotable. Questions: nom, dossier, presentation (multi), web, auth, database, examples, files, ci, sonar, agent-files, "Set up the project now?" (multi: first commit/bootstrap/bootstrap commit/copy .env, tout coche par defaut), recap, "Generate Acme.Guided?". 14 ecrans. Les defauts suffisent (Entree partout); chaque question explique ses choix.
12:07 installe CleanArch.WebApi.Templates 1.1.0-dev (auto), genere Acme.Guided.
12:07 "▲ First commit skipped: git doesn't know who you are yet: user.name and user.email aren't set. git config --global user.name ... " (limite d'env: pas de gitconfig dans mon bac a sable; message clair, dit quoi faire).
12:07-12:08:38 "Running bootstrap, this takes a few minutes" : mon `timeout 150` a tue le tout pendant bootstrap (mon erreur, pas du template). Bootstrap a refaire a la main. Pas de .env ni commit.
12:08:51 Acme.Guided contient: README.md (Run it: bootstrap, cp .env, docker compose up), AGENTS.md, docs/, Src/{Application,Domain,Infrastructure,Presentation}. Le README du projet dit "You need the .NET SDK global.json pins and Docker with Compose v2".
12:09:12 2b (launcher non interactif defauts) `dnx CleanArch.Create@1.1.0-dev new -n Acme.NonInt --yes </dev/null` -> ECHEC 1.8s: "There's no terminal to ask questions in, and these weren't given: --presentation, --web, --auth, --database, --examples, --files, --ci, --sonar, --agent-files. Pass them as options, or add --yes to use the defaults."  (alors que --yes etait passe: dnx avale --yes/-y pour lui; l'exemple de la doc `dnx CleanArch.Create new -n Acme.Shop ... --yes` ne marche donc pas tel quel; il faut `dnx CleanArch.Create@... -- new -n ... --yes`). Message trompeur: me dit d'ajouter ce que j'ai deja mis.
12:09:21 retry `dnx CleanArch.Create@1.1.0-dev -- new -n Acme.NonInt --yes </dev/null` -> OK en 13.5s (install template deja fait, genere, imprime "Done. The same with dotnet new: dotnet new cleanarch-webapi -n Acme.NonInt -o Acme.NonInt", puis Next: bootstrap, cp .env, docker compose up, table des adresses).
12:09 note: sans --setup, bootstrap n'est pas lance; instruction "Manual instructions: In the project folder, run dotnet run Tools/Tasks.cs bootstrap ..." clair.
12:09:52 2c `dotnet new list cleanarch` : 4 templates listes (webapi, presentation, webapp, webpackage). `dotnet new cleanarch-webapi --help` : options claires; NB `--database` liste `mysql` ("MySQL 8.4...") alors que le launcher/doc disent que mysql n'est pas genere par cette version (refus --my-sql-not-yet-generated).
12:10:00 `dotnet new cleanarch-webapi -n Acme.DnDefault` OK 12.8s. Post-action: "Bootstrap the project before anything else ... needs the network, not Docker." (message utile).
12:10:13 `... -n Acme.DnMin --auth none --examples none --files false` OK 5.5s.
12:10:18 `... -n Acme.DnRich --web vite-react --presentation api --presentation blazor-server --presentation worker --presentation mcp` OK 10.7s. Messages: "Bootstrap also creates what each scaffold.json describes", "installs the web workspace... needs Node.js and pnpm: run corepack enable once per machine."
12:10:42 2b `dnx ... -- new -n Acme.LMin --auth none --yes </dev/null` OK 11.9s. Le launcher deduit seul `--examples none --files false` ("Files module: No (the only option when Authentication is none)") et imprime la commande dotnet new equivalente. Bien.
12:10:54 2b `dnx ... -- new -n Acme.LRich --web vite-react --presentation api --presentation blazor-server --presentation worker --presentation mcp --yes` OK 19.2s (accepte api+worker; "Web app rendering: spa (the only option when First web app is vite-react)"). Next: mentionne Node/pnpm (corepack enable) et le port 5173.
12:09:06 `dotnet run Tools/Tasks.cs bootstrap` sur Acme.Guided (lance en fond, en parallele de 5 generations donc CPU partage) -> 12:11:33 'bootstrap' completed, 2m27s (user 7m16). Sortie: dotnet format, build Development (openapi), tool restore (dotnet-ef 10.0.14, sonarscanner), 2x `dotnet ef migrations add InitialCreate`. Dernier message: "Formatting stable, OpenAPI reference written, tools restored and each context's initial migration in its Migrations folder. Commit this before anything else". Fichiers nouveaux: 2 Migrations/ + OpenApi/v1.json.
12:12:34 `dotnet run Tools/Tasks.cs test --no-integration` (Acme.Guided) -> 12:13:45 OK 1m10s, 14 suites, 3257 tests, 0 failed, 0 skipped, 0 warning. Aucun besoin de Docker ni .env.
12:14:04 `flock docker.lock bash run-stack.sh Acme.Guided`: attente du verrou 1m47 (autre agent), puis 12:15:51 `docker compose up -d --build --wait` -> 38.7s (images deja en cache pour postgres/mailpit/seaweed? build api + 4 conteneurs healthy). .env copie (cp .env.example .env).
12:16:30 requetes reelles: GET / -> 401 application/problem+json {"code":"auth.required"} (l'API "repond" mais la racine est 401: la doc ne dit pas quoi appeler pour constater que ca vit); GET /health -> 200 (decouvert en devinant, /healthz /alive /health/live /api/v1/health = 401); GET /scalar/v1 -> 200; /openapi/v1.json -> 200 (openapi 3.1.1); mailpit 8025 -> 200; POST /api/v1/auth/register -> 202 {"status":"pending"}; mail recu dans Mailpit (total 1). compose down -v OK 12:16:36. jq absent sur ma machine (la doc first-run en depend pour le login, avec repli manuel documente).
12:16:47 Acme.LMin (auth none via launcher): `dotnet run Tools/Tasks.cs bootstrap` -> 12:17:37 OK 50.5s. Arbre propre apres (git add; "nothing to commit" -> rien de nouveau a committer? voir note), puis `test --no-integration` 12:17:43 -> 12:18:22 OK 39s, 11 suites, 798 tests verts; 2 suites "Zero tests ran" (Application.UnitTests, Domain.UnitTests: normal sans exemples), pas d'echec.
12:18:36 Acme.LRich (launcher non interactif, riche: vite-react + api+blazor-server+worker+mcp) `dotnet run Tools/Tasks.cs bootstrap` -> 12:21:53 OK 3m17s (user 8m18). Il cree Mcp, Portal (dotnet new blazor, hive temporaire), Worker, l'app Site (pnpm dlx create-vite@9.3.1), pnpm install x2, client TS genere (schema.ts). Dernier message: "Created Site. Commit the app with Src/Web/pnpm-lock.yaml ..." puis "Formatting stable ...". [WARN] pnpm: "The store at /home/pmmarchio/.local/share/pnpm/store/v11 is not used because packages cannot be hard linked from it into this project. Using the store at /tmp/.pnpm-store/v11 instead" (limite d'env: store sur un autre FS).
12:22:06 Acme.LRich `test --no-integration` -> 12:28:31 OK 6m25s elapsed (user 1m59: le temps est de l'attente), 17 suites, tous verts. Suites lentes: Acme.LRich.Mcp.UnitTests 2m56s, Acme.LRich.Portal.UnitTests 1m52s, les autres 1-4s. Charge machine elevee (load avg 8.7: autres agents) donc a confirmer par un second passage.
12:22 `git add -A` apres bootstrap: dizaines de "warning: in the working copy of '...Portal/Components/*.razor', CRLF will be replaced by LF the next time Git touches it" et "Acme.LRich.sln, LF will be replaced by CRLF" : fichiers ecrits par dotnet new blazor en CRLF, non normalises par bootstrap. Bruit sans blocage.
12:29 Constat doc: README du projet riche liste API/Scalar/Mailpit/Web app, mais pas le Portal (8090) ni le MCP (8091/mcp) que docs/getting-started/first-run.md liste. 
12:28:58 Acme.LRich stack: `flock docker.lock bash run-stack-rich.sh` (verrou obtenu tout de suite) `docker compose up -d --build --wait` -> 1m44s; 8 conteneurs healthy (api, db, mailpit, objectstore, worker, presentation-mcp, presentation-portal, web-site). Requetes reelles 12:30:43: API /health 200, /scalar/v1 200, Portal :8090/ 200 (HTML Blazor), MCP :8091/health 200, GET :8091/mcp 405, POST initialize sans jeton -> 401 "WWW-Authenticate: Bearer" (normal: besoin d'une cle d'acces), Web :5173/ 200 (React Vite servi par nginx), POST :5173/api/v1/auth/register via le relais nginx -> 202, mailpit 200. Worker: logs "Sweep of ... completed". NB: le service compose du portal s'appelle presentation-portal (pas portal) -> `docker compose logs portal` = "no such service". `cp -n` warning (mon script). compose down -v OK 12:30:47.
12:33 Comparaison d'arbres (script python, noms normalises, hors Migrations/.env/OpenApi): Acme.NonInt (launcher) == Acme.DnDefault (dotnet new): seul le .sln differe (GUID); Acme.DnMin == Acme.LMin idem. => launcher et dotnet new generent le meme projet; seule l'experience change. (Acme.Guided differe de DnDefault par les usings reformates par bootstrap.)
12:33:01 2a-riche: launcher guide pilote par un driver pty (python): presentation (espace sur blazor-server, mcp, worker; api verrouille ■), web=vite-react, puis Entree partout. Ecrans: "Web app name"(Site), "Web app audience", "Web app rendering: spa · the only option when First web app is vite-react" (reponse deduite, affichee), auth, database, examples, files, ci, sonar, agent, setup, recap, confirmation. Recap en tableau lisible. 12:33:2x projet Acme.GRich genere, "First commit done" (avec GIT_CONFIG_GLOBAL fourni par moi), puis bootstrap lance par le launcher.
12:33:06+ NB driver: titre "Presentation projects" defile ("…") a l'ecran, ma regex l'a rate (limite de mon pilote, pas du template).
12:34:07 Etape 4: `dotnet run Tools/Tasks.cs new-feature Widgets Widget --access owner --deletion delete` (Acme.Guided) -> 2.5s, ecrit 30 fichiers (5 couches + tests + manifeste + page docs/features/widgets.md), imprime 6 edits a la main, la ligne docs/README.md, la ligne docs/reference/api.md, et dit "The solution does not compile until edit 1 is made". Tres explicite.
12:34:39 Application a la main des 6 edits imprimes par new-feature (script python), puis `format-fix` (58s). 12:35:42 `test --no-integration` -> 12:37:15: 3 echecs sur 14 suites: (1) ErrorCodeTableTests: "widget.notFound (WidgetErrors.cs) ... every code the API can answer has its row in docs/reference/api.md", (2) PendingModelChangesTests: "the model has changed since the last migration... run 'dotnet run Tools/Tasks.cs migration-add <Name>'", (3) ErrorCodeStatusTests. Les messages disent quoi faire; la ligne api.md etait annoncee par new-feature ("And the row of its refusal under ### Resources in docs/reference/api.md") -> mon oubli, rattrape par le message de test.
12:37:30 ajout de la ligne api.md, `migration-add AddWidgets` (7s), `openapi`; 12:37:46 test --no-integration -> 12:38:44: 14/14 suites vertes (57s). Boucle "premier code metier" validee: ~4 min avec 6 edits manuels.
12:33-12:38:04 Acme.GRich (launcher guide, riche) : le launcher a fait lui-meme git init + First commit, bootstrap (~4 min), Bootstrap commit, copie de .env; a fini par "Next time, skip the questions with: dnx CleanArch.Create new -n Acme.GRich -o ./Acme.GRich --presentation api --presentation blazor-server --presentation mcp --presentation worker --web vite-react --yes --setup" + la commande dotnet new equivalente + "cd ./Acme.GRich ; docker compose up -d --build --wait" + table des adresses.
12:39:01 FRICTION CONFIRMEE: lancer exactement cette commande suggeree (`dnx CleanArch.Create@1.1.0-dev new -n Acme.Hint -o ./Acme.Hint --yes`) sous pty rouvre le TUI de questions (ecran Presentation projects) au lieu de prendre les defauts: dnx avale --yes. Avec `-- new ... --yes` ca genere sans question. L'astuce "skip the questions" imprimee par l'outil est donc fausse via dnx.
12:39:43 Rejeu isole, charge machine basse (load 0.3): `dotnet test Acme.LRich.Mcp.UnitTests` -> 26 tests, 2m54s; `Acme.LRich.Portal.UnitTests` -> 15 tests, 1m51s. Reproductible: pas un effet de contention. Les autres suites: 1-4s.
12:47 "Disk full: /tmp ... 0 MB free" (tmpfs 16G partage, 100%): limite d'environnement (agents en parallele). J'ai supprime bin/obj/node_modules de mes propres projets (3.7G -> 129M) pour continuer. Pour info: un seul projet riche construit pese >1 Go avec bin/obj/node_modules.
12:50 Fin des essais. Non verifie: connexion MCP avec une cle d'acces (POST /api/v1/auth/access-keys), login Portal, test des variantes --web angular/blazor-wasm, --database sqlserver/sqlite, nuget.org reel (version servie par flux local), end-to-end/verify/coverage (hors parcours premier usage).

# 2. Mesures

## Tableau etape -> duree -> resultat

| Etape | Duree | Resultat | Etapes manuelles | Prerequis decouverts |
|---|---|---|---|---|
| Lire le README, trouver `dnx CleanArch.Create` | ~1 min | OK | 0 | SDK 10.0.100+, Docker Compose v2 (annonces des le Quick start) |
| Installer le template | 0 (fait par le launcher: "installs CleanArch.WebApi.Templates at its own version") | OK | 0 | - |
| `dnx ... -- --help` | 1.3 s | OK, mais `dnx ... --help` montre l'aide de dnx | 1 (decouvrir `--`) | - |
| 2a guide, defauts (Acme.Guided) | ~1 min de questions + generation (14 ecrans, Entree partout) | OK | 1 (le nom) | pty pour mon pilote (limite d'agent) ; identite git (message clair) |
| 2b non interactif, defauts | 1.8 s en echec (--yes avale par dnx) puis 13.5 s avec `-- new --yes` | echec puis OK | 1 (decouvrir `--`) | - |
| 2c `dotnet new cleanarch-webapi` defauts | 12.8 s | OK | 0 | - |
| 2c minimal / riche (dotnet new) | 5.5 s / 10.7 s | OK | 0 | - |
| 2b minimal / riche (launcher) | 11.9 s / 19.2 s | OK | 0 | - |
| Bootstrap defaut (Acme.Guided) | 2m27 (en parallele de 5 generations) | OK | 1 | reseau |
| Bootstrap minimal (Acme.LMin) | 50 s | OK | 1 | reseau |
| Bootstrap riche (Acme.LRich) | 3m17 | OK | 1 | reseau, Node, pnpm (corepack enable annonce; mon pnpm etait deja la) |
| Bootstrap via launcher guide riche (Acme.GRich), incl. git init, 2 commits, .env | ~5 min total de bout en bout | OK | 0 apres les questions | identite git |
| `test --no-integration` defaut | 1m10 (14 suites, 3257 tests, 0 echec) | OK | 0 | rien (ni Docker ni .env) |
| `test --no-integration` minimal | 39 s (11 suites, 798 tests, 2 suites "Zero tests ran") | OK | 0 | rien |
| `test --no-integration` riche | 6m25 (17 suites, 0 echec) dont Mcp.UnitTests 2m56 et Portal.UnitTests 1m52 | OK, lent | 0 | rien |
| `docker compose up -d --build --wait` defaut | 38.7 s (apres 1m47 d'attente du verrou Docker, autre agent) | OK, 4 conteneurs healthy | 1 (cp .env.example .env) | Docker Compose v2, ports 8080/8025/5432/9000 libres |
| Requete reelle defaut | immediat | `/health` 200, `/scalar/v1` 200, `POST /api/v1/auth/register` 202, mail dans Mailpit | 0 | curl ; jq absent (la doc propose un repli) |
| `docker compose up` riche | 1m44 | OK, 8 conteneurs healthy | 1 | ports 8090, 8091, 5173 en plus |
| Requetes reelles riche | immediat | API /health 200, Portal 200, MCP /health 200 et POST sans cle 401 Bearer, Web 5173 200, inscription via le relais nginx 202 | 0 | - |
| `new-feature Widgets Widget` + 6 edits manuels + format-fix + migration-add + openapi + tests | 2.5 s + ~3 min d'edition + 58 s + 7 s + ~1m de tests | OK apres 1 aller-retour (ligne api.md oubliee, signalee par un test) | 6 edits + 1 ligne api.md | - |

Nombre d'etapes manuelles du chemin le plus court (README -> API qui repond) : 4 commandes (dnx, cd, bootstrap, cp .env, docker compose up = 5 avec le cd).

## Ou je mettrais mon premier code metier, et la doc me l'a-t-elle appris ?

Oui. Je ferais `dotnet run Tools/Tasks.cs new-feature <Features> <Feature> --access owner|shared` : il ecrit le squelette dans les couches (`Src/Application/<P>.Application/Features/<F>`, `Src/Infrastructure/<P>.Infrastructure.Persistence/Features/<F>`, `Src/Presentation/<P>.Api/Features/<F>`, tests miroirs) et, pour un etat a proteger, je choisirais `--model aggregate` (domaine dans `Src/Domain/<P>.Domain/Features/<F>`). Preuve : la sortie de new-feature liste ces chemins et les 6 edits ; `docs/getting-started/first-run.md` (section "Add a feature") et `docs/guides/adding-a-feature.md` (sections Domain / Application / Persistence / API / Tests, "closed lists") le disent. La suite vert apres les edits et la ligne `api.md`.

# 3. Frictions, du pire au moindre

## F1 (la pire, doc + outil) : `--yes` est avale par `dnx`, les exemples de la doc et l'astuce du launcher ne fonctionnent pas
- Doc : `docs/template/using-the-template.md:68-70` : `dnx CleanArch.Create new -n Acme.Shop --auth external --examples notes --yes`.
- Le launcher imprime lui-meme a la fin du mode guide : `Next time, skip the questions with: dnx CleanArch.Create new -n Acme.GRich -o ./Acme.GRich --presentation api --presentation blazor-server --presentation mcp --presentation worker --web vite-react --yes --setup`.
- Sans terminal (12:09:12) : `dnx CleanArch.Create@1.1.0-dev new -n Acme.NonInt --yes </dev/null` -> `There's no terminal to ask questions in, and these weren't given: --presentation, --web, --auth, --database, ... Pass them as options, or add --yes to use the defaults.` : le message demande d'ajouter ce que j'avais deja mis.
- Avec un terminal (12:39:01) : la meme commande rouvre le TUI de questions au lieu de prendre les defauts.
- Contournement trouve : `dnx CleanArch.Create@1.1.0-dev -- new -n Acme.NonInt --yes` -> OK en 13.5 s (le `--` n'est dit nulle part dans le README ni la doc).
- Impact : le parcours non interactif (CI, scripts, "skip the questions") echoue de facon trompeuse. A corriger dans la doc ET dans le texte imprime par l'outil, ou a rendre l'outil insensible au vol de `--yes`.
- Mon `-y` (dnx) etait necessaire a ma premiere commande de test ; sans `--`, `dnx ... --help` affiche l'aide de dnx (12:05:36), pas celle de l'outil.

## F2 : `test --no-integration` du projet riche prend 6m25 contre 1m10 ; deux suites attendent presque 5 minutes
- `Acme.LRich.Mcp.UnitTests` : 26 tests, `duration: 2m 54s 858ms` ; `Acme.LRich.Portal.UnitTests` : 15 tests, `duration: 1m 51s 524ms` (rejeu isole a 12:39:43, charge machine 0.3, donc pas de la contention). Toutes les autres suites : 1 a 4 s. user CPU total 1m59 pour 6m25 reels : le temps est de l'attente, pas du calcul.
- Les suites creent une `WebApplicationFactory<Program>` par test (`Tests/Presentation/Acme.LRich.Portal.UnitTests/PresentationHost.cs`). Cause exacte non identifiee (probable attente de ressources absentes sans Docker ; non verifie).
- La doc vante la commande comme "while you work" sans Docker ; rien n'annonce cette lenteur (`docs/guides/testing.md` ne la mentionne pas).

## F3 : le premier lancement de l'API ne dit pas quoi appeler pour voir qu'elle vit
- Le README dit "The API answers on http://localhost:8080". `GET /` -> `401 application/problem+json {"code":"auth.required"}` (12:16:30). `/health` -> 200 (je l'ai trouve en essayant 5 chemins ; `/healthz`, `/alive`, `/health/live`, `/api/v1/health` -> 401). `/health` n'est documente que dans `docs/reference/api.md:14` et `docs/features/observability.md`, pas dans first-run.

## F4 : README du projet riche et first-run ne sont pas d'accord sur les services
- `README.md` du projet liste API, Scalar, Mailpit, Web app ; `docs/getting-started/first-run.md` ajoute Portal 8090 et MCP 8091/mcp. Le service compose du portail s'appelle `presentation-portal`, `docker compose logs portal` -> `no such service: portal`.

## F5 : bruit git apres bootstrap du projet riche
- `git add -A` : des dizaines de `warning: in the working copy of 'Src/Presentation/Acme.LRich.Portal/Components/App.razor', CRLF will be replaced by LF the next time Git touches it` et `Acme.LRich.sln, LF will be replaced by CRLF` (fichiers de `dotnet new blazor` ecrits en CRLF, non normalises). Sans blocage.

## F6 : doc/outil incoherents sur MySQL et sur "API or worker"
- `dotnet new cleanarch-webapi --help` liste `mysql  MySQL 8.4: a table prefix per feature...` alors que le launcher et la doc disent qu'il est refuse (`--my-sql-not-yet-generated`).
- Le README GitHub (lignes 77-78) dit "a project holds the API or the worker" ; `--presentation api --presentation worker` est pourtant accepte et genere les deux (le worker prend alors les jobs). Le README ne dit pas ce que "holds ... or" veut dire ; la doc generee (`docs/features/background-jobs.md`) est, elle, claire.

## F7 : doc du template, section d'installation dupliquee
- `docs/template/using-the-template.md:81-84` et `:99-103` donnent deux fois `dotnet new install CleanArch.WebApi.Templates` / `dotnet new update` sous deux intitules "From nuget.org".

## F8 mineures
- Le README GitHub n'a pas de ligne `--database` dans sa table d'options (elle est dans using-the-template.md et dans `--help`).
- "Zero tests ran" pour `Application.UnitTests` et `Domain.UnitTests` du projet minimal : normal, mais non explique dans la sortie.
- Identite git absente : le launcher dit `First commit skipped: git doesn't know who you are yet: user.name and user.email aren't set. git config --global user.name "<your name>" ...` : clair et actionnable (limite d'environnement chez moi, pas une friction).
- `/tmp` plein (tmpfs 16G partage) pendant la session : limite d'environnement ; un projet riche construit pese >1 Go (bin/obj/node_modules).

# 4. Points forts (prouves)

- Le mode guide marche de bout en bout sous pty : 14 ecrans, chaque choix decrit, reponses deduites annoncees ("Web app rendering: spa · the only option when First web app is vite-react", "Files module: No (the only option when Authentication is none)"), recapitulatif en tableau, puis commit + bootstrap + commit + `.env` faits par l'outil (Acme.GRich : `git log` = `bbac4b6 chore: bootstrap`, `9f67bbb chore: generate ...`, `.env` present).
- Chaque generation imprime la commande `dotnet new` equivalente et les prochaines etapes avec la table des adresses.
- Launcher et `dotnet new` produisent des arbres identiques (comparaison par script, hors GUID du .sln).
- Bootstrap fiable : defaut 2m27, minimal 50 s, riche 3m17, tous OK, dernier message actionnable ("Commit this before anything else").
- Defaut : 3257 tests verts sans Docker ; minimal : 798 verts ; riche : 17 suites vertes. Les stacks defaut (4 conteneurs) et riche (8 conteneurs) sont `healthy` et repondent (inscription 202, mail dans Mailpit, relais nginx 202, Portal 200).
- `new-feature` : 2.5 s, 30 fichiers, 6 edits listees, et les tests d'architecture refusent un etat incomplet avec un message qui dit quoi faire (`widget.notFound ... has its row in docs/reference/api.md`, `run ... migration-add <Name>`).

# 5. Non verifie

- Connexion MCP avec une cle d'acces, connexion au Portal ; variantes `--web angular|blazor-wasm|react-router`, `--database sqlserver|sqlite`, `avalonia|wpf`.
- Cause exacte de la lenteur des suites Mcp et Portal.
- Page nuget.org reelle (version servie par flux local), `verify`, `coverage`, `end-to-end` (hors parcours premier usage).
- Jobs de fond en doublon quand API et worker sont tous deux generes (non inspecte).
