# p3-ours-2 : persona développeur .NET, template Clean Architecture, angle « suivre la doc du projet généré »

Projet : `Acme.Tracker`, options par défaut (auth local, examples all, PostgreSQL, API), template 1.1.0-dev.
Dossier de travail : `/tmp/claude-1000/-home-pmmarchio-code-Template-DotNet-Clean-Architecture/608f3f2d-3c02-4530-9e46-6f8e7e0e445b/scratchpad/personas/p3-ours-2b/work/Acme.Tracker` (sources gardées, un commit par tâche).
Journal horodaté : `.../personas/p3-ours-2b/log/journal.txt` ; sorties brutes : `.../p3-ours-2b/log/*.log`.
Aucun accès au dépôt du template ; sources : page GitHub (README, API GET), `--help`, messages du terminal, fichiers du projet généré.

## 1. Résumé

Tout le parcours a été exécuté, y compris les 8 tâches, avec un `verify` vert à la fin de chacune (3 771 à 3 900 tests, intégration PostgreSQL Testcontainers comprise). Le seul gate non obtenu est `coverage` : le profileur de couverture plante dans tous les projets de test (`PAL_SEHException`, « Zero tests ran »), alors qu'un projet témoin hors template, avec les mêmes paquets, fonctionne. Cause non établie (WSL2 ou propre au projet).

Ce qui marche très bien : la génération (10 s), `bootstrap` (1 min 53 s, sans erreur), `new-feature` (0,7 s, 52 fichiers), `new-use-case` (1,4 s), `presentation-join` (0,3 s, 24 éditions automatiques), les messages des tests d'architecture (ils nomment l'écart), et le fait que presque tout oubli est attrapé par un test.

Ce qui coûte : le template écrit la forme mais pas le fond. Une feature avec ses vrais champs, c'est environ 36 fichiers touchés à la main (plus de 440 lignes) alors que l'outil en écrit 54. Le gate attrape tout, mais souvent au 2e, 3e ou 4e `verify` (4 minutes chacun) : 6 `verify` rouges sur 16 lancés, tous évitables par un message de guide plus précis ou par un `format-fix`/`build` qui voit la même chose que `verify`. Retirer une feature laisse du périmé que rien ne signale (14 emplacements de doc, 25 lignes de `.http`, le client web généré).

Points les plus importants, par ordre :
1. F1. Les champs du placeholder (`Name`, `Description`) traversent 23 fichiers ; il n'y a pas d'option `--fields`. Chaque ajout de champ ensuite coûte 20 fichiers (T3).
2. F2. `dotnet build` local ne voit pas les erreurs que `verify` voit (IDE1006, IDE0005) : 2 `verify` rouges pour rien.
3. F3. `migration-add <Nom>` cible toujours le contexte métier : tâche AddAccessKeys (contexte Auth) = migration vide créée sans avertissement.
4. F4. `adding-a-setting.md` contient deux phrases fausses ou ambiguës (validateur « beside it », étape « Check » qui exige une base) : 3 `verify` rouges.
5. F5. Retrait d'une feature : pas de `--apply` dès qu'on a touché un fichier ; rapport bruité (268 « possibles correspondances » pour une feature nommée `Project`), et du périmé silencieux.
6. F6. Le câblage de la première app web est manuel (10 fichiers, 11 emplacements) alors que celui d'un host de présentation est automatique ; la 2e app web se réduit à 2 lignes.
7. F7. Pas de guide « domain event ».
8. F8. `coverage` non obtenu ici.

## 2. Tableau final

Mesures : durée mur (de ma première commande de la tâche au `verify` vert, réflexion et rédaction des tests comprises) ; « fichiers créés par l'outil » = fichiers écrits par `new-feature`, `new-use-case`, `dotnet new`, `presentation-scaffold`, `web-scaffold`, `dotnet ef` ; « modifiés à la main » = fichiers existants que j'ai édités + fichiers neufs que j'ai écrits ; « lignes à la main » = lignes ajoutées + supprimées (migrations, `v1.json`, lockfiles exclus), d'après `git diff --numstat` par commit ; « étapes imprimées » = actions que l'outil imprime sans les faire.

| Tâche | Durée | Fichiers créés par l'outil | Modifiés à la main | Lignes à la main | Étapes imprimées | Listes fermées à la main | Compile 1er coup | Gate 1er coup | Tests obtenus |
|---|---|---|---|---|---|---|---|---|---|
| T1 Feature complète `Project` (Name, Budget) | 13 min (dont 2 min 30 de diagnostic `coverage`) | 54 (+2 migration) | 7 de câblage + 22 de réécriture du placeholder + 3 docs + 2 neufs (tests d'intégration) = 36 | 49 + 441 = environ 490 | 11 (6 éditions, schéma de test, migration, ligne README, ligne api.md, page de feature) | 6 (`HostComposition` x5, `ApplicationModuleTests`, `TestDatabase`, api.md erreur) | oui après les 6 éditions imprimées ; oui après la réécriture | `verify` vert au 1er essai ; `coverage` KO (env ?) | 3 771 au total, dont des tests générés + 14 tests domaine/validateurs + 3 intégration bout en bout |
| T2a Commande `ArchiveProject` | 16 min pour T2a+T2b | 5 (+2 migration) | 16 (+1 test d'intégration neuf) | environ 190 | 4 (action contrôleur, usings, paramètre, openapi) | 1 (api.md) | non : 4 erreurs de compilation (méthode groupe CS0411, `Rehydrate` x2 tests, constructeur du test généré) | oui | 3 778 |
| T2b Requête `SearchProjects` | (compris ci-dessus) | 3 | 15 dont 8 neufs | environ 220 | 4 (action, response, usings, openapi) | 1 (api.md) | non : CA1862 ; puis `verify` rouge IDE1006 | non (2 `verify`) | 3 786 |
| T3 Champ `Deadline` + migration | 6 min | 0 (+2 migration) | 20 | 113 | 0 (aucun outil) | 0 | oui | oui | 3 789 |
| T4 Réglage `Projects:MaxPerOwner` | 15 min | 0 | 17 dont 3 neufs | 218 | 0 (le guide a 7 étapes) | 4 (configuration.md, api.md, appsettings, compose + .env.example) | oui | non : 4 `verify` (3 rouges) | 3 800 |
| T5 Domain event + réaction | 10 min | 0 | 10 dont 4 neufs | 192 | 0 | 0 (le consommateur satisfait `DomainEventTests`) | non : constructeur du test généré d'`Archive` (2e fois) | non : 2 `verify` | 3 806 |
| T6 App web (vite-react) + host MCP | 14 min | web : 58 ; MCP : 81 + 24 éditions automatiques + 2 migration Auth | web : 10 ; MCP : 0 | web : 63 ; MCP : 0 | web : 3 post-actions + 11 emplacements ; MCP : 3 | 0 | oui | oui (web hors gate) ; `verify` vert sur MCP au 1er essai après migration Auth | 3 900 |
| T7a Retrait de `Notes` | 11 min (15:26 à 15:37) | 0 (+2 migration) | 14 modifiés, 42 supprimés | 121 | 5 (rapport) | 3 (vocabulaire `LayoutConventionTests`, api.md, TestDatabase) | oui | non : 3 cycles | 3 802 |
| T7b Retrait de `Projects` | 6 min | 0 (+2 migration) | 13 modifiés, 77 supprimés | 127 | 5 (rapport) | 0 | oui | oui (migration et openapi faits avant) | 3 732 |
| T8 Casser Domain → Infrastructure | 1 min | 0 | 2 (annulé) | 3 | 0 | 0 | 1re étape : non (CS0234, voulu) ; 2e : oui | `ModuleDependencyTests` rouge en 6 s | 162 tests d'architecture |

`verify` lancés : 16, dont 6 rouges (T2b x1, T4 x3, T5 x1, T7a x1) plus 1 rouge de `coverage`. Durée d'un `verify` vert : 3 min 58 s à 4 min 25 s.

## 3. Parcours idéal et écart, tâche par tâche

### T1. Feature complète possédée

Réalité : `dotnet run Tools/Tasks.cs new-feature Projects Project --access owner --deletion delete --model aggregate --concurrency etag` : 52 fichiers en 0,67 s plus manifeste et page. Puis 6 éditions imprimées (AppDbContext, ApplicationModule, PersistenceModule, Program.cs, HostComposition, ApplicationModuleTests) ; le build compile après elles. Ensuite, le travail réel : mettre `Name`/`Budget` à la place du placeholder `Name`/`Description` (23 fichiers), règle `Budget >= 0` dans le domaine et dans les validateurs, tests, ligne api.md, ligne README, section de routes dans api.md (que l'outil n'imprime pas), page de feature, migration, openapi, 3 tests d'intégration bout en bout.

Idéal : `new-feature Projects Project --access owner --fields Name:string,Budget:decimal` écrit déjà le bon domaine, record, mapper, DTO, requêtes, réponses et tests (une règle `>= 0` serait `--rule Budget>=0`) ; et le câblage de 6 éditions est fait ou proposé en `--apply-edits` (diff). Le reste à la main : les règles métier, qui sont le fond.

Écart : l'outil écrit la structure et dit les éditions, le travail de remplacement du placeholder est entièrement manuel et sans signal ; la même opération (ajouter un champ) coûte 20 fichiers en T3.

Réponses aux questions :
- L'outil fait ou dit seulement ? Il écrit les fichiers ; il ne fait aucune édition (il l'annonce : « nothing here edited a file it does not own ») et dit tout ce qu'il faut éditer, sauf la section de routes de `docs/reference/api.md` (aucun test ne la lit : un oubli reste silencieux, vérifié).
- Peut-on se tromper sans signal ? Oui sur la section de routes d'api.md ; sur le reste, un test échoue (6 listes fermées).
- Compile au 1er coup ? Oui, après les 6 éditions.
- Gate au 1er coup ? `verify` oui (3 771 tests, 3 min 58 s). Sans la migration ni la ligne api.md, 3 tests échouent avec un message qui nomme le fichier et la règle (voir F9).
- Listes fermées à la main : 6, chacune gardée par un test.
- `--help` suffit-il ? Non sur la forme : `dotnet run Tools/Tasks.cs new-feature --help` affiche l'aide de `dotnet run` (il intercepte `--help`) ; `Tasks.cs new-feature -- --help` répond « '--help' is not an option of this file » ; l'aide réelle est dans `dotnet run Tools/NewFeature.cs -- --help` (F10). Le guide `adding-a-feature.md` suffit en revanche.

### T2. Une commande (Archive), puis une requête (recherche par nom)

Réalité : `new-use-case Projects ArchiveProject --kind command` écrit 5 fichiers et imprime l'action de contrôleur complète, prête à coller (point fort). Mais l'état « archivé » n'existe pas : il faut un champ `IsArchived` dans domaine, record, mapper, config, DTO, réponse (T3 en miniature), le corps du cas d'usage, `Project.Archive()`, un test de domaine par règle, une migration, la ligne api.md, openapi. Le test généré (`ArchiveProjectUseCaseTests`) construit le cas d'usage avec le seul validateur : il ne compile plus dès que le cas d'usage prend une dépendance (cassé 2 fois, T2 et T5, par `new ...(null!, null!, validator)`).

`new-use-case Projects SearchProjects --kind query --input none` : 3 fichiers dont un DTO `SearchProjectsDto(Guid Id)` sans rapport avec la recherche. Pour une requête avec un paramètre (le nom), `--input none` n'écrit ni query ni validateur ni test : j'ai écrit les 4 fichiers d'entrée à la main, la méthode de port, l'implémentation EF, la réponse et l'action. `--input id` n'est pas adapté non plus (c'est l'id de l'agrégat).

Idéal : `--input name:string` écrit la `Query`, le validateur (`NotEmpty`, `MaxLength`) et son test ; le test généré du cas d'usage instancie ses dépendances avec NSubstitute au lieu de planter à chaque ajout de dépendance ; `new-use-case` propose le champ d'état s'il manque.

Observation de correction : le généré `UpdateProject` appelait `project.Update(...)` sans `DomainGuard.Try` ; avec la règle « archivé = immuable », une mise à jour d'un projet archivé répondait 400 générique au lieu de 409 `domain.invariantViolated`. Je l'ai corrigé (le guide le dit : « un invariant que le cas d'usage prévoit » → `DomainGuard.Try`), mais rien ne le signale. De même, la forme générée de `Update` mutait `Name` avant de valider le second champ : un échec de `Budget` laissait le nom modifié en mémoire ; j'ai réécrit `Update` (validation de tout puis affectation) et ajouté le test.

Réponses : l'outil écrit et imprime (action HTTP, usings, paramètre) ; on peut se tromper sans signal sur `DomainGuard.Try` ; compile au 1er coup : non (4 puis 1 erreurs) ; gate : Archive oui, Search non (IDE1006, voir F2) ; listes fermées : api.md ; `--help` : l'aide de `NewUseCase.cs` existe, mais `--input` n'a que `id|none`.

### T3. Un champ de plus, avec sa migration

Réalité : `Deadline` (`DateOnly?`) : 20 fichiers modifiés (domaine, record, mapper x3, DTO, mapping, query, commandes, cas d'usage, requêtes, réponse, contrôleur, 5 tests dont `Rehydrate` à signature changée, intégration, docs) ; `migration-add AddProjectDeadline` (5,6 s, migration correcte : `AddColumn<DateOnly>`), `openapi`, `format-fix`. `verify` vert au 1er essai.

Idéal : un seul endroit déclare les champs d'une entité (la forme `--fields` de T1 réécrite par un `add-field <Features> <Champ>:<Type> [--nullable]`), l'outil édite les 15 emplacements et imprime les 5 qui sont du métier (règle, test, doc).

Écart : tout est manuel. Il n'y a pas de signal si on oublie un emplacement : le compilateur et le test `PendingModelChangesTests` attrapent le record/mapper/migration ; un champ oublié dans la réponse HTTP ne casse rien (le contrat ne bouge que par openapi).

### T4. Un réglage

Réalité : guide `adding-a-setting.md` suivi à la lettre (7 étapes). Le build nomme les 7 sites d'appel de `AddProjects()` (étape 3 : bien vu). Problèmes, tous documentaires :
- Étape 2 : « écrire `<Section>OptionsValidator` à côté ». Faire un second fichier `ProjectsOptionsValidator.cs` casse `FeatureFolderVocabularyTests` (« Configuration/Options/ … whose files are named [Options] »). Il faut le mettre dans le même fichier que `ProjectsOptions`, et `internal` (sinon `LayoutConventionTests` : un seul type public par fichier). Le guide ne dit ni l'un ni l'autre ; `IdempotencyPurge`, cité en exemple, a bien les deux types dans un fichier (illisible comme règle).
- Étape « Check » (`<Section>__<Key>=0 dotnet run Tools/Tasks.cs run` « stops before serving and names the key ») : sans base accessible, le processus meurt avant la validation, sur l'erreur Npgsql `Failed to connect to 127.0.0.1:5432`, parce que `MigrateAndSeedForDevelopmentAsync` s'exécute avant `RunAsync` (`Program.cs:93`) et la validation `ValidateOnStart` à `RunAsync`. Le guide ne dit pas qu'il faut une base. Non vérifié avec une base (je n'ai pas lancé Compose, port fixe).
- L'outil le plus utile serait `new-setting`. Idéal : `new-setting Projects MaxPerOwner:int=100 --range 1..10000` écrit options, validateur, test de bornes, test de démarrage, registration, ligne de reference, clé appsettings, compose, .env.

Réalité du gate : 4 `verify` (3 rouges) : (1) `FeatureFolderVocabularyTests` ; (2) mon test NSubstitute (`Returns` sur `RequireUserId()`) ; (3) using inutile dans mon test (IDE0005) ; (4) `LayoutConventionTests` public type. Les messages sont bons (ils disent le fichier et la règle), mais chacun coûte 3 min 30 à 4 min.

Listes fermées : `ConfigurationSurfaceTests` exige la ligne de configuration.md (point fort), mais je l'ai écrite d'emblée, sans voir le test échouer ; la liste fermée comptée ici est donc celle que le guide nomme (configuration.md, appsettings, compose, .env.example, plus api.md pour le code d'erreur).

### T5. Un domain event et sa réaction

Réalité : pas de guide ni d'outil. La page `docs/architecture/domain-and-results.md` décrit les mécanismes ; j'ai suivi l'exemple `TodoItemCompleted` : événement (record dans `Model/Events`), `Archive(DateTimeOffset)` qui lève l'événement (le temps est un paramètre, règle de la page), consommateur `LogProjectArchivedConsumer` (`internal`, dans `Services/Consumers`), enregistrement dans `AddProjects`. `DomainEventTests` n'exige rien dès qu'un consommateur existe (point fort : la liste `_deliberatelyUnconsumed` n'est touchée que sans consommateur).

Pièges : le test généré d'`Archive` casse à nouveau (nouveau paramètre du constructeur) ; `NSubstitute` ne peut pas proxifier `ILogger<LogProjectArchivedConsumer>` parce que le consommateur est `internal` (`Can not create proxy for type ILogger<...>`) : j'ai écrit un `ILogger` d'enregistrement à la main. Les tests d'intégration existants enregistrent via `RecordedDomainEvents`, spécifique à `TodoItemCompleted` ; j'ai testé par le journal (`Fixture.Logs`) comme `LogTodoItemCompletedConsumer`.

Idéal : un guide `adding-a-domain-event.md` (événement, levée dans l'agrégat, consommateur, enregistrement, ce que le test d'architecture exige, test de domaine et test du consommateur, piège « at most once, consommateur idempotent ») ; ou `new-domain-event <Feature> <Event> --raised-by <Method>`.

### T6. Une app web, puis un autre host

App web : `dotnet new cleanarch-webapp -n Site --framework vite-react --scope acme-tracker --workspace` (1,2 s, commande prise dans `overview.md`). Trois post-actions imprimées ; `web-scaffold Site` : 16 s, correct. Ensuite 11 emplacements à éditer à la main, listés dans `Src/Web/README.md` : `docker-compose.yml` (include), `.env.example`, `.dockerignore`, `dependabot.yml` x2, `codeql.yml`, `docs/README.md`, `docs/decisions/README.md`, ADR 0001, `project-layout.md`, ADR 0016 (paragraphe et ligne). `pnpm lint && pnpm test && pnpm build` verts en 14 s ; `verify` ne regarde pas le web (le web n'est gardé que par `web.yml` en CI), donc rien ne dit si une des 11 éditions est oubliée. Le fichier `docs/decisions/0024-a-server-rendered-web-app-runs-on-its-own-server.md` est écrit pour une app SPA et n'est pas dans l'index des décisions (je n'ai pas su si c'est voulu).

La 2e app (launcher en mode ajout) est mieux : `dnx CleanArch.Create@1.1.0-dev -- web-app -n Admin --framework vite-vue --yes` lit le projet (« Taken from Acme.Tracker: --workspace false, --scope acme-tracker … ») et imprime les 2 seules lignes à ajouter.

Host : `worker` ne figure pas dans les `--framework` de `cleanarch-presentation` (blazor-server, avalonia, mcp, wpf) : un worker ne s'ajoute pas après coup (« a project holds the API or the worker »). J'ai pris le MCP : `dotnet new cleanarch-presentation -n Mcp --framework mcp --project-name Acme.Tracker`, puis `presentation-join Acme.Tracker.Mcp` (24 éditions faites par l'outil, 0,3 s), `presentation-scaffold` (24 s), puis la migration des clés d'accès (voir F3), `openapi`, `format-fix`. `verify` vert (3 900 tests). Le README du projet MCP commence par deux BOM (`efbbbfefbbbf`), alors que la règle dit un seul et que `format-fix` répare ce genre d'écart : `verify` ne l'a pas vu.

Idéal : un `Tasks.cs web-join <App>` complet pour les apps non-Blazor (comme `presentation-join`), ou le launcher qui fait les 11 éditions de la première app ; et `new-host` unique côté `dnx` qui enchaîne `dotnet new`, `join`, `scaffold` et migration.

### T7. Retirer une feature d'exemple, puis la feature ajoutée

`remove-feature Notes` : rapport utile, sans manifeste (« no manifest, so its type is unknown »), qui énumère 7 dossiers, 12 enregistrements, 2 références EF, 15 mentions de migration, 27 usages et conclut « Nothing was changed. --apply would refuse ». Suppression manuelle : 42 fichiers (7 dossiers) + 14 éditions + suppression de 2 tests d'autorisation du MCP (`McpAuthorizationTests` : un `read-only key refused a write` et `a record of another caller answers as absent`, tous deux bâtis sur `InMemoryNotes` ; il n'existe plus d'équivalent, c'est un test de sécurité perdu faute de double sur un autre exemple) + migration `RemoveNotes` (DropTable correct) + `openapi`. Trois cycles : (1) tests d'architecture : `note.notFound` encore dans api.md, et `LayoutConventionTests` refuse le mot de vocabulaire `Access/Stores` désormais sans feature (le guide ne le mentionne pas) ; (2) `PendingModelChangesTests` rouge faute de migration (le guide le dit, c'est « Migrations ») ; (3) vert.

`remove-feature Projects` (type connu par le manifeste) : le rapport distingue 31 fichiers modifiés, 11 ajoutés, 60 possédés, et conclut au refus de `--apply` : exact, car j'avais modifié 31 fichiers. Pour une feature réellement implémentée, `--apply` n'est jamais applicable : il ne sert qu'à annuler un squelette tout juste généré. Le rapport contient 268 « possibles correspondances » pour la feature `Project` : `<Project>`, `Project(` des `.sln` et des `.props`. Rendu inexploitable pour un nom aussi banal. Il n'a pas listé `Projects__MaxPerOwner` (appsettings, compose, .env) comme à retirer, ni les sections de api.md/configuration.md. J'ai tout supprimé à la main (77 fichiers, 13 éditions), migration `RemoveProjects`, `verify` vert au 1er essai.

Périmé silencieux (le gate est vert, rien ne l'a signalé) après les deux retraits : `Acme.Tracker.Api.http` (25 lignes sur `/notes`), `docs/getting-started/overview.md` (2), `docs/architecture/persistence-model.md`, `docs/features/presentation.md` (3 outils MCP qui n'existent plus), `docs/architecture/layers-and-projects.md` (`AddNotes()` dans un extrait), `docs/guides/adding-a-feature.md` (exemple « Notes »), `docs/features/observability.md`, `docs/architecture/http-api.md`, `docs/architecture/feature-shapes.md`, `docs/decisions/0002`, `0009`, `docs/features/web-frontend.md`, `docs/guides/removing-features.md` ; et `Src/Web/packages/api-client/src/generated/schema.ts` garde 6 occurrences de `/api/v1/projects` et `/api/v1/notes` (régénérer demande `pnpm api`, que ni `verify` ni `openapi` ne lancent).

Idéal : `remove-feature --apply` sait retirer aussi une feature modifiée (il a le manifeste et le rapport), les éditions de câblage (les 12 enregistrements, EF, `TestDatabase`), la migration de suppression, et `openapi`/`api-client` ; et le rapport exclut `<Project>`/`Project(` par un nom entouré de bornes de mot dans des fichiers C# et de docs seulement.

### T8. Casser une règle d'architecture

Étape 1 : un `using Acme.Tracker.Infrastructure.Core.Common.Contexts;` dans `Src/Domain/.../Reminders/Model/Entities/Reminder.cs`, sans référence de projet : le compilateur arrête (`error CS0234: The type or namespace name 'Infrastructure' does not exist in the namespace 'Acme.Tracker' (are you missing an assembly reference?)`). Étape 2 : ajout de `<ProjectReference ... Infrastructure.Core ...>` dans `Acme.Tracker.Domain.csproj` (pas de cycle) : `dotnet build` passe (après avoir utilisé le type, sinon CS0414 en erreur). `dotnet test --project Tests/Architecture/Acme.Tracker.Architecture.Tests` (6 s) : un seul test rouge, `ModuleDependencyTests.TheDomainLayer_ReferencesNothingOutsideItself`, message :

```
Shouldly.ShouldAssertException : domainProjects.SelectMany(project => project.References.Where(...)...).Order(...) should be empty but had 1 item and was
["Src/Domain/Acme.Tracker.Domain/Acme.Tracker.Domain.csproj -> Acme.Tracker.Infrastructure.Core"]
Additional Info: The domain is the innermost layer: a project in it may reference another project of the same layer and nothing else at all.
```

Annulé par `git checkout -- Src/Domain` ; 162 tests d'architecture verts. Qui l'arrête : le compilateur (sans référence), puis un test d'architecture (avec) ; `verify` l'arrêterait aussi (il lance ce test). Qualité : le message nomme le fichier et la cible, mais répète l'expression LINQ de l'assertion avant (bruit) et ne dit pas quoi faire (« retirer la référence, ou déplacer le type derrière un port de l'application »).

## 4. Frictions, par ordre d'importance

F1. [important, tâches 1, 2, 3] Les champs du placeholder (`Name`, `Description`) traversent 23 fichiers, sans `--fields` ; chaque champ ajouté ensuite (T2a `IsArchived`, T3 `Deadline`) coûte 15 à 20 fichiers. Preuve : `git diff --numstat` du commit « Project (Name, Budget) » : 29 fichiers, +297 -96 ; commit « Project deadline » : 20 fichiers, +92 -21 (`work/Acme.Tracker`, commits 700b69a et fafc6c8). Le test généré `ArchiveProjectUseCaseTests` ne compile plus dès que le cas d'usage prend des dépendances (casse en T2 et en T5).

F2. [important, tâches 2, 4, 7] `dotnet build` ne voit pas les erreurs de style que `verify` voit : `IDE1006` (const privée sans `_`, `ProjectQueries.cs(132,26)`), `IDE0005` (using inutile dans un test, deux fois). Preuve : `log/t2b-verify.log` ligne 25 (`error IDE1006: Naming rule violation: Missing prefix: '_'`) alors que `dotnet build Acme.Tracker.sln` avait imprimé zéro erreur ; `log/t4-verify2.log` (IDE0005). `format-fix` ne répare pas IDE1006, il répare IDE0005 (je l'ai lancé avant l'édition, pas après). Un développeur perd 4 minutes par aller-retour. Idéal : que `format-fix` (ou `Tasks.cs check`, 20 s) lance exactement `dotnet format --verify-no-changes` et les analyseurs du gate, sans les tests.

F3. [important, tâche 6] `migration-add <Nom>` génère dans le contexte métier (`Persistence`) quel que soit le sujet ; l'item template MCP imprime « run dotnet ef migrations add AddAccessKeys --project …Infrastructure.Auth … ». En lançant `migration-add AddAccessKeys` (nom repris du message), j'ai obtenu une migration vide dans `Persistence` sans aucun message (`Up` vide). `dotnet ef migrations remove` a ensuite échoué (`Failed to connect to 127.0.0.1:5432`) : il faut supprimer les 2 fichiers à la main. Preuve : `Src/Infrastructure/.../Persistence/Migrations/20261030141827_AddAccessKeys.cs`, `Up` vide (supprimé ensuite) ; `dotnet ef migrations remove` : « Failed to connect to 127.0.0.1:5432 ». Idéal : `migration-add <Nom> --context auth|app` (refuser une migration vide : « no model change: nothing was added »).

F4. [important, tâche 4] `adding-a-setting.md` : étape 2 « beside it » fausse selon les tests (validateur dans le même fichier, `internal`) ; étape « Check » exige une base. Preuves : `log/t4-verify.log` (`FeatureFolderVocabularyTests … 'Configuration/Options/', whose files are named [Options]`) et `log/t4-verify4.log` ; `dotnet run Tools/Tasks.cs run` avec `Projects__MaxPerOwner=0` : trace Npgsql, pas d'`OptionsValidationException` (`Program.cs:85-96` : migration avant `RunAsync`).

F5. [important, tâche 7] Retrait : `--apply` inutilisable dès qu'un fichier est édité ; rapport bruité (268 « possibles correspondances » pour `Project`, `.sln` et `<Project>` en tête) ; du périmé silencieux (voir T7 : 14 emplacements de doc, 25 lignes de `.http`, `schema.ts`). Un retrait d'une feature réelle coûte 90 fichiers touchés et 3 cycles de gate pour `Notes` (Notes : `LayoutConventionTests` mot de vocabulaire, api.md, migration).

F6. [moyen, tâche 6] Asymétrie : un host de présentation se joint par un outil qui fait 24 éditions en 0,3 s ; la première app web se joint à la main (11 emplacements, 63 lignes) ; la 2e se réduit à 2 lignes imprimées par le launcher. `verify` ne vérifie pas le web, donc rien ne signale une édition oubliée.

F7. [moyen, tâche 5] Pas de guide ni d'outil pour un domain event ; piège `ILogger<internal>` avec NSubstitute ; l'infrastructure de test d'intégration enregistre un seul événement en dur (`RecordedDomainEvents` ne connaît que `TodoItemCompletedDomainEvent`).

F8. [moyen, tâche 1] `coverage` : `PAL_SEHException` dans tous les projets de test. Preuve : `log/t1-coverage1.log` (« Error output: terminate called after throwing an instance of 'PAL_SEHException' », « Zero tests ran », 16 erreurs en 12,6 s) ; commande exacte `dotnet test ./Acme.Tracker.sln --configuration Debug --results-directory ./TestResults --coverage --coverage-settings ./coverage.runsettings --coverage-output-format cobertura`. Contrôle : un projet xunit.v3 hors template (`ctrl/ctrl.csproj`, mêmes versions : Microsoft.Testing.Extensions.CodeCoverage 18.12.0, xunit.v3 4.0.2, même `InvariantGlobalization`, référence à `Acme.Tracker.Domain`) tourne avec `--coverage` sans erreur. Essais sans effet : `DOTNET_TieredCompilation=0`, `DOTNET_EnableDiagnostics=1`, `DOTNET_ReadyToRun=0`, sans `DOTNET_CLI_HOME` isolé. Le run raté laisse des `*.dll.orig` (178) dans `bin/` ; je les ai restaurés. Cause non établie : environnement (WSL2 6.6.87.2) ou propre au projet. Non classé comme friction du template sans preuve supplémentaire.

F9. [mineur] Messages des tests de gate : bons (fichier, règle, parfois la correction), mais deux défauts. `ErrorCodeTableTests` dit que la ligne manque dans `docs/reference/api.md` sans donner la ligne à ajouter (l'outil l'avait imprimée à la génération) ; `ModuleDependencyTests` répète l'expression LINQ avant le message. Aucun ne répond à « que faire ensuite » sauf `PendingModelChangesTests` (il donne la commande) et `ErrorCodeStatusTests`.

F10. [mineur] `--help` : `dotnet run Tools/Tasks.cs new-feature --help` → aide de `dotnet run` ; `… -- --help` → « '--help' is not an option of this file » ; l'aide complète est dans `dotnet run Tools/NewFeature.cs -- --help` (et `NewUseCase.cs`). `Tasks.cs help` dit « forwarded to Tools/NewFeature.cs, whose --help says what each one means » sans donner la commande qui fonctionne.

F11. [mineur] `dnx CleanArch.Create@… -- new` ne crée pas de dépôt git, alors que `bootstrap` et le README demandent « commit what it writes » ; seul `--setup` le fait (non essayé). Les docs du projet ne mentionnent pas `--setup`, ni `dnx`/`CleanArch.Create`.

F12. [mineur] `dotnet cleanarch` : introuvable (`Could not execute because the specified command or file was not found … dotnet-cleanarch does not exist`). `.config/dotnet-tools.json` du projet généré ne contient que `dotnet-ef` et `dotnet-sonarscanner`. Une recherche NuGet « cleanarch » renvoie `cleanarch.tool 2.0.3` (auteur « CleanArch Contributors »), `cleanarch.generator` : des paquets sans rapport avec le template, risque de confusion si la commande est annoncée. Le dépôt GitHub (develop) contient `Launcher/CleanArch.Cli` ; le paquet que j'ai reçu n'en porte pas la trace.

F13. [mineur] README du projet MCP généré : double BOM (`efbbbfefbbbf`) que ni `format-fix` ni `verify` ne signalent. La règle (docs) : « Markdown and C# files are UTF-8 with BOM … `format-fix` repairs them ».

F14. [mineur] Une feature nommée `Project` : le rapport `remove-feature` est inexploitable (voir F5) ; l'ADR 0024 (rendu serveur) est ajoutée pour une app SPA.

## 5. Ce qui est bon (preuves)

- Génération en 10 s et `bootstrap` en 1 min 53 s sans erreur ; 160 + 337 + 47… tests unitaires verts avant tout changement (`log/baseline-test.log`, 71 s).
- `new-feature` : « The solution does not compile until edit 1 is made, and the feature does not compose until all are », puis la liste numérotée des éditions, avec les lignes exactes à coller ; un seul oubli sur 11 (la section de routes d'api.md) ; tous les autres sont attrapés par un test nommé. Les 6 éditions m'ont donné un build vert du premier coup.
- `new-use-case` imprime l'action de contrôleur complète, utilisable telle quelle (aucune modification pour `Archive`).
- `presentation-join` (24 éditions, atomique, « all or none »), `web-scaffold` (16 s), launcher en mode ajout qui lit le projet.
- Les tests d'architecture disent le fichier fautif et la règle ; `LayoutConventionTests` refuse même un mot de vocabulaire devenu vide (preuve : « 'Access/Stores' is in this layer's vocabulary and no feature is filed under it »).
- `remove-feature` : rapport structuré avec blockers et commandes de vérification ; `--apply` refuse net plutôt que de supprimer à demi.
- La migration `AddColumn<DateOnly>` et `DropTable` générées sont correctes ; `PendingModelChangesTests` dit exactement la commande à lancer.
- Les 6 `verify` rouges ont tous donné un message qui m'a suffi à corriger sans autre recherche (sauf IDE1006, qu'il a fallu comprendre).

## 6. Limites et ce qui n'a pas été vérifié

- Environnement : WSL2, pas de TTY (le launcher exige `--yes` après `--` ; `dnx … --yes -- new` est absorbé par `dnx`, mentionné par le run précédent, non reproduit ici).
- `coverage` non obtenu (F8). `end-to-end` et Compose non lancés (port fixe) ; l'API n'a donc été exercée que par les tests d'intégration (Testcontainers PostgreSQL, 3 771 à 3 900 tests).
- L'étape « Check » du guide des réglages n'a été vue qu'en échec faute de base.
- Je n'ai pas vu un test échouer avant d'écrire chaque test de règle (« a test that buys a guarantee is seen failing once ») : j'ai surtout écrit les tests avec le code, sauf un cas où le test a trouvé un vrai défaut (`Update` qui mutait le nom avant de valider le budget : le test « garde l'ancien état » l'a révélé avant correction, car je l'avais écrit en premier).
- Les durées incluent mon temps de rédaction du code métier (les 3 `verify` rouges de T4 pèsent 11 min sur 15).
- `worker` : non ajouté (absent du template d'ajout). Desktop non essayé.
- Non touché : dépôt du template, dossiers des autres agents, conteneurs Docker.
- Nettoyage fait : `bin`, `obj`, `node_modules`, `TestResults`, `dist` du projet et du témoin supprimés ; `DOTNET_CLI_HOME` isolé supprimé ; sources, commits git et journaux gardés.
