using System.Collections.Concurrent;
using System.Globalization;
using ZapretCLI.Core.Interfaces;
using ZapretCLI.Core.Logging;

namespace ZapretCLI.Core.Services
{
    public class LocalizationService : ILocalizationService, IDisposable
    {
        private readonly IConfigService _configService;
        private readonly ILoggerService _logger;
        private readonly ConcurrentDictionary<string, string> _currentTranslations = new ConcurrentDictionary<string, string>();
        private string _currentLanguage = "en";
        private bool _disposed = false;

        private static readonly Dictionary<string, string> _availableLanguages = new Dictionary<string, string>
        {
            { "en", "English" },
            { "ru", "Русский" }
        };

        public LocalizationService(IConfigService configService, IFileSystemService fileSystemService, ILoggerService logger, string appPath)
        {
            _configService = configService;
            _logger = logger;

            // Убираем работу с файловой системой
            LoadLanguage();
        }

        private void LoadLanguage()
        {
            var config = _configService.GetConfig();

            // Определяем язык из настроек или системы
            _currentLanguage = !string.IsNullOrEmpty(config.Language) && _availableLanguages.ContainsKey(config.Language)
                ? config.Language
                : (CultureInfo.CurrentCulture.TwoLetterISOLanguageName.ToLower() == "ru" ? "ru" : "en");

            _logger.LogInformation($"Current language set to: {_currentLanguage}");
            LoadTranslations(_currentLanguage);
        }

        private void LoadTranslations(string languageCode)
        {
            // Загружаем встроенные переводы
            var translations = GetDefaultTranslations(languageCode)
                               ?? GetDefaultTranslations("en")
                               ?? new Dictionary<string, string>();

            _currentTranslations.Clear();
            foreach (var kvp in translations)
            {
                _currentTranslations[kvp.Key] = kvp.Value;
            }

            _logger.LogInformation($"Loaded {translations.Count} built-in translations for: {languageCode}");
        }

        private Dictionary<string, string> GetDefaultTranslations(string languageCode)
        {
            if (languageCode == "ru")
            {
                return new Dictionary<string, string>
                {
                    { "title", "Zapret CLI {0} от SillyApps" },
                    { "navigation", "Перемещение: стрелки ↑↓\nПодтверждение: клавиша Enter" },
                    { "menu_start", "Запустить сервис" },
                    { "menu_stop", "Остановить сервис" },
                    { "menu_status", "Статус сервиса" },
                    { "menu_edit", "Редактировать списки" },
                    { "menu_update", "Обновить приложение" },
                    { "menu_test", "Тестирование профилей" },
                    { "menu_settings", "Настройки" },
                    { "menu_exit", "Выйти" },
                    { "other_options", "Не все пункты помещаются на экран\nИспользуйте стрелки ↑ и ↓ для прокрутки списка" },
                    { "select_list", "Выберите список для редактирования" },
                    { "general_list", "Основной список" },
                    { "exclude_list", "Список исключений" },
                    { "back", "Вернуться назад" },
                    { "add_domain", "Добавить домен" },
                    { "empty_domain", "Домен не может быть пустым" },
                    { "domain_already_in_list", "Домен уже в списке" },
                    { "update_select", "Выберите, что вы хотите обновить" },
                    { "update_profiles", "Обновить профили и списки" },
                    { "update_app", "Обновить Zapret CLI" },
                    { "app_settings", "Настройки приложения" },
                    { "auto_run", "Автозапуск" },
                    { "enabled", "Включен" },
                    { "disabled", "Выключен" },
                    { "not_specified", "Не задан" },
                    { "auto_profile", "Авто-профиль" },
                    { "game_filter", "Фильтр для игр" },
                    { "delete_service", "Удалить службу Zapret (НЕ ПРИЛОЖЕНИЕ!)" },
                    { "no_profiles", "Нет доступных профилей (попробуйте обновить профили в меню)" },
                    { "auto_profile_title", "Выберите профиль для автозапуска" },
                    { "language_changed", "Язык изменен на Русский" },
                    { "updating_no_profiles", "Профили не найдены. Обновление..." },
                    { "no_profiles_after_update", "После обновления профили не найдены. Что-то пошло не так?" },
                    { "available_profiles", "Доступные профили" },
                    { "test_domain_ask", "Введите заблокированный домен для тестирования" },
                    { "update_ask", "Обновить?" },
                    { "zapret_start_timeout", "Истекло время ожидания инициализации Windivert (>{0} секунд). Что-то пошло не так?" },
                    { "zapret_start", "Zapret успешно запустился с профилем '{0}'!" },
                    { "zapret_start_fail", "Не удалось запустить zapret: {0}" },
                    { "zapret_stop_fail", "Не удалось остановить zapret: {0}" },
                    { "zapret_stop", "Zapret успешно остановлен" },
                    { "profile_not_selected", "Профиль не выбран" },
                    { "profile", "Профиль" },
                    { "description", "Описание" },
                    { "arguments", "Аргументы" },
                    { "running", "Запущен" },
                    { "stopped", "Остановлен" },
                    { "service_status", "Статус сервиса" },
                    { "status", "Статус" },
                    { "working_directory", "Каталог приложения" },
                    { "press_any_key", "Нажмите любую клавишу" },
                    { "profile_not_found", "Профиль '{0}' не найден" },
                    { "checking_for_updates", "Проверка обновлений..." },
                    { "hosts", "Домены" },
                    { "ips", "IP-адреса" },
                    { "ip_subnets", "IP-адреса/подсети" },
                    { "desync_profiles", "Рассинхронизирующие профили" },
                    { "low_priority_profile", "Профиль с низким приоритетом" },
                    { "parsed_profile", "Обработанный профиль" },
                    { "profile_parse_fail", "Не удалось обработать профиль" },
                    { "profile_load_fail", "Не удалось загрузить профиль" },
                    { "new_version_title", "Доступна новая версия Zapret" },
                    { "new_version", "Новая версия" },
                    { "current_version", "Текущая версия" },
                    { "changelog", "Журнал изменений" },
                    { "first_launch", "Похоже, это ваш первый запуск. Выполняется установка последней версии Zapret..." },
                    { "updates_check_fail", "Не удалось проверить обновления" },
                    { "new_cli_version_title", "Новая версия Zapret CLI" },
                    { "cli_updates_check_fail", "Не удалось проверить обновления Zapret CLI" },
                    { "downloading", "Скачивание..." },
                    { "launching_installer", "Запуск установщика..." },
                    { "close_app", "Выход из приложения..." },
                    { "update_fail", "Не удалось обновить" },
                    { "zapret_updated", "Обновление успешно установлено" },
                    { "services_stop", "Службы и процессы успешно остановлены" },
                    { "services_stop_fail", "Не удалось остановить некоторые службы/процессы" },
                    { "service_stop_fail", "Службу {0} не удалось остановить, попытка удаления все равно выполняется" },
                    { "service_delete_fail", "Не удалось остановить/удалить службу" },
                    { "process_exited_with_fail", "Процесс {0} завершился некорректно" },
                    { "process_terminate_fail", "Не удалось завершить процесс" },
                    { "processes_terminate_fail", "Ошибка проверки процессов" },
                    { "extract_success", "Архив успешно извлечен" },
                    { "extract_fail", "Не удалось извлечь архив" },
                    { "files_copy", "Копирование файлов..." },
                    { "lists_update", "Обновление списков..." },
                    { "profiles_proccess", "Обработка профилей..." },
                    { "profiles_proccessed", "Найдено и обработано {0} профилей" },
                    { "temp_cleanup_fail", "Не удалось очистить временные файлы" },
                    { "domain_add_fail", "Не удалось добавить домен" },
                    { "profile_testing_title", "Тестирование профилей" },
                    { "close_apps_warning", "Закройте все сторонние приложения для наилучшего результата (например, Discord, VPN и т. д.)" },
                    { "domain_accessible_warning", "{0} кажется доступным без обхода. Тестирование может быть неточным." },
                    { "continue_anyway", "Продолжить тестирование в любом случае?" },
                    { "no_profiles_to_test", "Нет доступных профилей для тестирования" },
                    { "starting_tests", "Запуск тестов для {0} с {1} профилями" },
                    { "testing_profile", "Тестирование профиля, {0}" },
                    { "init_failed", "Не удалось инициализировать" },
                    { "http_success", "HTTP {0}, Соединение успешно" },
                    { "success", "УСПЕХ" },
                    { "http_fail", "HTTP {0}, Соединение не удалось" },
                    { "failed", "НЕУДАЧА" },
                    { "conn_timeout", "Время ожидания соединения истекло" },
                    { "error_occurred", "Произошла ошибка" },
                    { "test_results", "Сводка результатов тестирования" },
                    { "profiles_bypassed", "{0} профилей успешно обошли блокировку" },
                    { "no_profiles_bypassed", "Ни один профиль не обошел блокировку. Да спасет нас Бог." },
                    { "domain_remove_fail", "Не удалось удалить домен" },
                    { "search_placeholder", "Введите текст для поиска..." },
                    { "add_new_domain_to", "Введите домен для добавления в {0}" },
                    { "domains_count", "Всего доменов: {0}"},
                    { "confirm_remove_domain", "Вы уверены, что хотите удалить домен {0}?"},
                    { "domain_added_success", "Домен '{0}' успешно добавлен"},
                    { "domain_removed_success", "Домен '{0}' успешно удален"},
                    { "no_domains_in_list", "В списке пока нет доменов" },
                    { "language_setting", "Язык" }
                };
            }
            else if (languageCode == "en")
            {
                return new Dictionary<string, string>
                {
                    { "title", "Zapret CLI {0} by SillyApps" },
                    { "navigation", "Movement: ↑↓ arrows\nConfirmation: Enter key" },
                    { "menu_start", "Start Service" },
                    { "menu_stop", "Stop Service" },
                    { "menu_status", "Service Status" },
                    { "menu_edit", "Edit Lists" },
                    { "menu_update", "Update Application" },
                    { "menu_test", "Test Profiles" },
                    { "menu_settings", "Settings" },
                    { "menu_exit", "Exit" },
                    { "other_options", "Not all items fit on the screen. Use the ↑ and ↓ arrows to scroll through the list." },
                    { "select_list", "Select a list to edit" },
                    { "general_list", "General list" },
                    { "exclude_list", "Exclude list" },
                    { "back", "Go back" },
                    { "add_domain", "Add a domain" },
                    { "empty_domain", "The domain cannot be empty" },
                    { "domain_already_in_list", "The domain is already on the list" },
                    { "update_select", "Select what you want to update" },
                    { "app_settings", "App settings" },
                    { "auto_run", "Autorun" },
                    { "enabled", "Enabled" },
                    { "disabled", "Disabled" },
                    { "not_specified", "Not specified" },
                    { "auto_profile", "Auto profile" },
                    { "game_filter", "Filter for games" },
                    { "delete_service", "Remove Zapret service (NOT the app!)" },
                    { "no_profiles", "No profiles available (try update profiles in the menu)" },
                    { "auto_profile_title", "Select a profile for autostart" },
                    { "language_changed", "Language changed to English" },
                    { "updating_no_profiles", "No profiles available. Updating..." },
                    { "no_profiles_after_update", "No profiles available after update. Something went wrong?" },
                    { "available_profiles", "Available profiles" },
                    { "test_domain_ask", "Enter a blocked domain for testing" },
                    { "update_ask", "Update?" },
                    { "zapret_start_timeout", "Timeout waiting for windivert initialization (>{0} seconds). Is something went wrong?" },
                    { "zapret_start", "Zapret started successfully with profile '{0}'!" },
                    { "zapret_start_fail", "Failed to start zapret: {0}" },
                    { "zapret_stop_fail", "Failed to stop zapret: {0}" },
                    { "zapret_stop", "Zapret stopped successfully" },
                    { "profile_not_selected", "No profile selected" },
                    { "profile", "Profile" },
                    { "description", "Description" },
                    { "arguments", "Arguments" },
                    { "running", "Running" },
                    { "stopped", "Stopped" },
                    { "service_status", "Service status" },
                    { "status", "Status" },
                    { "working_directory", "Working directory" },
                    { "press_any_key", "Press any key" },
                    { "profile_not_found", "Profile '{0}' not found" },
                    { "checking_for_updates", "Checking for updates..." },
                    { "hosts", "Hosts" },
                    { "ips", "IPs" },
                    { "ip_subnets", "IP/subnets" },
                    { "desync_profiles", "Desync profiles" },
                    { "low_priority_profile", "Low priority profile" },
                    { "parsed_profile", "Parsed profile" },
                    { "profile_parse_fail", "Failed to parse profile from" },
                    { "profile_load_fail", "Failed to load profile" },
                    { "new_version_title", "New version of Zapret available" },
                    { "new_version", "New version" },
                    { "current_version", "Current version" },
                    { "changelog", "Changelog" },
                    { "first_launch", "It looks like this is your first launch. Installing the latest version of Zapret..." },
                    { "updates_check_fail", "Failed to check updates" },
                    { "new_cli_version_title", "New Zapret CLI version" },
                    { "cli_updates_check_fail", "Failed to check Zapret CLI updates" },
                    { "downloading", "Downloading..." },
                    { "launching_installer", "Launching installer..." },
                    { "close_app", "Closing application..." },
                    { "update_fail", "Failed to update" },
                    { "zapret_updated", "Update installed successfully" },
                    { "services_stop", "Services and processes stopped successfully" },
                    { "services_stop_fail", "Failed to stop some services/processes" },
                    { "service_stop_fail", "Service {0} could not be stopped, attempting to delete anyway" },
                    { "service_delete_fail", "Failed to stop/delete service" },
                    { "process_exited_with_fail", "Process {0} did not exit gracefully" },
                    { "process_terminate_fail", "Failed to terminate process" },
                    { "processes_terminate_fail", "Error checking processes" },
                    { "extract_success", "Archive extracted successfully" },
                    { "extract_fail", "Failed to extract archive" },
                    { "files_copy", "Copying files..." },
                    { "lists_update", "Updating lists..." },
                    { "profiles_proccess", "Processing profiles..." },
                    { "profiles_proccessed", "Found and processed {0} profiles" },
                    { "temp_cleanup_fail", "Failed to cleanup temp files" },
                    { "domain_add_fail", "Failed to add domain" },
                    { "profile_testing_title", "Profile Testing" },
                    { "close_apps_warning", "Close all third-party applications for the best testing experience (e.g. Discord, VPN, etc.)" },
                    { "domain_accessible_warning", "{0} appears to be accessible without bypass. Testing may not be accurate." },
                    { "continue_anyway", "Continue testing anyway?" },
                    { "no_profiles_to_test", "No profiles available to test" },
                    { "starting_tests", "Starting tests for {0} with {1} profiles" },
                    { "testing_profile", "Testing profile, {0}" },
                    { "init_failed", "Failed to initialize" },
                    { "http_success", "HTTP {0}, Connection successful" },
                    { "success", "SUCCESS" },
                    { "http_fail", "HTTP {0}, Connection failed" },
                    { "failed", "FAILED" },
                    { "conn_timeout", "Connection timed out" },
                    { "error_occurred", "An error occurred" },
                    { "test_results", "Test Results Summary" },
                    { "profiles_bypassed", "{0} profile(s) successfully bypassed blocking" },
                    { "no_profiles_bypassed", "No profiles successfully bypassed the blocking. May God save us." },
                    { "domain_remove_fail", "Failed to remove domain" },
                    { "search_placeholder", "Type to search..." },
                    { "add_new_domain_to", "Enter a domain to add to {0}" },
                    { "domains_count", "Total domains: {0}"},
                    { "confirm_remove_domain", "Are you sure you want to remove domain {0}?"},
                    { "domain_added_success", "Domain '{0}' successfully added"},
                    { "domain_removed_success", "Domain '{0}' successfully removed"},
                    { "no_domains_in_list", "No domains in the list yet" },
                    { "language_setting", "Language" }
                };
            }
            else
            {
                return null;
            }
        }

        public string GetString(string key, params object[] args)
        {
            try
            {
                if (_currentTranslations.TryGetValue(key, out var value))
                {
                    return args.Length > 0 ? string.Format(value, args) : value;
                }

                _logger.LogWarning($"Translation key not found: {key}");
                return $"{{{key}}}";
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error getting translation for key '{key}': {ex.Message}", ex);
                return $"{{{key}}}";
            }
        }

        public string GetCurrentLanguage()
        {
            return _currentLanguage;
        }

        public async Task SetLanguageAsync(string languageCode)
        {
            if (_availableLanguages.ContainsKey(languageCode))
            {
                _logger.LogInformation($"Changing language from {_currentLanguage} to {languageCode}");
                _currentLanguage = languageCode;

                var config = _configService.GetConfig();
                config.Language = languageCode;
                _configService.SaveConfig();

                LoadTranslations(languageCode);
            }
            else
            {
                _logger.LogWarning($"Attempt to set unsupported language: {languageCode}");
            }
        }

        public Dictionary<string, string> GetAvailableLanguages()
        {
            return _availableLanguages;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _logger?.LogDebug("Disposing LocalizationService");
                }
                _disposed = true;
            }
        }
    }
}