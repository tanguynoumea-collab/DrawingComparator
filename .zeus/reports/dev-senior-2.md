# DEV-SENIOR n°2 — Cycle 2 DrawingComparator (2026-08-13)

Audit externe lecture seule sur les 5 points d'AUDIT_POINTS.md (rafraîchis), commit audité d9b2e82.
Vérité-terrain : build 0 warning, 75/75 tests, chaînes de matrices vérifiées à la main, 2 findings
prouvés par harnais exécuté contre le Core compilé.

## Verdict initial : GO AVEC RÉSERVES — 4 Majeurs manqués par le council, corrigés en boucle (itération 2/3)

| ID | Sévérité | Constat | Correction (commit suivant) |
|---|---|---|---|
| SEN2-01 | Majeur | Export A0/A1 @300 DPI réellement à ~247 DPI : bande pleine largeur > MaxRenderEdge → PDFium dégrade en silence, upscale CatmullRom ×1,2 — la promesse SEN-11 fausse pour toute feuille > ~1966 pt | Export en VRAIES tuiles X×Y bornées sous le plafond d'arête (marges comprises) ; test A0 sur fake vérifiant que chaque région tient au DPI demandé |
| SEN2-02 | Majeur | .dcproj sans champ FilePath → ArgumentNullException HORS de la frontière (crash générique) ; matrice singulière acceptée (calque muet) | ProjectSerializer.Validate : rejet FilePath null/vide + rejet déterminant ~0 ; 2 tests dédiés |
| SEN2-03 | Majeur | Échap pendant la pose du point de contrôle → Matrix = _beforeSession : le calage COMMITTÉ est détruit sans confirmation | Cancel depuis ControlOnRevision/ControlOnBase → retour à Aligned, matrice intacte ; test dédié |
| SEN2-04 | Majeur | DPI des tuiles ET des régions d'export du révisé rendu dans SON espace sans compenser l'échelle du calage : révisé flou ×s dès que les plans ne sont pas à la même échelle (1:50 vs 1:100) | Facteur √|det(AlignMatrix)| appliqué au DPI du révisé (tuiles VM + export) ; test export ×2 → 288 DPI |
| SEN2-05 | Mineur | Nudge silencieusement annulé par bascule Rigide/Affine et Undo (recalcul depuis les couples) | → roadmap |
| SEN2-06 | Mineur | ClearFile : CTS annulé non disposé ; _detailCts du VM non annulé (rejoint FIA2-03) | CTS disposé (fait) ; _detailCts → roadmap |
| SEN2-07 | Info | Écrans ~8K : tuile de vue sature MaxRenderEdge (dégradation silencieuse) | → roadmap (documentation) |
| SEN2-08 | Info | Export « vue courante » 2× = agrandissement de pixels, pas de netteté | → roadmap (reformuler le commentaire/l'option) |
| SEN2-09 | Info | KeyBinding « A » nu au niveau fenêtre — piège du premier champ texte futur | → roadmap |

## Verdicts par point d'audit (après corrections)

1. **Justesse géométrique** : SOLIDE avec preuve — aucune conversion inversée ; SEN2-04 était un
   défaut de résolution, pas de position — corrigé.
2. **Ressources natives** : SOLIDE — Retire/Begin-End cohérent, tuiles annulées disposées ; SEN2-06 corrigé.
3. **Threading** : SOLIDE — continuations sur contexte UI, pas de fenêtre de course exploitable.
4. **Fidélité du diff** : contrat teinte→multiply→échantillonnage tenu ; les deux casses de
   résolution (SEN2-01/04) corrigées et testées.
5. **Reprenabilité** : BONNE — AlignmentSession propre, IProjectStore, schéma versionné, chemins
   d'évolution identifiés.

## Re-gate après corrections

Build 0 warning, format OK, **81/81 tests** (6 nouveaux : tuiles A0 sous plafond, DPI compensé ×2,
FilePath manquant, matrice singulière, Échap pendant contrôle, miroir MaxRegionEdgePx).

**Verdict final : GO** (réserves levées ; SEN2-05/07/08/09 + reliquat SEN2-06 → roadmap).

## Non vérifié
CI GitHub réelle et publish.ps1 en ligne (premier push) ; UNC/SMB réel ; DPI mixtes multi-écrans ;
mémoire réelle sous écran 8K ; rendu visuel (périmètre design-review).
