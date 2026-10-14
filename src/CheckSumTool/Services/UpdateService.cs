using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;

namespace CheckSumTool.Services;

/// <summary>检查更新的结果。</summary>
public enum UpdateStatus
{
    /// <summary>已是最新版本。</summary>
    UpToDate,

    /// <summary>发现新版本。</summary>
    UpdateAvailable,

    /// <summary>检查失败（网络异常、限流等）。</summary>
    Failed,
}

/// <summary>一次更新检查的结果。</summary>
public readonly record struct UpdateCheckResult(UpdateStatus Status, string? LatestTag, string? ReleaseUrl);

/// <summary>
/// 版本更新检查：向 GitHub 查询最新 Release，并用系统默认方式打开链接。
/// </summary>
public static class UpdateService
{
    /// <summary>项目仓库地址（「关于软件」跳转目标）。</summary>
    public const string RepositoryUrl = "https://github.com/dfvips/checksum-tool";

    /// <summary>最新 Release 的 GitHub API 地址。</summary>
    private const string LatestReleaseApi =
        "https://api.github.com/repos/dfvips/checksum-tool/releases/latest";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        // GitHub API 强制要求带 User-Agent，否则直接 403。
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ChecksumTool");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    /// <summary>当前程序版本（与设置页展示的版本号一致）。</summary>
    public static string CurrentVersion { get; } = ResolveCurrentVersion();

    private static string ResolveCurrentVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        string? informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        return string.IsNullOrWhiteSpace(informational)
            ? assembly.GetName().Version?.ToString(3) ?? "1.0.0"
            : informational;
    }

    /// <summary>查询最新 Release 并与当前版本比较。任何异常都归一为 <see cref="UpdateStatus.Failed"/>。</summary>
    public static async Task<UpdateCheckResult> CheckAsync(CancellationToken ct)
    {
        try
        {
            using var response = await Http.GetAsync(LatestReleaseApi, ct);

            // 仓库还没有发布任何 Release：视为已是最新，不打扰用户。
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new UpdateCheckResult(UpdateStatus.UpToDate, null, null);
            }

            if (!response.IsSuccessStatusCode)
            {
                return new UpdateCheckResult(UpdateStatus.Failed, null, null);
            }

            var release = await response.Content.ReadFromJsonAsync<ReleaseDto>(ct);
            if (string.IsNullOrWhiteSpace(release?.TagName))
            {
                return new UpdateCheckResult(UpdateStatus.Failed, null, null);
            }

            bool newer = IsNewer(release.TagName, CurrentVersion);
            return new UpdateCheckResult(
                newer ? UpdateStatus.UpdateAvailable : UpdateStatus.UpToDate,
                release.TagName,
                release.HtmlUrl);
        }
        catch
        {
            return new UpdateCheckResult(UpdateStatus.Failed, null, null);
        }
    }

    /// <summary>
    /// 判断最新 tag 是否比当前版本新。
    /// 先按语义化版本比较；若当前版本是构建时间戳（无法解析为 <see cref="Version"/>），
    /// 只要 tag 与当前版本不同就提示用户去 Release 页面自行确认。
    /// </summary>
    public static bool IsNewer(string latestTag, string currentVersion)
    {
        string latest = Normalize(latestTag);
        if (latest.Length == 0)
        {
            return false;
        }

        string current = Normalize(currentVersion);
        if (string.Equals(latest, current, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (Version.TryParse(latest, out Version? latestVersion)
            && Version.TryParse(current, out Version? currentParsed))
        {
            return latestVersion > currentParsed;
        }

        return true;
    }

    /// <summary>去掉前缀 <c>v</c> 与语义化版本里的构建元数据（<c>+xxx</c>）。</summary>
    private static string Normalize(string version)
    {
        string value = version.Trim();
        if (value.StartsWith('v') || value.StartsWith('V'))
        {
            value = value[1..];
        }

        int plus = value.IndexOf('+');
        return plus >= 0 ? value[..plus] : value;
    }

    /// <summary>用系统默认方式打开外部链接（浏览器 / 默认处理程序）。</summary>
    public static void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url)
            {
                UseShellExecute = true,
            });
        }
        catch
        {
            // 没有可用的默认处理程序时静默忽略，不打断用户。
        }
    }

    private sealed record ReleaseDto(
        [property: JsonPropertyName("tag_name")] string? TagName,
        [property: JsonPropertyName("html_url")] string? HtmlUrl);
}
