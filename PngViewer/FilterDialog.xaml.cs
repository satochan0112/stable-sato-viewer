using System;
using System.Windows;

namespace StableSatoViewer
{
    public partial class FilterDialog : Window
    {
        public string PromptContains { get; private set; }

        public FilterDialog()
        {
            InitializeComponent();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            PromptContains = (promptTextBox.Text ?? string.Empty).Trim();
            this.DialogResult = true;
            this.Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}
