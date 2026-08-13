# Definition of Done — DrawingComparator

> Instanciée à l'initialisation ZEUS, adaptée au projet, puis STABLE (toute modification est une décision explicite, consignée dans l'historique d'état). C'est le SEUL critère de sortie de la phase GSD.

## Socle

- [x] Toutes les tâches du cycle (.zeus/tasks.md) sont `done`
- [x] `dotnet build -c Release` propre sur la cible net8.0-windows (App) + net8.0 (Core, Tests)
- [x] Tests unitaires : 100 % verts (DrawingComparator.Tests — AlignmentMath, machine à états de calage, compositeur)
- [x] Zéro warning de sévérité Warning+ au build Release
- [x] Aucune valeur magique UI (couleurs/tailles hors Themes/Tokens.xaml)
- [x] Aucun blocage du thread UI (rasterisation et export asynchrones, verrou PDFium global)
- [x] Les chaînes visibles respectent le vocabulaire du DESIGN_PLAN (Caler les plans, plan de BASE / plan RÉVISÉ, Exporter PNG)

## Spécifique projet

- [x] Exe publiable : `dotnet publish -r win-x64 --self-contained` sans trimming, pdfium natif embarqué
- [x] PDF corrompu / protégé / page inexistante → dialogue d'erreur explicite, jamais de crash
- [x] Matrice d’alignement stockée en 3×2 générale, exprimée en points PDF

## Par fonctionnalité du cycle

| Fonctionnalité | Critère d'acceptation observable | Fait |
|---|---|---|
| Chargement 2 PDF + teinte rouge/bleu | Deux PDF chargés (bouton ou drop) s'affichent superposés, base rouge, révision bleue | ☑ |
| Blending multiply | Traits communs sombres, traits propres rouge/bleu purs, fond blanc | ☑ |
| Sélecteur de page | Changer la page d'un plan recompose le comparatif | ☑ |
| Calage 2 points | 4 clics guidés (bandeau d'étapes) → plan 2 translaté/mis à l'échelle/pivoté, ancré au 1er point ; Échap annule ; points confondus refusés | ☑ |
| Zoom/pan | Molette = zoom centré curseur, drag = pan, double-clic = ajuster ; fluide sur A0 | ☑ |
| Opacité + permutation | Sliders par plan effectifs (lerp vers blanc), bouton ⇄ échange les teintes | ☑ |
| Export PNG | Ctrl+E → PNG du comparatif écrit sur disque, WYSIWYG | ☑ |
| Réinitialisation calage | Bouton Réinitialiser remet la matrice identité | ☑ |
