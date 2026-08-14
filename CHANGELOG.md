# Changelog — DrawingComparator

Le format suit [Keep a Changelog](https://keepachangelog.com/fr/) ; versionnage [SemVer](https://semver.org/lang/fr/).

## [0.2.0] — 2026-08-13

Cycle 2 : l'intégralité de la roadmap (14 points) + retouches d'UAT. Pipeline ZEUS complet —
LLM-Council, design validé (delta v1.1), design-review sur captures, dev-council (8 rôles +
sceptique + arbitrage : 1 bloquant + 3 majeurs corrigés), dev-senior (4 majeurs corrigés) —
tous soldés et re-vérifiés.

### Ajouté
- **Netteté vectorielle à tout zoom** : la zone visible est re-rendue par PDFium au DPI de l'écran
  (mémoire constante), le plan reste net à n'importe quel grossissement ; indicateur « ⟳ netteté… »
  discret pendant le rendu, l'image précédente reste affichée (jamais de flash blanc).
- **Projets de superposition (.dcproj)** : Ctrl+S/Ctrl+O enregistrent et rouvrent tout l'état
  (les 2 PDF, pages, calage, opacités, teintes, binarisation). Onglets **Accueil / Projet** :
  la page d'accueil (dépôt + liste « Reprendre » des 8 derniers projets) reste accessible même
  projet ouvert. Écriture atomique (un échec ne détruit jamais le fichier existant), chemins
  re-validés à l'ouverture (repli automatique à côté du projet, relocalisation guidée,
  confirmation explicite avant tout chemin réseau).
- **Ajustement fin au clavier** : une fois calé, les flèches translatent le plan révisé —
  pas de 0,01 / 0,1 / 1 mm papier visibles et cliquables, Maj = 0,01, Ctrl = 1.
- **Point de contrôle du calage** : une 5e étape facultative mesure l'erreur résiduelle en mm ;
  **mode affine 3 points** en option explicite (échelles X/Y et cisaillement affichés,
  avertissement d'anisotropie) — jamais de bascule silencieuse.
- **Calque des différences** : section AFFICHAGE — « Différences seules » efface les traits
  communs pour ne garder que les écarts rouges/bleus, ou les pose sur la BASE / le RÉVISÉ
  en gris de contexte ; s'applique aussi aux exports.
- **Export PDF** : une page à l'emprise exacte de la feuille, rasters au DPI choisi, écrit tuile
  par tuile — 600 DPI possible même sur un A0. Option **600 DPI** ajoutée ; progression
  déterminée + annulation ; confirmation par snackbar avec « Ouvrir le dossier ».
- **Binarisation par calque** (◐) : nettoie les scans — fond gris → blanc, traits → teinte pleine.
- Bandeau « cette page semble vide », thème clair suivi au démarrage, **logo et icône** de l'app.

### Modifié
- Mode calage : assombrissement plein canvas à 60 % (150 ms) et « clac » de 200 ms au commit ;
  la loupe perce le voile à pleine lumière. Échap après le calage appliqué le CONSERVE
  (y compris pendant la pose du point de contrôle).
- Bouton d'export renommé « Exporter » (choix PNG/PDF dans le dialogue).
- Ouvrir (Ctrl+O) accepte indifféremment un projet .dcproj ou un plan PDF.

### Corrigé
- L'export des grandes feuilles (A0/A1) annonçait 300 DPI mais rendait moins : découpe en vraies
  tuiles, le DPI demandé est réellement honoré.
- Le plan révisé restait flou quand les deux plans n'étaient pas à la même échelle : le rendu
  compense désormais l'échelle du calage.
- Un fichier projet incomplet ou corrompu pouvait faire planter l'ouverture : validation complète
  à la frontière avec messages explicites.
- Le canvas pouvait rester blanc/fantôme sur la page d'accueil après avoir tout fermé.

### Qualité
- CI GitHub Actions : build + format + 87 tests sur chaque push/PR ; sur tag, rebuild indépendant,
  garde tag↔version et Release automatique avec l'exe zippé + SHA-256. Dependabot (NuGet + Actions).
- 87 tests (rendu par région sur vrais PDF, export tuilé/PDF, affine, projets, différences),
  machine de calage extraite (`AlignmentSession`), persistance injectable (`IProjectStore`),
  rotation du journal de crash.

## [0.1.0] — 2026-08-13

Première version. Pipeline ZEUS complet : LLM-Council (architecture), DAEDALUS (design validé), GSD, gate de tests, design-review sur captures réelles, audit dev-council (7 rôles + sceptique + arbitrage), audit externe dev-senior — tous les bloquants et majeurs corrigés et re-vérifiés.

### Ajouté
- Superposition de deux plans PDF : base teintée rouge, révision teintée bleue, blending multiplicatif (luminance BT.709) — les traits communs ressortent sombres, les différences restent rouges ou bleues, les annotations colorées des plans restent visibles.
- Calage 2 points guidé : bandeau 4 étapes en vocabulaire métier, loupe ×4 au curseur, ancrage puis échelle+rotation (similitude, stockée en matrice 3×2 générale en points PDF), Échap/Retour arrière, refus des points confondus, panneau échelle/rotation + réinitialisation.
- Zoom (molette, centré curseur) / pan (drag milieu ou droit) / ajustement fenêtre (double-clic), rendu net à 100/125/150 % DPI (composition en pixels physiques).
- Sélecteur de page et slider d'opacité par plan, permutation des teintes.
- Export PNG : feuille entière (150/300 DPI, budget mémoire 120 Mpx avec DPI effectif annoncé) ou vue courante ×2 ; SHA-256 du livrable publié.
- Chargement direct : `DrawingComparator.exe base.pdf [revision.pdf]` ; drag & drop ; outillage `--align` / `--screenshot <png>` pour les revues de design.
- Robustesse : PDF corrompus/protégés/pages inexistantes → messages explicites ; budget de rendu inviolable côté service (MediaBox géante inoffensive) ; documents à comptage de références ; cycle de vie des rasters protégé pendant les exports ; journal de crash `%TEMP%\drawingcomparator-crash.log` avec garde anti-rafale.

### Qualité
- 29 tests (maths d'alignement 100 %, compositeur pixel-vérifié, workflow de calage d'intégration sur vrais PDF, cycle de vie de composition, budgets d'export/rendu).
- Build 0 avertissement avec `AnalysisLevel latest-recommended` + `TreatWarningsAsErrors`, format vérifié, lock files, exe unique self-contained win-x64 ~79 Mo.
