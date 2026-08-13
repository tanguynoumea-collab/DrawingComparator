# Changelog — DrawingComparator

Le format suit [Keep a Changelog](https://keepachangelog.com/fr/) ; versionnage [SemVer](https://semver.org/lang/fr/).

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
