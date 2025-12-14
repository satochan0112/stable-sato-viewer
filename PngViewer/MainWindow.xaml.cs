using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace StableSatoViewer
{
    public partial class MainWindow : Window
    {
        private string[] pngFiles;
        private int currentIndex = 0;
        private int layoutMode = 0; // 0: 通常（左画像+右パネル）, 1: フロート（画像最大化+プロンプトフロート）, 2: 非表示（画像のみ）

        public MainWindow()
        {
            InitializeComponent();

            // 画像領域のクリックをトンネルイベントでフック（領域のどこをクリックしても検出されるように）
            imageBorder.PreviewMouseLeftButtonUp += ImageBox_MouseLeftButtonUp;

            // マウスブラウザボタンや他のマウスボタンを受け取るためにプレビュー MouseDown を購読
            this.PreviewMouseDown += MainWindow_PreviewMouseDown;

            // キーイベント登録
            this.KeyDown += MainWindow_KeyDown;

            // ボタンイベント登録
            toggleButton.Click += ToggleButton_Click;
            fullScreenToggle.Click += FullScreenButton_Click;

            // TextBox のキーイベント登録（左右キーのみ処理）
            this.Loaded += (s, e) =>
            {
                foreach (var child in LogicalTreeHelper.GetChildren(this))
                {
                    if (child is TextBox textBox)
                    {
                        textBox.PreviewKeyDown += TextBox_PreviewKeyDown;
                    }
                }
            };

            // parameters トグルのイベント登録
            parametersToggleButton.Click += ParametersToggleButton_Click;
            negativeToggleButton.Click += NegativeToggleButton_Click;
            stepsToggleButton.Click += StepsToggleButton_Click;

            // クリックでコピー＆矢印キー転送の処理を各グリッドに登録
            parametersGrid.PreviewMouseLeftButtonUp += DataGrid_PreviewMouseLeftButtonUp;
            negativePromptGrid.PreviewMouseLeftButtonUp += DataGrid_PreviewMouseLeftButtonUp;
            stepsGrid.PreviewMouseLeftButtonUp += DataGrid_PreviewMouseLeftButtonUp;

            parametersGrid.PreviewKeyDown += DataGrid_PreviewKeyDown;
            negativePromptGrid.PreviewKeyDown += DataGrid_PreviewKeyDown;
            stepsGrid.PreviewKeyDown += DataGrid_PreviewKeyDown;
        }

        private void ImageBox_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // 画像が読み込まれていなければファイル選択ダイアログを開く
            if (pngFiles == null || pngFiles.Length == 0 || imageBox.Source == null)
            {
                OpenAndLoadImagesFromDialog();
            }
        }

        private void OpenAndLoadImagesFromDialog()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "PNG Files (*.png)|*.png"
            };

            if (dialog.ShowDialog() == true)
            {
                string dir = System.IO.Path.GetDirectoryName(dialog.FileName);
                pngFiles = Directory.GetFiles(dir, "*.png")
                                    .OrderBy(f => f) // 名前順に並べる
                                    .ToArray();

                currentIndex = Array.IndexOf(pngFiles, dialog.FileName);
                if (currentIndex < 0) currentIndex = 0;
                ShowImage(pngFiles[currentIndex]);
            }
        }

        private void DataGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Left || e.Key == Key.Right)
            {
                MainWindow_KeyDown(this, e);
                e.Handled = true;
            }
        }

        private void DataGrid_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // マウスの下にあるセルをヒットテストで探す
            var dep = (DependencyObject)e.OriginalSource;
            while (dep != null && !(dep is DataGridCell) && !(dep is DataGridRow))
            {
                dep = VisualTreeHelper.GetParent(dep);
            }

            if (dep is DataGridCell cell)
            {
                // セルのテキストを取得
                if (cell.Content is TextBlock tb)
                {
                    string text = tb.Text;
                    try
                    {
                        Clipboard.SetText(text);
                        ShowToast("Copied to clipboard!");
                    }
                    catch
                    {
                        ShowToast("Copy failed");
                    }
                }
            }
        }

        private void TextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Left || e.Key == Key.Right)
            {
                MainWindow_KeyDown(this, e);
                e.Handled = true;
            }
        }

        private void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (pngFiles == null || pngFiles.Length == 0) return;

            // Escape キーで全画面を解除
            if (e.Key == Key.Escape)
            {
                if (this.WindowState == WindowState.Maximized && this.WindowStyle == WindowStyle.None)
                {
                    this.WindowStyle = WindowStyle.SingleBorderWindow;
                    this.WindowState = WindowState.Normal;
                    if (fullScreenToggle != null)
                    {
                        fullScreenToggle.IsChecked = false;
                    }
                    e.Handled = true;
                    return;
                }
            }

            // 矢印キー（または Alt+矢印）での移動を許可
            var key = e.Key;
            // Alt 修飾付きで届く場合は SystemKey に入ることがあるため両方確認
            if (key == Key.System)
            {
                key = e.SystemKey;
            }

            if (key == Key.Right)
            {
                currentIndex = (currentIndex + 1) % pngFiles.Length;
                ShowImage(pngFiles[currentIndex]);
                e.Handled = true;
            }
            else if (key == Key.Left)
            {
                currentIndex = (currentIndex - 1 + pngFiles.Length) % pngFiles.Length;
                ShowImage(pngFiles[currentIndex]);
                e.Handled = true;
            }
        }

        private void MainWindow_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            // マウスのブラウザ戻る／進むボタンで画像を切り替え
            if (pngFiles == null || pngFiles.Length == 0) return;

            if (e.ChangedButton == MouseButton.XButton1)
            {
                // 通常 XButton1 は「戻る」
                currentIndex = (currentIndex - 1 + pngFiles.Length) % pngFiles.Length;
                ShowImage(pngFiles[currentIndex]);
                e.Handled = true;
            }
            else if (e.ChangedButton == MouseButton.XButton2)
            {
                // 通常 XButton2 は「進む」
                currentIndex = (currentIndex + 1) % pngFiles.Length;
                ShowImage(pngFiles[currentIndex]);
                e.Handled = true;
            }
        }

        private void ShowImage(string path)
        {
            var bitmap = new BitmapImage(new Uri(path));
            imageBox.Source = bitmap;

            // ウィンドウタイトルを更新
            UpdateWindowTitle(path);

            // tEXt チャンクを読み取って表示
            ExtractAndDisplayTextChunks(path);
        }

        private void UpdateWindowTitle(string path)
        {
            try
            {
                string fileName = System.IO.Path.GetFileName(path);
                int total = (pngFiles != null) ? pngFiles.Length : 0;
                int index = (pngFiles != null) ? (Array.IndexOf(pngFiles, path) + 1) : 0;
                if (total > 0 && index > 0)
                {
                    this.Title = $"{fileName} ({index}/{total})";
                }
                else
                {
                    this.Title = fileName;
                }
                this.Title = this.Title + " - StableSatoViewer";
            }
            catch
            {
                // タイトル更新エラーを無視
            }
        }

        private void ExtractAndDisplayTextChunks(string filePath)
        {
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            using (var br = new BinaryReader(fs))
            {
                // PNG シグネチャをスキップ
                byte[] signature = br.ReadBytes(8);

                string parameters = "";
                string negativePrompt = "";
                string steps = "";

                while (fs.Position < fs.Length)
                {
                    int length = ReadInt32BigEndian(br.ReadBytes(4));
                    string chunkType = Encoding.ASCII.GetString(br.ReadBytes(4));
                    byte[] data = br.ReadBytes(length);
                    br.ReadBytes(4); // CRC をスキップ

                    if (chunkType == "tEXt")
                    {
                        string text = Encoding.ASCII.GetString(data);

                        int nullIndex = text.IndexOf('\0');
                        if (nullIndex > 0)
                        {
                            string key = text.Substring(0, nullIndex);
                            string value = text.Substring(nullIndex + 1);

                            if (key.Equals("parameters", StringComparison.OrdinalIgnoreCase))
                            {
                                int negPromptIndex = value.IndexOf("Negative prompt:");
                                int stepsIndex = value.IndexOf("Steps:");

                                if (negPromptIndex >= 0)
                                {
                                    parameters = value.Substring(0, negPromptIndex).Trim();

                                    if (stepsIndex >= 0)
                                    {
                                        negativePrompt = value.Substring(negPromptIndex + "Negative prompt:".Length, stepsIndex - negPromptIndex - "Negative prompt:".Length).Trim();
                                        steps = value.Substring(stepsIndex + "Steps:".Length).Trim();
                                    }
                                    else
                                    {
                                        negativePrompt = value.Substring(negPromptIndex + "Negative prompt:".Length).Trim();
                                    }
                                }
                                else if (stepsIndex >= 0)
                                {
                                    parameters = value.Substring(0, stepsIndex).Trim();
                                    steps = value.Substring(stepsIndex).Trim();
                                }
                                else
                                {
                                    parameters = value.Trim();
                                }
                            }
                        }
                    }
                }

                DisplayTextAsGrid(parametersGrid, parameters);
                DisplayTextAsGrid(negativePromptGrid, negativePrompt);

                DisplayStepsAsGrid(steps);

                // モード 1（フロート表示）の場合はフローティングプロンプトも更新
                if (layoutMode == 1)
                {
                    UpdateFloatingPromptContent();
                }

            }
        }

        private void DisplayTextAsGrid(DataGrid grid, string text)
        {
            var items = new ObservableCollection<SimpleItem>();

            if (!string.IsNullOrEmpty(text))
            {
                // 改行で分割
                string[] lines = text.Split(new[] { "\n", "\r\n" }, StringSplitOptions.None);

                foreach (string line in lines)
                {
                    string trimmedLine = line.Trim();

                    // 空行、カンマのみ、またはカンマとスペースのみの場合はスキップ
                    if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.All(c => c == ',' || char.IsWhiteSpace(c)))
                    {
                        continue;
                    }

                    items.Add(new SimpleItem { Value = line });
                }
            }

            if (items.Count == 0)
            {
                items.Add(new SimpleItem { Value = "" });
            }

            grid.ItemsSource = items;
        }

        private void DisplayStepsAsGrid(string stepsText)
        {
            var items = new ObservableCollection<StepsItem>();

            if (!string.IsNullOrEmpty(stepsText))
            {
                // "Steps:" をプレフィックスとして追加
                if (!stepsText.StartsWith("Steps:"))
                {
                    stepsText = "Steps: " + stepsText;
                }

                // コロン記号で key:value のペアを分割
                var keyValuePairs = ParseKeyValuePairs(stepsText);

                foreach (var pair in keyValuePairs)
                {
                    items.Add(new StepsItem { Key = pair.Key, Value = pair.Value });
                }
            }

            if (items.Count == 0)
            {
                items.Add(new StepsItem { Key = "(なし)", Value = "" });
            }

            stepsGrid.ItemsSource = items;
        }

        private List<(string Key, string Value)> ParseKeyValuePairs(string text)
        {
            var pairs = new List<(string, string)>();
            var currentKey = new StringBuilder();
            var currentValue = new StringBuilder();
            bool isInValue = false;
            bool insideQuotes = false;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                // ダブルクォートの追跡
                if (c == '"')
                {
                    insideQuotes = !insideQuotes;
                    if (isInValue)
                        currentValue.Append(c);
                    else
                        currentKey.Append(c);
                }
                // コロンで key と value を分割
                else if (c == ':' && !insideQuotes && !isInValue)
                {
                    isInValue = true;
                }
                // カンマで key:value ペアを終了（クォート外のみ）
                else if (c == ',' && !insideQuotes && isInValue)
                {
                    string key = currentKey.ToString().Trim();
                    string value = currentValue.ToString().Trim();

                    if (!string.IsNullOrEmpty(key))
                    {
                        pairs.Add((key, value));
                    }

                    currentKey.Clear();
                    currentValue.Clear();
                    isInValue = false;
                }
                else
                {
                    if (isInValue)
                        currentValue.Append(c);
                    else
                        currentKey.Append(c);
                }
            }

            // 最後のペアを追加
            if (currentKey.Length > 0 || currentValue.Length > 0)
            {
                string key = currentKey.ToString().Trim();
                string value = currentValue.ToString().Trim();
                pairs.Add((key, value));
            }

            return pairs;
        }

        private int ReadInt32BigEndian(byte[] bytes)
        {
            return (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];
        }

        private void ToggleButton_Click(object sender, RoutedEventArgs e)
        {
            // DockPanel 内のグリッドを取得
            var dockPanel = (DockPanel)this.Content;
            Grid mainGrid = null;

            // DockPanel 内のすべての子要素からメイングリッドを探す
            foreach (UIElement child in dockPanel.Children)
            {
                if (child is Grid g && g.ColumnDefinitions.Count == 3)
                {
                    mainGrid = g;
                    break;
                }
            }

            if (mainGrid == null) return;

            var colDefs = mainGrid.ColumnDefinitions;
            
            // 右側のグリッド（Column=2）を探す
            Grid rightGrid = null;
            foreach (UIElement child in mainGrid.Children)
            {
                if (child is Grid g && Grid.GetColumn(g) == 2)
                {
                    rightGrid = g;
                    break;
                }
            }

            if (rightGrid == null) return;

            // 3つのモードを順に切り替え
            layoutMode = (layoutMode + 1) % 3;

            switch (layoutMode)
            {
                case 0:
                    // モード 0: 通常（左画像+右パネル表示）
                    rightGrid.Visibility = Visibility.Visible;
                    imageBorder.Visibility = Visibility.Visible;
                    floatingPromptBorder.Visibility = Visibility.Collapsed;
                    colDefs[1].Width = new GridLength(5);
                    colDefs[2].Width = new GridLength(300);
                    colDefs[0].Width = new GridLength(1, GridUnitType.Star);
                    break;

                case 1:
                    // モード 1: フロート（画像最大化 + プロンプトフロート表示）
                    rightGrid.Visibility = Visibility.Collapsed;
                    imageBorder.Visibility = Visibility.Visible;
                    floatingPromptBorder.Visibility = Visibility.Visible;
                    colDefs[1].Width = new GridLength(0);
                    colDefs[2].Width = new GridLength(0);
                    colDefs[0].Width = new GridLength(1, GridUnitType.Star);
                    UpdateFloatingPromptContent();
                    break;

                case 2:
                    // モード 2: 非表示（画像のみ、右パネルなし）
                    rightGrid.Visibility = Visibility.Collapsed;
                    imageBorder.Visibility = Visibility.Visible;
                    floatingPromptBorder.Visibility = Visibility.Collapsed;
                    colDefs[1].Width = new GridLength(0);
                    colDefs[2].Width = new GridLength(0);
                    colDefs[0].Width = new GridLength(1, GridUnitType.Star);
                    break;
            }
        }

        private void UpdateFloatingPromptContent()
        {
            // フローティングプロンプトに現在のパラメータテキストを表示（グリッド形式）
            var items = new ObservableCollection<SimpleItem>();
            if (parametersGrid.ItemsSource is System.Collections.IEnumerable enumerable)
            {
                foreach (var obj in enumerable)
                {
                    if (obj is SimpleItem si)
                    {
                        items.Add(new SimpleItem { Value = si.Value });
                    }
                }
            }
            if (items.Count == 0)
            {
                items.Add(new SimpleItem { Value = "" });
            }
            floatingPromptGrid.ItemsSource = items;
        }

        private void FullScreenButton_Click(object sender, RoutedEventArgs e)
        {
            if (this.WindowState == WindowState.Maximized && this.WindowStyle == WindowStyle.None)
            {
                // 全画面から通常に戻す
                this.WindowStyle = WindowStyle.SingleBorderWindow;
                this.WindowState = WindowState.Normal;
            }
            else
            {
                // 全画面にする
                this.WindowStyle = WindowStyle.None;
                this.WindowState = WindowState.Maximized;
            }
        }

        private void ParametersToggleButton_Click(object sender, RoutedEventArgs e)
        {
            if (parametersTextBox.Visibility == Visibility.Visible)
            {
                // グリッド表示に切り替え
                parametersTextBox.Visibility = Visibility.Collapsed;
                parametersGrid.Visibility = Visibility.Visible;

                // テキストからグリッドを更新
                var lines = parametersTextBox.Text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                var items = new ObservableCollection<SimpleItem>();
                foreach (var line in lines)
                {
                    if (!string.IsNullOrWhiteSpace(line)) items.Add(new SimpleItem { Value = line });
                }
                if (items.Count == 0) items.Add(new SimpleItem { Value = "" });
                parametersGrid.ItemsSource = items;
            }
            else
            {
                // 生テキスト表示に切り替え
                // グリッドアイテムから生テキストを構築
                var sb = new StringBuilder();
                if (parametersGrid.ItemsSource is System.Collections.IEnumerable enumerable)
                {
                    foreach (var obj in enumerable)
                    {
                        if (obj is SimpleItem si)
                        {
                            sb.AppendLine(si.Value ?? "");
                        }
                    }
                }

                parametersTextBox.Text = sb.ToString().TrimEnd('\r','\n');
                parametersGrid.Visibility = Visibility.Collapsed;
                parametersTextBox.Visibility = Visibility.Visible;
            }
        }

        private void NegativeToggleButton_Click(object sender, RoutedEventArgs e)
        {
            if (negativePromptTextBox.Visibility == Visibility.Visible)
            {
                // グリッド表示に切り替え
                negativePromptTextBox.Visibility = Visibility.Collapsed;
                negativePromptGrid.Visibility = Visibility.Visible;

                // テキストからグリッドを更新
                var lines = negativePromptTextBox.Text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                var items = new ObservableCollection<SimpleItem>();
                foreach (var line in lines)
                {
                    if (!string.IsNullOrWhiteSpace(line)) items.Add(new SimpleItem { Value = line });
                }
                if (items.Count == 0) items.Add(new SimpleItem { Value = "" });
                negativePromptGrid.ItemsSource = items;
            }
            else
            {
                // 生テキスト表示に切り替え
                var sb = new StringBuilder();
                if (negativePromptGrid.ItemsSource is System.Collections.IEnumerable enumerable)
                {
                    foreach (var obj in enumerable)
                    {
                        if (obj is SimpleItem si)
                        {
                            sb.AppendLine(si.Value ?? "");
                        }
                    }
                }

                negativePromptTextBox.Text = sb.ToString().TrimEnd('\r','\n');
                negativePromptGrid.Visibility = Visibility.Collapsed;
                negativePromptTextBox.Visibility = Visibility.Visible;
            }
        }

        private void StepsToggleButton_Click(object sender, RoutedEventArgs e)
        {
            if (stepsTextBox.Visibility == Visibility.Visible)
            {
                // グリッド表示に切り替え
                stepsTextBox.Visibility = Visibility.Collapsed;
                stepsGrid.Visibility = Visibility.Visible;

                // テキストからグリッドを更新 - key:value ラインを解析
                var lines = stepsTextBox.Text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                var items = new ObservableCollection<StepsItem>();
                foreach (var line in lines)
                {
                    var idx = line.IndexOf(':');
                    if (idx > 0)
                    {
                        var key = line.Substring(0, idx).Trim();
                        var value = line.Substring(idx + 1).Trim();
                        items.Add(new StepsItem { Key = key, Value = value });
                    }
                    else
                    {
                        items.Add(new StepsItem { Key = line.Trim(), Value = "" });
                    }
                }
                if (items.Count == 0) items.Add(new StepsItem { Key = "(なし)", Value = "" });
                stepsGrid.ItemsSource = items;
            }
            else
            {
                // 生テキスト表示に切り替え
                var sb = new StringBuilder();
                if (stepsGrid.ItemsSource is System.Collections.IEnumerable enumerable)
                {
                    foreach (var obj in enumerable)
                    {
                        if (obj is StepsItem si)
                        {
                            sb.AppendLine($"{si.Key}: {si.Value}");
                        }
                    }
                }

                stepsTextBox.Text = sb.ToString().TrimEnd('\r','\n');
                stepsGrid.Visibility = Visibility.Collapsed;
                stepsTextBox.Visibility = Visibility.Visible;
            }
        }

        private void PrevImageButton_Click(object sender, RoutedEventArgs e)
        {
            if (pngFiles == null || pngFiles.Length == 0) return;
            currentIndex = (currentIndex - 1 + pngFiles.Length) % pngFiles.Length;
            ShowImage(pngFiles[currentIndex]);
        }

        private void NextImageButton_Click(object sender, RoutedEventArgs e)
        {
            if (pngFiles == null || pngFiles.Length == 0) return;
            currentIndex = (currentIndex + 1) % pngFiles.Length;
            ShowImage(pngFiles[currentIndex]);
        }

        private async void ShowToast(string message)
        {
            toastText.Text = message;
            toastBorder.Visibility = Visibility.Visible;
            await System.Threading.Tasks.Task.Delay(1500);
            toastBorder.Visibility = Visibility.Collapsed;
        }

        private void OpenFilesButton_Click(object sender, RoutedEventArgs e)
        {
            OpenAndLoadImagesFromDialog();
        }
    }

    public class StepsItem
    {
        public string Key { get; set; }
        public string Value { get; set; }
    }

    public class SimpleItem
    {
        public string Value { get; set; }
    }
}