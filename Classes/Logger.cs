using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using Newtonsoft.Json;

namespace Timer_for_Donaton.Classes
{
    /// <summary>
    /// Одна запись лога. Хранит данные, нужные для отката доната (сумма времени), и умеет
    /// уведомлять UI об изменении состояния (INotifyPropertyChanged) — например, когда донат откатили.
    /// Сериализуется в logs_history.json, поэтому история переживает перезапуск приложения.
    /// </summary>
    public class LogEntry : INotifyPropertyChanged
    {
        public string Service { get; set; }
        public string Message { get; set; }

        /// <summary>Денежный ли это донат (для него доступен откат).</summary>
        public bool IsDonation { get; set; }

        /// <summary>Сколько секунд было начислено за этот донат (для отката).</summary>
        public long AddedSeconds { get; set; }

        private bool _rolledBack;
        public bool RolledBack
        {
            get => _rolledBack;
            set
            {
                if (_rolledBack == value) return;
                _rolledBack = value;
                Raise(nameof(RolledBack));
                Raise(nameof(CanRollback));
                Raise(nameof(TextDecorations));
                Raise(nameof(RollbackTooltip));
                Raise(nameof(RollbackIconOpacity));
            }
        }

        /// <summary>Кнопка отката активна только для не откаченного денежного доната.</summary>
        [JsonIgnore]
        public bool CanRollback => IsDonation && AddedSeconds > 0;

        /// <summary>Подсказка на кнопке зависит от состояния: откатить или вернуть время.</summary>
        [JsonIgnore]
        public string RollbackTooltip => RolledBack
            ? "Вернуть начисленное время обратно в таймер"
            : "Откатить донат — вычесть начисленное время из таймера";

        /// <summary>Иконка приглушается, когда донат уже откачен.</summary>
        [JsonIgnore]
        public double RollbackIconOpacity => RolledBack ? 0.45 : 1.0;

        /// <summary>Цвет строки по сервису; для неизвестных — цвет текста активной темы.</summary>
        [JsonIgnore]
        public Brush Color
        {
            get
            {
                switch (Service)
                {
                    case "Twitch": return new SolidColorBrush(System.Windows.Media.Color.FromRgb(160, 110, 255));
                    case "DonationAlerts": return new SolidColorBrush(System.Windows.Media.Color.FromRgb(242, 111, 17));
                    case "DonatePay": return new SolidColorBrush(System.Windows.Media.Color.FromRgb(78, 201, 90));
                    default:
                        return Application.Current?.TryFindResource("Brush.Text.Primary") as Brush ?? Brushes.Gray;
                }
            }
        }

        /// <summary>Откаченный донат отображается зачёркнутым.</summary>
        [JsonIgnore]
        public TextDecorationCollection TextDecorations =>
            RolledBack ? System.Windows.TextDecorations.Strikethrough : null;

        public event PropertyChangedEventHandler PropertyChanged;
        private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>
    /// Хранит историю логов (с сохранением на диск) и прокидывает её в окно логов.
    /// Источник истины — ObservableCollection, к которой напрямую привязано окно логов,
    /// поэтому новые записи и откаты обновляют UI автоматически.
    /// </summary>
    public class Logger
    {
        private readonly MainWindow _mainWindow;
        private LogsWindow _logsWindow;

        public ObservableCollection<LogEntry> Entries { get; } = new ObservableCollection<LogEntry>();

        private static string HistoryFilePath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs_history.json");

        public Logger(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
            LoadHistory();
        }

        public void Log(string service, string message, bool isDonation = false, long addedSeconds = 0)
        {
            Entries.Add(new LogEntry
            {
                Service = service,
                Message = message,
                IsDonation = isDonation,
                AddedSeconds = addedSeconds
            });
            SaveHistory();
        }

        /// <summary>Откатывает донат: вычитает начисленное время из таймера и помечает запись.</summary>
        public void ToggleRollback(LogEntry entry)
        {
            if (entry == null || !entry.CanRollback) return;
            if (!entry.RolledBack)
            {
                _mainWindow.RollbackSeconds(entry.AddedSeconds);
                entry.RolledBack = true;
            }
            else
            {
                _mainWindow.AddSeconds(entry.AddedSeconds);
                entry.RolledBack = false;
            }
            SaveHistory();
        }

        public void ClearHistory()
        {
            Entries.Clear();
            SaveHistory();
        }

        public void OpenLogWindow()
        {
            if (_logsWindow == null || !_logsWindow.IsVisible)
            {
                _logsWindow = new LogsWindow(this) { Owner = _mainWindow };
                _logsWindow.Show();
            }
            else
            {
                _logsWindow.Activate();
            }
        }

        private void LoadHistory()
        {
            try
            {
                if (!File.Exists(HistoryFilePath)) return;
                var json = File.ReadAllText(HistoryFilePath);
                var saved = JsonConvert.DeserializeObject<List<LogEntry>>(json);
                if (saved == null) return;
                foreach (var entry in saved) Entries.Add(entry);
            }
            catch { /* битый файл истории не должен ронять приложение */ }
        }

        private void SaveHistory()
        {
            try
            {
                File.WriteAllText(HistoryFilePath, JsonConvert.SerializeObject(Entries, Formatting.Indented));
            }
            catch { /* запись истории не должна ронять приложение */ }
        }
    }
}
