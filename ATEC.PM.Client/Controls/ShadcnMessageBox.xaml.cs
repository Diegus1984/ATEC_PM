using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace ATEC.PM.Client.Controls
{
    /// <summary>
    /// Finestra di dialogo personalizzata in stile Shadcn/UI.
    /// </summary>
    public partial class ShadcnMessageBox : Window
    {
        private MessageBoxResult _result = MessageBoxResult.None;
        private MessageBoxButton _buttonMode;

        private ShadcnMessageBox()
        {
            InitializeComponent();
        }

        public static MessageBoxResult Show(
            string messageBoxText, 
            string caption = "Messaggio", 
            MessageBoxButton button = MessageBoxButton.OK, 
            MessageBoxImage icon = MessageBoxImage.None, 
            Window? owner = null)
        {
            var msgBox = new ShadcnMessageBox();
            msgBox.txtMessage.Text = messageBoxText;
            msgBox.txtTitle.Text = caption;
            msgBox._buttonMode = button;

            // Configure Icon
            msgBox.ConfigureIcon(icon);

            // Configure Buttons
            msgBox.ConfigureButtons(button);

            // Resolve Owner and Position
            var activeWindow = owner ?? Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current.MainWindow;
            if (activeWindow != null && activeWindow.IsVisible)
            {
                msgBox.Owner = activeWindow;
            }
            else
            {
                msgBox.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            msgBox.ShowDialog();
            return msgBox._result;
        }

        private void ConfigureIcon(MessageBoxImage icon)
        {
            switch (icon)
            {
                case MessageBoxImage.Information: // Asterisk
                    txtIcon.Text = "\uE946"; // Info symbol
                    txtIcon.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2563EB")); // Shadcn blue
                    txtIcon.Visibility = Visibility.Visible;
                    break;
                case MessageBoxImage.Warning: // Exclamation
                    txtIcon.Text = "\uE7BA"; // Warning symbol
                    txtIcon.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EAB308")); // Yellow/amber
                    txtIcon.Visibility = Visibility.Visible;
                    break;
                case MessageBoxImage.Error: // Hand, Stop
                    txtIcon.Text = "\uEA39"; // Error/shield warning
                    txtIcon.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")); // Red
                    txtIcon.Visibility = Visibility.Visible;
                    break;
                case MessageBoxImage.Question:
                    txtIcon.Text = "\uE9CE"; // Question symbol
                    txtIcon.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#71717A")); // Zinc gray
                    txtIcon.Visibility = Visibility.Visible;
                    break;
                default:
                    txtIcon.Visibility = Visibility.Collapsed;
                    break;
            }
        }

        private void ConfigureButtons(MessageBoxButton button)
        {
            switch (button)
            {
                case MessageBoxButton.OK:
                    btnOk.Visibility = Visibility.Visible;
                    btnOk.IsDefault = true;
                    break;
                case MessageBoxButton.OKCancel:
                    btnOk.Visibility = Visibility.Visible;
                    btnOk.IsDefault = true;
                    btnCancel.Visibility = Visibility.Visible;
                    btnCancel.IsCancel = true;
                    break;
                case MessageBoxButton.YesNo:
                    btnYes.Visibility = Visibility.Visible;
                    btnYes.IsDefault = true;
                    btnNo.Visibility = Visibility.Visible;
                    btnNo.IsCancel = true;
                    break;
                case MessageBoxButton.YesNoCancel:
                    btnYes.Visibility = Visibility.Visible;
                    btnYes.IsDefault = true;
                    btnNo.Visibility = Visibility.Visible;
                    btnCancel.Visibility = Visibility.Visible;
                    btnCancel.IsCancel = true;
                    break;
            }
        }

        private MessageBoxResult DefaultCloseResult()
        {
            switch (_buttonMode)
            {
                case MessageBoxButton.OK:
                    return MessageBoxResult.OK;
                case MessageBoxButton.OKCancel:
                case MessageBoxButton.YesNoCancel:
                    return MessageBoxResult.Cancel;
                case MessageBoxButton.YesNo:
                    return MessageBoxResult.No;
                default:
                    return MessageBoxResult.None;
            }
        }

        private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            _result = DefaultCloseResult();
            this.DialogResult = false;
            Close();
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            _result = MessageBoxResult.OK;
            this.DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            _result = MessageBoxResult.Cancel;
            this.DialogResult = false;
            Close();
        }

        private void BtnYes_Click(object sender, RoutedEventArgs e)
        {
            _result = MessageBoxResult.Yes;
            this.DialogResult = true;
            Close();
        }

        private void BtnNo_Click(object sender, RoutedEventArgs e)
        {
            _result = MessageBoxResult.No;
            this.DialogResult = false;
            Close();
        }
    }
}
