# Carte du concurrent (JasonTaylorDev/CleanArchitecture), branche main

Racine : `/tmp/claude-1000/-home-pmmarchio-code-Template-DotNet-Clean-Architecture/608f3f2d-3c02-4530-9e46-6f8e7e0e445b/scratchpad/repos/competitor-main` (notée `main/`).
Comparaison : `.../repos/competitor-release` (tag v10.8.0, commit e5c08ec, 2026-03-24), noté `rel/`.
Méthode : lecture seule, aucun build, aucun test lancé. Tout ce qui suit vient de la lecture des fichiers et de `git`.
`main/` = `v10.8.0-58-g345e7fe` (commit 345e7fe, 2026-10-26), 976 commits, 257 fichiers suivis.

## 0. Résumé en dix lignes

- Template `dotnet new ca-sln` (id `Clean.Architecture.Solution.CSharp`) : une solution CQRS avec MediatR, un seul exemple (TodoLists / TodoItems / WeatherForecasts), organisée par feature dans Application.
- 3 options seulement : `--client-framework angular|react|none`, `--database sqlite|sqlserver|postgresql` (`.template.config/template.json:228-291`). Pas d'option d'authentification : Identity local est toujours là.
- Aucun test d'architecture, aucun analyzer, naming rules en `suggestion` seulement. Seule règle dure : `TreatWarningsAsErrors` (`Directory.Build.props:5`).
- Aucune pagination nulle part (ni main ni v10.8.0). Aucune propriété des données (pas de `OwnerId`) : tout utilisateur authentifié voit et modifie tout.
- Erreurs par exceptions (`ValidationException`, `NotFoundException` d'Ardalis, `ForbiddenAccessException`) traduites en ProblemDetails par un `IExceptionHandler`. `Result` n'existe que pour Identity.
- Domain events : levés dans le setter de `TodoItem.Done`, publiés AVANT le commit (`SavingChanges`), ce que l'ADR-003 contredit.
- Tests : 33 tests NUnit .NET (8 unitaires Application, 6 Domain, 19 fonctionnels), 6 scénarios Reqnroll/Playwright, 3 tests Angular (vitest), 0 test React. `Infrastructure.IntegrationTests` est vide. Aucune mesure de couverture (coverlet référencé, jamais exploité).
- Outillage : Aspire AppHost + TestAppHost, item template `ca-usecase`, scripts PowerShell dans `build/`. Pas de CLI propre, pas de lanceur, pas de Taskfile.
- Vie du projet : pas de CHANGELOG, notes de version dans les GitHub Releases (injectées dans le nuspec). Dernière release v10.8.0 le 2026-03-24 ; depuis, 58 commits dont 51 de dépendances.
- Delta main / v10.8.0 : surtout des bumps (Aspire 13.2 vers 13.6.4, nunit 4.5.1 vers 5.0.0, Angular 21 vers 22, vitest remplace karma), les 3 ADR, `.gitattributes`, `aspire.config.json`.

## 1. Projets, références, règles

### 1.1 Solution (`main/CleanArchitecture.slnx:1-29`)

`src/` : AppHost, ServiceDefaults, Application, Domain, Infrastructure, Shared, Web.
`tests/` : Application.FunctionalTests, TestAppHost, Application.UnitTests, Domain.UnitTests, Infrastructure.IntegrationTests, et Web.AcceptanceTests (conditionné par `<!--#if (!UseApiOnly)-->`, ligne 20).

### 1.2 Graphe de références (lu dans les `.csproj`)

| Projet | Références projet | Paquets notables |
|---|---|---|
| Domain (`src/Domain/Domain.csproj:8-10`) | aucune | `MediatR.Contracts` seul (ADR-003) |
| Application (`src/Application/Application.csproj:8-19`) | Domain | Ardalis.GuardClauses, AutoMapper, FluentValidation.DependencyInjectionExtensions, MediatR, Microsoft.EntityFrameworkCore, Microsoft.Extensions.Hosting |
| Infrastructure (`src/Infrastructure/Infrastructure.csproj:26-29`) | Application, Shared | EF Core, provider (Sqlite / Npgsql / SqlServer via Aspire), Identity.EntityFrameworkCore |
| Web (`src/Web/Web.csproj:13-17`) | Application, Infrastructure, ServiceDefaults | OpenApi, Scalar.AspNetCore, Identity, Azure KeyVault |
| Shared (`src/Shared/Shared.csproj`) | aucune | constantes de noms de services (`src/Shared/Services.cs:3-28`) |
| ServiceDefaults | aucune | OpenTelemetry, Http.Resilience, ServiceDiscovery |
| AppHost (`src/AppHost/AppHost.csproj:24-27`) | Shared (`IsAspireProjectResource=false`), Web | Aspire.Hosting.* |

Faits à relever :
- Application référence EF Core et l'assume (ADR-001, `docs/decisions/ADR-001-Use-EFCore-In-Application-Layer.md`) : pas de repository, `IApplicationDbContext` expose `DbSet<TodoList>`, `DbSet<TodoItem>` (`src/Application/Common/Interfaces/IApplicationDbContext.cs:5-12`).
- `Shared.csproj:8-12` redéclare `TargetFramework`, `ImplicitUsings`, `Nullable` déjà fournis par `Directory.Build.props:5-10` (redondance sans effet).
- Pas de `Directory.Build.targets`, pas de `.globalconfig`, pas de `AnalysisMode`, pas de `EnforceCodeStyleInBuild` (grep vide dans `*.props`/`*.csproj`).

### 1.3 Règles qui tiennent l'ensemble

- `Directory.Build.props:3-11` : net10.0, `TreatWarningsAsErrors=true`, `ImplicitUsings`, `Nullable`, `ArtifactsPath=artifacts`. Une exception : `WarningsNotAsErrors=NU1608` pour PostgreSQL (lignes 6-9).
- `Directory.Packages.props:4-5` : gestion centrale des versions + `CentralPackageTransitivePinningEnabled` (nouveau depuis v10.8.0).
- `.editorconfig` (382 lignes) : style et nommage, toutes les règles de nommage à la sévérité `suggestion` (`.editorconfig:196-268`). Aucun `dotnet_diagnostic.*.severity`. Une règle de nommage qui n'échoue jamais ne tient rien.
- Aucun test d'architecture : grep `NetArchTest|ArchUnit|architecture test` sans résultat dans tout le dépôt. Les dépendances inter-couches ne sont tenues que par le graphe `ProjectReference` (Domain ne peut pas voir Application, Application ne peut pas voir Infrastructure).
- Le graphe seul ne retient pas : Web référence Infrastructure et l'utilise directement (`src/Web/Endpoints/Users.cs:1,12` utilise `ApplicationUser`) ; les tests Application.UnitTests référencent Infrastructure (`tests/Application.UnitTests/Application.UnitTests.csproj:25-28`).
- `#pragma warning disable CS1998` présent dans `src/Application/WeatherForecasts/Queries/GetWeatherForecasts/GetWeatherForecastsQuery.cs:12-14` : l'avertissement est supprimé malgré `TreatWarningsAsErrors`.
- 3 ADR seulement (`docs/decisions/README.md`) : EF Core dans Application, Aspire, MediatR.Contracts dans Domain.

## 2. Chemin d'une requête

Pas de pagination dans le dépôt (grep `paginat|PageNumber|PageSize|Skip(` : aucun résultat dans `src`, `tests`, `templates`, `docs`, README, pour main comme pour v10.8.0). La « lecture paginée » demandée n'existe donc pas ; la plus proche est `GetTodos`, une lecture non paginée.

### 2.1 Écriture : `POST /api/TodoItems` (création d'un item)

1. Démarrage : `src/Web/Program.cs:7-12` appelle `AddServiceDefaults`, `AddKeyVaultIfConfigured`, `AddApplicationServices`, `AddInfrastructureServices`, `AddWebServices`. `app.MapEndpoints(typeof(Program).Assembly)` (ligne 45).
2. Découverte : `src/Web/Infrastructure/WebApplicationExtensions.cs:12-27` parcourt les types `IEndpointGroup` par réflexion ; préfixe `/api/{NomClasse}` (ligne 22), tag OpenAPI = nom de classe.
3. Endpoint : `src/Web/Endpoints/TodoItems.cs:9-19` (`RequireAuthorization()` au niveau du groupe, ligne 13). Handler `CreateTodoItem(ISender sender, CreateTodoItemCommand command)` (ligne 23) : `sender.Send(command)`, renvoie `TypedResults.Created($"/{nameof(TodoItems)}/{id}", id)` (ligne 27). Le nom de la méthode devient l'operationId (`EndpointRouteBuilderExtensions.cs:20-62`).
4. Pipeline MediatR, dans l'ordre d'enregistrement (`src/Application/DependencyInjection.cs:16-23`) : `LoggingBehaviour` (pré-processeur) puis `UnhandledExceptionBehaviour`, `AuthorizationBehaviour`, `ValidationBehaviour`, `PerformanceBehaviour`, puis le handler.
5. Validation : `CreateTodoItemCommandValidator` (`src/Application/TodoItems/Commands/CreateTodoItem/CreateTodoItemCommandValidator.cs:5-10`), `Title` non vide, 200 max. Échec : `ValidationException` (`ValidationBehaviour.cs:28-29`).
6. Handler : `CreateTodoItemCommandHandler.Handle` (`CreateTodoItem.cs:22-36`) : `new TodoItem{...}`, `_context.TodoItems.Add`, `SaveChangesAsync`, renvoie `entity.Id`. Pas de vérification que `ListId` existe (l'erreur vient de la clé étrangère en base).
7. `SaveChanges` : les deux `ISaveChangesInterceptor` enregistrés (`src/Infrastructure/DependencyInjection.cs:20-21`) : `AuditableEntityInterceptor` remplit `Created/CreatedBy/LastModified/LastModifiedBy` à partir de `IUser` et `TimeProvider` (`AuditableEntityInterceptor.cs:36-53`) ; `DispatchDomainEventsInterceptor` publie les événements.
8. Base : `ApplicationDbContext : IdentityDbContext<ApplicationUser>, IApplicationDbContext` (`src/Infrastructure/Data/ApplicationDbContext.cs:10-22`), configurations par `ApplyConfigurationsFromAssembly`. Le schéma vient de `EnsureDeletedAsync` + `EnsureCreatedAsync` (`ApplicationDbContextInitialiser.cs:45-46`), sans migrations (aucun dossier `Migrations`, `find` vide).
9. Retour : `Created<int>` avec l'identifiant. Erreurs : `ProblemDetailsExceptionHandler` (`src/Web/Infrastructure/ProblemDetailsExceptionHandler.cs:15-51`) : ValidationException 400, NotFoundException 404, UnauthorizedAccessException 401, ForbiddenAccessException 403.

### 2.2 Lecture : `GET /api/TodoLists` (non paginée)

1. `src/Web/Endpoints/TodoLists.cs:21-28` : `GetTodoLists(ISender)`, `sender.Send(new GetTodosQuery())`, `TypedResults.Ok(vm)`.
2. `GetTodosQuery` porte `[Authorize]` (`src/Application/TodoLists/Queries/GetTodos/GetTodos.cs:9`), le seul `[Authorize]` du dépôt (grep). `AuthorizationBehaviour` lit l'attribut par réflexion (`AuthorizationBehaviour.cs:24-26`), lève `UnauthorizedAccessException` si `_user.Id == null` (ligne 31), `ForbiddenAccessException` pour rôle ou policy manquants (lignes 55-72).
3. Handler `GetTodosQueryHandler.Handle` (`GetTodos.cs:23-49`) : énumère `PriorityLevel`, liste de `ColourDto` en dur, puis `_context.TodoLists.AsNoTracking().ProjectTo<TodoListDto>(...).OrderBy(Title).ToListAsync()`. Aucun filtre par utilisateur, aucun `Skip/Take`, tout est renvoyé.
4. DTO et mapping AutoMapper : `TodoListDto`, `TodoItemDto` (profil `Mapping : Profile` privé imbriqué dans le DTO, `TodoItemDto.cs:19-26`), `TodosVm` (`TodosVm.cs:5-18`).

## 3. Concepts

| Sujet | Réponse | Preuve |
|---|---|---|
| CQRS | Par convention de nommage et de dossier (`Commands/`, `Queries/`) ; commandes et requêtes sont toutes des `IRequest` MediatR sur la même base | `src/Application/TodoItems/Commands/...`, `.../TodoLists/Queries/GetTodos/` |
| Médiateur | Oui, MediatR 14.2.0 (licence commerciale chez l'éditeur depuis la v13, non vérifié dans le dépôt) | `Directory.Packages.props:14`, `DependencyInjection.cs:16` |
| Pipeline | 1 pré-processeur + 4 behaviours : unhandled exception (log), authorization, validation, performance (alerte > 500 ms, `PerformanceBehaviour.cs:37`) | `Application/DependencyInjection.cs:16-23` |
| Validation | FluentValidation, un validateur par commande, découverts par assembly ; le validateur de `CreateTodoList` interroge la base pour l'unicité du titre (`CreateTodoListCommandValidator.cs:46-62`) | `ValidationBehaviour.cs:15-33` |
| Erreurs | Exceptions de bout en bout. `Result` (`Common/Models/Result.cs:3-24`) sert uniquement à `IIdentityService` (`IdentityResultExtensions.cs:8-13`). Pas de `Result<T>` | `ProblemDetailsExceptionHandler.cs` |
| NotFound | `Guard.Against.NotFound` d'Ardalis (`UpdateTodoItem.cs:28`, `DeleteTodoItem.cs:21`), exception `NotFoundException` d'Ardalis, mappée en 404 | `Web/GlobalUsings.cs:1` |
| Domain events | `BaseEntity.AddDomainEvent` (`Domain/Common/BaseEntity.cs:16-19`), levé dans le setter de `TodoItem.Done` quand `false` vers `true` (`Domain/Entities/TodoItem.cs:14-26`). Publié par `DispatchDomainEventsInterceptor` dans `SavingChanges(Async)` (avant l'écriture en base, `DispatchDomainEventsInterceptor.cs:17-30`). Les événements sont vidés avant publication (ligne 45). Unique handler : `LogTodoItemCompleted` qui journalise (`src/Application/TodoItems/EventHandlers/LogTodoItemCompleted.cs:15-19`) | |
| Contradiction | ADR-003 écrit que les événements sont dispatchés « après `SaveChangesAsync` » (`docs/decisions/ADR-003-MediatR-Contracts-In-Domain.md`, section Context) ; le code publie avant. Selon la règle « le code d'abord », la doc est en défaut | |
| Entités | `BaseEntity` (clé `int Id` non générique, commentaire l.7-8 le reconnaît) > `BaseAuditableEntity` (Created, CreatedBy, LastModified, LastModifiedBy). Propriétés publiques avec setters : modèle anémique, pas d'invariant dans les entités (sauf le setter de `Done`) | `Domain/Common/*.cs` |
| Value object | `Colour` (`Domain/ValueObjects/Colour.cs:3-66`), base `ValueObject`, persisté en type possédé (`OwnsOne`, `TodoListConfiguration.cs:15-16`) | |
| Mapping | AutoMapper 16.2.0, profils dans les DTO, `ProjectTo` côté requête. Les commandes mappent à la main (`new TodoItem{...}`). Test `AssertConfigurationIsValid` (`tests/Application.UnitTests/Common/Mappings/MappingTests.cs:31-34`) | |
| Autorisation | Attribut `[Authorize(Roles, Policy)]` sur la requête (`Common/Security/AuthorizeAttribute.cs:7-23`), vérifié par le behaviour ; en plus `RequireAuthorization()` par groupe d'endpoints (`TodoItems.cs:13`, `TodoLists.cs:13`, `WeatherForecasts.cs:10`). Seul `GetTodosQuery` porte l'attribut ; les commandes ne s'appuient que sur la couche HTTP | grep `\[Authorize` |
| Identité | `AddIdentityCore<ApplicationUser>` + cookies (variantes SPA) ou Bearer token (variante API seule) selon `#if (UseApiOnly)` (`Infrastructure/DependencyInjection.cs:46-74`) ; `MapIdentityApi` (`Endpoints/Users.cs:12`). Utilisateur courant : `CurrentUser : IUser` lit `ClaimTypes.NameIdentifier` (`Web/Services/CurrentUser.cs:16-17`) | |
| Propriété des données | Inexistante. Aucun `OwnerId`/`UserId` sur `TodoList`/`TodoItem` (`Domain/Entities/*.cs`), aucun filtre de requête ; `CreatedBy` n'est qu'un champ d'audit non utilisé dans les requêtes (grep `UserId|OwnerId|CreatedBy ==` vide dans les handlers). Tout utilisateur authentifié lit et modifie les données de tous | |
| CORS | `AllowAnyMethod().AllowAnyHeader().AllowAnyOrigin()` (`Web/Program.cs:28-31`) | |
| Initialisation base | En Development : `EnsureDeleted` puis `EnsureCreated` à chaque démarrage, seed d'un administrateur `administrator@localhost` / `Administrator1!` et d'une liste (`ApplicationDbContextInitialiser.cs:45-46,79-107`) | |

## 4. Conventions

- Organisation : par feature dans Application (`src/Application/<Feature>/{Commands,Queries,EventHandlers}/<UseCase>/`), mais par couche pour le reste (Domain/Entities, Domain/Events, Infrastructure/Data/Configurations, Web/Endpoints). Un dossier de feature par agrégat : `TodoItems`, `TodoLists`, `WeatherForecasts`.
- Nommage : record `XxxCommand`/`XxxQuery`, handler `XxxCommandHandler`, validateur `XxxCommandValidator`. Fichier nommé sans suffixe (`CreateTodoItem.cs` contient la commande et son handler, `CreateTodoItemCommandValidator.cs` à part).
- Champs privés `_camelCase` (`.editorconfig:236`), sévérité `suggestion`.
- Namespaces fichier (`namespace X;`), `GlobalUsings.cs` par projet.
- Les extensions d'enregistrement vivent dans le namespace `Microsoft.Extensions.DependencyInjection` (`Application/DependencyInjection.cs:5`).
- Tests : projets miroirs dans `tests/` ; `Application.UnitTests/Common/...` pour les composants communs ; `Application.FunctionalTests/<Feature>/{Commands,Queries}/<UseCase>Tests.cs`.

### Forme exacte d'un use case (exemple complet, commande de création)

```csharp
// src/Application/TodoItems/Commands/CreateTodoItem/CreateTodoItem.cs:6-37
public record CreateTodoItemCommand : IRequest<int>
{
    public int ListId { get; init; }
    public string? Title { get; init; }
}

public class CreateTodoItemCommandHandler : IRequestHandler<CreateTodoItemCommand, int>
{
    private readonly IApplicationDbContext _context;
    public CreateTodoItemCommandHandler(IApplicationDbContext context) { _context = context; }

    public async Task<int> Handle(CreateTodoItemCommand request, CancellationToken cancellationToken)
    {
        var entity = new TodoItem { ListId = request.ListId, Title = request.Title, Done = false };
        _context.TodoItems.Add(entity);
        await _context.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }
}
// CreateTodoItemCommandValidator.cs : RuleFor(v => v.Title).MaximumLength(200).NotEmpty();
// Web/Endpoints/TodoItems.cs:23 : sender.Send(command) puis TypedResults.Created(...)
```

Point de contraste : pas de DTO de requête séparé, la commande MediatR est aussi le corps HTTP (`CreateTodoItem(ISender sender, CreateTodoItemCommand command)`).

Pour les mises à jour, l'URL et le corps portent chacun l'identifiant et l'endpoint contrôle l'égalité (`TodoItems.cs:34-35`, `TodoLists.cs:43`) : `400` sans corps de problème.

## 5. Outillage

### 5.1 Options du template (`.template.config/template.json`)

- `ClientFramework` : `Angular` (défaut) | `React` | `None` (lignes 228-259), calculés en `UseAngular`, `UseReact`, `UseApiOnly`. Courts : `--client-framework`/`-cf` (`dotnetcli.host.json`).
- `Database` : `sqlite` (défaut) | `sqlserver` | `postgresql` (lignes 260-291), `--database`/`-db`.
- Ports générés aléatoires par plage pour Kestrel et AppHost (lignes 28-203), versions injectées (`caPackageVersion`, 204-211).
- Pas d'option d'authentification, d'exemples, de fichiers d'agent, ni de nom d'hôte supplémentaire. Matrice testée : 3 x 3 = 9 combinaisons (`.github/workflows/test-templates.yml:18-20`).
- Mécanisme : directives de préprocesseur `#if (UsePostgreSQL)` dans les `.cs` (`Infrastructure/DependencyInjection.cs:26-40`, `AppHost/Program.cs:7-23`), `<!--#if-->` dans les `.csproj` et `.slnx`, modificateurs d'exclusion et de renommage par option (`template.json:321-394`, par exemple `ClientApp-React` renommé en `ClientApp`). Le dépôt lui-même compile comme la variante SQLite + Angular.
- Entrées périmées dans `template.json` : exclusions de fichiers inexistants (`tests/Application.FunctionalTests/PostgreSQLTestcontainersTestDatabase.cs`, `SqlTestDatabase.cs`, `SqliteTestDatabase.cs`, `appsettings*.json`, lignes 309-316 ; `src/Web/Pages/**`, ligne 350 ; `.azure/**/*`, ligne 298) et symboles hôte `appHostMcpHttpPort`/`appHostMcpHttpsPort` dans `dotnetcli.host.json` sans symbole correspondant dans `template.json`. Vestiges de refontes passées, sans effet fonctionnel.

### 5.2 Item template `ca-usecase` (`templates/ca-use-case/`)

`dotnet new ca-usecase --name CreateTodoList --feature-name TodoLists --usecase-type command --return-type int` (`README-template.md:40,46`). Génère un fichier unique (commande/requête + validateur + handler à `throw new NotImplementedException()`), `Commands/` ou `Queries/` selon `--usecase-type` (`.template.config/template.json:99-111`), type de retour optionnel via `//#if (hasReturnType)`. Il ne crée ni endpoint, ni test, ni DTO. Livré dans le même paquet NuGet (`template.json:300` l exclut du template solution, le nuspec embarque tout via `.\**`, `CleanArchitecture.nuspec:33`).

### 5.3 Scripts

`build/build.ps1` (build, `npm ci` des clients, tests, cible `Basic` sans acceptation), `build/test.ps1` (génère et teste la matrice 3x3 en local), `build/repack.ps1` (empaquette et réinstalle le template). PowerShell seulement, pas d'équivalent bash. Pas de CLI propre, pas de lanceur (le lanceur recommandé est `aspire run`, `README-template.md:13-17`), `aspire.config.json:2-4` désigne l'AppHost.

### 5.4 Aspire, Docker, front-ends

- AppHost (`src/AppHost/Program.cs:3-48`) : `AddAzureContainerAppEnvironment("aca-env")`, base selon le provider (PostgreSQL et SQL Server en conteneur persistant en mode run, SQLite via CommunityToolkit), projet Web avec URL « Scalar API Reference », front-end JavaScript uniquement en mode run (`IsRunMode`, ligne 37). Environnement ASP.NET par défaut Production hors mode run (`Extensions.cs:8-13`, nouveau depuis v10.8.0, PR #1661).
- `TestAppHost` (`tests/TestAppHost/Program.cs:8-21`) : seulement la base.
- ServiceDefaults : OpenTelemetry, résilience HTTP, service discovery, health checks `/health` et `/alive` en Development uniquement (`ServiceDefaults/Extensions.cs:101-116`).
- Docker : requis seulement pour sqlserver et postgresql (`README.md:19`, ADR-002). Pas de Dockerfile ni de compose dans le dépôt (le déploiement passe par Aspire / Azure Container Apps). `.devcontainer/devcontainer.json` : image python 3.13 + docker-in-docker (sans SDK .NET déclaré).
- Front-ends : Angular 22 (`ClientApp`, modules NgModule + `app.server.module.ts`, `package.json:20-24` en main) et React 19 + Vite 8 (`ClientApp-React`, composants `.jsx`, JavaScript non typé). Client d'API généré à l'`prebuild`/`prestart` par NSwag depuis `wwwroot/openapi/v1.json` (`package.json:9-13`, `nswag.json`). OpenAPI produit au build (`Web.csproj:7-9`) ; Scalar sert la doc (`Web/Program.cs:35-36`).
- Pas d'API cliente .NET, pas de MCP, pas de worker, pas d'application desktop.

## 6. Tests

| Projet | Outils | Contenu | Tests |
|---|---|---|---|
| Domain.UnitTests | NUnit 5.0.0, Shouldly 4.3.0 | `ColourTests` (`tests/Domain.UnitTests/ValueObjects/ColourTests.cs`) | 6 |
| Application.UnitTests | NUnit, Moq 4.21.0, Shouldly | `RequestLoggerTests` (2), `ValidationExceptionTests` (3), `MappingTests` (3 cas) | 8 |
| Application.FunctionalTests | NUnit, Moq, Respawn 7.0.0, Shouldly, `WebApplicationFactory<Program>`, Aspire.Hosting.Testing | 8 classes : commandes création, mise à jour, suppression de listes et d'items, requête `GetTodos` | 19 |
| Infrastructure.IntegrationTests | NUnit | un seul fichier `GlobalUsings.cs`, aucun test | 0 |
| Web.AcceptanceTests | Reqnroll.NUnit 3.3.4, Playwright 1.64.1, Aspire.Hosting.Testing | 4 features, 6 scénarios (Login 2, Home 1, Weather 1, Counter 2) | 6 |
| ClientApp (Angular) | vitest | 2 fichiers `spec.ts` | 3 |
| ClientApp-React | aucun | aucun test | 0 |

Total .NET NUnit : 33 tests (comptage des `[Test]` et `[TestCase]` en retirant le `[Test]` doublé sur le `[TestCase]`).
Détails d'exécution :
- Pas de Testcontainers (ADR-002 le rejette explicitement, `ADR-002...md:25`) : la base des tests fonctionnels est lancée par Aspire (`tests/Application.FunctionalTests/FunctionalTestSetup.cs:20-46`, démarrage dans un délai de 60 s). Respawn réinitialise entre les tests (`TestBase.cs:5-8`, `DatabaseResetter.cs:40-45`).
- Les tests fonctionnels envoient les requêtes MediatR directement par `TestApp.SendAsync` (`Infrastructure/TestApp.cs:16-32`) ; `IUser` est remplacé par un mock Moq (`WebApiFactory.cs:19-27`). Aucun test n'appelle les endpoints HTTP : le routage, les codes de statut et le `ProblemDetailsExceptionHandler` ne sont couverts que par les 6 scénarios Playwright (qui visent l'UI). Aucun test de non-régression sur l'autorisation par endpoint.
- État statique partagé (`TestApp._userId`, `_roles`, `TestApp.cs:13-14`) : les tests fonctionnels ne sont pas parallélisables.
- Sans Docker : avec SQLite (défaut) les tests fonctionnels ne devraient pas en avoir besoin (README.md:19, ADR-002) ; non exécuté ici, à confirmer. Avec sqlserver/postgresql : Docker requis. Les tests d'acceptation exigent Node, les navigateurs Playwright et l'AppHost complet.
- Couverture : `coverlet.collector` référencé dans 4 projets de test (`Directory.Packages.props:12`) mais aucun seuil, aucun rapport, aucune étape CI de couverture (grep `coverage` hors package vide). Couverture non mesurée.
- Analyseurs de test : `NUnit.Analyzers` 4.15.0 dans 4 projets de test (seuls analyzers du dépôt).
- Contradiction doc / code : `README-template.md:57` annonce des tests « unit, integration, and functional » alors que `Infrastructure.IntegrationTests` est vide.
- Les tests ne contiennent ni test de la pagination, ni de la propriété, ni de l'ordre des behaviours, ni de domain event (`TodoItemCompletedEvent` n'est testé nulle part, grep).

## 7. Vie du projet

- CHANGELOG : aucun (`find -iname CHANGELOG*` vide). Les notes viennent des GitHub Releases : `release.yml:38-50` injecte le corps de la release dans `releaseNotes` du nuspec ; la version vient du tag (`release.yml:28-36`, `caPackageVersion` réécrit par `jq`).
- Workflows : `build.yml` (PR vers main : restore, build Release, Playwright, `dotnet test` du solution, ubuntu seulement), `codeql.yml` (push, PR, cron lundi), `release.yml` (publication NuGet à la publication d'une release, `nuget pack` via Mono), `test-templates.yml` (push sur main et manuel : génère et build/teste les 9 combinaisons).
- `build.yml` teste le dépôt tel quel (variante par défaut) ; seules les 9 variantes générées passent par `test-templates.yml` et seulement après le push sur main, pas avant la fusion de la PR.
- Renovate : lundi, groupes .NET/Angular/React/Testing, automerge des non-majeurs (`renovate.json`). 51 des 58 commits depuis v10.8.0 sont des mises à jour de dépendances (`git log v10.8.0..HEAD`) ; 7 commits de Jason Taylor, 51 de `renovate[bot]`.
- Cadence : 22 tags (v10.0.0 le 2025-11-10, v10.8.0 le 2026-03-24). Aucune release depuis le 2026-03-24 (7 mois), alors que main avance (Angular 22, Aspire 13.6.4).
- Commits non-deps depuis v10.8.0 : ADR (#1595, 2026-03-28), Aspire 13.6 (#1656), environnement Production à la publication (#1661), Angular 22 (#1662), pin `System.Security.Cryptography.Pkcs` (#1663), React client build (#1667).
- Documentation : le site vit hors dépôt (`cleanarchitecture.jasontaylor.dev`) ; dans le dépôt seul `docs/decisions/` (3 ADR + gabarit). `README.md:84` dit Angular 21 alors que main est en Angular 22 (`src/Web/ClientApp/package.json:20-24`).

## 8. Comparaison main contre v10.8.0 (`diff` hors fins de ligne)

- Mêmes `.cs` d'application et de domaine : tout le code de logique (Application, Domain, Infrastructure, Web, tests) est identique, sauf `src/AppHost/Extensions.cs`.
- `Application.csproj` : retrait de `Microsoft.Build.Tasks.Core` et `Microsoft.Build.Utilities.Core` (présents dans v10.8.0).
- `Directory.Packages.props` : Aspire 13.2.0 vers 13.6.4, nunit 4.5.1 vers 5.0.0, NUnit.Analyzers 4.12 vers 4.15, Moq 4.20.72 vers 4.21.0, Playwright 1.58 vers 1.64.1, coverlet 8.0.1 vers 10.1.0, OpenTelemetry 1.15 vers 1.20.1, EF Core 10.0.5 vers 10.0.14 ; ajout du pin transitif de `System.Security.Cryptography.Pkcs` (alerte GHSA-846f-f764-76mx, GHSA-2fmv-rv2c-59x7) ; `Aspire.Hosting.AppHost` supprimé (SDK Aspire 13.6.4 + `AspireUseCliBundle`).
- `global.json` : SDK 10.0.201 vers 10.0.403.
- Ajouts : `.gitattributes` (LF dans le dépôt), `aspire.config.json`, `docs/decisions/` (3 ADR), section « Architectural decisions » dans les README, `aspire run` documenté.
- Front Angular : 21 vers 22, karma/jasmine remplacés par vitest + jsdom, `@angular/build` remplace `@angular-devkit/build-angular`, TypeScript 5.9 vers 6.0.
- Workflows : actions majeures relevées (checkout v6 vers v7, setup-dotnet v5 vers v6, nuget/setup-nuget v2 vers v4) et `setup-dotnet` ajouté à `codeql.yml`.
- Conclusion : main est une release v10.8.0 rafraîchie en dépendances, sans changement d'architecture ni de fonctionnalité du template.

## 9. Constats pour la comparaison

Points forts du concurrent (prouvés) :
- F1 Simplicité et lisibilité : un use case tient en 1 à 2 fichiers (`CreateTodoItem.cs`, `src/Application/TodoItems/Commands/CreateTodoItem/`) ; `IApplicationDbContext` évite les repositories (ADR-001).
- F2 Choix de matrice : 2 front-ends (Angular, React) + API seule x 3 bases, 9 combinaisons générées, construites et testées en CI (`test-templates.yml:18-20,59-77`).
- F3 Aspire intégré par défaut, avec une base de test pilotée par Aspire et Respawn (ADR-002, `FunctionalTestSetup.cs`), SQLite sans Docker.
- F4 Item template `ca-usecase` pour scaffolder un use case (`templates/ca-use-case/`).
- F5 Distribution NuGet automatisée par la release GitHub (`release.yml`), dépendances tenues à jour par Renovate (51/58 commits).
- F6 OpenAPI par endpoint avec résumé et description (`TodoItems.cs:21-22`) et Scalar ; client TypeScript généré par NSwag.

Faiblesses (prouvées) :
- W1 Aucune règle d'architecture exécutable (aucun test d'architecture, naming en `suggestion`, 1 `#pragma` dans le code livré, `GetWeatherForecastsQuery.cs:12-14`).
- W2 Aucune propriété des données ni pagination ; tout utilisateur authentifié voit toutes les listes (`GetTodos.cs:43-47`).
- W3 Autorisation de l'application portée seulement par la couche HTTP pour 8 requêtes sur 9 : les 7 commandes et `GetWeatherForecastsQuery` (`[Authorize]` seulement sur `GetTodosQuery`).
- W4 Tests fonctionnels contournant HTTP ; aucune couverture mesurée ; `Infrastructure.IntegrationTests` vide ; aucun test React.
- W5 Pas de migrations : `EnsureDeleted` + `EnsureCreated` en Development (`ApplicationDbContextInitialiser.cs:45-46`).
- W6 Doc en défaut face au code : ADR-003 (événements « après SaveChanges », code : avant), `README-template.md:57` (tests d'intégration inexistants), `README.md:84` (Angular 21).
- W7 Pas de CHANGELOG, aucune release depuis le 2026-03-24 alors que main a 58 commits.
- W8 Scripts PowerShell uniquement ; entrées périmées dans `template.json` / `dotnetcli.host.json`.
- W9 CORS ouvert à toute origine (`Program.cs:28-31`).

## 10. Non vérifié

- Aucun build, aucun test lancé : les comptes de tests viennent de la lecture du code, pas d'une exécution. Les tests ne sont pas prouvés verts.
- Fonctionnement réel de `dotnet new ca-sln` et des 9 variantes : non exécuté.
- Besoin ou non de Docker pour `Application.FunctionalTests` en SQLite : déduit de la doc (README.md:19, ADR-002), non exécuté.
- Licence de MediatR 14 : connaissance externe, non lue dans le dépôt.
- Les GitHub Releases et le site de documentation (hors dépôt) ne sont pas consultés ; la cadence de release vient des tags du clone.
- Contenu des fichiers front-end (Angular, React) hors `package.json`, specs et `nswag.json` : non lu en détail.
