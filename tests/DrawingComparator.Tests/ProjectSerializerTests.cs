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

    // ── Corrections dev-council n°2 (DON2-01/02, SEC2-02) ────────────────────

    [Fact]
    public void Save_OverExisting_IsAtomic_NoTmpLeftBehind()
    {
        string path = TempProject();
        try
        {
            ProjectSerializer.Save(path, SampleProject());
            var updated = new ComparisonProject
            {
                Base = new ProjectLayer(@"C:\plans\autre.pdf", 1, 50),
                Revision = new ProjectLayer(@"C:\plans\rev2.pdf", 4, 100),
            };
            ProjectSerializer.Save(path, updated);

            var loaded = ProjectSerializer.Load(path);
            Assert.Equal(updated.Base, loaded.Base);
            Assert.False(File.Exists(path + ".tmp"), "le fichier temporaire doit avoir été remplacé");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Save_NonFiniteMatrix_Fails_WithoutTouchingExistingFile()
    {
        // DON2-01/02 : la sérialisation échoue EN MÉMOIRE (ArgumentException sur NaN),
        // le .dcproj existant reste intact octet pour octet.
        string path = TempProject();
        try
        {
            ProjectSerializer.Save(path, SampleProject());
            byte[] before = File.ReadAllBytes(path);

            var poisoned = new ComparisonProject
            {
                Base = new ProjectLayer("a.pdf", 1, 100),
                Revision = new ProjectLayer("b.pdf", 1, 100),
                Align = new ProjectMatrix(float.NaN, 0, 0, 0, 1, 0),
            };
            Assert.ThrowsAny<ArgumentException>(() => ProjectSerializer.Save(path, poisoned));

            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_InfinityViaOverflow_IsRejectedWithUserMessage()
    {
        // SEC2-02 prouvé au témoin : System.Text.Json accepte 1e999 → Infinity en silence.
        // La validation de frontière doit le refuser AVANT qu'il n'atteigne la géométrie.
        string path = TempProject();
        try
        {
            File.WriteAllText(path, """
                {
                  "SchemaVersion": 1,
                  "Base": { "FilePath": "a.pdf", "Page": 1, "OpacityPercent": 100 },
                  "Revision": { "FilePath": "b.pdf", "Page": 1, "OpacityPercent": 100 },
                  "Align": { "ScaleX": 1e999, "SkewX": 0, "TransX": 0, "SkewY": 0, "ScaleY": 1, "TransY": 0 }
                }
                """);
            var ex = Assert.Throws<ProjectLoadException>(() => ProjectSerializer.Load(path));
            Assert.Contains("matrice de calage invalide", ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_MissingFilePath_IsRejectedWithUserMessage()
    {
        // SEN2-02 : le binding constructeur accepte un champ absent (FilePath = null) —
        // la frontière doit le refuser, jamais un ArgumentNullException en aval.
        string path = TempProject();
        try
        {
            File.WriteAllText(path, """
                {
                  "SchemaVersion": 1,
                  "Base": { "Page": 1, "OpacityPercent": 100 },
                  "Revision": { "FilePath": "b.pdf", "Page": 1, "OpacityPercent": 100 }
                }
                """);
            var ex = Assert.Throws<ProjectLoadException>(() => ProjectSerializer.Load(path));
            Assert.Contains("deux plans", ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_SingularMatrix_IsRejectedWithUserMessage()
    {
        string path = TempProject();
        try
        {
            File.WriteAllText(path, """
                {
                  "SchemaVersion": 1,
                  "Base": { "FilePath": "a.pdf", "Page": 1, "OpacityPercent": 100 },
                  "Revision": { "FilePath": "b.pdf", "Page": 1, "OpacityPercent": 100 },
                  "Align": { "ScaleX": 0, "SkewX": 0, "TransX": 10, "SkewY": 0, "ScaleY": 0, "TransY": 5 }
                }
                """);
            var ex = Assert.Throws<ProjectLoadException>(() => ProjectSerializer.Load(path));
            Assert.Contains("dégénérée", ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_ClampsHostileValues()
    {
        string path = TempProject();
        try
        {
            File.WriteAllText(path, """
                {
                  "SchemaVersion": 1,
                  "Base": { "FilePath": "a.pdf", "Page": 0, "OpacityPercent": 500 },
                  "Revision": { "FilePath": "b.pdf", "Page": -3, "OpacityPercent": -10 }
                }
                """);
            var loaded = ProjectSerializer.Load(path);
            Assert.Equal(100, loaded.Base.OpacityPercent);
            Assert.Equal(0, loaded.Revision.OpacityPercent);
            Assert.Equal(1, loaded.Base.Page);
            Assert.Equal(1, loaded.Revision.Page);
        }
        finally
        {
            File.Delete(path);
        }
    }
}