# Timer for Donaton

Таймер обратного отсчёта для стримеров, который автоматически добавляет время за донаты.
Поддерживает **DonatePay** и **DonationAlerts**, передаёт текущее время по WebSocket (удобно выводить в OBS)
и ведёт историю донатов с возможностью отката.

<!-- Добавь скриншот приложения: -->
<!-- ![Скриншот](docs/screenshot.png) -->

## Возможности

- ⏱️ **Точный обратный отсчёт** — привязан к монотонным часам, без накопления ошибки даже за несколько дней работы.
- 💸 **Автоначисление времени за донаты** по заданному курсу (например, 140 ₽ = 600 сек).
- 🔌 **Нативная интеграция** с DonatePay (Centrifugo WebSocket) и DonationAlerts (Socket.IO) — без внешних процессов.
- 📜 **История донатов** с логами и откатом начисленного времени по кнопке (повторное нажатие возвращает время).
- 🌗 **Тёмная и светлая темы**, кастомный заголовок окна, векторные иконки.
- 🖥️ **Вывод времени в OBS** через локальный WebSocket-сервер.
- 💾 **Автосохранение** остатка времени и настроек, автоподключение при запуске.

## Технологии

- C# / .NET Framework 4.8
- WPF (XAML)
- Newtonsoft.Json 13.0.3
- System.Net.WebSockets (`ClientWebSocket`)

## Требования

- Windows 7 SP1 и выше
- [.NET Framework 4.8](https://dotnet.microsoft.com/download/dotnet-framework/net48)
- Visual Studio 2019/2022 (для сборки)

## Сборка и запуск

1. Клонируй репозиторий:
   ```bash
   git clone https://github.com/Rep1ie/Timer-for-Donaton.git
   ```
2. Открой `Timer-for-Donaton.csproj` (или `.sln`) в Visual Studio.
3. Восстанови NuGet-пакеты (ПКМ по решению → *Restore NuGet Packages*).
4. Собери: *Build → Rebuild Solution* (конфигурация Release).
5. Готовый `.exe` — в папке `bin/Release/`.

## Настройка

Открой окно настроек в приложении:

- **DonatePay** — вставь свой API-токен ([donatepay.ru/page/api](https://donatepay.ru/page/api)).
- **DonationAlerts** — вставь ссылку на виджет алертов (из неё автоматически берётся параметр `token=`).
- **Курс** — сколько времени добавлять за сумму (₽ → секунды).
- **Тема, шрифт, автоподключение** — по вкусу.

Настройки сохраняются в `appsettings.json` рядом с программой.

> ⚠️ **Важно:** `appsettings.json` содержит твои токены и не должен попадать в репозиторий — он уже в `.gitignore`.

## Структура проекта

```
Timer-for-Donaton/
├─ App.xaml / App.xaml.cs          # точка входа, глобальная обработка ошибок
├─ MainWindow.xaml / .cs           # главное окно и логика таймера
├─ Settings.xaml / .cs             # окно настроек
├─ LogsWindow.xaml / .cs           # история донатов и откат
├─ Classes/
│  ├─ DonatePayClient.cs           # нативный клиент DonatePay (Centrifugo)
│  ├─ DonationAlertsClient.cs      # нативный клиент DonationAlerts (Socket.IO)
│  ├─ Donation.cs                  # модель доната
│  ├─ Logger.cs                    # логи и история
│  ├─ MoneyTimeConverter.cs        # конвертация денег в время
│  ├─ ThemeManager.cs              # переключение тем
│  ├─ TimerWebSocket.cs            # локальный WebSocket-сервер для OBS
│  └─ AppConfig.cs                 # модель настроек (appsettings.json)
├─ Themes/                         # цвета, иконки, стили (XAML)
└─ Resources/                      # иконка приложения
```

## История изменений

См. [CHANGELOG.md](CHANGELOG.md).

## Лицензия

Укажи лицензию по желанию (например, [MIT](https://choosealicense.com/licenses/mit/)).
