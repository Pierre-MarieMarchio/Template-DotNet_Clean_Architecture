# Carte fusionnée du concurrent (JasonTaylorDev/CleanArchitecture, branche main)

Dépôt lu : `/tmp/claude-1000/-home-pmmarchio-code-Template-DotNet-Clean-Architecture/608f3f2d-3c02-4530-9e46-6f8e7e0e445b/scratchpad/repos/competitor-main`
(noté `main/`), comparé à `.../repos/competitor-release` (tag v10.8.0, noté `rel/`).
Sources fusionnées : `out/p1-map-comp-A.md` (carte A) et `out/p1-map-comp-B.md` (carte B).
Méthode : chaque désaccord a été revérifié dans le code ou par commande (section « Désaccords »). Aucun build du dépôt
concurrent ni du dépôt de l'équipe ; une seule compilation, dans un projet jetable du scratchpad (désaccord D7).

Socle vérifié : `git rev-parse HEAD` = `345e7fe7612fdaf0cb5da58826a7c57d42e1fdd8`, `git describe` = `v10.8.0-58-g345e7fe`,
date du HEAD 2026-10-26, 976 commits, 257 fichiers suivis. v10.8.0 = `e5c08ec`, publiée le 2026-03-23T22:10:22Z
(`gh api repos/JasonTaylorDev/CleanArchitecture/releases/latest`, GET), toujours la dernière release.

## 0. Résumé

- Template `dotnet new ca-sln` (paquet NuGet `Clean.Architecture.Solution.Template`, `CleanArchitecture.nuspec:5` ; identité du template
  `Clean.Architecture.Solution.CSharp`, `.template.config/template.json:18`, nom court `ca-sln`). Une solution CQRS MediatR, un seul exemple
  (TodoLists, TodoItems, WeatherForecasts), organisée par feature dans Application.
- Deux options seulement : `--client-framework angular|react|none` et `--database sqlite|sqlserver|postgresql` (`-cf`, `-db`).
  Pas d'option d'authentification (Identity local en dur), pas d'option « exemples », pas de mode sans HTTP.
- Aucune règle d'architecture exécutable : pas de test d'architecture, `.editorconfig` entièrement en `suggestion`/`silent`
  (19 règles de nommage, `.editorconfig:196-268`), seul `TreatWarningsAsErrors` (`Directory.Build.props:5`) tient la qualité. Un
  `#pragma warning disable CS1998` livré (`GetWeatherForecastsQuery.cs:12-14`).
- Pas de pagination dans le code (retirée par `a6b7655`, #1587, incluse dans v10.8.0). Pas de propriété des données : tout utilisateur
  authentifié voit et modifie tout. Pas de migrations : `EnsureDeletedAsync` puis `EnsureCreatedAsync` à chaque démarrage en Development.
- Erreurs par exceptions traduites en ProblemDetails par un `IExceptionHandler`. `Result` n'existe que pour Identity.
- Tests : 32 méthodes NUnit (33 exécutions), 6 scénarios Reqnroll/Playwright, 3 tests Angular (vitest), 0 test React.
  `Infrastructure.IntegrationTests` est vide. Aucune couverture mesurée. Les tests fonctionnels passent par le médiateur, jamais par HTTP.
- Outillage : Aspire AppHost + TestAppHost, item template `ca-usecase`, scripts PowerShell dans `build/`. Pas de CLI propre, pas de
  lanceur de tâches, pas de Docker Compose ni de Dockerfile.
- Vie du projet : pas de CHANGELOG ; notes de version = corps de la GitHub Release recopié dans le nuspec. Dernière release 2026-03-24 ;
  depuis, 58 commits dont 51 de dépendances (Renovate) et 7 commits de Jason Taylor.

## 1. Projets, références, règles

Solution `CleanArchitecture.slnx` (29 lignes) : 7 projets `src/` (AppHost, ServiceDefaults, Application, Domain, Infrastructure, Shared, Web)
et 6 projets `tests/` (Application.FunctionalTests, TestAppHost, Application.UnitTests, Domain.UnitTests,
Infrastructure.IntegrationTests, Web.AcceptanceTests). Ce dernier est conditionné par `<!--#if (!UseApiOnly)-->` aux lignes 25-27.

| Projet | Références projet | Paquets notables |
|---|---|---|
| Domain | aucune | `MediatR.Contracts` seul (ADR-003) |
| Application | Domain | Ardalis.GuardClauses, AutoMapper, FluentValidation.DependencyInjectionExtensions, MediatR, EF Core, Microsoft.Extensions.Hosting |
| Infrastructure | Application, Shared (pas Domain, transitif) | EF Core, provider (Sqlite, Npgsql, SqlServer via Aspire), Identity.EntityFrameworkCore |
| Web | Application, Infrastructure, ServiceDefaults | OpenApi, Scalar, Identity, Azure Key Vault, HealthChecks.EFCore, EF Design |
| Shared | aucune | noms de services Aspire ; redéclare `TargetFramework`, `ImplicitUsings`, `Nullable` déjà dans `Directory.Build.props` |
| ServiceDefaults | aucune | OpenTelemetry, Http.Resilience, ServiceDiscovery |
| AppHost | Web, Shared (`IsAspireProjectResource=false`) | `Aspire.AppHost.Sdk/13.6.4`, Aspire.Hosting.* |
| TestAppHost | Shared | hébergement de la base seul |

Tests : Application.FunctionalTests référence Shared, Web, TestAppHost (`tests/Application.FunctionalTests/Application.FunctionalTests.csproj:35-37`) ;
Application.UnitTests référence Application et Infrastructure (Infrastructure inutile pour ses 3 fichiers) ;
Infrastructure.IntegrationTests ne référence rien et ne contient que `GlobalUsings.cs`.

Règles :
- `Directory.Build.props` : net10.0, `TreatWarningsAsErrors=true`, `ImplicitUsings`, `Nullable`, `ArtifactsPath`. Exception `WarningsNotAsErrors=NU1608` pour PostgreSQL.
- `Directory.Packages.props:4-5` : gestion centrale des versions + `CentralPackageTransitivePinningEnabled` ; épingle de sécurité
  `System.Security.Cryptography.Pkcs` 10.0.14 (lignes 33-34).
- `.editorconfig` : 382 lignes, 19 règles de nommage à `suggestion`, `file_scoped:silent` (ligne 181), aucun `dotnet_diagnostic.*.severity`.
- Aucun `NetArchTest`/`ArchUnit` ; le graphe `ProjectReference` est la seule garde des couches. Elle ne retient pas tout : Web référence
  Infrastructure et l'utilise (`src/Web/Endpoints/Users.cs:1,12`), Application.UnitTests référence Infrastructure.
- Application référence EF Core, assumé par ADR-001 : pas de repository, `IApplicationDbContext` expose les `DbSet`.
- `global.json` : SDK 10.0.403, `rollForward: latestFeature`.
- 3 ADR réels (`docs/decisions/` : 001 EF Core dans Application, 002 Aspire, 003 MediatR.Contracts dans Domain, plus le gabarit 000), rien qui les tienne par test.

## 2. Chemin d'une requête

Écriture `POST /api/TodoItems` :
1. `src/Web/Program.cs:7-12` enchaîne les `Add*` ; `MapEndpoints(typeof(Program).Assembly)` ; découverte par réflexion des `IEndpointGroup`
   (`src/Web/Infrastructure/WebApplicationExtensions.cs:12-27`), préfixe `/api/{NomDeClasse}`, `operationId` = nom de méthode.
2. Endpoint `TodoItems.cs:9-28`, `RequireAuthorization()` au groupe (ligne 13), la commande MediatR est aussi le corps HTTP, retour `Created<int>`.
   Les mises à jour comparent l'id d'URL et l'id du corps et répondent `BadRequest()` sans corps de problème (`TodoItems.cs:34-35`, `TodoLists.cs:43`).
3. Pipeline MediatR (`src/Application/DependencyInjection.cs:16-23`) : pré-processeur `LoggingBehaviour`, puis `UnhandledExceptionBehaviour`,
   `AuthorizationBehaviour`, `ValidationBehaviour` (validateurs exécutés en parallèle, `Task.WhenAll`), `PerformanceBehaviour` (avertissement > 500 ms, ligne 37).
4. Validation FluentValidation, 4 validateurs pour 7 commandes (voir D1). Handler : `new TodoItem{...}`, `Add`, `SaveChangesAsync`, renvoie l'id.
   Aucune vérification d'existence de `ListId` (clé étrangère ; l'effet exact n'a pas été exécuté).
5. `SaveChanges` : `AuditableEntityInterceptor` (Created/LastModified) et `DispatchDomainEventsInterceptor`.
6. Erreurs : `ProblemDetailsExceptionHandler` : Validation 400, NotFound (Ardalis) 404, Unauthorized 401, Forbidden 403.

Lecture `GET /api/TodoLists` : `GetTodosQuery` (seule requête portant `[Authorize]`, `GetTodos.cs:9`), `AsNoTracking().ProjectTo<TodoListDto>().OrderBy(Title).ToListAsync()`,
aucun filtre par utilisateur, aucun `Skip/Take`. La lecture paginée n'existe pas.

## 3. Concepts

| Sujet | Réalité |
|---|---|
| CQRS | Par convention de dossiers `Commands/` et `Queries/`, tout est `IRequest` MediatR ; pas de modèles de lecture/écriture séparés |
| Médiateur | MediatR 14.2.0 ; licence du paquet non vérifiable dans le dépôt (voir D12) |
| Erreurs | Exceptions de bout en bout ; `Result` (`Common/Models/Result.cs:3-24`) sert seulement à `IIdentityService` |
| NotFound | `Guard.Against.NotFound` d'Ardalis, mappé en 404 |
| Domain events | Levés dans le setter de `TodoItem.Done` (false vers true) ; publiés dans `SavingChanges(Async)`, donc AVANT l'écriture ; version synchrone en `.GetAwaiter().GetResult()` ; un seul handler qui journalise. ADR-003 écrit « après `SaveChangesAsync` » : la doc contredit le code |
| Entités | `BaseEntity` (id `int`), `BaseAuditableEntity`, setters publics : modèle anémique ; `Colour` en value object (`OwnsOne`) |
| Mapping | AutoMapper 16.2.0 (`ProjectTo` en lecture, affectation manuelle en écriture) ; `AssertConfigurationIsValid` testé |
| Autorisation | `RequireAuthorization()` par groupe + `[Authorize]` sur la requête traité par `AuthorizationBehaviour` ; un seul `[Authorize]` sur 9 requêtes (voir W3) ; seul rôle : `Administrator`, jamais exigé |
| Authentification | Identity local, `MapIdentityApi` sous `/api/Users` ; cookies (SPA) ou Bearer (API seule) ; pas d'OIDC, pas de choix |
| Propriété des données | Inexistante : aucun `OwnerId`, aucune règle 404/403 par propriétaire |
| CORS | `AllowAnyMethod().AllowAnyHeader().AllowAnyOrigin()` (`Program.cs:28-31`) |
| Base | `EnsureDeleted` + `EnsureCreated` à chaque démarrage en Development (`Program.cs:17-19`), seed `administrator@localhost` / `Administrator1!`, pas de dossier Migrations |

## 4. Conventions

- Par feature dans Application (`<Feature>/{Commands,Queries,EventHandlers}/<UseCase>/`), par couche ailleurs (Domain, Infrastructure, Web).
- Fichier du use case sans suffixe (`CreateTodoItem.cs` : commande + handler), validateur à part. Commandes en `record`, constructeurs classiques avec `_context`.
- Namespaces fichier, `GlobalUsings.cs` par projet, extensions d'enregistrement dans le namespace `Microsoft.Extensions.DependencyInjection`.
- Fichiers `.cs` : BOM mélangé, 70 avec et 40 sans (`git ls-files '*.cs'` + `xxd`) ; LF imposé depuis main par `.gitattributes`.
- Commentaires : pas de règle outillée.
- Tests : un fichier par use case sous `tests/Application.FunctionalTests/<Feature>/{Commands,Queries}/`.

## 5. Outillage

- Options du template : `ClientFramework` (Angular défaut, React, None), `Database` (sqlite défaut, sqlserver, postgresql), 8 paramètres de port
  générés aléatoirement (voir D5), `caPackageVersion`, `caRepositoryUrl`, `caDocsUrl`. Mécanisme : `#if (UseXxx)` dans les `.cs`, `<!--#if-->`
  dans les `.csproj` et `.slnx`, modificateurs d'exclusion et de renommage. Le dépôt compile lui-même (variante SQLite + Angular).
  Matrice : 3 x 3 = 9 combinaisons générées, construites et testées par `test-templates.yml`, déclenché sur push vers main et à la main, pas avant la fusion d'une PR.
- Dérive de `template.json` et de `dotnetcli.host.json` (voir D6) ; `src/Web/Web.http` appelle `/Identity/Account/Login` qui n'existe plus ;
  les deux fichiers `.http` appellent `GET /api/TodoItems?ListId=1&PageNumber=...&PageSize=...`, une route paginée retirée (voir D9).
- Item template `ca-usecase` (`templates/ca-use-case/`) : un fichier (commande ou requête + validateur + handler à `throw new NotImplementedException()`),
  ni endpoint, ni test, ni doc. Empaqueté par le nuspec (`.\**`), exclu du template solution.
- Scripts : `build/build.ps1`, `build/repack.ps1`, `build/test.ps1` (PowerShell seulement, exclus du paquet). Pas de CLI propre.
- Aspire : `AddAzureContainerAppEnvironment("aca-env")`, base selon le provider, URL Scalar, front-end JavaScript en mode run seulement ;
  environnement par défaut Production hors mode run (`src/AppHost/Extensions.cs`, #1661). ServiceDefaults : OpenTelemetry, résilience,
  découverte, health checks `/health` et `/alive` en Development seulement.
- Docker : requis pour sqlserver et postgresql seulement (`README.md:21`, ADR-002). Ni Dockerfile ni compose. `.devcontainer` : image python 3.13 + docker-in-docker, 8 Go, pas de SDK .NET déclaré.
- Front-ends : Angular 22 (NgModule, vitest) et React 19 + Vite 8 (JSX non typé, aucun test). Client TypeScript généré par NSwag (`"nswag": "latest"` non épinglé, `package.json:38`) depuis `wwwroot/openapi/v1.json`.
- OpenAPI : `AddOpenApi` + transformateurs, Scalar sur `/scalar`, résumé et description par endpoint.
- Renovate : lundi, automerge non-majeurs, groupes .NET/Angular/React/tests, TypeScript Angular figé en major/minor.

## 6. Tests

| Projet | Contenu | Méthodes | Exécutions |
|---|---|---|---|
| Domain.UnitTests | `ColourTests` | 6 | 6 |
| Application.UnitTests | RequestLogger 2, ValidationException 3, Mapping 2 méthodes (dont 1 avec 2 `TestCase`) | 7 | 8 |
| Application.FunctionalTests | 8 classes, 7 commandes + `GetTodos` | 19 | 19 |
| Infrastructure.IntegrationTests | rien | 0 | 0 |
| Web.AcceptanceTests | 4 features, 6 scénarios (Login 2, Home 1, Weather 1, Counter 2) | 6 | 6 |
| Angular (vitest) | 2 fichiers `spec.ts`, 3 `it()` | -- | 3 |
| React | aucun | 0 | 0 |

Total .NET NUnit : 32 méthodes, 33 exécutions (hors scénarios Reqnroll).
- Base des tests fonctionnels pilotée par Aspire (`TestAppHost`) + Respawn ; pas de Testcontainers (ADR-002, retiré par `a824c72`, #1536).
- Les tests envoient par `TestApp.SendAsync` (médiateur) : aucun test .NET n'appelle HTTP ; `grep -rn 'CreateClient|HttpClient' tests` ne trouve que `AspireSetup.cs:37`.
  `IUser` est un mock Moq ; état statique partagé `TestApp._userId`/`_roles` (`TestApp.cs:13-14`) donc tests non parallélisables.
- Aucun test de 403/`ForbiddenAccessException` (grep vide), aucun test d'événement de domaine, un seul test d'autorisation (`ShouldDenyAnonymousUser`, `GetTodosTests.cs:50-58`).
- Couverture : `coverlet.collector` 10.1.0 référencé, aucun rapport, aucun seuil. `NUnit.Analyzers` seul analyzer du dépôt.
- CI : `build.yml` (PR vers main : restore, build Release, Playwright, `dotnet test` de la solution) ; `test-templates.yml` (9 variantes, après push) ; `codeql.yml` ; `release.yml`.
- `README-template.md:57` annonce des tests « unit, integration, and functional » alors que le projet d'intégration est vide.

## 7. Vie du projet

- Pas de CHANGELOG ; `release.yml` copie le corps de la Release dans le nuspec, la version vient du tag.
- 16 releases v10.x publiées entre 2025-11-11 et 2026-03-24 (v10.0.0 puis 15 de v10.1.0 à v10.8.0) ; 22 tags au total depuis v6.0.8 ; aucune release depuis 7 mois.
- Site de documentation hors dépôt ; dans le dépôt seulement `docs/decisions/`. `README.md:84` dit Angular 21 alors que main est en Angular 22.
- 58 commits non publiés : 51 de `renovate[bot]`, 7 de Jason Taylor (ADR #1595, Aspire 13.6 #1656, Production à la publication #1661, schedule Renovate #1658, pin Pkcs #1663, Angular 22 #1662, React #1667).

## 8. main contre v10.8.0

`git diff --shortstat v10.8.0..HEAD` : 56 fichiers, +6138/-14622 (bruit : CRLF vers LF par `.gitattributes`, lockfiles npm).
Avec `--ignore-cr-at-eol` et sans `package-lock.json` : 45 fichiers, +457/-262. Fichiers `.cs`/`.csproj` modifiés : `AppHost.csproj`, `AppHost/Extensions.cs`,
`Application.csproj` (retrait de `Microsoft.Build.Tasks.Core` et `Utilities.Core`), `ServiceDefaults/Extensions.cs` (un commentaire seulement), `TestAppHost.csproj`.
Seul changement de comportement C# : l'environnement par défaut de l'AppHost. Le reste : bumps (Aspire 13.2 vers 13.6.4, nunit 5.0.0, EF Core 10.0.14,
Playwright 1.64.1), Angular 22 + vitest, ADR, `aspire.config.json`, `.gitattributes`. Les 7 commits de fond ne sont pas livrés aux utilisateurs du paquet NuGet.

## 9. Points forts et faiblesses prouvés

Forts : F1 use case en 1 ou 2 fichiers, sans repository (ADR-001) ; F2 matrice 9 variantes construites et testées en CI (`test-templates.yml`) ;
F3 Aspire intégré, tests fonctionnels contre une vraie base sans Docker pour SQLite (ADR-002, non exécuté) ; F4 item template `ca-usecase` ;
F5 publication NuGet automatisée par la release GitHub, Renovate (51 des 58 commits) ; F6 OpenAPI par endpoint + Scalar + client TypeScript généré.

Faibles : W1 aucune règle d'architecture exécutable ; W2 ni propriété ni pagination ; W3 autorisation applicative portée par la couche HTTP pour 8 requêtes sur 9 ;
W4 tests fonctionnels hors HTTP, pas de couverture, projet d'intégration vide, aucun test React ; W5 pas de migrations (`EnsureDeleted` en Development) ;
W6 doc contre code (ADR-003, `README-template.md:57`, `README.md:84`, `template.json`, `.http`) ; W7 pas de CHANGELOG, 7 mois sans release ;
W8 scripts PowerShell seulement ; W9 CORS ouvert à toute origine.

## 10. Désaccords

Entrée : 15 points relevés entre les cartes (D1 à D15). Chaque ligne a été revérifiée dans le code ou par commande.

### D1. Nombre de validateurs FluentValidation
- A : 4 validateurs (CreateTodoItem, UpdateTodoItem, CreateTodoList, UpdateTodoList) ; UpdateTodoItemDetail et les Delete n'en ont pas.
- B : « un validateur par commande ».
- Preuve : `find src/Application -name '*Validator*.cs'` renvoie 4 fichiers ; `grep -rn AbstractValidator` renvoie 4 classes ; `grep` des `IRequest` donne 7 commandes
  (CreateTodoItem, UpdateTodoItem, UpdateTodoItemDetail, DeleteTodoItem, CreateTodoList, UpdateTodoList, DeleteTodoList) et 2 requêtes.
- Qui avait raison : A. B a tort (4 validateurs pour 7 commandes).

### D2. Nombre de tests .NET
- A : 32 méthodes (33 exécutions avec les 2 `TestCase`) ; Application.UnitTests 7 méthodes, 8 exécutions.
- B : 33 tests (6 Domain + 8 Application.UnitTests + 19 fonctionnels).
- Preuve : `grep -E '^\s*\[Test\]'` : Domain 6, Application.UnitTests 7, Functional 19 = 32 ; `[TestCase` : 2 (`MappingTests.cs:37-38`) ; 32 + 2 - 1 (le `[Test]` doublé sur le `[TestCase]`) = 33 exécutions.
- Qui avait raison : les deux. Même fait, unité différente ; B appelle « tests » des exécutions, A distingue méthodes et exécutions. La carte fusionnée garde les deux nombres.

### D3. Tests Angular
- A : 2 specs. B : 3 tests dans 2 fichiers.
- Preuve : `authorize.interceptor.spec.ts` 1 `it(`, `counter.component.spec.ts` 2 `it(` ; React : aucun fichier de test.
- Qui avait raison : les deux. A compte des fichiers, B des tests.

### D4. Releases et tags, date de v10.0.0
- A : 16 releases v10.x entre 2025-11-10 et 2026-03-24 ; v10.0.0 le 2025-11-10 ; 15 tags de v10.1.0 à v10.8.0.
- B : « 22 tags (v10.0.0 le 2025-11-10, v10.8.0 le 2026-03-24) » présenté comme cadence.
- Preuve : `git tag | wc -l` = 22 ; `git tag | grep -c '^v10'` = 16 ; le plus ancien tag est v6.0.8 (2022-06-01) ; `gh api .../releases` : v10.0.0 publiée 2025-11-11T17:22:55Z,
  tag daté 2025-11-11 18:10 +0100 ; v10.1.0 le 2026-02-28 ; v10.8.0 le 2026-03-23T22:10Z.
- Qui avait raison : A en partie. A a raison sur 16 releases v10 et 15 de v10.1.0 à v10.8.0 ; B donne le total de tous les tags, pas la cadence v10. Les deux datent v10.0.0 du 10 au lieu du 11 novembre 2025 : erreur commune.

### D5. Symboles de port du template
- A : « 6 symboles de port cachés en plus » (résumé) et « 12 symboles de ports » (section 5), deux nombres qui se contredisent.
- B : ports aléatoires par plage, lignes 28-203, sans compter.
- Preuve : script Python sur `template.json` : 8 symboles `generator: port` (kestrelHttp, kestrelHttps, appHostHttp, appHostHttps, appHostOtlpHttp, appHostOtlpHttps,
  appHostResourceHttp, appHostResourceHttps), chacun avec un paramètre et un `coalesce` : 24 entrées. `dotnetcli.host.json` cache 10 noms dont `appHostMcpHttpPort` et `appHostMcpHttpsPort`, absents de `template.json`.
- Qui avait raison : aucune pour le nombre (ni 6 ni 12 ; 8 ports réels). B n'avance aucun chiffre faux.

### D6. Entrées périmées de `template.json`
- A : exclusions de fichiers de test inexistants (`PostgreSQLTestcontainersTestDatabase.cs`, `SqlTestDatabase.cs`, `SqliteTestDatabase.cs`, `appsettings*.json` de tests), `src/Web/Pages/**` (ligne 350), symboles hôte Mcp.
- B : mêmes fichiers de test (309-316), `.azure/**/*` (ligne 298), symboles hôte Mcp ; ne cite pas `Pages`.
- Preuve : `grep -n` de `template.json` : lignes 298, 309-316, 350 ; `git ls-files | grep -E '\.azure|Pages/|PostgreSQLTest|SqliteTest|SqlTest'` ne remonte que `tests/Web.AcceptanceTests/Pages/*` (autre dossier) et les `appsettings*.json` de `src/Web` et `src/AppHost` ;
  `ls -a` : pas de `.azure`. Les exclusions `src/Web/appsettings*.json` (305-308) sont vivantes (les 4 fichiers existent).
- Qui avait raison : les deux, complémentaires. La liste fusionnée est l'union.

### D7. Un use case généré par `ca-usecase` casse-t-il la compilation (CS1998 avec `TreatWarningsAsErrors`) ?
- A : « devrait casser » (déduction, non vérifiée par build). B : n'en parle pas.
- Preuve : lecture de `templates/ca-use-case/.../CleanArchitectureUseCase.cs` : `public async Task Handle(...) { throw new NotImplementedException(); }`, sans `await`.
  Essai dans `scratchpad/merge-cs1998` (SDK 10.0.301, `TreatWarningsAsErrors=true`, `Nullable`, `ImplicitUsings`) : `async Task N() { throw new NotImplementedException(); }`,
  `async Task M() { }` et `async Task<int> H() { return 1; }` compilent avec 0 avertissement, même avec `-p:WarningLevel=9999 -p:WarningsAsErrors=CS1998`.
  Le même projet signale bien `CS0219` promu en erreur, donc le harnais détecte les avertissements.
- Qui avait raison : aucune de façon établie. L'affirmation de A n'est pas reproduite avec le SDK 10.0.301 ; le dépôt épingle 10.0.403, non disponible ici, et le `#pragma` du dépôt montre qu'un SDK antérieur a signalé CS1998.
  La carte fusionnée retire la phrase et la classe en « non confirmé ».

### D8. Numéros de ligne du `.slnx`
- A : lignes 1-26, acceptation aux lignes 19-21. B : lignes 1-29, condition à la ligne 20.
- Preuve : `wc -l CleanArchitecture.slnx` = 29 ; `cat -n` : `<!--#if (!UseApiOnly)-->` ligne 25, projet ligne 26, `<!--#endif-->` ligne 27.
- Qui avait raison : le fait (Web.AcceptanceTests conditionné par `!UseApiOnly`) est juste chez les deux ; A se trompe sur le total (26) et sur la ligne (19-21), B sur la ligne (20) mais pas sur le total.

### D9. Existe-t-il encore une trace de pagination dans le dépôt ?
- A : aucun résultat applicatif ; la `PaginatedList` a existé et a été retirée par `a6b7655` (#1587).
- B : « aucun résultat dans `src`, `tests`, `templates`, `docs`, README, pour main comme pour v10.8.0 ».
- Preuve : `git merge-base --is-ancestor a6b7655 v10.8.0` vrai ; `a6b7655` daté 2026-03-24 07:24:54 +1000, `v10.8.0` 07:26:02 ; `grep -rIl -i 'paginat|PageNumber|PageSize'` hors `.git`/`node_modules` :
  `src/Web/Web.http:74-76` et `src/Web/Web-webapi.http:85-87` appellent `GET /api/TodoItems?ListId=1&PageNumber=...&PageSize=...`, route qui n'existe plus (`TodoItems.cs` n'a pas de GET).
- Qui avait raison : A. Le fond de B est juste (pas de pagination dans le code) mais sa formulation « aucun résultat dans `src` » est fausse à cause des deux `.http` ; aucune des deux cartes n'a signalé ces appels périmés.

### D10. Ligne de la prérequis Docker dans `README.md`
- A : `README.md:21-23`. B : `README.md:19`.
- Preuve : `sed -n 15,24p README.md` : la ligne Docker est la ligne 21 (la ligne 19 est .NET SDK).
- Qui avait raison : A (B décalé de deux lignes) ; le contenu est identique et confirmé : Docker seulement pour SQL Server et PostgreSQL.

### D11. Licence de MediatR 14 et AutoMapper 16
- A : ne conclut pas, demande à vérifier. B : « licence commerciale chez l'éditeur depuis la v13, non vérifié dans le dépôt ».
- Preuve : `grep -i licensekey` sur `src` : rien ; aucun paquet dans `~/.nuget/packages`. Versions confirmées : `Directory.Packages.props:9,14`.
- Qui avait raison : indéterminé. Aucune carte n'a de preuve ; B s'appuie sur une connaissance externe. Reste non vérifié.

### D12. Changements C# entre v10.8.0 et main
- A : « aucun changement de comportement hormis l'AppHost » ; « 5 fichiers C#/csproj ». B : « tout identique sauf `src/AppHost/Extensions.cs` » (et `Application.csproj` séparément).
- Preuve : `git diff --ignore-cr-at-eol --name-only v10.8.0..HEAD | grep -E '\.(cs|csproj)$'` : `AppHost.csproj`, `AppHost/Extensions.cs`, `Application.csproj`, `ServiceDefaults/Extensions.cs`, `TestAppHost.csproj`.
  `ServiceDefaults/Extensions.cs` : un seul commentaire modifié (« .NET Aspire » vers « Aspire »).
- Qui avait raison : A (le compte de 5 est exact) ; B omet `ServiceDefaults/Extensions.cs`, sans conséquence de comportement.

### D13. Faits présents chez une seule carte, revérifiés
| Fait | Carte | Verdict | Preuve |
|---|---|---|---|
| `Web.http` appelle `/Identity/Account/Login` disparu | A seule | vrai | `src/Web/Web.http:8,17` ; `Users.cs` mappe `MapIdentityApi` sous `/api/Users` ; aucun `AddRazorPages` |
| README main a supprimé le paragraphe sur la recréation de la base, le code la fait toujours | A seule | vrai | `rel/README.md:80-85` contient « deleted, recreated » ; absent de `README.md` ; `ApplicationDbContextInitialiser.cs:45-46` |
| `.cs` : BOM mélangé | A seule | vrai | 70 avec BOM, 40 sans |
| `"nswag": "latest"` | A seule | vrai | `src/Web/ClientApp/package.json:38` |
| Infrastructure ne référence pas Domain | A seule | vrai | `Infrastructure.csproj:27-28` |
| Validateurs en parallèle | A seule | vrai | `ValidationBehaviour.cs` : `Task.WhenAll` |
| Aucun test 403 | A seule | vrai | `grep -rn -i 'Forbidden|403' tests --include=*.cs` vide |
| `README.md:84` dit Angular 21, main est en 22 | B seule | vrai | `README.md:84` ; `package.json` Angular 22 |
| `README-template.md:57` annonce des tests d'intégration | B seule | vrai | ligne 57 ; `Infrastructure.IntegrationTests` vide |
| `TestApp` garde un état statique | B seule | vrai | `TestApp.cs:13-14` |
| 400 sans corps si l'id d'URL diffère | B seule | vrai | `TodoItems.cs:34-35`, `TodoLists.cs:43` |
| `/health` et `/alive` en Development seulement | B seule | vrai | `ServiceDefaults/Extensions.cs:101-116` |
| 976 commits, 257 fichiers suivis | B seule | vrai | `git rev-list --count HEAD`, `git ls-files | wc -l` |
| Seul `GetTodosQuery` porte `[Authorize]` (8 requêtes sur 9 sans) | A et B | vrai | `grep -rn '\[Authorize' src` : 1 résultat applicatif |

### D14. Docker requis pour Application.FunctionalTests en SQLite
- A et B : non exécuté, déduit de `README.md` et d'ADR-002. Pas de désaccord ; pas de preuve d'exécution. Le texte du README (`README.md:21`) et d'ADR-002 dit que SQLite n'en a pas besoin.

### D15. Qu'est-ce qui cadre un use case fraîchement généré ?
- A : le gabarit ne génère « ni endpoint, ni test, ni doc ». B : « ni endpoint, ni test, ni DTO ». Même fait, deux listes.
- Preuve : le dossier `templates/ca-use-case/` ne contient que les deux gabarits `.cs` (Commands, Queries) et `.template.config`.
- Qui avait raison : les deux.

## 11. Non vérifié

- Aucun build ni test du dépôt concurrent ; les comptes de tests viennent du `grep`, pas d'une exécution. Les tests ne sont pas prouvés verts.
- `dotnet new ca-sln` et les 9 variantes : non exécutés.
- Besoin de Docker pour les tests fonctionnels en SQLite : non exécuté.
- Licences de MediatR et AutoMapper : non vérifiées.
- Effet d'un `ListId` inexistant à la création (500 supposé par A) : non exécuté.
- CS1998 sur un use case généré avec le SDK 10.0.403 épinglé : non reproductible ici (SDK 10.0.301 seul).
- Contenu détaillé du code Angular et React, GitHub Releases autres que `latest` : non lus.
