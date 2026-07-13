using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using Timer_for_Donaton.Classes;

namespace Timer_for_Donaton
{
    /// <summary>
    /// Логика взаимодействия для MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public TimerWebSocket TimerWebSocketInstance { get; private set; }
        public MoneyTimeConverter MoneyTimeConverterInstance { get; private set; }
        public DonationAlertsClient DonationAlertsInstance { get; private set; }
        public DonatePayClient DonatePayInstance { get; private set; }
        public AppConfig AppConfigInstance { get; private set; }

        private Settings settings_window;
        private readonly Logger _logger;

        private readonly string APP_DIRECTORY = AppDomain.CurrentDomain.BaseDirectory;
        public static bool isTimerOn = false;
        public long total_seconds = 0;
        private bool _timerLoopRunning = false;

        public MainWindow()
        {
            InitializeComponent();

            TimerWebSocketInstance = new TimerWebSocket();
            MoneyTimeConverterInstance = new MoneyTimeConverter();
            DonationAlertsInstance = new DonationAlertsClient();
            DonatePayInstance = new DonatePayClient();
            AppConfigInstance = new AppConfig();

            _logger = new Logger(this);

            var config = AppConfigInstance.LoadConfig();

            // Применяем сохранённую тему
            ThemeManager.Apply(config.DarkTheme ? ThemeManager.AppTheme.Dark : ThemeManager.AppTheme.Light);
            UpdateThemeButton();

            total_seconds = LoadRemainingTime();
            Timer_TextBox.Text = TimeCipher();
            Timer_TextBox.MaxLength = 10;

            // Курс времени к рублю
            MoneyTimeConverterInstance.SetRate(config.PlusTime, config.MinusMoney);

            // События DonationAlerts
            DonationAlertsInstance.OnDonation += HandleDonation;
            DonationAlertsInstance.OnConnected += () => Dispatcher.Invoke(SetDonationAlertsConnected);
            DonationAlertsInstance.OnError += msg => Dispatcher.Invoke(() => MessageBox.Show(msg));

            // События DonatePay
            DonatePayInstance.OnDonation += HandleDonation;
            DonatePayInstance.OnConnected += () => Dispatcher.Invoke(SetDonatePayConnected);
            DonatePayInstance.OnError += msg => Dispatcher.Invoke(() => MessageBox.Show(msg));

            // Кнопки +/− (общая логика вместо четырёх дублирующихся веток)
            PlusTime_Button.Click += (s, e) => AdjustTime(+1);
            MinusTime_Button.Click += (s, e) => AdjustTime(-1);

            // Автоподключение при старте (если включено в настройках)
            if (config.AutoConnect)
            {
                if (!string.IsNullOrWhiteSpace(config.DALink))
                    _ = DonationAlertsInstance.StartAsync(Timer_for_Donaton.Settings.ExtractAccessToken(config.DALink));
                if (!string.IsNullOrWhiteSpace(config.DonatePayToken))
                    _ = DonatePayInstance.StartAsync(config.DonatePayToken.Trim());
                if (!TimerWebSocketInstance.IsServerStarted)
                    _ = TimerWebSocketInstance.StartServer();
            }
        }

        // ---------------- Таймер ----------------

        private async void InitializeTimer()
        {
            if (_timerLoopRunning) return; // защита от двойного запуска при быстрых кликах
            _timerLoopRunning = true;
            try
            {
                if (NegativeTimeCheck()) return;
                // Дрейф-устойчивый отсчёт: считаем реально прошедшее время по монотонным часам
                // (Stopwatch), а не суммируем интервалы Task.Delay. Даже если программа
                // подлагивает или тик задержался, вычтем ровно столько секунд, сколько
                // прошло на самом деле — за дни накопленной ошибки не будет.
                var sw = System.Diagnostics.Stopwatch.StartNew();
                long applied = 0; // сколько секунд уже вычтено за текущий запуск отсчёта
                while (isTimerOn)
                {
                    await System.Threading.Tasks.Task.Delay(250);
                    long elapsed = sw.ElapsedMilliseconds / 1000; // целых секунд реально прошло
                    long delta = elapsed - applied;
                    if (delta <= 0) continue; // целая секунда ещё не набралась
                    total_seconds -= delta;
                    applied = elapsed;
                    Timer_TextBox.Text = TimeCipher();
                    TimerWebSocketInstance.UpdateTime(TimeCipher());
                    if (NegativeTimeCheck()) break;
                }
            }
            finally
            {
                _timerLoopRunning = false;
            }
        }

        private bool NegativeTimeCheck()
        {
            if (total_seconds <= 0)
            {
                isTimerOn = false;
                total_seconds = 0;
                Timer_TextBox.Text = TimeCipher();
                ChangeStartIcon();
                return true;
            }
            return false;
        }

        private void ChangeStartIcon()
        {
            // Векторная иконка из Icons.xaml — цвет берётся из темы, не пикселизуется.
            StartIcon.Data = (Geometry)FindResource(isTimerOn ? "Icon.Pause" : "Icon.Play");
            double size = isTimerOn ? 24 : 26;
            StartIcon.Width = size;
            StartIcon.Height = size;
        }

        private long TimeDecipher(string cipheredTime)
        {
            var parts = cipheredTime.Split(':');
            long hours = Convert.ToInt64(parts[0]);
            long minutes = Convert.ToInt64(parts[1]);
            long seconds = Convert.ToInt64(parts[2]);
            return (hours * 3600) + (minutes * 60) + seconds;
        }

        private string TimeCipher()
        {
            long hours = total_seconds / 3600;
            long minutes = (total_seconds / 60) % 60;
            long seconds = total_seconds % 60;
            return $"{hours:D2}:{minutes:D2}:{seconds:D2}";
        }

        private static bool IsNumeric(string input) => long.TryParse(input, out _);

        private void AdjustTime(int sign)
        {
            if (!IsNumeric(PlusMinusTime_TextBox.Text)) return;

            long value = Convert.ToInt64(PlusMinusTime_TextBox.Text);
            total_seconds = TimeDecipher(Timer_TextBox.Text);

            long delta;
            switch (Currency_ComboBox.Text)
            {
                case "Ч": delta = value * 3600; break;
                case "м": delta = value * 60; break;
                case "с": delta = value; break;
                default: delta = MoneyTimeConverterInstance.ConvertMoneyToTime(value); break; // ₽
            }

            total_seconds += sign * delta;
            if (sign < 0) NegativeTimeCheck();
            Timer_TextBox.Text = TimeCipher();
        }

        // ---------------- Обработка донатов (единая для всех сервисов) ----------------

        private void HandleDonation(Donation donation)
        {
            Dispatcher.Invoke(() =>
            {
                string date = DateTime.Now.ToString();

                if (donation.AddsTime)
                {
                    total_seconds = TimeDecipher(Timer_TextBox.Text);
                    long addSeconds = MoneyTimeConverterInstance.ConvertMoneyToTime(donation.Amount.Value);
                    total_seconds += addSeconds;
                    Timer_TextBox.Text = TimeCipher();
                    if (isTimerOn) TimerWebSocketInstance.UpdateTime(TimeCipher());

                    _logger.Log(donation.Service,
                        $"{date} (Донат) — {donation.Username} отправил {donation.Amount} {donation.Currency} " +
                        $"(Добавлено {addSeconds} секунд к таймеру)",
                        isDonation: true, addedSeconds: addSeconds);
                }
                else
                {
                    // Неденежное событие (Twitch): только лог
                    _logger.Log(donation.Service, $"{date} {donation.RawText}");
                }
            });
        }

        /// <summary>Откат доната: вычитает ранее начисленное время из таймера (вызывается из окна логов).</summary>
        public void RollbackSeconds(long seconds)
        {
            if (seconds <= 0) return;
            total_seconds = TimeDecipher(Timer_TextBox.Text);
            total_seconds -= seconds;
            if (total_seconds < 0) total_seconds = 0;
            Timer_TextBox.Text = TimeCipher();
            if (isTimerOn) TimerWebSocketInstance.UpdateTime(TimeCipher());
        }

        /// <summary>Возврат отката: снова добавляет ранее вычтенное время (повторное нажатие кнопки в логах).</summary>
        public void AddSeconds(long seconds)
        {
            if (seconds <= 0) return;
            total_seconds = TimeDecipher(Timer_TextBox.Text);
            total_seconds += seconds;
            Timer_TextBox.Text = TimeCipher();
            if (isTimerOn) TimerWebSocketInstance.UpdateTime(TimeCipher());
        }

        private void SetDonationAlertsConnected()
        {
            DAStatus_Label.Visibility = Visibility.Hidden;
            if (settings_window != null && settings_window.IsVisible)
            {
                settings_window.ConnectDA_Button.Content = "Подключен";
                settings_window.ConnectDA_Button.IsEnabled = false;
            }
        }

        private void SetDonatePayConnected()
        {
            DPStatus_Label.Visibility = Visibility.Hidden;
            if (settings_window != null && settings_window.IsVisible)
            {
                settings_window.ConnectDP_Button.Content = "Подключен";
                settings_window.ConnectDP_Button.IsEnabled = false;
            }
        }

        private long LoadRemainingTime()
        {
            string filePath = Path.Combine(APP_DIRECTORY, "remaining_time.txt");
            if (File.Exists(filePath) && long.TryParse(File.ReadAllText(filePath), out long value))
            {
                return value;
            }
            return 0;
        }

        // ---------------- Обработчики UI ----------------

        private void StartTimer_Click(object sender, RoutedEventArgs e)
        {
            if (!Regex.IsMatch(Timer_TextBox.Text, @"^(?:[0-9]{1,4}):[0-6][0-9]:[0-6][0-9]$"))
            {
                MessageBox.Show("Введен неверный формат", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            isTimerOn = !isTimerOn;
            total_seconds = TimeDecipher(Timer_TextBox.Text);
            ChangeStartIcon();
            if (isTimerOn) InitializeTimer();
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            if (settings_window == null || !settings_window.IsVisible)
            {
                settings_window = new Settings(TimerWebSocketInstance, MoneyTimeConverterInstance,
                    DonationAlertsInstance, DonatePayInstance, AppConfigInstance) { Owner = this };
                settings_window.Show();
            }
            else settings_window.Activate();
        }

        private void LogsWindow_Click(object sender, RoutedEventArgs e)
        {
            _logger.OpenLogWindow();
        }

        private void Theme_Click(object sender, RoutedEventArgs e)
        {
            ThemeManager.Toggle();
            UpdateThemeButton();

            // Сохраняем выбор темы, не трогая остальные настройки
            var config = AppConfigInstance.LoadConfig();
            config.DarkTheme = ThemeManager.Current == ThemeManager.AppTheme.Dark;
            AppConfigInstance.SaveConfig(config);
        }

        private void UpdateThemeButton()
        {
            // В тёмной теме показываем солнце (перейти на светлую), в светлой — луну.
            ThemeIcon.Data = (Geometry)FindResource(ThemeManager.Current == ThemeManager.AppTheme.Dark ? "Icon.Sun" : "Icon.Moon");
        }

        private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            File.WriteAllText(Path.Combine(APP_DIRECTORY, "remaining_time.txt"), total_seconds.ToString());
            _ = DonationAlertsInstance.StopAsync();
            _ = DonatePayInstance.StopAsync();
        }
    }
}
