# LLM-Council — Approche technique DrawingComparator
**Date :** 2026-08-13 · **Question :** comment implémenter le comparateur de plans PDF rouge/bleu (WPF .NET 8) ?
**Protocole :** 5 membres indépendants (Pragmatique, Red-teamer, Rigoriste, Premiers principes, Généraliste) → relecture à l'aveugle par 5 relecteurs → synthèse du Président.

## Classement agrégé (position moyenne, 5 relecteurs)

| Rang | Réponse | Membre | Moyenne |
|---|---|---|---|
| 1 | A | Rigoriste du domaine | 1.0 (unanime) |
| 2 | D | Red-teamer | 2.2 |
| 3 | B | Généraliste | 2.8 |
| 4 | E | Premiers principes | 4.0 |
| 5 | C | Pragmatique | 4.8 |

## ARCHITECTURE RETENUE (synthèse)

### 1. Pipeline de rendu — PDFium + SkiaSharp, composition CPU sur le viewport
- Rasterisation : **PDFium**. PDFtoImage pour le rendu page entière ; **Docnet ou PDFiumCore** pour le rendu par région (PDFtoImage ne l'expose pas — point pratique du Généraliste).
- **PDFium n'est pas thread-safe** : verrou global sur tous les appels de rendu, rendus asynchrones avec `CancellationToken` pour jeter les rendus périmés.
- Teinte : `SKColorFilter.CreateColorMatrix` appliqué **au moment du dessin** (rouge : R'=255, G'=B'=L ; bleu symétrique). Permutation rouge/bleu = échange de deux matrices, coût nul.
- Composition : `SKSurface` de la taille du **viewport** (~2-4 Mpx), calque 1 normal, calque 2 avec `SKBlendMode.Multiply` + matrice d'alignement, blit vers un `WriteableBitmap` unique affiché dans un seul `Image`.

### 2. Blending — multiplicatif (unanimité des 5 membres)
- Blanc = élément neutre : trait rouge seul reste rouge pur, bleu seul bleu pur, recouvrement → violet très sombre/quasi noir. Modèle physique de la table lumineuse.
- Additif rejeté (sature sur fond blanc). Min/darken rejeté (gris neutre au recouvrement, AA moins bien géré). Alpha-blending 50/50 **proscrit comme mécanisme central** (rendu délavé).
- **Opacité des sliders = lerp vers le blanc dans la color matrix** (L' = 1 − op·(1−L)), jamais un alpha — l'alpha interagit mal avec multiply.

### 3. Transformation 2 points — matrice appliquée à la composition, jamais de re-bake
- Calcul fermé de la similitude : `s = |q2−q1|/|p2−p1|`, `θ = atan2(q2−q1) − atan2(p2−p1)`, `M = T(q1)·R(θ)·S(s)·T(−p1)`. Fonction pure statique, couverte de tests (identité, échelle pure, rotation 90°, cas dégénéré p1=p2 → rejet explicite).
- **Stocker une Matrix générale 3×2** (pas un quadruplet tx/ty/s/θ) — conseil clé du Red-teamer : rend l'affine 3 points gratuite en v1.1.
- **Matrice exprimée en coordonnées PDF (points), pas en pixels** (Premiers principes) : l'alignement survit aux changements de DPI et sert tel quel à l'export.
- Application : `canvas.SetMatrix(view ∘ M)` au dessin du calque 2, échantillonnage cubique. Un seul ré-échantillonnage par rendu, jamais cumulé, réalignement instantané et réversible.
- Outil = machine à états explicite (Idle → Pt1Plan2 → Pt1Plan1 → Pt2Plan2 → Pt2Plan1 → Aligné), Échap annule, **zoom/pan actif entre les clics** (indispensable pour viser un angle de mur).

### 4. Performance — rendu piloté par le viewport, DPI adaptatif, pas de tuilage v1
- Chiffres qui commandent tout : A0 à 300 DPI ≈ 9930×14040 px ≈ 560 Mo BGRA **par calque** → full-res permanent intenable et au-delà des limites de texture GPU (8192 px courant).
- À chaque état stable de la vue : rasteriser **uniquement la région visible** au DPI écran ×1,25, asynchrone (50-150 ms). Mémoire bornée par la fenêtre, netteté vectorielle à tout zoom dès la v1.
- Pendant le geste zoom/pan : `RenderTransform` WPF (GPU) sur le dernier composite, puis re-rendu débouncé (~150 ms). Fond de secours : composite pleine feuille ~72-100 DPI.
- Tuilage/pyramide rejeté en v1 (chaque tick de slider invaliderait la pyramide) ; l'interface `RenderRegion(page, rect, dpi)` prépare l'évolution.
- Export PNG : **même compositeur** rejoué offscreen au DPI demandé (300), **par bandes horizontales** → WYSIWYG garanti, zéro duplication de code de rendu.

### 5. Structure MVVM
```
DrawingComparator.Core (net8.0, sans WPF) :
  AlignmentMath                — statique pure, similitude 2 points (tests exacts)
  IPdfDocumentService          — ouverture, pages, dimensions en points PDF, verrou PDFium
  IRasterizer                  — RenderRegion(doc, page, srcRect, targetSize, ct) → bitmap
  IComparisonCompositor        — Compose(layer1, layer2, alignM, viewM, viewport, opts) — golden tests
  IExportService               — rendu par bandes + PNG
DrawingComparator.App (WPF) :
  MainViewModel, 2× LayerViewModel (fichier, page, opacité, rôle couleur)
  AlignmentViewModel           — machine à états des 4 clics
  ComparatorView               — contrôle custom mince : souris → coordonnées document, délègue au VM
```
- CommunityToolkit.Mvvm + Microsoft.Extensions.DependencyInjection. Services image sur buffers bruts → testables sans thread STA.
- Déploiement : `dotnet publish -r win-x64 --self-contained`, **PublishTrimmed=false** (WPF non trimmable, pdfium.dll natif).

## Risques produit actés (Red-teamer — à intégrer au design et à la doc)
1. **Anisotropie/cisaillement** : la similitude 4 DDL ne corrige pas un « fit to page » étiré → faux positifs loin des points de calage. Mitigation : Matrix 3×2 générale dès v1, 3e point de contrôle optionnel affichant l'erreur résiduelle, affine 3 points en v1.1.
2. **Franges d'anti-crénelage** : inévitables (frange ≈ 40 % d'un trait de 0,25 mm à 250 DPI) — outil de *revue visuelle*, pas de diff exact. À documenter.
3. **PDF scannés** (fond gris, bruit) : le multiply produit une bouillie sombre. V1 : avertissement ; v2 : binarisation optionnelle. Validation marché du concept : Bluebeam Revu fait exactement cette superposition.

## Alternatives écartées et pourquoi
- **ShaderEffect WPF multi-input (proposition Pragmatique, classée 5e)** : toolchain fxc archaïque en .NET 8, mapping de coordonnées entre samplers fragile, limite texture 8192 px → dépassement silencieux ; son plan B (alpha-blending) est le rendu délavé à proscrire.
- **Composite CPU pleine page à DPI plafonné (Premiers principes, 4e)** : élégant (packing de canaux) mais image molle en zoom profond et sliders à 50-150 ms — sous le cahier des charges. Son insight « M en coordonnées PDF » est conservé.
- **Additif / min-darken / alpha 50-50** : rejetés mathématiquement (cf. § 2).
- **Tuilage deep-zoom, diff vectoriel des opérateurs PDF, MatrixTransform WPF sur l'Image du plan 2** : coût/bénéfice défavorable v1 ou incompatible avec le multiply Skia.

## Désaccord réel et arbitrage
Le seul vrai clivage : **composition Skia par viewport (A/B)** vs **composite CPU statique pleine page (D/E)**. Tranché pour le viewport — c'est le seul schéma qui donne à la fois la netteté vectorielle à tout zoom, des sliders temps réel et une mémoire bornée. Le Président a grefé sur ce socle les protections du Red-teamer (Matrix 3×2, 3e point de contrôle, avertissement scans) et la convention de coordonnées PDF des Premiers principes.
