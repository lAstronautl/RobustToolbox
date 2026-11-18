using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Widget;
using Robust.Launcher.Core;

namespace Robust.Client.Android.Launcher;

[Activity(Label = "@string/app_name", MainLauncher = true, Exported = true, Theme = "@style/LauncherTheme",
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.KeyboardHidden,
    WindowSoftInputMode = SoftInput.AdjustResize)]
public sealed class MainActivity : Activity
{
    private enum Tab { Servers, Favorites, Account, Settings }

    private static readonly HttpClient Http = CreateHttp();

    private LauncherSettings _settings = null!;
    private FrameLayout _content = null!;
    private readonly Dictionary<Tab, Button> _tabButtons = new();
    private Tab _tab = Tab.Servers;

    private List<HubServerEntry> _hubServers = new();
    private string _filter = "";
    private CancellationTokenSource? _connectCts;

    private static HttpClient CreateHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("RobustAndroidLauncher/0.1");
        return http;
    }

    private string FilesPath => FilesDir!.AbsolutePath;
    private string CachePath => (GetExternalFilesDir(null) ?? FilesDir!).AbsolutePath;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        _settings = LauncherSettings.Load(FilesPath);
        CleanupOldRuntimeLocation();

        var root = Ui.Column(this);
        root.SetBackgroundColor(Ui.Bg);

        var header = Ui.Row(this);
        header.SetPadding(Ui.Dp(this, 16), Ui.Dp(this, 14), Ui.Dp(this, 16), Ui.Dp(this, 6));
        header.AddView(Ui.Label(this, GetString(Resource.String.app_name), 22, bold: true), Ui.Fill(1));
        root.AddView(header);

        var tabs = Ui.Row(this);
        tabs.SetPadding(Ui.Dp(this, 10), 0, Ui.Dp(this, 10), Ui.Dp(this, 6));
        foreach (var (tab, title) in new[]
                 {
                     (Tab.Servers, "Серверы"), (Tab.Favorites, "Избранное"), (Tab.Account, "Аккаунт"),
                     (Tab.Settings, "Настройки")
                 })
        {
            var b = Ui.Button(this, title, () => ShowTab(tab));
            var lp = Ui.Fill(1);
            lp.SetMargins(Ui.Dp(this, 3), 0, Ui.Dp(this, 3), 0);
            tabs.AddView(b, lp);
            _tabButtons[tab] = b;
        }

        root.AddView(tabs);

        _content = new FrameLayout(this);
        root.AddView(_content, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1));
        SetContentView(root);

        ShowTab(_settings.Login == null && _settings.Favorites.Count == 0 ? Tab.Servers : Tab.Favorites);
        ShowLastCrashIfAny();
    }

    protected override void OnResume()
    {
        base.OnResume();
        ShowLastCrashIfAny();
    }

    /// <summary>0.1.0 stored .NET runtimes on external storage, where native code cannot be loaded.</summary>
    private void CleanupOldRuntimeLocation()
    {
        try
        {
            var old = System.IO.Path.Combine(CachePath, "runtimes");
            if (CachePath != FilesPath && Directory.Exists(old))
                Directory.Delete(old, true);
        }
        catch
        {
            // Not important.
        }
    }

    private void ShowTab(Tab tab)
    {
        _tab = tab;
        foreach (var (t, b) in _tabButtons)
        {
            b.Background = Ui.Rounded(this, t == tab ? Ui.Accent : Ui.Panel);
            b.SetTextColor(t == tab ? Ui.Bg : Ui.Text);
        }

        _content.RemoveAllViews();
        _content.AddView(tab switch
        {
            Tab.Servers => BuildServersTab(),
            Tab.Favorites => BuildFavoritesTab(),
            Tab.Account => BuildAccountTab(),
            _ => BuildSettingsTab(),
        });
    }

    // ------------------------------------------------------------------ Servers

    private View BuildServersTab()
    {
        var col = Ui.Column(this, 12);

        var top = Ui.Row(this);
        var search = Ui.Input(this, "Поиск сервера", _filter);
        top.AddView(search, Ui.Fill(1));
        var refresh = Ui.Button(this, "⟳", () => _ = LoadHubAsync(true));
        top.AddView(refresh, Ui.Wrap(8, this));
        col.AddView(top);

        var direct = Ui.Button(this, "Подключиться по адресу…", AskDirectConnect);
        col.AddView(direct, Ui.Fill(0, 8, this));

        var status = Ui.Label(this, "", 13, Ui.Muted);
        col.AddView(status, Ui.Fill(0, 6, this));

        var list = new ListView(this) { DividerHeight = Ui.Dp(this, 6) };
        list.Divider = new global::Android.Graphics.Drawables.ColorDrawable(global::Android.Graphics.Color.Transparent);
        var adapter = new ServerAdapter(this);
        list.Adapter = adapter;
        list.ItemClick += (_, e) => ShowServerDialog(adapter[e.Position]);
        col.AddView(list, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1));

        void Apply()
        {
            var f = _filter.Trim();
            var items = _hubServers
                .Where(s => f.Length == 0
                            || (s.StatusData?.Name ?? "").Contains(f, StringComparison.OrdinalIgnoreCase)
                            || s.Address.Contains(f, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(s => s.StatusData?.Players ?? 0)
                .Select(s => new ServerItem(s.Address, s.StatusData?.Name ?? s.Address, s.StatusData?.Players ?? 0,
                    s.StatusData?.SoftMaxPlayers ?? 0))
                .ToList();
            adapter.SetItems(items);
            status.Text = _hubServers.Count == 0 ? "Загрузка списка серверов…" : $"Серверов: {items.Count}";
        }

        search.TextChanged += (_, _) =>
        {
            _filter = search.Text ?? "";
            Apply();
        };

        _onHubLoaded = Apply;
        Apply();
        if (_hubServers.Count == 0)
            _ = LoadHubAsync(false);
        return col;
    }

    private Action? _onHubLoaded;

    private async Task LoadHubAsync(bool force)
    {
        try
        {
            if (!force && _hubServers.Count > 0)
                return;
            _hubServers = await new ServerApi(Http).GetHubServersAsync(_settings.HubUrl);
        }
        catch (Exception e)
        {
            Toast.MakeText(this, $"Не удалось загрузить список: {e.Message}", ToastLength.Long)!.Show();
        }

        RunOnUiThread(() =>
        {
            if (_tab == Tab.Servers)
                _onHubLoaded?.Invoke();
        });
    }

    private void AskDirectConnect()
    {
        var input = Ui.Input(this, "ss14://адрес:порт");
        var wrap = Ui.Column(this, 16);
        wrap.AddView(input);
        new AlertDialog.Builder(this)
            .SetTitle("Адрес сервера")!
            .SetView(wrap)!
            .SetPositiveButton("Подключиться", (_, _) =>
            {
                var text = input.Text ?? "";
                if (ServerAddress.TryParse(text, out var addr))
                    Connect(addr, text);
                else
                    Toast.MakeText(this, "Неверный адрес", ToastLength.Short)!.Show();
            })!
            .SetNeutralButton("В избранное", (_, _) =>
            {
                var text = input.Text ?? "";
                if (ServerAddress.TryParse(text, out _))
                    AddFavorite(text, text);
            })!
            .SetNegativeButton("Отмена", (_, _) => { })!
            .Show();
    }

    private void ShowServerDialog(ServerItem item)
    {
        var isFav = _settings.Favorites.Any(f => f.Address == item.Address);
        var body = Ui.Column(this, 20);
        body.AddView(Ui.Label(this, item.Address, 13, Ui.Muted));
        var info = Ui.Label(this, "Запрос информации…", 14, Ui.Muted);
        body.AddView(info, Ui.Fill(0, 8, this));

        var dialog = new AlertDialog.Builder(this)
            .SetTitle(item.Name)!
            .SetView(body)!
            .SetPositiveButton("Подключиться", (_, _) =>
            {
                if (ServerAddress.TryParse(item.Address, out var addr))
                    Connect(addr, item.Name);
            })!
            .SetNeutralButton(isFav ? "Убрать из избранного" : "В избранное", (_, _) =>
            {
                if (isFav)
                    RemoveFavorite(item.Address);
                else
                    AddFavorite(item.Address, item.Name);
            })!
            .SetNegativeButton("Закрыть", (_, _) => { })!
            .Show();

        _ = Task.Run(async () =>
        {
            string text;
            try
            {
                var addr = ServerAddress.Parse(item.Address);
                var api = new ServerApi(Http);
                var status = await api.GetStatusAsync(addr);
                var srvInfo = await api.GetInfoAsync(addr);
                var ver = srvInfo.Build?.EngineVersion ?? "?";
                var engines = new EngineManager(Http, CachePath, _settings.EngineReleaseBase);
                var availability = ver == "?" ? EngineAvailability.None : await engines.CheckAvailabilityAsync(ver);
                var engineText = availability switch
                {
                    EngineAvailability.Patched => "✔ Android-сборка",
                    EngineAvailability.Official => "⚠ режим совместимости (без песочницы)",
                    _ => "✘ не поддерживается",
                };
                text = $"Игроков: {status.Players}/{status.SoftMaxPlayers}\n" +
                       (status.Map != null ? $"Карта: {status.Map}\n" : "") +
                       $"Движок: {ver} — {engineText}\n" +
                       $"Вход: {AuthText(srvInfo.Auth?.Mode)}" +
                       (string.IsNullOrWhiteSpace(srvInfo.Description) ? "" : $"\n\n{srvInfo.Description}");
            }
            catch (Exception e)
            {
                text = $"Сервер недоступен: {e.Message}";
            }

            RunOnUiThread(() =>
            {
                if (dialog.IsShowing)
                    info.Text = text;
            });
        });
    }

    private static string AuthText(string? mode) => mode switch
    {
        "Required" => "только с аккаунтом",
        "Disabled" => "без аккаунтов",
        _ => "аккаунт или гость",
    };

    // ------------------------------------------------------------------ Favorites

    private View BuildFavoritesTab()
    {
        var col = Ui.Column(this, 12);
        if (_settings.Favorites.Count == 0)
        {
            col.AddView(Ui.Label(this,
                "Избранных серверов пока нет.\nДобавьте сервер из списка или по адресу.", 15, Ui.Muted));
            col.AddView(Ui.Button(this, "Добавить по адресу…", AskDirectConnect), Ui.Fill(0, 12, this));
            return col;
        }

        var list = new ListView(this) { DividerHeight = Ui.Dp(this, 6) };
        list.Divider = new global::Android.Graphics.Drawables.ColorDrawable(global::Android.Graphics.Color.Transparent);
        var adapter = new ServerAdapter(this);
        adapter.SetItems(_settings.Favorites.Select(f => new ServerItem(f.Address, f.Name, -1, 0)).ToList());
        list.Adapter = adapter;
        list.ItemClick += (_, e) => ShowServerDialog(adapter[e.Position]);
        col.AddView(list, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1));
        col.AddView(Ui.Button(this, "Добавить по адресу…", AskDirectConnect), Ui.Fill(0, 8, this));

        // Refresh player counts in the background.
        _ = Task.Run(async () =>
        {
            var api = new ServerApi(Http);
            var items = new List<ServerItem>();
            foreach (var f in _settings.Favorites.ToList())
            {
                try
                {
                    var s = await api.GetStatusAsync(ServerAddress.Parse(f.Address));
                    items.Add(new ServerItem(f.Address, s.Name ?? f.Name, s.Players, s.SoftMaxPlayers));
                }
                catch
                {
                    items.Add(new ServerItem(f.Address, f.Name, -2, 0));
                }
            }

            RunOnUiThread(() => adapter.SetItems(items));
        });

        return col;
    }

    private void AddFavorite(string address, string name)
    {
        if (_settings.Favorites.All(f => f.Address != address))
        {
            _settings.Favorites.Add(new FavoriteServer { Address = address, Name = name });
            _settings.Save(FilesPath);
        }

        Toast.MakeText(this, "Добавлено в избранное", ToastLength.Short)!.Show();
        if (_tab == Tab.Favorites) ShowTab(Tab.Favorites);
    }

    private void RemoveFavorite(string address)
    {
        _settings.Favorites.RemoveAll(f => f.Address == address);
        _settings.Save(FilesPath);
        if (_tab == Tab.Favorites) ShowTab(Tab.Favorites);
    }

    // ------------------------------------------------------------------ Account

    private View BuildAccountTab()
    {
        var scroll = new ScrollView(this);
        var col = Ui.Column(this, 16);
        scroll.AddView(col);

        if (_settings.Login is { } login)
        {
            col.AddView(Ui.Label(this, "Вы вошли как", 14, Ui.Muted));
            col.AddView(Ui.Label(this, login.Username, 22, bold: true));
            col.AddView(Ui.Label(this, $"Вход действителен до {login.ExpireTime.LocalDateTime:g}", 13, Ui.Muted),
                Ui.Fill(0, 4, this));
            col.AddView(Ui.Button(this, "Выйти", () => _ = LogoutAsync()), Ui.Fill(0, 16, this));
            return scroll;
        }

        col.AddView(Ui.Label(this, "Аккаунт Space Station 14", 18, bold: true));
        col.AddView(Ui.Label(this, "Тот же аккаунт, что и в обычном лаунчере.", 13, Ui.Muted), Ui.Fill(0, 4, this));
        var user = Ui.Input(this, "Логин или e-mail");
        var pass = Ui.Input(this, "Пароль", password: true);
        var tfa = Ui.Input(this, "Код двухфакторной аутентификации");
        tfa.Visibility = ViewStates.Gone;
        var error = Ui.Label(this, "", 14, Ui.Bad);
        col.AddView(user, Ui.Fill(0, 12, this));
        col.AddView(pass, Ui.Fill(0, 8, this));
        col.AddView(tfa, Ui.Fill(0, 8, this));
        col.AddView(error, Ui.Fill(0, 8, this));

        Button? loginBtn = null;
        loginBtn = Ui.Button(this, "Войти", async () =>
        {
            loginBtn!.Enabled = false;
            error.Text = "";
            try
            {
                var res = await new AuthApi(Http).LoginAsync(user.Text ?? "", pass.Text ?? "",
                    tfa.Visibility == ViewStates.Visible ? tfa.Text : null);
                if (res.Success)
                {
                    _settings.Login = res.Info;
                    _settings.Save(FilesPath);
                    ShowTab(Tab.Account);
                    return;
                }

                if (res.Code == AuthDenyCode.TfaRequired)
                {
                    tfa.Visibility = ViewStates.Visible;
                    error.Text = "Введите код двухфакторной аутентификации.";
                }
                else
                {
                    error.Text = string.Join("\n", res.Errors);
                }
            }
            catch (Exception e)
            {
                error.Text = $"Ошибка: {e.Message}";
            }
            finally
            {
                loginBtn!.Enabled = true;
            }
        }, primary: true);
        col.AddView(loginBtn, Ui.Fill(0, 12, this));

        col.AddView(Ui.Label(this, "Или играйте гостем", 16, bold: true), Ui.Fill(0, 28, this));
        col.AddView(Ui.Label(this, "Работает на серверах, где вход в аккаунт не обязателен.", 13, Ui.Muted),
            Ui.Fill(0, 4, this));
        var guest = Ui.Input(this, "Имя гостя (необязательно)", _settings.GuestName);
        guest.TextChanged += (_, _) =>
        {
            _settings.GuestName = guest.Text ?? "";
            _settings.Save(FilesPath);
        };
        col.AddView(guest, Ui.Fill(0, 8, this));
        return scroll;
    }

    private async Task LogoutAsync()
    {
        if (_settings.Login is { } login)
        {
            try
            {
                await new AuthApi(Http).LogoutAsync(login.LoginToken);
            }
            catch
            {
                // Logging out locally is what matters.
            }
        }

        _settings.Login = null;
        _settings.Save(FilesPath);
        ShowTab(Tab.Account);
    }

    // ------------------------------------------------------------------ Settings

    private View BuildSettingsTab()
    {
        var scroll = new ScrollView(this);
        var col = Ui.Column(this, 16);
        scroll.AddView(col);

        col.AddView(Ui.Label(this, "Масштаб интерфейса игры", 16, bold: true));
        var scales = new[] { 0f, 1f, 1.25f, 1.5f, 1.75f, 2f, 2.5f };
        var scaleNames = scales.Select(s => s == 0 ? $"Авто ({DefaultUiScale():0.##})" : $"{s:0.##}×").ToArray();
        var spinner = new Spinner(this)
        {
            Adapter = new ArrayAdapter<string>(this, global::Android.Resource.Layout.SimpleSpinnerDropDownItem, scaleNames)
        };
        spinner.SetSelection(Math.Max(0, Array.IndexOf(scales, _settings.UiScale)));
        spinner.Background = Ui.Rounded(this, Ui.Panel, 8);
        spinner.ItemSelected += (_, e) =>
        {
            _settings.UiScale = scales[e.Position];
            _settings.Save(FilesPath);
        };
        col.AddView(spinner, Ui.Fill(0, 6, this));

        col.AddView(Ui.Label(this, "Сенсорное управление", 16, bold: true), Ui.Fill(0, 20, this));
        var touch = new Switch(this) { Text = "Показывать кнопки и джойстик", Checked = _settings.TouchControls };
        touch.SetTextColor(Ui.Text);
        touch.CheckedChange += (_, e) =>
        {
            _settings.TouchControls = e.IsChecked;
            _settings.Save(FilesPath);
        };
        col.AddView(touch, Ui.Fill(0, 6, this));

        var opacityLabel = Ui.Label(this, $"Прозрачность кнопок: {_settings.TouchOpacity}%", 14, Ui.Muted);
        col.AddView(opacityLabel, Ui.Fill(0, 10, this));
        var opacity = new SeekBar(this) { Max = 100, Progress = _settings.TouchOpacity };
        opacity.ProgressChanged += (_, e) =>
        {
            _settings.TouchOpacity = Math.Max(15, e.Progress);
            opacityLabel.Text = $"Прозрачность кнопок: {_settings.TouchOpacity}%";
            _settings.Save(FilesPath);
        };
        col.AddView(opacity);

        var sizeLabel = Ui.Label(this, $"Размер кнопок: {_settings.TouchScale:0.0}×", 14, Ui.Muted);
        col.AddView(sizeLabel, Ui.Fill(0, 10, this));
        var size = new SeekBar(this) { Max = 10, Progress = (int)Math.Round((_settings.TouchScale - 0.5f) * 10) };
        size.ProgressChanged += (_, e) =>
        {
            _settings.TouchScale = 0.5f + e.Progress / 10f;
            sizeLabel.Text = $"Размер кнопок: {_settings.TouchScale:0.0}×";
            _settings.Save(FilesPath);
        };
        col.AddView(size);

        col.AddView(Ui.Label(this, "Источник сборок движка", 16, bold: true), Ui.Fill(0, 20, this));
        col.AddView(Ui.Label(this, "Откуда скачивать Android-сборки движка (релизы GitHub).", 13, Ui.Muted));
        var engineUrl = Ui.Input(this, EngineManager.DefaultReleaseBase, _settings.EngineReleaseBase);
        engineUrl.TextChanged += (_, _) =>
        {
            _settings.EngineReleaseBase = string.IsNullOrWhiteSpace(engineUrl.Text)
                ? EngineManager.DefaultReleaseBase
                : engineUrl.Text!.Trim();
            _settings.Save(FilesPath);
        };
        col.AddView(engineUrl, Ui.Fill(0, 6, this));

        col.AddView(Ui.Label(this, "Хаб серверов", 16, bold: true), Ui.Fill(0, 20, this));
        var hubUrl = Ui.Input(this, ServerApi.DefaultHubUrl, _settings.HubUrl);
        hubUrl.TextChanged += (_, _) =>
        {
            _settings.HubUrl = string.IsNullOrWhiteSpace(hubUrl.Text) ? ServerApi.DefaultHubUrl : hubUrl.Text!.Trim();
            _settings.Save(FilesPath);
        };
        col.AddView(hubUrl, Ui.Fill(0, 6, this));

        col.AddView(Ui.Button(this, "Очистить кэш (движки, среды .NET, контент)", () =>
        {
            try
            {
                foreach (var p in new[]
                         {
                             System.IO.Path.Combine(CachePath, "engines"), System.IO.Path.Combine(CachePath, "content"),
                             System.IO.Path.Combine(FilesPath, "runtimes"),
                         })
                {
                    if (Directory.Exists(p)) Directory.Delete(p, true);
                }

                Toast.MakeText(this, "Кэш очищен", ToastLength.Short)!.Show();
            }
            catch (Exception e)
            {
                Toast.MakeText(this, e.Message, ToastLength.Long)!.Show();
            }
        }), Ui.Fill(0, 24, this));

        return scroll;
    }

    /// <summary>Phones have small, dense screens: scale UI so the game is readable at ~720p logical height.</summary>
    private float DefaultUiScale()
    {
        var m = Resources!.DisplayMetrics!;
        var shortSide = Math.Min(m.WidthPixels, m.HeightPixels);
        return Math.Clamp(MathF.Round(shortSide / 720f * 4) / 4, 1f, 2.5f);
    }

    // ------------------------------------------------------------------ Connecting

    private void Connect(ServerAddress address, string name)
    {
        _connectCts?.Cancel();
        var cts = _connectCts = new CancellationTokenSource();

        var body = Ui.Column(this, 20);
        var stage = Ui.Label(this, "Подготовка…", 15);
        var bar = new ProgressBar(this, null, global::Android.Resource.Attribute.ProgressBarStyleHorizontal)
            { Indeterminate = true, Max = 1000 };
        body.AddView(stage);
        body.AddView(bar, Ui.Fill(0, 12, this));

        var dialog = new AlertDialog.Builder(this)
            .SetTitle($"Подключение: {name}")!
            .SetView(body)!
            .SetCancelable(false)!
            .SetNegativeButton("Отмена", (_, _) => cts.Cancel())!
            .Show();

        var progress = new Progress<DownloadProgress>(p =>
        {
            stage.Text = p.Total > 0 ? $"{p.Stage}: {FormatAmount(p)}" : p.Stage;
            bar.Indeterminate = p.Total <= 0;
            if (p.Total > 0) bar.Progress = (int)(p.Done * 1000 / p.Total);
        });

        var defaultScale = DefaultUiScale();
        _ = Task.Run(async () =>
        {
            try
            {
                var connector = new Connector(Http, _settings, FilesPath, CachePath);
                var spec = await connector.PrepareAsync(address, defaultScale, ConfirmCompatModeAsync, progress, cts.Token);
                RunOnUiThread(() =>
                {
                    dialog.Dismiss();
                    StartGame(spec);
                });
            }
            catch (System.OperationCanceledException) when (cts.IsCancellationRequested)
            {
                RunOnUiThread(dialog.Dismiss);
            }
            catch (Exception e)
            {
                global::Android.Util.Log.Error("Launcher", e.ToString());
                RunOnUiThread(() =>
                {
                    dialog.Dismiss();
                    new AlertDialog.Builder(this)
                        .SetTitle("Не удалось подключиться")!
                        .SetMessage(e is UserFacingException ? e.Message : $"{e.GetType().Name}: {e.Message}")!
                        .SetPositiveButton("OK", (_, _) => { })!
                        .Show();
                });
            }
        });
    }

    private Task<bool> ConfirmCompatModeAsync(string engineVersion)
    {
        var tcs = new TaskCompletionSource<bool>();
        RunOnUiThread(() =>
        {
            new AlertDialog.Builder(this)
                .SetTitle("Режим совместимости")!
                .SetMessage(
                    $"Для движка {engineVersion} нет Android-сборки. Можно запустить официальную сборку этой версии, " +
                    "адаптированную под Android.\n\n" +
                    "В этом режиме песочница отключена: код сервера получит полный доступ к приложению " +
                    "(в том числе к сохранённому входу в аккаунт). Подключайтесь так только к серверам, которым доверяете.\n\n" +
                    "Возможны сбои: такие сборки не проверялись на Android.")!
                .SetCancelable(false)!
                .SetPositiveButton("Доверяю, запустить", (_, _) => tcs.TrySetResult(true))!
                .SetNegativeButton("Отмена", (_, _) => tcs.TrySetResult(false))!
                .Show();
        });
        return tcs.Task;
    }

    private static string FormatAmount(DownloadProgress p)
    {
        // Byte counts for engine/zip downloads, file counts for manifest downloads.
        if (p.Total > 100_000)
            return $"{p.Done / 1048576.0:0.0} / {p.Total / 1048576.0:0.0} МБ";
        return $"{p.Done} / {p.Total}";
    }

    private string CrashFile => System.IO.Path.Combine(FilesPath, "last-crash.txt");

    /// <summary>Extracts the game host assembly from the APK (cheap, done on every launch so updates apply).</summary>
    private string ExtractGameHost()
    {
        var dir = Directory.CreateDirectory(System.IO.Path.Combine(FilesPath, "gamehost")).FullName;
        var path = System.IO.Path.Combine(dir, "Robust.Android.GameHost.dll");
        using var src = Assets!.Open("gamehost/Robust.Android.GameHost.dll");
        using var dst = File.Create(path);
        src.CopyTo(dst);
        return path;
    }

    private void StartGame(GameLaunch launch)
    {
        // Must match robust.android.GameActivity (Java/robust/android/GameActivity.java).
        var intent = new Intent();
        intent.SetClassName(this, "robust.android.GameActivity");
        intent.PutExtra("runtime_dir", launch.RuntimeDir);
        intent.PutExtra("host_dll", ExtractGameHost());
        intent.PutExtra("spec", launch.SpecPath);
        intent.PutExtra("crash", CrashFile);
        intent.PutExtra("runtime_config", launch.RuntimeConfig);
        intent.PutExtra("touch", _settings.TouchControls);
        intent.PutExtra("touch_opacity", _settings.TouchOpacity);
        intent.PutExtra("touch_scale", _settings.TouchScale);
        StartActivity(intent);
    }

    private void ShowLastCrashIfAny()
    {
        var path = CrashFile;
        if (!File.Exists(path))
            return;

        var text = File.ReadAllText(path);
        File.Delete(path);
        new AlertDialog.Builder(this)
            .SetTitle("Игра завершилась с ошибкой")!
            .SetMessage(text.Length > 4000 ? text[..4000] + "…" : text)!
            .SetPositiveButton("OK", (_, _) => { })!
            .SetNeutralButton("Копировать", (_, _) =>
            {
                var cm = (ClipboardManager)GetSystemService(ClipboardService)!;
                cm.PrimaryClip = ClipData.NewPlainText("crash", text);
            })!
            .Show();
    }
}
