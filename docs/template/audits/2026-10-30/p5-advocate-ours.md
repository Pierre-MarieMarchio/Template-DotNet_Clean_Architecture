# P5 : plaidoirie pour notre template

« Nous » désigne notre template, « eux » jasontaylordev/CleanArchitecture. J'ai tout lu en lecture seule : `repos/ours` (5f22faa5), `repos/ours-head` (935d552a), `repos/competitor-main`, les personas p2/p3, `p5-verify-L1.md`, et GitHub en GET seulement. Je n'ai lancé aucun build.

Je ne conteste que ce qui me paraît injuste. Sur les 10 verdicts d'axe, 9 nous donnent déjà gagnants. Le dixième, « Communauté et entretien », reste « eux » à mes yeux pour l'essentiel : 1 étoile contre 20 700, aucun contributeur externe, discussions désactivées (`gh api repos/... --jq .has_discussions` → `false`, `stargazers_count` → 1). Je ne le plaide pas. Mes contestations portent sur cinq critères, dont aucun n'est un « global », et sur plusieurs constats qui pèsent contre nous dans les justifications.

## Biais repérés dans la matière

1. **L'environnement compté comme friction.** /tmp était un tmpfs plein (p2-ours-1.md:57, p2-ours-2.md:7, p3-ours-1.md:11). `coverage` plantait sous WSL2 6.6.87.2 (p3-ours-2.md:150). Or notre propre CI exécute `coverage` sur Linux et le seuil passe : run 41050107951, job « Build & test », étapes `Test (with coverage)=success` et `Enforce coverage floor=success` (`gh run view 41050107951 --json jobs`).
2. **Un travail inégal mesuré en lignes.** Nos personas ont écrit davantage de tests, et une migration qui s'applique vraiment. Ces lignes sont ensuite comparées à du code concurrent qui ne fait pas la même chose (détail en G1).
3. **Un état qui a changé depuis.** Sur HEAD, la PR de release périmée et la « release instable » s'expliquent par un seul incident (voir G3 CI).
4. **Des choix couverts par une ADR comptés comme des surprises.** C'est le cas des sessions SSR en mémoire (ADR 0024), de la durée de vie du jeton d'accès (ADR 0013) et des pages front laissées au projet (ADR template 0003/0008). Le cadrage (`00-cadrage.md:15`) dit qu'un tel choix n'est pas un retard sans preuve d'un coût pour les utilisateurs.

---

## G1 · Concepts du code : « code à écrire » (eux) et « lisibilité » (eux)

**Ce que dit le jugement** : B gagne parce que « ~110 lignes contre ~350 pour deux use cases, ~14 lignes sur 7 fichiers contre ~45 sur ~14 pour un champ », et parce qu'« un use case tient dans un seul fichier de 37 lignes ».

**Pourquoi c'est injuste** :
- **Les ~350 lignes comptent 10 tests, les ~110 en comptent un seul.** Notre persona a écrit 8 tests unitaires et 2 d'intégration (`p3-ours-1.md:36`, colonne « Tests obtenus » : « 8 écrits par moi + 2 d'intégration »). Celui du concurrent a écrit « +1 (24) » (`p3-comp-1.md:92`). Le juge G2 Extensibilité relève lui-même ce point : « ces 350 lignes incluent 10 tests, contre 1 chez A ». Deux juges lisent donc les mêmes chiffres de deux façons.
- **« Un seul fichier de 37 lignes » est inexact.** Le vérificateur a corrigé ce fait (`p5-verify-L1.md:9` et `:49`, L1-001 et L1-041) : le concurrent a un second fichier, `CreateTodoItemCommandValidator.cs` (11 lignes). La juste mesure est 4 fichiers et 72 lignes contre 2 fichiers et 48 lignes, soit un rapport de 1,5 et non de 3.
- **Les ~14 lignes du champ chez le concurrent ne donnent pas un résultat qui marche.** `p3-comp-1.md:40-41` : `EnsureDeletedAsync` puis `EnsureCreatedAsync` à chaque démarrage, « la migration n'est donc jamais appliquée », `PendingModelChangesWarning` ignoré, « oublier la migration ne produit aucun signal ». Nos ~45 lignes contiennent les modifications de tests (`p3-ours-1.md:37` : « 3 modifs de tests »), et le gate force la migration.
- **La mesure qui nous favorise est écartée.** Pour une feature complète, il faut ~215 lignes chez nous contre ~380 chez eux (`p3-ours-1.md:35`, `p3-comp-1.md:91`). Pour un host de plus, 0 ligne à la main chez nous (MCP, `p3-ours-1.md:41`) contre ~90 lignes chez eux, plus un plantage à l’exécution (`p3-comp-1.md:96` et `:57`).
- **Sur la lisibilité**, le juge reconnaît que B « cache une partie de sa logique » : 5 behaviours, une autorisation par réflexion, ProjectTo, un événement dans un setter. Il tranche pourtant sur un « un seul fichier » qui est faux. Il tranche aussi sur notre vocabulaire de ports et l'absence de MediatR, deux choix d'ADR 0003 (`00-cadrage.md:16-18`). Aucun persona n'a signalé de coût de compréhension dessus : p3-ours-1 note « Je n’ai pas eu à ouvrir la doc » (`p3-ours-1.md:50`).

**Verdict proposé** : code à écrire, égalité (le coût par use case est réel mais modéré, le coût par feature et par host va dans l'autre sens). Lisibilité, égalité.

## G1 · Architecture et frontières : « code à écrire » (eux)

**Ce que dit le jugement** : « Moins de projets, et aucune règle structurelle à respecter, donc moins de travail autour du métier. » Le juge ne donne aucune mesure.

**Pourquoi c'est injuste** : personne n'écrit les 14 projets, le générateur écrit chaque couche. `new-feature` produit 52 fichiers en 0,65 s sur 5 couches (`p3-head.md:33`, `p3-ours-1.md:47-50`). Les mesures du travail lié à l'architecture vont dans notre sens : une feature demande ~215 lignes contre ~380, et un second host 0 ligne contre ~90 avec un plantage `EndpointDataSource` (`p3-comp-1.md:57`). Casser une règle coûte 1 ligne des deux côtés (T8). Les règles ont un coût réel, que je ne nie pas : 6 éditions imprimées par `new-feature` (`p3-head.md:34`) et des allers-retours de gate. Mais le juge l'affirme sans le chiffrer, alors que les chiffres disponibles le contredisent.

**Verdict proposé** : égalité.

## G2 · Conventions : « code à écrire » (eux)

**Ce que dit le jugement** : « Un use case y tient dans un seul fichier… Chez B, il faut quatre fichiers par use case. »

**Pourquoi c'est injuste** :
- Le fait de départ est faux : 2 fichiers et 48 lignes chez le concurrent (L1-041, `p5-verify-L1.md:49`).
- Nos conventions (un type par fichier, un suffixe par dossier) n'ajoutent rien à écrire à la main. `new-use-case` écrit les 5 fichiers et le test en 1,4 s et imprime l'action de contrôleur (`p3-ours-2.md:12`, `p3-ours-1.md:56` : « Excellent pour Archive »). Le `ca-usecase` du concurrent écrit un squelette dont le corps est `throw new NotImplementedException()` (`p3-comp-1.md:28`).
- Le coût réel des conventions est le format et les noms attrapés au gate. Il est minoré par une étape documentée : `AGENTS.md:58` (« After creating or moving a file | `format-fix` »). Le persona de HEAD passe au vert juste après `format-fix` (`p3-head.md:37-38`).

**Verdict proposé** : égalité.

## G4 · Front-ends : « code à écrire » (eux) et constats de « surprises »

**Ce que dit le jugement** : A livre une app d'exemple (connexion, inscription, todo, météo). B « dit lui-même “the pages are yours to write” », et ajouter une app laisse ~10 éditions.

**Pourquoi c'est injuste** :
- **Les pages laissées au projet sont un choix d'ADR.** ADR template 0003, Consequences (`docs/template/decisions/0003-framework-apps-come-from-official-clis.md`) : « A project's first app is exactly the CLI's starter… the screens are the project's to write ». L'ADR 0008 la remplace avec le même principe, et la promesse 7 du cadrage le reprend (`00-cadrage.md:12`). Le projet généré est « un point de départ conservé, pas une démo » (`00-cadrage.md:3`).
- **Les todos et la météo du concurrent sont du code à retirer.** Les utilisateurs du concurrent demandent depuis 2024 de pouvoir générer sans exemples : #1192, ouverte, « This is on the roadmap » (`p5-verify-L1.md:64`, L1-056). La connexion est la seule page réellement économisée, et notre client typé fournit déjà le flux de session.
- **Les ~10 éditions pour ajouter une app sont comparées à rien.** Le concurrent n'a aucun outil pour ajouter une app : il faut copier 29 fichiers d'une génération jetable, et la publication n’embarque que l’Angular (`p3-comp-1.md:55`). Chez nous, une commande déduit les options, crée l'app, la joint et l'enregistre (`p3-head.md:40-41`).
- **Deux « surprises » sont en fait des choix documentés.** Les sessions SSR en mémoire sont décidées par ADR 0024 (`repos/ours/docs/decisions/0024-a-server-rendered-web-app-runs-on-its-own-server.md:47`, alternatives écartées l.127-130, « Revisit when » l.178). Le besoin de réseau au bootstrap est écrit dans ADR template 0003 (« Bootstrap needs the package registry once »).
- **« Des chemins de 176 caractères bloquent les commits sous Windows » va plus loin que l'issue.** L'issue #166 dit que le blocage n'arrive que « in a folder deeper than about 80 characters » et sans `core.longpaths`, et que le launcher l'explique et propose la commande (`gh issue view 166`).

**Verdict proposé** : code à écrire, égalité. Le verdict global de l'axe ne change pas.

## G5 · Communauté et entretien : « sûreté » (égalité)

**Ce que dit le jugement** : « Les deux laissent passer une erreur grave dans leur chaîne de publication. »

**Pourquoi c'est injuste** : les deux pannes n'ont pas le même effet sur les utilisateurs.
- **Celle du concurrent les atteint.** v10.8.0 est publiée le 2026-03-23 (`gh api .../releases/latest`) et ne compile plus : l'issue #1675 dit « 10.8.0 now fails to build because of NU1902/NU1903 », elle est ouverte avec 0 commentaire, et le persona mesure 120 erreurs (`p3-comp-1.md:90`).
- **La nôtre échoue fermée.** release-please plante sur le BOM avant toute publication, et les jobs de paquet et de publication en dépendent (`needs: release-please`, `.github/workflows/template-release.yml:47,122,144`). Aucun paquet cassé n'est sorti. Le tag v1.0.0 (24ff3256) n'a pas de BOM (`git show 24ff3256:.template.config/template.json | head -c3 | xxd` → `7b0a20`). Sur ce commit, la « Template acceptance » est verte (`gh run list --branch main` : `2026-10-28T16:30:14Z Template acceptance 24ff3256 success`), et cette acceptance installe le `.nupkg` empaqueté (`template-acceptance.yml:27` « what is proved is what a user installs », `:123`). La release audite en plus les paquets vulnérables avant de publier (`release.yml:73-79`). Ce garde-fou aurait arrêté la v10.8.0 du concurrent.
- La panne reste réelle, et le BOM est toujours là sur HEAD (`head -c 4 .template.config/template.json | xxd` → `efbb bf7b`). Mais son seul coût est qu'aucune nouvelle version ne sort. Le paquet publié reste sain.

**Verdict proposé** : sûreté, nous. Le global de l'axe reste « eux » : je ne conteste pas la pérennité.

---

## Constats à corriger dans des verdicts que nous gagnons déjà

### G3 · CI et release : « release instable dans la durée »
Le juge retient de sonnet « 36 échecs sur 73 runs » et conclut que la release est « instable dans la durée, et pas seulement depuis l'incident du BOM ». Les journaux disent autre chose. J'ai compté 35 échecs sur les 73 runs de `template-release.yml`. Le premier, le 2026-10-22, est une erreur de réponse transitoire de l'API GitHub (`##[error]release-please failed: Request failed due to following response errors`, run 39811890870), suivie de deux succès. Les 34 autres datent tous du 2026-10-28 07:32 ou après, avec une seule cause : `Unexpected token '﻿', "﻿{"` (run du 2026-10-28T07:32, `--log-failed`). Entre les deux, il y a 20 succès consécutifs (`gh run list --workflow template-release.yml -L 100`). Il s'agit donc d'un seul incident, toujours ouvert, et non d'une instabilité chronique.

« La PR de release annonce une version mineure alors que des commits marqués “!” ont été fusionnés » n'est pas un second défaut. C'est un symptôme du même incident. La PR #91 « chore: release 1.1.0 » a été mise à jour pour la dernière fois le 2026-10-28T16:37 (`gh pr list`). Les commits cassants datent d'après : 251b5173 du 2026-10-29T22:30 et 1dc96b3f du 2026-10-30T00:36 (`git log`). release-please plante avant de pouvoir recalculer la version.

Je propose de garder le verdict (nous) et de remplacer le constat par « un incident (BOM) qui bloque la release depuis le 28/10 sans rien publier ».

### G3 · Tests et qualité : « 6 runs rouges sur 16 » et « coverage a planté sous WSL2 »
- Le plantage de `coverage` vient de l'environnement de l'agent. Notre CI ubuntu-latest fait passer `Test (with coverage)` et `Enforce coverage floor` (run 41050107951). Le persona lui-même ne le classe pas comme friction (« Non classé comme friction du template sans preuve supplémentaire », `p3-ours-2.md:150`).
- Sur les 6 `verify` rouges, deux ne viennent pas du template. L'un est un bug du test que le persona a écrit (« mon test NSubstitute », `p3-ours-2.md:90`) : un gate qui l'attrape fait son travail. L'autre est un IDE0005 que `format-fix` répare, mais le persona l'avait lancé « avant l'édition, pas après » (`p3-ours-2.md:138`), contre l'ordre écrit dans `AGENTS.md:58`. Restent 4 rouges imputables au template : 2 dus au défaut de doc `adding-a-setting.md` (réel), 1 IDE1006 et 1 au retrait. Ce sont de vrais défauts, mais leur nombre est surestimé.

Je propose de garder le verdict (nous) et de corriger le constat en « 4 rouges imputables au template sur 16, coverage non reproduit hors WSL2 ».

### G4 · Sécurité : réserves de sonnet
- **« Un jeton d'accès reste valide après la fin de sa session »** est un choix écrit dans ADR 0013 (`repos/ours/docs/decisions/0013-a-session-is-a-refresh-token-family.md:35-36` : « the access token that asked lives out its few minutes ») avec son « Revisit when » (l.45). Ce n'est pas une réserve, c'est un choix assumé.
- **« 11 alertes de code scanning »** est exact (`gh api .../code-scanning/alerts?state=open`). Mais les 10 `cs/log-forging` visent des paramètres de templates de message structurés (`{Path}`, `{Method}`), par exemple `RequestLoggingMiddleware.cs:21-29` et `GlobalExceptionHandler.cs:58-93`, écrits par `AddJsonConsole` (`Program.cs:47`), qui encode les valeurs. La `js/polynomial-redos` porte sur `/\/+$/` appliqué à `options.baseUrl` (`Src/Web/packages/api-client/src/session.ts:50`), une valeur que le développeur fournit, pas l'attaquant. Le reproche juste est que ces alertes ne sont pas triées. Ce ne sont pas des failles démontrées.
- Je ne conteste pas l'absence d'audit npm face à `SECURITY.md:57` (« Every dependency pinned and audited ») : aucun workflow ne lance `pnpm audit` (grep `audit` sous `.github/workflows` : seulement NuGet). Je ne conteste pas non plus le secret scanning désactivé (`security_and_analysis.secret_scanning.status = disabled`).

### G2 · Extensibilité : « MySQL figure dans l'aide mais est refusé »
L'aide le dit elle-même : la description de l'option est « MySQL is not generated by this version of the template » (`repos/ours/.template.config/template.json:111`). MySQL est aussi un refus explicite de la roadmap (`00-cadrage.md:27`). Le défaut se réduit à une valeur listée, puis refusée avec une explication.

---

## Ce que je ne conteste pas (constats justes)

- Le pipeline de release est en panne sur HEAD, et aucun test ne contrôle le BOM de `template.json` (`efbb bf7b`).
- `#pragma ASP0000` et `NoWarn CS1591;CS1573` / `AV0029;AV0030` sont toujours présents sur HEAD (`HttpPipelineModule.cs:67`, `Directory.Build.props:27`, `AppTemplate.Api.csproj:16`), contre `build-and-ci.md:70-71`.
- ADR 0009 se contredit, puisque le code charge par id puis compare (L1-012). `domain.invariantViolated` répond 400 ou 409 selon le chemin (L1-005).
- `global.json` est en `rollForward: latestFeature` alors que la doc demande une « exact version » (`docs/decisions/0001-adopt-the-generated-baseline.md:34`).
- Il n'y a pas de tests navigateur. Le plancher de couverture n'est mesuré dans aucune variante générée.
- `dotnet cleanarch` sur HEAD n'a aucune commande et plante sur un `cleanarch.json` par défaut (`p3-head.md:23-29`).
- Communauté : la pérennité prouvée est du côté du concurrent.
