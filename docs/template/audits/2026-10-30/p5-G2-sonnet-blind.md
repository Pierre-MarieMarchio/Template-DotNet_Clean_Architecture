# Dossier anonymisé : conventions et extensibilité

Deux dépôts de solution .NET 10 en couches (Domain, Application, Infrastructure, présentation), notés Dépôt A et Dépôt B. Chaque axe décrit A puis B, dans le même ordre, avec des extraits courts. Les chemins sont relatifs à la racine de chaque dépôt ; les noms de projets sont remplacés par ProjA et ProjB ; les outils propres à chaque dépôt sont désignés par « l'outil de A » ou « l'outil de B ». Aucun jugement n'est porté ici.

Mesures relevées par commande :

| | Dépôt A | Dépôt B |
|---|---|---|
| Projets de test | 6 | 18 |
| Plus long chemin d'un fichier `.cs` ou de test (caractères, relatif à la racine) | 88 | 176 |
| Fichiers `.cs` de `src/` avec BOM | 70 sur 110 | tous (règle d'éditeur) |
| Lignes de commentaire `//` de prose dans le code de production | 43 | 0 |
| Classes avec constructeur primaire (code de production) | 2 | 208 |
| Règles de nommage dans `.editorconfig` | 19, toutes à la sévérité `suggestion` | 4, sévérités `error` ou `warning` |

---

## Axe 1. Conventions

### 1.1 Nommage et règles d'analyse

**Dépôt A.** Dix-neuf règles de nommage (types, interfaces, paramètres de type, méthodes, propriétés, événements, locaux, paramètres, champs publics, privés, constantes, énumérations, fonctions locales), toutes à `suggestion` ; la déclaration d'espace de noms de fichier est à `silent`. Le workflow de CI lance restore, build et test ; aucune étape de format. `TreatWarningsAsErrors` est actif. Un `#pragma warning disable CS1998` figure dans une requête d'exemple.

```
# .editorconfig:196-268 (extrait)
dotnet_naming_rule.types_and_namespaces_should_be_pascalcase.severity = suggestion
# .editorconfig:181
csharp_style_namespace_declarations = file_scoped:silent
# .github/workflows/build.yml:55-66
dotnet restore / dotnet build --no-restore --configuration Release / dotnet test --no-build --configuration Release
```

**Dépôt B.** Quatre règles de nommage : interfaces préfixées `I`, types en PascalCase, paramètres de type préfixés `T` (ces trois à `error`), champs privés en `_camelCase` (`warning`). L'espace de noms de fichier est à `error`, l'emplacement des `using` à `warning`. L'étape de vérification de l'outil de B lance `dotnet format --verify-no-changes`. `TreatWarningsAsErrors` est actif. Un `<NoWarn>` global (`CS1591;CS1573`) et un `#pragma warning disable ASP0000` figurent dans le code alors que le fichier `.editorconfig` annonce l'absence de `NoWarn` global.

```
# .editorconfig:30 et :63-65
csharp_style_namespace_declarations = file_scoped:error
dotnet_naming_rule.interfaces_start_with_i.severity = error
# Directory.Build.props:27
<NoWarn>$(NoWarn);CS1591;CS1573</NoWarn>
# outil de B, étape « format »
Step("dotnet", "format", solution, "--verify-no-changes", "--verbosity", "normal");
```

### 1.2 Organisation : par feature ou par couche

**Dépôt A.** Par feature dans Application ; par couche dans Domain, Infrastructure et la présentation. Un use case est un dossier à deux fichiers : un fichier portant la commande et son handler, un validateur. Les projets se nomment `Application`, `Domain`, `Infrastructure` ; le préfixe est dans l'espace de noms. Les méthodes d'enregistrement sont déclarées dans l'espace de noms `Microsoft.Extensions.DependencyInjection`. Aucun test ne vérifie l'arbre.

```
src/Application/TodoItems/Commands/CreateTodoItem/CreateTodoItem.cs
src/Application/TodoItems/Commands/CreateTodoItem/CreateTodoItemCommandValidator.cs
src/Domain/Entities/TodoList.cs
src/Infrastructure/Data/Configurations/TodoListConfiguration.cs
src/Web/Endpoints/TodoLists.cs
# src/Application/DependencyInjection.cs:5
namespace Microsoft.Extensions.DependencyInjection;
```

**Dépôt B.** Par feature dans chaque couche (`Common/` pour ce qui ne connaît aucune feature, `Features/<Feature>/` pour le reste), avec un vocabulaire de dossiers fermé. Un use case est un dossier à quatre fichiers (commande, interface, validateur, classe). Des tests d'architecture vérifient les mots de dossier, la racine de chaque projet, l'égalité espace de noms = dossier, l'absence de dossier vide. Les projets se nomment `ProjB.Application`, `ProjB.Domain`, etc. ; les extensions d'enregistrement sont dans l'espace de noms de leur projet.

```
Src/Application/ProjB.Application/Features/TodoLists/UseCases/Commands/CreateTodoList/{CreateTodoListCommand,ICreateTodoListUseCase,CreateTodoListUseCase,CreateTodoListCommandValidator}.cs
Src/Domain/ProjB.Domain/Features/TodoLists/Model/Entities/TodoList.cs
Src/Infrastructure/ProjB.Infrastructure.Persistence/Features/TodoLists/Rows/Configurations/TodoListRecordConfiguration.cs
# Tests/Architecture/.../Rules/LayoutConventionTests.cs
EveryCommonFolder_IsNamedFromItsProjectsVocabulary / EveryFeatureFolder_IsNamedFromItsLayersVocabulary / EverySourceFile_DeclaresTheNamespaceOfItsFolder / NoFolderInTheSourceTree_IsEmpty
```
Pour la feature TodoLists, Domain et Persistence comptent 24 fichiers `.cs`.

### 1.3 Emplacement et nommage des tests

**Dépôt A.** Six projets de test par type : tests unitaires du domaine, tests unitaires de l'application, tests fonctionnels, tests d'intégration d'infrastructure (le projet ne contient qu'un fichier `GlobalUsings.cs`), tests d'acceptation, hôte de test. Les tests fonctionnels sont rangés par feature et par use case ; ils envoient la commande par le médiateur et n'appellent pas HTTP. NUnit. Les noms décrivent l'attendu.

```
tests/Application.FunctionalTests/TodoLists/Commands/CreateTodoListTests.cs:33
[Test] public async Task ShouldCreateTodoList()
tests/Domain.UnitTests/ValueObjects/ColourTests.cs
tests/Application.UnitTests/Common/Mappings/MappingTests.cs
```

**Dépôt B.** Un projet de test par projet source, dont les dossiers reproduisent ceux de la source, plus des projets pour l'architecture, le bout-en-bout, l'intégration HTTP sur une vraie base et la charge. xUnit v3. Les noms sont de la forme `Sujet_Comportement`. La page de documentation de l'arborescence indique que chaque règle qu'elle énonce est tenue par un test ; la règle « un projet de test par projet source » ne figure dans aucun test d'architecture.

```
docs/reference/project-layout.md:196
Tests/   one test project per source project, mirroring its folders, plus Architecture/, EndToEnd/, Integration/ and Load/
Tests/Application/ProjB.Application.UnitTests/Features/TodoLists/UseCases/Commands/CreateTodoList/CreateTodoListUseCaseTests.cs:28
[Fact] public async Task AnAnonymousCaller_IsRefused()
```

### 1.4 Style de fichier, un type par fichier, commentaires

**Dépôt A.** Un fichier de use case déclare plusieurs types et porte le nom de l'opération. Encodage : BOM présent dans 70 fichiers `.cs` de `src/`, absent de 40. 43 lignes de commentaire `//` de prose dans le code de production, dont une partie reprise de la documentation d'un composant d'orchestration. Aucune règle outillée sur les commentaires.

```
src/Application/TodoItems/Commands/CreateTodoItem/CreateTodoItem.cs:6,13
public record CreateTodoItemCommand : IRequest<int>
public class CreateTodoItemCommandHandler : IRequestHandler<CreateTodoItemCommand, int>
src/ServiceDefaults/Extensions.cs:13-15
// Adds common ... services: service discovery, resilience, health checks, and OpenTelemetry.
```

**Dépôt B.** UTF-8 avec BOM et fins de ligne LF imposés par `.editorconfig` ; un type public par fichier, nommé comme le fichier, vérifié par un test d'architecture ; aucun commentaire `//` de prose dans le code de production, vérifié par un outil de contrôle (1 396 lignes) ; les raisons durables vont dans des documents de décision. La règle sur les commentaires est écrite dans le fichier d'instructions du dépôt.

```
.editorconfig:5,24-26
end_of_line = lf ; [*.cs] charset = utf-8-bom
Tests/Architecture/.../Rules/LayoutConventionTests.cs:544
EverySourceFile_DeclaresOnePublicType_NamedForTheFile
grep -rnE '^\s*//[^#/]' Src (hors code généré) : 0 résultat
```

### 1.5 Style déclaré et style écrit : constructeurs primaires

**Dépôt A.** `.editorconfig` déclare la préférence pour les constructeurs primaires à `suggestion` ; deux classes en utilisent un dans le code de production ; les autres déclarent un champ `_context` et un constructeur classique.

```
.editorconfig:184
csharp_style_prefer_primary_constructors = true:suggestion
src/Domain/ValueObjects/Colour.cs:3
public class Colour(string code) : ValueObject
src/Application/TodoLists/Commands/CreateTodoList/CreateTodoList.cs:15-20
private readonly IApplicationDbContext _context;  public CreateTodoListCommandHandler(IApplicationDbContext context) { _context = context; }
```

**Dépôt B.** `.editorconfig` déclare la préférence à `false:suggestion` ; 208 classes du code de production déclarent un constructeur primaire.

```
.editorconfig:48
csharp_style_prefer_primary_constructors = false:suggestion
Src/Application/ProjB.Application/Features/TodoLists/UseCases/Commands/CreateTodoList/CreateTodoListUseCase.cs:13
public sealed class CreateTodoListUseCase(ITodoListRepository repository, IUnitOfWork unitOfWork, ICurrentUser currentUser, ...) : ICreateTodoListUseCase
```

### 1.6 Où vivent les conventions : documentation

**Dépôt A.** Trois documents de décision dans `docs/decisions/`. Le projet généré contient un README d'une soixantaine de lignes. Des README par couche (`src/Application`, `src/Domain`, `src/Infrastructure`, `src/Web`) ont existé dans l'historique et ont été supprimés par un commit de mars 2026 ; aucun ne figure sur la branche principale. Un site de documentation hors dépôt est cité par le README.

**Dépôt B.** 88 pages sous `docs/` (architecture, guides, référence), dont une page « où va un fichier, comment il se nomme » conditionnée par les options de génération, et 28 documents de décision. Un fichier d'instructions à la racine et un seul fichier d'instructions de dossier (pour le dossier web). Aucun README sous `Src/Domain`, `Src/Application`, `Src/Infrastructure`.

### 1.7 Format de la solution

**Dépôt A.** Fichier de solution `.slnx` (29 lignes).

**Dépôt B.** Fichier de solution `.sln` pour la solution principale ; un second projet du dépôt (le lanceur) utilise un `.slnx`. Onze fichiers citent le nom du `.sln` (documentation, workflows, outil de B, tests).

---

## Axe 2. Extensibilité

### 2.1 Ajouter un use case à une feature existante

**Dépôt A.** Un gabarit d'élément (`dotnet new <gabarit-A> --name CreateTodoList --feature-name TodoLists --usecase-type command --return-type int`) écrit un fichier avec commande, validateur et handler dont le corps lève `NotImplementedException`. Il doit être lancé depuis `src/Application` ; ailleurs il écrit un dossier à un emplacement différent de celui attendu, sans erreur. Rien à enregistrer : MediatR et FluentValidation scannent l'assembly.

```
src/Application/DependencyInjection.cs:14,16-17
builder.Services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());
cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly());
README-template.md:40
dotnet new <gabarit-A> --name CreateTodoList --feature-name TodoLists --usecase-type command --return-type int
```
Mesure d'un essai : deux use cases ajoutés (archivage, recherche), compilation au premier essai ; ni DTO, ni route, ni test générés.

**Dépôt B.** Une commande de l'outil de B (`new-use-case <Features> <UseCase> --kind command|query [--input id|none]`) écrit quatre fichiers (commande, interface, validateur, use case) et un test, ajoute les fichiers au manifeste de la feature, imprime l'action de contrôleur à coller. Rien à enregistrer : un scan par dossier de feature trouve le use case et son validateur. La commande refuse une feature inexistante, une feature de forme simple, un use case déjà présent. `--input` accepte `id` ou `none`.

```
docs/guides/adding-a-feature.md:153
Nothing is registered by hand: `AddFeature` in `ApplicationModule` finds the use case and its validator by folder.
Src/Application/ProjB.Application/ApplicationModule.cs:156-165
services.AddValidatorsFromAssembly(typeof(ApplicationModule).Assembly, filter: scan => IsInVertical(scan.ValidatorType, vertical), ...); services.AddUseCases(TypesIn(vertical));
Tests/Architecture/.../Rules/UseCaseConventionTests.cs:58
"... registration is explicit rather than scanned. ..."
```
Mesure d'un essai : deux use cases ajoutés (archivage, recherche par nom) en une dizaine de minutes ; trois tours de build ; pour la recherche par texte, quatre fichiers réécrits à la main.

### 2.2 Ajouter une feature (entité, persistance, route, documentation)

**Dépôt A.** Aucun outil de feature. À la main : l'entité, sa configuration, un `DbSet` dans l'interface de contexte et dans le contexte, le DTO et le profil AutoMapper, un groupe d'endpoints découvert par réflexion. Dans un essai, une feature nommée `Projects` a fait échouer la première compilation : le dossier crée un espace de noms qui masque une classe `Projects` générée par le composant d'orchestration, utilisée par la configuration des tests.

```
src/Application/Common/Interfaces/IApplicationDbContext.cs:7,9
DbSet<TodoList> TodoLists { get; } ... DbSet<TodoItem> TodoItems { get; }
src/Infrastructure/Data/ApplicationDbContext.cs:14,16
public DbSet<TodoList> TodoLists => Set<TodoList>();
src/Web/Infrastructure/WebApplicationExtensions.cs:14,24
assembly.GetExportedTypes()... type.GetMethod(nameof(IEndpointGroup.Map))!.Invoke(null, [group]);
```

**Dépôt B.** L'outil de B (`new-feature <Features> <Feature> --access owner|shared [--deletion ...] [--model record|aggregate] [--concurrency none|etag]`) écrit un squelette dans chaque couche, un manifeste (SHA-256 des fichiers) et la page de documentation de la feature. Il imprime ensuite six modifications à faire à la main (contexte, deux modules de composition, `Program.cs`, deux compositions de test), onze lorsque la feature est la première à avoir un propriétaire, puis la migration et deux lignes de documentation. Douze listes fermées, chacune associée à un test qui échoue tant qu'elle n'est pas complétée, sont décrites dans le guide.

```
Tools/<outil de B>:1227-1229
"The solution does not compile until edit 1 is made ... nothing here edited a file it does not own."
"Six edits, each by hand, each somewhere an absence should be visible:"
docs/guides/adding-a-feature.md:173-197
« The closed lists a feature joins » (HostComposition, ErrorCodeTableTests, PersistenceModelTests, LayoutConventionTests, DomainEventTests, ...)
```
Mesure d'un essai : 54 fichiers écrits en 0,7 s ; environ 215 lignes écrites à la main ; premier gate non vert (`PendingModelChangesTests` en attente de migration, `format`).

### 2.3 Ajouter un host (worker, serveur MCP, portail, application de bureau)

**Dépôt A.** Aucun outil. Un host de plus est un projet écrit à la main. La méthode d'enregistrement de l'infrastructure déclare aussi les cookies, Identity et le sign-in. Dans un essai avec un worker issu du modèle du SDK, l'exécution a échoué à l'activation d'un service de politique d'autorisation (`EndpointDataSource` introuvable) ; la validation du conteneur à la construction a été désactivée pour continuer. Une seconde application web a été ajoutée en copiant un dossier de front et en déclarant une application JavaScript dans l'hôte d'orchestration.

```
src/Infrastructure/DependencyInjection.cs:48-72
builder.Services.AddAuthentication(...).AddIdentityCookies(); ... AddIdentityCore<ApplicationUser>() ... AddSignInManager()
```

**Dépôt B.** Option `--presentation` (api, blazor-server, mcp, avalonia, wpf, worker), item template de présentation, tâches `presentation-scaffold` et `presentation-join` de l'outil de B. Des tests d'architecture vérifient qu'aucun host n'en référence un autre et que chacun compose la couche application. Mesure d'un essai avec un serveur MCP : 30 s d'outil, 83 fichiers ajoutés, 31 fichiers existants modifiés par l'outil, tests du host fournis ; la migration du contexte d'authentification et la réécriture de la référence OpenAPI n'ont pas été faites par l'outil et ont fait échouer deux tests ensuite. Pour une application web vite-react : 58 fichiers, dix étapes imprimées dont sept sans test.

```
Tests/Architecture/.../Rules/ModuleDependencyTests.cs:403,421
NoHost_ReferencesAnotherHost ("Each host composes itself.") / EveryHost_ComposesTheApplicationLayer
```

### 2.4 Changer de provider, remplacer une brique

**Dépôt A.** La base se choisit à la génération par des directives conditionnelles dans le code ; trois fournisseurs (SQLite, SQL Server, PostgreSQL). Matrice de génération de 3 fronts x 3 bases construite et testée par un workflow. L'application référence EF Core et expose les `DbSet` par l'interface de contexte. Pas de module d'e-mail ni de stockage de fichiers. Une demande ouverte propose MySQL (3 commentaires, « not currently planned »).

```
src/Infrastructure/DependencyInjection.cs:26-33
#if (UsePostgreSQL) options.UseNpgsql(connectionString); #elif (UseSqlServer) options.UseSqlServer(connectionString); #else options.UseSqlite(connectionString);
.github/workflows/test-templates.yml:18-20
client-framework: [angular, react, none]   database: [sqlite, sqlserver, postgresql]
```

**Dépôt B.** Un fournisseur de base par projet, choisi à la génération ; PostgreSQL est compilé par défaut, SQLite et SQL Server sont des recouvrements de fichiers entiers ; MySQL est refusé à la génération bien que listé dans l'aide de la commande de création. Le changement de fournisseur après génération n'est pas un réglage. L'e-mail (SMTP ou Postmark) et le stockage de fichiers (S3 ou dossier local) sont choisis par configuration derrière un port ; un troisième transport d'e-mail se code en modifiant la méthode `AddTransport`. Un port compte au plus six opérations (test d'architecture) ; les adaptateurs sont internes à leur module.

```
Src/Infrastructure/ProjB.Infrastructure.Email/EmailModule.cs:39-60
string? configured = configuration[$"{EmailOptions.SectionName}:Transport"]; if (... SmtpTransport) {...} if (... PostmarkTransport) {...} throw new InvalidOperationException(...)
Tests/Architecture/.../Rules/PortConventionTests.cs:17
private const int _maximumOperationsPerPort = 6;
.template.config/variants/{database-sqlite,database-sqlserver}/
```
Matrice de recette : 25 jambes.

### 2.5 Comportement transversal : pipeline

**Dépôt A.** Des behaviours MediatR enregistrés en une ligne chacun, dans l'ordre voulu ; le pipeline HTTP est écrit dans le `Program.cs` du projet, que l’équipe modifie.

```
src/Application/DependencyInjection.cs:19-22
cfg.AddOpenBehavior(typeof(UnhandledExceptionBehaviour<,>)); cfg.AddOpenBehavior(typeof(AuthorizationBehaviour<,>)); cfg.AddOpenBehavior(typeof(ValidationBehaviour<,>)); cfg.AddOpenBehavior(typeof(PerformanceBehaviour<,>));
src/Web/Program.cs:25-49
app.UseHttpsRedirection(); app.UseCors(...); app.UseFileServer(); app.MapOpenApi(); app.UseExceptionHandler(...); ...
```

**Dépôt B.** Aucun behaviour : validation, journal et commit sont écrits dans chaque use case. Le pipeline HTTP est installé par un seul appel (`UseHttpPipeline()`) dans un ordre vérifié par test ; la documentation indique qu'un host ne peut pas insérer d'étape au milieu, et un test impose que cet appel soit le premier middleware du host. Les use cases sont enregistrés par une méthode centrale (`AddUseCases`) sous leur interface nommée. Aucune page ne décrit comment entourer tous les use cases d'un comportement commun ; le document de décision correspondant mentionne un décorateur dans sa clause de réexamen.

```
docs/architecture/request-pipeline.md:27
A host cannot insert a step in the middle. `RequestPipelineTests` holds the order: ...
Tests/Architecture/.../Rules/RequestPipelineTests.cs:63
TheHostInstallsTheHttpPipeline_BeforeAnyMiddlewareOfItsOwn
```

### 2.6 Retirer ce que l'on ne veut pas (exemples, authentification, front)

**Dépôt A.** Deux options de génération (front : Angular, React ou aucun ; base de données). Pas d'option pour exclure les exemples ni Identity ; deux demandes ouvertes ou closes en doublon l'évoquent, l'une est notée « on the roadmap ». Mesure d'un essai : retirer l'exemple météo = 17 fichiers supprimés et 7 modifiés à la main, guidé par `grep` ; retirer une feature ajoutée = 25 fichiers et 7 éditions, la migration retirée par `dotnet ef migrations remove`. Aucun outil de retrait.

**Dépôt B.** Options de génération `--examples all|notes|none`, `--auth local|external|none`, `--files`, `--web` (7 valeurs), `--presentation`, avec refus des combinaisons impossibles. Un projet `--auth none --examples none --files false` est généré en 5,5 s, son bootstrap prend 50 s, 798 tests passent. L'outil de B fournit un retrait de feature : un rapport sans écriture ; avec `--apply`, suppression d'un squelette resté intact (29 fichiers en une commande) ; refus dès qu'un fichier a été édité, le retrait est alors manuel (une dizaine à une vingtaine de fichiers, 25 minutes pour un exemple), guidé par des tests d'architecture qui échouent.

```
docs/guides/removing-features.md:21-25
remove-feature <Features>   # reports, writes nothing  /  remove-feature <Features> --apply   # deletes an untouched skeleton
```

### 2.7 Modifier l'outillage livré avec le projet

**Dépôt A.** Le projet généré ne contient pas d'outillage : le gabarit d'élément est installé avec le paquet. Les scripts `build/*.ps1` sont exclus du paquet. Personnaliser le gabarit suppose de forker le paquet. Un essai où une dépendance interdite est ajoutée au domaine : EF Core ajouté au projet Domain, « 0 erreur, 0 signal » ; une référence de projet circulaire échoue à la restauration avec un message qui ne cite pas la règle.

**Dépôt B.** Le dossier `Tools/` fait partie de l'arbre généré : 14 980 lignes de C# réparties en neuf fichiers (le générateur de feature en compte 5 891, l'outil de tâches 2 134). Un document de décision au statut « Proposed » décrit le remplacement de ces fichiers par un outil .NET épinglé et nomme l'état actuel : chaque équipe garde un fork de 11 600 lignes ; le code des générateurs est écrit comme des chaînes, modifiable seulement en éjectant la commande entière. Le manifeste de chaque feature retient le SHA-256 de ses fichiers et le retrait automatique refuse une feature dont un fichier a été édité. Dans un essai où une référence de projet interdite est ajoutée au domaine, le build passe et un test d'architecture échoue en 9 secondes.

```
docs/template/decisions/<ADR outil épinglé>.md:3,79,86
Status: Proposed ... "Keep the copied tools. Every team keeps a fork of 11,600 lines it did not write" ... "Generators as item templates alone ... cannot edit the registrations, the closed lists and the manifest"
```
