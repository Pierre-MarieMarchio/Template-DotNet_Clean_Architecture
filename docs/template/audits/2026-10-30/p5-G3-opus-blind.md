# Dossier à l'aveugle, groupe G3 : tests et qualité, CI et release

Deux templates de solution .NET 10 en Clean Architecture, distribués comme paquets `dotnet new` : **Dépôt A** et **Dépôt B**.
Les projets s'appellent ici ProjA et ProjB. Pour chaque point, A est décrit avant B, sur le même plan.
Sources : lecture du code des deux dépôts, projets générés depuis chaque paquet, historique public des workflows, issues publiques.

---

## Axe 1. Tests et qualité

### 1.1 Types de tests et volume

**Dépôt A.** 18 projets de test xUnit v3 (environ 3 300 méthodes `[Fact]/[Theory]`, plus 135 pour l'outil de création). Un tableau de la doc liste les types : unitaire, architecture, intégration, contrat, end-to-end et charge (k6, non bloquant), chacun avec ce qu'il prouve et ce qu'il ne doit pas prouver. Une décision écrite pose qu'un risque est tenu par un seul type de test. L'intégration passe par HTTP contre la vraie base :
```csharp
using var response = await intruder.GetAsync(new Uri($"{TodoListsRoute}/{listId}", UriKind.Relative), TestToken);
response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
using var ownerResponse = await owner.GetAsync(...);
ownerResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
```
Les règles de couches et de conventions sont des tests (NetArchTest), dont 17 contre-épreuves qui montrent qu'une règle sait échouer. Le document OpenAPI est committé et comparé à celui que sert l'API.

**Dépôt B.** 5 projets NUnit et un hôte de test : tests unitaires du domaine (6), tests unitaires de l'application (7), tests fonctionnels (19), un projet de tests d'intégration vide (un seul fichier `GlobalUsings.cs`), un projet d'acceptance (6 scénarios Gherkin, Reqnroll + Playwright). Les tests fonctionnels passent par le médiateur :
```csharp
public static async Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request)
{   using var scope = <SetupB>.ScopeFactory.CreateScope();
    var mediator = scope.ServiceProvider.GetRequiredService<ISender>();
    return await mediator.Send(request); }
```
L'utilisateur courant est gardé dans des champs statiques (`_userId`, `_roles`). Il n'y a pas de test d'architecture. Le README généré dit « unit, integration, and functional tests ».

### 1.2 Outils de test

**Dépôt A.** xUnit v3 sur Microsoft.Testing.Platform, Shouldly, NSubstitute, NetArchTest, Testcontainers (PostgreSQL, SQL Server), Microsoft.AspNetCore.Mvc.Testing, Microsoft.Testing.Extensions.CodeCoverage. Base remise à zéro par `TRUNCATE ... RESTART IDENTITY CASCADE`.

**Dépôt B.** NUnit, NUnit.Analyzers, Moq, Shouldly, Respawn, coverlet.collector, Microsoft.NET.Test.Sdk (VSTest), Aspire.Hosting.Testing, Reqnroll, Playwright.

### 1.3 Base de données des tests

**Dépôt A.** Testcontainers, avec l'image épinglée par digest :
```csharp
internal const string Image = "postgres:18.6-alpine3.23@sha256:1a0c23d5...";
private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(Image).WithDatabase(database)...Build();
```
Docker est requis pour PostgreSQL et SQL Server. La variante SQLite utilise un fichier. Une commande lance les tests sans Docker en excluant les projets qui référencent Testcontainers.

**Dépôt B.** Un hôte Aspire de test démarre la base :
```csharp
var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.<HôteDeTestB>>(...);
_app = await builder.BuildAsync(ct).WaitAsync(ct);
await _app.ResourceNotifications.WaitForResourceHealthyAsync(Services.Database, ct);
```
SQLite est le défaut et se passe de Docker ; PostgreSQL et SQL Server en ont besoin. Une décision écrite justifie Aspire et en reconnaît le couplage.

### 1.4 Vitesse

**Dépôt A.** Mesures sur un projet généré :
- tests sans Docker : 1 min 12 s ;
- gate complet (format, build, tous les tests dont l'intégration, dérive des migrations, audit des paquets) : environ 4 à 5 min ;
- un utilisateur de test a lancé 16 gates, dont 6 rouges ; deux de ces échecs venaient de diagnostics de style que `dotnet build` n'avait pas montrés.

CI du dépôt (moyennes des runs réussis récents) : 10 min par PR pour la CI, 35 min pour la matrice de génération.

**Dépôt B.** Mesures sur un projet généré : `dotnet test`, 40 s.
CI du dépôt : 3 min par PR pour le build, 5 min pour la matrice de génération (après fusion).

### 1.5 Couverture

**Dépôt A.** Un fichier de plancher (89 % de lignes) est lu par la CI et par l'outil de tâches. La CI produit du Cobertura, puis un outil maison fusionne les rapports et compare le total au plancher. Des tests d'architecture contrôlent le fichier (pas de BOM, une seule valeur, entre 1 et 100) et les réglages de couverture. Le plancher vaut 89 dans toutes les variantes générées, et aucune jambe de la matrice de génération ne mesure la couverture. Le commentaire du plancher parle d'une mesure en Debug ; la CI mesure en Release. Deux utilisateurs de test ont vu la tâche de couverture planter sous WSL2 (`PAL_SEHException`), cause non établie.

**Dépôt B.** `coverlet.collector` est référencé. Aucun workflow ni script ne produit de rapport ni n'impose de seuil.

### 1.6 Gate local

**Dépôt A.** Une tâche `verify` enchaîne :
```csharp
RequireInitialMigrations(task, contexts); Gates(repoRoot);
Step("dotnet", "format", solution, "--verify-no-changes", "--no-restore");
Step("dotnet", ["build", ...]); Step("dotnet", ["test", ...]);
Step("dotnet", "ef", "migrations", "has-pending-model-changes", ...);
VulnerablePackages(solution);
```
Chaque gate maison lance d'abord son autotest. Le gate ne lance pas les commandes du front web ; la CI les lance dans un workflow séparé.

**Dépôt B.** Pas de commande de gate dans le projet généré : le README dit `dotnet test`. Les scripts PowerShell du dépôt ne sont pas livrés dans le paquet. Un utilisateur de test a constaté qu'une migration oubliée ne fait échouer aucun test.

### 1.7 Analyseurs et style

**Dépôt A.**
```xml
<EnableNETAnalyzers>true</EnableNETAnalyzers>
<AnalysisLevel>latest-recommended</AnalysisLevel>
<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
<NuGetAudit>true</NuGetAudit><NuGetAuditMode>all</NuGetAuditMode><NuGetAuditLevel>low</NuGetAuditLevel>
<NoWarn>$(NoWarn);CS1591;CS1573</NoWarn>
```
La CI vérifie le format. Un outil maison refuse les commentaires `//` de prose. La doc et l'`.editorconfig` interdisent `<NoWarn>` global et `#pragma`. Le code en contient : le `NoWarn` ci-dessus, un `NoWarn` dans le projet API, trois `severity = none` globaux et un `#pragma warning disable ASP0000`. Aucun test ne tient cette règle.

**Dépôt B.**
```xml
<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
```
Il n'a ni `AnalysisLevel` ni `EnforceCodeStyleInBuild`. Son `.editorconfig` compte 19 règles en `suggestion` et 2 en `warning`. NUnit.Analyzers est le seul analyseur tiers. La CI ne vérifie pas le format.

### 1.8 Tests d'interface dans le navigateur

**Dépôt A.** Aucun test n'exécute une page dans un navigateur. La matrice de génération vérifie par `curl` et un script Python que chaque app web sert sa page, que sa CSP couvre ses scripts inline et qu'elle relaie vers l'API. L'espace web a ses tests unitaires (`pnpm test`). Playwright figure dans une issue « plus tard ».

**Dépôt B.** Reqnroll + Playwright, 4 fonctionnalités et 6 scénarios (connexion, accueil, météo, compteur), sur la pile démarrée par Aspire. La CI installe Chromium et les joue. Sur un poste neuf, ils échouent tant que les navigateurs ne sont pas installés, et le README n'en parle pas.

---

## Axe 2. CI et release

### 2.1 CI livrée au projet généré

**Dépôt A.** Le projet généré reçoit plusieurs workflows :
- `ci.yml` : restore, format, build, tests avec couverture et plancher, gates maison, audit des paquets vulnérables, image Docker (non root, port 8080 seul), validation Compose, suite end-to-end bloquante contre la pile, smoke k6 non bloquant ;
- `codeql.yml`, `release.yml`, `web.yml` (avec un front), `sonarqube.yml` (en option) ;
- `dependabot.yml` et un modèle de PR.

**Dépôt B.** Le projet généré ne reçoit aucun workflow ni aucune configuration de bot de dépendances : le nuspec exclut les quatre workflows et `renovate.json`.
```xml
...;.\.github\workflows\release.yml;.\.github\workflows\codeql.yml;.\.github\workflows\build.yml;...;.\renovate.json;...
```
Une option de pipeline (GitHub Actions ou Azure Pipelines) existait jusqu'en mars 2026.

### 2.2 Prouver ce que le template génère

**Dépôt A.** Le workflow de la matrice tourne sur chaque PR et chaque push. Il compte 25 jambes, définies par forme d'options et non par produit cartésien, dont 3 sur Windows. Sur une PR, un sélecteur choisit les jambes selon les chemins modifiés ; tout tourne en cas de doute, sur un push ou avec un label. Chaque jambe :
- installe le paquet packé ;
- génère sous un autre nom et vérifie qu'aucun nom du template ni aucun autre provider ne fuit ;
- lance le bootstrap, le build, les tests sans Docker, l'intégration, l'end-to-end selon la jambe et la dérive des migrations ;
- fait l'aller-retour du générateur de feature ;
- installe, lint, teste et construit le web, puis démarre la pile.

Un job vérifie que les combinaisons interdites sortent en erreur sans rien écrire. Une décision écrite rejette l'alternative « sous-ensemble sur PR, matrice complète la nuit ».

**Dépôt B.** 9 jambes (3 fronts × 3 bases) : génération, build, `npm ci && npm run build`, `dotnet test`. Le déclencheur :
```yaml
on:
  push:
    branches: [ main ]
  workflow_dispatch:
```
Une PR ne joue que le build du dépôt et CodeQL. Sur les 50 derniers runs de la matrice sur main, 11 ont échoué. Par exemple, une PR de mise à jour fusionnée par le bot avec le build et CodeQL verts a fait échouer la jambe `react-sqlserver` à l'étape des tests.

### 2.3 Mises à jour de dépendances

**Dépôt A.** Dependabot, hebdomadaire, sur 5 écosystèmes (nuget, github-actions, docker, npm, docker-compose), par groupes, sans fusion automatique. Le même fichier est livré au projet généré.

**Dépôt B.** Renovate, hebdomadaire, avec fusion automatique de ce qui n'est pas majeur :
```json
{ "matchUpdateTypes": ["minor","patch","digest"], "automerge": true },
{ "matchManagers": ["github-actions"], "groupName": "GitHub Actions", "automerge": true }
```
51 des 58 commits depuis la dernière release viennent du bot.

### 2.4 Avis de sécurité NuGet

**Dépôt A.** `NuGetAudit` en mode all, niveau low, avec `TreatWarningsAsErrors`. Un job CI lit `dotnet list package --vulnerable --include-transitive`. La doc demande de répondre par une épingle transitive, jamais en relâchant l'audit. Le seul workflow planifié est CodeQL ; aucun ne recompile le paquet publié.

**Dépôt B.** `TreatWarningsAsErrors` avec l'audit par défaut du SDK, et une épingle transitive de sécurité. Une issue publique ouverte, sans réponse, rapporte qu'un projet neuf généré depuis la dernière version publiée (7 mois) ne compile plus : 120 erreurs NU1902/NU1903. Deux utilisateurs de test l'ont reproduit. Aucun workflow ne recompile le paquet publié.

### 2.5 Versioning et changelog

**Dépôt A.** release-please sur la branche d'intégration, en Conventional Commits. Il produit un CHANGELOG, et un `!` donne une version majeure. Un garde vérifie que le tag égale la version du `template.json`. La branche de release n'avance que par fast-forward vers la release. Aucun check n'impose le format des titres de PR.

**Dépôt B.** La version vient du tag de la GitHub Release, et le corps de la release devient les notes du nuspec :
```yaml
run: echo "value=${GITHUB_REF_NAME#v}" >> $GITHUB_OUTPUT
... root.find('.//n:releaseNotes', ns).text = os.environ.get('RELEASE_NOTES', '')
```
Il n'y a pas de CHANGELOG. 14 releases sont sorties en mars 2026, aucune depuis.

### 2.6 Publication

**Dépôt A.**
1. Pack au tag.
2. Installation du paquet dans un hive isolé, génération et build du projet par défaut, essai de l'outil de création depuis son paquet.
3. Attestation de provenance (`actions/attest-build-provenance`).
4. Envoi vers nuget.org par Trusted Publishing (OIDC, sans clé stockée), dans un environnement qui attend un relecteur. Une version déjà publiée fait échouer l'envoi.
5. Miroir vers GitHub Packages.

**Dépôt B.** Sur `release: published` : `nuget/setup-nuget@v4` avec `nuget-version: 'latest'`, installation de Mono, `nuget pack`, puis
```yaml
run: nuget push *.nupkg -Source 'https://api.nuget.org/v3/index.json' -ApiKey ${{secrets.NUGET_API_KEY}} -SkipDuplicate
```
Le paquet n'est ni testé avant l'envoi, ni attesté.

### 2.7 Supply chain des workflows

**Dépôt A.** Actions épinglées par SHA de 40 caractères, avec un commentaire de version. Un outil maison du dépôt refuse toute action non épinglée et tout job sans `permissions` :
```csharp
private static readonly Regex ShaPin = new(@"^[\w\-./]+@[0-9a-f]{40}$", RegexOptions.Compiled);
```
Les images sont épinglées par digest et contrôlées par un test. Le binaire k6 est vérifié par SHA-256. Le workflow de release du projet généré produit un SBOM et une provenance signée de l'image. `global.json` porte `10.0.100` avec `rollForward: latestFeature`, alors que la doc dit que toute dépendance, SDK compris, est à une version exacte. Les déclencheurs décrits dans la doc (« push and pull request to `main` ») ne citent pas les PR vers la branche d'intégration, qui sont pourtant déclenchées.

**Dépôt B.** Actions référencées par tag de version majeure (`actions/checkout@v7`, `actions/setup-dotnet@v6`, `github/codeql-action/init@v4`). NuGet en version `latest`. Les mises à jour d'actions sont fusionnées automatiquement. Permissions `contents: read` au niveau du workflow. `global.json` porte `10.0.403` avec `rollForward: latestFeature`.

### 2.8 État du pipeline de release au moment de la lecture

**Dépôt A.** Le job release-please échoue à chaque push sur la branche d'intégration depuis 2 jours (10 runs consécutifs) :
```
##[error]release-please failed: Unexpected token '﻿', "﻿{  "$sch"... is not valid JSON
```
`template.json` commence par un BOM UTF-8, réintroduit par un commit de fonctionnalité alors qu'un commit antérieur l'avait retiré. Aucun test ni gate de PR ne contrôle ce BOM. La PR de release ouverte n'est plus mise à jour : elle annonce une version mineure, alors que deux commits marqués `!` ont été fusionnés depuis.

**Dépôt B.** Le workflow de release se déclenche à la publication d'une GitHub Release. Il n'a pas tourné depuis la dernière release, 7 mois plus tôt. Les runs récents des autres workflows sont verts, sauf les échecs de la matrice après fusion décrits en 2.2.

### 2.9 Analyse statique en CI

**Dépôt A.** CodeQL en matrice : C# en build manuel, TypeScript et Actions sans build. Il tourne chaque semaine, sur push et sur PR. SonarQube Cloud est disponible en option, et Sonar peut aussi tourner en local par une tâche.

**Dépôt B.** CodeQL `autobuild` chaque semaine, sur push et sur PR, avec détection automatique des langages.
