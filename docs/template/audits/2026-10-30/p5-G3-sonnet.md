# P5 G3 (sonnet) : tests et qualité, CI et release

Analyste : sonnet. Groupe G3, deux axes. Lecture seule : aucun build ni test lancé, `gh` en GET seulement.
Chemins relatifs à la racine de chaque clone : nous = `repos/ours` (develop, `5f22faa5`), concurrent = `repos/competitor-main` (main, `345e7fe`), release = `repos/competitor-release` (v10.8.0).
Le dépôt vivant de develop est à `935d552a` (le clone est de quatre commits plus ancien) ; les constats GitHub (runs, ruleset, PR) sont lus sur l'état d'aujourd'hui, 2026-10-30.

## Résumé

1. Sur les tests et la qualité, on dépasse le concurrent par le type de preuve (architecture, contrat, default-deny, plancher de couverture, workflows eux-mêmes testés). Le concurrent garde une avance sur un seul point : des tests navigateur (Playwright) lancés en CI ; on n'en a aucun (roadmap #122). Un défaut chez nous : la règle « jamais de `NoWarn` ni de `#pragma` » est contredite par trois usages dans le code, sans test qui la tienne.
2. Sur la CI et la release, notre mécanisme est nettement plus sérieux (SHA, permissions, publication par Trusted Publishing avec relecteur, provenance, installation du paquet avant push, projet généré qui reçoit CI, CodeQL, Dependabot). Mais **notre pipeline de release est cassé depuis le 2026-10-28** : `release-please` échoue sur le BOM de `.template.config/template.json` à chaque push sur develop (36 échecs, 35 succès, 2 annulés : 73 runs au total). Aucun test ni aucune étape de PR ne le voit.
3. Le coût utilisateur que le concurrent paie déjà (issue ouverte #1675 : le 10.8.0 publié ne compile plus, `NU1902/NU1903` sous `TreatWarningsAsErrors`) nous attend à l'identique : même `TreatWarningsAsErrors`, `NuGetAuditLevel=low`, aucun workflow planifié qui regénère le projet par défaut. Notre release automatisée est justement ce qui est en panne.

## Axe 1. Tests et qualité

### 1.1 Comment fait le concurrent

- 6 projets de test, 34 méthodes (`grep -rE "\[Test\]|\[TestCase" competitor-main/tests` = 34), 6 scénarios Gherkin : `tests/Application.UnitTests`, `Domain.UnitTests`, `Application.FunctionalTests` (8 fichiers de commandes), `Infrastructure.IntegrationTests` (vide : un `GlobalUsings.cs`, aucun test), `Web.AcceptanceTests` (Reqnroll + Playwright, 4 `.feature`).
- Outils : NUnit, Moq, Shouldly, Respawn, Aspire.Hosting.Testing, Playwright, `coverlet.collector` (`Directory.Packages.props:12,26-27,32,37-38`).
- Base réelle : `tests/Application.FunctionalTests/FunctionalTestSetup.cs:21-46` démarre un `DistributedApplication` Aspire (`TestAppHost`), attend la base saine, puis `DatabaseResetter` (Respawn) remet à zéro entre les tests. ADR-001 : « Functional tests must run against a real database. There is no lightweight substitute » (`docs/decisions/ADR-001-Use-EFCore-In-Application-Layer.md:51`).
- Aucun test d'architecture : `grep -rn "NetArchTest\|ArchUnit" competitor-main` ne rend rien. Aucun test de contrat OpenAPI ni de codes d'erreur.
- Couverture : `coverlet.collector` est référencé dans chaque projet de test, mais aucun workflow ne la lit, aucun plancher n'existe (`grep -n coverage .github/workflows/*.yml` vide).
- Analyse statique : `TreatWarningsAsErrors` seul (`Directory.Build.props:5`), plus `NUnit.Analyzers` dans les projets de test. Il n'est pas sans suppressions non plus : `Directory.Build.props:8` (`WarningsNotAsErrors` = `NU1608`, sous PostgreSQL) et un `#pragma warning disable CS1998` dans `src/Application/WeatherForecasts/Queries/GetWeatherForecasts/GetWeatherForecastsQuery.cs:12` ; mais il n'écrit aucune règle contre. `.editorconfig` ne fixe que des règles de nommage en `suggestion` (`.editorconfig:196-268`). Pas de `AnalysisLevel`, pas d'`EnforceCodeStyleInBuild`.
- Gate CI : `.github/workflows/build.yml:54-66` : restore, build Release, installation de Playwright, `dotnet test`. Le tout sur pull request vers main seulement (`build.yml:3-5`). Durée médiane des 100 derniers runs de PR réussis : 163 s (min 129, max 690).
- Tests du template : `.github/workflows/test-templates.yml:3-19` : 9 jambes (3 fronts x 3 bases), générer, builder, `npm ci`, tester ; déclenché sur **push main et dispatch, pas sur pull request**. 11 échecs sur les 50 derniers runs (`gh api .../workflows/test-templates.yml/runs`).

### 1.2 Comment on fait

- 3 456 méthodes `[Fact]`/`[Theory]` (+ 868 lignes `[InlineData]`) dans 535 fichiers sur 18 projets de test sous `Tests/` plus celui du lanceur (commande : `grep -rEh "^\s*\[(Fact|Theory)\b" Tests Launcher | wc -l`), xUnit v3 sur Microsoft.Testing.Platform (`global.json:8`), Shouldly, NSubstitute, NetArchTest, Testcontainers (`Directory.Packages.props:140-159`).
- Sept types, chacun justifié par un risque (`docs/guides/testing.md` tableau « The test types » ; ADR 0020 « One risk, one type of test ») : unitaire, architecture (34 fichiers de règles, 195 méthodes sous `Tests/Architecture/AppTemplate.Architecture.Tests/Rules/`), intégration, contrat (`OpenApiDocumentTests`, `ErrorCodeTableTests`, `ErrorCodeStatusTests`), bout en bout contre la pile Compose (`Tests/EndToEnd`), smoke de charge k6 non bloquant (`Tests/Load/smoke.js`, `ci.yml:315-319`), plus 135 tests du lanceur (`Launcher/CleanArch.Create.Tests`).
- Les gates se testent eux-mêmes : `ci.yml:172-191` lance `--self-test` de `CheckDocPaths`, `CheckWorkflows`, `CoverageGate`, `CheckComments` avant de juger l'arbre ; `Tools/CheckWorkflows.cs:34-62` liste ce qu'il refuse (action sans SHA, image sans digest, `permissions` absent, `needs:` vers un job inconnu…).
- Un gate refuse de passer sur rien : `ci.yml:80-103` (projet de test hors solution), `ci.yml:108-112,122` (`--minimum-expected-tests 3000`), `Tools/CoverageGate.cs`.
- Couverture : fusion Cobertura par union (fichier, ligne), plancher lu dans `coverage.minimum:2` (89), appliqué par `ci.yml:132-147`, tenu par `Tests/Architecture/.../Rules/CoverageFloorTests.cs` et `CoverageSettingsTests.cs` ; Sonar juge en plus le code nouveau (`sonarqube.yml:109`, `/d:sonar.qualitygate.wait=true`).
- Analyse statique : `AnalysisLevel=latest-recommended`, `EnforceCodeStyleInBuild`, `TreatWarningsAsErrors`, `NuGetAudit` mode `all` niveau `low` (`Directory.Build.props:12-21`), `dotnet format --verify-no-changes` (`ci.yml:72-73`), `CheckComments`, `CheckDocPaths`. Aucun paquet d'analyseurs tiers n'est référencé (`grep` de `PackageReference.*Analyzer` dans les props et csproj : rien).
- Preuve du template généré : `.github/workflows/template-acceptance.yml` : 25 jambes sélectionnées d'après les chemins modifiés (`.template.config/acceptance/matrix.json`, `rules.json`, ADR template 0010), jambes Windows (`template-acceptance.yml:849-870`), 12 refus de combinaison avec « aucun fichier écrit » (`:939-1000`), fuite du nom du template, JSON strict (`:277-289`), pile Compose démarrée et relais web vérifié (`:656-671`). Exigées en PR par le ruleset `develop` : 32 contrôles (`gh api repos/.../rulesets`). Durée médiane d'un run réussi : 1 137 s (max 3 433). CI de PR : médiane 409 s (min 230, max 1 768), end-to-end compris.
- Défaut constaté : `AGENTS.md` et `docs/guides/build-and-ci.md` disent « Never silence a diagnostic with `#pragma`, `<NoWarn>` ». Le code a `Directory.Build.props:27` (`<NoWarn>$(NoWarn);CS1591;CS1573</NoWarn>`), `Src/Presentation/AppTemplate.Api/AppTemplate.Api.csproj:16` (`AV0029;AV0030`) et `Src/Presentation/AppTemplate.Api/Common/Hosting/HttpPipelineModule.cs:67` (`#pragma warning disable ASP0000`). Aucun test d'architecture ne tient cette règle (`grep -rn "NoWarn\|pragma" Tests/Architecture` ne rend pas de règle). Le code et les tests passent avant la doc : la doc est fausse, ou le code doit changer. Déjà relevé dans `p4-devil.md` (§C2) ; je le confirme sur le code.

### 1.3 Verdicts

| Sous-axe | Verdict | Confiance |
|---|---|---|
| Étendue et types de tests (unitaire, architecture, contrat, e2e) | on dépasse | haute |
| Tests d'architecture et de contrat HTTP | on dépasse (le concurrent n'en a pas) | haute |
| Couverture (mesure, plancher, Sonar) | on dépasse | haute |
| Analyseurs et gate de style | on dépasse, avec un défaut interne (NoWarn/pragma contre la règle écrite) | haute |
| Tests navigateur (UI) | en retard | moyenne |
| Base réelle en test (Aspire + Respawn contre Testcontainers + troncature) | choix différent assumé (ADR 0001, ligne « xUnit v3, Shouldly, NSubstitute, Testcontainers, NetArchTest », `docs/decisions/0001-adopt-the-generated-baseline.md:73`) ; Aspire est en roadmap #122 | haute |
| Vitesse du retour de PR | égalité non concluante : 163 s contre 409 s de médiane, mais 34 tests contre 3 456 et des travaux différents (CI complète avec Docker, e2e, hygiène) ; on garde `test --no-integration` pour la boucle locale | basse |
| Preuve du template généré | on dépasse (25 jambes + Windows + refus + fuite du nom, exigés en PR) contre 9 jambes lancées après fusion, 22 % d'échecs | haute |

### 1.4 Ce qu'on gagnerait à lui prendre

- Un test navigateur par starter web. Le concurrent lance `Login.feature` (connexion valide et invalide) et `Weather.feature` (page protégée) en CI (`build.yml:61-66`). Chez nous la session passe par un cookie et un relais `/api` (ADR 0016) ; `Tests/EndToEnd/.../WebAppTests` prouve que l'app sert sa page et relaye vers l'API, jamais qu'un navigateur s'authentifie. Forme chez nous : une jambe de `template-acceptance.yml` avec Playwright .NET pour un seul starter (le SPA par défaut), un test « se connecter par le relais » ; c'est le point 4 de la roadmap #122 (`docs/template/roadmap.md`, « Playwright end-to-end tests for the web apps »). ADR à écrire : « un parcours navigateur par starter, jamais par framework », qui complète ADR 0020 (un second type de test seulement s'il échoue pour une cause que le premier ne voit pas : ici le CSP, le cookie, le relais).
- Le geste « Aspire pour le test de la base » n'est pas à prendre (voir 1.5).

### 1.5 Ce qu'il faut refuser

- Reqnroll et Gherkin pour 6 scénarios : une dépendance, une couche d'étapes et de pages pour moins de 40 lignes de vérité. Si on écrit le test navigateur, on l'écrit en xUnit simple ; l'ADR 0020 le justifie (un risque, un type).
- Un compteur de tests ou un plancher relevé « pour la forme » : refusé par ADR 0020 (« It is never raised to reward tests »).
- Moq/NUnit : pas de gain, on a NSubstitute et xUnit v3 ; ADR 0001.

### 1.6 Ce qu'on peut faire qu'il ne fait pas (ses issues et discussions comprises)

- Le concurrent n'a ni architecture, ni contrat OpenAPI, ni default-deny, ni plancher de couverture, ni test de ses workflows. Aucune de ses issues ouvertes ne les réclame (8 ouvertes ; recherche titre « test », « coverage », « analyzer » : rien d'ouvert).
- Sa discussion la plus commentée, « This is not exactly The Clean Architecture style » (27 commentaires, https://github.com/jasontaylordev/CleanArchitecture/discussions/482), est précisément ce que nos règles d'architecture rendent vérifiable ; je ne l'ai pas relue en détail (voir « Non vérifié »).
- Issue ouverte https://github.com/jasontaylordev/CleanArchitecture/issues/1594 (le front Angular du template par défaut appelle sa propre URL au lieu de l'API, ouverte depuis 2026-03-26, deux commentaires) : une classe de défaut que ses 9 jambes (build + tests) ne voient pas. Chez nous `template-acceptance.yml:656-671` démarre la pile et vérifie « every web app serves its page and relays to the API » : on dépasse sur cette classe. Confiance moyenne : je n'ai pas reproduit son bug ; je constate que sa CI ne contient aucun test qui appelle l'API par le front sur un template généré (`test-templates.yml:59-77` : build, `npm run build`, `dotnet test`).
- Repasser sur notre défaut `NoWarn`/`#pragma` : choisir entre (a) un test d'architecture « aucun `NoWarn` ni `#pragma` hors une liste motivée » (la proposition C2 de `p4-devil.md` §C2) et (b) corriger la phrase. Le test d'abord, vu rouge une fois, comme l'exige `AGENTS.md`.

## Axe 2. CI et release

### 2.1 Comment fait le concurrent

- Quatre workflows : `build.yml` (PR vers main), `codeql.yml` (push, PR, lundi), `test-templates.yml` (push main, dispatch), `release.yml` (publication d'une GitHub Release).
- Actions référencées par tag mobile : `actions/checkout@v7` (`build.yml:29`), `actions/setup-dotnet@v6`, `actions/cache@v6`. Pas de `concurrency`, pas de `timeout-minutes`. `renovate.json` étend `config:recommended` sans `helpers:pinGitHubActionDigests`.
- Release : un humain publie une Release GitHub ; `release.yml:3-5` s'y déclenche ; il extrait la version du tag (`:28-30`), l'écrit dans `template.json` avec `jq` (`:32-36`), met les notes dans le nuspec avec Python (`:38-50`), `nuget pack` (`:52-53`), puis `nuget push ... -ApiKey ${{secrets.NUGET_API_KEY}} -SkipDuplicate` (`:61-62`). Aucune étape de build, de test ni de scan avant le push ; `mono-complete` installé pour `nuget.exe` (`:25-26`). Pas de provenance.
- Versionnement : la version du paquet est le tag, alignée sur la majeure .NET (10.8.0). Cadence : 22 releases, 14 en mars 2026, la dernière le 2026-03-23 (`gh api repos/jasontaylordev/CleanArchitecture/releases`).
- Fraîcheur : 58 commits sur main depuis v10.8.0, dont 54 `chore(deps)` (`git log v10.8.0..main`). `main` est protégée mais sans contrôle requis (`required_status_checks.contexts: []`, `enforcement_level: off` ; `gh api repos/.../branches/main`) ; `renovate[bot]` fusionne lui-même (#1674). `renovate.json:15-23` automerge minor, patch et digest.
- Conséquence publiée : issue ouverte https://github.com/jasontaylordev/CleanArchitecture/issues/1675 « 10.8.0 now fails to build because of NU1902/NU1903 security advisories » (2026-10-28) : « Build fails with 118 errors (Warning As Error due to vulnerability). The last release (10.8.0) was created 7 months ago. Even though the package versions were updated in this repo, due to not releasing the project, the nuget package didn't get the update. » Le correctif existe sur main (`Directory.Packages.props:33-34`, épinglage de `System.Security.Cryptography.Pkcs`) mais n'est pas publié.
- Ce que le projet généré reçoit : rien. Le nuspec exclut `.github/workflows/build.yml`, `codeql.yml`, `release.yml`, `test-templates.yml`, `.github/ISSUE_TEMPLATE`, `renovate.json` (`CleanArchitecture.nuspec:33`) ; le projet généré n'a ni CI, ni scan, ni mise à jour automatique des dépendances.
- Pas de Sonar, pas d'audit de paquets vulnérables en CI : seul l'avertissement de build (`NuGetAudit` par défaut, promu en erreur par `TreatWarningsAsErrors`).

### 2.2 Comment on fait

- Sept workflows (`ci`, `codeql`, `release`, `sonarqube`, `template-acceptance`, `template-release`, `web`) ; les quatre premiers et `web` sont aussi livrés au projet généré (option `ci`), `template-*` restent dans le dépôt (ADR template 0002 ; `TemplatePackage.csproj:46`).
- Chaque action est épinglée par SHA avec le tag en commentaire (`ci.yml:36`), chaque image par digest (`ci.yml:281`), `permissions: contents: read` par défaut (`ci.yml:16`), `concurrency` et `timeout-minutes` partout. `CheckWorkflows` le vérifie et se teste (`ci.yml:178-182`).
- Audit : un job `vulnerable-packages` lit le rapport de `dotnet list package --vulnerable --include-transitive` et échoue (`ci.yml:239-265`) ; le même contrôle précède la release (`release.yml:73-80`).
- Release du template : `template-release.yml:4-8` lance `release-please` à chaque push sur develop (`:37-43`) ; la fusion de sa PR tague `v<version>` ; ensuite `pack` (`:45-118`) vérifie que le tag égale la version (`:67-77`), installe le paquet dans une ruche isolée, génère et construit le projet par défaut (`:86-94`), génère avec le lanceur (`:96-104`), atteste la provenance (`:106-109`) ; `publish` (`:141-174`) passe par Trusted Publishing (`NuGet/login`, environnement `nuget` qui attend un relecteur, aucune clé stockée) ; `mirror` pousse vers GitHub Packages ; `promote` avance `main` par fast-forward (`:205-230`). ADR template 0006 et 0007.
- Release du projet généré : `release.yml` (tag `v*.*.*`) refait le gate, construit un lot de migrations par contexte, pousse l'image sur GHCR multi-arch avec SBOM et provenance (`release.yml:238-260`).
- Chaîne d'approvisionnement : versions exactes, `.github/dependabot.yml` par bandes hebdomadaires (nuget, actions, docker, npm, compose), jamais d'`ignore` (ADR 0001, ligne « Every dependency at an exact version… »). Pas d'automerge. `NuGetAudit` fait échouer le build « on any known advisory, `low` included » ; la réponse écrite est un épinglage transitif, « never a relaxed audit level » (`docs/guides/build-and-ci.md`, § Supply chain). CodeQL sur C#, TypeScript et les workflows eux-mêmes (`codeql.yml:41-51`), hebdomadaire (`:9-10`).
- Défaut constaté, le plus lourd de ce rapport : **le job `release-please` échoue à chaque push sur develop depuis le 2026-10-28.** Preuves :
  - `gh run view 41053367456 --repo Pierre-MarieMarchio/Template-DotNet_Clean_Architecture --log-failed` : « release-please failed: Unexpected token '﻿', "﻿{ "$sch"... is not valid JSON », juste après « Fetching .template.config/template.json from branch develop ». Les cinq jobs suivants sont ignorés (`pack`, `attach`, `publish`, `mirror`, `promote`).
  - Statistique : le workflow « Template release » compte 73 runs, dont 35 réussis, 36 échoués, 2 annulés ; les succès s'arrêtent au 2026-10-27 (`d5436af5`) ; le premier échec à suivre est le 2026-10-28T07:32 (`72293efe`), tous les suivants échouent, dernier constaté `935d552a`.
  - Cause : `.template.config/template.json` commence par `EF BB BF` (`head -c3 | xxd`), et `.github/release-please/config.json:12-18` le déclare en `extra-files` de type `json`. Le commit `cade4e3d` (2026-10-22) l'avait écrit sans BOM ; le commit `72293efe` (#137, 2026-10-28, premier échec) l'a réintroduit (`git show 72293efe^:.template.config/template.json` sans BOM, `git show 72293efe:...` avec `EF BB BF`).
  - Conséquence : la PR de release https://github.com/Pierre-MarieMarchio/Template-DotNet_Clean_Architecture/pull/91 « chore: release 1.1.0 » est figée à son contenu du 2026-10-27 et « behind » ; les 124 commits que `release-please` considère (même log) ne partiront dans aucune release tant que ce n'est pas corrigé.
  - Pourquoi rien ne l'a vu : `.template.config/acceptance/rules.json` classe `template-release.yml` et `.github/release-please/**` en « none » (aucune jambe) ; l'étape « Assert every generated JSON file is strict JSON » lit en `utf-8-sig` (`template-acceptance.yml:282`), donc tolère le BOM ; `CoverageFloorTests.TheFile_CarriesNoByteOrderMark` interdit le BOM pour `coverage.minimum` mais aucun test ne le fait pour `template.json`.

### 2.3 Verdicts

| Sous-axe | Verdict | Confiance |
|---|---|---|
| Mécanique de publication du paquet (identité, relecteur, provenance, essai du paquet avant push) | on dépasse | haute |
| Versionnement et notes (release-please, semver, `net<major>.0`) | choix différent assumé (ADR template 0006) ; on dépasse en mécanisme | haute |
| Fonctionnement actuel de ce pipeline | en retard (il est en panne depuis le 2026-10-28 ; le `release.yml` du concurrent compte 15 succès et 1 échec sur 16 runs) | haute |
| Fraîcheur de la dernière release publiée (risque `NU190x` pour l'utilisateur) | à égalité dans le risque, en retard dans la défense : on n'a ni cron ni release à jour ; le concurrent a le même défaut, avec 7 mois et une issue ouverte | moyenne |
| CI de PR : épinglage, concurrence, permissions, délais, test des workflows | on dépasse | haute |
| Ce que reçoit le projet généré (CI, CodeQL, Dependabot, release, Sonar optionnel) | on dépasse | haute |
| Mise à jour des dépendances (Dependabot par bandes, sans automerge, contre Renovate avec automerge) | choix différent assumé (ADR 0001, ligne « Every dependency at an exact version… ») | moyenne |
| Preuve du template généré avant fusion | on dépasse (exigée par le ruleset `develop`, 32 contrôles) contre un workflow qui ne tourne qu'après fusion | haute |
| Analyse statique de sécurité (CodeQL) | à égalité ; on y ajoute JS/TS et `actions` | haute |

### 2.4 Ce qu'on gagnerait à lui prendre

Peu, mais deux choses qui sont dans ses preuves :
- Son enseignement, pas son mécanisme : une release non rafraîchie casse la première génération d'un utilisateur dès qu'un avis apparaît. Forme chez nous : un workflow planifié hebdomadaire (`on: schedule`) qui installe le paquet publié le plus récent, génère le projet par défaut, lance `dotnet build` et `dotnet list package --vulnerable --include-transitive`, et ouvre une issue si l'un échoue. On a déjà les briques (`template-release.yml:86-94`, `ci.yml:257-265`) ; il manque le déclencheur (`grep -n "schedule\|cron" .github/workflows/*.yml` ne rend que `codeql.yml:9-10`). ADR template à écrire : « une release publiée est vérifiée chaque semaine » (nouveau numéro après 0020). Son effet sur nos utilisateurs est prouvé par l'issue #1675.
- Automerge pour les seules mises à jour de GitHub Actions et les patchs de digests, **seulement** si le ruleset exige les contrôles : on a déjà 32 contrôles requis sur develop (ruleset `develop`), ce que le concurrent n'a pas sur main. C'est un confort, pas une urgence ; confiance basse.

À corriger d'abord chez nous, avant d'emprunter quoi que ce soit :
1. Retirer le BOM de `.template.config/template.json` et ajouter un test d'architecture `TemplateManifest_CarriesNoByteOrderMark` du modèle de `CoverageFloorTests.cs:12-20` (et vu rouge une fois). Ajouter `[.template.config/template.json] charset = utf-8` dans `.editorconfig` pour que `dotnet format` ne le réécrive pas. Les deux sont de petites modifications ; aucun ADR.
2. Faire que la jambe « none » ne laisse plus `template-release.yml` et `.github/release-please/**` sans preuve : une étape de PR qui parse `template.json` en JSON strict (sans `utf-8-sig`) et lance `release-please` en `--dry-run` (ou à défaut le simple parse strict).

### 2.5 Ce qu'il faut refuser

- Sa publication avec une clé longue durée stockée (`secrets.NUGET_API_KEY`, `release.yml:62`) et sans aucun contrôle avant le push : on a fait l'inverse (ADR template 0006), à garder.
- Ses actions par tag mobile : refusé par `CheckWorkflows` (« an action referenced without a SHA pin »).
- Un `WarningsNotAsErrors` ou un `NuGetAuditLevel` relâché pour éviter l'erreur `NU190x` (la rustine naturelle après #1675) : `docs/guides/build-and-ci.md` l'interdit, et c'est ce qui garde les utilisateurs au courant des avis. La réponse reste l'épinglage et la release fraîche.
- Un projet généré sans CI ni mise à jour des dépendances : contraire à la promesse 8 de l'étalon.

### 2.6 Ce qu'on peut faire qu'il ne fait pas (ses issues et discussions comprises)

- https://github.com/jasontaylordev/CleanArchitecture/issues/1675 : avis de sécurité qui cassent la génération d'un paquet vieux. On peut le prévenir (cron ci-dessus) et le dire dans `docs/template/maintaining-the-template.md`.
- https://github.com/jasontaylordev/CleanArchitecture/issues/1461 « Dependency Dashboard » (ouverte, Renovate) : c'est le signe que ses mises à jour s'accumulent hors de toute release. Chez nous une PR de release unique rassemble tout, à condition que le défaut de 2.2 soit corrigé.
- https://github.com/jasontaylordev/CleanArchitecture/issues/198 (fermée) « Use GitHub Releases to Notify of New Versions » : ses utilisateurs l'ont demandé ; nos releases portent un journal par commits conventionnels et un paquet attesté.
- Aucune discussion de ses 367 ne porte sur les tests ou la CI dans le top 10 lu ; je ne l'ai pas cherché plus bas.
- Provenance vérifiable du paquet NuGet : on atteste le nupkg (`template-release.yml:106-109`), le concurrent n'atteste rien. On ne le documente pas côté utilisateur (`grep -n attestation README.md docs/template/*.md` : vide) : à ajouter, une ligne `gh attestation verify` dans le README. Petit gain, haute confiance.

## Non vérifié

- Aucun build, aucun test, aucune CI relancés : les durées sont celles des runs GitHub (de `run_started_at` à `updated_at`, file d'attente comprise), pas des mesures isolées.
- Les causes des 13 échecs de « Template acceptance » et des 9 de « CI » sur les 100 derniers runs ne sont pas lues ; seul « Template release » l'est.
- Je n'ai pas vérifié qu'une version 1.0.0 de nos deux paquets est bien téléchargeable sur nuget.org ; le cadrage l'affirme, l'inventaire a vu une page à 1.0.0.
- Les téléchargements NuGet ne sont pas relevés.
- Les discussions du concurrent ne se lisent pas par REST ; je n'ai pas lancé GraphQL (POST), donc je m'appuie sur la liste de `p1-inventory.md`.
- Je n'ai pas lu l'issue #1594 jusqu'à un correctif ; je ne dis pas que son bug serait attrapé par un test, seulement que nos tests couvrent cette classe.
- Je n'ai pas vérifié pourquoi le BOM est revenu dans `template.json` au commit `72293efe` (outil, éditeur, `format-fix`).
