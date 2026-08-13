# DEV-SENIOR n°1 — Audit externe DrawingComparator
**Date :** 2026-08-13 · **Mode :** audit ciblé sur les 5 points d'AUDIT_POINTS.md (regard froid, lecture seule, .zeus/ ignoré)

## Verdicts par point
1. Justesse géométrique de bout en bout : **SOLIDE** (chaîne complète tracée, aucune conversion inversée ; SEN-01/SEN-02 Info)
2. Cycle de vie des ressources natives : **SOLIDE** (SEN-03/04 Mineurs, SEN-05 Info)
3. Modèle de threading : **SOLIDE** (SEN-06/07/08 Info)
4. Fidélité du diff visuel : **RÉSERVES** — SEN-09 **Majeur** (canal R pris comme luminance : les annotations rouges/jaunes disparaissaient du comparatif), SEN-10 Mineur (flou à 125/150 % DPI), SEN-11 Info
5. Reprenabilité/extensibilité : **SOLIDE** (« chemin trouvé en bien moins d'une heure » ; SEN-12/13/14)

## Verdict global : GO AVEC RÉSERVES → réserves levées
Corrections appliquées (itération dev_council/senior 2/3, commit 4f43a52) :
- **SEN-09 fermé** : luminance BT.709 dans la matrice de teinte + test pixel « trait rouge source visible dans les deux teintes ».
- **SEN-10 fermé** : composite rendu en pixels physiques (WriteableBitmap au DPI écran), conversions DIP↔device dans ComparatorView, loupe incluse.
- **SEN-02 fermé** : budget MaxPixels sur l'export vue courante.
- **SEN-03 fermé** : dispose garanti (try/finally) du bitmap viewport.
- **SEN-06 fermé** : asserts de thread (Debug) sur Begin/End/RetireBitmap.
Gate re-passé : build 0 erreur (analyzers stricts), 29/29 tests, format propre, publish 79,4 Mo.

## Restes → roadmap
SEN-01 (fallback silencieux MapBaseToRevision — pertinent avec l'affine 3 points), SEN-04 (état incohérent si le rendu échoue après une ouverture réussie), SEN-05 (purge des rasters au Dispose), SEN-07 (documenter l'hypothèse de lecture concurrente Skia), SEN-08 (sérialiser les drops rapides), SEN-11 (DPI d'export annoncé vs raster réel — sera résolu par le rendu par région), SEN-12 (=PERT-01 ClearFile), SEN-13 (trancher l'ambition du thème light), SEN-14 (offset de région dans LayerRenderInfo).

## Non vérifié (déclaré par l'auditeur)
Exécution réelle de l'app et rendu 125/150 % à l'écran ; thread-safety des lectures concurrentes d'un même SKImage (hypothèse Skia) ; internals PDFtoImage ; comportement sur PDF protégés réels ; exe single-file non exécuté sur machine vierge.
