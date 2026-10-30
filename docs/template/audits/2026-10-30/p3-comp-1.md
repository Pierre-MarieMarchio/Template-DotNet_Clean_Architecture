# Persona p3-comp-1 : développeur .NET découvre jasontaylordev/CleanArchitecture 10.8.0 (options par défaut)

Projet généré : `ca-sln -o Acme` (Angular + SQLite). Journal brut : `/tmp/claude-1000/-home-pmmarchio-code-Template-DotNet-Clean-Architecture/608f3f2d-3c02-4530-9e46-6f8e7e0e445b/scratchpad/personas/p3-comp-1/work/journal.txt`. Projet : `.../personas/p3-comp-1/work/Acme` (un commit par tâche).
Les durées sont du temps agent (frappe quasi instantanée) : elles mesurent les erreurs et les allers-retours, pas la lecture humaine.
Hors sources autorisées, donc non consulté : le site cleanarchitecture.jasontaylor.dev (le README y renvoie). Le dépôt n'a dans docs/ que 3 ADR (`gh api .../contents/docs`). Le projet généré n'embarque aucune doc : un README de 60 lignes.

## 1. Découverte (angle : `dotnet new list`, `--help`, README)
- `dotnet new install Clean.Architecture.Solution.Template` : 1,6 s, OK. Sortie bruitée par des lignes « workload pack installation record » (propres à mon poste, pas au template).
- `dotnet new list` expose 2 templates : `ca-sln` et `ca-usecase`. Rien d'autre (pas de feature, pas de worker, pas de remove, pas de web-app).
- `ca-sln --help` : seulement `--client-framework` (Angular/React/None) et `--database`. Le choix est figé à la génération.
- `ca-usecase --help` : options `-fn`, `-ut`, `-rt`, `-pn`. Elle ne dit pas qu'il faut être dans `src/Application`. Seul le README le dit. Depuis la racine, `--dry-run` annonce `Create: ./Bars/Commands/Foo/Foo.cs` : un dossier mal placé, sans erreur. Sans `-ut`, l'erreur « Missing mandatory option » est claire.
- README du projet : build, run (`dotnet run --project .\src\AppHost`, chemin Windows), scaffolding `ca-usecase`, `dotnet test`. Il ne dit rien de : migrations, ajout d'une entité, Playwright, événements, réglages, retrait d'un exemple.

## 2. Premier contact (gate initial)
| Étape | Résultat |
|---|---|
| `dotnet build` (12:49:23, 5,6 s) | ÉCHEC : 120 erreurs NU1902/NU1903 (`TreatWarningsAsErrors` + audit NuGet : MessagePack 2.5.192, System.Security.Cryptography.Xml/Pkcs 10.0.1, SQLitePCLRaw.lib.e_sqlite3 2.1.11, OpenTelemetry 1.15.0, Microsoft.OpenApi 2.0.0). Le message ne dit pas quoi faire. |
| Contournement | `<WarningsNotAsErrors>NU1901;NU1902;NU1903;NU1904</WarningsNotAsErrors>` dans `Directory.Build.props` : build OK, 240 avertissements. Dépend de la date : le projet généré du jour ne compile pas. |
| `dotnet test` (40 s) | Unit 8 OK, Domain 6 OK, Functional 19 OK, Infrastructure.IntegrationTests 0 test (projet vide), Acceptance 6 ÉCHEC (Playwright : navigateurs absents). Le message dit `pwsh bin/Debug/netX/playwright.ps1 install` ; le README n'en parle pas. |
| Playwright | Mon Ubuntu 26.04 n'est pas supporté ; `libnspr4.so` manquant. Limite d'environnement : acceptance « non vérifié (limite de l'agent) ». |
| `dotnet test --project X` | `MSB1001: Unknown switch` (mode VSTest). Il faut passer le chemin en positionnel. Détail mineur, pas propre au template. |
| Prérequis non dits | Node (Aspire lance `npm` pour Angular), Docker seulement hors SQLite, pwsh + navigateurs pour l'acceptance. Le README ne liste que « .NET SDK ». `global.json` demande 10.0.201 (rollForward), OK ici. |

## 3. Tâches
Colonnes du tableau final : voir §5. Ici, le détail par tâche.

### T1 - Feature Project (Name, Budget, propriétaire) : CRUD + liste paginée + règle Budget >= 0 + test
- Ce que l'outil fait : 5 squelettes `ca-usecase` (Create, Update, Delete, Get, GetWithPagination), un fichier chacun (commande + validateur + handler). Ils sont sans BOM et le corps est `throw new NotImplementedException()`. Ils compilent, mais lèvent à l'exécution ; rien ne signale qu'il reste à les remplir. Les DTO (`-rt ProjectDto`, `-rt PaginatedList`) ne sont pas créés.
- Ce que l'outil ne fait pas (tout à la main) : entité `Project`, `Configuration`, `DbSet` dans `IApplicationDbContext` ET `ApplicationDbContext`, DTO + profil AutoMapper, `PaginatedList<T>` (absent du template ; `GetTodos` n'est pas paginé), endpoint (`IEndpointGroup`, découvert par réflexion : aucune liste à compléter), filtrage par propriétaire (`IUser.Id`, rien d'exemplaire dans le template : aucune entité n'y est possédée par un utilisateur), test.
- PIÈGE 1 : compilation cassée au 1er build (CS0234 `The type or namespace name 'TestAppHost' does not exist in the namespace 'Acme.Application.Projects'`). Le dossier de feature `Projects` crée le namespace `Acme.Application.Projects`, qui masque la classe générée par Aspire `Projects` utilisée dans `tests/Application.FunctionalTests/FunctionalTestSetup.cs` (`Projects.TestAppHost`). Même masquage avec un dossier de test `Projects`. Contournement : `global::Projects.TestAppHost`. Rien ne prévient ; toute feature nommée « Projects » le déclenche.
- « Test de bout en bout » : le template n'offre que des tests fonctionnels MediatR sur l'hôte `WebApiFactory` (pas d'appel HTTP). J'ai fait 4 tests (budget négatif, cycle complet propriétaire, isolation entre utilisateurs, anonyme). Le HTTP et l'Angular ne sont pas testés.
- Résultat : compile après 2 corrections, tests 23 OK. OpenAPI régénéré au build (`/api/Projects` présent dans `v1.json`).

### T2 - Use case dans une feature existante (Archive, puis Search par nom)
- Outil : 2 squelettes (`ca-usecase`). Tout le reste à la main : champ `IsArchived`, DTO, handlers, 2 routes dans `Projects.cs` (l'outil ne parle pas des endpoints), test.
- Compile du 1er coup, 24 tests OK. On peut se tromper sans signal : un squelette non rempli ou une route oubliée passent tous les deux la compilation.

### T3 - Champ `Description` + migration
- Le projet n'a AUCUNE migration et pas de `dotnet-ef` (ni manifeste d'outils, ni mention au README). J'ai dû savoir faire `dotnet new tool-manifest && dotnet tool install dotnet-ef` (10.0.14), puis `dotnet ef migrations add AddProjectDescription --project src/Infrastructure --startup-project src/Web` (5 s, marche). La migration est la toute première : elle contient tout le schéma.
- Incohérence silencieuse : `ApplicationDbContextInitialiser.InitialiseAsync` fait `EnsureDeletedAsync` puis `EnsureCreatedAsync` (`src/Infrastructure/Data/ApplicationDbContextInitialiser.cs:44-46`) à chaque démarrage en Development ; rien n'appelle `Migrate`. La migration n'est donc jamais appliquée. Et `RelationalEventId.PendingModelChangesWarning` est ignoré (`src/Infrastructure/DependencyInjection.cs`, `ConfigureWarnings`) : oublier la migration ne produit aucun signal, pas même un test.
- Le gate n'attrape pas une migration manquante (24 tests OK avec ou sans).
- Avertissement de licence MediatR (LuckyPenny) à chaque exécution d'outil : à connaître pour la production.

### T4 - Réglage (options validées et documentées)
- Aucun exemple d'options dans le template (pas de `IOptions`, ni `ValidateOnStart`). Tout à la main : `ProjectsOptions` + `IValidateOptions`, enregistrement dans `AddApplicationServices`, `appsettings.json` de Web, tableau dans le README, usage dans 2 validateurs, 2 tests (validateur ; budget au-delà du max).
- PIÈGE 2 : `tests/Application.UnitTests` n'a pas de `global using NUnit.Framework` (FunctionalTests l'a) : CS0246 `TestCase`. Détail, mais incohérent.
- Rien ne vérifie qu'un réglage est documenté. `ValidateOnStart` n'est pas couvert par un test (je ne l'ai pas démontré rouge). 25+11 tests OK.

### T5 - Domain event + réaction
- Pas d'outil. Ça marche par modèle d'exemple (`TodoItemCompletedEvent` + `LogTodoItemCompleted` + `TodoItem.Done`). Enregistrement automatique (MediatR scanne l'assembly) : aucune liste. `DispatchDomainEventsInterceptor` publie avant `SaveChanges`.
- 4 nouveaux fichiers, 1 modifié. Test : l'entité lève l'événement une fois (Domain) ; le handler loggue (Application, Moq). Je n'ai pas de test qui prouve la distribution via l'intercepteur (la réaction est un log). 7+12+25 OK.

### T6 - Seconde web app, puis autre host
- L'outillage n'offre ni web-app supplémentaire, ni worker, ni MCP, ni desktop. Les variantes Angular/React sont deux sorties distinctes de `ca-sln` et partagent le même AppHost.
- Web app : j'ai généré une solution React jetable, copié son `ClientApp` (29 fichiers) vers `src/Web/ClientApp-React` (le `.gitignore` généré cite déjà ce chemin), ajouté `Services.WebFrontendReact` et un `AddJavaScriptApp` dans `AppHost/Program.cs`. AppHost compile. Non lancé (disque saturé, npm). Conflit connu non traité : la publication de Web n'embarque que `dist\browser` (Angular).
- Host 2 : `dotnet new worker` (template du SDK, pas du template), puis réécriture du csproj (la `Version=` du modèle SDK est refusée par la gestion centralisée des packages), `slnx` via `dotnet sln add`, références Application/Infrastructure/ServiceDefaults, `IUser` à fournir (`WorkerUser`), AppHost (csproj + `AddProject<Projects.Worker>`) et `Services.Worker`.
- PIÈGE 3 : l'exécution seule du worker échoue au build du conteneur : `Unable to resolve service for type 'Microsoft.AspNetCore.Routing.EndpointDataSource' while attempting to activate 'AuthorizationPolicyCache'`. `AddInfrastructureServices` enregistre auth, Identity et cookies, propres au web, et n'est pas neutre. Contournement : `ValidateOnBuild = false`. Ensuite le worker démarre, et `no such table: Projects` prouve que seul Web crée la base.
- Aucun test ajouté pour ces deux hosts ; l'acceptation Playwright ne couvre que l'Angular.

### T7 - Retrait d'exemple (Weather), puis de ma feature
- Pas d'outil : `git rm` à la main + `grep -i weather`. 17 fichiers supprimés, 7 modifiés (Angular `app.module.ts`, `nav-menu`, `proxy.conf.js`, `Web.http`, + mes fichiers React). Le build et les tests passent. Rien n'avertit d'un reste : seul `grep` guide (le client TS est généré et ignoré par git ; `wwwroot/openapi/v1.json` garde `Weather` jusqu'au prochain build).
- Retrait de Project : 25 fichiers + 7 éditions (DbSet x2, DI, appsettings, README, Worker). Les migrations : `dotnet ef migrations remove` supprime proprement les 3 fichiers (bon outil, mais à connaître). Retour à la ligne de base 6+8+19 tests.
- TodoLists/TodoItems (le gros exemple : Domain, Config, tests, Angular, acceptation) : non retirés, estimés lourds.

### T8 - Casser une règle d'architecture
- Le projet n'a AUCUN test d'architecture (aucun NetArchTest/ArchUnit dans `Directory.Packages.props` ni `tests`).
- `dotnet add src/Domain reference src/Infrastructure` puis `dotnet build` : `NuGet.targets(1297,5): error MSB4006: There is a circular dependency in the target dependency graph involving target "_GenerateRestoreProjectPathWalk"`. Seul le cycle bloque, au restore, avec un message qui ne cite ni la règle ni le projet fautif. Même chose pour Domain -> Application.
- Variante : `dotnet add src/Domain package Microsoft.EntityFrameworkCore` : 0 erreur, 0 signal (Domain dépend d'EF Core). Le même constat vaut pour toute violation qui ne crée pas de cycle (Web vers Domain, Application vers un package). `docs/decisions/ADR-001` autorise EF dans Application ; rien n'impose le reste.
- Annulé par `git checkout`.

## 4. Frictions (ce qu'il faut savoir sans qu'aucun outil le dise)
1. Le projet généré du jour ne compile pas : audit NuGet transformé en erreur (120 erreurs) ; aucune consigne.
2. `ca-usecase` doit être lancé depuis `src/Application` (README seulement ; `--help` muet ; silencieux sinon).
3. Les squelettes `ca-usecase` ne compilent pas seuls pour les DTO (`-rt`) ; ils lèvent `NotImplementedException` à l'exécution ; pas de BOM ; ni endpoint, ni test, ni `DbSet`, ni validateur utile.
4. Nommer une feature `Projects` casse la compilation des tests fonctionnels (collision avec la classe Aspire `Projects`).
5. Pas de pagination dans le template (`PaginatedList` à écrire), pas d'exemple d'entité possédée ni d'options.
6. « Bout en bout » = test MediatR, pas HTTP, pas UI ; Playwright demande `pwsh playwright.ps1 install` + libs système.
7. Migrations : `dotnet-ef` non installé ni documenté ; projet sans migration ; `EnsureDeleted/EnsureCreated` ignore toute migration ; `PendingModelChangesWarning` ignoré : une migration oubliée est invisible.
8. Pas d'outil pour ajouter une web app ou un host. Pas de retrait de feature. `AddInfrastructureServices` n'est pas neutre pour un host non web.
9. Aucun test d'architecture ; seul un cycle de projets est détecté, avec un message MSB4006 obscur.
10. Pas de docs dans le projet : un README. Le détail se trouve sur un site externe.
11. Licence MediatR commerciale en production : avertissement permanent.
12. Environnement (pas le template) : tmpfs /tmp saturé à plusieurs reprises par les autres agents (`No space left on device`) ; une sortie d'erreur citait un chemin de cache NuGet d'un autre agent (cause inconnue) ; Playwright non supporté sur Ubuntu 26.04.

## 5. Tableau final
Temps agent. « Listes fermées » = enregistrements manuels qu'aucun test ne force (les listes auto-détectées par scan ne comptent pas). « Imprimées » : l'outil n'imprime jamais de pas à suivre.

| tâche | durée | fichiers créés par l'outil | fichiers modifiés à la main | lignes à la main | étapes seulement imprimées | listes fermées complétées à la main | compile du 1er coup | gate du 1er coup | tests obtenus |
|---|---|---|---|---|---|---|---|---|---|
| 0 génération + 1er gate | ~3 min | 141 (`ca-sln`) | 1 (`Directory.Build.props`) | 1 | 0 | 0 | non (120 erreurs NU190x) | non (acceptation KO, 0 test d'intégration) | 33 existants OK |
| 1 feature Project | ~2 min | 5 squelettes | 3 + 6 fichiers neufs + 5 squelettes remplis | ~380 | 0 | 2 (DbSet x2) | non (2 erreurs de namespace) | oui après correction | +4 (23 au total) |
| 2 Archive + Search | ~1 min | 2 | 4 + 2 remplis | ~110 | 0 | 0 (2 routes à la main) | oui | oui | +1 (24) |
| 3 champ + migration | <1 min (+ install de l'outil) | 3 migration (`dotnet ef`) + manifeste | 7 | ~14 | 0 | 0 | oui | oui, mais la migration n'est jamais appliquée | 0 (24) |
| 4 réglage | ~1 min | 0 | 6 (+2 neufs) | ~70 | 0 | 3 (DI, appsettings, README) | non (using NUnit) | oui | +3 (36) |
| 5 domain event | <1 min | 0 | 1 (+4 neufs) | ~70 | 0 | 0 | oui | oui | +2 (44) |
| 6 web app 2 + worker | ~3 min | 0 (copie de 29 fichiers d'une génération React ; `dotnet new worker` du SDK) | ~7 + ~8 neufs | ~90 | 0 | 4 (slnx, AppHost csproj, AppHost Program, Services) | non (CPM sur le worker résolu en réécrivant le csproj ; run KO sans `ValidateOnBuild=false`) | oui (build) ; run non vérifié | 0 |
| 7 retraits (Weather puis Project) | ~1,5 min | 3 supprimés par `ef migrations remove` | ~14 éditions + ~42 suppressions | ~25 | 0 | 5 (DbSet x2, DI, appsettings, README) | oui | oui | retour à 33 |
| 8 règle d'architecture | ~1 min | 0 | 1 (annulé) | 0 | 0 | 0 | non (MSB4006) ; variante package : oui | n/a : cycle = build rouge ; sinon aucun signal | 0 |

## 6. Parcours idéal vs réalité
- Génération : idéal, `dotnet new ca-sln` puis `dotnet test` vert. Réalité : build rouge (audit), acceptation rouge (Playwright), 0 test d'intégration.
- T1 : idéal, une commande `ca-usecase`/`new-feature` qui crée entité, DbSet, DTO, endpoint, tests. Réalité : 5 squelettes vides, ~380 lignes à la main, deux collisions de namespace.
- T2 : idéal, `ca-usecase` imprime la route à ajouter. Réalité : fichier vide, route et DTO à la main.
- T3 : idéal, `dotnet ef` installé et un message qui dit comment la base est créée. Réalité : installation manuelle, migration jamais appliquée, dérive muette.
- T4 : idéal, un exemple d'options validées et une table de réglages testée. Réalité : tout à zéro.
- T5 : bon, grâce à l'exemple et à l'enregistrement automatique.
- T6 : idéal, une commande pour un second front et un host. Réalité : recomposition manuelle ; `AddInfrastructureServices` non réutilisable hors web.
- T7 : idéal, un rapport de retrait (`remove-feature`). Réalité : `grep` + `git rm` ; `dotnet ef migrations remove` est la seule aide.
- T8 : idéal, un test d'architecture nommant la règle. Réalité : cycle MSB4006 ou rien.

## 7. Points forts (prouvés)
- `ca-usecase` donne un squelette cohérent avec la convention (dossier `Commands/<Nom>/`), rapide : 5 appels, aucun conflit.
- Découverte automatique des endpoints (`IEndpointGroup`), validateurs, handlers, profils AutoMapper, événements : ajouter un use case ne demande aucune liste. Preuve : `src/Application/DependencyInjection.cs` (scan) et T5 sans enregistrement.
- Tests fonctionnels rapides (19 tests en ~1 s) avec `TestApp`, `RunAsDefaultUserAsync`, reset de base : il a suffi d'écrire les cas.
- `dotnet ef migrations add/remove` marche du premier coup une fois l'outil installé.
- OpenAPI régénéré à chaque build (`/api/Projects` présent dans `wwwroot/openapi/v1.json`).

## 8. Non vérifié
- Tests d'acceptation Playwright (limite de l'agent : Ubuntu 26.04, `libnspr4.so` absent).
- Exécution d'AppHost complet avec les deux fronts (Angular, React) et le worker ; build Angular/React ; publication ; Docker (base SQLite uniquement).
- `ValidateOnStart` des options (aucun test rouge/vert démontré) ; distribution d'événement via l'intercepteur sur ProjectArchivedEvent.
- Retrait de TodoLists/TodoItems.
