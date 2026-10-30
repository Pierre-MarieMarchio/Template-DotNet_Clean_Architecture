# Dossier à l'aveugle, groupe G1 : concepts du code, architecture et frontières

Deux templates de solution .NET 10 en Clean Architecture : le Dépôt A et le Dépôt B. Dans chaque section, A est décrit d'abord, B ensuite, sur les mêmes points. Les noms propres aux dépôts sont remplacés : projets et namespaces par « ProjA » et « ProjB », outils maison par « l'outil de A » ou « l'outil de B ». Les noms de bibliothèques tierces sont conservés. Les extraits sont abrégés (`...`).

Les deux dépôts appellent « décision écrite » un document de décision d'architecture (ADR). A en compte 28, B en compte 3.

---

## Axe 1. Concepts du code

### 1.1 Forme d'un use case

**Dépôt A.** Un dossier de quatre fichiers : la commande (`record`), une interface qui hérite de `IUseCase<TRequest, TResponse>`, le validateur, la classe du use case (`sealed`, constructeur primaire). Le contrôleur appelle l'interface.
```csharp
public interface ICreateTodoListUseCase : IUseCase<CreateTodoListCommand, Result<Versioned<TodoListDetailDto>>>;
public sealed class CreateTodoListUseCase(ITodoListRepository repository, IUnitOfWork unitOfWork,
    ICurrentUser currentUser, IDateTimeProvider clock, IValidator<CreateTodoListCommand> validator) : ICreateTodoListUseCase
{ ... var userId = currentUser.RequireUserId(); ... await validator.EnsureValidAsync(command, ct); ...
  var todoList = TodoList.Create(userId.Value, command.Name, clock.UtcNow);
  repository.Add(todoList); await unitOfWork.SaveChangesAsync(ct); return TodoListDtoMapping.Detail(todoList); }
```
Des tests d'architecture exigent qu'un use case soit scellé, public et seul dans un dossier qui porte son nom. L'outil de A génère un use case (5 fichiers et un test) et imprime l'action de contrôleur à coller.

**Dépôt B.** Un fichier qui contient la commande MediatR et son handler, plus un fichier de validateur. Le handler écrit par une interface qui expose les `DbSet` EF Core.
```csharp
public record CreateTodoItemCommand : IRequest<int> { public int ListId { get; init; } public string? Title { get; init; } }
public class CreateTodoItemCommandHandler : IRequestHandler<CreateTodoItemCommand, int>
{ ... var entity = new TodoItem { ListId = request.ListId, Title = request.Title, Done = false };
  _context.TodoItems.Add(entity); await _context.SaveChangesAsync(cancellationToken); return entity.Id; }
```
Un endpoint Minimal API envoie la commande par `ISender`. Le gabarit de use case de B écrit un fichier (commande, validateur vide, handler dont le corps est `throw new NotImplementedException()`), sans endpoint ni test.

**Mesures d'un même scénario, exécuté par un développeur simulé sur chaque dépôt :**

| Tâche | A | B |
|---|---|---|
| Ajouter une commande « Archive » et une requête « recherche par nom » | ~350 lignes à la main, 8 tests unitaires et 2 d'intégration écrits | ~110 lignes à la main, 1 test écrit |
| Ajouter un champ et sa migration | ~45 lignes, ~14 fichiers | ~14 lignes, 7 fichiers |
| Créer une feature complète (CRUD, règle, test) | ~215 lignes à la main après génération de 52 fichiers | ~380 lignes à la main après génération de 5 squelettes |

### 1.2 Médiateur et pipeline

**Dépôt A.** Aucun médiateur. Une décision écrite exclut « MediatR, a dispatcher and pipeline behaviours ». Un test d'architecture interdit la dépendance dans la couche application :
```csharp
private static readonly string[] _forbiddenInApplication = [ ..., "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", ..., "MailKit", "MediatR" ];
```
Le code transversal est réparti entre les middlewares et filtres HTTP et les intercepteurs EF Core.

**Dépôt B.** MediatR 14.2.0, avec un pré-processeur et quatre behaviours :
```csharp
cfg.AddOpenRequestPreProcessor(typeof(LoggingBehaviour<>));
cfg.AddOpenBehavior(typeof(UnhandledExceptionBehaviour<,>));
cfg.AddOpenBehavior(typeof(AuthorizationBehaviour<,>));
cfg.AddOpenBehavior(typeof(ValidationBehaviour<,>));
cfg.AddOpenBehavior(typeof(PerformanceBehaviour<,>));
```
Le pré-processeur journalise `"{Name} {@UserId} {@UserName} {@Request}"`, la requête entière. Le mainteneur a écrit qu'il garde MediatR et AutoMapper, sous licence commerciale au-delà d'un seuil de chiffre d'affaires ; le template ne configure aucune clé de licence. Un avertissement de licence s'affiche à l'exécution.

### 1.3 Validation

**Dépôt A.** FluentValidation, appelée explicitement dans le use case, après la vérification de l'appelant. Le résultat est un `Result` : code `request.validationFailed`, détail par champ. Le dépôt compte 56 validateurs et 57 appels à `EnsureValidAsync`. Aucun test ne relie un validateur au use case qui l'utilise ; aujourd'hui, chaque validateur est injecté quelque part.

**Dépôt B.** FluentValidation, appelée par un behaviour pour toute requête qui a un validateur, les validateurs en parallèle ; un échec lève une `ValidationException`.
```csharp
var validationResults = await Task.WhenAll(_validators.Select(v => v.ValidateAsync(new ValidationContext<TRequest>(request), cancellationToken)));
... if (failures.Count != 0) throw new ValidationException(failures);
```
4 validateurs pour 7 commandes. Un validateur vérifie l'unicité d'un titre par une requête en base (`MustAsync` + `AnyAsync`).

Dans les deux dépôts, une violation d'unicité levée par la base à l'enregistrement aboutit à une réponse 500.

### 1.4 Erreurs

**Dépôt A.** `Result`/`Error` pour les échecs attendus, avec 8 types d'erreur (Validation, NotFound, Unauthorized, Forbidden, Conflict, TooManyRequests, PreconditionFailed, PreconditionRequired) et un code `feature.cause` dans chaque ProblemDetails. Un test compare la table des codes de la documentation aux codes du code, dans les deux sens. Un gestionnaire global traduit les exceptions restantes :
```csharp
ConcurrencyConflictException => (409, ..., "concurrency.conflict", ...),
DomainException              => (400, ..., "domain.invariantViolated", ...),
OperationCanceledException when isServerTimeout => (504, ...), OperationCanceledException => (499, ...),
_ => (500, ..., "server.unexpected", ...)
```
Le même code `domain.invariantViolated` vaut 409 quand le use case enveloppe l'appel au domaine (`DomainGuard.Try` → `Error.Conflict`) et 400 sinon. La table de référence ne donne que 409, et les tests du gestionnaire n'ont pas de cas `DomainException`.

**Dépôt B.** Des exceptions de bout en bout ; un `IExceptionHandler` en traduit quatre en ProblemDetails, sans code applicatif, et laisse les autres au middleware par défaut :
```csharp
ValidationException ve => (400, new ValidationProblemDetails(ve.Errors) {...}),
NotFoundException ne => (404, ...), UnauthorizedAccessException => (401, ...), ForbiddenAccessException => (403, ...),
_ => (-1, null)
```
Un type `Result` (`bool Succeeded`, `string[] Errors`) ne sert qu'au service d'identité. Quand l'id de l'URL diffère de celui du corps, l'endpoint répond `BadRequest()` sans corps.

### 1.5 Événements de domaine

**Dépôt A.** Un agrégat lève ses événements (`RaiseDomainEvent`, protégé). Un intercepteur EF Core les collecte à `SavingChanges`, les publie à `SavedChanges`, après le commit, et les rend à l'agrégat à `SaveChangesFailed`. Un consommateur qui lève est journalisé et sauté. Une décision écrite fixe : publication en processus après le commit, au plus une fois, sans outbox ; des balayages périodiques servent de garantie. Un test exige que chaque événement ait un consommateur, ou figure dans une liste motivée.

**Dépôt B.** Une entité lève l'événement dans un setter (`Done` qui passe de false à true). Un intercepteur le publie par `IMediator.Publish` à `SavingChanges(Async)`, avant l'écriture ; le chemin synchrone appelle `.GetAwaiter().GetResult()`.
```csharp
public override InterceptionResult<int> SavingChanges(...) { DispatchDomainEvents(eventData.Context).GetAwaiter().GetResult(); ... }
```
La décision écrite de B dit que les événements sont publiés « after `SaveChangesAsync` ». Une issue fermée rapporte qu'un handler voit `Id == 0` (ids `int` d'identité). Le dépôt a un handler, qui journalise, et aucun test d'événement.

### 1.6 Entités de base

**Dépôt A.** `Entity<TId>`, `AggregateRoot<TId>`, `AuditableAggregateRoot<TId>`, un `UserId` scellé ; ids `Guid.CreateVersion7()`. EF Core ne mappe pas les types du domaine : il mappe des lignes, avec un mapper et un tracker par agrégat. Des tests d'architecture exigent des racines scellées, aucun état modifiable publiquement, aucun champ public, et des objets valeur et événements immuables.

**Dépôt B.** `BaseEntity` (`public int Id { get; set; }`, liste d'événements), `BaseAuditableEntity` (horodatages à setters publics) ; les entités exposent des setters publics. `BaseEvent : INotification`, donc le projet Domain référence `MediatR.Contracts` (décision écrite). Une décision écrite précise que le template ne prescrit pas le DDD, et qu'un projet DDD devrait employer des repositories.

### 1.7 Mapping

**Dépôt A.** Écrit à la main, en classes statiques internes : domaine vers DTO, DTO vers réponse, agrégat vers ligne. La documentation range AutoMapper parmi les absences voulues. Aucun test d'architecture n'interdit AutoMapper.

**Dépôt B.** AutoMapper 16.2.0 : un profil imbriqué dans chaque DTO, `ProjectTo` en lecture, affectation manuelle en écriture. Un test appelle `AssertConfigurationIsValid`. Les issues qui proposaient de remplacer AutoMapper (Mapperly, mapping manuel) ont été fermées sans changement.

### 1.8 Autorisation

**Dépôt A.** Politique de repli `RequireAuthenticatedUser`. Les actions anonymes forment une liste fermée, tenue par un test. Une politique `Administrator` (par rôle) est posée sur 5 contrôleurs ; un test d'intégration vérifie 403 avec code pour un utilisateur ordinaire et 401 pour un anonyme. Chaque use case exige un appelant (`RequireUserId`, 401). Les rôles sont vérifiés dans l'hôte (attributs de contrôleur, ou politique sur les outils d'un hôte MCP), pas dans la couche application.

**Dépôt B.** `RequireAuthorization()` sur chaque groupe d'endpoints, sans politique de repli. Un attribut applicatif `[Authorize(Roles, Policy)]` posé sur la requête est lu par un behaviour, valable quel que soit l'hôte ; il est posé sur une requête sur 9. CORS : `AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()`. Un test d'autorisation (refus d'un anonyme).

### 1.9 Propriété des données

**Dépôt A.** Une décision écrite : « The owner is in every predicate », et un enregistrement d'autrui répond 404, jamais 403. Les lectures et les features sans agrégat filtrent par propriétaire dans la requête SQL :
```csharp
.Where(list => list.Id == id && list.OwnerId == ownerId.Value)
```
Pour les agrégats, le dépôt charge par id, puis le use case compare le propriétaire en mémoire et renvoie la même erreur de non-trouvé :
```csharp
.FirstOrDefaultAsync(list => list.Id == id, cancellationToken);              // repository
if (aggregate is null || aggregate.OwnerId != ownerId) return Result.Failure<TAggregate>(notFound);
```
Le générateur de A écrit ce même schéma pour une nouvelle feature à agrégat. La décision écrite présente ce schéma, « load the record by its id and then compare », comme celui qu'elle écarte. Des tests d'intégration vérifient que la liste d'autrui ne peut être ni lue, ni renommée, ni supprimée, ni recevoir d'élément.

**Dépôt B.** Aucune notion de propriétaire : pas de colonne propriétaire, et tout utilisateur authentifié lit et modifie tous les enregistrements. Le handler de suppression charge par id seul.

### 1.10 CQRS et accès aux données

**Dépôt A.** Des dossiers `Commands/` et `Queries/`. Les écritures passent par l'agrégat, un port `Repository` et `IUnitOfWork`. Les lectures passent par un port `Queries` qui projette sans charger l'agrégat, avec pagination offset et keyset. Quatre mots désignent les ports (Repository, Queries, Store, Table) ; aucun port n'expose `IQueryable` et aucun n'a plus de six opérations, ce que des tests vérifient.

**Dépôt B.** Des dossiers `Commands/` et `Queries/`. Lectures et écritures passent par `IApplicationDbContext`, qui expose `DbSet<TodoList>` et `DbSet<TodoItem>` ; la couche application référence EF Core, par décision écrite (pas de couche repository). Lecture par `ProjectTo`, sans pagination. Deux discussions publiques du dépôt (27 et 16 commentaires) portent sur ce choix.

### 1.11 Enregistrement des dépendances

**Dépôt A.** Chaque feature est appelée explicitement depuis le module applicatif ; ses use cases et validateurs sont ensuite trouvés par scan, sur le préfixe de namespace de la feature. Un test compose le conteneur. Les messages d'un test de convention disent « registration is explicit rather than scanned ».

**Dépôt B.** Scan de toute l'assembly applicative pour MediatR, FluentValidation et AutoMapper. Aucun test de composition.

---

## Axe 2. Architecture et frontières

### 2.1 Projets et références

**Dépôt A.** 14 projets source sur 4 couches (Domain, Application, Infrastructure, Presentation) et un client généré. Chaque couche a un projet `.Core` qui ne connaît aucune feature. L'authentification a ses propres projets d'application et d'infrastructure. L'infrastructure a un projet par capacité : persistance, e-mail, stockage, authentification, doubles en mémoire. L'API référence la couche application et cinq modules d'infrastructure. Les adaptateurs qui implémentent un port sont `internal` à leur module, ce qu'un test vérifie.

**Dépôt B.** 7 projets source : Domain, Application, Infrastructure, Web, Shared, ServiceDefaults et AppHost (Aspire).

| Projet | Références |
|---|---|
| Domain | paquet `MediatR.Contracts` |
| Application | Domain ; paquets Ardalis.GuardClauses, AutoMapper, FluentValidation, MediatR, EF Core |
| Infrastructure | Application, Shared |
| Web | Application, Infrastructure, ServiceDefaults |

Un endpoint de Web importe un namespace d'Infrastructure pour mapper l'API d'identité (`MapIdentityApi<ApplicationUser>()`).

### 2.2 Règles des frontières et tests d'architecture

**Dépôt A.** Un projet de tests d'architecture (xUnit v3, NetArchTest.Rules 1.3.2) de 195 `[Fact]/[Theory]`. Il couvre :
- les dépendances de couche (le domaine ne dépend de rien ; l'application ne dépend ni d'EF Core, ni d'ASP.NET Core, ni de MediatR) ;
- les dépendances de module (seul un hôte référence l'infrastructure ; aucun hôte n'en référence un autre ; un client ne passe que par l'API) ;
- l'isolation des features (une feature n'en atteint une autre que par ses ports et ses événements) ;
- la forme des ports, du domaine et des use cases, la surface HTTP, la composition du conteneur.

17 méthodes réparties sur 14 fichiers sont des contre-épreuves : elles montrent qu'une règle détecte une violation construite exprès. Les décisions écrites nomment le test qui les tient.
Essai mesuré : une référence de projet Domain → module d'infrastructure, qui ne crée pas de cycle, compile. Le test de dépendance de module échoue ensuite en 9 s ; le message nomme le `.csproj` fautif et la raison de la règle.

**Dépôt B.** Aucun test d'architecture ni bibliothèque de règles. Le graphe des `ProjectReference` est la seule contrainte.
Essai mesuré : ajouter le paquet EF Core au projet Domain ne produit ni erreur ni avertissement. Une référence qui crée un cycle échoue au restore avec `MSB4006 There is a circular dependency`, sans nom de règle. Une contribution ouverte ajoute un fichier de consignes pour agents qui décrit les frontières en prose.

### 2.3 Analyzers et sévérités

**Dépôt A.** `TreatWarningsAsErrors`, `AnalysisLevel latest-recommended`, `EnforceCodeStyleInBuild`. `.editorconfig` met en erreur les namespaces file-scoped, CS8618/CS8602 et le nommage `I*`/`T*` ; les champs privés `_camelCase` et `IDE0005` sont en warning, donc en erreur. La documentation interdit `#pragma`, `<NoWarn>` et la baisse de sévérité. Le dépôt contient pourtant :
- un `#pragma warning disable ASP0000` ;
- un `<NoWarn>CS1591;CS1573</NoWarn>` pour tout le dépôt ;
- un `<NoWarn>AV0029;AV0030</NoWarn>` dans le projet API ;
- trois règles (`CA2007`, `CA1848`, `CA1515`) à `none` pour tout le dépôt, chacune avec sa raison en commentaire.

Aucun test ne vérifie cette interdiction.

**Dépôt B.** `TreatWarningsAsErrors`, avec `NU1608` en exception dans la variante PostgreSQL. `.editorconfig` : 19 règles de nommage, toutes en `suggestion`, et aucune sévérité de diagnostic. Le seul analyzer tiers est `NUnit.Analyzers`, dans les projets de test.

### 2.4 Documentation d'architecture et code

**Dépôt A.** Deux pages parlent de « six authentication features », et le projet en compte 7. La décision sur la propriété des données et le code des agrégats divergent (§1.9). La table des codes d'erreur ne donne qu'un des deux statuts de `domain.invariantViolated` (§1.4).

**Dépôt B.** La décision sur les événements dit « after `SaveChangesAsync` », et l'intercepteur publie dans `SavingChanges` (§1.5). La décision sur EF Core dans l'application justifie le choix par la direction des dépendances ; un endpoint Web utilise un type d'Infrastructure (§2.1).
