using System.IO;

using DrawingComparator.Core;

using SkiaSharp;

namespace DrawingComparator.Tests;

public class ProjectSerializerTests
{
    private static string TempProject()
        => Path.Combine(Path.GetTempPath(), $"dc-test-{Guid.NewGuid():N}{ProjectSerializer.FileExtension}");

    private static ComparisonProject SampleProject() => new()
    {
        Base = new ProjectLayer(@"C:\plans\base.pdf", Page: 3, OpacityPercent: 85.5, Binarize: false),
        Revision = new ProjectLayer(@"\\serveur\plans\rev.pdf", Page: 2, OpacityPercent: 100, Binarize: true),
        Align = ProjectMatrix.FromMatrix(
            SKMatrix.CreateRotationDegrees(1.5f, 500, 400).PostConcat(SKMatrix.CreateScale(0.96f, 0.96f))),
        TintsSwapped = true,
    };

    [Fact]
    public void RoundTrip_PreservesEveryField_FloatsExactly()
    {
        string path = TempProject();
        try
        {
            var original = SampleProject();
            ProjectSerializer.Save(path, original);
            var loaded = ProjectSerializer.Load(path);

            Assert.Equal(original.SchemaVersion, loaded.SchemaVersion);
            Assert.Equal(original.Base, loaded.Base);
            Assert.Equal(original.Revision, loaded.Revision);
            // Round-trip float exact (System.Text.Json, culture-invariant — poste FR inclus).
            Assert.Equal(original.Align, loaded.Align);
            Assert.Equal(original.Align.ToMatrix(), loaded.Align.ToMatrix());
            Assert.True(loaded.TintsSwapped);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_UnknownFields_AreIgnored()
    {
        // Un projet écrit par une version future (même schéma, champs en plus) reste ouvrable.
        string path = TempProject();
        try
        {
            File.WriteAllText(path, """
                {
                  "SchemaVersion": 1,
                  "Base": { "FilePath": "a.pdf", "Page": 1, "OpacityPercent": 100 },
                  "Revision": { "FilePath": "b.pdf", "Page": 1, "OpacityPercent": 100 },
                  "FutureFeature": { "nested": [1, 2, 3] }
                }
                """);
            var loaded = ProjectSerializer.Load(path);
            Assert.Equal("a.pdf", loaded.Base.FilePath);
            Assert.Equal(ProjectMatrix.Identity, loaded.Align);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_NewerSchema_FailsWithUserMessage()
    {
        string path = TempProject();
        try
        {
            File.WriteAllText(path, """
                {
                  "SchemaVersion": 99,
                  "Base": { "FilePath": "a.pdf", "Page": 1, "OpacityPercent": 100 },
                  "Revision": { "FilePath": "b.pdf", "Page": 1, "OpacityPercent": 100 }
                }
                """);
            var ex = Assert.Throws<ProjectLoadException>(() => ProjectSerializer.Load(path));
            Assert.Contains("version plus récente", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_CorruptJson_FailsWithUserMessage()
    {
        string path = TempProject();
        try
        {
            File.WriteAllText(path, "{ pas du json ");
            var ex = Assert.Throws<ProjectLoadException>(() => ProjectSerializer.Load(path));
            Assert.Contains("valide", ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_MissingFile_FailsWithUserMessage()
    {
        var ex = Assert.Throws<ProjectLoadException>(
            () => ProjectSerializer.Load(Path.Combine(Path.GetTempPath(), "dc-inexistant.dcproj")));
        Assert.Contains("Impossible de lire", ex.Message);
    }
}