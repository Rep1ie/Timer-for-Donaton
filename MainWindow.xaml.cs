using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Timer_for_Donaton.Classes;

namespace Timer_for_Donaton
{
    /// <summary>
    /// Логика взаимодействия для MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        // Создание общего экземпляра для всех классов проекта
        public TimerWebSocket TimerWebSocketInstance { get; private set; }
        public MoneyTimeConverter MoneyTimeConverterInstance { get; private set; }
        public DonationWatcher DonationWatcherInstance { get; private set; }
        public AppConfig AppConfigInstance { get; private set; }

        private Settings settings_window;
        private readonly Logger _logger;

        private string APP_DIRECTORY = AppDomain.CurrentDomain.BaseDirectory;
        public static bool isTimerOn = false;
        public long total_seconds = 0;

        public MainWindow()
        {
            InitializeComponent();

            TimerWebSocketInstance = new TimerWebSocket();
            MoneyTimeConverterInstance = new MoneyTimeConverter();
            AppConfigInstance = new AppConfig();

            _logger = new Logger(this);

            var config = AppConfigInstance.LoadConfig();  // Загружаем конфиг настроек приложения

            // Присваеваем текущему времени таймера время из сохраненного конфига
            long remaining_time = LoadRemainingTime();
            total_seconds = remaining_time;
            Timer_TextBox.Text = TimeСipher();

            DonationWatcherInstance = new DonationWatcher();
            DonationWatcherInstance.OnAnswerRecieved += OnAnswerRecieved;  // Создаем метод OnAnswerRecieved, принимающий ответы от py скрипта

            // Отправляем в калькулятор курс времени к рублю
            MoneyTimeConverterInstance.CurrentCourse(PlusTime: config.PlusTime, MinusMoney: config.MinusMoney);

            Timer_TextBox.MaxLength = 10;

            // События на уменьшение/добавление времени к таймеру 
            if (total_seconds >= 0)
            {
                PlusTime_Button.Click += (s, e) =>
                {
                    if (IsNumeric(PlusMinusTime_TextBox.Text))
                    {
                        if (Currency_ComboBox.Text == "Ч")
                        {
                            total_seconds = TimeDecipher(Timer_TextBox.Text);
                            total_seconds += Convert.ToInt64(PlusMinusTime_TextBox.Text) * 3600;
                            Timer_TextBox.Text = TimeСipher();
                        }
                        else if (Currency_ComboBox.Text == "м")
                        {
                            total_seconds = TimeDecipher(Timer_TextBox.Text);
                            total_seconds += Convert.ToInt64(PlusMinusTime_TextBox.Text) * 60;
                            Timer_TextBox.Text = TimeСipher();
                        }
                        else if (Currency_ComboBox.Text == "с")
                        {
                            total_seconds = TimeDecipher(Timer_TextBox.Text);
                            total_seconds += Convert.ToInt64(PlusMinusTime_TextBox.Text);
                            Timer_TextBox.Text = TimeСipher();
                        }
                        else
                        {
                            total_seconds = TimeDecipher(Timer_TextBox.Text);
                            total_seconds += MoneyTimeConverterInstance.ConvertMoneyToTime(PlusMinusTime_TextBox.Text);
                            Timer_TextBox.Text = TimeСipher();
                        }
                    }
                };
                MinusTime_Button.Click += (s, e) =>
                {
                    if (IsNumeric(PlusMinusTime_TextBox.Text))
                    {
                        if (Currency_ComboBox.Text == "Ч")
                        {
                            total_seconds = TimeDecipher(Timer_TextBox.Text);
                            total_seconds -= Convert.ToInt64(PlusMinusTime_TextBox.Text) * 3600;
                            NegativeTimeCheck();
                            Timer_TextBox.Text = TimeСipher();
                        }
                        else if (Currency_ComboBox.Text == "м")
                        {
                            total_seconds = TimeDecipher(Timer_TextBox.Text);
                            total_seconds -= Convert.ToInt64(PlusMinusTime_TextBox.Text) * 60;
                            NegativeTimeCheck();
                            Timer_TextBox.Text = TimeСipher();
                        }
                        else if (Currency_ComboBox.Text == "с")
                        {
                            total_seconds = TimeDecipher(Timer_TextBox.Text);
                            total_seconds -= Convert.ToInt64(PlusMinusTime_TextBox.Text);
                            NegativeTimeCheck();
                            Timer_TextBox.Text = TimeСipher();
                        }
                        else
                        {
                            total_seconds = TimeDecipher(Timer_TextBox.Text);
                            total_seconds -= MoneyTimeConverterInstance.ConvertMoneyToTime(PlusMinusTime_TextBox.Text);
                            NegativeTimeCheck();
                            Timer_TextBox.Text = TimeСipher();
                        }
                    }
                };
            }
        }

        // Асинхронный метод по инициализации таймера
        async private void InitializeTimer()
        {
            NegativeTimeCheck(); // Проверка, не равно общее кол-во секунд нулю? (с самого запуска)
            while (isTimerOn)
            {
                total_seconds--;
                Timer_TextBox.Text = TimeСipher();

                // Передаём остаток времени в экземпляр TimerWebSocket
                TimerWebSocketInstance.UpdateTime(TimeСipher());

                if (NegativeTimeCheck()) break;
                await Task.Delay(1000);
            }
        }

        // Проверка, чтобы время было выше 0
        private bool NegativeTimeCheck()
        {
            if (total_seconds <= 0)
            {
                isTimerOn = false;
                total_seconds = 0;
                Timer_TextBox.Text = TimeСipher();
                ChangeStartIcon();
                return true;
            }
            else
            {
                return false;
            }
        }

        // Смена иконки у кнопки старта/паузы
        private void ChangeStartIcon()
        {
            if (isTimerOn)
            {
                StartImage.Source = new BitmapImage(new Uri("/Resources/icon_pause.png", UriKind.Relative));
                StartImage.Width = 32;
                StartImage.Height = 32;
            }
            else
            {
                StartImage.Source = new BitmapImage(new Uri("/Resources/icon_start.png", UriKind.Relative));
                StartImage.Width = 29;
                StartImage.Height = 29;
            }

        }

        // Перевести время из вида "часы:минуты:секунды" в общее кол-во секунд
        private long TimeDecipher(string CipheredTime)
        {
            List<int> colons = new List<int>()
            {
                CipheredTime.IndexOf(":"), CipheredTime.IndexOf(":", CipheredTime.IndexOf(":")+1)
            };
            long hours = Convert.ToInt64($"{CipheredTime.Substring(0, colons[0])}");
            long minutes = Convert.ToInt64($"{CipheredTime.Substring(colons[0]+1, 2)}");
            long seconds = Convert.ToInt64($"{CipheredTime.Substring(colons[1]+1)}");
            long total_seconds = (hours*3600) + (minutes*60) + seconds;
            return total_seconds;
        }

        // Перевести из общего кол-ва секунд в вид "часы:минуты:секунды"
        private string TimeСipher()
        {
            long hours = total_seconds / 3600;
            long minutes = (total_seconds / 60) % 60;
            long seconds = total_seconds % 60;
            string ciphered_time = $"{hours:D2}:{minutes:D2}:{seconds:D2}";
            return ciphered_time;
        }

        private bool IsNumeric(string input)
        {
            int number;
            bool isNumeric = int.TryParse(input, out number);
            return isNumeric;
        }

        private static string ExtractValue(string input, string key)
        {
            // Создание словаря для пар ключ-значение
            var values = new Dictionary<string, string>();

            // Разделение строки по запятой
            var pairs = input.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var pair in pairs)
            {
                // Разделение по двоеточию и удаление пробелов и обратных кавычек
                var parts = pair.Split(new[] { ':' }, 2);
                if (parts.Length == 2)
                {
                    var k = parts[0].Trim();
                    var v = parts[1].Trim().Trim('`');
                    values[k] = v;
                }
            }

            // Извлечение значения по ключу
            return values.TryGetValue(key, out var value) ? value : null;
        }

        private long LoadRemainingTime()
        {
            string filePath = $"{APP_DIRECTORY}\\remaining_time.txt";
            if (File.Exists(filePath))
            {
                return Convert.ToInt64(File.ReadAllText(filePath));
            }
            return 0;
        }

        private void OnAnswerRecieved(string message)
        {
            string date = ExtractValue(message, "date");
            string username = ExtractValue(message, "username");
            string amount = ExtractValue(message, "amount");

            if (message.Contains("alert_type: `1`"))      // Донат
            {
                Application.Current.Dispatcher.Invoke(() =>
                {  // Выполняем изменение UI в главном потоке
                    total_seconds = TimeDecipher(Timer_TextBox.Text);
                    long add_seconds = MoneyTimeConverterInstance.ConvertMoneyToTime(amount);
                    total_seconds += add_seconds;
                    Timer_TextBox.Text = TimeСipher();

                    _logger.Log($"{date} (Донат) — {username} отправил {amount} {ExtractValue(message, "currency")} " +
                        $"(Добавлено {add_seconds} секунд к таймеру)");
                });
            }
            else if (message.Contains("true"))            // Файл открыт
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    settings_window.ConnectDA_Button.Content = "Подключен";
                    settings_window.ConnectDA_Button.IsEnabled = false;

                    DAStatus_Label.Content = "DonationAlerts подключен!";
                    DAStatus_Label.Foreground = new SolidColorBrush(Color.FromRgb(6, 118, 85));
                    DAStatus_Label.Margin = new Thickness(168, 0, 153, 0);
                });
            }
            else if (message.Contains("alert_type: `11`"))    // Twitch Битсы
            {
                Application.Current.Dispatcher.Invoke(() => { _logger.Log($"{date} (Twitch Битсы) — {username} отправил {amount} Bits"); });
            }
            else if (message.Contains("alert_type: `16`"))    // Подписки подаренные каналу Twitch
            {
                Application.Current.Dispatcher.Invoke(() => { _logger.Log($"{date} (Подписки подаренные каналу Twitch) — {username} подарил {amount} подписок"); });
            }
            else if (message.Contains("alert_type: `17`"))    // Рейд Twitch
            {
                Application.Current.Dispatcher.Invoke(() => { _logger.Log($"{date} (Рейд Twitch) — {username} зарейдил с {amount} зрителями"); });
            }
            else if (message.Contains("alert_type: `6`"))    // Бесплатная подписка Twitch
            {
                Application.Current.Dispatcher.Invoke(() => { _logger.Log($"{date} (Бесплатная подписка Twitch) — {username} зафолловился на канал"); });
            }
            else if (message.Contains("alert_type: `4`"))    // Платная подписка Twitch
            {
                Application.Current.Dispatcher.Invoke(() => { _logger.Log($"{date} (Платная подписка Twitch) — {username} оформил платную подписку на канал"); });
            }
            else if (message.Contains("alert_type: `13`"))    // Подарочные подписки Twitch
            {
                Application.Current.Dispatcher.Invoke(() => { _logger.Log($"{date} (Подарочные подписки Twitch) — {username} подарил {amount} подписок"); });
            }
            else if (message.Contains(". Возможно, вы используете VPN."))
            {
                Application.Current.Dispatcher.Invoke(() => { MessageBox.Show(message); });
            }
        }

        // Событие на клик по кнопке
        private void StartTimer_Click(object sender, RoutedEventArgs e)
        {
            isTimerOn = !isTimerOn; // Переключаем состояние

            if (Regex.IsMatch(Timer_TextBox.Text, @"^(?:[0-9]{1,4}):[0-6][0-9]:[0-6][0-9]$")) // Проверка на соответствие формату
            {
                total_seconds = TimeDecipher(Timer_TextBox.Text);
                ChangeStartIcon(); // Смена иконки
                InitializeTimer();
            }
            else
            {
                MessageBox.Show("Введен неверный формат", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            if (settings_window == null || !settings_window.IsVisible)  // Проверка, открыто ли уже окно с настройками
            {
                settings_window = new Settings(TimerWebSocketInstance, MoneyTimeConverterInstance, DonationWatcherInstance, AppConfigInstance);  // Передаем экземпляр в новое окно при создании
                settings_window.Owner = this; // Устанавливаем основное окно владельцем
                settings_window.Show();
            }
            else settings_window.Activate(); // активация существующего окна
        }

        private void LogsWindow_Click(object sender, RoutedEventArgs e)
        {
            _logger.OpenLogWindow();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            File.WriteAllText($"{APP_DIRECTORY}\\remaining_time.txt", total_seconds.ToString());
            _ = DonationWatcherInstance.StopListening();
        }
    }
}
