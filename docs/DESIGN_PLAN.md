# DESIGN_PLAN — DrawingComparator

> Statut : ☑ Brouillon ☑ Auto-critiqué (Passe B) ☐ **VALIDÉ PAR L'UTILISATEUR** (obligatoire avant tout XAML)
> Version : 1.0 — 2026-08-13

## 1. Ancrage

- **Métier / sujet de l'app** : la revue de révisions de plans du bâtiment — répondre en secondes à la question « qu'est-ce qui a changé entre ces deux versions ? », comme on le faisait en superposant deux calques sur une table lumineuse.
- **Utilisateur type** : professionnel du bâtiment (coordinateur BIM, conducteur de travaux, architecte) sur desktop Windows, plusieurs fois par semaine, sessions courtes et intenses : charger, caler, inspecter, exporter.
- **Job unique de l'écran principal** : montrer la superposition — le comparatif EST l'écran ; tout le reste s'efface autour.
- **Densité choisie** : ☑ Dense/production sur les contrôles, mais **canvas-first** : ~90 % de la surface pour les plans. C'est un instrument d'inspection, pas un formulaire.

## 2. Tokens

Le canvas affiche des documents à fond blanc (exigence du blending multiplicatif). Le chrome de l'app est donc **sombre** : il fait ressortir la feuille comme un négatoscope fait ressortir la radio, et laisse le rouge/bleu des traits comme seules couleurs vives de l'écran.

### Couleurs (6, nommées)

| Token | Hex (dark) | Hex (light) | Rôle |
|---|---|---|---|
| Background | #16181D | #F2F3F5 | Fond de fenêtre et pourtour du canvas |
| Surface | #21242C | #FFFFFF | Panneaux, cartes calques, barre d'outils |
| TextPrimary | #E8EAED | #1B1D23 | Texte courant |
| TextSecondary | #9AA0A6 | #5F6570 | Texte contextuel, raccourcis, coordonnées |
| Accent | #E8B93C | #B8860B | LA couleur d'action — **ambre chantier**. Délibérément ni rouge ni bleu : ces deux couleurs appartiennent aux plans et ne signifient qu'eux. |
| Danger | #E5534B | #C62828 | Sémantique erreur uniquement (jamais confondu avec la teinte du plan 1 : le Danger n'apparaît que dans les dialogues/snackbars, jamais sur le canvas) |

Couleurs métier (hors palette UI, constantes du domaine) : TeintePlan1 = #D32F2F, TeintePlan2 = #1E5AC8 — utilisées pour teinter les rasters ET comme liseré identitaire des cartes calques. Ce ne sont pas des couleurs d'interface : aucun bouton, aucun texte ne les emprunte.

Contraste vérifié AA dans les deux thèmes : ☑ (TextSecondary #9AA0A6 sur #21242C = 5.4:1 ; #5F6570 sur #FFFFFF = 5.9:1 ; Accent réservé aux fonds de bouton avec texte #16181D = 8.1:1)

### Typographie

| Rôle | Police | Taille | Usage |
|---|---|---|---|
| Titre | Segoe UI Variable Display | 24 | Écran vide, dialogues |
| Sous-titre | Segoe UI Variable | 18 | Titres de panneaux |
| Corps | Segoe UI Variable | 14 | Contrôles, labels |
| Caption | Segoe UI Variable | 12 | Raccourcis clavier, aides |
| Données | Cascadia Mono | 13 | Zoom %, coordonnées, échelle calculée, n° de page |

### Espacement et formes

- Grille : multiples de 4 px — XS 4 / S 8 / M 16 / L 24 / XL 32
- Rayons : contrôles 4 px, cartes 8 px
- Fondation : ☑ WPF UI (Fluent 2) — thème : ☑ Système (dark par défaut, light supporté)
- Icônes : Segoe Fluent Icons

## 3. Wireframes ASCII

### Écran principal — état comparaison (nominal)

```
┌───────────────────────────────────────────────────────────────────────┐
│ ⬒ DrawingComparator      [⊕ Caler les plans]   [⇄] [🖫 Exporter PNG]  │ ← barre d'outils (FluentWindow)
├───────────────────────────────────────────────────────────┬───────────┤
│                                                           │ CALQUES   │
│                                                           │┌─────────┐│
│              ┌─────────────────────────┐                  ││▌PLAN 1  ││ ← liseré rouge
│              │                         │                  ││ base.pdf││
│              │   feuille blanche       │                  ││ Page 3 ▾││
│              │   traits rouges/bleus/  │                  ││ ●────── ││ ← opacité
│              │   sombres (composite)   │                  │└─────────┘│
│              │                         │                  │┌─────────┐│
│              └─────────────────────────┘                  ││▌PLAN 2  ││ ← liseré bleu
│                     fond sombre                           ││ rev.pdf ││
│                                                           ││ Page 3 ▾││
│                                                           ││ ●────── ││
│                                                           │└─────────┘│
│                                                           │ Calage    │
│                                                           │ ✓ calé    │
│                                                           │ é=0.998   │
│                                                           │ θ=+0.4°   │
│                                                           │ [Réinit.] │
├───────────────────────────────────────────────────────────┴───────────┤
│ x 12.408  y 4.221 m   ·   zoom 164 %   ·   page 3/12 ↔ 3/8            │ ← barre de statut (Cascadia Mono)
└───────────────────────────────────────────────────────────────────────┘
```

Hiérarchie de lecture : 1er = le composite (seule zone claire de l'écran) · 2e = le bouton accentué « Caler les plans » (ambre, seul élément coloré du chrome) · 3e = les deux cartes calques avec leur liseré rouge/bleu.
Action primaire de l'écran : **Caler les plans** (c'est l'acte qui transforme deux PDF posés l'un sur l'autre en comparatif fiable).

### Écran principal — état vide

```
┌───────────────────────────────────────────────────────────────────────┐
│ ⬒ DrawingComparator                                                   │
├───────────────────────────────────────────────────────────────────────┤
│                                                                       │
│         ┌───────────────────┐      ┌───────────────────┐              │
│         │  ▌ PLAN DE BASE   │      │  ▌ PLAN RÉVISÉ    │              │
│         │                   │      │                   │              │
│         │  Déposer un PDF   │      │  Déposer un PDF   │              │
│         │  ou [Parcourir…]  │      │  ou [Parcourir…]  │              │
│         │  (teinté rouge)   │      │  (teinté bleu)    │              │
│         └───────────────────┘      └───────────────────┘              │
│                                                                       │
│     Les traits communs ressortiront sombres, les différences          │
│     resteront rouges ou bleues.                                       │
└───────────────────────────────────────────────────────────────────────┘
```

Hiérarchie : 1er = les deux zones de dépôt (seules surfaces claires) · 2e = la phrase d'explication du principe · 3e = rien d'autre.

### Mode calage (overlay sur le canvas — l'élément signature, § 6)

```
┌───────────────────────────────────────────────────────────────────────┐
│  ● 1 Point du plan RÉVISÉ   ○ 2 Même point sur la BASE                │
│  ○ 3 Second point du RÉVISÉ ○ 4 Même point sur la BASE      [Échap]   │ ← bandeau d'étapes
├───────────────────────────────────────────────────────────────────────┤
│                     ┌────────────┐                                    │
│                     │  ╭──────╮  │  ← loupe ×4 accrochée au curseur   │
│              ───────┼──┤  ✛   ├──┼──────                              │
│                     │  ╰──────╯  │     canvas assombri à 60 %,        │
│                     └────────────┘     seul le calque actif reste     │
│                                        à pleine opacité               │
└───────────────────────────────────────────────────────────────────────┘
```

Pendant le calage : zoom/pan restent actifs (molette + drag droit), clic gauche = poser le point, Échap = annuler proprement, retour arrière = re-poser le point précédent.

## 4. Carte de navigation

```
Écran principal (unique)
   ├──[Caler les plans / touche A]──▶ Mode calage (overlay, 4 étapes) ──▶ retour, panneau « Calage » renseigné
   ├──[Exporter PNG / Ctrl+E]──────▶ Dialogue d'export (résolution, zone) ──▶ Snackbar « Exporté → chemin »
   ├──[Parcourir / drop / Ctrl+O]──▶ Sélecteur de fichier natif
   └──[erreur PDF]─────────────────▶ ContentDialog erreur (message + action)
```

### Mapping fonctionnalités → emplacements (aucune orpheline)

| Fonctionnalité du brief | Écran | Emplacement / contrôle |
|---|---|---|
| Charger plan 1 / plan 2 | Principal | Cartes de dépôt (état vide) ; bouton ⋯ de chaque carte calque ensuite ; drag & drop permanent |
| Sélecteur de page par plan | Principal | ComboBox « Page n ▾ » dans chaque carte calque |
| Superposition teintée multiply | Principal | Le canvas composite lui-même |
| Alignement 2 points (4 clics) | Mode calage | Bouton accentué « Caler les plans » + bandeau d'étapes + loupe |
| Zoom/pan fluide | Principal + calage | Molette (zoom centré curseur), drag milieu/droit (pan), double-clic = ajuster à la fenêtre |
| Sliders d'opacité par plan | Principal | Slider dans chaque carte calque |
| Permutation rouge/bleu | Principal | Bouton ⇄ de la barre d'outils (tooltip « Permuter les teintes ») |
| Export PNG | Principal | Bouton « Exporter PNG » + Ctrl+E → dialogue |
| Réinitialiser le calage | Principal | Bouton « Réinitialiser » du panneau Calage |
| Échelle/rotation calculées (transparence du calage) | Principal | Panneau « Calage » : é=…, θ=…, en Cascadia Mono |

## 5. États non-nominaux

| Écran | État vide | Chargement | Erreur | Aucun résultat |
|---|---|---|---|---|
| Principal | Deux zones de dépôt + phrase expliquant le principe rouge/bleu (wireframe ci-dessus) | Rasterisation : ProgressRing discret DANS la carte calque concernée + canvas conservant l'ancien rendu légèrement voilé ; jamais de gel UI | ContentDialog : « Ce PDF est protégé par mot de passe. Déverrouillez-le dans votre lecteur PDF puis rechargez-le. » / « La page 9 n'existe pas dans rev.pdf (8 pages). » — cause + action, sans excuses | Page PDF blanche/vide : bandeau fin sur le canvas « Cette page semble vide — vérifier le n° de page » |
| Mode calage | s/o (inaccessible sans les 2 plans chargés — bouton désactivé avec tooltip « Chargez les deux plans d'abord ») | s/o (instantané) | Points trop proches (p1≈p2) : le 4e clic est refusé, message inline sous le bandeau « Points trop proches pour calculer l'échelle — choisissez un point plus éloigné » | s/o |
| Export | s/o | ProgressBar déterminée dans le dialogue (rendu par bandes) + annulable | Snackbar Danger « Écriture impossible (disque plein ?) — réessayer vers un autre dossier » | s/o |

## 6. Élément signature

**Le viseur de calage.** Quand l'utilisateur clique « Caler les plans », l'app devient un instrument de mesure : le canvas s'assombrit à 60 % sauf le calque concerné par l'étape courante, un bandeau montre les 4 étapes (● fait, ◉ courant, ○ à venir) avec un libellé métier (« Cliquez un angle de mur sur le plan RÉVISÉ »), et une **loupe ×4 accrochée au curseur** permet de viser l'angle de mur au pixel près — la précision du calage fait la fiabilité de tout le comparatif, l'interface doit la rendre possible ET la mettre en scène. C'est le moment « instrument d'optique » de l'app ; tout le reste de l'UI reste calme et discipliné.

## 7. Motion

- Transitions : entrée/sortie du mode calage (assombrissement 150 ms, ease-out) ; apparition des snackbars (150 ms) ; voile de rechargement du canvas (150 ms). Rien d'autre — surtout pas d'animation sur le zoom/pan (il doit coller à la main).
- Moment orchestré : au commit du 4e clic de calage, le canvas revient à pleine lumière pendant que la transformation s'applique en 200 ms — le « clac » visuel des deux plans qui tombent l'un dans l'autre. C'est la récompense du calage réussi.

## 8. Auto-critique (Passe B)

- **« Produirais-je ce plan pour n'importe quelle app similaire ? »** Première version : barre latérale générique à onglets + accent bleu standard. Révisé : (1) l'accent bleu était une faute professionnelle ici — il entrait en collision sémantique avec la teinte du plan 2 ; l'ambre chantier est né de cette contrainte métier, il n'est pas décoratif ; (2) le chrome sombre n'est pas un « dark mode par défaut » esthétique : il découle du fond blanc obligatoire du composite multiply (métaphore du négatoscope) ; (3) les cartes calques à liseré teinté remplacent des onglets génériques — la couleur du liseré EST l'information « qui est rouge, qui est bleu ».
- **Points faibles restants assumés (checklist heuristiques)** : (a) la loupe du viseur est le morceau le plus risqué techniquement (rendu temps réel d'une région agrandie) — si elle coûte trop cher en GSD, le repli est un zoom automatique ×2 du canvas pendant le calage, moins spectaculaire mais fonctionnel ; (b) le pan au drag-droit peut surprendre (convention CAO plutôt que bureautique) — doublé par le drag-milieu et documenté dans un tooltip ; (c) pas de flux clavier complet pour POSER les points de calage (pointage précis = souris) — le reste du flux (ouvrir, permuter, exporter, annuler) est intégralement au clavier ; assumé pour un outil de pointage.
- **Accessoire retiré (règle de Chanel)** : la minimap de navigation (redondante avec le double-clic « ajuster à la fenêtre » sur un document mono-feuille) et l'histogramme de différences envisagé un moment — le comparatif visuel EST la donnée, le chiffrer serait du faux-précis.

## 9. Journal des choix rejetés

| Proposition | Raison du rejet | Date |
|---|---|---|
| Accent bleu Fluent par défaut | Collision sémantique avec la teinte du plan 2 | 2026-08-13 |
| Chrome clair | Le composite à fond blanc se noie dans un chrome clair ; le sombre le met en scène (négatoscope) | 2026-08-13 |
| Minimap de navigation | Redondante (double-clic = fit) sur un document mono-feuille | 2026-08-13 |
| Histogramme / % de différence | Faux-précis : les franges d'AA rendraient le chiffre mensonger (cf. rapport LLM-Council, risque n°2) | 2026-08-13 |
| Onglets Plan 1 / Plan 2 dans le panneau | Masque un des deux plans ; les cartes empilées montrent les deux états en permanence | 2026-08-13 |
| Toolbar flottante sur le canvas | Occlusion du document ; le canvas reste vierge de tout chrome | 2026-08-13 |

## 10. Écarts consacrés / reportés (dev-council n°1, PERT-04 / PERT-06)

- **Consacré v1** : chargement par ligne de commande `DrawingComparator.exe base.pdf [revision.pdf]` (entrée depuis l'Explorateur). S'ajoute au mapping §4.
- **Reportés au cycle 2 (roadmap)** : bandeau « Cette page semble vide » ; progression déterminée + annulation de l'export ; motion §7 (assombrissement 150 ms, « clac » 200 ms du calage) ; thème light (tokens définis, non implémentés). Le plan reste la référence de ces intentions ; le produit v0.1.0 ne les promet pas.
