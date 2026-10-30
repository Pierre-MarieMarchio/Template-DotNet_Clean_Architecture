# Cartographie de NOTRE template (clone `develop`, HEAD 5f22faa5), version B

Clone lu : `/tmp/claude-1000/-home-pmmarchio-code-Template-DotNet-Clean-Architecture/608f3f2d-3c02-4530-9e46-6f8e7e0e445b/scratchpad/repos/ours`.
Méthode : lecture seule, aucun build, aucun test lancé. Les chiffres de tests sont des décomptes
d'attributs `[Fact]`/`[Theory]` dans les sources, pas des résultats d'exécution.
Chemins relatifs à la racine du clone, sauf mention.

Réserve sur l'état du clone : `git status --short` montre `.template.config/template.json` modifié
(la valeur `templateVersion` vaut `1.1.0-dev` dans l'arbre de travail, `1.0.0` dans HEAD :
`git diff` ligne 190 du fichier). Ce n'est pas de mon fait ; je cite `1.1.0-dev` comme l'état lu et
`1.0.0` comme l'état commité. Le tag unique du dépôt est `v1.0.0`.

---

## 0. Vue d'ensemble en dix lignes

- Un dépôt qui est à la fois (a) une solution .NET 10 qui compile telle quelle (`AppTemplate.sln`,
  tous les marqueurs `//#if` sont des commentaires C#) et (b) un paquet `dotnet new`
  (`.template.config/template.json`, 2275 lignes, 104 symboles, 25 `sources`).
- Clean Architecture stricte, 14 projets `Src/` en 4 couches plus `Clients`, chaque couche ayant un
  projet `*.Core` « sans feature » (`IsSdkProject`).
- Pas de médiateur, pas de pipeline de behaviours : un use case = une classe + une interface nommée
  + une commande/requête + un validateur FluentValidation (ADR 0003).
- Erreurs : `Result<T>` + `Error` typé pour l'attendu ; `DomainException` pour les invariants
  (mappée en 400 par le handler global) ; `ConcurrencyConflictException` en 409.
- Domaine non mappé par EF (ADR 0005) : agrégat, enregistrement (`*Record`), mapper, tracker.
- Domain events levés dans l'agrégat, drainés avant `SaveChanges`, publiés en processus après commit,
  au plus une fois, sans outbox (ADR 0006).
- Deux formes de feature : agrégat + use cases, ou service au-dessus d'un store (ADR 0002).
- Propriété des données dans la requête, 404 jamais 403 (ADR 0009).
- Outillage : `Tools/Tasks.cs` et 5 générateurs en C# « file-based » (~15 000 lignes), un lanceur
  interactif `CleanArch.Create` (Spectre.Console), 3 item templates, 11 starters, 25 jambes de recette.
- Tests : xUnit v3 + Shouldly + NSubstitute + NetArchTest + Testcontainers ; 3321 déclarations
  `[Fact]`/`[Theory]` sous `Tests/` (+135 dans le lanceur) ; plancher de couverture 89 %.

---

## 1. Projets, références et règles qui les tiennent

### 1.1 Projets (`Src/`), références par `<ProjectReference>`

| Projet | Contenu | Références projet | Paquets notables |
|---|---|---|---|
| `Domain/AppTemplate.Domain.Core` | `Entity`, `AggregateRoot`, `AuditableAggregateRoot`, `UserId`, `IDomainEvent`, `DomainException`, `IAuditable`/`IVersioned`/`IOwnedAggregate` (10 fichiers) | aucune | aucun |
| `Domain/AppTemplate.Domain` | agrégats, value objects, events, `I*Repository` de TodoLists, Reminders, Files, tagging | Domain.Core | aucun |
| `Application/AppTemplate.Application.Core` | `Result`, `Error`, `IUseCase`, validation, pagination (`Collections/`), concurrence, idempotence, ports transverses (`Common/Ports/`), `OwnedAggregate` | Domain.Core | FluentValidation, DI/Logging abstractions |
| `Application/AppTemplate.Application` | features métier : use cases, ports, DTO, validateurs, consumers | Domain, Application.Core | idem + Configuration.Abstractions |
| `Application/AppTemplate.Application.Auth` | 7 features d'authentification (AccessKeys, Accounts, ExternalLogins, Passkeys, SecurityEvents, Sessions, TwoFactor), 84 `*UseCase.cs` | Application.Core | FluentValidation |
| `Infrastructure/...Infrastructure.Core` | mécanismes de sauvegarde : intercepteurs, unité de travail, cache, contextes | Application.Core | EF Core, HybridCache, providers Npgsql/SqlServer/Sqlite (balises) |
| `...Infrastructure.Persistence` | `AppDbContext`, records, configurations, mappers, trackers, repositories, queries | Application, Application.Core, Infrastructure.Core | EF Core (Design en `PrivateAssets`) |
| `...Infrastructure.Auth` | ASP.NET Identity, JWT, refresh tokens, passkeys, `AuthDbContext` | Application.Core, Application.Auth, Infrastructure.Core | Identity.EFCore, JwtBearer, DataProtection.EFCore |
| `...Infrastructure.Email` | MailKit, gabarits de mail | Application, Application.Core, Infrastructure.Core | MailKit |
| `...Infrastructure.Storage` | adaptateurs S3 (SeaweedFS en compose) | Application, Application.Core | AWSSDK.S3 |
| `...Infrastructure.InMemory` | doubles publics (email, horloge, fichiers, identité externe, rappels) | Application, Application.Core, (Application.Auth) | DI abstractions |
| `Presentation/AppTemplate.Presentation.Core` | jobs périodiques, observabilité, HTTP sortant, options d'auth externe | Application.Core | OpenTelemetry, Http.Resilience |
| `Presentation/AppTemplate.Api` | hôte HTTP : contrôleurs, contrats, pipeline | Application, Application.Core, (Auth), Presentation.Core, tous les modules d'infra | Asp.Versioning, Scalar, OpenApi |
| `Clients/AppTemplate.Client` | client .NET généré par Kiota (dossier `Generated/`) | aucune (n'atteint le cœur que par l'API) | Kiota.Bundle, Http.Resilience |

Sources : `Src/**/*.csproj`. Taille hors `Generated/` et `Migrations/` : Domain 39 fichiers / 1 572
lignes ; Application 506 / 11 956 ; Infrastructure 210 / 12 480 ; Presentation 200 / 7 830 ; Clients
12 / 514. `Tests/` : 563 fichiers / 71 084 lignes (environ deux lignes de test par ligne de source).

Aucune migration EF n'est versionnée dans le dépôt : `Src/Infrastructure/AppTemplate.Infrastructure.Persistence/Migrations`
n'existe pas ; `.template.config/TemplatePackage.csproj:49` les exclut du paquet ; `bootstrap` crée
`InitialCreate` (`Tools/Tasks.cs:603-637`).

### 1.2 Règles de build (`Directory.Build.props`)

- `net10.0`, `Nullable`, `ImplicitUsings`, `InvariantGlobalization` (l. 4-8).
- `EnableNETAnalyzers`, `AnalysisLevel=latest-recommended`, `EnforceCodeStyleInBuild`,
  `TreatWarningsAsErrors=true` (l. 11-16).
- `NuGetAudit` en mode `all`, niveau `low` (l. 19-21) ; `Deterministic` ; `GenerateDocumentationFile` ;
  `<NoWarn>CS1591;CS1573</NoWarn>` (l. 27) ; versions centralisées (`Directory.Packages.props`,
  épinglage transitif activé).
- `Directory.Build.targets` : tout projet `IsTestProject` devient `OutputType=Exe`
  (runner Microsoft.Testing.Platform, `global.json:5-7`).
- Aucun analyzer tiers référencé (pas de SonarAnalyzer ni StyleCop dans `Directory.Packages.props`) ;
  les règles `S*`, `AV*` de `.editorconfig` ne s'appliquent donc qu'avec un outil externe (Sonar).
- `.editorconfig` (190 lignes) : namespaces file-scoped en erreur, `using` hors namespace, champs
  privés `_camelCase` (warning), interfaces en `I*` (erreur), paramètres de type `T*` (erreur),
  `CS8618`/`CS8602` en erreur, UTF-8 BOM + LF pour `.cs` et `.md`.

### 1.3 Tests d'architecture (`Tests/Architecture/AppTemplate.Architecture.Tests`)

xUnit v3 + NetArchTest.Rules 1.3.2 + Shouldly ; 195 déclarations, 43 fichiers, ~8 900 lignes.
Dossiers : `Rules/` (40 fichiers), `Composition/` (3), `Fixtures/` (5).

| Rôle | Tests (fichier) |
|---|---|
| Sens des dépendances | `LayerDependencyTests` (domaine ne dépend de rien, y compris EF, ASP.NET, DI, FluentValidation, System.Text.Json ; application interdit EF, ASP.NET, MailKit, **MediatR** : `LayerDependencyTests.cs:12-50`) ; `ModuleDependencyTests` (17 règles : module d'infra ne dépend ni d'un autre module ni de la présentation, seul un hôte référence l'infra, aucun hôte ne référence un autre hôte, tout client n'atteint le cœur que par l'API) ; lecture des manifestes d'assemblies (`TypeFacts.ReferencesAssembly`) |
| Isolation des features | `FeatureIsolationTests` (une feature n'atteint une autre que par ports et events ; sans dossier Domain = service sur store) |
| Forme des use cases | `UseCaseConventionTests` (sous `Features/<F>/UseCases/`, scellés, publics, un use case par dossier nommé pour lui) |
| Ports | `PortConventionTests` (pas de façade multi-capacité, plafond de 6 opérations, chaque port a un consommateur), `StorageVocabularyTests` (quatre mots : Repository, Queries, Store, Table), `StorageShapeTests` (aucun `IQueryable` exposé, aucun drapeau « supprimé » en base), `AdapterVisibilityTests` (adaptateurs `internal`, doubles publics) |
| Domaine | `DomainModelTests` (agrégats scellés, pas d'état publiquement modifiable, VO et events immuables), `DomainEventTests` (chaque event est consommé ou listé comme non consommé avec raison, dans les deux sens) |
| Layout | `LayoutConventionTests` (698 lignes : vocabulaire de dossiers par projet, namespace = dossier, un type public par fichier nommé comme lui, pas de dossier vide), `FeatureFolderVocabularyTests` (un fichier porte le mot de nature de son dossier) |
| HTTP | `HttpSurfaceTests` (endpoints anonymes = liste fermée `_anonymousActions`, pas de header `Link`, pas de `Deprecation`/`Sunset`), `IdempotentActionTests`, `RequestPipelineTests`, `OutboundHttpTests` (aucun `new HttpClient`), `HttpCommonBoundaryTests` |
| Contrats de collection | `CollectionContractTests` (records de pagination sans constructeur public, politiques cohérentes) |
| Composition DI | `ContainerCompositionTests` (conteneur API sous validation stricte, résout tous les use cases et ports, construit sans authentification, refuse un use case sans interface nommée), `SharedInstanceRegistrationTests` |
| Jobs | `BackgroundWorkTests` (aucun service d'arrière-plan ne prend le bail de leader ; le bail est pris par un use case ; portée d'arrière-plan) |
| Docs et config | `ConfigurationSurfaceTests` (chaque section liée a sa table dans `docs/reference/configuration.md`), `ErrorCodeTableTests` (codes d'erreur source <-> `docs/reference/api.md`), `DocumentationCitationTests` (chemins, liens, tâches, options cités par les pages existent), `ContainerImagePinningTests` (images pinnées par digest) |
| Couverture | `CoverageFloorTests`, `CoverageSettingsTests` |
| Paquet / template | `TemplatePackagingTests` (1099 lignes), `AcceptanceSelectionTests`, `PackageBoundaryTests`, `PortabilityTests` (seuls `TemplatePackagingTests` et `AcceptanceSelectionTests` sont exclus du paquet, `.template.config/TemplatePackage.csproj:48`) |

Particularité méthodologique : 17 tests nommés `...IsSensitive...` prouvent que la règle échoue sur un
cas fautif (négatifs de contrôle) ; `RuleAssertions.RequireTypes` refuse de passer sur un ensemble
vide ; `UseCaseConventionTests.cs:30-45` mesure l'ensemble candidat contre les déclarations sur disque
plutôt que contre un plancher numérique (`SourceDeclarations`).

### 1.4 Garde-fous hors tests (`Tools/`, lancés par `verify`)

`Tools/Tasks.cs:919-931` (`Gates`) : `--self-test` de `NewFeature`, `NewUseCase`, `RemoveFeature`,
`CoverageGate`, `CheckDocPaths`, `CheckWorkflows`, `CheckComments`, puis la passe réelle.
`Tools/CheckComments.cs:12-50` refuse tout commentaire C# sauf marqueurs de template, en-têtes
`<auto-generated>` et XML-doc de l'API (`<summary>`, `<param>`, `<response>`) ; interdit `#region` et
tout commentaire après une directive ; refuse les phrases narrant l'historique dans les `.md`.
Conséquence mesurable : le code `Src/` ne contient presque aucun commentaire.

---

## 2. Chemin d'une requête, fichier par fichier

Exemple pris : TodoLists (forme riche, agrégat). Le même squelette vaut pour les autres.

### 2.1 Écriture : `POST /api/v1/todo-lists`

1. **Pipeline HTTP** : `Src/Presentation/AppTemplate.Api/Program.cs:88-136` (`AddControllers`,
   `AddHttpPipeline`, `UseHttpPipeline`, `UseAuthentication`, `UseAuthorization`, `MapControllers`).
   Politique de repli : utilisateur authentifié requis (`Program.cs:112-115`), donc tout endpoint est
   fermé sauf `[AllowAnonymous]` listé dans `HttpSurfaceTests`.
2. **Filtre d'idempotence** : `Api/Common/Idempotency/IdempotencyFilter.cs` (`IAsyncResourceFilter`),
   déclenché par `[Idempotent]` sur l'action et l'en-tête `Idempotency-Key` ; clé portée par
   l'utilisateur (ADR 0010).
3. **Contrôleur** : `Api/Features/TodoLists/Controllers/TodoListsController.cs:65-80` (`Create`).
   Reçoit `CreateTodoListRequest(string Name)` (`Contracts/Requests/CreateTodoListRequest.cs`),
   construit `new CreateTodoListCommand(request.Name)`, appelle `ICreateTodoListUseCase.ExecuteAsync`,
   convertit via `TodoListResponseMapping.ToListResponse` (`Contracts/Mapping/TodoListResponseMapping.cs:75`)
   puis `CreatedOrProblem(result, nameof(GetTodoList), ...)` (`Common/Controllers/ApiControllerBase.cs:82-99`)
   qui pose l'en-tête `ETag` depuis la version et répond 201 `CreatedAtRoute`.
4. **Use case** : `Application/Features/TodoLists/UseCases/Commands/CreateTodoList/CreateTodoListUseCase.cs:13-47`.
   Ordre : `currentUser.RequireUserId()` (401 `auth.required` avant toute validation) ->
   `validator.EnsureValidAsync` (FluentValidation, `Core/Common/Validation/ValidationExtensions.cs:8-18`)
   -> `TodoList.Create(userId, name, now)` -> `repository.Add` -> `unitOfWork.SaveChangesAsync` ->
   `TodoListDtoMapping.Detail(todoList)` (`Model/Mapping/TodoListDtoMapping.cs:9`).
5. **Domaine** : `Domain/Features/TodoLists/Model/Entities/TodoList.cs:28-35` : `Guid.CreateVersion7()`,
   `TodoListName.Create` (VO, lève `DomainException` si vide ou > 200,
   `ValueObjects/TodoListName.cs`), `RaiseDomainEvent(new TodoListCreatedDomainEvent(...))`.
6. **Repository** : `Infrastructure.Persistence/Features/TodoLists/Access/Repositories/TodoListRepository.cs:68-77`
   (`Add`) : `mapper.ToNewRecord` (`Rows/Mapping/TodoListMapper.cs:29-57`) -> `context.TodoLists.Add(record)`
   -> `tracker.Track(aggregate, record)` (`Rows/Tracking/TodoListTracker.cs`, dérive de
   `AggregateTracker<TAggregate,TRecord>`).
7. **Unité de travail** : `Infrastructure.Core/Common/Saving/EfUnitOfWork.cs:9-21` ;
   `DbUpdateConcurrencyException` devient `ConcurrencyConflictException`.
8. **Intercepteurs de sauvegarde** (branchés par `PersistenceModule.cs:251-253`) :
   - `AggregateFlushSaveChangesInterceptor` : avant l'enregistrement, chaque tracker écrit l'état
     de l'agrégat dans les records (`FlushTo`), après il rafraîchit la version (`RefreshFromStore`) ;
   - `AuditingSaveChangesInterceptor.cs:345-370` : `CreatedAt/By`, `LastModifiedAt/By` via `IAuditable` ;
   - `VersionStampingSaveChangesInterceptor` : compteur de version (SQL Server/SQLite) ; sous
     PostgreSQL la version est `xmin` (`Infrastructure.Core/Common/Contexts/DatabaseProvider.cs:68-77`, `xid`,
     jeton de concurrence) ;
   - `DomainEventDispatchSaveChangesInterceptor.cs:38-135` : `Collect` au `SavingChanges` (drain des
     events des trackers), publication au `SavedChanges` après commit.
9. **Base** : `AppDbContext` (`Persistence/Common/Contexts/AppDbContext.cs`) ; un schéma par feature
   (`todo`, `reminders`, `files`, `notes`, `platform`, l. 30-49) ; `TodoListRecordConfiguration.cs:16`
   `ToTable("TodoLists", AppDbContext.TodoSchema)`, trois index `(OwnerId, <champ>, Id)` pour les tris.
10. **Après commit** : `DomainEventDispatcher.cs:177-210` résout `IDomainEventConsumer<TEvent>` dans
    le conteneur ; une exception de consommateur est journalisée et avalée.
11. **Retour** : `Result<Versioned<TodoListDetailDto>>` -> `Result<Versioned<TodoListResponse>>` ->
    201 + `ETag` + `Location`. Échec : `Error.ToActionResult(HttpContext)`
    (`Api/Common/Errors/ErrorMapping.cs:127-172`) -> `application/problem+json` avec extension `code` ;
    table `ErrorType` -> statut l. 178-189.

Mise à jour avec concurrence (`PUT {id}`, `Rename`) : `ReadPrecondition` lit `If-Match`
(`ApiControllerBase.cs:109-122`, 428 si requis et absent, 412 si périmé) ; `RenameTodoListUseCase`
passe par `ITodoListService.LoadOwnedAsync` (`Services/TodoListService.cs:12-30`) qui appelle
`OwnedAggregate.Require` (`Core/Common/Ownership/OwnedAggregate.cs:10-31`) : agrégat absent ou de
quelqu'un d'autre -> même `NotFound` ; version non satisfaite -> `PreconditionFailed`.

### 2.2 Lecture paginée : `GET /api/v1/todo-lists?paging=cursor&sort=...`

1. Contrôleur : `TodoListsController.cs:27-47` (`GetAll`) ; `[FromQuery] GetTodoListsRequest`
   (8 champs : paging, page, pageSize, cursor, sort, search, createdAfter, createdBefore, en
   `string?`/`int?` : pas de validation dans le contrat).
2. Use case : `Queries/GetTodoLists/GetTodoListsUseCase.cs:8-31` : `RequireUserId` ->
   `GetTodoListsRequestBinder.Bind(query)` -> `queries.GetForOwnerAsync`. Pas de FluentValidation ici ;
   la liaison est un `Result` (`Core/Common/Collections/CollectionBinding.cs:8-42`).
3. Liaison : `CollectionOrder.Parse(paging, sort, policy)` avec `TodoListCollectionPolicy`
   (`Model/Policies/TodoListCollectionPolicy.cs` : champs triables `name`, `createdAt` (keyset),
   `lastModifiedAt` (offset seul), tri par défaut `createdAt:desc`, page 20, max 100, 3 termes de tri) ;
   `TodoListFilter.Create` (`Ports/TodoListQueries/TodoListFilter.cs:24-81` : recherche, bornes de
   date ISO 8601, cohérence) ; `PageRequest.Create` (`PageRequest.cs:88-131` : offset XOR curseur) ;
   le curseur est du JSON base64url signé de champ, direction, clé, id (`Cursor.cs:236-330`, 512
   caractères max). Sortie : `FeaturePageRequest<TodoListFilter>`.
4. Port : `ITodoListQueries.GetForOwnerAsync` (`Ports/TodoListQueries/ITodoListQueries.cs:11-14`).
5. Adaptateur : `Persistence/Features/TodoLists/Access/Queries/TodoListQueries.cs:16-35` :
   `AsNoTracking().Where(OwnerId == caller)` (propriété dans la requête), `ApplyFilter` (`LIKE` en
   minuscules avec échappement via `TodoListLikePattern`), offset : `CountAsync` + `Skip/Take`
   (l. 77-95) ; keyset : `Take(pageSize + 1)`, `TodoListSortMap.ApplyKeyset` / `ApplyOrder` (tri
   secondaire sur `Id`), `Cursor.After(...).Encode()` du dernier élément (l. 98-125). Projection
   directe en `TodoListSummaryDto`, sans charger l'agrégat.
6. Réponse : `TodoListResponseMapping.ToPageResponse` -> `PagedResponse.From`
   (`Api/Common/Contracts/PagedResponse.cs`) : `Items, PageSize, Page, TotalCount, TotalPages,
   HasNextPage, NextCursor` ; `OkOrProblem` (`ApiControllerBase.cs:17-22`). Aucun header `Link`
   (interdit par `HttpSurfaceTests.cs:41`).

Lecture d'un seul agrégat : `ITodoListQueries.GetDetailAsync` projette directement
`Versioned<TodoListDetailDto>` avec `Id == id && OwnerId == owner` (`TodoListQueries.cs:37-58`) ;
le contrôleur répond 304 si `If-None-Match` correspond (`ApiControllerBase.cs:26-41`).

---

## 3. Concepts

| Sujet | Réponse dans le code |
|---|---|
| CQRS | Dossiers `UseCases/Commands/` et `UseCases/Queries/` ; la lecture passe par un port `*Queries` qui projette sans charger l'agrégat, l'écriture par le `Repository`. Pas de modèle de lecture séparé en base : même base, mêmes tables. |
| Médiateur | Non. ADR 0003 : « no MediatR, no dispatcher and no pipeline behaviour ». `MediatR` est dans la liste interdite de l'application (`LayerDependencyTests.cs:46`). |
| Pipeline / behaviours | Aucun. Validation, journalisation et commit sont écrits dans chaque use case. Le « pipeline » est celui d'ASP.NET (middlewares, filtres : `IdempotencyFilter`, `HttpPipelineModule`, `RequestLoggingMiddleware`) et celui de EF (intercepteurs de sauvegarde). |
| Enregistrement DI | Explicite, par feature : `ApplicationModule.AddTodoLists` (`Application/ApplicationModule.cs:69-82`) via `AddFeature`, `AddUseCases` scanne les types `IUseCase` et exige exactement une interface nommée (`ApplicationCoreModule.cs:49-87`). Chaque use case est `AddScoped`. |
| Validation | FluentValidation `AbstractValidator<TCommand>` (un par use case), appelé à la main par `EnsureValidAsync` ; l'échec devient `Error.Validation` avec `Details` par champ (`ValidationError.From`). Second niveau : les invariants des VO/agrégat lèvent `DomainException`. Les contrats HTTP restent des records nus. |
| Erreurs | `Result`/`Result<T>` (`Core/Common/Results/Result.cs`), conversion implicite de `T`, `.To<TOther>()` pour propager un échec. `Error(Code, Message, ErrorType, Details?)`, 8 `ErrorType` (Validation, NotFound, Unauthorized, Forbidden, Conflict, TooManyRequests, PreconditionFailed, PreconditionRequired). Les exceptions ne servent qu'aux invariants (`DomainException` -> 400 `domain.invariantViolated`) et à la concurrence (`ConcurrencyConflictException` -> 409 `concurrency.conflict`) ; `GlobalExceptionHandler.cs:19-47`. Les codes d'erreur ont chacun une ligne dans `docs/reference/api.md` (`ErrorCodeTableTests`). |
| Domain events | Levés dans l'agrégat (`AggregateRoot.RaiseDomainEvent`, `Domain.Core/.../AggregateRoot.cs:16-20`) ; collectés avant la sauvegarde ; publiés en processus après commit (`SavedChanges`), au plus une fois, sans outbox, ni retry ; un consommateur qui lève est journalisé et sauté ; `SaveChangesFailed` rend les events à l'agrégat (`DomainEventDispatchSaveChangesInterceptor.cs:102-119`). Un fait sans agrégat est publié par le use case via `IDomainEventPublisher` après son commit. Garantie réelle = un balayage périodique sous bail (ADR 0006/0011/0025). |
| Entités et bases | `Entity<TId>` (égalité par type + id), `AggregateRoot<TId>` (liste d'events), `AuditableAggregateRoot<TId>` (+ `IAuditable`, `IVersioned`, `uint Version`, mutateurs via implémentation d'interface explicite). `UserId` est un `record` à constructeur privé. |
| Mapping | Manuel, statique. Application : `TodoListDtoMapping` (`internal static`) agrégat -> DTO ; API : `TodoListResponseMapping` DTO -> réponse ; Persistence : `ITodoListMapper` record <-> agrégat (`ToAggregate`, `ToNewRecord`, `WriteTo` avec réconciliation des enfants). Aucun AutoMapper/Mapster/MediatR dans `Src/` (le seul nom `MediatR` est dans la liste d'interdits d'un test). |
| Autorisation | Politique de repli « utilisateur authentifié » (`Program.cs:112-115`) ; `[AllowAnonymous]` sur 12 actions d'auth (12 lignes d'attribut sous `Api/Features`), liste fermée `_anonymousActions` (12 noms, `HttpSurfaceTests.cs:11-26`) ; `[Authorize(Policy=Administrator)]` sur 5 contrôleurs d'administration ; pas d'autorisation par ressource dans les contrôleurs (voir propriété). Mode `--auth none` : repli `RequireAssertion(_ => true)` et démarrage refusé si un `[Authorize]` subsiste (`Program.cs:138-161`). |
| Propriété des données | `IOwnedAggregate.OwnerId` ; `ICurrentUser.UserId` (jamais pris du corps) ; filtre `OwnerId == caller` dans chaque requête et chaque écriture ; `OwnedAggregate.Require` pour les agrégats ; réponse identique pour « absent » et « à autrui » (404) ; ADR 0009 ; `OwnershipIsolationTests` (intégration). Une requête par feature traverse les propriétaires exprès : `ListDataOwnerIdsAsync` pour le balayage de suppression de compte (ADR 0012). |
| Concurrence | `Versioned<T>` ; `ETag` = version ; `If-Match` -> `VersionPrecondition` ; 412/428 ; option `ConcurrencyOptions.IfMatch` (requis ou non). ADR 0008. |
| Idempotence | `Idempotency-Key` sur les POST marqués `[Idempotent]`, empreinte de requête, table `IdempotencyRecord`, purge périodique (`PurgeExpiredIdempotencyKeysUseCase`). |
| Suppression de compte | Chaque feature décide (supprimer ou anonymiser) par un consommateur de `UserAccountDeleted` plus une politique `IDeletedAccountDataPolicy`, balayage `SweepDeletedAccountDataUseCase` (ADR 0012). |

---

## 4. Conventions

### 4.1 Nommage et organisation

- Organisation **par feature à l'intérieur de chaque couche**, pas un projet par feature :
  `Features/<Feature>/` existe dans Domain, Application, Persistence, Api, et les tests le reflètent.
  Les mécanismes sans feature vivent sous `Common/`. `ModuleDependencyTests` interdit à `Common/` de
  nommer une feature.
- Chaque dossier de feature porte un mot de nature fermé, vérifié par `LayoutConventionTests` et
  `FeatureFolderVocabularyTests` : Domain `Model/{Entities,ValueObjects,Events}`, `Repositories/` ;
  Application `UseCases/{Commands,Queries}`, `Model/{Dtos,Errors,Mapping,Policies}`, `Ports/<Port>/`,
  `Services/`, `Services/Consumers/`, `Helpers/` (classes statiques seulement) ; Persistence
  `Access/{Queries,Repositories,Stores}`, `Rows/{Models,Configurations,Mapping,Tracking}`,
  `Adapters/` ; Api `Controllers/`, `Contracts/{Requests,Responses,Mapping}`, `Jobs/`, `Composition/`.
- Suffixes : `*Command`/`*Query`/`*Request`, `*CommandValidator`, `*UseCase` + `I*UseCase`, `*Dto`,
  `*Record`, `*Mapper`, `*Tracker`, `*Repository`, `*Queries`, `*Store`, `*Policy`, `*DomainEvent`,
  `*Errors`, `*Response`. Un type public par fichier, nom du fichier = nom du type, namespace =
  dossier (tests `LayoutConventionTests`).
- Champs privés `_camelCase`, interfaces `I*`, paramètres de type `T*` (`.editorconfig`).
- Routes : `api/v{version:apiVersion}/<kebab-pluriel>` avec `Asp.Versioning` en segment d'URL.
- Codes d'erreur : `feature.cause` en camelCase (`todoList.notFound`, `request.validationFailed`).
- Tests : un projet de test par projet source, dossiers en miroir, un dossier par use case
  (`Tests/Application/AppTemplate.Application.UnitTests/Features/TodoLists/UseCases/Commands/CreateTodoList/CreateTodoListUseCaseTests.cs`),
  méthodes `Sujet_Comportement` (CA1707 désactivé sous `Tests/**`, `.editorconfig`).
  Tests d'intégration regroupés par thème (`TodoLists/`, `Security/`, `Saving/`, `OpenApi/`...).

### 4.2 Forme exacte d'un use case (exemple complet, 4 fichiers + test)

Dossier `Src/Application/AppTemplate.Application/Features/TodoLists/UseCases/Commands/CreateTodoList/` :

```csharp
// CreateTodoListCommand.cs
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

// CreateTodoListUseCase.cs (corps, namespaces omis)
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

Le test miroir `CreateTodoListUseCaseTests` (NSubstitute pour `ITodoListRepository`/`IUnitOfWork`,
`StubCurrentUser`, `StubDateTimeProvider`) vérifie : anonyme refusé avant validation, rien n'est écrit
sur échec, propriétaire = appelant jamais le corps, bornes de longueur, event levé.

La requête paginée a la même forme à trois fichiers + un `GetTodoListsRequestBinder` statique
(`Queries/GetTodoLists/`). Un use case de lecture de détail et un use case de mise à jour passent
par `ITodoListService.LoadOwnedAsync`.

### 4.3 Deux formes de feature (ADR 0002, `docs/architecture/feature-shapes.md`)

- **Riche (agrégat)** : TodoLists, Reminders, Files ; 66 use cases dans `Application`, un dossier par
  opération.
- **Simple (service sur store)** : Notes ; `INoteService`/`NoteService` (`Features/Notes/Services/`)
  sur `INoteStore` (écritures) et `INoteQueries` (lectures), l'enregistrement de persistance est le
  seul modèle (`NoteRecord`). `FeatureIsolationTests.AFeatureWithoutADomainFolder_IsAServiceOverAStore`
  tient la forme. Promotion simple -> riche documentée comme seul pont.

### 4.4 Générateurs de code (`Tools/`)

`NewFeature.cs` (5 891 lignes), `NewUseCase.cs` (1 443), `RemoveFeature.cs` (1 670) : options
`--access owner|shared`, `--deletion delete|anonymise`, `--operations`, `--model record|aggregate`,
`--concurrency none|etag` (`Tools/NewFeature.cs:15-37`). Un `.feature-manifest/` par feature générée
garde les empreintes pour que `remove-feature --apply` ne supprime qu'un squelette intact. Chacun a
un `--self-test`. La règle « une liste fermée à compléter » est listée dans
`docs/guides/adding-a-feature.md:173-197` (registration -> `HostComposition`, config ->
`ConfigurationSurfaceTests`, endpoint anonyme -> `_anonymousActions`, event -> `DomainEventTests`).

---

## 5. Outillage

### 5.1 Options du template (`.template.config/template.json`)

Paramètres libres (`type: parameter`) : `auth` (local | external | none, défaut `local`), `database`
(postgresql défaut | sqlserver | mysql refusé | sqlite dev seulement), `files` (bool, défaut vrai),
`examples` (all | notes | none), `ci`, `sonar`, `agentFiles` (bool), `web` (none | placeholder |
angular | vite-react | vite-vue | vite-svelte | react-router | blazor-wasm), `webAudience` (public |
admin), `webRendering` (spa | ssr), `webName`, `presentation` (multi-choix : api, blazor-server, mcp,
avalonia, wpf, worker). Des paramètres « refus » (`authNoneNeedsExamplesNone`, `mySqlNotYetGenerated`,
etc., `isRequired` conditionnel) transforment une combinaison invalide en erreur de génération ;
`refusedOptions` (l. 84-87) en est l'agrégat. 104 symboles au total, beaucoup calculés
(`localAuth`, `sqlServer`, `anyFeature`, `signedInRoute`...). 5 `postActions` (instructions de
bootstrap). `sources` : 25 entrées (arbre principal avec 46 modifiers d'exclusion, variantes
`.template.config/variants/*`, starters `.template.config/starters/*`).

Mécanique de génération : le code source du dépôt est la forme « tout activé » ; les conditions sont
des commentaires (`//#if (todoLists)` ... `//#endif`, `/*#if ... #endif*/`, `<!--#if-->` dans csproj
et markdown, `#if` dans `.env.example`) que le moteur `dotnet new` interprète. Conséquence : le
dépôt se construit tel quel, mais un fichier source lu seul mélange toutes les variantes (par
exemple `Program.cs:66-72`).

Item templates : `cleanarch-webapp` (`.template.config/webapp`), `cleanarch-webpackage`
(`.template.config/webpackage`), `cleanarch-presentation` (`.template.config/presentation`) ; le
projet généré les rejoue avec `dotnet run Tools/Tasks.cs web-scaffold|web-join|presentation-scaffold|presentation-join`.

Starters : angular (+ssr), react-router (+ssr), vite-react/vue/svelte, blazor-wasm, blazor-server,
avalonia, wpf, mcp, worker. Les frameworks Node viennent de la CLI officielle au version pinnée
(`scaffold.json`, ADR template 0003/0008) ; blazor-server/mcp/worker/wpf/avalonia sont des projets
.NET écrits ou générés (ADR template 0009/0013/0015). Le worker est dérivé de l'API (sources
`Src/Presentation/AppTemplate.Api/` -> `AppTemplate.Worker/`, `template.json`).

### 5.2 Scripts et CLI du projet : `Tools/Tasks.cs`

Application C# « file-based » (`dotnet run Tools/Tasks.cs <tâche>`), 2 134 lignes ; tâches :
restore, build, test (`--no-integration` = projets `IsTestProject` sans Testcontainers,
`Tasks.cs:1976-1986`), coverage, format, format-fix, new-feature, new-use-case, remove-feature,
migration-initial, migration-add, database-update, migration-bundle, openapi, api-client (`--check`),
run, compose-up, compose-down, end-to-end (`--keep-stack`), sonar, bootstrap, presentation-scaffold,
presentation-join, web-scaffold, web-join, hygiene, verify, web-lockfiles (maintenance du template).
`verify` (`Tasks.cs:651-688`) : refuse sans migration initiale, `hygiene` (self-tests + CheckDocPaths
+ CheckWorkflows + CoverageGate self-test + CheckComments), `api-client --check`, `web-lockfiles
--check`, restore, `dotnet format --verify-no-changes`, build, test, `dotnet ef migrations
has-pending-model-changes` pour chaque contexte (`AppDbContext`, `AuthDbContext`), liste des paquets
vulnérables, et la porte du lanceur (build + test de `Launcher/`). Outils locaux
(`.config/dotnet-tools.json`) : `dotnet-ef` 10.0.14, `dotnet-sonarscanner` 12.0.0, Kiota 1.36.1.
ADR template 0017-0019 proposent de remplacer `Tools/Tasks.cs` par un outil .NET `dotnet cleanarch`
(feuille de route, pas livré).

### 5.3 Lanceur `Launcher/CleanArch.Create`

Outil .NET (`PackAsTool`, `cleanarch-create`, lancé par `dnx CleanArch.Create`) utilisant
Spectre.Console 0.57.2 (`Launcher/Directory.Packages.props`). 6 447 lignes de C#. Lit les manifestes
embarqués du template (`CleanArch.Create.csproj:44-57`, `Manifests/TemplateManifest.cs`) pour poser
les questions (une conversation guidée « rail » ou simple, `Prompts/`), n'offre que les combinaisons
que le template génère (`Manifests/Offer.cs`, `Condition.cs`), installe le paquet si besoin,
exécute `dotnet new`, puis un « setup » optionnel : premier commit, bootstrap, commit de bootstrap,
copie de `.env` (`Launcher/CleanArch.Create/README.md`). Sous-commandes `web-app`, `web-package`,
`presentation`. 135 `[Fact]`/`[Theory]` dans `CleanArch.Create.Tests`. Exclu du paquet du template
(`TemplatePackage.csproj:42`).

### 5.4 Docker, Compose, Aspire, front-ends

- `docker-compose.yml` : `db`, `mailpit`, `api`, `worker`, `presentation-presentation-app-portal`,
  `presentation-presentation-app-mcp`, `objectstore` (SeaweedFS), profils `sonar` (`sonarqube`,
  `sonar-scanner`) ; images épinglées par digest, vérifié par `ContainerImagePinningTests`.
  `docker-compose.database.yml` pour la base seule. Dockerfile par hôte (API, worker, portail, MCP,
  chaque web app).
- **Aspire : absent.** Seule occurrence : la feuille de route (`docs/template/roadmap.md`, item
  « Around the apps », issue #122) ; aucun AppHost dans `Src/`.
- Front-ends : espace pnpm `Src/Web` (`apps/`, `packages/api-client`, `eslint-config`, `tsconfig`),
  client TypeScript généré depuis l'OpenAPI (`pnpm api`), `Src/Web/AGENTS.md`, workflow `web.yml`.
  Pour Blazor WASM, le client .NET de `Src/Clients`. OpenAPI committé
  (`Tests/Integration/AppTemplate.Api.IntegrationTests/OpenApi/v1.json`, test `OpenApiDocumentTests`).
- Observabilité : OpenTelemetry (OTLP, ASP.NET, HTTP, Npgsql/SqlClient), journaux JSON console,
  `RequestLoggingMiddleware`, instruments déclarés enregistrés (`ObservabilityRegistrationTests`).
- Sécurité HTTP : en-têtes de sécurité, CORS, limitation de débit, proxy inverse/forwarded headers,
  limites de corps/délais (`Api/Common/Security/*`, `Common/Hosting/*`).

### 5.5 Authentification (mode `local`, par défaut)

7 features dans `Application.Auth` et `Infrastructure.Auth` : comptes (ASP.NET Identity), sessions
(famille de refresh tokens, ADR 0013), JWT d'accès, passkeys (ADR 0015), 2FA, connexions externes,
journal d'événements de sécurité, clés d'accès (ADR 0026), cookie de refresh navigateur (ADR 0016).
Son propre contexte EF (`AuthDbContext`, schéma `identity`) et sa propre migration. Mode `external` :
validation de jetons OIDC, mappage (issuer, subject) -> `UserId` interne en base
(`external_identity.ExternalIdentities`, ADR 0017). Mode `none` : API publique.

---

## 6. Tests

### 6.1 Types, outils, nombre

Outils : **xUnit v3** 4.0.2 (partout, 18 projets), **Shouldly** 4.3.0, **NSubstitute** 6.2.0 (9
projets), **NetArchTest.Rules** 1.3.2 (architecture), **Testcontainers** 4.15/4.16
(`PostgreSql`/`MsSql`/`MySql` + générique ; seuls les 2 projets d'intégration), runner
Microsoft.Testing.Platform, `Microsoft.Testing.Extensions.CodeCoverage`, `Microsoft.AspNetCore.Mvc.Testing`,
`Microsoft.EntityFrameworkCore.Sqlite` (tests du pipeline de sauvegarde en processus). **Respawn : non
utilisé** ; la remise à zéro est un `TRUNCATE TABLE ... RESTART IDENTITY CASCADE` sur les tables des
schémas (`Tests/Integration/.../Infrastructure/DatabaseServer.cs:46-48`, `TestDatabase.cs:48-60`).
Image Postgres épinglée par digest (`DatabaseServer.cs:7-8`). Pas de NUnit, pas de Moq.

Décompte des `[Fact]`/`[Theory]` (déclarations, hors expansions de `InlineData`/`MemberData`,
895 lignes de données) :

| Projet de test | Déclarations |
|---|---|
| Application.UnitTests | 609 |
| Api.IntegrationTests (Docker) | 413 |
| Application.Auth.UnitTests | 395 |
| Domain.UnitTests | 385 |
| Api.UnitTests | 323 |
| Infrastructure.Auth.UnitTests | 237 |
| Architecture.Tests | 195 |
| Infrastructure.Persistence.UnitTests | 185 |
| Application.Core.UnitTests | 139 |
| Infrastructure.Storage.UnitTests | 127 |
| Infrastructure.InMemory.UnitTests | 71 |
| Infrastructure.Email.UnitTests | 52 |
| Presentation.Core.UnitTests | 50 |
| Infrastructure.Core.UnitTests | 40 |
| Domain.Core.UnitTests | 39 |
| Infrastructure.Auth.IntegrationTests (Docker) | 27 |
| Client.UnitTests | 26 |
| EndToEnd.Tests (Compose) | 8 |
| **Total `Tests/`** | **3 321** |
| Launcher `CleanArch.Create.Tests` | 135 |

Plancher de nombre : `.github/workflows/ci.yml:110-111` (étape « Test (with coverage) ») passe
`--minimum-expected-tests 3000` dans le seul dépôt du template (`#if (templateMaintenance)`).

### 6.2 Ce qui tourne sans Docker

`dotnet run Tools/Tasks.cs test --no-integration` : tous les projets `IsTestProject` dont le `.csproj`
ne mentionne pas Testcontainers (`Tasks.cs:1976-1986`), soit tout sauf `Api.IntegrationTests` et
`Infrastructure.Auth.IntegrationTests`, soit environ 2 873 déclarations sur 3 321. `EndToEnd.Tests`
n'est pas `IsTestProject` : il est dans la solution mais `verify`/`coverage` le laissent de côté
(`docs/guides/testing.md:204`) ; `Tools/Tasks.cs end-to-end` le lance contre le Compose (Docker requis).
`Tests/Load/smoke.js` : fumée de charge, job CI non bloquant (`ci.yml`, job `load`).

### 6.3 Couverture

`coverage.minimum` = 89 (pourcent de lignes ; commentaire : « a few points under a Debug measurement
with Docker present »). `coverage.runsettings` exclut assemblies de tests, migrations, `obj/`,
`Generated/`, auto-propriétés et `[ExcludeFromCodeCoverage]`. `Tools/CoverageGate.cs` fusionne les
rapports Cobertura par union ; `CoverageFloorTests` et `CoverageSettingsTests` tiennent le fichier et
la configuration. Aucun rapport de couverture n'est présent dans le clone ; je n'ai rien mesuré : le
chiffre réel n'est pas vérifié ici, seul le plancher est établi (ADR 0020 : « tripwire », jamais
relevé pour récompenser).

### 6.4 Philosophie (ADR 0020 `docs/decisions/0020-one-risk-one-type-of-test.md`)

Un risque = un type de test (unitaire, architecture, intégration, contrat, bout en bout,
recette). « Une population = un test » (il découvre ses membres, refuse un ensemble vide, nomme les
fautifs). Un double portant du comportement rejoue les tests de contrat de l'adaptateur réel
(`FileContentStoreContract`, liés par `<Compile Include=... LinkBase="Storage\Contracts">` dans
`Api.IntegrationTests.csproj`). Un nouveau contrôle doit être vu échouer une fois.
Le contrat OpenAPI est committé et comparé (`OpenApiDocumentTests`). L'autorisation « default
deny » est vérifiée sur tous les endpoints mappés (`DefaultDenyAuthorizationTests`).

---

## 7. Vie du projet vue depuis le dépôt

- Historique : 543 commits, premier le 2025-02-17, dernier le 2026-10-30 (`git log`). Tag unique :
  `v1.0.0` ; `CHANGELOG.md` (1 158 lignes) : l'entrée `1.0.0 (2026-10-22)` ne contient qu'une ligne
  générée par release-please ; la description de 1.0.0 comme un tout est rédigée à la main sous
  « 1.0.0: what the first release contains ». Version courante lue : `1.1.0-dev`.
- Convention de commits : Conventional Commits (`feat`, `fix`, `docs`, `feat(template)!`), issues et
  PR numérotés (#164 ... #175 sur les cinq derniers commits). `.github/pull_request_template.md`
  porte la règle d'attribution (`PullRequestTemplateTests`). Règle d'auteur : ADR 0028, pas de
  `Co-Authored-By` ni « Generated with », trailer `Assisted-by:` à la place (`AGENTS.md`, section
  Done means).
- Release du template : `.github/workflows/template-release.yml` (déclenché sur `develop`) : jobs
  `release-please` (PR de release, `.github/release-please/config.json` : type `simple`,
  mise à jour de `templateVersion` via jsonpath), `pack` (paquet template + lanceur), `attach`
  (GitHub Release), `publish` (nuget.org), `mirror` (GitHub Packages), `promote` (déplace `main` à la
  release). Modèle de branches : `develop` intègre, `main` porte les releases (ADR template 0006).
- Acceptation : `.github/workflows/template-acceptance.yml` (1 047 lignes) génère des projets depuis le
  paquet selon `.template.config/acceptance/matrix.json` : **25 jambes** (« defaults », « auth none,
  smallest project », 5 variantes `sqlite`/`sqlserver`, plusieurs hôtes, web angular/react/blazor,
  jambes Windows pour WPF), sélection par tags quand une PR ne touche qu'une zone (ADR template 0010,
  `AcceptanceSelectionTests`), un job `template-refusals`, un runner Windows (ADR 0014).
- CI du projet généré : `ci.yml` (jobs `build-test`, `repository-hygiene`, `launcher`, `hygiene`,
  `vulnerable-packages`, `docker`, `load` non bloquant, `end-to-end`, `compose`), `codeql.yml`
  (hebdomadaire + PR), `sonarqube.yml`, `web.yml`, `release.yml` (du projet : `verify`, `migration-bundle`,
  `image`). Dependabot : nuget, github-actions, docker, npm (`/Src/Web`), docker-compose.
- Documentation : `docs/` en pages à une question, 28 ADR de projet (`docs/decisions/0001..0028`) et
  20 ADR du template (`docs/template/decisions/`), feuille de route (`docs/template/roadmap.md` :
  Aspire, WinUI/MAUI, MVC/Razor/gRPC, outil `dotnet cleanarch`, d'autres templates Vite). La doc du
  template ne rejoint jamais un projet généré (ADR template 0002).

---

## 8. Constats (avec preuves)

1. **Pas de médiateur ni de behaviours, câblage explicite** : `docs/decisions/0003-use-cases-and-ports-are-explicit.md` ;
   `LayerDependencyTests.cs:46` ; `ApplicationCoreModule.cs:49-87`. Choix assumé et vérifié par tests.
2. **Règles d'architecture exécutables et auto-contrôlées** : 195 tests dont 17 « Sensitive »
   (`grep Sensitive Tests/Architecture` : 17) ; mesure des populations contre le disque
   (`UseCaseConventionTests.cs:30-45`).
3. **Domaine non mappé par EF** avec record/mapper/tracker : `TodoListRepository.cs:17-88`,
   `TodoListMapper.cs`, `TodoListTracker.cs:98-131`. Coût élevé en code (le tracker de TodoLists
   seul fait 181 lignes) ; bénéfice : domaine sans dépendance (`TheDomainLayer_DependsOnNothing`).
4. **Events après commit sans outbox, perte tolérée par conception** :
   `DomainEventDispatchSaveChangesInterceptor.cs:61-81` (try/catch + log), ADR 0006.
5. **Propriété dans la requête, 404 jamais 403** : `TodoListQueries.cs:23-26`,
   `OwnedAggregate.cs:20-23`, ADR 0009.
6. **Gestion des erreurs hybride** : `Result` pour l'attendu, exceptions `DomainException`/
   `ConcurrencyConflictException` pour le reste, mappées par `GlobalExceptionHandler.cs:19-47`.
   Les invariants d'un agrégat ne remontent donc pas par `Result` (400 générique
   `domain.invariantViolated`, sans détail par champ).
7. **Mise en forme et commentaires verrouillés par un outil** : `Tools/CheckComments.cs:12-50` ; le code
   source a très peu de commentaires par construction.
8. **Doc contredite par le code (mineur)** : `docs/architecture/layers-and-projects.md:34` et
   `docs/reference/project-layout.md:201` disent « six authentication features » ;
   `Src/Application/AppTemplate.Application.Auth/Features/` en contient 7 (AccessKeys, Accounts,
   ExternalLogins, Passkeys, SecurityEvents, Sessions, TwoFactor). Selon la règle de vérité, c'est la doc
   qui est en défaut.
9. **Règle « ne jamais faire taire un diagnostic » non littéralement tenue (mineur)** :
   `AGENTS.md` (Changing code) et `docs/guides/build-and-ci.md:70-72` interdisent `#pragma`,
   `<NoWarn>` et sévérité abaissée ; or `Src/Presentation/AppTemplate.Api/Common/Hosting/HttpPipelineModule.cs:67-69`
   contient `#pragma warning disable ASP0000`, `Directory.Build.props:27` porte
   `<NoWarn>CS1591;CS1573</NoWarn>`, et `.editorconfig` met `CA2007`, `CA1848`, `CA1515` à `none`
   globalement et 22 sections par fichier qui mettent une règle `S*`/`CA*`/`AV*` à `none` (25 lignes `severity = none` au total). Le texte réserve les
   sections `.editorconfig` par fichier avec raison ; la raison n'y est pas écrite (les commentaires
   sont filtrés par `CheckComments`). Le code généré par Kiota (`Generated/`) contient des
   `#pragma` hors périmètre.
10. **Aspire absent** : seule mention dans `docs/template/roadmap.md` (issue #122) ; aucun AppHost sous
    `Src/`.
11. **Pas de migration livrée, création au bootstrap** : `TemplatePackage.csproj:49`, `Tasks.cs:603-637`.
    Un projet fraîchement généré n'est pas utilisable avant `bootstrap` (réseau requis) ; le
    lanceur l'enchaîne en option.
12. **Lisibilité d'un fichier source seul** : les marqueurs `//#if`/`/*#if ... #endif*/` rendent
    certains fichiers (par ex. `Program.cs:10-12, 66-72, 96-102`) lisibles seulement en connaissant
    les symboles ; c'est le prix du « dépôt qui compile » et du moteur `dotnet new`.
13. **Largeur du template** : 6 hôtes de présentation, 8 frameworks web, 3 bases, 3 modes d'auth, 25
    jambes d'acceptation (`matrix.json`) ; 25 `sources` dans `template.json`. Gain : prouvé par
    génération réelle en CI ; coût : `TemplatePackagingTests.cs` (1099 lignes), `template.json`
    (2275 lignes), `Tools/NewFeature.cs` (5891 lignes).
14. **Couverture** : plancher 89 (`coverage.minimum`), taux réel non mesuré ici.
15. **Clone modifié** : `.template.config/template.json` diffère de HEAD (`templateVersion`
    `1.0.0` -> `1.1.0-dev`), hors de mon fait.

## 9. Non vérifié

- Aucun build, aucun test, aucune génération `dotnet new` exécutés : le fait que `AppTemplate.sln`
  compile et que les 3 321 déclarations passent n'est pas établi par moi (je cite le CI et la doc comme
  intention, pas comme résultat).
- Le taux de couverture réel (seul le plancher 89 est lu).
- Le nombre exact de tests exécutés (les `Theory` multiplient les cas ; 895 lignes de données).
- Le contenu complet de `Infrastructure.Auth`, `Tools/NewFeature.cs`, `Tools/RemoveFeature.cs`, des
  starters (`.template.config/starters/*`) et des 25 jambes : lus par structure et en-têtes, pas
  ligne à ligne.
- Le comportement runtime de SQL Server et SQLite (variantes `.template.config/variants/database-*`) :
  seul le chemin PostgreSQL (celui de l'arbre par défaut) a été suivi dans le code.
