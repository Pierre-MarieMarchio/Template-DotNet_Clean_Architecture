# P5 / G5 (sonnet) : docs et onboarding, communauté et entretien

Date : 2026-10-30. Lecture seule. Racine des chemins : `/tmp/claude-1000/-home-pmmarchio-code-Template-DotNet-Clean-Architecture/608f3f2d-3c02-4530-9e46-6f8e7e0e445b/scratchpad`.
Abréviations : `ours/` = `repos/ours` (develop, 5f22faa5, template.json 1.1.0-dev) ; `comp/` = `repos/competitor-main` (main, 345e7fe) ; `rel/` = `repos/competitor-release` (v10.8.0).
Sources externes lues en GET : `gh api` (dépôts, issues, discussions, releases, runs, journaux de workflow), pages publiques du site du concurrent (`curl`, copies dans `site/`), API de recherche nuget.org (compteurs de téléchargements), page HTML des discussions du concurrent.
Étalon : `out/00-cadrage.md` (promesses 3, 6, 8 ; template ADR 0002 « rien du template n'atteint un projet »).

## 0. Synthèse

| Axe | Verdict global | Confiance |
|---|---|---|
| 1. Docs et onboarding | on dépasse sur la doc du projet, ses garde-fous et ses ADR ; en retard sur la vitrine (site rendu, badges, description du dépôt) ; onboarding à égalité (chacun a une friction mesurée) | haute pour la doc et les ADR, moyenne pour l'onboarding |
| 2. Communauté et entretien | en retard sur l'audience, les canaux de questions et le code de conduite ; à égalité sur le bus factor ; notre chaîne de release automatisée est en panne depuis le 2026-10-27 | haute |

Trois constats qui comptent plus que les autres :
1. Notre release automatisée est cassée : `template-release.yml` échoue à chaque push sur develop depuis le 2026-10-27 à 19h (35 échecs), avec `release-please failed: Unexpected token '﻿' ... is not valid JSON`. `.template.config/template.json` commence par un BOM (`efbbbf`). La PR de release n°91 n'a pas bougé depuis le 2026-10-28 et propose 1.1.0 alors que deux commits `feat!` (#169, #172) sont arrivés après. Preuves en 2.4.
2. Notre dépôt GitHub n'a ni description, ni site, ni topics, ni Discussions, et `blank_issues_enabled: false` : un visiteur n'a aucun canal pour poser une question. Le concurrent a les quatre. Preuves en 1.2 et 2.5.
3. Les trois sources documentaires du concurrent (ADR, site, README) disent des choses que son code contredit, et aucun test ne les tient. Chez nous, des tests lisent les pages (codes d'erreur, réglages, citations). Nous avons quand même des écarts doc/code, listés en 1.3.

---

## AXE 1. Docs et onboarding

### 1.1 Où vit la documentation, et sous quelle forme

**Concurrent.** Dans le dépôt : 5 fichiers `.md` sous `docs/` (index des ADR, gabarit, 3 ADR) ; le reste est un site externe.
- `comp/README.md:11` : « For full documentation, visit **[cleanarchitecture.jasontaylor.dev](https://cleanarchitecture.jasontaylor.dev)** ».
- `comp/README-template.md:68` : le README du projet généré (68 lignes) renvoie au site (« go to the [project website](caDocsUrl) »). Le projet généré n'embarque donc aucune page de doc.
- Site (GET, 2026-10-30) : Starlight v0.38.1 sur Astro v6.0.4 (`<meta name="generator">` dans `site/getting-started.html`), recherche Pagefind (5 occurrences de « pagefind » dans la page), sitemap de 17 URLs : accueil, getting-started (installation, first-project, code-scaffolding, deployment), architecture (4 couches), features (aspire, automated-tests, multiple-database-options, multiple-ui-options, openapi-generated-clients, pipeline-behaviours, structured-use-cases). Annoncé le 2026-03-16 (discussion #1567, https://github.com/jasontaylordev/CleanArchitecture/discussions/1567) ; le source du site n'est pas dans le dépôt.
- Le tag v10.8.0 ne contient aucun `docs/` (inventaire p1 : 0 page).

**Nous.** 88 pages `.md` sous `ours/docs/` (88 502 mots, `find docs -name '*.md' | xargs wc -w`) : getting-started 3, architecture 10, features 8, guides 7, reference 3, decisions 29 (dont l'index), modèles de page 3, plus `docs/template/` (4 pages et 20 ADR du template) qui n'atteint jamais le projet.
- `ours/docs/README.md:7-14` : tableau « Folder | A page belongs here when the reader is » ; `:99-105` : « Adding a page » (où va une page, de quel modèle partir).
- `ours/.template.config/template.json:1124-1135` : exclusion de `README.md`, `docs/template/**`, `CHANGELOG.md`, `Tools/CheckDocPaths.cs` ; tout le reste de `docs/` est copié dans le projet.
- Pas de site rendu : la doc se lit sur GitHub ou dans le clone, avec les marqueurs `<!--#if-->` visibles en source.

**Verdict.** On dépasse (haute) pour un projet « gardé des années » (étalon) : la doc voyage avec le code, se conditionne par option et change dans la même PR. Choix assumé (template ADR 0002, `ours/docs/template/decisions/0002-nothing-about-the-template-reaches-a-project.md`, et ADR 0018 « Proposed »). En retard sur la découvrabilité avant adoption (1.2).

**À prendre.** Rien sur le fond. **À refuser.** Remplacer la doc du projet par un renvoi vers un site externe : un projet généré qui dépend d'une URL du template contredit la promesse 6 et le template ADR 0002.

### 1.2 Vitrine : site rendu, recherche, README, métadonnées du dépôt

**Concurrent.**
- `comp/README.md:3-7` : 5 badges (build, CodeQL, version NuGet, téléchargements NuGet, Twitter) ; `:79-89` : liste « Technologies » ; `:91-102` : table « Versions » (une branche par version de .NET, de `netcore3.1` à `net9.0` ; `main` = .NET 10) ; `:13` : « please give it a star ».
- Dépôt GitHub (GET) : `description: "Clean Architecture Solution Template for ASP.NET Core"`, `homepage: https://cleanarchitecture.jasontaylor.dev`, 9 topics (angular, asp-net, aspire, clean-architecture, dotnet, ...).
- `comp/.github/FUNDING.yml` : lien de sponsoring.

**Nous.**
- `ours/README.md:1-110` : aucun badge ; liste « What you get » (9 puces, `:9-26`), démarrage rapide (`:28-54`), table des options (`:56-81`), roadmap, support, licence. La liste `--web` du README (`:21-22`) dit « Vue, React, Svelte, Angular or a static page » alors que la table `:66` en compte 8 valeurs (react-router et blazor-wasm manquent à la liste).
- Dépôt GitHub (GET) : `description: null`, `homepage: null`, `topics: []`, 1 étoile, Discussions désactivées.
- Le paquet NuGet a ses métadonnées : `ours/.template.config/TemplatePackage.csproj` (`<PackageTags>dotnet-new;templates;clean-architecture;webapi;ddd;aspnetcore;efcore;postgresql;sqlserver;sqlite</PackageTags>`, description, readme, icône).

**Verdict.** En retard (moyenne : cela ne touche pas la qualité du projet généré, seulement la découverte et la confiance d'un visiteur). Le visiteur du dépôt ne voit ni description ni badge d'état de la CI alors que la CI existe (7 workflows, verts le 2026-10-30 sauf `Template release`, voir 2.4).

**À prendre (forme chez nous).** (a) Renseigner description, homepage et topics du dépôt : zéro code, effet immédiat ; (b) badges CI, CodeQL et version NuGet en tête du README ; (c) publier un site depuis `docs/` hors `docs/template/` : un workflow qui rend la variante complète (le dépôt dit déjà que `docs/` est « in its fullest form », `ours/README.md:85`). Si on le fait, un ADR du template (« la doc publiée est la variante complète ») : c'est un choix qu'une autre équipe aurait pu faire autrement. **À refuser.** Un site comme source unique.

### 1.3 La doc dit-elle vrai ? (le code et les tests d'abord)

**Concurrent : des écarts doc/code, aucun test ne lit la doc.**
- `comp/docs/decisions/ADR-003-MediatR-Contracts-In-Domain.md:13` : événements publiés « after `SaveChangesAsync` ». Le code : `comp/src/Infrastructure/Data/Interceptors/DispatchDomainEventsInterceptor.cs:25-29` appelle `await DispatchDomainEvents(...)` dans `SavingChangesAsync`, puis `base.SavingChangesAsync` : avant l'écriture. La page du site `/docs/architecture/infrastructure-layer/` répète l'ADR (« after the save completes, publishes any domain events »).
- `comp/README.md:84` : « Angular 21 » ; `comp/src/Web/ClientApp/package.json:20` : `"@angular/core": "^22.2.2"`. La page du site `/docs/features/multiple-ui-options/` dit aussi « Angular 21 ».
- `comp/README-template.md:57` : « The solution contains unit, integration, and functional tests » ; le projet `Infrastructure.IntegrationTests` ne contient aucun test (`out/p3-comp-1.md` : « 0 test (projet vide) »).
- Site `/docs/getting-started/first-project/` : « the template should build cleanly out of the box » et « All tests should pass on a freshly scaffolded solution » ; mesuré sur la version publiée 10.8.0 : 120 erreurs NU1902/NU1903 au `dotnet build` (`out/p2-comp-1.md`, journal 12:06:59), 6 tests d'acceptation en échec faute de navigateurs Playwright (12:08:55), issue ouverte #1675 (https://github.com/jasontaylordev/CleanArchitecture/issues/1675).
- Points favorables : les pages du site sont à jour sur la structure (couches, `IEndpointGroup`, `ca-usecase`), et il a une page de déploiement par `azd` (`/docs/getting-started/deployment/`) ; notre `ours/docs/guides/deployment.md` couvre l'image, les migrations à la release et les proxys mais pas un fournisseur précis.

**Nous : des tests lisent la doc, avec des limites et des écarts à corriger.**
- `ours/Tests/Architecture/AppTemplate.Architecture.Tests/Rules/ErrorCodeTableTests.cs:32,44` : chaque code d'erreur a sa ligne dans `docs/reference/api.md` et inversement ; `ConfigurationSurfaceTests.cs:141,157,201` : chaque réglage a sa section dans `docs/reference/configuration.md` ; `DocumentationCitationTests.cs:86-214` : chaque tâche, option, chemin et lien cités existent.
- Limite : dans le projet généré, `DocumentationCitationTests` ne lit que `AGENTS.md`, `README.md`, `CONTRIBUTING.md` et `SECURITY.md` (`DocumentationCitationTests.cs:12`, `:281-286`). `Tools/CheckDocPaths.cs`, qui lit les chemins de tout `docs/`, est exclu du projet (`template.json:1135`). Une page de `docs/` qui cite un fichier déplacé par l'équipe se périme sans signal (`out/p3-ours-2.md` l. 118 : treize pages périmées, gate vert, après deux retraits de feature).
- Écarts constatés (tous relus dans le dépôt) :
  - `ours/SECURITY.md:12-16` : « For the template itself, open a GitHub issue ... a public report is the fastest » ; `ours/README.md:105-106` : « report a vulnerability privately, in a security advisory » ; `ours/.github/ISSUE_TEMPLATE/config.yml:3-6` : « never in a public issue » ; GET `private-vulnerability-reporting` : `{"enabled":true}`. Trois textes, deux consignes.
  - `ours/docs/template/roadmap.md:39-41` : « WPF and Windows Forms » sous « Coming » alors que `wpf` est une valeur de `--presentation` (`ours/README.md:70`, `.template.config/starters/wpf/`).
  - `ours/.template.config/variants/project/README.md:57-67` : la table « Service | Address » du README de projet ne liste ni le portail ni le MCP que `ours/docs/getting-started/first-run.md:123,126` donne (8090, 8091).
  - `ours/docs/template/using-the-template.md:81-84` et `:99-103` : la même séquence `dotnet new install` / `dotnet new update` sous deux intitulés « From nuget.org ».
  - `ours/.template.config/template.json:103-104` : `dotnet new cleanarch-webapi --help` annonce `mysql` (« MySQL 8.4: a table prefix per feature ... ») alors que `:111,122-123` disent que la version ne le génère pas ; issue ouverte #165 (https://github.com/Pierre-MarieMarchio/Template-DotNet_Clean_Architecture/issues/165).
  - `ours/docs/guides/adding-a-setting.md:23` demande `<Section>OptionsValidator` « beside it » ; l'exemple cité, `IdempotencyPurgeOptions.cs:12` (dans `Src/Infrastructure/AppTemplate.Infrastructure.Persistence/Common/Idempotency/`), met le validateur `internal` dans le même fichier, car un second type public par fichier est refusé (`LayoutConventionTests.cs:586`). Qui suit la phrase à la lettre rougit deux tests (`out/p3-ours-1.md`, ligne « adding-a-setting »).

**Verdict.** On dépasse (haute) : les promesses 3 et 6 sont tenues en partie par test, ce que le concurrent n'a pas. La contradiction de SECURITY.md a un coût réel pour un rapporteur.

**À prendre.** Rien. **À corriger chez nous (sans emprunt).** Aligner SECURITY.md du dépôt sur l'advisory privée ; retirer WPF de la roadmap ; ajouter portail et MCP à la table du README de projet ; dédoublonner `using-the-template.md` ; reformuler l'étape 2 d'`adding-a-setting.md`. Étendre la vérification des chemins cités à tout `docs/` dans le projet généré serait un test de plus dans `Tests/Architecture` (règle « une règle vérifiée à la main devient un test », `ours/AGENTS.md`) ; rapatrier `CheckDocPaths` dans le projet change la décision du template ADR 0018 (un projet ne garde qu'une vérification de doc) : ADR à amender. **À refuser.** Un README de projet qui promet des tests d'intégration qu'il n'a pas.

### 1.4 Les décisions d'architecture (ADR)

**Concurrent.** 3 ADR + un gabarit + un index (`comp/docs/decisions/README.md:1-12`, table ADR | Title | Date | Status). Forme : Status, Date, Context, Decision, Rationale, Consequences (« Easier / Harder »), et une section « When this decision does not apply » (`ADR-001-Use-EFCore-In-Application-Layer.md:66-72` : elle renvoie vers DDD et repositories). ADR-001 (EF Core dans Application), ADR-002 (Aspire), ADR-003 (MediatR.Contracts dans Domain). Rien ne les tient par test, et ADR-003 contredit le code (1.3).

**Nous.** 28 ADR de projet (`ours/docs/decisions/0001..0028`) + 20 ADR du template. Critères d'écriture en trois tests (`docs/decisions/README.md:8-17`). Les ADR du projet propre démarrent à 1000, en `Proposed` (`:26-29`). Statut `Superseded by` (`:31-33`), gabarit avec « Alternatives considered » et « Revisit when » (`ours/docs/_page-templates/adr.md:14,24`), et la phrase « Where a test can hold a decision, it does, and the ADR names the test » (`docs/decisions/README.md:17-18`) ; ADR 0020 « one risk, one type of test » formalise la règle. ADR 0001 liste les choix hérités avec une cellule « Changed by ».

**Verdict.** On dépasse (haute) : volume, critères, cycle de vie, lien avec les tests. À égalité sur le format d'un ADR isolé : le « When this decision does not apply » du concurrent est un bon réflexe.

**À prendre.** Une rubrique « Where it does not apply » dans `ours/docs/_page-templates/adr.md`, seulement si un ADR l'a déjà dû dire ; gain faible. **À refuser.** Rien.

### 1.5 Le chemin d'onboarding (README, génération, premier appel)

**Concurrent.**
- `comp/README.md:23-35,61-75` : `dotnet new install Clean.Architecture.Solution.Template`, `dotnet new ca-sln --client-framework ... --database ... --output YourProjectName`, `aspire run` ou `dotnet run --project .\src\AppHost`. Prérequis `:19-21` : .NET 10, Node pour Angular/React, Docker seulement pour SQL Server et PostgreSQL (SQLite par défaut, sans conteneur). Trois exemples de commandes (`:44-57`).
- Mesuré sur la version publiée 10.8.0 (`out/p2-comp-1.md`) : install 2,7 s, génération 2,2 s, puis `dotnet build` en échec (120 erreurs NU1902/NU1903, aucun contournement documenté) ; avec `-p:NuGetAudit=false`, build propre, lancement par Aspire en 12 s, mais ports dynamiques, 401 sur l'API sans piste d'identifiant (`administrator@localhost` n'est que dans le code d'initialisation), avertissements de certificat non expliqués. Réussite « avec la seule doc » : 0 sur 3 builds.
- Le README généré `comp/README-template.md:13-23` utilise des chemins à antislash.

**Nous.**
- `ours/README.md:28-54` : `dnx CleanArch.Create` (questions guidées), `cd`, `dotnet run Tools/Tasks.cs bootstrap` (obligatoire, réseau), `cp .env.example .env`, `docker compose up -d --build --wait`, puis API sur :8080, Scalar sur :8080/scalar/v1, Mailpit sur :8025. Prérequis `:30` : .NET 10.0.100+ et Docker Compose v2, toujours.
- Mesuré sur develop 1.1.0-dev (`out/p2-ours-1.md`) : le mode guidé génère, commit, lance bootstrap (2m27 pour le défaut, 3m17 pour le riche) et prépare `.env` ; 3 257 tests verts sans Docker (1m10) ; pile par défaut saine en 38,7 s, inscription 202 et mail dans Mailpit. Frictions : `--yes` avalé par `dnx` (l'exemple de `ours/docs/template/using-the-template.md:68` ne marche pas tel quel, il faut `--`), et le message d'erreur recommande d'ajouter ce qui est déjà passé ; `GET /` répond 401 et rien ne dit d'appeler `/health` ; `test --no-integration` du projet riche prend 6m25 (deux suites à 2-3 min).
- Non mesuré : la version 1.0.0 de nuget.org ; les personas ont travaillé sur 1.1.0-dev.

**Verdict.** À égalité (moyenne), pour deux raisons opposées : le concurrent est plus court (3 commandes, pas de Docker en SQLite) mais sa version publiée ne compile pas ; nous avons plus d'étapes et exigeons Docker, mais le chemin mesuré arrive au bout. Choix assumé pour Docker : le projet généré est une pile Compose (ADR 0001) et SQLite est une base de développement (ADR 0021).

**À prendre.** Un bloc de trois exemples de commandes dans notre README (API seule, API + web, API + MCP), à la façon de `comp/README.md:44-57` : plus lisible que la table seule ; pas d'ADR. **À refuser.** Rendre Docker optionnel dans le démarrage rapide : la pile de référence est Compose. **Corrections nôtres.** Corriger l'exemple `--yes` (`using-the-template.md:68-70`) et le texte que le lanceur imprime (`out/p2-ours-1.md`, F1) ; dire dans `first-run.md` quoi appeler pour voir l'API vivre (`/health`).

### 1.6 Instructions pour les agents de code

**Concurrent.** Aucun `AGENTS.md`, `CLAUDE.md`, `copilot-instructions.md` ni `.cursorrules` dans le dépôt (`find comp -maxdepth 3 ...` : aucune sortie). Issue #1632 « can add AGENTS.md file make copilot can generate code by Clean Architecture » (https://github.com/jasontaylordev/CleanArchitecture/issues/1632) : ouverte le 2026-07-11, fermée le 2026-07-16, 0 commentaire.

**Nous.** `ours/AGENTS.md` (et `Src/Web/AGENTS.md`) : commandes, où va le code, règles, « Done means », « Where truths live » ; option `--agent-files` (`ours/README.md:65,90`) ; les pages qui le citent pointent la page propriétaire quand il est absent. Son contenu est tenu par `DocumentationCitationTests` (`:12`).

**Verdict.** On dépasse (haute). **À prendre.** Rien. **À refuser.** Rien.

### 1.7 Mettre à jour un projet depuis une nouvelle version du template

**Concurrent.** Discussion #1475 « Best strategy to keep a CA solution up to date » (https://github.com/jasontaylordev/CleanArchitecture/discussions/1475), réponse retenue du mainteneur : « There's no automated upgrade mechanism - that's a limitation of the `dotnet new` template system itself », puis désinstaller et installer la version qui correspond au projet.

**Nous.** `ours/docs/getting-started/existing-repository.md` couvre l'entrée d'un projet dans un dépôt existant ; aucune page ne dit comment reprendre une version plus récente du template. Le CHANGELOG définit ce qui est « notable » (un changement du projet généré) et « breaking » (un travail manuel pour un projet existant) (`ours/CHANGELOG.md:9-16`), matière d'un tel guide. Develop porte deux changements cassants depuis 1.0.0 (`feat!` #169 et #172, `git log` du clone).

**Verdict.** À égalité (haute : aucun des deux n'a de guide). **À faire.** Une page `docs/template/upgrading.md` fondée sur le CHANGELOG et sur la phrase de provenance du projet (`ours/docs/getting-started/overview.md:100`) ; elle répond à une question que la communauté du concurrent pose déjà. Pas d'ADR (une page).

### 1.8 Ce qu'on peut faire que le concurrent ne fait pas (axe 1)

- Une doc de projet de 88 pages, conditionnée et tenue en partie par test : le concurrent renvoie à un site, et sa page de premier projet affirme un build propre que la version publiée dément.
- Un AGENTS.md (l'issue #1632 du concurrent a été fermée sans réponse).
- Des gabarits de page (`ours/docs/_page-templates/{adr,feature,how-to}.md`) et des règles d'écriture (`ours/AGENTS.md`, « Writing docs »).
- Des messages de tests qui disent quoi faire (`PendingModelChangesTests`, `ErrorCodeTableTests`, `out/p2-ours-1.md`) : le concurrent n'a aucun test qui lise sa doc.

---

## AXE 2. Communauté et entretien

### 2.1 Audience et popularité

**Concurrent (GET 2026-10-30).** 20 692 étoiles, 4 295 forks, 371 observateurs, créé le 2019-10-07 ; 103 identités de commit (`git shortlog -sn`) ; NuGet `Clean.Architecture.Solution.Template` : 275 469 téléchargements, dont 6 011 pour 10.8.0 (API de recherche nuget.org).
**Nous.** 1 étoile, 0 fork, 0 observateur, créé le 2025-02-17 ; NuGet `CleanArch.WebApi.Templates` 1.0.0 : 89 téléchargements, `CleanArch.Create` 1.0.0 : 96 (mêmes sources ; ils incluent sans doute nos propres installations, non vérifié).

**Verdict.** En retard (haute), sans levier de code : seule la découverte joue (1.2 : description, topics, badges, annonce). **À refuser.** Toute course à l'audience qui dégrade la promesse 2 (une option retire, elle ne désactive pas) : le concurrent propose 3 front et 3 bases, nous 7 web, 6 présentations et 4 bases.

### 2.2 Cadence et état des releases

**Concurrent.** 22 releases (`gh api releases`) : 2022-06 : 2 ; 2022-10 : 1 ; 2023-06 : 1 ; 2023-11 : 1 ; 2025-07 : 1 ; 2025-11-11 : v10.0.0 ; puis 15 releases du 2026-02-28 (v10.1.0) au 2026-03-23 (v10.8.0). Aucune depuis : 221 jours. `main` avance pourtant (58 commits depuis le 2026-03-24 : 51 de `renovate[bot]`, 7 de l'auteur, `git log --since`), et la version publiée ne se construit plus : issue #1675, ouverte le 2026-10-28, 0 commentaire. `comp/.github/workflows/release.yml:3-5` : la publication NuGet part d'une release GitHub publiée à la main. `comp/renovate.json` : fusion automatique des mises à jour mineures et correctifs, mais rien ne déclenche de release.
**Nous.** 1 release, v1.0.0 le 2026-10-22, deux nupkg joints. Chaîne : release-please sur develop (`ours/.github/release-please/config.json`, `ours/.github/workflows/template-release.yml:3`, commentaire de tête qui décrit la suite), publication après le gate (`ours/.github/workflows/release.yml`, commit 60954b29 « release with release-please from develop and publish through Trusted Publishing »). État : 2.4.

**Verdict.** À égalité, par deux défauts distincts (haute sur les faits, moyenne sur le jugement) : le concurrent a une chaîne manuelle qui ne livre plus ; la nôtre est automatique et en panne.

**À prendre.** Du concurrent : rien. Leçon : sa chaîne de dépendances est automatisée jusqu'à `main` mais pas jusqu'à NuGet ; chez nous Dependabot (28 commits, `ours/.github/dependabot.yml`) ouvre des PR vers develop et la release est automatisée de bout en bout, à condition de la réparer (2.4). **À refuser.** Une release manuelle sans garde de fraîcheur des dépendances.

### 2.3 Notes de version et CHANGELOG

**Concurrent.** Pas de CHANGELOG. Le corps de chaque release est la liste générée des PR (« What's Changed »). v10.8.0 : 6 puces, dont 5 de `renovate[bot]` et « Slim down Todo demo and add colour theming » (#1587), sans autre explication ; v10.0.0 : une puce (« Upgrade to .NET 10 »). Le corps est recopié dans le nuspec (`out/p1-map-comp.md`, ligne « Vie du projet »).
**Nous.** `ours/CHANGELOG.md` (1 158 lignes) avec une politique écrite : `:9-16` « What counts as notable in a template ... anything that changes the *generated* project » et « What "breaking" means here ». Mais le corps de la release GitHub v1.0.0 (GET `releases/tags/v1.0.0`) est une seule ligne : `### Documentation * **security:** ask a SQLite project to move to a server database before it deploys` ; la section « 1.0.0: what the first release contains » (`ours/CHANGELOG.md:26`) n'est pas dans le corps. Pas de discussion d'annonce.

**Verdict.** On dépasse sur la politique et le contenu du fichier (haute) ; en retard sur la page Release elle-même (haute) : c'est celle qu'un adoptant ouvre. **À prendre.** L'annonce de chaque release dans les Discussions (le concurrent en a une par release, #1508 à #1593). Pour nous : faire porter à la release le corps complet de la première entrée. Pas d'ADR (la branche de release est déjà couverte par le template ADR 0006).

### 2.4 La chaîne de release automatisée est en panne (défaut à corriger)

- `gh api .../actions/workflows/template-release.yml/runs` : 36 échecs, 2 annulations, 35 succès ; dernier succès le 2026-10-27T18:58:17Z (sha d5436af) ; 35 échecs depuis, dont les trois derniers poussés le 2026-10-30 (12:10, 11:43, 11:02).
- Journal du job « Keep the release pull request », pas « Run release-please » : `##[error]release-please failed: Unexpected token '﻿', "﻿{ "$sch"... is not valid JSON`, juste après `Fetching .template.config/template.json from branch develop` (journal téléchargé en GET, `logs/l.zip`).
- `head -c 6 ours/.template.config/template.json | xxd` : `efbb bf7b 0a20` : le fichier commence par un BOM. `config.json` le fait lire en JSON (`extra-files`, `jsonpath: $.symbols.templateVersion.parameters.value`).
- Conséquence : la PR n°91 « chore: release 1.1.0 » (https://github.com/Pierre-MarieMarchio/Template-DotNet_Clean_Architecture/pull/91) a été mise à jour pour la dernière fois le 2026-10-28T16:37Z, avant les deux commits `feat!` du 2026-10-29 et du 2026-10-30 ; la politique du CHANGELOG (`:6-8`) en ferait un majeur.
- Les jobs de publication sont `skipped` : rien n'est publié à tort. Cause exacte non prouvée : le BOM est la seule piste relevée, la correction n'a pas été rejouée.

**Verdict.** En retard (haute) sur la promesse 8 (release), et nous seuls. **À faire.** Retirer le BOM de `template.json` ou faire lire le JSON sans BOM (vérifier ce que `format-fix` et les tests exigent des `.json`) ; ajouter un test qui échoue quand `template.json` a un BOM, car la panne est silencieuse (la CI des PR est verte, seul le workflow de release échoue). Pas d'ADR.

### 2.5 Questions, issues, discussions : canaux et réactivité

**Concurrent.**
- `comp/.github/ISSUE_TEMPLATE/config.yml` : issues vides autorisées, lien « Ask a question » vers les Discussions.
- Discussions : 367 au total (GET `discussions` ; Q&A 184, General 122, Ideas 36, Announcements 18, Show and tell 6, Polls 1) ; par année : 2021 : 60, 2022 : 90, 2023 : 102, 2024 : 64, 2025 : 27, 2026 : 22 ; Q&A répondues : 38 sur 184 ; sur les 12 derniers mois : 6 Q&A, 3 répondues ; dernière discussion d'utilisateur : 2026-04-17 (#1608).
- Issues : 539 fermées, 8 ouvertes. Rythme : le mainteneur ferme par lots (2025-07-16 à 07-28, 2026-02-18 à 02-25, 2026-10-11) ; correctifs le jour même en période active (#1555 et #1565, mars 2026) ; ailleurs 2 à 5 mois (#1613 : ouverte le 2026-04-29, fermée le 2026-10-11 ; #1625 : ouverte le 2026-05-25, première réponse du mainteneur le 2026-10-11). #1594 (« Wrong API Port is used by the Angular Frontend », 2026-03-26) : aucune réponse du mainteneur (2 commentaires d'utilisateurs).

**Nous.**
- Issues : 16 au total (10 ouvertes, 6 fermées), toutes ouvertes par l'auteur du dépôt, 0 commentaire d'un tiers (GET `issues`). Elles servent de plan de travail (« Later: ... », étiquette `roadmap`).
- Discussions désactivées (HTTP 410). `ours/.github/ISSUE_TEMPLATE/config.yml:1-6` : `blank_issues_enabled: false`, un seul `contact_links` (sécurité). Deux formulaires (`bug_report.yml` : version, source d'installation, commande de génération, étapes ; `feature_request.yml`). Aucun canal pour une question.
- `ours/README.md:101-106` : « Report a bug or ask for a feature through the repository's issue forms ».

**Verdict.** En retard sur les canaux (haute). La réactivité aux tiers n'est pas mesurable chez nous (0 issue externe) : à égalité (basse). Le canal du concurrent s'amenuise (27 puis 22 discussions par an, 21 % de Q&A répondues) : n'en prendre que la forme.

**À prendre.** Activer les Discussions avec une catégorie Q&A et ajouter un `contact_links` « Ask a question » ; garder les formulaires structurés. **À refuser.** `blank_issues_enabled: true` sans formulaire : les formulaires qui exigent la version et la commande de génération ont une valeur de triage que les issues libres du concurrent n'ont pas.

### 2.6 Bus factor

**Concurrent.** `git shortlog -sn HEAD` : l'auteur apparaît sous 4 identités (551 + 76 + 10 + 1 commits), soit environ 637 des 800 commits non automatiques (80 %) ; sur le dernier an, 72 des 82 commits humains (88 %) ; autres : sc2107 (7), LukasGrafTT (2), Henry Crans (1). Le reste est `renovate[bot]` (106) et `dependabot[bot]` (70). 103 identités au total.
**Nous.** `git shortlog -sn` : 495 + 19 commits du même auteur sous deux identités (100 % des commits non automatiques), 28 `dependabot[bot]`, 1 `github-actions[bot]` ; 543 commits. Sur les 100 PR les plus récentes : 62 de l'auteur, 36 de dependabot, 2 du bot de release (GET `pulls`).

**Verdict.** À égalité en pratique : un mainteneur unique de chaque côté (haute). Le concurrent a une traîne de contributeurs ponctuels, pas de relève. Chez nous le risque est atténué par ce qui est écrit : 48 ADR, AGENTS.md, tests d'architecture, gabarits (moyenne). **À faire.** `ours/CONTRIBUTING.md` (71 lignes) parle surtout de ce qu'un projet généré doit vérifier ; une section « première contribution au template » et des issues `good first issue` seraient le seul moyen d'élargir la base ; sans audience (2.1) l'effet est faible.

### 2.7 Fichiers de santé de la communauté et sécurité

| Fichier | Concurrent | Nous |
|---|---|---|
| Code of conduct | `comp/CODE_OF_CONDUCT.md` (GET community profile : true) | absent (false) |
| Contributing | absent | `ours/CONTRIBUTING.md` |
| Modèle de PR | absent | `ours/.github/pull_request_template.md` (tenu par `PullRequestTemplateTests`) |
| Modèles d'issue | 2 modèles Markdown (`bug_report.md`, `feature_request.md`) sans champ obligatoire, 1 `config.yml` | 2 formulaires YAML avec champs obligatoires, 1 `config.yml` |
| SECURITY.md | absent | `ours/SECURITY.md` (contradictoire pour le dépôt lui-même, 1.3) |
| Rapport privé de vulnérabilité | désactivé (GET : `enabled:false`) | activé (GET : `enabled:true`) |
| Score « health » GitHub | 71 | 71 |

**Verdict.** À égalité sur le score, on dépasse sur la structure des formulaires et la PR (moyenne) ; en retard sur le code de conduite (haute ; correction à un fichier, Contributor Covenant). Corriger SECURITY.md (1.3).

### 2.8 Issues ouvertes et discussions du concurrent : ce qu'on offre déjà

| Demande chez lui | URL | Chez nous | Preuve |
|---|---|---|---|
| « Add --minimal option to exclude sample code » (« on the roadmap » depuis 2026-02-19) | https://github.com/jasontaylordev/CleanArchitecture/issues/1192 | `--examples none` | `ours/README.md:61`, `template.json:202-214` |
| « Make Todo & Identity Features Optional » (fermée le 2026-10-11) | https://github.com/jasontaylordev/CleanArchitecture/issues/1613 | `--examples none`, `--auth external`, `--auth none` | `ours/README.md:60-61,74-76` |
| « Add Support for Vue » (ouverte) | https://github.com/jasontaylordev/CleanArchitecture/issues/1570 | `--web vite-vue` | `ours/README.md:66` |
| « Add Support for Blazor » (ouverte) | https://github.com/jasontaylordev/CleanArchitecture/issues/1444 | `--web blazor-wasm`, `--presentation blazor-server` | `ours/README.md:66,70` |
| « can add AGENTS.md » (fermée sans réponse) | https://github.com/jasontaylordev/CleanArchitecture/issues/1632 | `AGENTS.md`, `--agent-files` | 1.6 |
| « Wrong API Port is used by the Angular Frontend » (ouverte depuis 2026-03-26) | https://github.com/jasontaylordev/CleanArchitecture/issues/1594 | ports fixes 8080 (API) et 5173 (web) dans Compose et dans le README | `ours/.template.config/variants/project/README.md:57-67` |
| « 10.8.0 now fails to build because of NU1902/NU1903 » | https://github.com/jasontaylordev/CleanArchitecture/issues/1675 | même `NuGetAudit` en erreur, mais documenté et vérifié par `verify` (`dotnet list package --vulnerable --include-transitive`), règle « une épingle transitive, jamais un niveau d'audit relâché » | `ours/docs/guides/build-and-ci.md:161-165`, `Tools/Tasks.cs:681,2029-2045`, `Directory.Build.props:19` |
| Discussions « Why MediatR? » (#1009), « Repository in Asp Core Clean Architecture » (#405), « Email services... » (#685), « Can you create a schedule jobs solution? » (#395) | https://github.com/jasontaylordev/CleanArchitecture/discussions/1009, .../405, .../685, .../395 | réponses écrites : ADR 0003 (pas de MediatR), ports en quatre mots, module Email multilingue, jobs sous bail | `ours/docs/architecture/README.md:85-88`, `docs/features/email.md`, `docs/features/background-jobs.md` |
| MediatR devenu commercial (#1325), avertissement de licence (#1413) | https://github.com/jasontaylordev/CleanArchitecture/issues/1325, .../discussions/1413 | pas de MediatR : sans objet | `ours/docs/architecture/README.md:85` |

Ce qu'on ne peut pas revendiquer : MySQL (#1571, https://github.com/jasontaylordev/CleanArchitecture/issues/1571) est refusé chez nous (« Not planned », `ours/docs/template/roadmap.md:64`) ; SvelteKit en SPA statique (#1625) : notre `vite-svelte` est `create-vite`, pas SvelteKit (issue #119 ouverte chez nous, https://github.com/Pierre-MarieMarchio/Template-DotNet_Clean_Architecture/issues/119).

**Verdict.** On dépasse (haute) sur ces points. Il faut les dire : ce sont des demandes d'utilisateurs réelles, et une section « Comparaison » du README, ou une page, les rendrait lisibles ; pas d'ADR.

---

## Tableau de verdicts

| Id | Axe | Constat | Verdict | Confiance |
|---|---|---|---|---|
| G5-1 | docs | Doc de projet : 88 pages livrées au projet vs 5 pages + site externe | on dépasse (choix assumé template ADR 0002) | haute |
| G5-2 | docs | Pas de site rendu, ni recherche, ni badges ; dépôt sans description ni topics | en retard | moyenne |
| G5-3 | docs | Doc tenue en partie par test chez nous ; écarts chez lui (ADR-003, README, README-template) ; nos propres écarts listés | on dépasse | haute |
| G5-4 | docs | 28 + 20 ADR avec critères et lien aux tests vs 3 ADR | on dépasse | haute |
| G5-5 | docs | Onboarding : 3 commandes mais build publié cassé vs 5 commandes et pile saine (mesurée sur develop) | égalité | moyenne |
| G5-6 | docs | AGENTS.md absent chez lui | on dépasse | haute |
| G5-7 | docs | Aucun guide de mise à jour d'un projet des deux côtés | égalité | haute |
| G5-8 | communauté | Audience : 20 692 étoiles, 275 469 téléchargements vs 1 étoile, 89 | en retard | haute |
| G5-9 | communauté | Releases : lui 221 jours sans release et build publié cassé ; nous chaîne auto en panne | égalité | moyenne |
| G5-10 | communauté | release-please échoue (BOM de template.json) depuis 2026-10-27, PR 91 périmée | en retard | haute |
| G5-11 | communauté | CHANGELOG : politique écrite chez nous ; corps de la release v1.0.0 d'une ligne | on dépasse (fichier), en retard (page Release) | haute |
| G5-12 | communauté | Canaux : 367 discussions chez lui ; chez nous aucun canal de question | en retard | haute |
| G5-13 | communauté | Réactivité aux tiers : non mesurable chez nous ; fermetures par lots chez lui | égalité | basse |
| G5-14 | communauté | Bus factor : un mainteneur des deux côtés (80 à 88 % vs 100 %) | égalité | haute |
| G5-15 | communauté | Santé : CoC absent chez nous ; SECURITY.md contradictoire ; formulaires structurés et advisories privées chez nous seulement | égalité | moyenne |
| G5-16 | communauté | Demandes ouvertes du concurrent que nous couvrons (#1192, #1613, #1570, #1444, #1632) | on dépasse | haute |

## Non vérifié

- Téléchargements NuGet : compteurs de l'API de recherche nuget.org ; ils ne disent pas combien sont des CI ou des installations de l'auteur.
- Onboarding de notre version publiée 1.0.0 : les personas ont mesuré 1.1.0-dev.
- Cause de l'échec release-please : lue dans le journal (« not valid JSON » après la lecture de `template.json`), BOM constaté, correction non rejouée.
- Discussions du concurrent : décompte par l'endpoint REST `discussions` ; les comptes de commentaires de quelques discussions viennent de la page HTML et sont approximatifs.
- Délai de première réponse du concurrent sur les issues : calculé sur les dates d'ouverture et de fermeture, pas sur le premier commentaire du mainteneur (sauf #1625).
- Site du concurrent : lu en partie (pages getting-started, features, architecture) ; ni la page « pipeline-behaviours » ni les pages de couche autres qu'application et infrastructure n'ont été relues.
- Aucune build ni génération n'a été lancée par ce rapport.
