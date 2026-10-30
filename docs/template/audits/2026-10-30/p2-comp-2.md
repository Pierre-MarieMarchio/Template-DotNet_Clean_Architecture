# p2-comp-2b : jasontaylordev/CleanArchitecture 10.8.0, persona "developpeur .NET competent", angle nuget.org

Statut : PARTIEL. Installation, generation et lecture du projet genere faites ; AUCUN build, test ni lancement n'a pu etre execute (voir "Blocage").

## Blocage (limite d'environnement, pas du template)

A 14:2x, la premiere commande `dotnet build` (projet `def`) a ete refusee par le classifieur de permissions du mode auto :
`Permission for this action was denied by the Claude Code auto mode classifier. Reason: [Code from External].`
Je n'ai pas contourne le refus (pas de `dotnet test`/`dotnet run` a la place, pas d'autre outil). Consequence : les etapes 3 (build, tests verts, requete HTTP) sont NON MESUREES pour defaut, minimale et riche. Les constats ci-dessous sont donc statiques. Decision a prendre par le proprietaire : ajouter une regle de permission Bash pour `dotnet build/test/run` dans le dossier scratchpad du persona, puis relancer.
Dossier de travail : /tmp/claude-1000/-home-pmmarchio-code-Template-DotNet-Clean-Architecture/608f3f2d-3c02-4530-9e46-6f8e7e0e445b/scratchpad/personas/p2-comp-2b/work (def, min, rich ; aucun bin/obj cree). Journal : .../p2-comp-2b/log/journal.txt.

## Tableau etape -> duree -> resultat

| Etape | Duree | Resultat | Etapes manuelles | Prerequis decouverts |
|---|---|---|---|---|
| Lire la page nuget.org | ~quelques s | OK : commande d'install, 3 commandes de generation, prerequis (SDK .NET 10, Node LTS pour front), lancement `cd src/AppHost; dotnet run` | aucune | Node uniquement si front ; Docker jamais cite sur la page |
| `dotnet new install Clean.Architecture.Solution.Template` (sans version) | 2,3 s | OK, installe 10.8.0, deux templates `ca-sln`, `ca-usecase` | aucune | aucun |
| `dotnet new ca-sln --help` / `ca-usecase --help` | <1 s | OK, options `-cf Angular/React/None`, `-db postgresql/sqlite/sqlserver` | aucune | Aspire n'est PAS une option : toujours present |
| Generer defaut (Angular + sqlite) | 1,7 s | OK | aucune | aucun |
| Generer minimale (`-cf None`) | 2,4 s | OK | aucune | aucun |
| Generer riche (`-cf React -db postgresql`) | 4,7 s | OK (j'ai pris React ; Angular est le defaut) | aucune | aucun |
| Build defaut / minimale / riche | n/a | REFUSE par permission (environnement) | n/a | n/a |
| Tests | n/a | non execute | n/a | n/a |
| Requete HTTP reelle | n/a | non execute | n/a | n/a |
| `ca-usecase --dry-run` dans `src/Application` | <1 s | OK : `Create: ./Foos/Queries/GetFoo/GetFoo.cs` | cd manuel dans src/Application | aucun |

## Points forts (avec preuve)

1. Installation et generation sans accroc, tres rapides. Preuve : `Success: Clean.Architecture.Solution.Template@10.8.0 installed the following templates` en 2,26 s ; trois generations en 1,7 / 2,4 / 4,7 s.
2. La page nuget.org est autonome : install, trois variantes de generation, lancement, scaffolding de use case, base de donnees par defaut, URL Scalar (`/scalar`). C'est ce que je cherchais.
3. `--help` clair, valeurs et defauts listes (`Default: Angular`, `Default: sqlite`).
4. Le generateur de use case existe et est documente dans le README genere comme sur nuget.org ; il propose meme la reparation du message "No templates or subcommands found matching: 'ca-usecase'".
5. Page Architecture du site : reponse directe a "ou mettre mon code" : Domain = entites/regles, Application = commandes/requetes/handlers/validateurs, Infrastructure = implementations, `src/Web` = endpoints minimal API. Corroboree par le projet genere : `src/Application/TodoItems/Commands/CreateTodoItem/CreateTodoItem*.cs` (commande + handler + validateur) et `src/Web/Endpoints/TodoItems.cs` (groupe d'endpoints `IEndpointGroup`). Un exemple Todo concret montre le patron.
6. Le minimal (`-cf None`) est reellement plus petit : pas de `src/Web/ClientApp`, pas de `tests/Web.AcceptanceTests`, AppHost sans `AddJavaScriptApp` (min/src/AppHost/Program.cs).
7. Le README genere est court et couvre Build / Run / Test / Scaffolding.

## Frictions, du pire au moindre

1. (Bloquant pour mon parcours, mais environnement) build refuse par le classifieur ; non attribuable au template.
2. Docker : jamais mentionne ni sur nuget.org, ni dans le README genere, ni dans `--help`, alors que la variante riche (`rich/src/AppHost/Program.cs`) utilise `AddAzurePostgresFlexibleServer(...).RunAsContainer(...)` : un conteneur PostgreSQL sera demande au lancement. Je n'ai pu verifier l'echec en l'absence de Docker qu'en lecture ; a confirmer par un vrai run. Le devcontainer, lui, inclut docker-in-docker. Prerequis non dit (probable).
3. README genere : commande de lancement avec antislash Windows `dotnet run --project .\src\AppHost` (aussi sur la page d'accueil du site). Sous Linux/WSL, `.\src\AppHost` n'est pas un chemin valide en bash tel quel ; la page nuget.org, elle, donne `cd src/AppHost; dotnet run`. Incoherence entre deux sources.
4. Lancement : la page dit "Aspire dashboard will open automatically" ; en WSL/agent sans navigateur ce n'est pas verifiable ; le README ne dit pas ou trouver l'URL de l'API ni comment faire une requete reelle sans le dashboard. Les URLs ne sont donnees que par le dashboard ; `src/Web/Web.http` suppose `https://localhost:7088` et un login Identity par formulaire + jeton anti-forgery (`administrator@localhost` / `Administrator1!`), procedure lourde pour une premiere requete. Les endpoints Todo exigent `RequireAuthorization()` (src/Web/Endpoints/TodoItems.cs:11), donc pas de `curl` simple sur eux ; `WeatherForecasts` pourrait servir a tester, non verifie.
5. Generation par defaut : sqlite, mais l'AppHost, meme en sqlite et sans front, appelle `builder.AddAzureContainerAppEnvironment("aca-env")` (min/src/AppHost/Program.cs:5) : dependance Azure presente dans un demarrage local, intention peu claire pour un premier usage. Sans build je ne sais pas si ca gene.
6. `global.json` fige le SDK a `10.0.201` avec `rollForward: latestFeature` ; compatible ici avec 10.0.301, mais un SDK 10.0.1xx ferait echouer : le message n'est pas verifie, la page nuget dit seulement "latest version".
7. `ca-usecase` ne genere qu'un seul fichier (`GetFoo.cs`), sans validateur ni endpoint ni test ; l'endpoint se branche a la main dans `src/Web/Endpoints`. La page nuget dit `Run dotnet new ca-usecase from ./src/Application` (chemin a la Unix) mais il faut deviner le `-fn` (feature) pour que le dossier soit bien place ; le dry-run montre le resultat correct.
8. Le projet n'est pas un depot git (`git init` non fait) ; pas de CI generee (`ls -a` : pas de `.github`) ; le premier commit est a ta charge. Mineur.
9. Bruit au premier `dotnet new install` avec un `DOTNET_CLI_HOME` neuf : dizaines de lignes "Writing workload pack installation record..." et message de bienvenue/telemetrie. Limite d'environnement (home isole), pas du template.
10. Tests "unit, integration and functional" annonces ; la suite complete inclut Playwright (tests/Web.AcceptanceTests, absent en minimal) : `dotnet test` global exigera vraisemblablement navigateurs + Docker pour la variante front ; le README dit seulement `dotnet test`. Non verifie.

## Ou mettre mon premier code metier, la doc l'apprend-elle ?

Oui, en grande partie. La page Architecture le dit en une phrase par couche (Domain/Application/Infrastructure/Web), et le projet genere contient un exemple complet (Todo) a copier : entite dans `src/Domain/Entities`, cas d'usage dans `src/Application/<Feature>/Commands|Queries/<UseCase>/`, endpoint dans `src/Web/Endpoints/<Feature>.cs`, DbSet dans `IApplicationDbContext`. Manque : un pas-a-pas unique "ajouter une entite de bout en bout" (entite + DbSet + configuration EF + use case + endpoint + test), et la regle d'enregistrement automatique des endpoints (`MapEndpoints(typeof(Program).Assembly)`) n'est pas expliquee dans le README ; je l'ai devinee depuis `Program.cs`.

## Non verifie (a refaire quand le build est autorise)

Build defaut / minimale / riche et message d'erreur eventuel ; `dotnet test` (defaut, minimale) ; lancement AppHost, requete HTTP reelle (`/scalar`, `/openapi/v1.json`, endpoint public) ; comportement sans Docker pour la variante postgresql ; durees de restore/build ; installation npm du front.

## Sources utilisees

- https://www.nuget.org/packages/Clean.Architecture.Solution.Template (WebFetch)
- https://cleanarchitecture.jasontaylor.dev/ et /docs/architecture (WebFetch)
- `dotnet new ca-sln --help`, `dotnet new ca-usecase --help`, `dotnet new ca-usecase ... --dry-run`
- Fichiers generes sous .../p2-comp-2b/work/{def,min,rich} (README.md, global.json, src/AppHost/Program.cs, src/Web/Program.cs, src/Web/Endpoints/TodoItems.cs, src/Web/Web.http)
