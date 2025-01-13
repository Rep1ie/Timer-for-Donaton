using System.Collections.Generic;

namespace Timer_for_Donaton.Classes
{
    public class Logger
    {
        public readonly List<string> _logs = new List<string>();
        private LogsWindow _logsWindow;
        MainWindow _mainWindow;

        public void Log(string message)
        {
            _logs.Add(message); // Записываем все логи в список

            _logsWindow?.AddLog(message);  // Если окно логов открыто, то сделать запись
        }

        public Logger(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
        }

        public void OpenLogWindow()
        {
            if (_logsWindow == null || !_logsWindow.IsVisible)
            {
                _logsWindow = new LogsWindow();
                _logsWindow.Owner = _mainWindow;
                _logsWindow.Show();

                // Отображаем все ранее записанные логи, когда окно открывается
                foreach (var log in _logs)
                {
                    _logsWindow.AddLog(log);
                }
            }
            else _logsWindow.Activate();
        }
    }
}
