# Phase 5, groupe G1 : concepts du code, architecture et frontières (analyste opus)

Clones lus (lecture seule, ni build ni génération) :
- nous : `repos/ours`, develop `5f22faa5ec13be8bef0a793a63e06b533839c980` (2026-10-30), noté `ours:` ;
- concurrent : `repos/competitor-main`, main `345e7fe7612fdaf0cb5da58826a7c57d42e1fdd8` (2026-10-26), noté `comp:`.
  Le code des deux axes est le même dans la release v10.8.0 : `git diff --ignore-cr-at-eol --ignore-all-space --stat v10.8.0..HEAD -- 'src/Application/*.cs' 'src/Domain/*.cs' 'src/Infrastructure/*.cs' 'src/Web/*.cs' 'src/*/*.csproj'` ne renvoie que `AppHost.csproj` et `Application.csproj` (2 paquets de build retirés). Ce qui est dit de main vaut donc pour ce que les utilisateurs du paquet NuGet reçoivent.

Racine des chemins : `/tmp/claude-1000/-home-pmmarchio-code-Template-DotNet-Clean-Architecture/608f3f2d-3c02-4530-9e46-6f8e7e0e445b/scratchpad/repos/`.
Issues et discussions du concurrent : lues par `gh api` (GET REST, et requêtes GraphQL `query` en lecture seule).

Matière reprise et revérifiée dans le code : cartes p1 (fusionnées), personas p3 (`p3-comp-1.md`, `p3-ours-1.md`), socle p4 (`p4-devil.md`). Les comptes que je cite ont été refaits ; quand mon compte diffère de la carte, je le dis.

---

## Axe 1. Concepts du code

### 1.1 Forme d'un use case et entrée dans l'application

**Concurrent.** Une commande MediatR et son handler dans un fichier, le validateur à part ; le handler parle à EF par `IApplicationDbContext`.
```csharp
public record CreateTodoItemCommand : IRequest<int> { public int ListId { get; init; } public string? Title { get; init; } }
public class CreateTodoItemCommandHandler : IRequestHandler<CreateTodoItemCommand, int>
{ ... var entity = new TodoItem { ListId = request.ListId, Title = request.Title, Done = false };
  _context.TodoItems.Add(entity); await _context.SaveChangesAsync(cancellationToken); return entity.Id; }
```
`comp:src/Application/TodoItems/Commands/CreateTodoItem/CreateTodoItem.cs:6-36`. L'endpoint (Minimal API) envoie la commande par `ISender` : `comp:src/Web/Endpoints/TodoItems.cs:23-28`. Enregistrement par scan de toute l'assembly : `comp:src/Application/DependencyInjection.cs:14-17`. Gabarit `ca-usecase` : un fichier, corps `throw new NotImplementedException()`, ni endpoint ni test (`comp:templates/ca-use-case/FeatureName/Commands/CleanArchitectureUseCase/CleanArchitectureUseCase.cs:33-43`).

**Nous.** Un dossier de 4 fichiers : commande `record`, interface `IUseCase<,>`, validateur, classe scellée à constructeur primaire. Le contrôleur appelle l'interface.
```csharp
public interface ICreateTodoListUseCase : IUseCase<CreateTodoListCommand, Result<Versioned<TodoListDetailDto>>>;
public sealed class CreateTodoListUseCase(ITodoListRepository repository, IUnitOfWork unitOfWork,
    ICurrentUser currentUser, IDateTimeProvider dateTimeProvider, IValidator<CreateTodoListCommand> validator) : ICreateTodoListUseCase
{ ... var userId = currentUser.RequireUserId(); ... var validation = await validator.EnsureValidAsync(command, ct); ...
  var todoList = TodoList.Create(userId.Value, command.Name, dateTimeProvider.UtcNow);
  repository.Add(todoList); await unitOfWork.SaveChangesAsync(ct); return TodoListDtoMapping.Detail(todoList); }
```
`ours:Src/Application/AppTemplate.Application/Features/TodoLists/UseCases/Commands/CreateTodoList/ICreateTodoListUseCase.cs:8`, `.../CreateTodoListUseCase.cs:13-46`. Décision : `ours:docs/decisions/0003-use-cases-and-ports-are-explicit.md` (« There is no MediatR, no dispatcher and no pipeline behaviour »). Tenue par `UseCaseConventionTests` (`UseCases_AreSealed`, `EveryUseCaseFolder_HoldsOneUseCase_AndIsNamedForIt`, `ours:Tests/Architecture/AppTemplate.Architecture.Tests/Rules/UseCaseConventionTests.cs:82,120`). `new-use-case` écrit 5 fichiers + un test et imprime l'action de contrôleur (`p3-ours-1.md`, tâche 2).

**Verdict : choix différent assumé (ADR 0003), avec un coût prouvé. Confiance moyenne.**
Le coût existe : sur la même tâche « ajouter Archive puis une recherche par nom », le persona du concurrent écrit ~110 lignes à la main (`p3-comp-1.md`, tableau §5, ligne 2), le nôtre ~350 (`p3-ours-1.md`, tableau §2, ligne 2) ; pour « ajouter un champ + migration », ~14 lignes dans 7 fichiers contre ~45 dans ~14 fichiers. Ce coût vient surtout de l'ADR 0005 (ligne, mapper, tracker), pas de la forme du use case. Les deux mesures ne sont pas strictement comparables : notre persona a écrit 8 tests unitaires et 2 d'intégration, celui du concurrent un seul.
Ce qu'on gagne en retour, prouvé : graphe d'appel lisible au compilateur (F12), contrat HTTP typé par `Result`, refus de construire un use case sans son interface ; et pour une feature entière, notre générateur fait moins écrire (~215 lignes contre ~380, tâche 1 des deux personas).

### 1.2 Médiateur et pipeline

**Concurrent.** MediatR 14.2.0 avec 1 pré-processeur et 4 behaviours, dans cet ordre :
```csharp
cfg.AddOpenRequestPreProcessor(typeof(LoggingBehaviour<>));
cfg.AddOpenBehavior(typeof(UnhandledExceptionBehaviour<,>));
cfg.AddOpenBehavior(typeof(AuthorizationBehaviour<,>));
cfg.AddOpenBehavior(typeof(ValidationBehaviour<,>));
cfg.AddOpenBehavior(typeof(PerformanceBehaviour<,>));
```
`comp:src/Application/DependencyInjection.cs:16-23`, versions `comp:Directory.Packages.props:9,14`. Le journal écrit la requête entière : `"... {@UserId} {@UserName} {@Request}"` (`comp:src/Application/Common/Behaviours/LoggingBehaviour.cs:32-33`), de même en cas d'exception (`UnhandledExceptionBehaviour.cs:25`).
Licence : MediatR et AutoMapper sont commerciaux au-delà de 5 M$ de chiffre d'affaires ; le mainteneur les garde (https://github.com/jasontaylordev/CleanArchitecture/discussions/1346, 2025-07-16 ; issue https://github.com/jasontaylordev/CleanArchitecture/issues/1325 fermée en renvoyant à cette discussion ; PR https://github.com/jasontaylordev/CleanArchitecture/pull/1348 « use mit mediator » fermée sans fusion). Un commentaire du 2026-03-24 dans la discussion demande encore une alternative pour des clients PME. `grep -rni licensekey src tests` : rien, aucun réglage de clé dans le template. Le persona p3-comp-1 voit un avertissement de licence à chaque exécution (`p3-comp-1.md`, T3).

**Nous.** Aucun médiateur. MediatR est interdit dans la couche application par test :
```csharp
private static readonly string[] _forbiddenInApplication = [ ..., "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", ..., "MailKit", "MediatR" ];
```
`ours:Tests/Architecture/AppTemplate.Architecture.Tests/Rules/LayerDependencyTests.cs:32-49`, appliqué par `TheApplicationLayer_DependsOnlyOnTheDomain` (`:78-95`). Le transversal est ailleurs : middlewares et filtres côté HTTP, intercepteurs EF côté persistance (`ours:docs/architecture/README.md:85`).

**Verdict : choix différent assumé (ADR 0003). Confiance haute.** Ce choix évite aux utilisateurs une dépendance sous licence commerciale, ce que le concurrent laisse à leur charge.

**À refuser** : le pipeline de behaviours (ADR 0003 ; `p4-devil.md` B2 conclut de même). Le `LoggingBehaviour` du concurrent journalise chaque commande en entier, mots de passe compris si une commande en porte : c'est une raison de plus.

**À prendre (forme ADR 0003)** : la seule garantie que le pipeline donne et pas nous, c'est que la validation tourne sans qu'on l'appelle. Chez nous, 56 `AbstractValidator<` et 57 appels `EnsureValidAsync` sous `Src/Application` ; `grep -rn "IValidator\|AbstractValidator" Tests/Architecture` ne renvoie rien. Mon script (pour chaque `AbstractValidator<T>`, chercher `IValidator<T>` sous `Src`) ne trouve aucun validateur orphelin aujourd'hui. Un test d'architecture « chaque `AbstractValidator<T>` est une dépendance de constructeur d'exactement un use case ou service » rendrait la convention vérifiable. `p4-devil.md` (C1) arrive à la même conclusion. Pas d'ADR nouvelle : c'est la règle d'AGENTS.md (« A rule checked by hand becomes a test »).

### 1.3 Validation

**Concurrent.** Elle tourne dans le pipeline, les validateurs en parallèle, et lève une exception :
```csharp
var validationResults = await Task.WhenAll(_validators.Select(v => v.ValidateAsync(new ValidationContext<TRequest>(request), cancellationToken)));
... if (failures.Count != 0) throw new ValidationException(failures);
```
`comp:src/Application/Common/Behaviours/ValidationBehaviour.cs:17-29`. 4 validateurs pour 7 commandes (`find src/Application -name '*Validator*.cs'`). Un validateur interroge la base (`MustAsync(BeUniqueTitle)` → `AnyAsync`, `comp:src/Application/TodoLists/Commands/CreateTodoList/CreateTodoListCommandValidator.cs:16,21-25`) : l'unicité est vérifiée avant l'écriture, sans index unique cité, donc deux requêtes simultanées peuvent passer toutes les deux (lu dans le code, pas exécuté).

**Nous.** Appel explicite, retour `Result` (`request.validationFailed` avec détail par champ), après l'authentification (401 avant 400) : `CreateTodoListUseCase.cs:26-38`. Les invariants sont doublés dans le domaine (`TodoListName.MaxLength` réutilisé par le validateur, `.../CreateTodoListCommandValidator.cs:12-13`).

**Verdict : à égalité sur le fond, choix différent assumé (ADR 0003) sur le mécanisme. Confiance haute.** Ni l'un ni l'autre ne vérifie qu'une commande a son validateur. L'exception de validation du concurrent est une question récurrente chez lui (https://github.com/jasontaylordev/CleanArchitecture/discussions/401, « Performance overhead of throwing exception for validation Errors? », 7 commentaires) ; notre `Result` n'a pas ce sujet.
Une course d'unicité tombe en 500 dans les deux dépôts (`p4-devil.md` A1 : `ours:Src/Presentation/AppTemplate.Api/Common/Errors/GlobalExceptionHandler.cs:42-46`, aucun cas `DbUpdateException` ; le concurrent n'en a pas non plus).

### 1.4 Erreurs

**Concurrent.** Des exceptions de bout en bout ; un `IExceptionHandler` en traduit quatre en ProblemDetails, sans code stable, et laisse passer les autres :
```csharp
ValidationException ve => (400, new ValidationProblemDetails(ve.Errors) {...}),
NotFoundException ne   => (404, ...), UnauthorizedAccessException => (401, ...), ForbiddenAccessException => (403, ...),
_ => (-1, null)
```
`comp:src/Web/Infrastructure/ProblemDetailsExceptionHandler.cs:17-46`. `Result` n'existe que pour Identity (`string[] Errors`, `comp:src/Application/Common/Models/Result.cs:3-24`). Un id d'URL différent de celui du corps renvoie `BadRequest()` sans corps (`comp:src/Web/Endpoints/TodoItems.cs:34-35`).

**Nous.** `Result`/`Error` à 8 `ErrorType` (Validation, NotFound, Unauthorized, Forbidden, Conflict, TooManyRequests, PreconditionFailed, PreconditionRequired ; `ours:Src/Application/AppTemplate.Application.Core/Common/Results/ErrorType.cs:3-20`). Codes `feature.cause` en extension `code`. La table de `docs/reference/api.md` est tenue dans les deux sens par `ErrorCodeTableTests`. Les exceptions restantes sont traduites par `GlobalExceptionHandler` : 409 `concurrency.conflict`, 400 `domain.invariantViolated`, 504/499, 500 `server.unexpected` (`GlobalExceptionHandler.cs:19-47`).

**Verdict : on dépasse. Confiance haute.**

**Défaut chez nous (doc contre code)** : `domain.invariantViolated` répond **409** par `DomainGuard.Try` (`ours:Src/Application/AppTemplate.Application.Core/Common/Validation/DomainGuard.cs:18-20` → `CommonErrors.cs:9-11` = `Error.Conflict`) et **400** si la `DomainException` n'est pas attrapée (`GlobalExceptionHandler.cs:27-31`). `docs/architecture/domain-and-results.md:59-60` documente les deux ; `docs/reference/api.md:247` ne donne que 409 ; `Tests/Presentation/AppTemplate.Api.UnitTests/Common/Errors/GlobalExceptionHandlerTests.cs` ne contient aucune occurrence de `DomainException`. Un même code a donc deux statuts, et le 400 n'est tenu par aucun test. Correction : un statut par code (passer le cas non attrapé en 409, ou lui donner un autre code), plus le test manquant.

### 1.5 Événements de domaine

**Concurrent.** L'entité lève l'événement dans un setter ; l'intercepteur le publie dans `SavingChanges`, donc **avant** l'écriture, et en synchrone bloquant sur le chemin non asynchrone :
```csharp
public override InterceptionResult<int> SavingChanges(...) { DispatchDomainEvents(eventData.Context).GetAwaiter().GetResult(); ... }
```
`comp:src/Domain/Entities/TodoItem.cs:14-26`, `comp:src/Infrastructure/Data/Interceptors/DispatchDomainEventsInterceptor.cs:17-30,47-48`. Son ADR dit le contraire : « dispatch events after `SaveChangesAsync` » (`comp:docs/decisions/ADR-003-MediatR-Contracts-In-Domain.md:13`). Conséquence connue chez lui : avec des ids `int` d'identité, un handler voit `Id == 0` (https://github.com/jasontaylordev/CleanArchitecture/issues/1112, réponse du mainteneur qui confirme le `SavingChanges`). Ancien débat avant/après : https://github.com/jasontaylordev/CleanArchitecture/issues/326 (10 commentaires). Un seul handler, qui journalise. Aucun test d'événement (`p1-map-comp.md` §6, revérifié : `find tests -name '*.cs'` ne montre aucun test d'événement).

**Nous.** Collecte à `SavingChanges`, publication à `SavedChanges` (après commit), restitution à `SaveChangesFailed` :
`ours:Src/Infrastructure/AppTemplate.Infrastructure.Core/Common/Saving/DomainEvents/DomainEventDispatchSaveChangesInterceptor.cs:16-25,39-58,80-96`. Un consommateur qui lève est journalisé et sauté (`.../DomainEventDispatcher.cs:27-43`). Décision : `ours:docs/decisions/0006-events-after-commit-without-an-outbox.md:14-17` (au plus une fois, sans outbox ; la garantie, ce sont les balayages sous bail). `DomainEventTests` relie chaque événement à un consommateur ou à une liste motivée (`.../Rules/DomainEventTests.cs:27,71`).

**Verdict : on dépasse (la doc dit ce que fait le code, des règles tiennent par test), choix différent assumé (ADR 0006) sur le moment de publication. Confiance haute.**
**À refuser** : la publication avant commit, qui fait voir aux handlers un état non écrit et déclenche leurs effets même si le commit échoue.

### 1.6 Entités de base

**Concurrent.** `BaseEntity` avec `int Id { get; set; }` public, `BaseAuditableEntity` aux setters publics ; entités anémiques (`TodoList.Title { get; set; }`) ; `BaseEvent : INotification`, donc MediatR.Contracts dans Domain.
`comp:src/Domain/Common/BaseEntity.cs:5-9`, `comp:src/Domain/Common/BaseAuditableEntity.cs:3-11`, `comp:src/Domain/Entities/TodoList.cs:3-10`, `comp:src/Domain/Common/BaseEvent.cs:5`, `comp:src/Domain/Domain.csproj:9`, ADR-003.

**Nous.** `Entity<TId>`, `AggregateRoot<TId>` (`RaiseDomainEvent` protégé), `AuditableAggregateRoot<TId>`, `UserId` scellé ; ids `Guid.CreateVersion7()` (`ours:Src/Domain/AppTemplate.Domain.Core/Common/Primitives/AggregateRoot.cs:5-16`). Domaine non mappé par EF (ADR 0005). Tenu par `DomainModelTests` : `AggregateRoots_AreSealed`, `Entities_HaveNoPubliclySettableState`, `Entities_HaveNoPublicFields`, `ValueObjectsAndDomainEvents_AreImmutable`, `DomainEvents_AreSealedAndImplementTheMarker` (`.../Rules/DomainModelTests.cs:25,47,82,110,147`).

**Verdict : on dépasse. Confiance haute.** Le concurrent assume lui-même ne pas faire de DDD (`comp:docs/decisions/ADR-001-Use-EFCore-In-Application-Layer.md`, section « When this decision does not apply »).

### 1.7 Mapping

**Concurrent.** AutoMapper 16.2.0 : profil imbriqué dans le DTO, `ProjectTo` en lecture, configuration vérifiée par test.
`comp:src/Application/TodoLists/Queries/GetTodos/TodoListDto.cs:20-26`, `GetTodos.cs:43-47`, `comp:tests/Application.UnitTests/Common/Mappings/MappingTests.cs:29-33` (`AssertConfigurationIsValid`). Issues sur le sujet : https://github.com/jasontaylordev/CleanArchitecture/issues/1206 (Mapperly), PR https://github.com/jasontaylordev/CleanArchitecture/pull/1321 (mapping manuel), toutes deux refusées en renvoyant à la discussion 1346 ; https://github.com/jasontaylordev/CleanArchitecture/issues/1565 (AutoMapper 16.1.0 signalé vulnérable sous `TreatWarningsAsErrors`).

**Nous.** Mapping écrit à la main, en méthodes statiques internes : `...DtoMapping` (domaine vers DTO), `...ResponseMapping` (DTO vers réponse), `...Mapper` (agrégat vers ligne) ; `ours:Src/Application/AppTemplate.Application/Features/TodoLists/Model/Mapping/TodoListDtoMapping.cs:7-21`. Absence voulue et écrite : `ours:docs/architecture/README.md:89`.

**Verdict : choix différent assumé (`docs/architecture/README.md`, « What is deliberately absent »). Confiance haute.** Aucun test d'architecture n'interdit AutoMapper (`grep AutoMapper Tests/Architecture` vide), alors qu'un test interdit MediatR. L'écart est mineur : il suffirait d'ajouter `"AutoMapper"` à `_forbiddenInApplication`.

### 1.8 Autorisation

**Concurrent.** `RequireAuthorization()` par groupe d'endpoints (`comp:src/Web/Endpoints/TodoItems.cs:13`), plus un `[Authorize(Roles/Policy)]` applicatif posé sur la requête et lu par `AuthorizationBehaviour` (`comp:src/Application/Common/Behaviours/AuthorizationBehaviour.cs:24-73`). Une seule requête sur 9 le porte (`comp:src/Application/TodoLists/Queries/GetTodos/GetTodos.cs:9`). Pas de politique par défaut. CORS ouvert à toute origine (`comp:src/Web/Program.cs:28-31`). Un seul test d'autorisation (`ShouldDenyAnonymousUser`).

**Nous.** Default-deny : politique de repli `RequireAuthenticatedUser` (`ours:Src/Presentation/AppTemplate.Api/Program.cs:113-114`) ; `[AllowAnonymous]` en liste fermée tenue par `HttpSurfaceTests` ; politique `Administrator` sur 5 contrôleurs, tenue par `AdministratorPolicyTests` (403 avec code pour un utilisateur ordinaire, 401 pour un anonyme : `ours:Tests/Integration/AppTemplate.Api.IntegrationTests/Security/AdministratorPolicyTests.cs:13,28`). Dans l'application, chaque use case exige l'appelant (`currentUser.RequireUserId()`, 401 `auth.required`), quel que soit l'hôte.

**Verdict : on dépasse. Confiance haute.**
**Différence à expliciter** : chez le concurrent, le contrôle de rôle peut vivre dans la requête applicative, donc vaut pour tout hôte. Chez nous, les rôles sont portés par l'hôte : attributs de contrôleur, ou `Authorize(Policy=…)` sur les outils MCP (`grep -rln 'Authorize(Policy' .template.config/starters` : 4 fichiers d'outils MCP). L'ADR 0022 dit que chaque hôte compose « its authentication », mais aucune ADR ne dit que l'autorisation par rôle est l'affaire de l'hôte, et aucun test ne vérifie qu'un use case d'administration n'est atteint que derrière la politique. **Confiance moyenne** : je n'ai pas vérifié qu'un hôte en processus (Blazor Server, MCP) appelle aujourd'hui un use case d'administration. Forme proposée : une phrase dans l'ADR 0022, ou une ADR courte « l'autorisation par rôle est une décision de l'hôte », plus un test qui liste les use cases d'administration et les hôtes qui les appellent.

### 1.9 Propriété des données

**Concurrent.** Aucune : pas d'`OwnerId`, n'importe quel utilisateur authentifié lit et modifie tout. `DeleteTodoList` charge par id seul (`comp:src/Application/TodoLists/Commands/DeleteTodoList/DeleteTodoList.cs:18-22`). Le persona a dû l'écrire entièrement (`p3-comp-1.md`, T1 : « rien d'exemplaire dans le template »).

**Nous.** ADR 0009 : « The owner is in every predicate. Each query and each write ... filters by the caller's id in the same statement » (`ours:docs/decisions/0009-ownership-is-in-the-query.md:14-15`) ; 404 jamais 403. Les lectures et la forme simple le font : `TodoListQueries.cs:25,40` (`list.OwnerId == ownerId.Value`), `NoteStore.cs:76,91`. Tenu côté réponse par `OwnershipIsolationTests` (lecture, renommage, suppression, ajout d'élément : `ours:Tests/Integration/AppTemplate.Api.IntegrationTests/Security/OwnershipIsolationTests.cs:15-81`).

**Verdict : on dépasse. Confiance haute.**

**Défaut chez nous (ADR contre code), confiance haute.** Le chemin agrégat fait exactement ce que le Contexte de l'ADR 0009 refuse (« load the record by its id and then compare its owner with the caller », `0009:7-9`) :
```csharp
// ours:Src/Infrastructure/AppTemplate.Infrastructure.Persistence/Features/TodoLists/Access/Repositories/TodoListRepository.cs:17-29
.FirstOrDefaultAsync(list => list.Id == id, cancellationToken);
// ours:Src/Application/AppTemplate.Application.Core/Common/Ownership/OwnedAggregate.cs:20
if (aggregate is null || aggregate.OwnerId != ownerId) { return Result.Failure<TAggregate>(notFound); }
```
Les trois ports d'agrégat ne reçoivent pas le propriétaire (`ITodoListRepository.cs:8`, `IReminderRepository.cs:8`, `IStoredFileRepository.cs:8` : `GetAsync(Guid id, ...)`). Le générateur recopie ce schéma dans chaque nouvelle feature `--access owner --model aggregate` (`ours:Tools/NewFeature.cs:4536-4545` pour le dépôt, `:3648-3652` pour `OwnedAggregate.Require`). La réponse reste juste (même 404, tests verts) ; c'est la promesse « no code path holds another user's row » (`0009:28`) qui est fausse : la ligne d'autrui est chargée, puis gardée dans le tracker (`tracker.Find(id)`, `TodoListRepository.cs:19`).
Correction : `GetOwnedAsync(Guid id, UserId ownerId, ...)` avec le propriétaire dans le prédicat, dans les trois dépôts et dans le gabarit de `NewFeature.cs` ; `OwnedAggregate.Require` reste comme garde. À défaut, réécrire l'ADR. Le test qui tiendrait la règle : un test d'architecture qui exige un paramètre `UserId` sur le `Get*Async` de tout `Repository` d'un `IOwnedAggregate`.

### 1.10 CQRS et accès aux données

**Concurrent.** CQRS par dossiers ; lectures et écritures passent par `IApplicationDbContext`, qui expose les `DbSet` (`comp:src/Application/Common/Interfaces/IApplicationDbContext.cs:5-12`) ; pas de repository, par choix écrit (ADR-001). Lecture par `ProjectTo`, sans pagination ni filtre.

**Nous.** Dossiers `Commands/` et `Queries/`. Les écritures passent par l'agrégat, le `Repository` et `IUnitOfWork`. Les lectures passent par un port `*Queries` qui projette sans charger l'agrégat (`TodoListQueries.cs:16-35`, pagination offset et keyset). Quatre mots de port, pas d'`IQueryable` (ADR 0003), tenu par `StorageVocabularyTests`, `StorageShapeTests.NoApplicationPort_ExposesAQueryable` (`.../Rules/StorageShapeTests.cs:49`) et `PortConventionTests` (plafond de 6 opérations, `:32`).

**Verdict : choix différent assumé (ADR 0003, 0005). Confiance haute.** Le choix du concurrent est contesté chez lui : « This is not exactly The Clean Architecture style » (https://github.com/jasontaylordev/CleanArchitecture/discussions/482, 27 commentaires, sujet : l'Application référence EF Core) et « Repository in Asp Core Clean Architecture » (https://github.com/jasontaylordev/CleanArchitecture/discussions/405, 16 commentaires). Notre séparation répond aux deux questions et un test la tient.

### 1.11 Enregistrement des dépendances

**Concurrent.** Scan de toute l'assembly, pour MediatR et pour FluentValidation (`comp:src/Application/DependencyInjection.cs:11-17`). Aucun test de composition du conteneur.

**Nous.** Hybride : chaque feature est appelée explicitement (`AddTodoLists` → `AddFeature`), puis ses use cases et validateurs sont trouvés par scan, sur le préfixe de namespace de la feature (`ours:Src/Application/AppTemplate.Application/ApplicationModule.cs:156-170`). Le conteneur est vérifié par `ContainerCompositionTests`.

**Verdict : on dépasse (composition testée). Confiance haute.**
**Défaut chez nous (message de test contre code)** : les messages de `UseCaseConventionTests` disent « registration is explicit rather than scanned » (`.../Rules/UseCaseConventionTests.cs:58,197`), alors que le code scanne et que `docs/guides/adding-a-feature.md:153` dit « Nothing is registered by hand ». À réécrire : « registered by its feature's folder ».

### Synthèse de l'axe 1

| Concept | Verdict | Confiance |
|---|---|---|
| Forme d'un use case | choix assumé (ADR 0003), coût d'écriture prouvé (p3 T2/T3) | moyenne |
| Médiateur, pipeline | choix assumé (ADR 0003) ; on évite la licence commerciale | haute |
| Validation | à égalité (mécanisme : ADR 0003) | haute |
| Erreurs | on dépasse ; défaut 409/400 chez nous | haute |
| Événements | on dépasse ; moment : ADR 0006 | haute |
| Entités | on dépasse | haute |
| Mapping | choix assumé (architecture/README) | haute |
| Autorisation | on dépasse ; place des rôles à expliciter | haute / moyenne |
| Propriété | on dépasse le concurrent ; défaut ADR 0009 contre code | haute |
| CQRS, accès aux données | choix assumé (ADR 0003, 0005) | haute |
| Enregistrement DI | on dépasse ; message de test faux | haute |

---

## Axe 2. Architecture et frontières

### 2.1 Projets et références

**Concurrent.** 7 projets `src/` : Domain, Application, Infrastructure, Web, Shared, ServiceDefaults, AppHost.
- Domain : paquet `MediatR.Contracts` (`comp:src/Domain/Domain.csproj:9`, ADR-003).
- Application : Domain, plus `Ardalis.GuardClauses`, `AutoMapper`, `MediatR`, `Microsoft.EntityFrameworkCore`, `Microsoft.Extensions.Hosting` (`comp:src/Application/Application.csproj:9-18`, ADR-001).
- Infrastructure : Application, Shared (`comp:src/Infrastructure/Infrastructure.csproj:27-28`).
- Web : Application, Infrastructure, ServiceDefaults (`comp:src/Web/Web.csproj:14-16`). Un endpoint utilise directement un type d'Infrastructure : `using CleanArchitecture.Infrastructure.Identity;` puis `MapIdentityApi<ApplicationUser>()` (`comp:src/Web/Endpoints/Users.cs:1,12`).

**Nous.** 14 projets `Src/` (`find Src -name '*.csproj' | wc -l`). Chaque couche a un `.Core` qui ne connaît aucune feature ; l'authentification est à part (`Application.Auth`, `Infrastructure.Auth`) ; l'infrastructure a un module par capacité (Persistence, Email, Storage, Auth, InMemory). L'API référence l'application et cinq modules d'infrastructure, pas InMemory (`ours:Src/Presentation/AppTemplate.Api/AppTemplate.Api.csproj:46-60`). Les adaptateurs qui implémentent un port sont `internal` à leur module (`AdapterVisibilityTests.Adapters_ImplementingAnApplicationPort_AreInternalToTheirModule`, `.../Rules/AdapterVisibilityTests.cs:14`) : un contrôleur ne peut pas les nommer.

**Verdict : choix différent, sans ADR qui compare au modèle à 4 projets. Confiance haute sur les faits.** Le découpage de l'authentification répond à une question ouverte chez le concurrent : « How to decouple Identity from Domain Layer? » (https://github.com/jasontaylordev/CleanArchitecture/discussions/415). Chez nous, `TheApplicationLayer_KnowsNothingOfAuthentication` le tient (`ours:.../Rules/LayerDependencyTests.cs:177-203`).
Coût possible des 14 projets : plus de projets à comprendre. Aucun persona ne l'a relevé comme friction ; je n'ai donc pas de preuve de ce coût.

### 2.2 Règles qui tiennent les frontières, tests d'architecture

**Concurrent.** Aucun test d'architecture : ni NetArchTest ni ArchUnit dans `comp:Directory.Packages.props` ni sous `tests/`. Seul le graphe de `ProjectReference` tient les couches. Mesure du persona : ajouter `Microsoft.EntityFrameworkCore` à Domain donne « 0 erreur, 0 signal » ; seul un cycle de projets est arrêté, au restore, avec `MSB4006 There is a circular dependency`, sans nom de règle (`p3-comp-1.md`, T8). Les 3 ADR ne citent aucun test. Une PR ouverte ajoute un `AGENTS.md` qui décrit les frontières en prose (https://github.com/jasontaylordev/CleanArchitecture/pull/1633, ouverte le 2026-07-12) : rien d'exécutable.

**Nous.** `Tests/Architecture/AppTemplate.Architecture.Tests` : xUnit v3, NetArchTest.Rules 1.3.2, 195 `[Fact]/[Theory]` (`grep -rc` refait). Règles de couche, de module, de port, de forme, de surface HTTP, de composition ; par exemple `ModuleDependencyTests.TheDomainLayer_ReferencesNothingOutsideItself`, `TheApplicationLayer_ReferencesOnlyTheDomainAndItself`, `OnlyAHost_ReferencesInfrastructureModules`, `NoHost_ReferencesAnotherHost` (`.../Rules/ModuleDependencyTests.cs:263,293,380,403`) ; `FeatureIsolationTests.AFeatureOfTheApplicationProject_ReachesAnotherOnlyThroughItsPortsAndEvents` (`:20`). Contre-épreuves : 17 méthodes `public ...Sensitive...` dans 14 fichiers (`grep -rn 'public .*Sensitive'` ; la carte p1 dit 17 dans 16 fichiers, mon compte de fichiers diffère). Mesure du persona : la même violation (Domain → `Infrastructure.Core`) compile, puis `ModuleDependencyTests.TheDomainLayer_ReferencesNothingOutsideItself` tombe en 9 s, avec le csproj fautif et la raison (`p3-ours-1.md`, tâche 8). Les ADR nomment le test qui les tient (ex. ADR 0003, Consequences ; ADR 0022).

**Verdict : on dépasse. Confiance haute.**
Limite : 7 fichiers de règles n'ont pas de contre-épreuve, dont `UseCaseConventionTests`, `DomainModelTests`, `HttpSurfaceTests` et `ErrorCodeTableTests` (`p1-map-ours.md` D5 ; recoupé par mon grep, qui ne trouve aucune méthode `Sensitive` dans ces fichiers).

### 2.3 Analyzers et sévérités

**Concurrent.** `TreatWarningsAsErrors` seul (`comp:Directory.Build.props:5`), avec l'exception `WarningsNotAsErrors NU1608` en PostgreSQL (`:8`). `.editorconfig` : 19 règles de nommage, toutes en `suggestion` (`comp:.editorconfig:196-268`), aucun `dotnet_diagnostic.*.severity`. Seul analyzer tiers : `NUnit.Analyzers`.

**Nous.** `AnalysisLevel latest-recommended`, `EnforceCodeStyleInBuild`, `TreatWarningsAsErrors` (`ours:Directory.Build.props:13-15`) ; `.editorconfig` met en erreur les namespaces file-scoped, CS8618/CS8602 et le nommage `I*`/`T*`. Le persona a vu CA1862, IDE0005, xUnit1051 et IDE1006 arrêter le build à la première ligne fautive (`p3-ours-1.md` §5).

**Verdict : on dépasse. Confiance haute.**

**Défaut chez nous (règle contre code), confiance haute.** La règle « Do not silence a diagnostic with `#pragma`, `<NoWarn>` or a lowered severity » (`ours:docs/guides/build-and-ci.md:70-72`, `AGENTS.md`, en-tête `ours:.editorconfig:91` « never a repository-wide NoWarn ») n'est pas tenue :
- `#pragma warning disable ASP0000` : `ours:Src/Presentation/AppTemplate.Api/Common/Hosting/HttpPipelineModule.cs:67-69` ;
- `<NoWarn>$(NoWarn);CS1591;CS1573</NoWarn>` pour tout le dépôt : `ours:Directory.Build.props:27` ;
- `<NoWarn>$(NoWarn);AV0029;AV0030</NoWarn>` : `ours:Src/Presentation/AppTemplate.Api/AppTemplate.Api.csproj:16` ;
- `CA2007`, `CA1848`, `CA1515` en `none` pour tout le dépôt : `ours:.editorconfig:57-59`.

Aucun test ne l'interdit. Correction : un test (ou un ajout à `CheckComments`/`Tasks.cs hygiene`) qui refuse `#pragma warning disable` et `<NoWarn>` hors d'une liste d'exceptions motivées (sur le modèle de `_deliberatelyUnconsumed`), et la règle de la doc réécrite pour dire que cette liste existe. `p4-devil.md` (C2, ligne 589) arrive à la même conclusion.

### 2.4 Doc d'architecture contre code (chez nous)

- `docs/architecture/layers-and-projects.md:34` et `docs/reference/project-layout.md:201` : « six authentication features » ; `ls Src/Application/AppTemplate.Application.Auth/Features | wc -l` = 7. Confiance haute.
- Côté concurrent, pour comparer : son ADR-003 contredit son intercepteur (§1.5), et son ADR-001 se dit satisfait par l'inversion de dépendance alors que `Web` utilise un type d'Infrastructure (§2.1). Nous avons des écarts, lui aussi ; les nôtres sont décelables parce que nos ADR nomment du code et des tests.

### Synthèse de l'axe 2

| Point | Verdict | Confiance |
|---|---|---|
| Projets, références | choix différent (découpage plus fin, auth isolée) ; coût non prouvé | haute |
| Tests d'architecture | on dépasse (195 tests, contre-épreuves ; concurrent : aucun) | haute |
| Analyzers, sévérités | on dépasse ; nos suppressions contredisent notre règle | haute |
| Doc contre code | défauts mineurs des deux côtés | haute |

---

## Ce qu'on gagnerait à prendre au concurrent

1. **La garantie « la validation tourne toujours », sans pipeline** : un test d'architecture qui exige que chaque `AbstractValidator<T>` soit injecté dans exactement un use case ou service. Pas d'ADR : la règle découle de l'ADR 0003 et d'AGENTS.md.
2. **Écrire où vit l'autorisation par rôle.** Le concurrent la rend possible dans la requête applicative ; chez nous elle est dans l'hôte, sans ADR. Forme : une phrase dans l'ADR 0022 (ou une ADR courte) et un test qui dit quels hôtes atteignent les use cases d'administration.
3. **Rien d'autre sur ces deux axes.** Son `AssertConfigurationIsValid` répond à un risque qu'AutoMapper crée et que notre mapping manuel n'a pas. Son use case en un fichier se heurte à `LayoutConventionTests` (un type public par fichier).

## Ce qu'il faut refuser, et pourquoi

| Élément du concurrent | Raison |
|---|---|
| MediatR, behaviours | ADR 0003 ; licence commerciale au-delà de 5 M$ (discussion 1346) ; `{@Request}` journalisé en entier (`LoggingBehaviour.cs:32-33`) |
| AutoMapper | absence voulue (`architecture/README.md:89`) ; même licence ; erreurs de configuration à l'exécution |
| `IApplicationDbContext` qui expose les `DbSet` | ADR 0003 (pas d'`IQueryable` sur un port), ADR 0005 ; débat ouvert chez lui (discussions 482, 405) |
| Exceptions comme flux métier, sans code stable | notre contrat `code` + `ErrorCodeTableTests` ; ADR 0008/0010 pour le contrat HTTP |
| Publication des événements avant commit | ADR 0006 ; issue 1112 (id à 0) |
| Entités à setters publics, `int Id` | `DomainModelTests` ; ADR 0005 |
| MediatR.Contracts dans le domaine | `LayerDependencyTests` (le domaine ne dépend de rien) |

## Ce qu'on peut faire et qu'il ne fait pas

- Proposer un template sans dépendance sous licence commerciale, et le dire : c'est la question posée chez lui dans https://github.com/jasontaylordev/CleanArchitecture/issues/1325 et dans la discussion 1346 (commentaire du 2026-03-24), restée sans alternative. Notre README pourrait le dire en une ligne (non vérifié qu'il le dise déjà).
- Répondre par des tests aux débats récurrents chez lui : couche Application sans EF (https://github.com/jasontaylordev/CleanArchitecture/discussions/482), repository ou non (https://github.com/jasontaylordev/CleanArchitecture/discussions/405), Identity découplée (https://github.com/jasontaylordev/CleanArchitecture/discussions/415), validation sans exception (https://github.com/jasontaylordev/CleanArchitecture/discussions/401), moment des événements (https://github.com/jasontaylordev/CleanArchitecture/issues/326). Chacun a chez nous une ADR et un test ; une page « questions fréquentes de Clean Architecture » qui pointe vers ces ADR et tests serait un atout de comparaison (proposition, pas un retard).
- Des règles d'architecture exécutables là où le concurrent en est à une PR de prose (https://github.com/jasontaylordev/CleanArchitecture/pull/1633).
- La propriété des données par défaut, absente chez lui, à condition de corriger l'écart avec l'ADR 0009 (§1.9).

## Défauts chez nous trouvés sur ces axes (à corriger dans le même changement que la doc)

| # | Défaut | Preuve | Test qui le tiendrait |
|---|---|---|---|
| D1 | Le chemin agrégat charge par id puis compare le propriétaire en mémoire, contre l'ADR 0009 ; le générateur le recopie | `TodoListRepository.cs:17-29`, `OwnedAggregate.cs:20`, `0009:7-9,14-15,28`, `Tools/NewFeature.cs:4536-4545,3648-3652` | règle d'architecture sur la signature `Get*Async` des dépôts d'`IOwnedAggregate` |
| D2 | `domain.invariantViolated` répond 409 ou 400 ; `api.md:247` ne donne que 409 ; 400 non testé | `DomainGuard.cs:18-20`, `CommonErrors.cs:9-11`, `GlobalExceptionHandler.cs:27-31`, `domain-and-results.md:59-60` | cas `DomainException` dans `GlobalExceptionHandlerTests` |
| D3 | `#pragma` et `<NoWarn>` malgré la règle | `HttpPipelineModule.cs:67-69`, `Directory.Build.props:27`, `AppTemplate.Api.csproj:16`, `.editorconfig:57-59` contre `build-and-ci.md:70-72` | test de suppressions avec liste motivée |
| D4 | Rien ne vérifie qu'un validateur est consommé | 56 validateurs, 0 occurrence dans `Tests/Architecture` | test validateur → use case |
| D5 | Message de test « explicit rather than scanned » contre un scan | `UseCaseConventionTests.cs:58,197`, `ApplicationModule.cs:156-170` | (message à réécrire) |
| D6 | « six authentication features » pour 7 | `layers-and-projects.md:34`, `project-layout.md:201` | `DocumentationCitationTests` ne compte pas ; correction de texte |
| D7 | AutoMapper absent par choix, mais pas interdit par test (MediatR l'est) | `LayerDependencyTests.cs:32-49` | ajouter `"AutoMapper"` à `_forbiddenInApplication` |

## Non vérifié

- Aucun build ni test lancé (consigne) : les verdicts « tenu par test » reposent sur la lecture des tests, pas sur leur exécution. Les mesures d'exécution citées viennent des personas p3.
- Le comportement exact d'une course d'unicité chez le concurrent (validateur `MustAsync` puis insertion) : lu, pas exécuté.
- Qu'un hôte en processus (Blazor Server, MCP) appelle aujourd'hui un use case d'administration sans politique : non vérifié (§1.8).
- Que notre README dise déjà « sans dépendance sous licence commerciale ».
- La licence effective de MediatR 14 et d'AutoMapper 16 : connue seulement par la discussion 1346 du mainteneur, pas par les paquets.
- Les comparaisons de lignes écrites (p3 T2/T3) portent sur des tâches proches, pas identiques.
