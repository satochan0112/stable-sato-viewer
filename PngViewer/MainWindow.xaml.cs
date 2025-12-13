using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace StableSatoViewer
{
    public partial class MainWindow : Window
    {
        private string[] pngFiles;
        private int currentIndex = 0;

        public MainWindow()
        {
            InitializeComponent();

            // 最初に開くファイルを選択
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
                ShowImage(pngFiles[currentIndex]);
            }

            // キーイベント登録
            this.KeyDown += MainWindow_KeyDown;

            // ボタンイベント登録
            toggleButton.Click += ToggleButton_Click;
            fullScreenToggle.Click += FullScreenButton_Click; // 追加
            // TextBoxのキーイベントも登録（←→キーのみ処理）
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

            // Hook parameters toggle
            parametersToggleButton.Click += ParametersToggleButton_Click;
            negativeToggleButton.Click += NegativeToggleButton_Click;
            stepsToggleButton.Click += StepsToggleButton_Click;
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

            if (e.Key == Key.Right)
            {
                currentIndex = (currentIndex + 1) % pngFiles.Length;
                ShowImage(pngFiles[currentIndex]);
            }
            else if (e.Key == Key.Left)
            {
                currentIndex = (currentIndex - 1 + pngFiles.Length) % pngFiles.Length;
                ShowImage(pngFiles[currentIndex]);
            }
        }

        private void ShowImage(string path)
        {
            var bitmap = new BitmapImage(new Uri(path));
            imageBox.Source = bitmap;

            // tEXtチャンクを読み取って右側に表示
            ExtractAndDisplayTextChunks(path);
        }

        private void ExtractAndDisplayTextChunks(string filePath)
        {
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            using (var br = new BinaryReader(fs))
            {
                // PNGシグネチャをスキップ
                byte[] signature = br.ReadBytes(8);

                string parameters = "";
                string negativePrompt = "";
                string steps = "";

                while (fs.Position < fs.Length)
                {
                    int length = ReadInt32BigEndian(br.ReadBytes(4));
                    string chunkType = Encoding.ASCII.GetString(br.ReadBytes(4));
                    byte[] data = br.ReadBytes(length);
                    br.ReadBytes(4); // CRCをスキップ

                    if (chunkType == "tEXt")
                    {
                        string text = Encoding.ASCII.GetString(data);
                        
                        // キーと値を分離（最初のnullバイトで分割）
                        int nullIndex = text.IndexOf('\0');
                        if (nullIndex > 0)
                        {
                            string key = text.Substring(0, nullIndex);
                            string value = text.Substring(nullIndex + 1);

                            if (key.Equals("parameters", StringComparison.OrdinalIgnoreCase))
                            {
                                // テキストを "Negative prompt:" と "Steps:" で分割
                                int negPromptIndex = value.IndexOf("Negative prompt:");
                                int stepsIndex = value.IndexOf("Steps:");

                                if (negPromptIndex >= 0)
                                {
                                    // parametersは "Negative prompt:" の前まで
                                    parameters = value.Substring(0, negPromptIndex).Trim();

                                    if (stepsIndex >= 0)
                                    {
                                        // negativePromptは "Negative prompt:" から "Steps:" の前まで
                                        negativePrompt = value.Substring(negPromptIndex + "Negative prompt:".Length, stepsIndex - negPromptIndex - "Negative prompt:".Length).Trim();
                                        
                                        // stepsは "Steps:" 以降
                                        steps = value.Substring(stepsIndex + "Steps:".Length).Trim();
                                    }
                                    else
                                    {
                                        // "Steps:" がない場合
                                        negativePrompt = value.Substring(negPromptIndex + "Negative prompt:".Length).Trim();
                                    }
                                }
                                else if (stepsIndex >= 0)
                                {
                                    // "Negative prompt:" がなく "Steps:" がある場合
                                    parameters = value.Substring(0, stepsIndex).Trim();
                                    steps = value.Substring(stepsIndex).Trim(); // "Steps:" を含める
                                }
                                else
                                {
                                    // 両方ない場合はすべてparameters
                                    parameters = value.Trim();
                                }
                            }
                        }
                    }
                }

                // Prompt と Negative Prompt をグリッド表示
                DisplayTextAsGrid(parametersGrid, parameters);
                DisplayTextAsGrid(negativePromptGrid, negativePrompt);
                
                // Infos をグリッド表示
                DisplayStepsAsGrid(steps);
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
            // DockPanel内のグリッドを取得
            var dockPanel = (DockPanel)this.Content;
            Grid mainGrid = null;

            // DockPanel内のすべての子要素からメインGridを探す
            foreach (UIElement child in dockPanel.Children)
            {
                if (child is Grid g)
                {
                    mainGrid = g;
                    break;
                }
            }

            if (mainGrid == null) return;

            var colDefs = mainGrid.ColumnDefinitions;
            var rightGrid = (Grid)mainGrid.Children[3]; // 右側のGrid（Column=2）

            if (rightGrid.Visibility == Visibility.Visible)
            {
                // 非表示にして画像を全幅に
                rightGrid.Visibility = Visibility.Collapsed;
                colDefs[1].Width = new GridLength(0);   // スプリッターを消す
                colDefs[2].Width = new GridLength(0);   // 右カラムを消す
                colDefs[0].Width = new GridLength(1, GridUnitType.Star); // 左カラムを全幅に
            }
            else
            {
                // 再表示して右カラム復活
                rightGrid.Visibility = Visibility.Visible;
                colDefs[1].Width = new GridLength(5);   // スプリッターを復活
                colDefs[2].Width = new GridLength(300); // 右カラムを復活
                colDefs[0].Width = new GridLength(1, GridUnitType.Star);
            }
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
                // switch to grid view
                parametersTextBox.Visibility = Visibility.Collapsed;
                parametersGrid.Visibility = Visibility.Visible;

                // update grid from text
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
                // switch to raw text view
                // build raw text from grid items
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
                // switch to grid view
                negativePromptTextBox.Visibility = Visibility.Collapsed;
                negativePromptGrid.Visibility = Visibility.Visible;

                // update grid from text
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
                // switch to raw text view
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
                // switch to grid view
                stepsTextBox.Visibility = Visibility.Collapsed;
                stepsGrid.Visibility = Visibility.Visible;

                // update grid from text - parse key:value lines
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
                // switch to raw text view
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