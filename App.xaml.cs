using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace Timer_for_Donaton
{
    /// <summary>
    /// Логика взаимодействия для App.xaml.
    /// Здесь же — глобальный перехватчик ошибок, чтобы случайное исключение в UI
    /// не роняло всё приложение (раньше это выглядело как «MainWindow внезапно закрылся»).
    /// </summary>
    public partial class App : Application
    {
        private static readonly string ErrorLogPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error_logs.txt");

        public App()
        {
            // Ошибки в UI-потоке: логируем, показываем сообщение и продолжаем работу.
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            // Фатальные ошибки из фоновых потоков: хотя бы запишем в лог перед завершением.
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            WriteError("UI", e.Exception);
            MessageBox.Show(
                "Произошла ошибка, но программа продолжит работу.\n\n" + e.Exception.Message +
                "\n\nПодробности записаны в error_logs.txt рядом с программой.",
                "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true; // не даём приложению упасть
        }

        private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            WriteError("FATAL", e.ExceptionObject as Exception);
        }

        private static void WriteError(string scope, Exception ex)
        {
            if (ex == null) return;
            try
            {
                File.AppendAllText(ErrorLogPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{scope}] {ex.GetType().Name}: {ex.Message}\r\n{ex.StackTrace}\r\n\r\n");
            }
            catch { /* логирование не должно само ронять приложение */ }
        }
    }
}
