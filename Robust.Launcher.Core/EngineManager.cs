using System.IO.Compression;
using System.Security.Cryptography;

namespace Robust.Launcher.Core;

/// <summary>
/// Downloads builds of our engine fork (HorizonToolbox) for the current platform,
/// matched to the <c>engine_version</c> a server reports.
/// </summary>
/// <remarks>
/// Builds are published by CI as GitHub release assets:
/// <c>{base}/android-engine-v{version}/Robust.Client_android.zip</c> and a <c>.sha256</c> file next to it.
/// </remarks>
public sealed class EngineManager
{
    public const string DefaultReleaseBase = "https://github.com/lAstronautl/RobustToolbox/releases/download/";
    public const string PlatformName = "android";

    private readonly HttpClient _http;
    private readonly string _root;
    private readonly string _releaseBase;

    public EngineManager(HttpClient http, string cacheRoot, string? releaseBase = null)
    {
        _http = http;
        _root = Path.Combine(cacheRoot, "engines");
        _releaseBase = releaseBase ?? DefaultReleaseBase;
        if (!_releaseBase.EndsWith('/'))
            _releaseBase += "/";
        Directory.CreateDirectory(_root);
    }

    public static string NormalizeVersion(string v)
    {
        var plus = v.IndexOf('+');
        if (plus >= 0) v = v[..plus];
        if (Version.TryParse(v, out var parsed))
            return $"{parsed.Major}.{parsed.Minor}.{Math.Max(parsed.Build, 0)}";
        return v.Trim();
    }

    public Uri GetZipUrl(string version) =>
        new($"{_releaseBase}{PlatformName}-engine-v{NormalizeVersion(version)}/Robust.Client_{PlatformName}.zip");

    public string GetInstallDir(string version) => Path.Combine(_root, NormalizeVersion(version));

    public bool IsInstalled(string version) =>
        File.Exists(Path.Combine(GetInstallDir(version), ".installed"));

    /// <summary>
    /// Checks whether an engine build exists for the version, without downloading it.
    /// </summary>
    public async Task<bool> IsAvailableAsync(string version, CancellationToken ct = default)
    {
        if (IsInstalled(version))
            return true;

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Head, GetZipUrl(version));
            using var resp = await _http.SendAsync(req, ct);
            return resp.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    /// <summary>
    /// Makes sure the engine is downloaded and extracted. Returns the install directory.
    /// </summary>
    public async Task<string> EnsureEngineAsync(string version, IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        var dir = GetInstallDir(version);
        if (IsInstalled(version))
            return dir;

        var zipUrl = GetZipUrl(version);
        string? expectedSha = null;
        try
        {
            var shaText = await _http.GetStringAsync(new Uri(zipUrl.AbsoluteUri + ".sha256"), ct);
            expectedSha = shaText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        }
        catch (HttpRequestException)
        {
            // Checksum is optional; HTTPS already protects the download.
        }

        var tmpZip = Path.Combine(_root, NormalizeVersion(version) + ".zip.part");
        using (var resp = await _http.GetAsync(zipUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            if (!resp.IsSuccessStatusCode)
                throw new EngineNotAvailableException(version, (int)resp.StatusCode);

            var total = resp.Content.Headers.ContentLength ?? 0;
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(tmpZip);
            var buf = new byte[81920];
            long done = 0;
            int read;
            while ((read = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, read), ct);
                done += read;
                progress?.Report(new DownloadProgress("Загрузка движка", done, total));
            }
        }

        if (expectedSha != null)
        {
            await using var f = File.OpenRead(tmpZip);
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(f, ct));
            if (!actual.Equals(expectedSha, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(tmpZip);
                throw new InvalidDataException("Хэш архива движка не совпадает.");
            }
        }

        progress?.Report(new DownloadProgress("Распаковка движка", 0, 0));
        InstallFromZip(tmpZip, dir);
        File.Delete(tmpZip);
        return dir;
    }

    /// <summary>Installs an engine zip that is already on disk (e.g. for testing or side-loading).</summary>
    public static void InstallFromZip(string zipPath, string dir)
    {
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
        ZipFile.ExtractToDirectory(zipPath, dir);
        File.WriteAllText(Path.Combine(dir, ".installed"), DateTime.UtcNow.ToString("O"));
    }
}

public sealed class EngineNotAvailableException(string version, int status)
    : Exception($"Сборка движка {version} для Android не найдена (HTTP {status}).")
{
    public string Version { get; } = version;
}
