using System.IO;

using DrawingComparator.Core;

namespace DrawingComparator.App.Services;

/// <summary>
/// LA couture disque des projets (dev-council n°2, ARC2-02/TST2-01) : le ViewModel ne touche
/// jamais File/ProjectSerializer directement — il parle à cette interface, remplaçable en test.
/// </summary>
public interface IProjectStore
{
    /// <summary>Lit et VALIDE un .dcproj (matrice finie, valeurs clampées). Lève <see cref="ProjectLoadException"/>.</summary>
    ComparisonProject Load(string path);

    /// <summary>Écriture atomique (temp + remplacement) : n'endommage jamais le fichier existant.</summary>
    void Save(string path, ComparisonProject project);

    bool FileExists(string path);

    /// <summary>
    /// Un chemin est « non local » s'il est UNC (\\serveur\…) ou non pleinement qualifié :
    /// le sonder déclencherait une résolution réseau avec authentification implicite (SEC2-01).
    /// Les lecteurs mappés restent considérés locaux (mapping choisi par l'utilisateur).
    /// </summary>
    bool IsNonLocalPath(string path);
}

public sealed class ProjectStore : IProjectStore
{
    public ComparisonProject Load(string path) => ProjectSerializer.Load(path);

    public void Save(string path, ComparisonProject project) => ProjectSerializer.Save(path, project);

    public bool FileExists(string path) => File.Exists(path);

    public bool IsNonLocalPath(string path)
        => path.StartsWith(@"\\", StringComparison.Ordinal) || !Path.IsPathFullyQualified(path);
}