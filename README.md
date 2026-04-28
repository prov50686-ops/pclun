# PcLun — лаунчер Minecraft 1.16.5 + OptiFine для Windows

> Чёрно-белый минималистичный лаунчер на русском, оптимизированный под слабые ПК.
> **Ultra FPS Edition.**

![status](https://github.com/prov50686-ops/pclun/actions/workflows/build-windows.yml/badge.svg)

## Что умеет

- Запускает **Minecraft 1.16.5** с **OptiFine HD U G8** в один клик.
- Сам скачивает Java 8 (Adoptium Temurin), клиент игры, библиотеки, ассеты, нативы.
- Сам ставит OptiFine (скачивает с optifine.net и запускает официальный installer).
- Применяет агрессивный **профиль Ultra FPS** для слабых ПК:
  - render distance = 4, particles = minimal, без облаков, без AO, без энтити-теней.
  - OptiFine: Fast Render, Fast Math, Dynamic FPS, Smart Animations off, Lazy Chunk Loading.
  - JVM: G1GC + тонкие флаги (G1NewSizePercent, MaxGCPauseMillis=50, ParallelRefProcEnabled и т.д.).
  - Авто-подбор `-Xmx` по объёму системной RAM.
- **Два режима входа**: офлайн (любой ник) или Microsoft Device Code (премиум-аккаунт).
- **Онлайн-счётчик** через свой FastAPI-бэкенд: показывает реальное число игроков, запустивших лаунчер за последние 5 минут.
- Чёрно-белая тема, шрифт Inter.

## Скачать

[Releases →](https://github.com/prov50686-ops/pclun/releases) (готовый `PcLun.exe`, single-file, ~70 МБ).

Каждый push в `main` собирает свежий `.exe` в [Actions → Build Windows](https://github.com/prov50686-ops/pclun/actions/workflows/build-windows.yml).

## Сборка из исходников

Нужен **.NET 8 SDK**.

```powershell
git clone https://github.com/prov50686-ops/pclun
cd pclun
dotnet publish src/Launcher/Launcher.csproj -c Release -r win-x64 ^
    --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:EnableCompressionInSingleFile=true ^
    -o publish/win-x64
publish\win-x64\PcLun.exe
```

## Структура

```
src/Launcher/                 # Avalonia UI (.NET 8) — сам лаунчер
    Services/
        Paths.cs              # пути к рантайму
        SystemInfo.cs         # детекция RAM
        Http.cs               # HTTP + SHA1
        MinecraftDownloader.cs# скачка vanilla 1.16.5 (libs, assets, natives)
        JavaManager.cs        # авто-скачка Adoptium Temurin JRE 8u
        OptifineInstaller.cs  # скачка OptiFine + headless installer
        OptimizationProfile.cs# JVM флаги, options.txt, optionsof.txt
        GameLauncher.cs       # сборка classpath и финальной команды
        AuthService.cs        # offline + Microsoft Device Code Flow
        OnlineService.cs      # пинг бэкенда онлайна
    Views/MainWindow.axaml    # главное окно
    ViewModels/MainViewModel.cs
    Styles/Theme.axaml        # чёрно-белая палитра

backend/                      # FastAPI online-counter, деплой на Fly.io
    main.py
    pyproject.toml
```

## Бэкенд

Развёрнут на Fly.io: `https://pclun-online-trloyqbz.fly.dev/`

- `POST /heartbeat {"username": "..."}` → `{"online": N, "total": M}` — пинг от лаунчера.
- `GET /online` → `{"online": N, "total": M}` — публичная статистика.

## Папки данных лаунчера

Windows: `%APPDATA%\PcLun\`

- `runtime/jre8/` — встроенная Java
- `minecraft/` — `.minecraft` (versions, libraries, assets, saves, mods)
- `accounts.json` — сохранённый аккаунт
- `launcher.log`

## Известные ограничения

- Только **Windows x64** в Releases (UI на Avalonia собирается и для Linux/macOS, но скрипты сборки заточены под Windows).
- OptiFine скачивается с `optifine.net` через парсинг страницы `adloadx`. Если сайт изменит формат — нужен фоллбэк (есть резервный mirror).
- Microsoft auth использует publi-client-id Mojang и работает по Device Code Flow — открывает браузер, ты вводишь короткий код, лаунчер получает токен.

## Лицензия

MIT — см. [LICENSE](./LICENSE).

OptiFine, Minecraft и связанные товарные знаки принадлежат их правообладателям. Этот лаунчер — независимый open-source проект.
