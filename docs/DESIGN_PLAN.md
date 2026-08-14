# DESIGN_PLAN — DrawingComparator

> Statut v1.0 : ☑ Validé par l'utilisateur (cycle 1, 2026-08-13)
> Statut v1.1 (delta cycle 2, § 11) : ☑ Brouillon ☑ Auto-critiqué (Passe B) ☐ **VALIDÉ PAR L'UTILISATEUR** (obligatoire avant tout XAML)
> Version : 1.1 — 2026-08-13

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
- **Reportés au cycle 2 (roadmap)** : bandeau « Cette page semble vide » ; progression déterminée + annulation de l'export ; motion §7 (assombrissement 150 ms, « clac » 200 ms du calage) ; thème light (tokens définis, non implémentés). Le plan reste la référence de ces intentions ; le produit v0.1.0 ne les promet pas. → **Tous repris dans le delta v1.1 (§ 11).**

---

## 11. Delta cycle 2 (v1.1) — mode ÉVOLUTION

> Règle directrice : cohérence avant nouveauté. Aucun nouvel écran, aucune nouvelle couleur, aucun
> deuxième style. Les 8 nouveautés visibles s'insèrent dans le langage existant : l'ambre reste la seule
> couleur d'action, le rouge/bleu reste la propriété exclusive des plans, le canvas reste vierge de chrome.

### 11.1 Projets — barre d'outils et état vide enrichi

La « vérité » d'une session tient en une poignée de valeurs (2 PDF, pages, matrice, opacités, teintes) :
le projet `.dcproj` la capture. Deux points d'entrée, aucun nouvel écran :

- **Barre d'outils**, groupe gauche (avant « Caler les plans ») : `[📁 Ouvrir]` `[💾 Enregistrer]`
  en style discret (outline). Raccourcis : Ctrl+O = ouvrir un projet OU un PDF (sélecteur natif filtré
  `.dcproj;*.pdf`), **Ctrl+S = enregistrer le projet** (convention Windows), Ctrl+Maj+S = enregistrer sous.
  « Enregistrer » est désactivé tant que les 2 plans ne sont pas chargés (tooltip « Chargez les deux plans »).
- **État vide** : les deux zones de dépôt restent le 1er niveau de lecture ; en dessous, une section
  **« Reprendre »** liste les projets récents (max 8, MRU %APPDATA%) :

```
│         ┌───────────────────┐      ┌───────────────────┐              │
│         │  ▌ PLAN DE BASE   │      │  ▌ PLAN RÉVISÉ    │              │
│         │  Déposer un PDF   │      │  Déposer un PDF   │              │
│         │  ou [Parcourir…]  │      │  ou [Parcourir…]  │              │
│         └───────────────────┘      └───────────────────┘              │
│     Les traits communs ressortiront sombres, les différences          │
│     resteront rouges ou bleues.                                       │
│                                                                       │
│     REPRENDRE                                                         │
│     ▌▌ Façade-nord-rev3        base.pdf ↔ rev3.pdf     hier 16:42     │
│     ▌▌ Étage-2-lot-plomberie   e2.pdf ↔ e2-B.pdf       11 août        │
│     (double liseré rouge|bleu = miniature d'identité du projet)       │
```

  Hiérarchie préservée : 1er = zones de dépôt · 2e = phrase du principe · 3e = liste Reprendre
  (titres en Caption TextSecondary, entrées en Corps, dates en Cascadia Mono).
- **Chemin introuvable à l'ouverture** : ContentDialog conforme au ton du plan — « base.pdf n'est plus
  à l'emplacement enregistré (réseau déconnecté ?). » + actions [Rechercher à côté du projet] [Parcourir…]
  [Retirer des récents]. Jamais d'échec silencieux.

### 11.2 Calage fin au clavier (mode ajustement)

S'ajoute à l'élément signature sans le concurrencer. Une fois un calage posé (et à tout moment hors
pose de points), le **panneau Calage** devient l'instrument de retouche :

```
│ Calage                    │
│ ✓ calé   résiduel 0,4 mm  │  ← résiduel : § 11.4
│ é=0.998  θ=+0.4°          │
│ AJUSTER (flèches)         │
│ pas  [0,01] [0,1] [1] mm  │  ← segmented 3 pas, valeur active en ambre (UAT cycle 2 : plus fin)
│ Maj = 0,01   Ctrl = 1     │  ← Caption
│ [Réinitialiser]           │
```

- Flèches = translation du plan RÉVISÉ en mm papier de la BASE ; le pas actif est **visible** (pas
  seulement des modificateurs cachés) ; Maj/Ctrl basculent temporairement d'un cran (aide en Caption).
- Le canvas doit avoir le focus (clic ou Tab) ; anneau de focus visible sur le canvas (conforme § clavier).
- Chaque nudge rafraîchit é/θ/résiduel en Cascadia Mono — la retouche se mesure, elle ne se devine pas.

### 11.3 Scrim de calage 60 % + motion (§ 6/§ 7 enfin implémentés)

Décision compositeur actée par le LLM-Council : la Strength multiply ne peut qu'éclaircir → le scrim
est un **voile XAML plein canvas** (Background 60 % d'opacité) posé PAR-DESSUS le composite, le calque
actif de l'étape courante redessiné à pleine lumière au-dessus. Entrée 150 ms ease-out, « clac » de
200 ms au commit du 4e clic (retour pleine lumière pendant que la transformation s'applique). Aucun
autre mouvement ajouté.

### 11.4 3e point de contrôle + mode affine (opt-in explicite)

Extension du bandeau de calage, après le 4e clic :

```
│  ● 1  ● 2  ● 3  ● 4   ✓ Calé — résiduel : posez un point de contrôle  │
│  [+ Point de contrôle (facultatif)]                    [Terminer]     │
```

- Le 5e/6e clic (couple de contrôle) n'altère PAS la transformation : il affiche **« résiduel 0,4 mm »**
  dans le bandeau et le panneau Calage. C'est la réponse à « mon calage est-il bon ? ».
- **Mode affine** : dans le panneau Calage, segmented `Rigide (2 pts) | Affine (3 pts)` — Affine
  désactivé tant qu'un 3e couple n'existe pas ; jamais de bascule automatique (SEN-01). En mode affine,
  le panneau affiche é_x, é_y et cisaillement ; si |é_x/é_y − 1| > 0,2 %, ligne d'avertissement en
  Caption avec icône ⚠ (TextSecondary — pas Danger : c'est une information, pas une erreur) :
  « Échelles X/Y différentes : l'affine déforme le plan révisé. »
- Points de contrôle refusés si colinéaires : message inline sous le bandeau (même patron que « points
  trop proches »).

### 11.5 Export : progression déterminée, annulation, snackbar

- Dialogue d'export : ProgressBar **déterminée** (1 tick par bande rendue) + bouton [Annuler] actif
  pendant tout le rendu. La grille du dialogue ne bouge pas (pas de reflow pendant la progression).
- Succès → **Snackbar** en bas du canvas, Surface + liseré Accent, 150 ms : « Exporté → C:\…\diff.png
  [Ouvrir le dossier] », auto-fermeture 5 s. Remplace la mention en barre de statut.
- Échec → Snackbar Danger (patron § 5 inchangé).

### 11.6 Binarisation par calque (PDF scannés)

Dans chaque **carte calque**, sous le slider d'opacité : ToggleButton icône ◐ + libellé court
« Binariser » (tooltip : « Nettoie les scans : fond gris → blanc, traits → teinte pleine. Sans effet
sur les PDF vectoriels nets. ») Toggle indépendant par plan, état sauvé dans le `.dcproj`. Pas de
seuil réglable en v1 (valeur par défaut intelligente ; divulgation progressive — un réglage viendra
si l'usage le réclame).

### 11.7 Bandeau « page semble vide »

Patron § 5 inchangé : bandeau fin en haut du canvas, Surface, texte Caption « Cette page semble vide —
vérifier le n° de page », fermeture ✕, disparaît au changement de page. Jamais de dialogue pour ça.

### 11.8 Latence du rendu de région (FIA-07)

Le fond pleine page reste TOUJOURS affiché (jamais de flash blanc) ; pendant le rendu de la tuile
nette : point de progression discret en **barre de statut** (« ⟳ netteté… » en Caption, Cascadia Mono)
— pas de spinner sur le canvas, le document reste roi.

### 11.9 Thème light

Détection système **au démarrage uniquement** (SEN-13). Colonne light des tokens § 2 appliquée au
chrome seul — le canvas est déjà light par construction (multiply sur fond blanc). Aucun switch runtime.

### 11.10 Mapping des nouveautés (aucune orpheline)

| Fonctionnalité cycle 2 | Écran | Emplacement / contrôle |
|---|---|---|
| Enregistrer / ouvrir un projet | Principal | Barre d'outils groupe gauche + Ctrl+S / Ctrl+O / Ctrl+Maj+S |
| Projets récents | Principal (état vide) | Section « Reprendre », 8 entrées MRU, double liseré identitaire |
| Chemin PDF introuvable | Dialogue | ContentDialog « Rechercher à côté / Parcourir / Retirer » |
| Calage fin clavier + pas réglable | Principal (calé) | Panneau Calage, bloc « AJUSTER », segmented 0,1/0,5/2 mm |
| Scrim 60 % + motion calage | Mode calage | Voile XAML plein canvas + « clac » 200 ms |
| 3e point + erreur résiduelle | Mode calage | Bandeau étape 5 facultative + « résiduel x mm » |
| Mode affine opt-in + anisotropie | Principal | Panneau Calage, segmented Rigide/Affine + é_x é_y cisaillement |
| Binarisation par calque | Principal | ToggleButton ◐ dans chaque carte calque |
| Progression + annulation export | Dialogue export | ProgressBar déterminée + [Annuler] |
| Snackbar d'export | Principal | Snackbar bas de canvas, liseré Accent / Danger |
| Bandeau page vide | Principal | Bandeau fin haut de canvas |
| Indicateur netteté (FIA-07) | Principal | Barre de statut, « ⟳ netteté… » |
| Thème light | Global | Swap de dictionnaire de tokens au démarrage |

### 11.11 Auto-critique du delta (Passe B)

- **« Plan générique ? »** Non sur trois points, oui corrigé sur un : (1) la liste « Reprendre » avec
  double liseré rouge|bleu est une signature propre à CETTE app (l'identité d'un projet EST sa paire de
  plans) ; (2) le pas de nudge affiché en segmented plutôt qu'en tooltip caché découle de la philosophie
  « la précision se met en scène » du viseur ; (3) l'avertissement d'anisotropie en langage métier
  (« déforme le plan révisé ») plutôt qu'en jargon (« transformation non conforme »). Corrigé : ma
  première version mettait « Enregistrer le projet » en bouton accentué — faute de hiérarchie, l'action
  primaire de l'écran reste « Caler les plans » ; rétrogradé en outline.
- **Charge cognitive (7±2)** : la carte calque porte désormais liseré + nom + page + opacité +
  binarisation = 5 éléments, OK. La barre d'outils passe à 5 actions en 3 groupes (projet | calage |
  export), OK. Le panneau Calage est le plus chargé (état, é/θ, résiduel, AJUSTER, pas, Rigide/Affine,
  Réinitialiser) : accepté car c'est le poste de pilotage d'un outil de mesure, densité assumée § 1 —
  mais le bloc AJUSTER n'apparaît QUE lorsqu'un calage existe (divulgation progressive).
- **Clavier** : le nudge exige le focus canvas — risque de « flèches mortes » si le focus est ailleurs.
  Mitigation : clic sur le canvas = focus (déjà le cas), anneau de focus visible, et le panneau Calage
  affiche « (flèches) » en rappel. Assumé.
- **Points faibles restants** : (a) la section Reprendre allonge l'état vide — sur petite fenêtre elle
  passe sous la ligne de flottaison, accepté (les zones de dépôt priment) ; (b) « Binariser » sans
  réglage de seuil peut décevoir sur un scan très sombre — assumé v1, le toggle est réversible ;
  (c) l'étape 5 facultative complexifie légèrement le bandeau — mitigée par [Terminer] toujours visible.
- **Accessoire retiré (Chanel)** : la vignette-aperçu du canvas dans les entrées Reprendre (coût de
  génération/stockage pour un gain faible — le double liseré + noms de fichiers identifient déjà) ;
  le badge « NOUVEAU » sur les fonctions du cycle 2 (l'app n'a qu'un utilisateur, il sait ce qui est neuf).

### 11.11 bis — Retouches UAT cycle 2 (validées en usage réel)

- **Onglets Accueil / Projet** dans la barre d'outils (soulignement ambre = actif) : l'Accueil
  (dépôt + Reprendre) reste accessible projet ouvert — ouvrir un récent bascule vers Projet ;
  Projet désactivé tant qu'aucun document ; retour Accueil automatique quand tout est fermé.
- **Pas d'ajustement** : 3 pas, plus fins — 0,01 / 0,1 / 1 mm (défaut 0,1 ; Maj = 0,01, Ctrl = 1).
- **Export** : format PNG **ou PDF** (une page à l'emprise de la feuille, rasters embarqués,
  tuilé au fil de l'eau — 600 DPI possible même sur A0) + résolution 600 DPI ajoutée
  (« impression fine ») ; la « vue courante » reste réservée au PNG. Bouton barre d'outils
  renommé « Exporter ».
- **Calque des différences** (section AFFICHAGE du panneau) : 4 modes — Superposition /
  Différences seules / Différences sur BASE / Différences sur RÉVISÉ. Les traits communs
  (sombres sous multiply) s'effacent par filtre SKSL au compositeur ; en mode « sur … »,
  le plan choisi sert de fond gris de contexte. S'applique aussi aux exports.
- **Logo** : icône générée depuis l'identité (fond négatoscope #16181D, carrés rouge/bleu
  superposés, intersection multiply sombre, point ambre) — exe (app.ico 16/32/48/256) +
  icône de fenêtre.

### 11.12 Journal des choix rejetés (delta)

| Proposition | Raison du rejet | Date |
|---|---|---|
| « Enregistrer le projet » en bouton accentué | Concurrence l'action primaire « Caler les plans » ; l'ambre reste unique | 2026-08-13 |
| Vignette-aperçu dans les projets récents | Coût élevé, gain faible ; le double liseré + noms suffisent | 2026-08-13 |
| Pas de nudge uniquement via Maj/Ctrl (invisible) | Découvrabilité nulle ; le pas actif doit être visible et cliquable | 2026-08-13 |
| Avertissement anisotropie en Danger rouge | Le rouge appartient au plan 1 / aux erreurs ; l'anisotropie est une info, pas une faute | 2026-08-13 |
| Slider de seuil de binarisation | Divulgation progressive : v1 = toggle, réglage seulement si l'usage le réclame | 2026-08-13 |
| Sauvegarde auto du .dcproj à côté des PDF | Anti-pattern sur partages réseau d'équipe (LLM-Council) ; emplacement choisi + MRU %APPDATA% | 2026-08-13 |
