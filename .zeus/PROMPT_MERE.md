# PROMPT MÈRE — DrawingComparator
# À coller dans Claude Code au démarrage de session

---

## CONTEXTE PROJET

**Projet :** DrawingComparator
**Objectif :** Superposer deux plans PDF (base teintée rouge, révision teintée bleue) pour visualiser instantanément les différences : les traits identiques fusionnent en couleur sombre, les traits propres à un plan restent rouges ou bleus.
**Stack :** C# / WPF / .NET 8 — MVVM (CommunityToolkit.Mvvm), rendu PDF via PDFtoImage (PDFium)
**Déploiement :** Exe local self-contained (dotnet publish, un exe à copier)
**Conventions :** MVVM strict (Models/Views/ViewModels/Services), injection de dépendances, pas de code-behind métier, ressources XAML pour tous les tokens visuels

## INTÉGRATIONS EXTERNES

- **PDFtoImage / PDFium** (NuGet) : rasterisation des pages PDF — disponible immédiatement
- Aucune API réseau, aucune base de données : app 100 % locale

## CONTRAINTES ET RÈGLES

- Le plan 1 (base) est FIXE ; seul le plan 2 subit la transformation
- Transformation du plan 2 = similitude 2 points : translation (point d'ancrage) + échelle + rotation (second point de correspondance)
- Teinte rouge/bleu appliquée par recoloration des pixels sombres (les traits), fond blanc rendu transparent ; blending multiplicatif pour que l'identique ressorte foncé
- Zoom/pan fluide obligatoire (RenderTransform, pas de re-rasterisation à chaque frame)
- Rasterisation à DPI élevé (200–300) pour garder les traits nets au zoom
- Gestion d'erreurs : PDF corrompu, protégé par mot de passe, page inexistante → message utilisateur clair, jamais de crash

## FICHIER CLAUDE.md À CRÉER

Créer un fichier `CLAUDE.md` à la racine du projet avec ce contenu :

```markdown
# CLAUDE.md — DrawingComparator

## Contexte
Comparateur visuel de plans PDF : superposition rouge/bleu, alignement 2 points (ancrage + échelle/rotation), différences visibles immédiatement.

## Stack
C# / WPF / .NET 8, MVVM avec CommunityToolkit.Mvvm, PDFtoImage (PDFium) pour le rendu PDF.

## Conventions
- MVVM strict : Views (XAML) / ViewModels / Models / Services
- Services injectés via interfaces (IPdfRenderService, …)
- Tokens visuels en ressources XAML, jamais de valeurs en dur
- Transformations gérées en matrices (System.Windows.Media.Matrix)

## Statut GSD
[Sera rempli après /gsd export en fin de session]
```

---

## INITIALISATION GSD

Exécuter dans l'ordre :

```
/gsd init
```

---

## TÂCHES DU PROJET — Commandes /gsd add

### Architecture & fondations
```
/gsd add "Créer la solution VS DrawingComparator avec structure MVVM (Models/Views/ViewModels/Services)" --priority high --tag arch
/gsd add "Ajouter les packages NuGet : CommunityToolkit.Mvvm, PDFtoImage" --priority high --tag arch
/gsd add "Implémenter IPdfRenderService : rasterisation d'une page PDF en BitmapSource à DPI paramétrable" --priority high --tag arch
/gsd add "Implémenter le modèle PlanLayer : bitmap source, teinte, opacité, matrice de transformation" --priority high --tag arch
```

### Interface utilisateur
```
/gsd add "Créer MainWindow.xaml : canvas central de superposition, barre d'outils, panneau latéral de réglages" --priority medium --tag ui
/gsd add "Implémenter le chargement des deux PDF via boutons + sélecteur de page par plan" --priority medium --tag ui
/gsd add "Implémenter le zoom molette et le pan par drag sur le canvas de superposition" --priority medium --tag ui
/gsd add "Ajouter les sliders d'opacité par plan et le bouton de permutation rouge/bleu" --priority medium --tag ui
```

### Fonctionnalités métier
```
/gsd add "Implémenter la recoloration des bitmaps : traits sombres vers teinte, fond blanc vers transparent" --priority high --tag feat
/gsd add "Implémenter le blending de superposition rendant les zones identiques en couleur combinée" --priority high --tag feat
/gsd add "Implémenter l'outil d'ancrage : clic point de référence plan 2 puis point cible plan 1 (translation)" --priority high --tag feat
/gsd add "Implémenter l'outil échelle/rotation : second point plan 2 vers point correspondant plan 1, similitude ancrée" --priority high --tag feat
/gsd add "Implémenter l'export PNG de la vue superposée courante" --priority medium --tag feat
```

### Corrections & robustesse
```
/gsd add "Gérer les PDF corrompus, protégés ou pages inexistantes avec messages utilisateur explicites" --priority low --tag fix
/gsd add "Valider les cas limites d'alignement : points confondus, échelle extrême, annulation d'un placement" --priority low --tag fix
```

### Déploiement
```
/gsd add "Configurer dotnet publish self-contained single-file et vérifier l'exe sur machine propre" --priority low --tag deploy
```

---

## VÉRIFICATION FINALE

Après avoir saisi toutes les commandes :

```
/gsd status
```

Vérifier que :
- [ ] Toutes les tâches sont en statut `todo`
- [ ] Les priorités `high` couvrent les fondations et le cœur métier
- [ ] L'ordre respecte les dépendances (arch → ui → feat → fix → deploy)
- [ ] Aucune tâche ne contient "et" fusionnant deux livrables distincts

---

## INSTRUCTION DE DÉMARRAGE

Une fois GSD initialisé, me dire : **"Prêt. Commence par task-001."**
Je ferai `/gsd start task-001` et tu coderas uniquement cette tâche.
Règle : maximum 1 tâche `in-progress` à la fois.
