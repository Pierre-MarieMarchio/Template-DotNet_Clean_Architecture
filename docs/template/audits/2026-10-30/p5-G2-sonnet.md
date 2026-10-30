# Phase 5, groupe G2 (analyste Sonnet) : conventions et extensibilité

Périmètre : axe 1 (conventions : nommage, organisation par feature ou par couche, emplacement des tests, style) et axe 2 (extensibilité : ajouter un use case, une feature, un host, un provider, remplacer une brique, ce qu'une équipe peut changer sans casser le template).

Lecture seule, rien exécuté, rien généré. Les chiffres des personas (p3) sont repris tels quels et marqués « persona » ; tout le reste a été relu dans les clones ou par commande.

- Nous : `<scratchpad>/repos/ours`, develop `5f22faa5`. Chemins `nous:` relatifs à ce clone.
- Concurrent : `<scratchpad>/repos/competitor-main`, main `345e7fe`. Chemins `conc:` relatifs à ce clone. Issues et discussions : `gh api` en GET.
- Étalon : `out/00-cadrage.md`.

## 0. Synthèse

1. Sur les conventions, on dépasse le concurrent par l'exécution : les siennes sont des suggestions d'éditeur (19 règles de nommage à `suggestion`, `file_scoped:silent`, aucun contrôle de format en CI), les nôtres cassent le build ou le gate. Le prix est un arbre profond (chemins jusqu'à 176 caractères contre 88) et plus de fichiers par use case ; notre propre issue #166 l'a déjà relevé.
2. Sur l'extensibilité, on dépasse le concurrent sur tout ce qu'il n'offre pas (feature, host, retrait des exemples ou de l'authentification, garde-fous qui rougissent quand une équipe casse une règle). Lui garde deux atouts : zéro cérémonie pour ajouter une route ou un handler (tout est découvert par réflexion) et un pipeline transversal ouvert (`AddOpenBehavior`). Le second est un choix que le cadrage refuse (ADR 0003) ; il n'a pas de recette écrite chez nous, ce qui est un trou de documentation, pas de conception.
3. Quatre défauts à nous, prouvés, plus lourds que toute différence avec le concurrent :
   - `.editorconfig` dit `csharp_style_prefer_primary_constructors = false` alors que 208 classes de `Src/` utilisent un constructeur primaire (C5) ;
   - `docs/reference/project-layout.md:5` dit que chaque règle de la page est tenue par un test ; la règle « un projet de test miroir par projet source » (ligne 196) ne l'est par aucun (C3) ;
   - un message de `UseCaseConventionTests` dit « registration is explicit rather than scanned » alors que les use cases sont trouvés par scan (E1) ;
   - `new-feature` imprime 6 à 11 modifications à la main qu'il pourrait appliquer, comme `presentation-join` le fait déjà ; elles coûtent un premier gate rouge (E2).
4. Rien à prendre au concurrent qui soit structurant. À prendre en petit : son jeu complet de règles de nommage, mais à la sévérité `warning` (C1).

---

## Axe 1. Conventions

### C1. Nommage et règles d'analyse

**Comment fait le concurrent.** Dix-neuf règles de nommage, toutes `suggestion`, et les espaces de noms de fichier en `silent` : l'éditeur propose, rien ne casse.

```
# conc: .editorconfig:196-268 (19 règles, ex. :200)
dotnet_naming_rule.types_and_namespaces_should_be_pascalcase.severity = suggestion
# conc: .editorconfig:181
csharp_style_namespace_declarations = file_scoped:silent
```
La CI ne lance ni `dotnet format` ni test de style : `conc: .github/workflows/build.yml:55-66` = restore, build, test. Seul `TreatWarningsAsErrors` (`conc: Directory.Build.props:5`) tient quelque chose. Un `#pragma warning disable CS1998` est livré (`conc: src/Application/WeatherForecasts/Queries/GetWeatherForecasts/GetWeatherForecastsQuery.cs:12`).

**Comment on fait.** Interfaces, types et paramètres de type en `error`, champs privés `_camelCase` en `warning`, espaces de noms de fichier en `error`, `using` hors espace de noms en `warning`, et `dotnet format --verify-no-changes` dans `verify`.

```
# nous: .editorconfig:30 et :63-65
csharp_style_namespace_declarations = file_scoped:error
dotnet_naming_rule.interfaces_start_with_i.severity = error
# nous: Tools/Tasks.cs:481
Step("dotnet", "format", solution, "--verify-no-changes", "--verbosity", "normal");
```
Limites prouvées : quatre règles de nommage seulement (`nous: .editorconfig:63-85`), donc rien n'impose PascalCase aux méthodes, propriétés et constantes, ni camelCase aux paramètres ; et la règle « jamais de `NoWarn` » (AGENTS.md, en-tête `.editorconfig:91`) est violée par `<NoWarn>$(NoWarn);CS1591;CS1573</NoWarn>` (`nous: Directory.Build.props:27`) et `#pragma warning disable ASP0000` (`nous: Src/Presentation/AppTemplate.Api/Common/Hosting/HttpPipelineModule.cs:67`). Aucun test n'interdit ces écarts.

**Verdict : on dépasse** (confiance haute) sur l'exécution. Notre jeu est plus mince que le leur en couverture de symboles.

**À prendre.** Son jeu complet de règles de nommage (PascalCase des membres, camelCase des paramètres et locaux, constantes), repris à la sévérité `warning` pour que `format --verify-no-changes` les tienne. Forme : un ajout à `.editorconfig`, pas d'ADR (aucun choix qu'une autre équipe ferait autrement), une preuve rouge à voir une fois.

**À refuser.** La sévérité `suggestion` partout : une règle qui ne casse rien n'est pas une règle, et le concurrent n'en tire aucune garantie (une analyse à `suggestion` ne figure dans aucun gate).

**Ce qu'on fait et lui non.** Rien d'ouvert chez lui sur ce point : ses issues (#1213, #1218) corrigent une faute de frappe d'`.editorconfig` et le nom des handlers d'événements, https://github.com/jasontaylordev/CleanArchitecture/issues/1213 et https://github.com/jasontaylordev/CleanArchitecture/issues/1218.

### C2. Organisation : par feature ou par couche

**Comment fait le concurrent.** Par feature dans Application seulement ; par couche partout ailleurs.

```
conc: src/Application/TodoItems/Commands/CreateTodoItem/CreateTodoItem.cs       (feature)
conc: src/Domain/Entities/TodoList.cs                                           (couche)
conc: src/Infrastructure/Data/Configurations/TodoListConfiguration.cs           (couche)
conc: src/Web/Endpoints/TodoLists.cs                                            (couche)
```
Un use case = un dossier de deux fichiers (`CreateTodoItem.cs` : commande, handler ; plus le validateur). Les noms de projet sont nus (`Application`, `Domain`), l'espace de noms porte le préfixe (`conc: CleanArchitecture.slnx:10-16`). Les extensions d'enregistrement vivent dans l'espace de noms `Microsoft.Extensions.DependencyInjection` (`conc: src/Application/DependencyInjection.cs:5`). Aucune règle ne tient l'arbre.

**Comment on fait.** Par feature dans chaque couche, vocabulaire de dossiers fermé et vérifié dans les deux sens.

```
nous: Src/Application/AppTemplate.Application/Features/TodoLists/UseCases/Commands/CreateTodoList/{CreateTodoListCommand,ICreateTodoListUseCase,CreateTodoListUseCase,CreateTodoListCommandValidator}.cs
nous: Src/Domain/AppTemplate.Domain/Features/TodoLists/Model/Entities/TodoList.cs
nous: Src/Infrastructure/AppTemplate.Infrastructure.Persistence/Features/TodoLists/Rows/Configurations/TodoListRecordConfiguration.cs
nous: Tests/Architecture/.../Rules/LayoutConventionTests.cs:225,274,350,407,464,520 (mots de dossier, racine de projet, espace de noms = dossier, dossier vide)
```
Le résultat se mesure : 24 fichiers pour TodoLists dans Domain et Persistence (`find`), quatre fichiers par use case (liste ci-dessus) contre deux. ADR 0002 (deux formes), 0004 (les features se rencontrent par ports et événements) et 0007 (un schéma par feature) justifient la forme ; `docs/architecture/modules-and-boundaries.md` (« Inside a feature ») donne les quatre questions par niveau.

**Verdict : choix différent assumé (ADR 0002, 0004, 0007)**, confiance haute. Le coût pour l'utilisateur est prouvé sur un point précis : la longueur des chemins (C2b).

**C2b. Longueur des chemins : retard limité, déjà suivi.** Plus long chemin de fichier `.cs` hors `Generated/` et `Migrations/` :
```
nous (176 car.) : Tests/Application/AppTemplate.Application.Auth.UnitTests/Features/ExternalLogins/UseCases/Commands/SignInWithExternalProvider/SignInWithExternalProviderCommandValidatorTests.cs
conc (88 car., tous fichiers de src et tests hors node_modules) : mesuré par find | awk length
```
Notre issue #166 le dit : https://github.com/Pierre-MarieMarchio/Template-DotNet_Clean_Architecture/issues/166 (« a deep folder on Windows cannot commit »). Le chemin relatif seul n'atteint pas les 260 caractères de Windows ; il suffit d'un dossier de projet profond. Confiance moyenne sur l'effet réel (non reproduit sous Windows).

**À prendre.** Rien de la forme du concurrent. Une mitigation interne : un plafond de longueur de chemin dans `LayoutConventionTests` (règle d'architecture, pas d'ADR) et des noms de test raccourcis pour les validateurs d'Auth.

**À refuser.** La répartition par couche du domaine et de la persistance : elle rend une feature non extractible (ADR 0004).

**Ce qu'on fait et lui non.** Réponse à ses discussions « Repository in Asp Core Clean Architecture » (https://github.com/jasontaylordev/CleanArchitecture/discussions/405, 16 commentaires, sans réponse retenue) et « Service Layer » (https://github.com/jasontaylordev/CleanArchitecture/discussions/473, 8) : deux formes de feature et quatre mots de port (ADR 0002, 0003) donnent une réponse écrite et testée là où le concurrent laisse la question ouverte.

### C3. Emplacement et nommage des tests

**Comment fait le concurrent.** Des projets par type de test, un dossier par use case dans le projet fonctionnel.

```
conc: tests/Application.FunctionalTests/TodoLists/Commands/CreateTodoListTests.cs:33  public async Task ShouldCreateTodoList()
conc: tests/Domain.UnitTests/ValueObjects/ColourTests.cs
conc: tests/Application.UnitTests/Common/Mappings/MappingTests.cs
```
Six projets de test ; les tests fonctionnels envoient la commande par le médiateur, jamais par HTTP ; `Infrastructure.IntegrationTests` ne contient que `GlobalUsings.cs`. Le nom dit un attendu (`Should…`).

**Comment on fait.** Un projet de test miroir par projet source, mêmes dossiers, plus `Architecture/`, `EndToEnd/`, `Integration/`, `Load/`.

```
nous: docs/reference/project-layout.md:196   "one test project per source project, mirroring its folders"
nous: Tests/Application/AppTemplate.Application.UnitTests/Features/TodoLists/UseCases/Commands/CreateTodoList/CreateTodoListUseCaseTests.cs:28  public async Task AnAnonymousCaller_IsRefused()
```
18 projets de test, méthodes `Sujet_Comportement`.

**Écart doc/test (fait, haute).** `docs/reference/project-layout.md:5-6` : « Every rule on this page is held by a test ». La règle du miroir (ligne 196) n'est tenue par aucun test : `grep -rn "UnitTests" Tests/Architecture --include=*.cs` ne trouve que `TemplatePackagingTests.cs` (listes de chemins du template) et `Tools/NewUseCase.cs` écrit dans le bon projet sans qu'un test d'architecture le vérifie. Un projet de test déplacé ou manquant passe.

**Verdict : on dépasse** (confiance haute) sur la structure et sur l'expressivité des noms ; défaut propre : règle du miroir non tenue (« fait »).

**À prendre.** Rien. À faire chez nous : un test d'architecture « chaque projet de `Src/` a son projet de test sous `Tests/<Couche>/<Nom>.UnitTests` » (population mesurée sur disque, comme `SourceDeclarations`), ou retirer la promesse de la page.

**À refuser.** Un projet d'intégration vide livré (`conc: tests/Infrastructure.IntegrationTests/GlobalUsings.cs` seul, alors que `conc: README-template.md:57` annonce des tests d'intégration).

**Ce qu'on fait et lui non.** Des tests par HTTP sur un vrai PostgreSQL (`Tests/Integration/AppTemplate.Api.IntegrationTests`) ; lui n'en a aucun (`grep -rn "CreateClient|HttpClient" tests` ne trouve que `conc: tests/Web.AcceptanceTests/AspireSetup.cs:37`).

### C4. Style de fichier, un type par fichier, commentaires

**Comment fait le concurrent.** Un fichier de use case porte plusieurs types ; le fichier se nomme d'après l'opération.

```
conc: src/Application/TodoItems/Commands/CreateTodoItem/CreateTodoItem.cs:6,13
public record CreateTodoItemCommand : IRequest<int>
public class CreateTodoItemCommandHandler : IRequestHandler<CreateTodoItemCommand, int>
```
Fichiers `.cs` : BOM mélangé (70 avec, 40 sans, carte p1) ; 43 lignes de commentaire `//` de prose dans `src/` (`grep -rnE '^\s*//[^#]' src | grep -v '///'`), pour une part des textes Aspire d'origine (`conc: src/ServiceDefaults/Extensions.cs:13-15`). Aucune règle outillée.

**Comment on fait.** UTF-8 avec BOM et LF imposés par `.editorconfig`, un type public par fichier nommé comme lui, aucun commentaire de prose.

```
nous: .editorconfig:5,24-26  end_of_line = lf ; [*.cs] charset = utf-8-bom
nous: Tests/Architecture/.../Rules/LayoutConventionTests.cs:544  EverySourceFile_DeclaresOnePublicType_NamedForTheFile
nous: Tools/CheckComments.cs (1 396 lignes) : 0 commentaire // de prose dans Src/ hors Generated (grep -rnE '^\s*//[^#/]' Src : 0)
```
La règle « commentaires propres » vit dans AGENTS.md, sans ADR ; elle ne vaut que pour le code du projet.

**Verdict : on dépasse** (confiance haute) sur l'exécution ; **choix assumé** (AGENTS.md « Clean comments », pas d'ADR) sur le fond. Une équipe qui veut des commentaires doit changer le gate (`CheckComments.cs`), ce qui est le coût du choix : cf. E7.

**À prendre.** Rien.

**À refuser.** Le mélange BOM et le fichier à plusieurs types : le premier produit des diffs parasites, le second casse « un nom de fichier = un type » dont dépend `F12` (ADR 0003).

### C5. Style déclaré contre style écrit : constructeurs primaires

**Concurrent.** `conc: .editorconfig:184  csharp_style_prefer_primary_constructors = true:suggestion` ; le code en emploie peu (2 classes, `BearerSecuritySchemeTransformer.cs:12`, `Colour.cs:3`) et garde le champ `_context` explicite ailleurs.

**Nous.** `nous: .editorconfig:48  csharp_style_prefer_primary_constructors = false:suggestion`, alors que 208 classes de `Src/` (hors `Generated/` et `Migrations/`) déclarent un constructeur primaire (`grep -rnE "^\s*(public|internal) (sealed )?class [A-Za-z0-9_<>]+\(" Src`), par exemple `nous: Src/Application/AppTemplate.Application/Features/TodoLists/UseCases/Commands/CreateTodoList/CreateTodoListUseCase.cs:13`. La règle `_camelCase` (lignes 83-89) ne concerne donc que les rares classes à constructeur classique.

**Verdict : fait, défaut interne** (confiance haute ; sévérité basse : `suggestion` ne casse rien). Le fichier dit le contraire du code ; une équipe qui suit `.editorconfig` écrit autre chose que l'exemple. À corriger : mettre `true`, et le tenir à `warning`.

### C6. Où vivent les conventions : documentation et ADR

**Concurrent.** Trois ADR (`conc: docs/decisions/ADR-001..003`) ; le projet généré porte un README de 60 lignes. Les READMEs par couche demandés par l'issue #1364 ont été ajoutés puis supprimés : https://github.com/jasontaylordev/CleanArchitecture/issues/1364 est close le 2026-02-19 par « READMEs have been added to each project layer », et `git show --stat 94d3ba8` (#1568, 2026-03-17) supprime `src/{Application,Domain,Infrastructure,Web}/README.md`. Aucun n'existe sur main (`find . -name 'README*'`).

**Nous.** 88 pages de doc dont `docs/reference/project-layout.md` (où va un fichier, comment il se nomme), `docs/architecture/modules-and-boundaries.md`, 28 ADR et un AGENTS.md racine ; un seul AGENTS.md de dossier (`Src/Web/AGENTS.md`). La page de layout est conditionnée par les options.

**Verdict : on dépasse** (confiance haute). Ce qui manque : un lecteur qui ouvre `Src/Domain/` ne trouve rien sur place (pas de README de couche), l'AGENTS.md de dossier prévu par la règle racine n'existe que pour le web.

**À prendre.** Rien de lui (il a retiré la sienne). À envisager : un AGENTS.md de trois lignes par couche qui pointe la page de layout. Risque : duplication, et chaque AGENTS.md est une page de plus à tenir par `DocumentationCitationTests`. Faible priorité, confiance basse.

### C7. Format de la solution (observation)

`conc: CleanArchitecture.slnx` ; `nous: AppTemplate.sln`, alors que le lanceur du même dépôt utilise déjà `Launcher/CleanArch.Create.slnx` (`nous: Tools/Tasks.cs:936`). Onze fichiers citent `AppTemplate.sln` (`grep -rln`). Aucun coût utilisateur prouvé : **à égalité**, confiance basse. À noter seulement si une migration vers `.slnx` est envisagée.

---

## Axe 2. Extensibilité

### E1. Ajouter un use case à une feature existante

**Comment fait le concurrent.** Un gabarit d'élément `ca-usecase` écrit un fichier (commande ou requête, validateur, handler) ; rien à enregistrer, MediatR et FluentValidation scannent l'assembly.

```
conc: src/Application/DependencyInjection.cs:14  builder.Services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());
conc: src/Application/DependencyInjection.cs:16-17  cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly());
conc: README-template.md:40  dotnet new ca-usecase --name CreateTodoList --feature-name TodoLists --usecase-type command --return-type int
```
Persona p3-comp-1 : le gabarit doit être lancé depuis `src/Application` (sinon un dossier mal placé, sans erreur) ; le corps est `throw new NotImplementedException()` ; ni DTO, ni endpoint, ni test. Archive + Search : 2 squelettes, compile du premier coup, mais « un squelette non rempli ou une route oubliée passent tous les deux la compilation ».

**Comment on fait.** `new-use-case` écrit quatre fichiers (commande, interface, validateur, use case) et un test, rejoint le manifeste, imprime l'action de contrôleur prête à coller.

```
nous: docs/guides/adding-a-feature.md:153  Nothing is registered by hand: `AddFeature` in `ApplicationModule` finds the use case and its validator by folder.
nous: Src/Application/AppTemplate.Application/ApplicationModule.cs:156-165  AddFeature : AddValidatorsFromAssembly(filter vertical) + AddUseCases(TypesIn(vertical))
nous: Src/Application/AppTemplate.Application.Core/ApplicationCoreModule.cs:49-84  AddUseCases lève s'il y a 0 ou 2 interfaces nommées
```
Persona p3-ours-1, tâche 2 (Archive puis recherche par nom) : ~9 min actif, `new-use-case` « excellent pour Archive » ; mais `--input` n'offre que `id` ou `none` : pour `SearchProjects` (un texte), quatre fichiers réécrits et le contrôleur imprimé n'est pas utilisable ; le test généré cesse de compiler dès que le use case prend des dépendances ; 3 tours de build (CA1862, test généré, xUnit1051 + IDE0005).

**Écart doc/test (fait, haute).** `nous: Tests/Architecture/.../Rules/UseCaseConventionTests.cs:58` et `:197` disent « registration is explicit rather than scanned » ; le code scanne (`ApplicationModule.cs:156-165`). La page `adding-a-feature.md:153` est exacte ; le message de test est périmé. La carte p1 et le rapport G1 le rangeaient sous « doc contre code » ; c'est le message de test seul.

**Verdict : on dépasse** (confiance moyenne) : même absence d'enregistrement, plus de livré (test, manifeste, action imprimée). Le concurrent garde l'avantage du nombre de fichiers (un contre quatre plus un test).

**À prendre.** Rien du concurrent. À faire : `--input fields:<Nom:type>` pour `new-use-case` (la recherche par nom est le premier besoin réel), et corriger les deux messages de test. Pas d'ADR : c'est une option d'un générateur.

**À refuser.** Le squelette qui compile en lançant `NotImplementedException` sans signal : le nôtre lance aussi `NotImplementedException`, mais `new-use-case` l'annonce et le test généré le couvre.

**Ce qu'on fait et lui non.** Chemin de refus : `new-use-case` refuse une feature inexistante, une feature de forme simple, un use case déjà présent (`nous: docs/guides/adding-a-feature.md:169-172`) ; le gabarit du concurrent n'a aucun contrôle de ce genre.

### E2. Ajouter une feature (entité, persistance, route, doc)

**Comment fait le concurrent.** Pas d'outil. À la main : l'entité, sa configuration, `DbSet` dans l'interface et dans le contexte (deux fichiers), DTO et profil AutoMapper, endpoint (découvert par réflexion).

```
conc: src/Application/Common/Interfaces/IApplicationDbContext.cs:7,9  DbSet<TodoList> TodoLists { get; } ...
conc: src/Infrastructure/Data/ApplicationDbContext.cs:14,16        public DbSet<TodoList> TodoLists => Set<TodoList>();
conc: src/Web/Infrastructure/WebApplicationExtensions.cs:14,24    assembly.GetExportedTypes()...Invoke(null, [group])  // un IEndpointGroup suffit
```
Persona p3-comp-1, tâche 1 (feature avec propriétaire, CRUD, liste paginée) : 5 squelettes, tout le reste à la main ; la première compilation casse (CS0234) parce qu'un dossier de feature nommé `Projects` crée un espace de noms `Acme.Application.Projects` qui masque la classe générée par Aspire `Projects` (`conc: tests/Application.FunctionalTests/FunctionalTestSetup.cs:21`, `Projects.TestAppHost`). Aucune propriété par utilisateur ni pagination dans le template pour s'en inspirer.

**Comment on fait.** `new-feature <Features> <Feature> --access owner|shared ...` écrit un squelette dans les cinq couches, son manifeste et sa page de doc.

```
nous: docs/guides/adding-a-feature.md:18  dotnet run Tools/Tasks.cs new-feature <Features> <Feature> --access owner|shared [--deletion ...] [--model record|aggregate] [--concurrency none|etag]
nous: Tools/NewFeature.cs:1227-1229  "The solution does not compile until edit 1 is made ... nothing here edited a file it does not own." / "Six edits, each by hand, each somewhere an absence should be visible:"
nous: docs/guides/adding-a-feature.md:173-197  « The closed lists a feature joins » : 12 listes, chacune avec le test qui rougit
```
Persona p3-ours-1, tâche 1 : 54 fichiers écrits en 0,7 s ; 6 modifications imprimées (`AppDbContext`, `ApplicationModule`, `PersistenceModule`, `Program.cs`, deux compositions de test), 11 étapes à la main au total avec la migration et deux lignes de doc, ~215 lignes à la main, gate non vert du premier coup (`PendingModelChangesTests`, `format` IDE1006). `openapi` n'est pas imprimé parmi les étapes (p3-ours-1, F5).

**Verdict : on dépasse** (confiance haute). Le concurrent n'offre rien ; nous offrons la forme complète (propriété 404, ETag, pagination, suppression de compte) et un gate qui dit ce qui manque.

**À prendre.** Rien de lui. À faire chez nous, c'est notre propre trou : appliquer les modifications imprimées (`new-feature --apply-edits`), comme `presentation-join` applique les siennes (`nous: docs/template/using-the-template.md:247`), et imprimer `openapi`. Forme : option du générateur ; l'ADR existe déjà en creux (template ADR 0017, Proposed, « an item template cannot edit the registrations, the closed lists and the manifest ») et devrait le dire.

**À refuser.** La découverte par réflexion des features entières : l'absence d'un enregistrement doit se voir (`NewFeature.cs:1229`) et chaque feature doit rester retirable (`remove-feature`). Pour une route ou un contrôleur à l'intérieur d'une feature, notre MVC les découvre déjà (`app.MapControllers()`, `nous: Src/Presentation/AppTemplate.Api/Program.cs:136`).

**Ce qu'on fait et lui non.** Sa discussion « How to decouple Identity from Domain Layer? » (https://github.com/jasontaylordev/CleanArchitecture/discussions/415, 10 commentaires) : chez nous l'identité est un projet à part (`Application.Auth`, `Infrastructure.Auth`, `AuthDbContext`) retirable par option.

### E3. Ajouter un host (worker, MCP, portail, bureau)

**Comment fait le concurrent.** Aucun outil. Un host de plus est un projet à écrire.

```
conc: src/Infrastructure/DependencyInjection.cs:48-72  AddInfrastructureServices enregistre aussi cookies, Identity et sign-in (propres au web)
```
Persona p3-comp-1, tâche 6 : `dotnet new worker` du SDK, `Version=` refusé par la gestion centralisée, `IUser` à fournir, puis l'exécution échoue (« Unable to resolve service for type 'Microsoft.AspNetCore.Routing.EndpointDataSource' while attempting to activate 'AuthorizationPolicyCache' ») : `AddInfrastructureServices` n'est pas neutre ; contournement `ValidateOnBuild = false`. Une seconde web app : copie du `ClientApp` et `AddJavaScriptApp` à la main.

**Comment on fait.** `--presentation` (api, blazor-server, mcp, avalonia, wpf, worker), `dotnet new cleanarch-presentation`, tâches `presentation-scaffold` et `presentation-join`. Les règles sont tenues par test :

```
nous: Tests/Architecture/.../Rules/ModuleDependencyTests.cs:403  NoHost_ReferencesAnotherHost ("Each host composes itself.")
nous: Tests/Architecture/.../Rules/ModuleDependencyTests.cs:421  EveryHost_ComposesTheApplicationLayer
nous: docs/decisions/0022-a-presentation-project-reaches-the-core-through-the-api-or-the-application-layer.md
```
Persona p3-ours-1, 6b (MCP) : 30 s d'outil, 83 fichiers ajoutés et 31 modifiés par l'outil, tests MCP fournis ; deux étapes oubliées par l'outil (migration d'Auth, `openapi`) qui rougissent ensuite. 6a (app web vite-react) : 58 fichiers, 10 étapes imprimées dont 7 sans test.

**Verdict : on dépasse** (confiance haute). Le concurrent n'a pas d'équivalent ; son infrastructure n'est pas neutre pour un second host (fait prouvé par la persona, non rejoué).

**À prendre.** Rien. À faire : que `presentation-join` imprime ou lance la migration d'Auth et `openapi`, qui cassent le gate après coup.

**À refuser.** Un host qui référence un autre host (`NoHost_ReferencesAnotherHost`) : c'est la règle qui garde les jobs et les migrations de développement dans l'API seule.

**Ce qu'on fait et lui non.** Ses discussions « Can you create a schedule jobs solution? » (https://github.com/jasontaylordev/CleanArchitecture/discussions/395, 7 commentaires), « Email services are part of Infrastructure » (https://github.com/jasontaylordev/CleanArchitecture/discussions/685, 9) et son issue « Add Support for Blazor » (https://github.com/jasontaylordev/CleanArchitecture/issues/1444, ouverte, 0 commentaire) ont chez nous une réponse livrée : worker sous bail (ADR 0011, 0025), module Email, `blazor-server` et `blazor-wasm`.

### E4. Changer de provider, remplacer une brique

**Comment fait le concurrent.** La base se choisit à la génération par `#if` dans le code ; une base de plus = une branche de plus.

```
conc: src/Infrastructure/DependencyInjection.cs:26-33  #if (UsePostgreSQL) options.UseNpgsql(...) #elif (UseSqlServer) ... #else options.UseSqlite(...)
conc: src/Application/Common/Interfaces/IApplicationDbContext.cs:7-9  l'application expose les DbSet : EF n'est pas remplaçable
```
Matrice de génération : 3 fronts x 3 bases, 9 variantes construites et testées par `conc: .github/workflows/test-templates.yml:18-20`. Issue ouverte « Add Support for MySQL » (https://github.com/jasontaylordev/CleanArchitecture/issues/1571, 3 commentaires) : « This is not currently planned ». Un seul provider d'e-mail ou de stockage : il n'en livre aucun.

**Comment on fait.** Un provider par projet, choisi à la génération (`--database`), PostgreSQL compilé par défaut, les autres en recouvrement de fichiers entiers ; l'e-mail et le stockage se choisissent par configuration derrière un port.

```
nous: docs/template/decisions/0005-a-project-carries-one-database-provider.md  « A project cannot switch provider by editing a setting: it moves by generating again »
nous: .template.config/variants/{database-sqlite,database-sqlserver}/
nous: Src/Infrastructure/AppTemplate.Infrastructure.Email/EmailModule.cs:39-60  AddTransport : SmtpTransport ou PostmarkTransport selon la configuration, sinon InvalidOperationException
nous: Tests/Architecture/.../Rules/PortConventionTests.cs:17  _maximumOperationsPerPort = 6
```
Un troisième transport d'e-mail se code en modifiant la chaîne de `if` d'`EmailModule` (fait, haute) : l'extension est fermée par le module, voulue par `docs/architecture/modules-and-boundaries.md` (« a project per transport » refusé). MySQL : refusé à la génération (`--my-sql-not-yet-generated`) mais encore listé par `dotnet new cleanarch-webapi --help` (persona p2-ours-1, F6 ; notre issue #165).

**Verdict : choix différent assumé (template ADR 0005)** pour le changement de base après génération, **à égalité** avec le concurrent (aucun des deux ne bascule sans régénérer) ; **on dépasse** pour le remplacement d'une brique (ports de quatre mots, adaptateurs internes tenus par `AdapterVisibilityTests`). Confiance moyenne : un remplacement réel d'adaptateur n'a pas été exécuté.

**À prendre.** Rien. La matrice du concurrent est plus petite que la nôtre (25 jambes, `.template.config/acceptance/matrix.json`).

**À refuser.** Un `IApplicationDbContext` qui expose les `DbSet` : il rend EF permanent (ADR 0003 refuse l'`IQueryable` sur un port).

**Ce qu'on fait et lui non.** Pas de MySQL chez aucun des deux ; nous refusons explicitement et documentons le seuil (« Revisit when »), lui laisse une issue ouverte sans date. Corriger notre aide `--database` serait un gain net (issue #165).

### E5. Comportement transversal : pipeline

**Comment fait le concurrent.** Des behaviours MediatR enregistrés en une ligne chacun, dans l'ordre voulu.

```
conc: src/Application/DependencyInjection.cs:19-22
cfg.AddOpenBehavior(typeof(UnhandledExceptionBehaviour<,>)); AuthorizationBehaviour; ValidationBehaviour; PerformanceBehaviour
conc: src/Web/Program.cs:25-49  le pipeline HTTP est écrit dans le Program.cs de l'équipe : elle y insère ce qu'elle veut où elle veut
```
Coût prouvé : l'ordre des behaviours et l'autorisation portée par la couche HTTP pour 8 requêtes sur 9 (rapport G1 : issue 700, carte p1 W3).

**Comment on fait.** Pas de behaviour : validation, journal et commit dans chaque use case ; le HTTP passe par `UseHttpPipeline()`, un seul appel qui installe tout dans un ordre tenu par test.

```
nous: docs/architecture/request-pipeline.md:27  "A host cannot insert a step in the middle. `RequestPipelineTests` holds the order"
nous: Tests/Architecture/.../Rules/RequestPipelineTests.cs:63  TheHostInstallsTheHttpPipeline_BeforeAnyMiddlewareOfItsOwn
nous: docs/decisions/0003-use-cases-and-ports-are-explicit.md:45  « Revisit when: the project needs behaviour wrapped around every use case that a decorator or the use case itself cannot carry »
```
Pour un comportement autour de tous les use cases, le seul point central est `AddUseCases` (`ApplicationCoreModule.cs:49-67`), qui enregistre par interface ; un décorateur s'y brancherait en un endroit. Mais `grep -rn -i decorat docs Src` ne trouve que la ligne de l'ADR 0003 : aucune recette n'existe.

**Verdict : choix différent assumé (ADR 0003)** pour l'absence de behaviours (confiance haute). Pour le pipeline HTTP fermé : **choix assumé sans ADR** (confiance moyenne), la page dit « cannot insert a step in the middle » mais aucun ADR n'en porte la raison. Le coût pour une équipe qui veut insérer une étape avant l'authentification (un middleware de locataire, par exemple) n'est pas prouvé par une issue ; il est déduit de `RequestPipelineTests:63`.

**À prendre.** Rien de la mécanique. À écrire : une recette « un comportement autour de tous les use cases » dans `docs/architecture/request-pipeline.md` ou `domain-and-results.md` (le décorateur par `AddUseCases`, avec son test), et un ADR court pour le pipeline HTTP fermé. Cela répond à la clause « Revisit when » de l'ADR 0003 sans l'ouvrir.

**À refuser.** Les behaviours MediatR : ils cachent le graphe d'appels (ADR 0003) et la licence de MediatR/AutoMapper est un coût d'utilisateur documenté chez le concurrent (discussions 1346 et 1413, issues 1206 et 1325, citées par G1).

**Ce qu'on fait et lui non.** Sa discussion « Why MediatR? » (https://github.com/jasontaylordev/CleanArchitecture/discussions/1009, 8 commentaires) et l'issue « Should domain events be fired before saving changes? » (https://github.com/jasontaylordev/CleanArchitecture/issues/326, close, 10 commentaires) : chez nous les événements partent après commit par ADR 0006, avec balayage sous bail comme garantie.

### E6. Retirer ce que l'on ne veut pas (exemples, authentification, front)

**Comment fait le concurrent.** Pas d'option pour les exemples ni pour Identity ; retrait à la main.

```
conc: .template.config/template.json  seules options : client-framework (angular|react|none), database
```
Issues : « Add --minimal option to exclude sample code from template » (https://github.com/jasontaylordev/CleanArchitecture/issues/1192, ouverte depuis 2024-06, « This is on the roadmap », 2026-02-19) et « Make Todo & Identity Features Optional During Project Setup » (https://github.com/jasontaylordev/CleanArchitecture/issues/1613, close comme doublon de #1192). Persona p3-comp-1, tâche 7 : retirer Weather = 17 fichiers supprimés, 7 modifiés, `git rm` et `grep` à la main, « rien n'avertit d'un reste » ; TodoLists « non retiré, estimé lourd ».

**Comment on fait.** Options de génération qui retirent (`--examples all|notes|none`, `--auth local|external|none`, `--files`), et `remove-feature` pour une feature générée.

```
nous: docs/guides/removing-features.md:21-25  remove-feature <Features> (rapport) ; --apply supprime un squelette intact
nous: docs/guides/adding-a-feature.md:173-197  liste fermée : « LayoutConventionTests names one you missed » (removing-features.md)
```
Persona p2-ours-1 : projet minimal (`--auth none --examples none --files false`) généré en 5,5 s, bootstrap 50 s, 798 tests verts. Persona p3-ours-1 : retirer Notes à la main = 25 min actif, `remove-feature` ne supprime rien sans manifeste (rapport seul : 7 dossiers, 12 enregistrements, 27 usages, 109 « possibles » dont du bruit) ; `--apply` n'agit que sur un squelette intact (Widgets : 29 fichiers en une commande).

**Verdict : on dépasse** (confiance haute). Une limite à garder en tête : après modification, le retrait d'une feature reste à la main chez nous aussi, mais guidé par des tests qui rougissent (tâche 7a : 2 tests d'architecture rouges avant corrections).

**À prendre.** Rien.

**À refuser.** Rien à refuser ; sa demande #1613 (Identity optionnelle) est exactement ce que `--auth none` livre.

**Ce qu'on fait et lui non.** `--examples none` et `--auth none` répondent à ses issues #1192 et #1613. Ses demandes de fronts (« Add Support for Vue », https://github.com/jasontaylordev/CleanArchitecture/issues/1570 ; « Support SvelteKit with Static Adapter for SPA Client-Only Applications », https://github.com/jasontaylordev/CleanArchitecture/issues/1625, « SvelteKit isn't planned right now ») ont un équivalent partiel chez nous : `--web vite-vue`, `vite-svelte` (SPA, pas SvelteKit ; SvelteKit est dans notre issue #119 « Later »), `blazor-wasm`, `react-router`, `angular`, `vite-react`, `placeholder` (7 valeurs, `.template.config/template.json`).

### E7. Modifier l'outillage livré avec le projet

**Comment fait le concurrent.** Il n'y a pas d'outillage dans le projet : le gabarit `ca-usecase` est installé avec le paquet NuGet et décrit par `conc: templates/ca-use-case/.template.config/template.json`. Personnaliser = forker le paquet. Les scripts `build/*.ps1` sont exclus du paquet (carte p1).

**Comment on fait.** `Tools/` est dans l'arbre généré : 14 980 lignes de C# dont `NewFeature.cs` 5 891 et `Tasks.cs` 2 134 (`wc -l Tools/*.cs`). Une équipe qui veut changer la forme d'un générateur édite ces fichiers ou les éjecte.

```
nous: docs/template/decisions/0017-a-project-runs-its-tasks-from-a-pinned-dotnet-tool.md:3,79,86  Status: Proposed ; « Keep the copied tools. Every team keeps a fork of 11,600 lines it did not write » ; « Code kept as strings in the generators ... A team could change it only by ejecting the whole command »
nous: docs/template/roadmap.md  (le « dotnet cleanarch » est annoncé, issue #163, rien de livré)
```
Le fichier `.feature-manifest/` retient le SHA-256 de chaque fichier généré et `remove-feature --apply` refuse dès qu'un fichier a été édité : une équipe qui personnalise perd le retrait automatique (voulu, mais non annoncé au moment de la modification).

**Verdict : choix différent assumé (ADR template 0017, Proposed, non livré)**, confiance haute. L'état d'aujourd'hui est une dette que l'ADR nomme lui-même ; le concurrent ne la partage pas parce qu'il offre moins.

**À prendre.** Rien du concurrent. À mener : livrer `dotnet cleanarch` (ADR 0017-0019) ou, d'ici là, une section « changer un générateur » dans `docs/template/maintaining-the-template.md` pour les équipes.

**À refuser.** Un item template seul pour la génération de feature : il ne peut pas éditer les enregistrements ni le manifeste (ADR 0017, alternative écartée).

**Ce qu'on fait et lui non.** Des garde-fous qui rougissent quand une équipe casse une règle : persona p3-ours-1, tâche 8, un `ProjectReference` interdit passe le build et fait rougir `ModuleDependencyTests.TheDomainLayer_ReferencesNothingOutsideItself` en 9 s. Chez le concurrent (persona p3-comp-1, T8) rien n'avertit : un paquet EF ajouté au Domain donne « 0 erreur, 0 signal ». C'est ce qui permet à une équipe de changer le template sans le casser sans le savoir.

---

## Récapitulatif des verdicts

| Id | Sujet | Verdict | Confiance |
|---|---|---|---|
| C1 | Nommage et règles d'analyse | on dépasse (jeu plus mince en symboles) | haute |
| C2 | Organisation feature/couche | choix assumé (ADR 0002, 0004, 0007) | haute |
| C2b | Longueur des chemins | retard limité, suivi #166 | moyenne |
| C3 | Tests : emplacement et nom | on dépasse ; règle du miroir non tenue | haute |
| C4 | Style de fichier, commentaires | on dépasse (AGENTS.md pour le fond) | haute |
| C5 | Constructeurs primaires | défaut interne | haute |
| C6 | Doc des conventions | on dépasse | haute |
| C7 | `.slnx` | à égalité | basse |
| E1 | Ajouter un use case | on dépasse | moyenne |
| E2 | Ajouter une feature | on dépasse ; modifications imprimées | haute |
| E3 | Ajouter un host | on dépasse | haute |
| E4 | Provider et briques | assumé (template ADR 0005) / à égalité sur la base | moyenne |
| E5 | Pipeline transversal | assumé (ADR 0003) ; ADR et recette manquent | moyenne |
| E6 | Retirer exemples, auth | on dépasse | haute |
| E7 | Outillage modifiable | assumé (template ADR 0017, Proposed) | haute |

## Ce qui n'a pas été vérifié

- Aucun build ni test lancé : les temps et nombres d'étapes de p3 sont ceux des personas.
- La longueur de chemin de Windows n'est pas reproduite ; seule la longueur relative est mesurée.
- « 208 constructeurs primaires » : décompte par expression régulière sur `Src/` (hors `Generated/`, `Migrations/`).
- Que la règle du miroir de tests ne soit tenue par aucun test : relu par `grep` sur `Tests/Architecture` et `Tools/`, pas par une tentative de la violer.
- L'effet d'un décorateur branché sur `AddUseCases` : lu, non essayé.
- Les licences de MediatR et AutoMapper (non vérifiées dans les dépôts, cf. carte p1 D11).
