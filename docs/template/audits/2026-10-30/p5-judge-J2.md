# p5-judge-J2 (jugement à l'aveugle, écrit par l'orchestrateur depuis la sortie structurée)

| Groupe | Axe | Lisibilité | Code à écrire | Sûreté | Testabilité | Cohérence | Global | Nous = |
|---|---|---|---|---|---|---|---|---|
| G3 | Tests et qualité | A | A | A | A | A | **A** | A |
| G3 | CI et release | égalité | A | A | A | A | **A** | A |
| G4 | Sécurité | B | B | B | B | B | **B** | B |
| G4 | Front-ends | B | A | B | égalité | B | **B** | B |

## Justifications

### G3 · Tests et qualité

AVERTISSEMENT PRÉALABLE (vaut pour les quatre axes). Les lettres ne désignent pas le même dépôt dans G3 et dans G4. Les faits se recoupent : le dépôt qui a Renovate en fusion automatique, l'issue NU1902/NU1903 (118 à 120 erreurs) et une dernière release vieille de 7 mois s'appelle B dans les deux dossiers G3 (opus 2.3-2.4, sonnet 2.3-2.4), mais A dans les deux dossiers G4 (opus 1.6, sonnet 1.6). Le dépôt qui a Dependabot sur 5 écosystèmes et NuGetAudit all/low est A en G3 et B en G4. Je juge chaque groupe avec ses propres lettres, sans chercher qui est qui. Quand on agrège, il ne faut pas additionner les A de G3 et les A de G4.

VERDICT : A, nettement.

Lisibilité : A. A documente chaque type de test dans un tableau : ce qu'il prouve, ce qu'il ne doit pas prouver, et la règle « un risque, un type de test » (opus 1.1, sonnet 1.2). B est plus petit (34 méthodes environ, sonnet 1.1), donc plus facile à parcourir. Mais il se présente mal : le README annonce des tests d'intégration alors que le projet ne contient qu'un GlobalUsings.cs (opus 1.1, sonnet 1.1). La taille de A (18 projets, environ 3 300 à 3 456 méthodes) reste une charge de lecture réelle.

Code à écrire : A. A livre les règles d'architecture (195 méthodes, 17 contre-épreuves), le contrat OpenAPI, le plancher de couverture fusionné et une tâche verify qui enchaîne format, build, tests, dérive des migrations et audit (opus 1.5-1.6, sonnet 1.2-1.4). Avec B, l'équipe doit écrire tout cela elle-même : B n'a ni gate, ni seuil, ni test d'architecture (opus 1.6, sonnet 1.4).

Sûreté : A. A combine AnalysisLevel latest-recommended, EnforceCodeStyleInBuild, TreatWarningsAsErrors et le contrôle du format en CI. B n'a que TreatWarningsAsErrors, 19 règles en suggestion et aucune vérification du format (opus 1.7, sonnet 1.5). Chez B, une migration oubliée ne fait échouer aucun test (opus 1.6). Réserves sur A, relevées par les deux rédacteurs : la règle « jamais de NoWarn ni de #pragma » est violée dans le code et rien ne la teste. Opus ajoute que le plancher de 89 % n'est mesuré dans aucune variante générée et que le commentaire parle de Debug alors que la CI mesure en Release (1.5).

Testabilité : A. A teste par HTTP contre une vraie base Testcontainers épinglée par digest, et une commande lance les tests sans Docker (opus 1.3). B a un vrai point fort : 6 scénarios Playwright pilotent un navigateur, ce que A n'a pas du tout (opus 1.8, sonnet 1.6). B a aussi SQLite sans Docker et un temps de test de 40 s. Mais ses tests fonctionnels passent par le médiateur, sans HTTP, avec un utilisateur en champs statiques. Ses Playwright échouent sur un poste neuf, et le README n'en parle pas (opus 1.8). Ses tests Angular ne sont pas lancés en PR (sonnet 1.6).

Cohérence avec le but : A. A outille davantage le quotidien, mais avec des frictions. Le gate complet prend 4 à 5 min. Un utilisateur a eu 6 runs rouges sur 16, dont 2 à cause de diagnostics de style que dotnet build n'avait pas montrés. La tâche de couverture a planté sous WSL2 (opus 1.4-1.5). Les règles de A protègent, mais elles gênent un peu. Celles de B ne gênent pas, mais ne protègent presque pas.

Divergences entre rédacteurs : le volume de A (environ 3 300 contre 3 456) et le nombre de projets de B (5 + hôte contre 6) sont des écarts de comptage sans effet sur le verdict. Seul opus rapporte les 6 gates rouges sur 16 et le plantage WSL2. Seul sonnet note chez B un WarningsNotAsErrors NU1608 et un #pragma CS1998. Je retiens les deux séries : elles ne se contredisent pas.

### G3 · CI et release

Rappel : lettres propres à G3 (voir l'avertissement du premier axe).

VERDICT : A. Sa release est cependant cassée au moment de la lecture.

Lisibilité : égalité. B a 4 workflows courts, faciles à lire (sonnet 2.1). A en a 7, avec un sélecteur de jambes par chemins, 25 jambes et 32 contrôles requis (opus 2.2, sonnet 1.7 et 2.6). Sa documentation des déclencheurs est inexacte : elle ne cite pas les PR vers la branche d'intégration (opus 2.7). En contrepartie, le projet généré par B ne reçoit aucun workflow, donc il n'y a rien à lire, alors que A livre ci.yml, codeql, release, web, Sonar et un modèle de PR, avec des étapes nommées (opus 2.1, sonnet 2.5). La simplicité de B et la complétude lisible de A se compensent.

Code à écrire : A, sans discussion. Le nuspec de B exclut les workflows et renovate.json : l'équipe écrit toute sa CI et sa politique de dépendances (opus 2.1, sonnet 2.5).

Sûreté : A. A épingle ses actions par SHA, et un outil le vérifie avec les permissions (opus 2.7, sonnet 2.1). A publie par Trusted Publishing OIDC derrière un relecteur. Il installe et construit le paquet avant l'envoi, et produit une attestation de provenance (opus 2.6, sonnet 2.2). B publie par nuget push avec une clé stockée, sans test ni attestation. Il référence ses actions par tag mobile et fusionne automatiquement les mises à jour, sans aucun contrôle requis sur la branche principale (sonnet 2.4, 2.6). Sa matrice ne tourne qu'après fusion, et 11 runs sur 50 ont échoué (opus 2.2, sonnet 1.7). Son paquet publié ne compile plus depuis 7 mois (opus 2.4, sonnet 2.3). Réserves fortes sur A : le job release-please échoue à cause d'un BOM dans template.json, réintroduit après un premier retrait, et aucun test ne le contrôle (opus 2.8, sonnet 2.3). Ironie relevée : A teste l'absence de BOM dans le fichier du plancher de couverture, pas dans ce fichier-là. La PR de release ouverte annonce une version mineure alors que des commits marqués « ! » ont été fusionnés (opus 2.8). global.json est en rollForward alors que la doc exige des versions exactes (opus 2.7).

Testabilité : A. La matrice de A installe le paquet packé, génère sous un autre nom, vérifie les fuites et les combinaisons refusées, sur chaque PR (opus 2.2, sonnet 1.7). B installe depuis le dossier source, pas depuis le paquet (sonnet 1.7), et seulement après fusion.

Cohérence avec le but : A. Pour B, « un outillage qui fait le travail au quotidien » est absent du projet généré. Le coût de A est réel : PR plus lente et release fragile.

Divergences entre rédacteurs, et mon choix. 1) Durées de A : opus donne des moyennes (10 min par PR, 35 min pour la matrice), sonnet des médianes (409 s, 1 137 s). Ce sont des statistiques différentes sur des échantillons différents. Je retiens la médiane, plus robuste. Les deux montrent que A est 2,5 à 6 fois plus lent que B. 2) Panne de release de A : opus la date de 2 jours (10 runs), sonnet ajoute 36 échecs sur 73 runs au total. Les deux s'accordent sur la cause actuelle. Je retiens que la release de A est instable dans la durée, et pas seulement depuis l'incident du BOM. 3) Seul sonnet note l'absence de contrôles requis chez B. Ce fait aggrave le cas de B sans contredire opus.

### G4 · Sécurité

Rappel : dans G4, les lettres sont inversées par rapport à G3 d'après les faits recoupés (voir le premier axe). Je juge avec les lettres de G4.

VERDICT : B, nettement. Les deux rédacteurs concordent sur l'essentiel.

Lisibilité : B. La posture de B se lit en un endroit : une politique de repli « utilisateur authentifié », une liste fermée de 12 actions anonymes plus 4 endpoints fluent, et un SECURITY.md avec une liste « avant de déployer » (opus 1.2-1.3, sonnet 1.1 et 1.7). A protège à deux endroits : RequireAuthorization par groupe, et un attribut [Authorize] lu par MediatR et posé sur une requête sur neuf. Il faut lire les deux pour savoir ce qui est protégé (opus 1.2, sonnet 1.1). Le module d'authentification de B est gros (7 dossiers de features), mais il est structuré.

Code à écrire : B. B livre le filtre propriétaire dans chaque requête avec une réponse 404, le limiteur de débit, les en-têtes, un CORS fermé et la validation des options au démarrage (opus 1.2-1.5, sonnet 1.2-1.5). Avec A, l'équipe doit ajouter tout cela, et d'abord la notion de propriétaire : la lecture des listes renvoie celles de tout le monde (opus 1.2, sonnet 1.2).

Sûreté : B. Chez A : CORS AllowAnyOrigin dans tous les environnements, mot de passe administrateur écrit dans le code, OpenAPI et Scalar exposés en production. A n'a aucun en-tête de sécurité. Son pré-processeur journalise la requête entière déstructurée (opus 1.4-1.5, sonnet 1.3-1.4), et son paquet publié ne compile plus (opus 1.6, sonnet 1.6). B refuse au démarrage la clé JWT de développement et un mot de passe d'amorçage vide. Un test d'architecture fige la liste anonyme, et un test HTTP vérifie que chaque route renvoie 401 sans jeton (opus 1.2-1.5, sonnet 1.1-1.5). Réserves sur B, relevées surtout par sonnet : 11 alertes de code scanning ouvertes (une ReDoS high, dix log-forging) ; secret scanning et push protection désactivés (1.7) ; aucun audit npm, alors que la doc dit « Every dependency pinned and audited » (1.6). Sonnet note aussi qu'un jeton d'accès reste valide après la fin de sa session et qu'aucune rotation de clé n'est décrite (1.3). La pile Compose locale est permissive (1.5). Opus signale enfin que l'hôte Blazor Server optionnel n'a pas de repli et pas de CSP (1.2, 1.4).

Testabilité : B. B teste en intégration HTTP la politique de mot de passe, le verrouillage, la rotation des refresh tokens, l'uniformité des refus, l'absence de secrets dans les journaux et la propriété (7 tests). A teste l'autorisation par le médiateur, sans HTTP (opus 1.1-1.2, 1.5 ; sonnet 1.1-1.3).

Cohérence avec le but : B. Le socle transverse de sécurité est presque complet, et ses règles cassent tôt, au démarrage ou en test, au lieu de laisser passer. A laisse l'essentiel à l'équipe.

Divergences : opus ne mentionne pas les alertes de code scanning ni les réglages du dépôt B, que sonnet documente (1.7). Sonnet seul note que les routes de santé de A ne sont mappées qu'en Development (1.1) et que le message d'exception part dans Detail en 404 (1.4). Je retiens les faits de sonnet comme des réserves réelles sur B. Elles relèvent de l'hygiène du dépôt et de défauts corrigeables, pas de l'architecture de sécurité livrée : elles ne renversent pas l'écart.

### G4 · Front-ends

Lettres propres à G4.

VERDICT : B, mais l'axe est le plus disputé. C'est aussi là que les deux rédacteurs divergent le plus.

Lisibilité : B. B produit des squelettes officiels à version exacte (CLI Angular standalone, create-vite, create-react-router), un client typé partagé et une fonction unwrap qui lève une erreur avec un code stable (opus 2.1, 2.3 ; sonnet 2.1, 2.3). Le code de A est plus daté et bruité : Angular en NgModule, React en .jsx non typé qui importe un client .ts, un module serveur Angular mort, et oidc-client listé sans le paquet (opus 2.1, 2.4 ; sonnet 2.1, 2.7).

Code à écrire : A. A livre une vraie application d'exemple dans chaque framework : connexion, inscription, liste de tâches, météo (sonnet 2.2), lancée par Aspire avec découverte de services (sonnet 2.6). B dit lui-même « the pages are yours to write ». Aucun starter, sauf Blazor, n'a de page de connexion : il faut copier l'extrait de la doc (sonnet 2.2). Ajouter une app laisse environ dix éditions manuelles (opus 2.7). B épargne en revanche le code « autour » : Dockerfiles, CSP, relais, client. Mais pour avoir un front authentifié qui marche le premier jour, A demande moins de code.

Sûreté : B. B fournit une CSP par app, avec nonce de 128 bits et empreintes en SSR. Le cookie __Host-session est HttpOnly, Secure et SameSite. B exige l'en-tête X-CSRF, garde le jeton d'accès en mémoire, installe avec frozen-lockfile, refuse les scripts d'installation et lance ses images nginx en non root, épinglées par digest. Un check CI vérifie que le client correspond à l'API (opus 2.2-2.5, sonnet 2.3-2.5). Chez A : aucune CSP, script inline et polices Google, nswag en « latest », npm install à la publication, allowScripts sur 4 paquets, client généré non suivi par git (opus 2.3, 2.5 ; sonnet 2.3, 2.5).

Testabilité : égalité. Les manques des deux dépôts se complètent. A a 6 scénarios Playwright qui pilotent un navigateur sur des parcours utilisateur, mais ils ne couvrent qu'Angular. A n'a aucun test React et seulement 3 tests Angular, et l'installation des navigateurs passe par un script PowerShell (opus 2.6, sonnet 2.6). B a 67 tests vitest du client partagé et des tests de bout en bout HTTP sur la pile (relais, CSP, nonce, CSRF, retour de connexion). Mais B ne pilote aucun navigateur, et sa commande de vérification locale ne lance pas le lint ni les tests web (opus 2.6). Aucun des deux ne couvre à la fois les parcours et les propriétés de sécurité.

Cohérence avec le but : B, de peu. Le but favorise un socle transverse complet et des règles qui protègent. B l'offre : 8 frameworks, SSR, client unique, déploiement durci, clients Kiota pour .NET. Mais B a des surprises au premier usage, relevées par sonnet : le bootstrap a besoin du réseau, un clone n'a pas de front avant le bootstrap, et un job CI échoue avec « holds no app » (2.2, 2.7). Des chemins de 176 caractères bloquent les commits sous Windows (2.7), et les sessions SSR sont en mémoire (opus 2.4, sonnet 2.4). A a aussi des surprises : le mauvais port d'API sous Aspire, et une version publiée en Angular 21 avec Karma qui diffère de la branche principale (opus 2.1-2.2).

Divergence entre rédacteurs et mon choix : opus met en avant l'étendue et le durcissement de B et les faiblesses de socle de A. Sonnet insiste sur ce que B ne livre pas : pages, connexion, front présent dès le clone. Il crédite A d'une app d'exemple complète et d'une boucle Aspire. Les faits ne se contredisent pas, ils sont pondérés différemment. Je les garde tous les deux. C'est pourquoi je donne le critère « code à écrire » à A et la testabilité à égalité. Je donne quand même l'axe à B, parce que le but demande de ne pas s'occuper de « ce qu'il y a autour ». Les pages de connexion et d'exemple sont plus faciles à écrire pour une équipe que la CSP à nonce, la protection CSRF, le client typé vérifié en CI et les images durcies. Si l'équipe privilégie un premier usage immédiat et sans réseau, l'avantage de B se réduit fortement.

