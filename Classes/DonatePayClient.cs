using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Timer_for_Donaton.Classes
{
    /// <summary>
    /// Real-time клиент DonatePay через WebSocket (Centrifugo) — нативно на C#, без Python/exe.
    ///
    /// Зачем сокет, а не REST: тестовые донаты DonatePay не пишутся в историю /api/v1/transactions,
    /// они приходят только в real-time канал. Поэтому для тестов и мгновенного начисления нужен сокет.
    ///
    /// Протокол (Centrifugo v2, как у виджета DonatePay):
    ///   1. GET  https://donatepay.ru/api/v1/user?access_token=ТОКЕН            -> data.id (ID пользователя)
    ///   2. POST https://donatepay.ru/api/v2/socket/token { access_token }       -> connection token (JWT)
    ///   3. WS connect -> команда connect (method=0) { token }                   -> client id
    ///   4. POST /api/v2/socket/token { access_token, client, channels:["$public:ID"] } -> subscription token
    ///   5. команда subscribe (method=1) { channel, token }
    ///   6. слушаем publication-сообщения с донатами.
    ///
    /// ВАЖНО: нужен API-токен со страницы https://donatepay.ru/page/api (тот же, что вводится в настройках).
    ///
    /// Прошлый адрес :43002 умер, поэтому пробуем несколько адресов по очереди. В error_logs.txt
    /// рядом с программой пишутся ТОЛЬКО ошибки — если подключиться не удалось, лог покажет причину.
    ///
    /// Публичный интерфейс (StartAsync/StopAsync/события/IsListening) не менялся.
    /// </summary>
    public class DonatePayClient
    {
        // Старый :43002 больше не отвечает, поэтому сначала пробуем стандартный 443.
        private static readonly string[] SocketUrls =
        {
            "wss://centrifugo.donatepay.ru/connection/websocket",
            "wss://centrifugo.donatepay.ru:43002/connection/websocket"
        };
        private const string SocketTokenUrl = "https://donatepay.ru/api/v2/socket/token";
        private const string UserApiUrl = "https://donatepay.ru/api/v1/user?access_token=";

        private static readonly string ErrorLogPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error_logs.txt");

        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        public bool IsListening { get; private set; }

        public event Action<Donation> OnDonation;
        public event Action OnConnected;
        public event Action<string> OnError;

        private ClientWebSocket _socket;
        private CancellationTokenSource _cts;
        private string _accessToken;
        private int _commandId;
        private bool _connectedOnce;
        private readonly Queue<JObject> _inbox = new Queue<JObject>();
        private readonly HashSet<string> _seen = new HashSet<string>();

        public Task StartAsync(string accessToken)
        {
            if (IsListening) return Task.CompletedTask;
            _accessToken = accessToken?.Trim();
            if (string.IsNullOrEmpty(_accessToken))
            {
                OnError?.Invoke("Не указан API-токен DonatePay.");
                return Task.CompletedTask;
            }

            _connectedOnce = false;
            _cts = new CancellationTokenSource();
            IsListening = true;
            _ = Task.Run(() => RunAsync(_cts.Token));
            return Task.CompletedTask;
        }

        public async Task StopAsync()
        {
            IsListening = false;
            try { _cts?.Cancel(); } catch { /* ignore */ }
            try
            {
                if (_socket != null && _socket.State == WebSocketState.Open)
                {
                    await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "stop", CancellationToken.None);
                }
            }
            catch (Exception ex) { WriteLog("StopAsync: " + ex.Message); }
            finally
            {
                _socket?.Dispose();
                _socket = null;
                try { _cts?.Dispose(); } catch { /* ignore */ }
                _cts = null;
            }
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
                        // Первое подключение не удалось — сообщаем один раз и останавливаемся,
                        // чтобы не спамить окнами. Пользователь может нажать «Подключить» снова.
                        IsListening = false;
                        OnError?.Invoke("Не удалось подключиться к DonatePay в реальном времени: " + ex.Message +
                            "\n\nПодробности — в файле error_logs.txt рядом с программой. " +
                            "Проверьте API-токен на https://donatepay.ru/page/api.");
                        break;
                    }
                }
                finally
                {
                    _socket?.Dispose();
                    _socket = null;
                    _inbox.Clear();
                }

                if (ct.IsCancellationRequested || !IsListening) break;
                try { await Task.Delay(TimeSpan.FromSeconds(10), ct); } catch { break; }
            }
            IsListening = false;
        }

        private async Task ConnectAndListenAsync(CancellationToken ct)
        {
            string userId = await GetUserIdAsync();
            if (string.IsNullOrEmpty(userId))
                throw new Exception("не удалось получить ID пользователя (проверьте API-токен).");
            string channel = "$public:" + userId;

            string connectionToken = await GetTokenAsync(null, null);
            if (string.IsNullOrEmpty(connectionToken))
                throw new Exception("не удалось получить токен подключения от /api/v2/socket/token.");

            // Пробуем адреса сокета по очереди.
            Exception lastError = null;
            foreach (var url in SocketUrls)
            {
                try
                {
                    var ws = new ClientWebSocket();
                    await ws.ConnectAsync(new Uri(url), ct);
                    _socket = ws;
                    break;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    WriteLog($"Не удалось подключиться к {url}: {ex.Message}");
                }
            }
            if (_socket == null)
                throw new Exception("ни один адрес сокета не ответил. " + (lastError?.Message ?? ""));

            // 1. connect
            var connectReply = await SendCommandAsync(0, new JObject { ["token"] = connectionToken }, ct);
            if (connectReply["error"] != null)
                throw new Exception("connect: " + connectReply["error"].ToString(Formatting.None));
            string clientId = (string)connectReply["result"]?["client"];
            if (string.IsNullOrEmpty(clientId))
                throw new Exception("Centrifugo не вернул client id.");

            // 2. subscription token для приватного канала
            string subToken = await GetTokenAsync(clientId, channel);

            // 3. subscribe
            var subParams = new JObject { ["channel"] = channel };
            if (!string.IsNullOrEmpty(subToken)) subParams["token"] = subToken;
            var subReply = await SendCommandAsync(1, subParams, ct);
            if (subReply["error"] != null)
                throw new Exception("subscribe: " + subReply["error"].ToString(Formatting.None));

            // Восстановленные публикации (если сервер их прислал вместе с ответом на subscribe).
            var recovered = subReply["result"]?["publications"] as JArray;
            if (recovered != null)
                foreach (var pub in recovered) HandlePublicationData(pub["data"]);

            _connectedOnce = true;
            OnConnected?.Invoke();

            await ReceiveLoopAsync(ct);
        }

        // ---------------- Centrifugo protocol ----------------

        private async Task<JObject> SendCommandAsync(int method, JObject prms, CancellationToken ct)
        {
            int id = ++_commandId;
            var command = new JObject { ["id"] = id, ["method"] = method, ["params"] = prms };
            await SendRawAsync(command.ToString(Formatting.None), ct);

            // Ждём ответ с нужным id, попутно обрабатывая пинги и асинхронные пуши.
            while (true)
            {
                var frame = await ReadFrameAsync(ct);
                if (frame == null) throw new Exception("соединение закрыто во время ожидания ответа.");
                if (!frame.HasValues) { await SendRawAsync("{}", ct); continue; } // ping -> pong
                if (frame["id"] != null && (int)frame["id"] == id) return frame;
                DispatchPush(frame);
            }
        }

        private async Task ReceiveLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                var frame = await ReadFrameAsync(ct);
                if (frame == null) { return; }
                if (!frame.HasValues) { await SendRawAsync("{}", ct); continue; } // ping -> pong
                DispatchPush(frame);
            }
        }

        private void DispatchPush(JObject frame)
        {
            // Centrifugo v3+ формат пуша: {"push":{"channel":...,"pub":{"data":{...}}}}
            var push = frame["push"] as JObject;
            if (push != null)
            {
                var dataV3 = push["pub"]?["data"];
                HandlePublicationData(dataV3);
                return;
            }

            // Centrifugo v2 формат пуша: {"result":{"channel":...,"data":{"data":{...}}}}
            var result = frame["result"] as JObject;
            if (result == null) return;
            var data = result["data"];
            if (data == null) return;
            HandlePublicationData(data["data"] ?? data);
        }

        private void HandlePublicationData(JToken payload)
        {
            if (payload == null) return;
            try
            {
                // Дедупликация по id (на случай восстановления/переподключения).
                string id = FindFirst(payload, "id");
                if (!string.IsNullOrEmpty(id))
                {
                    if (_seen.Contains(id)) return;
                    _seen.Add(id);
                }

                string sumStr = FindFirst(payload, "sum", "amount", "summ");
                string name = FindFirst(payload, "name", "username", "nickname");
                string comment = FindFirst(payload, "comment", "message", "text");
                string currency = FindFirst(payload, "currency", "cur");

                decimal sum = ParseDecimal(sumStr);
                if (string.IsNullOrWhiteSpace(name)) name = "Аноним";
                if (string.IsNullOrWhiteSpace(currency)) currency = "RUB";

                OnDonation?.Invoke(new Donation
                {
                    Service = "DonatePay",
                    Username = name,
                    Amount = sum,
                    Currency = currency,
                    RawText = comment
                });
            }
            catch (Exception ex) { WriteLog("HandlePublicationData: " + ex.Message); }
        }

        // ---------------- WebSocket I/O ----------------

        private async Task SendRawAsync(string json, CancellationToken ct)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            await _socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
        }

        /// <summary>Читает один разобранный JSON-фрейм. Пустой JObject означает ping.</summary>
        private async Task<JObject> ReadFrameAsync(CancellationToken ct)
        {
            if (_inbox.Count > 0) return _inbox.Dequeue();

            string text = await ReceiveTextAsync(ct);
            if (text == null) return null;

            // Centrifugo может присылать несколько ответов в одном сообщении, разделённых \n.
            foreach (var line in text.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0) continue;
                try { _inbox.Enqueue(JObject.Parse(trimmed)); }
                catch (Exception ex) { WriteLog("Не удалось разобрать фрейм: " + ex.Message + " | raw=" + trimmed); }
            }
            return _inbox.Count > 0 ? _inbox.Dequeue() : new JObject();
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

        // ---------------- HTTP helpers ----------------

        private async Task<string> GetUserIdAsync()
        {
            try
            {
                var response = await Http.GetStringAsync(UserApiUrl + Uri.EscapeDataString(_accessToken));
                var json = JObject.Parse(response);
                return (string)(json["data"]?["id"]) ?? (string)json["id"];
            }
            catch (Exception ex)
            {
                WriteLog("GetUserIdAsync: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Запрашивает токен у /api/v2/socket/token.
        /// Если client и channel заданы — это subscription-токен приватного канала, иначе — токен подключения.
        /// </summary>
        private async Task<string> GetTokenAsync(string client, string channel)
        {
            try
            {
                object body = channel == null
                    ? (object)new { access_token = _accessToken }
                    : new { access_token = _accessToken, client, channels = new[] { channel } };

                var content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");
                using (var response = await Http.PostAsync(SocketTokenUrl, content))
                {
                    var text = await response.Content.ReadAsStringAsync();
                    var json = JObject.Parse(text);

                    if (channel == null) return (string)json["token"];

                    var channels = json["channels"] as JArray;
                    if (channels != null && channels.Count > 0) return (string)channels[0]["token"];
                    return (string)json["token"];
                }
            }
            catch (Exception ex)
            {
                WriteLog($"GetTokenAsync(channel={channel}): {ex.Message}");
                return null;
            }
        }

        // ---------------- Утилиты ----------------

        /// <summary>Ищет (в ширину) первое скалярное значение с одним из указанных имён ключей.</summary>
        private static string FindFirst(JToken root, params string[] keys)
        {
            var queue = new Queue<JToken>();
            queue.Enqueue(root);
            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                if (node is JObject obj)
                {
                    foreach (var prop in obj.Properties())
                    {
                        foreach (var k in keys)
                        {
                            if (string.Equals(prop.Name, k, StringComparison.OrdinalIgnoreCase) &&
                                (prop.Value.Type == JTokenType.String ||
                                 prop.Value.Type == JTokenType.Integer ||
                                 prop.Value.Type == JTokenType.Float))
                            {
                                return prop.Value.ToString();
                            }
                        }
                    }
                    foreach (var prop in obj.Properties()) queue.Enqueue(prop.Value);
                }
                else if (node is JArray arr)
                {
                    foreach (var item in arr) queue.Enqueue(item);
                }
            }
            return null;
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
                File.AppendAllText(ErrorLogPath, $"[{DateTime.Now}] (DonatePayClient.cs) {message}{Environment.NewLine}");
            }
            catch { /* логирование не должно ронять приложение */ }
        }
    }
}
