using System;
using System.Windows;

namespace Timer_for_Donaton.Classes
{
    public class MoneyTimeConverter
    {
        long time;
        long money;
        float sec_per_ruble;

        public MoneyTimeConverter()
        { }

        public void CurrentCourse(string PlusTime, string MinusMoney)
        {
            if (PlusTime == null || MinusMoney == null || PlusTime == "" || MinusMoney == "" ||
                !IsNumeric(PlusTime) || !IsNumeric(MinusMoney))
            {
                MessageBox.Show("Введите корректные значения в поля с временем и валютой", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            time = Convert.ToInt64(PlusTime);
            money = Convert.ToInt64(MinusMoney);

            sec_per_ruble = (float)time / money;  // Рассчет секунд за 1 рубль
        }

        public long ConvertMoneyToTime(string Donate)
        {
            long time_sec = (long)(sec_per_ruble * Convert.ToInt64(Donate));
            return time_sec;
        }

        private bool IsNumeric(string input)
        {
            long number;
            bool isNumeric = long.TryParse(input, out number);
            return isNumeric;
        }
    }
}
