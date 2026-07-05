using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace WpfApp4
{
    public partial class SettingsWindow : Window
    {
        private readonly AppSettings _settings;
        private readonly ObservableCollection<HotCommand> _hotCommands;

        public SettingsWindow(AppSettings settings)
        {
            InitializeComponent();
            _settings = settings;

            UseCustomDirCheck.IsChecked = _settings.UseCustomToolsDir;
            AdbDirBox.Text = _settings.CustomAdbDir;
            ScrcpyDirBox.Text = _settings.CustomScrcpyDir;
            RefreshIntervalBox.Text = _settings.DeviceRefreshIntervalSeconds.ToString();
            ConfirmPowerCheck.IsChecked = _settings.ConfirmBeforePower;

            _hotCommands = new ObservableCollection<HotCommand>(_settings.HotCommands);
            HotCommandsList.ItemsSource = _hotCommands;

            UpdateToolsPathsEnabled();
        }

        private void UseCustomDirCheck_Changed(object sender, RoutedEventArgs e) => UpdateToolsPathsEnabled();

        private void UpdateToolsPathsEnabled()
        {
            bool enabled = UseCustomDirCheck.IsChecked == true;
            AdbDirBox.IsEnabled = enabled;
            ScrcpyDirBox.IsEnabled = enabled;
        }

        private void BrowseAdbDir_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog { Title = "Выберите папку с adb.exe" };
            if (dlg.ShowDialog() == true) AdbDirBox.Text = dlg.FolderName;
        }

        private void BrowseScrcpyDir_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog { Title = "Выберите папку со scrcpy.exe" };
            if (dlg.ShowDialog() == true) ScrcpyDirBox.Text = dlg.FolderName;
        }

        private void AddHotCommand_Click(object sender, RoutedEventArgs e)
        {
            var name = NewHotCommandName.Text.Trim();
            var cmd = NewHotCommandValue.Text.Trim();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(cmd)) return;

            _hotCommands.Add(new HotCommand { Name = name, Command = cmd });
            NewHotCommandName.Clear();
            NewHotCommandValue.Clear();
        }

        private void RemoveHotCommand_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is HotCommand hc)
                _hotCommands.Remove(hc);
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            _settings.UseCustomToolsDir = UseCustomDirCheck.IsChecked == true;
            _settings.CustomAdbDir = AdbDirBox.Text.Trim();
            _settings.CustomScrcpyDir = ScrcpyDirBox.Text.Trim();

            if (int.TryParse(RefreshIntervalBox.Text, out var seconds) && seconds > 0)
                _settings.DeviceRefreshIntervalSeconds = seconds;

            _settings.ConfirmBeforePower = ConfirmPowerCheck.IsChecked == true;
            _settings.HotCommands = new System.Collections.Generic.List<HotCommand>(_hotCommands);

            _settings.Save();
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}