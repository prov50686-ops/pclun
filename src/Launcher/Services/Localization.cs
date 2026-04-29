using System.Collections.Generic;

namespace PcLun.Services;

/// <summary>
/// Простой словарь переводов. Используется для отдельных строк, которые задаются из C# кода
/// (диалоги/тосты). XAML-разметка остаётся на русском, потому что переводить весь UI на ходу —
/// отдельная работа и сейчас не критична.
/// </summary>
public static class Localization
{
    public enum Lang { Ru, En, Uk }

    public static Lang Current { get; set; } = Lang.Ru;

    private static readonly Dictionary<string, Dictionary<Lang, string>> _strings = new()
    {
        ["update.available"] = new()
        {
            [Lang.Ru] = "Доступна новая версия лаунчера: {0}",
            [Lang.En] = "A new launcher version is available: {0}",
            [Lang.Uk] = "Доступна нова версія лаунчера: {0}",
        },
        ["update.latest"] = new()
        {
            [Lang.Ru] = "Установлена последняя версия.",
            [Lang.En] = "You have the latest version.",
            [Lang.Uk] = "Встановлено останню версію.",
        },
        ["backup.created"] = new()
        {
            [Lang.Ru] = "Бэкап создан: {0}",
            [Lang.En] = "Backup created: {0}",
            [Lang.Uk] = "Бекап створено: {0}",
        },
        ["backup.restored"] = new()
        {
            [Lang.Ru] = "Бэкап восстановлен.",
            [Lang.En] = "Backup restored.",
            [Lang.Uk] = "Бекап відновлено.",
        },
        ["crash.scanning"] = new()
        {
            [Lang.Ru] = "Сканируем crash-reports…",
            [Lang.En] = "Scanning crash-reports…",
            [Lang.Uk] = "Скануємо crash-reports…",
        },
        ["crash.none"] = new()
        {
            [Lang.Ru] = "Крашей не найдено. Можно играть.",
            [Lang.En] = "No crashes found. You're good to go.",
            [Lang.Uk] = "Крашів не знайдено. Можна грати.",
        },
    };

    public static string T(string key, params object[] args)
    {
        if (_strings.TryGetValue(key, out var byLang) && byLang.TryGetValue(Current, out var s))
            return args.Length > 0 ? string.Format(s, args) : s;
        return key;
    }
}
