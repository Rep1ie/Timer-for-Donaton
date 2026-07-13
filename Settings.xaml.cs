using System;
using System.Drawing.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Timer_for_Donaton.Classes;

namespace Timer_for_Donaton
{
    /// <summary>
    /// Логика взаимодействия для Settings.xaml
    /// </summary>
    public partial class Settings : Window
    {
        private readonly TimerWebSocket TimerWebSocketInstance;
        private readonly MoneyTimeConverter MoneyTimeConverterInstance;
        private readonly DonationAlertsClient DonationAlertsInstance;
        private readonly DonatePayClient DonatePayInstance;
        private readonly AppConfig AppConfigInstance;

        private bool _isLoaded = false;

        public Settings(TimerWebSocket timerWebSocket, MoneyTimeConverter moneyTimeConverter,
            DonationAlertsClient donationWatcher, DonatePayClient donatePayClient, AppConfig appConfig)
        {
            InitializeComponent();

            TimerWebSocketInstance = timerWebSocket;
            MoneyTimeConverterInstance = moneyTimeConverter;
            DonationAlertsInstance = donationWatcher;
            DonatePayInstance = donatePayClient;
            AppConfigInstance = appConfig;

            PopulateFonts();
            LoadSavedConfig();
            ReflectConnectionState();
            WireChangeTracking();

            _isLoaded = true;

            // Начальное состояние: всё сохранено (зелёный индикатор, кнопка неактивна)
            SetSaveState(false);
        }

        private void PopulateFonts()
        {
            using (var fonts = new InstalledFontCollection())
            {
                foreach (var family in fonts.Families)
                {
                    Font_ComboBox.Items.Add(family.Name);
                }
            }
        }

        private void LoadSavedConfig()
        {
            var config = AppConfigInstance.LoadConfig();
            DALink_PasswordBox.Password = config.DALink ?? "";
            DPLink_PasswordBox.Password = config.DonatePayToken ?? "";
            PlusTime_TextBox.Text = config.PlusTime;
            MinusMoney_TextBox.Text = config.MinusMoney;
            Font_ComboBox.Text = config.Font;
            TextColor_ComboBox.Text = config.TextColor;
            BorderSize_ComboBox.Text = config.BorderSize;
            BorderColor_ComboBox.Text = config.BorderColor;
            AutoConnect_CheckBox.IsChecked = config.AutoConnect;
        }

        private void ReflectConnectionState()
        {
            if (DonationAlertsInstance.IsListening)
            {
                ConnectDA_Button.Content = "Подключен";
                ConnectDA_Button.IsEnabled = false;
            }
            if (DonatePayInstance.IsListening)
            {
                ConnectDP_Button.Content = "Подключен";
                ConnectDP_Button.IsEnabled = false;
            }
        }

        /// <summary>Подписываемся на изменения полей, чтобы активировать кнопку «Сохранить».</summary>
        private void WireChangeTracking()
        {
            TextChangedEventHandler onText = (s, e) => EnableSave();
            SelectionChangedEventHandler onSelection = (s, e) => EnableSave();

            PlusTime_TextBox.TextChanged += onText;
            MinusMoney_TextBox.TextChanged += onText;
            Font_ComboBox.SelectionChanged += onSelection;
            Font_ComboBox.AddHandler(TextBoxBase.TextChangedEvent, onText);
            TextColor_ComboBox.SelectionChanged += onSelection;
            TextColor_ComboBox.AddHandler(TextBoxBase.TextChangedEvent, onText);
            BorderSize_ComboBox.SelectionChanged += onSelection;
            BorderSize_ComboBox.AddHandler(TextBoxBase.TextChangedEvent, onText);
            BorderColor_ComboBox.SelectionChanged += onSelection;
            BorderColor_ComboBox.AddHandler(TextBoxBase.TextChangedEvent, onText);
            AutoConnect_CheckBox.Checked += (s, e) => EnableSave();
            AutoConnect_CheckBox.Unchecked += (s, e) => EnableSave();
        }

        private void EnableSave()
        {
            if (_isLoaded) SetSaveState(true);
        }

        /// <summary>
        /// Обновляет индикатор сохранения: красная точка/обводка — есть несохранённые изменения,
        /// зелёная — всё сохранено.
        /// </summary>
        private void SetSaveState(bool hasUnsaved)
        {
            SaveSettings_Button.IsEnabled = hasUnsaved;
            var brush = (Brush)FindResource(hasUnsaved ? "Brush.Status.Error" : "Brush.Status.Success");
            SaveIndicator.Fill = brush;
            SaveSettings_Button.BorderBrush = brush;
            SaveSettings_Button.BorderThickness = new Thickness(2);
            SaveIndicator.ToolTip = hasUnsaved
                ? "Есть несохранённые изменения — нажмите «Сохранить»"
                : "Все изменения сохранены";
        }

        // ---------------- Подключение сервисов ----------------

        private void ConnectDA_Button_Click(object sender, RoutedEventArgs e)
        {
            string token = ExtractAccessToken(DALink_PasswordBox.Password);
            if (string.IsNullOrWhiteSpace(token))
            {
                MessageBox.Show("Введите ссылку на виджет DonationAlerts.");
                return;
            }

            ConnectDA_Button.IsEnabled = false;
            ConnectDA_Button.Content = "Подключение...";
            // Долгоживущая задача — не дожидаемся завершения, статус обновит событие OnConnected.
            _ = DonationAlertsInstance.StartAsync(token);
        }

        private void ConnectDP_Button_Click(object sender, RoutedEventArgs e)
        {
            // ВАЖНО: для DonatePay нужен API-токен (donatepay.ru/page/api), а НЕ ссылка на виджет.
            string token = DPLink_PasswordBox.Password?.Trim();
            if (string.IsNullOrWhiteSpace(token))
            {
                MessageBox.Show("Введите API-токен DonatePay (со страницы https://donatepay.ru/page/api).");
                return;
            }

            ConnectDP_Button.IsEnabled = false;
            ConnectDP_Button.Content = "Подключение...";
            _ = DonatePayInstance.StartAsync(token);
        }

        /// <summary>Извлекает token из ссылки на виджет; если token= не найден, возвращает ввод как есть.</summary>
        internal static string ExtractAccessToken(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return "";
            const string key = "token=";
            int idx = url.IndexOf(key, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return url.Trim();
            string rest = url.Substring(idx + key.Length);
            int amp = rest.IndexOf('&');
            return (amp >= 0 ? rest.Substring(0, amp) : rest).Trim();
        }

        // ---------------- OBS ----------------

        private void CreateOBSLink_Button_Click(object sender, RoutedEventArgs e)
        {
            if (!TimerWebSocketInstance.IsServerStarted)
            {
                _ = TimerWebSocketInstance.StartServer();
            }
            OBSLink_TextBox.Text = "http://localhost:8080/";
        }

        private void CopyOBSLink_Button_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(OBSLink_TextBox.Text)) return;

            // Clipboard.SetText периодически бросает COM/ExternalException, когда буфер обмена
            // временно заблокирован другим процессом (частый баг WPF). Раньше это роняло программу —
            // теперь оборачиваем в try с парой повторов.
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    Clipboard.SetDataObject(OBSLink_TextBox.Text, true);
                    return;
                }
                catch (Exception)
                {
                    System.Threading.Thread.Sleep(80);
                }
            }

            MessageBox.Show(
                "Не удалось скопировать ссылку: буфер обмена занят другой программой. Скопируйте вручную: двойной клик по полю → Ctrl+C.",
                "Буфер обмена занят", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void OBSLink_TextBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            OBSLink_TextBox.SelectAll();
        }

        // ---------------- Сохранение ----------------

        private void SaveSettings_Button_Click(object sender, RoutedEventArgs e)
        {
            if (!MoneyTimeConverterInstance.SetRate(PlusTime_TextBox.Text, MinusMoney_TextBox.Text))
            {
                MessageBox.Show("Неверные значения начисления времени: укажите целые числа, рубли не равны 0.");
                return;
            }

            var config = AppConfigInstance.LoadConfig();
            config.DALink = DALink_PasswordBox.Password;
            config.DonatePayToken = DPLink_PasswordBox.Password;
            config.PlusTime = PlusTime_TextBox.Text;
            config.MinusMoney = MinusMoney_TextBox.Text;
            config.Font = Font_ComboBox.Text;
            config.TextColor = TextColor_ComboBox.Text;
            config.BorderSize = BorderSize_ComboBox.Text;
            config.BorderColor = BorderColor_ComboBox.Text;
            config.DarkTheme = ThemeManager.Current == ThemeManager.AppTheme.Dark;
            config.AutoConnect = AutoConnect_CheckBox.IsChecked == true;
            AppConfigInstance.SaveConfig(config);

            // Применяем стили к таймеру в OBS
            TimerWebSocketInstance.Font = Font_ComboBox.Text;
            TimerWebSocketInstance.TextColor = ConvertColor(TextColor_ComboBox.Text);
            TimerWebSocketInstance.BorderSize = BorderSize_ComboBox.Text;
            TimerWebSocketInstance.BorderColor = ConvertColor(BorderColor_ComboBox.Text);
            _ = TimerWebSocketInstance.BroadcastStyles();

            SetSaveState(false);
        }

        /// <summary>Переводит русское название цвета в CSS-цвет.</summary>
        private string ConvertColor(string color)
        {
            switch (color)
            {
                case "Нет":
                case "Прозрачный": return "transparent";
                case "Черный": return "black";
                case "Белый": return "white";
                case "Серый": return "grey";
                case "Желтый": return "#FBE77D";
                case "Оранжевый": return "#FEA351";
                case "Красный": return "#F96266";
                case "Розовый": return "#FE69B1";
                case "Фиолетовый": return "#AA96DA";
                case "Синий": return "#6883BA";
                case "Голубой": return "#AEEEED";
                case "Зеленый": return "#3A6B33";
                default: return color;
            }
        }

        // ---------------- Прочее ----------------

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && SaveSettings_Button.IsEnabled)
            {
                SaveSettings_Button_Click(sender, e);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
