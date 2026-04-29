# История изменений

Все заметные изменения в проекте документируются здесь. Формат вдохновлён [Keep a Changelog](https://keepachangelog.com/ru/), нумерация — [SemVer](https://semver.org/lang/ru/).

---

## [0.4.0] — 2026-04-29 — Pro Edition 🌟

Самый большой релиз: **31 новая фича** одной волной. Лаунчер из «1.16.5 + OptiFine» превращается в универсальный.

### ✨ Новое

#### Геймплей
- 🗂 **Мульти-инстансы** — отдельный `.minecraft` под каждый профиль (vanilla / OptiFine / Forge / тестовый).
- 🎯 **Любая версия Minecraft** через Mojang piston-meta: **1.8.9, 1.12.2, 1.16.5, 1.18.2, 1.19.4, 1.20.1**.
- 📦 **Импорт модпаков** — `.zip`, `.mrpack` (Modrinth), CurseForge overrides.
- 🔍 **Modrinth-браузер** — поиск модов прямо из лаунчера, установка одной кнопкой.
- 🛰 **Браузер серверов** — нативный MC SLP-протокол (handshake + status), показывает онлайн / MOTD / задержку.
- 🧰 **One-click installers**: Replay Mod, Sodium + Iris (Fabric), Vivecraft, Fabric Loader.
- 🎩 **Cape support** через Microsoft Account (выбор плаща).
- 📊 **FPS-overlay scaffolding** — читает OptiFine F3-строки из логов.

#### Соц / бэкенд
- 👥 **Друзья** и **WebSocket-чат** лаунчера.
- 🏆 **10 достижений PcLun** — локально + синхронизация с бэкендом.
- ☁️ **Облачный бэкап миров** через S3/R2 с presigned URL.
- 📈 **Анонимная opt-in телеметрия** (Sentry-style).
- 🌐 **Публичный сайт** под Cloudflare Pages с iframe live changelog.

#### UX
- 🎨 **Темы** — светлая / тёмная / системная + кастомный hex-акцент.
- 🪟 **System tray + autostart** (Windows registry / Linux `.desktop` / macOS LaunchAgent).
- 🛡 **Стример-режим** — маскирует ник, токены и IP сервера.
- 🖱 **Drag-drop** модпака.
- 🔔 **Кросс-платформенные уведомления** (Windows toast / `notify-send` / `osascript`).
- 📐 **Компактный режим** окна 520×380.

#### Performance
- 🚀 **Авто-FPS-профиль** — детектит CPU/GPU/RAM, выбирает Potato / Low / Balanced / Ultra сам.
- 🌐 **Network tuner** — TCP/IPv4 JVM-аргументы.
- 🎮 **GPU-детектор** + предупреждения (Intel HD сразу подсвечивается).
- 🌡 **Тротлинг защита** — мониторит температуру CPU.
- ⏬ **Демоушн фоновых процессов** при запуске игры (Chrome/Discord на Windows).

#### Безопасность
- 🛡 **Anti-cheat self-test** — хэш-чек модов перед заходом на ванильный сервер.
- 📜 **Hosts-редактор** для альтернативных серверов.
- ⚠️ **Mod conflict detector** — парсит `mods.toml` и `fabric.mod.json`.

#### Дистрибуция
- 📦 **MSI / .deb / .rpm** пакеты в Releases.
- 🔄 **Background auto-update** — стейджит новый бинарь пока идёт игра.
- 🍺 **Манифесты** для WinGet / Chocolatey / Homebrew.

### 🛠 Технические детали
- Backend v0.4.0: новые таблицы `friends`, `achievements`, `telemetry`, новые эндпоинты.
- 12 новых xUnit тестов (28/28 зелёные).
- Лаунчер собирается под **Windows / Linux / macOS Intel / macOS Apple Silicon**.

### 🐛 Исправлено
- Конфликт имён релиз-ассетов между Linux и macOS.
- WiX v7 OSMF EULA — пин на стабильную v4.

---

## [0.3.0] — 2026-04 — Community Edition

### ✨ Новое
- 🔄 **Авто-апдейт** через GitHub Releases с баннером в шапке.
- 💥 **Анализатор краш-логов** — парсит `crash-reports/`, подсвечивает причину и предлагает фикс.
- 🧩 **Менеджер модов / шейдеров / ресурспаков** — drag-drop, вкл/выкл одной галочкой, удаление.
- 💾 **Бэкап миров** — снимок `saves/` в zip + восстановление с авто-снимком перед заменой.
- 🟢 **Discord Rich Presence** — «Играет в PcLun · сервер · 1ч 23м».
- 🚀 **Performance Pack** в один клик: FerriteCore + Krypton + Starlight + SmoothBoot + EntityCulling + MemoryLeakFix.
- 🌑 **Каталог low-end шейдеров** с авто-загрузкой в `shaderpacks/`.
- 📊 **Локальная статистика игрока** (часы, сессии, средний FPS).
- 🧑 **Скин-вьювер** через Mojang API.
- 📤 **Импорт/экспорт настроек** в один JSON.
- 🖼 **Галерея скриншотов** в лаунчере.
- 🌍 Две новые вкладки: **«Контент»** и **«Сообщество»**.

### Бэкенд (v0.2.0)
- 💾 SQLite + WAL вместо `total.txt` (auto-migration).
- 🛡 Rate-limit на `/heartbeat` (30/мин/IP).
- 📈 Новые эндпоинты: `/stats`, `/news`, `/servers/featured`, `/leaderboard`.

### CI / тесты
- 🐧🍎 Linux + macOS (osx-x64/arm64) релизные workflow.
- 🧹 `dotnet format` check + `.editorconfig` + pre-commit.
- ✅ xUnit-проект, 16 тестов.

---

## [0.2.4] — 2026-04

- Добавлена иконка приложения (P-логотип) для `.exe` и окна.
- Метаданные сборки: Product / Company = MrDomik.

## [0.2.3] — 2026-04

- Кнопка «Открыть папку с игрой» рядом с «Играть» на главной.

## [0.2.2] — 2026-04

- Ребрендинг на «PcLun by MrDomik», убраны Telegram-ссылки.

## [0.2.1] — 2026-04

- Брендинг «by damirov666», ссылка на Telegram t.me/damirov666 в сайдбаре / hero / about / footer.

## [0.2.0] — 2026-04

- Первый публичный билд. 8 вкладок, авто-Java/MC/OptiFine, Ultra FPS-профиль, MSA + offline auth, бэкенд онлайна на Fly.io.

## [0.1.0] — 2026-04

- Прототип.
