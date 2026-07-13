using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Net.WebSockets;
using Newtonsoft.Json;

namespace Timer_for_Donaton.Classes
{
    /// <summary>
    /// Локальный HTTP/WebSocket-сервер для отображения таймера в браузерном источнике OBS.
    /// </summary>
    public class TimerWebSocket
    {
        public bool IsServerStarted = false;

        public string FontSize { get; set; } = "128";
        public string Font { get; set; } = "Arial";
        public string TextColor { get; set; } = "black";
        public string BorderSize { get; set; } = "1";
        public string BorderColor { get; set; } = "transparent";
        public string _timeLeft = "00:00:00";

        private readonly HttpListener _httpListener;
        private List<WebSocket> _clients = new List<WebSocket>();

        // Шаблон страницы. Не интерполированная строка — плейсхолдеры подставляются через Replace,
        // чтобы не воевать с экранированием фигурных скобок JS/CSS.
        private const string HtmlTemplate = @"<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <style>
        body {
            font-family: '__FONT__', sans-serif;
            color: __TEXTCOLOR__;
            font-size: __FONTSIZE__px;
            margin: 0;
            display: flex;
            justify-content: center;
            align-items: center;
            height: 100vh;
            -webkit-text-stroke: __BORDERSIZE__px __BORDERCOLOR__;
        }
    </style>
</head>
<body>
    <div id='timer'>__TIMELEFT__</div>
    <script>
        const socket = new WebSocket('ws://localhost:8080/');
        socket.onmessage = function (event) {
            try {
                const data = JSON.parse(event.data);
                if (data.Font || data.TextColor || data.BorderColor || data.BorderSize) {
                    // Пришли стили — обновляем внешний вид
                    document.body.style.fontFamily = data.Font;
                    document.body.style.color = data.TextColor;
                    document.body.style.webkitTextStroke = data.BorderSize + 'px ' + data.BorderColor;
                } else if (data.TimeLeft !== undefined) {
                    // Пришло новое время — обновляем текст таймера
                    document.getElementById('timer').textContent = data.TimeLeft;
                }
            } catch (e) {
                console.error(e);
            }
        };
    </script>
</body>
</html>";

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
                HttpListenerContext context;
                try
                {
                    context = await _httpListener.GetContextAsync();
                }
                catch (HttpListenerException)
                {
                    break; // сервер остановлен
                }

                if (context.Request.IsWebSocketRequest)
                {
                    var wsContext = await context.AcceptWebSocketAsync(null);
                    lock (_clients) { _clients.Add(wsContext.WebSocket); }
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
            _httpListener?.Stop();
            lock (_clients)
            {
                foreach (var client in _clients)
                {
                    if (client.State == WebSocketState.Open)
                    {
                        client.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server shutting down", CancellationToken.None);
                    }
                }
                _clients.Clear();
            }
        }

        private void SendHtml(HttpListenerContext context)
        {
            string html = HtmlTemplate
                .Replace("__FONT__", Font)
                .Replace("__TEXTCOLOR__", TextColor)
                .Replace("__FONTSIZE__", FontSize)
                .Replace("__BORDERSIZE__", BorderSize)
                .Replace("__BORDERCOLOR__", BorderColor)
                .Replace("__TIMELEFT__", _timeLeft);

            byte[] buffer = Encoding.UTF8.GetBytes(html);
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.OutputStream.Write(buffer, 0, buffer.Length);
            context.Response.Close();
        }

        public async Task BroadcastTime()
        {
            await Broadcast(JsonConvert.SerializeObject(new { TimeLeft = _timeLeft }));
        }

        public async Task BroadcastStyles()
        {
            await Broadcast(JsonConvert.SerializeObject(new { Font, TextColor, BorderSize, BorderColor }));
        }

        private async Task Broadcast(string message)
        {
            List<WebSocket> snapshot;
            lock (_clients)
            {
                _clients = _clients.Where(c => c.State == WebSocketState.Open).ToList();
                snapshot = _clients.ToList();
            }

            var bytes = Encoding.UTF8.GetBytes(message);
            foreach (var client in snapshot)
            {
                if (client.State == WebSocketState.Open)
                {
                    await client.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
                }
            }
        }

        public void UpdateTime(string timeLeft)
        {
            _timeLeft = timeLeft;
            _ = BroadcastTime();
        }
    }
}
