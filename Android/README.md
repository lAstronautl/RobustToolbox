# Движок для SS14.Android

Ветка **SS14.Android** — движок RobustToolbox с правками для Android. Его скачивает лаунчер [SS14.Android](https://github.com/lAstronautl/SS14.Launcher/tree/SS14.Android). Самого лаунчера здесь нет.

## Что публикуется

Workflow **Android engine** (`.github/workflows/android.yml`) при push создаёт релиз `android-engine-v<версия движка>`, если такого ещё нет:

- `Robust.Client_android.zip` (+ `.sha256`) — движок (managed DLL), `Resources/`, `runtimeconfig`. Среда .NET и нативные библиотеки в него не входят: они поставляются с лаунчером.
- `Robust.Client_android-aot-arm64.zip` — предкомпилированный код этого же zip для быстрого запуска. Собирается под версию .NET из `runtime-versions.txt`; эта версия должна совпадать с той, что зафиксирована в лаунчере.

## Правки движка

Все правки — это проверки во время выполнения (`OperatingSystem.IsAndroid()`), поэтому на ПК движок работает как обычно:

- GLES и SDL3 без отдельного оконного потока (`Clyde.cs`);
- пути к данным и к исполняемому файлу (`UserDataDir.cs`, `PathHelpers.cs`);
- имена нативных библиотек (`ZStd.cs`, `DllMapHelper.cs`, `ClientDllMap.cs`);
- запасной менеджер системных шрифтов;
- каталог фреймворка для песочницы (`AssemblyTypeChecker.cs`, `ROBUST_FRAMEWORK_DIR`);
- ввод мыши в SDL3, открытие ссылок, логи без цветовых кодов (ANSI), предзагрузка ресурсов, настройки сборщика мусора (GC).

## Локально

```bash
Android/package-engine.sh release
ANDROID_NDK_HOME=$ANDROID_HOME/ndk/27.2.12479018 Android/aot-compile.sh 10.0.15 aot <распакованный движок> -- <распакованный движок>
```
