using System.Globalization;
using System.Windows;

namespace TsOverlay.Services;

public static class Loc
{
    private static readonly Dictionary<string, (string ru, string en)> Table = new(StringComparer.OrdinalIgnoreCase)
    {
        // Окно настроек
        ["Settings_Title"] = ("TS Overlay — настройки", "TS Overlay — Settings"),
        ["Settings_SavePrompt"] = ("Сохранить изменения перед закрытием?", "Save changes before closing?"),

        // Вкладки
        ["Tab_General"] = ("Общие", "General"),
        ["Tab_Overlay"] = ("Оверлей", "Overlay"),
        ["Tab_Games"] = ("Игры", "Games"),
        ["Tab_Connection"] = ("Подключение", "Connection"),

        // Язык
        ["Language_Label"] = ("Язык интерфейса", "Interface language"),
        ["Lang_Auto"] = ("Автоматически (система)", "Automatic (system)"),
        ["Lang_Ru"] = ("Русский", "Русский (Russian)"),
        ["Lang_En"] = ("English", "English"),

        // Клиент TeamSpeak
        ["Client_Header"] = ("Клиент TeamSpeak", "TeamSpeak Client"),
        ["Client_Mode"] = ("Какой клиент использовать", "Client to use"),
        ["Client_Mode_Auto"] = ("Автоматически (запущенный)", "Automatic (running client)"),
        ["Client_Mode_Ts3"] = ("Только TeamSpeak 3", "TeamSpeak 3 only"),
        ["Client_Mode_Ts6"] = ("Только TeamSpeak 6", "TeamSpeak 6 only"),
        ["Status_Label"] = ("Статус:", "Status:"),

        // Запуск
        ["Startup_Header"] = ("Запуск программы", "Startup & Behavior"),
        ["Startup_Windows"] = ("Запускать вместе с Windows", "Start with Windows"),
        ["Startup_AutoLaunch"] = ("Запускать TeamSpeak при старте оверлея", "Launch TeamSpeak on startup"),
        ["Startup_ExitWithTs"] = ("Закрывать оверлей при выходе из TeamSpeak", "Close overlay when TeamSpeak exits"),
        ["Startup_Notify"] = ("Показывать системные уведомления", "Show system notifications"),
        ["Startup_Updates"] = ("Проверять обновления при запуске", "Check for updates on start"),

        // Горячие клавиши
        ["Hotkeys_Header"] = ("Горячие клавиши", "Global Hotkeys"),
        ["Hotkeys_Toggle"] = ("Показать / скрыть оверлей", "Toggle overlay"),
        ["Hotkeys_Hud"] = ("Список говорящих (поверх всех окон)", "Who is talking (topmost)"),
        ["Hotkeys_Hint"] = ("Кликните в поле и нажмите комбинацию клавиш. Backspace — отключить.", "Click inside the box and press keys. Backspace clears the key."),

        // Отображение
        ["Display_Header"] = ("Режим отображения", "Display Mode"),
        ["Display_ShowSelf"] = ("Показывать себя в списке канала", "Show self in channel list"),
        ["Display_AlwaysShow"] = ("Показывать поверх всех окон", "Always on top"),
        ["Display_AlwaysShow_Hint"] = ("Оверлей виден поверх любых окон, пока активна игра из списка. Если список игр пуст — виден всегда.", "Overlay is visible over any window while a game from the list is running. If the list is empty — always visible."),

        // Положение и размер оверлея
        ["Overlay_Pos_Header"] = ("Положение и размеры", "Position & Size"),
        ["Overlay_Anchor"] = ("Положение на экране", "Screen anchor"),
        ["Anchor_TopLeft"] = ("Слева сверху", "Top-Left"),
        ["Anchor_TopCenter"] = ("Сверху по центру", "Top-Center"),
        ["Anchor_TopRight"] = ("Справа сверху", "Top-Right"),
        ["Anchor_LeftCenter"] = ("Слева по центру", "Center-Left"),
        ["Anchor_RightCenter"] = ("Справа по центру", "Center-Right"),
        ["Anchor_BottomLeft"] = ("Слева снизу", "Bottom-Left"),
        ["Anchor_BottomCenter"] = ("Снизу по центру", "Bottom-Center"),
        ["Anchor_BottomRight"] = ("Справа снизу", "Bottom-Right"),
        ["Anchor_Custom"] = ("Пользовательское (перетаскивание)", "Custom (drag & drop)"),

        ["Overlay_Margin"] = ("Отступ X / Y (пикс.)", "Offset X / Y (px)"),
        ["Overlay_Width"] = ("Ширина панели", "Panel width"),
        ["Overlay_Scale"] = ("Масштаб", "Scale"),
        ["Overlay_RowHeight"] = ("Высота строки", "Row height"),
        ["Overlay_FontSize"] = ("Размер шрифта", "Font size"),
        ["Overlay_Opacity"] = ("Прозрачность фона", "Background opacity"),
        ["Overlay_MaxRows"] = ("Макс. строк", "Max rows"),
        ["Overlay_CornerRadius"] = ("Скругление углов", "Corner radius"),
        ["Overlay_ShowHeader"] = ("Показывать шапку", "Show header"),
        ["Overlay_ShowServer"] = ("Показывать имя сервера", "Show server name"),
        ["Overlay_ShowAvatars"] = ("Показывать аватары", "Show avatars"),
        ["Overlay_DragHint"] = ("Перемещение: зажмите Ctrl+Alt и потяните оверлей мышью — позиция сохранится.", "Drag overlay: hold Ctrl+Alt and drag with mouse — position will be saved."),

        // Цвета
        ["Colors_Header"] = ("Цвета интерфейса (HEX: #AARRGGBB)", "UI Colors (HEX: #AARRGGBB)"),
        ["Color_Background"] = ("Фон оверлея", "Overlay background"),
        ["Color_Background_Tip"] = ("Цвет фона и прозрачность подложки оверлея", "Background color and opacity of the overlay panel"),
        ["Color_Text"] = ("Имена участников", "Participant names"),
        ["Color_Text_Tip"] = ("Основной цвет имён участников в канале (когда молчат)", "Default color for client nicknames in channel"),
        ["Color_Dim"] = ("Неактивные / AFK", "Inactive / AFK"),
        ["Color_Dim_Tip"] = ("Цвет участников, отошедших от компьютера (AFK) или приглушённых", "Color for participants who are AFK or muted/inactive"),
        ["Color_Talking"] = ("Подсветка речи", "Speaking highlight"),
        ["Color_Talking_Tip"] = ("Цвет подсветки ника и точки, когда участник говорит в микрофон", "Color of the nickname and dot when client is speaking"),
        ["Color_Muted"] = ("Микрофон выключен", "Microphone muted"),
        ["Color_Muted_Tip"] = ("Цвет значка перечёркнутого микрофона", "Color of the muted microphone icon"),
        ["Color_Palette_Tip"] = ("Выбрать цвет из палитры", "Pick color from palette"),

        // Игры
        ["Games_Desc"] = ("Оверлей появляется поверх перечисленных игр, когда окно игры активно. Игру можно добавить из запущенных процессов или выбрать .exe файл.", "The overlay appears over listed games when the game window is active. Games can be added from running processes or by selecting an .exe file."),
        ["Col_Enabled"] = ("Вкл", "Active"),
        ["Col_Game"] = ("Игра", "Game"),
        ["Col_Process"] = ("Процесс", "Process"),
        ["Btn_AddRunning"] = ("Добавить из запущенных…", "Add from running…"),
        ["Btn_BrowseExe"] = ("Указать .exe…", "Browse .exe…"),
        ["Btn_RemoveGame"] = ("Удалить", "Remove"),

        // Подключение
        ["Conn_Ts3_Header"] = ("TeamSpeak 3 — ClientQuery", "TeamSpeak 3 — ClientQuery"),
        ["Conn_Ts3_Desc"] = ("В клиенте TS3 должен быть включён плагин ClientQuery (Инструменты → Параметры → Аддоны). Ключ можно взять из clientquery.ini автоматически.", "In TS3, ClientQuery plugin must be enabled (Tools → Options → Addons). API key can be loaded from clientquery.ini automatically."),
        ["Conn_ApiKey"] = ("Ключ API", "API Key"),
        ["Conn_ReadIni"] = ("Взять из ini", "Read from ini"),
        ["Conn_Port"] = ("Порт", "Port"),
        ["Conn_Ts6_Header"] = ("TeamSpeak 6 — Remote Apps", "TeamSpeak 6 — Remote Apps"),
        ["Conn_Ts6_Desc"] = ("При первом запуске TeamSpeak 6 запросит разрешение — подтвердите его (Настройки → Удалённые приложения / Remote Apps). Ключ выдаётся клиентом автоматически.", "On first run, TeamSpeak 6 will request permission — allow it (Settings → Remote Apps). The API key is issued automatically."),
        ["Conn_Paths_Header"] = ("Пути к клиентам (если установлены нестандартно)", "Client paths (if installed in custom location)"),

        // Нижние кнопки
        ["Btn_OpenLog"] = ("Открыть журнал", "Open log"),
        ["Btn_Save"] = ("Сохранить", "Save"),
        ["Btn_Cancel"] = ("Отмена", "Cancel"),

        // Окно запущенных игр
        ["Running_Title"] = ("Добавить игру из запущенных", "Add game from running processes"),
        ["Running_Desc"] = ("Выберите окна игр (можно несколько). В список попадёт имя процесса — по нему оверлей будет находить игру.", "Select game windows (multiple selection supported). Process names will be used to detect games."),
        ["Running_Col_Process"] = ("Процесс", "Process"),
        ["Running_Col_Window"] = ("Окно", "Window Title"),
        ["Running_Btn_Refresh"] = ("Обновить", "Refresh"),
        ["Running_Btn_Add"] = ("Добавить", "Add"),

        // Меню трея
        ["Tray_NoConnection"] = ("Нет подключения", "No connection"),
        ["Tray_OverlayEnabled"] = ("Оверлей включён", "Overlay enabled"),
        ["Tray_ToggleOverlay"] = ("Показать/скрыть оверлей", "Toggle overlay"),
        ["Tray_WhoTalking"] = ("Кто говорит (поверх любого окна)", "Who is talking (topmost)"),
        ["Tray_AlwaysOnTop"] = ("Поверх любых окон", "Always on top"),
        ["Tray_DragHint"] = ("Перетащить: Ctrl+Alt + мышь", "Drag: Ctrl+Alt + mouse"),
        ["Tray_PreviewDesktop"] = ("Показать оверлей на рабочем столе", "Show overlay on desktop"),
        ["Tray_Settings"] = ("Настройки…", "Settings…"),
        ["Tray_LaunchTs"] = ("Запустить TeamSpeak", "Launch TeamSpeak"),
        ["Tray_Reconnect"] = ("Переподключиться", "Reconnect"),
        ["Tray_CheckUpdates"] = ("Проверить обновления", "Check for updates"),
        ["Tray_StartWithWindows"] = ("Запускать с Windows", "Start with Windows"),
        ["Tray_Exit"] = ("Выход", "Exit"),

        // Уведомления и статусы
        ["Notice_Ts6_Permission"] = ("TeamSpeak 6: разрешите доступ оверлею в клиенте (Настройки → Удалённые приложения / Remote Apps).", "TeamSpeak 6: please allow overlay access in client (Settings → Remote Apps)."),
        ["Status_Ts6_WaitingAuth"] = ("TeamSpeak 6: ожидаю разрешение приложения", "TeamSpeak 6: waiting for app permission"),
        ["Status_Ts6_Connected"] = ("TeamSpeak 6: подключено", "TeamSpeak 6: connected"),
        ["Status_Ts6_AccessDenied"] = ("TeamSpeak 6: доступ запрещён — разрешите приложение", "TeamSpeak 6: access denied — allow the app"),
        ["Status_Ts6_Reconnecting"] = ("TeamSpeak 6: нет связи, переподключаюсь…", "TeamSpeak 6: no connection, reconnecting…"),
        ["Status_Ts6_NotConnected"] = ("TeamSpeak 6 не подключён к серверу", "TeamSpeak 6 is not connected to a server"),
        ["Status_Ts3_Connected"] = ("TeamSpeak 3: подключено", "TeamSpeak 3: connected"),
        ["Status_Ts_NotRunning"] = ("TeamSpeak не запущен", "TeamSpeak is not running"),
        ["Status_Ts6_NotRunning"] = ("TeamSpeak 6 не запущен", "TeamSpeak 6 is not running"),
        ["Status_Ts3_NotRunning"] = ("TeamSpeak 3 не запущен", "TeamSpeak 3 is not running"),
    };

    public static string CurrentLanguage { get; private set; } = "ru";

    public static event Action? LanguageChanged;

    public static void Initialize(string configuredLanguage)
    {
        SetLanguage(configuredLanguage);
    }

    public static void SetLanguage(string lang)
    {
        var resolved = ResolveLanguage(lang);
        CurrentLanguage = resolved;

        var app = Application.Current;
        if (app != null)
        {
            foreach (var kvp in Table)
            {
                var text = resolved == "en" ? kvp.Value.en : kvp.Value.ru;
                app.Resources["Loc_" + kvp.Key] = text;
            }
        }

        LanguageChanged?.Invoke();
    }

    public static string Tr(string key)
    {
        if (!Table.TryGetValue(key, out var pair))
        {
            return key;
        }

        return CurrentLanguage == "en" ? pair.en : pair.ru;
    }

    public static string ResolveLanguage(string lang)
    {
        if (string.Equals(lang, "en", StringComparison.OrdinalIgnoreCase))
        {
            return "en";
        }

        if (string.Equals(lang, "ru", StringComparison.OrdinalIgnoreCase))
        {
            return "ru";
        }

        // "auto"
        var uiName = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
        return uiName is "ru" or "uk" or "be" ? "ru" : "en";
    }
}
