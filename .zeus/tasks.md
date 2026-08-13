# Tâches GSD — Cycle 1 DrawingComparator

> Mode dégradé consigné : le plugin GSD installé impose sa propre orchestration lourde ; ZEUS tient ici la liste équivalente, dérivée du PROMPT_MERE et du DESIGN_PLAN validé.

## arch
- [x] T01 — Créer la solution (Core net8.0 / App WPF net8.0-windows / Tests xunit) + packages NuGet
- [x] T02 — Implémenter AlignmentMath : similitude 2 points → Matrix 3×2 en points PDF (+ cas dégénérés)
- [x] T03 — Implémenter IPdfDocumentService (PDFtoImage, verrou global, pages, dimensions points PDF)
- [x] T04 — Implémenter IComparisonCompositor Skia (teinte color-matrix, multiply, opacité lerp-blanc, matrices vue/alignement, mipmaps en minification)

## ui
- [x] T05 — Tokens.xaml + App.xaml (WPF UI Fluent, thème sombre, palette DESIGN_PLAN)
- [x] T06 — MainWindow : barre d'outils, canvas central, panneau cartes calques, barre de statut
- [x] T07 — État vide : deux zones de dépôt teintées + drag & drop
- [x] T08 — Zoom/pan : molette centrée curseur, drag pan, double-clic fit, transform GPU + recomposition coalescée

## feat
- [x] T09 — Chargement PDF + sélecteur de page par carte calque + rasterisation asynchrone
- [x] T10 — Machine à états de calage 4 clics + bandeau d'étapes + Échap/retour + refus points confondus
- [x] T11 — Loupe ×4 du viseur de calage (élément signature) — visuel à confirmer en UAT
- [x] T12 — Sliders d'opacité + permutation rouge/bleu
- [x] T13 — Export PNG (dialogue zone+résolution, rendu offscreen plafonné 120 Mpx)
- [x] T14 — Panneau Calage : échelle/rotation affichées + Réinitialiser

## fix
- [x] T15 — Erreurs PDF (corrompu/protégé/page inexistante) → dialogues explicites + crash log global
- [x] T16 — Tests : AlignmentMath (8), compositeur golden pixels (4), workflow de calage intégration sur vrais PDF (3) — 15/15 verts

## deploy
- [x] T17 — dotnet publish win-x64 self-contained single-file : exe OK, pdfium.dll + libSkiaSharp embarqués

## Hors périmètre initial, ajouté en cours de cycle
- [x] T18 — Chargement par arguments CLI (base.pdf revision.pdf) + outillage --screenshot/--align pour la design-review
