# Findings bruts — dev-council n°1 (2026-08-13) — entrée du sceptique

Projet : DrawingComparator, WPF .NET 8 mono-utilisateur, comparateur de plans PDF, pré-v0.1.0, exe local.

## ARCHITECTE
- ARC-01 (Mineur) MainViewModel dépend de WPF (WriteableBitmap, SkiaInterop) → suite de tests otage de net8.0-windows. MainViewModel.cs:1,82,460.
- ARC-02 (Mineur) async void RequestRecompose + fire-and-forget `_ = RunStartupSequenceAsync` : exceptions de la tâche de démarrage non observées (pas de hook UnobservedTaskException). App.xaml.cs:45.
- ARC-03 (Mineur) IPdfDocumentService sans cycle de vie : _bytesCache jamais purgé (singleton). PdfDocumentService.cs:17,35.
- ARC-04 (Mineur) Logique du dialogue d'export en code-behind sans VM ; dérogation non documentée contrairement à ComparatorView. ExportDialog.xaml.cs:12-13.
- ARC-05 (Mineur) Outillage --align/--screenshot + Task.Delay(2500) dans le composition root de prod. App.xaml.cs:55-79.
- ARC-06 (Mineur) Aucun garde-fou build centralisé (pas de Directory.Build.props/.editorconfig/AnalysisMode) ; « 0 warning » = niveau par défaut seulement ; dotnet format échoue (9 écarts AssemblyInfo.cs).
- ARC-07 (Info) Couplage parent↔enfant par 3 délégués de ctor (LayerViewModel). OK à 2 calques.
- ARC-08 (Info) Contrats Core couplés à SkiaSharp — choix défendable, à documenter (ADR).

## MAINTENEUR
- MNT-01 (Mineur) 4 blocs XAML quasi identiques (2 cartes calques + 2 drop cards) sans signalement de duplication volontaire. MainWindow.xaml:103-282.
- MNT-02 (Mineur) Export « vue courante » ré-implémente compose→encode→save de ExportService (message d'erreur dupliqué). MainViewModel.cs:317-328.
- MNT-03 (Mineur) Ratio 96/72 en littéral dupliqué (déf. du zoom 100 %) + bornes zoom non nommées. MainViewModel.cs:77,229,194.
- MNT-04 (Mineur) Libellé UI « é = » cryptique (échelle). MainViewModel.cs:410.
- MNT-05 (Mineur) = ARC-05 (outillage + délai magique 2500 ms).
- MNT-06 (Mineur) Pas d'.editorconfig ; = ARC-06.
- MNT-07 (Info) 5 valeurs liées de la loupe réparties code-behind/XAML sans commentaire (144=140+2×2…).
- MNT-08 (Info) Contrat d'erreur de RequestRecompose non documenté (exceptions → DispatcherUnhandledException).

## TESTS (vérité-terrain Coverlet : 36,7 % lignes global ; Core 74 % ; ExportService 4 % ; branches PdfDocumentService 10 %)
- TST-01 (Majeur) ExportService non testé (4 %) : garde-fou MaxPixels/effectiveDpi/annulation invérifiés. ExportService.cs:21-48.
- TST-02 (Majeur) RequestRecompose async void intestable : coalescing + _retiredBitmaps jamais exercés (14 %). MainViewModel.cs:439-472.
- TST-03 (Majeur) Chemins d'erreur PdfDocumentService à zéro (corrompu/protégé/page hors limites, TranslatePdfiumError non testé — matching de sous-chaînes fragile). PdfDocumentService.cs:73-96.
- TST-04 (Majeur) LayerViewModel : annulation de rendu, rechargement, ClearFile non couverts (pas de fake IPdfDocumentService). LayerViewModel.cs:103-172.
- TST-05 (Mineur) ZoomAt/Pan/FitToWindow/UpdateCursorPosition non testés ; calage jamais testé sous vue zoomée. MainViewModel.cs:184-238.
- TST-06 (Mineur) UndoAlignmentPoint (rollback matrice à l'étape 3) non testé. MainViewModel.cs:267-285.
- TST-07 (Mineur) ExportAsync 0 % (stub retourne null) ; branche vue courante + chemin d'erreur non couverts. MainViewModel.cs:304-344.
- TST-08 (Mineur) BuildRenderLayers/ToRenderInfo (dim de calage, conversion opacité) non couverts. MainViewModel.cs:421-424.
- TST-09 (Info) Assertions : RevisionBakedTransform dupliqué avec le générateur de samples ; Assert.True(HandleAlignmentClick) ne distingue pas traité/ignoré.

## FIABILITÉ
- FIA-01 (Bloquant) Use-after-free SKImage : pendant ExportAsync (Task.Run long), un changement de page dispose le raster (RetireBitmap ne compte que la composition viewport : soit dispose immédiat si _composeRunning=false, soit purge du finally dès la fin de la compose viewport) → AccessViolationException ou PNG corrompu. MainViewModel.cs:311-336,492-498 ; LayerViewModel.cs:142-143.
- FIA-02 (Majeur) = ARC-03/SEC-06 : _bytesCache non borné → croissance mémoire monotone sur session longue.
- FIA-03 (Mineur) RequestRecompose sans try/catch : sous pression mémoire, MessageBox modale par MouseMove (tempête) + _composeDirty perdu (vue figée périmée).
- FIA-04 (Mineur) DispatcherUnhandledException Handled=true inconditionnel, y compris OutOfMemory/état corrompu ; pas de garde anti-rafale. App.xaml.cs:19-26.
- FIA-05 (Mineur) = ARC-02 + : en mode --screenshot, une exception de CaptureWindow → hang silencieux (Shutdown jamais atteint), rien de journalisé.
- FIA-06 (Mineur) CTS jamais disposés ; IsLoading retombe trop tôt sur rendus chevauchés ; double rendu au chargement (SelectedPage=1 déclenche un rendu + l'appel explicite). LayerViewModel.cs:87-156.
- FIA-07 (Mineur) Rendu PDFium non annulable sous verrou global : feuilletage rapide → latence perçue de plusieurs secondes (limite PDFium, à documenter/afficher).
- FIA-08 (Mineur) Fallback File.ReadAllBytes hors try dans RenderPageAsync (IOException brute) ; détection « password » par sous-chaîne fragile. PdfDocumentService.cs:62,87-96.
- FIA-09 (Info) Loupe UI-thread 30 img/s : bornée, OK (magnification → pas de build mipmap).
- FIA-10 (Info) ClearFileCommand non câblé ; si câblé un jour, HasAnyDocument jamais recalculé → export PNG blanc possible.

## SÉCURITÉ (vérité-terrain : dotnet list --vulnerable → App/Core 0 vuln ; Tests : System.Net.Http 4.3.0 + System.Text.RegularExpressions 4.3.0 High, transitves du Test SDK)
- SEC-01 (Mineur) Garde-fou raster contourné par MediaBox géante : clamp MinDpi=72 annule le plafond 8192 px dès que la page dépasse 8192 pt → allocation ~830 Mo+ possible par PDF forgé (dégradation non silencieuse mais pic d'allocation réel). LayerViewModel.cs:124.
- SEC-02 (Mineur) Vulnérabilités transitives High dans le projet de TESTS uniquement (Test SDK 17.8.0/xunit 2.5.3) — non expédiées.
- SEC-03 (Mineur) Exe self-contained non signé (SmartScreen, intégrité invérifiable) — Majeur si distribution à des tiers.
- SEC-04 (Info) Posture PDFium : in-process sans sandbox, sans V8/XFA (flavor bblanchon non-V8), surface = parseur/rasterizer ; seul contrôle = fraîcheur du paquet (152.0.7961 courante) ; CVE natives non couvertes par l'audit NuGet.
- SEC-05 (Info) Crash log %TEMP% par utilisateur : OK ; croissance non bornée (pas de rotation).
- SEC-06 (Info) = ARC-03/FIA-02 (cache octets non purgé).
- SEC-07 (Info) Path traversal / secrets / DPAPI : hors modèle ici (tous les chemins viennent de l'utilisateur ; aucun chemin dérivé du contenu PDF ; zéro secret) — constat, ne pas requalifier.

## PACKAGING
- PKG-01 (Majeur) Aucune Version dans les csproj : l'exe s'annonce 1.0.0.0 avant la v0.1.0 ; aucun tag git. (Hash git déjà dans ProductVersion — bien.)
- PKG-02 (Mineur) Versions de packages épinglées (bien) mais pas de global.json ni packages.lock.json ; M.E.DI 10.0.11 (série .NET 10) sur net8.0.
- PKG-03 (Majeur) Promesse single-file non tenue : 7 DLLs natives requises à côté de l'exe (pdfium, libSkiaSharp, natives WPF) ; copie de l'exe seul → crash au premier rendu. IncludeNativeLibrariesForSelfExtract absent ; options de publish uniquement dans CLAUDE.md.
- PKG-04 (Mineur) PDB livrés dans publish/ (~89 Mo dont libSkiaSharp.pdb).
- PKG-05 (Mineur) EnableCompressionInSingleFile absent (exe 161 Mo, −40/50 % possible).
- PKG-06 (Mineur) Binaires non signés (exe NotSigned, pdfium.dll NotSigned) — calibré petite échelle ; publier un SHA-256 au minimum.
- PKG-07 (Info) Pas de stratégie de mise à jour — acceptable ; le vrai manque est PKG-01 (savoir quelle version tourne).

## PERTINENCE
- PERT-01 (Mineur) ClearFileCommand jamais branché (aucun binding/appel/test) — feature « retirer un plan » à trancher.
- PERT-02 (Mineur) IsComparisonVisible : propriété morte (doublon de HasAnyDocument, zéro référence).
- PERT-03 (Mineur) = ARC-05/MNT-05 (outillage --screenshot/--align en prod) — question : garder documenté ou retirer ?
- PERT-04 (Info) Chargement CLI base.pdf/revision.pdf : vraie feature mais hors DESIGN_PLAN — consacrer ou retirer avec PERT-03.
- PERT-05 (Info) IComparisonCompositor.Compose(SKCanvas) sans appelant externe — surcharge d'interface à justifier ou privatiser.
- PERT-06 (Info) Scope fantôme documentaire : DESIGN_PLAN promet bandeau « page vide », progression d'export annulable, motion (150/200 ms), thème light — rien dans le code ; DoD cochée sans ces points.
- PERT-07 (Info) Tokens définis jamais consommés (SpaceXS/S/XL, FontSizeTitle, RadiusControl).

## Recoupements déjà identifiés par l'orchestrateur
- Cache PDF non purgé : ARC-03 = FIA-02 = SEC-06 (sévérité max proposée : Majeur).
- Outillage prod : ARC-05 = MNT-05 = PERT-03 (+ FIA-05 pour le hang --screenshot).
- async void / fire-and-forget démarrage : ARC-02 = FIA-05 (+ MNT-08, TST-02 pour la testabilité).
- .editorconfig/analyzers : ARC-06 = MNT-06.
- Signature : SEC-03 = PKG-06.
