# ADR-002 : Arbitrages métier du dashboard de risque de contrepartie

- **Statut** : **Accepté** (décisions métier, PO et Expert CCR, 2026-11-04). Références réglementaires **sous réserve** de la confrontation à EUR-Lex (section « Points à confronter à EUR-Lex »), à faire avant l'implémentation définitive des tickets concernés.
- **Date** : 2026-11-04
- **Décideurs** : Product Owner (sponsor métier), Expert CCR (validation métier), Lead (rédaction)
- **Issue** : #6 ; spécification des financements : #45 ; gouvernance des versions de méthode : #52 (amendement du 2026-11-11)
- **Tickets impactés** : #5, #20, #21, #22, #23, #24, #25, #26, #27, #28, #29, #30, #32 à #37, #41, #43, #44, #45

Légende de nature : **[R]** réglementaire · **[M]** pratique de marché · **[H]** hypothèse du projet.

## Contexte

L'expression des besoins (EDB, document local non versionné) laissait plusieurs points ouverts : définition du « Gross Commitment », séparation des visions économique et réglementaire, maintien de Bâle 1 / CEM, quantile et horizons de la PFE, règle d'allocation sous le netting set, périmètre XVA, approche de capital CVA, scénarios de stress. Elle laissait aussi implicites la devise de reporting, la mesure des limites, les grands risques, l'output floor et les seuils d'alerte.

Le Lead a proposé des arbitrages dans #6. L'Expert CCR les a confrontés aux textes (vérification du 2026-11-04) et a corrigé plusieurs références et formulations. Le PO a tranché les deux questions restantes le 2026-11-04 : les financements tirés et non tirés entrent dans le Gross Commitment, et l'allocation Euler accepte les contributions négatives. L'Expert CCR a ensuite spécifié les financements (CCF CRR3, transitoire de l'art. 495d, base du non tiré, classement hors bilan) dans #45.

Cet ADR consigne les décisions **finales**, c'est-à-dire corrigées par l'Expert CCR. Il ne reprend pas la proposition initiale lorsqu'elle a été corrigée.

Règles structurantes de l'EDB, que toutes les décisions respectent :

- un seul quantile de PFE pour tout le dashboard ;
- jamais de mesure économique (EE, EPE, PFE) et de mesure réglementaire (EAD, RWA) dans un même graphe ;
- un niveau d'exposition explicite sur chaque montant ;
- PFE, EEPE et EAD sont non additives ; une règle d'allocation unique, stable et documentée s'applique sous le netting set ;
- un historique recalculé à méthode constante, les changements de méthode étant seulement annotés ;
- les benchmarks externes (BIS, EBA, FR Y-9C) sont affichés tels que publiés, jamais sur la même courbe que les séries recalculées ;
- chaque valeur porte sa source, sa date d'arrêté et son statut (réel, transformé ou synthétique), selon les principes BCBS 239.

## Décision

### Principe transverse : paramètres versionnés

Aucun paramètre méthodologique ou réglementaire n'est une constante dans le code. Deux familles de configuration typée, en lecture seule à l'exécution :

- `RiskMethodologyOptions` (#5) : choix de méthode du projet, portés par la `MethodVersion` (#26) ;
- `RegulatoryParameterSet` (#23) : paramètres réglementaires datés (output floor, BA-CVA, CCF, transitoires, grands risques).

Toute modification passe par une PR revue par le Lead et l'Expert CCR, crée une nouvelle `MethodVersion` et relance le recalcul de tout l'historique. Aucun endpoint d'administration ne les modifie. Le hash des paramètres est stocké avec chaque résultat.

**Gouvernance des versions de méthode [H]** (décision du PO du 2026-11-05, capacités validées par l'Expert CCR dans #52) :

- chaque version est définie dans un **catalogue de versions** livré dans le code (`MethodVersionCatalog`) : identifiant, paramètres, `parameters_sha256`, `regulatory_reference_date`. Son contenu n'arrive que par une PR revue ;
- `PublishMethodVersionCommand` (capacité `ManageMethodVersions`, risk manager) **ne porte qu'un identifiant de version**, jamais de paramètre. Le handler charge la définition depuis le catalogue et contrôle son hash ; la commande ne fait que créer la version `Computing` et mettre le recalcul en file, exécuté par le scheduler (`HistoryRecalculationJob`, identité `cr_scheduler`) ;
- cycle de vie : `Computing` → `PendingApproval` (recalcul complet et contrôles DQ réussis) → `Active` ou `Rejected` ; `Retired` pour l'ancienne version active. Aucun résultat d'une version n'est visible avant son approbation ;
- la bascule n'est **plus automatique** : `ApproveMethodVersionActivationCommand` (capacité `ApproveMethodVersion`, direction), avec un `sub` différent de celui de la publication (quatre yeux). Après approbation, le pointeur `meta.active_method_version` bascule en une seule transaction ; `cr_scheduler` n'a aucun droit d'écriture sur ce pointeur ;
- les résultats des versions `Retired` sont conservés ; le retour arrière vers une version `Retired` suit le même circuit à quatre yeux ;
- **recalcul ciblé après correction d'un snapshot** (décision du PO du 2026-11-05) : `RequestHistoryCorrectionCommand` (capacité `ManageHistoryCorrections`, risk manager) porte une date d'arrêté de la version active et un code motif issu d'une liste fermée, sans paramètre libre, une fois le snapshot corrigé clôturé avec un nouvel `inputs_sha256`. `ApproveHistoryCorrectionCommand` (capacité `ApproveHistoryCorrection`, direction, `sub` différent du demandeur) l'approuve ou la rejette ; seule l'approbation met en file le recalcul de cette date, à version constante, exécuté par le scheduler. L'approbation crée une **annotation obligatoire dans W12** (`meta.risk_event` de type `HistoryCorrection` : date, motif, demandeur, approbateur) : un point corrigé n'est jamais affiché sans elle ;
- règles d'autorisation et menaces : threat model §7.1, règles R9 à R13, TM-TB3-14 et TM-TB3-15.

### 1. Gross Commitment, y compris les financements

**Décision [H]** : le Gross Commitment n'est pas un indicateur Bâle/CRR. C'est un **titre de section** (info-bulle « regroupement interne, non défini par Bâle/CRR ») qui regroupe des mesures standards de deux natures, `ExposureNature { Ccr, Financing }`. Chaque montant porte obligatoirement sa **nature** et son **niveau**.

| Bloc | Mesures | Niveau | Base |
| --- | --- | --- | --- |
| CCR (dérivés OTC, ETD, SFT) | Notionnel brut Σ\|N_i\| (volume, pas une exposition) | `Notional` | [M] |
| | Exposition brute avant CRM : dérivés Σ max(V_i, 0) (code de mesure `GrossPositiveMtm`) ; SFT Σ E_i, espèces ou titres remis avant collatéral reçu (code de mesure `GrossSftExposure`) | `GrossBeforeCrm` | [M] |
| | EAD CCR : dérivés en SA-CCR ; SFT en méthode générale fondée sur les sûretés financières (FCCM, art. 223 ; art. 220 pour les netting sets sous accord-cadre, sous réserve de vérification), jamais en SA-CCR | `Ead` | [R] art. 271-274, 223, 220 ; choix FCCM [H] (art. 111(6)) |
| Financement (prêts, lignes, engagements par signature) | Tiré = capital restant dû + intérêts courus (valeur comptable brute) | `Drawn` | [R] art. 110, 111(1) |
| | Non tiré = max(limite engagée − Σ capital tiré, 0), **hors intérêts courus** ; 0 si la ligne est gelée ou annulée, ou si la période de disponibilité est échue | `Undrawn` | [R] art. 111(2), annexe I |
| | Engagement total = tiré + non tiré | `TotalCommitment` | [M] |
| | EAD hors bilan = CCF effectif × max(non tiré − SCRA hors bilan, 0) | `Ead` | [R] art. 111(2)-(3) |
| | EAD financement = max(tiré − SCRA bilan, 0) + EAD hors bilan | `Ead` | [R] art. 111(1)-(2) |

Règles de restitution [H] :

- deux blocs côte à côte, jamais fusionnés ; la cascade des financements est un graphe distinct de la cascade CCR de W2 ;
- seuls l'**EAD** et la **valeur exposée grands risques** s'agrègent entre natures, toujours en barres empilées par nature avec le total ;
- agrégats interdits : notionnel + engagement total, MtM brut + tiré, toute mesure économique (EE, EPE, PFE) avec une mesure de financement, IMM simulé avec une EAD réglementaire ;
- l'EAD financement est additive par facilité et n'utilise pas Euler ;
- W1 : la tuile « EAD totale » affiche la décomposition CCR / financement dans une seule tuile (toujours 5 tuiles au maximum, règle EDB) ; « RWA total CCR + CVA » ne change pas ;
- en V1, les financements n'alimentent ni les limites (W4) ni les RWA (W6) ; une limite de crédit de groupe sur l'engagement total est envisagée en V2.

**Niveau `GrossBeforeCrm` [H]** (question soulevée dans #45, tranchée ici) :

- `ExposureLevel.GrossBeforeCrm` désigne l'étape « brut, avant netting et avant collatéral » de la chaîne de mitigation, première des étapes à afficher (brut → après netting → après collatéral → EAD). Un niveau décrit un stade de mitigation, pas un instrument.
- `GrossPositiveMtm` n'est plus un niveau : c'est le **code de mesure** des dérivés (terme BIS « gross positive market value »).
- Les SFT ont leur propre code de mesure, `GrossSftExposure` (Σ E_i), car un E_i n'est pas un MtM.
- Libellé affiché : « Exposition brute avant CRM (avant netting et collatéral) ».
- La première marche de la cascade W2 (#20) est au niveau `GrossBeforeCrm`, mesure `GrossPositiveMtm`. Le critère de #45 devient « 130, mesure `GrossPositiveMtm`, niveau `GrossBeforeCrm`, nature `Ccr` ».
- Garde d'agrégation : `GrossBeforeCrm` ne s'agrège jamais avec `Drawn` (interdiction « MtM brut + tiré » inchangée).

**Critères caducs de la proposition initiale** : « financement rejeté hors périmètre CCR » et « Gross Commitment seulement dans le glossaire ». Les financements sont routés vers le modèle de #43.

### 2. RCI : vision économique et vision réglementaire

**Décision** : deux visions séparées, jamais dans un même graphe.

- **Économique [H]** : EE, EPE, PFE, ENE par Monte-Carlo (#22).
- **Réglementaire [R]** : EAD SA-CCR (art. 274-280f ; EAD = 1,4 × (RC + multiplicateur × AddOn), art. 274(2)). L'IMM exige une autorisation de l'autorité compétente (art. 283), que la banque fictive n'a pas.
- **[H]** Le libellé **« IMM simulé, non approuvé »** est obligatoire sur tout ce qui dérive de l'EEPE : EEPE, EEPE stressée (W11), écart SA-CCR vs IMM (W5), RWA IMM et U-TREA de l'output floor (#23), y compris dans les exports. Un RWA IMM n'est jamais présenté comme réglementaire.

### 3. Bâle 1 / CEM

**Décision** : retirés du calcul et de l'axe « Approche réglementaire ».

- **[R]** La CEM a été supprimée par CRR2 (règlement (UE) 2019/876), applicable le 28/06/2021. CRR2 a introduit deux méthodes allégées : la SA-CCR simplifiée (art. 281, seuils de l'art. 273a(1) : 10 % du total des actifs et 300 M€) et la méthode de l'exposition initiale (OEM, art. 282, seuils de l'art. 273a(2) : 5 % et 100 M€). Bâle 1 n'a aucune portée en UE.
- **[H]** L'historique est recalculé à méthode constante : un moteur CEM n'apporte rien. La profondeur d'historique (5 ans en mensuel) commence après la bascule SA-CCR.
- Axe « Approche réglementaire » : `SA-CCR` et `IMM simulé` pour le CCR, `SA crédit (art. 111)` pour les financements, `Fccm` pour l'EAD des SFT. La valeur `CEM` disparaît.
- Marqueurs d'événements W12 : « CEM → SA-CCR, 28/06/2021 », « CRR3, 01/01/2025 », « CRR3 CCF, 01/01/2025 », « phase-in UCC 2030-2032 ».

### 4. Quantile et grille d'horizons de la PFE

**Décision [H]** : PFE à **97,5 %**, unilatérale, unique pour tout le dashboard (W1 tuile « PFE max », W3, W4, W11, PFE stressée).

- **[R]** Aucun texte n'impose le quantile de la PFE : c'est une mesure de gestion. **[M]** 95 %, 97,5 % et 99 % coexistent sur le marché ; 97,5 % est un choix courant. L'argument d'une cohérence avec l'Expected Shortfall FRTB est **retiré** (l'ES est une moyenne de queue de pertes, pas un quantile d'exposition).
- Le quantile est porté par la `MethodVersion`. La validation au démarrage compare la `MethodVersion` active **à sa propre définition dans le catalogue de versions** (quantile et `parameters_sha256`) et échoue en cas d'écart. Elle ne compare pas la version active à un quantile configuré globalement : pendant le recalcul d'une nouvelle version (`Computing`), puis pendant son attente d'approbation, l'ancienne version reste active avec son propre quantile et un tel contrôle échouerait. Chaque calcul lit le quantile de la version qu'il produit. Changer de quantile crée une nouvelle `MethodVersion` et relance le recalcul de l'historique. La liste {95 % ; 97,5 % ; 99 %} ne sert qu'aux tests.
- **Grille d'horizons** (celle de #6 fait foi, #22 s'y aligne), en horizons **relatifs** à la date d'arrêté, identiques sur tout l'historique : quotidienne jusqu'à 2 semaines, hebdomadaire jusqu'à 3 mois, mensuelle jusqu'à 1 an, trimestrielle jusqu'à 5 ans, annuelle ensuite jusqu'à la maturité maximale, plafond 30 ans. S'y ajoutent les points `t − MPOR` et les dates d'échéance et de flux.
- **[R]** La maille fine sur 1 an sert l'Effective EE (art. 284(5)) et l'Effective EPE (art. 284(6), moyenne sur la première année). La MPOR est d'au moins 10 jours ouvrés hors SFT (art. 285(2)).

### 5. Allocation sous le netting set : Euler avec contributions négatives

**Décision** : allocation **Euler**, contributions négatives acceptées, **sans plancher à zéro** (décision PO). Règle identique sur tout l'historique.

- **[R]** Aucune règle n'est imposée : l'EAD et l'EEPE sont définies au netting set (art. 272(4), 274, 284). **[M]** Euler est la seule allocation compatible avec la mesure de performance ajustée du risque (RORAC-compatible, Tasche) pour une mesure homogène de degré 1. Formulation corrigée : Euler n'est pas « la seule allocation additive », le pro rata l'étant aussi.
- **[H] Homogénéité** : le collatéral est mis à l'échelle avec les transactions (traité comme une pseudo-position et alloué), pour que l'EAD SA-CCR soit homogène de degré 1. Le résidu se limite aux points de non-différentiabilité (RC = 0, plancher du multiplicateur), traités par dérivée à droite documentée.
- **EE et CVA** : contribution exacte `E[V_i · 1{V − C > 0}]` par transaction, et `−E[C · 1{V − C > 0}]` pour la pseudo-position de collatéral ; leur somme vaut `E[max(V − C, 0)]`, exactement additive. La CVA, linéaire en EE, l'est aussi.
- **EEPE** : allocation via les contributions à l'EE aux dates où l'Effective EE est fixée (opérateur max), selon une règle documentée.
- **PFE** : la PFE est le quantile q de l'exposition **nette du collatéral** `E = max(V − C, 0)`. L'estimateur conditionne sur ce quantile, et non sur `V` brut : contribution de la transaction i = `E[V_i | V − C = PFE_q]`, contribution de la pseudo-position de collatéral = `−E[C | V − C = PFE_q]`, de sorte que la somme vaut `PFE_q` (lorsque `PFE_q > 0`). Le collatéral est donc alloué de façon cohérente avec sa mise à l'échelle (même pseudo-position que pour l'EAD et l'EE). Espérance conditionnelle estimée par noyau autour du quantile, largeur de fenêtre fixée dans la `MethodVersion`, graine identique à celle du run, erreur standard publiée.
- **RWA** : contribution à l'EAD × pondération de la contrepartie.
- **Invariant** : `Σ contributions + non alloué = total`. La ligne « non alloué » est toujours calculée. Elle est affichée dès que le résidu dépasse 0,1 % du total du netting set, seuil porté par la `MethodVersion` [H]. La méthode du plus grand reste ne sert qu'aux arrondis et ne masque jamais un résidu Euler.
- Repli **pro rata** des montants stand-alone implémenté mais **inactif**.
- Euler ne s'applique qu'au bloc CCR. Les widgets empilés (#25, #32 à #37) gèrent les valeurs négatives.

### 6. XVA hors CVA/DVA

**Décision** : V1 limitée à **CVA et DVA** ; **FVA, MVA et KVA en V2**.

- **[R]** CVA et DVA relèvent de la juste valeur (IFRS 13). FVA, MVA et KVA ne sont pas des exigences réglementaires.
- **[R]** La DVA est affichée comme **mesure comptable** uniquement : les gains de juste valeur liés au risque de crédit propre sur les passifs dérivés sont déduits du CET1 (art. 33(1)(c)). Jamais présentée comme un gain de capital.
- **[H]** Aucune source ouverte ne fournit les courbes de financement, l'IM SIMM projetée ni le coût du capital.
- **[H]** La PD proxy (notation × table de PD) n'est pas implicite du marché : W7 affiche « CVA proxy (PD non implicite du marché) ».

### 7. Capital CVA : BA-CVA réduite

**Décision [H]** : **BA-CVA réduite** (sans couvertures) en V1 ; **BA-CVA complète** uniquement si des CDS sont désignés couvertures CVA éligibles ; **SA-CVA exclue**.

- **[R] Références corrigées** : choix d'approche à l'art. 382a ; SA-CVA (art. 383) sur autorisation seulement ; BA-CVA à l'art. 384, qui distingue la version avec couvertures éligibles (§2) de la version sans couverture (§3) et interdit de les combiner ; approche simplifiée (art. 385) non combinable ; couvertures éligibles à l'art. 386.
- **[R] Paramètres BA-CVA** : DS = 0,65, β = 0,25, ρ = 0,5, α = 1,4.
- **[R] Correction** : M_NS se calcule selon l'art. 162, plafonnée à la **plus longue maturité contractuelle résiduelle du netting set**. DF_NS = 1 pour une EAD IMM, sinon (1 − e^(−0,05·M)) / (0,05·M).
- **[R] Périmètre (art. 382)** : dérivés OTC ; SFT seulement si l'exposition CVA est significative (art. 382(2)) ; exclusion des transactions avec une QCCP (art. 382(3)) et des exemptions UE de l'art. 382(4). Indicateur « CVA exemptée (notionnelle) » pour le calcul notionnel déclaré (art. 382(4b)).
- **[R]** Le calendrier CVA de CRR3 s'applique depuis le 01/01/2025 ; le report de la FRTB au 01/01/2027 ne le concerne pas.
- **[H]** Un CDS acheté reçoit une **désignation exclusive** : couverture CRM par substitution (art. 235, #29), couverture CVA éligible (art. 386, #23), ou aucune.

### 8. Catalogue de stress (W11)

**Décision [H]** : un **catalogue unique et versionné**, aligné entre #6 et #28.

| Famille | Scénarios |
| --- | --- |
| Historiques | `HIST-GFC-2008`, `HIST-EUROSOV-2011`, `HIST-COVID-2020`, `HIST-RATES-2022` (choc de taux EUR, à la place de LDI/gilts, faute de séries ECB sur les taux gilts) |
| Réglementaire | `EBA-2025-ADVERSE`, version **figée** ; W11 n'en reprend que les chocs de marché instantanés, avec un libellé qui le précise |
| Hypothétiques | `HYP-IR-UP-200`, `HYP-IR-DOWN-200` (plancher documenté), `HYP-EUR-DOWN-15`, `HYP-EUR-UP-15`, `HYP-CREDIT-X2` |
| Compléments | Scénarios mono-facteur de #28 (actions, matières premières, crédit ×3), dans le même catalogue versionné |

- Le scénario EBA est figé et non « le dernier » : l'exercice 2025 (scénario conçu par l'ESRB, horizon 2025-2027) reste la référence. L'intégration de l'exercice suivant passe par une nouvelle version du catalogue avec recalcul.
- **[R] Références corrigées** : les art. 290 (stress tests) et 291 (WWR) relèvent de la Section 6 (IMM) et ne s'imposent qu'à un établissement IMM ; pour un établissement SA-CCR, l'exigence relève du Pilier 2. **[M]** Les « Guidelines for counterparty credit risk management » du BCBS (d588, finales le 11/12/2024) en font une bonne pratique.
- **[R]** EEPE stressée : art. 284(3) et art. 292(2), second alinéa (et non art. 284(4), qui fixe α = 1,4).
- **[H]** Hypothèses libellées comme telles : fenêtre de stress de l'EEPE 2007-2009 (ou équivalent), seuils de WWR (ρ > 0,3, ratio > 1,25). Les définitions de ρ (corrélation exposition / qualité de crédit) et du ratio figurent dans #28.
- Les scénarios sont des fichiers de données validés par schéma (bornes sur les chocs), sans expression exécutable.

### 9. Devise de reporting

**Décision [H]** : **EUR**, avec les taux de référence ECB de la date d'arrêté et la mention « Source: ECB statistics. ».

- Jour sans fixing : dernier taux publié, tracé.
- Devise non couverte : cross via USD, lignée `Transformed`.
- Les taux ECB sont publiés à titre d'information : acceptable pour un usage de démonstration, signalé dans l'aide.
- Revolving multidevise : le capital de chaque tirage est converti dans la devise de la limite au taux ECB de la date d'arrêté pour calculer le non tiré.

### 10. Mesure des limites

**Décision [H]** : **une seule mesure par limite**. Limites contrepartie et groupe en **PFE (pic)**.

- La PFE de contrepartie ou de groupe s'obtient en sommant l'exposition **par chemin** sur les netting sets, puis en prenant le quantile ; jamais en sommant des quantiles.
- Le quantile vient de la `MethodVersion` ; ce n'est pas un attribut de la limite.
- **[M]** Une limite par tranche de maturité (profil de limite) est une évolution possible en V2.
- Une limite PFE (économique) et un ratio de grands risques (réglementaire) ne figurent jamais dans le même graphe. W10 (Top N, HHI, répartitions) : une base unique au choix parmi l'EAD CCR, l'EAD totale (décomposée par nature), la valeur exposée grands risques, ou le pic de PFE (CCR seul, vision économique). Jamais deux mesures dans un même graphe, jamais la PFE avec des financements.

### 11. Grands risques

**Décision** :

- **[R]** Limite de 25 % du Tier 1 après CRM (art. 395(1)), 15 % entre G-SII ; un grand risque commence à 10 % du Tier 1 (art. 392).
- **[R] Correction** : la valeur exposée des dérivés suit l'**art. 390(4)** (Sections 3, 4 ou 5 du Chapitre 6 : SA-CCR, SA-CCR simplifiée, OEM). **Jamais l'IMM.** La référence à l'art. 429c (ratio de levier) est retirée.
- **[R]** Financements : valeur exposée grands risques calculée **après SCRA** = max(tiré − SCRA bilan, 0) + f_LE × max(non tiré − SCRA hors bilan, 0), avec le hors bilan à **100 % du nominal** (f_LE = 1), sans CCF (art. 389), sauf exemptions : non tiré de catégorie 5 exempté (art. 400(1)(i)) ; discrétion de l'art. 400(2)(i) (50 % pour la catégorie 4 et la catégorie 3 d'échéance initiale ≤ 1 an) **prévue en paramètre, désactivée par défaut** [H]. Le projet suit le CRR et non LEX30. **[H]** Une offre non acceptée entre à 100 % du nominal, sauf exemption UCC (hypothèse prudente).
- **[R]** CRM selon les art. 399-403 ; expositions de transaction sur QCCP exemptées (art. 400(1)(j)).
- W10 lit la **valeur exposée grands risques** (`LargeExposureValue`), jamais l'EAD de solvabilité ; décomposition par nature obligatoire.
- **[H]** Le groupe de clients liés (art. 4(1)(39)) recouvre contrôle et interdépendance économique ; GLEIF ne couvre que le contrôle, limite signalée dans l'aide de W10. Le Tier 1 de la banque fictive est un paramètre synthétique versionné.

### 12. Output floor

**Décision [R]** : facteur x de 72,5 % (art. 92(3)) ; par dérogation, 50 / 55 / 60 / 65 / 70 % de 2025 à 2029 (art. 465(1)). En paramètres versionnés.

| Année | 2025 | 2026 | 2027 | 2028 | 2029 | À partir de 2030 |
| --- | --- | --- | --- | --- | --- | --- |
| Facteur x | 50 % | 55 % | 60 % | 65 % | 70 % | 72,5 % |
| Base | art. 465(1) | art. 465(1) | art. 465(1) | art. 465(1) | art. 465(1) | art. 92(3) |

- Plafond transitoire de 125 % du U-TREA jusqu'au 31/12/2029 : dérogation de l'art. 465(2), **activée par défaut** [H], paramètre `applied` dans le `RegulatoryParameterSet` (sur le modèle de l'art. 495d).
- Transitoires du S-TREA (art. 465(3), (5), (9) et (13)) jusqu'à fin 2032.
- Formulation corrigée : montée en charge jusqu'en 2030, dispositions transitoires jusqu'à fin 2032.
- **Point de veille** : la communication de la Commission du 17/07/2026 sur la compétitivité bancaire annonce des propositions sur l'output floor et ses transitoires, attendues début 2027. Le calendrier en vigueur est inchangé, d'où un `RegulatoryParameterSet` versionné.
- **[H]** Sans IRB ni IMM approuvée, le U-TREA CCR égale le S-TREA. L'impact affiché est **illustratif** (« si l'IMM était approuvée »), sous le libellé « IMM simulé, non approuvé ».

### 13. Seuils d'alerte

**Décision** : seuil d'**alerte** paramétrable, défaut **80 %** de la limite, contrainte 0 < alerte < 100 % [M pour le niveau, H pour la règle]. Le **dépassement** commence à **100 %** de la limite, fixe. `Dépassement = max(Exposition − Limite, 0)`. Un niveau d'escalade supérieur à 100 % peut s'ajouter sans redéfinir le dépassement. La borne « dépassement ≤ 150 % » de #27 est supprimée.

Le même seuil d'alerte (défaut 80 %) s'applique au ratio grands risques rapporté à la limite de l'art. 395(1) : alerte à 20 % du Tier 1, dépassement à 25 %.

### 14. CCF et classement hors bilan des financements

**Décision [R]** : approche standard CRR3 (art. 111(2) et annexe I). La banque fictive n'a aucune autorisation IRB [H].

| Catégorie | CCF | Exemples |
| --- | --- | --- |
| 1 | 100 % | Garanties à caractère de substitut de crédit, acceptations, achats d'actifs à terme |
| 2 | 50 % | Engagements liés à des transactions (garanties de bonne fin, cautions de soumission ; catégorie 4 si financement du commerce), facilités d'émission (NIF, RUF) |
| 3 | 40 % | Engagements non classés ailleurs, dont les lignes non tirées, quelle que soit leur maturité |
| 4 | 20 % | Financement du commerce à court terme et auto-liquidatif |
| 5 | 10 % | Engagements révocables sans condition (UCC) |

- **[R]** Art. 111(3) : un engagement sur un autre élément hors bilan prend le **plus bas** des deux CCF (résolution récursive, détection de cycle, profondeur ≤ 3 [H]).
- **[R]** Art. 111(4) : une offre contractuelle non encore acceptée se traite comme un engagement ; statut `FacilityStatus.Offered`, catégorie du produit une fois accepté (C3 par défaut, C5 seulement si UCC sans facteur de contrainte). Une offre non acceptée prévoyant un montant à tirer obligatoirement reste en C3. Une fois acceptée, le montant qui doit être tiré passe en C1, le reste en C3 [R par anticipation, EBA/RTS/2025/06]. Les accords de principe ne sont pas modélisés [H]. Le cas à 0 % de l'art. 5(10)(a)-(e) n'est pas généré en V1 [H].
- **Transitoire UCC (art. 495d)** : le CCF de 10 % est multiplié par un facteur.

  | Période | Facteur | CCF effectif UCC |
  | --- | --- | --- |
  | 01/01/2025 – 31/12/2029 | 0 % | 0 % |
  | 2030 | 25 % | 2,5 % |
  | 2031 | 50 % | 5 % |
  | 2032 | 75 % | 7,5 % |
  | À partir du 01/01/2033 | n/a | 10 % |

  Le texte ouvre une **faculté** (« may ») que le projet active par défaut [H], [M] ; c'est signalé dans l'aide. L'EBA doit remettre d'ici le 31/12/2028 un rapport sur une prolongation au-delà de 2032 (art. 495d(2)) ; la Commission peut légiférer d'ici le 31/12/2031. Le CCF affiché porte son origine, par exemple « CCF 0 % (UCC, transitoire art. 495d) », pour ne pas confondre un 0 transitoire avec une absence de risque.
- **Date de référence du CCF [H]** : la date de référence réglementaire de la `MethodVersion` (date du recalcul), et non la date d'arrêté ; tout l'historique W12 est recalculé avec le CCF en vigueur à cette date. Le franchissement d'une marche du calendrier (par exemple au 01/01/2030) impose une nouvelle `MethodVersion` ; un contrôle DQ (#19) le signale. **L'approbation est refusée** (`ApproveMethodVersionActivationCommand`, #26) si une marche du calendrier réglementaire (art. 495d, output floor) est franchie après la `regulatory_reference_date` de la version, c'est-à-dire entre cette date et la date d'approbation : la version serait active avec un CCF déjà périmé ; il faut publier une nouvelle version (#52).
- **Classement UCC [M]** : le projet applique **par anticipation** le projet final de RTS de l'EBA sur l'art. 111(8) (EBA/RTS/2025/06, remis le 18/08/2025, « not yet applicable »), sous `ClassificationRuleVersion = 1`. Catégorie 5 si et seulement si l'engagement est contractuellement annulable sans condition (art. 5(10), annexe I cat. 5) **et** qu'aucun facteur de contrainte n'est identifié. `CancellationConstraint` = { `None`, `RiskManagementDeficiency`, `CommercialConsideration`, `ReputationalRisk`, `LitigationRisk` }. Si un facteur est identifié, l'élément prend la catégorie qu'il aurait sans le droit d'annulation (catégorie 3 pour une ligne de crédit). Une valeur inconnue ou manquante n'est jamais lue comme `None` : reclassement conservateur en catégorie 3 avec `FinancingDataIssue`. À la publication au JOUE, relecture par l'Expert CCR, version 2 si le texte diffère et recalcul de l'historique.
- **Crédit documentaire et garanties** (attribut `is_trade_finance` : échéance initiale ≤ 1 an, sans renouvellement automatique, sous-jacent biens ou services, art. 4(1)(80)) :
  - lettre de crédit à caractère de substitut de crédit : C1 (100 %) [R] ;
  - crédit documentaire émis ou confirmé avec `is_trade_finance` : C4 (20 %) [R] ;
  - crédit documentaire hors financement du commerce, sans caractère de substitut de crédit : C2 (50 %), défaut du projet [M], [H] ;
  - ligne de crédit documentaire : C3 sur la ligne, puis minimum des CCF (art. 111(3)) [R] ;
  - `PerformanceGuarantee` : C4 si `is_trade_finance`, sinon C2 [R].
- **IRB, pour mémoire [R]** : CRR3 impose en IRB-F les CCF de l'approche standard (art. 166(8a)) ; les CCF propres sont réservés aux engagements revolving (art. 166(8b)), avec un plancher (art. 166(8c)). Non appliqué par le projet.
- **Rupture de séries [R]** : les CCF CRR3 remplacent ceux de CRR2 (0 / 20 / 50 / 100 %). Seuls les arrêtés publiés à partir du T1 2025 servent au calibrage (#44).

## Paramètres versionnés

| Paramètre | Valeur par défaut | Porteur | Nature |
| --- | --- | --- | --- |
| `PfeQuantile` | 0,975, unilatéral | `RiskMethodologyOptions`, `MethodVersion` | [H] |
| `ExposureHorizonGrid` | Grille relative du §4, plafond 30 ans, + `t − MPOR`, échéances et flux | `RiskMethodologyOptions` | [H] |
| `AllocationRule` | `Euler`, contributions négatives, repli pro rata inactif | `RiskMethodologyOptions` | [H] |
| Largeur du noyau PFE (Euler) et graine | À fixer dans la `MethodVersion` | `MethodVersion` | [H] |
| `CvaCapitalApproach` | BA-CVA réduite ; complète si couvertures CVA désignées | `RiskMethodologyOptions` | [H] |
| Paramètres BA-CVA | DS = 0,65, β = 0,25, ρ = 0,5, α = 1,4 | `RegulatoryParameterSet` | [R] |
| `StressScenarioSet` | Catalogue du §8, `EBA-2025-ADVERSE` figé | Fichiers de données versionnés | [H] |
| Fenêtre EEPE stressée, seuils WWR | 2007-2009 ; ρ > 0,3, ratio > 1,25 | `StressScenarioSet` | [H] |
| `ReportingCurrency` | EUR, taux ECB de la date d'arrêté | `RiskMethodologyOptions` | [H] |
| `LimitAlertThresholds` | Alerte 80 % (0 < alerte < 100 %) ; dépassement fixe à 100 % | `RiskMethodologyOptions` | [M], [H] |
| Calendrier output floor | 50 / 55 / 60 / 65 / 70 % (2025 à 2029, art. 465(1)), puis 72,5 % (art. 92(3)) | `RegulatoryParameterSet` | [R] |
| Plafond output floor | 125 % du U-TREA jusqu'au 31/12/2029, `applied = true` | `RegulatoryParameterSet` | [R], dérogation activée [H] |
| Seuil d'affichage du « non alloué » (Euler) | 0,1 % du total du netting set | `MethodVersion` | [H] |
| Seuils grands risques | 10 %, 25 % (15 % entre G-SII) du Tier 1 | `RegulatoryParameterSet` | [R] |
| Tier 1 de la banque fictive | Valeur synthétique | `RegulatoryParameterSet` | [H] |
| CCF par catégorie | 100 / 50 / 40 / 20 / 10 % | `credit-sa-ccf.crr3.v1.json` (`RegulatoryParameterSet`) | [R] |
| Transitoire art. 495d | Facteurs 0 / 25 / 50 / 75 % (2025-2029 / 2030 / 2031 / 2032), `applied = true` | `credit-sa-ccf.crr3.v1.json` | [R], faculté activée [H] |
| Options grands risques des financements | `art400_1_i_uccExempt = true`, `art400_2_i_enabled = false`, facteur 50 % | `credit-sa-ccf.crr3.v1.json` | [R], [H] |
| Date de référence réglementaire | Date du recalcul | `MethodVersion` (`regulatory_reference_date`) | [H] |
| `ClassificationRuleVersion` | 1 (EBA/RTS/2025/06, projet final) | Règle de classement hors bilan | [M] |

## Conséquences

**Positives**

- Une décision unique par sujet, avec sa nature, sa base réglementaire et sa clé de configuration.
- Une décision finale différente ne change qu'un paramètre versionné, sans modification de code.
- Les visions économique et réglementaire, ainsi que les natures CCR et financement, restent séparées par construction (garde d'agrégation dans le domaine et l'API).

**Négatives et risques**

- Le périmètre s'élargit au risque de crédit (financements), avec de nouveaux tickets (#43, #44, #45) et des axes supplémentaires (catégorie hors bilan, classe d'exposition SA de l'art. 112).
- Euler demande un effort de calcul et d'explication ; les contributions négatives doivent être lisibles dans les widgets empilés.
- Chaque marche du calendrier art. 495d ou de l'output floor impose une nouvelle `MethodVersion` et un recalcul de l'historique.
- Plusieurs textes restent à confirmer (section suivante) ; la RTS UCC et la révision annoncée de l'output floor peuvent faire évoluer les paramètres.

**Impacts sur les tickets**

- #5 : `RiskMethodologyOptions` ; `ExposureLevel` += `GrossBeforeCrm` (remplace l'usage de `GrossPositiveMtm` comme niveau), `Drawn`, `Undrawn`, `TotalCommitment`, `LargeExposureValue` ; codes de mesure `GrossPositiveMtm` (dérivés) et `GrossSftExposure` (SFT) ; `ExposureNature` ; `RegulatoryApproach` += `SaCreditRisk`, `Fccm`.
- #20 : première marche de la cascade W2 au niveau `GrossBeforeCrm`, code de mesure `GrossPositiveMtm` ; libellé « Exposition brute avant CRM (avant netting et collatéral) ».
- #21 : SA-CCR seule approche réglementaire des dérivés ; EAD des SFT en FCCM (art. 223 ; art. 220 pour les netting sets sous accord-cadre, sous réserve de vérification).
- #22 : grille du §4, quantile validé contre la `MethodVersion`, EEPE stressée sous art. 284(3) et 292(2), PFE agrégée par chemin, libellé IMM simulé.
- #23 : BA-CVA (M_NS corrigée, périmètre art. 382, CVA exemptée notionnelle), output floor du §12, section CCF du `RegulatoryParameterSet`.
- #24 : CVA et DVA seulement, DVA comptable, contributions CVA additives, libellé PD proxy.
- #25 : Euler du §5.
- #26 : annotations W12, `regulatory_reference_date`, versioning ; catalogue de versions, commande de publication réduite à un identifiant, approbation à quatre yeux et refus d'approbation après une marche du calendrier ; recalcul ciblé après correction d'un snapshot en quatre yeux, avec annotation W12 de type `HistoryCorrection` (#52).
- #19 : `ManageJobRuns` et `ReadDataQualityDetail` (#52).
- #27 : mesure du §10 et seuils du §13. #30 et #36 : grands risques du §11.
- #28 : catalogue du §8. #29 : désignation exclusive des CDS.
- #43, #44, #45 : financements du §1 et du §14.
- #32 à #37 : textes d'aide (quantile, allocation, CCF, libellés obligatoires). #41 : section méthodologie.

**Mises à jour de l'EDB à porter par le PO** (document local, non versionné ; liste sans extrait) :

- W3 : quantile unique de 97,5 %, au lieu de la liste de trois quantiles ;
- W5 et W11 : libellé « IMM simulé, non approuvé » sur l'EEPE ;
- W6 : output floor, montée en charge jusqu'en 2030, dispositions transitoires jusqu'à fin 2032 ;
- W10 : « valeur exposée grands risques » au lieu d'« exposition groupe » ;
- W2 : niveau `GrossBeforeCrm` pour la première marche de la cascade ;
- axe « Approche réglementaire » : SA-CCR, IMM simulé, SA crédit (art. 111), FCCM ; sans CEM ;
- points ouverts restants (RCI, Bâle 1 / CEM, quantile, XVA, approche CVA, stress) à cocher, avec renvoi à l'ADR-002.

## Points à confronter à EUR-Lex

L'Expert CCR a relu les articles sur des sources secondaires (texte consolidé reproduit par gesetze.legal, PRA Rulebook, synthèses de cabinets), EUR-Lex étant inaccessible lors de sa vérification. Avant toute implémentation définitive, confronter à la version EUR-Lex en vigueur du règlement (UE) n° 575/2013 modifié par le règlement (UE) 2019/876 et le règlement (UE) 2024/1623 :

- [ ] Art. 273a(1)-(2), 281, 282 : seuils et méthodes allégées (SA-CCR simplifiée, OEM).
- [ ] Art. 274(2), 283, 284(3), (4), (5), (6), 285(2), 292(2) second alinéa : SA-CCR, IMM, Effective EE/EPE, MPOR, EEPE stressée.
- [ ] Art. 290 et 291 : rattachement à la Section 6 (IMM).
- [ ] Art. 382, 382(2), (3), (4), (4b), 382a, 383, 384 (§2, §3), 385, 386, 162 : périmètre, approches et paramètres CVA, M_NS.
- [ ] Art. 33(1)(c) : traitement de la DVA en CET1.
- [ ] Art. 389, 390(4), 392, 395(1), 399-403, 400(1)(i), 400(1)(j), 400(2)(i), 4(1)(39) : grands risques (l'art. 395 a été relu sur le PRA Rulebook, texte britannique).
- [ ] Art. 92(3), 465(1), (2), (3), (5), (9), (13) : facteur permanent de 72,5 %, calendrier dérogatoire et transitoires de l'output floor ; nature (faculté ou non) de la dérogation de l'art. 465(2).
- [ ] Art. 110, 111(1)-(4), 111(6), 111(8), 112, 5(10), 4(1)(80), annexe I (contenu des catégories 1 à 5), 223 : approche standard, CCF, définition des UCC et du financement du commerce, FCCM.
- [ ] Art. 220 : FCCM pour les netting sets de SFT sous accord-cadre (GMRA).
- [ ] Art. 495d, dont 495d(2) : calendrier transitoire des UCC et clause de revue.
- [ ] Art. 166(8a)-(8c) : CCF en IRB (pour mémoire).
- [ ] RTS EBA de l'art. 111(8) (EBA/RTS/2025/06) : publication au JOUE et écarts éventuels avec le projet final.
- [ ] Propositions de la Commission sur l'output floor annoncées pour début 2027.
- [ ] Exercice de stress test EBA 2027 : nouvelle version du catalogue le moment venu.

## Références

- Issue #6 : proposition du Lead, validation de l'Expert CCR, décisions du PO du 2026-11-04, note sur les critères caducs.
- Issue #45 : spécification des financements et réponses de l'Expert CCR.
- Issue #52 : gouvernance des versions de méthode et recalcul ciblé (décisions du PO du 2026-11-05, capacités validées par l'Expert CCR). Limites : aucun texte consulté n'impose ces rôles ni les quatre yeux sur une version de méthode (hypothèse du projet) ; les art. 286 à 288 du CRR relèvent de l'IMM (analogie) et ont été lus sur une version britannique, à confronter à EUR-Lex ; l'art. 76(5) de la CRD n'est pas vérifié.
- [Règlement (UE) 2024/1623 (CRR3) – EUR-Lex](https://eur-lex.europa.eu/eli/reg/2024/1623/oj), [Règlement (UE) 2019/876 (CRR2) – EUR-Lex](https://eur-lex.europa.eu/eli/reg/2019/876/oj), [Règlement (UE) n° 575/2013 (CRR) – EUR-Lex](https://eur-lex.europa.eu/eli/reg/2013/575/oj).
- [BCBS MAR50](https://www.bis.org/basel_framework/chapter/MAR/50.htm), [BCBS d588](https://www.bis.org/bcbs/publ/d588.htm).
- [Tasche, « Capital Allocation to Business Units and Sub-Portfolios: the Euler Principle »](https://arxiv.org/abs/0708.2542).
- [EBA – RTS sur les éléments hors bilan et les UCC](https://www.eba.europa.eu/activities/single-rulebook/regulatory-activities/credit-risk/regulatory-technical-standards-5).
- [Taux de référence ECB](https://www.ecb.europa.eu/stats/policy_and_exchange_rates/euro_reference_exchange_rates/html/index.en.html).
