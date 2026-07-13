using System;
using System.Windows;
using System.Windows.Controls;
using Timer_for_Donaton.Classes;

namespace Timer_for_Donaton
{
    public partial class LogsWindow : Window
    {
        private readonly Logger _logger;

        public LogsWindow(Logger logger)
        {
            InitializeComponent();

            _logger = logger;
            Logs_ItemsControl.ItemsSource = _logger.Entries;

            // Автопрокрутка вниз при добавлении новой записи.
            _logger.Entries.CollectionChanged += (s, e) => ScrollToEnd();
            Loaded += (s, e) => ScrollToEnd();
        }

        private void ScrollToEnd()
        {
            // BeginInvoke — чтобы прокрутка выполнилась после пересчёта разметки.
            Dispatcher.BeginInvoke(new Action(() => Logs_ScrollViewer.ScrollToEnd()));
        }

        private void Rollback_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is LogEntry entry)
            {
                _logger.ToggleRollback(entry);
            }
        }

        private void ClearLogs_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Очистить всю историю логов? Это действие нельзя отменить.",
                "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes) _logger.ClearHistory();
        }

        private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
