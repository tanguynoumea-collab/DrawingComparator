# DESIGN-REVIEW n°1 — DrawingComparator (DAEDALUS Phase 2)
**Date :** 2026-08-13 · **Contrat :** docs/DESIGN_PLAN.md v1.0 (validé checkpoint 1)
**Captures :** docs/ui-baseline/01…04 (self-capture RenderTargetBitmap — la capture bureau étant indisponible dans cette session, fallback consigné ; outillage `--screenshot`/`--align` ajouté à l'app)

## Verdicts par écran

### 01 — État vide : ✅ CONFORME
Deux zones de dépôt à liseré rouge/bleu, phrase d'explication du principe, chrome sombre, boutons désactivés tant que rien n'est chargé. Hiérarchie conforme au plan (les cartes de dépôt sont les seules surfaces saillantes).

### 02 — Comparaison : ✅ CONFORME
Canvas « négatoscope » : la feuille blanche est la seule zone claire de l'écran ; seul élément coloré du chrome = bouton ambre « Caler les plans ». Cartes calques avec liseré teinté, page, opacité. Statut : zoom, pages, coordonnées mm en Cascadia Mono. Vérification pixel : mur base = RVB(255,0,0) pur, révision = bleu pur, cartouches correctement teintés.

### 03 — Mode calage : ✅ CONFORME (1 écart mineur)
Bandeau 4 étapes (◉ courante / ○ à venir) + instruction en vocabulaire métier + rappel Échap/Retour arrière. Le calque non concerné par l'étape s'efface à 25 % d'intensité.

### 04 — Après calage : ✅ LE CONTRAT VISUEL EST TENU
Calage simulé programmatiquement (4 clics réels dans la machine à états, sur les PDF d'exemple transformés à 96 %/1,5°) : matrice retrouvée é=1,0417, θ=−1,50°. Résultat : traits communs NOIRS, refend déplacé visible rouge+bleu côte à côte, cloison ajoutée bleue, poteau supprimé rouge. C'est exactement le pitch.

## Écarts relevés

| # | Écart vs plan | Gravité | Décision |
|---|---|---|---|
| 1 | Le mode calage ATTÉNUE le calque inactif vers le blanc au lieu d'ASSOMBRIR le canvas à 60 % | Mineur | Roadmap (l'effet fonctionnel — l'actif domine — est atteint ; l'assombrissement plein canvas serait plus théâtral) |
| 2 | Confirmation d'export en barre de statut au lieu d'un Snackbar | Mineur | Roadmap |
| 3 | Loupe ×4 non vérifiée visuellement (nécessite une souris réelle ; le code et son throttle 30 img/s sont en place) | À vérifier | **UAT utilisateur** au premier lancement |
| 4 | Frange d'anti-crénelage : le plan pivoté paraît plus épais avant calage (AA d'un raster tourné, minification) | Connu | Documenté au rapport LLM-Council (risque n°2) — disparaît une fois calé, cf. capture 04 |

## Checklist heuristiques (points saillants)
- ✅ Une seule action primaire (ambre) ; poids visuel = importance
- ✅ Cibles ≥ 32 px ; conventions Windows (Ctrl+O, Ctrl+E, Échap, double-clic fit)
- ✅ État vide = invitation ; erreurs PDF = cause + action, sans excuses ; jamais de gel UI (rendu async + verrou PDFium)
- ✅ Zéro valeur magique : tout le chrome dérive de Themes/Tokens.xaml
- ⚠️ Pose des points de calage non faisable au clavier (pointage souris) — assumé au DESIGN_PLAN §8
- ⚠️ Thème light non capturé (tokens light définis au plan, non implémentés en v1) — noter roadmap

## Verdict global
**Zéro écart bloquant.** Itération design_review consommée : 0 (aucun retour GSD nécessaire). Mineurs n°1, 2 + thème light → pour le RETOUR ROADMAP. Loupe → à confirmer en UAT.
