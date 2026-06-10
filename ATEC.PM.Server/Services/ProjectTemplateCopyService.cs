using Dapper;
using Microsoft.Extensions.Logging;

namespace ATEC.PM.Server.Services;

/// <summary>Copia struttura cartelle template commessa su disco da project_template_* nel DB.</summary>
public class ProjectTemplateCopyService
{
    private readonly DbService _db;
    private readonly ILogger<ProjectTemplateCopyService> _logger;

    public ProjectTemplateCopyService(DbService db, ILogger<ProjectTemplateCopyService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public void CopyToProject(string projectCode)
    {
        string basePath = _db.GetConfig("BasePath", @"C:\ATEC_Commesse");
        string year = DateTime.Now.Year.ToString();
        string targetPath = Path.Combine(basePath, year, projectCode);

        using MySqlConnector.MySqlConnection c = _db.Open();

        List<TemplateFolderRow> folderList = c.Query<TemplateFolderRow>(
            "SELECT id AS Id, parent_id AS ParentId, name AS Name, sort_order AS SortOrder " +
            "FROM project_template_folders WHERE is_active=1").ToList();

        if (folderList.Count == 0)
        {
            Directory.CreateDirectory(targetPath);
            _logger.LogWarning(
                "[CopyTemplateToProject] Nessuna cartella template nel DB — creata solo root per {ProjectCode}",
                projectCode);
            return;
        }

        Directory.CreateDirectory(targetPath);

        Dictionary<int, string> folderPaths = new();
        HashSet<int> unresolved = folderList.Select(f => f.Id).ToHashSet();

        while (unresolved.Count > 0)
        {
            int resolvedThisPass = 0;
            foreach (TemplateFolderRow f in folderList.Where(x => unresolved.Contains(x.Id)))
            {
                if (f.ParentId == null)
                {
                    folderPaths[f.Id] = f.Name;
                    unresolved.Remove(f.Id);
                    resolvedThisPass++;
                }
                else if (folderPaths.TryGetValue(f.ParentId.Value, out string? parentPath))
                {
                    folderPaths[f.Id] = Path.Combine(parentPath, f.Name);
                    unresolved.Remove(f.Id);
                    resolvedThisPass++;
                }
            }
            if (resolvedThisPass == 0)
            {
                _logger.LogWarning(
                    "[CopyTemplateToProject] {Count} cartelle template hanno parent_id orfano e saranno saltate: {Ids}",
                    unresolved.Count, string.Join(",", unresolved));
                break;
            }
        }

        foreach (KeyValuePair<int, string> kv in folderPaths.OrderBy(p => p.Value.Length))
            Directory.CreateDirectory(Path.Combine(targetPath, kv.Value));

        string templatesRoot = Path.Combine(basePath, "TEMPLATES");
        IEnumerable<dynamic> files = c.Query(
            "SELECT folder_id, file_name, disk_path FROM project_template_files");

        foreach (dynamic tf in files)
        {
            int folderId = (int)tf.folder_id;
            string fileName = (string)tf.file_name;
            string diskPath = (string)tf.disk_path;

            if (!folderPaths.TryGetValue(folderId, out string? relFolder))
                continue;

            string sourcePath = Path.IsPathRooted(diskPath) ? diskPath : Path.Combine(templatesRoot, diskPath);
            string destPath = Path.Combine(targetPath, relFolder, fileName);

            if (File.Exists(sourcePath) && !File.Exists(destPath))
                File.Copy(sourcePath, destPath, overwrite: false);
        }
    }

    private sealed class TemplateFolderRow
    {
        public int Id { get; set; }
        public int? ParentId { get; set; }
        public string Name { get; set; } = "";
        public int SortOrder { get; set; }
    }
}
