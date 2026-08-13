using System.IO;
using System.Text.Json;

namespace DrawingComparator.App.Services;

/// <summary>Une entrée de la liste « Reprendre » : le projet, ses deux plans, sa dernière ouverture.</summary>
public sealed record RecentProject(string ProjectPath, string BaseName, string RevisionName, DateTime LastOpened)
{
    public string ProjectName => Path.GetFileNameWithoutExtension(ProjectPath);

    /// <summary>« base.pdf ↔ rev.pdf » pour la liste Reprendre.</summary>
    public string PairLabel => $"{BaseName} ↔ {RevisionName}";

    public string LastOpenedLabel => LastOpened.ToString("dd MMM HH:mm", System.Globalization.CultureInfo.CurrentCulture);
}

public interface IRecentProjectsService
{
    IReadOnlyList<RecentProject> Items { get; }

    /// <summary>Ajoute ou remonte un projet en tête de liste (MRU, plafonnée).</summary>
    void Touch(RecentProject project);

    void Remove(string projectPath);
}

/// <summary>
/// Liste MRU persistée dans %APPDATA%\DrawingComparator\recent.json — le store central
/// qui rend le panneau « Reprendre » possible (les .dcproj, eux, vivent où l'utilisateur veut).
/// Toute erreur d'E/S est silencieuse : la liste des récents est un confort, jamais un blocage.
/// </summary>
public sealed class RecentProjectsService : IRecentProjectsService
{
    public const int MaxItems = 8;

    private readonly string _storePath;
    private readonly List<RecentProject> _items;

    public RecentProjectsService()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DrawingComparator", "recent.json"))
    {
    }

    /// <summary>Chemin injectable pour les tests.</summary>
    public RecentProjectsService(string storePath)
    {
        _storePath = storePath;
        _items = Load();
    }

    public IReadOnlyList<RecentProject> Items => _items;

    public void Touch(RecentProject project)
    {
        _items.RemoveAll(p => string.Equals(p.ProjectPath, project.ProjectPath, StringComparison.OrdinalIgnoreCase));
        _items.Insert(0, project);
        if (_items.Count > MaxItems)
            _items.RemoveRange(MaxItems, _items.Count - MaxItems);
        Save();
    }

    public void Remove(string projectPath)
    {
        if (_items.RemoveAll(p => string.Equals(p.ProjectPath, projectPath, StringComparison.OrdinalIgnoreCase)) > 0)
            Save();
    }

    private List<RecentProject> Load()
    {
        try
        {
            if (File.Exists(_storePath))
                return JsonSerializer.Deserialize<List<RecentProject>>(File.ReadAllText(_storePath)) ?? [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // liste illisible → on repart de zéro, sans gêner le démarrage
        }
        return [];
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_storePath)!);
            File.WriteAllText(_storePath, JsonSerializer.Serialize(_items));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // l'échec d'écriture des récents ne doit jamais faire échouer une sauvegarde de projet
        }
    }
}