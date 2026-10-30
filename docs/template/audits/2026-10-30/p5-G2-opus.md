# Phase 5, groupe G2 : conventions et extensibilité (analyste opus)

Clones lus en lecture seule, sans build ni génération :
- nous : `repos/ours`, develop `5f22faa5ec13be8bef0a793a63e06b533839c980` (2026-10-30), noté `ours:` ;
- concurrent : `repos/competitor-main`, main `345e7fe7612fdaf0cb5da58826a7c57d42e1fdd8` (2026-10-26), noté `comp:`.
  Sur les deux axes, main et v10.8.0 ne diffèrent que par l'AppHost et deux paquets de build (carte `p1-map-comp.md` §8,
  revérifié pour le gabarit `ca-usecase` : `parentNamespace` est aussi dans `competitor-release/templates/ca-use-case/.template.config/template.json:24`).

Racine des chemins : `/tmp/claude-1000/-home-pmmarchio-code-Template-DotNet-Clean-Architecture/608f3f2d-3c02-4530-9e46-6f8e7e0e445b/scratchpad/repos/`.
Issues et discussions du concurrent : `gh api` en GET seulement (REST `issues`, `search/issues`, `discussions`). La liste brute des
367 discussions est dans `scratchpad/g2opus/discussions.tsv`.

Matière reprise et revérifiée : cartes p1, personas `p3-comp-1.md`, `p3-ours-1.md`, `p3-ours-2-partial.md`, socle `p4-devil.md`.
Chaque chiffre cité ici a été recompté, sauf ceux des personas (exécutions que je n'ai pas refaites, attribuées comme telles).

---

## Axe 1. Conventions

### 1.1 Organisation : par feature ou par couche

**Concurrent.** Par feature dans Application seulement, par couche partout ailleurs.
```
src/Application/TodoItems/Commands/CreateTodoItem/CreateTodoItem.cs
src/Domain/Entities/TodoItem.cs            src/Domain/Events/TodoItemCompletedEvent.cs
src/Infrastructure/Data/Configurations/TodoItemConfiguration.cs
src/Web/Endpoints/TodoItems.cs
```
(`find src -name '*.cs'` sur `comp:`). Une feature est donc éparpillée sur 4 dossiers de nature (`Entities`, `Events`, `Configurations`,
`Endpoints`). Aucune règle ne tient cette organisation : pas de test d'architecture (`grep -r NetArchTest\|ArchUnit` vide, carte p1 §1).
Le seul piège mesuré : un dossier de feature crée un namespace de premier niveau (`<Projet>.Application.<Feature>`). Le persona
`p3-comp-1` (T1) a nommé sa feature `Projects` : le namespace masque la classe `Projects` générée par Aspire et casse la compilation
des tests fonctionnels (CS0234), sans avertissement.

**Nous.** Par feature dans chaque couche, avec un vocabulaire fermé à quatre niveaux.
```
| 1 | `Common/` or `Features/<Feature>/` | does it know a feature? |
| 2 | a concept: `UseCases/`, `Ports/`, `Model/`, `Access/`, `Rows/`, … | what role does it play? |
| 3 | a nature: `Commands/`, `Dtos/`, `Repositories/`, `Models/`, … | what is it? |
| 4 | a subject: `<Operation>/`, `<Port>/`, `<Event>/` | which one? |
```
`ours:docs/architecture/modules-and-boundaries.md:101-108`. Le vocabulaire de chaque projet est un dictionnaire de test
(`ours:Tests/Architecture/AppTemplate.Architecture.Tests/Rules/LayoutConventionTests.cs:12-110`), vérifié dans les deux sens
(`EveryFeatureFolder_IsNamedFromItsLayersVocabulary`, `:274` ; `EveryProjectOnDisk_HasAVocabularyOfItsOwn`, `:350`). Le segment
`Features.` évite la collision de namespace du concurrent par construction (`AppTemplate.Application.Features.TodoLists...`).
Raison écrite : « so the tree says which files leave with a feature » (`modules-and-boundaries.md:116-117`), et ADR 0004 (extraction).

**Verdict : on dépasse. Confiance haute.** La forme est tenue par des tests, la sienne par rien.
Réserve : le choix « par feature dans chaque couche, vocabulaire fermé » n'a pas d'ADR. ADR 0004 couvre l'isolation entre features, pas
l'arborescence ; la page `project-layout.md` décrit sans dire pourquoi pas l'autre forme. `AGENTS.md` exige une ADR pour « a choice
another team could reasonably have made otherwise » : le concurrent l'a fait autrement. Défaut à corriger (voir §Défauts).

### 1.2 Nommage : types, fichiers, projets

**Concurrent.**
- Un fichier par use case, sans suffixe, qui contient deux types publics (commande et handler) :
  `public record CreateTodoItemCommand : IRequest<int>` puis `public class CreateTodoItemCommandHandler` (`comp:src/Application/TodoItems/Commands/CreateTodoItem/CreateTodoItem.cs:6,13`).
- Dossiers de projet courts, nom complet porté par le `.csproj` : `src/Application/Application.csproj` avec
  `<RootNamespace>CleanArchitecture.Application</RootNamespace>` (`comp:src/Application/Application.csproj:4-5`, même schéma pour les 13 projets).
- Chemin suivi le plus long : 95 caractères (`templates/ca-use-case/...`), moyenne des `.cs` : 53 (`git ls-files | awk '{print length}'`).
- Routes : `/api/{ClassName}` par réflexion (`comp:src/Web/Infrastructure/WebApplicationExtensions.cs:39-41`), soit `/api/TodoLists`.
- Règles de nommage `.editorconfig` : 19 règles, toutes `severity = suggestion` (`comp:.editorconfig:196-268`).

**Nous.**
- Un type public par fichier, nommé comme le fichier (`LayoutConventionTests.EverySourceFile_DeclaresOnePublicType_NamedForTheFile`, `:544`).
- Le suffixe nomme la nature, la nature nomme le dossier : table de 22 lignes (`ours:docs/reference/project-layout.md`, section `## Naming`),
  tenue par `FeatureFolderVocabularyTests.EveryFileUnderAFeature_CarriesItsFoldersNatureWord` (`:78`) ; mots bannis `Manager`, `Utils`,
  `Processor`, `Handler` nu ; règle du port sans `I` pour l'adaptateur.
- Dossier de projet = nom complet : `Src/Application/AppTemplate.Application/`. Namespace = dossier, sans exception
  (`EverySourceFile_DeclaresTheNamespaceOfItsFolder`, `:464`).
- Routes `api/v{version:apiVersion}/<kebab-pluriel>` (carte p1 ours §5).

**Verdict : on dépasse sur la règle (confiance haute) ; en retard sur la longueur des chemins (confiance moyenne).**
Le coût de notre nommage est mesuré : chemin suivi le plus long sous `Tests/` = 176 caractères
(`Tests/Application/AppTemplate.Application.Auth.UnitTests/Features/ExternalLogins/UseCases/Commands/SignInWithExternalProvider/SignInWithExternalProviderCommandValidatorTests.cs`),
208 dans les starters, moyenne des `.cs` 103 (même commande). Issue ouverte chez nous :
https://github.com/Pierre-MarieMarchio/Template-DotNet_Clean_Architecture/issues/166 (« a project created in a folder deeper than about 80
characters cannot be committed » sous Windows sans `core.longpaths`). Le nom du produit apparaît une fois dans chacun de nos chemins (le dossier de projet) ; chez le concurrent, zéro fois. La forme courte du concurrent (`Application/` +
`RootNamespace`) économise `len(<Nom>) + 1` caractères par chemin, ce qui grandit avec le nom réel du produit (un nom de 25 caractères
coûte 14 de plus que `AppTemplate`).

### 1.3 Emplacement et nommage des tests

**Concurrent.** Un projet de test par couche et par type (`Domain.UnitTests`, `Application.UnitTests`, `Application.FunctionalTests`,
`Infrastructure.IntegrationTests` vide, `Web.AcceptanceTests`) ; un fichier par use case, sans dossier par use case :
`tests/Application.FunctionalTests/TodoItems/Commands/CreateTodoItemTests.cs`, méthodes `ShouldRequireMinimumFields`,
`ShouldCreateTodoItem` (`:11,19`). Pas de projet de test pour Web ni pour Infrastructure en pratique. Incohérence relevée par le persona :
`Application.UnitTests` n'a pas `global using NUnit.Framework` alors que `FunctionalTests` l'a (`p3-comp-1.md`, T4, CS0246).

**Nous.** Un projet de test miroir par projet source, rangé par couche, plus `Architecture/`, `Integration/`, `EndToEnd/`, `Load/`
(`ours:docs/reference/project-layout.md`, fin de la carte des dossiers). Recompté : 14 projets sous `Src/`, chacun a son `.UnitTests`
(14/14, script sur `find -name '*.csproj'`). Les tests reprennent les dossiers source, dossier d'opération compris. Méthodes
`Sujet_Comportement` (`CA1707` à `none` pour les tests seulement, `ours:.editorconfig:190`). Règles maison écrites
(`ours:docs/guides/testing.md:235-260`, au moins 17 règles avec un exemple chacune).

**Verdict : on dépasse. Confiance moyenne.** Deux réserves prouvées :
- le miroir « un projet de test par projet source » n'est tenu par aucun test : `grep -rn 'UnitTests\|mirror' Tests/Architecture/.../Rules`
  ne trouve que `TemplatePackagingTests` (exclu du projet généré). Il tient aujourd'hui par discipline, contre la règle d'`AGENTS.md`
  (« A rule checked by hand becomes a test in `Tests/Architecture` ») ;
- le dossier par use case dans les tests est l'origine directe des 176 caractères (#166 propose déjà de le raccourcir).

### 1.4 Style

| Point | Concurrent | Nous |
|---|---|---|
| Sévérités | 19 règles de nommage à `suggestion`, `file_scoped:silent` (`comp:.editorconfig:181,196-268`) ; seul `TreatWarningsAsErrors` (`comp:Directory.Build.props:5`), pas d'`EnforceCodeStyleInBuild` (grep vide) | `file_scoped:error`, `I*`/`T*`/PascalCase en `error`, `_camelCase` en `warning` (`ours:.editorconfig:30,63-89`) ; `EnforceCodeStyleInBuild` + `latest-recommended` + `TreatWarningsAsErrors` (`ours:Directory.Build.props:13-15`) |
| Constructeurs | `prefer_primary_constructors = true:suggestion` mais 35 champs `private readonly` injectés par constructeur classique pour 1 constructeur primaire (`grep` sur `comp:src`) : la préférence annoncée n'est pas suivie | constructeurs primaires dans les use cases (ex. `CreateTodoListUseCase`), style tenu par le build |
| Encodage | aucune règle `charset` (`comp:.editorconfig:1-30`) ; 70 `.cs` avec BOM, 40 sans | `charset = utf-8-bom` pour `.cs` et `.md` (`ours:.editorconfig:16,26`), `dotnet format --verify-no-changes` dans `verify` ; 1 774 `.cs` sur 1 774 avec BOM (hors `Generated/`) |
| Commentaires | 43 lignes `//` de prose dans `src` (ex. `// Add services to the container.`, `comp:src/Web/Program.cs:6`) | 0 ligne de prose dans `Src` hors `Generated/`, tenu par `Tools/CheckComments.cs` (carte p1 ours D9) |
| Suppressions | 1 `#pragma warning disable CS1998` (`comp:src/Application/WeatherForecasts/Queries/GetWeatherForecasts/GetWeatherForecastsQuery.cs:12`) | 1 `#pragma warning disable ASP0000` (`ours:Src/Presentation/AppTemplate.Api/Common/Hosting/HttpPipelineModule.cs:67`), contre notre propre règle (`AGENTS.md`), déjà relevé par G1 et p4 (C2) |

**Verdict : on dépasse. Confiance haute.** Chez le concurrent, la convention est une suggestion d'IDE ; chez nous, une erreur de build.

### 1.5 Synthèse de l'axe 1

| Sous-point | Verdict | Confiance |
|---|---|---|
| Organisation par feature, vocabulaire fermé | on dépasse (sans ADR : défaut) | haute |
| Nommage tenu par tests | on dépasse | haute |
| Longueur des chemins | en retard | moyenne |
| Emplacement des tests | on dépasse (miroir non tenu par test) | moyenne |
| Style, encodage, commentaires | on dépasse | haute |

---

## Axe 2. Extensibilité

Mesures des personas qui ont fait les mêmes tâches sur les deux projets générés (non refaites ici) :

| Tâche | Concurrent (`p3-comp-1.md` §5) | Nous (`p3-ours-1.md` §2) |
|---|---|---|
| Feature complète | 5 squelettes `ca-usecase`, ~380 lignes à la main, 2 listes à la main (DbSet x2), compile au 2e essai (namespace `Projects`) | 54 fichiers écrits par `new-feature`, ~215 lignes à la main, 6 listes fermées tenues par test, gate vert après `migration-add` |
| Use case dans une feature | 2 squelettes, ~110 lignes, route à la main sans signal si oubliée | 8 fichiers écrits par l'outil, ~350 lignes à la main (dont tests), action de contrôleur imprimée ; route oubliée sans signal aussi |
| Second host | `dotnet new worker` du SDK, 4 listes, échec au démarrage sans `ValidateOnBuild = false` | MCP : 1 commande, 83 fichiers, 0 ligne à la main, 4 listes fermées complétées par l'outil |
| Seconde app web | copie de 29 fichiers d'une autre génération | `web-app` en 2,6 s, puis 10 éditions imprimées dont 7 non contrôlées |
| Retirer une feature | `grep` + `git rm` | `remove-feature` : rapport ; `--apply` seulement sur squelette intact (29 fichiers en 1 appel) |

### 2.1 Ajouter une feature, un use case

**Concurrent.** Un item template, un fichier, corps vide :
```csharp
public async Task<TReturnType> Handle(CleanArchitectureUseCaseCommand request, CancellationToken cancellationToken)
{
    throw new NotImplementedException();
}
```
`comp:templates/ca-use-case/FeatureName/Commands/CleanArchitectureUseCase/CleanArchitectureUseCase.cs:34-37`. Options : `featureName`,
`useCaseType`, `returnType`, `parentNamespace` (`.template.config/template.json:24-80`). Le reste se branche seul, par scan de
l'assembly : handlers, validateurs, profils AutoMapper (`comp:src/Application/DependencyInjection.cs:11-23`) et endpoints
(`IEndpointGroup` découvert par réflexion, `comp:src/Web/Infrastructure/WebApplicationExtensions.cs:31-44`). Seule liste à la main :
`DbSet` dans `IApplicationDbContext` et dans `ApplicationDbContext` (`comp:src/Application/Common/Interfaces/IApplicationDbContext.cs:7-9`).
Rien ne signale un handler laissé à `NotImplementedException` ni une route oubliée.

**Nous.** Trois générateurs dans le projet : `new-feature` (forme record ou agrégat, `--access`, `--deletion`, `--operations`,
`--concurrency`, `ours:Tools/NewFeature.cs:15-40`), `new-use-case`, `remove-feature` (manifeste `.feature-manifest/`). `new-feature`
n'édite que ses propres fichiers et imprime le reste :
```csharp
output.WriteLine("The solution does not compile until edit 1 is made, and the feature does not compose until all are; nothing here edited a file it does not own.");
... "Six edits, each by hand, each somewhere an absence should be visible:"
```
`ours:Tools/NewFeature.cs:1227-1229`. Les listes qu'une feature rejoint sont nommées avec le test qui échoue tant qu'elles ne le
disent pas (`ours:docs/guides/adding-a-feature.md:173-196`, 12 lignes). L'enregistrement est explicite par feature
(`services.AddFeature(_todoLists)`, `ours:Src/Application/AppTemplate.Application/ApplicationModule.cs:73`), le scan est borné au
dossier de la feature (`:152-165`) et refuse un use case sans interface nommée ou avec deux
(`ours:Src/Application/AppTemplate.Application.Core/ApplicationCoreModule.cs:67-86`).

**Verdict : on dépasse. Confiance moyenne.** Plus de travail écrit par l'outil, rien de silencieux sur les listes ; en contrepartie
plus d'éditions à la main que de lignes au concurrent par tâche courte (use case : ~350 contre ~110, mais avec 10 tests contre 1 ;
G1 §1.1 attribue l'écart surtout à ADR 0005). Les listes fermées sont un choix assumé (AGENTS.md, promesse 3 du cadrage, ADR 0020).

### 2.2 Ajouter un module (une capacité d'infrastructure)

**Concurrent.** Une seule `Infrastructure` qui mêle base, interceptors, Identity et schémas d'authentification web :
```csharp
builder.Services.AddAuthentication(options => { options.DefaultScheme = IdentityConstants.ApplicationScheme; ... })
    .AddIdentityCookies();
builder.Services.AddIdentityCore<ApplicationUser>() ... .AddSignInManager() ...
```
`comp:src/Infrastructure/DependencyInjection.cs:58-73`. Un module de plus (Redis, mail) va dans ce projet ou dans un projet que l'équipe
invente, sans règle ni page. Question restée sans réponse : https://github.com/jasontaylordev/CleanArchitecture/discussions/945
(« Where should be a Redis service included on the infrastructure project? », 0 réponse).

**Nous.** Un projet par capacité, une méthode d'enregistrement, un contexte par module qui stocke ; un module ne référence jamais un
autre module (`ours:docs/architecture/modules-and-boundaries.md:7-20`), tenu par `NoInfrastructureModule_DependsOnAnotherInfrastructureModule`
et `InfrastructureModules_ReferenceOnlyTheLayersCoreHorizontally` (`ours:Tests/Architecture/.../Rules/ModuleDependencyTests.cs:215,345`).
Ajouter un projet : une entrée dans les deux vocabulaires (`LayoutConventionTests`), `AppTemplate.sln`, une ligne `COPY` du `Dockerfile`,
un miroir sous `Tests/`, les planchers de `ModuleDependencyTests` (`ours:docs/guides/adding-a-feature.md:196` ;
`ours:docs/reference/project-layout.md:302-310`). Le message du test dit quoi faire :
« Give it an entry in each — empty when its features hold their files side by side, as Email and InMemory do — and write the words
into docs/reference/project-layout.md's folder map. » (`LayoutConventionTests.cs:367-371`).

**Verdict : on dépasse. Confiance haute.** p4 (A8) a écarté un outil `NewModule` : l'ajout est rare et la procédure existe.

### 2.3 Providers : base, mail, stockage, authentification

**Concurrent.** La base se choisit à la génération par des `#if` dans le code (`comp:src/Infrastructure/DependencyInjection.cs:26-40`,
`comp:src/AppHost/Program.cs:7-23` ; 7 fichiers portent ces branches). Après génération, il reste une ligne `UseXxx`, un paquet et une
ressource Aspire à changer à la main. Pas de mail, pas de stockage de fichiers, une seule authentification (Identity locale).
Demandes ouvertes : MySQL https://github.com/jasontaylordev/CleanArchitecture/issues/1571 (6 👍) ; alternative à Identity
https://github.com/jasontaylordev/CleanArchitecture/discussions/1043.

**Nous.**
- Base : un provider par projet, PostgreSQL dans l'arbre, les autres en fichiers de surcouche (`ours:docs/template/decisions/0005-a-project-carries-one-database-provider.md`).
  Ce qui dépend du provider est concentré : 5 fichiers sous `Src` pour SQL Server (`DatabaseProvider.cs`, `LeaderLease.cs`,
  `OwnerQuotaLock.cs`, `RedundantCascadePathsConvention.cs`, `DatabaseInstrumentation.cs`), 7 pour SQLite
  (`find .template.config/variants/database-*/Src`). Changer après génération : « moves by generating again and carrying its own
  code across » (ADR template 0005, Consequences).
- Mail : deux transports dans un module, choisis par `Email:Transport`, avec refus explicite d'une valeur inconnue
  (`ours:Src/Infrastructure/AppTemplate.Infrastructure.Email/EmailModule.cs:39-65`). Un troisième transport = une classe
  `IEmailSender` et une branche.
- Stockage : S3 ou disque ; un nouvel adaptateur hérite des tests de contrat déjà joués par les deux adaptateurs et le double :
  `public abstract class FileContentStoreContract` (`ours:Tests/Infrastructure/AppTemplate.Infrastructure.Storage.UnitTests/Features/Files/Contracts/FileContentStoreContract.cs:8`),
  hérité par `StoredObjectTests`, `FileSystemFileContentStoreTests`, `InMemoryFileContentStoreTests` (ADR 0020, ligne 34-37).
- Authentification : `--auth local|external|none` à la génération.

**Verdict : on dépasse pour mail, stockage, auth (haute) ; choix différent assumé (ADR template 0005) pour la base, à égalité sur le
changement après génération (moyenne).** Les deux demandent un travail à la main pour changer de base une fois le projet créé ; le
nôtre est plus long (5 à 7 fichiers contre 1 ligne) parce qu'il porte plus de comportement propre au provider (verrous, bail, `xmin`).

### 2.4 Ajouter un host

**Concurrent.** Aucun outil. `AddInfrastructureServices` enregistre les cookies Identity et le `SignInManager` (§2.2), si bien qu'un
worker qui l'appelle échoue au démarrage : `Unable to resolve service for type 'Microsoft.AspNetCore.Routing.EndpointDataSource' while
attempting to activate 'AuthorizationPolicyCache'` (`p3-comp-1.md`, T6, contourné par `ValidateOnBuild = false`). Le même couplage
apparaît dans https://github.com/jasontaylordev/CleanArchitecture/discussions/1607 (Infrastructure obtient
`Microsoft.AspNetCore.Authorization` par la référence de Web, 2026-04). Demandes historiques : worker
https://github.com/jasontaylordev/CleanArchitecture/issues/95 (2020, mise à la roadmap puis fermée, aucun worker dans main),
https://github.com/jasontaylordev/CleanArchitecture/issues/195, plusieurs présentations
https://github.com/jasontaylordev/CleanArchitecture/issues/246 et
https://github.com/jasontaylordev/CleanArchitecture/discussions/1388 (« three presentation layers: API, Web Service, Worker … how to share
common objects? », 0 réponse). Point fort réel : l'AppHost Aspire rend l'ajout d'une ressource locale court
(`builder.AddProject<Projects.Web>(...)`, `comp:src/AppHost/Program.cs:25`) et `AddServiceDefaults()` donne télémétrie et santé à tout host.

**Nous.** « Each host composes itself » (`ours:docs/decisions/0022-a-presentation-project-reaches-the-core-through-the-api-or-the-application-layer.md:21-29`),
tenu par `NoHost_ReferencesAnotherHost` et `OnlyAHost_ReferencesInfrastructureModules` (`ModuleDependencyTests.cs:380,403`). Le worker
compose feature par feature et module par module, sans rien de web :
```csharp
builder.Services.AddTodoLists();
builder.Services.AddPersistenceModule(builder.Configuration);
builder.Services.AddEmailModule(builder.Configuration);
builder.Services.AddWorkerCallerIdentity();
```
`ours:.template.config/starters/worker/Src/Presentation/AppTemplate.Worker/Program.cs:37,65-71`. Ce qu'ils partagent est dans
`AppTemplate.Presentation.Core` (observabilité, HTTP sortant, jobs). Après génération : `cleanarch-presentation` propose
`blazor-server`, `avalonia`, `mcp`, `wpf` (lu dans `.template.config/presentation/.template.config/template.json`), puis
`presentation-join` applique les éditions (`ours:Tools/Tasks.cs:147`). Limites prouvées :
- `worker` n'est pas proposé après génération (même fichier) ; choix assumé par l'ADR template 0012 (« No item template moves the
  jobs »), le chemin manuel est dans `ours:docs/features/background-jobs.md:156-163` ;
- `web-app` imprime 10 éditions, 7 non contrôlées par un test (`p3-ours-1.md`, F13) ;
- `migration-add` ne vise que le contexte métier (`ours:Tools/Tasks.cs:529-535`) : après un host qui touche l'auth, la migration
  `AuthDbContext` se tape à la main (`p3-ours-1.md`, F16).
- Orchestration locale : Compose (ADR 0001, ligne « Docker Compose for the local stack », `ours:docs/decisions/0001-adopt-the-generated-baseline.md:62`) ;
  Aspire en option future (#122, `docs/template/roadmap.md`).

**Verdict : on dépasse. Confiance haute** (outil + règle + composition par host contre rien et un `AddInfrastructureServices` non
neutre). Le worker hors génération est un choix assumé (ADR template 0012).

### 2.5 Remplacer une brique

**Concurrent.** Les briques tierces sont dans les signatures :
- MediatR : 22 fichiers `.cs` utilisent `IRequest`, `ISender`, `INotification` ou un behaviour (`grep -rlE` sur `src tests templates`),
  et le domaine en dépend : `public abstract class BaseEvent : INotification` (`comp:src/Domain/Common/BaseEvent.cs:5`,
  `MediatR.Contracts` dans `comp:src/Domain/Domain.csproj:9`). Licence commerciale annoncée et gardée :
  https://github.com/jasontaylordev/CleanArchitecture/discussions/1346 ; la PR « use mit mediator »
  https://github.com/jasontaylordev/CleanArchitecture/pull/1348 a été fermée sans fusion (G1 §1.2) ; plainte d'avertissement de licence
  https://github.com/jasontaylordev/CleanArchitecture/discussions/1413.
- EF Core : `IApplicationDbContext` expose des `DbSet` (`comp:src/Application/Common/Interfaces/IApplicationDbContext.cs:7-9`), utilisé par
  13 fichiers de `src` ; remplacer EF touche tous les handlers (assumé par leur ADR-001).
- En revanche, ajouter un comportement transverse à toutes les requêtes coûte une ligne :
  `cfg.AddOpenBehavior(typeof(ValidationBehaviour<,>));` (`comp:src/Application/DependencyInjection.cs:21`).

**Nous.** L'application ne voit ni EF, ni ASP.NET Core, ni MediatR, ni MailKit, tenu par test :
`private static readonly string[] _forbiddenInApplication = [ ..., "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", ..., "MailKit", "MediatR" ];`
(`ours:Tests/Architecture/AppTemplate.Architecture.Tests/Rules/LayerDependencyTests.cs:32-49`). Remplacer un adaptateur = une classe
derrière un port, et pour les ports de fichiers les tests de contrat suivent (§2.3). Le transverse sur tous les use cases n'a pas de
point d'extension : chaque use case est enregistré sous son interface nommée (`ApplicationCoreModule.cs:58-61`), donc un décorateur
générique ne s'applique pas d'un coup. ADR 0003 l'assume (« There is no MediatR, no dispatcher and no pipeline behaviour »,
`ours:docs/decisions/0003-use-cases-and-ports-are-explicit.md:18-20`) et prévoit sa révision (« behaviour wrapped around every use case
that a decorator or the use case itself cannot carry », `:45-46`). Aucune page ne montre comment écrire ce décorateur
(`grep -rn -i decorat docs Src` : seule la ligne 45 de l'ADR).

**Verdict : on dépasse pour remplacer une brique (haute) ; choix différent assumé (ADR 0003) pour le transverse sur tous les use cases
(moyenne).** Aucun persona n'a eu besoin d'un transverse de ce type : pas de coût utilisateur prouvé.

### 2.6 Ce qu'une équipe peut changer sans casser le template

**Concurrent.** Tout, puisqu'aucune règle n'est exécutable : l'équipe peut ajouter une dépendance Domain vers EF Core sans aucun signal
(`p3-comp-1.md`, T8 : « 0 erreur, 0 signal »). Question ouverte sur ce point : « How to enforce that it is only startup/program that has a
reference to infrastructure? » https://github.com/jasontaylordev/CleanArchitecture/discussions/731 (réponse communautaire : ArchUnitNET).
Ses ADR sont numérotées `ADR-001..003` dans `docs/decisions/`, que `template.json` n'exclut pas (`comp:.template.config/template.json:297-317`) :
sur main, elles partent dans le projet généré sans règle de numérotation pour l'équipe (le tag v10.8.0 n'a pas de `docs/`).

**Nous.** Changer une convention fait rougir un test, et le test dit où l'écrire (§2.2). La règle du dépôt est de changer la décision et
son test ensemble (`AGENTS.md`, « a decision is an ADR and the test that holds it, changed together »). Les ADR de l'équipe partent de
1000 (`ours:docs/decisions/README.md:22-25`), sans collision avec celles livrées. `remove-feature` compare les hashes du manifeste.
Coût : une équipe qui veut assouplir une règle doit lire et modifier `Tests/Architecture` (8 899 lignes, carte p1 ours §2.3).

**Verdict : on dépasse. Confiance moyenne.** Liberté du concurrent = absence de garde ; la nôtre a un prix d'entrée réel mais guidé.

### 2.7 Synthèse de l'axe 2

| Sous-point | Verdict | Confiance |
|---|---|---|
| Feature, use case | on dépasse | moyenne |
| Module d'infrastructure | on dépasse | haute |
| Providers mail, stockage, auth | on dépasse | haute |
| Provider de base | choix assumé (ADR template 0005), égalité après génération | moyenne |
| Host | on dépasse ; worker hors génération assumé (ADR template 0012) | haute |
| Remplacer une brique | on dépasse | haute |
| Transverse sur tous les use cases | choix assumé (ADR 0003) | moyenne |
| Changer sans casser | on dépasse | moyenne |

---

## Ce qu'on gagnerait à lui prendre

1. **Dossiers de projet courts** (`Src/Application/Application/` ou `Src/Application/Application.Auth/` avec `RootNamespace` et
   `AssemblyName` au nom complet), comme `comp:src/Application/Application.csproj:4-5`. Gain mesuré : `len(<Nom>)+1` caractères par chemin,
   12 pour `AppTemplate`, plus pour un vrai nom ; répond à #166 sans toucher au vocabulaire. Forme chez nous : changer
   `EverySourceFile_DeclaresTheNamespaceOfItsFolder` pour qu'il lise le `RootNamespace` du projet, le renommage du template
   (`sourceName`) et les chemins de `LayoutConventionTests`. C'est un choix de convention : **ADR à écrire** (« A project folder carries
   the layer's short name, the assembly carries the product's »). Confiance moyenne : le gain de 12 caractères ne suffit pas seul à
   passer sous 80 de marge Windows pour un dossier profond ; à combiner avec l'option de #166 qui aplatit le dossier d'opération dans les tests.
2. **Un fichier de test par use case sans dossier d'opération** côté tests, comme `comp:tests/Application.FunctionalTests/TodoItems/Commands/CreateTodoItemTests.cs`.
   Économise le nom de l'opération une fois (`SignInWithExternalProvider/`, 27 caractères sur le chemin de 176). Forme chez nous : règle dans `LayoutConventionTests` pour
   les projets de test, ligne dans `project-layout.md`. Confiance moyenne.
3. **Rien d'autre sur ces deux axes.** L'item template `ca-usecase` est en dessous de `new-use-case` ; la découverte des endpoints par
   réflexion et le scan global n'ont pas d'équivalent souhaitable (voir refus). La seule force réelle (transverse en une ligne) est un
   choix contraire à ADR 0003, sans coût prouvé chez nous.

## Ce qu'il faut refuser, et pourquoi

| Pratique du concurrent | Pourquoi la refuser | Preuve |
|---|---|---|
| Conventions à `suggestion`, aucun test de forme | La convention dérive déjà chez lui : primary constructors annoncés, 35 champs injectés par constructeur classique ; BOM 70/40 | `comp:.editorconfig:184,196-268` ; grep `private readonly` |
| Organisation par couche hors Application | Une feature éparpillée sur 4 dossiers de nature n'est ni retirable ni extractible d'un bloc (ADR 0004) | `comp:src/Domain/Entities`, `comp:src/Infrastructure/Data/Configurations` |
| Un fichier = commande + handler (+ validateur dans le gabarit) | Contredit « un type public par fichier », tenu chez nous par test | `comp:.../CreateTodoItem.cs:6,13` ; `LayoutConventionTests.cs:544` |
| Une `Infrastructure` unique qui enregistre l'auth web | Rend tout second host non web fragile (échec prouvé du worker) | `comp:src/Infrastructure/DependencyInjection.cs:58-73` ; `p3-comp-1.md` T6 |
| Branches `#if` de provider dans le code | ADR template 0005 : code d'un provider jamais exercé par notre gate | `comp:src/Infrastructure/DependencyInjection.cs:26-40` |
| Gabarit de use case à `throw new NotImplementedException()` | Compile, échoue à l'exécution, rien ne le signale | `comp:templates/ca-use-case/.../CleanArchitectureUseCase.cs:34-42` |
| Routes `/api/{ClassName}` par réflexion | Le nom de classe devient contrat public ; notre contrat est versionné et tenu par `OpenApiDocumentTests` | `comp:src/Web/Infrastructure/WebApplicationExtensions.cs:39-41` |

## Ce qu'on peut faire qu'il ne fait pas (ses issues et discussions comprises)

| Demande chez lui | État chez lui | Chez nous | Preuve chez nous |
|---|---|---|---|
| Sans exemples : https://github.com/jasontaylordev/CleanArchitecture/issues/1192 (7 👍, roadmap depuis 2026-02-19) et sans Identity : https://github.com/jasontaylordev/CleanArchitecture/issues/1613 (fermée comme doublon) | ouvert | `--examples none`, `--auth none|external` | carte p1 ours §6 ; `template.json` |
| Blazor : https://github.com/jasontaylordev/CleanArchitecture/issues/1444 (26 👍) | ouvert, sans calendrier | `--web blazor-wasm`, `--presentation blazor-server` | inventaire p1 §2 |
| Vue : https://github.com/jasontaylordev/CleanArchitecture/issues/1570 (7 👍) | ouvert | `--web vite-vue` | inventaire p1 §2 |
| Worker : https://github.com/jasontaylordev/CleanArchitecture/issues/95, https://github.com/jasontaylordev/CleanArchitecture/issues/195 | fermées, aucun worker livré | `--presentation worker` | ADR template 0012 |
| Plusieurs présentations sur la même application : https://github.com/jasontaylordev/CleanArchitecture/issues/246, https://github.com/jasontaylordev/CleanArchitecture/discussions/1388 | discussion sans réponse | `Presentation.Core` partagé, ADR 0022, `cleanarch-presentation` + `presentation-join` | `ModuleDependencyTests.cs:380,403` |
| Tenir les références par test : https://github.com/jasontaylordev/CleanArchitecture/discussions/731 | réponse « use ArchUnitNET », rien dans le template | `ModuleDependencyTests`, `LayerDependencyTests` | `Rules/ModuleDependencyTests.cs:263,380` |
| Où placer Redis : https://github.com/jasontaylordev/CleanArchitecture/discussions/945 ; mails et gabarits : https://github.com/jasontaylordev/CleanArchitecture/discussions/685 | sans réponse / sans exemple | un module par capacité, `ICacheStore`, module Email à deux transports | `modules-and-boundaries.md:7-42` |
| Alternative à Identity : https://github.com/jasontaylordev/CleanArchitecture/discussions/1043 | ouverte | `--auth external` (OIDC) | inventaire p1 §2 |
| Retirer une feature | rien | `remove-feature`, manifeste de hashes | `AGENTS.md`, `p3-ours-1.md` T7 |

Pistes pour aller plus loin que lui, sur nos propres frictions (prouvées par les personas, sans ADR contraire) :
- `migration-add --context auth` : la seule tâche manquante pour le contexte Auth (`Tools/Tasks.cs:529-535`, `p3-ours-1.md` F16) ;
- un `web-join` qui applique les 10 éditions de `web-app`, comme `presentation-join` le fait (`p3-ours-1.md` F13 ; issue
  https://github.com/Pierre-MarieMarchio/Template-DotNet_Clean_Architecture/issues/151 sur `web-join`) ;
- un test du miroir des projets de test (voir défauts).

## Défauts chez nous trouvés sur ces axes

1. **Pas d'ADR pour l'arborescence par feature à vocabulaire fermé.** Choix qu'une autre équipe a fait autrement (le concurrent) ;
   `AGENTS.md` (« A choice another team could reasonably have made otherwise is an ADR »). Seules la page `project-layout.md` et
   `modules-and-boundaries.md:116-117` en donnent la raison. À écrire : ADR « The tree is closed: a feature folder in every layer, one
   vocabulary per project », liée aux tests `LayoutConventionTests` et `FeatureFolderVocabularyTests`.
2. **Le miroir « un projet de test par projet source » n'est tenu par aucun test** (14/14 aujourd'hui, par discipline) alors que la
   table des listes fermées l'exige pour « A new project » (`adding-a-feature.md:196`). À tenir dans `Tests/Architecture`.
3. **`docs/guides/adding-a-setting.md:23` (« Write `<Section>OptionsValidator` … beside it ») se lit comme un fichier voisin**, que
   `FeatureFolderVocabularyTests` refuse (`["Options"] = ["Options"]`, `Rules/FeatureFolderVocabularyTests.cs:37`) ; le modèle cité
   met le validateur `internal` dans le même fichier (`IdempotencyPurgeOptions.cs:5,12`). Le persona a eu deux tests rouges en suivant
   la page (`p3-ours-1.md` F10). Écrire « in the same file, `internal` ».
4. **`project-layout.md:3-4,304` dit qu'un fichier mal placé « fails the build »** : c'est un test qui échoue, le build passe
   (`p3-ours-1.md` T8 : « le build passe avec la référence interdite »). Mineur ; écrire « fails `verify` ».
5. **Chemins de 176 caractères** : issue #166 ouverte, les deux options du §« prendre » y répondent.

## Non vérifié

- Aucun build, test ni génération : les comportements à l'exécution (échec du worker du concurrent, rouges de nos tests) viennent des
  personas, non refaits.
- L'effet d'un dossier de projet court sur `TemplatePackagingTests`, le renommage `sourceName` et les 25 jambes d'acceptation n'est pas mesuré.
- Le gain exact en caractères de l'aplatissement du dossier d'opération sur tous les chemins (seul le plus long est calculé).
- Les réponses du site de documentation externe du concurrent (non lu).
- Les discussions du concurrent ont été filtrées par mots-clés sur les titres : une discussion pertinente au titre vague a pu échapper.
