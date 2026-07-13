using System;
using System.IO;
using Newtonsoft.Json;

namespace Timer_for_Donaton.Classes
{
    /// <summary>
    /// Настройки приложения, сериализуемые в appsettings.json.
    /// </summary>
    public class AppConfig
    {
        public string DALink { get; set; } = "";
        public string DonatePayToken { get; set; } = "";
        public string PlusTime { get; set; } = "600";
        public string MinusMoney { get; set; } = "140";
        public string Font { get; set; } = "Arial";
        public string TextColor { get; set; } = "Белый";
        public string BorderSize { get; set; } = "1";
        public string BorderColor { get; set; } = "Черный";
        public bool DarkTheme { get; set; } = false;

        /// <summary>Автоматически подключать DonationAlerts, DonatePay и запускать сервер OBS при старте.</summary>
        public bool AutoConnect { get; set; } = false;

        [JsonIgnore]
        private static string ConfigFilePath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");

        /// <summary>Загружает конфиг. При отсутствии/повреждении файла возвращает настройки по умолчанию.</summary>
        public AppConfig LoadConfig()
        {
            try
            {
                if (!File.Exists(ConfigFilePath)) return new AppConfig();
                var json = File.ReadAllText(ConfigFilePath);
                return JsonConvert.DeserializeObject<AppConfig>(json) ?? new AppConfig();
            }
            catch
            {
                // Битый JSON не должен ронять приложение на старте.
                return new AppConfig();
            }
        }

        public void SaveConfig(AppConfig settings)
        {
            var json = JsonConvert.SerializeObject(settings, Formatting.Indented);
            File.WriteAllText(ConfigFilePath, json);
        }
    }
}
