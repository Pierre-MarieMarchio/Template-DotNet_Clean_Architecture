# Carte du concurrent (JasonTaylorDev/CleanArchitecture), branche main

Clone : /tmp/claude-1000/-home-pmmarchio-code-Template-DotNet-Clean-Architecture/608f3f2d-3c02-4530-9e46-6f8e7e0e445b/scratchpad/repos/competitor-main
HEAD : 345e7fe7612fdaf0cb5da58826a7c57d42e1fdd8 (2026-10-26), `git describe` = v10.8.0-58-g345e7fe, `main...origin/main` à jour.
Release comparée : repos/competitor-release = v10.8.0 (tag du 2026-03-24 ; `gh api .../releases/latest` renvoie v10.8.0 publiée le 2026-03-23T22:10Z, donc c'est bien la dernière release).
Chemins ci-dessous relatifs au clone competitor-main. Aucun build lancé ; tout est lu dans le code (sauf mention).

## 0. Résumé en une page

- Template `dotnet new ca-sln` (NuGet `Clean.Architecture.Solution.Template`, nuspec) : une seule solution, 7 projets src + 5 projets tests + 1 projet Aspire de test. Options : `--client-framework angular|react|none` et `--database sqlite|postgresql|sqlserver` (6 symboles de port cachés en plus). Pas d'option d'auth (Identity en dur), pas d'option "exemples".
- Architecture : Clean Architecture classique, par feature dans Application (`TodoItems/Commands/...`), MediatR + FluentValidation + AutoMapper + Ardalis.GuardClauses, exceptions pour les erreurs (pas de Result métier), `IApplicationDbContext` exposant `DbSet<T>` à l'Application (ADR-001, EF Core référencé par Application).
- Aucune règle mécanique : pas de projet de tests d'architecture, pas d'analyzers hors `NUnit.Analyzers`, .editorconfig entièrement en `suggestion`/`silent`, seul `TreatWarningsAsErrors` tient la qualité. Pas de couverture mesurée (coverlet.collector référencé, jamais utilisé), pas de seuil.
- Pas de lecture paginée dans le code actuel (supprimée à #1587 "Slim down Todo demo"). Pas de propriété des données : toute liste est visible par tout utilisateur authentifié. Pas de migrations EF : la base est supprimée et recréée à chaque démarrage en Development.
- Tests : 32 méthodes de test .NET (33 exécutions avec les 2 `TestCase`) + 6 scénarios Reqnroll/Playwright + 2 specs Angular. Fonctionnels via Aspire `TestAppHost` + Respawn, appelés par le médiateur (jamais par HTTP). Le projet `Infrastructure.IntegrationTests` est vide.
- Vie du projet : 16 releases v10.x entre 2025-11-10 et 2026-03-24, puis aucune release depuis (58 commits non publiés dont 51 de dépendances ; 7 commits notables dont Angular 22, Aspire 13.6). Pas de CHANGELOG : les notes de release sont le corps de la Release GitHub, copié dans le nuspec par `release.yml`.

## 1. Projets, références, règles

### 1.1 Solution (CleanArchitecture.slnx:1-26)

| Projet | Fichier | SDK / type | Références projet | Paquets notables |
|---|---|---|---|---|
| Domain | src/Domain/Domain.csproj:8-10 | Microsoft.NET.Sdk | aucune | `MediatR.Contracts` (ADR-003) |
| Application | src/Application/Application.csproj:8-19 | Microsoft.NET.Sdk | Domain | Ardalis.GuardClauses, AutoMapper, FluentValidation.DependencyInjectionExtensions, MediatR, Microsoft.EntityFrameworkCore, Microsoft.Extensions.Hosting |
| Infrastructure | src/Infrastructure/Infrastructure.csproj:8-29 | Microsoft.NET.Sdk | Application, Shared | EF Core + provider (Sqlite / Npgsql / SqlServer via Aspire.*), Identity.EntityFrameworkCore, Diagnostics.EntityFrameworkCore |
| Shared | src/Shared/Shared.csproj:1-13 | Microsoft.NET.Sdk | aucune | rien ; redéclare TargetFramework/ImplicitUsings/Nullable (doublon de Directory.Build.props) ; porte les noms de services Aspire (src/Shared/Services.cs:3-26) |
| ServiceDefaults | src/ServiceDefaults/ServiceDefaults.csproj:1-21 | `IsAspireSharedProject` | aucune | OpenTelemetry, Http.Resilience, ServiceDiscovery |
| Web | src/Web/Web.csproj:13-37 | Microsoft.NET.Sdk.Web | Application, Infrastructure, ServiceDefaults | OpenApi, Scalar.AspNetCore, Azure.Identity + Key Vault, HealthChecks.EFCore, EF Design |
| AppHost | src/AppHost/AppHost.csproj:1-29 | Aspire.AppHost.Sdk/13.6.4 | Web, Shared (`IsAspireProjectResource=false`) | Aspire.Hosting.Azure.AppContainers, Aspire.Hosting.JavaScript, hébergement DB selon option |
| TestAppHost | tests/TestAppHost/TestAppHost.csproj:1-26 | Aspire.AppHost.Sdk/13.6.4 | Shared | hébergement DB seul |

Graphe : Domain <- Application <- Infrastructure -> Shared ; Web -> {Application, Infrastructure, ServiceDefaults} ; AppHost -> {Web, Shared}. Infrastructure n'a pas de ProjectReference vers Domain (transitif par Application). Application référence EF Core et Microsoft.Extensions.Hosting : choix assumé par docs/decisions/ADR-001-Use-EFCore-In-Application-Layer.md. Domain référence MediatR.Contracts : docs/decisions/ADR-003-MediatR-Contracts-In-Domain.md.

Projets de tests (src/ ... jamais référencés par des src) :

| Projet | Références | Paquets |
|---|---|---|
| tests/Domain.UnitTests/Domain.UnitTests.csproj:8-27 | Domain | NUnit, NUnit.Analyzers, Shouldly, coverlet.collector |
| tests/Application.UnitTests/Application.UnitTests.csproj:8-28 | Application, Infrastructure (Infrastructure inutile pour les 3 fichiers de test) | NUnit, Moq, Shouldly, Azure.Identity |
| tests/Application.FunctionalTests/Application.FunctionalTests.csproj:8-38 | Shared, Web, TestAppHost | NUnit, Moq, Respawn, Shouldly, Mvc.Testing, Aspire.Hosting.Testing, System.IdentityModel.Tokens.Jwt |
| tests/Infrastructure.IntegrationTests/Infrastructure.IntegrationTests.csproj:8-20 | aucune | NUnit seulement ; aucun fichier de test (seul GlobalUsings.cs:1) |
| tests/Web.AcceptanceTests/Web.AcceptanceTests.csproj:9-30 (absent en `--client-framework none`, CleanArchitecture.slnx:19-21) | AppHost, Shared | Playwright, Reqnroll.NUnit, Shouldly, Aspire.Hosting.Testing |

### 1.2 Règles qui tiennent l'architecture

Constat : aucune règle structurelle exécutable.
- Pas de tests d'architecture : `grep -rIl -i "NetArchTest|ArchUnit|Roslynator|StyleCop|EnforceCodeStyleInBuild|AnalysisLevel|AnalysisMode"` ne remonte que `.gitignore`.
- Directory.Build.props:1-15 : net10.0, `TreatWarningsAsErrors=true`, `ImplicitUsings`, `Nullable`, `ArtifactsPath=artifacts`. Pas d'`AnalysisMode`, pas de `EnforceCodeStyleInBuild`, pas d'analyzer de code. Seul `NUnit.Analyzers` est référencé (tests).
- .editorconfig (382 lignes) : conventions de style et de nommage, toutes à `suggestion` ou `silent` (ex. `csharp_style_namespace_declarations = file_scoped:silent`, .editorconfig:181 ; 19 règles `dotnet_naming_rule.*.severity = suggestion`, .editorconfig:196-268). `end_of_line = lf` (.editorconfig:30). Rien ne casse la build.
- Directory.Packages.props:4-5 : gestion centralisée des versions + `CentralPackageTransitivePinningEnabled` ; une épingle de sécurité commentée pour `System.Security.Cryptography.Pkcs` (Directory.Packages.props:33-34).
- La séparation des couches ne tient que par les ProjectReference (impossible de référencer Infrastructure depuis Application sans cycle), pas par test. Rien n'empêche Web de référencer Domain ou un handler d'utiliser un type EF concret.
- `global.json` : SDK 10.0.403, `rollForward: latestFeature`.
- Dérogation : `#pragma warning disable CS1998` dans src/Application/WeatherForecasts/Queries/GetWeatherForecasts/GetWeatherForecastsQuery.cs:12-14 (handler async sans await). Les gabarits de use case (voir 5.2) génèrent aussi des `async Task Handle` sans await : avec `TreatWarningsAsErrors`, CS1998 devrait casser la compilation d'un use case fraîchement généré (déduction non vérifiée par build).

## 2. Chemin d'une requête, fichier par fichier

### 2.1 Écriture : `POST /api/TodoItems` (CreateTodoItem)

1. Démarrage : src/Web/Program.cs:7-12 enchaîne `AddServiceDefaults`, `AddKeyVaultIfConfigured`, `AddApplicationServices`, `AddInfrastructureServices`, `AddWebServices` ; src/Web/Program.cs:45 `MapEndpoints(typeof(Program).Assembly)`.
2. Découverte des routes : src/Web/Infrastructure/WebApplicationExtensions.cs:12-28 scanne par réflexion les types `IEndpointGroup` (src/Web/Infrastructure/IEndpointGroup.cs:10-19), préfixe `/api/{NomDeClasse}`, tag OpenAPI = nom de classe. `operationId` = nom de la méthode (src/Web/Infrastructure/EndpointRouteBuilderExtensions.cs:20-62, avec garde anti-lambda MethodInfoExtensions.cs:22-26).
3. Endpoint : src/Web/Endpoints/TodoItems.cs:9-28. `groupBuilder.RequireAuthorization()` (ligne 13), `MapPost(CreateTodoItem)` ; la commande est liée depuis le corps ; `sender.Send(command)` ; retourne `TypedResults.Created($"/{nameof(TodoItems)}/{id}", id)` (ligne 27). Pas de DTO de requête séparé : la commande MediatR est le contrat HTTP.
4. Pipeline MediatR (src/Application/DependencyInjection.cs:16-23), ordre d'enregistrement : `LoggingBehaviour` (pré-processeur, src/Application/Common/Behaviours/LoggingBehaviour.cs:21-34) -> `UnhandledExceptionBehaviour` (log + rethrow) -> `AuthorizationBehaviour` (src/Application/Common/Behaviours/AuthorizationBehaviour.cs:22-79) -> `ValidationBehaviour` (src/Application/Common/Behaviours/ValidationBehaviour.cs:15-33) -> `PerformanceBehaviour` (log WARNING au-delà de 500 ms, PerformanceBehaviour.cs:37) -> handler.
5. Autorisation : le comportement ne vérifie que les requêtes portant `[Authorize]` (src/Application/Common/Security/AuthorizeAttribute.cs:6-23 ; Roles, Policy). Seul `GetTodosQuery` le porte (src/Application/TodoLists/Queries/GetTodos/GetTodos.cs:9). `CreateTodoItemCommand` n'a pas l'attribut : l'accès tient à `RequireAuthorization()` de l'endpoint seulement.
6. Validation : src/Application/TodoItems/Commands/CreateTodoItem/CreateTodoItemCommandValidator.cs:5-10 (`MaximumLength(200)`, `NotEmpty`). Échec -> `ValidationException` (src/Application/Common/Exceptions/ValidationException.cs:13-19).
7. Handler : src/Application/TodoItems/Commands/CreateTodoItem/CreateTodoItem.cs:13-37. Construit `new TodoItem { ListId, Title, Done = false }`, `_context.TodoItems.Add`, `SaveChangesAsync`, renvoie `entity.Id`. Aucune vérification que `ListId` existe (une clé étrangère invalide donnerait une erreur EF non traduite, 500 ; déduction du code, non exécutée).
8. Persistance : `IApplicationDbContext` (src/Application/Common/Interfaces/IApplicationDbContext.cs:5-12) implémentée par src/Infrastructure/Data/ApplicationDbContext.cs:10-23 (`IdentityDbContext<ApplicationUser>`, configurations par `ApplyConfigurationsFromAssembly`). Intercepteurs enregistrés en scoped (src/Infrastructure/DependencyInjection.cs:20-21, ajoutés ligne 25) :
   - `AuditableEntityInterceptor` (src/Infrastructure/Data/Interceptors/AuditableEntityInterceptor.cs:36-54) pose Created/CreatedBy/LastModified/LastModifiedBy depuis `IUser` et `TimeProvider`.
   - `DispatchDomainEventsInterceptor` (src/Infrastructure/Data/Interceptors/DispatchDomainEventsInterceptor.cs:32-49) publie les événements via `IMediator.Publish`.
9. Réponse : `Created<int>`. Erreurs : `ProblemDetailsExceptionHandler` (src/Web/Infrastructure/ProblemDetailsExceptionHandler.cs:13-51) traduit ValidationException -> 400 `ValidationProblemDetails`, NotFoundException (Ardalis) -> 404, UnauthorizedAccessException -> 401, ForbiddenAccessException -> 403 ; le reste retombe dans le gestionnaire par défaut. Branchement : src/Web/DependencyInjection.cs:19 et src/Web/Program.cs:38 (`UseExceptionHandler(options => { })`).
10. Identité de l'appelant : `CurrentUser : IUser` lit `ClaimTypes.NameIdentifier` / `ClaimTypes.Role` dans `IHttpContextAccessor` (src/Web/Services/CurrentUser.cs:7-18), enregistré src/Web/DependencyInjection.cs:15.

### 2.2 Lecture : `GET /api/TodoLists` (GetTodos) ; la lecture paginée n'existe pas

- `grep -rIn -i "paginat|PageNumber|PageSize|Skip\(|Take\("` dans src et tests : aucun résultat applicatif. `git log -S"PaginatedList"` montre qu'elle a existé (commits e8540ce, 55b2d93, 1c2ef45) et que a6b7655 "Slim down Todo demo and add colour theming (#1587)" l'a retirée.
- La seule lecture de liste est donc non paginée : endpoint src/Web/Endpoints/TodoLists.cs:21-28 -> `GetTodosQuery` (`[Authorize]`, src/Application/TodoLists/Queries/GetTodos/GetTodos.cs:9-10) -> handler lignes 23-49 : `_context.TodoLists.AsNoTracking().ProjectTo<TodoListDto>(_mapper.ConfigurationProvider).OrderBy(t => t.Title).ToListAsync(...)`, plus listes de priorités et couleurs construites en mémoire. Aucun filtre par utilisateur (lignes 43-47) : tout utilisateur authentifié voit toutes les listes.
- Mapping : profils AutoMapper imbriqués dans les DTO (TodoListDto.cs:20-26, TodoItemDto.cs:19-26, LookupDto.cs:11-18), enregistrés par `AddAutoMapper(cfg => cfg.AddMaps(assembly))` (src/Application/DependencyInjection.cs:11-12). Test de validité : tests/Application.UnitTests/Common/Mappings/MappingTests.cs:30-44.
- Réponse : `Ok<TodosVm>` (TodoLists.cs:23). Le DTO racine s'appelle `Vm` (src/Application/TodoLists/Queries/GetTodos/TodosVm.cs:5-18).

## 3. Concepts

| Sujet | Réalité dans le code | Preuve |
|---|---|---|
| CQRS | Oui, par convention de dossiers `Commands/` et `Queries/`, une commande ou requête MediatR par opération ; pas de modèles de lecture/écriture séparés | src/Application/TodoItems/Commands/, src/Application/TodoLists/Queries/ |
| Médiateur | MediatR 14.2.0 ; le endpoint injecte `ISender` | Directory.Packages.props:14 ; src/Web/Endpoints/TodoItems.cs:23 |
| Pipeline | 1 pré-processeur + 4 behaviours ouverts, ordre fixe (voir 2.1.4) | src/Application/DependencyInjection.cs:16-23 |
| Validation | FluentValidation, validateurs découverts par assembly, exécutés en parallèle par `ValidationBehaviour` ; 4 validateurs (CreateTodoItem, UpdateTodoItem, CreateTodoList, UpdateTodoList) ; les validateurs de liste interrogent la base (unicité du titre, async, injectent `IApplicationDbContext`) ; UpdateTodoItemDetail et les Delete n'en ont pas | src/Application/TodoLists/Commands/CreateTodoList/CreateTodoListCommandValidator.cs:9-25 |
| Erreurs | Exceptions, traduites en ProblemDetails (voir 2.1.9). `Result` existe (src/Application/Common/Models/Result.cs:3-24) mais ne sert que de retour à `IIdentityService` (Identity), pas aux use cases. `Guard.Against.NotFound` (Ardalis) lève NotFoundException (UpdateTodoItem.cs:28) | src/Application/Common/Interfaces/IIdentityService.cs:13-15 |
| Domain events | Levés dans l'entité : l'accesseur `Done` de `TodoItem` ajoute `TodoItemCompletedEvent` au passage false -> true (src/Domain/Entities/TodoItem.cs:13-26). Stockés dans `BaseEntity.DomainEvents` (`[NotMapped]`, src/Domain/Common/BaseEntity.cs:11-29). Publiés par `DispatchDomainEventsInterceptor` AVANT l'écriture (override de `SavingChanges`/`SavingChangesAsync`, DispatchDomainEventsInterceptor.cs:17-30), dans la même portée DI, via `IMediator.Publish` (handlers `INotificationHandler`) ; la version synchrone fait `.GetAwaiter().GetResult()` (ligne 19 du fichier). Un seul handler : `LogTodoItemCompleted` (src/Application/TodoItems/EventHandlers/LogTodoItemCompleted.cs:6-21), qui ne fait que journaliser. | -- |
| Doc contre code | ADR-003 écrit que MediatR dispatche "après `SaveChangesAsync`" (docs/decisions/ADR-003-MediatR-Contracts-In-Domain.md, section Context) ; le code publie avant l'écriture : la doc contredit le code | DispatchDomainEventsInterceptor.cs:25-30 |
| Entités, classes de base | `BaseEntity` (Id int, événements), `BaseAuditableEntity` (Created, CreatedBy, LastModified, LastModifiedBy), `BaseEvent : INotification`, `ValueObject` (égalité par composants), `Colour` en value object (src/Domain/ValueObjects/Colour.cs:3-66, propriété possédée `OwnsOne`, TodoListConfiguration.cs:15-16). Entités à setters publics (`TodoItem.Title { get; set; }`), pas d'encapsulation ni de constructeur : modèle anémique à l'exception de `Done` | src/Domain/Common/*.cs, src/Domain/Entities/*.cs |
| Clés | `int` ; un commentaire propose `BaseEntity<T>` (src/Domain/Common/BaseEntity.cs:7-8) | -- |
| Mapping | AutoMapper 16.2.0 (`ProjectTo` en lecture) ; écriture : affectation manuelle dans les handlers | Directory.Packages.props:9 |
| Autorisation | Deux couches : `RequireAuthorization()` sur chaque groupe d'endpoints (TodoItems.cs:13, TodoLists.cs:13, WeatherForecasts.cs:10) ; `[Authorize(Roles=..., Policy=...)]` sur la requête, traité par `AuthorizationBehaviour` (rôles OR, politiques via `IIdentityService.AuthorizeAsync`). Le seul rôle défini : `Administrator` (src/Domain/Constants/Roles.cs:3-6), jamais exigé par un use case | grep `[Authorize` : 1 occurrence, GetTodos.cs:9 |
| Authentification | ASP.NET Core Identity avec API (`MapIdentityApi<ApplicationUser>` sous `/api/Users`, src/Web/Endpoints/Users.cs:10-14) ; cookies (Angular/React) ou jeton Bearer (option API seule, src/Infrastructure/DependencyInjection.cs:46-56) ; pas d'OIDC, pas de choix | -- |
| Propriété des données | Inexistante : pas d'`OwnerId` sur `TodoList`/`TodoItem`, requêtes non filtrées. `CreatedBy` n'est qu'un champ d'audit. Aucune règle 404 contre 403 : `ForbiddenAccessException` n'est levée que par rôle/politique | src/Domain/Entities/TodoList.cs:3-10 |
| CORS | `AllowAnyOrigin/AnyMethod/AnyHeader` en dur | src/Web/Program.cs:28-31 |
| Base | `EnsureDeleted` puis `EnsureCreated` à chaque démarrage en Development ; seed admin `administrator@localhost` / `Administrator1!` ; pas de dossier Migrations, pas de bundle | src/Infrastructure/Data/ApplicationDbContextInitialiser.cs:40-53, 68-108 |

## 4. Conventions

- Organisation : par feature dans Application (`<Feature>/Commands|Queries/<UseCase>/`, plus `EventHandlers/`), par couche ailleurs. Dans Domain : `Common`, `Constants`, `Entities`, `Enums`, `Events`, `Exceptions`, `ValueObjects` (par type, pas par feature). Dans Infrastructure : `Data` (Configurations, Interceptors), `Identity`. Dans Web : `Endpoints`, `Infrastructure`, `Services`.
- Nommage : fichier du use case = nom du use case sans suffixe (`CreateTodoItem.cs`) contenant `CreateTodoItemCommand` et `CreateTodoItemCommandHandler` ; validateur dans un fichier à part `CreateTodoItemCommandValidator.cs` ; requêtes : `GetTodosQuery`, DTO `TodoListDto`, vue `TodosVm`. Un fichier de test par use case (`CreateTodoItemTests`). Endpoints : une classe par groupe, nommée au pluriel (`TodoItems`) ; méthodes nommées par verbe (`CreateTodoItem`).
- Style : espaces de noms à portée fichier, constructeurs classiques avec champs `_context` (pas de constructeur primaire, malgré `csharp_style_prefer_primary_constructors = suggestion`, .editorconfig:184), commandes en `record` avec `init`, usings globaux par projet (src/Application/GlobalUsings.cs:1-6). Fichiers C# en UTF-8 avec BOM mélangés (certains sans BOM) ; LF imposé depuis main par `.gitattributes`.
- Commentaires : doc XML et commentaires de raison dans Web/Infrastructure ; commentaires "what" ailleurs ; pas de règle outillée.

### Exemple complet de use case (écriture) : CreateTodoList

Fichiers : src/Application/TodoLists/Commands/CreateTodoList/CreateTodoList.cs et CreateTodoListCommandValidator.cs ; endpoint src/Web/Endpoints/TodoLists.cs:30-37 ; test tests/Application.FunctionalTests/TodoLists/Commands/CreateTodoListTests.cs.

```csharp
public record CreateTodoListCommand : IRequest<int>
{
    public string? Title { get; init; }
    public string? Colour { get; init; }
}

public class CreateTodoListCommandHandler : IRequestHandler<CreateTodoListCommand, int>
{
    private readonly IApplicationDbContext _context;
    public CreateTodoListCommandHandler(IApplicationDbContext context) { _context = context; }

    public async Task<int> Handle(CreateTodoListCommand request, CancellationToken cancellationToken)
    {
        var entity = new TodoList
        {
            Title = request.Title,
            Colour = Colour.From(request.Colour ?? Colour.Grey)
        };
        _context.TodoLists.Add(entity);
        await _context.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }
}
```

Validateur : `RuleFor(v => v.Title).NotEmpty().MaximumLength(200).MustAsync(BeUniqueTitle).WithMessage(...).WithErrorCode("Unique")`, avec `BeUniqueTitle` qui fait `!await _context.TodoLists.AnyAsync(l => l.Title == title, ct)`.

Remarque : le DTO d'entrée de l'API est la commande elle-même (pas d'attributs de contrat à part), donc toute évolution du handler est une évolution du contrat HTTP.

- Emplacement des tests : projets par couche (`tests/<Projet>.Tests/...`) qui reproduisent l'arborescence des features ; fonctionnels = un fichier par use case sous `tests/Application.FunctionalTests/<Feature>/Commands|Queries/`.

## 5. Outillage

### 5.1 Options du template (.template.config/template.json)

- `ClientFramework` (choice Angular|React|None, défaut Angular, template.json:228-) ; calculés `UseAngular`, `UseReact`, `UseApiOnly`. Raccourcis CLI `--client-framework` / `-cf` (dotnetcli.host.json).
- `Database` (choice postgresql|sqlite|sqlserver, défaut sqlite, template.json:260-) ; `UsePostgreSQL`/`UseSqlite`/`UseSqlServer`. `--database` / `-db`.
- 12 symboles de ports (`kestrelHttpPort`, `appHostHttpPort`, ... avec générateur `port` + `coalesce`) ; `caPackageVersion`, `caRepositoryUrl`, `caDocsUrl` (constantes substituées dans README-template.md).
- Mécanique : directives `#if (UseXxx)` / `<!--#if-->` / `//#if` directement dans les sources du dépôt (le dépôt reste compilable dans sa variante SQLite/Angular), `modifiers` d'exclusion par condition, renommages (`ClientApp-React` -> `ClientApp`, `appsettings.SQLite.json` -> `appsettings.json`, `Web-webapi.http` -> `Web.http`, `README-template.md` -> `README.md`).
- Dérive : template.json:296-370 (exclusions ligne 309, modifiers ligne 321, `Pages` ligne 350, inclusions ligne 362) exclut/inclut des fichiers qui n'existent plus (`tests/Application.FunctionalTests/PostgreSQLTestcontainersTestDatabase.cs`, `SqlTestDatabase.cs`, `SqliteTestDatabase.cs`, `appsettings*.json` de tests, `src/Web/Pages/**`) ; `git ls-files | grep -E "PostgreSQLTest|SqliteTest|SqlTest"` ne renvoie rien. dotnetcli.host.json:24-28 déclare `appHostMcpHttpPort` / `appHostMcpHttpsPort` sans symbole correspondant dans template.json. Web.http (src/Web/Web.http:6-16) appelle `/Identity/Account/Login` qui n'existe plus (l'API Identity est sous /api/Users).
- Pas d'options : authentification, exemples (Todo/Weather toujours générés), mode sans HTTP, forme de feature.

### 5.2 Item template (templates/ca-use-case)

- `dotnet new ca-usecase --name <UseCase> --feature-name <Feature> --usecase-type command|query [--return-type T]` (templates/ca-use-case/.template.config/template.json:11-97). À lancer dans `src/Application` (README-template.md:35-47). Génère un seul fichier contenant record + validateur + handler (templates/ca-use-case/FeatureName/Commands/CleanArchitectureUseCase/CleanArchitectureUseCase.cs:1-44) avec `throw new NotImplementedException()`. Pas d'endpoint, pas de test, pas de doc générés. Le gabarit n'est pas dans la solution générée (le template racine exclut `templates/**/*`, .template.config/template.json:296-300) mais empaqueté par le nuspec (CleanArchitecture.nuspec:33).

### 5.3 Scripts, CLI, Aspire, Docker, front-ends

- Pas de CLI propre ni de lanceur `Tasks.cs`. Scripts PowerShell uniquement : build/build.ps1 (build, `npm ci`+`npm run build` des clients, tests ; cible `Basic` = sans acceptation), build/repack.ps1 (pack local + `dotnet new install`), build/test.ps1 (matrice 3x3 : génère, build, tests ; exigent pwsh). Ils sont exclus du package (nuspec:33).
- Aspire : `AppHost` (src/AppHost/Program.cs:1-48) déclare `AddAzureContainerAppEnvironment("aca-env")` (déploiement Azure Container Apps), la base (PostgreSQL flexible server / SQL Server Azure en `RunAsContainer` persistant ; SQLite via CommunityToolkit), le projet Web (`WithExternalHttpEndpoints`, URL Scalar) et, en mode run seulement, l'app JavaScript `./../Web/ClientApp` (`WithRunScript("start")`). `aspire.config.json` pointe l'AppHost pour l'Aspire CLI. `AspireExtensions.WithAspNetCoreEnvironment` (src/AppHost/Extensions.cs:3-17) fixe l'environnement (Development en run, Production en publication). `ServiceDefaults` : OpenTelemetry, health checks, service discovery, résilience HTTP (src/ServiceDefaults/Extensions.cs:17-60).
- Docker : pas de Dockerfile ni docker-compose ; Docker n'est requis que pour PostgreSQL et SQL Server (ADR-002, README.md:21-23). Dev container (.devcontainer/devcontainer.json : image python:3.13-bullseye avec docker-in-docker, 8 Go).
- Front-ends : Angular (src/Web/ClientApp, NgModule `app.module.ts`, Angular 22.2, TypeScript 6.0, zone.js, tests Vitest + jsdom, 2 specs) ; React (src/Web/ClientApp-React, JSX non typé, React 19, Vite 8, ESLint 10, aucun test) ; les deux utilisent NSwag (`"nswag": "latest"` non épinglé, package.json) pour générer `web-api-client` depuis `wwwroot/openapi/v1.json` (OpenAPI écrit à la build par `Microsoft.Extensions.ApiDescription.Server`, src/Web/Web.csproj:8-9, 26-29) avant `start` et `build`. Le client généré n'est pas versionné. Le projet Web publie le SPA via une cible `PublishRunWebpack` (npm install + build).
- OpenAPI : `AddOpenApi` + transformateurs (erreurs 400/401/403 ajoutées partout, src/Web/Infrastructure/ApiExceptionOperationTransformer.cs:13-30 ; résumés pour l'API Identity, IdentityApiOperationTransformer.cs:13-41) ; interface Scalar sur `/scalar` (Program.cs:35-36).
- Dépendances : Renovate (renovate.json : lundi, automerge minor/patch, groupes .NET/Angular/React/tests, TypeScript Angular figé en major/minor).

## 6. Tests

| Type | Outils | Volume | Docker ? |
|---|---|---|---|
| Domain.UnitTests | NUnit 5.0.0, Shouldly | 6 tests (ColourTests.cs) | non |
| Application.UnitTests | NUnit, Moq, Shouldly | 7 méthodes, 8 exécutions : LoggingBehaviour (2), ValidationException (3), mapping AutoMapper (1 + 1 `TestCase` x 2) | non |
| Application.FunctionalTests | NUnit, Aspire.Hosting.Testing, Respawn 7, Moq, Shouldly | 19 tests : 8 fichiers, 7 commandes + `GetTodos` ; `WebApplicationFactory<Program>` ; `IUser` remplacé par un Moq (WebApiFactory.cs:17-28) | SQLite non (ADR-002 le dit, non exécuté ici) ; PostgreSQL/SQL Server oui |
| Infrastructure.IntegrationTests | NUnit | 0 | -- |
| Web.AcceptanceTests | Reqnroll + Playwright (Chromium), Aspire testing builder sur l'AppHost réel | 6 scénarios (Counter 2, Home 1, Login 2, Weather 1) | oui pour PostgreSQL/SQL Server + navigateur Playwright |
| Angular | Vitest (`@angular/build:unit-test`) | 2 specs (compteur ; l'intercepteur se limite à "should be created") | non |
| React | -- | 0 | -- |

- Mécanique fonctionnelle : tests/Application.FunctionalTests/FunctionalTestSetup.cs:14-46 démarre le `TestAppHost` (base seule) via `DistributedApplicationTestingBuilder`, attend la santé de la base, injecte la chaîne de connexion à la `WebApiFactory`. `TestBase.SetUp` appelle `TestApp.ResetState` (Respawn, tests/Application.FunctionalTests/Infrastructure/TestBase.cs:5-9). Les tests envoient les commandes par `ISender` (tests/Application.FunctionalTests/Infrastructure/TestApp.cs:16-32), pas par HTTP : `grep CreateClient|HttpClient tests` ne trouve que la config de résilience des tests d'acceptation. Donc routes, codes de statut, ProblemDetails et `RequireAuthorization` ne sont testés par aucun test .NET non-navigateur.
- Pas de Testcontainers (ADR-002 le rejette explicitement). Un commit l'a retiré (`git log -S"Testcontainers"` : a824c72 "migrate Application.FunctionalTests to Aspire").
- Lacunes mesurées par grep : aucun test de l'événement de domaine `TodoItemCompletedEvent`, aucun test 403/`ForbiddenAccessException`, aucun test du `PerformanceBehaviour` ni de l'`AuthorizationBehaviour` isolé (seul le refus anonyme de GetTodos, GetTodosTests.cs:50-58).
- Couverture : `coverlet.collector` 10.1.0 est référencé dans 4 projets de tests mais aucun workflow, script ni runsettings ne collecte ou ne mesure la couverture ; aucun seuil (`grep -i coverage` hors Angular : rien).
- CI : .github/workflows/build.yml:25-66 (sur PR vers main) = restore, build Release, installe Playwright, `dotnet test --no-build` sur toute la solution (acceptation incluse). .github/workflows/test-templates.yml:11-77 (sur push main et manuel) génère les 9 combinaisons client x base, construit, construit le client npm et lance les tests. CodeQL hebdomadaire et sur PR/push (.github/workflows/codeql.yml).

## 7. Vie du projet

- Pas de CHANGELOG. Le texte des releases est le corps de la Release GitHub, recopié dans le nuspec par .github/workflows/release.yml:38-50 ; la version vient du tag (`GITHUB_REF_NAME#v`, release.yml:28-36, injectée dans `caPackageVersion` par `jq`), `nuget pack` + `nuget push` sur NuGet.org (release.yml:52-62). Déclencheur : Release GitHub publiée. Aucun contrôle automatique de contenu entre le tag et le template.
- Cadence des tags v10.x : v10.0.0 2025-11-10 ; puis 15 tags entre 2026-02-28 et 2026-03-24 (v10.1.0 à v10.8.0) ; aucun depuis. HEAD est au 2026-10-26.
- Docs : site externe (cleanarchitecture.jasontaylor.dev, README.md:11) ; dans le dépôt, seulement docs/decisions/ (ADR 000 modèle, 001 EF Core dans Application, 002 Aspire, 003 MediatR.Contracts dans Domain), ajoutées le 2026-03-28 (f934abb). 3 ADR réels, pas de test qui les tienne.
- Licences : MediatR 14.2.0 et AutoMapper 16.2.0 sont en versions récentes ; aucune clé de licence ni mention de licence commerciale dans le dépôt (`grep -i licensekey` : rien). Je n'ai pas vérifié la licence de ces paquets : à contrôler avant de tirer une conclusion.

## 8. Différences main contre v10.8.0

`git diff --stat v10.8.0..HEAD` brut : 56 fichiers, +6138/-14622, mais l'essentiel est du bruit. Deux sources : (a) fins de ligne CRLF -> LF (`.gitattributes` ajouté, `* text=auto`) qui réécrit build.yml, Program.cs, Infrastructure/DependencyInjection.cs, les csproj, etc. (`file`: release = CRLF, main = LF) ; (b) lockfiles npm (Angular : -17611 lignes). Avec `--ignore-cr-at-eol` : 45 fichiers hors lockfiles, +457/-262, dont 5 fichiers C#/csproj seulement.

Commits non publiés : 58 (`git rev-list --count v10.8.0..HEAD`). Hors `chore(deps)`/`fix(deps)` (51), 7 commits :
1. f934abb (2026-03-28) Add architectural decision records (#1595) : docs/decisions/ ADR 000-003, lien dans README.md et README-template.md.
2. b167c9c (2026-10-12) Upgrade to Aspire 13.6 (#1656) : Aspire.AppHost.Sdk 13.2.0 -> 13.6.4, `AspireUseCliBundle=true`, retrait du paquet `Aspire.Hosting.AppHost` (AppHost.csproj, TestAppHost.csproj), `aspire.config.json` remplace `.aspire/settings.json`, README : `aspire run` recommandé.
3. cae3a11 (2026-10-12) Default to Production environment when publishing (#1661) : src/AppHost/Extensions.cs:8-13.
4. f44846e (2026-10-12) Update schedule for Renovate configuration (#1658) : "on monday".
5. 0896746 (2026-10-15) Update .NET packages to 10.0.13 and pin System.Security.Cryptography.Pkcs (#1663) : `CentralPackageTransitivePinningEnabled`, retrait de `Microsoft.Build.Tasks.Core` et `Microsoft.Build.Utilities.Core` d'Application.csproj (-2 lignes), épingle Pkcs 10.0.14.
6. c030e41 (2026-10-15) Upgrade Angular to 22 (#1662) et 947a2f6 Tidy up React client build (#1667) : Angular 21.1 -> 22.2, builder `@angular-devkit/build-angular` -> `@angular/build`, Karma/Jasmine -> Vitest + jsdom (karma.conf.js, src/test.ts supprimés), TypeScript 5.9 -> 6.0, `platformBrowser` remplace `platformBrowserDynamic`, `provideHttpClient(withXhr(), ...)`, imports `src/...` -> relatifs, `allowScripts` npm ; côté React : ESLint 9 -> 10, `quietDeps` SCSS.

Autres écarts : actions GitHub (checkout v6->v7, setup-dotnet v5->v6, setup-node v6->v7, cache v5->v6, setup-nuget v2->v4), `setup-dotnet` ajouté à codeql.yml, devcontainer docker-in-docker 2 -> 4, `global.json` SDK 10.0.201 -> 10.0.403, tous les paquets NuGet (EF/ASP.NET 10.0.5 -> 10.0.14, nunit 4.5.1 -> 5.0.0, Moq 4.20.72 -> 4.21.0, Playwright 1.58.0 -> 1.64.1, OpenTelemetry 1.15 -> 1.20, Aspire 13.2.0 -> 13.6.4), README.md réécrit (tableau d'options, section Docker, suppression du paragraphe sur la suppression/recréation de base au démarrage, bien que le code le fasse toujours).

Comportement C# runtime : aucun changement, hormis la sélection de l'environnement dans l'AppHost (src/AppHost/Extensions.cs). Les use cases, l'API, le pipeline et les tests .NET sont identiques à v10.8.0. Les 7 commits de fond ne sont donc pas livrés aux utilisateurs du template installé depuis NuGet.

## 9. Points forts et faiblesses (avec preuves)

Points forts
- Parcours d'adoption : `dotnet new ca-sln -cf ... -db ...` + `aspire run`, 9 variantes testées en CI à chaque push (test-templates.yml:16-21).
- Tests fonctionnels contre une vraie base, sans Docker pour SQLite (ADR-002), avec Respawn : isolation propre (TestBase.cs:5-9).
- Forme du use case simple et uniforme (fichier unique record + handler), endpoints minimaux découverts par réflexion et nommés pour la génération de client (EndpointRouteBuilderExtensions.cs:20-62).
- Chaîne front : client TypeScript généré depuis OpenAPI (nswag.json), Aspire lance API + front + base ensemble (src/AppHost/Program.cs:25-45).
- Gestion de dépendances automatisée (Renovate, versions centralisées, épingle de sécurité documentée).

Faiblesses
- Aucune garde mécanique de l'architecture (section 1.2).
- Pas de propriété des données ni de pagination (sections 2.2 et 3) ; le gabarit montre un modèle où tout utilisateur voit tout.
- Doc contre code : ADR-003 (événements après SaveChanges) contredit DispatchDomainEventsInterceptor.cs:25-30 (avant l'écriture) ; template.json et dotnetcli.host.json font référence à des fichiers et symboles disparus ; Web.http appelle une route disparue.
- Base recréée à chaque démarrage en Development, sans migration (ApplicationDbContextInitialiser.cs:45-46) : pas de chemin de production prévu hors `EnsureCreated`.
- Tests : HTTP non testé en .NET, couverture non mesurée, projet d'intégration vide, événements de domaine non testés.
- Release : 58 commits, dont Angular 22 et Aspire 13.6, hors de la dernière release publiée depuis 7 mois.
