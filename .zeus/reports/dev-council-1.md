# DEV-COUNCIL n°1 — DrawingComparator (audit complet)
**Date :** 2026-08-13 · **Périmètre :** pré-v0.1.0, cycle 1
**Roster :** architecte, mainteneur, tests, fiabilite, securite, packaging, pertinence (7). `donnees` écarté sur calibration (aucune persistance) ; `autodesk` non applicable. Filtre sceptique passé (0 invalidé, 9 sévérités abaissées, 1 arbitrage). Cross-challenge §6.2 : 2 paires (2 findings CROSS émis, non passés au sceptique — signalé). Arbitrage §6.3 : 1 question, 3 juges, verdict A.

---

## Couche 1 — Verdict et résumé exécutif

**Verdict : SOLIDE AVEC UN BLOQUANT.** Le cœur (maths d'alignement, compositeur multiply, découpage Core/App, DI, machine à états testée sur vrais PDF) est sain — trois auditeurs le disent explicitement ("un des codebases les plus reprenables", "sens des dépendances irréprochable", coutures de test réelles). Le build est à 0 warning et 15/15 tests verts, MAIS ce "0 warning" ne couvre que les analyseurs par défaut (aucun AnalysisMode étendu).

**Après challenge sceptique : 1 Bloquant, 2 Majeurs, ~21 Mineurs, ~20 Infos** (l'inflation initiale — 8 Majeurs — était dans les sévérités, pas dans les constats : tous les faits cités ont été vérifiés dans le code).

- **FIA-01 (Bloquant, CONFIRMÉ ligne à ligne par le sceptique)** : use-after-free d'un `SKImage` — pendant un export long (Task.Run, plusieurs secondes), changer de page dispose le raster que le thread d'export est en train de dessiner (`RetireBitmap` ne protège que la composition viewport). Issue : AccessViolationException (crash process) ou PNG corrompu.
- **TST-02 (Majeur)** : `RequestRecompose` async void = le mécanisme qui héberge FIA-01 est intestable ; le fix du Bloquant serait invérifiable sans ce refactor.
- **TST-01 (Majeur)** : ExportService couvert à 4 % — garde-fou mémoire MaxPixels et DPI effectif jamais vérifiés.

**Couverture Coverlet (vérité-terrain)** : 36,7 % lignes global — Core 74 % (AlignmentMath 100 %, Compositor 100 %), App 26 %. Les trous sont ciblés, pas diffus.

**Non vérifié faute d'outil (transversal)** : analyseurs CA étendus jamais exécutés (AnalysisMode absent) ; reproduction dynamique de FIA-01 ; CVE des binaires natifs pdfium/libSkiaSharp (hors audit NuGet) ; exécution sur machine vierge ; automatisation UI (loupe, drag & drop réels).

## Couche 2 — Plan de remédiation priorisé

### P1 — Avant publication (Bloquant + Majeurs, se corrigent ensemble)
1. **FIA-01 + TST-02 + CROSS-01** (`MainViewModel.cs:439-498, 311-336`) : compter TOUTES les compositions en vol (viewport, export, loupe) avant de purger `_retiredBitmaps` — ou snapshot des rasters à l'entrée d'export ; extraire le cœur de `RequestRecompose` en `internal async Task` testable ; contrat de propriété explicite du composite (CROSS-01). Tests : coalescing (3 appels → 2 composes), retrait pendant compose → dispose différé, export + changement de page → pas de dispose prématuré.
2. **TST-01** (`ExportService.cs`) : 3 tests (DPI sous le seuil → identique ; A0 600 DPI → réduit ≤ MaxPixels ; annulation → pas de fichier).

### P2 — Recommandé ce cycle (groupes cohérents)
3. **Groupe ressources/budget** — ARC-03 + FIA-02 + SEC-06 + SEC-01 + CROSS-02 (`PdfDocumentService.cs`, `LayerViewModel.cs:124`) : handle de document qui possède ses octets (Dispose = purge, plus de fallback ReadAllBytes par chemin) + budget pixels DANS le service (le clamp MinDpi actuel est contournable par MediaBox > 8192 pt → allocation ~830 Mo par PDF forgé). Le sceptique et le cross-challenge convergent : traiter ces cinq IDs comme UN correctif.
4. **Groupe démarrage/filet d'erreur** — ARC-02 + FIA-05 + FIA-04 + FIA-03 (`App.xaml.cs`, `MainViewModel.cs`) : try/catch + LogCrash + Shutdown(1) sur la séquence outillée ; hook `TaskScheduler.UnobservedTaskException` ; `Handled=false` pour OOM/état corrompu + garde anti-rafale ; try/catch dans la boucle de recompose.
5. **Groupe outillage (arbitré : option A)** — PERT-03 + ARC-05 + MNT-05 : garder `--screenshot/--align` en Release ; remplacer `Task.Delay(2500)` par attente `CompositeUpdated` + timeout ; documenter les flags ; consacrer le chargement CLI (PERT-04) au DESIGN_PLAN.
6. **Groupe hygiène build/version** — ARC-06 + MNT-06 + PKG-01 + PKG-02 : `Directory.Build.props` (Version 0.1.0, AnalysisMode Recommended, TreatWarningsAsErrors), `.editorconfig`, `global.json`, lock file ; corriger AssemblyInfo.cs.
7. **Groupe packaging** — PKG-03 + PKG-04 + PKG-05 (+ SEC-03/PKG-06) : `IncludeNativeLibrariesForSelfExtract`, exclusion des PDB, compression single-file, SHA-256 publié ; figer le publish dans le csproj.
8. **MNT-02 + TST-07** : la branche export « vue courante » passe par ExportService (suppression de la duplication encode/save).

### P3 — Mineurs restants et Infos (roadmap ou opportuniste)
- Tests : TST-03 (chemins d'erreur PDF, abaissé Mineur), TST-04 (fake IPdfDocumentService, abaissé Mineur), TST-05/06/08, TST-09.
- Fiabilité : FIA-06 (CTS dispose, IsLoading, double rendu), FIA-07 (latence PDFium à afficher/documenter), FIA-08 (Info — inatteignable tant que le cache n'est pas purgé, re-vérifier après le groupe 3), FIA-10.
- Code : MNT-01 (commentaire duplication XAML), MNT-03 (constante 96/72), MNT-04 (libellé « é = » → « échelle »), MNT-07/08, ARC-04 (Info), ARC-07/08, PERT-01 (câbler ou retirer ClearFile), PERT-02 (Info, supprimer IsComparisonVisible), PERT-05.
- Documentaire : PERT-06 (le DESIGN_PLAN promet bandeau page vide / progression annulable / motion / light non livrés — tracer au cycle 2 OU amender le plan §9), PERT-07 (tokens orphelins).
- Sécurité : SEC-02 (Info, bump Test SDK), SEC-04 (cadence de mise à jour PDFium = seul contrôle), SEC-05 (rotation du crash log).

## Couche 3 — Findings détaillés

Constats complets par auditeur dans `.zeus/reports/_findings-raw-1.md` (entrée du sceptique, IDs stables). Statuts de challenge : tous **validés** sauf — abaissés : TST-03, TST-04 (Majeur→Mineur), PKG-01, PKG-03 (Majeur→Mineur, pré-distribution), groupe cache ARC-03/FIA-02/SEC-06 (escalade Majeur refusée → Mineur), ARC-04 (Mineur→Info), SEC-02 (Mineur→Info), PERT-02 (Mineur→Info), FIA-08 (Mineur→Info, inatteignable) ; arbitré : PERT-03 (option A). CROSS-01/CROSS-02 émis en cross-challenge, **non passés au sceptique** (à challenger au prochain cycle s'ils ne sont pas soldés avant).

Annexe « écartés » : néant (0 finding invalidé). Rôle `donnees` écarté sur calibration, non sur silence.
