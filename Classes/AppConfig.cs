using System;
using System.IO;
using Newtonsoft.Json;

namespace Timer_for_Donaton.Classes
{
    public class AppConfig
    {
        public string DALink { get; set; } = "";
        public string PlusTime { get; set; } = "600";
        public string MinusMoney { get; set; } = "140";
        public string Font { get; set; } = "Arial";
        public string TextColor { get; set; } = "Белый";
        public string BorderSize { get; set; } = "1";
        public string BorderColor { get; set; } = "Черный";

        public AppConfig LoadConfig()
        {
            string configFilePath = $"{AppDomain.CurrentDomain.BaseDirectory}\\appsettings.json";

            if (!File.Exists(configFilePath)) return new AppConfig();  // Вернуть новый объект с настройками по умолчанию
            var json = File.ReadAllText(configFilePath);

            return JsonConvert.DeserializeObject<AppConfig>(json);  // Возвращается форматированный объект класса с настройками
        }

        public void SaveConfig(AppConfig settings)
        {
            string configFilePath = $"{AppDomain.CurrentDomain.BaseDirectory}\\appsettings.json";

            var json = JsonConvert.SerializeObject(settings, Formatting.Indented);
            File.WriteAllText(configFilePath, json);
        }
    }
}
