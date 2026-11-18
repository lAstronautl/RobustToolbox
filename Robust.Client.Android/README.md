# Robust Android (лаунчер + клиент)

Android-приложение, которое показывает серверы SS14, входит в аккаунт, скачивает движок и контент сервера и запускает игру с сенсорным управлением.

## Как это устроено

```
APK (Robust.Client.Android)
├─ Лаунчер (MainActivity)      серверы из хаба, избранное, аккаунт, настройки
├─ GameActivity (процесс :game) SDLActivity → libmain.so → GameHost → EngineLoader
├─ Нативные .so                SDL3, OpenAL Soft, FreeType, libsodium, zstd
└─ Robust.Launcher.Core        хаб, авторизация, загрузка движка и контента

Скачивается во время работы:
├─ Движок   релизы этого репозитория: android-engine-v<версия>/Robust.Client_android.zip
└─ Контент  с самого сервера (ACZ-манифест /manifest.txt + /download или zip)
```

- **Движок в APK не вшит.** Лаунчер берёт `engine_version` из `/info` сервера и скачивает Android-сборку **этой** версии из релизов форка. Подключиться можно к любому серверу, для версии движка которого есть релиз.
- **Каждая игра — новый процесс `:game`.** Движок нельзя запустить дважды в одном процессе. При выходе из игры процесс завершается, лаунчер продолжает работать.
- **Песочница контента включена.** DLL фреймворка для проверщика лежат в пакете движка (`framework/`), потому что внутри APK у них нет пути на диске.
- **Управление.** Слева джойстик (W/A/S/D). Справа кнопки на стандартных клавишах: Z — использовать, E — активировать, X — сменить руку, Q — бросить, H — отпустить, I — инвентарь. Есть залипающие Shift, Ctrl и Alt.
  - Тап — ЛКМ, долгое нажатие — ПКМ.
  - Перетаскивание — ЛКМ с удержанием, два пальца — прокрутка.
  - Сверху кнопки Esc, «Чат» (T), Tab и зум (−/+). Кнопка «◐» прячет всё управление.
  - Кнопка «Назад» работает как Esc.
  - Физические клавиатура и мышь работают напрямую.

## Изменения движка для Android

Это проверки во время выполнения (`OperatingSystem.IsAndroid()`), поэтому один и тот же пакет движка работает и на ПК:

- GLES3/GLES2 и SDL3 без отдельного оконного потока (`Clyde.cs`);
- пути к пользовательским данным (`UserDataDir.cs`) и к исполняемому файлу (`PathHelpers.cs`);
- имена нативных библиотек (`ZStd.cs`, `DllMapHelper.cs`, `ClientDllMap.cs`);
- запасной менеджер системных шрифтов вместо fontconfig;
- каталог фреймворка для песочницы (`AssemblyTypeChecker.cs`, `ROBUST_FRAMEWORK_DIR`);
- исправление: headless-клиент в релизной сборке больше не обращается к OpenGL (`ResourceCache.Preload.cs`).

## Сборка

Проще всего через GitHub Actions: workflow **Android** (`.github/workflows/android.yml`). С галочкой **publish** он создаёт релиз `android-engine-v<версия>` с движком и APK.

Локально (Linux, .NET 10, workload `android`, Android SDK/NDK):

```bash
ANDROID_NDK_HOME=$ANDROID_HOME/ndk/27.2.12479018 Robust.Client.Android/natives/build-natives.sh
Robust.Client.Android/engine/package-engine.sh release
dotnet publish Robust.Client.Android -c Release -p:AndroidSdkDirectory=$ANDROID_HOME -o apk-out
```

## Проверка без телефона

`Robust.Launcher.Cli` делает то же самое, что Android-лаунчер, но на ПК:

```bash
KEEP_NATIVES=linux-x64 Robust.Client.Android/engine/package-engine.sh /tmp/engine
dotnet run --project Robust.Launcher.Cli -c Release -- play 127.0.0.1:1212 --engine-zip /tmp/engine/Robust.Client_android.zip --headless
```
