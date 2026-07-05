using Microsoft.Win32;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace WpfApp4
{
    public partial class MainWindow : Window
    {
        // было:
        // private static string ToolsDir => AppDomain.CurrentDomain.BaseDirectory;
        // private static string AdbPath => Path.Combine(ToolsDir, "adb.exe");
        // private static string ScrcpyPath => Path.Combine(ToolsDir, "scrcpy.exe");

        // стало:
        private static string ToolsDir => AppDomain.CurrentDomain.BaseDirectory;
        private AppSettings _settings = AppSettings.Load();

        private string AdbPath => ResolveExecutable(
            _settings.UseCustomToolsDir ? _settings.CustomAdbDir : null,
            "adb.exe");

        private string ScrcpyPath => ResolveExecutable(
            _settings.UseCustomToolsDir ? _settings.CustomScrcpyDir : null,
            "scrcpy.exe");

        // Порядок поиска исполняемого файла:
        // 1) папка из настроек (если включена и в ней реально есть нужный exe)
        // 2) папка рядом с самой программой (ToolsDir)
        // 3) просто имя файла без пути — Windows найдёт его через PATH
        //    (сработает, если adb/scrcpy добавлены в системный PATH)
        private static string ResolveExecutable(string? customDir, string exeName)
        {
            if (!string.IsNullOrWhiteSpace(customDir))
            {
                var customFull = Path.Combine(customDir, exeName);
                if (File.Exists(customFull)) return customFull;
            }

            var localFull = Path.Combine(ToolsDir, exeName);
            if (File.Exists(localFull)) return localFull;

            return exeName; // Process.Start сам поищет его в PATH
        }

        private readonly ObservableCollection<string> _devices = new();

        private readonly DispatcherTimer _deviceWatchTimer = new() { Interval = TimeSpan.FromSeconds(2) };
        private readonly DispatcherTimer _processesAutoRefreshTimer = new() { Interval = TimeSpan.FromSeconds(3) };

        private Process? _logcatProcess;
        private string? _logcatSerial;
        private bool _logcatPaused;

        private readonly ObservableCollection<string> _logcatBuffer = new();
        private readonly List<string> _logcatPausedBacklog = new();
        private readonly ConcurrentQueue<string> _logcatIncoming = new();
        private readonly DispatcherTimer _logcatFlushTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
        private ICollectionView? _logcatView;
        private const int LogcatMaxLines = 4000;

        public MainWindow()
        {
            try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); } catch { /* already registered */ }

            InitializeComponent();

            _deviceWatchTimer.Interval = TimeSpan.FromSeconds(_settings.DeviceRefreshIntervalSeconds);
            RebuildHotCommandButtons();

            DeviceComboBox.ItemsSource = _devices;

            _logcatView = CollectionViewSource.GetDefaultView(_logcatBuffer);
            _logcatView.Filter = LogcatFilterPredicate;
            LogcatListBox.ItemsSource = _logcatView;

            SeedConsole();
            UpdateDeviceDependentUi();

            _deviceWatchTimer.Tick += async (s, e) => await RefreshDevicesAsync(silent: true);
            _deviceWatchTimer.Start();

            _processesAutoRefreshTimer.Tick += async (s, e) => await RefreshProcessesAsync();

            _logcatFlushTimer.Tick += LogcatFlushTimer_Tick;
            _logcatFlushTimer.Start();

            _ = RefreshDevicesAsync();
        }

        protected override void OnClosed(EventArgs e)
        {
            _deviceWatchTimer.Stop();
            _processesAutoRefreshTimer.Stop();
            _logcatFlushTimer.Stop();
            StopLogcat();
            base.OnClosed(e);
        }

        // ---------------------------------------------------------------
        // Console
        // ---------------------------------------------------------------
        private void SeedConsole()
        {
            AppendPrompt("adb devices");
            AppendPlain("List of devices attached", italic: true);
            AppendCaret();
        }

        private void AppendPrompt(string command)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            line.Children.Add(new TextBlock
            {
                Text = "~$",
                FontFamily = new FontFamily("Consolas"),
                Foreground = (Brush)FindResource("PrimaryBrush"),
                Opacity = 0.7,
                Margin = new Thickness(0, 0, 8, 0)
            });
            line.Children.Add(new TextBlock
            {
                Text = command,
                FontFamily = new FontFamily("Consolas"),
                Foreground = (Brush)FindResource("OnSurfaceVariantBrush"),
                TextWrapping = TextWrapping.Wrap
            });
            ConsolePanel.Children.Add(line);
        }

        private void AppendPlain(string text, bool italic = false)
        {
            var tb = new TextBlock
            {
                Text = text,
                FontFamily = new FontFamily("Consolas"),
                Foreground = (Brush)FindResource("SecondaryBrush"),
                Margin = new Thickness(20, 2, 0, 0),
                FontStyle = italic ? FontStyles.Italic : FontStyles.Normal,
                TextWrapping = TextWrapping.Wrap
            };
            ConsolePanel.Children.Add(tb);
        }

        private void AppendError(string text)
        {
            var tb = new TextBlock
            {
                Text = text,
                FontFamily = new FontFamily("Consolas"),
                Foreground = (Brush)FindResource("ErrorBrush"),
                Margin = new Thickness(20, 2, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };
            ConsolePanel.Children.Add(tb);
        }

        private TextBlock? _caret;

        private void AppendCaret()
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            line.Children.Add(new TextBlock
            {
                Text = "~$",
                FontFamily = new FontFamily("Consolas"),
                Foreground = (Brush)FindResource("PrimaryBrush"),
                Opacity = 0.7,
                Margin = new Thickness(0, 0, 8, 0)
            });
            _caret = new TextBlock
            {
                Text = "_",
                FontFamily = new FontFamily("Consolas"),
                Foreground = (Brush)FindResource("OnSurfaceVariantBrush")
            };
            line.Children.Add(_caret);
            ConsolePanel.Children.Add(line);
        }

        private void RemoveCaret()
        {
            if (_caret?.Parent is StackPanel sp)
                ConsolePanel.Children.Remove(sp);
            _caret = null;
        }

        // ---------------------------------------------------------------
        // Process helpers
        // ---------------------------------------------------------------
        private async Task<string> RunProcessCaptureAsync(string fileName, IEnumerable<string> arguments)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            foreach (var a in arguments) psi.ArgumentList.Add(a);

            using var proc = Process.Start(psi) ?? throw new InvalidOperationException($"Не удалось запустить {fileName}");
            string stdout = await proc.StandardOutput.ReadToEndAsync();
            string stderr = await proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();

            if (proc.ExitCode != 0 && string.IsNullOrWhiteSpace(stdout))
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr) ? $"exit code {proc.ExitCode}" : stderr.Trim());

            return stdout;
        }

        private async Task RunProcessVisibleAsync(string fileName, IEnumerable<string> arguments, string displayCommand, Encoding? encoding = null)
        {
            encoding ??= Encoding.UTF8;

            RemoveCaret();
            AppendPrompt(displayCommand);
            ConsoleScroller.ScrollToEnd();

            CommandInput.IsEnabled = false;
            RunButton.IsEnabled = false;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = encoding,
                    StandardErrorEncoding = encoding
                };
                foreach (var a in arguments) psi.ArgumentList.Add(a);

                using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

                process.OutputDataReceived += (s, e) =>
                {
                    if (e.Data is null) return;
                    Dispatcher.Invoke(() => { AppendPlain(e.Data); ConsoleScroller.ScrollToEnd(); });
                };
                process.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data is null) return;
                    Dispatcher.Invoke(() => { AppendError(e.Data); ConsoleScroller.ScrollToEnd(); });
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                    AppendError($"Process exited with code {process.ExitCode}");
                else
                    AppendPlain("Command executed successfully...", italic: true);
            }
            catch (Exception ex)
            {
                AppendError($"Failed to run command: {ex.Message}");
            }
            finally
            {
                CommandInput.IsEnabled = true;
                RunButton.IsEnabled = true;
                CommandInput.Focus();
                AppendCaret();
                ConsoleScroller.ScrollToEnd();
            }
        }

        private async void RunAdbCommand(params string[] argsWithoutSerial)
        {
            var serial = DeviceComboBox.SelectedItem as string;
            var full = new List<string>();
            if (!string.IsNullOrEmpty(serial)) { full.Add("-s"); full.Add(serial); }
            full.AddRange(argsWithoutSerial);

            var display = "adb " + string.Join(' ', full);
            await RunProcessVisibleAsync(AdbPath, full, display, Encoding.UTF8);
        }

        private Task RunShellCommand(string command) =>
            RunProcessVisibleAsync("cmd.exe", new[] { "/c", command }, command, GetOemEncoding());

        private static Encoding GetOemEncoding()
        {
            try { return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage); }
            catch { return Encoding.UTF8; }
        }

        // ---------------------------------------------------------------
        // Device list
        // ---------------------------------------------------------------
        private async void RefreshDevicesButton_Click(object sender, RoutedEventArgs e) => await RefreshDevicesAsync();

        private async Task RefreshDevicesAsync(bool silent = false)
        {
            if (!silent) RefreshDevicesButton.IsEnabled = false;
            try
            {
                var output = await RunProcessCaptureAsync(AdbPath, new[] { "devices" });
                var found = new List<string>();

                foreach (var rawLine in output.Split('\n'))
                {
                    var line = rawLine.Trim('\r', '\n', ' ');
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (line.StartsWith("List of devices", StringComparison.OrdinalIgnoreCase)) continue;

                    var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2 &&
                        (parts[1] == "device" || parts[1] == "unauthorized" || parts[1] == "offline"))
                    {
                        found.Add(parts[0]);
                    }
                }

                var previouslySelected = DeviceComboBox.SelectedItem as string;

                bool changed = found.Count != _devices.Count || !found.SequenceEqual(_devices);
                if (changed)
                {
                    _devices.Clear();
                    foreach (var d in found) _devices.Add(d);
                }

                if (previouslySelected != null && found.Contains(previouslySelected))
                    DeviceComboBox.SelectedItem = previouslySelected;
                else if (found.Count > 0)
                    DeviceComboBox.SelectedIndex = 0;
                else
                    DeviceComboBox.SelectedItem = null;
            }
            catch (Exception ex)
            {
                if (!silent) AppendError($"Не удалось получить список устройств: {ex.Message}");
            }
            finally
            {
                if (!silent) RefreshDevicesButton.IsEnabled = true;
                UpdateDeviceDependentUi();
            }
        }

        private async void DeviceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateDeviceDependentUi();

            if (DeviceComboBox.SelectedItem is string serial)
            {
                await FetchDeviceInfoAsync(serial);

                if (LogcatPage.Visibility == Visibility.Visible)
                    EnsureLogcatRunning();

                if (ProcessesPage.Visibility == Visibility.Visible)
                    _ = RefreshProcessesAsync();
            }
            else
            {
                StopLogcat();
                ProcessesGrid.ItemsSource = null;
            }
        }

        private void UpdateDeviceDependentUi()
        {
            bool hasDevice = DeviceComboBox.SelectedItem is string s && !string.IsNullOrWhiteSpace(s);
            double opacity = hasDevice ? 1.0 : 0.4;

            foreach (var btn in new[]
                     {
                         ViewScreenButton, PowerButton,
                         VolUpButton, VolDownButton, MenuButton, HomeButton, BackButton,
                         BtnAdbShell, BtnReboot, BtnInstallApk, BtnPushFile
                     })
            {
                btn.IsEnabled = hasDevice;
                btn.Opacity = opacity;
            }

            if (!hasDevice)
            {
                BatteryText.Text = "—";
                StorageText.Text = "—";
                RamText.Text = "—";
                AndroidVersionText.Text = "—";
            }
        }

        // ---------------------------------------------------------------
        // Device info
        // ---------------------------------------------------------------
        private async Task FetchDeviceInfoAsync(string serial)
        {
            try
            {
                var battery = await RunProcessCaptureAsync(AdbPath, new[] { "-s", serial, "shell", "dumpsys", "battery" });
                var level = ExtractValue(battery, "level");
                var statusCode = ExtractValue(battery, "status");
                bool charging = statusCode == "2";
                BatteryText.Text = level != null ? $"{level}%{(charging ? " (Charging)" : "")}" : "—";
            }
            catch { BatteryText.Text = "—"; }

            try
            {
                var df = await RunProcessCaptureAsync(AdbPath, new[] { "-s", serial, "shell", "df", "/data" });
                StorageText.Text = ParseStorage(df) ?? "—";
            }
            catch { StorageText.Text = "—"; }

            try
            {
                var mem = await RunProcessCaptureAsync(AdbPath, new[] { "-s", serial, "shell", "cat", "/proc/meminfo" });
                RamText.Text = ParseRam(mem) ?? "—";
            }
            catch { RamText.Text = "—"; }

            try
            {
                var ver = (await RunProcessCaptureAsync(AdbPath, new[] { "-s", serial, "shell", "getprop", "ro.build.version.release" })).Trim();
                AndroidVersionText.Text = string.IsNullOrWhiteSpace(ver) ? "—" : $"Android {ver}";
            }
            catch { AndroidVersionText.Text = "—"; }
        }

        private static string? ExtractValue(string dumpsysText, string key)
        {
            foreach (var raw in dumpsysText.Split('\n'))
            {
                var line = raw.Trim();
                if (line.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase))
                    return line.Substring(key.Length + 1).Trim();
            }
            return null;
        }

        private static string? ParseStorage(string dfOutput)
        {
            var lines = dfOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 2) return null;
            var cols = lines[1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (cols.Length < 3) return null;
            if (!long.TryParse(cols[1], out var usedKb)) return null;
            if (!long.TryParse(cols[2], out var availKb)) return null;

            double usedGb = usedKb / 1024.0 / 1024.0;
            double totalGb = (usedKb + availKb) / 1024.0 / 1024.0;
            return $"{usedGb:0.#}GB / {totalGb:0.#}GB";
        }

        private static string? ParseRam(string meminfo)
        {
            long? total = null, avail = null;
            foreach (var line in meminfo.Split('\n'))
            {
                if (line.StartsWith("MemTotal:")) total = ExtractKb(line);
                else if (line.StartsWith("MemAvailable:")) avail = ExtractKb(line);
            }
            if (total == null) return null;

            double totalGb = total.Value / 1024.0 / 1024.0;
            if (avail == null) return $"{totalGb:0.#}GB";

            double usedGb = (total.Value - avail.Value) / 1024.0 / 1024.0;
            return $"{usedGb:0.#}GB / {totalGb:0.#}GB";
        }

        private static long? ExtractKb(string line)
        {
            var digits = new string(line.Where(char.IsDigit).ToArray());
            return long.TryParse(digits, out var v) ? v : null;
        }

        // ---------------------------------------------------------------
        // Devices page event handlers
        // ---------------------------------------------------------------
        private async void AdbDevicesButton_Click(object sender, RoutedEventArgs e)
        {
            await RunProcessVisibleAsync(AdbPath, new[] { "devices" }, "adb devices", Encoding.UTF8);
            await RefreshDevicesAsync();
        }

        private void AdbShellButton_Click(object sender, RoutedEventArgs e)
        {
            if (DeviceComboBox.SelectedItem is not string serial) return;

            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/k \"{AdbPath}\" -s {serial} shell",
                UseShellExecute = true,
                WorkingDirectory = ToolsDir
            });
        }

        private void RebootButton_Click(object sender, RoutedEventArgs e) => RunAdbCommand("reboot");

        private void InstallApkButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Title = "Выберите APK для установки", Filter = "Android package (*.apk)|*.apk" };
            if (dlg.ShowDialog() != true) return;
            RunAdbCommand("install", "-r", dlg.FileName);
        }

        private void PushFileButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Title = "Выберите файл для отправки на устройство (в /sdcard/Download)" };
            if (dlg.ShowDialog() != true) return;
            var fileName = Path.GetFileName(dlg.FileName);
            RunAdbCommand("push", dlg.FileName, $"/sdcard/Download/{fileName}");
        }

        private void RunButton_Click(object sender, RoutedEventArgs e)
        {
            var cmd = CommandInput.Text;
            CommandInput.Clear();
            _ = RunShellCommand(cmd);
        }

        private void CommandInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            var cmd = CommandInput.Text;
            CommandInput.Clear();
            _ = RunShellCommand(cmd);
        }

        private void HwControl_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string keycode)
                RunAdbCommand("shell", "input", "keyevent", keycode);
        }

        private void ViewScreen_Click(object sender, RoutedEventArgs e)
        {
            var serial = DeviceComboBox.SelectedItem as string;

            RemoveCaret();
            AppendPrompt(serial != null ? $"scrcpy -s {serial}" : "scrcpy");
            AppendPlain("Запуск scrcpy…", italic: true);
            ConsoleScroller.ScrollToEnd();
            AppendCaret();

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = ScrcpyPath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                    WorkingDirectory = ToolsDir
                };
                if (!string.IsNullOrEmpty(serial))
                {
                    psi.ArgumentList.Add("-s");
                    psi.ArgumentList.Add(serial);
                }

                var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

                process.OutputDataReceived += (s, ev) =>
                {
                    if (ev.Data is null) return;
                    Dispatcher.Invoke(() =>
                    {
                        RemoveCaret();
                        AppendPlain("[scrcpy] " + ev.Data);
                        AppendCaret();
                        ConsoleScroller.ScrollToEnd();
                    });
                };
                process.ErrorDataReceived += (s, ev) =>
                {
                    if (ev.Data is null) return;
                    Dispatcher.Invoke(() =>
                    {
                        RemoveCaret();
                        AppendError("[scrcpy] " + ev.Data);
                        AppendCaret();
                        ConsoleScroller.ScrollToEnd();
                    });
                };
                process.Exited += (s, ev) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        RemoveCaret();
                        AppendPlain($"[scrcpy] процесс завершён (код {process.ExitCode})", italic: true);
                        AppendCaret();
                        ConsoleScroller.ScrollToEnd();
                    });
                    process.Dispose();
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                AppendError($"Failed to launch scrcpy: {ex.Message}");
            }
        }

        // ---------------------------------------------------------------
        // Power menu: Reboot / Recovery / Bootloader / Shutdown
        // ---------------------------------------------------------------
        private void PowerButton_Click(object sender, RoutedEventArgs e)
        {
            if (PowerButton.ContextMenu != null)
            {
                PowerButton.ContextMenu.PlacementTarget = PowerButton;
                PowerButton.ContextMenu.IsOpen = true;
            }
        }

        private void RebootNormal_Click(object sender, RoutedEventArgs e) => RunAdbCommand("reboot");

        private void RebootRecovery_Click(object sender, RoutedEventArgs e) => RunAdbCommand("reboot", "recovery");

        private void RebootBootloader_Click(object sender, RoutedEventArgs e) => RunAdbCommand("reboot", "bootloader");

        private void ShutdownDevice_Click(object sender, RoutedEventArgs e)
        {
            if (_settings.ConfirmBeforePower)
            {
                var result = MessageBox.Show(
                    "Выключить подключённое устройство?",
                    "Подтверждение выключения",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes) return;
            }

            RunAdbCommand("shell", "reboot", "-p");
        }

        // ---------------------------------------------------------------
        // Settings
        // ---------------------------------------------------------------
        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var win = new SettingsWindow(_settings) { Owner = this };
            if (win.ShowDialog() == true)
            {
                _settings = AppSettings.Load();
                _deviceWatchTimer.Interval = TimeSpan.FromSeconds(_settings.DeviceRefreshIntervalSeconds);
                RebuildHotCommandButtons();
            }
        }

        private void RebuildHotCommandButtons()
        {
            var toRemove = QuickCommandsPanel.Children
                .OfType<Button>()
                .Where(b => b.Tag is string tag && tag == "hotcmd")
                .ToList();
            foreach (var b in toRemove) QuickCommandsPanel.Children.Remove(b);

            foreach (var hc in _settings.HotCommands)
            {
                var btn = new Button { Style = (Style)FindResource("PillButton"), Tag = "hotcmd" };
                btn.Content = new TextBlock { Text = hc.Name, Margin = new Thickness(6, 0, 0, 0) };
                var command = hc.Command;
                btn.Click += (s, e) =>
                {
                    var args = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    RunAdbCommand(args);
                };
                QuickCommandsPanel.Children.Add(btn);
            }
        }

        // ---------------------------------------------------------------
        // Page navigation
        // ---------------------------------------------------------------
        private void NavDevices_Click(object sender, RoutedEventArgs e) => SetActivePage("devices");
        private void NavLogcat_Click(object sender, RoutedEventArgs e) => SetActivePage("logcat");
        private void NavProcesses_Click(object sender, RoutedEventArgs e) => SetActivePage("processes");



        private void AboutButton_Click(object sender, RoutedEventArgs e)
        {
            var about = new AboutWindow { Owner = this };
            about.ShowDialog(); // или about.Show(), если хочешь немодально
        }

        private void SetActivePage(string page)
        {
            DevicesPage.Visibility = page == "devices" ? Visibility.Visible : Visibility.Collapsed;
            LogcatPage.Visibility = page == "logcat" ? Visibility.Visible : Visibility.Collapsed;
            ProcessesPage.Visibility = page == "processes" ? Visibility.Visible : Visibility.Collapsed;

            var selectedBrush = (Brush)FindResource("SecondaryContainerBrush");
            NavDevicesButton.Background = page == "devices" ? selectedBrush : Brushes.Transparent;
            NavLogcatButton.Background = page == "logcat" ? selectedBrush : Brushes.Transparent;
            NavProcessesButton.Background = page == "processes" ? selectedBrush : Brushes.Transparent;

            _processesAutoRefreshTimer.Stop();

            if (page == "logcat")
            {
                EnsureLogcatRunning();
            }
            else if (page == "processes")
            {
                _ = RefreshProcessesAsync();
                if (ProcessesAutoRefreshCheck.IsChecked == true)
                    _processesAutoRefreshTimer.Start();
            }
        }

        // ---------------------------------------------------------------
        // Logcat
        // ---------------------------------------------------------------
        private void EnsureLogcatRunning()
        {
            if (DeviceComboBox.SelectedItem is not string serial)
            {
                StopLogcat();
                return;
            }

            if (_logcatProcess != null && _logcatSerial == serial && !_logcatProcess.HasExited)
                return;

            StartLogcat(serial);
        }

        private void StartLogcat(string serial)
        {
            StopLogcat();

            _logcatBuffer.Clear();
            _logcatPausedBacklog.Clear();
            while (_logcatIncoming.TryDequeue(out _)) { }
            _logcatSerial = serial;

            var psi = new ProcessStartInfo
            {
                FileName = AdbPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            psi.ArgumentList.Add("-s");
            psi.ArgumentList.Add(serial);
            psi.ArgumentList.Add("logcat");
            psi.ArgumentList.Add("-v");
            psi.ArgumentList.Add("time");

            _logcatProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };

            _logcatProcess.OutputDataReceived += (s, e) =>
            {
                if (e.Data is null) return;
                _logcatIncoming.Enqueue(e.Data);
            };
            _logcatProcess.ErrorDataReceived += (s, e) =>
            {
                if (e.Data is null) return;
                _logcatIncoming.Enqueue(e.Data);
            };

            try
            {
                _logcatProcess.Start();
                _logcatProcess.BeginOutputReadLine();
                _logcatProcess.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                _logcatBuffer.Add($"[Ошибка запуска logcat: {ex.Message}]");
            }
        }

        private void StopLogcat()
        {
            if (_logcatProcess == null) return;
            try { if (!_logcatProcess.HasExited) _logcatProcess.Kill(entireProcessTree: true); } catch { /* ignore */ }
            _logcatProcess.Dispose();
            _logcatProcess = null;
            _logcatSerial = null;
        }

        private void LogcatFlushTimer_Tick(object? sender, EventArgs e)
        {
            if (_logcatIncoming.IsEmpty) return;

            var batch = new List<string>();
            while (_logcatIncoming.TryDequeue(out var line)) batch.Add(line);

            if (_logcatPaused)
            {
                _logcatPausedBacklog.AddRange(batch);
                if (_logcatPausedBacklog.Count > LogcatMaxLines)
                    _logcatPausedBacklog.RemoveRange(0, _logcatPausedBacklog.Count - LogcatMaxLines);
                return;
            }

            AppendBatchToLogcatBuffer(batch);
        }

        private void AppendBatchToLogcatBuffer(List<string> batch)
        {
            if (batch.Count == 0) return;

            foreach (var line in batch)
                _logcatBuffer.Add(line);

            if (_logcatBuffer.Count > LogcatMaxLines)
            {
                int excess = _logcatBuffer.Count - LogcatMaxLines;
                for (int i = 0; i < excess; i++)
                    _logcatBuffer.RemoveAt(0);
            }

            if (LogcatListBox.Items.Count > 0)
                LogcatListBox.ScrollIntoView(LogcatListBox.Items[^1]);
        }

        private bool LogcatFilterPredicate(object obj)
        {
            var line = obj as string;
            var q = LogcatSearchBox?.Text;
            return string.IsNullOrWhiteSpace(q) || (line?.IndexOf(q, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0;
        }

        private void LogcatSearchBox_TextChanged(object sender, TextChangedEventArgs e) => _logcatView?.Refresh();

        private void LogcatPauseButton_Click(object sender, RoutedEventArgs e)
        {
            _logcatPaused = !_logcatPaused;
            LogcatPauseButton.Content = _logcatPaused ? "Продолжить" : "Пауза";

            if (!_logcatPaused && _logcatPausedBacklog.Count > 0)
            {
                var toApply = new List<string>(_logcatPausedBacklog);
                _logcatPausedBacklog.Clear();
                AppendBatchToLogcatBuffer(toApply);
            }
        }

        private void LogcatClearButton_Click(object sender, RoutedEventArgs e)
        {
            _logcatBuffer.Clear();
            _logcatPausedBacklog.Clear();
            while (_logcatIncoming.TryDequeue(out _)) { }
        }

        private void LogcatRestartButton_Click(object sender, RoutedEventArgs e)
        {
            if (DeviceComboBox.SelectedItem is string serial)
                StartLogcat(serial);
        }

        // ---------------------------------------------------------------
        // Processes
        // ---------------------------------------------------------------
        private class ProcessEntry
        {
            public int Pid { get; set; }
            public string CpuDisplay { get; set; } = "0.0";
            public string RamDisplay { get; set; } = "0.0";
            public string Name { get; set; } = "";
        }

        private async void ProcessesRefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshProcessesAsync();

        private void ProcessesAutoRefreshCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (ProcessesAutoRefreshCheck.IsChecked == true && ProcessesPage.Visibility == Visibility.Visible)
                _processesAutoRefreshTimer.Start();
            else
                _processesAutoRefreshTimer.Stop();
        }

        private async Task RefreshProcessesAsync()
        {
            if (DeviceComboBox.SelectedItem is not string serial)
            {
                ProcessesGrid.ItemsSource = null;
                return;
            }

            try
            {
                var output = await RunProcessCaptureAsync(AdbPath, new[] { "-s", serial, "shell", "ps", "-A", "-o", "PID,%CPU,RSS,NAME" });
                var list = ParseProcesses(output);
                ProcessesGrid.ItemsSource = list;
            }
            catch
            {
                // Тихо игнорируем — например, старое устройство без поддержки этих колонок ps.
            }
        }

        private static List<ProcessEntry> ParseProcesses(string output)
        {
            var result = new List<ProcessEntry>();

            var lines = output.Split('\n')
                .Select(l => l.TrimEnd('\r'))
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToArray();

            if (lines.Length < 2) return result;

            var headerParts = lines[0].Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            int idxPid = Array.FindIndex(headerParts, h => h.Equals("PID", StringComparison.OrdinalIgnoreCase));
            int idxCpu = Array.FindIndex(headerParts, h => h.Equals("%CPU", StringComparison.OrdinalIgnoreCase));
            int idxRss = Array.FindIndex(headerParts, h => h.Equals("RSS", StringComparison.OrdinalIgnoreCase));
            int idxName = Array.FindIndex(headerParts, h => h.Equals("NAME", StringComparison.OrdinalIgnoreCase));

            if (idxPid < 0) return result;

            for (int i = 1; i < lines.Length; i++)
            {
                var parts = lines[i].Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                int maxIdx = new[] { idxPid, idxCpu, idxRss, idxName }.Where(x => x >= 0).DefaultIfEmpty(0).Max();
                if (parts.Length <= maxIdx) continue;

                var pidStr = parts[idxPid];
                if (!int.TryParse(pidStr, out var pid)) continue;

                double cpu = 0;
                if (idxCpu >= 0) double.TryParse(parts[idxCpu], NumberStyles.Any, CultureInfo.InvariantCulture, out cpu);

                long rssKb = 0;
                if (idxRss >= 0) long.TryParse(parts[idxRss], out rssKb);

                string name = "—";
                if (idxName >= 0 && idxName < parts.Length)
                    name = string.Join(" ", parts.Skip(idxName));

                result.Add(new ProcessEntry
                {
                    Pid = pid,
                    CpuDisplay = cpu.ToString("0.0", CultureInfo.InvariantCulture),
                    RamDisplay = (rssKb / 1024.0).ToString("0.0", CultureInfo.InvariantCulture),
                    Name = string.IsNullOrWhiteSpace(name) ? "—" : name
                });
            }

            return result
                .OrderByDescending(p => double.TryParse(p.CpuDisplay, NumberStyles.Any, CultureInfo.InvariantCulture, out var c) ? c : 0)
                .ToList();
        }

        private void ProcessesGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var dep = (DependencyObject)e.OriginalSource;
            while (dep != null && dep is not DataGridRow)
                dep = VisualTreeHelper.GetParent(dep);

            if (dep is DataGridRow row)
                row.IsSelected = true;
        }

        private void CopyProcessPackage_Click(object sender, RoutedEventArgs e)
        {
            if (ProcessesGrid.SelectedItem is ProcessEntry p)
                Clipboard.SetText(p.Name);
        }

        private void CopyProcessPid_Click(object sender, RoutedEventArgs e)
        {
            if (ProcessesGrid.SelectedItem is ProcessEntry p)
                Clipboard.SetText(p.Pid.ToString());
        }
    }
}