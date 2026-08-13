# DESIGN-REVIEW n°2 — Cycle 2 (2026-08-13)

Protocole : app réelle Release lancée via l'outillage CLI (`--screenshot`, `--align`), captures dans
`docs/ui-baseline/cycle2/`, critique contre DESIGN_PLAN v1.1 (§11) + heuristiques.
Projet et liste Reprendre seedés pour rendre l'état vide représentatif.

## Captures

| Fichier | Écran / état |
|---|---|
| 01-etat-vide-reprendre.png | État vide + section Reprendre (re-capturé après correction) |
| 02-comparaison.png | Comparaison nominale, cartes calques v1.1 |
| 03-mode-calage-scrim.png | Mode calage : bandeau, scrim 60 %, loupe |

## Verdicts par écran

### État vide + Reprendre — ✅ conforme (après 1 bloquant corrigé)
- **BLOQUANT corrigé (itération 1/3)** : le canvas composait un bitmap BLANC sans aucun document
  (la recomposition tournait à vide) — la liste Reprendre en TextPrimary y était illisible et le
  fond violait le wireframe §3 (« fond sombre »). Correctif : `RequestRecompose` ne compose plus
  sans document (CompositeBitmap = null → fond sombre) + `HasAnyDocument` recalculé dans
  OnLayerChanged (ClearFile ne le mettait pas à jour — bug attrapé au passage). Re-capture : ✅.
- Hiérarchie conforme : zones de dépôt 1er, phrase 2e, Reprendre 3e. Double liseré rouge|bleu ✅,
  dates Cascadia Mono ✅, hover discret ✅.

### Comparaison — ✅ conforme
- Barre d'outils v1.1 : groupe projet (Ouvrir…/Enregistrer) | séparateur | Caler (SEUL bouton
  ambre) | ⇄ | Exporter ✅. « Enregistrer » désactivé tant que les 2 plans ne sont pas là ✅.
- Cartes calques : liseré teinté, page, ⋯, ✕ (PERT-01), opacité, toggle ◐ Binariser ✅.
- Panneau CALAGE « non calé », Réinitialiser désactivé ✅. Statut : coordonnées/zoom/pages ✅.

### Mode calage — ✅ conforme
- Bandeau 4 chips + instruction métier + aide Échap/Retour ✅. Scrim 60 % plein canvas ✅
  (chrome latéral et bandeau hors voile ✅). Loupe à liseré ambre + réticule ✅ — elle perce le
  voile à pleine lumière (décision §11.3). « Caler les plans » désactivé pendant la session ✅.

## Mineurs → roadmap

- M1 (pré-existant) : la barre de statut affiche des coordonnées curseur sans aucun document chargé.
- M2 : la loupe rend une pastille blanche quand le curseur est hors de la feuille (marginal en usage réel).

## Non capturés par l'outillage headless (vérifiés par code + tests, à confirmer en UAT)

État « Calé » du bandeau (étape 5 + résiduel + Terminer), bloc AJUSTER, snackbar d'export,
dialogue de progression + annulation, effet binarisation sur scan réel, bandeau page vide,
thème light (le poste de capture est en thème sombre). L'UAT du checkpoint 3 les couvrira.

## Verdict

**PASSE** — 1 bloquant trouvé et corrigé dans la boucle (itération design_review 1/3), re-gate
69/69 vert, re-capture conforme. 2 mineurs consignés pour la roadmap. → DEV-COUNCIL.
