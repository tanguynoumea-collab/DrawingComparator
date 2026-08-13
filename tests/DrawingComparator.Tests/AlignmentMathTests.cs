using DrawingComparator.Core;

using SkiaSharp;

namespace DrawingComparator.Tests;

public class AlignmentMathTests
{
    private const float Tolerance = 1e-3f;

    private static void AssertMaps(SKMatrix m, SKPoint input, SKPoint expected)
    {
        var actual = m.MapPoint(input);
        Assert.Equal(expected.X, actual.X, Tolerance);
        Assert.Equal(expected.Y, actual.Y, Tolerance);
    }

    [Fact]
    public void Identity_WhenPointsCoincide()
    {
        var m = AlignmentMath.ComputeSimilarity(
            new SKPoint(10, 20), new SKPoint(10, 20),
            new SKPoint(110, 20), new SKPoint(110, 20));

        AssertMaps(m, new SKPoint(50, 70), new SKPoint(50, 70));
    }

    [Fact]
    public void PureTranslation()
    {
        var m = AlignmentMath.ComputeSimilarity(
            new SKPoint(0, 0), new SKPoint(30, -12),
            new SKPoint(100, 0), new SKPoint(130, -12));

        AssertMaps(m, new SKPoint(0, 0), new SKPoint(30, -12));
        AssertMaps(m, new SKPoint(50, 50), new SKPoint(80, 38));
    }

    [Fact]
    public void PureScale_AnchoredOnFirstPoint()
    {
        // p1 est l'ancrage : il ne bouge pas, le reste s'étire ×2 autour de lui.
        var m = AlignmentMath.ComputeSimilarity(
            new SKPoint(10, 10), new SKPoint(10, 10),
            new SKPoint(20, 10), new SKPoint(30, 10));

        AssertMaps(m, new SKPoint(10, 10), new SKPoint(10, 10));
        AssertMaps(m, new SKPoint(10, 20), new SKPoint(10, 30));

        var (scale, rotation) = AlignmentMath.Decompose(m);
        Assert.Equal(2.0, scale, 3);
        Assert.Equal(0.0, rotation, 3);
    }

    [Fact]
    public void Rotation90Degrees()
    {
        var m = AlignmentMath.ComputeSimilarity(
            new SKPoint(0, 0), new SKPoint(0, 0),
            new SKPoint(100, 0), new SKPoint(0, 100));

        AssertMaps(m, new SKPoint(200, 0), new SKPoint(0, 200));

        var (scale, rotation) = AlignmentMath.Decompose(m);
        Assert.Equal(1.0, scale, 3);
        Assert.Equal(90.0, rotation, 2);
    }

    [Fact]
    public void ScaleAndRotationCombined()
    {
        // ×0.5 et −90° : p2−p1 = (0,100) doit devenir q2−q1 = (50,0).
        var m = AlignmentMath.ComputeSimilarity(
            new SKPoint(0, 0), new SKPoint(1000, 500),
            new SKPoint(0, 100), new SKPoint(1050, 500));

        var (scale, rotation) = AlignmentMath.Decompose(m);
        Assert.Equal(0.5, scale, 3);
        Assert.Equal(-90.0, rotation, 2);
        AssertMaps(m, new SKPoint(0, 0), new SKPoint(1000, 500));
    }

    [Fact]
    public void Throws_WhenSourcePointsCoincide()
    {
        Assert.Throws<DegenerateAlignmentException>(() => AlignmentMath.ComputeSimilarity(
            new SKPoint(5, 5), new SKPoint(0, 0),
            new SKPoint(5.2f, 5.2f), new SKPoint(100, 0)));
    }

    [Fact]
    public void Throws_WhenTargetPointsCoincide()
    {
        Assert.Throws<DegenerateAlignmentException>(() => AlignmentMath.ComputeSimilarity(
            new SKPoint(0, 0), new SKPoint(50, 50),
            new SKPoint(100, 0), new SKPoint(50.3f, 50.3f)));
    }

    [Fact]
    public void AnchorTranslation_MovesP1OntoQ1()
    {
        var m = AlignmentMath.ComputeAnchorTranslation(new SKPoint(10, 10), new SKPoint(200, -40));
        AssertMaps(m, new SKPoint(10, 10), new SKPoint(200, -40));
    }

    // ── Affine 3 points (item 7, SEN-01) ─────────────────────────────────────

    [Fact]
    public void Affine_MapsAllThreePairsExactly()
    {
        var p1 = new SKPoint(0, 0);
        var p2 = new SKPoint(100, 0);
        var p3 = new SKPoint(0, 100);
        var q1 = new SKPoint(20, 10);
        var q2 = new SKPoint(118, 14);   // ~×0.98 en X, léger biais
        var q3 = new SKPoint(17, 112);   // ~×1.02 en Y

        var m = AlignmentMath.ComputeAffine(p1, q1, p2, q2, p3, q3);

        AssertMaps(m, p1, q1);
        AssertMaps(m, p2, q2);
        AssertMaps(m, p3, q3);
        // Et l'affine interpole linéairement entre eux.
        AssertMaps(m, new SKPoint(50, 50), new SKPoint((q2.X - q1.X) / 2f + (q3.X - q1.X) / 2f + q1.X,
                                                       (q2.Y - q1.Y) / 2f + (q3.Y - q1.Y) / 2f + q1.Y));
    }

    [Fact]
    public void Affine_ReproducesASimilarity_WhenPairsAreSimilar()
    {
        var sim = SKMatrix.CreateRotationDegrees(2f, 300, 300).PostConcat(SKMatrix.CreateScale(0.97f, 0.97f));
        var p1 = new SKPoint(50, 60);
        var p2 = new SKPoint(700, 80);
        var p3 = new SKPoint(200, 500);

        var m = AlignmentMath.ComputeAffine(p1, sim.MapPoint(p1), p2, sim.MapPoint(p2), p3, sim.MapPoint(p3));

        var (sx, sy, _, shear) = AlignmentMath.DecomposeAffine(m);
        Assert.Equal(0.97, sx, 3);
        Assert.Equal(0.97, sy, 3);
        Assert.Equal(0.0, shear, 3);
    }

    [Fact]
    public void Affine_Throws_WhenPointsColinear()
    {
        Assert.Throws<DegenerateAlignmentException>(() => AlignmentMath.ComputeAffine(
            new SKPoint(0, 0), new SKPoint(0, 0),
            new SKPoint(100, 100), new SKPoint(100, 100),
            new SKPoint(200, 200), new SKPoint(210, 190)));
    }

    [Fact]
    public void DecomposeAffine_ExposesAnisotropy()
    {
        // « fit to page » anisotrope : ×1.0 en X, ×0.9 en Y — exactement ce que
        // l'avertissement du panneau Calage doit rendre visible.
        var m = SKMatrix.CreateScale(1.0f, 0.9f);
        var (sx, sy, rotation, shear) = AlignmentMath.DecomposeAffine(m);
        Assert.Equal(1.0, sx, 3);
        Assert.Equal(0.9, sy, 3);
        Assert.Equal(0.0, rotation, 3);
        Assert.Equal(0.0, shear, 3);
    }

    [Fact]
    public void ResidualMm_MeasuresControlPointError()
    {
        // Identité : p = (0,0) censé tomber sur q = (72,0) pt → erreur de 72 pt = 25,4 mm.
        double mm = AlignmentMath.ResidualMm(SKMatrix.Identity, new SKPoint(0, 0), new SKPoint(72, 0));
        Assert.Equal(25.4, mm, 3);

        // Point de contrôle parfait → résiduel nul.
        Assert.Equal(0.0, AlignmentMath.ResidualMm(SKMatrix.Identity, new SKPoint(10, 10), new SKPoint(10, 10)), 6);
    }
}