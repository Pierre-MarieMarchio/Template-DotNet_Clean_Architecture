# Dossier anonymisé G3 : tests et qualité, CI et release

Deux dépôts de template .NET (Clean Architecture), nommés Dépôt A et Dépôt B. Les deux produisent un projet .NET 10 à partir d'un paquet `dotnet new` publié sur nuget.org. Les chiffres viennent de la lecture des deux arbres et de l'API GitHub (lecture seule) à la date du 2026-10-30 ; aucun build ni test n'a été lancé. Chaque point décrit A puis B, dans le même ordre.

## Axe 1. Tests et qualité

### 1.1 Projets de test et volume

Dépôt A
- 18 projets de test sous `Tests/`, plus un projet de test pour le lanceur en ligne de commande.
- 3 456 méthodes `[Fact]`/`[Theory]` (868 lignes `[InlineData]`), environ 535 fichiers de test.
- Sous `Tests/Architecture/ProjA.Architecture.Tests/Rules/` : 34 fichiers de règles, 195 méthodes.
- Outils : xUnit v3 sur Microsoft.Testing.Platform, Shouldly, NSubstitute, NetArchTest, Testcontainers.
- Le fichier `global.json` désigne le runner : `"test": { "runner": "Microsoft.Testing.Platform" }`.

Dépôt B
- 6 projets de test sous `tests/` (dont un projet qui n'a que son fichier d'usings) ; 34 méthodes `[Test]`/`[TestCase]`, 6 scénarios Gherkin.
- Outils : NUnit, Moq, Shouldly, Respawn, Aspire.Hosting.Testing, Playwright, Reqnroll.
- Aucun usage de NetArchTest ni d'ArchUnit dans l'arbre.

### 1.2 Types de test et ce que chacun couvre

Dépôt A
- Sept types, listés dans une page de documentation avec, pour chacun, ce qu'il prouve et ce qu'il ne prouve pas : unitaire, architecture, intégration, contrat, bout en bout, smoke de charge (k6, non bloquant), tests du lanceur.
- Contrat : un test compare le document OpenAPI servi à un document versionné ; un test vérifie que chaque code d'erreur a sa ligne dans une table de référence, dans les deux sens, et son statut.
- Un test appelle chaque endpoint que l'hôte expose pour vérifier le refus par défaut.
- Le bout en bout lance la pile Docker Compose telle que livrée et appelle l'API par HTTP.

Dépôt B
- Trois types : unitaire (domaine, application), fonctionnel (`Application.FunctionalTests`, commandes et requêtes envoyées à un hôte en mémoire contre une vraie base), acceptation navigateur (`Web.AcceptanceTests`).
- Fonctionnel : un hôte d'orchestration (Aspire) démarre une base conteneurisée, Respawn la remet à zéro entre deux tests.
  ```csharp
  var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.<hôte de test de B>>(...)
  await _app.ResourceNotifications.WaitForResourceHealthyAsync(Services.Database, cancellationToken);
  ```
- Acceptation : Playwright et Reqnroll, 4 fichiers `.feature` (connexion valide et invalide, page de comptage, page protégée de météo, accueil), lancés dans le workflow de pull request.
- Pas de test de contrat OpenAPI, pas de test de refus par défaut.

### 1.3 Base de données dans les tests

Dépôt A
- Un conteneur Testcontainers par projet d'intégration et un hôte pour toute la suite ; tous les tests d'une suite sont dans une même collection et s'exécutent l'un après l'autre.
- Une classe de base vide les schémas listés avant chaque test.
- Pour SQLite, un fichier de base propre à la suite.

Dépôt B
- Un hôte Aspire démarre un conteneur de base par exécution ; Respawn efface les lignes entre les tests.
- Une page de décision écrit : « Functional tests must run against a real database. There is no lightweight substitute. »

### 1.4 Couverture

Dépôt A
- Les rapports Cobertura de chaque module sont fusionnés (union fichier, ligne) par un petit programme du dépôt, comparés à un plancher lu dans un fichier à la racine (89).
  ```yaml
  dotnet run Tools/<outil de A pour la couverture>.cs --root TestResults --minimum "$minimum" --summary "$GITHUB_STEP_SUMMARY"
  ```
- Un test d'architecture vérifie ce fichier (un seul nombre, pas de BOM, mêmes lectures côté shell et côté .NET).
- Une analyse Sonar facultative juge le code nouveau (`/d:sonar.qualitygate.wait=true`).

Dépôt B
- `coverlet.collector` est référencé dans les projets de test.
- Aucun workflow ne lit un rapport de couverture ; aucun plancher.

### 1.5 Analyse statique et style

Dépôt A
- `Directory.Build.props` :
  ```xml
  <EnableNETAnalyzers>true</EnableNETAnalyzers>
  <AnalysisLevel>latest-recommended</AnalysisLevel>
  <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
  <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  ```
- `dotnet format --verify-no-changes` en CI ; un outil du dépôt contrôle les commentaires, un autre les chemins cités par la documentation.
- Aucun paquet d'analyseurs tiers référencé.
- Un fichier de consignes et une page de documentation écrivent : ne jamais réduire un diagnostic avec `#pragma` ou `<NoWarn>`. Le code contient `<NoWarn>$(NoWarn);CS1591;CS1573</NoWarn>` à la racine, un `<NoWarn>` de deux identifiants dans le projet de l'API et un `#pragma warning disable ASP0000`. Aucun test ne vérifie cette règle.

Dépôt B
- `Directory.Build.props` : `TreatWarningsAsErrors` à `true`, `Nullable` activé ; pas de `AnalysisLevel` ni d'`EnforceCodeStyleInBuild`.
- `.editorconfig` : règles de nommage en `suggestion`.
- `NUnit.Analyzers` dans les projets de test.
- Une ligne `<WarningsNotAsErrors>NU1608</WarningsNotAsErrors>` (sous PostgreSQL) et un `#pragma warning disable CS1998` dans une requête d'exemple. Aucune règle écrite sur les suppressions.

### 1.6 Tests des fronts

Dépôt A
- Un espace de travail pnpm : lint, test, build et vérification que le client d'API correspond au document de l'API.
- 5 fichiers de test TypeScript dans le paquet de client d'API.
- Un test de bout en bout vérifie que chaque application web sert sa page et relaye `/api` vers l'API ; aucun test ne pilote un navigateur.

Dépôt B
- 2 fichiers de test Angular (`*.spec.ts`), aucun pour React ; le workflow de pull request lance `dotnet test` seulement, pas `ng test`.
- 6 scénarios Playwright (voir 1.2) lancés par `dotnet test`.

### 1.7 Tests du template lui-même (projet généré)

Dépôt A
- Un workflow génère le projet à partir du paquet empaqueté dans la même exécution, avec un autre nom, dans 25 configurations listées dans un fichier JSON, plus 2 configurations Windows et un job de refus.
- Les configurations sont choisies d'après les chemins modifiés par la pull request ; les contrôles requis sont 32 (ruleset de la branche d'intégration).
- Contrôles : nom du template absent des fichiers et des chemins, autre fournisseur de base absent, JSON valide (lu en `utf-8-sig`), bootstrap, build, tests sans Docker, tests d'intégration, pile Compose démarrée, relais web, migrations sans changement en attente, génération puis suppression d'une feature sans reste.
- Refus : 12 combinaisons doivent sortir en erreur sans écrire de fichier.
- Déclenchement : pull request et push sur les branches d'intégration et de release. Durée médiane d'un run réussi : 1 137 s. Sur 100 runs : 72 réussis, 13 échoués, 15 annulés.

Dépôt B
- Un workflow génère le projet dans 9 configurations (3 fronts x 3 bases) : `dotnet new install .`, `dotnet new <nom court de B> ...`, build Release, `npm ci` et `npm run build` du front, Playwright, `dotnet test`.
  ```yaml
  matrix:
    client-framework: [angular, react, none]
    database: [sqlite, sqlserver, postgresql]
  ```
- Déclenchement : push sur la branche principale et lancement manuel, pas sur pull request. Durée médiane d'un run réussi : 197 s. Sur les 50 derniers runs : 39 réussis, 11 échoués.
- Le template est installé depuis le dossier source, pas depuis le paquet.

### 1.8 Durée de la pull request

Dépôt A
- Workflow principal de pull request : médiane 409 s (min 230, max 1 768) sur 100 runs réussis ; il comprend build, formatage, tests avec couverture, hygiène, audit, image Docker, pile Compose, bout en bout.
- Une tâche locale lance les projets sans Docker seulement.

Dépôt B
- Workflow de pull request : médiane 163 s (min 129, max 690) sur 100 runs réussis ; il comprend restore, build, installation de Playwright, `dotnet test`.
- Sur 100 runs : 79 réussis, 21 échoués.

## Axe 2. CI et release

### 2.1 Workflows

Dépôt A
- Sept workflows : intégration continue, analyse de code (CodeQL), release de projet, SonarQube, génération du template, release du template, espace web.
- Les cinq premiers et celui du web sont aussi livrés dans le projet généré (option `ci`) ; ceux du template ne le sont pas.
- Pour chaque action : SHA de 40 caractères et tag en commentaire.
  ```yaml
  uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7.0.1
  ```
- `permissions: contents: read` en tête, `concurrency` annulant les runs obsolètes, `timeout-minutes` sur chaque job. Un outil du dépôt vérifie ces règles sur tous les workflows et se teste lui-même.

Dépôt B
- Quatre workflows : build, CodeQL, test des templates, release.
- Actions par tag mobile :
  ```yaml
  - uses: actions/checkout@v7
  ```
- `permissions: contents: read` sur build et release ; pas de `concurrency` ni de `timeout-minutes`.

### 2.2 Publication du paquet

Dépôt A
- Un bot de release (release-please) tient une pull request de release sur la branche d'intégration ; la fusionner crée le tag `v<version>` ; ensuite : vérifier que le tag égale la version lue dans le manifeste, empaqueter deux paquets (template et lanceur), les installer dans une ruche isolée, générer et construire le projet par défaut, générer avec le lanceur, attester la provenance, publier.
- Publication : échange d'identité OIDC contre une clé de courte durée (`NuGet/login`), environnement protégé qui attend un relecteur, aucune clé stockée ; puis copie vers GitHub Packages, puis avance de la branche `main` par fast-forward.
- Un nupkg déjà présent sur nuget.org fait échouer le push.

Dépôt B
- Une personne publie une Release GitHub ; le workflow se déclenche sur `release: published`.
- Il lit la version dans le tag, la copie dans le manifeste avec `jq`, copie les notes de la Release dans le nuspec avec Python, exécute `nuget pack` et `nuget push` avec une clé stockée en secret :
  ```yaml
  run: nuget push *.nupkg -Source 'https://api.nuget.org/v3/index.json' -ApiKey ${{secrets.NUGET_API_KEY}} -SkipDuplicate
  ```
- Pas d'étape de build, de test ni d'analyse avant le push ; installation de `mono-complete` pour `nuget.exe`. Pas d'attestation de provenance.

### 2.3 Versionnement et cadence

Dépôt A
- Semver propre au template, calculée depuis les commits conventionnels ; une branche `net<major>.0` est prévue pour une ancienne version majeure de .NET.
- Releases publiées : 1 (2026-10-22).
- Le workflow de release du template tourne à chaque push sur la branche d'intégration. Sur ses 73 runs : 35 réussis, 36 échoués, 2 annulés. Le dernier succès date du 2026-10-27 ; depuis le 2026-10-28 chaque run échoue à l'étape du bot de release, avec le message « release-please failed: Unexpected token '﻿', "﻿{ "$sch"... is not valid JSON » après la lecture du manifeste `template.json`, qui commence par les octets `EF BB BF`.
- La pull request de release ouverte porte la version suivante (mineure), créée le 2026-10-22 et mise à jour le 2026-10-28 ; son état est « behind » ; le bot dit considérer 124 commits.

Dépôt B
- La version du paquet est le tag, dont la majeure est celle de .NET ; les notes sont celles écrites dans la Release.
- Releases publiées : 22, dont 14 en mars 2026 ; la dernière date du 2026-03-23.
- Le workflow de release compte 16 runs : 15 réussis, 1 échoué.
- Depuis la dernière release : 58 commits sur la branche principale, dont 54 de type `chore(deps)`.
- Une issue ouverte le 2026-10-28 : « <version> now fails to build because of NU1902/NU1903 security advisories » ; elle dit : « Build fails with 118 errors (Warning As Error due to vulnerability). The last release (<version>) was created 7 months ago. Even though the package versions were updated in this repo, due to not releasing the project, the nuget package didn't get the update. »

### 2.4 Dépendances et chaîne d'approvisionnement

Dépôt A
- Versions exactes dans `Directory.Packages.props`, SDK dans `global.json`, outils .NET dans `.config/dotnet-tools.json`, images par digest, actions par SHA.
- Dependabot, mise à jour hebdomadaire par bandes (nuget, actions, docker, npm, compose) ; pas d'`ignore`, pas d'automerge.
- Audit : `NuGetAudit` mode `all`, niveau `low`, promu en erreur par `TreatWarningsAsErrors` ; un job de CI lit la sortie de `dotnet list package --vulnerable --include-transitive` et échoue sur une ligne « has the following vulnerable packages » ; le même contrôle précède la release du projet.
- CodeQL sur C#, TypeScript et les workflows ; planification hebdomadaire. Seul CodeQL a un déclencheur `schedule`.
- L'image Docker du projet est publiée avec SBOM et attestation de provenance.

Dépôt B
- Versions exactes dans `Directory.Packages.props`, SDK dans `global.json` (`rollForward: latestFeature`).
- Renovate (`config:recommended`) : lundi, groupes par ensemble (.NET, Angular, React, tests, GitHub Actions), automerge des mises à jour mineures, patch et digest.
  ```json
  "matchUpdateTypes": ["minor","patch","digest"], "automerge": true
  ```
- La branche principale est protégée ; la liste des contrôles de statut requis est vide (`enforcement_level: off`).
- Audit : les avertissements `NuGetAudit` par défaut, promus en erreur par `TreatWarningsAsErrors` ; pas de job dédié. Un épinglage de `System.Security.Cryptography.Pkcs` figure dans `Directory.Packages.props` sur la branche principale.
- CodeQL (autobuild) sur push, pull request et chaque lundi.

### 2.5 Ce que reçoit le projet généré

Dépôt A
- Un workflow d'intégration continue, un de CodeQL, un de release (image sur GHCR, lots de migrations), un d'analyse Sonar facultatif, un pour le web, la configuration Dependabot ; chacun est retiré par des options (`ci`, `sonar`) ou par l'absence de web.
- Un fichier `.github/pull_request_template.md`.

Dépôt B
- Le nuspec exclut les workflows du dépôt, `renovate.json`, `.github/ISSUE_TEMPLATE`, `.github/FUNDING.yml` et le fichier `icon.png` de `.github` :
  ```
  exclude="... .\.github\workflows\release.yml;.\.github\workflows\codeql.yml;.\.github\workflows\build.yml; ...;.\renovate.json; ... .\.github\workflows\test-templates.yml; ..."
  ```
- Le projet généré ne contient pas de workflow ni de configuration de mise à jour des dépendances ; il contient un devcontainer.

### 2.6 Protection des branches, Sonar, taux de réussite observés

Dépôt A
- Branche d'intégration protégée par un ruleset : pull request obligatoire, historique linéaire, 32 contrôles requis ; ruleset sur `main` (pas de suppression, pas de non-fast-forward) et sur les tags de release (pas de suppression ni de mise à jour).
- Sonar : analyse de la branche par défaut et des pull requests du dépôt ; ignorée tant que les variables du dépôt ne sont pas définies.

Dépôt B
- Branche principale protégée sans contrôle requis ; les mises à jour de dépendances sont fusionnées par le bot `renovate[bot]` .
- Pas de Sonar.
- Workflow de build : 79 réussis, 21 échoués sur 100 runs ; CodeQL : 92 réussis, 8 échoués sur 100 runs.
