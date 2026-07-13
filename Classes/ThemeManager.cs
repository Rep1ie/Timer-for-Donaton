using System;
using System.Linq;
using System.Windows;

namespace Timer_for_Donaton.Classes
{
    /// <summary>
    /// Управляет активной темой оформления. Стили ссылаются на кисти темы через
    /// DynamicResource, поэтому достаточно подменить словарь темы — весь UI перекрасится в рантайме.
    /// </summary>
    public static class ThemeManager
    {
        public enum AppTheme { Light, Dark }

        public static AppTheme Current { get; private set; } = AppTheme.Light;

        public static void Apply(AppTheme theme)
        {
            Current = theme;

            string source = theme == AppTheme.Dark
                ? "/Themes/DarkTheme.xaml"
                : "/Themes/LightTheme.xaml";

            var dictionaries = Application.Current.Resources.MergedDictionaries;

            // Удаляем предыдущий словарь темы (LightTheme/DarkTheme), Styles.xaml остаётся.
            // Важно: сравниваем именно LightTheme/DarkTheme, а не подстроку "Theme" —
            // иначе под условие попадают и Styles.xaml, и Icons.xaml из папки Themes.
            var previous = dictionaries.FirstOrDefault(d =>
                d.Source != null && (d.Source.OriginalString.Contains("LightTheme") || d.Source.OriginalString.Contains("DarkTheme")));
            if (previous != null)
            {
                dictionaries.Remove(previous);
            }

            var themeDict = new ResourceDictionary { Source = new Uri(source, UriKind.Relative) };
            // Тему вставляем перед Styles.xaml, чтобы DynamicResource-стили видели кисти.
            dictionaries.Insert(0, themeDict);
        }

        public static void Toggle()
        {
            Apply(Current == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark);
        }
    }
}
