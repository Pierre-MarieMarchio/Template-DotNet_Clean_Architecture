# Phase 5, groupe G3 : tests et qualité, CI et release (analyste opus)

Clones lus en lecture seule, sans build ni génération :
- nous : `repos/ours`, develop `5f22faa5` (le `templateVersion` `1.1.0-dev` de l'arbre est un artefact de l'audit), noté `ours:` ;
- concurrent : `repos/competitor-main`, main `345e7fe`, noté `comp:` ; tag `repos/competitor-release` (v10.8.0) pour ce que le paquet NuGet livre, noté `rel:`.

Racine des chemins : `/tmp/claude-1000/-home-pmmarchio-code-Template-DotNet-Clean-Architecture/608f3f2d-3c02-4530-9e46-6f8e7e0e445b/scratchpad/`.
État GitHub lu par `gh api` en GET (REST et GraphQL en lecture) le 2026-10-30.
Matière reprise et revérifiée : cartes p1 fusionnées, inventaire p1, personas p2 et p3, socle p4. Les projets générés par les personas (`personas/*/work/*`) ont servi de preuve de ce qu'un utilisateur reçoit. Quand mon compte diffère d'une carte, je le dis.

---

## Résumé

- **Tests** : nous dépassons nettement sur la quantité, la variété des types et les garde-fous (architecture, contrat, couverture plancher, analyseurs stricts, format vérifié). Le concurrent a un avantage réel sur deux points : une boucle de retour bien plus courte (`dotnet test` en 40 s, CI en 3 min) et des tests d'interface dans le navigateur (Reqnroll + Playwright) que nous n'avons pas.
- **CI et release** : sur le papier, nous dépassons partout : CI livrée au projet généré, matrice de génération jouée avant la fusion, publication sans secret avec attestation, actions épinglées par SHA. **Dans l'état actuel, notre release est cassée** : `release-please` échoue à chaque push sur develop depuis le 2026-10-28, à cause du BOM de `.template.config/template.json`. La PR de release #91 est figée à « 1.1.0 », alors que develop contient deux changements cassants. Aucun gate ne l'a vu.
- **Exposition commune** : les deux templates transforment tout avis NuGet en erreur de build. Le concurrent en souffre aujourd'hui (issue #1675, 120 erreurs sur un projet neuf). Nous avons le même mécanisme et aucun contrôle planifié du paquet publié.

---

## Axe 1. Tests et qualité

### 1.1 Types de tests et volume

**Concurrent.** Cinq projets NUnit et un TestAppHost Aspire. Les comptes viennent de `grep '[Test]'` : Domain 6, Application.UnitTests 7 méthodes (8 exécutions), Application.FunctionalTests 19, Infrastructure.IntegrationTests 0 (le projet ne contient que `GlobalUsings.cs`), plus Web.AcceptanceTests avec 6 scénarios Reqnroll (4 fichiers `.feature`). Les tests « fonctionnels » passent par le médiateur, jamais par HTTP :
```csharp
public static async Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request)
{   using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
    var mediator = scope.ServiceProvider.GetRequiredService<ISender>();
    return await mediator.Send(request); }
```
Références : `comp:tests/Application.FunctionalTests/Infrastructure/TestApp.cs:16-23`. L'état utilisateur est statique (`TestApp.cs:13-14`, `_userId`, `_roles`). La doc annonce plus que ce qui existe : « The solution contains unit, integration, and functional tests. » (`comp:README-template.md:57`), alors que le projet d'intégration est vide. Aucun test d'architecture : la discussion #731 demande comment imposer la règle des couches, et la réponse renvoie vers ArchUnitNET (https://github.com/jasontaylordev/CleanArchitecture/discussions/731).

**Nous.** Sous `Tests/`, 18 projets xUnit v3 plus le lanceur, 3 321 `[Fact]/[Theory]`, plus 135 pour le lanceur, soit 3 456 au total (`grep -rE '^\s*\[(Fact|Theory)' Tests Launcher | wc -l`). Les types sont tabulés, avec ce que chacun prouve et ce qu'il ne doit jamais prouver (`ours:docs/guides/testing.md:17-31`) : unitaire, architecture, intégration, contrat, end-to-end, charge. L'intégration passe par HTTP, contre PostgreSQL :
```csharp
using var response = await intruder.GetAsync(new Uri($"{TodoListsRoute}/{listId}", UriKind.Relative), TestToken);
response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
using var ownerResponse = await owner.GetAsync(...);   // la contre-épreuve
ownerResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
```
Références : `ours:Tests/Integration/AppTemplate.Api.IntegrationTests/Security/OwnershipIsolationTests.cs:15-29`. Le contrat OpenAPI est committé et comparé (`OpenApi/OpenApiDocumentTests.cs:52`). Les tests d'architecture portent 17 contre-épreuves `...IsSensitive...` réparties dans 16 fichiers ; je retrouve le compte de la carte. La politique de test est une ADR : « Each risk is held by one type of test » (`ours:docs/decisions/0020-one-risk-one-type-of-test.md`).

**Verdict : on dépasse. Confiance haute.** Le volume ne prouve rien à lui seul, mais nos tests portent sur des risques que le concurrent ne teste jamais : l'isolation par propriétaire (aucun test 403/404 chez lui, cf. carte p1), le contrat HTTP et les règles de couches.

**À prendre** : rien sur ce point.
**À refuser** : les tests qui passent par le médiateur au lieu de HTTP. Ils ne voient ni l'autorisation, ni la sérialisation, ni le statut renvoyé (ADR 0020, ligne « The contract is held where a client reads it »). L'état statique partagé est à refuser aussi : il interdit la parallélisation.

### 1.2 Outils de test

**Concurrent.** NUnit 5.0.0, NUnit.Analyzers 4.15.0, Moq 4.21.0, Shouldly 4.3.0, Respawn 7.0.0, coverlet.collector 10.1.0, Microsoft.NET.Test.Sdk (VSTest), Aspire.Hosting.Testing 13.6.4, Reqnroll.NUnit 3.3.4, Microsoft.Playwright 1.64.1 (`comp:Directory.Packages.props:12-38,54`).

**Nous.** xUnit v3 4.0.2 sur Microsoft.Testing.Platform (`ours:global.json:7-9`), Shouldly 4.3.0, NSubstitute 6.2.0, NetArchTest.Rules 1.3.2, Testcontainers 4.16.0 (MsSql 4.15.0), Microsoft.AspNetCore.Mvc.Testing, Microsoft.Testing.Extensions.CodeCoverage 18.12.0 (`ours:Directory.Packages.props:137-159`). Les choix sont écrits : `ours:docs/decisions/0001-adopt-the-generated-baseline.md:73`.

**Verdict : choix différent assumé (ADR 0001). Confiance haute.** Les deux piles sont courantes. La nôtre est plus récente (MTP, xUnit v3). Un défaut de notre côté : `Testcontainers.MySql` reste dans `ours:Directory.Packages.props:154`, alors que MySQL est refusé à la génération (carte p1, D23).

**À prendre** : rien. Respawn n'apporte rien face à notre `TRUNCATE ... RESTART IDENTITY CASCADE` (`ours:Tests/Integration/AppTemplate.Api.IntegrationTests/Infrastructure/DatabaseServer.cs:49`).

### 1.3 Base de données des tests

**Concurrent.** Aspire orchestre la base des tests :
```csharp
var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.TestAppHost>(...);
_app = await builder.BuildAsync(cancellationToken).WaitAsync(cancellationToken);
await _app.ResourceNotifications.WaitForResourceHealthyAsync(Services.Database, cancellationToken);
```
Références : `comp:tests/Application.FunctionalTests/FunctionalTestSetup.cs:20-41` et `comp:tests/TestAppHost/Program.cs:11-20`. SQLite est le défaut et se passe de Docker ; PostgreSQL et SQL Server en ont besoin (`comp:docs/decisions/ADR-002-Aspire-For-Orchestration-And-Testing.md:32-38`). Les utilisateurs bloquent quand Docker manque : discussion #1194, « Docker file is misconfigured » (https://github.com/jasontaylordev/CleanArchitecture/discussions/1194).

**Nous.** Testcontainers, avec l'image épinglée par digest :
```csharp
internal const string Image = "postgres:18.6-alpine3.23@sha256:1a0c23d5...";
private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(Image).WithDatabase(database)...Build();
```
Références : `ours:Tests/Integration/AppTemplate.Api.IntegrationTests/Infrastructure/DatabaseServer.cs:7-14`. Une variante SQLite tourne sur un fichier, sans conteneur (`ours:docs/guides/testing.md:36-40`). `test --no-integration` exclut les projets qui référencent Testcontainers (`ours:Tools/Tasks.cs:432-462`).

**Verdict : choix différent assumé (ADR 0001, ligne 73 ; Aspire figure parmi les « Later » de l'issue #122). Confiance moyenne.** Le concurrent a l'avantage d'un seul outil pour le développement et les tests. Nous avons celui d'images épinglées par digest, tenues par `ContainerImagePinningTests`.

**À refuser** : Aspire comme socle de test. Il couple la suite à l'orchestrateur, et son ADR-002 reconnaît ce couplage (`comp:docs/decisions/ADR-002...md:37`).

### 1.4 Vitesse de la boucle de retour

**Concurrent.**
- `dotnet test` sur un projet neuf prend 40 s (`out/p3-comp-1.md:19`).
- CI GitHub : Build sur PR en 3 min en moyenne (11 max, n=20), Test Templates en 5 min (15 max, n=9). Source : `gh api repos/jasontaylordev/CleanArchitecture/actions/runs?status=success`, durées `updated_at - run_started_at`.

**Nous.**
- `test --no-integration` : 1 min 12 s.
- `verify` : 3 min 58 s à 5 min 11 s (`out/p3-ours-1.md:27`, `out/p3-ours-2.md:43`).
- CI sur PR : 10 min en moyenne (25 max, n=12). Template acceptance : 35 min sur PR (40 max), 26 min sur push. Même source `gh api`.

Le persona p3-ours-2 a lancé 16 `verify`, dont 6 rouges, chacun à environ 4 min. Deux de ces échecs venaient de diagnostics que `dotnet build` n'avait pas montrés (IDE1006, IDE0005) (`out/p3-ours-2.md:18,138`).

**Verdict : en retard sur la vitesse de la boucle interne. Confiance moyenne.** C'est en partie le prix de 3 400 tests. Le défaut précis : rien de rapide ne dit « ce que `verify` dira ». La cause de l'écart build/verify n'est pas établie : le persona l'a observé, je ne l'ai pas reproduit.

**À prendre (sous notre forme)** : une tâche `check` qui enchaîne `format --verify-no-changes`, le build et les tests d'architecture, en moins d'une minute, citée par `AGENTS.md` comme boucle de travail. Pas d'ADR : c'est un outil, pas un choix.

### 1.5 Couverture

**Concurrent.** `coverlet.collector` est référencé (`comp:tests/Application.FunctionalTests/Application.FunctionalTests.csproj:18`). Aucun workflow ni script ne produit de rapport ou n'impose de seuil (`comp:.github/workflows/build.yml:65-66` : `dotnet test --no-build --configuration Release`).

**Nous.**
- Plancher : `89` dans `ours:coverage.minimum:2`.
- CI : `--coverage` en Cobertura, puis `Tools/CoverageGate.cs` fusionne les rapports et compare au plancher (`ours:.github/workflows/ci.yml:106-147`). Localement, la tâche `coverage` fait de même (`ours:Tools/Tasks.cs:955-990`).
- Le fichier est tenu par `CoverageFloorTests` : pas de BOM, une seule valeur, entre 1 et 100, lue de la même façon par le shell et par .NET (`ours:Tests/Architecture/AppTemplate.Architecture.Tests/Rules/CoverageFloorTests.cs:13-55`). Les réglages sont tenus par `CoverageSettingsTests`.
- La politique est écrite : « Coverage is a tripwire » (ADR 0020).

**Verdict : on dépasse. Confiance haute.** Trois défauts sont pourtant à corriger :
1. **Le plancher n'est prouvé dans aucune variante.** `coverage.minimum` vaut 89 dans tout projet généré (vérifié dans `personas/p2-ours-1/work/Acme.DnDefault/coverage.minimum` et `personas/p2-ours-2/work/LNiMin/coverage.minimum`). Le `ci.yml` généré l'impose (`Enforce coverage floor`, ligne 119 du projet généré). Mais aucune jambe d'acceptance ne lance la couverture (`grep -n -i coverage .github/workflows/template-acceptance.yml` ne renvoie rien). Une variante sous 89 % échouerait à la première PR de l'adoptant. Je n'ai pas vérifié qu'une variante descend sous ce seuil.
2. **Mesure et contrôle n'utilisent pas la même configuration.** Le commentaire du plancher dit « set a few points under a Debug measurement » (`ours:coverage.minimum:1`, et `testing.md:228-231`), alors que la CI mesure en Release (`ours:.github/workflows/ci.yml:25`). Gravité faible.
3. **`coverage` a planté sous WSL2 pour les deux personas** (`PAL_SEHException`, « Zero tests ran »), alors qu'un projet témoin hors template, avec les mêmes versions, passait (`out/p3-ours-2.md:150`). La cause n'est pas établie et la doc n'en dit rien.

**À refuser** : une couverture sans seuil, comme chez le concurrent.

### 1.6 Gate local

**Concurrent.** Aucune commande de gate : le README généré dit seulement `dotnet test`. `build/build.ps1` et `build/test.ps1` sont exclus du paquet (`comp:CleanArchitecture.nuspec:33`, `.\build\**`) et demandent PowerShell. Le persona a trouvé que le gate ne voit pas une migration oubliée : « Le gate n'attrape pas une migration manquante (24 tests OK avec ou sans) » (`out/p3-comp-1.md:41`).

**Nous.** `verify` enchaîne :
```csharp
RequireInitialMigrations(task, contexts); Gates(repoRoot);
... Step("dotnet", "format", solution, "--verify-no-changes", "--no-restore");
Step("dotnet", ["build", ...]); Step("dotnet", ["test", ...]);
Step("dotnet", "ef", "migrations", "has-pending-model-changes", ...);
VulnerablePackages(solution);
```
Références : `ours:Tools/Tasks.cs:651-685`. Chaque gate prouve d'abord qu'il sait échouer (`--self-test`, `ours:docs/guides/build-and-ci.md:63-67`).

**Verdict : on dépasse. Confiance haute.** Le persona l'a vérifié : « le gate attrape tout » (`out/p3-ours-2.md:14`). Il y a deux trous. `verify` ne couvre pas le web (pas de `pnpm lint/test/build`, `out/p3-ours-1.md:103`), alors que la CI le couvre par `web.yml` (`ours:.github/workflows/web.yml:57-66`). Et un retrait de feature laisse des fichiers périmés que rien ne signale (`out/p3-ours-2.md:118`).

### 1.7 Analyseurs et style

**Concurrent.** `TreatWarningsAsErrors` seulement (`comp:Directory.Build.props:5`). Il n'a ni `AnalysisLevel`, ni `EnforceCodeStyleInBuild` (le grep sur `*.props` et `*.csproj` ne renvoie rien). Son `.editorconfig` compte 19 règles en `suggestion` et 2 en `:warning` (`.editorconfig:77,114`), qui ne s'appliquent pas au build sans `EnforceCodeStyleInBuild`. Seul analyseur tiers : NUnit.Analyzers. La CI ne vérifie pas le format.

**Nous.** `EnableNETAnalyzers`, `AnalysisLevel latest-recommended`, `EnforceCodeStyleInBuild`, `TreatWarningsAsErrors`, `NuGetAudit` en mode all, niveau low (`ours:Directory.Build.props:11-22`). La CI vérifie le format (`ours:.github/workflows/ci.yml:71-73`). `Tools/CheckComments.cs` refuse les commentaires de prose. Les personas confirment l'effet : CA1862, IDE0005, xUnit1051 et IDE1006 remontent en erreur (`out/p3-ours-1.md:116`).

**Verdict : on dépasse. Confiance haute.**

**Défaut doc contre code, déjà relevé en p1 (F2) et revérifié.** La règle « never a repository-wide NoWarn » (`ours:.editorconfig:91`, `AGENTS.md:90`, `docs/guides/build-and-ci.md:70-72`) est contredite à quatre endroits :
- `ours:Directory.Build.props:27` (`<NoWarn>$(NoWarn);CS1591;CS1573</NoWarn>`) ;
- `ours:Src/Presentation/AppTemplate.Api/AppTemplate.Api.csproj:16` (`AV0029;AV0030`) ;
- `ours:.editorconfig:57-59` (trois `severity = none` globaux) ;
- `ours:Src/Presentation/AppTemplate.Api/Common/Hosting/HttpPipelineModule.cs:67-69` (`#pragma warning disable ASP0000`).

Aucun test ne tient cette règle. Correction : soit une section justifiée de l'`.editorconfig` par cas, soit une règle reformulée et une ADR (CS1591 est le prix de `GenerateDocumentationFile`, qui active IDE0005). Puis un test dans `Tests/Architecture` qui refuse tout `<NoWarn>` et tout `#pragma` hors `Generated/`.

### 1.8 Tests d'interface (navigateur)

**Concurrent.** Reqnroll + Playwright : 4 features, 6 scénarios (Login, Home, Weather, Counter), sur la pile lancée par Aspire (`comp:tests/Web.AcceptanceTests/Features/Login.feature`, `AspireSetup.cs`). La CI du dépôt les joue après l'installation de Chromium (`comp:.github/workflows/build.yml:60-66`), comme chaque jambe front de la matrice (`test-templates.yml:70-77`). Hors CI, ils échouent d'emblée : il faut installer les navigateurs, ce que le README ne dit pas (`out/p3-comp-1.md:19`, `out/p2-comp-2-journal.md:138`).

**Nous.** Aucun test qui exécute une page dans un navigateur. Les jambes web vérifient par `curl` et Python que chaque app sert sa page, que sa CSP couvre ses scripts inline et qu'elle relaie vers l'API (`ours:.github/workflows/template-acceptance.yml:671-700`). Le web a ses tests unitaires (`pnpm test`, `web.yml:63`). Playwright figure dans « Later: around the apps (Aspire, Playwright, Storybook, Expo or Capacitor) » (https://github.com/Pierre-MarieMarchio/Template-DotNet_Clean_Architecture/issues/122).

**Verdict : en retard. Confiance moyenne.** Un bundle qui plante au chargement, une hydratation SSR cassée ou un login OIDC qui boucle passent tous nos contrôles. C'est précisément « a cause the first cannot see » au sens de l'ADR 0020.

**À prendre (sous notre forme)** : un smoke Playwright par starter, écrit en TypeScript dans l'espace pnpm (`Src/Web`), joué par `web.yml` et par les jambes `web` de l'acceptance : la page charge sans erreur console, et un login local aboutit. Pas en Reqnroll/.NET : nos apps web n'ont pas de projet .NET. Il faut une ligne dans `testing.md` (type « Browser smoke ») et un amendement de l'ADR 0020.
**À refuser** : la couche Gherkin. Six scénarios ne paient pas un DSL de plus, et la discussion #1311 montre que les utilisateurs ne distinguent déjà plus acceptance et functional (https://github.com/jasontaylordev/CleanArchitecture/discussions/1311).

### 1.9 Ce qu'on peut faire qu'il ne fait pas (tests)

- **Compter les requêtes SQL d'un endpoint.** La discussion #1608 (https://github.com/jasontaylordev/CleanArchitecture/discussions/1608) le demande. Personne ne le fait : nous n'avons d'intercepteur de commandes que pour l'injection de fautes (`ours:Tests/Integration/AppTemplate.Infrastructure.Auth.IntegrationTests/Fixtures/OneTimeFault.cs:43`). Cela pourrait prendre la forme d'un assistant d'intégration `CountingCommands` et d'un test sur les listes paginées (risque N+1). Priorité basse.
- **Répondre au besoin de la discussion #731** (imposer les couches par test) : c'est déjà fait chez nous (`LayerDependencyTests`, `ModuleDependencyTests`). Cela mérite d'être dit dans le README.

---

## Axe 2. CI et release

### 2.1 La CI que reçoit le projet généré

**Concurrent.** Aucune. Le nuspec exclut les quatre workflows et `renovate.json` du paquet :
```xml
...;.\.github\workflows\release.yml;.\.github\workflows\codeql.yml;.\.github\workflows\build.yml;...;.\renovate.json;.\build\**;.\.github\workflows\test-templates.yml;...
```
Références : `comp:CleanArchitecture.nuspec:33`, identique au tag v10.8.0 (`rel:CleanArchitecture.nuspec`). C'est vérifié sur les 13 projets générés par les personas p2-comp et p3-comp : aucun n'a de dossier `.github` (`ls -a personas/p2-comp-*/work/*`). L'option `PipelineProvider` (GitHub Actions ou Azure Pipelines) a été retirée par `e30d9b3` (#1584, 2026-03-21), alors que les marqueurs `#if (!UseApiOnly)` subsistent dans `build.yml:40,60` sans plus servir. Les utilisateurs s'en servaient : discussion #1122 (https://github.com/jasontaylordev/CleanArchitecture/discussions/1122).

**Nous.** Le projet généré reçoit `ci.yml`, `codeql.yml`, `release.yml`, `web.yml` (avec un front), `sonarqube.yml` (avec l'option), `dependabot.yml` et le modèle de PR. C'est vérifié dans `personas/p2-ours-1/work/Acme.DnDefault/.github/`. `ci.yml` construit, teste avec couverture et plancher, lance les gates `hygiene`, l'audit des paquets, l'image (non root, port 8080 seul), Compose, l'end-to-end bloquant et le smoke k6 non bloquant (`ours:.github/workflows/ci.yml:28-442`).

**Verdict : on dépasse. Confiance haute.**

### 2.2 Prouver ce que le template génère

**Concurrent.** `test-templates.yml` croise 3 fronts et 3 bases, soit 9 jambes : génération, build, `npm ci && npm run build`, `dotnet test` (`comp:.github/workflows/test-templates.yml:16-20,47-77`). Il ne tourne **qu'après la fusion** sur main, ou à la main :
```yaml
on:
  push:
    branches: [ main ]
  workflow_dispatch:
```
Références : `test-templates.yml:3-6`. Les PR ne jouent que `build.yml` (le dépôt lui-même) et CodeQL. Conséquence mesurée : sur les 50 derniers runs Test Templates sur main, **11 échecs**. Par exemple, la PR Renovate #1670, fusionnée par `renovate[bot]` avec `build` et `Analyze` verts, a cassé la jambe `react-sqlserver` à l'étape `Test solution` (run 39630944581). D'autres échecs suivent #1664, #1637, #1630 et #1650. Sources : `gh api .../actions/runs?event=push&branch=main`, `.../pulls/1670`, `.../commits/<sha>/check-runs`.

**Nous.** `template-acceptance.yml` joue sur chaque PR vers develop ou main, et sur chaque push :
- 25 jambes choisies par forme et non par produit cartésien (`ours:.template.config/acceptance/matrix.json`), dont 3 sur `windows-latest` ;
- une sélection des jambes par chemins modifiés, qui joue tout en cas de doute (`template-acceptance.yml:53-105`) ;
- un job de refus des combinaisons interdites (`template-acceptance.yml:939-1000`) ;
- des assertions de non-fuite du nom du template et des autres providers (lignes 134-167) ;
- l'aller-retour du générateur de feature (ligne 429).

Le choix est écrit, alternative rejetée comprise : « A fast subset on pull requests, the full matrix nightly. A break in a skipped shape is found after the merge, by someone else » (`ours:docs/template/decisions/0010-a-pull-request-runs-the-legs-its-change-can-affect.md`, Alternatives).

**Verdict : on dépasse (choix assumé, ADR template 0010). Confiance haute.** Les 11 échecs post-fusion du concurrent illustrent exactement le risque que l'ADR 0010 écarte.

**Coût chez nous** : 35 min par PR en moyenne (`gh api`, n=6), contre 5 min chez le concurrent. C'est assumé par l'ADR 0010, qui prévoit d'y revenir si cela devient trop lent.

**À refuser** : la matrice après fusion et le produit cartésien complet. Avec nos options (7 web × 6 présentations × 4 bases × 3 auth), ce produit est irréaliste ; les formes le remplacent.

### 2.3 Mises à jour de dépendances

**Concurrent.** Renovate, le lundi, avec fusion automatique de tout ce qui n'est pas majeur, y compris les GitHub Actions :
```json
{ "description": "Automerge non-major updates", "matchUpdateTypes": ["minor","patch","digest"], "automerge": true },
{ "description": "Group GitHub Actions updates", "matchManagers": ["github-actions"], "groupName": "GitHub Actions", "automerge": true }
```
Références : `comp:renovate.json:14-31`. Le flux est réel : 51 des 58 commits depuis v10.8.0 viennent de Renovate (carte p1). La fusion automatique ne passe que par `build` et CodeQL, pas par la matrice (2.2).

**Nous.** Dependabot, hebdomadaire, sur 5 écosystèmes (nuget, github-actions, docker, npm, docker-compose), par groupes, avec `target-branch: develop` dans le dépôt template. Rien n'est fusionné automatiquement (`ours:.github/dependabot.yml:5-198`). Le projet généré reçoit le même fichier. 28 commits dependabot à ce jour.

**Verdict : on dépasse sur la sûreté ; le concurrent va plus vite. Confiance moyenne.** Sa fusion automatique sans la matrice produit des main rouges (2.2).

**À refuser** : la fusion automatique tant que la vérification complète ne la précède pas. Si on la veut un jour, elle ne doit porter que sur les correctifs et passer par les checks requis du ruleset (ADR template 0010), jamais par un simple `build`.

### 2.4 Avis de sécurité NuGet et fraîcheur du paquet publié

**Concurrent.** `TreatWarningsAsErrors` (`comp:Directory.Build.props:5`) et l'audit NuGet par défaut du SDK 10. Une seule épingle transitive de sécurité (`Directory.Packages.props:33-34`, Pkcs). Résultat : un projet neuf généré depuis 10.8.0 ne compile pas (120 erreurs NU1902/NU1903) : issue ouverte, 0 réponse, https://github.com/jasontaylordev/CleanArchitecture/issues/1675 (« The last release (10.8.0) was created 7 months ago... the nuget package didn't get the update »). Les deux personas p2-comp et p3-comp ont buté dessus (`out/p3-comp-1.md:17`, `out/p2-comp-2-journal.md:69-72`).

**Nous.** Le même mécanisme, en plus strict : `NuGetAudit` en mode all, niveau low, avec `TreatWarningsAsErrors` (`ours:Directory.Build.props:15,19-21`). Un job CI `vulnerable-packages` (`ci.yml:239-265`). La doc dit quoi faire : « the answer is a transitive pin in `Directory.Packages.props` ..., never a relaxed audit level » (`ours:docs/guides/build-and-ci.md:161-165`).

**Verdict : à égalité. Confiance haute.** Notre doc aide l'adoptant, mais rien ne protège le paquet déjà publié. Nos seuls workflows planifiés sont CodeQL (`grep -rn "schedule:" .github/workflows` ne renvoie que `codeql.yml:9`). Le jour où un avis touche une dépendance de la 1.0.0, `dotnet new cleanarch-webapi && dotnet build` échouera comme chez le concurrent.

**Ce qu'on peut faire qu'il ne fait pas** : un workflow planifié (quotidien ou hebdomadaire) qui installe **la dernière version publiée sur nuget.org** (`dotnet new install CleanArch.WebApi.Templates`), génère les défauts et compile. En cas d'échec, il ouvre ou signale une issue, ce qui déclenche un correctif, donc une release. C'est une réponse directe à #1675, qui est la faiblesse visible du concurrent aujourd'hui. Si on veut fixer la cadence de correctif, cela mérite une ADR template (« a published version is rebuilt on a schedule »).

### 2.5 Versioning et changelog

**Concurrent.** La version vient du tag de la GitHub Release, et le corps de la release devient les notes du nuspec :
```yaml
run: echo "value=${GITHUB_REF_NAME#v}" >> $GITHUB_OUTPUT
... root.find('.//n:releaseNotes', ns).text = os.environ.get('RELEASE_NOTES', '')
```
Références : `comp:.github/workflows/release.yml:28-50`. Il n'a pas de CHANGELOG. La cadence va par rafales : 14 releases en mars 2026, puis aucune depuis 7 mois, avec 58 commits non publiés (inventaire p1, carte p1).

**Nous.** release-please sur develop, en Conventional Commits. La section CHANGELOG est générée, et un `!` signifie une version majeure (`ours:CHANGELOG.md:6-8`, `.github/release-please/config.json`). Un garde vérifie que le tag égale `templateVersion` (`ours:.github/workflows/template-release.yml:67-77`). main n'avance que par fast-forward vers la release (`template-release.yml:205-230`, ADR template 0006).

**Verdict : on dépasse dans la conception. Confiance haute.** Le fonctionnement actuel, lui, est en retard (2.8). Un trou mineur : rien n'impose le format Conventional Commits aux titres de PR, alors que release-please en dépend. Aucun check de titre n'existe dans `.github/workflows`, seul `CONTRIBUTING.md:65` le demande. Une PR fusionnée avec un titre non conventionnel disparaît du changelog sans bruit.

### 2.6 Publication du paquet

**Concurrent.** Le workflow réagit à `release: published`. Il utilise `nuget/setup-nuget@v4` avec `nuget-version: 'latest'`, installe Mono, lance `nuget pack`, puis `nuget push` avec une **clé API stockée** (`${{secrets.NUGET_API_KEY}}`) et `-SkipDuplicate` (`comp:.github/workflows/release.yml:3-5,20-26,52-62`). Il n'y a ni test du paquet avant envoi, ni attestation, ni environnement protégé.

**Nous.** Le pack se fait au tag. Avant d'envoyer, le paquet est installé dans un hive isolé ; le projet par défaut est généré et compilé, puis le lanceur est testé depuis son paquet (`template-release.yml:86-104`). Viennent ensuite une attestation de provenance (`actions/attest-build-provenance`, lignes 106-109), l'**envoi par Trusted Publishing** (OIDC, aucune clé stockée), dans l'environnement `nuget` qui attend un relecteur (`template-release.yml:141-174`), puis un miroir GitHub Packages (ADR template 0007). Une version déjà publiée fait échouer l'envoi : « a release is never replaced » (ligne 170).

**Verdict : on dépasse. Confiance haute.**
**À refuser** : la clé API longue durée, `nuget-version: 'latest'` et `-SkipDuplicate` à l'envoi vers nuget.org (qui masquerait une version déjà publiée).

### 2.7 Supply chain des workflows

**Concurrent.** Actions référencées par tag de version majeure (`actions/checkout@v7`, `actions/setup-dotnet@v6`, `github/codeql-action/init@v4`) et nuget `latest`. Renovate fusionne seul les mises à jour d'actions (`renovate.json:24-31`). Permissions `contents: read` au niveau du workflow (`build.yml:20-21`, `release.yml:7-8`).

**Nous.** Toutes les actions sont épinglées par SHA, avec un commentaire de version. C'est **imposé par un gate** :
```csharp
private static readonly Regex ShaPin = new(@"^[\w\-./]+@[0-9a-f]{40}$", RegexOptions.Compiled);
... problems.Add($"{name}: job '{jobName}' uses '{uses.Value}' without a 40-char SHA pin");
```
Références : `ours:Tools/CheckWorkflows.cs:65,240-242`. Le même gate impose les permissions par job ou par workflow (lignes 197-199). Les images sont épinglées par digest (`ContainerImagePinningTests`). Le binaire k6 est vérifié par SHA-256 (`ci.yml:362-375`). Le `release.yml` du projet généré produit un SBOM et une provenance signée de l'image (`ours:.github/workflows/release.yml:249-256`).

**Verdict : on dépasse. Confiance haute.**

**Défaut doc contre code (ADR 0001, lignes 33-34, et `build-and-ci.md:148-154`)** : la règle « Every dependency is at an exact version », qui place le SDK dans `global.json`, est fausse pour le SDK. `ours:global.json:3-4` porte `"version": "10.0.100", "rollForward": "latestFeature"`, donc tout SDK 10.0.x l'emporte ; le Dockerfile de l'API, lui, utilise 10.0.403 (carte p1). Le concurrent épingle 10.0.403 avec le même roll-forward (`comp:global.json`). Correction : ou la doc dit « le SDK minimum », ou `global.json` épingle la version que la CI et le Dockerfile utilisent, avec `rollForward: latestPatch`.

**Défaut doc contre code (faible)** : `build-and-ci.md:83` dit que `ci.yml` tourne sur « push and pull request to `main` ». En réalité, il tourne sur les PR vers `main` et `develop`, plus `workflow_dispatch` (`ours:.github/workflows/ci.yml:3-8`, identique dans le projet généré). Même écart pour `codeql.yml:4-8` et `web.yml:4-9`.

### 2.8 État actuel : notre release est cassée

**Fait.** Le job `release-please` de `template-release.yml` échoue **à chaque push sur develop depuis le 2026-10-28 07:32**. Il a échoué sur les 10 derniers runs, et le dernier succès date du 2026-10-22. Le journal du run 41053367456 (GET `actions/jobs/<id>/logs`) donne la cause :
```
❯ Fetching .template.config/template.json from branch develop
##[error]release-please failed: Unexpected token '﻿', "﻿{  "$sch"... is not valid JSON
```
La cause est un BOM UTF-8 en tête de `template.json` (`head -c3 | xxd` : `efbbbf`). Le dépôt l'avait retiré exprès (`cade4e3d`, 2026-10-22, « write template.json without a BOM »). Il est revenu avec `428c0db2` (#137, 2026-10-28 08:32, « a .NET client of the API generated by Kiota ») et n'est pas reparti depuis. Les conséquences :
- La PR de release #91 « chore: release 1.1.0 » n'est plus mise à jour depuis le 2026-10-28 16:37 (`gh api .../pulls?state=open`). Elle annonce 1.1.0 alors que develop contient `feat(template)!` (#169, #172), qui exigent 2.0.0 selon `CHANGELOG.md:6-8`.
- Aucun pack, aucune attestation, aucune publication possible tant que le BOM reste.
- Aucun gate de PR ne l'a vu : release-please ne tourne qu'au push, et ni `TemplatePackagingTests`, ni `CheckWorkflows`, ni `.editorconfig` n'imposent un `template.json` sans BOM. L'`.editorconfig` pose `charset = utf-8` sur `[*]` (`ours:.editorconfig:3-4`), mais personne ne le vérifie pour le JSON.

**Verdict : en retard dans l'état actuel. Confiance haute.** Le concurrent, lui, peut publier aujourd'hui, même s'il ne le fait pas.

**Correctif** :
1. Retirer le BOM.
2. Ajouter un test `TemplateJson_CarriesNoByteOrderMark` dans `TemplatePackagingTests`, sur le modèle de `CoverageFloorTests.TheFile_CarriesNoByteOrderMark`, en le voyant d'abord échouer.
3. Lancer un `release-please` à blanc sur PR, ou un job de PR qui lit les `extra-files` comme le fera release-please.
4. Laisser release-please recalculer la PR #91, qui doit devenir 2.0.0.

### 2.9 Analyse statique en CI

**Concurrent.** CodeQL `autobuild` sur push, PR et chaque semaine, langages détectés automatiquement (`comp:.github/workflows/codeql.yml:3-44`).

**Nous.** CodeQL en matrice : C# en build manuel, TypeScript et Actions en `build-mode: none`, chaque semaine, sur main et develop (`ours:.github/workflows/codeql.yml:4-80`). S'y ajoutent SonarQube Cloud en option (`sonarqube.yml`, ADR 0001) et Sonar local par `Tasks.cs sonar`.

**Verdict : à égalité sur CodeQL ; on dépasse avec Sonar. Confiance moyenne.**

---

## Ce qu'on gagnerait à prendre, ce qu'il faut refuser (synthèse)

| Prendre | Forme chez nous | ADR |
|---|---|---|
| Des tests d'interface dans le navigateur | Un smoke Playwright TypeScript par starter dans `Src/Web`, dans `web.yml` et les jambes `web` | amender ADR 0020 (nouveau type) |
| Une boucle courte | Une tâche `check` : format, build, architecture, moins d'une minute | aucune |

| Refuser | Pourquoi (preuve) |
|---|---|
| Fusion automatique Renovate | 11 sur 50 Test Templates rouges après fusion (#1670 et d'autres) |
| Matrice après fusion | ADR template 0010, alternative rejetée ; mêmes 11 échecs |
| Tests fonctionnels par le médiateur, état statique | Ne voient ni HTTP ni l'autorisation (ADR 0020) ; `TestApp.cs:13-14` |
| Aspire comme socle de test | ADR 0001 ; couplage reconnu par `ADR-002:37` |
| Clé API NuGet, `nuget latest`, actions par tag | Trusted Publishing et pins SHA déjà en place et tenus par `CheckWorkflows` |
| Gherkin pour 6 scénarios | Coût d'un DSL ; discussion #1311 |

**Ce qu'on peut faire qu'il ne fait pas** :
- Une recompilation planifiée du paquet publié, réponse à https://github.com/jasontaylordev/CleanArchitecture/issues/1675.
- Une CI livrée au projet généré : il l'a retirée en v10.7.x (`e30d9b3`), et la discussion #1122 montre que les utilisateurs s'en servaient.
- Des tests d'architecture exécutables (discussion #731).
- Un compte des requêtes SQL (discussion #1608).

## Défauts de notre côté à corriger dans le même mouvement

1. BOM de `template.json`, qui casse release-please, et PR #91 périmée (2.8). Haute.
2. Règle « no NoWarn / no pragma » contredite à 4 endroits et non tenue par un test (1.7). Haute.
3. Plancher de couverture identique dans toutes les variantes et jamais prouvé par l'acceptance (1.5). Moyenne.
4. « SDK at an exact version » contredit par `rollForward: latestFeature` (2.7). Moyenne.
5. Déclencheurs de `ci.yml`, `codeql.yml` et `web.yml` mal décrits dans `build-and-ci.md:83` (2.7). Basse.
6. Plancher « Debug » contre mesure CI en Release (1.5). Basse.
7. Titres Conventional Commits non vérifiés (2.5). Basse.
8. `Testcontainers.MySql` encore référencé alors que MySQL est refusé (1.2). Basse.

## Non vérifié

- Aucun build ni test lancé ; les comptes viennent de `grep`, les durées des personas et de l'API Actions.
- Que l'une des variantes générées tombe sous 89 % de couverture.
- La cause du `PAL_SEHException` de `coverage` sous WSL2, et celle de l'écart `dotnet build`/`verify` sur IDE1006 et IDE0005.
- Le ruleset de `develop` et ses checks requis (API d'administration, non lue). L'ADR template 0010 les décrit.
- Le workflow `architecture-verify.v1.lock.yml`, enregistré chez le concurrent le 2026-09-21 mais absent de main : contenu non lu.
- Les téléchargements NuGet des deux paquets.
