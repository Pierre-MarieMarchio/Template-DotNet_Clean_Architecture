# Phase 5, groupe G5 : docs et onboarding, communauté et entretien (analyste opus)

Clones lus en lecture seule, sans build ni génération :
- nous : `repos/ours`, develop `5f22faa5` (le `templateVersion` `1.1.0-dev` est un artefact de l'audit), noté `ours:` ;
- concurrent : `repos/competitor-main`, main `345e7fe`, noté `comp:` ; tag `repos/competitor-release` (v10.8.0), ce que livre le paquet NuGet, noté `rel:`.

Racine des chemins : `/tmp/claude-1000/-home-pmmarchio-code-Template-DotNet-Clean-Architecture/608f3f2d-3c02-4530-9e46-6f8e7e0e445b/scratchpad/`.
État en ligne lu le 2026-10-30, en GET seulement : `gh api` (REST, aucune requête GraphQL), WebFetch du site
`cleanarchitecture.jasontaylor.dev` et des pages Discussions de GitHub, API de recherche nuget.org.
Matière reprise et revérifiée : l'étalon `out/00-cadrage.md`, les cartes et l'inventaire p1, les personas p2 et p3, le socle p4.
Quand mon compte diffère d'une carte, je le signale.

---

## Résumé

- **Docs et onboarding : on dépasse sur le fond, avec une dette de surface.** Le projet que nous générons reçoit 88 pages,
  conditionnées par les options (790 blocs `<!--#if` dans 56 pages). Des tests vérifient que les chemins, les tâches, les réglages et
  les codes cités existent vraiment. Nous avons aussi 28 ADR, dont 25 nomment le test qui les tient. Le projet du concurrent ne reçoit
  qu'un README de 68 lignes, plus 3 ADR sur main ; le tag v10.8.0 n'en contient aucune. Sa vraie doc est un site externe, court et bien
  construit, mais il ne suit pas les versions.
  Deux points jouent pour lui :
  - un premier contact beaucoup plus court ;
  - une page NuGet qui se suffit à elle-même.
  Chez nous, j'ai trouvé 9 écarts entre la doc et le code qu'aucun test ne voit. Les deux plus visibles : le README du dépôt omet
  `--database`, et le README généré promet une API à un projet qui n'en a pas.
- **Communauté et entretien : en retard, nettement.** Les écarts sont les suivants :
  - 20 692 étoiles contre 1, et 275 469 téléchargements NuGet contre 185 ;
  - 17 releases en 24 mois contre 1 ;
  - 367 discussions contre des Discussions désactivées ;
  - aucune issue externe chez nous.

  Le bus factor vaut 1 des deux côtés. Notre chaîne de release est cassée depuis le 2026-10-28, et les notes GitHub de la 1.0.0 ne
  disent presque rien. Le concurrent a ses propres faiblesses : des réponses qui prennent parfois des mois, 58 commits non publiés, un
  lien d'ADR mort dans une réponse du mainteneur. Plusieurs de ses issues et discussions ouvertes demandent ce que nous livrons déjà.

---

## Axe 1. Docs et onboarding

### 1.1 Où vit la doc, et qui la reçoit

**Concurrent.** La doc de référence est un site hors du dépôt. Le README y renvoie :
```text
For full documentation, visit **[cleanarchitecture.jasontaylor.dev](https://cleanarchitecture.jasontaylor.dev)**.
```
Références : `comp:README.md:11`, `comp:.template.config/template.json:220-227` (`caDocsUrl`, injecté dans le README généré).

D'après WebFetch, le site compte 18 pages en trois sections :
- **Getting started**, 5 pages : Overview, Installation, Your first project, Code scaffolding, Deployment ;
- **Architecture**, 5 pages : une par couche, plus l'overview ;
- **Features**, 8 pages.

Une annonce du 2026-03-16 l'a ouvert : https://github.com/jasontaylordev/CleanArchitecture/discussions/1567.

Le site ne suit pas les versions : le pied de page affiche « 10.8.0 », sans aucune page par version. Le README tient pourtant une branche
par version de .NET (`comp:README.md:93-102`).

Le projet généré reçoit :
- `README-template.md`, renommé en `README.md` (68 lignes : build, run, scaffolding, test, ADR, aide) ;
- `docs/decisions/` sur main seulement. Le répertoire n'est pas exclu (`comp:.template.config/template.json:297-361`), mais il est
  arrivé après le tag : `f934abb`, 2026-03-28, #1595. `ls rel:docs` répond « No such file or directory ».

Un utilisateur de v10.8.0 reçoit donc un projet sans aucune page de doc.

**Nous.** La doc appartient au projet généré, pas au template. Elle est livrée et conditionnée par les options :
```text
- **The documentation under `docs/` is the project's.** It speaks of "this project", reads true the
  day after generation and a year later, and is conditioned per option like the code.
```
Références : `ours:docs/template/decisions/0002-nothing-about-the-template-reaches-a-project.md` (Accepted) et le résumé qu'en fait
la décision 0018 (`ours:docs/template/decisions/0018-...md:16-18`). Les chiffres :
- 88 pages, environ 88 500 mots (`find docs -name '*.md' -exec cat {} + | wc -w`) ;
- 790 blocs `<!--#if` dans 56 pages ;
- un index qui classe les pages par intention du lecteur (`ours:docs/README.md:7-14`) et retire les lignes des parties absentes
  (`ours:docs/README.md:35-40`, `<!--#if (api)-->`) ;
- un README propre au projet généré, tiré d'une variante (`ours:.template.config/variants/project/README.md`, 93 lignes) ;
- `AGENTS.md`, `CONTRIBUTING.md` et `SECURITY.md`, livrés aussi.

La provenance est inscrite dans le projet :
```text
This project was generated by version 0.0.0-checkout of `CleanArch.WebApi.Templates`, ...
| Option | Value | Gives |
```
Référence : `ours:docs/getting-started/overview.md:100-108`. `0.0.0-checkout` est remplacé à l'empaquetage
(`ours:docs/template/maintaining-the-template.md:526`).

**Verdict : on dépasse, choix assumé (template ADR 0002). Confiance haute.** Notre étalon veut une « doc du projet, pas du template,
conditionnée par les options » (promesse 6). Le concurrent fait l'inverse : une doc du template, sur un site qui ne suit pas les versions
et qu'un projet de 2026-03 lira décrire une version ultérieure.

### 1.2 Premier contact : README, page NuGet, « first run »

**Concurrent.** Le README est court (118 lignes) et donne les options complètes, avec leurs valeurs par défaut :
```text
| `--client-framework` | `angular`, `react`, `none` | `angular` |
| `--database` | `postgresql`, `sqlite`, `sqlserver` | `sqlite` |
```
Référence : `comp:README.md:37-40`. Viennent ensuite trois exemples et la commande `aspire run` (`comp:README.md:42-67`). La page
« Your first project » du site fait environ 500 mots : create, explore, build, run, API, database, tests. Les personas confirment la
clarté du parcours :
- « La page nuget.org est autonome : install, trois variantes de generation, lancement, scaffolding » (`out/p2-comp-2.md`, ligne 30) ;
- « Lire le README GitHub ~1 min OK » (`out/p2-comp-1.md`, ligne 59).

Ce que la doc promet ne tient pas :
- « the template should build cleanly out of the box » : 0 build sur 3 a réussi sans deviner `-p:NuGetAudit=false`
  (`out/p2-comp-1.md`, lignes 77 et 85 ; issue https://github.com/jasontaylordev/CleanArchitecture/issues/1675) ;
- rien sur `dotnet dev-certs`, ni sur l'identifiant d'amorçage `administrator@localhost` : 0 occurrence dans la doc
  (`out/p2-comp-1.md`, lignes 52 et 87-89).

**Nous.** Le README annonce la promesse, puis un quick start en 4 commandes et une table d'options (`ours:README.md:28-70`). Le persona
p2-ours-1 l'a suivi jusqu'à une requête réelle :
- README lu en ~1 min ;
- bootstrap en 2 min 27 ;
- `test --no-integration` en 1 min 10, 3 257 tests verts ;
- `compose up` en 38,7 s ;
- `/health` 200, register 202, mail visible dans Mailpit.

Source : `out/p2-ours-1.md`, lignes 66-82. Le « first run » fait 1 955 mots et va jusqu'à un appel authentifié, puis une première feature
(`ours:docs/getting-started/first-run.md`).

Défauts de premier contact, tous vérifiés dans le code :
- `ours:README.md:56-70` : la table d'options n'a pas de ligne `--database`, alors que la ligne 13 dit « chosen at generation ». Le
  persona l'a relevé (`out/p2-ours-1.md`, ligne 23).
- `ours:README.md:21` cite « Vue, React, Svelte, Angular or a static page », sans React Router ni Blazor WebAssembly. La ligne 66 les liste
  pourtant.
- Le README du projet généré n'a pas de branche sans API : il affirme « This project is a .NET 10 HTTP API » et liste l'API sur
  `:8080` et Scalar sans condition `api` (`ours:.template.config/variants/project/README.md:9-20,55-61`). Or ce projet existe :
  - `api` est calculé comme `(presentationApi || needsApi)` dans `template.json` ;
  - la jambe d'acceptation `--presentation blazor-server --presentation mcp --presentation worker --auth external` ne contient pas
    l'API (`ours:.template.config/acceptance/matrix.json:86`).

  À l'inverse, `overview.md` et `AGENTS.md` ont bien leur branche `!api`. Je n'ai pas généré ce projet, la preuve est la condition.
- `dnx CleanArch.Create -- --help` exige le `--`, et le README ne le dit pas (`out/p2-ours-1.md`, ligne 22).

**Verdict : à égalité. Confiance moyenne.** Le concurrent gagne sur la brièveté et sur l'autonomie de la page NuGet. Nous gagnons sur un
parcours qui marche de bout en bout, jusqu'à l'appel authentifié. Lui promet un build propre qui échoue aujourd'hui.

### 1.3 Profondeur : architecture, guides, références

**Concurrent.** Ce qui existe :
- une page par couche sur le site (environ 350-400 mots pour l'overview, un schéma SVG) ;
- les guides « Code scaffolding » et « Deployment » (azd, environ 450 mots ; la page ne traite ni des secrets ni de l'identité, d'après
  WebFetch) ;
- aucune référence de configuration, de routes ni de codes d'erreur.

Le persona p3-comp-1, sans le site, a constaté :
- « Le projet n'a AUCUNE migration et pas de `dotnet-ef` (ni manifeste d'outils, ni mention au README) » (`out/p3-comp-1.md`, ligne 39) ;
- « Aucun exemple d'options... Rien ne vérifie qu'un réglage est documenté » (`out/p3-comp-1.md`, lignes 45-47).

**Nous.** Ce qui existe :
- 10 pages d'architecture et 8 pages de capacités ;
- 7 guides : feature, retrait, réglage, migrations, tests, CI, déploiement ;
- 3 références : configuration, API avec routes et codes, arborescence ;
- un schéma mermaid (`ours:docs/architecture/README.md`) ;
- une table « What is deliberately absent » qui répond d'avance aux questions récurrentes. Exemple :
  `| MediatR, a dispatcher, pipeline behaviours | a controller calls a use case through its interface | ADR 0003 |`
  (`ours:docs/architecture/README.md:85,90`).

Les guides ne sont pas sans manques :
- **Ajouter un réglage.** L'étape 2 du guide dit « Write `<Section>OptionsValidator` ... beside it » (`ours:docs/guides/adding-a-setting.md:23`).
  Le persona en a fait un fichier séparé, et `FeatureFolderVocabularyTests` l'a refusé
  (`ours:Tests/Architecture/.../FeatureFolderVocabularyTests.cs:37`, `["Options"] = ["Options"]`). Le code d'exemple, lui, met le
  validateur dans le même fichier (`ours:Src/Presentation/AppTemplate.Api/Features/Files/Jobs/JobsFilesOptions.cs:22`). Sources :
  `out/p3-ours-2.md`, ligne 86 ; `out/p3-ours-1.md`, ligne 92.
- **Ajouter un événement de domaine.** Aucun guide (`out/p3-ours-2.md`, lignes 96-100).

**Verdict : on dépasse. Confiance haute.**

### 1.4 La doc tenue par les tests

**Concurrent.** Aucun test ne lit la doc. Les écarts entre doc et code se constatent à la lecture :
- `comp:README.md:84` dit « Angular 21 », alors que `comp:src/Web/ClientApp/package.json:20` est en `^22.2.2`.
- `comp:README-template.md:57` dit « The solution contains unit, integration, and functional tests. » Or
  `comp:tests/Infrastructure.IntegrationTests/` ne contient que `GlobalUsings.cs` et le `.csproj`.
- Le site, page Automated tests, annonce « four distinct layers » et n'en décrit que trois catégories (WebFetch).
- `comp:docs/decisions/ADR-003-MediatR-Contracts-In-Domain.md:13` dit que les événements partent « after `SaveChangesAsync` ». Le code les
  publie dans `SavingChanges(Async)`, donc avant l'écriture
  (`comp:src/Infrastructure/Data/Interceptors/DispatchDomainEventsInterceptor.cs:17-29`).
- Dans l'issue #1554, le mainteneur renvoie vers `wiki/ADR-003-Adopting-Aspire-For-Orchestration-And-Testing`. Le wiki est désactivé
  (`gh api repos/jasontaylordev/CleanArchitecture --jq .has_wiki` répond `false`), et l'ADR Aspire porte aujourd'hui le numéro 002
  (`comp:docs/decisions/README.md:10`). Le lien est donc mort et le numéro faux. Source :
  https://github.com/jasontaylordev/CleanArchitecture/issues/1554.

**Nous.** Plusieurs tests et outils lisent la doc :
- `DocumentationCitationTests` vérifie trois choses :
  - les chemins et les liens des pages racines et des `AGENTS.md` (`ours:Tests/Architecture/AppTemplate.Architecture.Tests/Rules/DocumentationCitationTests.cs:12,137-211`) ;
  - les tâches et options citées (`:85-120`) ;
  - le sort des lignes de chaque feature « owner » (`:122-135`).

  Le fichier contient aussi une contre-épreuve (`:213`).
- `ConfigurationSurfaceTests` exige une section de `configuration.md` pour chaque réglage.
- `ErrorCodeTableTests` confronte la table des codes et le code, dans les deux sens.
- `Tools/CheckDocPaths.cs` contrôle les chemins de toutes les pages, côté template seulement (`ours:AGENTS.md`, section « Working on the template »).

Ces garde-fous ne voient pas tout. J'ai trouvé 9 écarts entre la doc et le code qu'aucun test n'attrape :
1. `ours:README.md:56-70` : pas de ligne `--database`.
2. `ours:README.md:21` : la liste des fronts est incomplète.
3. `ours:docs/architecture/layers-and-projects.md:34` et `ours:docs/reference/project-layout.md:201` parlent de « six authentication
   features », alors que `ls Src/Application/AppTemplate.Application.Auth/Features` en montre 7.
4. `ours:docs/reference/api.md:247` donne `domain.invariantViolated` à 409 seulement, alors que
   `ours:Src/Presentation/AppTemplate.Api/Common/Errors/GlobalExceptionHandler.cs:27-31` le renvoie en 400.
5. ADR 0009, ligne 28 : « no code path holds another user's row ». Pourtant
   `ours:Src/Infrastructure/AppTemplate.Infrastructure.Persistence/Features/TodoLists/Access/Repositories/TodoListRepository.cs:24-29`
   charge par `list.Id == id` seul. C'est le constat D4 de la carte p1 ; il touche d'abord un autre groupe, mais c'est aussi un écart
   entre une ADR et le code.
6. Le README généré n'a pas de branche sans API (voir 1.2).
7. `ours:docs/template/roadmap.md:38-41` range encore « WPF » dans « Coming ». Or `--presentation wpf` est livré (`ours:README.md:70`),
   et la page dit elle-même « a shipped item leaves this page » (ligne 4).
8. Les issues ouvertes #120 et #121 listent WPF, le serveur MCP et le worker, tous livrés :
   - https://github.com/Pierre-MarieMarchio/Template-DotNet_Clean_Architecture/issues/120
   - https://github.com/Pierre-MarieMarchio/Template-DotNet_Clean_Architecture/issues/121
9. Le guide des réglages est ambigu (voir 1.3).

**Verdict : on dépasse. Confiance haute.** Nous testons une partie de la doc, lui rien. Mais l'étalon place la doc au troisième rang des
sources de vérité, et chacun de ces 9 écarts est un défaut à corriger.

### 1.5 ADR

**Concurrent.** Il a 3 ADR et un gabarit (Context, Decision, Rationale, Consequences), soit 1 560 mots en tout
(`comp:docs/decisions/README.md:7-11`). Aucune ne nomme un test. ADR-003 contredit le code (voir 1.4).

**Nous.** Nous avons 28 ADR de projet (17 093 mots) et 20 ADR du template (12 925 mots).
- Les critères d'écriture sont explicites : « a competent team could reasonably have decided otherwise » (`ours:docs/decisions/README.md:10-15`).
- La règle « the ADR names the test » est respectée par 25 ADR sur 28 : `grep -E '[A-Z][A-Za-z]+Tests\b'`. Les exceptions sont 0019,
  0021 et 0022.
- Chaque ADR a une section « Revisit when » (par exemple `ours:docs/decisions/0009-ownership-is-in-the-query.md:46`).
- Le projet numérote ses propres ADR à partir de 1000 (`ours:docs/decisions/README.md:22-25`).

**Verdict : on dépasse. Confiance haute.**

### 1.6 Ce qu'on gagnerait à lui prendre

1. **Un README de dépôt aussi complet que le sien sur les options.** Ajouter la ligne `--database` et compléter la liste des fronts. Le
   moyen sûr : étendre `DocumentationCitationTests` pour qu'il compare la table d'options du README aux symboles `parameter` de
   `template.json`. C'est le même principe que `ConfigurationSurfaceTests`, sans ADR à écrire.
2. **Une page NuGet qui se suffit à elle-même.** Notre `PackageReadmeFile` est le README du dépôt
   (`ours:.template.config/TemplatePackage.csproj:23`). Il doit donner, sans clic, l'install, la création, le lancement et l'adresse de
   l'API. Il le fait déjà en partie (`ours:README.md:28-43`) ; il faut corriger le point 1 et documenter le `--` de `dnx`.
3. **Une réponse écrite à « comment suivre le template ».** Il y a répondu dans une discussion, par une recette « régénérer puis
   comparer » (https://github.com/jasontaylordev/CleanArchitecture/discussions/1475). Nous avons de quoi faire mieux :
   - la provenance et les options sont inscrites dans `overview.md:100-108` ;
   - le CHANGELOG définit déjà « breaking » comme « adopting the change in an existing project ... requires manual work »
     (`ours:CHANGELOG.md:15-18`).

   La forme : une section « Moving a project to a later version » dans `docs/template/using-the-template.md`. Elle ne va pas dans `docs/`,
   car c'est un sujet du template (template ADR 0002). Elle recommanderait de régénérer avec les options inscrites et de comparer. Pas
   d'ADR à écrire, tant que l'outil `dotnet cleanarch` (template ADR 0017-0019, Proposed) n'existe pas.
4. **Une page de dépannage du premier lancement.** Les personas de chez nous ont buté sur trois points :
   - `/` renvoie 401 et rien n'indique `/health/ready` sur la variante par défaut (`out/p2-ours-1.md`, ligne 72) ;
   - le dossier créé par `dotnet new` n'est pas un dépôt git, alors que la doc demande de commiter (`out/p2-ours-1.md`, ligne 64) ;
   - un bootstrap qui échoue sous le lanceur ne montre pas sa cause.

   Son site n'en a pas, mais son absence lui coûte (personas p2-comp). Nous gagnerions à l'avoir : une section de
   `first-run.md`, ou une page `guides/` si elle grossit.

### 1.7 Ce qu'il faut refuser

- **La doc de référence hors du projet généré, sur un site.** Template ADR 0002 l'exclut. La preuve du coût vient du concurrent lui-même :
  - son site décrit main, pas la version installée ;
  - v10.8.0 n'embarque aucune page ;
  - un lien d'ADR vers le wiki est mort (#1554).

  Un site qui ne ferait que rendre `docs/template/` serait acceptable, mais il n'est pas nécessaire : GitHub rend déjà le Markdown.
- **Les ADR dans un wiki.** Elles n'ont pas de revue, pas de lien testable, et le lien de #1554 en est mort.
- **Le gabarit d'issue générique** (« Go to '...' 2. Click on '....' », `comp:.github/ISSUE_TEMPLATE/bug_report.md`). Nos formulaires
  demandent la version, la commande de génération, `dotnet --info` et la jambe d'acceptation la plus proche
  (`ours:.github/ISSUE_TEMPLATE/bug_report.yml:8-65`). C'est ce qu'il faut pour reproduire un bug de template à combinaisons.
- **La liste de bibliothèques comme section du README** (`comp:README.md:79-89`). Elle se périme déjà : Angular 21 contre 22.

### 1.8 Ce qu'on peut faire qu'il ne fait pas

- Répondre aux grandes discussions de doctrine qui restent ouvertes chez lui. Nous le faisons par ADR, sans fil de 27 commentaires :
  - https://github.com/jasontaylordev/CleanArchitecture/discussions/482 (« not exactly The Clean Architecture style », 27 commentaires) ;
  - #405 « Repository in Asp Core Clean Architecture » (16) ;
  - #1009 « Why MediatR? » (8).

  Nos réponses : ADR 0003 (pas de médiateur, ports en quatre mots) et la table « What is deliberately absent ». À rendre visible dans le
  README du dépôt, par une ligne « Why not MediatR, a generic repository, AutoMapper » qui renvoie vers cette table.
- La discussion https://github.com/jasontaylordev/CleanArchitecture/discussions/1346 (MediatR et AutoMapper devenus commerciaux) réclame
  des alternatives. Un commentaire du 2026-03-24 demande quoi faire pour des clients qui ne paieront pas. Notre template n'a aucune
  dépendance commerciale de ce type (ADR 0003, `ours:docs/architecture/README.md:85,90`). C'est un argument de doc à écrire dans le
  README, sans viser le concurrent.
- Il a fermé sans réponse la demande d'un `AGENTS.md` (https://github.com/jasontaylordev/CleanArchitecture/issues/1632, fermée par son
  auteur, 0 commentaire). Nous le livrons, conditionné par `--agent-files`.

---

## Axe 2. Communauté et entretien

### 2.1 Popularité et adoption

**Concurrent.**
- Sur GitHub : 20 692 étoiles, 4 295 forks, 371 watchers, créé le 2019-10-07 (`gh api repos/jasontaylordev/CleanArchitecture`). Le dépôt
  a une description, 9 topics et une homepage.
- Sur NuGet : `Clean.Architecture.Solution.Template` compte 275 469 téléchargements, en version 10.8.0. Source :
  `curl 'https://azuresearch-usnc.nuget.org/query?q=packageid:Clean.Architecture.Solution.Template'`, le 2026-10-30. L'inventaire p1
  n'avait pas obtenu ce chiffre.

**Nous.**
- Sur GitHub : 1 étoile, 0 fork, 0 watcher, créé le 2025-02-17. `description` vaut `null`, `topics` vaut `[]` et `homepage` vaut `null`
  (`gh api repos/Pierre-MarieMarchio/Template-DotNet_Clean_Architecture`).
- Sur NuGet : `CleanArch.WebApi.Templates` 1.0.0 compte 89 téléchargements, `CleanArch.Create` 1.0.0 en compte 96 (même API).
- Les métadonnées des paquets, elles, sont soignées (`ours:.template.config/TemplatePackage.csproj:20-26`).

**Verdict : en retard. Confiance haute.** Ce retard tient surtout à l'âge : 7 ans contre 8 mois, et une première release le 2026-10-22.
Une partie reste toutefois de notre ressort : le dépôt n'a ni description ni topics, alors que la recherche GitHub et les listes
« awesome » s'en servent.

### 2.2 Releases et cadence

**Concurrent.**
- 22 tags, dont 17 releases en 24 mois. 14 d'entre elles sont tombées en mars 2026 ; la dernière est v10.8.0, le 2026-03-23
  (`out/p1-inventory.md`, section 3).
- Les notes de version sont la liste générée des PR (« ## What's Changed * chore(deps): ... », `gh api .../releases`). Il n'y a pas de
  CHANGELOG.
- Chaque release a son annonce en discussion, de #1508 (v10.1.0) à #1593 (v10.8.0) :
  https://github.com/jasontaylordev/CleanArchitecture/discussions/categories/announcements.
- Il garde une branche par version de .NET (`comp:README.md:93-102`).
- Point faible : 58 commits sur main ne sont pas publiés depuis 7 mois (carte p1). Pendant ce temps, l'issue
  https://github.com/jasontaylordev/CleanArchitecture/issues/1675 (10.8.0 ne compile plus, à cause de NU1902/NU1903) est ouverte
  depuis le 2026-10-28, sans réponse.

**Nous.**
- Une seule release, v1.0.0 du 2026-10-22, avec ses deux `.nupkg` attachés.
- Le CHANGELOG est géré par release-please et donne une définition de « breaking » adaptée à un template (`ours:CHANGELOG.md:1-18`).
- Une branche `net<major>.0` est prévue par version de .NET (`ours:docs/template/maintaining-the-template.md:535`), comme chez lui.

Il y a deux défauts :
- **Les notes GitHub de la 1.0.0 font 261 caractères.** Elles contiennent une seule ligne, « Documentation : ask a SQLite project to
  move to a server database » (`gh api .../releases --jq '.[0].body'`). Le contenu réel est dans une autre section du CHANGELOG
  (`ours:CHANGELOG.md:26` et suivantes). Template ADR 0006 exige pourtant que « its notes must say what changed in a generated project »
  (`ours:docs/template/decisions/0006-develop-integrates-main-holds-the-releases.md:7-9`).
- **La chaîne de release est cassée.** Les 6 dernières exécutions de `template-release.yml` sur develop ont échoué, le 2026-10-30
  (`gh api .../actions/workflows/template-release.yml/runs`). La PR #91 « chore: release 1.1.0 » est ouverte depuis le 2026-10-22, alors
  que develop porte des changements cassants (#169, #172). La cause, le BOM de `template.json`, est établie par le rapport G3.

**Verdict : en retard. Confiance haute.** Nous gagnons sur le CHANGELOG et sur la définition de « breaking », mais une seule release,
avec des notes vides et une chaîne cassée, ne tient pas face à 17 releases annoncées.

### 2.3 Issues, discussions, réactivité

**Concurrent.**
- **Issues, sur 12 mois** (`gh api repos/.../issues?state=all&sort=created`, filtré sur `created_at >= 2025-10-30`) : 22 issues ouvertes,
  15 fermées.
- **Réactivité en rafale.** En mars, il ferme souvent le jour même : #1555, #1565, #1474, #1521.
- **Ailleurs, la réponse traîne des mois :**
  - #1628, ouverte le 2026-06-09 : première réponse du mainteneur le 2026-10-11, « sorry it took so long to get to this » ;
  - #1392, fermée au bout de 3,5 mois, sans commentaire ;
  - #1594, un bug de port du front Angular, ouverte depuis le 2026-03-26 sans réponse du mainteneur. Un contributeur externe s'est
    proposé le 2026-05-14.
- **Discussions** : 367 au total (`out/p1-inventory.md`), six catégories (Announcements, General, Ideas, Polls, Q&A, Show and tell).
  Deux Q&A récentes sont restées sans réponse (WebFetch de la page Discussions).
- **Accueil** : le formulaire d'issue renvoie les questions vers les Discussions (`comp:.github/ISSUE_TEMPLATE/config.yml:3-5`).

**Nous.**
- **Issues** : 16, toutes ouvertes par le mainteneur ; aucune ne vient de l'extérieur. 10 sont ouvertes, dont 5 « Later » de roadmap
  (`gh api .../issues?state=all`).
- **Discussions** : désactivées (`has_discussions: false`).
- **Accueil** : les formulaires d'issue ferment les issues vierges et envoient les vulnérabilités vers un advisory privé
  (`ours:.github/ISSUE_TEMPLATE/config.yml`). `SECURITY.md` et `CONTRIBUTING.md` existent, le concurrent n'a ni l'un ni l'autre. En
  revanche, nous n'avons pas de `CODE_OF_CONDUCT.md`, lui oui (`comp:CODE_OF_CONDUCT.md`).

**Verdict : en retard. Confiance haute.** L'écart porte sur l'existence même d'une communauté, pas sur sa qualité : la réactivité du
concurrent est inégale et nous n'avons rien à comparer. Nos formulaires, notre `SECURITY.md` et notre `CONTRIBUTING.md` sont meilleurs ;
il nous manque les Discussions et un code de conduite.

### 2.4 Bus factor et entretien

**Concurrent.**
- Sur 12 mois, il y a 83 commits humains. Jason Taylor en signe 72 sous trois identités, soit 87 % ; viennent ensuite sc2107 (7),
  LukasGrafTT (2), gyoce (1) et Henry Crans (1). Commande : `git log --since=2025-10-30 --format='%an' | grep -v -i bot | sort | uniq -c`.
- Depuis le début, il signe 637 commits sur 976.
- Les PR externes fusionnées sur 12 mois sont 7 (recherche `is:pr is:merged -author:jasontaylordev`, hors bots).
- Renovate fait l'entretien : 106 commits.
- Le dépôt affiche un financement par GitHub Sponsors (`comp:.github/FUNDING.yml:3`).

**Nous.**
- 514 commits humains sur 543, tous du même auteur sous deux identités (`git log --format='%an' | sort | uniq -c`).
- 28 commits de Dependabot.
- Un seul contributeur.

**Verdict : à égalité, sur un bus factor de 1. Confiance haute.** Le concurrent a quelques contributions externes, mais 87 % reste une
dépendance à une seule personne. Les deux templates compensent par l'automatisation des dépendances. Nous ajoutons un garde-fou
documentaire que lui n'a pas : `AGENTS.md`, `CONTRIBUTING.md`, des ADR qui nomment leurs tests, et le guide de maintenance du template.
Un repreneur aurait donc moins à deviner chez nous.

### 2.5 Ce qu'on gagnerait à lui prendre

1. **Activer les Discussions**, avec Q&A et Announcements, et pointer les questions vers elles depuis `config.yml`. C'est le bon endroit
   pour les questions de doctrine, à part des issues. La roadmap dit aujourd'hui que la discussion d'un item se tient dans son issue
   (`ours:docs/template/roadmap.md:70`), et il faudra en garder le sens. Pas d'ADR : c'est un réglage du dépôt.
2. **Annoncer chaque release.** La forme : le corps de la GitHub Release égal à l'entrée du CHANGELOG, plus une annonce en discussion.
   Avant cela, il faut réparer release-please, puis vérifier que l'entrée de la 1.1.0 dit ce qui change dans un projet généré, comme
   l'exige template ADR 0006.
3. **Renseigner la description, les topics et la homepage du dépôt.** La homepage peut être la page NuGet. Coût nul, et c'est de la
   découvrabilité.
4. **Ajouter un `CODE_OF_CONDUCT.md`**, au moins au dépôt du template. Faut-il le livrer au projet généré ? C'est un choix de template
   ADR 0002 : à exclure par défaut, comme le README du dépôt.

### 2.6 Ce qu'il faut refuser

- **Publier en rafale, puis laisser main dériver.** Chez lui, 14 releases en un mois ont été suivies de 7 mois sans release, et le
  paquet publié ne compile plus (#1675). Notre modèle (template ADR 0006 : release-please, PR de release, publication sans secret) est
  meilleur, à condition qu'il marche.
- **Des notes de version qui ne sont qu'une liste de PR**, sans CHANGELOG. Une PR Renovate n'apprend rien à l'utilisateur d'un template.
  Notre définition de « notable » (`ours:CHANGELOG.md:10-14`) est la bonne.
- **Lier une ADR depuis un wiki** (#1554, voir 1.7).

### 2.7 Ce qu'on peut faire qu'il ne fait pas (ses issues et discussions ouvertes)

| Sa demande | URL | Ce que nous livrons déjà |
|---|---|---|
| Option « minimal » sans code d'exemple, ouverte depuis 2024-06-13 | https://github.com/jasontaylordev/CleanArchitecture/issues/1192 | `--examples none`, plus `remove-feature` |
| Rendre Todo et Identity optionnels (fermée en doublon de #1192) | https://github.com/jasontaylordev/CleanArchitecture/issues/1613 | `--auth none`, `--examples none` (`ours:README.md:60-61`) |
| Vue | https://github.com/jasontaylordev/CleanArchitecture/issues/1570 | `--web vite-vue` |
| SvelteKit en SPA statique | https://github.com/jasontaylordev/CleanArchitecture/issues/1625 | `--web vite-svelte` ; SvelteKit est sur notre roadmap (#119) |
| Blazor | https://github.com/jasontaylordev/CleanArchitecture/issues/1444 | `--web blazor-wasm`, `--presentation blazor-server` |
| `AGENTS.md` | https://github.com/jasontaylordev/CleanArchitecture/issues/1632 | `AGENTS.md`, sous `--agent-files` |
| Suivre les versions du template | https://github.com/jasontaylordev/CleanArchitecture/discussions/1475 | La provenance et les options inscrites ; la page reste à écrire (1.6, point 3) |
| Jobs planifiés | https://github.com/jasontaylordev/CleanArchitecture/discussions/395 | Jobs sous lease, dans l'API ou dans le worker (ADR 0011, 0025) |
| Modèles d'email | https://github.com/jasontaylordev/CleanArchitecture/discussions/685 | Mail multilingue, SMTP ou Postmark (`ours:README.md:19-20`) |
| Le paquet publié ne compile plus | https://github.com/jasontaylordev/CleanArchitecture/issues/1675 | Rien encore. Le rapport G3 relève le même mécanisme chez nous, d'où l'idée d'un contrôle planifié du paquet publié |
| MySQL | https://github.com/jasontaylordev/CleanArchitecture/issues/1571 | Refusé chez nous aussi (roadmap « Not planned »). À égalité. |

Ces réponses ont un usage de communication : une section « Questions fréquentes » du README, ou des annonces. Le ton reste factuel et le
concurrent n'est jamais nommé.

---

## Synthèse des verdicts

| Point | Verdict | Confiance |
|---|---|---|
| 1.1 Où vit la doc | on dépasse, choix assumé (template ADR 0002) | haute |
| 1.2 Premier contact | à égalité | moyenne |
| 1.3 Profondeur | on dépasse | haute |
| 1.4 Doc tenue par les tests | on dépasse, avec 9 écarts à corriger | haute |
| 1.5 ADR | on dépasse | haute |
| 2.1 Popularité | en retard | haute |
| 2.2 Releases | en retard | haute |
| 2.3 Issues et discussions | en retard | haute |
| 2.4 Bus factor | à égalité (1 et 1) | haute |

## Non vérifié

- La recherche plein texte du site concurrent : WebFetch n'en montre pas, mais le rendu peut la masquer.
- Le taux de réponse des 367 discussions : il faudrait GraphQL, que je n'ai pas utilisé pour rester en GET.
- La courbe des téléchargements NuGet dans le temps : seul le total a été relevé.
- Le README généré d'un projet sans API : la preuve est la condition du gabarit et la jambe de `matrix.json`, pas un projet généré.
- La cause de l'échec de release-please : reprise du rapport G3, non relue ici. Le fait de l'échec est vérifié (6 exécutions sur 6).
- Le rendu de nos pages sur GitHub, mermaid compris.
- Le nombre exact de contributeurs du concurrent : la première page de l'API est plafonnée à 100.
