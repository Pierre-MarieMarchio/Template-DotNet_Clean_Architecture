# P4 — Avocat du « non » : chaque brique proposée, plaidée contre, puis jugée

## Périmètre et méthode

- Entrées : `out/00-cadrage.md`, `out/p4-inventory-opus.md`, `out/p4-inventory-sonnet.md`,
  `out/p4-ordeal-opus.md`, `out/p4-ordeal-sonnet.md` (les trois derniers sont des sorties structurées
  récupérées après un disque plein, complètes sur le fond).
- Code relu : notre dépôt `repos/ours` au commit `5f22faa5` (« feat(tools): new-feature, new-use-case
  and remove-feature work without the API (#175) ») et le concurrent `repos/competitor-main` au commit
  `345e7fe`. Les chemins ci-dessous sont relatifs à la racine de chaque clone.
- Rien n'a été généré, compilé ni exécuté : chaque verdict repose sur la lecture du code, des ADR et
  des greps cités. Aucun build n'était nécessaire pour plaider : la question porte sur ce qui manque,
  et chaque affirmation de manque a été vérifiée dans le code.
- Règle d'arbitrage : la règle d'AGENTS.md « Extract an abstraction only once two real cases prove
  its shape » est une condition **nécessaire**, pas suffisante. Une brique survit quand deux cas réels
  existent (dans le template ou, de façon prouvée, dans les épreuves) **et** que le gain dépasse le
  coût d'entretien dans le template.

### Ce que coûte une brique de plus dans ce template (base de la plaidoirie « complexité »)

| Mesure | Valeur | Preuve |
|---|---|---|
| Paramètres du template | 22 | `python3 -I` sur `.template.config/template.json` : `parameters 22` |
| Symboles calculés | 49 | même commande : `computed 49` |
| Modificateurs de sources (exclusions conditionnelles) | 82 | même commande : `sources modifiers 82` |
| Jambes de la matrice d'acceptance | 25 | `.template.config/acceptance/matrix.json` : `legs 25` |
| Marqueurs conditionnels dans `docs`, `Src`, `Tests` | 1 444 | `grep -c "<!--#if\|#if ("` cumulé |

Chaque brique ajoutée à une partie amovible (exemples, Files, auth) doit être conditionnée, lue dans
la forme par défaut et dans `--auth none --examples none --files false` (AGENTS.md, « Working on the
template »), et passer dans les 25 jambes. Une nouvelle option multiplie ce coût.

### Les trois classes

- **Indispensable** : chaque projet qui touche le cas la réécrirait ; deux cas réels sont montrés.
- **Utile en option** : vaut la peine derrière une option du template ou un réglage existant, ou
  conditionnée par un symbole existant.
- **Hors périmètre** : refusée par une ADR, déjà fournie par le framework ou une bibliothèque, ou dont
  le gain ne paie pas le coût ; la raison est donnée.

---

## A. Briques proposées par les épreuves (écrire une feature)

### A1. Course d'unicité traduite en 409 (épreuve opus `F1-unique-500`, épreuve sonnet `F02`)

**Le constat tient.** `EfUnitOfWork` ne traduit que `DbUpdateConcurrencyException`
(`Src/Infrastructure/AppTemplate.Infrastructure.Core/Common/Saving/EfUnitOfWork.cs:15-20`).
`GlobalExceptionHandler` n'a pas de branche `DbUpdateException` : elle tombe dans `_ => 500
server.unexpected` (`Src/Presentation/AppTemplate.Api/Common/Errors/GlobalExceptionHandler.cs:42-46`).

**Plaidoirie contre.**
- L'abstraction utile existe déjà : `DatabaseProvider.IsUniqueViolation`
  (`Src/Infrastructure/AppTemplate.Infrastructure.Core/Common/Contexts/DatabaseProvider.cs:98-103`).
- Les cas réels ne veulent **pas** un 409 générique. Chacun transforme la course en résultat métier
  propre : `AccountsService` renvoie `AlreadyRegistered`
  (`Src/Infrastructure/AppTemplate.Infrastructure.Auth/Features/Accounts/Adapters/Services/AccountsService.cs:63-67`),
  `IdempotencyStore` relit la ligne gagnante (`.../Persistence/Common/Idempotency/IdempotencyStore.cs:47-52`),
  et `ExternalIdentityStore` répond `false` (`.../Features/ExternalIdentity/Access/Stores/ExternalIdentityStore.cs:59-66`).
  Un 409 générique masquerait la contrainte en cause et donnerait un code que le client ne peut pas
  exploiter.
- La bibliothèque `EntityFrameworkCore.Exceptions` (paquets `.PostgreSQL` et `.SqlServer`) fournit déjà
  `UniqueConstraintException`.
- Le concurrent ne fait pas mieux : `grep -rEl "DbUpdateException|UniqueConstraint|23505" src` renvoie 0.

**Verdict : survit, en petit.** La plaidoirie établit qu'un projet ne doit pas recevoir de 409
générique *à la place* d'un traitement au niveau du store. Elle ne justifie pas le défaut actuel : une
course **prévisible**, causée par le client, répond 500 sans code exploitable. Cela va contre la
promesse 4 du cadrage et contre `docs/architecture/http-api.md:44-46` (« a stable, dotted `code` …
One situation always answers the same status and code »). Les deux cas réels sont
`AccountsService.cs:63` et `ExternalIdentityStore.cs:59` : la course existe dès qu'une feature a un
index unique métier, et les deux épreuves sont tombées dessus indépendamment avec Contact. La brique
minimale tient en 15 à 20 lignes, sans option :
- dans `EfUnitOfWork`, `DbUpdateException when IsUniqueViolation` est levée comme une exception
  applicative typée, sur le modèle de `ConcurrencyConflictException` ;
- `GlobalExceptionHandler` la traduit en 409 avec un code stable ;
- un paragraphe de `feature-shapes.md` dit d'attraper la course au store quand la feature veut un code
  à elle.

**Classe : indispensable.**

### A2. Lignes de tags partagées (épreuve opus `F1-tags`, épreuve sonnet `F01`)

**Le constat tient.** Les deux trios sont identiques au nom près : `diff` entre
`Features/TodoLists/Rows/Models/TodoItemTagRecord.cs` et `Features/Files/Rows/Models/StoredFileTagRecord.cs`
ne montre que le namespace, le nom de classe et le nom de la clé étrangère. Les configurations ne
diffèrent que par la table et le schéma.

**Plaidoirie contre.**
- Le gain est d'environ 30 lignes par feature taguée : record de 8 lignes et configuration de 23.
- La requête, elle, **diffère** : `TodoItemTagQueries` traverse `list.Items` puis `item.Tags` alors que
  `StoredFileTagQueries` lit `file.Tags` directement (même `diff`). Elle resterait donc écrite à la main.
- EF exige un type CLR fermé par table, et l'ADR 0007 un schéma par feature. Il faudrait donc une base
  générique de record et de configuration dans un `Common/` de la persistance. Ce `Common/` nommerait
  le type métier `Tag` (`docs/architecture/modules-and-boundaries.md:90-93`) et devrait être conditionné
  par `tagging` comme le sont déjà le domaine et l'application (`.template.config/template.json:1460-1464`).
- Les deux cas sont des exemples amovibles. Avec `--examples none --files false`, la base générique
  n'aurait plus aucun utilisateur.
- Le concurrent n'a pas de tags (grep `Tag` : seulement `WithTags` d'OpenAPI).

**Verdict : ne survit pas.** La règle des deux cas est remplie, mais 30 lignes évidentes coûtent moins
qu'une base générique d'EF, sa condition et son test.

**Classe : hors périmètre** (gain inférieur au coût).

### A3. Annuaire d'adresses des propriétaires (épreuve opus `F2-recipient`, épreuve sonnet `F07`)

**Plaidoirie contre.**
- Il y a **un** cas réel : Reminders. Il fait 49 lignes au total, dont un port de 8 lignes et deux
  adaptateurs de 15 et 23 lignes
  (`find Src -path '*Reminder*Recipient*'`, `Src/Presentation/AppTemplate.Api/Features/Reminders/Composition/*.cs`).
- L'auth envoie ses mails depuis sa propre adresse de compte (`AccountEmailFactory`) : ce n'est pas un
  second cas.
- La règle des ports nommés par intention (`docs/architecture/ports-and-adapters.md:7-10`) et l'ADR 0004
  (« The port belongs to the feature that owns the data ») placent ce port dans la feature qui en a
  besoin.
- La « Commande » des épreuves est hypothétique ; elle ne compte pas comme cas réel.

**Verdict : ne survit pas aujourd'hui.** Elle deviendra légitime le jour où une seconde feature écrira
à son propriétaire. `IUserAccountDirectory` (`Src/Application/AppTemplate.Application.Core/Common/Ports/IUserAccountDirectory.cs`)
recevrait alors une méthode `FindContactAsync`.

**Classe : hors périmètre** (un seul cas réel).

### A4. Port applicatif de mail à gabarit (épreuve opus `F2-mailport`)

**Plaidoirie contre.** La brique existe déjà, au bon niveau. `EmailTemplate`
(`Src/Infrastructure/AppTemplate.Infrastructure.Core/Common/Templating/EmailTemplate.cs`) a deux
utilisateurs réels : `EmailReminderNotifier.cs:11-29` et `AccountEmailFactory.cs`
(`grep -rln EmailTemplate Src`). Ce qu'il reste à écrire par notification, environ 30 lignes
d'adaptateur, est le prix voulu du port par intention (`IReminderNotifier`). Un `ITemplatedMailer` en
Application ferait remonter le rendu HTML et les gabarits dans la couche application.

**Verdict : ne survit pas.** L'épreuve sonnet (`F08` : « IEmailSender, EmailTemplate (130 l.),
transports SMTP/Postmark et Mailpit sont fournis ») a raison contre l'épreuve opus.

**Classe : hors périmètre** (déjà fournie).

### A5. Outbox, ou garantie d'envoi d'un mail (épreuve opus `F2-outbox`, épreuve sonnet `F08`, inventaire sonnet `B20`)

**Plaidoirie contre.** L'ADR 0006 la refuse explicitement : « no outbox and no retry … A sweep is the
guarantee » (`docs/decisions/0006-events-after-commit-without-an-outbox.md:14-29`). Elle est aussi
listée dans `docs/architecture/README.md:89`. Le cas « Revisit when » de l'ADR (« a message to another
system that must leave exactly once ») ne couvre pas un mail de confirmation, qu'un balayage peut
re-dériver. Le concurrent n'en a pas (grep `Outbox` : 0). Une outbox ajouterait une table, un relais
et une déduplication, et devrait être portée sur les trois fournisseurs de base.

**Classe : hors périmètre** (ADR 0006).

### A6. Planification calendaire, de type cron ou « chaque nuit à 2 h » (épreuve opus `F3-schedule`, épreuve sonnet `F12`)

**Plaidoirie contre.** L'ADR 0025 a pesé et écarté « A scheduler such as Quartz.NET or Hangfire.
Calendars, retries and a dashboard that interval passes do not need »
(`docs/decisions/0025-jobs-run-in-a-worker-under-a-lease.md`, section *Alternatives considered*). Son
« Revisit when » et celui de l'ADR 0011 (`docs/decisions/0011-jobs-run-in-the-api-under-a-lease.md:62-63`)
nomment justement « scheduling beyond intervals (calendars …) » comme déclencheur de révision.
Aucun cas réel n'existe dans le template, et le concurrent n'a rien (grep `Cron|Quartz|Hangfire` : 0).
Une passe horaire avec une garde « déjà fait aujourd'hui » coûte environ 25 lignes (épreuve sonnet
`F12`). La plus petite brique tierce possible serait l'analyseur `Cronos` branché dans `PeriodicJob`,
mais c'est une dépendance pour un cas hypothétique.

**Classe : hors périmètre** (ADR 0025 et 0011, aucun cas réel). L'épreuve opus le classe à tort comme
« manque/important ».

### A7. Job générique « une passe à intervalle propre qui appelle un use case » (épreuve opus `F3-jobbrick`, épreuve sonnet `F11`)

**Plaidoirie contre.**
- Le cas courant est déjà générique. `AddMaintenanceTask<TUseCase>`
  (`Src/Presentation/AppTemplate.Api/Features/Maintenance/Jobs/MaintenanceJobsExtensions.cs:34-50`) a
  quatre appelants dans trois features (`grep -rn AddMaintenanceTask` : Maintenance, Auth ×2,
  BackgroundJobs).
- Il **est** documenté : `docs/features/background-jobs.md`, section « Add a job » (lignes 179-189).
  Sa visibilité `internal` ne gêne pas, puisque le job d'une feature vit dans le même assembly hôte.
- Les instruments d'une feature (par exemple `ReminderInstruments.Notified`) et ses mots dans les
  journaux sont propres à chaque job : un générique les aplatirait.

**Verdict : survit.** Les deux cas réels sont dans l'arbre et identiques au nom près :
`ReminderBackgroundService.RunIterationAsync`
(`Src/Presentation/AppTemplate.Api/Features/Reminders/Jobs/ReminderBackgroundService.cs:35-90`) et
`FileBackgroundService.RunPassAsync<TUseCase>` (`.../Features/Files/Jobs/FileBackgroundService.cs:102-150`).
Les deux font la même chose :
- ouvrir une portée de fond ;
- appeler un `IUseCase<Result<int>>` ;
- compter `iterations` par `outcome` (succès, échec, exception, désactivé) ;
- tracer, journaliser, puis relancer `OperationCanceledException`.

`FileBackgroundService` est déjà générique dans `TUseCase`. `MaintenanceBackgroundService` est un
troisième cas. On extrait environ 50 lignes vers `Presentation.Core/Common/Jobs` : un projet qui ajoute
une passe à son intervalle ne copie plus ces 170 lignes, et le compteur et l'instrument restent
nommés par la feature. Il n'y a ni option ni condition, car la Maintenance est toujours présente.

**Classe : indispensable** (extraction interne, trois cas réels).

### A8. Projet d'infrastructure pour un adaptateur tiers, ou outil `NewModule` (épreuve opus `F3-integration-home`, épreuve sonnet `F16`)

**Plaidoirie contre.** L'ajout d'un projet est rare : une fois par capacité, quand une feature en
ajoute à chaque sprint. La règle « one project per capability »
(`docs/architecture/modules-and-boundaries.md:7-13`, `docs/architecture/README.md:96`) est voulue. La
procédure existe déjà :
- `docs/reference/project-layout.md:302-310` ;
- la ligne « A new project » de la table des listes fermées (`docs/guides/adding-a-feature.md:196`).

Un générateur de module devrait connaître chaque hôte, chaque fournisseur et le Dockerfile, et passer
dans les 25 jambes. La feuille de route place les ajouts dans `dotnet cleanarch add …` sur des item
templates (`docs/template/decisions/0019-the-launcher-creates-and-the-project-tool-adds.md:21-22`),
pas sur un générateur de module. Le concurrent n'a rien de tel.

**Verdict : ne survit pas comme outil.** Une page « ajouter un module » plus détaillée que huit lignes
serait un gain de documentation, pas une brique.

**Classe : hors périmètre.**

### A9. Canal d'alerte vers l'exploitation (épreuve opus `F3-alert`, épreuve sonnet `F17`)

**Plaidoirie contre.** L'alerte relève de l'exploitation : une métrique OTLP plus une règle dans
Prometheus, Grafana ou le fournisseur. `docs/features/observability.md` invite déjà à alerter sur une
métrique, et le patron `ReminderInstruments` donne un compteur en 5 lignes (épreuve sonnet `F17`). Un
mail à une adresse de réglage reviendrait à réécrire un gestionnaire d'alertes sans déduplication ni
silence. Le concurrent n'a rien non plus.

**Classe : hors périmètre.**

### A10. Aide à la réconciliation parent/enfants dans les mappers (épreuve sonnet `F05`)

**Le constat tient.** Le même motif apparaît trois fois : « indexer les lignes par clé, mettre à jour
celles qui correspondent, ajouter les nouvelles, retirer les orphelines ». On le trouve dans
`TodoListMapper.ReconcileItems` et `ReconcileTags`
(`Src/Infrastructure/AppTemplate.Infrastructure.Persistence/Features/TodoLists/Rows/Mapping/TodoListMapper.cs:68-120`)
et dans `StoredFileMapper.ReconcileTags` (`.../Features/Files/Rows/Mapping/StoredFileMapper.cs:72-88`).

**Plaidoirie contre.**
- Chaque boucle fait 15 à 20 lignes lisibles. Un générique à cinq délégués (clé de ligne, clé de
  domaine, création, mise à jour, indicateur de changement de structure) serait aussi long à
  l'appel et moins lisible.
- `TodoListMapper` récurse dans les tags d'un item et calcule `structureChanged`, ce que
  `StoredFileMapper` ne fait pas : les formes divergent déjà.
- Les ~330 lignes avancées par l'épreuve sonnet sont surtout le record, la configuration, le mapper et
  le tracker, propres à chaque agrégat. C'est le prix assumé de l'ADR 0005 (domaine non mappé par EF).
- `AggregateTracker` (124 lignes) est déjà la partie générique.
- Le concurrent n'a pas ce besoin, puisqu'EF mappe son domaine.

**Classe : hors périmètre** (gain inférieur au coût, conséquence de l'ADR 0005).

### A11. Port d'écriture entre features, par exemple réserver le stock (épreuve sonnet `F06`)

**Plaidoirie contre.** Ce n'est pas une brique : l'ADR 0004 ne limite pas les ports à la lecture. Elle
dit « A feature uses another only through that feature's public application port »
(`docs/decisions/0004-features-meet-through-ports-and-events.md:14-17`). Avec un seul `AppDbContext`, un
port d'écriture appelé dans le use case est déjà atomique (épreuve opus `F2-spared`). Il manque
seulement un exemple : `docs/architecture/modules-and-boundaries.md:63-67` ne montre qu'un port de
lecture.

**Classe : hors périmètre** comme brique. Une phrase de documentation suffit (« un port peut écrire ;
dans le même contexte, il commit avec l'appelant »).

### A12. `RetrySchedule`, recul exponentiel persisté (épreuve sonnet `F15`)

**Le constat tient.** Le même algorithme apparaît deux fois : un premier délai d'une minute, doublé,
plafonné à une heure. On le trouve dans `Reminder.RetryDelayAfter`
(`Src/Domain/AppTemplate.Domain/Features/Reminders/Model/Entities/Reminder.cs:11-13,131-142`) et dans
`StoredFile.InspectionRetryDelayAfter` (`.../Features/Files/Model/Entities/StoredFile.cs:14-16,188-199`),
avec les mêmes contrôles de réhydratation (`Reminder.cs:88-93`, `StoredFile.cs:100-105`).

**Plaidoirie contre.**
- Chaque copie fait 12 lignes.
- Polly ne sert pas ici : il gère des reprises en mémoire, pas un compteur persisté.
- Les deux cas sont des exemples amovibles. Placée dans `Domain.Core`, qui est toujours présent, la
  brique resterait orpheline avec `--examples none --files false`, ce que contredit le principe « une
  option retire » (template ADR 0001).
- Le concurrent n'a rien (grep `Retry|Backoff` : 0).

**Verdict : survit faiblement.** La règle des deux cas est remplie. Un objet-valeur `RetryBackoff` de
dix lignes, conditionné par `reminders || files` comme l'est `tagging`, retire une duplication réelle
et ses tests en double. La priorité est basse.

**Classe : utile en option** (conditionnée par des symboles existants, sans option nouvelle).

---

## B. Briques proposées par les inventaires (partielles ou absentes)

### B1. Identifiants typés (inventaire opus, « Identifiants typés P » ; inventaire sonnet `B03`)

**Plaidoirie contre.**
- Le seul cas réel est `UserId` (`Src/Domain/AppTemplate.Domain.Core/Common/Primitives/UserId.cs`).
- Les agrégats sont des `Guid.CreateVersion7()` (`TodoList.cs:32`, `Reminder.cs:64`, `StoredFile.cs:85`).
- Typer chaque Id demanderait un convertisseur EF, un binder de route, un schéma OpenAPI et un client
  régénéré par Id. Il faudrait aussi lever la contrainte `AggregateRoot<Guid>` d'`AggregateTracker`
  (`Src/Infrastructure/AppTemplate.Infrastructure.Core/Common/Saving/Tracking/AggregateTracker.cs:10`).
- Des bibliothèques le font si une équipe le veut : `StronglyTypedId`, `Vogen`.
- Le concurrent n'a qu'un `int Id` (grep : 0).

**Classe : hors périmètre.**

### B2. Pipeline de behaviours : validation, autorisation, logs, performance (les deux inventaires)

**Plaidoirie contre.** L'ADR 0003 le refuse, et `docs/architecture/README.md:85` (« MediatR, a
dispatcher, pipeline behaviours ») aussi. Le cadrage le range parmi les choix assumés. Le
`LoggingBehaviour` du concurrent journalise `{@Request}` en entier (`LoggingBehaviour.cs:32-33`,
inventaire opus) : c'est un contre-exemple.

**Classe : hors périmètre** (ADR 0003). Voir C1 pour ce qui survit de la critique.

### B3. Seuil « requête lente » (inventaire opus, ligne Pipeline)

**Plaidoirie contre.** La durée est dans chaque ligne de requête (« responded 201 in 12.4ms »,
`docs/features/observability.md:8-12`) et dans chaque trace OTLP. Le seuil codé en dur à 500 ms du
concurrent (`PerformanceBehaviour.cs:37`) relève de la configuration du collecteur.

**Classe : hors périmètre** (déjà couvert par les traces et la ligne de journal).

### B4. Recherche plein texte (les deux inventaires, « Recherche P » et `B26`)

**Plaidoirie contre.** Le plein texte dépend du fournisseur : `tsvector` sur PostgreSQL, `CONTAINS`
sur SQL Server, FTS5 sur SQLite. Il faudrait trois implémentations, et le template porte un fournisseur
par projet parmi trois (template ADR 0005). La sous-chaîne `LIKE` échappée et bornée suffit à une liste
possédée (`SearchTerm.cs`). Aucun cas réel n'existe, et le concurrent n'a rien.

**Classe : hors périmètre.**

### B5. Import et export (inventaire opus)

**Plaidoirie contre.** Aucun cas réel. Le format (CSV, XLSX) est un choix métier, la brique tiendrait
en un appel à `CsvHelper`, et le concurrent n'a rien.

**Classe : hors périmètre.**

### B6. Coffre de secrets, par exemple Azure Key Vault (inventaire opus « Secrets P », inventaire sonnet `B29`)

**Plaidoirie contre.** Le template est neutre vis-à-vis du nuage : S3, Postmark, ClamAV. La
configuration lit les variables d'environnement, et la plateforme (secrets Kubernetes, Container Apps,
ECS) les injecte (`docs/guides/deployment.md:92-106`, « Take secrets from a secret manager »). Le
branchement du concurrent est un `if` sur une variable, propre à Azure
(`src/Web/DependencyInjection.cs:39-48`). Le reproduire supposerait un fournisseur par nuage dans les
options.

**Classe : hors périmètre.**

### B7. Export OTLP des journaux (inventaire opus « Observabilité P », inventaire sonnet `B30`)

**Le constat tient.** `Program.cs:45-47` fait `ClearProviders` puis `AddJsonConsole`, et
`docs/features/observability.md:5-17` ne décrit que la console. Le concurrent a
`Logging.AddOpenTelemetry` (`src/ServiceDefaults/Extensions.cs:46-50`).

**Plaidoirie contre.** Les journaux sur stdout sont la norme des douze facteurs : la plateforme les
collecte, et le récepteur `filelog` du collecteur OpenTelemetry les relit. Exporter aussi en OTLP
double le volume si les deux restent actifs.

**Verdict : survit.** Le paquet est déjà référencé (`OpenTelemetry.Extensions.Hosting` 1.20.1,
`Directory.Packages.props:104`), et le réglage `OpenTelemetry:Enabled` existe et est validé. La brique
tient en 5 lignes dans `Presentation.Core/Common/Observability/ObservabilityExtensions.cs`, partagée
par les deux hôtes (API et worker). Elle fermerait le seul trou par lequel une trace ne rejoint pas son
journal dans le même backend. Il n'y a pas de nouvelle option.

**Classe : utile en option** (derrière le réglage existant `OpenTelemetry:Enabled`).

### B8. ProblemDetails localisés (inventaire opus « Localisation P », inventaire sonnet `B39`)

**Plaidoirie contre.** Le contrat dit « A client branches on `code`, never on the prose in `detail`,
which is not part of the contract » (`docs/architecture/http-api.md:44-45`). La traduction appartient
au client, qui possède le code stable. Traduire `detail` côté serveur ferait du texte une partie du
contrat. Le concurrent n'a rien.

**Classe : hors périmètre.**

### B9. Feature flags (les deux inventaires, `B40`)

**Plaidoirie contre.** Aucun cas réel. `Microsoft.FeatureManagement.AspNetCore` se branche en une ligne
le jour où il faut. Les options du template retirent du code à la génération, ce qui est le principe
contraire d'un drapeau d'exécution (template ADR 0001). Le concurrent n'a rien (grep : 0).

**Classe : hors périmètre.**

### B10. Orchestration Aspire (inventaire opus « Orchestration locale P », inventaire sonnet `B43`)

**Plaidoirie contre.**
- Docker Compose est choisi : ADR 0001, ligne « Docker Compose for the local stack »
  (`docs/decisions/0001-adopt-the-generated-baseline.md:62`).
- Un AppHost est un projet de plus, avec ses paquets `Aspire.*` au rythme de publication rapide, et
  une jambe de plus dans la matrice.
- L'AppHost du concurrent est lié à Azure Container Apps (inventaire sonnet `B43`).

**Verdict : survit comme option déjà prévue.** La feuille de route l'inscrit
(`docs/template/roadmap.md:45-48`, #122 « an Aspire app host beside Docker Compose ») : à côté de
Compose, pas à sa place.

**Classe : utile en option** (#122).

### B11. HSTS et redirection HTTPS (inventaire opus, inventaire sonnet `B41`)

**Plaidoirie contre.** Le choix est écrit : « HTTPS redirection, HSTS | TLS terminates upstream »
(`docs/architecture/README.md:103`).

**Classe : hors périmètre** (choix documenté).

### B12. Suppression douce, multi-tenant, messagerie (les deux inventaires, `B08`, `B20`, `B21`, `B40`)

**Plaidoirie contre.** Chacun est écarté par écrit :
- suppression douce : `docs/architecture/README.md:88`, tenue par
  `StorageShapeTests.NoPersistenceRecord_CarriesADeletedFlag`
  (`Tests/Architecture/AppTemplate.Architecture.Tests/Rules/StorageShapeTests.cs:21`) ;
- multi-tenant : `README.md:106,111` ;
- outbox et bus : `README.md:89` et ADR 0006.

**Classe : hors périmètre.**

### B13. Classe de base `ValueObject` (inventaire sonnet `B02`)

**Plaidoirie contre.** Un `sealed record` donne l'égalité structurelle sans base. L'immuabilité est
tenue par `DomainModelTests.ValueObjectsAndDomainEvents_AreImmutable` (`DomainModelTests.cs:110`), et
le constructeur non public par `CollectionContractTests.EveryRecord_WithAValidatingFactory_HasNoPublicConstructor`
(`CollectionContractTests.cs:166`). La base du concurrent (`src/Domain/Common/ValueObject.cs`) recrée
ce que le langage offre depuis C# 9.

**Classe : hors périmètre.**

### B14. `TimeProvider` à la place d'`IDateTimeProvider` (inventaire sonnet `B09`)

**Plaidoirie contre le changement.**
- Le port maison est plus étroit : `UtcNow` seul (`IDateTimeProvider.cs`).
- Il est consommé par 46 fichiers (`grep -rln IDateTimeProvider Src | wc -l`).
- Il a un double fixe qui refuse de reculer (`FixedDateTimeProvider.cs`). `FakeTimeProvider`
  (`Microsoft.Extensions.TimeProvider.Testing`) offre l'équivalent, mais la migration n'apporte aucune
  capacité.

Concession : aucune page ne dit pourquoi le template n'utilise pas la BCL. Le grep
`(?<![Dd]ate)TimeProvider` sur `docs` est vide, et `docs/architecture/shared-mechanisms.md:75-81`
décrit l'horloge sans comparer avec `TimeProvider`.

**Classe : hors périmètre** comme brique. Une phrase dans `shared-mechanisms.md` est à ajouter.

### B15. Cache L2 partagé et output caching (inventaire opus, « Cache, limité par choix »)

**Plaidoirie contre.**
- L'output caching est écarté par écrit (`docs/architecture/README.md:94`), avec la raison : une
  réponse lue derrière default-deny ne se partage pas (`docs/architecture/shared-mechanisms.md:34-36`).
- Le niveau L2 est « one `IDistributedCache` registration away »
  (`docs/decisions/0001-adopt-the-generated-baseline.md:55`, `shared-mechanisms.md:19-21`).

**Classe : hors périmètre** (déjà à une ligne).

### B16. Rate limiting par utilisateur, ou partagé entre instances (les deux inventaires, `B37`)

**Plaidoirie contre.** Les deux limites sont écrites et motivées :
- le partitionnement par adresse vient de ce que le limiteur passe avant l'authentification ;
- les compteurs sont en mémoire, donc par instance.

Voir `docs/architecture/request-pipeline.md:112-118`. `Microsoft.AspNetCore.RateLimiting` n'a pas de
stockage distribué. Un plafond global se règle à la passerelle. Le concurrent n'a aucun rate limiting.

**Classe : hors périmètre.**

### B17. Curseur signé (inventaire opus, « Pagination »)

**Plaidoirie contre.** Le curseur est validé champ par champ (`Cursor.cs:53-141`), et la requête reste
bornée au propriétaire (ADR 0009). Un curseur falsifié ne fait que déplacer le point de départ dans les
données du même appelant. Le signer imposerait une gestion de clé (Data Protection) pour aucun gain de
confidentialité.

**Classe : hors périmètre.**

### B18. Idempotence écrite dans la transaction métier (inventaire sonnet `B18`)

**Le constat tient.** `IdempotencyFilter` appelle `next()` (`IdempotencyFilter.cs:100`), donc le commit
métier, puis `CompleteAsync` (`:129`). Si le processus meurt entre les deux, le bail expire et un retry
reprend la clé, ce qui rejoue l'écriture.

**Plaidoirie contre.**
- L'ADR 0010 a choisi « Claims commit on their own, outside the request's unit of work »
  (`docs/decisions/0010-idempotency-key-is-a-lease-per-user.md:24-25`) et nomme ce risque dans son
  contexte (« a server that dies between claiming a key and finishing the work », `:10-11`).
- Écrire la complétion dans la même transaction coudrait le filtre HTTP à l'unité de travail de chaque
  use case, et l'auth a son propre contexte.
- Le concurrent n'a aucune idempotence.

**Classe : hors périmètre** comme brique. Les conséquences de l'ADR 0010 ne disent pas la fenêtre
résiduelle : c'est un défaut de documentation à corriger dans la même PR qu'un autre changement de
l'ADR.

### B19. Reprise d'envoi des notifications (inventaire sonnet `B24`)

**Plaidoirie contre.** « a failed send is not retried » (`docs/features/email.md`) relève de l'ADR 0006.
Reminders, le seul cas réel, porte déjà sa reprise (`Reminder.cs:127-128`).

**Classe : hors périmètre** (ADR 0006).

---

## C. Règles qu'un test devrait tenir (proposées par les inventaires)

### C1. Un use case de commande consomme son validateur (inventaire opus `D3`, inventaire sonnet `F6`)

**Le constat tient.** Aucun test d'architecture ne relie un validateur à un use case :
`grep -rn "IValidator\|AbstractValidator" Tests/Architecture` est vide. Le seul lien est
`ContainerCompositionTests`, qui résout les services sans vérifier leur usage.

**Plaidoirie contre.**
- Le choix explicite de l'ADR 0003 accepte la convention.
- Il y a 56 `AbstractValidator<` et 58 appels `EnsureValidAsync` sous `Src/Application`, et aucun
  oubli n'a été relevé.

**Verdict : survit.** AGENTS.md dit « A rule checked by hand becomes a test in `Tests/Architecture` ».
La règle se tient à faible coût : les use cases prennent leurs dépendances par constructeur primaire
(`CompleteTodoItemUseCase.cs:12-16`), et `TreatWarningsAsErrors` (`Directory.Build.props:15`) fait d'un
paramètre jamais lu une erreur CS9113. Un test qui exige que chaque `AbstractValidator<TCommand>` soit
injecté dans exactement un use case suffit donc : le compilateur garantit qu'il est lu. Il n'y a ni
option ni code de production, et les cas réels sont les 56 validateurs.

**Classe : indispensable** (test).

### C2. Interdire `#pragma` et `<NoWarn>` par test (inventaire sonnet `F2`, inventaire opus `D2`)

**Le constat tient, et l'inventaire sonnet est plus complet que l'opus.** Le grep trouve deux sites
hors code généré :
- `Src/Presentation/AppTemplate.Api/Common/Hosting/HttpPipelineModule.cs:67-69`
  (`#pragma warning disable ASP0000` autour de `BuildServiceProvider`) ;
- `Src/Presentation/AppTemplate.Api/AppTemplate.Api.csproj:16` (`<NoWarn>$(NoWarn);AV0029;AV0030</NoWarn>`).

La règle est dans AGENTS.md et dans `docs/guides/build-and-ci.md:70-72`. Le motif d'AV0029 et AV0030
n'existe plus que dans le message du commit `06d10744` (« declined, and the reason is measured … 26
schema descriptions lose text »). Aucune page de `docs` ne le porte : `grep -rn AV0029 docs` est vide.

**Plaidoirie contre.** `Tools/CheckComments.cs` contrôle déjà les directives : ses fixtures, aux lignes
1103 et 1180, exigent un commentaire après un `#pragma`. Un test de plus doublerait ce contrôle.

**Verdict : survit.** `CheckComments` vit dans le dépôt du template et ne rejoint pas un projet généré
(AGENTS.md, « a generated project keeps none of them »). Il vérifie qu'une directive est commentée, pas
qu'elle est interdite. Deux violations réelles prouvent que la règle tenue à la main fuit. Le test doit
porter une liste d'exceptions motivées, sur le modèle de `_deliberatelyUnconsumed`, dont le motif
renvoie à une page.

**Classe : indispensable** (test).

### C3. Chaque consommateur d'événement a son balayage (inventaire sonnet `F7`)

**Plaidoirie contre.**
- L'ADR 0006 réserve le balayage à « a fact whose loss would matter ». C'est un jugement qu'un test ne
  peut pas porter.
- `DomainEventTests` relie déjà les événements aux consommateurs, dans les deux sens (ADR 0006,
  Consequences).
- Une liste déclarative « consommateur → balayage » ne prouverait pas que ce balayage re-dérive le
  même effet.

**Classe : hors périmètre.** La revue reste le garde-fou que l'ADR 0006 nomme (« Reviewing a new
consumer means finding its precondition and its sweep »).

---

## D. Contradictions entre les rapports, tranchées par le code

| Sujet | Rapport A dit | Rapport B dit | Le code dit | Gagnant |
|---|---|---|---|---|
| Planification à heure fixe | épreuve opus `F3-schedule` : « manque/important » | épreuve sonnet `F12` : décision, ADR 0025 | ADR 0025, *Alternatives considered* : Quartz et Hangfire écartés ; *Revisit when* : « calendars » ; ADR 0011:62-63 de même | sonnet |
| Brique de job générique | épreuve opus : « internal et non documentée » | épreuve sonnet : « interne à l'API et réservée aux purges » | `docs/features/background-jobs.md:179-189` la documente (« Add a job ») ; quatre appelants (`grep -rn AddMaintenanceTask`) ; contrainte `IUseCase<Result<int>>` (`MaintenanceJobsExtensions.cs:39`) | sonnet |
| Port de mail à gabarit | épreuve opus `F2-mailport` : manque | épreuve sonnet `F08` : fourni | `EmailTemplate` a deux utilisateurs (`EmailReminderNotifier.cs:11`, `AccountEmailFactory.cs`) | sonnet |
| Gravité du projet de module | épreuve opus : mineur | épreuve sonnet `F16` : important | procédure écrite (`project-layout.md:302-310`, `adding-a-feature.md:196`) ; ajout rare | opus |
| `#pragma` et `NoWarn` | inventaire opus `D2` : un `#pragma` seulement | inventaire sonnet `F2` : un `#pragma` et un `NoWarn` | les deux existent (`HttpPipelineModule.cs:67`, `AppTemplate.Api.csproj:16`) | sonnet |
| Value objects | inventaire opus : C | inventaire sonnet `B02` : P | records scellés, tenus par deux règles (`DomainModelTests.cs:110`, `CollectionContractTests.cs:166`) ; une base serait redondante | opus |
| Suppression douce | inventaire opus : choix écrit dans la doc | inventaire sonnet `B08` : tenu par un test | `StorageShapeTests.cs:21` existe ; ce sont les deux à la fois | sonnet (plus complet) |
| Horloge | inventaire opus : égalité | inventaire sonnet `B09` : friction sans justification | aucune page ne cite `TimeProvider` ; le port est plus étroit ; voir B14 | les deux, en partie : égalité de fond, manque d'une phrase |
| `TimeProvider` du concurrent | inventaire sonnet : `DependencyInjection.cs:190` | inventaire opus : `:76` | `src/Infrastructure/DependencyInjection.cs:76` | opus |
| Publication des événements du concurrent | épreuve sonnet `B04` : `DispatchDomainEventsInterceptor.cs:89-93` | inventaire opus : `:17-30` | le fichier fait 50 lignes ; la publication a lieu dans `SavingChanges(Async)`, lignes 17-30 | opus (citation de sonnet invalide, fond identique) |
| Tests du concurrent | inventaire opus : 32 | inventaire sonnet : environ 34 | 32 `[Test]` + 2 `[TestCase]` = 34 lignes (`grep -rE` sur `tests`) | les deux, selon ce qu'on compte |
| Mail de la Commande | épreuve opus : brique manquante | épreuve sonnet : ~60 lignes plus gabarits | voir « Port de mail à gabarit » | sonnet |
| Port d'écriture entre features | épreuve sonnet `F06` : manque | épreuve opus `F2-spared` : commit atomique déjà épargné | l'ADR 0004:14-17 n'interdit pas l'écriture ; un seul `AppDbContext` | opus sur le fond ; sonnet a raison sur l'absence d'exemple |

## E. Défauts relevés en relisant (pas des briques)

- **Un code, deux statuts.** `domain.invariantViolated` répond 409 par `DomainGuard`
  (`docs/reference/api.md:247`, `domain-and-results.md:59`) et 400 quand la `DomainException` n'est pas
  attrapée (`GlobalExceptionHandler.cs:27-31`, `domain-and-results.md:60`). Cela contredit
  `http-api.md:45-46` (« One situation always answers the same status and code »). C'est l'inventaire
  opus `D1`, confirmé.
- **AGENTS.md contredit l'outil.** Le fichier dit que `new-use-case` « adds one operation to a rich
  feature (a simple one is promoted first) » (`AGENTS.md:143,149`). `Tools/NewUseCase.cs:288-293` refuse
  au contraire la feature simple et demande une promotion manuelle, comme le guide
  (`docs/guides/adding-a-feature.md:169-171`). C'est l'épreuve opus `DOC-usecase-promote`, confirmée.
- **Le motif d'AV0029 et AV0030 est perdu.** Il n'est plus que dans le commit `06d10744` ; voir C2.
- **La fenêtre résiduelle de l'idempotence n'est pas écrite** dans les conséquences de l'ADR 0010 ;
  voir B18.

## Tableau final

| Brique | Proposée par | Plaidoirie contre (résumé) | Survit ? | Classe | Preuve |
|---|---|---|---|---|---|
| Course d'unicité traduite en 409 | épreuves opus `F1-unique-500`, sonnet `F02` | `IsUniqueViolation` existe ; les cas réels veulent un résultat métier, pas un 409 ; `EntityFrameworkCore.Exceptions` le fait ; le concurrent n'a rien | **oui**, en petit (repli 409 au lieu de 500) | indispensable | `EfUnitOfWork.cs:15-20` ; `GlobalExceptionHandler.cs:42-46` ; cas réels `AccountsService.cs:63`, `ExternalIdentityStore.cs:59` ; `http-api.md:44-46` |
| Job générique à intervalle | épreuves opus `F3-jobbrick`, sonnet `F11` | `AddMaintenanceTask` couvre les purges, est documentée et a quatre appelants ; les instruments sont propres à chaque feature | **oui** | indispensable (extraction interne) | `ReminderBackgroundService.cs:35-90` contre `FileBackgroundService.cs:102-150` ; `background-jobs.md:179-189` |
| Test : un validateur est consommé | inventaires opus `D3`, sonnet `F6` | ADR 0003 assume la convention ; aucun oubli constaté | **oui** | indispensable (test) | grep vide dans `Tests/Architecture` ; 56 validateurs ; CS9113 et `Directory.Build.props:15` |
| Test : ni `#pragma` ni `<NoWarn>` | inventaires sonnet `F2`, opus `D2` | `CheckComments` contrôle déjà les directives | **oui**, avec une liste d'exceptions motivées | indispensable (test) | `HttpPipelineModule.cs:67`, `AppTemplate.Api.csproj:16`, `build-and-ci.md:70-72`, commit `06d10744` |
| Export OTLP des journaux | inventaires opus, sonnet `B30` | stdout est la norme ; doublon de volume | **oui** | utile en option (réglage `OpenTelemetry:Enabled` existant) | `Program.cs:45-47` ; `Directory.Packages.props:104` ; concurrent `ServiceDefaults/Extensions.cs:46-50` |
| `RetryBackoff` partagé | épreuve sonnet `F15` | 12 lignes par copie ; les deux cas sont des exemples amovibles | **oui**, faiblement | utile en option (conditionné `reminders \|\| files`) | `Reminder.cs:131-142`, `StoredFile.cs:188-199` |
| AppHost Aspire | inventaires opus, sonnet `B43` | Compose choisi (ADR 0001:62) ; churn des paquets Aspire ; une jambe de plus | **oui**, comme option déjà prévue | utile en option (#122) | `roadmap.md:45-48` ; concurrent `src/AppHost/Program.cs` |
| Lignes de tags partagées | épreuves opus `F1-tags`, sonnet `F01` | 30 lignes contre une base générique d'EF ; la requête diffère ; exemples seulement | non | hors périmètre | `diff` des deux trios ; `template.json:1460-1464` |
| Annuaire d'adresses des propriétaires | épreuves opus `F2-recipient`, sonnet `F07` | un seul cas réel (49 lignes) ; port par intention (ADR 0004) | non, pas avant un second cas | hors périmètre | `IUserAccountDirectory.cs` ; `Reminders/Composition/*.cs` |
| Port de mail à gabarit | épreuve opus `F2-mailport` | déjà fourni (`EmailTemplate`, deux utilisateurs) | non | hors périmètre | `EmailTemplate.cs` ; `EmailReminderNotifier.cs:11-29` |
| Outbox, garantie d'envoi | épreuves opus `F2-outbox`, sonnet `F08` ; inventaire `B20` | ADR 0006 ; le balayage est la garantie | non | hors périmètre | `0006-events-after-commit-without-an-outbox.md:14-29` ; `README.md:89` |
| Planification calendaire (cron) | épreuves opus `F3-schedule`, sonnet `F12` | ADR 0025 et 0011 l'écartent ; aucun cas réel ; une garde de 25 lignes suffit | non | hors périmètre | ADR 0025 *Alternatives* ; ADR 0011:62-63 |
| Outil `NewModule`, projet d'adaptateur tiers | épreuves opus `F3-integration-home`, sonnet `F16` | ajout rare ; procédure écrite ; un projet par capacité | non (au plus une page de doc) | hors périmètre | `project-layout.md:302-310` ; `adding-a-feature.md:196` ; `modules-and-boundaries.md:7-13` |
| Canal d'alerte | épreuves opus `F3-alert`, sonnet `F17` | relève de l'exploitation (métrique et gestionnaire d'alertes) | non | hors périmètre | `observability.md` ; `ReminderInstruments.cs` |
| Réconciliation parent/enfants | épreuve sonnet `F05` | boucles de 15 lignes plus lisibles qu'un générique à cinq délégués ; formes divergentes ; prix de l'ADR 0005 | non | hors périmètre | `TodoListMapper.cs:68-120` ; `StoredFileMapper.cs:72-88` |
| Port d'écriture entre features | épreuve sonnet `F06` | l'ADR 0004 le permet déjà ; un contexte, donc atomique | non (une phrase de doc) | hors périmètre | `0004-…md:14-17` ; `modules-and-boundaries.md:63-67` |
| Identifiants typés | inventaires opus, sonnet `B03` | un seul cas (`UserId`) ; convertisseur, binder et OpenAPI par Id ; `StronglyTypedId` et `Vogen` existent | non | hors périmètre | `UserId.cs` ; `AggregateTracker.cs:10` |
| Pipeline de behaviours | les deux inventaires | ADR 0003 ; README:85 | non | hors périmètre | `0003-use-cases-and-ports-are-explicit.md` ; `README.md:85` |
| Seuil de requête lente | inventaire opus | la durée est dans la ligne de journal et la trace | non | hors périmètre | `observability.md:8-12` |
| Recherche plein texte | les deux inventaires (`B26`) | dépend du fournisseur, ×3 implémentations ; aucun cas réel | non | hors périmètre | template ADR 0005 ; `SearchTerm.cs` |
| Import et export | inventaire opus | aucun cas réel ; `CsvHelper` suffit le jour venu | non | hors périmètre | grep vide des deux côtés |
| Coffre de secrets | inventaires opus, sonnet `B29` | neutre vis-à-vis du nuage ; la plateforme injecte les variables | non | hors périmètre | `deployment.md:92-106` |
| ProblemDetails localisés | inventaires opus, sonnet `B39` | le client branche sur `code`, pas sur `detail` | non | hors périmètre | `http-api.md:44-45` |
| Feature flags | les deux inventaires (`B40`) | aucun cas ; `Microsoft.FeatureManagement` en une ligne ; les options retirent à la génération | non | hors périmètre | grep vide des deux côtés |
| HSTS et redirection HTTPS | inventaires opus, sonnet `B41` | TLS terminé en amont, choix écrit | non | hors périmètre | `README.md:103` |
| Suppression douce, multi-tenant, bus | les deux inventaires | écartés par écrit, en partie tenus par un test | non | hors périmètre | `README.md:88,89,106` ; `StorageShapeTests.cs:21` |
| Base `ValueObject` | inventaire sonnet `B02` | les records font l'égalité ; deux règles tiennent la forme | non | hors périmètre | `DomainModelTests.cs:110` ; `CollectionContractTests.cs:166` |
| Remplacer par `TimeProvider` | inventaire sonnet `B09` | port plus étroit ; 46 fichiers ; aucune capacité gagnée | non (une phrase de doc) | hors périmètre | `IDateTimeProvider.cs` ; `shared-mechanisms.md:75-81` |
| Cache L2, output caching | inventaire opus | L2 à une ligne ; output caching écarté pour raison de default-deny | non | hors périmètre | ADR 0001:55 ; `README.md:94` ; `shared-mechanisms.md:34-36` |
| Rate limit par utilisateur ou partagé | les deux inventaires (`B37`) | le limiteur passe avant l'auth ; plafond global à la passerelle | non | hors périmètre | `request-pipeline.md:112-118` |
| Curseur signé | inventaire opus | validé et borné au propriétaire ; une clé à gérer pour rien | non | hors périmètre | `Cursor.cs:45-141` ; ADR 0009 |
| Idempotence dans la transaction métier | inventaire sonnet `B18` | ADR 0010 « Claims commit on their own » ; couplage du filtre à l'unité de travail | non (documenter la fenêtre) | hors périmètre | `IdempotencyFilter.cs:100,129` ; ADR 0010:10-11,24-25 |
| Reprise d'envoi des notifications | inventaire sonnet `B24` | ADR 0006 ; Reminders a déjà sa reprise | non | hors périmètre | `email.md` ; `Reminder.cs:127-128` |
| Test : un consommateur a son balayage | inventaire sonnet `F7` | « loss would matter » est un jugement ; `DomainEventTests` relie déjà événements et consommateurs | non | hors périmètre | ADR 0006, Consequences |

**Bilan.** Sur 34 briques plaidées, 4 sont indispensables, et deux d'entre elles sont des tests. 3 sont
utiles en option, sans option nouvelle : un réglage existant, des symboles existants, ou l'entrée #122
de la feuille de route. Les 27 autres sont hors périmètre : une douzaine sont écartées par une ADR ou une ligne
« deliberately absent », les autres faute de second cas réel ou parce que le gain ne paie pas le coût.
Les trois briques de code qui survivent coûtent ensemble moins de 100 lignes de production et n'ajoutent
aucune jambe d'acceptance.

## Non vérifié

- Aucun build ni test lancé : les brouillons de code proposés (unicité vers 409, job générique, test des
  validateurs, test de `#pragma`) n'ont pas été écrits ni vus échouer.
- Le comportement de CS9113 sur un paramètre de constructeur primaire capturé mais jamais lu a été
  raisonné, pas compilé dans ce dépôt.
- Le comportement de `FakeTimeProvider.SetUtcNow` vers le passé (refus) est cité de mémoire, pas
  vérifié dans le paquet.
- Les tailles de lignes « environ » des épreuves n'ont pas été remesurées, sauf celles citées avec un
  chemin.
- Les formes générées autres que la forme par défaut (SQL Server, SQLite, `--auth none`) n'ont pas été
  produites ; les conditions citées viennent de `template.json` et des marqueurs.
