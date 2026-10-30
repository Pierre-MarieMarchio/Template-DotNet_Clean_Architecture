# Carte fusionnée de NOTRE template (clone `develop`, HEAD `5f22faa5`)

Sources fusionnées : `/tmp/claude-1000/-home-pmmarchio-code-Template-DotNet-Clean-Architecture/608f3f2d-3c02-4530-9e46-6f8e7e0e445b/scratchpad/out/p1-map-ours-A.md` (A) et `.../p1-map-ours-B.md` (B).
Clone : `/tmp/claude-1000/-home-pmmarchio-code-Template-DotNet-Clean-Architecture/608f3f2d-3c02-4530-9e46-6f8e7e0e445b/scratchpad/repos/ours` (lecture seule, aucun build ni test lancé ; chemins relatifs à ce clone).
Méthode : chaque fait en désaccord entre A et B a été relu dans le code ou recalculé par commande (section 9). Les faits sur lesquels A et B s'accordent ont été contrôlés par sondage (comptes, références, lignes), pas un à un.

Réserve d'état : `git status` montre `.template.config/template.json` modifié (`templateVersion` `1.0.0` en HEAD, `1.1.0-dev` dans l'arbre ; `git diff` : 1 ligne). Tag unique `v1.0.0`.

---

## 1. Vue d'ensemble

- Un dépôt à la fois solution .NET 10 qui compile telle quelle (les conditions `dotnet new` sont des commentaires : `//#if (todoLists)`, `/*#if ... #endif*/`, `<!--#if-->`) et paquet `dotnet new` (`.template.config/template.json`, 2 275 lignes, 104 symboles, 25 `sources`, 5 `postActions`).
- Clean Architecture : **14** projets sous `Src/` (pas 15), 4 couches plus `Clients`, chaque couche avec un projet `*.Core` sans feature. Les hôtes autres que l'API (Portal Blazor Server, Mcp, Worker, Avalonia, WPF) sont des starters sous `.template.config/starters/<x>/` (11 dossiers de starters : angular, avalonia, blazor-server, blazor-wasm, mcp, react-router, vite-react, vite-svelte, vite-vue, worker, wpf), créés à la génération ou au bootstrap.
- Pas de médiateur ni de behaviours (ADR 0003) ; `Result<T>` + `Error` pour l'attendu ; exceptions pour les bugs ; domaine non mappé par EF (ADR 0005) ; événements après commit sans outbox (ADR 0006) ; propriété dans la requête et 404 jamais 403 (ADR 0009, avec un écart de code, voir D4).
- Taille de `Src/` hors `Generated/` et `Migrations/` : 34 352 lignes (Domain 39 fichiers / 1 572 ; Application 506 / 11 956 ; Infrastructure 210 / 12 480 ; Presentation 200 / 7 830 ; Clients 12 / 514). `Tests/` : 563 fichiers / 71 084 lignes.

## 2. Projets, références, règles

### 2.1 Projets `Src/` (14) et références de projet (relues dans les `.csproj`)

| Projet | Références projet |
|---|---|
| `Domain/AppTemplate.Domain.Core` | aucune |
| `Domain/AppTemplate.Domain` | Domain.Core |
| `Application/AppTemplate.Application.Core` | Domain.Core |
| `Application/AppTemplate.Application` | Domain, Application.Core |
| `Application/AppTemplate.Application.Auth` | Application.Core seul |
| `Infrastructure/...Infrastructure.Core` | Application.Core |
| `Infrastructure/...Infrastructure.Persistence` | Application, Application.Core, Infrastructure.Core |
| `Infrastructure/...Infrastructure.Auth` | Application.Core, Application.Auth, Infrastructure.Core |
| `Infrastructure/...Infrastructure.Email` | Application, Application.Core, Infrastructure.Core |
| `Infrastructure/...Infrastructure.Storage` | Application, Application.Core |
| `Infrastructure/...Infrastructure.InMemory` | Application, Application.Core, Application.Auth |
| `Presentation/AppTemplate.Presentation.Core` | Application.Core (jobs `BackgroundScope`/`PeriodicJob`, observabilité, HTTP sortant, `AuthorizationPolicies`, options OIDC externes, `NoCallerCurrentUser`) |
| `Presentation/AppTemplate.Api` | Application, Application.Core, Application.Auth, Presentation.Core, et **cinq** modules d'infra (Email, Storage, Auth, Core, Persistence). **Pas InMemory** : A et B écrivent « tous les modules d'infra », c'est inexact. |
| `Clients/AppTemplate.Client` | aucune (client Kiota ; `Generated/` = 151 `.cs`, le projet en compte 163) |

Front : espace pnpm `Src/Web` (`apps/PlaceholderSite`, `apps/FrameworkSite`, `packages/{api-client,eslint-config,tsconfig}`).
Tests : 18 projets sous `Tests/`, un miroir par projet source, plus `Tests/Load/smoke.js` (k6) et `Launcher/CleanArch.Create.Tests`.

### 2.2 Règles de build

- `Directory.Build.props` : `net10.0`, `LangVersion latest`, `Nullable`, `ImplicitUsings`, `InvariantGlobalization`, `EnableNETAnalyzers`, `AnalysisLevel latest-recommended`, `EnforceCodeStyleInBuild`, `TreatWarningsAsErrors`, `NuGetAudit` (all/low), `Deterministic`, `GenerateDocumentationFile`, **`<NoWarn>$(NoWarn);CS1591;CS1573</NoWarn>` (ligne 27)**, versions centralisées. `Directory.Build.targets` : tout `IsTestProject` devient `Exe`. `global.json` : SDK 10.0.100, runner Microsoft.Testing.Platform.
- `.editorconfig` (190 lignes) : namespaces file-scoped en erreur, `using` hors namespace (warning), `CS8618`/`CS8602` en erreur, `IDE0005` warning (donc erreur par `TreatWarningsAsErrors`), champs `_camelCase` (warning), `I*` et `T*` (erreur). 25 lignes `severity = none` : trois globales (`CA2007`, `CA1848`, `CA1515`, lignes 57-59, avec raison en commentaire) et le reste par fichier, chacun avec sa raison en commentaire (D7).
- Aucun analyseur tiers dans `Directory.Packages.props` ; les `S....`/`AV....` ne servent qu'avec Sonar.
- Outils locaux `.config/dotnet-tools.json` : dotnet-ef 10.0.14, dotnet-sonarscanner 12.0.0, kiota 1.36.1.

### 2.3 Tests d'architecture (`Tests/Architecture/AppTemplate.Architecture.Tests`)

xUnit v3 + NetArchTest.Rules 1.3.2 + Shouldly. 43 fichiers (`Rules/` 34, `Composition/` 3, `Fixtures/` 6), 8 899 lignes, 195 `[Fact]/[Theory]`.

- Dépendances : `LayerDependencyTests` (domaine : ni Application, Infrastructure, Presentation, EF, AspNetCore, DI, FluentValidation, Npgsql, MailKit, System.Text.Json ; application : ni EF, AspNetCore, Npgsql, MailKit, **MediatR**), `ModuleDependencyTests` (17 tests), `PackageBoundaryTests`, `PortabilityTests`, `FeatureIsolationTests`.
- Use cases, domaine, ports : `UseCaseConventionTests` (population mesurée sur disque par `SourceDeclarations`), `DomainModelTests`, `DomainEventTests`, `PortConventionTests` (plafond `_maximumOperationsPerPort = 6`), `StorageVocabularyTests`, `StorageShapeTests`, `AdapterVisibilityTests`.
- Layout : `LayoutConventionTests` (698 l.), `FeatureFolderVocabularyTests`.
- HTTP : `HttpSurfaceTests` (12 actions `[AllowAnonymous]` dans `_anonymousActions` + 4 endpoints anonymes « fluents » comptés à part : OpenAPI, Scalar, 2 health ; pas de header `Link`, pas de `Deprecation`/`Sunset`), `IdempotentActionTests`, `RequestPipelineTests`, `OutboundHttpTests`, `ErrorCodeTableTests`, `CollectionContractTests`.
- Composition : `ContainerCompositionTests` (15 tests, 431 l.), `SharedInstanceRegistrationTests`, `BackgroundWorkTests` (aucun `BackgroundService` ne prend `ILeaderLease`, le bail est pris par un use case).
- Hygiène : `ConfigurationSurfaceTests`, `DocumentationCitationTests`, `ContainerImagePinningTests`, `CoverageFloorTests`, `CoverageSettingsTests`, `PullRequestTemplateTests`.
- Spécifique template (exclus du paquet) : `TemplatePackagingTests` (1 099 l.), `AcceptanceSelectionTests`.
- **Contre-épreuves** : 17 méthodes nommées `...IsSensitive...` réparties sur 16 fichiers de `Rules/` prouvent que la règle sait échouer (ex. `LayerDependencyTests`, `FeatureIsolationTests`, `BackgroundWorkTests`, `PortConventionTests`) ; `RuleAssertions.RequireTypes` refuse un ensemble vide. **Ce n'est pas universel** : `UseCaseConventionTests`, `DomainModelTests`, `HttpSurfaceTests`, `StorageShapeTests`, `StorageVocabularyTests`, `ErrorCodeTableTests`, `ConfigurationSurfaceTests` n'en ont pas (D5).

### 2.4 Garde-fous hors tests

`Tools/` : `Tasks.cs` 2 134 l. ; trois générateurs `NewFeature.cs` 5 891, `NewUseCase.cs` 1 443, `RemoveFeature.cs` 1 670 (9 004 l.) ; garde-fous `CoverageGate.cs` 612, `CheckDocPaths.cs` 675, `CheckWorkflows.cs` 953, `CheckComments.cs` 1 396, `TestSummary.cs` 206 (14 980 l. au total pour 9 fichiers). `verify` (`Tools/Tasks.cs:651-688`) : migration initiale exigée, `Gates` (self-tests + vérifs), `api-client --check`, `web-lockfiles --check` (template), restore, `dotnet format --verify-no-changes`, build, test, `dotnet ef migrations has-pending-model-changes` (AppDbContext, AuthDbContext), paquets vulnérables, porte du lanceur (template). `CheckComments` refuse tout commentaire `//` hors marqueurs de template : relecture confirmée, `Src/` hors `Generated/` contient **0** commentaire `//` de prose (321 marqueurs `//#`, 319 lignes `///` XML-doc de l'API).

## 3. Chemin d'une requête

Exemple TodoLists (forme riche). Références de ligne vérifiées sur les fichiers (A exact ; les numéros de B sont hors plage, voir D11).

### 3.1 Écriture `POST /api/v1/todo-lists`

1. `Src/Presentation/AppTemplate.Api/Program.cs` : `UseHttpPipeline` (`Common/Hosting/HttpPipelineModule.cs` : forwarded headers, en-têtes de sécurité et de cache, journal de requêtes, limites, `UseExceptionHandler` = `GlobalExceptionHandler`, status pages, CORS, rate limiter, langue, timeouts), puis `UseAuthentication`, `UseAuthorization`, `MapControllers`. Politique de repli `RequireAuthenticatedUser` (`Program.cs:111-115`) ; en `--auth none` repli `RequireAssertion(_ => true)` et démarrage refusé si un `[Authorize]` subsiste sans `[AllowAnonymous]`.
2. `Common/Idempotency/IdempotencyFilter.cs` (`IAsyncResourceFilter`) : `[Idempotent]` + `Idempotency-Key`, empreinte SHA-256 de endpoint+corps (`:149`), `ClaimAsync` (bail par utilisateur, ADR 0010), rejoue (`Idempotency-Replayed`).
3. `Features/TodoLists/Controllers/TodoListsController.cs:64-80` (`Create`) : `CreateTodoListRequest` -> `CreateTodoListCommand` -> `ICreateTodoListUseCase` -> `TodoListResponseMapping.ToListResponse` -> `CreatedOrProblem` (`Common/Controllers/ApiControllerBase.cs:82-99` : `ETag`, `CreatedAtRoute`, ou `ErrorMapping.ToActionResult`, `Common/Errors/ErrorMapping.cs`, 84 lignes, `application/problem+json` avec extension `code`).
4. Use case `Src/Application/AppTemplate.Application/Features/TodoLists/UseCases/Commands/CreateTodoList/CreateTodoListUseCase.cs:13-47` : `currentUser.RequireUserId()` (401 `auth.required` avant toute validation) -> `validator.EnsureValidAsync` (FluentValidation) -> `TodoList.Create` -> `repository.Add` -> `unitOfWork.SaveChangesAsync` -> `TodoListDtoMapping.Detail`.
5. Domaine `Domain/Features/TodoLists/Model/Entities/TodoList.cs:28-36` : `Guid.CreateVersion7()`, `TodoListName.Create` (lève `DomainException`), `RaiseDomainEvent(TodoListCreatedDomainEvent)`. `Rehydrate` (ligne 38) ne lève rien. Plafond `MaxItems = 500`.
6. `Infrastructure.Persistence/Features/TodoLists/Access/Repositories/TodoListRepository.cs:68-77` : `mapper.ToNewRecord` -> `context.TodoLists.Add(record)` -> `tracker.Track(aggregate, record)` (`Rows/Tracking/TodoListTracker.cs`, 92 lignes, dérive de `AggregateTracker`).
7. `Infrastructure.Core/Common/Saving/EfUnitOfWork.cs:9-21` (`DbUpdateConcurrencyException` -> `ConcurrencyConflictException`). Intercepteurs branchés par `PersistenceModule.cs:251-253` (`AddContext`) dans l'ordre : `UseAggregateFlushing`, `UseAuditStamping`, `UseDomainEventDispatch`. Flush : chaque tracker écrit l'agrégat dans ses lignes, puis recopie version et audit ; audit : `CreatedAt/By`, `LastModifiedAt/By` ; dispatch : `Collect` à `SavingChanges`, publication à `SavedChanges`, restitution des événements à `SaveChangesFailed`. Version : colonne système `xmin` sous PostgreSQL (`Infrastructure.Core/Common/Contexts/DatabaseProvider.cs:68-77`) ; sous SQL Server/SQLite un compteur estampillé par `VersionStampingSaveChangesInterceptor` (branché seulement par les variantes de base, absent du `PersistenceModule` par défaut).
8. Après commit : `DomainEventDispatcher.cs` (46 lignes) résout les `IDomainEventConsumer<TEvent>` ; un consommateur qui lève est journalisé et sauté.
9. Retour : 201 + `Location` + `ETag`. Échecs : `Result.Failure` -> statut de l'`ErrorType` ; `ConcurrencyConflictException` -> 409 `concurrency.conflict` ; `DomainException` non rattrapée -> **400** `domain.invariantViolated` ; autre exception -> 500 `server.unexpected` (`GlobalExceptionHandler.cs:19-47`).

Mise à jour : `ReadPrecondition` lit `If-Match` (`ApiControllerBase.cs:109-122`, 428 si requis et absent, 412 si périmé) ; `ITodoListService.LoadOwnedAsync` (`Services/TodoListService.cs:13-30`) -> `OwnedAggregate.Require` (`Application.Core/Common/Ownership/OwnedAggregate.cs:10-31`, absent ou autre propriétaire = même `NotFound`) ; les appels à l'agrégat sont enveloppés par `DomainGuard.Try` (`Application.Core/Common/Validation/DomainGuard.cs`) qui renvoie `CommonErrors.InvariantViolated` = `Error.Conflict("domain.invariantViolated")`, soit **409** (D3).

### 3.2 Lecture paginée `GET /api/v1/todo-lists`

`TodoListsController.cs:26-47` -> `GetTodoListsRequest` (strings/int nullables, sans validation dans le contrat) -> `GetTodoListsUseCase` (`RequireUserId`, `GetTodoListsRequestBinder.Bind`) -> `CollectionBinding.Bind` (`Application.Core/Common/Collections/CollectionBinding.cs:8-42`) : `CollectionOrder.Parse` avec `TodoListCollectionPolicy` (champs `name` et `createdAt` triables par curseur, `lastModifiedAt` en offset seul ; tri par défaut `createdAt:desc` ; page 20, max 100 ; 3 termes de tri), `TodoListFilter.Create`, `PageRequest.Create` (offset XOR curseur) -> port `ITodoListQueries.GetForOwnerAsync` -> `Persistence/Features/TodoLists/Access/Queries/TodoListQueries.cs:16-35` : `AsNoTracking().Where(OwnerId == ownerId)`, `LIKE` insensible à la casse avec échappement, offset (`COUNT` + `Skip/Take`) ou keyset (`Take(pageSize+1)`, `TodoListSortMap`, `Cursor.After(...).Encode()`). Le curseur est un JSON `{f,d,k,i}` en Base64Url, **non signé**, 512 caractères max (`Cursor.cs`, 149 lignes). Projection directe en `TodoListSummaryDto`, sans charger l'agrégat. Réponse `PagedResponse` (`Items, PageSize, Page, TotalCount, TotalPages, HasNextPage, NextCursor`), pas de `Link`. Le détail `GET {id}` renvoie `ETag` et honore `If-None-Match` (304).

## 4. Concepts

| Sujet | Réponse du code |
|---|---|
| CQRS | léger : dossiers `Commands/` et `Queries/` ; écritures par l'agrégat + repository + `IUnitOfWork` ; lectures par un port `*Queries` qui projette sans passer par l'agrégat ; même base, pas de modèle de lecture séparé. |
| Médiateur / pipeline | aucun ; MediatR interdit par test ; validation, journal et commit écrits dans chaque use case. Transversal HTTP = middlewares et filtres ; transversal persistance = intercepteurs EF. |
| Enregistrement DI | **hybride** (D8) : chaque feature est appelée explicitement (`ApplicationModule.AddTodoLists` -> `AddFeature`), mais ses use cases et validateurs FluentValidation sont trouvés par scan de l'assembly sur le préfixe de namespace `Features.<Feature>.` ; `AddUseCases` enregistre en `Scoped` sous l'unique interface nommée de chaque `IUseCase` et lève s'il y en a 0 ou 2. |
| Validation | FluentValidation appelée à la main (`EnsureValidAsync`) -> `request.validationFailed` avec `Details` par champ ; invariants du domaine redoublés (`DomainException`). |
| Erreurs | `Result`/`Error` (8 `ErrorType`) pour l'attendu ; `DomainException` non attendue -> 400 ; `DomainGuard.Try` -> `Result` 409 ; `ConcurrencyConflictException` -> 409 ; reste -> 500. Table des codes dans `docs/reference/api.md`, testée dans les deux sens (`ErrorCodeTableTests`), statuts par `ErrorCodeStatusTests` pour les `Error` des classes `*Errors` seulement. |
| Événements | levés dans l'agrégat, drainés avant la sauvegarde, publiés en processus après commit, au plus une fois, sans outbox (ADR 0006) ; garantie réelle = balayage sous bail (ADR 0011/0025). Les consommateurs appellent eux-mêmes `SaveChangesAsync` (deux commits distincts). |
| Entités | `Entity<TId>`, `AggregateRoot<TId>`, `AuditableAggregateRoot<TId>` ; `UserId` = `record` scellé à constructeur privé, `Create` refuse `Guid.Empty` ; ids `Guid.CreateVersion7()`. |
| Mapping | à la main : `...DtoMapping` (domaine -> DTO), `...ResponseMapping` (DTO -> réponse), `...Mapper` (agrégat <-> ligne EF). Ni AutoMapper ni Mapster. |
| Autorisation | repli authentifié ; une politique nommée `Administrator` (rôle `Administrator`, `AuthComposition.cs:49`, ou rôle OIDC configuré) posée sur **5 contrôleurs** (`ExternalIdentityAdministration`, `SecurityEventMaintenance`, `AccountAdministration`, `RefreshTokenMaintenance`, `Maintenance`) ; `[AllowAnonymous]` sur 12 actions, liste fermée testée ; pas de tenants ni de permissions fines. |
| Propriété | `ICurrentUser` jamais pris du corps ; 404 jamais 403 ; chemins de lecture et forme simple (`NoteStore`) filtrent par propriétaire dans le SQL ; **le chemin agrégat charge par id puis compare en mémoire** (D4). |
| Concurrence | `ETag` = version ; `If-Match` -> `VersionPrecondition` ; 412/428 ; ADR 0008. |
| Idempotence | `Idempotency-Key` sur POST `[Idempotent]`, table `IdempotencyRecord`, purge `PurgeExpiredIdempotencyKeysUseCase`. |
| Suppression de compte | consommateur de `UserAccountDeleted` + `IDeletedAccountDataPolicy` + balayage ; chaque feature a une requête qui traverse les propriétaires exprès (`ListDataOwnerIdsAsync`, ADR 0012). |

## 5. Conventions

- Organisation par feature à l'intérieur de chaque couche : `Common/` (sans feature) + `Features/<Feature>/` + un fichier de composition à la racine. Mots de dossier en liste fermée (`FeatureFolderVocabularyTests`, `LayoutConventionTests`) : Domain `Model/{Entities,ValueObjects,Events}`, `Repositories/` ; Application `UseCases/{Commands,Queries}/<Op>`, `Ports/<Port>`, `Model/{Dtos,Errors,Mapping,Policies}`, `Services/(Consumers)`, `Helpers/` ; Persistence `Access/{Queries,Repositories,Stores}`, `Rows/{Configurations,Mapping,Models,Tracking}`, `Adapters/` ; Api `Controllers/`, `Contracts/{Requests,Responses,Mapping}`, `Jobs/`, `Composition/`.
- Deux formes de feature (ADR 0002) : riche (agrégat) = TodoLists, Reminders, Files ; simple (service sur store) = Notes. Les use cases : **33** dans `Application` (TodoLists 16, Files 12, Reminders 5), 42 dans `Application.Auth`, 2 dans `Application.Core` ; chaque use case = 2 fichiers de classe/interface plus commande/requête et validateur, d'où 66 fichiers `*UseCase.cs` dans `Application` et 84 dans `Application.Auth` (D13).
- Use case : exemple complet `CreateTodoList`, dossier à 4 fichiers (commande `record`, interface `IUseCase<,>`, validateur, classe scellée à constructeur primaire) ; A et B donnent le même extrait, conforme au fichier.
- Nommage : suffixes imposés par dossier, `_camelCase`, `I*`, `T*`, un type public par fichier, namespace = dossier ; routes `api/v{version:apiVersion}/<kebab-pluriel>` ; codes d'erreur `feature.cause`. Tests : un projet miroir par projet source, un dossier par use case, méthodes `Sujet_Comportement`.
- Générateurs : `new-feature <Features> <Feature> --access owner|shared [--deletion] [--operations] [--model record|aggregate] [--concurrency none|etag]`, `new-use-case`, `remove-feature [--apply]` (manifeste `.feature-manifest/`, comparaison de hashes).

## 6. Outillage

- `dotnet new cleanarch-webapi` : `--auth local|external|none` (défaut `local`), `--database postgresql|sqlserver|sqlite` (`mysql` refusé), `--files`, `--examples all|notes|none`, `--ci`, `--sonar`, `--agent-files`, `--web none|placeholder|angular|vite-react|vite-vue|vite-svelte|react-router|blazor-wasm` (7 valeurs de frameworks ou de page statique hors `none`), `--web-name`, `--web-audience`, `--web-rendering`, `--presentation` (multi : api, blazor-server, mcp, avalonia, wpf, worker). Combinaisons invalides = symbole calculé `refusedOptions` (lignes 84-87). **5 `postActions`** (rappels de bootstrap conditionnels : API ou non, scaffolds, client .NET, espace web) (D2).
- Item templates : `cleanarch-webapp`, `cleanarch-webpackage`, `cleanarch-presentation` (dossiers `.template.config/{webapp,webpackage,presentation}`).
- `Tools/Tasks.cs` : restore, build, test (`--no-integration`), coverage, format, format-fix, new-feature, new-use-case, remove-feature, migration-initial, migration-add, database-update, migration-bundle, openapi, api-client, run, compose-up, compose-down, end-to-end, sonar, bootstrap, presentation-scaffold, presentation-join, web-scaffold, web-join, hygiene, verify, web-lockfiles (template seul). Aucune migration versionnée : `bootstrap` crée `InitialCreate` par contexte.
- Lanceur `Launcher/CleanArch.Create` (`PackAsTool`, `cleanarch-create`, Spectre.Console 0.57.2, manifestes embarqués) : **3 736 lignes** de C# pour l'outil, 2 711 pour `CleanArch.Create.Tests` (135 `[Fact]/[Theory]`) ; le chiffre de 6 447 donné par A et B est le total des deux (D14). Le CLI de projet `dotnet cleanarch` n'existe pas : ADR template 0017-0019 `Proposed`, issue #163 ; une branche distante `feat/cleanarch-cli-skeleton`.
- Docker : `docker-compose.yml` services `db`, `mailpit`, `presentation-presentation-app-portal`, `presentation-presentation-app-mcp`, `api`, `worker`, `objectstore` (SeaweedFS), profil `sonar` (`sonarqube`, `sonar-scanner`) ; `docker-compose.database.yml` ; images pinnées par tag et digest (`ContainerImagePinningTests`). Dockerfiles : `Src/Presentation/AppTemplate.Api/Dockerfile` (SDK 10.0.403-noble, final `aspnet:10.0.14-alpine3.24`), `Src/Web/apps/{FrameworkSite,PlaceholderSite}/Dockerfile`, et dans les starters ceux du portail, du MCP et du worker (D15).
- **Aspire absent** du code ; mention seule dans `docs/template/roadmap.md` (issue #122).
- Front : `Src/Web` (pnpm), client TypeScript généré depuis l'OpenAPI, client .NET Kiota (`Src/Clients`) pour Blazor WASM/Avalonia/WPF ; OpenAPI committé `Tests/Integration/AppTemplate.Api.IntegrationTests/OpenApi/v1.json` (11 568 lignes), tenu par `OpenApiDocumentTests`.

## 7. Tests

| Élément | Constat |
|---|---|
| Outils | xUnit v3 4.0.2 sur Microsoft.Testing.Platform, Shouldly 4.3.0, NSubstitute 6.2.0, NetArchTest.Rules 1.3.2, Testcontainers 4.16.0 (+ `PostgreSql` 4.16.0, `MsSql` 4.15.0, `MySql` 4.15.0 ; `Testcontainers.MySql` encore référencé par deux projets d'intégration alors que MySQL est refusé), `Microsoft.AspNetCore.Mvc.Testing`, CodeCoverage. Pas de Respawn, NUnit ni Moq : remise à zéro par `TRUNCATE ... RESTART IDENTITY CASCADE` (`Tests/Integration/.../Infrastructure/TestDatabase.cs`, `DatabaseServer.cs`). |
| Nombre (déclarations, recompté) | 3 321 `[Fact]/[Theory]` sous `Tests/` + 135 dans le lanceur. Par projet : Application 609, Api.Integration 413, Application.Auth 395, Domain 385, Api.Unit 323, Infra.Auth 237, Architecture 195, Persistence 185, Application.Core 139, Storage 127, InMemory 71, Email 52, Presentation.Core 50, Infra.Core 40, Domain.Core 39, Auth.Integration 27, Client 26, EndToEnd 8. Plancher CI `minimum_tests=3000` (`.github/workflows/ci.yml:110`). |
| Sans Docker | `dotnet run Tools/Tasks.cs test --no-integration` : tous les projets `IsTestProject` sans Testcontainers (environ 2 873 déclarations) ; 440 tests d'intégration exigent Docker ; `EndToEnd` (8) n'est pas `IsTestProject`, il tourne par la tâche `end-to-end` contre Compose. |
| Types | unitaires par couche, architecture, contrat (OpenAPI, table des codes), intégration HTTP sur vrai PostgreSQL, contrats d'adaptateurs rejoués sur les doubles (ADR 0020), bout-en-bout, charge k6 non bloquante, recette du template (25 jambes dans `.template.config/acceptance/matrix.json`). |
| Couverture | plancher `coverage.minimum` = 89 (% de lignes). Dernière mesure documentée : 93,63 % (14 439/15 421), Debug avec Docker, 2026-10-22 (message du commit `8200de77`, plancher 90 -> 89 présenté comme correction de mesure) ; avant : 90,46 % pour 3 259 tests (commit `556b66b5`, 2026-09-04). **Rien n'a été remesuré** : seul le plancher est vérifié dans le clone. |

## 8. Vie du dépôt

543 commits, du 2025-02-17 au 2026-10-30 (495 + 14 + 5 du mainteneur sous deux identités, 28 dependabot, 1 github-actions) ; pics : 98 le 2026-10-21, 85 le 2026-09-09, 63 le 2026-10-22 ; 20 le 29 et 2 le 30 octobre. Tag unique `v1.0.0` (2026-10-22). CHANGELOG géré par release-please (entrée 1.0.0 rédigée à la main sous « 1.0.0: what the first release contains »). Conventional Commits ; les commits `feat(template)!` (#169, #172) signalent des changements cassants. 28 ADR de projet (`docs/decisions/0001..0028`, tous `Accepted`, dont 0011 « jobs dans l'API » et 0025 « jobs dans un worker » qui coexistent) et 20 ADR du template (0017-0019 `Proposed`, 0003 remplacé par 0008). Workflows : `ci.yml` (jobs `build-test`, `repository-hygiene`, `launcher`, `hygiene`, `vulnerable-packages`, `docker`, `load`, `end-to-end`, `compose`), `template-release.yml` (release-please, pack, attach, publish nuget.org, mirror GitHub Packages, promote), `template-acceptance.yml` (25 jambes, jambe Windows, sélection par tags), `release.yml`, `codeql.yml`, `sonarqube.yml`, `web.yml`, dependabot sur 5 écosystèmes.

## 9. Constats consolidés (avec preuve)

Forces
- S1. Règles d'architecture exécutables, populations mesurées sur disque, 17 contre-épreuves (`grep Sensitive Tests/Architecture` : 17 méthodes, 16 fichiers) ; limite : pas partout (D5).
- S2. Registration DI vérifiée par `ContainerCompositionTests` (15 tests, validation stricte).
- S3. Contrat HTTP tenu par tests : OpenAPI committé, `ErrorCodeTableTests`, `api-client --check`.
- S4. Concurrence (`xmin`, `ETag`/`If-Match`), idempotence par bail, 404 pour autrui (en réponse).
- S5. Mesure de couverture documentée avec garde (`CoverageSettingsTests` ; message `8200de77`).
- S6. Chaîne de livraison du template : 25 jambes d'acceptation, actions pinnées, images pinnées par digest.

Faiblesses, frictions, écarts doc-code (tous revérifiés)
- F1. ADR 0009 contredit par le code d'agrégat (D4).
- F2. Règle « ne jamais faire taire un diagnostic » (`AGENTS.md`, `docs/guides/build-and-ci.md:70-72`, et l'en-tête de l'`.editorconfig` « never a repository-wide NoWarn ») non tenue : `#pragma warning disable ASP0000` (`HttpPipelineModule.cs:67-69`), `<NoWarn>` repo-wide (`Directory.Build.props:27`), `<NoWarn>AV0029;AV0030` (`AppTemplate.Api.csproj:16`), trois `severity = none` globaux (`.editorconfig:57-59`). Aucun test d'architecture ne l'interdit.
- F3. `domain.invariantViolated` répond 409 par `DomainGuard.Try` et 400 si non rattrapée ; `docs/reference/api.md:247` ne liste que le 409 ; aucun test ne tient le 400 (`GlobalExceptionHandlerTests` n'a pas de cas `DomainException`).
- F4. Messages de `UseCaseConventionTests.cs:58,197` disent « registration is explicit rather than scanned » alors que l'enregistrement des use cases est un scan (`ApplicationModule.cs:152-165`) ; `docs/guides/adding-a-feature.md:153` dit « Nothing is registered by hand ».
- F5. `docs/architecture/layers-and-projects.md:34` et `docs/reference/project-layout.md:201` disent « six authentication features » : `Application.Auth/Features/` en compte 7.
- F6. `README.md:58-70` omet `--database` ; `README.md:21` ne cite ni React Router ni Blazor WebAssembly.
- F7. Coût d'écriture d'un agrégat : chargement complet (`AsSplitQuery`, plafond 500), mapper + tracker + repository (ADR 0005/0006 assument).
- F8. Plancher de couverture 90 -> 89 (`8200de77`), motivé par la correction de mesure, face à la règle « never lowered to pass ».
- F9. `HttpPipelineModule.cs` et les `Program.cs` mêlent toutes les variantes par marqueurs `//#if` : lisibilité d'un fichier seul réduite.

Absences : Aspire, `dotnet cleanarch`, Respawn, NUnit, MediatR, AutoMapper, outbox, soft delete, `IQueryable` sur un port, génération MySQL, tenants et permissions fines.

## 10. Désaccords entre A et B

Légende : verdict = qui avait raison (A, B, les deux en partie, aucune).

**D1. Nombre de projets sous `Src/`.** A : « 15 projets .NET ». B : « 14 projets ». Preuve : `find Src -name '*.csproj'` -> 14 fichiers ; 14 entrées `Src\` dans `AppTemplate.sln` ; la table de A elle-même compte 14 lignes de projets. Verdict : **B**.

**D2. `postActions` du template.** A : « un seul rappel, bootstrap ». B : « 5 `postActions` ». Preuve : `template.json` lu en JSON : 5 entrées (bootstrap avec API, bootstrap sans API, scaffolds, client .NET, espace web), conditions `!refusedOptions && ...`, toutes `continueOnError`. Verdict : **B**.

**D3. Statut des violations d'invariant du domaine.** A : 409 par `DomainGuard.Try`, 400 si non rattrapée ; le 400 n'est tenu par aucun test. B : « `DomainException` -> 400 » et « les invariants d'un agrégat ne remontent pas par `Result` (400 générique, sans détail) ». Preuve : `DomainGuard.cs` renvoie `Result.Failure(CommonErrors.InvariantViolated(message))` ; `CommonErrors.cs:9-11` = `Error.Conflict`; `GlobalExceptionHandler.cs:26-31` = 400 ; `docs/architecture/domain-and-results.md:59-60` documente les deux ; `docs/reference/api.md:247` = 409 seul ; `AggregateInvariantTests.cs:25-28` assert 409 ; aucun test sur `DomainException` dans `GlobalExceptionHandlerTests.cs`. Verdict : **A** ; B n'a vu que le chemin non rattrapé et en tire une généralisation fausse.

**D4. La propriété est-elle dans la requête pour les agrégats ?** A : non, `GetAsync` charge par id seul et la propriété est comparée en mémoire (F-OWN). B : « filtre `OwnerId == caller` dans chaque requête et chaque écriture ». Preuve : `TodoListRepository.cs:24-29` `FirstOrDefaultAsync(list => list.Id == id)` ; `StoredFileRepository.cs:27` idem ; `ReminderRepository.cs:25` (par id) ; `IXRepository.GetAsync(Guid id, ...)` ne reçoit pas de propriétaire ; `OwnedAggregate.cs:20` compare `aggregate.OwnerId != ownerId` ; ADR 0009 lignes 7-9 décrit ce schéma comme celui qu'on refuse et ligne 28 promet « no code path holds another user's row ». Les chemins de lecture (`*Queries`, `NoteStore`) filtrent bien par propriétaire (`TodoListQueries.cs:23-26`, `ReminderRepository.cs:66`, `StoredFileRepository.cs:83` pour les listes). Verdict : **A** (B vrai seulement pour les lectures et la forme simple).

**D5. Contre-épreuves de sensibilité.** A : « chaque règle négative a un test jumeau `TheRule_IsSensitive_AndDetectsAViolation` ». B : « 17 tests nommés `...IsSensitive...` ». Preuve : `grep Sensitive Tests/Architecture` : 17 méthodes réparties sur 16 des 34 fichiers de `Rules/` (ex. 4 dans `BackgroundWorkTests`, 5 dans `PullRequestTemplateTests`) ; 0 dans `UseCaseConventionTests`, `DomainModelTests`, `HttpSurfaceTests`, `StorageShapeTests`, `StorageVocabularyTests`, `ErrorCodeTableTests`, `ConfigurationSurfaceTests`. Verdict : **B** ; la généralisation de A est fausse (la propriété est réelle mais partielle).

**D6. Doc sur le nombre de features d'authentification.** A : n'en parle pas. B : la doc dit « six », le code en a 7. Preuve : `ls Src/Application/AppTemplate.Application.Auth/Features` = 7 dossiers ; `docs/architecture/layers-and-projects.md:34` et `docs/reference/project-layout.md:201` disent « six authentication features ». Verdict : **B** (absent chez A).

**D7. Suppressions de diagnostics.** A : `#pragma` (HttpPipelineModule), `NoWarn` AV0029/AV0030 (Api.csproj), `NoWarn` CS1591/CS1573 ; les sections `.editorconfig` portent leur raison. B : `#pragma`, `NoWarn` CS1591/CS1573, `CA2007`/`CA1848`/`CA1515` à `none` globalement, 22 sections par fichier, « la raison n'y est pas écrite ». Preuve : `HttpPipelineModule.cs:67-69` ; `Directory.Build.props:27` ; `AppTemplate.Api.csproj:16` (`AV0029;AV0030`, absent chez B) ; `.editorconfig:57-59` (globaux, absents chez A) ; chaque section par fichier est suivie d'un commentaire `# <règle>: <raison>` (lignes 92-188), donc B a tort sur « la raison n'y est pas écrite » (le filtre `CheckComments` laisse passer `.editorconfig`, `Tools/CheckComments.cs:38`) ; l'en-tête ligne 90 dit « never a repository-wide NoWarn ». Verdict : **les deux en partie** (A : raisons et `AV0029/30` ; B : sévérités globales ; chacune a manqué un élément).

**D8. L'enregistrement DI est-il explicite ou par scan ?** A : « par convention », scan d'assembly (F-SCAN : les messages de test disent le contraire). B : « explicite, par feature », avec `AddUseCases` qui scanne. Preuve : `ApplicationModule.cs:68-82` (`AddTodoLists` appelle `AddFeature`) ; `:152-165` `AddFeature` = `AddValidatorsFromAssembly` filtré + `AddUseCases(TypesIn(vertical))` ; `ApplicationCoreModule.cs:49-87` ; `UseCaseConventionTests.cs:58,197` « explicit rather than scanned » ; `docs/guides/adding-a-feature.md:153` « Nothing is registered by hand ». Verdict : **les deux en partie** ; seule A relève l'incohérence entre message de test et code.

**D9. Quantité de commentaires dans `Src/`.** A : « 151 lignes `//` hors marqueurs dans 34 352 lignes ». B : « presque aucun commentaire ». Preuve : recomptage sur `Src/**/*.cs` hors `Generated/` : 0 ligne `//` de prose (`grep -hE '//' | grep -vE '///|//#|https?://'` ne renvoie que deux lignes de code contenant `://`), 321 marqueurs `//#`, 319 `///` ; le chiffre 151 coïncide avec le nombre de `.cs` de `Generated/`. Verdict : **B** (A : chiffre faux).

**D10. Le curseur de pagination est-il signé ?** A : « Base64Url d'un JSON `{f,d,k,i}` ». B : « JSON base64url signé de champ, direction, clé, id ». Preuve : `Cursor.cs:45-50` `JsonSerializer.SerializeToUtf8Bytes` puis `Base64Url.EncodeToString`, aucune signature ni HMAC ; `grep -i hmac` sans résultat dans `Application`. Verdict : **A**.

**D11. Fiabilité des numéros de ligne de B.** A cite des plages valides ; B en cite hors plage : `AuditingSaveChangesInterceptor.cs:345-370` (fichier de 62 lignes), `DomainEventDispatcher.cs:177-210` (46), `Cursor.cs:236-330` (149), `PageRequest.cs:88-131` (89), `ErrorMapping.cs:127-172` (84), `TodoListTracker.cs:98-131` (92) ; B écrit aussi « le tracker de TodoLists seul fait 181 lignes » : 92 (181 = 92 + 89 du repository). Preuve : `wc -l` sur chaque fichier. Verdict : **A** ; les faits décrits par B restent exacts pour la plupart, ses citations de ligne ne sont pas utilisables.

**D12. Taille de `Tests/Architecture`.** A : 43 fichiers. B : 43 fichiers au total mais « `Rules/` (40), `Composition/` (3), `Fixtures/` (5) » (somme 48). Preuve : `find` par dossier : `Rules` 34, `Composition` 3, `Fixtures` 6 = 43. Verdict : **A** (B : ventilation fausse).

**D13. Nombre de use cases.** A : ne donne pas de total. B : « 66 use cases dans `Application` », « 84 `*UseCase.cs` » dans Auth. Preuve : classes `sealed class \w+UseCase` : `Application` 33 (TodoLists 16, Files 12, Reminders 5), `Application.Auth` 42, `Application.Core` 2 ; 66 et 84 sont des nombres de fichiers (classe + interface). Verdict : **B en partie** (les 84 sont bien étiquetés « fichiers » ; les 66 sont présentés à tort comme des use cases, il y en a 33).

**D14. Taille du lanceur.** A et B : « 6 447 lignes ». Preuve : tous `.cs` de `Launcher/` = 6 447 ; sans `CleanArch.Create.Tests` : 3 736. Verdict : **aucune** (erreur commune ; le chiffre inclut les tests).

**D15. Dockerfiles.** A : un seul, celui de l'API. B : « un Dockerfile par hôte (API, worker, portail, MCP, chaque web app) ». Preuve : `find . -name 'Dockerfile*'` : `Src/Presentation/AppTemplate.Api/Dockerfile`, `Src/Web/apps/{FrameworkSite,PlaceholderSite}/Dockerfile`, et dans les starters `blazor-server`, `mcp`, `worker` ; le dépôt tel quel n'en a donc que 3 sous `Src/`. Verdict : **B pour le projet généré, A pour l'arbre `Src/`** : les deux en partie.

**D16. Autorisation par rôle.** A : « une seule politique nommée `Administrator` ... pas de rôles ... au-delà ». B : « `[Authorize(Policy=Administrator)]` sur 5 contrôleurs ». Preuve : 5 contrôleurs portent l'attribut (`grep` : ExternalIdentityAdministration, SecurityEventMaintenance, AccountAdministration, RefreshTokenMaintenance, Maintenance) ; la politique exige un rôle (`AuthComposition.cs:49` `RequireRole(IdentityRoles.Administrator)`). Verdict : **B** pour le détail, **A** pour « une seule politique » ; formule « pas de rôles » de A trop absolue.

**D17. Références de `AppTemplate.Api`.** A et B : « tous les modules d'infra ». Preuve : `AppTemplate.Api.csproj` ne référence pas `Infrastructure.InMemory` (Email, Storage, Auth, Core, Persistence seulement). Verdict : **aucune** (erreur commune, mineure).

**D18. Couverture.** A : donne 93,63 % (commit) et ne remesure pas. B : « taux réel non mesuré ; seul le plancher est établi ». Preuve : `git show 8200de77` (message : « 93.63 % (14439/15421) », plancher 90 -> 89) ; `556b66b5` : « 90.46% (7874/8704) over 3259 tests » ; aucun rapport de couverture dans le clone. Verdict : **les deux** (compatibles ; A apporte une mesure datée, B est exact sur l'absence de mesure ici).

**D19. Version de Testcontainers.** A : « 4.16 » pour tous. B : « 4.15/4.16 ». Preuve : `Directory.Packages.props:148-157` : Testcontainers 4.16.0, PostgreSql 4.16.0, MsSql 4.15.0, MySql 4.15.0. Verdict : **B**.

**D20. Générateurs.** A : trois générateurs (`NewFeature`, `NewUseCase`, `RemoveFeature`) et des garde-fous. B : « 5 générateurs (~15 000 lignes) ». Preuve : `Tools/` compte 9 fichiers, 14 980 lignes ; générateurs = 3 (9 004 lignes). Verdict : **A**.

**D21. Politique de collection de TodoLists.** A : champs triables, tri par défaut, tailles. B : en plus, `lastModifiedAt` en offset seul. Preuve : `TodoListCollectionPolicy.cs` : `Keyset(name)`, `Keyset(createdAt)`, `OffsetOnly(lastModifiedAt)`, `MaxSortTerms 3`. Verdict : **B** (précision absente chez A, pas contradiction).

**D22. Client généré.** A : « 163 `.cs` » dans `Generated`. Preuve : `Generated/` = 151 `.cs` ; 163 = tout le projet `AppTemplate.Client`. Verdict : **A en partie** (chiffre du projet entier).

**D23. Points présents chez un seul et vérifiés vrais** : A seul : `README.md` sans `--database` et sans React Router/Blazor WASM (F6) ; `Testcontainers.MySql` encore référencé (deux csproj d'intégration) ; seuil 90 -> 89 ; incohérence des messages de `UseCaseConventionTests` (F4) ; `ErrorCodeStatusTests` ne couvre que les classes `*Errors`. B seul : `InvariantGlobalization`, 4 endpoints anonymes « fluents » comptés à part (`HttpSurfaceTests.cs:28`), `PortConventionTests` plafonné à 6 opérations, politique `Administrator` sur 5 contrôleurs, `refusedOptions` aux lignes 84-87, `postActions` (D2), 104 symboles / 25 sources / 46 modificateurs de la source 0, 11 starters, l'écart « six » (D6). Tous confirmés dans le code.

## 11. Non vérifié

- Compilation, tests, couverture réelle, génération `dotnet new` : rien exécuté.
- Contenu ligne à ligne des starters, de `Tools/NewFeature.cs`/`RemoveFeature.cs`, des modules Auth, Storage, Email, des workflows hors `ci.yml`.
- Comportement runtime de SQL Server/SQLite (variantes) : seul le chemin PostgreSQL de l'arbre par défaut a été lu.
- Équivalence entre `matrix.json` (25 entrées) et le tableau de `docs/template/maintaining-the-template.md`.
- Un faux positif possible : `git ls-remote` a été lancé une fois (lecture) pour confirmer l'existence de la branche distante `feat/cleanarch-cli-skeleton`, cités par A (la liste `git branch -r` locale la montre déjà).
