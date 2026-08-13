# ROADMAP — DrawingComparator

> Alimentée en fin de chaque cycle ZEUS (checkpoint 4). Les items « UAT utilisateur » viennent
> d'un usage réel de l'app : ils priment sur le reste à l'ouverture du cycle suivant.

## Priorité 1 — Demandes utilisateur (UAT v0.1.0, 2026-08-13)

1. **Qualité du raster** — améliorer la netteté des plans convertis en image (pixellisation visible). Pistes déjà actées par le LLM-Council : rendu par région PDFium au DPI de la vue (netteté vectorielle à tout zoom, mémoire constante — prévoir l'offset de région dans LayerRenderInfo, cf. SEN-14) ; à défaut, relever le plafond de rasterisation quand la RAM le permet.
2. **Projets de superposition** — sauvegarder un projet (les 2 PDF, pages, matrice de calage, opacités, teintes) et le rouvrir ; barre/panneau « projets récents » au démarrage. Format simple (JSON à côté ou %APPDATA%), chemin des PDF re-validé à l'ouverture.
3. **Calage fin au clavier** — dans l'outil de calage : mode « ajustement » où les flèches du clavier translatent le plan RÉVISÉ d'un pas très faible, pas adaptable (ex. 0,1 / 0,5 / 2 mm papier, modificateurs Maj/Ctrl ou réglage visible). Complète le calage 2 points pour la retouche finale.

## Priorité 2 — Produit / UX (design-review n°1 + LLM-Council)

4. Assombrissement plein canvas à 60 % en mode calage (au lieu de l'atténuation du calque inactif).
5. Snackbar de confirmation d'export (actuellement barre de statut).
6. Thème light — trancher l'ambition (au démarrage suffit probablement, cf. SEN-13) puis implémenter les tokens light du DESIGN_PLAN.
7. 3e point de contrôle optionnel affichant l'erreur résiduelle, puis transformation affine 3 points (risque anisotropie « fit to page » ; corriger le fallback silencieux SEN-01 au passage).
8. Binarisation optionnelle pour PDF scannés (fond gris/bruit).
9. Reportés du DESIGN_PLAN §10 : bandeau « cette page semble vide », progression déterminée + annulation de l'export, motion du calage (assombrissement 150 ms, « clac » 200 ms).

## Priorité 3 — Dette technique (dev-council n°1 P3 + restes dev-senior)

10. Tests complémentaires : chemins d'erreur PDF + fake IPdfDocumentService (TST-03/04), zoom/pan + calage sous vue zoomée (TST-05), UndoAlignmentPoint (TST-06), ExportAsync côté VM (TST-07/08).
11. Fiabilité : latence PDFium affichée pendant le feuilletage (FIA-07), état cohérent si le rendu échoue après une ouverture réussie (SEN-04), sérialiser les drops rapides (SEN-08), purge des rasters au Dispose (SEN-05).
12. Nettoyage : câbler ou retirer ClearFile (PERT-01/SEN-12), privatiser Compose(SKCanvas) (PERT-05), tokens orphelins (PERT-07), ADR « Skia dans les contrats du Core » (ARC-08), documenter l'hypothèse de lecture concurrente Skia (SEN-07).
13. Sécurité/ops : rotation du crash log (SEC-05), cadence de mise à jour PDFtoImage/PDFium — seul contrôle contre les CVE natives (SEC-04), bump Test SDK (SEC-02), DPI d'export annoncé vs raster réel (SEN-11 — résolu de fait par l'item 1).
