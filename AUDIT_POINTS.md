# AUDIT_POINTS — DrawingComparator

> Les 5 points fixes de l'audit externe DEV-SENIOR, versionnés avec le projet.
> Proposés par ZEUS au cycle 1, à ratifier (ou amender) au checkpoint de publication.

1. **Justesse géométrique de bout en bout** — la chaîne de repères (pixels écran ↔ points PDF du plan de base ↔ points PDF du plan révisé, via ViewMatrix et AlignMatrix) est-elle cohérente partout (clics de calage, loupe, curseur, export), sans conversion oubliée ni inversée ?
2. **Cycle de vie des ressources natives** — SKImage/SKBitmap/PDFium : zéro chemin de use-after-free, de fuite ou de double-dispose sous usage réel (rechargements, annulations, export concurrent, fermeture).
3. **Modèle de threading** — les invariants « mutations sur le thread UI, lectures de fond encadrées » tiennent-ils dans tous les chemins (async void restants, continuations, drag & drop) ?
4. **Fidélité du diff visuel** — le pipeline teinte → multiply → échantillonnage produit-il exactement le contrat (identique sombre, différences pures, opacité = lerp blanc) à tous les niveaux de zoom, y compris les cas limites (opacité 0, pages de tailles différentes, permutation) ?
5. **Reprenabilité et extensibilité** — les évolutions déjà actées en roadmap (rendu par région au zoom, affine 3 points, thème light) s'insèrent-elles sans refonte, et un dev qui débarque trouve-t-il le chemin en moins d'une heure ?
