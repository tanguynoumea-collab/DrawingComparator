# Tâches GSD — Cycle 2 DrawingComparator

> Mode dégradé consigné (comme au cycle 1) : ZEUS tient la liste dérivée du rapport LLM-Council cycle 2
> et du DESIGN_PLAN v1.1 §11 validé. Ordre = vagues V0→V4 du council.

## V0 — Rails (CI, sécu, nettoyage, spike)
- [x] C01 — CI GitHub Actions : build Release + `dotnet format --verify-no-changes` + tests sur push/PR ; job tag → publish.ps1 + upload artefact (item 14)
- [x] C02 — Dependabot (nuget + github-actions) = SEC-04 ; bump Microsoft.NET.Test.Sdk = SEC-02
- [x] C03 — Rotation du crash log (SEC-05)
- [x] C04 — Nettoyage : PERT-05 privatiser Compose(SKCanvas) ; PERT-07 tokens orphelins ; ARC-08 ADR « Skia dans les contrats du Core » ; SEN-07 doc lecture concurrente Skia (PERT-01 → V3)
- [x] C05 — Spike région : test d'intégration RenderOptions.Bounds/DpiRelativeToBounds sur vrais PDF (précision géométrique + latence mesurée)

## V1 — Pipeline raster v2 (point 1 + fiabilité + export)
- [x] C06 — IPdfDocumentService.RenderRegionAsync (Bounds, CapDpi sur la RÉGION, sous PdfiumLock) + échelles X/Y séparées
- [x] C07 — LayerRenderInfo : RegionOriginDoc + RasterScaleY (SEN-14) ; Compose : rasterToDoc = Translation(origin)·Scale(1/sx,1/sy)
- [x] C08 — LayerViewModel bi-raster : overview pleine page (fallback permanent) + tuile de vue (debounce 150 ms, annulation, swap RetireBitmap) ; JAMAIS tuile empilée sur overview en multiply (choix par calque) ; plafond overview abaissé
- [x] C09 — Fiabilité intégrée : FIA-07 (indicateur « ⟳ netteté… » barre de statut), SEN-04 (échec de rendu → overview conservé + statut), SEN-05 (purge rasters au Dispose)
- [x] C10 — Export tuilé par bandes via RenderRegionAsync : DPI demandé honoré (SEN-11), IProgress (1 tick/bande) + annulation déjà câblée (ct)
- [x] C11 — UI export : ProgressBar déterminée + [Annuler] dans le dialogue ; snackbar succès (liseré Accent, [Ouvrir le dossier]) / échec (Danger) — remplace la barre de statut
- [x] C12 — Fake IPdfDocumentService (APRÈS gel de l'interface) + TST-03/04 (chemins d'erreur) + TST-07/08 (ExportAsync VM)

## V2 — Calage v2
- [x] C13 — Refactor machine de calage : liste de couples (p,q) + index (translation 1 pt / similitude 2 pts / + contrôle 3 pts)
- [x] C14 — Scrim 60 % plein canvas en XAML (voile par-dessus le composite, calque actif à pleine lumière) ; retire dimForAlignment ; motion 150 ms + « clac » 200 ms
- [x] C15 — Calage fin clavier : NudgeRevision(mm base ×72/25,4, PreConcat repère base), PreviewKeyDown ComparatorView, bloc AJUSTER (segmented 0,1/0,5/2 mm, Maj/Ctrl), focus visible
- [x] C16 — 3e point de contrôle : étape 5 facultative du bandeau, résiduel ‖M·p₃−q₃‖ en mm affiché (bandeau + panneau)
- [x] C17 — Mode affine opt-in : AlignmentMath.ComputeAffine (Cramer double, DegenerateAlignmentException si colinéaire — SEN-01 plus jamais silencieux), segmented Rigide/Affine, Decompose étendu (é_x, é_y, cisaillement) + avertissement anisotropie > 0,2 %
- [x] C18 — TST-05 (zoom/pan + calage sous vue zoomée) + TST-06 (UndoAlignmentPoint) sur la NOUVELLE machine + tests affine/résiduel

## V3 — Projets
- [x] C19 — SEN-08 : sérialiser les chargements (file par calque, drops rapides + ouverture projet)
- [x] C20 — Core ProjectService : DTO ComparisonProject (6 floats nommés, schemaVersion, champs inconnus ignorés), System.Text.Json invariant, round-trip testé
- [x] C21 — App : Ouvrir/Enregistrer/.dcproj (Ctrl+O/S/Maj+S), MRU %APPDATA%, re-validation chemins (Exists+taille+mtime) + dialogue relocaliser
- [x] C22 — État vide : section « Reprendre » (8 MRU, double liseré rouge|bleu, dates Cascadia Mono) ; PERT-01 : câbler ClearFile
- [x] C23 — Tests projets : round-trip, fichier manquant, schéma inconnu

## V4 — Finitions
- [x] C24 — Binarisation par calque : SKColorFilter seuil composé AVANT la teinte (CreateCompose), flag LayerRenderInfo, toggle ◐ carte calque, état dans .dcproj
- [x] C25 — Bandeau « page semble vide » (heuristique quasi-blanc post-rendu, fermeture ✕, disparaît au changement de page)
- [x] C26 — Thème light : dictionnaire tokens light (chrome seul), détection système au démarrage (SEN-13), aucun switch runtime
- [x] C27 — DoD complète + gate final (build 0 warning, format, tous tests verts)
