# Definition of Done — DrawingComparator

> Instanciée à l'initialisation ZEUS, adaptée au projet, puis STABLE (toute modification est une décision explicite, consignée dans l'historique d'état). C'est le SEUL critère de sortie de la phase GSD.

## Socle

- [x] Toutes les tâches du cycle (.zeus/tasks.md) sont `done`
- [x] `dotnet build -c Release` propre sur la cible net8.0-windows (App) + net8.0 (Core, Tests)
- [x] Tests unitaires : 100 % verts (DrawingComparator.Tests — 69 tests : AlignmentMath + affine, calage, compositeur, région, export tuilé, projets, VM)
- [x] Zéro warning de sévérité Warning+ au build Release
- [x] Aucune valeur magique UI (couleurs/tailles hors Themes/Tokens.xaml + Tokens.Light.xaml)
- [x] Aucun blocage du thread UI (rasterisation, tuiles et export asynchrones, verrou PDFium global)
- [x] Les chaînes visibles respectent le vocabulaire du DESIGN_PLAN (Caler les plans, plan de BASE / plan RÉVISÉ, Exporter PNG, Reprendre)

## Spécifique projet

- [x] Exe publiable : `dotnet publish -r win-x64 --self-contained` sans trimming, pdfium natif embarqué
- [x] PDF corrompu / protégé / page inexistante → dialogue d'erreur explicite, jamais de crash
- [x] Matrice d’alignement stockée en 3×2 générale, exprimée en points PDF
- [x] Aucun type Skia sérialisé sur disque (.dcproj = six floats nommés, ADR 001)

## Par fonctionnalité du cycle 2

| Item roadmap | Critère d'acceptation observable | Fait |
|---|---|---|
| 1 Qualité raster | Zoom au-delà du DPI de l'overview → tuile de région rendue au DPI de la vue (debounce 150 ms), mémoire bornée par CapDpi(RÉGION) ; jamais tuile empilée sur overview en multiply ; échec → overview conservé + statut (SEN-04) ; « ⟳ netteté… » pendant le rendu (FIA-07) | ☑ |
| 2 Projets | Ctrl+S écrit un .dcproj versionné (chemins, pages, matrice 6 floats, opacités, teintes, binarisation) ; Ctrl+O le rouvre ; chemins re-validés avec repli « à côté du .dcproj » puis relocalisation ; section Reprendre (8 MRU, double liseré) sur l'état vide | ☑ |
| 3 Calage clavier | Un calage posé + focus canvas → flèches = translation du RÉVISÉ en mm papier BASE ; pas 0,1/0,5/2 visible et cliquable ; Maj/Ctrl = cran temporaire ; échelle/rotation intactes | ☑ |
| 4 Scrim calage | Pose de point → voile 60 % plein canvas (150 ms) ; commit → retour pleine lumière (200 ms, le « clac ») ; la loupe perce le voile | ☑ |
| 5 Snackbar export | Export réussi → snackbar liseré Accent avec [Ouvrir le dossier] ; échec → snackbar Danger ; plus de message en barre de statut | ☑ |
| 6 Thème light | Windows en mode clair au démarrage → chrome light (Tokens.Light) ; pas de switch runtime (SEN-13) | ☑ |
| 7 3e point + affine | Étape 5 facultative → « résiduel x mm » ; segmented Rigide/Affine visible avec 3e couple seulement ; affine = choix explicite, é_x/é_y/cisaillement affichés + avertissement anisotropie ; colinéaire → refus (SEN-01, jamais silencieux) | ☑ |
| 8 Binarisation | Toggle ◐ par carte → fond gris de scan blanchi, traits en teinte pleine, réversible, état dans le .dcproj | ☑ |
| 9 Reliquats design | Bandeau « page semble vide » fermable ; ProgressBar déterminée + Annuler à l'export ; motion du calage (150/200 ms) | ☑ |
| 10-13 Dette/sécu | TST-03..08 écrits ; FIA-07/SEN-04/05/08 intégrés ; PERT-01/05/07, ARC-08 (ADR), SEN-07 (doc) ; SEC-02 (Test SDK 17.11.1), SEC-04 (Dependabot), SEC-05 (rotation log) ; SEN-11 mort (export tuilé) | ☑ |
| 14 CI | Workflow build+format+tests sur push/PR ; tag v* → publish + zip + Release GitHub | ☑ (vérification en ligne au premier push) |
