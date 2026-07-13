using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Timer_for_Donaton.Classes
{
    /// <summary>
    /// Нативный клиент DonationAlerts на C# — без Python и DonationWatcher.exe.
    ///
    /// Раньше донаты принимал отдельный процесс DonationWatcher.exe (~11 МБ, PyInstaller + socket.io),
    /// а приложение читало его stdout и парсило текст. Это был костыль: лишний процесс,
    /// зависающие бутлоадеры PyInstaller, повторный парсинг строк. Здесь тот же источник
    /// (виджет-сокет DonationAlerts) реализован напрямую через ClientWebSocket.
    ///
    /// Протокол — Socket.IO v2 поверх Engine.IO v3 (тот же, что использовал прежний watcher):
    ///   URL: wss://socket.donationalerts.ru/socket.io/?EIO=3&amp;transport=websocket
    ///   1. Сервер присылает Engine.IO open: 0{"sid":...,"pingInterval":25000,...}
    ///   2. Сервер присылает Socket.IO connect: 40
    ///   3. Клиент эмитит событие add-user: 42["add-user",{"token":"ВИДЖЕТ_ТОКЕН","type":"alert_widget"}]
    ///   4. Сервер шлёт события доната: 42["donation","{...json...}"]
    ///   Heartbeat: клиент раз в pingInterval шлёт "2" (ping), сервер отвечает "3" (pong).
    ///
    /// Токен берётся из ссылки на виджет (параметр token=), как и прежде — настройка не меняется.
    /// В error_logs.txt пишутся ТОЛЬКО ошибки.
    ///
    /// Публичный интерфейс совместим с DonatePayClient: StartAsync/StopAsync/IsListening + события.
    /// </summary>
    public class DonationAlertsClient
    {
        private const string SocketUrl =
            "wss://socket.donationalerts.ru/socket.io/?EIO=3&transport=websocket";

        private static readonly string ErrorLogPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error_logs.txt");

        public bool IsListening { get; private set; }

        public event Action<Donation> OnDonation;
        public event Action OnConnected;
        public event Action<string> OnError;

        private ClientWebSocket _socket;
        private CancellationTokenSource _cts;
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        private string _token;
        private bool _connectedOnce;
        private int _pingIntervalMs = 25000;

        // alert_type -> человекочитаемое описание Twitch-события (время не начисляют, только лог).
        private static readonly Dictionary<string, string> TwitchAlerts = new Dictionary<string, string>
        {
            { "11", "Twitch Битсы" },
            { "16", "Подаренные подписки Twitch" },
            { "17", "Рейд Twitch" },
            { "6",  "Бесплатная подписка Twitch" },
            { "4",  "Платная подписка Twitch" },
            { "13", "Подарочные подписки Twitch" },
        };

        public Task StartAsync(string token)
        {
            if (IsListening) return Task.CompletedTask;
            _token = token?.Trim();
            if (string.IsNullOrEmpty(_token))
            {
                OnError?.Invoke("Неверная ссылка на виджет DonationAlerts. Возможно, вы не сохранили настройки.");
                return Task.CompletedTask;
            }

            // TLS 1.2 на случай, если система не включает его по умолчанию (защита от ошибок TLS при переподключении).
            try { System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12; } catch { /* ignore */ }

            _connectedOnce = false;
            _cts = new CancellationTokenSource();
            IsListening = true;
            _ = Task.Run(() => RunAsync(_cts.Token));
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            IsListening = false;
            try { _cts?.Cancel(); } catch { /* ignore */ }
            // Abort() не бросает исключений на уже прерванном сокете — не засоряем error_logs.
            try { _socket?.Abort(); } catch { /* ignore */ }
            try { _socket?.Dispose(); } catch { /* ignore */ }
            _socket = null;
            try { _cts?.Dispose(); } catch { /* ignore */ }
            _cts = null;
            return Task.CompletedTask;
        }

        // ---------------- Основной цикл с переподключением ----------------

        private async Task RunAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && IsListening)
            {
                try
                {
                    await ConnectAndListenAsync(ct);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    WriteLog("RunAsync: " + ex.Message);
                    if (!_connectedOnce)
                    {
                        IsListening = false;
                        OnError?.Invoke("Не удалось подключиться к DonationAlerts: " + ex.Message +
                            "\n\nПодробности — в файле error_logs.txt рядом с программой. " +
                            "Проверьте ссылку на виджет DonationAlerts в настройках.");
                        break;
                    }
                }
                finally
                {
                    _socket?.Dispose();
                    _socket = null;
                }

                if (ct.IsCancellationRequested || !IsListening) break;
                try { await Task.Delay(TimeSpan.FromSeconds(10), ct); } catch { break; }
            }
            IsListening = false;
        }

        private async Task ConnectAndListenAsync(CancellationToken ct)
        {
            var connCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            try
            {
                var ws = new ClientWebSocket();
                await ws.ConnectAsync(new Uri(SocketUrl), connCts.Token);
                _socket = ws;

                bool namespaceConnected = false;
                while (!connCts.IsCancellationRequested)
                {
                    string msg = await ReceiveTextAsync(connCts.Token);
                    if (msg == null) return;          // сокет закрыт
                    if (msg.Length == 0) continue;

                    char type = msg[0];

                    if (type == '0')                  // Engine.IO open
                    {
                        TryParsePingInterval(msg.Substring(1));
                        _ = HeartbeatAsync(connCts.Token);
                    }
                    else if (type == '2')             // сервер прислал ping -> отвечаем pong
                    {
                        await SendRawAsync("3", connCts.Token);
                    }
                    else if (type == '3')             // pong на наш ping — игнорируем
                    {
                    }
                    else if (type == '4')             // Socket.IO message
                    {
                        char sio = msg.Length > 1 ? msg[1] : ' ';
                        string rest = msg.Length > 2 ? msg.Substring(2) : "";

                        if (sio == '0')               // connect к пространству имён
                        {
                            if (!namespaceConnected)
                            {
                                namespaceConnected = true;
                                await EmitAddUserAsync(connCts.Token);
                                _connectedOnce = true;
                                OnConnected?.Invoke();
                            }
                        }
                        else if (sio == '2')          // event
                        {
                            HandleEvent(rest);
                        }
                        else if (sio == '1' || sio == '4') // disconnect / error
                        {
                            WriteLog("Socket.IO закрыл соединение: " + msg);
                            return;
                        }
                    }
                }
            }
            finally
            {
                try { connCts.Cancel(); } catch { /* ignore */ }
                connCts.Dispose();
            }
        }

        private async Task HeartbeatAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested && _socket != null && _socket.State == WebSocketState.Open)
                {
                    await Task.Delay(_pingIntervalMs, ct);
                    if (ct.IsCancellationRequested || _socket == null || _socket.State != WebSocketState.Open) break;
                    await SendRawAsync("2", ct);
                }
            }
            catch { /* останавливается вместе с сокетом */ }
        }

        private async Task EmitAddUserAsync(CancellationToken ct)
        {
            var payload = new JObject
            {
                ["token"] = _token,
                ["type"] = "alert_widget"
            };
            var frame = new JArray { "add-user", payload };
            await SendRawAsync("42" + frame.ToString(Formatting.None), ct);
        }

        // ---------------- Разбор событий ----------------

        private void HandleEvent(string rest)
        {
            try
            {
                var arr = JArray.Parse(rest);
                if (arr.Count < 1) return;
                string eventName = (string)arr[0];
                if (!string.Equals(eventName, "donation", StringComparison.OrdinalIgnoreCase)) return;
                if (arr.Count < 2) return;

                // payload приходит как строка JSON (реже — как объект).
                JToken payloadToken = arr[1];
                JObject data = payloadToken.Type == JTokenType.String
                    ? JObject.Parse((string)payloadToken)
                    : payloadToken as JObject;
                if (data == null) return;

                string alertType = data.Value<string>("alert_type");
                string username = data.Value<string>("username");
                string amountRaw = data.Value<string>("amount");
                string currency = data.Value<string>("currency");

                if (string.IsNullOrWhiteSpace(username)) username = "Аноним";
                if (string.IsNullOrWhiteSpace(currency)) currency = "RUB";

                if (alertType == "1")   // денежный донат
                {
                    OnDonation?.Invoke(new Donation
                    {
                        Service = "DonationAlerts",
                        Username = username,
                        Amount = ParseDecimal(amountRaw),
                        Currency = currency
                    });
                }
                else if (alertType != null && TwitchAlerts.TryGetValue(alertType, out string label))
                {
                    OnDonation?.Invoke(new Donation
                    {
                        Service = "Twitch",
                        Username = username,
                        Amount = null,
                        RawText = $"({label}) — {username}, {amountRaw}"
                    });
                }
            }
            catch (Exception ex) { WriteLog("HandleEvent: " + ex.Message + " | raw=" + rest); }
        }

        // ---------------- WebSocket I/O ----------------

        private async Task SendRawAsync(string text, CancellationToken ct)
        {
            var socket = _socket;
            if (socket == null || socket.State != WebSocketState.Open) return;
            var bytes = Encoding.UTF8.GetBytes(text);
            await _sendLock.WaitAsync(ct);
            try
            {
                await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
            }
            finally { _sendLock.Release(); }
        }

        private async Task<string> ReceiveTextAsync(CancellationToken ct)
        {
            var buffer = new byte[8192];
            var sb = new StringBuilder();
            while (true)
            {
                var result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    WriteLog("WS закрыт сервером: " + result.CloseStatus + " " + result.CloseStatusDescription);
                    return null;
                }
                sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                if (result.EndOfMessage) break;
            }
            return sb.ToString();
        }

        // ---------------- Утилиты ----------------

        private void TryParsePingInterval(string json)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(json)) return;
                var obj = JObject.Parse(json);
                var pi = (int?)obj["pingInterval"];
                if (pi.HasValue && pi.Value > 1000) _pingIntervalMs = pi.Value;
            }
            catch { /* оставляем значение по умолчанию */ }
        }

        private static decimal ParseDecimal(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0m;
            s = s.Replace(',', '.');
            return decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal v) ? v : 0m;
        }

        private static void WriteLog(string message)
        {
            try
            {
                File.AppendAllText(ErrorLogPath, $"[{DateTime.Now}] (DonationAlertsClient.cs) {message}{Environment.NewLine}");
            }
            catch { /* логирование не должно ронять приложение */ }
        }
    }
}
