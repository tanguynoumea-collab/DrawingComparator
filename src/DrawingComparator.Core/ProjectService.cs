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

    /// <summary>
    /// Écriture ATOMIQUE (DON2-01) : sérialisation en mémoire d'abord — un état float
    /// pathologique échoue AVANT de toucher le disque (DON2-02) — puis fichier temporaire
    /// et remplacement ; un crash ou une IOException mi-écriture ne peut jamais tronquer
    /// le .dcproj existant.
    /// </summary>
    public static void Save(string path, ComparisonProject project)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(project, Options);

        string tmp = path + ".tmp";
        File.WriteAllBytes(tmp, payload);
        if (File.Exists(path))
        {
            try
            {
                File.Replace(tmp, path, destinationBackupFileName: null);
            }
            catch (PlatformNotSupportedException)
            {
                // Certains partages réseau refusent Replace : Move-écrase est le repli
                // le moins pire (fenêtre de vulnérabilité réduite au rename).
                File.Move(tmp, path, overwrite: true);
            }
        }
        else
        {
            File.Move(tmp, path);
        }
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
            return Validate(project, name);
        }
        catch (ProjectLoadException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ProjectLoadException($"Impossible de lire « {name} » : {ex.Message}", ex);
        }
        catch (JsonException ex)
        {
            throw new ProjectLoadException($"« {name} » n'est pas un fichier projet DrawingComparator valide.", ex);
        }
        catch (Exception ex)
        {
            // Frontière de confiance : AUCUNE exception d'un fichier tiers ne doit dépasser
            // le message « projet invalide » (SEC2-02 — un .dcproj hostile n'atteint pas le
            // filet de crash générique).
            throw new ProjectLoadException($"« {name} » n'est pas un fichier projet DrawingComparator valide.", ex);
        }
    }

    /// <summary>
    /// Validation sémantique à la frontière (cross-challenge Sécurité↔Données) : System.Text.Json
    /// accepte silencieusement 1e999 → Infinity ; une matrice non finie produirait une géométrie
    /// muette (TryInvert en échec, canvas blanc). Les opacités sont clampées par symétrie avec
    /// les pages (clampées côté ouverture, faute de connaître PageCount ici).
    /// </summary>
    private static ComparisonProject Validate(ComparisonProject project, string name)
    {
        var m = project.Align;
        ReadOnlySpan<float> terms = [m.ScaleX, m.SkewX, m.TransX, m.SkewY, m.ScaleY, m.TransY];
        foreach (float t in terms)
        {
            if (!float.IsFinite(t))
                throw new ProjectLoadException($"« {name} » contient une matrice de calage invalide (valeur non finie).");
        }

        static ProjectLayer Clamp(ProjectLayer layer) => layer with
        {
            OpacityPercent = Math.Clamp(layer.OpacityPercent, 0, 100),
            Page = Math.Max(1, layer.Page),
        };
        return new ComparisonProject
        {
            SchemaVersion = project.SchemaVersion,
            Base = Clamp(project.Base),
            Revision = Clamp(project.Revision),
            Align = project.Align,
            TintsSwapped = project.TintsSwapped,
        };
    }
}