# Ce que ce template veut être (étalon de l'audit)

Public : une équipe .NET qui démarre un produit qu'elle gardera des années ; le projet généré est un point de départ conservé, pas une démo.

Promesses :
1. Ne coder que du métier : socle livré avant le premier usage (pagination, idempotence, HTTP sortant, jobs sous lease, cache, mail, fichiers) — ADR 0001.
2. Une option retire, elle ne désactive pas ; combinaison interdite refusée à la génération — template ADR 0001.
3. Les règles sont tenues par des tests (architecture, contrat OpenAPI, codes d'erreur, default-deny, citations doc) ; un risque = un type de test — ADR 0020.
4. Un contrat HTTP uniforme : ProblemDetails à codes stables, ETag/If-Match, Idempotency-Key, pagination offset et keyset, rate limit — ADR 0008, 0010.
5. Sécurité sérieuse par défaut : default-deny, propriétaire dans la requête (404 jamais 403, ADR 0009), sessions famille de refresh tokens (0013), routes anonymes muettes (0014), passkeys (0015), fichiers contrôlés (0018).
6. Doc du projet, pas du template, conditionnée par les options — template ADR 0002.
7. Hosts et fronts au choix, issus des générateurs officiels épinglés — template ADR 0008, 0009, 0020.
8. Un gate complet lancé depuis un clone (verify, coverage), CI, release, CodeQL, Sonar.

Choix assumés (une différence avec le concurrent sur ces points n'est PAS un retard, sauf preuve d'un coût pour les utilisateurs) :
- pas de MediatR ni de pipeline behaviours ; le contrôleur appelle IUseCase<,> par son interface — ADR 0003 ;
- deux formes de feature : agrégat, ou service sur un store — ADR 0002 ;
- ports en quatre mots (Repository, Queries, Store, Table), pas de repository générique ni d'IQueryable — ADR 0003 ;
- domaine non mappé par EF (rows + mapper + tracker) — ADR 0005 ;
- événements après commit sans outbox, sweep comme garantie — ADR 0006 ;
- une base, un schéma par feature, un contexte par module — ADR 0007 ;
- pas de soft delete, pas d'AutoMapper, pas de Minimal APIs, pas de multi-tenant ni de permissions fines (docs/architecture/README.md, « What is deliberately absent ») ;
- jobs dans l'API ou dans un worker, sous lease — ADR 0011, 0025 ;
- un provider de base par projet, PostgreSQL par défaut — template ADR 0005 ; SQLite = développement seulement — ADR 0021 ;
- une abstraction n'est extraite qu'une fois deux cas réels prouvés (AGENTS.md).

Refus explicites (roadmap) : générateur au choix de l'utilisateur, app desktop branchée sur la couche application, MySQL.

En cours, pas livré : `dotnet cleanarch` (ADR template 0017/0018/0019 « Proposed », aucun CleanArch.Cli). Aujourd'hui l'outillage d'un projet est `dotnet run Tools/Tasks.cs` + `dnx CleanArch.Create` (création et ajouts) + item templates `cleanarch-webapp`, `cleanarch-webpackage`, `cleanarch-presentation`.

Faits du cadrage : 1.0.0 publiée sur nuget.org (CleanArch.WebApi.Templates, CleanArch.Create) ; develop a des changements cassants depuis (#172). L'audit évalue develop (empaqueté en 1.1.0-dev). Concurrent : jasontaylordev/CleanArchitecture, Clean.Architecture.Solution.Template 10.8.0 (tag v10.8.0), main à 345e7fe (2026-10-26).
