using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Timer_for_Donaton
{
    public partial class LogsWindow : Window
    {
        public LogsWindow()
        {
            InitializeComponent();
        }

        public void AddLog(string message)
        {
            Paragraph paragraph = new Paragraph(new Run(message))
            {
                FontSize = 14,
                Margin = new Thickness(1)
            };

            Logs_RichTextBox.Document.Blocks.Add(paragraph);

            Logs_RichTextBox.ScrollToEnd();
        }
    }
}
