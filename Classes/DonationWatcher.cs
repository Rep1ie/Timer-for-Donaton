using System;
using System.Threading.Tasks;
using System.Windows;
using System.Diagnostics;
using System.IO;

namespace Timer_for_Donaton.Classes
{
    public class DonationWatcher
    {
        public bool isListening = false;

        public event Action<string> OnAnswerRecieved;
        private string _accessToken;  // Токен авторизации
        private Process _process;  // Процесс python скрипта

        public async Task StartListening(string accessToken)
        {
            _accessToken = accessToken;
            if (_accessToken == "") MessageBox.Show("Не правильный формат ссылки на виджет DonationAlerts. Возможно, вы не сохранили настройки.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            try
            {
                isListening = true;

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = $"{AppDomain.CurrentDomain.BaseDirectory}\\DonationWatcher.exe", // Укажите путь к exe скрипту
                    Arguments = $"{_accessToken}", // токен в качестве аргумента
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardInput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (_process = new Process { StartInfo = startInfo })
                {
                    _process.Start();

                    // Пример отправки данных в Python-скрипт
                    using (var writer = _process.StandardInput)
                    {
                        if (writer.BaseStream.CanWrite)
                        {
                            await writer.WriteLineAsync("IS_OPENED");
                        }
                    }

                    // Асинхронное чтение вывода
                    var outputTask = ReadOutputAsync(_process.StandardOutput);
                    var errorTask = ReadOutputAsync(_process.StandardError);

                    await Task.WhenAll(outputTask, errorTask);

                    await Task.Run(() => _process.WaitForExit());
                }
                
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка подключения к DA: {ex}");
            } 
        }

        public async Task StopListening()
        {
            await Task.Run(() =>
            {
                // Завершение процесса через объект Process
                if (_process != null)
                {
                    try
                    {
                        if (!_process.HasExited)
                        {
                            _process.Kill();
                            _process.WaitForExit();
                        }
                    }
                    catch (Exception ex)
                    {
                        File.AppendAllText($"{AppDomain.CurrentDomain.BaseDirectory}\\error_log.txt", $"[{DateTime.Now}] Ошибка при завершении основного процесса: {ex.Message}\n");
                    }
                    finally
                    {
                        _process.Dispose();
                        _process = null;
                    }
                }

                // Дополнительно завершаем процессы по имени
                KillProcessesByName("DonationWatcher");
            });
        }

        async Task ReadOutputAsync(StreamReader reader)
        {
            while (!reader.EndOfStream)
            {
                var message = await reader.ReadLineAsync();

                OnAnswerRecieved?.Invoke(message);
            }
        }

        public void UpdateAccessToken(string newToken)
        {
            _accessToken = newToken;
        }


        private void KillProcessesByName(string processName)
        {
            DateTime time_now = DateTime.Now;
            try
            {
                // Находим все процессы с заданным именем
                Process[] processes = Process.GetProcessesByName(processName);
                foreach (var process in processes)
                {
                    try
                    {
                        process.Kill();
                        process.WaitForExit();
                    }
                    catch (Exception ex)
                    {
                        File.AppendAllText($"{AppDomain.CurrentDomain.BaseDirectory}\\error_log.txt", $"[{time_now}] KillProcessesByName: Не удалось завершить процесс {processName} (ID: {process.Id}): {ex.Message}\n");
                    }
                }
            }
            catch (Exception ex)
            {
                File.AppendAllText($"{AppDomain.CurrentDomain.BaseDirectory}\\error_log.txt", $"[{time_now}] KillProcessesByName: Ошибка при поиске процессов {processName}: {ex.Message}\n");
            }
        }

        //private void KillProcessesByPath(string exePath)  // Не работает
        //{
        //    try
        //    {
        //        // Получаем список всех процессов
        //        Process[] allProcesses = Process.GetProcesses();
        //        foreach (var process in allProcesses)
        //        {
        //            try
        //            {
        //                // Проверяем путь запуска процесса
        //                if (!process.HasExited && process.MainModule.FileName.Equals(exePath, StringComparison.OrdinalIgnoreCase))
        //                {
        //                    process.Kill();
        //                    process.WaitForExit();
        //                    File.AppendAllText($"{AppDomain.CurrentDomain.BaseDirectory}\\log.txt", $"KillProcessesByPath: Процесс {exePath} (ID: {process.Id}) завершен\n");
        //                }
        //            }
        //            catch (Exception ex)
        //            {
        //                File.AppendAllText($"{AppDomain.CurrentDomain.BaseDirectory}\\log.txt", $"KillProcessesByPath: Не удалось завершить процесс {exePath} (ID: {process.Id}): {ex.Message}\n");
        //            }
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        File.AppendAllText($"{AppDomain.CurrentDomain.BaseDirectory}\\log.txt", $"KillProcessesByPath: Ошибка при проверке процессов для {exePath}: {ex.Message}\n");
        //    }
        //}
    }
}
