using System.Text.Json;
using Robust.Launcher.Core;

namespace Robust.Client.Android.Launcher;

/// <summary>
/// Everything that happens between "tap Connect" and starting the game process:
/// server info, account token refresh, engine + content download, launch spec.
/// </summary>
public sealed class Connector
{
    private readonly HttpClient _http;
    private readonly LauncherSettings _settings;
    private readonly string _filesDir;
    private readonly string _cacheDir;

    public Connector(HttpClient http, LauncherSettings settings, string filesDir, string cacheDir)
    {
        _http = http;
        _settings = settings;
        _filesDir = filesDir;
        _cacheDir = cacheDir;
    }

    /// <summary>Returns the path of the launch spec file for the game process.</summary>
    /// <param name="confirmCompatMode">
    /// Asked when only the official engine build is available (runs without the content sandbox).
    /// Gets the engine version, returns whether the user agreed.
    /// </param>
    public async Task<string> PrepareAsync(ServerAddress address, float defaultUiScale,
        Func<string, Task<bool>> confirmCompatMode, IProgress<DownloadProgress> progress, CancellationToken ct)
    {
        progress.Report(new DownloadProgress("Получение информации о сервере", 0, 0));
        var info = await new ServerApi(_http).GetInfoAsync(address, ct);

        var engineVersion = info.Build?.EngineVersion;
        if (string.IsNullOrEmpty(engineVersion))
            throw new UserFacingException("Сервер не сообщил версию движка.");

        // Account.
        var login = _settings.Login;
        var authMode = info.Auth?.Mode ?? "Optional";
        if (login != null && authMode != "Disabled")
        {
            var auth = new AuthApi(_http);
            if (login.LoginToken.IsExpired)
            {
                login = null;
            }
            else if (login.LoginToken.ShouldRefresh)
            {
                progress.Report(new DownloadProgress("Обновление входа", 0, 0));
                var refreshed = await auth.RefreshAsync(login.LoginToken, ct);
                login = refreshed == null ? null : login with { Token = refreshed.Token, ExpireTime = refreshed.ExpireTime };
            }

            _settings.Login = login;
            _settings.Save(_filesDir);
        }

        if (login == null && authMode == "Required")
            throw new UserFacingException("Этот сервер требует вход в аккаунт. Войдите на вкладке «Аккаунт».");

        // Engine: our Android build of that version if published, otherwise the official build adapted at runtime.
        var engines = new EngineManager(_http, _cacheDir, _settings.EngineReleaseBase);
        var availability = await engines.CheckAvailabilityAsync(engineVersion, ct);
        if (availability == EngineAvailability.None)
            throw new UserFacingException($"Движок версии {engineVersion} не найден ни в Android-сборках, ни в официальных.");

        if (availability == EngineAvailability.Official && !_settings.TrustedServers.Contains(address.Original))
        {
            if (!await confirmCompatMode(engineVersion))
                throw new System.OperationCanceledException();
            _settings.TrustedServers.Add(address.Original);
            _settings.Save(_filesDir);
        }

        ResolvedEngine engine;
        try
        {
            engine = await engines.ResolveEngineAsync(engineVersion, progress, ct: ct);
        }
        catch (EngineTooOldException e)
        {
            throw new UserFacingException(e.Message);
        }
        catch (EngineNotAvailableException e)
        {
            throw new UserFacingException(e.Message);
        }

        // Content.
        var content = new ContentManager(_http, Path.Combine(_cacheDir, "content"));
        var bundle = await content.EnsureContentAsync(address, info, progress, ct);

        var launchDir = Path.Combine(_filesDir, "launch");
        Directory.CreateDirectory(launchDir);
        var bundlePath = Path.Combine(launchDir, "content.json");
        ContentManager.SaveBundle(bundle, bundlePath);

        var username = login?.Username ?? (string.IsNullOrWhiteSpace(_settings.GuestName) ? null : _settings.GuestName);
        var uiScale = _settings.UiScale > 0 ? _settings.UiScale : defaultUiScale;
        var cvars = new List<string>(CompatShims.AndroidCVars)
        {
            $"display.uiScale={uiScale.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
        };

        var spec = new GameLaunchSpec
        {
            EngineDir = engine.Dir,
            EngineSource = engine.Source,
            ContentBundlePath = bundlePath,
            EngineArgs = EngineLoader.BuildEngineArgs(address, info, username, cvars),
            Environment = EngineLoader.BuildAuthEnvironment(login, info),
        };

        // The spec may contain the auth token: it lives in app-private storage and the game deletes it after reading.
        var specPath = Path.Combine(launchDir, "spec.json");
        await File.WriteAllTextAsync(specPath, JsonSerializer.Serialize(spec), ct);
        return specPath;
    }
}

public sealed class UserFacingException(string message) : Exception(message);
