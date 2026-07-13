using System;

namespace Timer_for_Donaton.Classes
{
    /// <summary>
    /// Конвертер денег в секунды по текущему курсу.
    /// Замечание: класс не показывает MessageBox — валидацией и UI занимается вызывающий код
    /// (разделение ответственностей).
    /// </summary>
    public class MoneyTimeConverter
    {
        private double _secondsPerRuble;

        public bool IsRateConfigured { get; private set; }

        /// <summary>Устанавливает курс времени к деньгам. Возвращает false при неверных входных данных.</summary>
        public bool SetRate(string plusTimeSeconds, string minusMoney)
        {
            if (!long.TryParse(plusTimeSeconds, out long seconds) ||
                !long.TryParse(minusMoney, out long money) ||
                money == 0)
            {
                IsRateConfigured = false;
                return false;
            }

            _secondsPerRuble = (double)seconds / money;
            IsRateConfigured = true;
            return true;
        }

        /// <summary>Переводит сумму доната в секунды.</summary>
        public long ConvertMoneyToTime(decimal donate)
        {
            return (long)Math.Round(_secondsPerRuble * (double)donate);
        }
    }
}
