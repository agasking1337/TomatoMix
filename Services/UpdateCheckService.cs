using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace TomatoMix.Services;

public sealed class UpdateCheckService
{
    private const string LatestReleaseApiUrl = "https://api.github.com/repos/agasking1337/TomatoMix/releases/latest";
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(8)
    };

    public async Task<string?> GetAvailableVersionAsync(CancellationToken cancellationToken)
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(UpdateCheckService).Assembly;
        var currentVersionText =
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString();

        if (!TryParseVersion(currentVersionText, out var currentVersion))
            return null;

        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApiUrl);
        request.Headers.UserAgent.ParseAdd("TomatoMix-UpdateChecker");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");

        using var response = await HttpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("tag_name", out var tagNameElement) ||
            tagNameElement.ValueKind != JsonValueKind.String ||
            !TryParseVersion(tagNameElement.GetString(), out var latestVersion))
        {
            return null;
        }

        return latestVersion > currentVersion ? latestVersion.ToString() : null;
    }

    private static bool TryParseVersion(string? value, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var normalized = value.Trim().TrimStart('v', 'V');
        var suffixIndex = normalized.IndexOfAny(['-', '+']);
        if (suffixIndex >= 0)
            normalized = normalized[..suffixIndex];

        return Version.TryParse(normalized, out version!) && version.Build >= 0;
    }
}
