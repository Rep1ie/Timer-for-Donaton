using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.WebSockets;
using System.Net;
using System.Threading;
using Newtonsoft.Json;

namespace Timer_for_Donaton.Classes
{
    public class TimerWebSocket
    {
        public bool IsServerStarted = false;

        public string FontSize { get; set; } = "128";
        public string Font { get; set; } = "Arial";
        public string TextColor { get; set; } = "black";
        public string BorderSize { get; set; } = "1";
        public string BorderColor { get; set; } = "transparent";
        public string _timeLeft = "00:00:00";

        private HttpListener _httpListener;
        private List<WebSocket> _clients = new List<WebSocket>();

        public TimerWebSocket()
        {
            _httpListener = new HttpListener();
            _httpListener.Prefixes.Add("http://localhost:8080/");
        }

        public async Task StartServer()
        {
            _httpListener?.Start();
            IsServerStarted = true;

            while (IsServerStarted)
            {
                var context = await _httpListener.GetContextAsync();

                // Если клиент пытается подключиться через WebSocket
                if (context.Request.IsWebSocketRequest)
                {
                    var wsContext = await context.AcceptWebSocketAsync(null);
                    _clients.Add(wsContext.WebSocket);
                }
                else
                {
                    SendHtml(context);
                }
            }
        }

        public void StopServer()
        {
            IsServerStarted = false;
            _httpListener?.Stop(); // Прекращаем прослушивание
            foreach (var client in _clients)
            {
                if (client.State == WebSocketState.Open)
                {
                    client.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server shutting down", CancellationToken.None);
                }
            }
            _clients.Clear();
        }

        private void SendHtml(HttpListenerContext context)
        {
            string html = $@"
            <html>
            <head>
                <style>
                    body {{
                        font-family: '{Font}', sans-serif;
                        color: {TextColor};
                        font-size: {FontSize}px;
                        margin: 0;
                        display: flex;
                        justify-content: center;
                        align-items: center;
                        height: 100vh;
                        -webkit-text-stroke: {BorderSize}px {BorderColor};
                    }}
                </style>
                <script>
                    const socket = new WebSocket('ws://localhost:8080/');
                    
                    socket.onmessage = (event) =>
                    {{
                        try
                        {{
                            const data = JSON.parse(event.data); 
                            if (data.FontSize || data.Font || data.TextColor)
                            {{
                                // Если пришли стили, обновляем их
                                document.body.style.fontFamily = data.Font;
                                document.body.style.color = data.TextColor;
                                document.body.style.webkitTextStroke = `${{data.BorderSize}}px ${{data.BorderColor}}`;
                            }}
                            else
                            {{
                                // Если это не стили – обновляем оставшееся время
                                document.body.innerHTML = data.TimeLeft;
                            }}
                        }}
                        catch
                        {{
                            // Если не JSON, обновляем только время (обратная совместимость)
                            document.body.innerHTML = event.data.TimeLeft;
                        }}
                    }};
                </script>
            </head>
            <body>
                <div>{_timeLeft}</div>
            </body>
            </html>";

            byte[] buffer = Encoding.UTF8.GetBytes(html);
            context.Response.OutputStream.Write(buffer, 0, buffer.Length);
            context.Response.Close();
        }

        public async Task BroadcastTime()
        {
            lock (_clients)  // lock гарантирует что в один момент времени отправляется один поток
            {
                // Проверка состояние клиентов и сортировка только тех клиентов у которых соединение активно
                _clients = _clients.Where(c => c.State == WebSocketState.Open).ToList();
            }

            // Формируем JSON сообщение с текущим временем
            var timeMessage = new
            {
                TimeLeft = _timeLeft
            };

            string message = JsonConvert.SerializeObject(timeMessage);

            foreach (var client in _clients)
            {
                if (client.State == WebSocketState.Open)
                {
                    var messageBytes = Encoding.UTF8.GetBytes(message);
                    await client.SendAsync(new ArraySegment<byte>(messageBytes), WebSocketMessageType.Text, true, CancellationToken.None);
                }
            }
        }

        public async Task BroadcastStyles()
        {
            lock (_clients)
            {
                // Проверяем состояние клиентов (оставляем только активных)
                _clients = _clients.Where(c => c.State == WebSocketState.Open).ToList();
            }

            // Формируем JSON-объект для передачи стилей
            var stylesMessage = new
            {
                Font,
                TextColor,
                BorderSize,
                BorderColor
            };
            // Сериализуем объект в JSON
            string message = JsonConvert.SerializeObject(stylesMessage);

            foreach (var client in _clients)
            {
                if (client.State == WebSocketState.Open)
                {
                    var messageBytes = Encoding.UTF8.GetBytes(message);
                    await client.SendAsync(new ArraySegment<byte>(messageBytes), WebSocketMessageType.Text, true, CancellationToken.None);
                }
            }
        }

        public void UpdateTime(string TimeLeft)
        {
            _timeLeft = TimeLeft;
            Task.Run(BroadcastTime); // Разослать новую информацию о времени
        }
    }
}
