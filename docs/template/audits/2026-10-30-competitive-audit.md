# Audit concurrentiel et plan de publication

> Rapport publié aussi comme artifact privé : https://claude.ai/artifact/3Xc3EpABBhSGmgvjiqNzGZ.
> Les rapports bruts des agents sont dans [`2026-10-30/`](2026-10-30/). Branche de lecture : elle n'est pas faite pour être fusionnée telle quelle (les rapports citent des chemins du scratchpad et du dépôt concurrent, que les gates de doc refuseraient).

Comparaison du template `dotnet new` de ce dépôt (`Pierre-MarieMarchio/Template-DotNet_Clean_Architecture`) avec
[jasontaylordev/CleanArchitecture](https://github.com/jasontaylordev/CleanArchitecture), le 30 octobre 2026.

- **Notre template** : `develop` au commit `5f22faa5`, empaqueté en `1.1.0-dev` pour l'audit (artefact de version,
  pas une friction). Un complément mesure `develop` à jour, au commit `935d552a` (7 commits de plus, dont l'outil
  `dotnet cleanarch`), empaqueté en `1.2.0-dev`.
- **Le concurrent** : `main` au commit `345e7fe` (2026-10-26) et sa dernière release `v10.8.0`
  (`Clean.Architecture.Solution.Template` sur nuget.org), installée comme un vrai utilisateur.
- **Méthode** : 44 lancements d'agents, jamais plus de 3 à la fois, en lecture seule sur ce dépôt. Le détail est en section 12,
  les rapports bruts de chaque agent sont en annexe.

## 1. Verdict

1. **Sur le fond, notre template dépasse nettement le concurrent** sur 9 axes de 13 : architecture tenue par 195 tests, socle transverse, sécurité par défaut, tests et gate, CI et release, docs, conventions, extensibilité, front-ends. Les juges à l'aveugle arrivent au même résultat sans savoir quel dépôt est le nôtre.
2. **Le premier usage est notre meilleur argument** : deux personas sur deux vont au bout en 5 commandes (3 257 tests verts, API qui répond), alors que la version publiée du concurrent ne compile plus (120 erreurs NU1902/NU1903, issue #1675).
3. **Le CLI est en avance, mais encore loin de l'idéal** : nos générateurs écrivent 52 fichiers en 0,7 s, puis *impriment* 6 à 11 éditions, la migration et les listes à compléter ; ajouter un champ, un réglage ou un événement n'a aucun outil, et un retrait réel reste manuel. C'est là que se jouera l'écart.
4. **Il garde trois avances réelles** : moins de code par use case et par champ (choix des ADR 0003 et 0005), des tests navigateur, et surtout une audience sans commune mesure (20 692 étoiles et 275 469 téléchargements contre 1 et 89).
5. **Publiable en l'état : non.** Quatre bloquants, tous petits : la release automatique est en panne (BOM dans `template.json`), la version annoncée (1.1.0) devrait être une majeure, `dotnet cleanarch` plante sur tout projet généré (sur `develop` à jour), et le parcours non interactif documenté échoue (`--yes` avalé par `dnx`). Les lots E1, E2 et E3 du plan les lèvent en deux à trois jours.

---

## 2. Ce que ce template veut être

La page qui sert d'étalon à tout l'audit. Une différence avec le concurrent n'est un retard que si elle va contre ce
que nous voulons faire ; une différence couverte par une ADR est un choix, remis en cause seulement si une preuve
montre qu'elle coûte aux utilisateurs.

**Public.** Une équipe .NET qui démarre un produit et compte le garder des années. Le projet généré est un point de
départ conservé, pas une démonstration.

**Promesses.**

1. **Ne coder que du métier** : le socle est livré avant que la première feature en ait besoin (pagination,
   idempotence, HTTP sortant, jobs sous lease, cache, mail, fichiers). ADR 0001.
2. **Une option retire une partie, elle ne la désactive pas** ; une combinaison interdite est refusée à la
   génération. ADR template 0001.
3. **Les règles sont tenues par des tests**, pas par la discipline (architecture, contrat OpenAPI, codes d'erreur,
   default-deny, citations de la doc) ; un risque, un type de test. ADR 0020.
4. **Un contrat HTTP uniforme** : ProblemDetails à codes stables, `ETag`/`If-Match`, `Idempotency-Key`, pagination
   offset et keyset, rate limit. ADR 0008, 0010.
5. **Une sécurité sérieuse par défaut** : default-deny, propriétaire dans la requête (404, jamais 403), sessions en
   familles de refresh tokens, routes anonymes muettes, passkeys, fichiers contrôlés. ADR 0009, 0013 à 0016, 0018.
6. **Une doc écrite pour le projet**, conditionnée par les options. ADR template 0002.
7. **Des hosts et des fronts au choix**, issus des générateurs officiels à version épinglée. ADR template 0008, 0009,
   0020.
8. **Un gate complet lancé depuis un clone** (`verify`, `coverage`), avec CI, release, CodeQL et Sonar.

**Choix assumés.**

| Choix | ADR |
|---|---|
| Pas de MediatR ni de behaviours : le contrôleur appelle `IUseCase<,>` par son interface | 0003 |
| Deux formes de feature : un agrégat, ou un service sur un store | 0002 |
| Quatre mots pour les ports (Repository, Queries, Store, Table), pas de repository générique ni d'`IQueryable` | 0003 |
| Le domaine n'est pas mappé par EF : des records de ligne, un mapper et un tracker | 0005 |
| Événements publiés après le commit, sans outbox ; un balayage sert de garantie | 0006 |
| Une base, un schéma par feature, un contexte par module | 0007 |
| Pas de suppression douce, d'AutoMapper, de Minimal APIs, de multi-tenant ni de permissions fines | `docs/architecture/README.md` |
| Jobs dans l'API ou dans un worker, sous lease | 0011, 0025 |
| Un provider de base par projet, PostgreSQL par défaut ; SQLite pour le développement seulement | template 0005, 0021 |
| Une abstraction n'est extraite qu'une fois deux cas réels prouvés | `AGENTS.md` |

**Refus explicites** (roadmap) : un générateur au choix de l'utilisateur, une app desktop branchée sur la couche
application, MySQL tant qu'EF Core 10 n'a pas de provider officiel.

**En cours, pas encore livré au commit audité** : `dotnet cleanarch` (ADR template 0017 à 0019, « Proposed »).

---

## 3. Le premier usage

Quatre personas, développeurs .NET compétents qui découvrent le template, partis seulement de ce qu'un utilisateur
voit (page GitHub, page nuget.org, `--help`, messages du terminal, doc du projet généré). Deux sur notre template, deux
sur le concurrent. Le second persona du concurrent a été arrêté avant la fin pour libérer une place ; son journal est
gardé et recoupe le premier.

### Les mesures côte à côte

Configuration par défaut, du README à l'application qui répond.

| Étape | Nous (`p2-ours-1`, `p2-ours-2`) | Concurrent (`p2-comp-1`, `p2-comp-2`) |
|---|---|---|
| Installer | 0 étape avec le launcher (`dnx CleanArch.Create` installe le paquet) ; `dotnet new install` sinon | `dotnet new install` en 2,3 à 2,7 s |
| Générer | 10 à 13 s (`dotnet new` ou launcher) ; mode guidé : 14 écrans, réponses déduites annoncées | 1,7 à 2,2 s, deux options seulement (`-cf`, `-db`) |
| Premier build | inclus dans `bootstrap` : 1 min 53 à 2 min 27, **réussi** | **échec : 120 erreurs NU1902/NU1903** (audit NuGet sous TreatWarningsAsErrors), aucun contournement documenté ; 108 erreurs pour la configuration minimale |
| Débloquer le build | sans objet | contournement deviné `-p:NuGetAudit=false`, trouvé dans l'issue ouverte [#1675](https://github.com/jasontaylordev/CleanArchitecture/issues/1675) |
| Premiers tests verts | `test --no-integration` : 3 257 tests en 1 min 04 à 1 min 10, sans Docker | 33 tests verts en 1 min 16 ; 6 tests d'acceptation Playwright en échec (environnement non supporté, non compté comme friction) |
| Application qui répond | `cp .env.example .env` puis `docker compose up -d --build --wait` : 35 à 39 s, 4 conteneurs sains ; `/health` 200, inscription 202, mail dans Mailpit | `dotnet run --project src/AppHost` : 12 s ; frontend 200, health 200 ; connexion en 500 sans `?useCookies=true` |
| Étapes manuelles du chemin le plus court | 5 commandes (`dnx`, `cd`, `bootstrap`, `cp .env`, `compose up`) | 3 commandes, plus le contournement du build |
| Prérequis | SDK 10.0.100+, Docker Compose v2, réseau ; Node et pnpm avec un front ; tous annoncés dans le README | SDK 10, Node, Docker pour PostgreSQL ou SQL Server, certificat de dev, Playwright et pwsh pour les tests d'acceptation |

Configurations complémentaires chez nous : minimale (`--auth none --examples none --files false`) bootstrap en 50 s à
1 min 03 et 798 tests verts ; riche (`--web vite-react` et les hosts `api`, `blazor-server`, `worker`, `mcp`)
bootstrap en 3 min 17, 17 suites vertes en 6 min 25, 8 conteneurs sains qui répondent (API, portail, MCP, app web
derrière son relais).

**Ce qui distingue le plus les deux parcours** : le nôtre va au bout sans rien deviner, en 5 commandes et environ
5 minutes ; celui du concurrent est plus court mais casse au premier build de la version publiée, depuis que des
avis de sécurité visent ses dépendances épinglées. Trois agents indépendants l'ont constaté, et l'issue #1675
est ouverte sans correctif publié.

**Où mettre son premier code métier.** Chez nous, les deux personas l'ont trouvé seuls : `new-feature`, indiqué par
`first-run.md` et `adding-a-feature.md`. Chez le concurrent, le README cite `ca-usecase`, mais rien ne dit où vont
l'entité, sa configuration ni son endpoint.

### Les frictions de notre premier usage, du pire au moindre

| # | Friction | Preuve | Vu par |
|---|---|---|---|
| P1 | `--yes` est avalé par `dnx` : les exemples de la doc (`dnx CleanArch.Create new … --yes`) et la commande que le launcher imprime lui-même (« Next time, skip the questions with: … --yes ») rouvrent les questions ou échouent avec « add --yes ». Il faut `dnx CleanArch.Create -- new … --yes`, que rien ne dit. | `docs/template/using-the-template.md:68-70` ; journal `p2-ours-1` 12:09:12 et 12:39:01 ; inchangé sur `935d552a` (`p3-head` F9) | 2 personas + `p3-head` |
| P2 | La suite `test --no-integration` du projet riche prend 6 min 25 contre 1 min 10 : `Mcp.UnitTests` (2 min 55) et `Portal.UnitTests` (1 min 52) attendent, sans calcul. La doc la présente comme la commande « while you work ». | `p2-ours-1` F2 | 1 persona |
| P3 | `GET /` répond 401 ; rien dans `first-run.md` ne dit d'appeler `/health` pour voir que l'API vit. | `p2-ours-1` F3 | 2 personas |
| P4 | Le README du projet riche et `first-run.md` ne listent pas les mêmes services ; le service Compose du portail s'appelle `presentation-portal`, `docker compose logs portal` échoue. | `p2-ours-1` F4 | 1 persona |
| P5 | Le README généré affirme une HTTP API sur `:8080` même pour un projet sans API (hosts sans `api`). | `.template.config/variants/project/README.md:9-20` | analystes G5 |
| P6 | `dotnet new cleanarch-webapi --help` liste `mysql`, que le launcher et la doc refusent ; le README dit « a project holds the API or the worker » alors que les deux ensemble sont acceptés. | `p2-ours-1` F6 | 1 persona |
| P7 | Le README racine n'a pas de ligne `--database` dans sa table d'options, et sa liste des fronts est incomplète. | `README.md:56-70`, `:21` | persona + analystes G5 |
| P8 | Bruit git après le bootstrap riche : fichiers de `dotnet new blazor` en CRLF non normalisés. | `p2-ours-1` F5 | 1 persona |
| P9 | `docs/template/using-the-template.md` donne deux fois la section d'installation depuis nuget.org. | `:81-84` et `:99-103` | persona + analyste G5 |

### Les points forts du premier usage, prouvés

- Le mode guidé du launcher va de bout en bout : choix décrits, réponses déduites annoncées (« the only option when
  Authentication is none »), récapitulatif, puis commit, bootstrap, second commit et `.env` faits par l'outil.
- Chaque génération imprime la commande `dotnet new` équivalente et les étapes suivantes avec la table des adresses.
- Le launcher et `dotnet new` produisent des arbres identiques (comparaison par script, au GUID du `.sln` près).
- Les messages disent quoi faire : `git` sans identité (« First commit skipped: … git config --global user.name »),
  `bootstrap` (« Commit this before anything else »), tests d'architecture (« has its row in docs/reference/api.md »).

---

## 4. Le CLI au quotidien

Trois personas sur un projet généré par défaut : deux chez nous (`p3-ours-1` angle `--help`, `p3-ours-2` angle doc),
un chez le concurrent (`p3-comp-1`). Un quatrième (`p3-head`) a refait les tâches clés sur `develop` à jour.
Les durées chez nous sont des temps d'agent avec réflexion et `verify` ; celles du concurrent sont des temps d'agent
sans gate équivalent : elles ne se comparent pas entre elles, les compteurs si.

### Tâche par tâche

| Tâche | Nous : ce que fait l'outil | Nous : à la main | Concurrent : ce que fait l'outil | Concurrent : à la main | Verdict |
|---|---|---|---|---|---|
| 1. Feature possédée, CRUD, liste paginée, validation, test de bout en bout | `new-feature` : 52 à 54 fichiers en 0,7 s, manifeste, page de doc, ~23 tests ; possession 404, ETag, pagination, suppression de compte générées | 6 éditions imprimées (enregistrements, listes fermées), migration, lignes `docs/README.md` et `api.md` ; le champ d'exemple `Description` à réécrire dans 22-23 fichiers ; ~215 à ~490 lignes ; aucun test d'intégration généré | `ca-usecase` : 5 squelettes à `NotImplementedException`, DTO non créés | entité, configuration, 2 `DbSet`, DTO et profil AutoMapper, `PaginatedList` (absent), endpoint, filtrage par propriétaire (aucun modèle) ; ~380 lignes ; build cassé par la collision du namespace `Projects` avec Aspire | **on dépasse** (moins de lignes, garanties générées) mais l'outil imprime au lieu de faire |
| 2. Un use case de plus (commande puis requête) | `new-use-case` : 5 fichiers et l'action de contrôleur prête à coller pour une commande ; pour une recherche, ni `--input id` ni `--input none` ne conviennent | ~350 lignes (deux personas : ~190 + ~220) ; 3 à 4 tours de compilation | 2 squelettes | ~110 lignes ; une route ou un squelette oublié passe sans signal | **en retard en volume** (choix ADR 0003 : 4 fichiers par use case), **devant en sûreté** |
| 3. Un champ et sa migration | aucun outil ; `migration-add` | 14 à 20 fichiers, ~45 à 113 lignes | aucun outil ; pas de `dotnet-ef` épinglé ni de migration initiale | ~14 lignes, 7 fichiers, après avoir installé `dotnet-ef` soi-même | **en retard** (coût de l'ADR 0005 : rows + mapper + DTO) |
| 4. Un réglage validé et documenté | aucun outil ; un guide en 7 étapes | ~120 à 218 lignes ; le guide `adding-a-setting.md:23` contredit deux tests d'architecture (validateur dans un fichier séparé refusé) ; les clés `appsettings`/Compose/`.env` ne sont tenues par aucun test | aucun outil | ~70 lignes ; rien ne vérifie la doc ni la validation au démarrage | **égalité** sur l'outillage, **devant** sur la garantie (`ConfigurationSurfaceTests`), **en retard** sur le guide |
| 5. Un domain event et sa réaction | aucun outil ni guide | ~110 à 192 lignes ; `DomainEventTests` satisfait par la présence d'un consommateur | aucun outil | ~70 lignes ; événement publié avant l'écriture | **devant** sur la sûreté, **égalité** sur l'outillage |
| 6a. Une app web | `dotnet new cleanarch-webapp` ou launcher : 58 fichiers, `web-scaffold` en 16 à 22 s | 10 à 11 éditions imprimées (include Compose, `.env.example`, `.dockerignore`, Dependabot ×2, CodeQL, 5 docs), dont 7 à 8 ne sont tenues par aucun test ; `verify` ne lance pas le web en local (la CI `web.yml` le fait) | rien : copie de 29 fichiers d'une autre génération | ~7 fichiers et ~8 neufs, 4 listes à la main | **on dépasse** mais la première app reste câblée à la main |
| 6b. Un autre host | MCP par `cleanarch-presentation` + `presentation-join` : 81 à 83 fichiers créés, 24 à 31 éditions faites en 0,3 à 30 s ; **le worker ne peut pas être ajouté après coup** | migration du contexte Auth imprimée en haut de sortie ; `migration-add` crée une migration **vide dans le mauvais contexte** sans avertir (2 personas sur 2, et toujours sur `935d552a`) ; `openapi` et `pnpm api` non cités | `dotnet new worker` du SDK | le worker échoue à l'activation (Identity et cookies enregistrés par l'unique `Infrastructure`) ; réécriture du `.csproj` | **on dépasse**, avec un piège silencieux |
| 7. Retirer une feature d'exemple, puis la sienne | `remove-feature --apply` supprime un squelette jamais modifié (29 fichiers en une commande) | sur un exemple ou une feature modifiée : rapport seul (bruité : 169 à 268 correspondances « possibles ») ; 11 à 25 min, 14 à 25 fichiers ; 14 endroits de doc, 25 lignes de `.http` et le client web restent périmés sans signal ; 2 à 3 tests d'architecture rouges sur des listes que le guide ne cite pas | aucun outil | ~25 lignes, 5 listes à la main | **devant** pour un squelette, **en retard** sur l'ergonomie d'un vrai retrait |
| 8. Casser une règle d'architecture (Domain → Infrastructure) | le build passe ; `ModuleDependencyTests.TheDomainLayer_ReferencesNothingOutsideItself` échoue en 9 s avec le chemin du `.csproj` et la raison | — | aucune règle : seul un cycle MSBuild (`MSB4006`) arrête la référence ; une référence par paquet passe sans signal | — | **on dépasse** nettement |

**`verify` du premier coup.** Chez nous, `verify` est vert à la fin de chaque tâche chez les deux personas (de 3 732
à 3 900 tests), mais 6 `verify` sur 16 ont d'abord échoué chez `p3-ours-2` (4 imputables au template selon l'avocat de notre camp), chacun avec un message exploitable :
migration manquante, format, test d'architecture. `dotnet build` ne voit pas IDE1006 ni IDE0005, que `verify` voit :
un build vert peut cacher un gate rouge. Chez le concurrent, il n'y a pas de gate à passer ; une migration oubliée
passe sans signal.

**`--help` suffit-il ?** Presque : `dotnet run Tools/Tasks.cs help` liste chaque tâche, mais
`Tasks.cs new-feature --help` affiche l'aide de `dotnet run` ; la vraie aide est `Tools/NewFeature.cs -- --help`.

### `develop` à jour (`935d552a`) : ce qui a changé

| Friction relevée sur `5f22faa5` | Sur `935d552a` | Preuve |
|---|---|---|
| `dotnet cleanarch` introuvable | **présent mais squelette** : `--help` et `--version` seulement ; **plante sur tout projet généré par défaut** (`cleanarch.json is not a project file: … Path: $.presentation`), car `ProjectFile` attend un objet là où le fichier écrit un tableau ; ni la doc, ni le README, ni `.config/dotnet-tools.json` ne disent comment l'obtenir | `Launcher/CleanArch.Cli/ProjectFile.cs:9` contre `cleanarch.json:17` (vérifié par l'orchestrateur) ; `p3-head` F1, F2 |
| Le projet ne sait pas ce qu'il contient | **corrigé** : `cleanarch.json` enregistre la génération et chaque ajout (web, presentation), et le launcher le lit | `p3-head` F3 |
| Première app web câblée à la main | **en partie corrigé** : le launcher joint l'app, mais 10 éditions restent imprimées, dont 8 sans test | `p3-head` F5 |
| `new-feature` imprime 6 éditions et la migration, pas de `--fields` | inchangé | `p3-head` F4 |
| `migration-add` vide dans le mauvais contexte après le MCP | inchangé | `p3-head` F6 |
| Worker non ajoutable après coup | inchangé | `p3-head` F7 |
| `remove-feature` inutilisable dès qu'une feature est enregistrée ou éditée | inchangé | `p3-head` F8 |
| `--yes` avalé par `dnx`, double BOM du README MCP | inchangés | `p3-head` F9 |

### Les frictions du quotidien : ce qu'il faut savoir sans qu'aucun outil le dise

1. Les 6 éditions de `new-feature` ne se font pas seules, et la migration non plus.
2. Le champ d'exemple `Name`/`Description` est un placeholder à réécrire dans 22 à 23 fichiers.
3. Après un ajout de host, la migration va dans le contexte Auth, pas dans celui que vise `migration-add`.
4. `openapi` (et `pnpm api` avec un front) est à relancer après tout changement de route : l'outil ne le cite pas.
5. Un réglage : le validateur va dans le même fichier que les options, `internal`, contrairement à ce que le guide
   laisse lire.
6. Après un retrait, la doc, le `.http` et le client web gardent des références mortes, sans test qui le dise.
7. Le worker se choisit à la génération ou jamais.
8. `dotnet build` vert ne veut pas dire `verify` vert.
9. Le web n'est vérifié que par la CI : `verify` en local ne lance ni lint, ni tests, ni build du workspace.

### Le parcours idéal, et l'écart

| Tâche | Parcours idéal | Écart aujourd'hui |
|---|---|---|
| Feature | `dotnet cleanarch add feature Projects Project --access owner --fields "Name:string(120), Budget:decimal>=0"` : fichiers, éditions, migration, `openapi`, page de doc, test d'intégration de base ; `verify` vert | les éditions et la migration sont imprimées ; pas de `--fields` ; pas de test d'intégration |
| Use case | `add use-case Projects SearchProjects --kind query --input "Name:string"` avec l'action et sa route | `--input` limité à `id`/`none` |
| Champ | `add field Projects Project Deadline:DateOnly?` puis migration | aucun outil |
| Réglage | `add setting Projects:SearchResultLimit:int(1..100)=20` : options, validateur, enregistrement, `appsettings`, ligne de `configuration.md` | aucun outil, guide ambigu |
| Événement | `add event Projects ProjectArchived --consumer …` | aucun outil ni guide |
| App web, host | `add web-app`, `add presentation worker` qui complètent toutes les listes, `verify` qui couvre le web | 10 éditions imprimées, worker impossible, web hors `verify` |
| Retrait | `remove feature Projects` qui retire aussi les enregistrements, les docs, les `.http` et le client, avec `--dry-run` | rapport seul dès qu'une feature est enregistrée |

---

## 5. Le socle

### Les briques, chez nous et chez le concurrent

État : C complète, P partielle, A absente. La classe vient de l'avocat du « non » (indispensable, utile en option,
hors périmètre) ; « déjà là » quand rien ne manque.

| Brique | Nous | Concurrent | Classe | Note |
|---|---|---|---|---|
| Entité et agrégat de base | C | P (setters publics, id `int`) | déjà là | `Entity.cs`, `AggregateRoot.cs` ; `DomainModelTests` |
| Value objects | C (records) | P (classe `ValueObject`) | déjà là | |
| Identifiants typés | P (`UserId` seul) | A | hors périmètre | un seul cas réel ; `StronglyTypedId`/`Vogen` existent |
| Domain events | C (après commit) | P (avant l'écriture) | déjà là | ADR 0006 |
| Invariants | C | P (entités anémiques) | déjà là | `DomainGuard` |
| Concurrence optimiste | C (ETag de bout en bout) | A | déjà là | ADR 0008 |
| Horodatage et audit | C | C | déjà là | |
| Suppression douce | A (choix) | A | hors périmètre | `docs/architecture/README.md` |
| Horloge abstraite | C (`IDateTimeProvider` maison) | C (`TimeProvider`) | hors périmètre (doc) | doublon du framework, contourné à 2 endroits |
| Forme d'un use case | C (choix) | C (MediatR) | déjà là | ADR 0003 |
| Pipeline (validation, autorisation, logs) | P (choix : appel explicite) | C (behaviours) | **indispensable : un test** | rien ne vérifie qu'un validateur est consommé |
| Result et erreurs typées | C | P | déjà là | un défaut : `domain.invariantViolated` en 409 ou 400 |
| Pagination | C (offset et keyset) | A (retirée de `main`) | déjà là | |
| Tri et filtre | C | A | déjà là | |
| Projections | C (à la main) | C (`ProjectTo`) | déjà là | |
| Utilisateur courant | C | C | déjà là | |
| Propriété des données | C (un écart à l'ADR 0009) | A | déjà là | chemin agrégat chargé par id puis comparé en mémoire |
| Idempotence | C | A | déjà là | ADR 0010 ; fenêtre résiduelle non écrite |
| Unicité concurrente | P (`IsUniqueViolation` hors du commit métier : 500) | A | **indispensable** | repli 409 dans `EfUnitOfWork` |
| Persistance et unit of work | C | P (`IApplicationDbContext`) | déjà là | ADR 0005, 0007 |
| Outbox et événements d'intégration | A (choix) | A | hors périmètre | ADR 0006 |
| Jobs planifiés | C (intervalle, sous lease) | A | **indispensable : extraction interne** | un job générique « passe à intervalle qui appelle un use case » (~170 lignes copiées de Reminders aujourd'hui) |
| Planification calendaire (cron) | A (choix) | A | hors périmètre | ADR 0025, 0011 |
| Cache | C (limité par choix) | A | déjà là | |
| Mails et notifications | C (SMTP, Postmark, gabarits par langue) | A | déjà là | |
| Fichiers | C (option `--files`) | A | déjà là | ADR 0018 |
| Recherche | P (`LIKE` échappé) | A | hors périmètre | |
| Import et export | A | A | hors périmètre | |
| HTTP sortant et résilience | C | C | déjà là | |
| Recul exponentiel partagé (`RetryBackoff`) | P (2 copies) | A | utile en option | conditionné par `reminders \|\| files` |
| Secrets | P (user secrets, variables) | P (Key Vault optionnel) | hors périmètre (brique) ; une page de doc utile | |
| Observabilité | P (traces et métriques OTLP ; logs en console) | C (ServiceDefaults Aspire) | utile en option | export OTLP des logs derrière `OpenTelemetry:Enabled` |
| Health checks | C | P (Development seulement) | déjà là | |
| Configuration validée | C | A | déjà là | `ConfigurationSurfaceTests` |
| ProblemDetails | C | P (sans code, 400 sans corps) | déjà là | |
| Versioning d'API | C | A | déjà là | |
| OpenAPI | C (Development seulement) | C (y compris en production) | déjà là | |
| Rate limiting | C (par adresse) | A | déjà là | |
| CORS | C (fermé par défaut) | P (`AllowAnyOrigin`) | déjà là | |
| Localisation | P (mails) | A | hors périmètre | |
| Multi-tenant, feature flags | A | A | hors périmètre | |
| En-têtes de sécurité | C (API) ; **A sur le portail Blazor** | P (HSTS seul) | déjà là ; défaut à corriger | voir section 10 |
| Orchestration locale | P (Compose) | C (Aspire) | utile en option | roadmap #122 |

### Le code non métier que les trois features réalistes ont dû écrire

Deux agents (Opus et Sonnet) ont écrit, chacun de son côté, trois features sur les deux templates.

| Feature | Chez nous, non métier | Chez le concurrent, non métier |
|---|---|---|
| **Contact** : CRUD possédé, email unique par propriétaire, tags | ~25 lignes pour traduire la course d'unicité en 409 (elle finit en 500) ; ~90 lignes de tags recopiées par feature ; le socle épargne possession, ETag, pagination, suppression de compte | ~190 lignes, et il manque quand même la possession, la concurrence, la pagination, les tags et le 409 |
| **Commande** : machine à états, mail à la validation, stock réservé par un autre module | port d'adresse du propriétaire (~40 + 100 de test) ; ~330 lignes de mapper et tracker pour les enfants d'agrégat ; balayage de garantie d'envoi (80 à 250 lignes, choix de l'ADR 0006) | ~130 lignes de mail ; 409 de transition et jeton de concurrence à écrire ; événement publié avant le commit, donc un mail peut partir pour une transaction annulée |
| **Taux de change** : API tierce, job nocturne, relance, alerte | ~170 lignes de squelette de job recopié ; ~140 lignes d'éditions pour un nouveau module d'infrastructure ; pas de déclencheur horaire (choix) | job, verrou, options validées et mail à écrire ; la résilience HTTP est fournie |

**Lecture** : chez nous, le non métier restant tient en quatre briques étroites ; chez le concurrent, il touche les
fondations (possession, concurrence, mail, jobs). Les deux agents concluent à une avance nette sur Contact et
Commande, plus faible sur Taux de change.

### Les plaidoiries de l'avocat du « non »

34 briques proposées, toutes plaidées contre, puis jugées. **4 indispensables, 3 utiles en option, 27 hors
périmètre.** Les trois briques de code qui survivent coûtent ensemble moins de 100 lignes de production et
n'ajoutent aucune jambe d'acceptance.

| Brique | Plaidoirie contre | Survit ? | Classe |
|---|---|---|---|
| Course d'unicité traduite en 409 | `IsUniqueViolation` existe ; un cas réel veut un résultat métier, pas un 409 ; `EntityFrameworkCore.Exceptions` le fait | oui, en petit : un repli 409 au lieu de 500 | indispensable |
| Job générique à intervalle | `AddMaintenanceTask` couvre les purges et a quatre appelants | oui : les deux copies Reminders et Files prouvent la forme | indispensable (extraction interne) |
| Test : chaque validateur est consommé | l'ADR 0003 assume la convention ; aucun oubli constaté | oui : la règle est écrite, rien ne la tient | indispensable (test) |
| Test : ni `#pragma` ni `<NoWarn>` | `CheckComments` contrôle déjà certaines directives | oui, avec une liste d'exceptions motivées | indispensable (test) |
| Export OTLP des journaux | stdout est la norme | oui | utile en option (réglage existant) |
| `RetryBackoff` partagé | 12 lignes par copie ; deux exemples amovibles | oui, faiblement | utile en option |
| AppHost Aspire | Compose est choisi (ADR 0001) ; churn des paquets Aspire | oui, déjà en roadmap | utile en option (#122) |
| Outbox, cron, behaviours | ADR 0006, 0025, 0003 | non | hors périmètre |
| Identifiants typés, annuaire d'adresses, tags partagés, réconciliation parent/enfants | un seul cas réel, ou le gain ne paie pas le coût | non | hors périmètre |
| Recherche plein texte, import/export, feature flags, multi-tenant, soft delete, HSTS | aucun cas réel, ou choix écrit | non | hors périmètre |

Le rapport complet (`p4-devil.md`) détaille les 34 plaidoiries et relève aussi trois erreurs dans les rapports
d'entrée, tranchées dans le code.

---

## 6. Le tableau concurrentiel

Pour chaque axe des phases 5 : deux analystes indépendants (Opus et Sonnet), un juge à l'aveugle (Opus, sans
`AGENTS.md`, sur des dossiers anonymisés), les deux avocats, puis la vérification adverse de chaque constat. Les axes
« premier usage », « CLI » et « socle » reprennent les phases 2 à 4, passées par les mêmes avocats et vérificateurs.
Confiance : **haute** quand analystes, juge et vérification concordent ; **moyenne** quand un avocat a contesté avec
une preuve recevable ou qu'une mesure n'a été faite qu'une fois.

| Axe | Verdict | Preuve principale | Juge à l'aveugle | Confiance |
|---|---|---|---|---|
| Premier usage | **on dépasse** | nous : 5 commandes, bout en bout réussi par 2 personas ; lui : build en échec (120 erreurs NU1902/NU1903) dans la version publiée, #1675 ; réserve : `--yes` avalé par `dnx` | — | haute |
| CLI et outillage | **on dépasse**, loin de l'idéal | `new-feature`, `new-use-case`, `remove-feature`, launcher, item templates, `presentation-join` contre le seul `ca-usecase` ; mais 6 à 11 éditions imprimées, pas d'outil pour champ, réglage, événement ; `dotnet cleanarch` cassé sur `develop` | — | haute |
| Socle | **on dépasse** | 4 briques non métier restantes contre des fondations absentes (possession, concurrence, pagination, mail, jobs) ; 2 épreuves concordantes | — | haute |
| Concepts du code | **choix différent assumé (ADR 0003, 0005)**, devant en sûreté | `Result` à codes stables et table testée, possession, événements après commit, entités verrouillées ; lui : MediatR, AutoMapper (licences), exceptions, événements avant l'écriture ; il demande moins de code par use case (~2 fichiers et 48 lignes contre 4 et 72) | nous (lisibilité et code à écrire : eux) | haute |
| Architecture et frontières | **on dépasse** | 195 tests d'architecture et 17 contre-épreuves ; Domain → Infrastructure arrêté en 9 s ; chez lui aucune règle exécutable, EF ajouté au Domain sans signal | nous | haute |
| Conventions | **on dépasse** ; en retard sur la longueur des chemins | vocabulaire de dossiers fermé et testé, style imposé au build ; lui : suggestions d'éditeur ; nos chemins jusqu'à 176 caractères contre 95, `NU5123` ; l'arborescence n'a pas d'ADR | nous | moyenne |
| Extensibilité | **on dépasse** | un module par capacité, chaque host se compose, adaptateurs à tests de contrat ; lui : une `Infrastructure` unique qui fait échouer un worker ; réserve : le worker ne s'ajoute pas après coup chez nous, et `migration-add` piège | nous (contesté sur « code à écrire ») | moyenne |
| Tests et qualité | **on dépasse** ; en retard sur les tests navigateur et la vitesse de boucle | 3 456 tests sur 6 types, intégration HTTP contre PostgreSQL, seuil de couverture tenu par test ; lui : 32 méthodes, intégration vide, mais Playwright en CI ; `verify` 4 à 5 min contre `dotnet test` 40 s | nous | haute |
| CI et release | **on dépasse en conception** ; release en panne | Trusted Publishing, provenance, 25 jambes avant fusion, CI livrée au projet ; lui : clé API, matrice après fusion (11 échecs sur 50), aucun workflow livré ; chez nous un incident (BOM) bloque la release depuis le 28/10 | nous | haute |
| Sécurité | **on dépasse** | default-deny testé route par route, possession, en-têtes et CSP, CORS fermé, limiteur ; lui : pas de repli, `AllowAnyOrigin`, OpenAPI en production ; réserves : portail Blazor sans en-têtes ni repli, 11 alertes CodeQL à trier | nous | haute |
| Front-ends | **on dépasse** (contesté) | 7 valeurs `--web` issues des CLI officielles, client typé commité et contrôlé, SSR avec nonce ; lui : Angular et React copiés, mais une app authentifiée qui marche dès la génération et testée dans un navigateur | nous (avocat du concurrent : égalité) | moyenne |
| Docs et onboarding | **on dépasse** ; en retard sur la vitrine | jusqu'à 64 pages livrées au projet, conditionnées, tenues en partie par des tests, 48 ADR ; lui : un README de 56 lignes et un site externe sans versions, dont l'ADR-003 contredit le code ; chez nous : pas de description, topics ni badges | nous | haute |
| Communauté et entretien | **en retard** | 20 692 étoiles, 4 295 forks, 367 discussions, 275 469 téléchargements contre 1, 0, Discussions désactivées et 89 ; bus factor de 1 des deux côtés ; lui : 221 jours sans release | eux | haute |

**Le risque partagé.** Les deux templates combinent TreatWarningsAsErrors et l'audit NuGet. Chez lui, c'est déjà arrivé
(#1675) ; chez nous, rien n'empêche que ça arrive. Ce fait est compté une seule fois, dans la communauté et
l'entretien, et le lot E4 du plan le traite.

---

## 7. Ce qu'on prend au concurrent

| Ce qu'on prend | La forme chez nous | ADR à écrire ou modifier |
|---|---|---|
| La garantie que la validation tourne (son `ValidationBehaviour`) | un test d'architecture : chaque `AbstractValidator<T>` est injecté dans un use case | amender 0003 (la garantie, pas le behaviour) |
| Les tests navigateur (Playwright, scénario de connexion) | un smoke Playwright TypeScript par starter web dans `Src/Web`, joué par la jambe web | amender 0020 |
| L'orchestration Aspire | une option `--orchestration aspire` à côté de Compose | nouvelle ADR (réserve de l'ADR 0001) ; roadmap #122 |
| Des dossiers de projet courts | dossiers `Domain`, `Application`… avec `RootNamespace`/`AssemblyName` complets, contre les chemins à 176 caractères et `NU5123` | nouvelle ADR ; issue #166 |
| Des règles de nommage plus complètes | son jeu de règles `.editorconfig`, à sévérité `warning` | aucune |
| Une boucle courte | une tâche `check` qui prédit `verify` en moins d'une minute (format, architecture, unitaires touchés) | aucune |
| La vitrine | description, topics et homepage du dépôt, badges, page nuget.org autonome (README du paquet), Discussions, `CODE_OF_CONDUCT.md`, annonces de release | aucune |
| La rubrique « When this decision does not apply » de ses ADR | une section facultative du gabarit d'ADR | aucune |
| Une recette de coffre de secrets | une page de doc (Key Vault, Secrets Manager, variables injectées), sans dépendance de fournisseur | aucune |

## 8. Ce qu'on refuse de lui prendre

| Refusé | Pourquoi |
|---|---|
| MediatR et les pipeline behaviours | ADR 0003 ; licence commerciale de MediatR 14 au-delà d'un seuil de revenu, que ses utilisateurs découvrent ([discussion #1346](https://github.com/jasontaylordev/CleanArchitecture/discussions/1346), [#1413](https://github.com/jasontaylordev/CleanArchitecture/discussions/1413)) ; ordre des behaviours et exceptions de validation discutés dans ses issues |
| AutoMapper | même licence ; mapping à la main vérifié par le compilateur |
| `IApplicationDbContext` et les `DbSet` exposés à l'application | ADR 0003 et 0005 : aucun `IQueryable` dans un port |
| Les exceptions comme flux de contrôle | `Result` à codes stables, table testée |
| Les événements publiés avant le commit | ADR 0006 ; chez lui un mail peut partir pour une transaction annulée, et son ADR-003 dit le contraire de son code |
| Les entités à setters publics et id `int` | invariants et ids Guid v7 tenus par `DomainModelTests` |
| `AllowAnyOrigin`, OpenAPI et Scalar en production | default-deny et surface anonyme fermée |
| La fusion automatique des mises à jour par Renovate et la matrice jouée après fusion | `main` rouge 11 fois sur 50 chez lui ; ADR template 0010 |
| Une doc hors du projet, des ADR dans un wiki | ADR template 0002 ; son lien vers le wiki est mort |
| Les fronts copiés dans le template | ADR template 0008 : les CLI officielles à version exacte |
| La publication en rafale (15 releases en un mois, puis 221 jours sans) | cadence portée par release-please, une fois réparé |

## 9. Où on peut le dépasser

**Ce qu'il ne fait pas, ou fait mal, prouvé par ses propres issues et discussions.**

| Chez lui | Ce qu'on fait déjà | Ce qu'on peut faire de plus |
|---|---|---|
| La version publiée ne compile plus ([#1675](https://github.com/jasontaylordev/CleanArchitecture/issues/1675)), 221 jours sans release alors que `main` a 58 commits | release-please, Trusted Publishing, paquet construit avant push | un workflow planifié qui installe le paquet publié et génère le projet par défaut chaque semaine, pour ne jamais vivre son #1675 (section 10) |
| Pas de mise à niveau d'un projet existant ([discussion #1475](https://github.com/jasontaylordev/CleanArchitecture/discussions/1475)) | `cleanarch.json` enregistre la génération et chaque ajout (sur `develop`) | `dotnet cleanarch upgrade --report` : ce qui a changé entre la version du projet et la dernière, à partir de la provenance |
| Pas d'exemples optionnels ([#1192](https://github.com/jasontaylordev/CleanArchitecture/issues/1192), [#1613](https://github.com/jasontaylordev/CleanArchitecture/issues/1613)) | `--examples none` | — |
| Pas de Vue ([#1570](https://github.com/jasontaylordev/CleanArchitecture/issues/1570)), pas de Blazor ([#1444](https://github.com/jasontaylordev/CleanArchitecture/issues/1444)) | `vite-vue`, `blazor-wasm`, `blazor-server` | SvelteKit ([#1625](https://github.com/jasontaylordev/CleanArchitecture/issues/1625)) est déjà dans notre roadmap (#119) |
| Pas d'instructions pour agents ([#1632](https://github.com/jasontaylordev/CleanArchitecture/issues/1632), fermée sans réponse) | `AGENTS.md`, `--agent-files` | — |
| Aucune règle d'architecture exécutable, aucune propriété des données, pagination retirée, intégration vide | 195 tests d'architecture, ADR 0009, pagination keyset, intégration HTTP contre PostgreSQL | — |
| Aucun outil au-delà de `ca-usecase` | générateurs de feature, de use case, de retrait, launcher, item templates | un CLI qui **fait** les éditions au lieu de les imprimer (section 11, lots D1 à D6) : c'est là que l'écart deviendrait décisif |
| Pas de MySQL ([#1571](https://github.com/jasontaylordev/CleanArchitecture/issues/1571)) | refusé tant qu'EF Core 10 n'a pas de provider officiel | rien : ne pas revendiquer |

**Ce qu'on peut faire mieux que lui, au-delà de ses manques** : un premier usage qui va de bout en bout dans toutes
les combinaisons (y compris le parcours non interactif, aujourd'hui cassé par `--yes`) ; un CLI de projet qui applique
ce qu'il génère ; un gate rapide qui prédit le gate complet ; des tests navigateur générés avec chaque front.

---

## 10. Les manques pour publier

### Bloquants

| # | Manque | Preuve | Lot |
|---|---|---|---|
| B1 | **La release automatique est en panne.** release-please échoue à chaque push sur `develop` depuis le 2026-10-27/28 : `.template.config/template.json` commence par un BOM (`EF BB BF`), réintroduit par #137, que release-please lit comme un JSON invalide. Rien ne le détecte : aucun test, et `template-release.yml` est classé « none » par le sélecteur de jambes. | `head -c3 .template.config/template.json` = `efbbbf` ; les 6 derniers runs « Template release » de `develop` en échec, dont ceux de ce matin ; message `Unexpected token '﻿' … is not valid JSON` | E1 |
| B2 | **La version annoncée est fausse.** La PR de release #91 propose `1.1.0`, figée au 27 octobre ; deux commits `feat!` (#169, #172) ont cassé la compatibilité depuis la 1.0.0, la prochaine version est donc une majeure. | PR #91 « chore: release 1.1.0 », état `BEHIND` ; `git log` de `develop` | E1 |
| B3 | **`dotnet cleanarch` plante sur tout projet généré par défaut** (sur `develop` à jour), et rien ne dit comment l'obtenir. Publier `CleanArch.Cli` en l'état, c'est livrer un outil qui échoue à sa première commande. | `ProjectFile.cs:9` (`PresentationProject?`) contre `cleanarch.json` (`"presentation": [ … ]`) ; `p3-head` F1, F2 | E2 |
| B4 | **Le parcours non interactif documenté ne marche pas** : `--yes` est avalé par `dnx` dans la doc et dans la commande que le launcher imprime lui-même. C'est le premier geste d'un script ou d'une CI. | P1, section 3 | E3 |

### Importants

| # | Manque | Preuve | Lot |
|---|---|---|---|
| I1 | Même exposition que le #1675 du concurrent : TreatWarningsAsErrors avec l'audit NuGet, et aucun workflow planifié qui génère le projet depuis le paquet publié. Le jour où un avis vise une dépendance épinglée, chaque nouveau projet cesse de compiler. | `Directory.Build.props:15,21` ; seul `codeql.yml` est planifié | E4 |
| I2 | Sécurité du dépôt : 11 alertes CodeQL ouvertes et non triées (1 haute `js/polynomial-redos`, 10 `cs/log-forging` sur des gabarits structurés que la console JSON encode, donc peut-être sans risque) ; secret scanning, push protection et mises à jour de sécurité Dependabot désactivés ; aucun audit des paquets npm dans `verify` ni en CI, alors que `SECURITY.md` dit « every dependency audited ». | API GitHub `code-scanning/alerts`, `security_and_analysis` (vérifiés par l'orchestrateur) | E5 |
| I3 | Le portail Blazor Server n'a ni politique d'autorisation de repli ni en-têtes de sécurité. Il ne livre pas de page de connexion, mais toute page que l'équipe écrira pourra être encadrée par un autre site, et une page oubliée sera publique. | `.template.config/starters/blazor-server/…/Program.cs:31,76-105` | E5 |
| I4 | Les règles écrites ne sont pas toutes tenues : `#pragma` et `NoWarn` présents malgré `AGENTS.md` ; ADR 0009 contredite par le chemin agrégat (chargé par id, comparé en mémoire), et le générateur recopie la forme ; `domain.invariantViolated` répond 409 ou 400 selon le chemin, `api.md` ne dit que 409 ; rien ne vérifie qu'un validateur est consommé ; le miroir des projets de test promis par `project-layout.md` n'est pas testé. | voir section 6, axes 1 et 2 | E6, E7 |
| I5 | Chemins de fichiers jusqu'à 176 caractères (208 dans les starters) : `NU5123` à l'empaquetage, risque sous Windows sans chemins longs. | `dotnet pack` de l'audit ; issue #166 | E12 |
| I6 | Vitrine : dépôt sans description, topics ni Discussions ; notes de la release 1.0.0 en une ligne ; pas de `CODE_OF_CONDUCT.md` ; README du paquet nuget.org à vérifier ; 89 et 96 téléchargements. | API GitHub ; analystes G5 | E9 |
| I7 | Docs à corriger : `first-run.md` sans `/health` ; README racine sans `--database` ; README généré qui affirme une API même sans API ; `adding-a-setting.md:23` ambigu ; services du README riche ; roadmap qui range WPF dans « Coming » ; `SECURITY.md` qui contredit le README sur le canal de signalement. | sections 3 et 6 | E8 |

### Mineurs

`mysql` listé dans `--help` alors qu'il est refusé ; section d'installation en double dans `using-the-template.md` ;
double BOM du README du MCP généré ; bruit CRLF après le bootstrap riche ; « six features » d'authentification
alors qu'il y en a 7 avec le host MCP ; message de `UseCaseConventionTests` périmé (« explicit rather than
scanned ») ; `global.json` qui laisse monter le SDK alors que la doc parle de version exacte ; couverture mesurée en
Debug et contrôlée en Release ; `Tasks.cs <tâche> --help` qui affiche l'aide de `dotnet run`.

---

## 11. Le plan d'action

Deux temps : **égaler** (réparer ce qui bloque, tenir ce qu'on promet, rattraper le concurrent là où il est devant),
puis **dépasser** (rendre le CLI décisif). Dans chaque temps, les lots sont rangés par valeur sur coût ; à valeur
égale, premier usage, CLI et socle passent devant. Un lot est une préoccupation, donc une pull request, comme le
demande `CONTRIBUTING.md`. Tailles : S (moins d'une journée), M (un à trois jours), L (plus).

### Égaler

| Ordre | Lot | But | Périmètre et fichiers | Taille | Dépend de | Valeur pour l'utilisateur | Critère de fin |
|---|---|---|---|---|---|---|---|
| 1 | **E1. Release réparée, version juste** | pouvoir publier | retirer le BOM de `.template.config/template.json` ; un test dans `TemplatePackagingTests` qui refuse un BOM sur chaque `extra-files` de `.github/release-please/config.json` ; une règle de `.template.config/acceptance/rules.json` qui ne classe plus `template-release.yml` en « none » | S | — | une release publiable ; la PR de release annonce la majeure qu'imposent #169 et #172 | « Template release » vert sur `develop` ; la PR de release annonce 2.0.0 ; le test vu rouge avec le BOM remis |
| 2 | **E3. Parcours non interactif** | que la commande documentée marche | `docs/template/using-the-template.md`, `README.md`, le texte « Next time, skip the questions » du launcher (`Launcher/CleanArch.Create`) : `dnx CleanArch.Create -- new … --yes` ; une jambe d'acceptance qui rejoue la commande imprimée | S | — | un script ou une CI génère du premier coup | la commande imprimée par le mode guidé, rejouée sans terminal, génère le projet |
| 3 | **E2. `dotnet cleanarch` utilisable, ou retenu** | ne pas publier un outil qui plante | `Launcher/CleanArch.Cli/ProjectFile.cs` (`presentation` en liste) ; un test qui lit le `cleanarch.json` de chaque variante ; l'outil épinglé dans `.config/dotnet-tools.json` et restauré par `bootstrap` (ADR template 0017) ; une ligne dans `first-run.md`. Sinon : exclure `CleanArch.Cli` de la release | S à M | E1 | l'outil promis répond dans tout projet généré | `dotnet cleanarch` affiche l'identité du projet dans chaque jambe d'acceptance |
| 4 | **E14. Pièges silencieux du CLI** | qu'aucune commande ne se trompe sans le dire | `Tools/Tasks.cs` : `migration-add` prend `--context` et refuse d'écrire une migration vide, avec un message ; `new-feature`, `new-use-case` et `presentation-join` citent `openapi` (et `pnpm api` avec un front) ; `Tasks.cs <tâche> --help` renvoie l'aide de la tâche ; README du MCP sans double BOM | S | — | la migration Auth et l'OpenAPI ne s'oublient plus | `migration-add` sans changement de modèle échoue avec un message ; test qui interdit le double BOM dans les starters |
| 5 | **E4. Garde contre le « #1675 »** | qu'un avis de sécurité ne casse pas les projets neufs | un workflow planifié hebdomadaire qui installe le dernier paquet de nuget.org, génère le projet par défaut, lance `bootstrap` et `build` ; une ADR sur la politique d'audit NuGet du projet généré (garder l'erreur, dire comment remonter une version) ; une section de `build-and-ci.md` | M | E1 | le premier usage ne casse pas entre deux releases | le workflow est vert ; une dépendance volontairement vulnérable le fait échouer |
| 6 | **E8. Docs du premier usage** | qu'un débutant n'ait rien à deviner | `docs/getting-started/first-run.md` (`/health`, l'appel à faire), `README.md` (`--database`, liste des fronts), `.template.config/variants/project/README.md` (branche sans API, services du projet riche), `adding-a-setting.md:23`, `layers-and-projects.md:34`, `roadmap.md`, `SECURITY.md`, `using-the-template.md` (section en double), `--help` sans `mysql` | S | — | un premier usage sans surprise | un persona refait le parcours sans les frictions P3 à P9 ; `DocumentationCitationTests` vert |
| 7 | **E11. Socle indispensable** | supprimer le non métier récurrent | repli 409 sur une violation d'unicité dans `EfUnitOfWork` (+ code dans `api.md`) ; extraction d'un job générique « passe à intervalle qui appelle un use case » à partir de Reminders et Files | S à M | — | une feature n'écrit plus ces ~25 et ~170 lignes | test d'intégration : deux créations concurrentes avec le même email donnent 201 et 409 ; Reminders et Files sur la brique commune |
| 8 | **E6. Les règles écrites tenues par des tests** | que `AGENTS.md` dise vrai | tests d'architecture : ni `#pragma` ni `<NoWarn>` hors liste motivée ; chaque validateur est consommé ; chaque projet source a son projet de test miroir ; AutoMapper interdit comme MediatR ; corriger ou motiver les violations existantes (`HttpPipelineModule.cs:67`, `AppTemplate.Api.csproj:16`, `Directory.Build.props:27`) | S à M | — | une règle cassée rougit au lieu de dériver | chaque test vu rouge une fois, puis vert |
| 9 | **E7. Tenir l'ADR 0009 et le contrat d'erreur** | que le code fasse ce que les ADR disent | repository des agrégats possédés : le propriétaire dans le prédicat (`TodoListRepository.cs:24-29`, gabarit de `Tools/NewFeature.cs`) ; `domain.invariantViolated` au même statut sur tous les chemins, `api.md` à jour, test du chemin non attrapé | M | — | la propriété est garantie par la requête, comme promis | un test d'architecture sur la signature des repositories possédés ; `ErrorCodeStatusTests` couvre le chemin global |
| 10 | **E5. Sécurité du dépôt et du portail** | publier sans alerte ouverte | trier puis corriger ou justifier les 11 alertes CodeQL ; activer secret scanning, push protection et mises à jour de sécurité Dependabot (réglages du dépôt) ; `pnpm audit` dans `verify` et en CI ; politique de repli et en-têtes de sécurité sur le portail Blazor (à partir de `Presentation.Core`) | M | — | la promesse de `SECURITY.md` tenue | 0 alerte ouverte ; un test d'en-têtes sur le portail ; un paquet npm vulnérable fait échouer `verify` |
| 11 | **E10. Boucle rapide** | un retour en moins d'une minute | une tâche `check` (format, architecture, unitaires des projets touchés) ; que `dotnet build` voie IDE0005 et IDE1006 comme `verify` | S à M | — | moins de `verify` rouges (6 sur 16 chez un persona) | une erreur vue par `verify` l'est par `check` |
| 12 | **E9. Vitrine et communauté** | être trouvé et inspirer confiance | description, topics et homepage du dépôt ; Discussions avec Announcements ; `CODE_OF_CONDUCT.md` ; `PackageReadmeFile` des paquets ; notes de release écrites par release-please ; badges du README | S | E1 | la page nuget.org et GitHub se suffisent | la page nuget.org affiche le README ; la prochaine release a des notes |
| 13 | **E13. Tests navigateur** | rattraper son Playwright | un smoke Playwright TypeScript par starter web (`Src/Web`), joué par la jambe web ; amender l'ADR 0020 | M | — | chaque front est prouvé dans un vrai navigateur | la jambe web lance le smoke de chaque starter |
| 14 | **E12. Chemins courts** | lever `NU5123` et le risque Windows | dossiers de projet courts avec `RootNamespace`/`AssemblyName` complets ; nouvelle ADR ; issue #166 | L | E1 | un projet qui s'ouvre partout | chemin le plus long sous 120 caractères ; `dotnet pack` sans `NU5123` ; jambe Windows verte |

### Dépasser

| Ordre | Lot | But | Périmètre et fichiers | Taille | Dépend de | Valeur pour l'utilisateur | Critère de fin |
|---|---|---|---|---|---|---|---|
| 1 | **D1. `add feature` qui fait tout** | une feature sans édition manuelle | `dotnet cleanarch add feature` (en reprenant `Tools/NewFeature.cs`) applique les enregistrements et les listes fermées, écrit la migration, lance `openapi`, ajoute la ligne de `docs/README.md` et d'`api.md` ; `--fields "Name:string(120), Budget:decimal>=0"` génère les champs, la validation et un test d'intégration de base | L | E2, E14 | la tâche 1 passe de ~215-490 lignes à la main à la seule logique métier | le parcours de la tâche 1 se fait sans autre édition que le métier, et `verify` est vert du premier coup ; une jambe d'acceptance le joue |
| 2 | **D2. `add field`** | ajouter un champ en une commande | lignes, mapper, DTO, validateur, migration, `openapi` | L | D1 | la tâche 3 passe de 14-20 fichiers à une commande | idem, sur la tâche 3 |
| 3 | **D5. `remove feature` réel** | retirer une feature modifiée proprement | `--dry-run` par défaut et `--apply` ; retire enregistrements, docs, `.http`, client généré ; rapport sans bruit | L | E2 | la tâche 7 tombe de 11-25 minutes et 14-25 fichiers à une commande | une feature générée puis modifiée se retire avec `--apply`, `verify` vert |
| 4 | **D4. `add web-app` et `add presentation` complets** | des ajouts qui ne laissent rien à faire | toutes les listes (Compose, `.env.example`, `.dockerignore`, Dependabot, CodeQL, docs) ; `verify` lance le lint, les tests et le build du web ; le worker ajoutable après coup (revoir l'ADR template 0012) | M à L | E2 | la tâche 6 sans édition manuelle | une app et un worker ajoutés à un projet existant, `verify` vert en local comme en CI |
| 5 | **D3. `add setting` et `add event`** | les deux tâches sans outil | options, validateur, enregistrement, `appsettings`, ligne de `configuration.md` ; événement, consommateur, test ; un guide pour les domain events | M | E2 | tâches 4 et 5 en une commande | idem, sur les tâches 4 et 5 |
| 6 | **D6. `upgrade --report`** | faire ce que le concurrent ne sait pas faire (discussion #1475) | à partir de la provenance de `cleanarch.json`, lister ce qui change entre la version du projet et la dernière, par partie | L | E2 | une équipe sait ce qu'une nouvelle version lui apporte | un projet 2.0.0 obtient le rapport vers 2.1.0 |
| 7 | **D7. Socle utile en option** | sans option nouvelle | export OTLP des journaux derrière `OpenTelemetry:Enabled` ; `RetryBackoff` partagé sous `reminders \|\| files` ; puis l'AppHost Aspire (#122) | M à L | — | observabilité complète ; orchestration au choix | logs exportés en OTLP dans un test ; une jambe Aspire verte |

**Ce qui n'entre pas au plan**, par décision de l'avocat du « non » et des ADR : outbox, cron, behaviours, identifiants
typés, recherche plein texte, import/export, feature flags, multi-tenant, soft delete, HSTS, MySQL.

---

## 12. Méthode et fiabilité

### Les agents lancés

44 lancements, jamais plus de 3 agents en même temps : 1 Haiku, 24 Sonnet, 19 Opus. Environ 8 millions de tokens
consommés par les agents.

| Phase | Rôle | Agents | Modèle, effort | Pourquoi ce modèle |
|---|---|---|---|---|
| 0 | cadrage, arbitrages, synthèse, plan | l'orchestrateur | Opus | jugement et arbitrage |
| 1 | inventaire chiffré (projets, tests, releases, issues) | 1 | Haiku, bas | comptages mécaniques |
| 1 | cartographie, 2 par dépôt, sans se voir | 4 | Sonnet, élevé | lecture large et précise |
| 1 | fusion des deux cartes, chaque désaccord revérifié dans le code | 2 | Sonnet, élevé | vérification ciblée |
| 2 | personas du premier usage | 4 (+ 2 reprises, + 1 arrêtée) | Sonnet, élevé | un vrai débutant n'a pas besoin du modèle le plus fort |
| 3 | personas du CLI au quotidien | 3 (+ 1 reprise) ; + 1 sur `develop` à jour | Sonnet, élevé | idem |
| 4 | inventaire du socle | 2 | Opus et Sonnet, élevé | deux regards de force différente |
| 4 | épreuve des trois features | 2 | Opus et Sonnet, élevé | idem |
| 4 | avocat du « non » | 1 (+ 1 reprise) | Opus, élevé | plaider contre demande du jugement |
| 5 | analystes, 5 groupes de 2 axes | 10 | 5 Opus et 5 Sonnet, élevé | deux modèles différents par axe |
| 5 | juges à l'aveugle | 3 | Opus, élevé, agent sans `AGENTS.md` | ne pas savoir quel dépôt est le nôtre |
| 5 | vérificateurs adverses | 3 (+ 2 relancés après un changement de plafond) | Opus, élevé | réfuter demande le modèle le plus fort |
| 5 | avocats, un par camp | 2 | Opus, élevé | idem |

### Calibration des vérificateurs

Un leurre a été glissé, sans le signaler, dans chacun des trois lots, à une position quelconque :

| Lot | Leurre (faux) | Verdict du vérificateur | Preuve rendue |
|---|---|---|---|
| L1 (71 constats) | « aucun test d'architecture n'interdit MediatR chez nous » | **REJETÉ** | `LayerDependencyTests.cs:48`, appliqué par `TheApplicationLayer_DependsOnlyOnTheDomain` |
| L2 (83 constats) | « le concurrent a un projet de tests d'architecture NetArchTest » | **REJETÉ** | aucun projet `Architecture.Tests`, `git log -S NetArchTest` vide sur tout l'historique |
| L3 (91 constats) | « le concurrent publie ses événements après la sauvegarde, dans `SavedChangesAsync` » | **REJETÉ** | `DispatchDomainEventsInterceptor.cs:17,25` : `SavingChanges` et `SavingChangesAsync` |

**Score de calibration : 3 sur 3.** Aucun lot n'a eu à être revérifié par l'agent de réserve. Les leurres sont retirés
de tout le rapport.

### Résultat de la vérification adverse

Sur les 242 constats réels : **183 confirmés, 58 plausibles, 1 rejeté.**

- **Rejeté** : G3-C9 (une affirmation sur l'issue #1594 du concurrent et ses tests du front). Le vérificateur montre que
  son workflow `test-templates.yml` lance bien Playwright sur le projet généré.
- **Plausibles, corrigés dans ce rapport** : la doc livrée au projet compte au plus 64 pages et non 88 ;
  « six features d'auth » est juste sans le host MCP, donc mineur ; `prefer_primary_constructors = false` coupe une
  suggestion et n'interdit rien, donc retiré des défauts ; la règle des commentaires a bien son ADR (0001) ;
  l'arborescence par feature n'est décidée par aucune ADR (les ADR 0002, 0004 et 0007 ne la couvrent pas) ;
  `verify` ne lance pas le web en local mais la CI (`web.yml`) le fait ; le portail Blazor ne livre pas de formulaire
  de connexion, le risque d'encadrement vise les pages que l'équipe écrira ; la limite de révocation du jeton d'accès
  est écrite dans `SECURITY.md` (ADR 0013) ; le commit qui a remis le BOM est `428c0db2` sur `develop` ; 35 échecs de
  release depuis le 28/10 (un échec du 22/10 est une erreur transitoire de l'API GitHub) ; le concurrent a bien des
  identifiants d'amorçage, dans `src/Web/Web.http`.

### Les désaccords et leur arbitrage

| Désaccord | Position A | Position B | Ce qui a tranché |
|---|---|---|---|
| Cartes de notre dépôt | carte A | carte B | 23 désaccords revérifiés dans le code : B avait raison sur 8 points, A sur 8, les deux en partie sur les autres ; deux erreurs communes corrigées |
| Cartes du concurrent | carte A | carte B | 15 désaccords revérifiés : A juste sur les validateurs (4 pour 7 commandes) et la cadence de release, B sur les numéros de ligne du README ; un point (CS1998 sur `ca-usecase`) non reproduit avec le SDK 10.0.301 |
| La validation (axe concepts) | analyste Opus : égalité, les 56 validateurs sont tous appelés aujourd'hui | analyste Sonnet : choix assumé avec un trou, rien ne le garantit | les deux faits sont vrais ; verdict « choix assumé (ADR 0003) » et un test à écrire (lot E6) |
| L'arborescence par feature | analyste Sonnet : choix assumé (ADR 0002, 0004, 0007) | analyste Opus : aucune ADR ne la décide | le vérificateur donne raison à l'Opus ; une ADR à écrire |
| `domain.invariantViolated` | analyste Sonnet : le même code sur les deux chemins | analyste Opus : deux statuts, 409 et 400 | le juge et le vérificateur retiennent l'Opus : le code est le même, le statut diffère ; défaut (lot E7) |
| Le cron | épreuve Opus : une brique manque | avocat du « non » : choix de l'ADR 0025 | les deux : une absence réelle, assumée par écrit ; hors périmètre |
| Le port de mail à gabarit | épreuve Opus : il manque | avocat du « non » : déjà fourni par `EmailTemplate` | le vérificateur nuance : `IEmailSender` prend un HTML brut, l'épreuve a en partie raison ; reste hors du plan faute de second cas |
| Le code à écrire (concepts, architecture, conventions) | juges : le concurrent demande moins | avocat de notre camp : comparaison inégale (nos 350 lignes comptent 10 tests, les siennes 1 ; ses use cases ont deux fichiers, pas un) | le volume par use case reste plus élevé chez nous (4 fichiers contre 2) ; égalité sur une feature entière grâce au générateur ; le rapport garde « eux » pour le use case et le champ |
| Les front-ends | analystes et juge : nous | avocat du concurrent : égalité (il livre une app authentifiée qui marche et est testée dans un navigateur) | verdict maintenu, confiance abaissée à moyenne, le test navigateur compté comme un avantage net du concurrent (lot E13) |
| Le #1675 du concurrent | analystes : compté contre lui dans 4 axes | avocat du concurrent : le même mécanisme existe chez nous | compté une seule fois, comme un risque partagé ; lot E4 |
| Les `verify` rouges | persona : 6 sur 16 | avocat de notre camp : 4 imputables au template | le rapport cite les deux chiffres |
| Les alertes CodeQL | analyste : défaut | avocat de notre camp : les 10 log-forging visent des gabarits structurés encodés par la console JSON | non triées, donc ni failles démontrées ni faux positifs prouvés ; le lot E5 les trie |

### Les limites et ce qui n'a pas pu être vérifié

- **Couverture** : `coverage` plante dans l'environnement de l'audit (`PAL_SEHException` dans 13 hôtes de test sur 15,
  sous WSL2, dans le scratchpad en tmpfs, avec un dossier home isolé). Sur la même machine, `coverage` a réussi le
  28 octobre dans le vrai dépôt, et la CI la passe : ce n'est pas compté comme une friction du template.
- **La version testée** : `develop` empaqueté en 1.1.0-dev (option choisie au cadrage) ; le parcours d'un utilisateur
  qui installe aujourd'hui la 1.0.0 de nuget.org n'a pas été mesuré.
- **Variantes** : SQL Server, SQLite, `--auth external`, les hosts Avalonia et WPF et Windows n'ont pas été générés ;
  la jambe d'acceptance Windows du dépôt les couvre en partie.
- **Le concurrent** : sa configuration riche (React, PostgreSQL) a été construite, testée et lancée par le second
  persona, arrêté avant d'écrire son rapport ; son journal est en annexe. Ses tests Playwright n'ont pas tourné
  (Ubuntu 26.04 non supporté), ce qui n'est pas compté contre lui.
- **Les projets d'un persona** : `p3-ours-2` a supprimé son projet généré en nettoyant ; ses constats ont été vérifiés
  dans le code du template et dans le projet de `p3-ours-1`.
- **Le contexte des agents** : les agents lancés depuis le dépôt reçoivent son `AGENTS.md`. Les personas avaient pour
  consigne de l'ignorer ; les juges tournaient sans lui. L'anonymat des juges reste imparfait : ils reconnaissent
  « le template à 14 projets » de « celui avec MediatR », sans connaître les noms.
- **Incidents de l'audit** : le tmpfs de 16 Go s'est rempli vers 13 h (caches NuGet par persona), trois rapports ont
  été récupérés des sorties structurées et trois tâches relancées ; le classifieur de permissions a refusé deux fois de
  compiler le code du concurrent, jusqu'à ce que le propriétaire ajoute une règle d'autorisation.
- **Tout est daté** : `develop` a reçu 7 commits pendant l'audit ; les constats portent sur `5f22faa5`, sauf la
  section « `develop` à jour ».

### Les rapports bruts

Chaque agent a laissé son rapport complet, en français, dans `2026-10-30/` à côté de ce fichier : cartes (`p1-*`),
journaux des personas (`p2-*`, `p3-*`), socle (`p4-*`), analyses et dossiers anonymisés (`p5-G*`), jugements
(`p5-judge-*`), vérifications (`p5-verify-*`) et plaidoiries (`p5-advocate-*`).
