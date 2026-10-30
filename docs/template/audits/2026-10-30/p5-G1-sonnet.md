# Phase 5, groupe G1 (analyste Sonnet) : concepts du code, architecture et frontières

Périmètre : axe 1 (concepts du code : CQRS, médiateur, pipeline, validation, erreurs, événements, entités de base, mapping, autorisation, propriété des données, forme d'un use case) et axe 2 (architecture et frontières : projets, références, règles qui les tiennent, tests d'architecture, analyzers).

Dépôts lus (lecture seule, rien exécuté, rien généré) :
- Nous : `<scratchpad>/repos/ours`, develop `5f22faa5`. Chemins `nous:` relatifs à ce clone.
- Concurrent : `<scratchpad>/repos/competitor-main`, main `345e7fe` (v10.8.0 + 58 commits). Chemins `conc:` relatifs à ce clone. `repos/competitor-release` (v10.8.0) : les fichiers C# cités n'y diffèrent pas, seul l'AppHost a changé (carte p1-map-comp, section 8).
- Issues et discussions du concurrent : `gh api` en GET seulement. La recherche GitHub a atteint sa limite de débit en cours de route ; les issues citées ont été relues une à une par leur numéro.

Étalon : `out/00-cadrage.md`. Rappel de la règle de jugement : une différence n'est un retard que si elle va contre ce que notre template veut faire ; une différence couverte par une ADR reste un « choix assumé » tant qu'aucune preuve ne montre un coût pour les utilisateurs.

## 0. Synthèse

1. Sur les concepts, le concurrent n'a rien qu'on doive lui prendre de structurant. Son pipeline de behaviours MediatR, son AutoMapper et son `IApplicationDbContext` sont des choix que notre cadrage refuse, et les issues du concurrent apportent des preuves de coût pour ses utilisateurs : licence de MediatR et d'AutoMapper (discussions 1346 et 1413, issues 1206 et 1325), exceptions de validation journalisées comme des erreurs (issue 324), ordre des behaviours (issue 700), plaintes récurrentes sur EF Core dans la couche Application (issues 464, 1100, 1607).
2. Le concurrent n'a ni propriété des données, ni test d'architecture, ni règle d'analyse qui tienne ses couches. Sur ces deux axes on dépasse largement, avec preuves de code et de tests.
3. Notre propre dépôt a cinq défauts de cohérence, prouvés dans le code, qui pèsent plus que toute différence avec le concurrent :
   - ADR 0009 dit « aucun chemin de code ne tient la ligne d'un autre utilisateur » ; le chemin agrégat charge par id puis compare en mémoire, et le générateur écrit la même forme (F1) ;
   - le code `domain.invariantViolated` répond 409 ou 400 selon le chemin, contre la phrase « une situation répond toujours le même statut et le même code » (F2) ;
   - la règle « ne jamais faire taire un diagnostic » est violée en cinq endroits, sans test qui la tienne (F3) ;
   - « l'horloge unique » `IDateTimeProvider` est contournée à deux endroits de production et double un type du framework que le concurrent utilise (F4) ;
   - un message de test et une page de guide disent « pas de scan, tout à la main » alors que les use cases sont trouvés par scan (F5).
4. Une pièce du concurrent vaut une décision chez nous : son issue ouverte 1675 (10.8.0 ne compile plus à cause de NU1902/NU1903) montre le coût de `TreatWarningsAsErrors` avec `NuGetAudit` sur un paquet de template figé. Nous avons exactement cette configuration (`Directory.Build.props:15,19-21`) et une doc qui interdit de relâcher l'audit (F6).

Ce que le rapport ne prouve pas : aucun build, aucun test, aucune génération. « Les tests passent » n'est affirmé nulle part ; les verdicts reposent sur la lecture et sur des `grep` rejoués dans les deux clones.

---

## Axe 1. Concepts du code

### 1.1 Forme d'un use case, CQRS, médiateur

**Comment fait le concurrent.** Tout est une requête MediatR ; le contrôleur minimal envoie la commande par `ISender`. La commande, son handler et (à part) son validateur tiennent en un ou deux fichiers.

```csharp
// conc: src/Application/TodoItems/Commands/CreateTodoItem/CreateTodoItem.cs:6-37
public record CreateTodoItemCommand : IRequest<int> { public int ListId { get; init; } public string? Title { get; init; } }
public class CreateTodoItemCommandHandler : IRequestHandler<CreateTodoItemCommand, int>
{
    private readonly IApplicationDbContext _context;
    ...
        _context.TodoItems.Add(entity);
        await _context.SaveChangesAsync(cancellationToken);
        return entity.Id;
```
```csharp
// conc: src/Web/Endpoints/TodoItems.cs:23-27
public static async Task<Created<int>> CreateTodoItem(ISender sender, CreateTodoItemCommand command)
{ var id = await sender.Send(command); return TypedResults.Created($"/{nameof(TodoItems)}/{id}", id); }
```
CQRS par dossiers `Commands/` et `Queries/` seulement : pas de modèle de lecture séparé (carte p1-map-comp, section 3). Un item template `ca-usecase` génère un fichier avec un handler `throw new NotImplementedException()`.

**Comment on fait.** Quatre fichiers par use case (commande, interface `IUseCase<,>`, validateur, classe scellée à constructeur primaire) ; le contrôleur appelle l'interface. Les lectures passent par un port `*Queries` qui projette sans charger l'agrégat.

```csharp
// nous: Src/Application/AppTemplate.Application/Features/TodoLists/UseCases/Commands/CreateTodoList/ICreateTodoListUseCase.cs:8
public interface ICreateTodoListUseCase : IUseCase<CreateTodoListCommand, Result<Versioned<TodoListDetailDto>>>;
// .../CreateTodoListUseCase.cs:33-45
var validation = await validator.EnsureValidAsync(command, cancellationToken);
if (validation.IsFailure) { return validation.To<Versioned<TodoListDetailDto>>(); }
var todoList = TodoList.Create(userId.Value, command.Name, dateTimeProvider.UtcNow);
repository.Add(todoList);
await unitOfWork.SaveChangesAsync(cancellationToken);
```
Le générateur `new-use-case` écrit ces fichiers (AGENTS.md, section « Adding or removing a feature »). `LayerDependencyTests.cs:48` interdit `MediatR` dans Application.

**Verdict : choix différent assumé (ADR 0003).** Confiance haute.
Coût réel chez nous : 4 fichiers et ~72 lignes pour `CreateTodoList` (3 + 14 + 47 + 8) contre 37 lignes pour `CreateTodoItem`. Le générateur écrit ces fichiers et le cadrage promet « ne coder que du métier » ; le coût est d'écriture, pas de lecture. On ne le qualifie pas de retard : le concurrent n'a ni validation systématique (4 validateurs pour 7 commandes, carte p1-map-comp D1) ni propriété.

**Ce qu'on gagnerait à lui prendre.** Rien de structurant. Seul le principe « un fichier » du gabarit `ca-usecase` pourrait inspirer un affichage plus court du résultat de `new-use-case` ; le gain est cosmétique, aucun ADR.

**Ce qu'il faut refuser, et pourquoi.** MediatR : la discussion 1413 du concurrent (https://github.com/jasontaylordev/CleanArchitecture/discussions/1413) documente un avertissement de licence « You do not have a valid license key for the Lucky Penny software MediatR » dans un projet fraîchement généré, et un utilisateur y signale qu'un client dépassant le seuil de 5 M USD a failli être touché, parce que des assistants de code reprennent ce dépôt comme référence. Le mainteneur répond qu'il garde MediatR et AutoMapper (discussion 1346, https://github.com/jasontaylordev/CleanArchitecture/discussions/1346). Notre choix est donc confirmé par un coût que ses utilisateurs ont déjà payé.

**Ce qu'on peut faire qu'il ne fait pas.** Un projet généré sans licence tierce à surveiller ; un graphe d'appel que le compilateur connaît (`F12` du contrôleur à l'implémentation). Issue 1325 (https://github.com/jasontaylordev/CleanArchitecture/issues/1325, « MediatR is now commercial », fermée sans changement) : la demande d'un médiateur maison n'a pas reçu de réponse technique, la nôtre est de ne pas en avoir.

### 1.2 Pipeline transversal et validation

**Comment fait le concurrent.** Cinq éléments enregistrés sur MediatR, dont la validation et l'autorisation.

```csharp
// conc: src/Application/DependencyInjection.cs:16-23
builder.Services.AddMediatR(cfg => {
    cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly());
    cfg.AddOpenRequestPreProcessor(typeof(LoggingBehaviour<>));
    cfg.AddOpenBehavior(typeof(UnhandledExceptionBehaviour<,>));
    cfg.AddOpenBehavior(typeof(AuthorizationBehaviour<,>));
    cfg.AddOpenBehavior(typeof(ValidationBehaviour<,>));
    cfg.AddOpenBehavior(typeof(PerformanceBehaviour<,>));
```
`ValidationBehaviour` exécute les validateurs en parallèle puis lève `ValidationException` (`conc: src/Application/Common/Behaviours/ValidationBehaviour.cs:19-29`). `LoggingBehaviour` sérialise la requête entière (`{@Request}`, `LoggingBehaviour.cs:32`). Pas de transaction dans le pipeline.

**Comment on fait.** Aucun behaviour. Validation appelée à la main dans chaque use case (`EnsureValidAsync`), journal de requête en middleware, commit par `IUnitOfWork`, transversal persistance en intercepteurs EF.

```csharp
// nous: Src/Application/AppTemplate.Application.Core/Common/Validation/ValidationExtensions.cs:8-19
var validation = await validator.ValidateAsync(request, cancellationToken);
return validation.IsValid ? Result.Success() : Result.Failure(ValidationError.From(validation));
// nous: Src/Presentation/AppTemplate.Api/Common/Observability/RequestLoggingMiddleware.cs:19-26
"{Method} {Path} responded {StatusCode} in {ElapsedMilliseconds}ms (traceIdentifier ..., trace ...)"
```

**Verdict : choix différent assumé (ADR 0003), avec un trou à fermer.** Confiance moyenne à haute.
Le trou : `grep -rn "IValidator\|EnsureValid" Tests/Architecture` ne renvoie rien (rejoué). `UseCaseConventionTests` ne contrôle que dossier, nom, `sealed` et `public` (`:23,82,101,120`). Un use case qui oublierait `EnsureValidAsync` passerait tous les tests d'architecture : la garantie que le behaviour donnait au concurrent n'est tenue chez nous que par des tests unitaires de chaque use case (non lus un à un). Le cadrage dit « les règles sont tenues par des tests » (promesse 3) ; ici elle ne l'est pas.

**Ce qu'on gagnerait à lui prendre.** Rien du pipeline. Du seuil de lenteur de `PerformanceBehaviour` (500 ms) : nous journalisons la durée de chaque requête HTTP sans seuil d'alerte ; une ligne `LogWarning` au-delà d'une option validée dans `RequestLoggingMiddleware` serait l'équivalent, sans behaviour. Gain faible, c'est une option de configuration (ligne de `docs/reference/configuration.md` exigée par `ConfigurationSurfaceTests`), pas un ADR.

**Ce qu'il faut refuser, et pourquoi.** Le pipeline de behaviours. Preuves de coût chez le concurrent : issue 700 (https://github.com/jasontaylordev/CleanArchitecture/issues/700, `UnhandledExceptionBehaviour` s'exécute avant et malgré les handlers d'exception de MediatR, réponse : « réordonnez les behaviours ») ; issue 324 (https://github.com/jasontaylordev/CleanArchitecture/issues/324, une validation échouée est journalisée comme une erreur avec trace de pile) ; issue 1046 (https://github.com/jasontaylordev/CleanArchitecture/issues/1046, chaque autorisation fait un aller-retour base, corrigée en 2025). Refuser aussi le journal `{@Request}` : il écrit le corps complet de la requête, mots de passe compris ; chez nous `RequestLogRedactionTests` tient la rédaction (p4-inventory-sonnet F15, déduit de la lecture et non rejoué).

**Ce qu'on peut faire qu'il ne fait pas.** Un test d'architecture qui tient l'appel du validateur : pour chaque classe `*UseCase` dont la commande a un `AbstractValidator`, exiger une dépendance sur `IValidator<>` (NetArchTest `HaveDependencyOn`), avec sa contre-épreuve. Le concurrent ne peut pas le faire : il n'a aucun test d'architecture.

### 1.3 Erreurs

**Comment fait le concurrent.** Exceptions de bout en bout, traduites par un `IExceptionHandler`. `Result` n'existe que pour `IIdentityService`.

```csharp
// conc: src/Web/Infrastructure/ProblemDetailsExceptionHandler.cs:17-44 (extrait)
ValidationException ve => (400, new ValidationProblemDetails(ve.Errors) { ... }),
NotFoundException ne   => (404, new ProblemDetails { Title = "The specified resource was not found." ... }),
UnauthorizedAccessException => (401, ...),  ForbiddenAccessException => (403, ...),
_ => (-1, null)
// conc: src/Application/Common/Models/Result.cs:11-13
public bool Succeeded { get; init; }  public string[] Errors { get; init; }
```
Pas de code d'erreur stable dans le corps ; les mises à jour dont l'id d'URL diffère de celui du corps répondent `BadRequest()` sans corps (`conc: TodoItems.cs:34-35`).

**Comment on fait.** `Result`/`Error` avec huit `ErrorType` pour l'attendu, un `code` pointé stable, `DomainException` pour l'inattendu, une table des codes testée dans les deux sens.

```csharp
// nous: Src/Application/AppTemplate.Application.Core/Common/Results/ErrorType.cs:3-20
Validation, NotFound, Unauthorized, Forbidden, Conflict, TooManyRequests, PreconditionFailed, PreconditionRequired
// nous: Src/Application/AppTemplate.Application.Core/Common/Validation/DomainGuard.cs:18-21
catch (DomainException exception) { return Result.Failure(CommonErrors.InvariantViolated(exception.Message)); }
// nous: Src/Presentation/AppTemplate.Api/Common/Errors/GlobalExceptionHandler.cs:27-31
DomainException => (StatusCodes.Status400BadRequest, "Invalid request", "domain.invariantViolated", ...)
```

**Verdict : on dépasse.** Confiance haute, avec un défaut interne (F2).
F2, prouvé : le même code `domain.invariantViolated` répond 409 quand le use case enveloppe l'appel dans `DomainGuard.Try` (`docs/architecture/domain-and-results.md:59`, `docs/reference/api.md:247`, tests `AggregateInvariantTests.cs:28`) et 400 quand la `DomainException` n'est pas rattrapée (`GlobalExceptionHandler.cs:27-31`, documenté `domain-and-results.md:60`). `docs/reference/api.md:247` ne liste que le 409. `GlobalExceptionHandlerTests.cs` n'a aucun cas `DomainException` (`grep` vide). La page `docs/architecture/http-api.md:44-46` promet « one situation always answers the same status and code, on every endpoint ». `ErrorCodeTableTests` ne voit pas l'écart : il compare la table à des `Error` déclarés, pas aux gestionnaires d'exceptions.

**Ce qu'on gagnerait à lui prendre.** Rien. Le concurrent n'a ni code stable, ni table, ni test.

**Ce qu'il faut refuser, et pourquoi.** Les exceptions pour le flux attendu : issue 341 (https://github.com/jasontaylordev/CleanArchitecture/issues/341, « performance overhead of throwing exception for validation errors », 8 réactions, des utilisateurs y recommandent un `Result`) et issue 324 ci-dessus. Aussi le 403 : le concurrent renvoie `ForbiddenAccessException` en 403 (`ProblemDetailsExceptionHandler.cs:37`), contraire à ADR 0009.

**Ce qu'on peut faire qu'il ne fait pas.** Fermer F2 : soit le chemin non rattrapé répond comme l'autre (409 `domain.invariantViolated`), soit il prend un code distinct (`domain.invariantUnexpected`, 500 puisque personne ne l'attendait) ; dans les deux cas une ligne dans `docs/reference/api.md` et un test dans `GlobalExceptionHandlerTests`. Comme les changements cassants sont déjà dans develop (#172), la fenêtre est ouverte avant 1.1.

### 1.4 Événements de domaine

**Comment fait le concurrent.** `BaseEvent` est un `INotification` MediatR défini dans Domain. L'intercepteur publie dans `SavingChanges`, donc avant l'écriture, et la version synchrone bloque.

```csharp
// conc: src/Infrastructure/Data/Interceptors/DispatchDomainEventsInterceptor.cs:17-20, 47-48
public override InterceptionResult<int> SavingChanges(...) { DispatchDomainEvents(eventData.Context).GetAwaiter().GetResult(); ... }
foreach (var domainEvent in domainEvents) await _mediator.Publish(domainEvent);
// conc: src/Domain/Entities/TodoItem.cs:47-52
set { if (value && !_done) { AddDomainEvent(new TodoItemCompletedEvent(this)); } _done = value; }
```
Un seul handler, qui journalise (`conc: src/Application/TodoItems/EventHandlers/LogTodoItemCompleted.cs`). `docs/decisions/ADR-003-MediatR-Contracts-In-Domain.md:13` écrit que les événements sont publiés « after `SaveChangesAsync` » : la doc contredit le code. Aucun test d'événement (carte p1-map-comp, section 6).

**Comment on fait.** L'agrégat lève `IDomainEvent`, l'intercepteur collecte avant la sauvegarde, publie après le commit, restitue les événements si la sauvegarde échoue ; un consommateur qui lève est journalisé et sauté.

```csharp
// nous: Src/Infrastructure/AppTemplate.Infrastructure.Core/Common/Saving/DomainEvents/DomainEventDispatchSaveChangesInterceptor.cs:39-56, 80-86
SavedChangesAsync: foreach (var domainEvent in TakePending(eventData.Context)) { try { await dispatcher.DispatchAsync(domainEvent, CancellationToken.None); } catch (Exception exception) { LogDispatchFailure(...); } }
SaveChangesFailed: RestorePending(eventData.Context);
```
Même défaut synchrone chez nous : `SavedChanges` fait `dispatcher.DispatchAsync(domainEvent).GetAwaiter().GetResult()` (`:69`). `DomainEventTests.cs:27,71` relie événements levés et consommateurs, dans les deux sens.

**Verdict : choix différent assumé (ADR 0006), et on dépasse sur la preuve.** Confiance haute.
La question « avant ou après la sauvegarde » est celle de l'issue 326 du concurrent (https://github.com/jasontaylordev/CleanArchitecture/issues/326, 10 commentaires, fermée sans consensus). Le concurrent a choisi « avant, même transaction » sans le dire dans sa propre ADR. Chez nous ADR 0006 écrit les deux conséquences et un test les tient.

**Ce qu'on gagnerait à lui prendre.** Rien. Un événement avant sauvegarde, dans la même transaction, serait un autre contrat, que les consommateurs « qui recalculent leur précondition » (ADR 0006) n'ont pas.

**Ce qu'il faut refuser, et pourquoi.** `MediatR.Contracts` dans le domaine : discussion 1298 (https://github.com/jasontaylordev/CleanArchitecture/discussions/1298, « Domain project referencing MediatR », la réponse du mainteneur : « un petit compromis intentionnel »). Chez nous `LayerDependencyTests.cs:10-30` interdit au domaine Application, EF, DI, FluentValidation, System.Text.Json. Refuser aussi la publication synchrone-bloquante dans `SavingChanges` (blocage du thread, événements publiés pour une écriture qui peut ensuite échouer).

**Ce qu'on peut faire qu'il ne fait pas.** Le balayage sous bail comme garantie, et la règle « chaque événement est consommé ou listé comme non consommé ». La discussion 1395 (https://github.com/jasontaylordev/CleanArchitecture/discussions/1395, « Encapsulation Domain Event », sans réponse) montre une interrogation que nos `DomainModelTests` tranchent : les événements sont des `record` scellés (`DomainModelTests.cs:110,147`).

### 1.5 Entités de base

**Comment fait le concurrent.** `BaseEntity` à id `int` et setters publics ; `BaseAuditableEntity` avec `Created/CreatedBy` ; `ValueObject` classe abstraite.

```csharp
// conc: src/Domain/Common/BaseEntity.cs:9,13-14
public int Id { get; set; }   [NotMapped] public IReadOnlyCollection<BaseEvent> DomainEvents => _domainEvents.AsReadOnly();
// conc: src/Domain/Entities/TodoItem.cs:35-41  (ListId, Title, Note, Priority : tous set; publics)
```

**Comment on fait.** `Entity<TId>` immuable, `AggregateRoot<TId>`, `AuditableAggregateRoot<TId>`, ids `Guid.CreateVersion7()`, value objects en `record` à constructeur privé, fabrique `Create` qui lève, `Rehydrate` qui ne lève pas.

```csharp
// nous: Src/Domain/AppTemplate.Domain.Core/Common/Primitives/Entity.cs:12  public TId Id { get; private init; }
// nous: Src/Domain/AppTemplate.Domain/Features/TodoLists/Model/Entities/TodoList.cs:28-35
var list = new TodoList(Guid.CreateVersion7(), ownerId, TodoListName.Create(name));
list.RaiseDomainEvent(new TodoListCreatedDomainEvent(list.Id, ownerId, list.Name.Value, now));
```
`DomainModelTests.cs:47` : `Entities_HaveNoPubliclySettableState`.

**Verdict : on dépasse.** Confiance haute. Les invariants vivent dans le domaine et un test interdit les setters publics ; le concurrent n'a qu'un validateur côté Application. Le coût chez nous est la règle écrite deux fois (validateur et fabrique) ; `CreateTodoListCommandValidator.cs:12` réutilise `TodoListName.MaxLength`, ce qui borne la dérive.

**Ce qu'on gagnerait à lui prendre.** Rien. **À refuser** : les setters publics, l'id `int` par défaut. **Ce qu'on fait qu'il ne fait pas** : un domaine sans base `ValueObject` (les `record` donnent l'égalité par valeur).

### 1.6 Mapping

**Comment fait le concurrent.** AutoMapper 16 : `ProfileMapping` privé dans chaque DTO, `ProjectTo` en lecture, affectation manuelle en écriture, `AssertConfigurationIsValid` testé.

```csharp
// conc: src/Application/TodoLists/Queries/GetTodos/TodoListDto.cs:20-26   private class Mapping : Profile { CreateMap<TodoList, TodoListDto>(); }
// conc: src/Application/TodoLists/Queries/GetTodos/GetTodos.cs:43-47      _context.TodoLists.AsNoTracking().ProjectTo<TodoListDto>(...).OrderBy(t => t.Title).ToListAsync(...)
// conc: tests/Application.UnitTests/Common/Mappings/MappingTests.cs:31-34 AssertConfigurationIsValid()
```

**Comment on fait.** Mappeurs statiques à la main : domaine vers DTO, DTO vers réponse, agrégat vers ligne EF.

```csharp
// nous: Src/Application/AppTemplate.Application/Features/TodoLists/Model/Mapping/TodoListDtoMapping.cs:8-24
internal static class TodoListDtoMapping { public static Versioned<TodoListDetailDto> Detail(TodoList todoList) { ... new TodoListDetailDto(todoList.Id, todoList.Name.Value, ...) } }
```

**Verdict : choix différent assumé, mais sans ADR numéroté.** Confiance moyenne. Le choix est écrit dans `docs/architecture/README.md` (« What is deliberately absent »). Preuves de coût côté concurrent : la discussion 1346 (AutoMapper devenu commercial), l'issue 1206 (https://github.com/jasontaylordev/CleanArchitecture/issues/1206, demande de passer à Mapperly, refusée) où un utilisateur écrit que Mapperly « n'était pas un bon choix » dès que les noms de propriétés divergent et qu'il est revenu à des classes de mapping simples.

**Ce qu'on gagnerait.** Une ADR « pas d'AutoMapper, pas de Mapperly » : la règle d'AGENTS.md (« un choix qu'une autre équipe aurait pu faire autrement est une ADR ») s'applique, et les preuves de licence sont maintenant dans l'audit. Coût : une page, pas de code.
**À refuser** : AutoMapper (licence, `ProjectTo` qui lie la forme du DTO au modèle EF). **Qu'on fait en plus** : une projection EF explicite dans `TodoListQueries.cs:16-35`, vérifiée par le compilateur à chaque changement de colonne.

### 1.7 Autorisation

**Comment fait le concurrent.** `RequireAuthorization()` posé à chaque groupe d'endpoints ; en plus, `[Authorize]` sur la requête MediatR lu par `AuthorizationBehaviour` par réflexion.

```csharp
// conc: src/Web/Endpoints/TodoItems.cs:13   groupBuilder.RequireAuthorization();
// conc: src/Application/Common/Behaviours/AuthorizationBehaviour.cs:58-66  request.GetType().GetCustomAttributes<AuthorizeAttribute>() ... throw new ForbiddenAccessException();
```
Un seul `[Authorize]` applicatif pour 9 requêtes (`GetTodos.cs:9`, `grep -rn "\[Authorize" src`). Un groupe d'endpoints sans l'appel serait anonyme ; aucun test ne le détecterait. Rôle `Administrator` jamais exigé. Un seul test d'autorisation (`GetTodosTests.cs:50-58`).

**Comment on fait.** Politique de repli `RequireAuthenticatedUser` : tout est authentifié sauf `[AllowAnonymous]`, dont la liste fermée est testée ; un `[Authorize]` sans `[AllowAnonymous]` en `--auth none` refuse le démarrage.

```csharp
// nous: Src/Presentation/AppTemplate.Api/Program.cs:111-116   builder.Services.AddAuthorizationBuilder().SetFallbackPolicy(... .RequireAuthenticatedUser() ...)
// nous: Tests/Architecture/AppTemplate.Architecture.Tests/Rules/HttpSurfaceTests.cs:10-23  _anonymousActions = [ "ConfirmEmail", "Login", ... "ResetPassword" ];
```
Une politique nommée `Administrator` sur 5 contrôleurs (`AuthComposition.cs:49`).

**Verdict : on dépasse.** Confiance haute. Default-deny contre « opt-in par groupe » ; liste fermée contre rien. Pas de permissions fines des deux côtés (choix déclaré dans `docs/architecture/README.md`, cadrage).
**Ce qu'on gagnerait à lui prendre.** Rien. **À refuser** : l'autorisation par réflexion sur un attribut de requête (une requête sans attribut est ouverte). **Qu'on fait en plus** : des rôles OIDC configurables pour `Administrator` (`Presentation.Core`, options OIDC externes) ; le concurrent n'a pas d'OIDC (issue 1613, https://github.com/jasontaylordev/CleanArchitecture/issues/1613, « exclure Identity et Todo », fermée : le mainteneur renvoie à l'issue 1192).

### 1.8 Propriété des données

**Comment fait le concurrent.** Aucune. `GetTodos` renvoie toutes les listes (`conc: GetTodos.cs:43-47`), aucun `OwnerId`, aucun test 403 (`grep -rni "Forbidden\|403" tests` vide).

**Comment on fait.** Les chemins de lecture et le chemin « service sur store » filtrent par propriétaire dans le SQL ; le chemin agrégat charge par id et compare en mémoire.

```csharp
// nous: Src/Infrastructure/AppTemplate.Infrastructure.Persistence/Features/TodoLists/Access/Repositories/TodoListRepository.cs:24-29
var record = await context.TodoLists.Include(list => list.Items)... .FirstOrDefaultAsync(list => list.Id == id, cancellationToken);
// nous: Src/Application/AppTemplate.Application.Core/Common/Ownership/OwnedAggregate.cs:20-23
if (aggregate is null || aggregate.OwnerId != ownerId) { return Result.Failure<TAggregate>(notFound); }
// nous: docs/decisions/0009-ownership-is-in-the-query.md:28   "no code path holds another user's row."
```
`ITodoListRepository.GetAsync(Guid id, ...)` ne reçoit pas de propriétaire (`Src/Domain/AppTemplate.Domain/Features/TodoLists/Repositories/ITodoListRepository.cs:9`) ; le générateur écrit la même forme (`Tools/NewFeature.cs:3051` pour le port, `:3648` pour `OwnedAggregate.Require`), donc chaque nouvelle feature agrégat en hérite.

**Verdict : on dépasse le concurrent ; défaut interne F1 contre notre ADR.** Confiance haute.
Ce que la réponse HTTP donne reste juste : 404 identique pour « absent » et « d'un autre » (`OwnershipIsolationTests`). Ce qui est faux est le texte d'ADR 0009 : lignes 7-9, il décrit comme refusée la méthode « charger par id puis comparer le propriétaire », et ligne 28 il promet qu'aucune ligne d'un autre utilisateur n'est tenue en mémoire. Le chemin agrégat fait exactement ce que le contexte refuse. Gravité de sécurité faible (la ligne n'est pas divulguée), gravité de cohérence haute : la source de vérité 2 contredit la source 1, et le défaut est reproduit par le générateur.

**Ce qu'on gagnerait à lui prendre.** Rien.
**Ce que nous devons faire (F1).** Deux voies, à trancher par ADR : (a) faire porter le propriétaire par le port (`GetOwnedAsync(Guid id, UserId ownerId, ...)`, `Where(list => list.Id == id && list.OwnerId == ownerId.Value)`), ce qui rend le texte vrai et retire une branche de `OwnedAggregate` ; (b) réécrire ADR 0009 pour dire que l'agrégat est chargé par id et comparé, en précisant pourquoi c'est sûr. La voie (a) tient la promesse 5 du cadrage. Dans les deux cas un test d'architecture : tout port `Repository` d'un agrégat `IOwnedAggregate` expose un `GetAsync` qui prend un `UserId`.
**À refuser** : l'absence de propriétaire par défaut (`--access shared` existe pour le cas voulu).

### 1.9 Horloge (point trouvé en passant)

**Comment fait le concurrent.** `TimeProvider.System` enregistré une fois, consommé par l'intercepteur d'audit.

```csharp
// conc: src/Infrastructure/DependencyInjection.cs:76   builder.Services.AddSingleton(TimeProvider.System);
// conc: src/Infrastructure/Data/Interceptors/AuditableEntityInterceptor.cs:12  private readonly TimeProvider _dateTime;
```

**Comment on fait.** Un port maison, présent dans 46 fichiers de `Src`.

```csharp
// nous: Src/Application/AppTemplate.Application.Core/Common/Ports/IDateTimeProvider.cs:5   DateTimeOffset UtcNow { get; }
// nous: docs/architecture/shared-mechanisms.md:76   "`IDateTimeProvider.UtcNow` is the one clock."
// nous: Src/Infrastructure/AppTemplate.Infrastructure.Storage/Features/Files/Adapters/Transfers/S3FileDirectTransfer.cs:78  DateTimeOffset.UtcNow.Add(granted)
// nous: Src/Infrastructure/AppTemplate.Infrastructure.Storage/Features/Files/Adapters/Inventories/FileSystemFileContentInventory.cs:61  DateTime.UtcNow - File.GetLastWriteTimeUtc(path)
```
Faux d'horloge dans les tests : `SettableClock` (`Tests/Presentation/AppTemplate.Api.UnitTests/Features/ExternalIdentity/ExternalIdentityProvisioningTests.cs:368`), `FixedClock` (`.../AuditingSaveChangesInterceptorTests.cs:100`), `MovableDateTimeProvider` (`.../Infrastructure.Auth.UnitTests/Fixtures/MovableDateTimeProvider.cs:5`), et `FixedDateTimeProvider` dans un projet de production (`Infrastructure.InMemory/Common/Time/FixedDateTimeProvider.cs`). `grep "FakeTimeProvider\|Time.Testing" Directory.Packages.props Tests` : vide. Aucun test d'architecture n'interdit `DateTime.UtcNow`.

**Verdict : en retard (mineur), F4.** Confiance moyenne à haute. Le concurrent s'appuie sur un type du framework qui accepte `FakeTimeProvider` et que les bibliothèques (HybridCache, résilience) lisent déjà ; nous maintenons un double, quatre faux et une règle « horloge unique » que deux sites de production violent sans qu'aucun test le voie.
**Ce qu'on gagnerait à lui prendre.** Remplacer `IDateTimeProvider` par `TimeProvider`, ajouter `Microsoft.Extensions.TimeProvider.Testing` aux tests, ajouter une règle d'architecture (`DateTime.UtcNow`, `DateTimeOffset.UtcNow` interdits hors du module qui enregistre `TimeProvider.System`) avec sa contre-épreuve. Forme : une ADR courte « l'horloge est `TimeProvider` » (c'est un choix qu'une autre équipe peut faire autrement) ; changement mécanique sur 46 fichiers, cassant seulement pour les projets déjà générés, dans la fenêtre des changements cassants de 1.1.
**À refuser** : rien. **Qu'on peut faire en plus** : la règle d'architecture, que le concurrent ne peut pas écrire.

---

## Axe 2. Architecture et frontières

### 2.1 Projets et références

**Comment fait le concurrent.** Sept projets sous `src/`, graphe de références comme seule garde. Domain référence `MediatR.Contracts` ; Application référence EF Core ; Web référence Infrastructure et l'utilise.

```xml
<!-- conc: src/Domain/Domain.csproj:9 -->  <PackageReference Include="MediatR.Contracts" />
<!-- conc: src/Web/Web.csproj:14-16 -->     ProjectReference Application, Infrastructure, ServiceDefaults
```
```csharp
// conc: src/Web/Endpoints/Users.cs:1,12  using CleanArchitecture.Infrastructure.Identity;  groupBuilder.MapIdentityApi<ApplicationUser>();
```
Aucun `NetArchTest`/`ArchUnit` (`grep` vide) ; ADR-001 assume EF Core dans Application. La discussion 1607 (https://github.com/jasontaylordev/CleanArchitecture/discussions/1607) montre un utilisateur qui ne comprend pas d'où Infrastructure tient `Microsoft.AspNetCore.Authorization` : la réponse est « Web référence Infrastructure, le framework partagé s'y propage ».

**Comment on fait.** 14 projets sous `Src/` en quatre couches, chacune avec un projet `*.Core` sans feature ; seules les hôtes référencent les modules d'infrastructure ; les règles sont des tests.

```xml
<!-- nous: Src/Application/AppTemplate.Application/AppTemplate.Application.csproj:13-14 -->
<ProjectReference Include="..\..\Domain\AppTemplate.Domain\AppTemplate.Domain.csproj" /> <ProjectReference Include="..\AppTemplate.Application.Core\AppTemplate.Application.Core.csproj" />
```
```csharp
// nous: Tests/Architecture/.../Rules/ModuleDependencyTests.cs:380-397  OnlyAHost_ReferencesInfrastructureModules : offenders.ShouldBeEmpty("Only a composition root — a project with a Program.cs — may reference an infrastructure module.")
```
Autres règles : `NoInfrastructureModule_DependsOnAnotherInfrastructureModule` (`:215`), `EveryClient_ReachesTheCoreThroughTheApiAlone` (`:434`), `FeatureIsolationTests`, `AdapterVisibilityTests` (adaptateurs d'un port : internes à leur module).

**Verdict : on dépasse.** Confiance haute. Le graphe du concurrent a des fuites prouvées (Web → Infrastructure utilisé ; `Application.UnitTests` référence Infrastructure sans l'utiliser, carte p1-map-comp section 1) ; chez nous chaque règle est un test.
**À prendre.** Rien. **À refuser** : domaine dépendant d'un paquet de médiateur. **Qu'on fait en plus** : `ProjectReferenceGraph` lit les `.csproj`, donc la règle porte sur le graphe réel et pas seulement sur les types.

### 2.2 Tests d'architecture et leurs contre-épreuves

**Comment fait le concurrent.** Aucun test d'architecture. 32 méthodes NUnit en tout, dont 6 sur le domaine ; `Infrastructure.IntegrationTests` est vide (carte p1-map-comp, section 6).

**Comment on fait.** xUnit v3 + NetArchTest 1.3.2 ; 43 fichiers, 195 `[Fact]/[Theory]`. Chaque règle refuse un ensemble vide (`RuleAssertions.RequireTypes`) et 17 méthodes `...IsSensitive...` prouvent que la règle sait échouer.

```csharp
// nous: Tests/Architecture/.../Fixtures/RuleAssertions.cs:11-17  types.ShouldNotBeEmpty("... so every rule written against it would pass for the wrong reason.")
// nous: Tests/Architecture/.../Rules/LayerDependencyTests.cs:206-219  TheDomainRule_IsSensitive_AndDetectsARealViolation : applique la liste interdite du domaine à Persistence et exige l'échec
```
Comptage rejoué : `grep -ln Sensitive Rules/*.cs` = 16 fichiers sur 34. Les fichiers sans contre-épreuve : `UseCaseConventionTests`, `DomainModelTests`, `HttpSurfaceTests`, `StorageShapeTests`, `StorageVocabularyTests`, `ErrorCodeTableTests`, `ConfigurationSurfaceTests` (carte p1-map-ours D5, recoupée).

**Verdict : on dépasse ; la contre-épreuve n'est pas universelle (F7).** Confiance haute. Un test d'architecture sans contre-épreuve peut passer sur un ensemble qu'il ne lit plus ; `RequireTypes` couvre le cas « ensemble vide », pas le cas « prédicat qui ne détecte rien ». Les sept fichiers listés portent des règles centrales (use cases, domaine, surface HTTP).
**À prendre.** Rien. **Ce que nous devons faire.** Ajouter le jumeau `...IsSensitive...` aux sept fichiers, ou dire dans `docs/guides/testing.md` quelles règles en sont dispensées et pourquoi. Le cadrage (promesse 3, ADR 0020) vise « un risque = un type de test », pas « tout test a sa contre-épreuve » : on le formule comme une cible à tenir, sans prétendre que c'est livré.
**Qu'on fait en plus.** Les règles de composition (`ContainerCompositionTests`, 15 tests avec validation stricte du conteneur) et la citation de la doc (`DocumentationCitationTests`) n'ont pas d'équivalent chez le concurrent.

### 2.3 Analyzers, diagnostics et la règle « ne jamais les taire »

**Comment fait le concurrent.** `TreatWarningsAsErrors`, `.editorconfig` entièrement en `suggestion`/`silent` (19 règles de nommage, aucun `dotnet_diagnostic.*.severity`), un seul analyzer (NUnit.Analyzers), un `#pragma warning disable CS1998` livré.

```xml
<!-- conc: Directory.Build.props:5  <TreatWarningsAsErrors>true</TreatWarningsAsErrors> -->
```
```csharp
// conc: src/Application/WeatherForecasts/Queries/GetWeatherForecasts/GetWeatherForecastsQuery.cs:12-14  #pragma warning disable CS1998 ... #pragma warning restore CS1998
```
Pas de règle écrite à ce sujet.

**Comment on fait.** `latest-recommended`, `EnforceCodeStyleInBuild`, `TreatWarningsAsErrors`, namespaces file-scoped et `IDE0005` en erreur ; règle écrite : « jamais de `#pragma`, de `<NoWarn>` ni de sévérité abaissée » (`AGENTS.md`, « Changing code » ; `docs/guides/build-and-ci.md:70-72`), et `.editorconfig:90` « never a repository-wide NoWarn ».

```
$ grep -rn "#pragma\|<NoWarn>" Src Directory.* (hors Generated, Migrations) :
Directory.Build.props:27:    <NoWarn>$(NoWarn);CS1591;CS1573</NoWarn>
Src/Presentation/AppTemplate.Api/AppTemplate.Api.csproj:16:    <NoWarn>$(NoWarn);AV0029;AV0030</NoWarn>
Src/Presentation/AppTemplate.Api/Common/Hosting/HttpPipelineModule.cs:67:#pragma warning disable ASP0000
$ grep -n "severity = none" .editorconfig | wc -l   -> 25   (dont .editorconfig:57-59 : CA2007, CA1848, CA1515, globaux)
$ grep -rln "pragma\|NoWarn" Tests/Architecture   -> (vide)
```
Le `#pragma` couvre `services.BuildServiceProvider()` (`HttpPipelineModule.cs:67-69`) : ASP0000 signale un second conteneur qui duplique les singletons, utilisé pour lire `IApiVersionDescriptionProvider` au démarrage (`Program.cs:106`, `ApiVersionGroups`).

**Verdict : en retard sur nous-mêmes (F3), à égalité avec le concurrent sur le fait.** Confiance haute. Le concurrent n'a pas de règle, donc pas de contradiction ; nous en avons une, violée cinq fois, et la règle AGENTS « une règle vérifiée à la main devient un test d'architecture » n'est pas appliquée. Le `#pragma` masque en plus un défaut de conception réel (second conteneur de services).
**Ce qu'on gagnerait à lui prendre.** Rien.
**Ce que nous devons faire.** (1) Supprimer le `#pragma` en remplaçant `ApiVersionGroups` par une liste de versions déclarée une fois (constante ou options liées) sans `BuildServiceProvider`. (2) Ou bien amender la règle : « sévérité par fichier, avec raison » est déjà ce que fait `.editorconfig` (sections 92-188) ; les trois `none` globaux (`:57-59`) et les deux `NoWarn` de projet sont ce que la règle interdit. (3) Un test d'architecture qui échoue sur `#pragma warning disable` et `<NoWarn>` hors liste fermée, avec sa contre-épreuve. Une ADR n'est pas nécessaire si on suit la règle telle qu'écrite ; elle l'est si on décide d'autoriser les `NoWarn` de projet.

### 2.4 Accès aux données depuis Application : ports contre `IApplicationDbContext`

**Comment fait le concurrent.** `IApplicationDbContext` expose les `DbSet` ; pas de dépôt (ADR-001 du concurrent).

```csharp
// conc: src/Application/Common/Interfaces/IApplicationDbContext.cs:5-12
DbSet<TodoList> TodoLists { get; }  DbSet<TodoItem> TodoItems { get; }  Task<int> SaveChangesAsync(CancellationToken cancellationToken);
```

**Comment on fait.** Ports nommés par intention en quatre mots (Repository, Queries, Store, Table), jamais d'`IQueryable` ; domaine non mappé par EF.

```csharp
// nous: Src/Domain/AppTemplate.Domain/Features/TodoLists/Repositories/ITodoListRepository.cs:9-17  Task<TodoList?> GetAsync(Guid id, ...); Task<IReadOnlyList<TodoList>> GetForOwnerAsync(UserId ownerId, ...); void Add(...); void Remove(...)
```
`PortConventionTests` : au plus six opérations par port ; `StorageShapeTests` refuse `IQueryable` sur un port.

**Verdict : choix différent assumé (ADR 0003, ADR 0005).** Confiance haute. Preuve de coût chez le concurrent : l'issue 464 (https://github.com/jasontaylordev/CleanArchitecture/issues/464, « This is not exactly The Clean Architecture style », 7 réactions, 10 commentaires), la discussion 482 du même sujet (16 commentaires, 16 votes, https://github.com/jasontaylordev/CleanArchitecture/discussions/482), l'issue 1100 (https://github.com/jasontaylordev/CleanArchitecture/issues/1100) et la discussion 1607 : le sujet revient à chaque génération d'utilisateurs. Notre coût propre est F7 de la carte (chargement complet d'un agrégat, mapper + tracker + repository), déjà assumé par ADR 0005 et 0006.
**À prendre.** Rien ; ADR-001 du concurrent a un paragraphe « When this decision does not apply » (le dépôt de style DDD appartient au domaine) qui décrit à peu près notre forme : on peut le citer en une ligne dans `docs/architecture/ports-and-adapters.md`, sans ADR.
**À refuser.** `IQueryable` ou `DbSet` à travers un port.

### 2.5 Audit des paquets et `TreatWarningsAsErrors` sur un template figé

**Comment fait le concurrent.** Même configuration de base (`TreatWarningsAsErrors`, `NuGetAudit` par défaut) ; l'issue 1675 est ouverte depuis le 2026-10-28.

> « Build fails with 118 errors (Warning As Error due to vulnerability). The last release (10.8.0) was created 7 months ago. Since then some of the package versions have become outdated. Even though the package versions were updated in this repo, due to not releasing the project, the nuget package didn't get the update. »
> https://github.com/jasontaylordev/CleanArchitecture/issues/1675 (ouverte, 0 commentaire)

**Comment on fait.** `NuGetAudit` en mode `all`, niveau `low`, avec `TreatWarningsAsErrors`.

```xml
<!-- nous: Directory.Build.props:15,19-21 -->
<TreatWarningsAsErrors>true</TreatWarningsAsErrors> <NuGetAudit>true</NuGetAudit> <NuGetAuditMode>all</NuGetAuditMode> <NuGetAuditLevel>low</NuGetAuditLevel>
```
`docs/guides/build-and-ci.md:161-165` : « `NuGetAudit` fails the build on any known advisory, `low` included. [...] the answer is a transitive pin [...] never a relaxed audit level ». Aucun `WarningsNotAsErrors` pour NU190x (`grep` vide). En parallèle, un job de CI (`.github/workflows/ci.yml:239-265`) et `verify` lisent `dotnet list package --vulnerable --include-transitive`.

**Verdict : à égalité sur l'exposition, choix documenté sans ADR.** Confiance moyenne : le scénario n'est pas rejoué ici (pas de build), mais le mécanisme est identique et la preuve de coût est chez le concurrent, 7 mois après sa dernière release. Notre 1.0.0 date du 2026-10-22 ; un projet généré depuis ce paquet figé buildera tant qu'aucun avis ne touche les versions épinglées de `Directory.Packages.props`.
**Ce qu'on gagnerait.** Garder l'exigence (l'audit reste bloquant dans `verify` et en CI, par le job existant) mais ne pas la faire échouer à la première compilation d'un projet généré : `WarningsNotAsErrors` pour NU1901-NU1904 dans `Directory.Build.props`, en laissant `dotnet list package --vulnerable` bloquant dans `verify` et CI. Cela contredit la phrase de `build-and-ci.md:163` : un choix de ce genre est une ADR (alternative : garder le strict et s'engager sur une cadence de releases du template, documentée).
**À refuser.** Relâcher `NuGetAuditLevel` ou supprimer l'audit.
**Ce qu'on peut faire qu'il ne fait pas.** Une release du template déclenchée par un avis sur une version épinglée : `template-release.yml` et Dependabot sur cinq écosystèmes existent ; le concurrent a une release manuelle (carte p1-map-comp, section 7).

### 2.6 Doc et message de test qui contredisent le code (enregistrement par scan)

```csharp
// nous: Tests/Architecture/.../Rules/UseCaseConventionTests.cs:58,197   "registration is explicit rather than scanned" / "is explicit rather than scanned"
// nous: Src/Application/AppTemplate.Application/ApplicationModule.cs:152-165  AddFeature : AddValidatorsFromAssembly(... filter: IsInVertical ...) ; services.AddUseCases(TypesIn(vertical));
// nous: docs/guides/adding-a-feature.md:153  "Nothing is registered by hand: `AddFeature` ... finds the use case and its validator by folder."
```
La page de guide dit la vérité (scan par dossier) ; les deux messages de test disent le contraire. **Verdict : défaut mineur (F5), confiance haute.** Le même défaut existe chez le concurrent sous une autre forme (`ADR-003:13` dit « après `SaveChangesAsync` » ; le code publie avant). Correction : réécrire les deux messages. Pas d'ADR.

---

## Résumé des constats internes à fermer (par ordre de gravité)

| Id | Constat | Preuve | Forme de la correction |
|---|---|---|---|
| F1 | ADR 0009 dit qu'aucun chemin ne tient la ligne d'un autre propriétaire ; le chemin agrégat la charge puis compare | `TodoListRepository.cs:24-29`, `OwnedAggregate.cs:20`, ADR 0009:7-9 et :28, `Tools/NewFeature.cs:3051,3648` | port `GetOwnedAsync(id, ownerId)` ou réécriture de l'ADR ; test sur la signature |
| F2 | `domain.invariantViolated` répond 409 ou 400 ; la table de `api.md` n'a que 409 | `DomainGuard.cs:18-21`, `GlobalExceptionHandler.cs:27-31`, `api.md:247`, `http-api.md:44-46` | un seul statut ou un code distinct ; test dans `GlobalExceptionHandlerTests` |
| F3 | « Jamais de `#pragma` ni de `NoWarn` » violé cinq fois, sans test | `Directory.Build.props:27`, `AppTemplate.Api.csproj:16`, `HttpPipelineModule.cs:67`, `.editorconfig:57-59` | supprimer `BuildServiceProvider` ; test d'architecture sur `#pragma`/`NoWarn` |
| F4 | `IDateTimeProvider` : 46 fichiers, quatre faux, deux contournements, doublon de `TimeProvider` | `shared-mechanisms.md:76`, `S3FileDirectTransfer.cs:78`, `FileSystemFileContentInventory.cs:61` | `TimeProvider` + `FakeTimeProvider` + règle d'architecture ; ADR courte |
| F5 | Messages de test « explicit rather than scanned » contre un enregistrement par scan | `UseCaseConventionTests.cs:58,197`, `ApplicationModule.cs:152-165` | réécrire les messages |
| F6 | `NuGetAudit` bloquant + template figé : coût prouvé chez le concurrent | issue 1675 ; `Directory.Build.props:15,19-21`, `build-and-ci.md:161-165` | ADR : audit bloquant en CI et `verify`, pas à la première compilation |
| F7 | Contre-épreuve absente de 7 fichiers de règles sur 34 | `grep -ln Sensitive Rules/*.cs` : 16 fichiers | jumeaux `IsSensitive` ou dispense écrite |
| F8 | Aucune règle n'exige l'appel du validateur dans un use case | `grep "IValidator\|EnsureValid" Tests/Architecture` vide | test d'architecture sur la dépendance à `IValidator<>` |

## Ce que le concurrent a de mieux, honnêtement

- Le gabarit `ca-usecase` (un fichier, un handler) : plus court à lire que nos quatre fichiers. Notre générateur couvre le même besoin et l'étend, mais refuse un service (`new-use-case` ne promeut qu'une forme riche).
- `TimeProvider` du framework (F4).
- Un seuil de lenteur en un endroit (`PerformanceBehaviour.cs:37`), que nous n'avons pas. Gain faible.
- La matrice 3 × 3 construite en CI après chaque push (hors périmètre G1).

## Issues et discussions du concurrent utiles à notre feuille de route

| URL | État | Pourquoi |
|---|---|---|
| https://github.com/jasontaylordev/CleanArchitecture/discussions/1413 | discussion | un projet généré affiche l'avertissement de licence MediatR ; un utilisateur cite un client touché ; argument pour garder notre absence de médiateur |
| https://github.com/jasontaylordev/CleanArchitecture/discussions/1346 | discussion (annonce) | la réponse officielle du mainteneur sur les licences |
| https://github.com/jasontaylordev/CleanArchitecture/issues/1675 | ouverte | coût d'un `TreatWarningsAsErrors` sur un template figé (F6) |
| https://github.com/jasontaylordev/CleanArchitecture/issues/1192 | ouverte | `--minimal` (sans exemples) « sur la roadmap » depuis 2026-02-19 ; nous l'avons : `--examples none` |
| https://github.com/jasontaylordev/CleanArchitecture/issues/1613 | fermée | exclure Identity et Todo ; nous : `--auth none|external`, `--examples none` |
| https://github.com/jasontaylordev/CleanArchitecture/issues/464 et discussions/482 | fermées | « pas vraiment Clean Architecture » (EF Core dans Application) ; notre structure y répond par construction |
| https://github.com/jasontaylordev/CleanArchitecture/issues/326 | fermée | événements avant ou après la sauvegarde ; sans consensus chez lui, tranché chez nous |
| https://github.com/jasontaylordev/CleanArchitecture/issues/341 et issues/324 | fermées | exceptions pour les erreurs attendues ; notre `Result` y répond |
| https://github.com/jasontaylordev/CleanArchitecture/issues/1329 | fermée | soft delete refusé comme « trop complexe » ; nous n'en avons pas non plus (`StorageShapeTests.cs:21` l'interdit) |

## Non vérifié

- Aucun build, aucun test, aucune génération. Les verdicts sont des lectures de code, de tests et de documents, et des `grep` rejoués.
- F6 : le scénario d'échec de génération n'est pas rejoué chez nous ; il est prouvé chez le concurrent par l'issue 1675 et déduit chez nous de la configuration.
- F8 : l'absence de test d'architecture sur le validateur est prouvée par `grep` ; que chaque use case existant appelle bien son validateur n'est pas vérifié un à un (les tests unitaires par use case n'ont pas été lus tous).
- Les 17 contre-épreuves et leur répartition sont rejouées par `grep` ; que chaque règle de ces 16 fichiers soit réellement sensible n'est pas démontré par rupture du code.
- Les affirmations sur la performance d'AutoMapper, de Mapperly et de MediatR sont celles des utilisateurs cités, non mesurées.
- La licence exacte de MediatR 14 et d'AutoMapper 16 n'est lue que dans les textes du mainteneur (discussion 1346), pas dans les paquets.
- Le contenu de `Tools/NewFeature.cs` n'est lu qu'aux lignes citées (3051, 3648, 835-843).
