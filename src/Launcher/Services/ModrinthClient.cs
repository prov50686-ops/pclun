using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Web;

namespace PcLun.Services;

/// <summary>
/// Minimal Modrinth API client: search + project versions + direct download.
/// </summary>
public static class ModrinthClient
{
    private const string Api = "https://api.modrinth.com/v2";

    public class ProjectHit
    {
        public string Project_id { get; set; } = "";
        public string Slug { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string Author { get; set; } = "";
        public int Downloads { get; set; }
        public string Icon_url { get; set; } = "";
    }

    private class SearchResp { public List<ProjectHit> Hits { get; set; } = new(); }

    public class ProjectVersion
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Version_number { get; set; } = "";
        public List<string> Game_versions { get; set; } = new();
        public List<string> Loaders { get; set; } = new();
        public List<VersionFile> Files { get; set; } = new();
    }

    public class VersionFile
    {
        public string Url { get; set; } = "";
        public string Filename { get; set; } = "";
        public bool Primary { get; set; }
    }

    public static async Task<IReadOnlyList<ProjectHit>> SearchAsync(string query, string mcVersion, string loader, int limit = 20)
    {
        try
        {
            var facets = $"[[\"versions:{mcVersion}\"],[\"project_type:mod\"],[\"categories:'{loader}'\"]]";
            var url = $"{Api}/search?query={HttpUtility.UrlEncode(query)}&facets={HttpUtility.UrlEncode(facets)}&limit={limit}";
            var data = await Http.Client.GetFromJsonAsync<SearchResp>(url);
            return data?.Hits ?? new List<ProjectHit>();
        }
        catch (Exception ex)
        {
            AppLogger.Warn("ModrinthClient.SearchAsync failed: " + ex.Message);
            return Array.Empty<ProjectHit>();
        }
    }

    public static async Task<IReadOnlyList<ProjectVersion>> ListVersionsAsync(string slug, string mcVersion, string loader)
    {
        try
        {
            var url = $"{Api}/project/{HttpUtility.UrlPathEncode(slug)}/version" +
                      $"?game_versions=[\"{mcVersion}\"]&loaders=[\"{loader}\"]";
            var data = await Http.Client.GetFromJsonAsync<List<ProjectVersion>>(url);
            return data ?? new List<ProjectVersion>();
        }
        catch (Exception ex)
        {
            AppLogger.Warn("ModrinthClient.ListVersionsAsync failed: " + ex.Message);
            return Array.Empty<ProjectVersion>();
        }
    }

    public static async Task<bool> DownloadAsync(VersionFile file, string targetDir)
    {
        try
        {
            Directory.CreateDirectory(targetDir);
            var bytes = await Http.Client.GetByteArrayAsync(file.Url);
            File.WriteAllBytes(Path.Combine(targetDir, file.Filename), bytes);
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("ModrinthClient.DownloadAsync failed: " + ex.Message);
            return false;
        }
    }
}
