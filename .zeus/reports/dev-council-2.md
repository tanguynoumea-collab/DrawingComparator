# DEV-COUNCIL n°2 — Cycle 2 DrawingComparator (2026-08-13)

Protocole complet : 8 auditeurs (architecte, mainteneur, tests, fiabilité, sécurité, données,
packaging, pertinence — pas de RevitAPI → pas d'Autodesk) → sceptique anti-inflation →
cross-challenge (4 paires fixes) → arbitrage council (1 question, 3 juges aveugles).
Vérité-terrain : build 0 warning, format OK, 69/69 tests, Coverlet (Core 92,5 % L / App 44,8 % L),
`dotnet list package --vulnerable`, programmes témoins (System.Text.Json floats, troncature File.Create),
NU1004 prouvé sur restore win-x64.

## 1. Verdict et résumé exécutif

**1 Bloquant, 3 Majeurs, ~18 Mineurs, ~20 Infos. 1 finding invalidé (FIA2-10). 9 sévérités abaissées
par le sceptique — l'inflation était dans les sévérités, pas dans les constats.**

Le cœur du cycle (rendu par région, export tuilé, maths de calage/affine, compositeur) est jugé
**sain et bien testé** (Core à 92,5 % de couverture, budgets aux bonnes couches, aucun réflexe web).
Les problèmes se concentrent sur **la persistance des projets** — la feature phare du cycle est
celle qui porte le Bloquant et 2 des 3 Majeurs :

- **DON2-01 (Bloquant)** : `ProjectSerializer.Save` écrit EN PLACE (`File.Create`) — un crash ou une
  IOException (partage réseau, disque plein) pendant Ctrl+S **tronque le .dcproj existant à 0 octet**.
  Perte définitive du calage que le format existe pour préserver. Prouvé par programme témoin.
- **SEC2-01 (Majeur)** : un `.dcproj` reçu de tiers peut contenir un chemin UNC — `File.Exists` puis
  la lecture déclenchent SMB/NTLM sortant (fuite de hash) et chargent un PDF contrôlé par l'attaquant
  dans PDFium. Vecteur desktop classique « document piégé ».
- **TST2-01 (Majeur)** : le chemin d'ÉCRITURE de projet côté VM (`SaveProjectTo` : mapping état→DTO,
  TintsSwapped…) est à **0 % de couverture** — le test « round-trip » construit le projet à la main.
- **ARC2-01 (Majeur, arbitré 3/3)** : `MainViewModel` = 1133 lignes (+640/cycle), ~8 responsabilités.
  Arbitrage unanime : l'extraction d'`AlignmentSession` (la machine de calage, ~400 lignes cohésives)
  est un **prérequis du cycle 3** — périmètre strict (pas les 7 autres responsabilités), les 69 tests
  servent de harnais, la fenêtre à bas coût se referme dès la prochaine feature de calage.

**Le fix unique du cross-challenge** (Architecte↔Tests, Fiabilité↔Données, Sécurité↔Données) :
un `IProjectStore` injectable dont `Save` = sérialisation en mémoire → temp → `File.Replace`
(atomique), et `Load` = validation à la frontière (`float.IsFinite` sur les 6 termes, clamps
d'opacité, politique de chemins non locaux). Ce seul chantier ferme DON2-01, DON2-02, SEC2-02
(fusionné), une partie de SEC2-01, TST2-01 (testabilité) et ARC2-02 (asymétrie).

## 2. Plan de remédiation priorisé

### P1 — proposés « critiques » (correction avant publication du cycle)

| # | Findings | Chantier | Coût estimé |
|---|---|---|---|
| 1 | **DON2-01** + DON2-02 + SEC2-02(+DON2-06+FIA2-12) + ARC2-02 + TST2-01 | `IProjectStore` injectable : Save atomique (SerializeToUtf8Bytes → tmp → File.Replace), Load validé (IsFinite ×6, clamp opacité, catch élargi), round-trip VM complet testé | ~½ journée |
| 2 | **SEC2-01** | Politique chemins non locaux à l'ouverture d'un projet : repli « à côté du .dcproj » TENTÉ EN PREMIER, confirmation explicite avant tout chemin UNC/non qualifié | ~1-2 h |
| 3 | **FIA2-04** + FIA2-02 | Course du composite fantôme sur état vide (re-vérifier HasAnyDocument avant d'affecter CompositeBitmap) + catch terminal du pipeline tuiles (SEN-04 complet) | ~1 h |
| 4 | **PKG2-01** (abaissé Mineur mais bloquant de facto pour la publication) | Version 0.2.0 dans Directory.Build.props au commit taggé + garde tag↔version dans le job publish | ~15 min |
| 5 | **ARC2-01** (arbitré Majeur) | Extraction `AlignmentSession` hors de MainViewModel — prérequis cycle 3 ; peut être fait CE cycle (harnais de tests vert) ou en ouverture du cycle 3 : décision de triage | ~½ journée |

### P2 — Mineurs validés (roadmap/issues si non traités ce cycle)

- SEC2-03 arrondi DPI post-CapDpi (plancher 1 + floor) — SEC2-05 épingler les actions GitHub par SHA
  + permissions read au niveau workflow — PKG2-02 lock files win-x64 (`RuntimeIdentifiers`) —
  PKG2-04 rollForward/setup-dotnet — ARC2-03 RevealInExplorer via IUserDialogs — ARC2-04
  (+MAINT2-04+PERT2-02) source unique du pas de nudge — ARC2-05 (+FIA2-06) dictionnaires par identité —
  MAINT2-02 booléens de contrôle redondants — MAINT2-03 gardes de RecomputeFromPairs commentées —
  MAINT2-05 géométrie loupe — TST2-02 traduction erreurs PDFium réelles — TST2-03 (+DON2-03)
  RecentProjectsService — TST2-04 branches Undo contrôle/affine — TST2-05 ClearFile/concurrence —
  DON2-04 ADR versioning .dcproj — DON2-05 rendu page cible attendu à l'ouverture — SEC2-04
  (+PKG2-08, abaissé) attestation d'artefact GitHub.

### P3 — Infos (opportunistes)

FIA2-01 (re-check ct snackbar), FIA2-03 (annuler _detailCts au ClearFile), FIA2-05 (dialogues en mode
--screenshot), FIA2-07/08/09/11, MAINT2-06/07(+PERT2-01)/08(+PERT2-04)/09, TST2-06/07/08/09,
SEC2-06/07 (abaissés), SEC2-08/09/10, PKG2-03 (abaissé)/05/06/07, DON2-06/07, PERT2-03/05
(CHANGELOG au checkpoint publication)/06 (AUDIT_POINTS à rafraîchir avant dev-senior), ARC2-06/07/08.

### Écartés (annexe)

- **FIA2-10** (invalidé) : l'avalage inconditionnel des flèches est documenté comme délibéré dans le
  code (bloquer la navigation de focus WPF) ; aucun impact utilisateur démontré.

## 3. Détail

Les sorties complètes des 8 auditeurs, du sceptique (verdicts par ID, fusions, preuve témoin
System.Text.Json) et de l'arbitrage (3 juges, unanime MAJEUR sur ARC2-01) sont consignées dans
l'historique de session du 2026-08-13. Fusions actées : SEC2-02←{DON2-02, DON2-06, FIA2-12} ;
ARC2-04←{MAINT2-04, PERT2-02} ; ARC2-05←{FIA2-06} ; MAINT2-07←{PERT2-01} ; MAINT2-08←{PERT2-04} ;
TST2-03←{DON2-03} ; SEC2-04←{PKG2-08} ; ARC2-01←{MAINT2-01}.

## 4. Non vérifié faute d'outil (agrégé)

Exécution CI GitHub réelle (1er push la validera) ; comportement Dependabot sur .slnx ; profilage
mémoire natif (churn SkiaSharp, pic export ~800 Mo) ; corpus de PDF corrompus/protégés réels ;
runtime thème light et multi-DPI à l'écran ; Security Code Scan (paquet absent) ; CVE des natives
pdfium/libSkiaSharp hors advisories NuGet (contrôle = Dependabot).
