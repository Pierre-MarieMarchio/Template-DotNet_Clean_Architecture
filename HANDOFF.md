# Reprise du chantier

> **Document de travail temporaire.** Il n'appartient pas à la documentation du template —
> celle-ci vit dans `docs/`, en anglais. À supprimer quand le chantier est terminé.
>
> Dernier commit du chantier : `3b762de` · branche `chore/modernise-template`

Ce fichier existe pour qu'on puisse reprendre depuis un autre poste sans relire la conversation
qui a produit ce travail. Il contient les décisions déjà tranchées et leur raison, l'état réel du
dépôt, les pièges connus, et ce qui reste à faire dans l'ordre.

---

## 1. Ce qu'on construit

Un template .NET Clean Architecture, DDD, KISS et DRY, destiné à servir de socle à **tous** les
projets — y compris des **API critiques à fort trafic**. Deux profils d'usage cibles guident les
arbitrages :

1. **Supervision temps réel** (type connectivité d'un réseau ferroviaire) : ingestion soutenue,
   lectures très fréquentes, disponibilité non négociable, plusieurs instances derrière un
   répartiteur de charge.
2. **Pont entre un LLM et un programme applicatif** (type Codex ou Claude Code) : réponses en
   streaming, opérations longues, appelants machine sans session utilisateur, annulation fréquente
   par le client, effets de bord à ne jamais perdre.

La couche de présentation est une API HTTP. **Un projet Avalonia viendra plus tard** et appellera
les use cases en direct : tout ce qui est réutilisable doit donc vivre dans `Application`, jamais
dans `Api`.

**Critère de jugement retenu pour toute décision d'architecture :** ajouter une capacité doit
coûter *un adaptateur*, pas une modification du domaine ni des use cases.

---

## 2. Prendre la main sur un poste neuf

```bash
git checkout chore/modernise-template
dotnet build AppTemplate.sln          # doit finir à 0 erreur ET 0 avertissement
```

Les tests unitaires, projet par projet (rapides, sans dépendance externe) :

```
Tests/Application/AppTemplate.Application.UnitTests            720
Tests/Domain/AppTemplate.Domain.UnitTests                      238
Tests/Architecture/AppTemplate.Architecture.Tests               50
Tests/Infrastructure/AppTemplate.Infrastructure.Persistence.UnitTests   96
Tests/Infrastructure/AppTemplate.Infrastructure.Identity.UnitTests      26
Tests/Infrastructure/AppTemplate.Infrastructure.Email.UnitTests         50
Tests/Infrastructure/AppTemplate.Infrastructure.InMemory.UnitTests      30
Tests/Presentation/AppTemplate.Api.UnitTests                    56
Tests/Presentation/AppTemplate.Worker.UnitTests                 13
                                                        total 1279
```

`TreatWarningsAsErrors` est actif : un avertissement est une erreur.

> **Ne lance pas `Tests/Integration` sans être prévenu** — voir le piège n°1 ci-dessous.

---

## 3. Décisions déjà tranchées (ne pas rouvrir sans raison neuve)

| Décision | Raison |
|---|---|
| **Helpers explicites**, pas de chaînage fluide ni de décorateurs DI | Le flux d'un use case reste impératif et lisible, chaque étape sur une ligne. Un template se lit pour s'apprendre. |
| **Les écritures renvoient la ressource complète + ETag** | Sans ça le client doit refaire un `GET` avant chaque écriture suivante pour connaître la version. Décisif pour l'Avalonia. |
| **Projection depuis l'agrégat en mémoire après `SaveChangesAsync`** | `TodoListTracker` réécrit déjà la version attribuée par PostgreSQL. Coût zéro. Une relecture via `ITodoListQueries` serait un second SELECT hors transaction, avec une fenêtre pendant laquelle la version renvoyée serait plus récente que l'état produit — strictement pire que rien. |
| **Contrats HTTP dans le projet Api**, pas dans un projet partagé | Choix du propriétaire. Conséquence : un futur client HTTP typé devra dupliquer ou référencer l'Api. |
| **Types applicatifs nommés `…Command`**, jamais `…Request` | Libère les noms `…Request` pour les contrats HTTP et supprime les `using` ambigus. |
| **`Error.Details`** (et non `Failures`) sérialisé dans le membre `errors` de la RFC 9457 | Un seul nom, choisi une fois. |
| **Pas de pagination sur les items d'une liste** | L'agrégat est borné à `TodoList.MaxItems`, `GetDetailAsync` les rapatrie déjà tous. Un curseur sur une sous-collection bornée est de la machinerie sans bénéfice. `GET .../items` renvoie tout, avec l'ETag de la liste. |
| **`LoginOutcome` discriminé posé maintenant**, branche `TwoFactorRequired` jamais produite | Le TOTP changera la réponse de `/login`. Poser la forme après livraison casserait tous les clients. |
| **TOTP en dernière vague**, mais contrat décidé | Le TOTP protège du vol de mot de passe ; livrer ça avant la réinitialisation, c'est blinder la porte d'une maison sans clé de secours. |
| **Deuxième feature d'exemple : Reminders** | Voir §7. |
| **Pas de rate limiter Redis, pas de cache distribué, pas de bulkhead in-process** | Sur-ingénierie pour un template. La limite par instance × N se **documente**. Deux déploiements de la même image avec des règles d'ingress distinctes sont la bonne réponse au cloisonnement — décision de déploiement, pas de code. |

---

## 4. État par vague

**Fait**

- **Vague 0 — primitives partagées.** `Error.Details` avec égalité structurelle (attention : un
  dictionnaire dans un record positionnel casse l'égalité générée, `Equals` **et** `GetHashCode`
  doivent aller ensemble, sinon CS8851 est fatal). `ValidationError.From` unique,
  `Result.To<TOther>()`, `DomainGuard`, `EnsureValidAsync`, `RequireUserId`, `CommonErrors`,
  `ConcurrencyErrors`, `VersionPrecondition` remonté dans `Common/Concurrency`.
- **Auth-infrastructure.** Clés Data Protection persistées en base, port `ISecurityEventLog`, purge
  des refresh tokens avec index sur `ExpiresAt`, `RevokeAllForUserAsync` en `ExecuteUpdateAsync`.
- **Vague 1 — TodoLists.** `ITodoListAccess`, `TodoListProjection`, méthodes de domaine manquantes,
  six use cases CRUD, onze validateurs, retours `Result<Versioned<…>>`, binder de query string
  extrait.
- **Vague 1 — Auth applicative.** Renommages `…Command`, `ISecurityEventLog` câblé depuis les use
  cases, `LoginOutcome`, erreurs de rejet par champ.
- **Vague 2 — transport HTTP.** `ApiControllerBase` enrichi, `PreconditionProblems`,
  `ProblemDetailsDefaults.Normalise` appelé par les six producteurs, `ProblemTypes`,
  `InvalidModelStateResponseFactory`, OpenAPI versionné, `Vary`, mécanisme `[NoStore]`.
- **Auth fonctionnelle.** `GetCurrentUserUseCase`, `ChangePasswordUseCase`, mot de passe oublié et
  réinitialisation avec provider de jeton nommé à durée courte.
- **Worker et durcissement.** `AppTemplate.Worker`, pool de connexions borné, timeout de commande,
  purge par lots.

**Non fait** — voir §6.

---

## 5. Pièges connus

1. **Les tests d'intégration compilent mais n'ont jamais été exécutés depuis le début du chantier.**
   Leurs assertions portent encore sur les anciens codes d'erreur (`todoList.validationFailed`,
   `auth.validation`, `todoList.invariantViolated`) et sur les anciennes formes de réponse (GUID nu
   en corps, 204 sur les écritures). **Ils échoueront.** Il faut Testcontainers et Docker. C'est le
   principal angle mort.
2. **`Result<TValue>.Value` lève sur un échec.** Donc `is { IsSuccess: true, Value: var x }` évalue
   le getter *pendant* le filtrage et lève au lieu de ne pas correspondre. Toujours tester
   `IsFailure` d'abord, puis lire `.Value`.
3. **`Arg.Is<T>` de NSubstitute prend un arbre d'expression**, qui n'accepte pas le pattern
   matching (CS8122). Pour asserter sur un record, comparer directement par égalité de valeur
   plutôt que par prédicat.
4. **FluentValidation enchaîne les règles d'une même propriété même après un `NotNull()` en
   échec.** Un `Must` qui déréférence doit être précédé de `.Cascade(CascadeMode.Stop)`. Ce bug a
   déjà été trouvé une fois dans `ReplaceTodoItemTagsCommandValidator`.
5. **L'ETag d'idempotence n'est pas persisté.** Le filtre le capture et le rejoue, mais
   `IdempotencyRecord` n'a pas de colonne : il ne survit qu'à un aller-retour en mémoire, pas à un
   rejeu relu depuis PostgreSQL. Migration à faire.
6. **`PasswordReset:ResetPasswordUrl` est validé au démarrage sur les DEUX hôtes**, l'API et le
   worker, parce que chacun compose le module Identity. Absent, le conteneur refuse de démarrer.
7. **Ne jamais journaliser une adresse email en clair** sur les chemins anti-énumération
   (`resend-confirmation-email`, `forgot-password`, échec de connexion). Journaliser un identifiant.
8. **Sur un tri, la projection en mémoire doit utiliser exactement le comparateur du SQL**, sinon
   la même ressource a deux représentations selon qu'elle sort d'une écriture ou d'une lecture.

---

## 6. Ce qui reste, dans l'ordre

### Vague 2 bis — contrats HTTP et contrôleurs (prochaine étape)

C'est la plus grosse dette restante : **aucun contrat de réponse n'existe**, et deux endpoints
bindent encore des types applicatifs.

- Contrats requête **et** réponse pour chaque endpoint, sous
  `Api/Features/<Feature>/Contracts/{Requests,Responses}/`, plus un mappeur explicite par feature
  sous `Mapping/`. Mapping **manuel** : deux ADR écartent déjà les bibliothèques de mapping, et
  `TreatWarningsAsErrors` plus des records positionnels font que le compilateur attrape un champ
  ajouté.
- Ne pas mutualiser `CreateTodoListRequest` et `RenameTodoListRequest` malgré leur champ commun.
  Mutualiser en revanche `TodoItemResponse` (corps de toutes les écritures d'item) et
  `TokenResponse` entre `login` et `refresh`, qui deviennent identiques une fois le profil retiré.
- Retirer `UserId` de la réponse d'inscription et le profil de la réponse de connexion — le profil
  est sur `GET /auth/me`.
- Supprimer le **GUID nu** renvoyé par les créations et l'**entier nu** de `MaintenanceController`.
- Réécrire les six écritures TodoLists sur `ReadPrecondition`, `RequiringExistence`,
  `UpdatedOrProblem`, `CreatedOrProblem` versionné ; supprimer les helpers privés du contrôleur.
- Basculer sur `PreconditionProblems` et `ToActionResult(HttpContext)`, puis retirer
  `TodoListErrors.MalformedIfMatch` et `IfMatchRequired`.
- Poser `[NoStore]` sur `login` et `refresh` (RFC 6749 §5.1).
- `ProducesResponseType` en `ValidationProblemDetails` partout où un 400 est déclaré.
- `[HttpHead]` à côté de chaque `[HttpGet]` de ressource unitaire.
- Endpoints Auth manquants :

  | Use case | Route | Verbe | Corps | Succès |
  |---|---|---|---|---|
  | `IGetCurrentUserUseCase` | `/api/v1/auth/me` | GET | — | 200 |
  | `IChangePasswordUseCase` | `/api/v1/auth/change-password` | POST | `{ currentPassword, newPassword }` | 204 |
  | `IRequestPasswordResetUseCase` | `/api/v1/auth/forgot-password` | POST | `{ email }` | 204 toujours |
  | `IResetPasswordUseCase` | `/api/v1/auth/reset-password` | POST | `{ email, token, newPassword }` | 204 |

- Endpoint de purge des refresh tokens sur `MaintenanceController`.

### Vague 3 — CRUD exposé et tests d'intégration

- Câbler les six use cases TodoLists sans route : update d'item, reopen, ajout / retrait /
  remplacement de tags, collection d'items.
- Passer `complete` en 200 + ressource + ETag. **Ça corrige incidemment un vrai bug** : le filtre
  d'idempotence ne mémorise que les `ObjectResult` 2xx, donc une écriture idempotente répondant 204
  relâche sa clé et est rejouée pour de vrai.
- Persister l'ETag dans le store d'idempotence (piège n°5).
- **Exécuter enfin la suite d'intégration** et corriger les assertions.
- `AppTemplate.Api.http` est cassé depuis longtemps : il lit `$.id` sur des réponses qui renvoient
  un GUID nu. La vague 2 bis le corrige incidemment ; il faut aussi y ajouter les nouveaux
  endpoints.

### Vague 4 — Auth, suite

Par ordre de valeur : sessions actives (`GET /auth/sessions`, révocation unitaire,
`POST /auth/logout-all` — la table `RefreshToken` ne stocke ni IP, ni user-agent, ni date de
dernier usage, donc une liste n'est pas affichable en l'état) ; changement d'adresse email ;
cache du security stamp (voir §8) ; administration (verrouillage, rôles, recherche paginée
d'utilisateurs — toute modification de rôle **doit** faire tourner le security stamp) ; suppression
de compte ; TOTP en dernier.

### Vague 5 — production

Indispensables : bascule de la readiness sur SIGTERM et `ShutdownTimeout` configurable ; timeouts
de requête avec une politique longue ; rate limiting partitionné par appelant plus documentation de
la multiplication par N instances ; métriques runtime, Npgsql et compteur de rejets du limiteur.

Souhaitables : outbox transactionnel ; authentification machine-à-machine ; `IStreamingUseCase` et
un endpoint SSE d'exemple ; opérations longues en 202 avec ressource de suivi.

---

## 7. Deuxième feature d'exemple : Reminders

Un rappel programmé sur un item de todo list. Choisi parce qu'il exerce ce que TodoList n'exerce
pas, et parce qu'il **prouve** deux choses au lieu de les affirmer :

- **Le premier consommateur d'événement qui écrit.** Aujourd'hui `LogTodoItemCompletedConsumer` ne
  fait que journaliser, donc perdre un événement ne se voit pas — c'est précisément l'argument sur
  lequel `docs/adr/0017` refuse un outbox. Un rappel qui devrait être annulé quand l'item est
  complété, et qui sonne quand même parce que le processus est mort entre le commit et la
  publication, **ça se voit**. La condition de révision que l'ADR fixe lui-même est alors atteinte
  par le code, pas par une hypothèse.
- **Un agrégat plat, sans entité fille**, ce qui révélera dans le tracker et le mapper de TodoList
  ce qui tenait à la présence d'enfants et ce qui est réellement générique.

Il apporte aussi une requête temporelle (« les rappels échus », index sur `DueAt`, pas de page par
propriétaire) et un `BackgroundService` de déclenchement.

---

## 8. Conclusions d'audit encore non traitées

Quatre audits ont été menés. Ce qu'ils ont trouvé et qui **n'est pas encore corrigé** :

**Sécurité et échelle**

- **Une lecture en base par requête authentifiée.** `ConfigureJwtBearerOptions.OnTokenValidated`
  appelle `ValidateSecurityStampAsync` sur chaque requête portant un bearer, sans intervalle ni
  cache. Le pendant cookie d'Identity a un `ValidationInterval` de 30 minutes par défaut,
  précisément parce que ce coût est prohibitif. À 1 000 req/s c'est 1 000 SELECT/s avant qu'un seul
  endpoint métier n'ait travaillé. Correctif : cache à TTL court, invalidé aux points de rotation.
- **Rien ne borne le credential stuffing distribué.** Le verrouillage est par compte, la limitation
  de débit par IP : une attaque à un mot de passe sur cent mille comptes depuis mille IP ne
  déclenche ni l'un ni l'autre. Pistes : limite globale sur les échecs de `/login`, refus des mots
  de passe compromis, alerte.
- **Oracle temporel à la connexion.** Le hash leurre couvre l'email inconnu, mais
  `CheckPasswordSignInAsync` exécute `PreSignInCheckAsync` **avant** toute vérification de mot de
  passe : un compte verrouillé ou non confirmé répond en ~1 ms contre ~100 ms de PBKDF2. Les corps
  de réponse sont identiques et testés, le temps ne l'est pas. Correctif : vérifier le mot de passe
  même après un `PreSignInCheck` négatif, résultat ignoré.
- **Déni de service par verrouillage** : 5 échecs verrouillent 15 minutes, donc 20 requêtes/heure
  suffisent à maintenir un compte bloqué. Pistes : délai croissant, ou notification par email.
- **Aucun test ne vérifie que la rotation du security stamp invalide un access token en
  circulation.** C'est la garantie la plus subtile du système, elle est correctement implémentée,
  et personne ne l'a jamais vérifiée. **À écrire avant toute nouvelle fonctionnalité.**
- Pas de rotation de la clé de signature JWT (une seule clé, pas de `kid`, pas de fenêtre de
  recouvrement). `jti` est émis mais jamais exploité.
- Le JWT porte les types de claims XML longs (62 caractères pour `nameidentifier`), répétés à
  chaque requête.

**Architecture**

- **`ICurrentUser` suppose que tout appelant est un utilisateur identifié par un `Guid`.**
  Conséquence non documentée : `IdempotencyFilter` se désactive **silencieusement** quand `UserId`
  est nul. Pour un appelant machine — le profil LLM — l'idempotence disparaît sans bruit exactement
  pour la population qui en a le plus besoin. Correctif additif : ajouter `ClientId` et un
  `CallerId` dérivé, sans toucher aux use cases.
- **`IUseCase` et `Result` n'expriment pas un flux.** Le streaming demande un troisième contrat
  (`IStreamingUseCase`), additif lui aussi. Règle à écrire : validation et autorisation **avant**
  le premier `yield`, car une fois le premier octet parti le canal d'échec vers `ProblemDetails`
  n'existe plus.

**Qualité restante**

- Le projet de tests de l'infrastructure Identity ne compte que **26 tests**, ce qui reste mince
  pour la couche qui porte l'authentification.
- Aucune règle n'interdit qu'un type de `AppTemplate.Application` soit retourné par une action de
  contrôleur. **À ajouter en règle NetArchTest** plutôt qu'en test de forme : une règle couvre les
  endpoints qui n'existent pas encore.
- `docs/adr/0009` affirme que `/health/ready` vérifie deux `DbContext` alors qu'il n'y en a plus
  qu'un.

---

## 9. Méthode de travail

Le chantier a été mené par agents parallèles, trois au maximum, sur des **périmètres de fichiers
disjoints** — rien n'étant commité en cours de route, les worktrees git étaient inutilisables et le
seul garde-fou était la disjonction. Les collisions à surveiller sont toujours les mêmes : le
fichier `.sln`, `Directory.Packages.props`, `ServiceRegistrationTests.cs`, `appsettings.json`, et
les tests d'architecture qui comptent des choses.

Un agent qui construit pendant qu'un autre écrit rapporte des échecs qui ne sont pas les siens :
**toujours reconstruire et rejouer les tests soi-même après la dernière réception**, avant de
conclure quoi que ce soit.

Style du dépôt : commentaires rares, courts, expliquant le **pourquoi**, jamais de référence à
l'ancien code ni à une migration. Arborescence : couche, puis feature, puis responsabilité.
