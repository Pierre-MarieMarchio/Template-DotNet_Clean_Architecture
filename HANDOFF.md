# Reprise du chantier

> **Document de travail temporaire.** Il n'appartient pas à la documentation du template —
> celle-ci vit dans `docs/`, en anglais. À supprimer quand le chantier est terminé.
>
> Branche `chore/modernise-template`
>
> **Les vagues 4 et 5 sont livrées pour l'essentiel**, la refonte d'arborescence et Reminders aussi,
> et la suite passe à 1829 tests, cinq exécutions consécutives. Ce qui a été arbitré, écarté ou
> reporté est au §11 — à lire avant le §6.

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

**Docker doit tourner** avant de lancer les tests : deux projets utilisent Testcontainers. Sous WSL,
il faut activer l'intégration WSL dans les réglages de Docker Desktop, sans quoi `docker info` répond
« The command 'docker' could not be found in this WSL 2 distro ».

```
Tests/Application/AppTemplate.Application.UnitTests             804
Tests/Domain/AppTemplate.Domain.UnitTests                       280
Tests/Architecture/AppTemplate.Architecture.Tests                55
Tests/Infrastructure/AppTemplate.Infrastructure.Persistence.UnitTests   126
Tests/Infrastructure/AppTemplate.Infrastructure.Identity.UnitTests       29   ← 3 exigent Docker
Tests/Infrastructure/AppTemplate.Infrastructure.Email.UnitTests          50
Tests/Infrastructure/AppTemplate.Infrastructure.InMemory.UnitTests       30
Tests/Presentation/AppTemplate.Api.UnitTests                    148
Tests/Presentation/AppTemplate.Worker.UnitTests                  31
Tests/Integration/AppTemplate.Api.IntegrationTests              276   ← exige Docker
                                                         total 1829
```

`TreatWarningsAsErrors` est actif : un avertissement est une erreur.

**Contrairement à ce que ce document affirmait, les projets « unitaires » ne sont pas tous sans
dépendance externe.** `Identity.UnitTests.Tokens.RefreshTokenRotationTests` (3 tests) démarre un
conteneur PostgreSQL depuis une fixture de classe. Le geste correct est un projet d'intégration
distinct, pas un `[Trait]` — voir §8.

La suite d'intégration est exécutée **cinq fois de suite** avant d'être déclarée verte, à chaque
vague. Un seul passage ne prouve rien : le dernier intermittent en date n'apparaissait qu'une fois
sur cinq.

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
| **`LoginResponse.Authenticated` imbrique le couple de jetons sous `tokens`** | C'est la seule façon de partager une définition unique de `TokenResponse` avec `/refresh` : une branche ne peut hériter à la fois de `LoginResponse` et de `TokenResponse`. Coût : `$.accessToken` devient `$.tokens.accessToken`. |
| **Les DTO applicatifs gardent les champs retirés du contrat HTTP** (`RegisterResponse.UserId`, le profil de `LoginOutcome.Authenticated`) | C'est eux que l'Avalonia consommera en appelant les use cases en direct. Le retrait ne concerne que le fil. |
| **`[AllowAnonymous]` déclaré action par action sur `AuthController`, jamais sur la classe** | `IAllowAnonymous` trouvé **n'importe où** dans les métadonnées d'un endpoint court-circuite l'autorisation : un attribut de classe défait le `[Authorize]` d'une action et sert le profil du porteur du jeton à n'importe qui. `DefaultDenyAuthorizationTests` assied maintenant son **absence** de la classe. |
| **`GET /auth/me` hors de la politique de limitation d'authentification** | Une lecture de profil n'est pas une tentative de credential. Un client qui vérifie sa session au démarrage dépenserait l'allocation qui existe pour ralentir la force brute. Les neuf actions de credentials portent la politique action par action ; `me` tombe sur le limiteur global. |
| **`DELETE .../tags/{tag}` : le tag voyage dans la route, avec une limite assumée** | Un `Tag` est du texte libre. Un tag contenant `/` n'est pas adressable (`%2F` est décodé avant le routage) et se retire via `PUT .../tags`. C'est écrit dans le commentaire XML des deux actions pour que ce ne soit pas un piège silencieux. |
| **`GET .../items` renvoie `{ "items": [...] }`, pas un tableau nu** | Même raison que le refus des scalaires nus : un tableau au premier niveau ne peut jamais gagner un champ frère. |
| **`AddItemTag` répond 200, pas 201** | Ajouter un tag déjà présent est un no-op côté domaine, et il n'existe pas de `GET .../tags/{tag}` qu'un `Location` nommerait. |
| **`ApiControllerBase` nomme le type déclaré de chaque réponse porteuse de valeur** | `Ok(value)` laisse `DeclaredType` à `null` et le formateur sérialise le type runtime, ce qui supprime silencieusement le discriminant d'une hiérarchie fermée. Voir §10. |
| **`ClockSkew` à 30 secondes, ni zéro ni les 5 minutes du framework** | Zéro refuse tous les jetons en circulation au moindre recalage d'horloge en arrière ; 5 minutes maintient un jeton volé vivant bien après son expiration. Voir §10. |
| **Pas de cache du security stamp** — `docs/adr/0023` | L'invalidation « aux points de rotation » que le §8 réclamait n'est pas propageable : une éviction locale ne touche pas les N-1 autres instances, donc la garantie observable reste « au plus TTL » et l'invalidation n'ajoute rien. Pire, elle rend la révocation *instantanée en dev* (N=1) et *bornée en prod* — un mécanisme exercé là où il ne compte pas. Le chiffre du §8 était par ailleurs faux : c'est un lookup par clé primaire sur le `DbContext` de la requête, réutilisé par le change tracker. Le jour où le pool est la contrainte **mesurée** : TTL absolu, sans invalidation. |
| **`logout-all` ne fait pas tourner le security stamp** | Le porteur demande à déconnecter ses *autres* appareils. Faire tourner le stamp tuerait aussi sa session courante. Seuls les refresh tokens sont révoqués ; les access tokens vivent jusqu'à leur expiration. |
| **Les sessions actives sont repoussées, et pas pour la raison du §6** | Voir §11. `TryRotateAsync` insère une ligne neuve à chaque rotation : l'`Id` publié par `GET /auth/sessions` serait mort en 15 minutes et `DELETE .../{id}` échouerait **en silence** sur une session vivante. Il manque une colonne `SessionId` stable, pas seulement les trois colonnes d'affichage. |
| **Le limiteur global reste partitionné par adresse, pas par identité** | La clé de partition est calculée là où le limiteur tourne, et il tourne **avant** `UseAuthentication` : le principal y est toujours anonyme. Avancer l'authentification pour disposer de l'identité ferait payer à **chaque requête que le limiteur s'apprête à rejeter** une validation de bearer *et* sa lecture de security stamp (`adr/0023`) — le limiteur deviendrait un amplificateur de la charge qu'il existe pour absorber. Conséquence assumée et documentée : les appelants d'une même adresse partagent un budget. |
| **Les sondes de santé sont hors du limiteur** | Sous sidecar de maillage ou ingress en `hostNetwork`, la sonde et le trafic partagent l'adresse source donc la partition. Un pic épuisait la partition, `/health` répondait 429 et le kubelet **redémarrait** un processus simplement occupé. `ObservabilityPolicies` excluait déjà `/health` des traces et des logs ; le même raisonnement manquait au limiteur. |
| **La readiness bascule à l'arrêt, la liveness jamais** | Faire échouer la liveness pendant un arrêt propre demande à l'orchestrateur de **tuer** un processus qui termine correctement son travail. Seule la readiness répond « peut-il recevoir du trafic ». |
| **Le timeout de requête par défaut est plus long que le budget de retry de la persistance** | `CommandTimeout` 30 s × 6 tentatives ≈ 230 s au pire. Un timeout HTTP plus court annulerait routinièrement une écriture encore en train de réessayer sainement, transformant une panne transitoire récupérable en échec non observable — inacceptable pour « effets de bord à ne jamais perdre ». Si des timeouts plus courts deviennent prioritaires, le bon levier est `Database:CommandTimeoutSeconds` / `EnableRetryOnFailure`, pas ce défaut. |
| **Sur un flux, le timeout se désactive, il ne s'allonge pas** | Une valeur « très grande » finit toujours par être atteinte au pire moment, et une fois le premier octet parti il n'existe plus de canal pour répondre un `ProblemDetails` : un timeout tardif ne peut produire qu'une réponse tronquée. |

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
- **Vague 2 bis — contrats HTTP.** Contrats requête et réponse pour les 21 endpoints sous
  `Contracts/{Requests,Responses}/`, un mappeur manuel par feature sous `Mapping/`. Plus aucun
  scalaire nu (GUID des créations, entier de la purge). Les six écritures TodoLists renvoient la
  ressource + ETag ; `complete` passe en 200. Quatre endpoints Auth câblés (`me`,
  `change-password`, `forgot-password`, `reset-password`), `TokenResponse` mutualisé, `[NoStore]`
  sur les deux réponses porteuses de jeton, `[HttpHead]` sur les lectures unitaires,
  `ValidationProblemDetails` sur tous les 400. `PreconditionProblems` remplace les deux erreurs
  `If-Match` de `TodoListErrors`, supprimées.
- **Vague 3 — CRUD complet et idempotence.** Les six use cases sans route sont exposés
  (`GET .../items`, `PUT .../items/{id}`, `POST .../items/{id}/reopen`, `POST .../items/{id}/tags`,
  `PUT .../items/{id}/tags`, `DELETE .../items/{id}/tags/{tag}`) : 15 actions sur
  `TodoListsController`. L'ETag est persisté dans le store d'idempotence (colonne + migration).
  `AppTemplate.Api.http` couvre toute la surface.
- **Deux garde-fous ajoutés.** `Tests/Presentation/AppTemplate.Api.UnitTests/Conventions/ControllerContractTests.cs`
  interdit qu'une action de contrôleur retourne **ou binde** un type `AppTemplate.Application`, et
  contient un contrôleur imbriqué qui viole la règle pour prouver que le détecteur peut échouer.
  `Tests/Infrastructure/.../Migrations/PendingModelChangesTests.cs` appelle
  `Database.HasPendingModelChanges()` et échoue si le modèle a changé sans migration — sans base et
  sans `dotnet ef`. Sa sensibilité a été vérifiée en le mettant volontairement en défaut.

- **Vague 4 — Auth, ce qui a été retenu.** `CredentialInvalidation`, point d'invalidation **unique**
  appelé par `ChangePassword` et `ResetPassword` : la révocation des refresh tokens après rotation
  du stamp était une ligne recopiée, que les cinq fonctionnalités à venir auraient dû se rappeler
  d'écrire. `LogoutEverywhereUseCase` + `POST /auth/logout-all` (204, pas de politique
  `authentication`, pas de rotation de stamp). Oracle temporel soldé : les branches `LockedOut` et
  `EmailNotConfirmed` dérivent maintenant une clé, résultat ignoré — via le hasher **directement**,
  pas `CheckPasswordAsync`, qui réécrit le hash sur `SuccessRehashNeeded` et ferait tourner le stamp
  sur une connexion refusée. `docs/adr/0023` refuse le cache du security stamp.
- **Vague 4 — le trou de couverture, qui était le vrai sujet.** `change-password`, `forgot-password`
  et `reset-password` n'avaient **aucun** test d'intégration — soit exactement les deux seuls chemins
  qui font tourner le security stamp. `SecurityStampRotationTests` prouve enfin qu'une rotation tue
  un access token **déjà émis**, en assertant le motif via `X-Test-Auth-Failure` pour qu'un 401
  fortuit ne puisse pas faire passer le test. `PasswordManagementTests` couvre les trois endpoints.
  `LoginResponseContractTests` lit le **JSON brut** de `/login` et assied `status` : la garantie du
  §10 n'était jusque-là protégée que par accident, via une désérialisation qui aurait cascadé.
  L'ETag du rejeu d'idempotence est asserté (un commentaire affirmait qu'il l'était ; il ne l'était
  pas). `ApiControllerBaseTests` assied `DeclaredType`, donc un retour à `Ok(value)` rougit.

- **Vague 5 — production.** `Common/Lifecycle/` : `ShutdownHealthCheck` (readiness qui bascule sur
  `ApplicationStopping`), `ShutdownOptions` (`HostOptions.ShutdownTimeout`, 30 s par défaut, la
  valeur de `terminationGracePeriodSeconds` de Kubernetes), `RequestTimeoutsOptions` +
  `LifecyclePolicies` (politique par défaut et politique nommée `long`, avec `WriteTimeoutResponse`
  qui rend un `ProblemDetails` au même format que le reste). `DisableRateLimiting` sur les deux
  endpoints de santé. Statut 499 réellement posé sur la réponse : il ne l'était pas, donc **chaque
  annulation client était journalisée et mesurée en 500** — le taux d'erreur 5xx était faux, et il
  l'était surtout sur le profil LLM où l'annulation est normale. Métriques : `System.Runtime`,
  `Npgsql` (pool de connexions) et `Microsoft.AspNetCore.RateLimiting` — trois meters **intégrés**,
  zéro paquet nouveau, noms vérifiés à l'exécution par `MeterListener` et non supposés.
  Échantillonnage de traces configurable. Le Worker a enfin traces et métriques, et sa boucle de
  maintenance journalise chaque itération y compris à zéro suppression : une purge cassée était
  jusque-là **strictement invisible**.
- **Vague 5 — stabilité et documentation.** `RateLimiterWindow` rend la fenêtre du limiteur
  remplaçable par l'hôte de test (la fenêtre fixe avance sur l'horloge murale et n'expose aucune
  horloge injectable — `AutoReplenishment = false` **ne suffit pas**, vérifié). `DataProtectionKeys`
  est exclu de la troncature entre tests. Le ×N instances est documenté, ainsi que le partage de
  budget par adresse. Trois affirmations documentaires périmées corrigées : `adr/0009` et
  `ARCHITECTURE.md` sur les « deux DbContext », `adr/0005` sur le job de purge « laissé au lecteur »
  qui existe depuis la vague 2.

- **Vague 6 — refonte d'arborescence** (commit `b644e12`). Vocabulaire de dossiers fermé et
  identique par couche, un dossier seulement s'il a du contenu, un type public de premier niveau par
  fichier. Un dossier par cas d'usage (28 dossiers) et un dossier par port (les 28 fichiers plats
  d'`Auth/Ports` deviennent 10 dossiers). `Dtos/` d'Auth vidé — ses quatre types n'avaient chacun
  qu'un consommateur. `Collections/` et `Access/` supprimés. `Domain/…/Stores/` → `Repositories/`,
  et `Persistence/…/Mappers/` → `Mapping/`, le mot des trois couches.
  Trois règles de convention refondues pour qu'elles cessent de dépendre du rangement :
  `PortConventionTests` (son motif était ancré par `$` et n'aurait plus rien matché — un test vert
  qui ne teste rien), `CollectionContractTests` (découverte par le namespace `.Collections`,
  remplacée par « tout record déclarant une fabrique qui renvoie `Result<lui-même>` n'a pas de
  constructeur public », critère qui voyage avec le type), et surtout
  `EverythingInAUseCasesFolder_IsAUseCaseOrItsInputContract` qui **interdisait l'arborescence
  cible** : remplacé par `EveryUseCaseFolder_HoldsOneUseCase_AndIsNamedForIt`, plus mordant, dont la
  sensibilité a été prouvée en y glissant un second use case.

- **Vague 7 — Reminders** (commit `3e1a43f`). Agrégat **plat**, deuxième feature d'exemple. Ce
  qu'elle a mesuré, et qui commande le refactor du commun : tracker **−31 %**, mapper **−48 %**,
  mais repository **+23 %** (deux méthodes de plus). L'hypothèse « les repositories se ressemblent »
  est donc fausse dès le second cas, et un `AggregateRepository<>` générique ne pourrait offrir que
  la surface CRUD que l'ADR 0003 refuse — **ne pas l'extraire**. Le noyau du tracker, lui, a bien
  deux cas qui le démontrent.
  Deux instants `ClaimedAt`/`NotifiedAt` plutôt qu'un booléen : sans état intermédiaire en base,
  « réclamé mais jamais notifié » n'existe pas et la garantie est indémontrable.
  Décision de domaine à retenir : **« `DueAt` dans le futur » n'est PAS un invariant d'état.** C'est
  une précondition de commande ; l'imposer dans `Rehydrate` ferait refuser exactement les lignes que
  la requête de déclenchement cherche, et la feature s'auto-bloquerait au premier tick.
  `docs/adr/0026` amende `0017` : la correction ne dépend pas de la livraison (revérification au
  point d'effet + annulation idempotente + compteur `apptemplate.reminders.missed_cancellations`,
  qui **est** le nombre d'événements perdus). Deux défauts de rédaction de `0017` corrigés au
  passage : sa condition de révision était contournable par la solution même qu'elle déclenche, et
  elle préemptait sa propre conclusion.
  Conséquence non anticipée : **la suppression se propage sans aucun événement de domaine** — un id
  absent de la projection est un item supprimé. Les deux événements que l'analyse jugeait
  nécessaires ne l'étaient pas.

**Non fait** — voir §6 et §11.

---

## 5. Pièges connus

1. ~~Les tests d'intégration n'ont jamais été exécutés.~~ **Soldé.** 255/255, cinq fois de suite.
   Ce qu'ils ont révélé est au §10 — dont un défaut de production réel que rien d'autre n'attrapait.

   La correction la plus importante côté suite était silencieuse : `IntegrationTestBase`
   désérialisait le `RegisterResponse` et le `LoginOutcome` **applicatifs** depuis des réponses HTTP
   dont la forme avait changé. Ça ne levait pas — ça remplissait `Guid.Empty` et des jetons `null`.
   `TestUser` ne porte donc plus d'identifiant : il est lu sur `GET /auth/me`, seul endroit qui
   publie un profil, et exposé par un `TestSession` distinct.
2. **`Result<TValue>.Value` lève sur un échec.** Donc `is { IsSuccess: true, Value: var x }` évalue
   le getter *pendant* le filtrage et lève au lieu de ne pas correspondre. Toujours tester
   `IsFailure` d'abord, puis lire `.Value`.
3. **`Arg.Is<T>` de NSubstitute prend un arbre d'expression**, qui n'accepte pas le pattern
   matching (CS8122). Pour asserter sur un record, comparer directement par égalité de valeur
   plutôt que par prédicat.
4. **FluentValidation enchaîne les règles d'une même propriété même après un `NotNull()` en
   échec.** Un `Must` qui déréférence doit être précédé de `.Cascade(CascadeMode.Stop)`. Ce bug a
   déjà été trouvé une fois dans `ReplaceTodoItemTagsCommandValidator`.
5. ~~L'ETag d'idempotence n'est pas persisté.~~ **Corrigé.** Le piège était plus étroit que décrit :
   `IdempotentResponse` portait déjà un membre `ETag` et `IdempotencyFilter` le capturait et le
   rejouait déjà. Seule la colonne manquait. `IdempotencyRecord.ETag` existe désormais, avec la
   migration `20260807200740_AddIdempotencyResponseETag`. Le défaut réel était plus grave que
   « ne survit pas à un aller-retour » : c'est en production, avec plusieurs instances derrière un
   répartiteur, qu'un rejeu servi par une autre instance rendait un corps sans validateur.

   **`dotnet ef` est inutilisable sur ce poste** (`dotnet tool restore` et l'installation globale
   échouent tous deux sur `Settings file 'DotnetToolSettings.xml' was not found in the package`).
   Cette migration, son `.Designer.cs` et le `ModelSnapshot` ont donc été écrits à la main. C'est
   `PendingModelChangesTests` qui prouve qu'ils ne divergent pas du modèle — et qui protégera le
   prochain.
6. **`PasswordReset:ResetPasswordUrl` est validé au démarrage sur les DEUX hôtes**, l'API et le
   worker, parce que chacun compose le module Identity. Absent, le conteneur refuse de démarrer.
7. **Ne jamais journaliser une adresse email en clair** sur les chemins anti-énumération
   (`resend-confirmation-email`, `forgot-password`, échec de connexion). Journaliser un identifiant.
8. **Sur un tri, la projection en mémoire doit utiliser exactement le comparateur du SQL**, sinon
   la même ressource a deux représentations selon qu'elle sort d'une écriture ou d'une lecture.
9. **L'horloge injectable ne contrôle PAS la validation JWT, ni rien qui lise `TimeProvider`.**
   `ValidateLifetime` lit l'horloge machine (avec 30 s de tolérance) tandis que l'émetteur date le
   jeton depuis `IDateTimeProvider`. Donc **avancer l'horloge puis se connecter produit un jeton
   `nbf` dans le futur, refusé en `IDX10222`** — pas un jeton expiré. Pour un jeton expiré il faut
   *reculer* l'horloge (`Set` accepte le recul, `Advance` non), se connecter, puis revenir au
   présent. Et ASP.NET Identity utilisant `TimeProvider.System`, l'horloge n'a aucun effet sur la
   fin de verrouillage ni sur la durée de vie des jetons de confirmation et de réinitialisation.
10. **La fenêtre du limiteur de débit avance sur l'horloge murale et n'expose aucune horloge
    injectable.** `AutoReplenishment = false` **ne suffit pas** : le limiteur intégré rattrape le
    temps réel écoulé à chaque acquisition. C'est pour ça que la fenêtre elle-même est remplaçable
    (`RateLimiterWindow`) et que l'hôte de test l'élargit. Sans ça, un test qui dépense N permis puis
    en attend un refus échoue quand la frontière de fenêtre tombe au milieu — un intermittent de la
    forme exacte de celui qui a demandé cinq passages pour être compris.
11. **Un `using` orphelin ne casse PAS le build.** `.editorconfig:47-51` documente le choix :
    `IDE0005` n'est pas levé au build parce que cela exigerait `GenerateDocumentationFile=true`,
    donc CS1591 sur chaque membre public. Le filet est `dotnet format --verify-no-changes`, que le
    CI rejoue. Conséquence : **un build vert ne prouve pas que les `using` sont propres**, et tout
    éclatement de fichier en produit. En revanche CS1574 — un `<see cref="…"/>` non résoluble après
    un déplacement — casse bien le build.
12. **`[*.cs] charset = utf-8-bom` (`.editorconfig:18-19`).** Un fichier créé sans BOM fait échouer
    le gate de formatage. Et le test naïf `head -c3 "$f" | grep -q $'\xef\xbb\xbf'` **ne marche
    pas** : `grep` ne matche pas fiablement des octets binaires, si bien qu'un script fondé dessus
    ajoute un *second* BOM aux fichiers conformes. Normaliser en Python, en retirant les BOM
    répétés avant d'en garantir un.
13. **Ne jamais donner à un dossier le nom du type qu'il contient.** `Services/TodoListAccess/`
    contenant la classe `TodoListAccess` crée un namespace homonyme du type : la résolution de nom
    remonte les namespaces englobants et rend CS0118 possible chez les consommateurs. `Services/`
    reste donc à plat. Même raison pour `CredentialInvalidation`, rangé dans `Policies/`.
14. **`TestDatabase.PrepareAsync` tronque toutes les tables des schémas modules.**
    `identity.DataProtectionKeys` en est désormais exclu : l'y laisser était inoffensif avec un hôte
    unique, et deviendrait une bombe au premier test multi-instances (un jeton émis par un hôte
    illisible par l'autre, qu'on prendrait pour un défaut de partage d'état).

---

## 6. Ce qui reste, dans l'ordre

### Vague 4 — Auth, ce qui reste

Dans cet ordre. **Le point d'invalidation unique (`CredentialInvalidation`) existe maintenant :
tout ce qui suit doit l'appeler plutôt que recopier une révocation.**

1. **Changement d'adresse email.** Le meilleur item pédagogique restant : deux jetons, deux
   adresses, une rotation faite par le framework et une révocation faite à la main, un chemin
   anti-énumération sur la nouvelle adresse. Aucune migration.
2. **Verrouillage administratif et rôles.** Petit en code, mais **la rotation du stamp y est
   obligatoire** et n'est pas gratuite : `SetLockoutEndDateAsync`, `AddToRoleAsync` et
   `RemoveFromRoleAsync` ne rotent pas d'eux-mêmes. Sans rotation, un compte qu'on vient de
   verrouiller garde son access token vivant jusqu'à 15 minutes — ce qui vide de sens la promesse
   même du verrouillage administratif — et un rôle retiré reste porté par le jeton d'autant.
3. **Sessions actives.** Voir §11 : il faut d'abord une colonne `SessionId` stable à travers la
   rotation, recopiée par `TryRotateAsync`, avec un index `(UserId, SessionId)`. Une fois qu'elle
   existe, le reste est **dérivé** des colonnes présentes : début de session `MIN(CreatedAt)` de la
   famille, dernier usage `MAX(CreatedAt)` — inutile de stocker une date de dernier usage et surtout
   de l'écrire à chaque requête, la rotation fait déjà un INSERT. IP et user-agent : à laisser en
   point d'extension documenté, pas à livrer (voir §11).
4. **Suppression de compte.** ~20 lignes, mais ne prouve rien tant qu'on ne pose pas la seule
   question intéressante : que deviennent les `TodoList` de l'utilisateur ? Aucun agrégat métier ne
   référence l'utilisateur aujourd'hui. C'est un item **domaine**, à poser avec Reminders (§7).
5. **TOTP** en dernier, contrat déjà décidé (§3).
6. **Recherche paginée d'utilisateurs : ne pas livrer.** Elle déverserait des adresses email en
   masse dans une verticale dont chaque autre endpoint passe son temps à ne pas révéler qu'une
   adresse existe. Le template dirait neuf fois « ne révélez jamais une adresse », puis livrerait
   l'endpoint qui les liste toutes.

### Vague 5 — ce qui reste

~~**Le défaut de production le plus coûteux encore ouvert : la revendication d'idempotence n'a pas de
bail.**~~ **Soldé** (commit `24c7e48`). `IdempotencyRecord.ClaimedUntil` + `IdempotencyOptions.ClaimLease`
(15 min par défaut, soit 1,5× le plafond de `RequestTimeoutsOptions.Extended` ; validé `> 0` et
`<= Retention`). La reprise d'une revendication périmée est **atomique** : mise à jour conditionnelle
dont « zéro ligne affectée » est le signal de course perdue — même rendez-vous que
`RefreshTokenStore.TryRotateAsync` — et un test d'intégration lance deux réclamations concurrentes
contre une vraie base pour le prouver. Le bail **n'est pas** la rétention : `ExpiresAt` gouverne
combien de temps une réponse *complétée* reste rejouable, le bail seulement combien de temps une
revendication *inachevée* bloque les autres.

~~La libération de clé après une écriture déjà commitée.~~ **Soldé** au même commit.
`Response.HasStarted` distingue les deux cas. **Attention en le relisant** : c'est une heuristique,
pas une certitude — un échec de sérialisation survenant après le commit mais avant le premier octet
libère une clé dont l'écriture a bel et bien eu lieu. Le commentaire du code le dit ; c'est le bail
qui borne l'erreur, dans les deux sens.


Souhaitables, inchangés : outbox transactionnel ; authentification machine-à-machine ;
`IStreamingUseCase` et un endpoint SSE d'exemple (le mécanisme de timeout et sa politique `long`
existent maintenant pour l'accueillir) ; opérations longues en 202 avec ressource de suivi.

Un projet partagé entre les deux hôtes serait la bonne réponse à la duplication de la configuration
d'observabilité entre `Api` et `Worker` : le code est aujourd'hui écrit deux fois parce qu'aucun
projet commun de présentation n'existe.

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

- ~~Une lecture en base par requête authentifiée.~~ **Tranché, et refusé** — `docs/adr/0023`. Le
  chiffre de l'audit était faux (c'est un lookup par clé primaire sur le `DbContext` de la requête,
  réutilisé par le change tracker), et l'invalidation « aux points de rotation » qu'il proposait ne
  se propage pas entre instances. Le vrai coût est une connexion prise dans un pool borné, pas du
  CPU — et c'est maintenant mesurable, le meter `Npgsql` est exporté.
- **Rien ne borne le credential stuffing distribué.** Le verrouillage est par compte, la limitation
  de débit par IP : une attaque à un mot de passe sur cent mille comptes depuis mille IP ne
  déclenche ni l'un ni l'autre. Pistes : limite globale sur les échecs de `/login`, refus des mots
  de passe compromis, alerte.
- ~~Oracle temporel à la connexion.~~ **Corrigé**, via le hasher directement et non
  `CheckPasswordAsync` (qui réécrit le hash sur `SuccessRehashNeeded`, donc ferait tourner le stamp
  sur une connexion refusée). Un test compte les dérivations sur chaque branche — une mesure de
  temps réelle serait inutilisable en CI. **Il subsiste un écart d'E/S non soldé** : la branche
  « mot de passe faux » fait un UPDATE (`AccessFailedAsync`) que la branche « adresse inconnue » ne
  fait pas. Plus petit que PBKDF2, systématiquement du même signe, donc extractible en moyennant. Le
  rendre nul demanderait d'écrire pour un utilisateur qui n'existe pas.
- **Déni de service par verrouillage** : 5 échecs verrouillent 15 minutes, donc 20 requêtes/heure
  suffisent à maintenir un compte bloqué. Pistes : délai croissant, ou notification par email.
- ~~Aucun test ne vérifie que la rotation du security stamp invalide un access token en
  circulation.~~ **Écrit** — `SecurityStampRotationTests`. Le trou était plus large que décrit :
  les trois endpoints concernés n'avaient aucun test d'intégration.
- **Deux points de rotation manquants, à traiter avec la vague 4 restante.**
  `SetLockoutEndDateAsync`, `AddToRoleAsync` et `RemoveFromRoleAsync` ne rotent pas le stamp.
  `ConfirmEmailAsync` non plus, ce qui rend le jeton de confirmation rejouable jusqu'à son
  expiration alors que `ConfirmEmailCommand` le documente comme « single-use ».
- Pas de rotation de la clé de signature JWT (une seule clé, pas de `kid`, pas de fenêtre de
  recouvrement). `jti` est émis mais jamais exploité.
- Le JWT porte les types de claims XML longs (62 caractères pour `nameidentifier`), répétés à
  chaque requête.

**Architecture**

- ~~`ICurrentUser` suppose que tout appelant est un utilisateur identifié par un `Guid`, et
  `IdempotencyFilter` se désactive silencieusement quand `UserId` est nul.~~ **Le silence est
  corrigé** : le filtre refuse désormais explicitement (400 `idempotency.callerNotIdentifiable`) et
  journalise. La branche est morte aujourd'hui — aucun appelant ne l'atteint — donc le refus ne
  casse aucun contrat ; le jour où un appelant machine existera, son auteur résoudra le problème
  au lieu de le découvrir en production.
  **`ClientId` a été explicitement refusé** : rien ne le peuplerait tant qu'il n'y a pas
  d'authentification machine-à-machine, et un membre jamais peuplé dans une abstraction que huit use
  cases consomment ferait déduire au lecteur une capacité inexistante. À ajouter **avec** le
  mécanisme qui le remplit, pas avant.
- **`IUseCase` et `Result` n'expriment pas un flux.** Le streaming demande un troisième contrat
  (`IStreamingUseCase`), additif lui aussi. Règle à écrire : validation et autorisation **avant**
  le premier `yield`, car une fois le premier octet parti le canal d'échec vers `ProblemDetails`
  n'existe plus.

**Qualité restante**

- Le projet de tests de l'infrastructure Identity ne compte que **26 tests**, ce qui reste mince
  pour la couche qui porte l'authentification.
- ~~Aucune règle n'interdit qu'un type de `AppTemplate.Application` soit retourné par une action de
  contrôleur.~~ **Fait**, et dans les deux sens : retour **et** binding, parce qu'un `…Command` bindé
  depuis le corps rend settable de l'extérieur tout membre qu'on lui ajoute. Écrit par réflexion et
  non en NetArchTest : le sujet est le type de retour d'une méthode et ses `ProducesResponseType`,
  qu'un moteur de règles au niveau du type ne sait pas adresser. Vit dans `Api.UnitTests` plutôt que
  dans `Architecture.Tests`, qui ne référence délibérément pas `AppTemplate.Api`.
- ~~`docs/adr/0009` affirme que `/health/ready` vérifie deux `DbContext`.~~ **Corrigé**, ainsi que
  la même affirmation dans `ARCHITECTURE.md` — qui n'avait pas été repérée — et l'affirmation de
  `docs/adr/0005` selon laquelle le job de purge des refresh tokens est « laissé au lecteur » alors
  que le Worker le fait depuis la vague 2. **La leçon vaut au-delà de ces trois lignes : une
  affirmation documentaire périmée se trouve par grep, jamais par relecture.**
- **Les 3 tests Docker de `Identity.UnitTests.Tokens.RefreshTokenRotationTests` sont toujours dans
  un projet « unitaire ».** Le bon geste n'est pas un `[Trait]` — un attribut au milieu d'un fichier
  n'est pas de la documentation, l'arborescence si — mais un projet
  `Tests/Integration/AppTemplate.Infrastructure.Identity.IntegrationTests`. Les fondre dans la
  suite d'intégration existante les mettrait dans sa collection partagée et **casserait la propriété
  qu'ils testent** (un rendez-vous à deux participants sur une mise à jour conditionnelle). La
  fixture plaide elle-même pour ce déménagement : « a container rather than an in-memory provider,
  because the property under test is a property of the database ».

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

Une leçon de la vague 2 bis, qui vaut d'être retenue : **une instruction fausse donnée à un agent
peut revenir corrigée, ou ne pas revenir du tout.** J'avais demandé de laisser `[AllowAnonymous]` sur
la classe `AuthController` et d'ajouter `[Authorize]` sur les actions authentifiées — ce qui aurait
ouvert `GET /auth/me` à tout le monde. L'agent a refusé la consigne et documenté pourquoi. Un brief
qui dicte le *comment* plutôt que le *pourquoi* n'aurait pas laissé cette porte de sortie.

---

## 10. Ce que la première exécution de la suite d'intégration a révélé

Elle a démarré à 253 échecs sur 255, et fini à 0. Ce qu'elle a trouvé mérite d'être retenu, parce que
rien d'autre ne l'attrapait.

**Un défaut de production : `/auth/login` servait un corps sans discriminant.** `[JsonPolymorphic]`
n'écrit le membre `status` que si la sérialisation *démarre à la base polymorphe*. Or `Ok(value)`
laisse `ObjectResult.DeclaredType` à `null`, et le formateur sérialise alors le type **runtime** — la
branche `Authenticated`, dont le contrat ne porte aucun discriminant. Le corps réel était
`{"tokens":{…}}`. Aucun client n'aurait pu distinguer les branches, ce qui vide de sens la décision
« poser la forme discriminée maintenant pour ne pas casser les clients plus tard ».

Le plus instructif est **pourquoi les tests unitaires ne l'ont pas vu** : celui qui vérifie le
discriminant appelle `JsonSerializer.Serialize<LoginResponse>(…)`, en typant explicitement la base. Il
choisissait le type statique ; MVC ne le choisit pas. Un test de sérialisation qui nomme lui-même le
type ne teste pas le chemin réel — la leçon vaut au-delà de ce cas.

Correctif : `ApiControllerBase` nomme le type déclaré (`Serialised`, `Located`), pour toutes les
réponses porteuses de valeur et pas seulement pour celle qui était cassée.

**`ClockSkew = TimeSpan.Zero` ne pardonnait aucun pas d'horloge en arrière.** Symptôme : un
`IDX10222 — token is not yet valid` intermittent, sur un jeton émis quelques centaines de
millisecondes plus tôt, avec un `nbf` ~3 s dans le futur. L'émetteur date le jeton avec
`IDateTimeProvider`, la validation lisait l'horloge machine : deux horloges pour une décision, sans
tolérance. Un recalage NTP ou une VM reprise suffit alors à refuser d'un coup tous les jetons en
circulation, sur toutes les instances — inacceptable pour le profil « disponibilité non négociable ».
Porté à 30 secondes, très en dessous du défaut de 5 minutes du framework.

Piste écartée en cours de route, et pourquoi : pointer `LifetimeValidator` sur `IDateTimeProvider`
pour n'avoir qu'une horloge. Ça rendait l'échec déterministe (10 au lieu de 5), mais ça ne gagne
**rien** en production — émetteur et validateur y utilisent déjà tous deux l'horloge système — et ça
cassait cinq tests qui déplacent légitimement le temps. Écarté après mesure, pas par principe.

**Trois tests anti-énumération comparaient des documents de problème entiers, `traceId` compris.** Le
`traceId` identifie la requête, pas son issue : deux requêtes en ont toujours deux différents, donc
l'assertion affirmait que deux requêtes étaient la même. `ProblemResponse.BodyWithoutTraceId` compare
tout le reste, ce qui est bien la propriété visée.

**`AssertCleanRefusalAsync` prenait `traceId` pour la signature du gestionnaire de dernier recours.**
Tous les documents de problème en portent un, par décision. Ce qui distingue un refus décidé d'un
plantage, c'est le code stable, le titre, et l'absence de ce qu'une exception aurait fui.

**Un diagnostic ajouté côté hôte de test : l'en-tête `X-Test-Auth-Failure`.** Le produit ne dit
jamais *pourquoi* un jeton est refusé — expiré, mal signé, security stamp révoqué doivent être
indistinguables, et un test l'exige. La conséquence est qu'un 401 inattendu est indiagnosticable :
`ApiFactory` publie donc le motif dans un en-tête que seul l'hôte de test écrit. C'est ce qui a rendu
`IDX10222` visible.

---

## 11. Vagues 4 et 5 : ce qui a été arbitré, et ce qui a été écarté

Trois analyses critiques ont précédé l'écriture. Elles ont convergé sur un point : **l'ordre du §6
était mal calibré**, parce qu'il avait été écrit de mémoire. Ce qui suit dit ce qui a été changé et
pourquoi, pour qu'on n'ait pas à le redécouvrir.

### La raison neuve qui a fait descendre « sessions actives » de la première place

`RefreshTokenStore.TryRotateAsync` marque la ligne présentée comme révoquée et **insère une ligne
neuve avec un `Id` neuf**. L'identifiant qu'un client lirait dans `GET /auth/sessions` est donc mort
au refresh suivant, quinze minutes plus tard : `DELETE /auth/sessions/{id}` s'adresserait à une ligne
déjà révoquée et **la session continuerait**. Une révocation qui échoue en silence est pire que pas
de révocation du tout.

Le §6 croyait qu'il manquait trois colonnes d'affichage (IP, user-agent, dernier usage). Il en manque
une quatrième, structurelle — `SessionId`, stable à travers la rotation — et c'est **la seule qui
porte le sens de la fonctionnalité**. Les trois autres sont dérivables ou dispensables (voir §6).

`POST /auth/logout-all`, en revanche, était la partie **gratuite** de cet item : `RevokeAllForUserAsync`
existait déjà, aucune migration, quinze lignes. Un ADR annonçait cette capacité comme « le hook pour
sign out everywhere » sans qu'aucune route ne l'expose. Livré.

### Ce qui a été refusé, et pourquoi le refus est le livrable

- **Le cache du security stamp** → `docs/adr/0023`. Un ADR qui explique pourquoi l'invalidation par
  point de rotation est un piège enseigne davantage au lecteur du template que le cache lui-même, et
  retire l'item sans laisser de dette.
- **La partition du limiteur par identité** → voir §3. Le blocage a été trouvé par l'agent qui
  l'implémentait, pas par l'analyse : l'ordre du pipeline rend le principal invisible au sélecteur.
  Le code capable de partitionner par identité a été **retiré** plutôt que laissé inerte — il aurait
  fait croire à une capacité qui ne se déclenche jamais.
- **`ICurrentUser.ClientId`** → voir §8. Même raison : pas de source, donc pas de membre.
- **La recherche paginée d'utilisateurs** → voir §6. Elle contredirait tout le reste de la verticale.

Le fil commun des trois : **un template ne doit pas contenir de code qui affirme une capacité qu'il
n'a pas.** C'est le même défaut que celui du §10, transposé du test au code de production.

### Ce qui n'a pas été fait, et qu'il faut savoir

- **Le bail sur la revendication d'idempotence** (§6), qui est le défaut de production le plus
  coûteux encore ouvert. Écarté pour une raison unique et assumée : il exige une migration écrite à
  la main, et le §5 piège 5 dit ce que ça coûte. Ce n'est pas un arbitrage de valeur, c'est un report.
- **Le déménagement des 3 tests Docker** hors du projet « unitaire » (§8) : il crée un projet, donc
  touche le `.sln` — le fichier que le §9 nomme comme la première collision à éviter entre agents.
- **Aucun test ne prouve le drain réel sur SIGTERM.** Le health check qui bascule est testé ; que
  Kestrel draine vraiment, que `ShutdownTimeout` soit honoré et que SIGTERM soit câblé ne l'est pas.
  Ça demande de lancer le binaire et de lui envoyer le signal, donc un projet de test d'un autre
  genre. **Ne pas se raconter qu'un test de `StopApplication()` prouverait le drain** — ce serait la
  faute du §10 transposée à l'hôte.
- **L'écart d'E/S résiduel** sur les branches de connexion (§8), documenté plutôt que corrigé.
