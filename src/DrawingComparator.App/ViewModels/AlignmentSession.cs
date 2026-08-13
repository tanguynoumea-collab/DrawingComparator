using CommunityToolkit.Mvvm.ComponentModel;

using DrawingComparator.Core;

using SkiaSharp;

namespace DrawingComparator.App.ViewModels;

/// <summary>Étapes de l'outil de calage (DESIGN_PLAN §3 « mode calage », §11.4 point de contrôle).</summary>
public enum AlignmentStep
{
    Inactive,
    Point1OnRevision,
    Point1OnBase,
    Point2OnRevision,
    Point2OnBase,

    /// <summary>Calage appliqué, bandeau encore ouvert : point de contrôle facultatif ou Terminer.</summary>
    Aligned,
    ControlOnRevision,
    ControlOnBase,
}

/// <summary>
/// La machine à états du calage, extraite de MainViewModel (dev-council n°2, ARC2-01) :
/// états, couples de points, matrices de session, mode affine, résiduel et nudge vivent ICI —
/// aucune autre responsabilité du shell ne peut muter ses invariants autrement que par ses
/// méthodes. Les noms de propriétés sont ceux que la façade MainViewModel re-expose : le
/// PropertyChanged est relayé tel quel vers le XAML.
/// </summary>
public sealed partial class AlignmentSession : ObservableObject
{
    private readonly Action _requestRecompose;

    // Points de calage capturés, dans le repère PDF de leur plan respectif.
    private SKPoint _p1, _q1, _p2, _q2, _p3, _q3;
    private bool _hasControlPoint;
    private SKMatrix _beforeSession = SKMatrix.Identity;
    private SKMatrix _afterAnchor = SKMatrix.Identity;

    public AlignmentSession(Action requestRecompose)
    {
        _requestRecompose = requestRecompose;
    }

    /// <summary>Matrice de calage : points PDF du plan révisé → points PDF du plan de base. 3×2 générale.</summary>
    public SKMatrix Matrix { get; private set; } = SKMatrix.Identity;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAligning), nameof(IsPosingPoint), nameof(IsAlignmentCommitted),
        nameof(StepInstruction), nameof(Step1Glyph), nameof(Step2Glyph), nameof(Step3Glyph), nameof(Step4Glyph))]
    private AlignmentStep _alignmentStep = AlignmentStep.Inactive;

    public bool IsAligning => AlignmentStep != AlignmentStep.Inactive;

    /// <summary>Un clic est attendu sur le canvas (scrim 60 % actif, item 4 — pas en état « Aligned »).</summary>
    public bool IsPosingPoint => AlignmentStep is AlignmentStep.Point1OnRevision or AlignmentStep.Point1OnBase
        or AlignmentStep.Point2OnRevision or AlignmentStep.Point2OnBase
        or AlignmentStep.ControlOnRevision or AlignmentStep.ControlOnBase;

    /// <summary>Le calage est appliqué, le bandeau propose le point de contrôle et Terminer (§11.4).</summary>
    public bool IsAlignmentCommitted => AlignmentStep == AlignmentStep.Aligned;

    public string Step1Glyph => StepGlyph(1, AlignmentStep.Point1OnRevision, "Point du RÉVISÉ");
    public string Step2Glyph => StepGlyph(2, AlignmentStep.Point1OnBase, "Même point sur la BASE");
    public string Step3Glyph => StepGlyph(3, AlignmentStep.Point2OnRevision, "Second point du RÉVISÉ");
    public string Step4Glyph => StepGlyph(4, AlignmentStep.Point2OnBase, "Même point sur la BASE");

    private string StepGlyph(int number, AlignmentStep stepOfChip, string label)
    {
        int currentIndex = (int)AlignmentStep;
        int chipIndex = (int)stepOfChip;
        string glyph = currentIndex > chipIndex ? "●" : currentIndex == chipIndex ? "◉" : "○";
        return $"{glyph} {number} {label}";
    }

    public string StepInstruction => AlignmentStep switch
    {
        AlignmentStep.Point1OnRevision => "Cliquez un point de référence sur le plan RÉVISÉ (ex. un angle de mur)",
        AlignmentStep.Point1OnBase => "Cliquez le même point sur le plan de BASE — il servira d'ancrage",
        AlignmentStep.Point2OnRevision => "Cliquez un second point sur le plan RÉVISÉ, loin du premier (ex. le bout du mur)",
        AlignmentStep.Point2OnBase => "Cliquez le même point sur le plan de BASE — il fixe l'échelle et la rotation",
        AlignmentStep.Aligned => "Calage appliqué — posez un point de contrôle pour mesurer l'erreur, ou terminez",
        AlignmentStep.ControlOnRevision => "Cliquez un point de VÉRIFICATION sur le plan RÉVISÉ, loin des deux premiers",
        AlignmentStep.ControlOnBase => "Cliquez le même point sur le plan de BASE — l'écart mesuré s'affichera",
        _ => string.Empty,
    };

    [ObservableProperty]
    private string? _alignmentInlineError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAlignment))]
    private string? _alignmentSummary;

    public bool HasAlignment => AlignmentSummary is not null;

    /// <summary>Erreur résiduelle du point de contrôle, en mm papier de la base (null tant qu'aucun contrôle).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResidualText))]
    private double? _residualMm;

    public string? ResidualText => ResidualMm is { } r ? $"résiduel {r:0.00} mm" : null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUseAffine))]
    private bool _hasControlPointForDisplay;

    public bool CanUseAffine => HasControlPointForDisplay;

    /// <summary>Mode affine 3 points : opt-in explicite, jamais de bascule silencieuse (SEN-01).</summary>
    [ObservableProperty]
    private bool _isAffineMode;

    partial void OnIsAffineModeChanged(bool value) => RecomputeFromPairs();

    /// <summary>Avertissement d'anisotropie (mode affine) — une information métier, pas une erreur.</summary>
    [ObservableProperty]
    private string? _anisotropyWarning;

    /// <summary>Pas de translation des flèches, en millimètres papier de la BASE (0,1 / 0,5 / 2).</summary>
    [ObservableProperty]
    private double _nudgeStepMm = 0.5;

    public bool CanNudge => HasAlignment && !IsPosingPoint;

    // ── Transitions ───────────────────────────────────────────────────────────
    // Graphe : Inactive → P1Rev → P1Base(ancrage) → P2Rev → P2Base(similitude) → Aligned
    //          Aligned ⇄ ControlOnRevision → ControlOnBase → Aligned (résiduel posé)
    //          Aligned —Terminer/Échap→ Inactive (conserve) ; états de pose —Échap→ Inactive (restaure).

    public void Start()
    {
        _beforeSession = Matrix;
        _hasControlPoint = false;
        HasControlPointForDisplay = false;
        ResidualMm = null;
        IsAffineMode = false;
        AnisotropyWarning = null;
        AlignmentInlineError = null;
        AlignmentStep = AlignmentStep.Point1OnRevision;
        _requestRecompose();
    }

    public void Cancel()
    {
        if (!IsAligning)
            return;
        if (AlignmentStep == AlignmentStep.Aligned)
        {
            // Le calage est déjà appliqué : Échap referme simplement le bandeau.
            Finish();
            return;
        }
        if (AlignmentStep is AlignmentStep.ControlOnRevision or AlignmentStep.ControlOnBase)
        {
            // Le calage committé par les 4 clics n'est PAS remis en cause : Échap referme
            // seulement la pose du point de contrôle (dev-senior SEN2-03).
            AlignmentStep = AlignmentStep.Aligned;
            AlignmentInlineError = null;
            return;
        }
        Matrix = _beforeSession;
        AlignmentStep = AlignmentStep.Inactive;
        AlignmentInlineError = null;
        _hasControlPoint = false;
        HasControlPointForDisplay = false;
        ResidualMm = null;
        UpdateSummary();
        _requestRecompose();
    }

    /// <summary>Ferme le bandeau de calage en conservant le résultat (bouton Terminer, §11.4).</summary>
    public void Finish()
    {
        if (AlignmentStep != AlignmentStep.Aligned)
            return;
        AlignmentStep = AlignmentStep.Inactive;
        AlignmentInlineError = null;
        _requestRecompose();
    }

    /// <summary>Ouvre la pose du couple de contrôle facultatif (étape 5, §11.4).</summary>
    public void AddControlPoint()
    {
        if (AlignmentStep != AlignmentStep.Aligned)
            return;
        AlignmentInlineError = null;
        AlignmentStep = AlignmentStep.ControlOnRevision;
    }

    /// <summary>Retour arrière : re-poser le point précédent.</summary>
    public void Undo()
    {
        AlignmentInlineError = null;
        switch (AlignmentStep)
        {
            case AlignmentStep.Point1OnBase:
                AlignmentStep = AlignmentStep.Point1OnRevision;
                break;
            case AlignmentStep.Point2OnRevision:
                // L'ancrage (translation) avait été appliqué : on le retire.
                Matrix = _beforeSession;
                AlignmentStep = AlignmentStep.Point1OnBase;
                _requestRecompose();
                break;
            case AlignmentStep.Point2OnBase:
                AlignmentStep = AlignmentStep.Point2OnRevision;
                break;
            case AlignmentStep.Aligned:
                if (_hasControlPoint)
                {
                    // Retirer le contrôle (et le mode affine qui s'appuyait dessus).
                    _hasControlPoint = false;
                    HasControlPointForDisplay = false;
                    ResidualMm = null;
                    if (IsAffineMode)
                        IsAffineMode = false; // recompose en rigide via OnIsAffineModeChanged
                    break;
                }
                // Re-poser le second couple : revenir à l'état ancré.
                Matrix = _afterAnchor;
                AlignmentStep = AlignmentStep.Point2OnBase;
                UpdateSummary();
                _requestRecompose();
                break;
            case AlignmentStep.ControlOnRevision:
                AlignmentStep = AlignmentStep.Aligned;
                break;
            case AlignmentStep.ControlOnBase:
                AlignmentStep = AlignmentStep.ControlOnRevision;
                break;
        }
    }

    public void Reset()
    {
        Matrix = SKMatrix.Identity;
        _hasControlPoint = false;
        HasControlPointForDisplay = false;
        ResidualMm = null;
        IsAffineMode = false;
        AnisotropyWarning = null;
        UpdateSummary();
        _requestRecompose();
    }

    /// <summary>Restaure la matrice d'un projet ouvert : la session repart sans couples ni contrôle.</summary>
    public void ApplyLoadedMatrix(SKMatrix matrix)
    {
        Matrix = matrix;
        _hasControlPoint = false;
        HasControlPointForDisplay = false;
        ResidualMm = null;
        IsAffineMode = false;
        UpdateSummary();
    }

    /// <summary>
    /// Translate le plan RÉVISÉ de (dx, dy) pas dans le repère de la BASE — composition à gauche
    /// par une translation pure : l'échelle et la rotation du calage sont intactes.
    /// </summary>
    public void Nudge(int dxSteps, int dySteps, double? overrideStepMm = null)
    {
        if (!CanNudge)
            return;
        double mm = overrideStepMm ?? NudgeStepMm;
        float pt = (float)(mm * 72.0 / 25.4);
        Matrix = SKMatrix.CreateTranslation(dxSteps * pt, dySteps * pt).PreConcat(Matrix);
        UpdateResidual();
        UpdateSummary();
        _requestRecompose();
    }

    /// <summary>Traite un clic gauche (déjà converti en points PDF de la BASE). Retourne false hors mode calage.</summary>
    public bool HandleClick(SKPoint basePoint)
    {
        if (!IsAligning)
            return false;

        AlignmentInlineError = null;

        switch (AlignmentStep)
        {
            case AlignmentStep.Point1OnRevision:
                _p1 = MapBaseToRevision(basePoint);
                AlignmentStep = AlignmentStep.Point1OnBase;
                break;

            case AlignmentStep.Point1OnBase:
                _q1 = basePoint;
                // Ancrage immédiat : le plan révisé vient se poser sur le point cible,
                // ce qui facilite le choix du second couple de points.
                Matrix = AlignmentMath.ComputeAnchorTranslation(
                    Matrix.MapPoint(_p1), _q1).PreConcat(Matrix);
                _afterAnchor = Matrix;
                AlignmentStep = AlignmentStep.Point2OnRevision;
                _requestRecompose();
                break;

            case AlignmentStep.Point2OnRevision:
                _p2 = MapBaseToRevision(basePoint);
                AlignmentStep = AlignmentStep.Point2OnBase;
                break;

            case AlignmentStep.Point2OnBase:
                try
                {
                    _q2 = basePoint;
                    Matrix = AlignmentMath.ComputeSimilarity(_p1, _q1, _p2, _q2);
                    AlignmentStep = AlignmentStep.Aligned;
                    UpdateSummary();
                    _requestRecompose();
                }
                catch (DegenerateAlignmentException ex)
                {
                    AlignmentInlineError = $"{ex.Message} Choisissez un point plus éloigné.";
                }
                break;

            case AlignmentStep.ControlOnRevision:
                _p3 = MapBaseToRevision(basePoint);
                AlignmentStep = AlignmentStep.ControlOnBase;
                break;

            case AlignmentStep.ControlOnBase:
                _q3 = basePoint;
                _hasControlPoint = true;
                HasControlPointForDisplay = true;
                AlignmentStep = AlignmentStep.Aligned;
                if (IsAffineMode)
                    RecomputeFromPairs();
                UpdateResidual();
                break;

            case AlignmentStep.Aligned:
                break; // aucun point attendu : le bandeau attend Terminer ou + Point de contrôle
        }
        return true;
    }

    /// <summary>Point du repère base → repère propre du plan révisé (via l'inverse du calage courant).</summary>
    private SKPoint MapBaseToRevision(SKPoint basePoint)
        => Matrix.TryInvert(out var inv) ? inv.MapPoint(basePoint) : basePoint;

    /// <summary>
    /// Recalcule la matrice depuis les couples posés, selon le mode (SEN-01 : le choix
    /// rigide/affine est TOUJOURS celui de l'utilisateur, jamais un repli).
    /// Invariant des gardes : les couples 1-2 ne sont complets qu'à partir d'Aligned ;
    /// Inactive = session terminée dont les couples restent valides (MAINT2-03).
    /// </summary>
    private void RecomputeFromPairs()
    {
        if (!HasAlignment && AlignmentStep == AlignmentStep.Inactive)
            return;
        try
        {
            if (IsAffineMode)
            {
                if (!_hasControlPoint)
                    return; // le segmented est désactivé sans 3e couple ; garde-fou
                Matrix = AlignmentMath.ComputeAffine(_p1, _q1, _p2, _q2, _p3, _q3);
            }
            else if (_hasControlPoint || AlignmentStep is AlignmentStep.Aligned or AlignmentStep.Inactive)
            {
                Matrix = AlignmentMath.ComputeSimilarity(_p1, _q1, _p2, _q2);
            }
            UpdateResidual();
            UpdateSummary();
            _requestRecompose();
        }
        catch (DegenerateAlignmentException ex)
        {
            AlignmentInlineError = $"{ex.Message}";
            if (IsAffineMode)
                IsAffineMode = false;
        }
    }

    private void UpdateResidual()
        => ResidualMm = _hasControlPoint ? AlignmentMath.ResidualMm(Matrix, _p3, _q3) : null;

    private void UpdateSummary()
    {
        if (Matrix == SKMatrix.Identity)
        {
            AlignmentSummary = null;
            AnisotropyWarning = null;
            return;
        }

        if (IsAffineMode)
        {
            var (sx, sy, rotation, shear) = AlignmentMath.DecomposeAffine(Matrix);
            AlignmentSummary =
                $"é_x ={sx,7:0.0000}  é_y ={sy,7:0.0000}\nrotation ={rotation,7:+0.00;-0.00}°  cis. ={shear,7:0.0000}";
            AnisotropyWarning = Math.Abs(sx / sy - 1.0) > 0.002
                ? "Échelles X/Y différentes : l'affine déforme le plan révisé."
                : null;
        }
        else
        {
            var (scale, rotation) = AlignmentMath.Decompose(Matrix);
            AlignmentSummary = $"échelle ={scale,7:0.0000}\nrotation ={rotation,7:+0.00;-0.00}°";
            AnisotropyWarning = null;
        }
    }
}