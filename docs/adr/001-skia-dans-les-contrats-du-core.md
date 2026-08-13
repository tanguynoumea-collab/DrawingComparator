# ADR 001 — Types SkiaSharp dans les contrats du Core

Statut : accepté (cycle 2, 2026-08-13 — répond au finding ARC-08 du dev-council n°1).

## Contexte

`DrawingComparator.Core` expose des types SkiaSharp dans ses contrats publics :
`SKBitmap`/`SKImage` (rasters), `SKMatrix` (alignement, vue), `SKSize`/`SKPoint`/`SKRect`
(géométrie en points PDF). L'orthodoxie voudrait des abstractions neutres (des records
maison) pour ne pas coupler les contrats à une bibliothèque tierce.

## Décision

Skia RESTE dans les contrats du Core. C'est un choix assumé, pas un oubli.

## Justification

- Skia n'est pas un détail d'implémentation ici : c'est le moteur de composition du
  produit (multiply, color-matrix, échantillonnage mipmap). Le Core sans Skia n'aurait
  aucun sens fonctionnel — l'abstraire reviendrait à réécrire son vocabulaire.
- Une couche de types miroirs (Matrix3x2 maison, PixelBuffer maison) ajouterait des
  conversions à chaque frontière — du code, des copies et des bugs de conversion — sans
  aucun consommateur alternatif en vue : l'app est mono-front WPF, mono-utilisateur.
- `SKMatrix` en 3×2 générale est précisément la représentation contractuelle voulue pour
  l'alignement (similitude aujourd'hui, affine demain) ; les tests du Core l'exercent
  directement.

## Conséquences

- Le remplacement de SkiaSharp serait une refonte du Core, pas un swap de dépendance.
  Accepté : la probabilité est faible, le coût de l'assurance est permanent.
- La règle de sérialisation compense le couplage : AUCUN type Skia ne sort vers le disque
  (les fichiers projet `.dcproj` sérialisent la matrice en six floats nommés — voir
  `ProjectService`), donc les données utilisateur ne dépendent pas du layout de SKMatrix.
- La version de SkiaSharp est alignée entre Core et App par la restauration NuGet
  (packages.lock.json) et surveillée par Dependabot (SEC-04).
