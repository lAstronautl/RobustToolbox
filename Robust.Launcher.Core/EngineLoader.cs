using System.Reflection;
using System.Runtime.Loader;
using Robust.LoaderApi;

namespace Robust.Launcher.Core;

/// <summary>
/// Everything the game process needs to start the engine. Serialized by the launcher, read by the game process.
/// </summary>
public sealed class GameLaunchSpec
{
    public string EngineDir { get; set; } = "";
    public string ContentBundlePath { get; set; } = "";
    public string[] EngineArgs { get; set; } = Array.Empty<string>();
    public Dictionary<string, string> Environment { get; set; } = new();
}

/// <summary>
/// Loads a downloaded engine build and runs it through its <see cref="ILoaderEntryPoint"/>,
/// the same way the official launcher's loader does.
/// </summary>
public static class EngineLoader
{
    public static string[] BuildEngineArgs(ServerAddress address, ServerInfo info, string? username,
        IEnumerable<string>? extraCVars = null)
    {
        var args = new List<string>
        {
            "--launcher",
            "--connect-address", address.GetConnectAddress(info),
            "--ss14-address", address.Original,
        };

        if (!string.IsNullOrEmpty(username))
        {
            args.Add("--username");
            args.Add(username);
        }

        var build = info.Build;
        if (build != null)
        {
            void CVar(string name, string? value)
            {
                if (value == null) return;
                args.Add("--cvar");
                args.Add($"{name}={value}");
            }

            CVar("build.fork_id", build.ForkId);
            CVar("build.version", build.Version);
            CVar("build.engine_version", build.EngineVersion);
            CVar("build.manifest_hash", build.ManifestHash);
        }

        if (extraCVars != null)
        {
            foreach (var cvar in extraCVars)
            {
                args.Add("--cvar");
                args.Add(cvar);
            }
        }

        return args.ToArray();
    }

    /// <summary>
    /// Auth environment variables read by the engine's AuthManager.
    /// </summary>
    public static Dictionary<string, string> BuildAuthEnvironment(LoginInfo? login, ServerInfo info,
        string authServer = AuthApi.DefaultAuthServer)
    {
        var env = new Dictionary<string, string>();
        if (login == null || info.Auth?.Mode == "Disabled")
            return env;

        env["ROBUST_AUTH_USERID"] = login.UserId.ToString();
        env["ROBUST_AUTH_TOKEN"] = login.Token;
        env["ROBUST_AUTH_SERVER"] = authServer;
        if (!string.IsNullOrEmpty(info.Auth?.PublicKey))
            env["ROBUST_AUTH_PUBKEY"] = info.Auth!.PublicKey!;
        return env;
    }

    /// <summary>
    /// Runs the engine. Blocks until the game exits. Must be called on the thread that should own the window.
    /// </summary>
    public static void Run(GameLaunchSpec spec, IRedialApi? redial = null)
    {
        foreach (var (k, v) in spec.Environment)
            System.Environment.SetEnvironmentVariable(k, v);

        var engineDir = Path.GetFullPath(spec.EngineDir);

        // Framework assemblies for the content sandbox verifier (see AssemblyTypeChecker).
        var frameworkDir = Path.Combine(engineDir, "framework");
        if (Directory.Exists(frameworkDir) && string.IsNullOrEmpty(typeof(int).Assembly.Location))
            System.Environment.SetEnvironmentVariable("ROBUST_FRAMEWORK_DIR", frameworkDir);

        // Resolve the engine's managed dependencies from its install directory.
        // Robust.LoaderApi must stay the one we were compiled against, so it is never loaded from there.
        AssemblyLoadContext.Default.Resolving += (ctx, name) =>
        {
            if (name.Name == null || name.Name == "Robust.LoaderApi")
                return null;
            // Never load framework assemblies from the engine package; those come from the app.
            var path = Path.Combine(engineDir, name.Name + ".dll");
            return File.Exists(path) ? ctx.LoadFromAssemblyPath(path) : null;
        };

        var clientAsm = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(engineDir, "Robust.Client.dll"));
        var attr = clientAsm.GetCustomAttribute<LoaderEntryPointAttribute>()
                   ?? throw new InvalidOperationException("Robust.Client has no LoaderEntryPoint.");
        var entry = (ILoaderEntryPoint)Activator.CreateInstance(attr.LoaderEntryPointType)!;

        var engineFiles = new DirectoryFileApi(engineDir);
        var content = ContentManager.LoadBundle(spec.ContentBundlePath).OpenFileApi();

        entry.Main(new MainArgs(spec.EngineArgs, engineFiles, redial, new[] { new ApiMount(content, "/") }));
    }

    private sealed class MainArgs(string[] args, IFileApi fileApi, IRedialApi? redial, IEnumerable<ApiMount> mounts)
        : IMainArgs
    {
        public string[] Args { get; } = args;
        public IFileApi FileApi { get; } = fileApi;
        public IRedialApi? RedialApi { get; } = redial;
        public IEnumerable<ApiMount>? ApiMounts { get; } = mounts;
    }
}
