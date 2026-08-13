# LLM-Council — Cycle 2 DrawingComparator (2026-08-13)

Question : comment implémenter les 14 points de docs/ROADMAP.md (architecture, ordre, découpage) ?
Protocole : 5 membres indépendants (Pragmatique, Red-teamer, Rigoriste, Premiers principes, Généraliste),
relecture à l'aveugle par 5 relecteurs avec vérification sur pièces (repo + cache NuGet), synthèse Président.

## Classement agrégé (unanime, 5/5 relecteurs)

1. **Rigoriste** — seul à avoir vérifié le fait pivot, exact partout où contrôlé.
2. Premiers principes — décomposition en 5 problèmes, catch unique du double-multiply.
3. Généraliste — catch de la Strength→blanc (item 4), corrections produit pertinentes.
4. Pragmatique — séquencement sain mais « SEN-11 mort par construction » sans export tuilé.
5. Red-teamer — analyse fine mais prémisse centrale falsifiée (voir ci-dessous).

## Fait décisif (vérité-terrain, vérifié par 4 relecteurs indépendamment)

**PDFtoImage 5.3.0 (version installée) expose le rendu par région** : `RenderOptions.Bounds`
(RectangleF?, « The bound units are relative to the PDF size (at 72 DPI) » — points PDF, origine
haut-gauche, Y-down, exactement le repère doc de l'app), `DpiRelativeToBounds`, `UseTiling`.
**Le rapport llm-council-2026-08-13.md (cycle 1, l.18) affirmait le contraire et est PÉRIMÉ sur ce point.**
Aucune migration Docnet/PDFiumCore n'est nécessaire ; pas de double binding PDFium.

Risque résiduel réel (Red-teamer + Rigoriste, vérifié `PdfDocumentService.cs:115`) : l'API stateless
re-parse `doc.Bytes` à chaque rendu → chaque tuile paie le parsing. Mitigation : debounce + fallback
pleine page masquent la latence ; si mesure > ~300 ms/tuile, bascule vers un handle PDFium persistant
**confinée dans PdfDocumentService** (c'est le rôle de l'interface). Mesurer avant de payer.

## Architecture retenue

### Point 1 — Pipeline raster v2 (bi-raster)
- `IPdfDocumentService.RenderRegionAsync(path, page, SKRect regionPoints, float dpi, ct)` ;
  implémentation `RenderOptions(Dpi, Bounds, DpiRelativeToBounds: true, WithAnnotations, AntiAliasing: All)`
  sous le `PdfiumLock` existant.
- **CapDpi s'applique à la taille de la RÉGION** (surcharge, même code) → mémoire constante ≈ viewport ×1,5².
- `Dpi` est un int → échelle réelle recalculée depuis les pixels retournés, **scaleX et scaleY séparés**
  (l'arrondi entier sur petite région crée une anisotropie sub-pixel).
- `LayerRenderInfo` : + `SKPoint RegionOriginDoc = default` (+ RasterScaleY) — SEN-14 ; une ligne dans
  Compose : `Translation(origin).PreConcat(Scale(1/sx, 1/sy))`. Record à défaut = aucun appelant cassé.
- Calque **bi-raster** : overview pleine page (fallback permanent, affiché pendant pan/zoom, plafond
  abaissé 8192→4096 ensuite) + tuile de vue (debounce ~150 ms, swap via `RetireBitmap`).
- **INTERDIT d'empiler tuile sur overview en multiply** (zone multipliée deux fois = assombrie) :
  `BuildRenderLayers` CHOISIT l'un ou l'autre par calque (catch Premiers principes, unique).
- Géométrie : viewport → ViewMatrix⁻¹ → AABB base clampé ×1,5 ; révisé : rect base → AlignMatrix⁻¹ →
  AABB dans SON repère, clampé à SA page. DPI de vue = ViewMatrix.ScaleX × 72. Chemin région seulement
  au-delà du DPI du fallback (en dessous, mipmap actuel optimal).
- FIA-07/SEN-04/SEN-05 = cahier des charges de robustesse de CE chantier, pas des items séparés.
- Rotation ~45° : l'AABB approche la page entière — accepté (plans quasi droits), documenté.

### Export tuilé par bandes (correction de la roadmap)
« SEN-11 résolu de fait par l'item 1 » est **faux pour l'export** (page entière A0 300 DPI ≈ 139 Mpx >
budget 70 Mpx). L'export passe par `RenderRegionAsync` **par bandes** → DPI demandé réellement honoré
(SEN-11 mort), et progression déterminée + annulation (item 9b) gratuites : 1 tick par bande, le ct
traverse déjà ExportAsync. Trois lignes de roadmap = une seule tâche (avec snackbar item 5 et TST-07/08).

### Point 2 — Projets
- DTO `ComparisonProject` dans le Core : 2 chemins absolus, pages 1-based, matrice en **6 floats nommés**
  (jamais SKMatrix brut), opacités, teintes, **`schemaVersion` + champs inconnus ignorés dès le jour 1**
  (tout futur champ — binarisation, mode affine — n'invalide aucun projet).
- System.Text.Json (round-trip float exact, culture-invariant — poste FR : aucun formatage manuel).
- **Pas de JSON auto « à côté des PDF »** (partages réseau d'équipe — anti-pattern, correction roadmap) :
  fichier `.dcproj` à emplacement choisi ; MRU + récents dans `%APPDATA%\DrawingComparator\`.
- Re-validation à l'ouverture : File.Exists + taille + mtime, repli « chercher à côté du .dcproj »,
  dialogue « relocaliser ». Panneau récents sur l'état vide (emplacement naturel du DESIGN_PLAN).
- **SEN-08 est un prérequis** (ouvrir un projet = 2 chargements d'affilée = le scénario des drops rapides) :
  une file par LoadIntoLayerAsync règle les deux. PERT-01/ClearFile câblé ici (fermer un calque prend sens).

### Point 3 — Calage clavier
Pas en **mm de la feuille de BASE** (×72/25,4) ; translation post-composée repère base :
`AlignMatrix = CreateTranslation(dxPt, dyPt).PreConcat(AlignMatrix)` (idiome existant). Ne perturbe ni
échelle ni rotation. Haut = dy négatif (Y-down). `PreviewKeyDown` dans ComparatorView (code-behind
légitime : maths d'input) → `NudgeRevision`. Pas 0,1/0,5/2 mm = état du VM **visible dans le bandeau**
(découvrabilité), modifié par Maj/Ctrl. RequestRecompose coalesce déjà l'auto-repeat.

### Point 4 — Assombrissement 60 %
La Strength du compositeur interpole vers le **blanc** (multiply) — elle ne peut pas assombrir.
Décision : **scrim en XAML par-dessus le composite**, calque actif redessiné à pleine lumière ;
supprime `dimForAlignment` de BuildRenderLayers (simplification). Motion 150 ms dans la même passe.

### Point 7 — 3e point : 7a dans le cycle, 7b en opt-in explicite
- **7a** : la matrice reste la similitude ; le 3e couple affiche `‖M·p₃ − q₃‖ × 25,4/72` mm (erreur
  résiduelle). Correction **SEN-01** : plus jamais de repli silencieux — `DegenerateAlignmentException`
  si colinéarité (`|det(P)|/maxCôté < MinPointDistance`).
- **7b (affine)** : le conseil est divisé (2 membres voulaient le sortir du cycle : une affine peut
  « corriger » les différences que le produit existe pour révéler). Arbitrage Président, la consigne
  utilisateur étant « tous les points » : implémenté, mais **derrière un choix explicite**
  « 2 points (rigide) / 3 points (affine) », avec anisotropie/cisaillement AFFICHÉS (Decompose étendu :
  scaleX, scaleY=det/scaleX, shear ; avertissement si |sx/sy−1| > 0,2 %). Résolution : Cramer en double,
  deux systèmes 3×3. Jamais de bascule automatique.
- Machine de calage refactorée : liste de couples (p,q) + index (l'enum 2 points en dur ne généralise pas).

### Point 8 — Binarisation
`SKColorFilter` de seuillage **composé dans la chaîne du compositeur** (luminance → table → teinte via
CreateCompose) — zéro passe raster, réglable en direct, gratuit à l'export, flag dans LayerRenderInfo
(+1 champ projet absorbé par le schéma). Optionnelle par calque (garde le pipeline BT.709 pour les
traits colorés — mise en garde Red-teamer). V1 = seuil simple, pas de Sauvola.

### Point 6 — Thème light
Tranché (SEN-13) : **détection système au démarrage uniquement**, pas de bascule runtime. Chrome
uniquement — le canvas multiply sur fond blanc est déjà light par construction. Dictionnaire de tokens
light parallèle, swap dans App.xaml.cs. **En dernier** : les items 4/5/8/9 créent des tokens.

### Points 13-14 — Ops
CI GitHub Actions en PREMIER (windows-latest : build Release + dotnet format --verify + tests sur PR
et tag ; publish.ps1 + artefact sur Release). SEC-04 = **Dependabot** (la « cadence » devient un bot).
SEC-02 bump Test SDK validé par la CI. SEC-05 rotation crash log (~20 lignes).

## Ordre d'implémentation (vagues)

- **V0 — Rails (1-2 j)** : CI (14) + SEC-02/04/05 + nettoyage 12 (PERT-05 avant de toucher le
  compositeur ; PERT-01 reporté en V3) + **spike région** (½ j : précision de Bounds à fort zoom,
  mesure de latence/tuile — seul risque technique du cycle).
- **V1 — Pipeline raster v2 (~1 sem)** : point 1 + FIA-07/SEN-04/SEN-05 + export tuilé par bandes
  (SEN-11 + progression/annulation 9b + snackbar 5) + fake IPdfDocumentService APRÈS gel de l'interface
  + TST-03/04/07/08.
- **V2 — Calage v2 (3-4 j)** : refactor machine (liste de couples) → 4 (scrim) → motion → 3 (clavier)
  → 7a → 7b opt-in → TST-05/06 (sur la NOUVELLE machine).
- **V3 — Projets (3-4 j)** : SEN-08 (file de chargements) → point 2 complet → PERT-01/ClearFile.
- **V4 — Finitions (2 j)** : 8 (binarisation) → 9a (bandeau page vide, heuristique quasi-blanc) →
  6 (tokens light) → reste 12 (ADR ARC-08, doc SEN-07, tokens orphelins si pas déjà faits en V0).

Rejeté explicitement : le repli « relever le plafond raster si la RAM le permet » (contredit mémoire
constante, explose sur A0 — unanime) ; la migration de binding préventive (prémisse fausse) ;
le JSON auto à côté des PDF.

## Notes du conseil

- **Accord unanime** : CI d'abord ; point 1 = seul chantier structurel, absorber FIA/SEN-04/05 dedans ;
  export tuilé nécessaire ; schéma projet versionné jour 1 ; SEN-08 prérequis des projets ; 7 découpé
  7a/7b ; binarisation en color-filter ; light au démarrage seul, en dernier.
- **Vrai désaccord n°1** : pipeline avant ou après les items UAT faciles (projets/clavier). Tranché
  côté « pipeline tôt » (position majoritaire + 1er du classement) : la netteté est LA demande UAT n°1
  et a besoin du temps de cycle devant elle ; la vague projets reste une soupape promue à tout moment.
- **Vrai désaccord n°2** : 7b et 8 dans le cycle ou reportés. Tranché « dans le cycle, en opt-in
  minimal » — la consigne utilisateur est « tous les points » ; les garde-fous (anisotropie affichée,
  seuil simple) neutralisent les risques produit soulevés.
- **Outrepassé** : la recommandation du Red-teamer de scinder en deux cycles — bâtie sur la prémisse
  API falsifiée ; ses observations secondaires (re-parse, export tuilé, anti-produit affine) sont
  intégrées.
- Action de traçabilité : le rapport cycle 1 est marqué périmé sur l'API région (voir en tête).
