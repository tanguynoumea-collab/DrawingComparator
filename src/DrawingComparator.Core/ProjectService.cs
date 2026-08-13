using System.Text.Json;
using System.Text.Json.Serialization;

using SkiaSharp;

namespace DrawingComparator.Core;

/// <summary>
/// La matrice de calage sérialisée en six floats NOMMÉS, en points PDF — jamais un type Skia
/// sur le disque (ADR 001) : les projets ne dépendent pas du layout mémoire de SKMatrix.
/// </summary>
public sealed record ProjectMatrix(float ScaleX, float SkewX, float TransX, float SkewY, float ScaleY, float TransY)
{
    public static ProjectMatrix FromMatrix(SKMatrix m)
        => new(m.ScaleX, m.SkewX, m.TransX, m.SkewY, m.ScaleY, m.TransY);

    public SKMatrix ToMatrix()
        => new(ScaleX, SkewX, TransX, SkewY, ScaleY, TransY, 0, 0, 1);

    public static ProjectMatrix Identity { get; } = FromMatrix(SKMatrix.Identity);
}

/// <summary>État sérialisé d'un calque : chemin absolu, page 1-based, réglages d'affichage.</summary>
public sealed record ProjectLayer(string FilePath, int Page, double OpacityPercent, bool Binarize = false);

/// <summary>
/// Un projet de superposition (fichier .dcproj) : l'état logique complet d'une session —
/// les deux PDF, leurs pages, la matrice de calage, les opacités, les teintes.
/// Tout le reste (rasters, composite) est du cache reconstructible.
/// </summary>
public sealed class ComparisonProject
{
    /// <summary>Version du schéma courant. Les champs inconnus des versions futures sont ignorés en lecture.</summary>
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public required ProjectLayer Base { get; init; }

    public required ProjectLayer Revision { get; init; }

    public ProjectMatrix Align { get; init; } = ProjectMatrix.Identity;

    /// <summary>true si l'utilisateur a permuté les teintes (base bleue, révision rouge).</summary>
    public bool TintsSwapped { get; init; }
}

/// <summary>Échec de lecture d'un fichier projet, avec un message destiné à l'utilisateur.</summary>
public sealed class ProjectLoadException(string userMessage, Exception? inner = null) : Exception(userMessage, inner);

/// <summary>
/// Lecture/écriture des fichiers projet .dcproj (JSON versionné, System.Text.Json :
/// round-trip float exact et culture-invariant par construction — aucun formatage manuel,
/// le poste FR ne peut pas produire de virgules décimales).
/// </summary>
public static class ProjectSerializer
{
    public const string FileExtension = ".dcproj";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        // Tolérance de lecture : un projet écrit par une version future reste ouvrable.
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
    };

    public static void Save(string path, ComparisonProject project)
    {
        using var stream = File.Create(path);
        JsonSerializer.Serialize(stream, project, Options);
    }

    public static ComparisonProject Load(string path)
    {
        string name = Path.GetFileName(path);
        try
        {
            using var stream = File.OpenRead(path);
            var project = JsonSerializer.Deserialize<ComparisonProject>(stream, Options)
                ?? throw new ProjectLoadException($"« {name} » est vide.");
            if (project.SchemaVersion > ComparisonProject.CurrentSchemaVersion)
                throw new ProjectLoadException(
                    $"« {name} » a été créé par une version plus récente de DrawingComparator (schéma {project.SchemaVersion}). Mettez l'application à jour.");
            return project;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ProjectLoadException($"Impossible de lire « {name} » : {ex.Message}", ex);
        }
        catch (JsonException ex)
        {
            throw new ProjectLoadException($"« {name} » n'est pas un fichier projet DrawingComparator valide.", ex);
        }
    }
}