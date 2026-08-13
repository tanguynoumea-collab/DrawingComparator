# Tâches GSD — Cycle 1 DrawingComparator

> Mode dégradé consigné : le plugin GSD installé (workflow new-project/plan-phase/execute-phase) impose sa propre orchestration lourde ; ZEUS tient ici la liste équivalente, dérivée du PROMPT_MERE et du DESIGN_PLAN validé. Une tâche = un incrément commitable.

## arch
- [ ] T01 — Créer la solution (Core net8.0 / App WPF net8.0-windows / Tests xunit) + packages NuGet
- [ ] T02 — Implémenter AlignmentMath : similitude 2 points → Matrix 3×2 en points PDF (+ cas dégénérés)
- [ ] T03 — Implémenter IPdfDocumentService (PDFtoImage, verrou global, pages, dimensions points PDF)
- [ ] T04 — Implémenter IComparisonCompositor Skia (teinte color-matrix, multiply, opacité lerp-blanc, matrices vue/alignement)

## ui
- [ ] T05 — Tokens.xaml + App.xaml (WPF UI Fluent, thème sombre, palette DESIGN_PLAN)
- [ ] T06 — MainWindow : barre d'outils, canvas central, panneau cartes calques, barre de statut
- [ ] T07 — État vide : deux zones de dépôt teintées + drag & drop
- [ ] T08 — Zoom/pan : molette centrée curseur, drag pan, double-clic fit, transform GPU + recomposition débouncée

## feat
- [ ] T09 — Chargement PDF + sélecteur de page par carte calque + rasterisation asynchrone
- [ ] T10 — Machine à états de calage 4 clics + bandeau d'étapes + Échap/retour + refus points confondus
- [ ] T11 — Loupe ×4 du viseur de calage (élément signature)
- [ ] T12 — Sliders d'opacité + permutation rouge/bleu
- [ ] T13 — Export PNG (dialogue DPI, rendu offscreen, snackbar résultat)
- [ ] T14 — Panneau Calage : échelle/rotation affichées + Réinitialiser

## fix
- [ ] T15 — Erreurs PDF (corrompu/protégé/page inexistante) → ContentDialog explicites
- [ ] T16 — Tests unitaires : AlignmentMath, machine à états, compositeur (golden pixels)

## deploy
- [ ] T17 — dotnet publish win-x64 self-contained + vérification pdfium embarqué
