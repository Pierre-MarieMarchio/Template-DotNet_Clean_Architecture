# Phase 5 : plaidoirie de l'avocat du concurrent (jasontaylordev/CleanArchitecture)

Racines des chemins :
- `R` = `/tmp/claude-1000/-home-pmmarchio-code-Template-DotNet-Clean-Architecture/608f3f2d-3c02-4530-9e46-6f8e7e0e445b/scratchpad/repos/`
- `O` = `/tmp/claude-1000/-home-pmmarchio-code-Template-DotNet-Clean-Architecture/608f3f2d-3c02-4530-9e46-6f8e7e0e445b/scratchpad/out/`
- `P` = `/tmp/claude-1000/-home-pmmarchio-code-Template-DotNet-Clean-Architecture/608f3f2d-3c02-4530-9e46-6f8e7e0e445b/scratchpad/personas/`

Je ne plaide que ce qui me semble injuste envers le concurrent. Je n'ai rien à contester sur la plupart des
constats de sûreté du concurrent : ils sont prouvés (événements publiés avant l'écriture, CORS ouvert en
production, OpenAPI et Scalar exposés hors Development, licence MediatR, aucun test d'architecture, nuspec qui
retire toute CI du projet généré, actions par tag mobile). Je ne conteste pas non plus le verdict « eux » de G5
Communauté. Je retiens onze griefs. Quatre demandent de changer un verdict de critère ou un verdict global
(n° 1, 3, 10 et 11). Les autres corrigent un fait faux, un fait compté plusieurs fois, ou une limite de
l'environnement de test comptée comme un défaut du template.

---

## 1. G2 Extensibilité, « code à écrire » : le tableau ne tient pas, ce n'est pas « nous »

**Verdict attaqué.** G2 Extensibilité, `code_a_ecrire: nous`. Le juge donne « nous » sur les six critères. Il
reconnaît pourtant que le concurrent gagne sur l'ajout d'un use case et sur le comportement transverse.

**Argument.** Les tâches qu'une équipe répète chaque semaine, c'est ajouter un use case et retirer quelque chose.
Le concurrent y est nettement plus court, en lignes comme en allers-retours.

| Tâche | Concurrent | Nous |
|---|---|---|
| Use case dans une feature | ~110 lignes, ~1 min, compile du premier coup (`O/p3-comp-1.md`, §5, ligne T2) | ~350 lignes, ~9 min + 4 min de `verify`, 3 tours de corrections (CA1862, test généré qui ne compile plus, xUnit1051 + IDE0005) (`O/p3-ours-1.md:36`) |
| Retirer un exemple | Weather : `git rm` + `grep`, ~1,5 min, build et tests verts du premier coup (`O/p3-comp-1.md`, T7) | Notes : `remove-feature` ne fait qu'un rapport ; ~25 min de travail + 5 min de `verify`, 4 erreurs de compilation, 2 tests d'architecture rouges, des mentions en prose qu'aucun test ne voit (`O/p3-ours-1.md:42,80`) |
| Retirer sa propre feature | 25 fichiers + 7 éditions, `dotnet ef migrations remove` propre (T7) | « rapport seul, 5 bloqueurs ; suppression à la main avec le même schéma » (`O/p3-ours-1.md:81`) |

L'argument « B a un outil pour retirer une feature » ne tient pas. Sur develop à jour, `remove-feature` refuse
d'agir dès qu'une feature est enregistrée, même intacte (« 0 edited » puis « Nothing was changed. --apply would
refuse ») : `P/p3-head/log/t3c-remove-untouched.log`, et `O/p3-head.md` §3(c). Or une feature non enregistrée ne
se compose pas. L'outil ne supprime donc que des squelettes qui n'ont jamais servi.

L'argument « ces 350 lignes incluent 10 tests contre 1 » corrige la mesure en partie, mais pas le temps ni les
allers-retours. 8 de ces tests ont été écrits par le persona lui-même (`O/p3-ours-1.md:36`, dernière colonne).

**Preuves.** `O/p3-comp-1.md` §5 (lignes T2 et T7) ; `O/p3-ours-1.md:36,42,80-82` ; `P/p3-head/log/t3c-remove-untouched.log`
et `t3c-remove-edited.log` ; `O/p3-head.md` §3(c) et §4 (« remove-feature inutilisable dès qu'une feature est
éditée (ou enregistrée) | inchangé »).

**Verdict proposé.** `code_a_ecrire` : égalité. Nous gagnons la feature complète et la seconde app web. Le
concurrent gagne le use case, le retrait et le transverse. Le verdict global de l'axe peut rester « nous », mais
pas sur les six critères.

## 2. G2 Extensibilité, « second host » : la comparaison n'oppose pas la même tâche

**Constat attaqué.** G2 Extensibilité, sûreté et code à écrire : « Pour un second host, une commande et 0 ligne
chez B. Chez A, quatre listes à la main et un échec au démarrage » et « A échoue au démarrage dès le premier host
ajouté ».

**Argument.** Chez le concurrent, le persona a ajouté un worker. Chez nous, il a ajouté un MCP, parce que notre
outil refuse le worker après la génération. Sur develop à jour, le refus est toujours là :
`--framework can't be 'worker'. Use one of blazor-server, avalonia, mcp, wpf.` (`P/p3-head/log/t3b-worker.log`).
À tâche égale (un worker après coup), le concurrent y arrive avec un contournement, et nous pas du tout.

Le « 0 ligne » est faux. Après le MCP, il reste une migration Auth à taper à la main, que l'outil imprime en haut
de sa sortie et pas dans « Next: », plus `openapi`. Et `Tasks.cs migration-add` crée une migration vide dans le
mauvais contexte sans prévenir (`O/p3-head.md` §3(b) et §4 ; `P/p3-head/log/t3b-migadd.log`). C'est le même
défaut que le juge reproche au concurrent (« le gabarit lancé hors de src/Application écrit au mauvais endroit
sans erreur ») : chacun des deux outils a son échec silencieux.

**Preuves.** `P/p3-head/log/t3b-worker.log` ; `O/p3-head.md` §3(b) (« Piège migration-add : TOUJOURS là ») ;
`O/p3-ours-1.md:76` ; `O/p5-G2-opus.md:131-134` (le tableau qui met en face le worker du concurrent et notre MCP).

**Verdict proposé.** Retirer « échec au démarrage dès le premier host » des raisons. La sûreté peut rester
« nous », de peu (listes fermées tenues par des tests), mais un échec silencieux est imputé aux deux camps, pas à
un seul.

## 3. G1 Architecture, « lisibilité » : les « frontières floues » sont des choix écrits, ou la norme

**Verdict attaqué.** G1 Architecture et frontières, `lisibilite: égalité`. Le juge écrit : « Domain dépend de
MediatR.Contracts, Application référence EF Core, et un endpoint Web importe un type d'Infrastructure. La
décision de B sur EF Core invoque la direction des dépendances, que ce même endpoint contredit. »

**Argument.**
- Domain vers MediatR.Contracts : c'est un choix écrit, dans `R/competitor-main/docs/decisions/ADR-003-MediatR-Contracts-In-Domain.md`
  (« The "zero NuGet dependencies" ideal is intentionally relaxed »). L'étalon traite nos différences couvertes par
  une ADR comme des choix assumés. La même règle vaut pour les siennes.
- Application vers EF Core : c'est un choix écrit, dans `ADR-001-Use-EFCore-In-Application-Layer.md`.
- « Un endpoint Web importe un type d'Infrastructure » (`R/competitor-main/src/Web/Endpoints/Users.cs:1,12`,
  `MapIdentityApi<ApplicationUser>()`) : c'est de la composition dans le host. Notre API en fait autant dans
  9 fichiers (`grep -rln "using AppTemplate.Infrastructure" Src/Presentation/AppTemplate.Api` sur `R/ours-head`
  renvoie 9 fichiers, dont `Common/Security/CurrentUser.cs` et `Features/Auth/Composition/AuthComposition.cs`), et
  notre règle l'autorise (« Only a composition root … may reference an infrastructure module »).
- La « contradiction » avec l'ADR-001 n'existe pas. Cette ADR parle de la couche Application (« `IApplicationDbContext`
  is defined in the Application layer… The dependency arrow points inward »). Elle ne dit rien d'un host qui
  référence Infrastructure, ce que fait toute Clean Architecture.

Une fois ces trois griefs retirés, il reste 7 projets d'un côté, et de l'autre 14 projets avec un `.Core` par
couche et 8 899 lignes de `Tests/Architecture` à lire pour changer une règle (`O/p5-G2-opus.md`, §2.6).

**Preuves.** Les ADR ci-dessus ; `R/competitor-main/src/Web/Endpoints/Users.cs:1-15` ; le grep sur `R/ours-head`
(9 fichiers) ; `O/p5-G2-opus.md` §2.6.

**Verdict proposé.** `lisibilite` : eux. Le global « nous » de l'axe n'est pas contesté : la sûreté reste
nettement de notre côté.

## 4. G1 Architecture, « testabilité » : « aucun test ne vérifie la composition » est faux

**Constat attaqué.** G1 Architecture, testabilité : « Chez B … aucun test ne vérifie la composition. »

**Argument.** Chaque exécution des tests fonctionnels construit le conteneur réel du host Web.
`WebApiFactory(string connectionString) : WebApplicationFactory<Program>` (`R/competitor-main/tests/Application.FunctionalTests/Infrastructure/WebApiFactory.cs:10`)
est instancié dans le `[OneTimeSetUp]` (`FunctionalTestSetup.cs:43-44`), qui résout ensuite `IServiceScopeFactory`.
L'environnement par défaut est Development, donc la validation du conteneur au build s'applique. C'est d'ailleurs
cette validation qui a fait échouer le worker du persona (`O/p3-comp-1.md`, T6). La vérification L1 l'a déjà
relevé (`O/p5-verify-L1.md:22`, L1-014 : « ses tests fonctionnels démarrent l'hôte »). Le test n'est pas dédié et
ne couvre qu'un host, mais il existe.

**Preuves.** `R/competitor-main/tests/Application.FunctionalTests/FunctionalTestSetup.cs:43-44` ; `WebApiFactory.cs:10-29` ;
`O/p5-verify-L1.md:22`.

**Verdict proposé.** `testabilite` : « nous », mais de peu, sans l'argument « aucun test de composition ».

## 5. G1 Concepts, « testabilité » : un volume de tests ne mesure pas la testabilité

**Constat attaqué.** G1 Concepts, testabilité : « Le dépôt compte environ 3 300 méthodes de test, contre 32 pour B »
et « l'application voit les DbSet d'EF Core, ce qui complique les tests unitaires ».

**Argument.** Le nombre de tests livrés relève de « code à écrire » ou de la couverture, pas de la facilité à
tester. Sur la facilité à tester, les faits sont plus partagés que ne le dit le juge :
- Le concurrent a choisi par écrit de tester contre une vraie base, avec Respawn : « Testing against a real
  database is preferred … Unit tests for pure domain logic and Application-layer validation do not require
  database access at all » (`R/competitor-main/docs/decisions/ADR-001-Use-EFCore-In-Application-Layer.md`, Rationale).
  C'est un choix assumé, au même titre que notre ADR 0005.
- Le persona du concurrent : « Tests fonctionnels rapides (19 tests en ~1 s) avec `TestApp`, `RunAsDefaultUserAsync`,
  reset de base : il a suffi d'écrire les cas » (`O/p3-comp-1.md` §7).
- Chez nous, le test généré d'un use case « ne se recompile plus dès qu'on injecte ses dépendances »
  (`O/p3-ours-1.md`, F8 et ligne 56).

**Preuves.** Ci-dessus.

**Verdict proposé.** `testabilite` : « nous » (ports, horloge, interfaces de use case), mais sans l'argument du
volume, et en reconnaissant que la stratégie du concurrent est un choix écrit.

## 6. « Le paquet publié ne compile plus » : un risque partagé, compté quatre fois, et sur une durée fausse

**Constats attaqués.** G3 CI et release (sûreté : « Son paquet publié ne compile plus depuis 7 mois ») ; G4
Sécurité (sûreté : « son paquet publié ne compile plus ») ; G5 Docs (sûreté et cohérence : « le premier `dotnet build`
de la version publiée échoue ») ; G5 Communauté (sûreté : « la version publiée ne compile plus, l'issue est restée
sans réponse »).

**Argument.**
1. **Le mécanisme est le même chez nous, et plus large.** `R/ours-head/Directory.Build.props:15,19-21` :
   `TreatWarningsAsErrors` + `NuGetAudit` + `NuGetAuditMode all` + `NuGetAuditLevel low`. Notre tag v1.0.0, celui
   publié sur nuget.org, a les mêmes lignes (`git show v1.0.0:Directory.Build.props`). Le concurrent n'a que le
   défaut du SDK (`R/competitor-main/Directory.Build.props:5,8`). Dès qu'un avis touchera une dépendance transitive
   de notre 1.0.0, ce paquet cessera de compiler lui aussi. L'analyste G3 sonnet le dit : « à égalité dans le
   risque, en retard dans la défense … le concurrent a le même défaut » (`O/p5-G3-sonnet.md:11,103`). Notre
   avantage réel est la doc (`R/ours-head/docs/guides/build-and-ci.md:161-165`, épingler plutôt que relâcher).
   Ce n'est pas un avantage de sûreté sur le paquet publié.
2. **Notre chaîne de release ne pourrait pas publier de correctif aujourd'hui non plus.** Les 8 derniers runs de
   `template-release.yml` ont échoué, y compris sur develop à jour, 935d552a (`gh run list -R Pierre-MarieMarchio/Template-DotNet_Clean_Architecture --workflow template-release.yml -L 8`
   renvoie 8 `failure`, du 2026-10-30T00:28 au 2026-10-30T12:10). Le BOM est toujours en tête de `template.json` :
   `head -c 4 R/ours-head/.template.config/template.json | xxd` donne `efbb bf7b`.
3. **« Depuis 7 mois » est faux.** Le paquet a 7 mois (v10.8.0 publiée le 2026-03-23, d'après `gh api repos/jasontaylordev/CleanArchitecture/releases`).
   Les avis qui le cassent sont plus récents : GHSA-2f33-pr97-265q (MessagePack) publié le 2026-06-25,
   GHSA-846f-f764-76mx (Pkcs) publié le 2026-10-13 (`gh api /advisories/<id>`). Le paquet échoue donc depuis
   environ 4 mois au plus, d'après les avis datés.
4. **« L'issue est restée sans réponse »** : #1675 a été ouverte le 2026-10-28T06:27Z, il y a deux jours
   (`gh api repos/jasontaylordev/CleanArchitecture/issues/1675`). Notre propre release échoue depuis trois jours.
   Ce n'est pas une preuve d'abandon.
5. **Le même fait est compté sur quatre axes**, alors qu'il relève d'une seule chose : la cadence de release du
   mainteneur.

**Verdict proposé.** Compter ce fait une seule fois, en G5 Communauté (cadence de publication), comme un risque
que nous partageons. Le retirer des sûretés de G3 CI, de G4 Sécurité et de G5 Docs. G3 CI peut rester « nous »
sur la conception (OIDC, SHA, matrice en PR), mais avec la mention « chaîne cassée des deux côtés aujourd'hui ».

## 7. G4 Sécurité : deux griefs qui n'en sont pas

**Constat attaqué.** G4 Sécurité, sûreté : « mot de passe administrateur écrit dans le code ». Et, parmi les faits
rapportés de sonnet : « les routes de santé de A ne sont mappées qu'en Development ».

**Argument.**
- L'amorçage `Administrator1!` (`R/competitor-main/src/Infrastructure/Data/ApplicationDbContextInitialiser.cs:83`)
  ne s'exécute qu'en Development : `if (app.Environment.IsDevelopment()) { await app.InitialiseDatabaseAsync(); }`
  (`R/competitor-main/src/Web/Program.cs:17-19`). C'est un compte de développement, comme le nôtre, qui n'est créé
  lui aussi qu'en Development (`R/ours-head/Src/Presentation/AppTemplate.Api/Program.cs:170-176`, `MigrateAndSeedForDevelopmentAsync`).
  Notre seule différence est d'exiger un mot de passe venu des secrets utilisateur. C'est mieux, mais ce n'est pas
  un mot de passe de production écrit dans le code.
- Ne mapper `/health` et `/alive` qu'en Development est un réglage prudent, que le code justifie : « Adding health
  checks endpoints to applications in non-development environments has security implications »
  (`R/competitor-main/src/ServiceDefaults/Extensions.cs:103-106`). Ce n'est pas un défaut de sécurité.
- La compilation du paquet publié : voir le grief 6.

**Verdict proposé.** Le verdict « nous » reste. Il faut retirer ces trois griefs. Ceux qui restent suffisent :
CORS `AllowAnyOrigin` dans tous les environnements (`Program.cs:28-31`), OpenAPI et Scalar hors Development
(`Program.cs:35-36`), aucun propriétaire, aucun en-tête de sécurité.

## 8. G5 Docs : « 401 sans piste d'identifiant » est faux, et Playwright est une limite de l'environnement

**Constats attaqués.** G5 Docs et onboarding, cohérence : « l'API répond 401 sans piste d'identifiant ». Et
testabilité : « Chez A, 6 tests d'acceptation échouent faute de navigateurs Playwright ».

**Argument.**
- Les identifiants sont dans le projet généré. `src/Web/Web.http` des projets du persona lui-même porte
  `@Email=administrator@localhost` et `@Password=Administrator1!` : `P/p2-comp-1/work/Def/src/Web/Web.http:14-15`
  et `P/p2-comp-1/work/Min/src/Web/Web.http:4-5`. Le persona a écrit le contraire (`O/p2-comp-1.md:89`, « Ni README,
  ni doc, ni Web.http ne l'indiquent »). La vérification L3 a déjà relevé l'erreur (`O/p5-verify-L3.md:58`, L3-051 :
  « Faux pour Web.http »). L'application Angular générée a en plus ses pages `login` et `register`
  (`O/p5-G4-sonnet.md:138`). Ce qui reste vrai : ces identifiants ne sont ni dans le README ni sur le site.
- Playwright : le persona l'écrit lui-même, c'est une limite de son poste. « Mon Ubuntu 26.04 n'est pas supporté ;
  `libnspr4.so` manquant. Limite d'environnement » (`O/p3-comp-1.md` §2, et §4 point 12 : « Playwright non supporté
  sur Ubuntu 26.04 »). Le message d'échec donne la commande d'installation (`O/p2-comp-1.md`, journal 12:08:55). Le
  seul défaut du template est que le README ne parle pas de cette étape.

**Verdict proposé.** Cohérence : « nous », de peu, avec un premier contact du concurrent corrigé (401, mais
identifiants dans `Web.http`). Testabilité : « nous » (ADR reliées à leurs tests), sans l'argument Playwright.

## 9. G3 Tests et qualité : le même biais d'environnement sur Playwright

**Constat attaqué.** G3 Tests et qualité, testabilité : « Ses Playwright échouent sur un poste neuf, et le README
n'en parle pas (opus 1.8). »

**Argument.** Même cause qu'au grief 8 : le poste du persona, un Ubuntu 26.04 que Playwright ne supporte pas
(`O/p3-comp-1.md` §2 et §8). Sur un poste supporté, la commande imprimée suffit. Il faut aussi garder la juste
proportion : le concurrent est le seul des deux à piloter un navigateur. Six scénarios Reqnroll + Playwright
tournent dans `R/competitor-main/tests/Web.AcceptanceTests/`, et notre propre extrait de connexion n'est tenu par
aucun test de bout en bout (`O/p5-G4-sonnet.md:142`).

**Verdict proposé.** `testabilite` : « nous » peut rester (HTTP contre une vraie base), mais sans ce grief, et avec
le test navigateur compté comme un avantage net du concurrent.

## 10. G2 Conventions, « lisibilité » : nos règles maison servent d'étalon, et nos propres incohérences sont mises ailleurs

**Verdict attaqué.** G2 Conventions, `lisibilite: nous`. Les griefs retenus contre le concurrent : 70 fichiers avec
BOM et 40 sans, 43 lignes de commentaires de prose, constructeurs classiques et primaires mélangés.

**Argument.**
- « Aucun commentaire de prose » et « BOM partout » sont nos règles maison (AGENTS.md, « Clean comments » ;
  `Tools/CheckComments.cs`). Ce ne sont pas des critères de lisibilité reconnus. Une partie des 43 lignes du
  concurrent vient du texte d'origine d'Aspire (`O/p5-G2-sonnet.md:132`, `src/ServiceDefaults/Extensions.cs:13-15`).
  Un BOM présent ou absent ne change rien pour celui qui lit.
- Le mélange des constructeurs est imputé au seul concurrent dans la lisibilité. Nous avons pire : `.editorconfig:48`
  déclare `csharp_style_prefer_primary_constructors = false`, alors que 208 classes de `Src/` en utilisent un
  (`O/p5-G2-sonnet.md:16,153`). Le concurrent déclare `true` et en emploie peu (`:151`). Les deux sont incohérents.
  Le juge n'a mis le nôtre qu'en sûreté.
- La profondeur coûte à la lecture : chemin moyen d'un `.cs` à 103 caractères chez nous contre 53 (`git ls-files '*.cs'`
  donne 103,1 sur 1 935 fichiers pour `R/ours-head`, 53,3 sur 110 pour `R/competitor-main`), maximum 208 contre 95,
  et une issue ouverte chez nous : #166 « Generated paths reach 176 characters: a deep folder on Windows cannot commit ».
- La testabilité de cet axe reprend des faits de G3 (projet d'intégration vide, tests sans HTTP). Ce sont des
  questions de contenu des tests, pas de conventions. Ils sont comptés deux fois.

**Verdict proposé.** `lisibilite` : égalité (uniformité contre profondeur). `testabilite` : jugée sur les seules
conventions de tests (placement, nommage), « nous » de peu. Le global « nous » peut rester, porté par la sûreté.

## 11. G4 Front-ends, global : « le plus disputé » devrait donner égalité

**Verdict attaqué.** G4 Front-ends, `global: nous`, avec `code_a_ecrire: eux`, `testabilite: égalité` et une
cohérence « de peu ».

**Argument.**
- Le juge le reconnaît : c'est l'axe « le plus disputé ». Son départage repose sur une seule phrase sans preuve :
  « Les pages de connexion et d'exemple sont plus faciles à écrire … que la CSP à nonce ». Aucun persona n'a mesuré
  ni l'un ni l'autre.
- Le concurrent livre une application qui marche le premier jour, dans deux frameworks : connexion, inscription,
  tâches, météo. Elle est construite et testée dans un navigateur en CI (`R/competitor-main/.github/workflows/test-templates.yml`,
  et `O/p5-G4-sonnet.md:138,176`). Chez nous, « the pages are yours to write ». Aucun starter, sauf Blazor, n'a de
  page de connexion. L'extrait de la doc n'est tenu par aucun test de bout en bout (`O/p5-G4-sonnet.md:140-142`).
- Sur develop à jour, nos garde-fous web sont plus faibles que ne le suppose la « sûreté » : `verify` ne lance aucun
  `pnpm` (lint, test, build, `schema.ts`). `O/p3-head.md` §4 : « verify ne couvre pas le web … | inchangé |
  `t3b-verify.log` : 0 `pnpm` ». Après `web-app`, 9 des 10 éditions imprimées ne sont contrôlées par aucun test
  (`O/p3-head.md` §3(b)). Un clone n'a aucun front avant le `bootstrap`, qui a besoin du réseau, et le job
  `web-apps` échoue avec « holds no app » (`O/p5-G4-sonnet.md:187`).
- L'étalon fait de « pas de pages » un choix assumé (template ADR 0003, 0008). Mais l'analyste lui-même y voit « un
  coût pour l'utilisateur … identifiable » (`O/p5-G4-sonnet.md:142`). L'étalon ne neutralise plus une différence
  quand ce coût est prouvé.

**Verdict proposé.** `global` : égalité. Notre durcissement (CSP, CSRF, images) et notre étendue (8 frameworks)
sont contrebalancés par une application authentifiée qui marche et qui est testée en navigateur chez le concurrent,
et par un web que notre gate local ne vérifie pas.

---

## Ce que je ne conteste pas

- G1 Concepts, sûreté et global « nous » : événements avant l'écriture contre l'ADR-003 du concurrent (« dispatch
  events after `SaveChangesAsync` »), aucune notion de propriétaire, journalisation de la requête entière. Ces faits
  sont prouvés.
- G1 Architecture, sûreté « nous » : aucune règle exécutable chez le concurrent (`O/p3-comp-1.md`, T8).
- G3 Tests et G3 CI, « code à écrire » « nous » : le nuspec retire les quatre workflows du projet généré
  (`R/competitor-main/CleanArchitecture.nuspec`, attribut `exclude`). La matrice ne tourne qu'après fusion
  (`test-templates.yml`, `on: push: branches: [ main ]`).
- G5 Communauté : le verdict « eux » est juste.

## Biais repérés, en résumé

- **Environnement compté comme un défaut** : Playwright sur Ubuntu 26.04 (griefs 8 et 9).
- **Travail inégal** : un worker chez le concurrent contre un MCP chez nous (grief 2) ; 10 tests écrits d'un côté,
  1 de l'autre (grief 1).
- **Biais maison** : nos règles de commentaires et de BOM servent d'étalon de lisibilité (grief 10) ; les ADR du
  concurrent ne sont pas lues comme des choix assumés, alors que les nôtres le sont (griefs 3 et 5).
- **Double comptage** : le paquet publié qui ne compile plus, sur quatre axes (grief 6) ; les tests d'intégration
  vides, en G2 Conventions et en G3.
- **État périmé ou fait faux** : « depuis 7 mois », « issue sans réponse » après deux jours (grief 6) ; « aucun test
  de composition » (grief 4) ; « 401 sans piste » (grief 8).
