# CLAUDE.md — DrawingComparator

## Contexte
Comparateur visuel de plans PDF : superposition rouge/bleu en blending multiplicatif (traits communs sombres, différences rouge/bleu pur), alignement 2 points (ancrage + échelle/rotation), zoom/pan, export PNG. Mono-utilisateur Windows, exe local self-contained.

## Stack
C# / WPF / .NET 8 — MVVM avec CommunityToolkit.Mvvm, WPF-UI (Fluent 2, thème sombre), PDFtoImage (PDFium) pour le rendu PDF, SkiaSharp pour la composition.

## Architecture
- `src/DrawingComparator.Core` (net8.0, sans WPF) : AlignmentMath (similitude 2 points, Matrix 3×2 en points PDF), PdfDocumentService (verrou PDFium global), ComparisonCompositor (teinte color-matrix + multiply), ExportService.
- `src/DrawingComparator.App` (net8.0-windows) : MVVM strict, DI via Microsoft.Extensions.DependencyInjection, tokens visuels dans `Themes/Tokens.xaml` uniquement.
- `tests/DrawingComparator.Tests` : xunit sur le Core.

## Conventions
- MVVM strict : Views / ViewModels / Services ; le seul code-behind toléré = maths d'input souris (ComparatorView) et routage drag & drop (MainWindow).
- Zéro valeur magique UI : toute couleur/taille vient de `Themes/Tokens.xaml` (contrat : docs/DESIGN_PLAN.md).
- Matrices : alignement exprimé en points PDF (1/72"), jamais en pixels ; stocké en 3×2 générale.
- La colonne de translation des matrices couleur SkiaSharp 4 est en 0..1, pas 0..255.
- PDFium n'est pas thread-safe : tout appel passe par le verrou global de PdfDocumentService.
- Rasters SKImage remplacés → `MainViewModel.RetireBitmap` (libération différée), jamais Dispose direct ; toute composition de fond qui lit les rasters (viewport, export) s'encadre de `Begin/EndBackgroundCompose` (dev-council FIA-01).
- Budget de rendu (arête ≤ 8192 px, surface ≤ 70 Mpx) appliqué DANS PdfDocumentService.CapDpi, sur les tailles mesurées par le service — jamais côté appelant seul.
- Documents PDF : OpenAsync/Release avec comptage de références ; les octets ne sont jamais relus par chemin après ouverture.

## Pipeline ZEUS
Ce projet est piloté par ZEUS : état dans `.zeus/state.json`, DoD dans `.zeus/DOD.md`, tâches du cycle dans `.zeus/tasks.md`, rapports d'audit dans `.zeus/reports/`.

## Commandes
- Build : `dotnet build -c Release` (TreatWarningsAsErrors + AnalysisLevel latest-recommended via Directory.Build.props)
- Format : `dotnet format --verify-no-changes`
- Tests : `dotnet test -c Release`
- Publish : `powershell -File scripts/publish.ps1` → exe unique (natives embarquées, compressé) + SHA256SUMS.txt. Options figées dans le csproj ; PublishTrimmed interdit (WPF).
- CLI de l'exe : `DrawingComparator.exe base.pdf [revision.pdf]` (chargement direct) ; `--align`, `--screenshot <png>` = outillage design-review, volontairement présent en Release (arbitrage dev-council n°1).

## Statut GSD
Cycle 1 : voir `.zeus/tasks.md`.
