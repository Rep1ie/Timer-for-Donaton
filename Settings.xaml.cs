using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Drawing.Text;
using Timer_for_Donaton.Classes;

namespace Timer_for_Donaton
{
    public partial class Settings : Window
    {
        // Объявляем событие для изменения DALink
        public event EventHandler<string> DALinkChanged;

        private TimerWebSocket TimerWebSocketInstance;           // Объект для управления сервером
        private MoneyTimeConverter MoneyTimeConverterInstance;   // Объект для рассчета времени за донат
        private DonationWatcher DonationWatcherInstance;
        private AppConfig AppConfigInstance;

        public Settings(TimerWebSocket timerWebSocket, MoneyTimeConverter moneyTimeConverter, DonationWatcher donationWatcher, AppConfig appConfig)
        {
            InitializeComponent();

            TimerWebSocketInstance = timerWebSocket;              // Присваеваем экземпляр класса серверирования
            MoneyTimeConverterInstance = moneyTimeConverter;      // Присваеваем экземпляр класса конвертирования
            DonationWatcherInstance = donationWatcher;
            AppConfigInstance = appConfig;


            DALinkChanged += OnDALinkChanged;

            // Если сервер уже запущен, его нельзя запустить снова
            if (TimerWebSocketInstance.IsServerStarted)
            {
                CreateOBSLink_Button.IsEnabled = false;
                OBSLink_TextBox.Text = "http://localhost:8080/";
            }

            // Если уже подключен к DonationAlerts, то кнопка disabled
            if (DonationWatcherInstance.isListening)
            {
                ConnectDA_Button.IsEnabled = false;
            }

            // Скан шрифтов на пк и добавление их в ComboBox
            var fonts = new InstalledFontCollection();
            foreach (var font in fonts.Families)
            {
                Font_ComboBox.Items.Add(font.Name);
            }

            // Загружаем сохраненный конфиг настроек и применяем к элементам управления
            LoadSavedConfig();

            // Подписываемся на события изменения значений в настройках
            DALink_PasswordBox.PasswordChanged += Control_ValueChanged;
            PlusTime_TextBox.TextChanged += Control_ValueChanged;
            MinusMoney_TextBox.TextChanged += Control_ValueChanged;
            // Для комбобоксов динамически добавляем обработчики событий TextChanged, т.к. их по дефолту нет
            Font_ComboBox.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler(Control_ValueChanged));
            TextColor_ComboBox.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler(Control_ValueChanged));
            BorderSize_ComboBox.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler(Control_ValueChanged));
            BorderColor_ComboBox.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler(Control_ValueChanged));

            // (TextColor_ComboBox.SelectedItem as ComboBoxItem)?.Content.ToString();  <----- интересная конструкция

            // Отправляем в калькулятор курс времени к рублю
            MoneyTimeConverterInstance.CurrentCourse(PlusTime: PlusTime_TextBox.Text, MinusMoney: MinusMoney_TextBox.Text);
        }

        private void Control_ValueChanged(object sender, EventArgs e)
        {
            // Проверка: изменилось ли значение в настройках
            SaveSettings_Button.IsEnabled = true;
        }

        private string ConvertColor(string color)
        {
            string converted_color;
            switch (color)
            {
                case "Нет":
                    converted_color = "transparent";
                    break;
                case "Прозрачный":
                    converted_color = "transparent";
                    break;
                case "Черный":
                    converted_color = "black";
                    break;
                case "Белый":
                    converted_color = "white";
                    break;
                case "Серый":
                    converted_color = "grey";
                    break;
                case "Желтый":
                    converted_color = "#FBE77D";
                    break;
                case "Оранжевый":
                    converted_color = "#FEA351";
                    break;
                case "Красный":
                    converted_color = "#F96266";
                    break;
                case "Розовый":
                    converted_color = "#FE69B1";
                    break;
                case "Фиолетовый":
                    converted_color = "#AA96DA";
                    break;
                case "Синий":
                    converted_color = "#6883BA";
                    break;
                case "Голубой":
                    converted_color = "#AEEEED";
                    break;
                case "Зеленый":
                    converted_color = "#3A6B33";
                    break;
                default:
                    converted_color = color;
                    break;
            }
            
            return converted_color;
        }

        private void LoadSavedConfig()
        {
            var config = AppConfigInstance.LoadConfig();

            // Применение настроек к элементам управления
            DALink_PasswordBox.Password = config.DALink;
            PlusTime_TextBox.Text = config.PlusTime;
            MinusMoney_TextBox.Text = config.MinusMoney;
            Font_ComboBox.SelectedValue = config.Font;
            TextColor_ComboBox.SelectedValue = config.TextColor;
            BorderSize_ComboBox.SelectedValue = config.BorderSize;
            BorderColor_ComboBox.SelectedValue = config.BorderColor;
        }

        private void OnDALinkChanged(object sender, string newDALink)
        {
            string newAccessToken = ExtractAccessToken(newDALink) ?? "";
            DonationWatcherInstance.UpdateAccessToken(newAccessToken);
        }

        private string ExtractAccessToken(string URL)
        {
            // Проверка на пустую строку или null
            if (string.IsNullOrEmpty(URL))
            {
                return null;
            }
            // Поиск индекса "token="
            int tokenIndex = URL.IndexOf("token=");
            if (tokenIndex == -1)
            {
                return null;
            }

            string at = URL.Substring(URL.IndexOf("token=") + 6);
            return at;
        }

        private void ConnectDA_Button_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(DALink_PasswordBox.Password))
            {
                MessageBox.Show("Ссылка не может быть пустой", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            else if (DALink_PasswordBox.Password.IndexOf("token=") == -1)
            {
                MessageBox.Show("В указанном URL токен не найден", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            else
            {
                DonationWatcherInstance.UpdateAccessToken(ExtractAccessToken(DALink_PasswordBox.Password));
                Task.Run(() => DonationWatcherInstance.StartListening(ExtractAccessToken(DALink_PasswordBox.Password)));
            }
        }

        private void CreateOBSLink_Button_Click(object sender, RoutedEventArgs e)
        {
            // Передаем стили в экземпляр вебсокета
            TimerWebSocketInstance.Font = Font_ComboBox.Text;                                    // Шрифт
            TimerWebSocketInstance.TextColor = ConvertColor(TextColor_ComboBox.Text);            // Цвет текста
            TimerWebSocketInstance.BorderSize = BorderSize_ComboBox.Text;                        // Размер обводки
            TimerWebSocketInstance.BorderColor = ConvertColor(BorderColor_ComboBox.Text);        // Цвет контура

            // Запускаем сервер
            Task.Run(async () =>
            {
                await TimerWebSocketInstance.StartServer();
            });

            OBSLink_TextBox.Text = "http://localhost:8080/";

            CreateOBSLink_Button.IsEnabled = false;
        }

        private void CopyOBSLink_Button_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (OBSLink_TextBox.Text != "")
                {
                    Clipboard.SetText(OBSLink_TextBox.Text);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message.Contains("0x800401D0") ? "Буфер обмена занят другой программой" : $"{ex}", "Предупреждение", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void SaveSettings_Button_Click(object sender, RoutedEventArgs e)
        {
            var config = new AppConfig
            {
                DALink = DALink_PasswordBox.Password,
                PlusTime = PlusTime_TextBox.Text,
                MinusMoney = MinusMoney_TextBox.Text,
                Font = Font_ComboBox.Text,
                TextColor = TextColor_ComboBox.Text,
                BorderSize = BorderSize_ComboBox.Text,
                BorderColor = BorderColor_ComboBox.Text,
            };
            AppConfigInstance.SaveConfig(config);

            // Вызываем событие, чтобы оповестить об изменении
            DALinkChanged?.Invoke(this, config.DALink);     // Для динамичного изменения токена в DonationWatcher

            // Отправляем в калькулятор курс времени к рублю
            MoneyTimeConverterInstance.CurrentCourse(PlusTime: PlusTime_TextBox.Text, MinusMoney: MinusMoney_TextBox.Text);

            if (TimerWebSocketInstance.IsServerStarted)
            {
                // Обновляем стили в экземпляре WebSocket
                TimerWebSocketInstance.Font = Font_ComboBox.Text;
                TimerWebSocketInstance.TextColor = ConvertColor(TextColor_ComboBox.Text);
                TimerWebSocketInstance.BorderSize = BorderSize_ComboBox.Text;
                TimerWebSocketInstance.BorderColor = ConvertColor(BorderColor_ComboBox.Text);

                // Обновляем сервер
                Task.Run(async () =>
                {
                    await TimerWebSocketInstance.BroadcastStyles();
                    await TimerWebSocketInstance.BroadcastTime();
                });
            }

            SaveSettings_Button.IsEnabled = false;
        }

        // Выделить весь текстбокс даблкликом
        private void OBSLink_TextBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is TextBox OBSLink_TextBox)
            {
                OBSLink_TextBox.SelectAll();
            }
        }

        // При нажатии Enter сохранялись настройки
        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (SaveSettings_Button.IsEnabled)
                {
                    SaveSettings_Button_Click(sender, e);
                }
            }
        }
    }
}
