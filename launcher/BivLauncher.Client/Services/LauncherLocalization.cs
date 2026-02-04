using BivLauncher.Client.Models;
using System.Globalization;

namespace BivLauncher.Client.Services;

public static class LauncherLocalization
{
    public static readonly IReadOnlyList<LocalizedOption> SupportedLanguages =
    [
        new LocalizedOption { Value = "ru", Label = "Русский" },
        new LocalizedOption { Value = "en", Label = "English" },
        new LocalizedOption { Value = "uk", Label = "Українська" },
        new LocalizedOption { Value = "kk", Label = "Қазақша" }
    ];

    private static readonly Dictionary<string, Dictionary<string, string>> Translations = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ru"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["tagline.default"] = "Управляемый лаунчер",
            ["status.loading"] = "Загрузка...",
            ["status.ready"] = "Готово.",
            ["status.notLoggedIn"] = "Не выполнен вход.",
            ["status.fetchingBootstrap"] = "Загрузка конфигурации...",
            ["status.noServers"] = "Нет доступных серверов от backend.",
            ["status.loadedServers"] = "Загружено серверов: {0}.",
            ["status.fetchingManifest"] = "Загрузка манифеста...",
            ["status.verifyingProgress"] = "Проверено {0}/{1}: {2}",
            ["status.verifyComplete"] = "Проверка завершена. Скачано: {0}, проверено: {1}.",
            ["status.launchingJava"] = "Запуск Java...",
            ["status.gameExitedNormally"] = "Игра завершилась без ошибок.",
            ["status.gameExitedCode"] = "Игра завершилась с кодом {0}.",
            ["status.settingsSaved"] = "Настройки сохранены.",
            ["status.crashCopied"] = "Лог краша скопирован.",
            ["status.authorizing"] = "Авторизация...",
            ["status.loggedInAs"] = "Вход выполнен: {0} ({1})",
            ["status.error"] = "Ошибка: {0}",
            ["status.ramMin"] = "мин {0} МБ",
            ["status.ramMax"] = "макс {0} МБ",
            ["status.skin"] = "Скин: {0}",
            ["status.cape"] = "Плащ: {0}",
            ["status.hasCrash"] = "Есть краш: {0}",
            ["common.yes"] = "да",
            ["common.no"] = "нет",
            ["button.refresh"] = "Обновить",
            ["button.saveSettings"] = "Сохранить настройки",
            ["button.login"] = "Войти",
            ["button.verifyFiles"] = "Проверить файлы",
            ["button.play"] = "Играть",
            ["button.openLogs"] = "Открыть папку логов",
            ["button.copyCrash"] = "Скопировать последние строки",
            ["header.servers"] = "Серверы",
            ["header.news"] = "Новости",
            ["header.settings"] = "Настройки лаунчера",
            ["header.account"] = "Аккаунт",
            ["header.runtime"] = "Рантайм",
            ["header.crash"] = "Сводка краша",
            ["header.debugLog"] = "Отладочный вывод",
            ["label.apiBaseUrl"] = "API Base URL",
            ["label.installDirectory"] = "Папка установки",
            ["label.language"] = "Язык",
            ["label.route"] = "Куда заходить",
            ["label.username"] = "Логин",
            ["label.password"] = "Пароль",
            ["label.javaMode"] = "Режим Java",
            ["label.ram"] = "RAM (МБ)",
            ["label.debugMode"] = "Режим отладки (живые логи)",
            ["route.main"] = "Основной сервер (DE хост)",
            ["route.ru"] = "RU сервер (через прокси)",
            ["java.auto"] = "Авто",
            ["java.bundled"] = "Встроенная",
            ["java.system"] = "Системная",
            ["rpc.disabled"] = "RPC отключен",
            ["rpc.preview"] = "RPC {0}: {1}",
            ["news.meta.pinned"] = "Закреплено | {0} | {1}",
            ["news.meta.regular"] = "{0} | {1}",
            ["news.source.manual"] = "вручную",
            ["validation.usernameRequired"] = "Требуется логин.",
            ["validation.passwordRequired"] = "Требуется пароль.",
            ["validation.ruProxyAddressRequired"] = "Для маршрута RU у сервера не настроен адрес прокси."
        },
        ["en"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["tagline.default"] = "Managed launcher",
            ["status.loading"] = "Loading...",
            ["status.ready"] = "Ready.",
            ["status.notLoggedIn"] = "Not logged in.",
            ["status.fetchingBootstrap"] = "Fetching bootstrap...",
            ["status.noServers"] = "No servers available from backend.",
            ["status.loadedServers"] = "Loaded server(s): {0}.",
            ["status.fetchingManifest"] = "Fetching manifest...",
            ["status.verifyingProgress"] = "Verified {0}/{1}: {2}",
            ["status.verifyComplete"] = "Verify complete. Downloaded: {0}, verified: {1}.",
            ["status.launchingJava"] = "Launching Java...",
            ["status.gameExitedNormally"] = "Game exited normally.",
            ["status.gameExitedCode"] = "Game exited with code {0}.",
            ["status.settingsSaved"] = "Settings saved.",
            ["status.crashCopied"] = "Crash log copied.",
            ["status.authorizing"] = "Authorizing...",
            ["status.loggedInAs"] = "Logged in as {0} ({1})",
            ["status.error"] = "Error: {0}",
            ["status.ramMin"] = "min {0} MB",
            ["status.ramMax"] = "max {0} MB",
            ["status.skin"] = "Skin: {0}",
            ["status.cape"] = "Cape: {0}",
            ["status.hasCrash"] = "Has crash: {0}",
            ["common.yes"] = "yes",
            ["common.no"] = "no",
            ["button.refresh"] = "Refresh",
            ["button.saveSettings"] = "Save Settings",
            ["button.login"] = "Login",
            ["button.verifyFiles"] = "Verify Files",
            ["button.play"] = "Play",
            ["button.openLogs"] = "Open Logs Folder",
            ["button.copyCrash"] = "Copy Last Lines",
            ["header.servers"] = "Servers",
            ["header.news"] = "News",
            ["header.settings"] = "Launcher Settings",
            ["header.account"] = "Account",
            ["header.runtime"] = "Runtime",
            ["header.crash"] = "Crash Summary",
            ["header.debugLog"] = "Debug Log Output",
            ["label.apiBaseUrl"] = "API Base URL",
            ["label.installDirectory"] = "Install Directory",
            ["label.language"] = "Language",
            ["label.route"] = "Join Route",
            ["label.username"] = "Username",
            ["label.password"] = "Password",
            ["label.javaMode"] = "Java Mode",
            ["label.ram"] = "RAM (MB)",
            ["label.debugMode"] = "Debug mode (live logs)",
            ["route.main"] = "Main server (DE host)",
            ["route.ru"] = "RU server (via proxy)",
            ["java.auto"] = "Auto",
            ["java.bundled"] = "Bundled",
            ["java.system"] = "System",
            ["rpc.disabled"] = "RPC disabled",
            ["rpc.preview"] = "RPC {0}: {1}",
            ["news.meta.pinned"] = "Pinned | {0} | {1}",
            ["news.meta.regular"] = "{0} | {1}",
            ["news.source.manual"] = "manual",
            ["validation.usernameRequired"] = "Username is required.",
            ["validation.passwordRequired"] = "Password is required.",
            ["validation.ruProxyAddressRequired"] = "RU route is selected but proxy address is not configured for this server."
        },
        ["uk"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["tagline.default"] = "Керований лаунчер",
            ["status.loading"] = "Завантаження...",
            ["status.ready"] = "Готово.",
            ["status.notLoggedIn"] = "Вхід не виконано.",
            ["status.fetchingBootstrap"] = "Завантаження конфігурації...",
            ["status.noServers"] = "Немає доступних серверів з backend.",
            ["status.loadedServers"] = "Завантажено серверів: {0}.",
            ["status.fetchingManifest"] = "Завантаження маніфесту...",
            ["status.verifyingProgress"] = "Перевірено {0}/{1}: {2}",
            ["status.verifyComplete"] = "Перевірку завершено. Завантажено: {0}, перевірено: {1}.",
            ["status.launchingJava"] = "Запуск Java...",
            ["status.gameExitedNormally"] = "Гру завершено без помилок.",
            ["status.gameExitedCode"] = "Гру завершено з кодом {0}.",
            ["status.settingsSaved"] = "Налаштування збережено.",
            ["status.crashCopied"] = "Лог крешу скопійовано.",
            ["status.authorizing"] = "Авторизація...",
            ["status.loggedInAs"] = "Вхід виконано: {0} ({1})",
            ["status.error"] = "Помилка: {0}",
            ["status.ramMin"] = "мін {0} МБ",
            ["status.ramMax"] = "макс {0} МБ",
            ["status.skin"] = "Скін: {0}",
            ["status.cape"] = "Плащ: {0}",
            ["status.hasCrash"] = "Є креш: {0}",
            ["common.yes"] = "так",
            ["common.no"] = "ні",
            ["button.refresh"] = "Оновити",
            ["button.saveSettings"] = "Зберегти налаштування",
            ["button.login"] = "Увійти",
            ["button.verifyFiles"] = "Перевірити файли",
            ["button.play"] = "Грати",
            ["button.openLogs"] = "Відкрити папку логів",
            ["button.copyCrash"] = "Скопіювати останні рядки",
            ["header.servers"] = "Сервери",
            ["header.news"] = "Новини",
            ["header.settings"] = "Налаштування лаунчера",
            ["header.account"] = "Акаунт",
            ["header.runtime"] = "Рантайм",
            ["header.crash"] = "Зведення крешу",
            ["header.debugLog"] = "Відладочний вивід",
            ["label.apiBaseUrl"] = "API Base URL",
            ["label.installDirectory"] = "Папка встановлення",
            ["label.language"] = "Мова",
            ["label.route"] = "Куди заходити",
            ["label.username"] = "Логін",
            ["label.password"] = "Пароль",
            ["label.javaMode"] = "Режим Java",
            ["label.ram"] = "RAM (МБ)",
            ["label.debugMode"] = "Режим налагодження (живі логи)",
            ["route.main"] = "Основний сервер (DE хост)",
            ["route.ru"] = "RU сервер (через проксі)",
            ["java.auto"] = "Авто",
            ["java.bundled"] = "Вбудована",
            ["java.system"] = "Системна",
            ["rpc.disabled"] = "RPC вимкнено",
            ["rpc.preview"] = "RPC {0}: {1}",
            ["news.meta.pinned"] = "Закріплено | {0} | {1}",
            ["news.meta.regular"] = "{0} | {1}",
            ["news.source.manual"] = "вручну",
            ["validation.usernameRequired"] = "Потрібен логін.",
            ["validation.passwordRequired"] = "Потрібен пароль.",
            ["validation.ruProxyAddressRequired"] = "Для маршруту RU не налаштовано адресу проксі для цього сервера."
        },
        ["kk"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["tagline.default"] = "Басқарылатын лаунчер",
            ["status.loading"] = "Жүктелуде...",
            ["status.ready"] = "Дайын.",
            ["status.notLoggedIn"] = "Кіру орындалмаған.",
            ["status.fetchingBootstrap"] = "Конфигурация жүктелуде...",
            ["status.noServers"] = "Backend-тен қолжетімді серверлер жоқ.",
            ["status.loadedServers"] = "Жүктелген сервер саны: {0}.",
            ["status.fetchingManifest"] = "Манифест жүктелуде...",
            ["status.verifyingProgress"] = "Тексерілді {0}/{1}: {2}",
            ["status.verifyComplete"] = "Тексеру аяқталды. Жүктелді: {0}, тексерілді: {1}.",
            ["status.launchingJava"] = "Java іске қосылуда...",
            ["status.gameExitedNormally"] = "Ойын қалыпты аяқталды.",
            ["status.gameExitedCode"] = "Ойын {0} кодымен аяқталды.",
            ["status.settingsSaved"] = "Баптаулар сақталды.",
            ["status.crashCopied"] = "Краш журналы көшірілді.",
            ["status.authorizing"] = "Авторизация...",
            ["status.loggedInAs"] = "Кіру орындалды: {0} ({1})",
            ["status.error"] = "Қате: {0}",
            ["status.ramMin"] = "мин {0} МБ",
            ["status.ramMax"] = "макс {0} МБ",
            ["status.skin"] = "Скин: {0}",
            ["status.cape"] = "Плащ: {0}",
            ["status.hasCrash"] = "Краш бар: {0}",
            ["common.yes"] = "иә",
            ["common.no"] = "жоқ",
            ["button.refresh"] = "Жаңарту",
            ["button.saveSettings"] = "Баптауларды сақтау",
            ["button.login"] = "Кіру",
            ["button.verifyFiles"] = "Файлдарды тексеру",
            ["button.play"] = "Ойнау",
            ["button.openLogs"] = "Логтар бумасын ашу",
            ["button.copyCrash"] = "Соңғы жолдарды көшіру",
            ["header.servers"] = "Серверлер",
            ["header.news"] = "Жаңалықтар",
            ["header.settings"] = "Лаунчер баптаулары",
            ["header.account"] = "Аккаунт",
            ["header.runtime"] = "Рантайм",
            ["header.crash"] = "Краш қорытындысы",
            ["header.debugLog"] = "Отладка логы",
            ["label.apiBaseUrl"] = "API Base URL",
            ["label.installDirectory"] = "Орнату бумасы",
            ["label.language"] = "Тіл",
            ["label.route"] = "Кіру бағыты",
            ["label.username"] = "Логин",
            ["label.password"] = "Құпиясөз",
            ["label.javaMode"] = "Java режимі",
            ["label.ram"] = "RAM (МБ)",
            ["label.debugMode"] = "Отладка режимі (тірі логтар)",
            ["route.main"] = "Негізгі сервер (DE хост)",
            ["route.ru"] = "RU сервері (прокси арқылы)",
            ["java.auto"] = "Авто",
            ["java.bundled"] = "Кірістірілген",
            ["java.system"] = "Жүйелік",
            ["rpc.disabled"] = "RPC өшірулі",
            ["rpc.preview"] = "RPC {0}: {1}",
            ["news.meta.pinned"] = "Бекітілген | {0} | {1}",
            ["news.meta.regular"] = "{0} | {1}",
            ["news.source.manual"] = "қолмен",
            ["validation.usernameRequired"] = "Логин қажет.",
            ["validation.passwordRequired"] = "Құпиясөз қажет.",
            ["validation.ruProxyAddressRequired"] = "RU бағыты таңдалды, бірақ бұл сервер үшін прокси мекенжайы бапталмаған."
        }
    };

    public static string NormalizeLanguage(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return "ru";
        }

        var normalized = languageCode.Trim().ToLowerInvariant();
        return Translations.ContainsKey(normalized) ? normalized : "ru";
    }

    public static string T(string languageCode, string key)
    {
        var normalized = NormalizeLanguage(languageCode);
        if (Translations.TryGetValue(normalized, out var localized) &&
            localized.TryGetValue(key, out var value))
        {
            return value;
        }

        if (Translations["ru"].TryGetValue(key, out var fallback))
        {
            return fallback;
        }

        return key;
    }

    public static string F(string languageCode, string key, params object[] args)
    {
        return string.Format(CultureInfo.CurrentCulture, T(languageCode, key), args);
    }
}
