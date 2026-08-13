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

    /// <summary>
    /// Transformation affine exacte qui envoie p1→q1, p2→q2, p3→q3 (deux systèmes 3×3 résolus
    /// par Cramer en double précision). Contrairement à la similitude, elle porte des échelles
    /// X/Y distinctes et un cisaillement : à n'appliquer QUE sur choix explicite de l'utilisateur,
    /// jamais en repli silencieux (SEN-01), et avec l'anisotropie affichée (cf. <see cref="DecomposeAffine"/>).
    /// </summary>
    /// <exception cref="DegenerateAlignmentException">points colinéaires ou trop proches.</exception>
    public static SKMatrix ComputeAffine(SKPoint p1, SKPoint q1, SKPoint p2, SKPoint q2, SKPoint p3, SKPoint q3)
    {
        // Aire signée ×2 du triangle source ; la hauteur minimale du triangle décide de la
        // stabilité du système (des points quasi colinéaires démultiplient le moindre bruit de clic).
        double det = (p2.X - p1.X) * (double)(p3.Y - p1.Y) - (p3.X - p1.X) * (double)(p2.Y - p1.Y);
        double maxSide = Math.Max(Distance(p1, p2), Math.Max(Distance(p1, p3), Distance(p2, p3)));
        if (maxSide < MinPointDistance || Math.Abs(det) / maxSide < MinPointDistance)
            throw new DegenerateAlignmentException(
                "Les trois points sont alignés ou trop proches : impossible de calculer une transformation affine stable.");

        // M·[x y 1]ᵀ : chaque ligne (a b c) se résout indépendamment par Cramer sur P = [[xᵢ yᵢ 1]]
        // (cofacteurs développés : colonne 1 = x, colonne 2 = y, colonne 3 = 1).
        (float A, float B, float C) SolveRow(double r1, double r2, double r3)
        {
            double dA = r1 * (p2.Y - (double)p3.Y) + r2 * (p3.Y - (double)p1.Y) + r3 * (p1.Y - (double)p2.Y);
            double dB = r1 * (p3.X - (double)p2.X) + r2 * (p1.X - (double)p3.X) + r3 * (p2.X - (double)p1.X);
            double dC = r1 * (p2.X * (double)p3.Y - p3.X * (double)p2.Y)
                      + r2 * (p3.X * (double)p1.Y - p1.X * (double)p3.Y)
                      + r3 * (p1.X * (double)p2.Y - p2.X * (double)p1.Y);
            return ((float)(dA / det), (float)(dB / det), (float)(dC / det));
        }

        var (a, b, c) = SolveRow(q1.X, q2.X, q3.X);
        var (d, e, f) = SolveRow(q1.Y, q2.Y, q3.Y);
        return new SKMatrix(a, b, c,
                            d, e, f,
                            0, 0, 1);
    }

    /// <summary>
    /// Décomposition d'une affine générale pour l'affichage : échelles X/Y (décomposition QR,
    /// scaleY signé par le déterminant), rotation en degrés, cisaillement. Pour une similitude,
    /// ScaleX = ScaleY et Shear = 0.
    /// </summary>
    public static (double ScaleX, double ScaleY, double RotationDegrees, double Shear) DecomposeAffine(SKMatrix m)
    {
        double a = m.ScaleX, b = m.SkewX, d = m.SkewY, e = m.ScaleY;
        double scaleX = Math.Sqrt(a * a + d * d);
        double det = a * e - b * d;
        double scaleY = scaleX == 0 ? 0 : det / scaleX;
        double rotation = Math.Atan2(d, a) * 180.0 / Math.PI;
        double shear = scaleX == 0 || scaleY == 0 ? 0 : (a * b + d * e) / (scaleX * scaleX);
        return (scaleX, scaleY, rotation, shear);
    }

    /// <summary>Erreur résiduelle du point de contrôle : ‖M·p − q‖ convertie en millimètres papier.</summary>
    public static double ResidualMm(SKMatrix m, SKPoint p, SKPoint q)
        => Distance(m.MapPoint(p), q) * 25.4 / 72.0;

    private static float Length(SKPoint v) => (float)Math.Sqrt(v.X * v.X + v.Y * v.Y);

    private static double Distance(SKPoint a, SKPoint b)
        => Math.Sqrt((a.X - b.X) * (double)(a.X - b.X) + (a.Y - b.Y) * (double)(a.Y - b.Y));
}

/// <summary>Points de calage invalides (confondus ou trop proches pour définir une échelle).</summary>
public sealed class DegenerateAlignmentException(string message) : Exception(message);