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
}