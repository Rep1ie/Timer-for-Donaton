namespace Timer_for_Donaton.Classes
{
    /// <summary>
    /// Единая модель события для всех источников (DonationAlerts, DonatePay, Twitch и т.д.).
    /// Позволяет обрабатывать все донаты единообразно в одном месте.
    /// </summary>
    public class Donation
    {
        /// <summary>Источник: "DonationAlerts", "DonatePay", "Twitch".</summary>
        public string Service { get; set; }

        /// <summary>Имя отправителя.</summary>
        public string Username { get; set; }

        /// <summary>Сумма доната (в валюте Currency). null если событие не денежное.</summary>
        public decimal? Amount { get; set; }

        /// <summary>Валюта (RUB, USD и т.д.).</summary>
        public string Currency { get; set; }

        /// <summary>Готовая строка для лога (если событие не денежное, например Twitch-рейд).</summary>
        public string RawText { get; set; }

        /// <summary>Начислять ли время за это событие (только для денежных донатов).</summary>
        public bool AddsTime => Amount.HasValue && Amount.Value > 0;
    }
}
