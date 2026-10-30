# Dossier anonymisé : concepts du code, architecture et frontières

Deux dépôts de solution .NET 10 en couches (Domain, Application, Infrastructure, Web), notés Dépôt A et Dépôt B. Chaque axe décrit A puis B, dans le même ordre, avec des extraits courts. Les chemins sont relatifs à la racine de chaque dépôt ; les noms de projets sont remplacés par ProjA et ProjB. Aucun jugement n'est porté ici.

Mesures générales relevées par commande :

| | Dépôt A | Dépôt B |
|---|---|---|
| Projets `src/` ou `Src/` | 14 | 7 |
| Projets de test | 18 | 6 |
| Méthodes de test (`[Fact]`/`[Theory]` ou `[Test]`) | environ 3 300 | 32 |
| Fichiers de test d'architecture | 43 | 0 |

---

## Axe 1. Concepts du code

### 1.1 Forme d'un use case, CQRS, médiateur

**Dépôt A.** Un dossier par use case, quatre fichiers : commande, interface, validateur, classe. Le contrôleur dépend de l'interface du use case. Aucun médiateur : une règle de test interdit la référence à `MediatR` dans la couche Application. Les lectures passent par un port `*Queries` qui projette des lignes vers des DTO sans charger l'agrégat. Un générateur en ligne de commande écrit ces fichiers.

```csharp
// Src/Application/ProjA.Application/Features/TodoLists/UseCases/Commands/CreateTodoList/ICreateTodoListUseCase.cs:8
public interface ICreateTodoListUseCase : IUseCase<CreateTodoListCommand, Result<Versioned<TodoListDetailDto>>>;
// .../CreateTodoListCommand.cs:3
public sealed record CreateTodoListCommand(string Name);
// .../CreateTodoListUseCase.cs:40-45
var todoList = TodoList.Create(userId.Value, command.Name, dateTimeProvider.UtcNow);
repository.Add(todoList);
await unitOfWork.SaveChangesAsync(cancellationToken);
return TodoListDtoMapping.Detail(todoList);
```
Taille du use case de création : 3 + 14 + 47 + 8 lignes (hors contrôleur).

**Dépôt B.** Une classe de commande et son handler dans un même fichier, un validateur à part quand il existe. Tout est une requête MediatR ; l'endpoint envoie la commande par `ISender`. Dossiers `Commands/` et `Queries/` ; pas de modèle de lecture séparé. Un gabarit d'élément de projet génère un fichier de use case (handler qui lève `NotImplementedException`).

```csharp
// src/Application/TodoItems/Commands/CreateTodoItem/CreateTodoItem.cs:6, 13, 31-35
public record CreateTodoItemCommand : IRequest<int> { ... }
public class CreateTodoItemCommandHandler : IRequestHandler<CreateTodoItemCommand, int>
    _context.TodoItems.Add(entity);
    await _context.SaveChangesAsync(cancellationToken);
    return entity.Id;
// src/Web/Endpoints/TodoItems.cs:23-27
public static async Task<Created<int>> CreateTodoItem(ISender sender, CreateTodoItemCommand command)
{ var id = await sender.Send(command); return TypedResults.Created($"/{nameof(TodoItems)}/{id}", id); }
```
Taille du use case de création : 37 lignes dans un fichier (hors endpoint).

### 1.2 Pipeline transversal et validation

**Dépôt A.** Aucun behaviour de pipeline. La validation FluentValidation est appelée explicitement dans chaque use case et renvoie un `Result`. Le journal de requête est un middleware HTTP qui écrit méthode, chemin, statut, durée et identifiants de trace. Le commit passe par `IUnitOfWork` ; l'audit et la publication d'événements sont des intercepteurs EF.

```csharp
// Src/Application/ProjA.Application.Core/Common/Validation/ValidationExtensions.cs:8-19
var validation = await validator.ValidateAsync(request, cancellationToken);
return validation.IsValid ? Result.Success() : Result.Failure(ValidationError.From(validation));
// Src/Presentation/ProjA.Api/Common/Observability/RequestLoggingMiddleware.cs:19-26
"{Method} {Path} responded {StatusCode} in {ElapsedMilliseconds}ms (traceIdentifier {TraceIdentifier}, trace {TraceId})."
```
Recherche dans les tests d'architecture de `IValidator` ou `EnsureValid` : aucun résultat.

**Dépôt B.** Cinq éléments de pipeline MediatR : pré-processeur de journal, gestion d'exception, autorisation, validation, mesure de durée. La validation exécute tous les validateurs en parallèle et lève une exception ; la mesure de durée journalise au-delà de 500 ms.

```csharp
// src/Application/DependencyInjection.cs:16-23
cfg.AddOpenRequestPreProcessor(typeof(LoggingBehaviour<>));
cfg.AddOpenBehavior(typeof(UnhandledExceptionBehaviour<,>));
cfg.AddOpenBehavior(typeof(AuthorizationBehaviour<,>));
cfg.AddOpenBehavior(typeof(ValidationBehaviour<,>));
cfg.AddOpenBehavior(typeof(PerformanceBehaviour<,>));
// src/Application/Common/Behaviours/ValidationBehaviour.cs:19-29
var validationResults = await Task.WhenAll(_validators.Select(v => v.ValidateAsync(new ValidationContext<TRequest>(request), cancellationToken)));
... if (failures.Count != 0) throw new ValidationException(failures);
```
Validateurs présents : 4 classes `AbstractValidator` pour 7 commandes.

### 1.3 Erreurs

**Dépôt A.** `Result` et `Error` pour les échecs attendus, huit `ErrorType`, un `code` pointé stable. `DomainException` pour un invariant violé. Un helper `DomainGuard.Try` transforme cette exception en `Result` ; une exception non rattrapée est traduite par un gestionnaire global. Les deux chemins portent le même code.

```csharp
// Src/Application/ProjA.Application.Core/Common/Results/ErrorType.cs:3-20
Validation, NotFound, Unauthorized, Forbidden, Conflict, TooManyRequests, PreconditionFailed, PreconditionRequired
// Src/Application/ProjA.Application.Core/Common/Validation/DomainGuard.cs:18-21
catch (DomainException exception) { return Result.Failure(CommonErrors.InvariantViolated(exception.Message)); }   // statut de l'erreur : 409
// Src/Presentation/ProjA.Api/Common/Errors/GlobalExceptionHandler.cs:27-31
DomainException => (StatusCodes.Status400BadRequest, "Invalid request", "domain.invariantViolated", ...)
```
Une table des codes d'erreur est comparée aux `Error` déclarés par un test. Recherche d'un cas `DomainException` dans `GlobalExceptionHandlerTests` : aucun résultat.

**Dépôt B.** Exceptions de bout en bout, traduites en `ProblemDetails` par un `IExceptionHandler`. `Result` n'existe que pour le service d'identité. Pas de champ de code d'erreur dans le corps.

```csharp
// src/Web/Infrastructure/ProblemDetailsExceptionHandler.cs:19-43 (extrait)
ValidationException ve => (400, new ValidationProblemDetails(ve.Errors) { ... }),
NotFoundException ne => (404, new ProblemDetails { Title = "The specified resource was not found." ... }),
UnauthorizedAccessException => (401, ...),
ForbiddenAccessException => (403, ...),
_ => (-1, null)
// src/Application/Common/Models/Result.cs:11-13
public bool Succeeded { get; init; }   public string[] Errors { get; init; }
```
Les mises à jour dont l'id d'URL diffère de l'id du corps répondent `BadRequest()` sans corps (`src/Web/Endpoints/TodoItems.cs:34-35`).

### 1.4 Événements de domaine

**Dépôt A.** L'agrégat lève des `IDomainEvent`. L'intercepteur EF les collecte à `SavingChanges`, les publie à `SavedChanges` (après le commit), les restitue à `SaveChangesFailed`. Un consommateur qui lève est journalisé et sauté. La version synchrone attend le résultat asynchrone avec `GetAwaiter().GetResult()`. Un test relie les événements levés aux consommateurs enregistrés.

```csharp
// Src/Infrastructure/ProjA.Infrastructure.Core/Common/Saving/DomainEvents/DomainEventDispatchSaveChangesInterceptor.cs:39-56
SavedChangesAsync: foreach (var domainEvent in TakePending(eventData.Context)) { try { await dispatcher.DispatchAsync(domainEvent, CancellationToken.None); } catch (Exception exception) { LogDispatchFailure(domainEvent, exception); } }
// :69  dispatcher.DispatchAsync(domainEvent).GetAwaiter().GetResult();   (SavedChanges, version synchrone)
// :80-86  SaveChangesFailed: RestorePending(eventData.Context);
```

**Dépôt B.** `BaseEvent` implémente `INotification` de MediatR, défini dans le projet Domain. L'événement est levé dans le setter d'une propriété. L'intercepteur EF publie à `SavingChanges`, donc avant l'écriture ; la version synchrone attend le résultat asynchrone avec `GetAwaiter().GetResult()`. Un handler existant, qui journalise.

```csharp
// src/Domain/Entities/TodoItem.cs:47-52
set { if (value && !_done) { AddDomainEvent(new TodoItemCompletedEvent(this)); } _done = value; }
// src/Infrastructure/Data/Interceptors/DispatchDomainEventsInterceptor.cs:17-20, 47-48
public override InterceptionResult<int> SavingChanges(...) { DispatchDomainEvents(eventData.Context).GetAwaiter().GetResult(); ... }
foreach (var domainEvent in domainEvents) await _mediator.Publish(domainEvent);
```
Recherche de tests sur les événements de domaine : aucun résultat.

### 1.5 Entités de base

**Dépôt A.** `Entity<TId>` (id à `private init`), `AggregateRoot<TId>`, `AuditableAggregateRoot<TId>`. Identifiants `Guid.CreateVersion7()`. Value objects en `record` à constructeur privé avec fabrique `Create`. Un test refuse les propriétés publiquement modifiables sur les entités.

```csharp
// Src/Domain/ProjA.Domain.Core/Common/Primitives/Entity.cs:12
public TId Id { get; private init; }
// Src/Domain/ProjA.Domain/Features/TodoLists/Model/Entities/TodoList.cs:28-35
var list = new TodoList(Guid.CreateVersion7(), ownerId, TodoListName.Create(name));
list.RaiseDomainEvent(new TodoListCreatedDomainEvent(list.Id, ownerId, list.Name.Value, now));
```

**Dépôt B.** `BaseEntity` (id `int`), `BaseAuditableEntity` (`Created`, `CreatedBy`, `LastModified`, `LastModifiedBy`), classe abstraite `ValueObject`. Propriétés publiques avec setters ; invariants portés par les validateurs de l'Application.

```csharp
// src/Domain/Common/BaseEntity.cs:9, 13-14
public int Id { get; set; }   [NotMapped] public IReadOnlyCollection<BaseEvent> DomainEvents => _domainEvents.AsReadOnly();
// src/Domain/Entities/TodoItem.cs:35-41
public int ListId { get; set; }  public string? Title { get; set; }  public string? Note { get; set; }  public PriorityLevel Priority { get; set; }
```

### 1.6 Mapping

**Dépôt A.** Classes statiques écrites à la main : domaine vers DTO, DTO vers réponse HTTP, agrégat vers ligne EF. La lecture paginée projette directement dans une requête EF.

```csharp
// Src/Application/ProjA.Application/Features/TodoLists/Model/Mapping/TodoListDtoMapping.cs:8-24
internal static class TodoListDtoMapping { public static Versioned<TodoListDetailDto> Detail(TodoList todoList) { ... new TodoListDetailDto(todoList.Id, todoList.Name.Value, ...) } }
```
Aucun paquet de mapping dans `Directory.Packages.props`.

**Dépôt B.** AutoMapper 16 : un `Profile` privé dans le fichier du DTO, `ProjectTo` en lecture, affectation manuelle en écriture. Un test appelle `AssertConfigurationIsValid`.

```csharp
// src/Application/TodoLists/Queries/GetTodos/TodoListDto.cs:20-26
private class Mapping : Profile { public Mapping() { CreateMap<TodoList, TodoListDto>(); } }
// src/Application/TodoLists/Queries/GetTodos/GetTodos.cs:43-47
_context.TodoLists.AsNoTracking().ProjectTo<TodoListDto>(_mapper.ConfigurationProvider).OrderBy(t => t.Title).ToListAsync(cancellationToken)
```

### 1.7 Autorisation

**Dépôt A.** Politique de repli `RequireAuthenticatedUser` : toute route est authentifiée sauf `[AllowAnonymous]`. La liste des actions anonymes est une liste fermée lue par un test. Une politique nommée `Administrator` (rôle) est posée sur cinq contrôleurs.

```csharp
// Src/Presentation/ProjA.Api/Program.cs:111-116
builder.Services.AddAuthorizationBuilder().SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
// Tests/Architecture/.../Rules/HttpSurfaceTests.cs:10-23
private static readonly string[] _anonymousActions = [ "ConfirmEmail", "Login", ... "ResetPassword" ];
```

**Dépôt B.** `RequireAuthorization()` posé sur chaque groupe d'endpoints ; en plus, un attribut `[Authorize]` sur une requête MediatR est lu par réflexion dans un behaviour. Un seul `[Authorize]` applicatif pour neuf requêtes ; un rôle `Administrator` existe dans le seed.

```csharp
// src/Web/Endpoints/TodoItems.cs:13
groupBuilder.RequireAuthorization();
// src/Application/Common/Behaviours/AuthorizationBehaviour.cs:58-66
var authorizeAttributes = request.GetType().GetCustomAttributes<AuthorizeAttribute>();
if (authorizeAttributes.Any()) { if (_user.Id == null) { throw new UnauthorizedAccessException(); } ... throw new ForbiddenAccessException(); }
```
Un test d'autorisation dans le dépôt (`tests/.../GetTodosTests.cs:50-58`).

### 1.8 Propriété des données

**Dépôt A.** Les requêtes de lecture et la forme « service sur store » filtrent par propriétaire dans le SQL. Le chargement d'un agrégat par le dépôt se fait par id seul ; le propriétaire est comparé en mémoire dans un helper, qui renvoie la même erreur « introuvable » pour un agrégat absent ou d'un autre propriétaire.

```csharp
// Src/Infrastructure/ProjA.Infrastructure.Persistence/Features/TodoLists/Access/Repositories/TodoListRepository.cs:24-29
var record = await context.TodoLists.Include(list => list.Items)... .FirstOrDefaultAsync(list => list.Id == id, cancellationToken);
// Src/Application/ProjA.Application.Core/Common/Ownership/OwnedAggregate.cs:20-23
if (aggregate is null || aggregate.OwnerId != ownerId) { return Result.Failure<TAggregate>(notFound); }
// Src/Infrastructure/.../TodoListRepository.cs:52  .Where(list => list.OwnerId == ownerId.Value)    (GetForOwnerAsync)
```
Signature du port : `Task<TodoList?> GetAsync(Guid id, CancellationToken cancellationToken);`. Un test d'intégration compare la réponse pour un id d'autrui à celle d'un id absent.

**Dépôt B.** Aucun champ propriétaire sur les entités. La requête de lecture renvoie toutes les listes.

```csharp
// src/Application/TodoLists/Queries/GetTodos/GetTodos.cs:43-47
Lists = await _context.TodoLists.AsNoTracking().ProjectTo<TodoListDto>(_mapper.ConfigurationProvider).OrderBy(t => t.Title).ToListAsync(cancellationToken)
// src/Application/TodoItems/Commands/UpdateTodoItem/UpdateTodoItem.cs:25-28
var entity = await _context.TodoItems.FindAsync([request.Id], cancellationToken); Guard.Against.NotFound(request.Id, entity);
```
Recherche de `Forbidden` ou `403` dans les tests : aucun résultat.

### 1.9 Horloge

**Dépôt A.** Port `IDateTimeProvider` propre au dépôt, référencé dans 46 fichiers de `Src`. Quatre implémentations de substitution : une dans un projet de production, trois dans des projets de test. Deux appels directs à `UtcNow` hors du port.

```csharp
// Src/Application/ProjA.Application.Core/Common/Ports/IDateTimeProvider.cs:5
DateTimeOffset UtcNow { get; }
// Src/Infrastructure/ProjA.Infrastructure.Storage/Features/Files/Adapters/Transfers/S3FileDirectTransfer.cs:78
return DateTimeOffset.UtcNow.Add(granted);
// Src/Infrastructure/ProjA.Infrastructure.Storage/Features/Files/Adapters/Inventories/FileSystemFileContentInventory.cs:61
if (DateTime.UtcNow - File.GetLastWriteTimeUtc(path) > StaleTemporaryFileAge)
```

**Dépôt B.** Le type `TimeProvider` du framework, enregistré en singleton et injecté dans l'intercepteur d'audit.

```csharp
// src/Infrastructure/DependencyInjection.cs:76
builder.Services.AddSingleton(TimeProvider.System);
// src/Infrastructure/Data/Interceptors/AuditableEntityInterceptor.cs:12
private readonly TimeProvider _dateTime;
```

---

## Axe 2. Architecture et frontières

### 2.1 Projets et références

**Dépôt A.** 14 projets sous `Src/`, quatre couches ; chaque couche a un projet `*.Core` sans notion de feature. Application référence Domain et Application.Core ; le projet Application.Core référence Domain.Core ; les modules d'infrastructure (Persistence, Auth, Email, Storage, InMemory, Core) sont référencés par les seuls hôtes ; les adaptateurs d'un port sont internes à leur module.

```xml
<!-- Src/Application/ProjA.Application/ProjA.Application.csproj:13-14 -->
<ProjectReference Include="..\..\Domain\ProjA.Domain\ProjA.Domain.csproj" />
<ProjectReference Include="..\ProjA.Application.Core\ProjA.Application.Core.csproj" />
```
```csharp
// Tests/Architecture/.../Rules/ModuleDependencyTests.cs:380-397  (règle OnlyAHost_ReferencesInfrastructureModules)
```

**Dépôt B.** Sept projets sous `src/` (AppHost, ServiceDefaults, Application, Domain, Infrastructure, Shared, Web). Domain référence `MediatR.Contracts`. Application référence Domain et EF Core. Web référence Application, Infrastructure et ServiceDefaults, et utilise un type d'Infrastructure dans un endpoint.

```xml
<!-- src/Domain/Domain.csproj:9 -->  <PackageReference Include="MediatR.Contracts" />
<!-- src/Web/Web.csproj:14-16 -->     <ProjectReference Include="..\Application\Application.csproj" /> ... Infrastructure ... ServiceDefaults
```
```csharp
// src/Web/Endpoints/Users.cs:1, 12
using ProjB.Infrastructure.Identity;   groupBuilder.MapIdentityApi<ApplicationUser>();
```

### 2.2 Tests d'architecture

**Dépôt A.** Projet de test dédié : xUnit v3, NetArchTest.Rules 1.3.2, 43 fichiers, 195 méthodes. Familles : dépendances de couches, graphe des références de projet lu dans les `.csproj`, conventions de use case, de port et de domaine, surface HTTP, composition du conteneur. Chaque règle refuse un ensemble de types vide ; 17 méthodes `...IsSensitive...` appliquent une règle à un cas qui doit échouer, réparties sur 16 des 34 fichiers de `Rules/`.

```csharp
// Tests/Architecture/.../Fixtures/RuleAssertions.cs:11-17
types.ShouldNotBeEmpty("... produced no types for NetArchTest to inspect, so every rule written against it would pass for the wrong reason.");
// Tests/Architecture/.../Rules/LayerDependencyTests.cs:206-219
public void TheDomainRule_IsSensitive_AndDetectsARealViolation() { ... Types.InAssembly(ArchitectureAssemblies.Persistence).ShouldNot().HaveDependencyOnAny(_forbiddenInDomain).GetResult().ShouldDetectAViolation(...) }
```

**Dépôt B.** Aucun test d'architecture : recherche de `NetArchTest` et `ArchUnit` dans le dépôt, aucun résultat. Les tests sont NUnit : 6 sur le domaine, 7 sur l'Application (dont un test de configuration AutoMapper), 19 fonctionnels passant par le médiateur ; le projet `Infrastructure.IntegrationTests` ne contient que `GlobalUsings.cs`.

```csharp
// tests/Application.UnitTests/Common/Mappings/MappingTests.cs:30-34
[Test] public void ShouldHaveValidConfiguration() { _configuration!.AssertConfigurationIsValid(); }
```

### 2.3 Analyzers et diagnostics

**Dépôt A.** `AnalysisLevel latest-recommended`, `EnforceCodeStyleInBuild`, `TreatWarningsAsErrors`. `.editorconfig` : namespaces file-scoped en erreur, `IDE0005` en warning (donc erreur), `CS8618` et `CS8602` en erreur ; 25 lignes `severity = none` dont trois globales ; sections par fichier, chacune suivie d'un commentaire de raison. Aucun analyzer tiers hors Sonar. Suppressions relevées par `grep` (hors code généré et migrations) :

```
Directory.Build.props:27:    <NoWarn>$(NoWarn);CS1591;CS1573</NoWarn>
Src/Presentation/ProjA.Api/ProjA.Api.csproj:16:    <NoWarn>$(NoWarn);AV0029;AV0030</NoWarn>
Src/Presentation/ProjA.Api/Common/Hosting/HttpPipelineModule.cs:67:#pragma warning disable ASP0000    (autour de services.BuildServiceProvider())
.editorconfig:57-59  dotnet_diagnostic.CA2007 / CA1848 / CA1515 .severity = none
```

**Dépôt B.** `TreatWarningsAsErrors` (`Directory.Build.props:5`). `.editorconfig` de 382 lignes : 19 règles de nommage en `suggestion`, `file_scoped` en `silent`, aucune ligne `dotnet_diagnostic.*.severity`. Un analyzer : NUnit.Analyzers. Suppressions relevées par `grep` :

```
src/Application/WeatherForecasts/Queries/GetWeatherForecasts/GetWeatherForecastsQuery.cs:12:#pragma warning disable CS1998
Directory.Build.props:8  <WarningsNotAsErrors>NU1608</WarningsNotAsErrors>   (variante PostgreSQL)
```

### 2.4 Accès aux données depuis l'Application

**Dépôt A.** Ports nommés par intention : `Repository` (déclaré dans le domaine), `Queries`, `Store`, `Table`. Aucun `IQueryable` sur un port ; au plus six opérations par port, tenu par un test. Le domaine n'est pas mappé par EF : des lignes, un mapper et un tracker.

```csharp
// Src/Domain/ProjA.Domain/Features/TodoLists/Repositories/ITodoListRepository.cs:9-17
Task<TodoList?> GetAsync(Guid id, CancellationToken cancellationToken);
Task<IReadOnlyList<TodoList>> GetForOwnerAsync(UserId ownerId, CancellationToken cancellationToken);
void Add(TodoList todoList);   void Remove(TodoList todoList);
```

**Dépôt B.** Interface `IApplicationDbContext` déclarée dans Application, qui expose les `DbSet` ; les handlers l'utilisent directement. Pas de dépôt.

```csharp
// src/Application/Common/Interfaces/IApplicationDbContext.cs:5-12
DbSet<TodoList> TodoLists { get; }   DbSet<TodoItem> TodoItems { get; }
Task<int> SaveChangesAsync(CancellationToken cancellationToken);
```

### 2.5 Audit des paquets et avertissements-erreurs

**Dépôt A.** `NuGetAudit` activé en mode `all`, niveau `low`, avec `TreatWarningsAsErrors`. Pas de `WarningsNotAsErrors` pour les avertissements d'audit. Un job de CI exécute `dotnet list package --vulnerable --include-transitive` et échoue sur sa sortie ; versions centralisées dans `Directory.Packages.props`.

```xml
<!-- Directory.Build.props:15, 19-21 -->
<TreatWarningsAsErrors>true</TreatWarningsAsErrors> <NuGetAudit>true</NuGetAudit> <NuGetAuditMode>all</NuGetAuditMode> <NuGetAuditLevel>low</NuGetAuditLevel>
```

**Dépôt B.** `TreatWarningsAsErrors` ; `NuGetAudit` non configuré explicitement (valeurs par défaut du SDK). Gestion centrale des versions avec `CentralPackageTransitivePinningEnabled`, une épingle de sécurité transitive. Pas de job de liste de paquets vulnérables dans les quatre workflows.

```xml
<!-- Directory.Packages.props:4-5, 33-34 -->
<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally> <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>
<PackageVersion Include="System.Security.Cryptography.Pkcs" Version="10.0.14" />
```
