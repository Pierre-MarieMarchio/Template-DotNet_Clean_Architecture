# Persona p3-ours-1 : un développeur .NET qui découvre le template (1.1.0-dev), angle « help d'abord »

Projet généré : `Acme.Shop` (options par défaut : api, auth local, PostgreSQL, examples all, files true).
Chemin du projet : `/tmp/claude-1000/-home-pmmarchio-code-Template-DotNet-Clean-Architecture/608f3f2d-3c02-4530-9e46-6f8e7e0e445b/scratchpad/personas/p3-ours-1/work/Acme.Shop`
(historique git local : un commit par tâche, `git log` y est lisible). Journal horodaté : `.../personas/p3-ours-1/journal.txt`. Logs bruts : `.../personas/p3-ours-1/t*.log`.

Remarques de méthode : lecture seule sur le dépôt du template respectée. Une seule entorse involontaire : un `ls` du dossier `scratchpad/out` en fin de run (noms de fichiers seulement, aucun contenu lu). Je n'ai ouvert la doc du projet généré que dans trois cas de blocage (`docs/guides/adding-a-setting.md`, `docs/guides/adding-a-feature.md` pour les listes fermées, le `README` de l'app web via le message du terminal).

## 0. Limites de l'environnement (pas des frictions du template)

- `/tmp` est un tmpfs de 16 Go partagé avec les autres agents. Il a été plein à 100 % à trois reprises (12:26 pendant `bootstrap`, 12:40 pendant `build`, 12:52 pendant `verify`), puis bloqué pendant environ 35 minutes (tous les agents attendaient). J'ai libéré mes propres `bin/obj` pour sortir de l'impasse. Les durées « mur » de la tâche 1 sont donc faussées ; je donne des durées actives.
- `coverage` : plantage reproductible des hôtes de test, `terminate called after throwing an instance of 'PAL_SEHException'`, `Zero tests ran`, sur 13 des 15 modules, aussi bien via `Tasks.cs coverage -m:1` que via `dotnet test --project ... --coverage` isolé, avec ou sans `DOTNET_EnableWriteXorExecute=0`. Probablement le profileur de couverture sous WSL2 ; non vérifié (limite de l'agent). Le gate `coverage` n'a donc jamais pu être exécuté. Aucune mention de ce plantage dans la doc du projet généré (grep PAL_SEH, WSL : rien).
- `dnx` sans TTY : pas de mode interactif essayé (il demande tout en option ; message clair).
- pnpm a utilisé un store dans `/tmp/.pnpm-store` (le store du home n'est pas lié en dur) : avertissement affiché, sans effet.

## 1. Mise en route (hors tâches numérotées)

| Étape | Durée | Résultat |
|---|---|---|
| `dnx CleanArch.Create@1.1.0-dev --yes -- --help` | 1,4 s | Aide lisible : commandes `new`, `web-app`, `web-package`, `presentation`, options. |
| Génération `dnx CleanArch.Create@1.1.0-dev --yes -- -n Acme.Shop --yes` | 10 s | OK. Premier essai `dnx ... --yes -n Acme.Shop -y` échoue : `There's no terminal to ask questions in, and these weren't given: --presentation, --web, ...`. dnx consomme `--yes`/`-y` lui-même ; il faut `--yes` avant `--` pour dnx ET `--yes` après pour le lanceur. L'aide du lanceur annonce `-y, --yes` sans le piège dnx. |
| `git init` + commit | manuel | Le projet généré n'est pas un dépôt git ; le message final dit « commit what it writes » sans dire d'initialiser git (`--setup` le fait, mais n'est pas évoqué dans le message de fin). |
| `dotnet run Tools/Tasks.cs help` | 5 s | Complet et bien rangé (tâches, options, exemples). |
| `bootstrap` | 2 min 30 | OK (formatage, openapi, tool restore, migrations initiales Auth + Persistence). |
| `dotnet cleanarch` | 0 s | `Could not execute because the specified command or file was not found.` (message générique du SDK, aucune indication). La commande n'existe pas dans 1.1.0-dev ; l'outillage disponible est `Tasks.cs`, le lanceur dnx, les item templates. |

Mesures générales : build incrémental 11 s ; `test --no-integration` 1 min 12 s (432+175+686+162+...) ; `verify` 3 min 55 à 5 min 11 selon le contenu (3 733 à 3 789 tests, Docker/Testcontainers fonctionnel) ; test d'intégration ciblé `--filter-class` 9 à 14 s.

## 2. Tableau final obligatoire

Durées = temps actif d'agent (sans les attentes disque), `verify` inclus quand il a été lancé. `verify` : lancé après T1, T2, T5 (groupé avec T3-T4), T6, T7a, T7b ; `coverage` jamais possible (voir §0).

| Tâche | Durée | Fichiers créés par l'outil | Fichiers modifiés à la main | Lignes à la main | Étapes seulement imprimées | Listes fermées complétées à la main | Compile du 1er coup | Gate du 1er coup | Tests obtenus |
|---|---|---|---|---|---|---|---|---|---|
| 1. Feature Project (Name, Budget, CRUD, Budget >= 0, e2e) | ~20 min actif (+ 4 min verify) | 54 (52 + manifest + page doc) + 2 migration (`migration-add`) | 9 existants + 22 fichiers générés réécrits (Description devient Budget) + 1 page doc | ~215 (47 d'enregistrements, ~60 de réécriture du champ, ~40 de tests, 76 de test d'intégration, ~33 de doc) | 11 (6 modifications, `migration-add`, ligne `docs/README.md`, ligne `api.md`, texte de la page, + `openapi` non imprimé du tout) | 6 (`AppDbContext`, `ApplicationModule`, `PersistenceModule`, `HostComposition`, `ApplicationModuleTests`, `TestDatabase`) + code d'erreur `api.md` + ligne `docs/README.md` | Oui, après les 6 modifications (par construction, l'outil le dit) | Non : `PendingModelChangesTests` rouge jusqu'à `migration-add` ; `format` rouge (IDE1006 sur ma constante) ; ensuite `verify` vert (3 768) | ~23 tests unitaires générés (validateurs, domaine, mapper, config EF) ; 0 d'intégration (« not generated, because it is a decision ») ; 3 écrits par moi |
| 2. Use cases Archive (commande) puis recherche par nom (requête) | ~9 min actif (+ 4 min verify) | 5 (Archive) + 3 (Search) | ~15 (domaine, record, mapper x3, config, DTO, mapping, queries, port, controller, réponses) + 6 fichiers écrits à la main | ~350 | 6 (action de contrôleur copiée-collée x2, `openapi`, ligne de route en doc, code d'erreur, corps du use case) | 2 (`openapi` ; ligne de route dans la page) | Non (3 tours : CA1862, test généré qui ne construit plus le use case, xUnit1051 + IDE0005) | Non au 1er essai ; vert après 3 corrections et migration | 2 générés (Archive) ; 0 pour Search (`--input none`), 8 écrits par moi + 2 d'intégration |
| 3. Champ `Deadline` + migration | ~4 min actif | 0 (aucun outil ne l'offre) ; 2 fichiers de migration par `migration-add` | ~14 | ~45 | 0 (la migration est une commande) | 2 (migration, `openapi`) | Oui | Non : `PendingModelChangesTests` rouge jusqu'à `migration-add` (message clair) | 0 généré ; 3 modifs de tests |
| 4. Réglage `Projects:SearchResultLimit` | ~8 min actif | 0 | ~13 (+ 3 fichiers créés à la main) | ~120 | 0 (aucun outil) | 4 (`HostComposition` x5, `ApplicationModuleTests`, ligne `configuration.md`, [non contrôlé] `appsettings`/compose/`.env`) | Oui | Non : 2 tests d'architecture rouges (voir F13) | 0 généré ; 4 écrits |
| 5. Domain event + réaction | ~8 min actif | 0 | ~8 (+ 3 fichiers créés) | ~110 | 0 | 1 (`DomainEventTests` satisfait par la présence d'un consommateur, rien à lister) | Oui | Oui (unitaires + intégration + `verify` 3 789) | 0 généré ; 3 écrits (domaine, use case, intégration par les journaux) |
| 6a. App web (vite-react) | ~9 min actif | 58 (dont 38 sous `Src/Web`) + `web-scaffold` 22 s | 10 | ~80 | 10 (compose include, `.env.example`, `.dockerignore`, dependabot x2, codeql, 5 lignes de doc) | 10 (dont 7 non contrôlées par un test) | Oui (rien de .NET touché) | Oui pour `verify`/archi ; `pnpm lint/test/build` verts séparément (5,8 s / 2,4 s / 4,3 s), `verify` ne les lance pas | 0 (`No test files found`) |
| 6b. Autre host : MCP (`presentation`) | 30 s (outil) + ~6 min (migration Auth, openapi, client web, verify) | 83 ajoutés + 31 fichiers existants modifiés par l'outil | 0 (j'ai ajouté migration + openapi + `pnpm api`) | 0 | 1 imprimée en haut de sortie (migration Auth via `dotnet ef`), `openapi` et `pnpm api` non imprimés | 3 faites par l'outil (`LayoutConventionTests`, `FeatureFolderVocabularyTests`, `PortConventionTests`, `OutboundHttpTests`) ; 2 oubliées par l'outil (migration, openapi) | Oui | Non : `PendingModelChangesTests` (Auth) puis `OpenApiDocumentTests` rouges | tests MCP fournis (hôte, autorisation, outils) |
| 7a. Retirer l'exemple Notes | ~25 min actif (+ 5 min verify) | 0 (`remove-feature` ne supprime rien : rapport seul, pas de manifeste) | ~20 (7 dossiers supprimés, 12 enregistrements, 2 refs EF, 5 tests/MCP, 6 pages de doc, `.http`) | ~60 supprimées/modifiées | tout : le rapport liste 7 dossiers, 12 enregistrements, 2 refs EF, 27 usages, 15 mentions de migration | 3 (`ErrorCodeTableTests` ligne `note.notFound`, vocabulaire `Access/Stores` de `LayoutConventionTests`, catalogue d'outils MCP) | Non (4 erreurs : tests MCP dépendants de Notes) | Non (2 tests d'archi rouges avant corrections) ; verify vert ensuite (3 786) | -1 suite (tests de Notes supprimés) |
| 7b. Retirer Project (modifié) puis un squelette intact (Widgets) | ~15 min actif (+ 5 min verify) | 0 ; `--apply` sur squelette intact Widgets : 29 fichiers supprimés en 1 commande | ~25 | ~40 | rapport seul pour Project : 5 bloqueurs (30 fichiers édités, 8 ajoutés, usages, EF, enregistrements, migrations) | 5 | Oui | Oui (3 733, 0 échec) | n/a |
| 8. Casser une règle d'architecture | ~2 min | 0 | 1 csproj (annulé) | 1 | 0 | 0 | Oui (le build passe avec la référence interdite !) | Rouge attendu en 9 s : `ModuleDependencyTests.TheDomainLayer_ReferencesNothingOutsideItself` | n/a |

## 3. Résultats par tâche : l'outil fait-il, peut-on se tromper, parcours idéal et écart

### Tâche 1 : feature complète
- **Outil** : `dotnet run Tools/Tasks.cs new-feature Projects Project --access owner --deletion delete --model aggregate --concurrency etag` : 0,7 s, 52 fichiers + manifeste + page de doc sur 5 couches. Il imprime ensuite six modifications à faire à la main, « each somewhere an absence should be visible », la migration, deux lignes de doc, et dit « The solution does not compile until edit 1 is made ». Rien de cela n'est fait. `--help` : `Tasks.cs new-feature --help` imprime l'aide de `dotnet run` (dotnet mange `--help`) ; il faut deviner `dotnet run Tools/NewFeature.cs -- --help`. L'aide de `Tasks.cs help` dit « whose --help says what each one means » sans dire comment l'obtenir. Le message pour `Tasks.cs -- new-feature --help` est correct et utile (`'--help' is not an option of this file...`).
- **Choix** : l'outil ne dit pas lequel des deux modèles prendre ; l'aide dit seulement « aggregate: one use case per operation ». J'ai pris aggregate (règle de validation, archive à venir). Je n'ai pas eu à ouvrir la doc.
- **Se tromper sans signal** : oublier un enregistrement donne un test rouge (`HostComposition` / `ApplicationModuleTests`) ou un conteneur non résolu ; oublier la migration donne un test rouge dont le message dit quoi faire (`run 'dotnet run Tools/Tasks.cs migration-add <Name>'`). Oublier `openapi` donne un rouge (`OpenApiDocumentTests`). Mais l'outil n'imprime pas `openapi` parmi les étapes. Sans test d'intégration généré, un champ mal mappé n'est détecté que si on écrit son propre test (le test de fidélité du mapper généré ne couvre que les champs qu'on y a mis).
- **Écart** : les champs `Name`/`Description` sont des substituts dans 22 fichiers ; passer à `Name`/`Budget` a coûté ~60 lignes de réécriture à la main. Le test e2e (76 lignes) est entièrement à écrire.
- **Parcours idéal** : `new-feature Projects Project --access owner --fields Name:string,Budget:decimal>=0 ...` qui écrit aussi les 6 enregistrements (ou qui les fait par `--apply`, comme `presentation-join`), lance `migration-add` et `openapi`, et génère un test d'intégration CRUD de base. Reste à la main : la règle métier et la doc.

### Tâche 2 : use cases Archive puis recherche
- **Outil** : `new-use-case Projects ArchiveProject --kind command` écrit 5 fichiers + test, enregistre dans le manifeste, dit « Nothing to register » (vrai : enregistrement par dossier) et imprime l'action de contrôleur complète, prête à coller, avec `openapi` en suivant. Excellent pour Archive. Le test généré construit le use case avec le seul validateur, il ne compile plus dès qu'on ajoute les dépendances (normal, mais le test était le seul point d'ancrage).
- `new-use-case Projects SearchProjects --kind query --input none` : 3 fichiers dont une interface sans paramètre (`ExecuteAsync(ct)`) et un DTO `SearchProjectsDto(Guid Id)`. Ni `--input id` ni `--input none` ne convient à une recherche par nom (un paramètre de texte) : j'ai réécrit la requête, le validateur, l'interface et le use case (4 fichiers) et le contrôleur n'est pas celui imprimé (`[HttpGet("search-projects")]` sans paramètre).
- **Se tromper sans signal** : les analyseurs attrapent beaucoup (CA1862 sur `Contains` + `ToLower`, IDE0005, xUnit1051) ; `build` les remonte en erreurs. Aucun test ne vérifie que la route imprimée a bien été collée (on ne s'en rend compte qu'en l'appelant).
- **Parcours idéal** : `--input none|id|fields:<Name:string>` ; générer le test du corps avec des doublures prêtes (`IProjectService` + `IUnitOfWork`).

### Tâche 3 : ajouter un champ + migration
- Aucun outil, et `help` n'en annonce aucun : j'ai dû comprendre que le champ traverse domaine, record EF, mapper (3 endroits), configuration, DTO, mapping DTO, projection de requête, requête/commande, réponse, contrôleur, tests (~14 fichiers). Compile du 1er coup, puis `PendingModelChangesTests` rouge : message exact. `migration-add AddProjectDeadline` (5 s) a écrit la bonne migration (`AddColumn<DateOnly>`, nullable).
- **Parcours idéal** : une checklist imprimée ou `new-field` ; à défaut une page « ajouter un champ » indexée depuis `help`.

### Tâche 4 : un réglage
- Aucun outil ; `help` n'en parle pas. J'ai ouvert `docs/guides/adding-a-setting.md` (7 étapes claires). Deux tests d'architecture rouges au premier essai en suivant la doc à la lettre : l'étape 2 dit « Write `<Section>OptionsValidator` beside it », mais `FeatureFolderVocabularyTests` refuse ce fichier dans `Configuration/Options/` (« whose files are named [Options] ») puis `LayoutConventionTests` refuse deux types publics dans un fichier ; la solution est un validateur `internal` dans le même fichier que les options (comme `IdempotencyPurgeOptions.cs`). Défaut de doc.
- **Contrôles** : `ConfigurationSurfaceTests` échoue bien quand la ligne de `configuration.md` manque (testé : `Projects:SearchResultLimit — bound by ...`, message clair). En revanche, supprimer la clé de `appsettings.json`, `docker-compose.yml` et `.env.example` ne fait échouer aucun test (étape 6 non contrôlée).
- **Parcours idéal** : `new-setting <Feature> <Section> <Key>:int[1..200]=50` qui écrit options, validateur, enregistrement (avec le passage de `IConfiguration` aux 8 sites d'appel), ligne de doc, appsettings, compose, `.env`.

### Tâche 5 : domain event + réaction
- Aucun outil ; la ligne « A domain event | a consumer, or its name and reason in DomainEventTests._deliberatelyUnconsumed » est en `docs/guides/adding-a-feature.md` et dans la sortie de `new-feature`. J'ai copié le modèle de `TodoItemCompleted` (event record, consommateur dans `Services/Consumers/<Event>/`, `AddDomainEventConsumer`). Test d'intégration par `Fixture.Logs` (rampe fournie par le projet) : OK. Compile et gate du 1er coup. Rien de silencieux : un événement sans consommateur ferait échouer `DomainEventTests` (non testé volontairement, la doc le dit et le message du test est explicite).
- **Parcours idéal** : `new-event <Features> <Event> --consumer log` (écrit l'événement, le consommateur, l'enregistrement, le test de domaine).

### Tâche 6 : app web puis autre host
- **App web** : `dnx CleanArch.Create@1.1.0-dev --yes -- web-app -n Portal --framework vite-react` : 2,6 s. Bonne surprise : il déduit `--workspace true --scope acme-shop --project-name --auth local` du projet (« Taken from Acme.Shop »). Mais il n'applique aucune modification des fichiers hors `Src/Web` ; il imprime dix modifications (compose `include`, `.env.example`, `.dockerignore`, dependabot x2, codeql, `docs/README.md`, README des ADR, ADR 0001, `project-layout.md`, ADR 0016). `web-join Portal` répond `Src/Web/apps/Portal/README.md has no '## Joining a project generated without one' section, so there is nothing to join.` : `web-join` ne sert pas pour la première app (elle est décrite dans `Src/Web/README.md`). Sept des dix modifications ne sont contrôlées par aucun test (dependabot, codeql, 5 lignes de doc : tests d'archi verts sans elles). `web-scaffold Portal` : 22 s, OK. `pnpm lint/test/build` : verts. L'ADR 0024 est créé sans ligne dans l'index des ADR, et le message n'en parle pas. `verify` ne lance pas le web (pas de lint/test/build/schéma). L'aide `dnx ... -- web-app --help` réaffiche l'aide globale sans les options du web-app (il faut `dotnet new cleanarch-webapp --help`).
- **Autre host** : `worker` n'est pas proposé par `presentation` (seulement à la génération) ; j'ai pris MCP : `dnx ... -- presentation -n Mcp --framework mcp` : 30 s, fait lui-même join + scaffold + ajout à la solution + format (83 fichiers créés, 31 modifiés, y compris 4 listes fermées de tests d'architecture et la doc). C'est le bon modèle. Reste : la migration du contexte Auth (imprimée en HAUT de la sortie comme « Manual instructions », absente du « Next: » final) ; `Tasks.cs migration-add AddAccessKeys` a créé silencieusement une migration VIDE dans le contexte métier (`Persistence`), sans avertir qu'il n'y avait aucun changement de modèle ; il n'y a aucune tâche `Tasks.cs` pour le contexte Auth, il faut `dotnet ef migrations add AddAccessKeys --project Src/Infrastructure/Acme.Shop.Infrastructure.Auth ...` tapé à la main. Puis `OpenApiDocumentTests` rouge (le join ajoute des routes) : message correct mais non anticipé par l'outil ; `pnpm api` à lancer pour le client web (aucun gate ne le vérifie).
- **Parcours idéal** : même comportement pour `web-app` que pour `presentation` (join automatique des 10 éditions), `migration-add --context auth`, et les étapes `openapi` + `pnpm api` lancées par le join.

### Tâche 7 : retirer des features
- `remove-feature Notes` (exemple, sans manifeste) : rapport seul (`Nothing was changed. --apply would refuse`) ; la sortie liste 7 dossiers, 12 enregistrements, 2 références EF, 15 mentions de migration, 27 usages et 109 « possibles » (bruit : chaque cellule `Notes` des tableaux de `configuration.md`). L'exemple était déjà consommé par le MCP (outils `*_note`) que j'avais ajouté à la tâche 6 : le MCP a dû être touché aussi. Suppression à la main : 4 erreurs de compilation (tests MCP), 2 tests d'archi rouges (ligne `note.notFound` de `api.md`, mot de vocabulaire `Access/Stores` devenu orphelin dans `LayoutConventionTests`), migration `RemoveNotes` à générer, `openapi`, `pnpm api`, `.http` (sections 46 à 52) et 7 pages de doc dont il reste des mentions en prose (`overview.md`, `feature-shapes.md`, ADR 0002, 0009, `removing-features.md`...) qu'aucun test ne détecte. `verify` vert ensuite.
- `remove-feature Projects` (ma feature, 30 fichiers modifiés + 8 ajoutés) : rapport seul, 5 bloqueurs ; suppression à la main avec le même schéma.
- `remove-feature Widgets --apply` (squelette neuf, jamais enregistré) : 29 fichiers supprimés en un coup, c'est le seul cas où l'outil agit. (Note : la liste « possible matches » inclut `Tools/RemoveFeature.cs` lui-même.)
- **Parcours idéal** : `remove-feature X --apply --force-edited` qui exécute le plan (dossiers + enregistrements + migration de suppression + openapi) en laissant un rapport des mentions en prose.

### Tâche 8 : casser une règle d'architecture
- `dotnet add Src/Domain/.../Acme.Shop.Domain.csproj reference Src/Infrastructure/.../Acme.Shop.Infrastructure.Email.csproj` : `MSB4006 There is a circular dependency` : c'est MSBuild, pas le template (0,8 s). Avec `Infrastructure.Core` (pas de cycle) : `dotnet build` réussit (2 s) ; rien ne s'oppose à la compilation. `dotnet test --project Tests/Architecture/...` : 9 s, 1 échec `ModuleDependencyTests.TheDomainLayer_ReferencesNothingOutsideItself`, message : `["Src/Domain/Acme.Shop.Domain/Acme.Shop.Domain.csproj -> Acme.Shop.Infrastructure.Core"]` + « The domain is the innermost layer: a project in it may reference another project of the same layer and nothing else at all. ». Chemin, règle, raison : bon. Annulé par `git checkout` ; 162/162 verts.

## 4. Liste des frictions (ce qu'il faut savoir sans qu'aucun outil ne le dise)

F1. `dnx` consomme `--yes`/`-y` : écrire `dnx CleanArch.Create@1.1.0-dev --yes -- -n X --yes`. L'aide du lanceur ne le dit pas.
F2. Le projet généré n'est pas un dépôt git, et le message final demande de commiter ; `--setup` fait tout mais n'est pas mentionné à la fin.
F3. `dotnet run Tools/Tasks.cs new-feature --help` affiche l'aide de `dotnet run` ; la vraie aide est `dotnet run Tools/NewFeature.cs -- --help`, que `Tasks.cs help` ne cite pas.
F4. `dotnet cleanarch` n'existe pas ; message générique du SDK sans piste.
F5. `new-feature` n'applique pas ses 6 modifications (par choix : « nothing here edited a file it does not own »), n'imprime pas `openapi`, n'écrit aucun champ métier et aucun test d'intégration.
F6. Les champs placeholders `Name`/`Description` sont répartis dans 22 fichiers.
F7. `new-use-case --input id|none` ne couvre pas une requête paramétrée par du texte ; l'interface générée n'a pas de paramètre.
F8. Le test généré d'un use case de commande ne se recompile plus dès qu'on injecte ses dépendances.
F9. Aucun outil pour : champ, réglage, événement. Il faut savoir où regarder dans `docs/guides/`.
F10. `adding-a-setting.md` étape 2 contredit `FeatureFolderVocabularyTests` / `LayoutConventionTests` (validateur dans un fichier séparé refusé, deux types publics refusés).
F11. Les clés `appsettings.json` / `docker-compose.yml` / `.env.example` d'un réglage ne sont contrôlées par aucun test.
F12. `format-fix` ne corrige pas IDE1006 (nommage `_` des constantes privées) et le message `Unable to fix IDE1006` ne dit pas dans quel fichier ; il faut lancer `format` pour voir la ligne.
F13. `web-app` imprime 10 modifications à faire à la main ; `web-join` ne marche pas pour la première app ; sept de ces dix ne sont contrôlées par aucun test ; l'ADR 0024 est ajouté sans ligne d'index.
F14. `verify` ne couvre pas le web (pas de `pnpm lint/test/build`, pas de contrôle du `schema.ts` généré) ; un client web périmé après un changement d'API passe le gate.
F15. `presentation` ne propose pas `worker` ; ni la doc de `web-app --help` ni `presentation --help` ne le disent (il se choisit seulement à la génération).
F16. Après `presentation`, la migration du contexte Auth est imprimée en haut (post-action), pas dans « Next: » ; `Tasks.cs migration-add` ne vise que le contexte métier et crée une migration vide sans avertir.
F17. `openapi` et `pnpm api` ne sont lancés ni par `presentation`, ni par `new-feature`, ni par `new-use-case` (ce dernier l'imprime).
F18. `remove-feature` n'agit que sur un squelette jamais modifié ni enregistré ; sinon rapport (109 « possibles » dont du bruit) et travail manuel : la suppression d'un exemple casse aussi les tests MCP, vocabulaire de `LayoutConventionTests`, tableaux de doc, `.http`.
F19. `coverage` : plantage `PAL_SEHException` sous WSL2 (non vérifié côté template), pas de doc.
F20. Pas de test d'intégration ni de e2e généré pour une feature : tout reste à écrire (76 lignes pour 3 tests).

## 5. Points forts (prouvés)

- Les messages d'erreur des tests disent quoi faire : `PendingModelChangesTests` (« run migration-add <Name> »), `OpenApiDocumentTests` (« run dotnet run Tools/Tasks.cs openapi »), `ConfigurationSurfaceTests` (« Add a row to that section's table »), `ModuleDependencyTests` (nom de la règle, csproj fautif, raison).
- `presentation` (MCP) : un seul appel de 30 s fait join, scaffold, solution, formatage, 83 fichiers créés, 4 listes fermées complétées.
- Tout le gate `verify` passe sans surprise sur du code correct ; 3 733 à 3 789 tests, Docker/Testcontainers sans intervention.
- Les analyseurs (warnings en erreurs) attrapent les erreurs de style tôt et en une ligne (CA1862, IDE0005, xUnit1051, IDE1006).
- `new-feature` : 52 fichiers + manifeste en 0,7 s ; `remove-feature --apply` sur un squelette intact : 29 fichiers en un appel.
- `web-app` déduit ses paramètres depuis le projet existant (« Taken from Acme.Shop ») et `web-scaffold` marche en 22 s.

## 6. Non vérifié

- `coverage` (plantage de l'agent/WSL, voir §0) ; donc « Done means » du projet n'est jamais atteint ici.
- Modes interactifs de `dnx` (pas de TTY) ; `worker`, `blazor-server`, `avalonia`, `wpf` ; `end-to-end` ; démarrage réel de l'API et du MCP ; `docker compose up` (seul `docker compose config -q` a été lancé, OK).
- Un test qui prouve chaque garantie en la cassant : fait pour `ConfigurationSurfaceTests` et pour la règle du domaine ; pas fait pour `DomainEventTests`.
