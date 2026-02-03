using System.Windows;

namespace DataViewer_1._0._0._0
{
    public enum SaveRawDataDialogResult
    {
        Yes,
        No,
        DontAskAgain
    }

    public partial class SaveRawDataDialog : Window
    {
        public SaveRawDataDialog(string message)
        {
            InitializeComponent();
            Result = SaveRawDataDialogResult.No;
            if (!string.IsNullOrWhiteSpace(message))
            {
                textBlockMessage.Text = message;
            }
        }

        public SaveRawDataDialogResult Result { get; private set; }

        private void Yes_Click(object sender, RoutedEventArgs e)
        {
            Result = SaveRawDataDialogResult.Yes;
            DialogResult = true;
            Close();
        }

        private void No_Click(object sender, RoutedEventArgs e)
        {
            Result = SaveRawDataDialogResult.No;
            DialogResult = false;
            Close();
        }

        private void DontAsk_Click(object sender, RoutedEventArgs e)
        {
            Result = SaveRawDataDialogResult.DontAskAgain;
            DialogResult = false;
            Close();
        }
    }
}
