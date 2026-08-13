using SkiaSharp;

namespace DrawingComparator.Core;

/// <summary>
/// Calcul de la similitude 2 points (translation + échelle + rotation).
/// Toutes les coordonnées sont exprimées en points PDF (1/72 pouce) du plan concerné,
/// jamais en pixels : la matrice survit aux changements de DPI de rasterisation.
/// La matrice résultat est stockée en 3×2 générale (SKMatrix) pour que le passage
/// à une transformation affine 3 points reste un ajout, pas une refonte.
/// </summary>
public static class AlignmentMath
{
    /// <summary>Distance minimale (en points PDF ≈ 0,35 mm) entre deux points de référence.</summary>
    public const float MinPointDistance = 1.0f;

    /// <summary>
    /// Similitude qui envoie p1→q1 et p2→q2 : le premier couple est l'ancrage,
    /// le second fixe l'échelle et la rotation autour de cet ancrage.
    /// (p1, p2) : points du plan révisé, dans son propre repère.
    /// (q1, q2) : points cibles dans le repère du plan de base.
    /// </summary>
    /// <exception cref="DegenerateAlignmentException">points sources ou cibles confondus.</exception>
    public static SKMatrix ComputeSimilarity(SKPoint p1, SKPoint q1, SKPoint p2, SKPoint q2)
    {
        var dp = new SKPoint(p2.X - p1.X, p2.Y - p1.Y);
        var dq = new SKPoint(q2.X - q1.X, q2.Y - q1.Y);

        float lenP = Length(dp);
        float lenQ = Length(dq);
        if (lenP < MinPointDistance)
            throw new DegenerateAlignmentException("Les deux points du plan révisé sont confondus.");
        if (lenQ < MinPointDistance)
            throw new DegenerateAlignmentException("Les deux points du plan de base sont confondus.");

        // Similitude en arithmétique complexe : a = dq / dp (échelle + rotation).
        float denom = dp.X * dp.X + dp.Y * dp.Y;
        float aRe = (dq.X * dp.X + dq.Y * dp.Y) / denom;
        float aIm = (dq.Y * dp.X - dq.X * dp.Y) / denom;

        // M = [ aRe -aIm tx ; aIm aRe ty ] avec t = q1 − a·p1
        float tx = q1.X - (aRe * p1.X - aIm * p1.Y);
        float ty = q1.Y - (aIm * p1.X + aRe * p1.Y);

        return new SKMatrix(aRe, -aIm, tx,
                            aIm, aRe, ty,
                            0, 0, 1);
    }

    /// <summary>Translation pure qui envoie p1 sur q1 (étape d'ancrage, avant le second couple).</summary>
    public static SKMatrix ComputeAnchorTranslation(SKPoint p1, SKPoint q1)
        => SKMatrix.CreateTranslation(q1.X - p1.X, q1.Y - p1.Y);

    /// <summary>Extrait (échelle, rotation en degrés) d'une similitude — pour l'affichage du panneau Calage.</summary>
    public static (double Scale, double RotationDegrees) Decompose(SKMatrix m)
    {
        double scale = Math.Sqrt(m.ScaleX * (double)m.ScaleX + m.SkewY * (double)m.SkewY);
        double rotation = Math.Atan2(m.SkewY, m.ScaleX) * 180.0 / Math.PI;
        return (scale, rotation);
    }

    private static float Length(SKPoint v) => (float)Math.Sqrt(v.X * v.X + v.Y * v.Y);
}

/// <summary>Points de calage invalides (confondus ou trop proches pour définir une échelle).</summary>
public sealed class DegenerateAlignmentException(string message) : Exception(message);