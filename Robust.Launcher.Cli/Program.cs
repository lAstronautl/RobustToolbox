using Robust.Launcher.Core;

// Usage:
//   Robust.Launcher.Cli hub
//   Robust.Launcher.Cli info <address>
//   Robust.Launcher.Cli play <address> [--engine-zip <zip>] [--cache <dir>] [--headless] [--username <name>]
var cache = GetOpt("--cache") ?? Path.Combine(Path.GetTempPath(), "robust-launcher-cli");
using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("RobustLauncherCli/1.0");
var api = new ServerApi(http);

switch (args.ElementAtOrDefault(0))
{
    case "hub":
    {
        var servers = await api.GetHubServersAsync();
        Console.WriteLine($"{servers.Count} servers");
        foreach (var s in servers.Take(15))
            Console.WriteLine($"  {s.StatusData?.Players,3}/{s.StatusData?.SoftMaxPlayers,-3} {s.StatusData?.Name} [{s.Address}]");
        return 0;
    }
    case "info":
    {
        var addr = ServerAddress.Parse(args[1]);
        var info = await api.GetInfoAsync(addr);
        var status = await api.GetStatusAsync(addr);
        Console.WriteLine($"{status.Name}: {status.Players}/{status.SoftMaxPlayers}");
        Console.WriteLine($"engine {info.Build?.EngineVersion} fork {info.Build?.ForkId} acz {info.Build?.Acz} auth {info.Auth?.Mode}");
        return 0;
    }
    case "play":
    {
        var addr = ServerAddress.Parse(args[1]);
        var info = await api.GetInfoAsync(addr);
        var engineVersion = info.Build?.EngineVersion ?? throw new Exception("no engine version");
        Console.WriteLine($"Server engine: {engineVersion}");

        var engines = new EngineManager(http, cache);
        if (GetOpt("--engine-zip") is { } zip)
            EngineManager.InstallFromZip(zip, engines.GetInstallDir(engineVersion));
        var progress = new Progress<DownloadProgress>(p => Console.Write($"\r{p.Stage}: {p.Done}/{p.Total}      "));
        var engineDir = await engines.EnsureEngineAsync(engineVersion, progress);
        Console.WriteLine($"\nEngine: {engineDir}");

        var content = new ContentManager(http, Path.Combine(cache, "content"));
        var bundle = await content.EnsureContentAsync(addr, info, progress);
        var bundlePath = Path.Combine(cache, "bundle.json");
        ContentManager.SaveBundle(bundle, bundlePath);
        Console.WriteLine($"\nContent files: {bundle.Files?.Count ?? -1}");

        var engineArgs = EngineLoader.BuildEngineArgs(addr, info, GetOpt("--username") ?? "AndroidTest").ToList();
        if (args.Contains("--headless"))
            engineArgs.Add("--headless");

        EngineLoader.Run(new GameLaunchSpec
        {
            EngineDir = engineDir,
            ContentBundlePath = bundlePath,
            EngineArgs = engineArgs.ToArray(),
            Environment = new() { ["ROBUST_DISABLE_SANDBOX"] = Environment.GetEnvironmentVariable("ROBUST_DISABLE_SANDBOX") ?? "0" },
        });
        return 0;
    }
    default:
        Console.WriteLine("commands: hub | info <addr> | play <addr> [--engine-zip z] [--headless]");
        return 1;
}

string? GetOpt(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}
