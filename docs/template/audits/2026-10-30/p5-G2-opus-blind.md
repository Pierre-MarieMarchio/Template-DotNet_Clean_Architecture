# Dossier pour le juge : conventions et extensibilité de deux templates .NET Clean Architecture

Deux templates `dotnet new` pour .NET 10, appelés Dépôt A et Dépôt B. Les noms de projets, de namespaces et d'outils maison sont
masqués : `ProjA` et `ProjB` remplacent le nom du produit. Pour chaque point, A est décrit d'abord, B ensuite. Les mesures marquées
« persona » viennent d'un développeur simulé qui a fait les mêmes tâches sur un projet généré par chaque template.

---

## Axe 1. Conventions

### 1.1 Organisation des dossiers

**Dépôt A.** Par feature dans la couche Application, par nature dans les autres couches.
```
src/Application/TodoItems/Commands/CreateTodoItem/CreateTodoItem.cs
src/Domain/Entities/TodoItem.cs
src/Domain/Events/TodoItemCompletedEvent.cs
src/Infrastructure/Data/Configurations/TodoItemConfiguration.cs
src/Web/Endpoints/TodoItems.cs
```
Aucun test ne vérifie cette organisation. Le namespace d'une feature est `ProjA.Application.<Feature>` ; le persona a nommé une feature
`Projects`, ce qui a masqué une classe générée de même nom et cassé la compilation d'un projet de test (CS0234).

**Dépôt B.** Par feature dans chaque couche (`Common/` pour ce qui ne connaît aucune feature, `Features/<Feature>/` pour le reste),
avec quatre niveaux de dossiers :
```
| 1 | `Common/` or `Features/<Feature>/` | does it know a feature? |
| 2 | a concept: `UseCases/`, `Ports/`, `Model/`, `Access/`, `Rows/`, … | what role does it play? |
| 3 | a nature: `Commands/`, `Dtos/`, `Repositories/`, `Models/`, … | what is it? |
| 4 | a subject: `<Operation>/`, `<Port>/`, `<Event>/` | which one? |
```
Les mots autorisés par projet sont listés dans un dictionnaire d'un test d'architecture (NetArchTest + lecture du disque), vérifié dans
les deux sens ; un projet absent du dictionnaire fait échouer un test. Exemple d'entrée :
```csharp
["Src/Domain/ProjB.Domain"] = ["Model/Entities", "Model/Events", "Model/ValueObjects", "Repositories"],
```
La raison est donnée dans une page d'architecture ; aucune décision d'architecture (ADR) n'est consacrée à cette arborescence.

### 1.2 Nommage

**Dépôt A.**
- Un fichier par use case, sans suffixe, contenant la commande et son handler :
  ```csharp
  public record CreateTodoItemCommand : IRequest<int> { ... }
  public class CreateTodoItemCommandHandler : IRequestHandler<CreateTodoItemCommand, int> { ... }
  ```
- Dossier de projet court, nom complet dans le `.csproj` :
  ```xml
  <!-- src/Application/Application.csproj -->
  <RootNamespace>ProjA.Application</RootNamespace>
  <AssemblyName>ProjA.Application</AssemblyName>
  ```
- Routes `/api/{NomDeClasse}`, déduites par réflexion des classes qui implémentent une interface de groupe d'endpoints.
- `.editorconfig` : 19 règles de nommage, toutes à `severity = suggestion`.
- Chemin de fichier suivi le plus long : 95 caractères ; moyenne des `.cs` : 53.

**Dépôt B.**
- Un type public par fichier, nommé comme le fichier, vérifié par test.
- Le suffixe d'un type désigne son dossier (table de 22 lignes : `…UseCase`/`…Command`/`…Validator` dans `UseCases/<Op>/`, `…Dto` dans
  `Model/Dtos/`, `…Record` dans `Rows/Models/`, etc.), vérifié par test ; mots refusés : `Manager`, `Utils`, `Processor`, `Handler` seul.
- Dossier de projet au nom complet (`Src/Application/ProjB.Application/`), namespace = dossier, vérifié par test.
- Routes `api/v{version}/<kebab-pluriel>`.
- Chemin suivi le plus long sous `Tests/` : 176 caractères
  (`Tests/Application/ProjB.Application.Auth.UnitTests/Features/ExternalLogins/UseCases/Commands/SignInWithExternalProvider/SignInWithExternalProviderCommandValidatorTests.cs`) ;
  moyenne des `.cs` : 103. Une issue ouverte du dépôt signale qu'un projet créé dans un dossier profond ne peut pas être commité sous
  Windows sans `core.longpaths`.

### 1.3 Emplacement des tests

**Dépôt A.** Un projet de test par couche et par type : `Domain.UnitTests`, `Application.UnitTests`, `Application.FunctionalTests`,
`Infrastructure.IntegrationTests` (vide), `Web.AcceptanceTests`. Un fichier par use case, sans dossier d'opération :
`tests/Application.FunctionalTests/TodoItems/Commands/CreateTodoItemTests.cs`, méthodes `ShouldRequireMinimumFields`. NUnit ;
`global using NUnit.Framework` présent dans un projet de test, absent d'un autre.

**Dépôt B.** Un projet de test par projet source (14 pour 14), rangé par couche, qui reprend les dossiers source y compris le dossier
d'opération ; plus des projets `Architecture`, `Integration`, `EndToEnd`, `Load`. xUnit v3, méthodes `Sujet_Comportement`. Une page
liste au moins 17 règles d'écriture de test. La correspondance « un projet de test par projet source » n'est vérifiée par aucun test.

### 1.4 Style

| Point | Dépôt A | Dépôt B |
|---|---|---|
| Sévérité des règles de style | `suggestion` ou `silent` ; `TreatWarningsAsErrors` seul | `error` ou `warning` ; `EnforceCodeStyleInBuild`, `AnalysisLevel latest-recommended`, `TreatWarningsAsErrors` |
| Constructeurs | préférence `primary_constructors:suggestion` ; 35 champs injectés par constructeur classique, 1 constructeur primaire | constructeurs primaires dans les use cases |
| Encodage des `.cs` | pas de règle `charset` ; 70 fichiers avec BOM, 40 sans | `charset = utf-8-bom`, vérifié par `dotnet format --verify-no-changes` ; 1 774 sur 1 774 avec BOM |
| Commentaires `//` de prose dans le code source | 43 lignes (ex. `// Add services to the container.`) | 0 ligne, vérifié par un outil du dépôt |
| `#pragma warning disable` | 1 (CS1998) | 1 (ASP0000), alors qu'une règle écrite du dépôt l'interdit |

---

## Axe 2. Extensibilité

### 2.0 Mesures du persona (mêmes tâches sur chaque projet généré)

| Tâche | Dépôt A | Dépôt B |
|---|---|---|
| Feature complète | 5 fichiers écrits par l'outil, ~380 lignes à la main, 2 listes à compléter, compile au 2e essai | 54 fichiers écrits par l'outil, ~215 lignes à la main, 6 listes à compléter (chacune vérifiée par un test) |
| Use case dans une feature existante | 2 fichiers par l'outil, ~110 lignes, 1 test | 8 fichiers par l'outil, ~350 lignes, 10 tests |
| Second host | template `worker` du SDK, 4 listes à la main, échec au démarrage sans `ValidateOnBuild = false` | serveur MCP : 1 commande, 83 fichiers, 0 ligne à la main |
| Seconde app web | copie de 29 fichiers d'une autre génération | 1 commande, puis 10 éditions imprimées (7 non vérifiées par un test) |
| Retirer une feature | `grep` + `git rm` | outil qui produit un rapport ; suppression automatique seulement si le squelette n'a pas été modifié |

### 2.1 Ajouter une feature ou un use case

**Dépôt A.** Un item template produit un fichier par use case :
```csharp
public async Task<TReturnType> Handle(ProjAUseCaseCommand request, CancellationToken cancellationToken)
{
    throw new NotImplementedException();
}
```
Options : nom de feature, type (command ou query), type de retour, namespace parent. Le reste est enregistré par scan de l'assembly :
```csharp
builder.Services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());
builder.Services.AddMediatR(cfg => { cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly()); ... });
```
Les endpoints sont découverts par réflexion. À la main : un `DbSet` dans l'interface du contexte et un dans le contexte.

**Dépôt B.** Trois scripts dans le projet : un générateur de feature (options : accès propriétaire ou partagé, sort des données à la
suppression d'un compte, opérations, forme record ou agrégat, concurrence), un générateur de use case, un outil de retrait qui compare
les empreintes d'un manifeste. Le générateur de feature n'édite que les fichiers qu'il crée et imprime les autres éditions :
```
The solution does not compile until edit 1 is made, and the feature does not compose until all are; nothing here edited a file it does not own.
Six edits, each by hand, each somewhere an absence should be visible:
```
Une page liste 12 « listes fermées » qu'un ajout rejoint, chacune avec le test qui échoue tant qu'elle n'est pas complétée.
L'enregistrement est appelé par feature, et le scan est borné au dossier de la feature :
```csharp
services.AddFeature(_todoLists);   // scan des validateurs et use cases sous Features.TodoLists
```
Un use case sans interface nommée, ou avec deux, fait lever l'enregistrement.

### 2.2 Ajouter un module d'infrastructure

**Dépôt A.** Un projet `Infrastructure` unique : base, interceptors, Identity et schémas d'authentification web.
```csharp
builder.Services.AddAuthentication(options => { options.DefaultScheme = IdentityConstants.ApplicationScheme; ... })
    .AddIdentityCookies();
builder.Services.AddIdentityCore<ApplicationUser>() ... .AddSignInManager() ...
```
Aucune page ni règle ne dit où placer un nouveau service (une discussion publique demandant où placer Redis n'a pas de réponse).

**Dépôt B.** Un projet par capacité (Persistence, Auth, Email, Storage, InMemory), une méthode d'enregistrement chacun. Deux tests
interdisent qu'un module référence un autre module. Ajouter un projet : une entrée dans le dictionnaire de dossiers, la solution, une
ligne `COPY` du Dockerfile, un projet de test, les planchers d'un test de dépendances. Message du test quand un projet manque :
```
Give it an entry in each — empty when its features hold their files side by side, as Email and InMemory do — and write the words
into docs/reference/project-layout.md's folder map.
```

### 2.3 Providers

**Dépôt A.** Base choisie à la génération par des directives `#if` dans le code (7 fichiers) :
```csharp
#if (UsePostgreSQL)
            options.UseNpgsql(connectionString);
#elif (UseSqlServer)
            options.UseSqlServer(connectionString);
#else
            options.UseSqlite(connectionString);
#endif
```
Pas de module de mail, pas de stockage de fichiers, une seule authentification (Identity locale).

**Dépôt B.**
- Base : un provider par projet, choisi à la génération ; PostgreSQL dans l'arbre, les autres en fichiers de remplacement (5 fichiers
  sous `Src` pour SQL Server, 7 pour SQLite). Une décision écrite indique qu'un projet change de provider en régénérant et en reportant
  son code.
- Mail : deux transports choisis par configuration, valeur inconnue refusée :
  ```csharp
  if (string.Equals(transport, EmailOptions.SmtpTransport, ...)) { services.AddScoped<IEmailSender, MailKitEmailSender>(); return; }
  if (string.Equals(transport, EmailOptions.PostmarkTransport, ...)) { AddPostmarkTransport(services, configuration); return; }
  throw new InvalidOperationException($"'{EmailOptions.SectionName}:Transport' is '{transport}', which names no transport ...");
  ```
- Stockage : S3 ou disque ; une classe de tests abstraite (`public abstract class FileContentStoreContract`) est héritée par les deux
  adaptateurs et par le double en mémoire.
- Authentification : locale, externe (OIDC) ou aucune, à la génération.

### 2.4 Ajouter un host

**Dépôt A.** Pas d'outil. Un AppHost Aspire déclare les ressources (`builder.AddProject<Projects.Web>(...)`), et un projet commun ajoute
télémétrie et santé (`AddServiceDefaults()`). Le persona a ajouté un worker : `AddInfrastructureServices` y enregistre l'authentification
web et le démarrage échoue :
```
Unable to resolve service for type 'Microsoft.AspNetCore.Routing.EndpointDataSource' while attempting to activate 'AuthorizationPolicyCache'
```

**Dépôt B.** Règle écrite « each host composes itself », tenue par deux tests (aucun host ne référence un autre host ; seul un host
référence un module d'infrastructure). Le worker compose feature par feature et module par module :
```csharp
builder.Services.AddTodoLists();
builder.Services.AddPersistenceModule(builder.Configuration);
builder.Services.AddEmailModule(builder.Configuration);
builder.Services.AddWorkerCallerIdentity();
```
Un projet partagé par les hosts porte observabilité, HTTP sortant et jobs. Après génération, un sous-template ajoute un portail Blazor
Server, un serveur MCP, une app Avalonia ou WPF, et une tâche applique les éditions. Le worker ne s'ajoute qu'à la génération (décision
écrite ; une page décrit le déplacement à la main). La tâche de migration ne vise que le contexte métier. Orchestration locale par
Docker Compose.

### 2.5 Remplacer une brique

**Dépôt A.**
- MediatR : 22 fichiers utilisent `IRequest`, `ISender`, `INotification` ou un behaviour ; le domaine en dépend
  (`public abstract class BaseEvent : INotification`).
- EF Core : l'interface de l'application expose des `DbSet` ; 13 fichiers l'utilisent.
- Comportement transverse sur toutes les requêtes : une ligne.
  ```csharp
  cfg.AddOpenBehavior(typeof(ValidationBehaviour<,>));
  ```

**Dépôt B.**
- L'application ne référence ni EF Core, ni ASP.NET Core, ni MediatR, ni MailKit, vérifié par test :
  ```csharp
  private static readonly string[] _forbiddenInApplication = [ ..., "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", ..., "MailKit", "MediatR" ];
  ```
- Un adaptateur se remplace derrière un port.
- Pas de pipeline : chaque use case est enregistré sous sa propre interface nommée ; une décision écrite exclut médiateur et behaviours
  et indique quand la réviser. Aucune page ne montre un décorateur appliqué à tous les use cases.

### 2.6 Ce qu'une équipe peut changer

**Dépôt A.** Aucune règle de structure n'est exécutable : le persona a ajouté une dépendance du domaine vers EF Core, sans erreur ni
avertissement. Trois décisions écrites sont dans `docs/decisions/` et partent dans le projet généré sur la branche
principale, sans règle de numérotation pour l'équipe.

**Dépôt B.** Une convention changée fait échouer un test, dont le message indique le fichier à modifier. Règle écrite : une décision et
le test qui la tient changent ensemble. Les décisions de l'équipe sont numérotées à partir de 1000. Les règles occupent un projet de
tests d'architecture d'environ 8 900 lignes.

### 2.7 Demandes publiques sur ces axes (état actuel)

Dépôt A :

| Demande | État dans A | Dans B |
|---|---|---|
| Générer sans les exemples ; sans Identity | ouverte (7 votes), sur la feuille de route | option « sans exemples », authentification externe ou aucune |
| Blazor | ouverte (26 votes) | app Blazor WebAssembly, portail Blazor Server |
| Vue | ouverte (7 votes) | app Vite + Vue |
| Worker | fermée, aucun worker livré | option worker à la génération |
| Plusieurs présentations sur la même application | discussion sans réponse | projet partagé par les hosts, sous-template, règles testées |
| Tenir les références de projets par test | réponse : « utiliser ArchUnitNET » | tests de dépendances livrés |
| Alternative à Identity | ouverte | authentification externe OIDC |

Dépôt B (issues ouvertes, aucune n'a de commentaire) :

| Demande | État dans B | Dans A |
|---|---|---|
| Chemins générés jusqu'à 176 caractères, commit impossible sous Windows depuis un dossier profond | ouverte | chemin le plus long : 95 caractères |
| La tâche qui joint une app web refuse une app WebAssembly qui a créé son propre espace de travail | ouverte | pas d'outil d'ajout d'app web |
| Un outil de projet installé (`dotnet <outil>`) qui ajoute des parties, le lanceur ne faisant que créer | ouverte, décisions au statut « Proposed » | pas d'outil de projet |
