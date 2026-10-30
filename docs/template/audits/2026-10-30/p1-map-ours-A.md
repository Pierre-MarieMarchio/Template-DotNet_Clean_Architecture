# Carte de NOTRE template (develop), agent A

Dépôt lu : `/tmp/claude-1000/-home-pmmarchio-code-Template-DotNet-Clean-Architecture/608f3f2d-3c02-4530-9e46-6f8e7e0e445b/scratchpad/repos/ours`
(chemins ci-dessous relatifs à ce dépôt). HEAD = `5f22faa5` (2026-10-30), branche `develop`.
Aucun build ni test lancé : tout ce qui suit est lu dans le code, les workflows et les messages de commit.

Réserve d'état : le clone n'est pas vierge, `git status` montre `.template.config/template.json` modifié
(`templateVersion` `1.0.0` en HEAD, `1.1.0-dev` dans l'arbre, ligne 190). Le comptage ci-dessous lit l'arbre tel qu'il est.

Le dépôt est à la fois (a) une solution .NET 10 qui compile telle quelle (forme par défaut : PostgreSQL, auth locale,
API, exemples, Files) et (b) le `dotnet new`. Les conditions du template sont des commentaires que le compilateur
ignore : `//#if (todoLists)` ... `//#endif`, `/*#if ... #endif*/`, `<!--#if-->` dans les `.md`
(`Tests/.../LayerDependencyTests.cs:12-26`, `docs/architecture/README.md:60-70`). Le code source est donc lisible "en forme par défaut".

---------------------------------------------------------------------------------------------------

## 1. Projets, références, règles qui les tiennent

### 1.1 Projets (`Src/`)

15 projets .NET sous `Src/` (+ 2 apps web sous `Src/Web/apps`, qui sont des starters). Chaque couche a un `.Core` pour ce qui ne connaît aucune feature.

| Couche | Projet | Références (csproj) | Rôle |
|---|---|---|---|
| Domaine | `AppTemplate.Domain.Core` | aucune (`Src/Domain/AppTemplate.Domain.Core/AppTemplate.Domain.Core.csproj`) | `Entity<TId>`, `AggregateRoot<TId>`, `AuditableAggregateRoot<TId>`, `UserId`, `IDomainEvent`, `DomainException`, `IAuditable/IVersioned/IOwnedAggregate` |
| Domaine | `AppTemplate.Domain` | Domain.Core (`...Domain.csproj:4`) | agrégats TodoList, Reminder, StoredFile ; value objects, événements, contrat `I<X>Repository` ; `Common/Tagging` |
| Application | `AppTemplate.Application.Core` | Domain.Core (`...Application.Core.csproj:15`) ; FluentValidation, DI/Logging abstractions | `Result`, `Error`, `IUseCase`, `PagedResult`, collections (tri, curseur), idempotence, concurrence, ports transverses (`IUnitOfWork`, `ICurrentUser`, `IDateTimeProvider`, `IEmailSender`, `ILeaderLease`...), `AddUseCases` |
| Application | `AppTemplate.Application` | Domain + Application.Core (`...Application.csproj:13-14`) | use cases et services des features TodoLists, Reminders, Files, Notes, ExternalIdentity ; ports de feature ; DTO |
| Application | `AppTemplate.Application.Auth` | Application.Core seul (`...Application.Auth.csproj:15`) | comptes, sessions, 2FA, passkeys, access keys, security events comme use cases |
| Infra | `AppTemplate.Infrastructure.Core` | Application.Core ; EF, Npgsql, Hybrid cache (`...Infrastructure.Core.csproj`) | `EfUnitOfWork`, interceptors de sauvegarde, `DatabaseProvider`, cache, gabarits de mail, horloge |
| Infra | `...Infrastructure.Persistence` | Application, Application.Core, Infra.Core (`...Persistence.csproj:13-15`) | `AppDbContext`, records de lignes, mappers, trackers, repositories, queries, idempotence, `LeaderLease` |
| Infra | `...Infrastructure.Auth` | Application.Core, Application.Auth, Infra.Core (`...Auth.csproj:19-21`) ; Identity, JwtBearer | `AuthDbContext` (contexte propre), JWT, refresh tokens, passkeys |
| Infra | `...Infrastructure.Email` | Application, Application.Core, Infra.Core | MailKit (SMTP), Postmark (HTTP) |
| Infra | `...Infrastructure.Storage` | Application, Application.Core | S3 (AWSSDK), système de fichiers, ClamAV |
| Infra | `...Infrastructure.InMemory` | Application, Application.Core (+Auth) | doubles publics à dessein (`AdapterVisibilityTests.InMemoryDoublesArePublicDeliberately`) |
| Présentation | `AppTemplate.Presentation.Core` | Application.Core ; OpenTelemetry, Http.Resilience | ce que tout host utilise, sans ASP.NET (`BackgroundScope`, `PeriodicJob`, `AuthorizationPolicies`, options OIDC externes) |
| Présentation | `AppTemplate.Api` (Sdk.Web) | Application*, Presentation.Core, tous les modules Infra (`...Api.csproj:46-60`) | contrôleurs MVC, pipeline HTTP, jobs, racine de composition ; seul à référencer les modules d'infra |
| Client | `AppTemplate.Client` | aucune réf. projet ; Kiota Bundle | client .NET généré depuis OpenAPI (`Src/Clients/AppTemplate.Client/Generated`, 163 `.cs`) |
| Web | `Src/Web` (pnpm) | | `apps/PlaceholderSite`, `apps/FrameworkSite` (starters), `packages/{api-client,eslint-config,tsconfig}` |

Les autres hôtes (Portal Blazor Server, Mcp, Desktop Avalonia/WPF, Worker) ne sont pas dans `Src/` : ils vivent
comme starters sous `.template.config/starters/<x>/` et sont créés à la génération/bootstrap
(`.template.config/starters/mcp/Src/Presentation/AppTemplate.PresentationAppMcp/...`).

### 1.2 Projets de tests : 18 sous `Tests/` + le lanceur

`Tests/Application/{Application,Application.Auth,Application.Core}.UnitTests`, `Tests/Domain/{Domain,Domain.Core}.UnitTests`,
`Tests/Infrastructure/{Auth,Core,Email,InMemory,Persistence,Storage}.UnitTests`, `Tests/Presentation/{Api,Presentation.Core}.UnitTests`,
`Tests/Clients/Client.UnitTests`, `Tests/Architecture/AppTemplate.Architecture.Tests`,
`Tests/Integration/{Api.IntegrationTests,Infrastructure.Auth.IntegrationTests}`, `Tests/EndToEnd/AppTemplate.EndToEnd.Tests`, `Tests/Load/smoke.js` (k6).
Un projet de test par projet source, sous un dossier miroir de la couche.

### 1.3 Règles de construction

- `Directory.Build.props:3-18` : `net10.0`, `LangVersion latest`, `Nullable enable`, `ImplicitUsings`, `EnableNETAnalyzers`,
  `AnalysisLevel latest-recommended`, `EnforceCodeStyleInBuild`, **`TreatWarningsAsErrors true`**, `NuGetAudit` (mode `all`, niveau `low`),
  `GenerateDocumentationFile` (c'est ce qui fait échouer IDE0005, `docs/guides/build-and-ci.md:73-75`).
- `Directory.Build.targets:3-5` : tout projet `IsTestProject` devient `Exe` (Microsoft.Testing.Platform).
- `Directory.Packages.props` : gestion centrale des versions (`ManagePackageVersionsCentrally`).
- `global.json` : SDK 10.0.100, `rollForward latestFeature`, **runner de test `Microsoft.Testing.Platform`** (`dotnet test --solution`).
- `.editorconfig` (8 Ko) : fichiers-scope, BOM UTF-8 pour `.cs`/`.md`, LF, `csharp_style_namespace_declarations = file_scoped:error`,
  `dotnet_diagnostic.CS8618/CS8602 = error`, nommage (`I` pour interfaces, `T` pour paramètres de type, `_camel` pour champs privés : `error`/`warning`),
  `IDE0005 = warning` (donc erreur par TreatWarningsAsErrors). Les suppressions sont des sections par fichier avec leur raison (ex. `.editorconfig` section
  `[Src/.../Results/Error.cs]` CA1716). Pas de paquet d'analyseurs tiers dans `Directory.Packages.props` (ni Roslynator, ni StyleCop, ni Meziantou) ;
  les identifiants `S....` de l'`.editorconfig` ne servent qu'à l'analyse SonarQube (compose profile `sonar`).
- `dotnet format --verify-no-changes` est une étape de CI (`.github/workflows/ci.yml`, "Verify formatting (.editorconfig)").

### 1.4 Tests d'architecture (`Tests/Architecture/AppTemplate.Architecture.Tests`)

xUnit v3 + **NetArchTest.Rules** 1.3.2 + Shouldly ; 43 fichiers `.cs`, 8 899 lignes, 195 `[Fact]/[Theory]` (comptage grep). Familles :

- Dépendances de couches : `Rules/LayerDependencyTests.cs` (le domaine ne dépend de rien : EF, AspNetCore, DI, FluentValidation, Npgsql, MailKit, System.Text.Json interdits `:12-26` ; l'application n'a pas MediatR/EF/AspNetCore `:28-44`), vérifie aussi les manifestes d'assembly (`TheManifests_ShowTheDependencyPointingInwardAndNoFurther`).
- Modules/hôtes : `Rules/ModuleDependencyTests.cs` (17 tests : aucun module d'infra ne dépend d'un autre, seul un host référence l'infra, aucun host ne référence un autre host, chaque client atteint le cœur par l'API seule), `Fixtures/ProjectReferenceGraph.cs`, `Rules/PackageBoundaryTests.cs`, `Rules/PortabilityTests.cs`.
- Features isolées : `Rules/FeatureIsolationTests.cs` (une feature n'atteint une autre que par ports et événements ; une feature sans dossier domaine est un service au-dessus d'un store).
- Use cases : `Rules/UseCaseConventionTests.cs` (scellés, publics, sous `Features/<F>/UseCases/(Commands|Queries)/<Op>`, un dossier = un use case nommé `<Op>UseCase` + `I<Op>UseCase`, l'entrée `<Op>Command|Query|Request`) ; la population est mesurée depuis le disque (`SourceDeclarations.UseCaseFullNames`), jamais figée par un nombre.
- Domaine : `Rules/DomainModelTests.cs` (racines scellées, pas d'état publiquement modifiable, VO et événements immuables), `Rules/DomainEventTests.cs` (tout événement est consommé ou listé comme non consommé avec raison).
- Ports : `Rules/PortConventionTests.cs` (pas de façade multi-capacités, chaque port a un consommateur), `Rules/StorageVocabularyTests.cs` (quatre mots : Repository, Queries, Store, Table), `Rules/StorageShapeTests.cs` (pas d'`IQueryable` sur un port, pas de drapeau "supprimé").
- Visibilité : `Rules/AdapterVisibilityTests.cs` (adaptateurs internes, ports publics).
- Layout/nommage : `Rules/LayoutConventionTests.cs` (698 l.), `Rules/FeatureFolderVocabularyTests.cs:13-60` (liste fermée de mots de dossier et de suffixes de fichier : `Controllers->Controller`, `Repositories->Repository`, `Tracking->Tracker`, ...).
- HTTP : `Rules/HttpSurfaceTests.cs` (`EveryAnonymousEndpoint_IsOnTheList`), `Rules/IdempotentActionTests.cs` (Idempotent = POST), `Rules/RequestPipelineTests.cs` (ordre des middlewares), `Rules/ErrorCodeTableTests.cs` (codes d'erreur source <-> `docs/reference/api.md`, dans les deux sens), `Rules/CollectionContractTests.cs`.
- Composition DI : `Composition/ContainerCompositionTests.cs` (431 l., 15 tests : le conteneur se construit en validation stricte, résout chaque use case et chaque port, refuse un use case sans ou avec deux interfaces nommées), `Composition/HostComposition.cs`.
- Hygiène du dépôt : `Rules/ConfigurationSurfaceTests.cs` (chaque option liée a sa section dans `docs/reference/configuration.md`), `Rules/DocumentationCitationTests.cs`, `Rules/ContainerImagePinningTests.cs` (images pinnées par digest), `Rules/CoverageFloorTests.cs`, `Rules/CoverageSettingsTests.cs`, `Rules/PullRequestTemplateTests.cs`.
- Spécifique template (exclus de la génération, `.template.config/TemplatePackage.csproj` TemplateExclude) : `Rules/TemplatePackagingTests.cs` (1 099 l.), `Rules/AcceptanceSelectionTests.cs`.
- **Propriété distinctive** : chaque règle négative a un test jumeau "TheRule_IsSensitive_AndDetectsAViolation" qui prouve que la règle sait échouer (ex. `LayerDependencyTests.cs:TheDomainRuleIsSensitive`, `FeatureIsolationTests`, `BackgroundWorkTests`, `PortConventionTests`), et `RuleAssertions.RequireTypes` refuse une règle sur un ensemble vide.

---------------------------------------------------------------------------------------------------

## 2. Le chemin d'une requête, fichier par fichier

Exemple : feature TodoLists (forme "riche" = agrégat).

### 2.1 Écriture : `POST /api/v1/todo-lists` (création)

1. **Pipeline HTTP** : `Src/Presentation/AppTemplate.Api/Program.cs:128-134` appelle `app.UseHttpPipeline(...)` puis `UseAuthentication`, `UseAuthorization`, `MapControllers`. `Common/Hosting/HttpPipelineModule.cs:93-115` installe : en-têtes forwarded, en-têtes de sécurité, en-têtes de cache, journal de requêtes, limites de requête, `UseExceptionHandler` (`GlobalExceptionHandler`), status code pages, CORS, rate limiter, langue de requête, timeouts. Politique de repli par défaut : `RequireAuthenticatedUser` (`Program.cs:111-115`) : refus par défaut.
2. **Contrôleur** : `Features/TodoLists/Controllers/TodoListsController.cs:64-80` : `[HttpPost] [Idempotent]`, lie `CreateTodoListRequest` (`Contracts/Requests/CreateTodoListRequest.cs`, un `record`), construit `new CreateTodoListCommand(request.Name)`, appelle `ICreateTodoListUseCase` (injecté), mappe le `Result` par `TodoListResponseMapping.ToListResponse` puis `CreatedOrProblem(...)` (`:79`). Classe de base `Common/Controllers/ApiControllerBase.cs:82-99` : pose l'`ETag` (depuis `Versioned.Version`) et `CreatedAtRoute`, ou convertit l'`Error` via `ErrorMapping.ToActionResult` (`Common/Errors/ErrorMapping.cs:17-70`, table type d'erreur -> statut, problème `application/problem+json`, extension `code`).
3. **Filtre d'idempotence** : `Common/Idempotency/IdempotencyFilter.cs:30-130` (`IAsyncResourceFilter`) : si `Idempotency-Key` présent sur un POST `[Idempotent]`, empreinte SHA-256 du couple endpoint+corps, `IIdempotencyStore.ClaimAsync` (bail par utilisateur : ADR 0010), rejoue la réponse stockée (`Idempotency-Replayed: true`) ou exécute puis `CompleteAsync`.
4. **Use case** : `Src/Application/AppTemplate.Application/Features/TodoLists/UseCases/Commands/CreateTodoList/CreateTodoListUseCase.cs:20-46` : (a) `currentUser.RequireUserId()` (`Application.Core/Common/Ports/CurrentUserExtensions.cs:8-16`, sinon `auth.required` 401) ; (b) `validator.EnsureValidAsync` (FluentValidation, `Application.Core/Common/Validation/ValidationExtensions.cs:8-18`, erreur `request.validationFailed` avec `Details` par champ camelCase) ; (c) `TodoList.Create(userId, name, now)` ; (d) `repository.Add` ; (e) `unitOfWork.SaveChangesAsync` ; (f) renvoie `TodoListDtoMapping.Detail(todoList)` (`Features/TodoLists/Model/Mapping/TodoListDtoMapping.cs:9-21`) enveloppé dans `Versioned<T>`.
5. **Domaine** : `Src/Domain/AppTemplate.Domain/Features/TodoLists/Model/Entities/TodoList.cs:28-36` : `Create` valide via `TodoListName.Create` (lève `DomainException`, `.../ValueObjects/TodoListName.cs:13-28`), génère l'id `Guid.CreateVersion7()`, **lève** `TodoListCreatedDomainEvent` (`RaiseDomainEvent`, `Domain.Core/Common/Primitives/AggregateRoot.cs:16-20`).
6. **Persistance (stage)** : `Src/Infrastructure/.../Persistence/Features/TodoLists/Access/Repositories/TodoListRepository.cs:68-77` : `mapper.ToNewRecord(aggregate)` (`Rows/Mapping/TodoListMapper.cs:31-55`) -> `context.TodoLists.Add(record)` -> `tracker.Track(aggregate, record)`. Le domaine n'est **pas** mappé par EF (ADR 0005) : `TodoListRecord` (`Rows/Models/TodoListRecord.cs`) est la ligne, le mapper fait l'aller-retour, le tracker (`Rows/Tracking/TodoListTracker.cs`, hérite de `Infrastructure.Core/Common/Saving/Tracking/AggregateTracker.cs`) garde la paire agrégat/ligne.
7. **Commit** : `IUnitOfWork` est `EfUnitOfWork<AppDbContext>` (`PersistenceModule.cs:174,172-176` ; `Infrastructure.Core/Common/Saving/EfUnitOfWork.cs:9-21`, traduit `DbUpdateConcurrencyException` en `ConcurrencyConflictException`). `SaveChanges` déclenche, dans l'ordre d'attache (`PersistenceModule.cs:244-256`, `AddContext`) :
   - `AggregateFlushSaveChangesInterceptor` (`Saving/Tracking/AggregateFlushSaveChangesInterceptor.cs:55-73`) : `FlushTo(context)` de chaque tracker (écrit l'agrégat sur ses lignes, réconcilie items/tags, "touche" la racine quand seul un enfant change, `TodoListTracker.cs:12-92`) ; après la sauvegarde, `RefreshFromStore` recopie version et audit sur l'agrégat.
   - `AuditingSaveChangesInterceptor` (`Saving/Auditing/AuditingSaveChangesInterceptor.cs:35-61`) : `CreatedAt/By` et `LastModifiedAt/By` sur toute entité `IAuditable`, acteur = `IAuditActor` (utilisateur courant, ou `BackgroundAuditActor` hors requête).
   - `DomainEventDispatchSaveChangesInterceptor` (`Saving/DomainEvents/DomainEventDispatchSaveChangesInterceptor.cs:16-59,99-113`) : à `SavingChanges` draine les événements de chaque `IDomainEventSource` (les trackers) ; à `SavedChanges` (après le commit) les diffuse via `DomainEventDispatcher` ; sur `SaveChangesFailed` les **restitue** pour la reprise (`:80-97,118-124`).
   - Version : sur PostgreSQL c'est la colonne système `xmin` (`Infrastructure.Core/Common/Contexts/DatabaseProvider.cs:15,68-77`, `IsVersion()`), jeton de concurrence ; sur SQL Server/SQLite un compteur estampillé par `VersionStampingSaveChangesInterceptor` (variants, `.template.config/variants/database-sqlserver/.../DatabaseProvider.cs:67`).
8. **Retour** : `Result<Versioned<TodoListDetailDto>>` -> mapping -> `PagedResponse`/`TodoListResponse` -> `201 Created`, `Location`, `ETag: "<xmin>"`, corps JSON.
   Erreurs : `Result.Failure` pour les cas attendus (statut par `ErrorType`) ; `ConcurrencyConflictException` -> 409 `concurrency.conflict` par `GlobalExceptionHandler.cs:19-47` ; autre exception -> 500 `server.unexpected` avec `traceId`.

Variante "mise à jour" (ex. `AddTodoItemUseCase.cs:17-59`) : la validation d'abord, puis `ITodoListService.LoadOwnedAsync(id, precondition)` (`Features/TodoLists/Services/TodoListService.cs:13-30`, charge via `ITodoListRepository.GetAsync`, puis `OwnedAggregate.Require`, `Application.Core/Common/Ownership/OwnedAggregate.cs:10-31` : autre propriétaire ou absent = même `NotFound` ; précondition `If-Match` non satisfaite = 412), puis les appels à l'agrégat enveloppés dans `DomainGuard.Try(...)` (`Application.Core/Common/Validation/DomainGuard.cs:8-36` : `DomainException` -> `Error.Conflict("domain.invariantViolated")` 409).

### 2.2 Lecture paginée : `GET /api/v1/todo-lists?paging=&page=&pageSize=&cursor=&sort=&search=&createdAfter=&createdBefore=`

1. Contrôleur `TodoListsController.cs:26-47` : `[FromQuery] GetTodoListsRequest` (`Contracts/Requests/GetTodoListsRequest.cs`) -> `GetTodoListsQuery` (`Application/.../UseCases/Queries/GetTodoLists/GetTodoListsQuery.cs:5-17`, implémente `ICollectionQuery`).
2. `IGetTodoListsUseCase.ExecuteAsync` -> `GetTodoListsUseCase.cs:10-31` : `RequireUserId`, puis `GetTodoListsRequestBinder.Bind(query)` (`.../GetTodoListsRequestBinder.cs:10-19`) qui appelle `CollectionBinding.Bind` (`Application.Core/Common/Collections/CollectionBinding.cs:8-42`) : `CollectionOrder.Parse` (mode + tri selon `TodoListCollectionPolicy`, `Model/Policies/TodoListCollectionPolicy.cs:6-30` : champs triables, tri par défaut `createdAt:desc`, `MaxPageSize 100`, `DefaultPageSize 20`), construction du filtre `TodoListFilter.Create` (recherche, dates ISO 8601, `Ports/TodoListQueries/TodoListFilter.cs:24-81`), puis `PageRequest.Create` (offset ou curseur, `Application.Core/Common/Collections/PageRequest.cs:45-88`). Tout échec = `Error.Validation` -> 400.
3. Port : `ITodoListQueries.GetForOwnerAsync(ownerId, FeaturePageRequest<TodoListFilter>)` (`Application/.../Ports/TodoListQueries/ITodoListQueries.cs:9-22`).
4. Adaptateur : `Infrastructure.Persistence/Features/TodoLists/Access/Queries/TodoListQueries.cs:16-32` : `context.TodoLists.AsNoTracking().Where(list => list.OwnerId == ownerId.Value)` (le propriétaire est dans le prédicat), `ApplyFilter` (`LIKE` insensible à la casse avec motif échappé `TodoListLikePattern`), puis **offset** (`GetOffsetPageAsync :81-98`, `COUNT` + `Skip/Take`) ou **keyset** (`GetKeysetPageAsync :100-130`, `Take(pageSize+1)`, curseur `Cursor.After(...).Encode()`, Base64Url d'un JSON `{f,d,k,i}`, `Application.Core/Common/Collections/Cursor.cs:9-149`). Tri via `TodoListSortMap` (`Access/Queries/TodoListSortMap.cs`, tri final stable sur `Id`). Projection directe vers `TodoListSummaryDto` (`_toSummary :144-150`) : la lecture **ne passe pas par l'agrégat**.
5. Retour : `PagedResult<TodoListSummaryDto>` (`Application.Core/Common/Results/PagedResult.cs:4-31`) -> `TodoListResponseMapping.ToPageResponse` (`Contracts/Mapping/TodoListResponseMapping.cs:55-57`) -> `PagedResponse<T>` (`Common/Contracts/PagedResponse.cs`, `Items, PageSize, Page, TotalCount, TotalPages, HasNextPage, NextCursor`) -> `OkOrProblem` -> 200. Pas d'ETag sur la liste ; sur le détail (`GET {id}`, `:49-62`) `ETag` + `If-None-Match` -> 304 (`ApiControllerBase.cs:26-41`).

---------------------------------------------------------------------------------------------------

## 3. Les concepts

| Concept | Réponse du code | Preuve |
|---|---|---|
| CQRS | "léger" : un dossier `Commands/` et un `Queries/` par feature ; commandes = agrégat via repository + `IUnitOfWork` ; requêtes = port `...Queries` projetant les lignes en DTO ; **même base**, pas de modèle de lecture séparé | `Features/TodoLists/UseCases/{Commands,Queries}` ; `TodoListQueries.cs:23-32` |
| Médiateur | **Non.** Ni MediatR ni dispatcher ni pipeline behaviour : le contrôleur appelle l'interface du use case ; MediatR interdit par test ; ADR 0003 | `LayerDependencyTests.cs:28-44` ("MediatR") ; `docs/decisions/0003-...md` ; `docs/architecture/README.md` section "deliberately absent" |
| Pipeline / behaviours | Aucun. Validation, journalisation, commit sont dans le use case ; le transversal HTTP est dans les filtres/middlewares (idempotence = `IAsyncResourceFilter`, `IdempotencyFilter.cs`) | `HttpPipelineModule.cs:93-115` |
| Enregistrement DI | par convention : `AddUseCases` (`ApplicationCoreModule.cs:49-65`) enregistre en `Scoped` chaque classe `IUseCase` sous le contrat de son interface nommée unique (`ContractOf :67-85` lève s'il y en a 0 ou 2) ; `AddFeature` (`ApplicationModule.cs:156-165`) scanne l'assembly par préfixe de namespace `...Features.<Feature>.` et enregistre aussi les validateurs FluentValidation | `ApplicationModule.cs:73,90,114,134` |
| Validation | FluentValidation, appelée **explicitement** dans le use case (`validator.EnsureValidAsync`), résultat = `Result` d'erreur `request.validationFailed` + `Details`. Les invariants du domaine sont redoublés (le validateur reprend les limites du VO, ex. `TodoListName.MaxLength`) | `CreateTodoListCommandValidator.cs:8-13` ; `ValidationError.cs:7-25` |
| Erreurs | **Result pour l'attendu, exceptions pour les bugs** (`Result<T>`, `Error{Code,Message,Type,Details}`, `ErrorType` = Validation, NotFound, Unauthorized, Forbidden, Conflict, TooManyRequests, PreconditionFailed, PreconditionRequired). Chaque feature a sa classe `...Errors` (`TodoListErrors.cs`) ; codes pointillés stables, table dans `docs/reference/api.md`, vérifiée par test dans les deux sens. Exceptions : `DomainException` (invariant), `ConcurrencyConflictException` (commit) | `Result.cs:3-65` ; `ErrorMapping.cs:59-70` ; `GlobalExceptionHandler.cs:19-47` |
| Domain events : où levés | dans les méthodes de l'agrégat (`RaiseDomainEvent`) : `TodoList.Create` (`TodoList.cs:33`), `CompleteItem` (`:74`), `ReopenItem` (`:83`) ; la liste vit dans l'agrégat (`AggregateRoot.cs:8-22`) | `Domain/.../TodoLists/Model/Events/*` |
| Domain events : quand publiés | **après le commit**, en processus, de façon synchrone, par l'interceptor (`SavedChanges`), sans outbox (ADR 0006). Le consommateur qui lève est journalisé et ignoré : livraison *au plus une fois* ; la garantie derrière un fait critique est un balayage (sweep), pas un retry. Un échec de sauvegarde rend les événements pour la reprise. Un fait sans agrégat se publie par `IDomainEventPublisher` après le commit du use case | `DomainEventDispatchSaveChangesInterceptor.cs:39-59,80-97` ; `DomainEventDispatcher.cs:12-45` ; `docs/architecture/domain-and-results.md:40-50` |
| Consommateurs | `IDomainEventConsumer<TEvent>` (`Application.Core/Common/Events/IDomainEventConsumer.cs`), enregistrés par `AddDomainEventConsumer` ; ex. `CancelRemindersOnTodoItemCompletedConsumer` (Reminders réagit à un événement de TodoLists, `Features/Reminders/Services/Consumers/TodoItemCompleted/...`), `DeleteTodoListsOnAccountDeletedConsumer` (+ `IDeletedAccountDataPolicy` pour le balayage) | `ApplicationModule.cs:76-81` |
| Entités / bases | `Entity<TId>` (égalité par type+id), `AggregateRoot<TId>`, `AuditableAggregateRoot<TId>` (`Version uint`, audit, via interfaces explicites), `UserId` (record scellé, refuse `Guid.Empty`), ids `Guid.CreateVersion7()`, `Create` (lève les événements) distinct de `Rehydrate` (ne lève rien, refuse une ligne impossible) | `Domain.Core/Common/Primitives/*` ; `TodoList.cs:28-56` |
| Mapping | **à la main, vérifié par le compilateur** (pas d'AutoMapper) : 3 mappings par feature, `...DtoMapping` (domaine -> DTO, `internal static`), `...ResponseMapping` (DTO -> réponse HTTP), `...Mapper` (agrégat <-> ligne EF) | `TodoListDtoMapping.cs`, `TodoListResponseMapping.cs`, `TodoListMapper.cs` |
| Autorisation | refus par défaut (`SetFallbackPolicy(RequireAuthenticatedUser)`, `Program.cs:111-115`) ; une seule politique nommée `Administrator` (`Presentation.Core/Common/Security/AuthorizationPolicies.cs`, `Api/Features/Auth/Composition/AuthComposition.cs:49`) ; `[AllowAnonymous]` limité à une liste fermée vérifiée sur tous les endpoints (`HttpSurfaceTests.EveryAnonymousEndpoint_IsOnTheList`, `DefaultDenyAuthorizationTests`) ; pas de rôles/tenants/permissions au-delà | `Security/DefaultDenyAuthorizationTests.cs` |
| Propriété des données | `ICurrentUser` (claim `NameIdentifier` -> `UserId`, `Api/Common/Security/CurrentUser.cs:10-23`), jamais pris du corps ; **le propriétaire est dans la requête** (ADR 0009) ; un enregistrement d'autrui répond 404, jamais 403 (`OwnedAggregate.cs:20-23`). Chemin "service/record" : filtre dans le SQL (`NoteStore.cs:91`). Chemin agrégat : voir finding F-OWN | `docs/decisions/0009-...md` |
| Concurrence | `ETag` = version de la ligne ; `If-Match` -> `VersionPrecondition` (`Api/Common/Concurrency/*`, option `If-Match` requis/optionnel) ; la précondition est évaluée sur l'agrégat chargé (`OwnedAggregate.Require`) puis la base refuse un écart au commit (jeton de concurrence) | ADR 0008 ; `OptimisticConcurrencyTests.cs`, `IfMatchRequiredTests.cs` |
| Localisation | `RequestLanguage` + `CurrentLanguage`, mails multilingues (`Infrastructure.Core/Common/Templating/EmailTemplate.cs`) | `Api/Common/Localization/*` |

---------------------------------------------------------------------------------------------------

## 4. Conventions

### 4.1 Organisation

**Par feature à l'intérieur de chaque couche** : tout projet = `Common/` (ne connaît aucune feature) + `Features/<Feature>/` + un seul fichier de composition à la racine (`...Module.cs` ou `Program.cs`). Mots de dossier en liste fermée (`FeatureFolderVocabularyTests.cs:13-60`), suffixe de fichier imposé par le dossier. `docs/reference/project-layout.md` documente la carte, testée par `LayoutConventionTests`.

Exemple de carte pour TodoLists : `Domain/Features/TodoLists/{Model/{Entities,Events,ValueObjects},Repositories}` ; `Application/Features/TodoLists/{UseCases/{Commands,Queries}/<Op>,Ports/<Port>,Model/{Dtos,Errors,Mapping,Policies},Services/Consumers,Helpers}` ; `Infrastructure.Persistence/Features/TodoLists/{Access/{Queries,Repositories},Rows/{Configurations,Mapping,Models,Tracking}}` ; `Api/Features/TodoLists/{Controllers,Contracts/{Requests,Responses,Mapping}}`.

### 4.2 Deux formes de feature (ADR 0002, `docs/architecture/feature-shapes.md`)

- **Riche** (agrégat à protéger) : TodoLists, Reminders, Files. Un use case par opération, un repository dans le domaine, port `...Queries` pour les lectures, mapper + tracker.
- **Simple** (service au-dessus d'un store) : Notes. Pas de dossier domaine ; `INoteService` (interne `NoteService`) sur `INoteStore` (écritures) et `INoteQueries` (lectures) ; le record est le seul modèle ; le propriétaire est dans chaque requête (`NoteStore.cs:76,91`). Tenu par `FeatureIsolationTests.AFeatureWithoutADomainFolder_IsAServiceOverAStore`.
- Le générateur écrit l'une ou l'autre (`new-feature --model record|aggregate`, voir 5).
- Un `Service` existe aussi dans la forme riche, quand plusieurs use cases partagent une porte : `ITodoListService.LoadOwnedAsync` (charge + propriété + précondition).

### 4.3 Forme exacte d'un use case : exemple complet, `CreateTodoList`

Dossier : `Src/Application/AppTemplate.Application/Features/TodoLists/UseCases/Commands/CreateTodoList/` (4 fichiers, un use case par dossier, nommé comme l'opération).

```csharp
// CreateTodoListCommand.cs
namespace AppTemplate.Application.Features.TodoLists.UseCases.Commands.CreateTodoList;
public sealed record CreateTodoListCommand(string Name);

// ICreateTodoListUseCase.cs
public interface ICreateTodoListUseCase : IUseCase<CreateTodoListCommand, Result<Versioned<TodoListDetailDto>>>;

// CreateTodoListCommandValidator.cs
public sealed class CreateTodoListCommandValidator : AbstractValidator<CreateTodoListCommand>
{
    public CreateTodoListCommandValidator() =>
        RuleFor(command => command.Name)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("A list name is required.")
            .Must(name => name.Trim().Length <= TodoListName.MaxLength)
            .WithMessage($"A list name cannot exceed {TodoListName.MaxLength} characters.");
}

// CreateTodoListUseCase.cs
public sealed class CreateTodoListUseCase(
    ITodoListRepository repository, IUnitOfWork unitOfWork, ICurrentUser currentUser,
    IDateTimeProvider dateTimeProvider, IValidator<CreateTodoListCommand> validator) : ICreateTodoListUseCase
{
    public async Task<Result<Versioned<TodoListDetailDto>>> ExecuteAsync(
        CreateTodoListCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var userId = currentUser.RequireUserId();
        if (userId.IsFailure) { return userId.To<Versioned<TodoListDetailDto>>(); }
        var validation = await validator.EnsureValidAsync(command, cancellationToken);
        if (validation.IsFailure) { return validation.To<Versioned<TodoListDetailDto>>(); }
        var todoList = TodoList.Create(userId.Value, command.Name, dateTimeProvider.UtcNow);
        repository.Add(todoList);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return TodoListDtoMapping.Detail(todoList);
    }
}
```
(Fichiers réels : `CreateTodoListCommand.cs:3`, `ICreateTodoListUseCase.cs:8`, `CreateTodoListCommandValidator.cs:6-14`, `CreateTodoListUseCase.cs:13-47` ; le code ci-dessus est l'extrait fidèle, usings omis.)
Particularités : constructeur primaire, `sealed`, publique (test), un `Result<>` en sortie, `Versioned<T>` quand l'ETag est exposé, validation avant le travail, "mauvais appelant" refusé *avant* la validation (test `AnAnonymousCaller_IsRefusedBeforeTheirInputIsValidated`, `Tests/.../CreateTodoListUseCaseTests.cs:~60`).
Un echec se propage en `x.To<Autre>()` (`Result.cs:35-37`) ; pas d'exception pour un cas attendu.

### 4.4 Nommage

Suffixes imposés par dossier (Controller, Repository, Queries, Store, Tracker, Mapper, Consumer, Validator, Dto, Request, Response, Options, ...). Champs privés `_camelCase` (`.editorconfig` warning) ; interfaces `I...` (error) ; paramètres de type `T...` ; les tests ont des underscores dans les noms de méthodes (`CA1707` désactivé dans `Tests/`). Les adaptateurs implémentant un port sont `internal` (test). Les options = classe `...Options` + `IValidateOptions<>` + `ValidateOnStart`, avec section dans `docs/reference/configuration.md` (test `ConfigurationSurfaceTests`).
Style : `file_scoped` namespaces, BOM UTF-8, LF, `ArgumentNullException.ThrowIfNull` en entrée de chaque méthode publique, accolades quand multi-ligne ; commentaires quasi absents (151 lignes `//` hors marqueurs de template dans 34 352 lignes de `Src`, plus les `///` XML de l'API) : règle "Clean comments" d'`AGENTS.md`, gate `Tools/CheckComments.cs`.

### 4.5 Emplacement des tests

Un dossier miroir : `Tests/<Couche>/<Projet>.UnitTests/Features/<Feature>/UseCases/Commands/<Op>/<Op>UseCaseTests.cs` (ex. `Tests/Application/AppTemplate.Application.UnitTests/Features/TodoLists/UseCases/Commands/CreateTodoList/CreateTodoListUseCaseTests.cs`, NSubstitute + `StubCurrentUser`/`StubDateTimeProvider` de `TestDoubles`, Shouldly). Les tests d'intégration sont rangés par thème sous `Tests/Integration/AppTemplate.Api.IntegrationTests/{TodoLists,Notes,Files,Reminders,Security,Auth,Idempotency,Saving,Leases,Storage,OpenApi,Http,Health,Caching}`.

---------------------------------------------------------------------------------------------------

## 5. Outillage

### 5.1 `dotnet new` : template principal `cleanarch-webapi`

`.template.config/template.json` (2 275 lignes). `identity CleanArch.WebApi.CSharp`, `sourceName AppTemplate`. Paramètres (`symbols`) :

| Option | Valeurs | Défaut |
|---|---|---|
| `--auth` | `local`, `external` (OIDC), `none` | `local` |
| `--database` | `postgresql`, `sqlserver`, `sqlite`(dev seulement) ; `mysql` refusé | `postgresql` |
| `--files` | bool | true |
| `--examples` | `all` (TodoLists+Reminders+Notes), `notes`, `none` | `all` |
| `--ci`, `--sonar`, `--agent-files` | bool | true |
| `--web` | `none`, `placeholder`, `angular`, `vite-react`, `vite-vue`, `vite-svelte`, `react-router`, `blazor-wasm` | `none` |
| `--web-name`, `--web-audience (public|admin)`, `--web-rendering (spa|ssr)` | | `Site`, `public`, `spa` |
| `--presentation` (multi) | `api`, `blazor-server`, `mcp`, `avalonia`, `wpf`, `worker` | `api` |

Combinaisons refusées déclarées par des symboles "never set" (ex. `authNoneNeedsExamplesNone`, `mySqlNotYetGenerated`, `refusedOptions` qui active `variants/refused/.../RefusedGenerationOptions.cs`). Calculés : `localAuth`, `externalAuth`, `publicApi`, `accounts`, `api`, `jobsWorker`, `needsApi`, `dotnetClient`, `accessKeys`, `scaffoldedAtBootstrap`... Les `sources` (`:1107` et suivants) excluent par condition des dossiers entiers, copient les starters (`.template.config/starters/<framework>/`) et les variants de base (`.template.config/variants/database-{sqlserver,sqlite}/`, `refused`, `no-client`, `no-sonar`, `web-PlaceholderSite`...). Opérations spéciales de conditionnelles pour `.axaml/.xaml/.runsettings/.env.example/.http/Dockerfile/.gitignore/.dockerignore/.editorconfig`.
`postActions` : un seul rappel, "bootstrap" à lancer (pas d'exécution automatique, `continueOnError`).

### 5.2 Item templates (`dotnet new` en dehors du projet)

| Template | shortName | Paramètres clefs |
|---|---|---|
| `.template.config/webapp` | `cleanarch-webapp` | `--framework (placeholder|angular|vite-*|react-router|blazor-wasm)`, `--audience`, `--rendering`, `--workspace`, `--scope`, `--port`, `--project-name`, `--examples`, `--files`, `--auth`, `--has-client`, `--ci`, `--agent-files` |
| `.template.config/webpackage` | `cleanarch-webpackage` | `--scope` (package TypeScript du workspace pnpm) |
| `.template.config/presentation` | `cleanarch-presentation` | `--framework (blazor-server|avalonia|mcp|wpf)`, `--project-name`, `--auth`, `--examples`, `--files`, `--database`, `--has-client`, `--has-api` |

### 5.3 Scripts / CLI de projet : `Tools/Tasks.cs` (2 134 lignes, programme C# fichier unique `dotnet run Tools/Tasks.cs <tâche>`)

Tâches (`Tasks.cs:70-100`) : `restore, build, test (--no-integration), coverage, format, format-fix, new-feature, new-use-case, remove-feature, migration-initial, migration-add, database-update, migration-bundle, openapi, api-client (--check), run, compose-up, compose-down, end-to-end (--keep-stack), sonar, bootstrap, presentation-scaffold, presentation-join, web-scaffold, web-join, hygiene, verify`, + `web-lockfiles` (template seul). Chaque tâche imprime la commande qu'elle lance. Générateurs : `Tools/NewFeature.cs` (5 891 l.), `Tools/NewUseCase.cs` (1 443 l.), `Tools/RemoveFeature.cs` (1 670 l.) ; garde-fous : `Tools/CoverageGate.cs` (612), `Tools/CheckDocPaths.cs`, `Tools/CheckWorkflows.cs`, `Tools/CheckComments.cs` (les trois derniers : template seulement), `Tools/TestSummary.cs`. `verify` (`Tasks.cs:651-684`) = refuse sans migration initiale, `hygiene`, `api-client --check`, `web-lockfiles --check`, restore, `dotnet format --verify-no-changes`, build, test, `dotnet ef migrations has-pending-model-changes` par contexte, listing des paquets vulnérables, + gate du lanceur. `new-feature` écrit le squelette dans toutes les couches (+ manifeste `.feature-manifest/`, page de doc) et imprime les éditions manuelles ; `remove-feature --apply` supprime un squelette intact (comparaison de hashes).
Pas de migration livrée : `bootstrap` crée la `InitialCreate` de chaque contexte (`Infrastructure.Persistence/Migrations` et `Infrastructure.Auth/Migrations` exclus de la génération, `template.json` `exclude`).

### 5.4 Lanceur `Launcher/CleanArch.Create` (outil .NET, `dnx CleanArch.Create`)

`Launcher/CleanArch.Create/CleanArch.Create.csproj` : `PackAsTool`, commande `cleanarch-create`, Spectre.Console 0.57.2, les manifestes `template.json` sont embarqués (`EmbeddedResource`) : le lanceur lit les manifestes et ne propose que ce que le template génère (ADR template 0004). Questions guidées (retour `esc`, résumé avant écriture, `--yes`, sortie plate sans terminal), installation du package `CleanArch.WebApi.Templates` à sa version, `dotnet new`, puis étapes (commit initial, bootstrap, commit bootstrap, copie `.env`). Sous-commandes `web-app`, `web-package`, `presentation` (item templates). 6 447 lignes, tests `Launcher/CleanArch.Create.Tests` (135 `[Fact]/[Theory]`, xUnit v3, `Spectre.Console.Testing`, `EndToEndTests.cs`).
**CLI de projet `dotnet cleanarch` : non réalisé**, c'est l'issue #163 et les ADR template 0017/0018/0019, statut *Proposed* (`docs/template/decisions/`, `docs/template/roadmap.md`) ; une branche distante `feat/cleanarch-cli-skeleton` existe.

### 5.5 Aspire, Docker, front-ends

- **Aspire : absent du code** ; seulement dans `docs/template/roadmap.md` ("an Aspire app host beside Docker Compose", issue #122).
- **Docker** : `docker-compose.yml` (services `db`, `mailpit`, `api`, `worker`, `objectstore` = SeaweedFS, `presentation-...portal`, `presentation-...mcp`, profil `sonar` = `sonarqube`+`sonar-scanner`) + `docker-compose.database.yml` (PostgreSQL 18.6-alpine). Toutes les images sont pinnées par tag **et** digest (test `ContainerImagePinningTests`, `CheckWorkflows`). `Src/Presentation/AppTemplate.Api/Dockerfile` : build SDK 10.0.403 noble, image finale `aspnet:10.0.14-alpine`, port 8080, non privilégiée (étape CI "Docker image"). `.env.example` (6,5 Ko), `.dockerignore`.
- **Front-ends** : workspace pnpm `Src/Web` (Node >= 24.11, pnpm 11) avec `packages/api-client` (client TypeScript généré depuis l'OpenAPI), apps générées par les CLI officielles des frameworks (Angular, Vite React/Vue/Svelte, React Router, Blazor WASM) selon les `scaffold.json` des starters (`.template.config/starters/*`) ; rendu serveur (Node) pour Angular et React Router ; client .NET Kiota pour Blazor WASM/Avalonia/WPF (`Src/Clients`, `kiota-lock.json`) ; ADR 0019, 0023, 0024.
- Hôtes serveur additionnels (starters) : Portal Blazor Server, Mcp (`ModelContextProtocol.AspNetCore`, `Directory.Packages.props:93`), Worker ; desktop Avalonia 12.1.4 / WPF.

---------------------------------------------------------------------------------------------------

## 6. Tests

| Élément | Constat | Preuve |
|---|---|---|
| Framework | **xUnit v3 4.0.2** sur **Microsoft.Testing.Platform** (`global.json`), `dotnet test --solution` ; **Shouldly 4.3.0**, **NSubstitute 6.2.0** (9 projets), **NetArchTest.Rules 1.3.2** (architecture) ; couverture `Microsoft.Testing.Extensions.CodeCoverage 18.12.0` (17 projets) | `Directory.Packages.props:137-159` ; `grep PackageReference Tests/*/*/*.csproj` |
| Conteneurs | **Testcontainers** 4.16 (`Testcontainers.PostgreSql`, `.MsSql`, `.MySql` référencés mais MySQL non généré) ; **pas de Respawn** : la remise à zéro est un `TRUNCATE ... RESTART IDENTITY CASCADE` généré depuis `information_schema` (`Tests/Integration/AppTemplate.Api.IntegrationTests/Infrastructure/TestDatabase.cs:44-62`, `DatabaseServer.cs:48-49`) ; un conteneur par `ICollectionFixture<ApiFixture>` (`ApiFixture.cs:13-72`) ; image Postgres pinnée par digest | idem |
| Nombre | **3 321** méthodes `[Fact]/[Theory]` dans `Tests/` (grep `^\s*\[(Fact|Theory)`), dont ~54+ théories ; **895** lignes `[InlineData|MemberData|ClassData]` ; + 135 pour le lanceur. Les lignes de test (71 084) valent ~2x celles de `Src` (34 352 sans le client généré). Plancher CI `minimum_tests=3000` (`ci.yml`, bloc `templateMaintenance`, passé à `--minimum-expected-tests`) | comptage grep ; `.github/workflows/ci.yml` |
| Par projet (Fact+Theory) | Application 609 ; Application.Auth 395 ; Domain 385 ; Api.IntegrationTests 413 ; Api.UnitTests 323 ; Infra.Auth 237 ; Architecture 195 ; Persistence 185 ; Storage 127 ; Application.Core 139 ; InMemory 71 ; Email 52 ; Presentation.Core 50 ; Infra.Core 40 ; Domain.Core 39 ; Auth.Integration 27 ; Client 26 ; EndToEnd 8 | grep par dossier |
| Sans Docker | tout sauf les 2 projets `*.IntegrationTests` (Testcontainers) : `dotnet run Tools/Tasks.cs test --no-integration` filtre les projets `IsTestProject` qui ne mentionnent pas Testcontainers (`Tools/Tasks.cs:1975-1995`). Les 440 tests d'intégration (413+27) exigent Docker ; `EndToEnd` (8 tests, `Stack.cs`, `Wire.cs`) tourne contre la pile Compose via la tâche `end-to-end`, hors `dotnet test` (pas de `IsTestProject`) | `Tasks.cs:1985` |
| Types de tests | unitaires par couche ; **architecture** (règles + tests "sensibles") ; **contrat** (`OpenApiDocumentTests` contre `Tests/Integration/.../OpenApi/v1.json`, 11 569 lignes committées ; `ErrorCodeTableTests`) ; intégration HTTP en mémoire (`Microsoft.AspNetCore.Mvc.Testing`, `ApiFactory`) sur vrai PostgreSQL ; contrats d'adaptateurs rejoués contre les doubles in-memory (ADR 0020) ; bout-en-bout ; charge k6 `Tests/Load/smoke.js` (non bloquant en CI) ; acceptation du template (25 jambes) | `docs/decisions/0020-one-risk-one-type-of-test.md` ; `.template.config/acceptance/matrix.json` (25 entrées) |
| Règles maison | un test qui achète une garantie est vu échouer une fois (rouge puis restauré) ; une population se mesure depuis l'arbre, jamais par un nombre tapé ; une condition de course est un fait via `Rendezvous` ; un test de refus porte son témoin | `docs/guides/testing.md:238-262` |
| Couverture | **Mesurée** (et plancher imposé) : `coverage.minimum` = **89** (% de lignes) ; `Tools/CoverageGate.cs` fusionne les Cobertura par (fichier, ligne). Dernière mesure documentée par le dépôt : 93,63 % (14 439/15 421) en Debug avec Docker, intégration comprise, le 2026-10-22 (message du commit `8200de77`, plancher passé de 90 à 89 "correction de la mesure"). Une mesure antérieure : 90,46 % pour 3 259 tests (2026-09-04, commit `556b66b5`). **Je n'ai pas relancé la mesure.** `coverage.runsettings` exclut tests, migrations, `obj/`, `Generated/`, auto-propriétés, `[ExcludeFromCodeCoverage]` | `coverage.minimum:2` ; `coverage.runsettings` ; `git show 8200de77` |
| CI | `ci.yml` : build-test (restore, format, build, vérif des projets de tests dans la solution, `dotnet test --coverage` + cobertura, `TestSummary`, `CoverageGate`), hygiène, vulnérables, image Docker, load (non bloquant), end-to-end, compose | `.github/workflows/ci.yml:29-412` |

---------------------------------------------------------------------------------------------------

## 7. Vie du projet (vue depuis le dépôt)

- **Historique** : 543 commits, du 2025-02-17 (démarrage du dépôt) au 2026-10-30 ; auteur ~95 % (495 + 19 commits du mainteneur sous deux identités, 28 `dependabot[bot]`, 1 `github-actions[bot]`). Activité concentrée : 85 commits le 2026-09-09, 98 le 2026-10-21, 63 le 2026-10-22 ; 20 le 29 octobre, 2 le 30. (Les dates en rafale suggèrent des fusions groupées ou une réécriture d'historique : non vérifié.)
- **Versions** : une seule étiquette, `v1.0.0` (2026-10-22, commit `9531963b` "chore: release 1.0.0"). Depuis, des commits `feat(template)!` (#169, #172) signalent des changements cassants vers 2.0 (flag `!`) : `--web` (une valeur par framework) et `--presentation` (l'API devient un choix) ; l'arbre porte `templateVersion` `1.1.0-dev` non commité (clone modifié).
- **CHANGELOG** (`CHANGELOG.md`, 100 Ko) : release-please écrit l'entrée 1.0.0 depuis les commits conventionnels (une seule ligne "Documentation") ; la description fonctionnelle de 1.0.0 est rédigée à la main sous "1.0.0: what the first release contains". Le fichier dit lui-même ce qui compte comme notable pour un template cloné.
- **Release du template** : `.github/workflows/template-release.yml` (230 l.) : `release-please` tient la PR de release (`.github/release-please/config.json`, type `simple`, version portée aussi dans `template.json` `$.symbols.templateVersion.parameters.value`), puis `pack` (paquet `CleanArch.WebApi.Templates` + outil `CleanArch.Create`, vérifie que le tag égale la version, installe le paquet et génère le projet par défaut, **attestation de provenance**), `attach` (GitHub Release), `publish` (nuget.org, par échange d'identité de workflow sans clé longue durée), `mirror` (GitHub Packages), `promote` (avance rapide de `main` vers la release). Branches : `develop` intègre, `main` porte les releases (ADR template 0006).
- **Release d'un projet généré** : `.github/workflows/release.yml` (281 l., tag `v*.*.*`) : gate complet de nouveau, audit des dépendances vulnérables, bundles de migrations, image vers GHCR ; absent pour SQLite.
- **Acceptation** : `.github/workflows/template-acceptance.yml` (1 047 l.) : package packé une fois, 25 jambes (matrice `.template.config/acceptance/matrix.json`, doc `docs/template/maintaining-the-template.md`) qui installent le `.nupkg`, génèrent sous un autre nom, vérifient l'absence de fuite du nom source, bootstrap, build avec warnings-as-errors, tests sans Docker, `hygiene` ; les jambes "défauts" lancent aussi les suites d'intégration et le bout-en-bout ; jambe Windows (ADR 0014) ; sélection des jambes touchées par une PR (`SelectLegs.cs`, ADR 0010).
- Autres workflows : `codeql.yml` (C# + TypeScript, hebdo), `sonarqube.yml`, `web.yml`. `dependabot.yml` : nuget, github-actions, docker, npm, docker-compose. Les actions GitHub sont pinnées par SHA (`ci.yml` `uses: ...@<sha> # v7.0.1`), contrôlé par `Tools/CheckWorkflows.cs`.
- **Décisions** : 28 ADR de projet (tous `Accepted`) sous `docs/decisions/`, 20 ADR du template sous `docs/template/decisions/` dont 3 `Proposed` (0017-0019), 1 remplacé (0003 par 0008).
- **Feuille de route** (`docs/template/roadmap.md`) : autres templates Vite, frameworks web restants, clients desktop/mobile (MAUI, Uno, WinUI), hôtes serveur (MVC, Razor Pages, gRPC, console), Aspire / Playwright / Storybook, CLI de projet épinglé (#163). "Pas prévu" : générateur au choix de l'utilisateur, desktop branché sur la couche application, MySQL.

---------------------------------------------------------------------------------------------------

## 8. Constats classés (preuves)

### Forces prouvées

- **S1. Règles d'architecture exécutables, avec preuve de sensibilité.** Chaque règle négative est doublée d'un test qui prouve qu'elle détecte une vraie violation, et refuse un ensemble vide : `LayerDependencyTests.cs:TheDomainRule_IsSensitive_AndDetectsARealViolation`, `RuleAssertions.RequireTypes` ; 195 tests sur 43 fichiers.
- **S2. Population mesurée sur disque.** `UseCaseConventionTests.cs:36-48` compare l'ensemble des use cases trouvés à celui lu dans les sources (`SourceDeclarations`), au lieu d'un plancher numérique.
- **S3. Registration DI vérifiée.** `ContainerCompositionTests` construit le conteneur en validation stricte et résout chaque use case/port, y compris sans auth et en hôte "jobs seuls" (15 tests).
- **S4. Propriété et concurrence de bout en bout.** 404 pour la donnée d'autrui (`OwnedAggregate.cs:20-23`), `ETag`/`If-Match` (`ApiControllerBase.cs:35-40,109-122`), jeton de concurrence `xmin` (`DatabaseProvider.cs:68-77`), idempotence par clé (`IdempotencyFilter.cs`).
- **S5. Contrat HTTP tenu par des tests.** OpenAPI committé (`Tests/Integration/.../OpenApi/v1.json`) + `OpenApiDocumentTests`, table des codes d'erreur vérifiée dans les deux sens (`ErrorCodeTableTests`), client Kiota régénéré en CI (`api-client --check`).
- **S6. Chaîne de livraison du template mûre** : 25 jambes d'acceptation, provenance attestée, publication NuGet par identité de workflow, miroir GHCR, actions pinnées par SHA, images pinnées par digest.
- **S7. Mesure de couverture documentée, avec garde.** 93,63 % mesuré le 2026-10-22 (message de `8200de77`), plancher 89, `CoverageSettingsTests` prouve qu'un corps async est mesuré.

### Faiblesses / frictions / écarts doc-code

- **F-OWN (mineur, doc contredit le code).** ADR 0009 affirme "Each query and each write ... filters by the caller's id in the same statement that finds the record" et "no code path holds another user's row". Or, pour les agrégats, `TodoListRepository.GetAsync` charge **par id seul** (`Src/Infrastructure/AppTemplate.Infrastructure.Persistence/Features/TodoLists/Access/Repositories/TodoListRepository.cs:24-29`) et la propriété est comparée **en mémoire** après coup (`Src/Application/AppTemplate.Application.Core/Common/Ownership/OwnedAggregate.cs:20`). Même chose pour `StoredFileRepository.GetAsync` et `IReminderRepository.GetAsync` (`Src/Domain/AppTemplate.Domain/Features/*/Repositories/I*Repository.cs:8`). La réponse reste un 404 identique (pas de fuite fonctionnelle), mais la ligne d'autrui est bien chargée et mappée. Seule la forme "simple" respecte la lettre (`NoteStore.cs:76,91`).
- **F-PRAGMA (mineur, règle non respectée par son auteur).** `AGENTS.md` : "Never silence a diagnostic with `#pragma`, `<NoWarn>`, a lowered severity" et `docs/guides/build-and-ci.md:71-73` le répète. Pourtant : `#pragma warning disable ASP0000` (`Src/Presentation/AppTemplate.Api/Common/Hosting/HttpPipelineModule.cs:67-69`), `<NoWarn>$(NoWarn);AV0029;AV0030</NoWarn>` (`Src/Presentation/AppTemplate.Api/AppTemplate.Api.csproj:16`), `<NoWarn>$(NoWarn);CS1591;CS1573</NoWarn>` (`Directory.Build.props:27`). Aucun test d'architecture ne l'interdit.
- **F-DOMERR (mineur, incohérence documentée à moitié).** Le code `domain.invariantViolated` répond **409** par `DomainGuard.Try` (`CommonErrors.cs:9-11`, `docs/reference/api.md:247`) mais **400** quand une `DomainException` n'est pas rattrapée (`GlobalExceptionHandler.cs:27-31`). `docs/architecture/domain-and-results.md:59-60` documente les deux ; la table de référence `api.md` ne liste que le 409, et `ErrorCodeStatusTests` ne parcourt que les `Error` déclarés dans les classes `*Errors` (`Tests/Presentation/AppTemplate.Api.UnitTests/Common/Errors/ErrorCodeStatusTests.cs:14-27`), pas la réponse codée en dur du gestionnaire global : le 400 n'est tenu par aucun test de cette famille.
- **F-SCAN (mineur, message de test faux).** `UseCaseConventionTests.cs:58` et `:197` disent "registration is explicit rather than scanned" ; l'enregistrement est justement par scan de namespace (`ApplicationModule.cs:156-165`, `ApplicationCoreModule.cs:49-65`) et `docs/guides/adding-a-feature.md:153` dit "Nothing is registered by hand".
- **F-README (mineur).** Le tableau d'options du `README.md` (`:58-70`) omet `--database` (présent dans `template.json` et `docs/template/using-the-template.md:164`) ; la ligne "Web apps if you want them" ne cite ni React Router ni Blazor WebAssembly ("Vue, React, Svelte, Angular or a static page", `README.md:21`) alors que `--web` accepte `blazor-wasm` et `react-router`.
- **F-LOSS (choix assumé, ADR 0006).** Événements de domaine au plus une fois, sans outbox : un consommateur qui lève est perdu (`DomainEventDispatcher.cs:35-43`, `DomainEventDispatchSaveChangesInterceptor.cs:134-140`). Les consommateurs appellent `SaveChangesAsync` eux-mêmes après le commit initial (`CancelRemindersOnTodoItemCompletedConsumer.cs`), donc deux commits distincts ; la compensation est le balayage (`IDeletedAccountDataPolicy`).
- **F-LOAD (choix, coût).** Toute écriture sur un agrégat charge l'agrégat entier (liste + items + tags, `AsSplitQuery`, `TodoListRepository.cs:24-29`, plafond 500 items `TodoList.cs:12`), puis réconcilie ligne par ligne (`TodoListMapper.cs:68-121`) : mapper + tracker + repository par agrégat, ce que la doc assume ("pays for a mapper, a tracker and a repository").
- **F-LOWER (info).** Le plancher de couverture est passé de 90 à 89 (`git show 8200de77`), expliqué dans le message comme correction de mesure (93,63 % mesuré) ; la règle `AGENTS.md` dit "never lowered to pass".
- **F-DEAD (info).** `Testcontainers.MySql` est déclaré (`Directory.Packages.props:154`) et deux csproj de test le référencent, alors que `--database mysql` est refusé par le template (`template.json`, symbole `mySqlNotYetGenerated`).
- **F-CLONE (info).** L'arbre cloné diffère de HEAD (`template.json:190`).

### Absences (à ne pas supposer)

- Pas de médiateur/pipeline behaviours, pas d'AutoMapper, pas de repository générique, pas d'`IQueryable` sur un port, pas de soft delete, pas d'outbox, pas de Minimal APIs, pas de HTTPS/HSTS dans l'app, pas de cache de sortie (`docs/architecture/README.md`, section "What is deliberately absent").
- Pas d'Aspire, pas de CLI de projet `dotnet cleanarch`, pas de Respawn, pas de NUnit, pas de génération MySQL.
- Pas de multi-tenant, pas de permissions fines (une politique `Administrator`).

## Non vérifié

- Compilation, tests, couverture, génération `dotnet new` : rien exécuté.
- Équivalence exacte entre le nombre de jambes de `matrix.json` (25) et le tableau de `docs/template/maintaining-the-template.md`.
- Le nombre de cas réels d'exécution des théories (seules les lignes d'attributs ont été comptées).
- Les forks de forme par défaut non lus : le contenu des starters (`Portal`, `Mcp`, `Worker`, desktop) et les 7 workflows hors `ci.yml` n'ont été lus que par structure.
- Le détail des modules Auth (JWT, refresh, passkeys), Storage, Email, et des features Reminders/Files, vus seulement par structure et listes de use cases.
