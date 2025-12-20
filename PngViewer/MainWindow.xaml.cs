using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Linq;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfDataGrid = System.Windows.Controls.DataGrid;
using WpfDataGridCell = System.Windows.Controls.DataGridCell;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfDragEventArgs = System.Windows.DragEventArgs;

namespace StableSatoViewer
{
    public partial class MainWindow : Window
    {
        private string[] pngFiles;
        private int currentIndex = 0;
        private int layoutMode = 0; // 0: 通常（左画像+右パネル）, 1: フロート（画像最大化+プロンプトフロート）, 2: 非表示（画像のみ）
        private List<string> favorites = new List<string>();
        private string[] allPngFilesInFolder; // すべてのPNGファイル（フィルター前）
        private bool isInitializing = false; // 初期化中フラグ（フォルダ選択イベントを抑制）
        private string windowStateFilePath => Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), "StableSatoViewer", "windowstate.json");

        private string favoritesFilePath => Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), "StableSatoViewer", "favorites.txt");

        public MainWindow()
        {
            InitializeComponent();
            // 読み込んだお気に入りに基づき UI を更新
            LoadFavorites();
            UpdateFavoritesIndicator();

            // ウィンドウの状態を復元
            RestoreWindowState();

            // Build folder tree and restore last folder
            try
            {
                BuildFolderTree();
                
                // 前回表示していた画像を復元
                var lastImage = LoadLastImage();
                if (!string.IsNullOrEmpty(lastImage) && File.Exists(lastImage))
                {
                    string dir = Path.GetDirectoryName(lastImage);
                    pngFiles = Directory.GetFiles(dir, "*.png").OrderBy(f => f).ToArray();
                    allPngFilesInFolder = pngFiles;
                    currentIndex = Array.IndexOf(pngFiles, lastImage);
                    if (currentIndex < 0) currentIndex = 0;
                    ShowImage(pngFiles[currentIndex]);
                    
                    // フォルダツリー選択時のイベントを抑制
                    isInitializing = true;
                    SelectFolderInTree(dir);
                    isInitializing = false;
                }
                else
                {
                    // 前回の画像がない場合は、前回のフォルダを復元
                    var last = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StableSatoViewer", "lastfolder.txt");
                    if (File.Exists(last))
                    {
                        var lf = File.ReadAllText(last, Encoding.UTF8).Trim();
                        if (Directory.Exists(lf)) SelectFolderInTree(lf);
                    }
                }
            }
            catch { }

            // もしコマンドライン引数で画像ファイルが渡されていたら最初に表示する
            try
            {
                var args = Environment.GetCommandLineArgs();
                if (args != null && args.Length > 1)
                {
                    string first = args[1];
                    if (!string.IsNullOrEmpty(first) && File.Exists(first))
                    {
                        var ext = Path.GetExtension(first);
                        if (!string.IsNullOrEmpty(ext) && ext.Equals(".png", StringComparison.OrdinalIgnoreCase))
                        {
                            string dir = Path.GetDirectoryName(first);
                            pngFiles = Directory.GetFiles(dir, "*.png").OrderBy(f => f).ToArray();
                            currentIndex = Array.IndexOf(pngFiles, first);
                            if (currentIndex < 0) currentIndex = 0;
                            // ShowImage will update UI elements; it's safe after InitializeComponent
                            ShowImage(pngFiles[currentIndex]);
                            // フォルダツリーでもこのフォルダを選択
                            SelectFolderInTree(dir);
                        }
                    }
                }
            }
            catch
            {
                // 引数読み込みエラーは無視して通常起動
            }

            // マウスブラウザボタンや他のマウスボタンを受け取るためにプレビュー MouseDown を購読
            this.PreviewMouseDown += MainWindow_PreviewMouseDown;

            // キーイベント登録
            this.KeyDown += MainWindow_KeyDown;

            // ボタンイベント登録
            toggleButton.Click += ToggleButton_Click;
            treeToggleButton.Click += TreeToggleButton_Click;
            fullScreenToggle.Click += FullScreenButton_Click;

            // TextBox のキーイベント登録（左右キーのみ処理）
            this.Loaded += (s, e) =>
            {
                foreach (var child in LogicalTreeHelper.GetChildren(this))
                {
                    if (child is WpfTextBox textBox)
                    {
                        textBox.PreviewKeyDown += TextBox_PreviewKeyDown;
                    }
                }
            };

            // parameters トグルのイベント登録
            parametersToggleButton.Click += ParametersToggleButton_Click;
            negativeToggleButton.Click += NegativeToggleButton_Click;
            stepsToggleButton.Click += StepsToggleButton_Click;

            // ファイルリストのキーイベント登録
            folderFilesListBox.PreviewKeyDown += FolderFilesListBox_PreviewKeyDown;

            // 右パネルのグリッドクリックでクリップボードにコピー
            parametersGrid.PreviewMouseLeftButtonUp += DataGrid_PreviewMouseLeftButtonUp;
            negativePromptGrid.PreviewMouseLeftButtonUp += DataGrid_PreviewMouseLeftButtonUp;
            stepsGrid.PreviewMouseLeftButtonUp += DataGrid_PreviewMouseLeftButtonUp;

            // 右パネルのグリッドでも←→キーで画像切り替え
            parametersGrid.PreviewKeyDown += DataGrid_PreviewKeyDown;
            negativePromptGrid.PreviewKeyDown += DataGrid_PreviewKeyDown;
            stepsGrid.PreviewKeyDown += DataGrid_PreviewKeyDown;

            // フィルター入力で Enter 押下時にフィルターを実行
            if (filterTextBox != null)
            {
                filterTextBox.KeyDown += FilterTextBox_KeyDown;
            }

            // ウィンドウクローズ時に状態を保存
            this.Closing += (s, e) => SaveWindowState();
        }

        private void FilterTextBox_KeyDown(object sender, WpfKeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                try
                {
                    FilterButton_Click(filterButton, new RoutedEventArgs());
                }
                catch { }
                e.Handled = true;
            }
        }

        private void DataGrid_PreviewKeyDown(object sender, WpfKeyEventArgs e)
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
            while (dep != null && !(dep is WpfDataGridCell))
            {
                dep = VisualTreeHelper.GetParent(dep);
            }

            if (dep is WpfDataGridCell cell)
            {
                // セルのテキストを取得
                if (cell.Content is TextBlock tb)
                {
                    string text = tb.Text;
                    try
                    {
                        System.Windows.Clipboard.SetText(text);
                        ShowToast("Copied to clipboard!");
                    }
                    catch
                    {
                        ShowToast("Copy failed");
                    }
                }
            }
        }

        private void TextBox_PreviewKeyDown(object sender, WpfKeyEventArgs e)
        {
            if (e.Key == Key.Left || e.Key == Key.Right)
            {
                MainWindow_KeyDown(this, e);
                e.Handled = true;
            }
        }

        private void MainWindow_KeyDown(object sender, WpfKeyEventArgs e)
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
                if (currentIndex < pngFiles.Length - 1)
                {
                    currentIndex++;
                }
                else if (folderFilesListBox.Tag is string currentDir)
                {
                    try
                    {
                        string parentDir = Directory.GetParent(currentDir)?.FullName;
                        if (!string.IsNullOrEmpty(parentDir))
                        {
                            var subDirs = Directory.GetDirectories(parentDir).OrderBy(d => d).ToArray();
                            int idx = Array.IndexOf(subDirs, currentDir);
                            if (idx >= 0 && idx < subDirs.Length - 1)
                            {
                                var files = Directory.GetFiles(subDirs[idx + 1], "*.png").OrderBy(x => x).ToArray();
                                if (files.Length > 0)
                                {
                                    pngFiles = files;
                                    folderFilesListBox.ItemsSource = files.Select(f => System.IO.Path.GetFileName(f)).ToList();
                                    folderFilesListBox.Tag = subDirs[idx + 1];
                                    currentIndex = 0;
                                    ShowImage(pngFiles[currentIndex]);
                                    isInitializing = true;
                                    SelectFolderInTree(subDirs[idx + 1]);
                                    isInitializing = false;
                                    e.Handled = true;
                                    return;
                                }
                            }
                        }
                    }
                    catch { }
                    currentIndex = 0;
                }
                else
                {
                    currentIndex = 0;
                }
                ShowImage(pngFiles[currentIndex]);
                e.Handled = true;
            }
            else if (key == Key.Left)
            {
                if (currentIndex > 0)
                {
                    currentIndex--;
                }
                else if (folderFilesListBox.Tag is string currentDir)
                {
                    try
                    {
                        string parentDir = Directory.GetParent(currentDir)?.FullName;
                        if (!string.IsNullOrEmpty(parentDir))
                        {
                            var subDirs = Directory.GetDirectories(parentDir).OrderBy(d => d).ToArray();
                            int idx = Array.IndexOf(subDirs, currentDir);
                            if (idx > 0)
                            {
                                var files = Directory.GetFiles(subDirs[idx - 1], "*.png").OrderBy(x => x).ToArray();
                                if (files.Length > 0)
                                {
                                    pngFiles = files;
                                    folderFilesListBox.ItemsSource = files.Select(f => System.IO.Path.GetFileName(f)).ToList();
                                    folderFilesListBox.Tag = subDirs[idx - 1];
                                    currentIndex = files.Length - 1;
                                    ShowImage(pngFiles[currentIndex]);
                                    isInitializing = true;
                                    SelectFolderInTree(subDirs[idx - 1]);
                                    isInitializing = false;
                                    e.Handled = true;
                                    return;
                                }
                            }
                        }
                    }
                    catch { }
                    currentIndex = pngFiles.Length - 1;
                }
                else
                {
                    currentIndex = pngFiles.Length - 1;
                }
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

            // 最後に表示した画像を保存
            SaveLastImage(path);

            // 背景アイコンを非表示
            if (bitmap != null)
            {
                // imageBorder内のGridの子要素から背景画像を探す
                try
                {
                    Grid innerGrid = imageBorder.Child as Grid;
                    if (innerGrid != null)
                    {
                        foreach (var child in innerGrid.Children)
                        {
                            if (child is System.Windows.Controls.Image img && img.Name != "imageBox")
                            {
                                img.Opacity = 0;
                                break;
                            }
                        }
                    }
                }
                catch { }
            }

            // ウィンドウタイトルを更新
            UpdateWindowTitle(path);

            // tEXt チャンクを読み取って表示
            ExtractAndDisplayTextChunks(path);

            // Update favorites indicator when image changes
            UpdateFavoritesIndicator();

            // 左パネルのファイル一覧を現在の画像のディレクトリで表示し、選択状態を反映する
            try
            {
                if (folderFilesListBox != null && pngFiles != null)
                {
                    var dir = System.IO.Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    {
                        // 現在のfolderFilesListBoxのディレクトリと異なる場合のみ更新
                        if (folderFilesListBox.Tag as string != dir)
                        {
                            // フィルター状態が無い場合のみファイルリストを再取得
                            var files = pngFiles;
                            var names = files.Select(f => System.IO.Path.GetFileName(f)).ToList();
                            folderFilesListBox.ItemsSource = names;
                            folderFilesListBox.Tag = dir;
                        }

                        // 現在の画像に対応するインデックスを取得して選択
                        int idx = Array.IndexOf(pngFiles, path);
                        if (idx >= 0)
                        {
                            folderFilesListBox.SelectedIndex = idx;
                            var item = folderFilesListBox.SelectedItem;
                            if (item != null)
                            {
                                folderFilesListBox.ScrollIntoView(item);
                            }
                        }
                        else
                        {
                            folderFilesListBox.SelectedIndex = -1;
                        }
                    }
                }
            }
            catch
            {
                // 無視
            }
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

        private void DisplayTextAsGrid(WpfDataGrid grid, string text)
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
            // 3つのモードを順に切り替え
            layoutMode = (layoutMode + 1) % 3;
            ApplyLayoutMode();
        }

        private void ApplyLayoutMode()
        {
            // DockPanel 内のグリッドを取得
            var dockPanel = (DockPanel)this.Content;
            Grid mainGrid = null;

            // DockPanel 内のすべての子要素からメイングリッドを探す
            foreach (UIElement child in dockPanel.Children)
            {
                if (child is Grid g && g.ColumnDefinitions.Count >= 5)
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
                if (child is Grid g && Grid.GetColumn(g) == 4)
                {
                    rightGrid = g;
                    break;
                }
            }

            if (rightGrid == null) return;

            switch (layoutMode)
            {
                case 0:
                    // モード 0: 通常（左画像+右パネル表示）
                    rightGrid.Visibility = Visibility.Visible;
                    imageBorder.Visibility = Visibility.Visible;
                    floatingPromptBorder.Visibility = Visibility.Collapsed;
                    // restore image/right splitter and right column
                    colDefs[3].Width = new GridLength(5);
                    colDefs[4].Width = new GridLength(300);
                    colDefs[2].Width = new GridLength(1, GridUnitType.Star);
                    break;

                case 1:
                    // モード 1: フロート（画像最大化 + プロンプトフロート表示）
                    rightGrid.Visibility = Visibility.Collapsed;
                    imageBorder.Visibility = Visibility.Visible;
                    floatingPromptBorder.Visibility = Visibility.Visible;
                    colDefs[3].Width = new GridLength(0);
                    colDefs[4].Width = new GridLength(0);
                    colDefs[2].Width = new GridLength(1, GridUnitType.Star);
                    UpdateFloatingPromptContent();
                    break;

                case 2:
                    // モード 2: 非表示（画像のみ、右パネルなし）
                    rightGrid.Visibility = Visibility.Collapsed;
                    imageBorder.Visibility = Visibility.Visible;
                    floatingPromptBorder.Visibility = Visibility.Collapsed;
                    colDefs[3].Width = new GridLength(0);
                    colDefs[4].Width = new GridLength(0);
                    colDefs[2].Width = new GridLength(1, GridUnitType.Star);
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
            if (currentIndex > 0)
            {
                currentIndex--;
            }
            else if (folderFilesListBox.Tag is string currentDir)
            {
                try
                {
                    string parentDir = Directory.GetParent(currentDir)?.FullName;
                    if (!string.IsNullOrEmpty(parentDir))
                    {
                        var subDirs = Directory.GetDirectories(parentDir).OrderBy(d => d).ToArray();
                        int idx = Array.IndexOf(subDirs, currentDir);
                        if (idx > 0)
                        {
                            var files = Directory.GetFiles(subDirs[idx - 1], "*.png").OrderBy(x => x).ToArray();
                            if (files.Length > 0)
                            {
                                pngFiles = files;
                                folderFilesListBox.ItemsSource = files.Select(f => System.IO.Path.GetFileName(f)).ToList();
                                folderFilesListBox.Tag = subDirs[idx - 1];
                                currentIndex = files.Length - 1;
                                ShowImage(pngFiles[currentIndex]);
                                isInitializing = true;
                                SelectFolderInTree(subDirs[idx - 1]);
                                isInitializing = false;
                                return;
                            }
                        }
                    }
                }
                catch { }
                currentIndex = pngFiles.Length - 1;
            }
            else
            {
                currentIndex = pngFiles.Length - 1;
            }
            ShowImage(pngFiles[currentIndex]);
        }

        private void NextImageButton_Click(object sender, RoutedEventArgs e)
        {
            if (pngFiles == null || pngFiles.Length == 0) return;
            if (currentIndex < pngFiles.Length - 1)
            {
                currentIndex++;
            }
            else if (folderFilesListBox.Tag is string currentDir)
            {
                try
                {
                    string parentDir = Directory.GetParent(currentDir)?.FullName;
                    if (!string.IsNullOrEmpty(parentDir))
                    {
                        var subDirs = Directory.GetDirectories(parentDir).OrderBy(d => d).ToArray();
                        int idx = Array.IndexOf(subDirs, currentDir);
                        if (idx >= 0 && idx < subDirs.Length - 1)
                        {
                            var files = Directory.GetFiles(subDirs[idx + 1], "*.png").OrderBy(x => x).ToArray();
                            if (files.Length > 0)
                            {
                                pngFiles = files;
                                folderFilesListBox.ItemsSource = files.Select(f => System.IO.Path.GetFileName(f)).ToList();
                                folderFilesListBox.Tag = subDirs[idx + 1];
                                currentIndex = 0;
                                ShowImage(pngFiles[currentIndex]);
                                isInitializing = true;
                                SelectFolderInTree(subDirs[idx + 1]);
                                isInitializing = false;
                                return;
                            }
                        }
                    }
                }
                catch { }
                currentIndex = 0;
            }
            else
            {
                currentIndex = 0;
            }
            ShowImage(pngFiles[currentIndex]);
        }

        private async void ShowToast(string message)
        {
            toastText.Text = message;
            toastBorder.Visibility = Visibility.Visible;
            await System.Threading.Tasks.Task.Delay(1500);
            toastBorder.Visibility = Visibility.Collapsed;
        }

        private void FavoritesButton_Click(object sender, RoutedEventArgs e)
        {
            // Load favorites
            LoadFavorites();
            favoritesListBox.ItemsSource = null;
            favoritesListBox.ItemsSource = favorites;
            favoritesPopup.IsOpen = true;
        }

        private void AddFavoriteButton_Click(object sender, RoutedEventArgs e)
        {
            if (imageBox?.Source is BitmapImage bm && bm.UriSource != null)
            {
                var path = bm.UriSource.LocalPath;
                if (!favorites.Contains(path))
                {
                    favorites.Add(path);
                    SaveFavorites();
                    favoritesListBox.ItemsSource = null;
                    favoritesListBox.ItemsSource = favorites;
                    ShowToast("Added to favorites");
                    UpdateFavoritesIndicator();
                }
                else
                {
                    ShowToast("Already in favorites");
                }
            }
            else
            {
                ShowToast("No image open");
            }
        }

        private void RemoveAllFavoritesButton_Click(object sender, RoutedEventArgs e)
        {
            favorites.Clear();
            SaveFavorites();
            favoritesListBox.ItemsSource = null;
            ShowToast("All favorites removed");
            UpdateFavoritesIndicator();
        }

        private void EditFavoritesButton_Click(object sender, RoutedEventArgs e)
        {
            // simple edit: open folder
            var dir = Path.GetDirectoryName(favoritesFilePath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{favoritesFilePath}\"") { UseShellExecute = true });
        }

        private void FavoritesListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (favoritesListBox.SelectedItem is string path)
            {
                if (File.Exists(path))
                {
                    string dir = Path.GetDirectoryName(path);
                    pngFiles = Directory.GetFiles(dir, "*.png").OrderBy(f => f).ToArray();
                    currentIndex = Array.IndexOf(pngFiles, path);
                    if (currentIndex < 0) currentIndex = 0;
                    ShowImage(pngFiles[currentIndex]);
                    favoritesPopup.IsOpen = false;
                    // フォルダツリーでもこのフォルダを選択
                    SelectFolderInTree(dir);
                }
                else
                {
                    ShowToast("Favorite image not found");
                }
            }
        }

        private void SaveFavorites()
        {
            try
            {
                var dir = Path.GetDirectoryName(favoritesFilePath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllLines(favoritesFilePath, favorites, Encoding.UTF8);
            }
            catch
            {
                // ignore
            }
        }

        private void LoadFavorites()
        {
            try
            {
                if (File.Exists(favoritesFilePath))
                {
                    favorites = File.ReadAllLines(favoritesFilePath, Encoding.UTF8).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
                }
                else
                {
                    favorites = new List<string>();
                }
            }
            catch
            {
                favorites = new List<string>();
            }
        }

        private void UpdateFavoritesIndicator()
        {
            try
            {
                if (favoritesButton == null) return;
                if (imageBox?.Source is BitmapImage bm && bm.UriSource != null)
                {
                    if (favorites.Contains(bm.UriSource.LocalPath))
                    {
                        favoritesButton.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#60a0ff");
                        favoritesButton.Foreground = System.Windows.Media.Brushes.White;
                        favoritesButton.ToolTip = "Favorited";
                        return;
                    }
                }
                favoritesButton.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#2d2d2d");
                favoritesButton.Foreground = System.Windows.Media.Brushes.White;
                favoritesButton.ToolTip = "Favorites";
            }
            catch
            {
            }
        }

        private void ImageBorder_PreviewDragOver(object sender, WpfDragEventArgs e)
        {
            // PNG ファイルのみ許可
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
                if (files != null && files.Length > 0 && Path.GetExtension(files[0])?.Equals(".png", StringComparison.OrdinalIgnoreCase) == true)
                {
                    e.Effects = System.Windows.DragDropEffects.Copy;
                }
                else
                {
                    e.Effects = System.Windows.DragDropEffects.None;
                }
            }
            else
            {
                e.Effects = System.Windows.DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void ImageBorder_Drop(object sender, WpfDragEventArgs e)
        {
            if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)) return;
            var files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
            if (files == null || files.Length == 0) return;

            // 最初のファイルが PNG なら読み込む
            var first = files[0];
            if (!File.Exists(first)) return;
            if (!string.Equals(Path.GetExtension(first), ".png", StringComparison.OrdinalIgnoreCase)) return;

            try
            {
                string dir = Path.GetDirectoryName(first);
                pngFiles = Directory.GetFiles(dir, "*.png").OrderBy(f => f).ToArray();
                currentIndex = Array.IndexOf(pngFiles, first);
                if (currentIndex < 0) currentIndex = 0;
                ShowImage(pngFiles[currentIndex]);
                // フォルダツリーでもこのフォルダを選択
                SelectFolderInTree(dir);
            }
            catch
            {
                // ignore
            }
        }

        private void BuildFolderTree()
        {
            folderTreeView.Items.Clear();
            try
            {
                foreach (var d in DriveInfo.GetDrives().Where(d => d.IsReady))
                {
                    var ti = new TreeViewItem { Header = d.Name, Tag = d.RootDirectory.FullName };
                    ti.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#e0e0e0");
                    ti.Items.Add(null);
                    ti.Expanded += Folder_Expanded;
                    folderTreeView.Items.Add(ti);
                }
            }
            catch { }
        }

        private void Folder_Expanded(object sender, RoutedEventArgs e)
        {
            if (sender is TreeViewItem ti)
            {
                if (ti.Items.Count == 1 && ti.Items[0] == null)
                {
                    ti.Items.Clear();
                    try
                    {
                        var path = ti.Tag as string;
                        foreach (var sub in Directory.GetDirectories(path))
                        {
                            var child = new TreeViewItem { Header = Path.GetFileName(sub), Tag = sub };
                            child.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#e0e0e0");
                            child.Items.Add(null);
                            child.Expanded += Folder_Expanded;
                            ti.Items.Add(child);
                        }
                    }
                    catch { }
                }
            }
        }

        private void FolderTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            // 初期化中はイベントを無視
            if (isInitializing) return;

            if (folderTreeView.SelectedItem is TreeViewItem t && t.Tag is string p)
            {
                SaveLastFolder(p);
                LoadImagesFromFolderWithFilter(p);
                // populate file list
                try
                {
                    var files = Directory.GetFiles(p, "*.png").OrderBy(x => x).ToArray();
                    allPngFilesInFolder = files; // すべてのファイルを保存

                    // If a filter is active, apply it to the new folder; otherwise show all
                    if (filterTextBox != null && !string.IsNullOrWhiteSpace(filterTextBox.Text))
                    {
                        // FilterButton_Click relies on allPngFilesInFolder being set
                        folderFilesListBox.Tag = p; // store current folder
                        FilterButton_Click(filterButton, new RoutedEventArgs());
                    }
                    else
                    {
                        pngFiles = files; // フィルターをリセット
                        folderFilesListBox.ItemsSource = files.Select(f => System.IO.Path.GetFileName(f)).ToList();
                        folderFilesListBox.Tag = p; // store current folder
                        // do not clear filterTextBox - preserve user's input
                        if (pngFiles.Length > 0)
                        {
                            currentIndex = 0;
                            ShowImage(pngFiles[0]);
                        }
                    }
                }
                catch { folderFilesListBox.ItemsSource = null; folderFilesListBox.Tag = null; }
            }
        }

        private void FolderFilesListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (folderFilesListBox.SelectedItem is string name && folderFilesListBox.Tag is string dir)
            {
                var full = System.IO.Path.Combine(dir, name);
                if (File.Exists(full))
                {
                    // set pngFiles to files in dir and show selected
                    try
                    {
                        pngFiles = Directory.GetFiles(dir, "*.png").OrderBy(x => x).ToArray();
                        currentIndex = Array.IndexOf(pngFiles, full);
                        if (currentIndex < 0) currentIndex = 0;
                        ShowImage(pngFiles[currentIndex]);
                    }
                    catch { }
                }
            }
        }

        private void SaveLastFolder(string dir)
        {
            try
            {
                var fn = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StableSatoViewer");
                if (!Directory.Exists(fn)) Directory.CreateDirectory(fn);
                File.WriteAllText(Path.Combine(fn, "lastfolder.txt"), dir, Encoding.UTF8);
            }
            catch { }
        }

        private void SaveLastImage(string imagePath)
        {
            try
            {
                var fn = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StableSatoViewer");
                if (!Directory.Exists(fn)) Directory.CreateDirectory(fn);
                File.WriteAllText(Path.Combine(fn, "lastimage.txt"), imagePath, Encoding.UTF8);
            }
            catch { }
        }

        private void SelectFolderInTree(string path)
        {
            try
            {
                // パスを正規化
                path = System.IO.Path.GetFullPath(path).TrimEnd(System.IO.Path.DirectorySeparatorChar);

                // ドライブを探す
                string drive = System.IO.Path.GetPathRoot(path).TrimEnd(System.IO.Path.DirectorySeparatorChar);
                
                TreeViewItem driveNode = null;
                foreach (TreeViewItem t in folderTreeView.Items)
                {
                    string nodeTag = t.Tag as string;
                    if (!string.IsNullOrEmpty(nodeTag))
                    {
                        string nodeRoot = System.IO.Path.GetPathRoot(nodeTag).TrimEnd(System.IO.Path.DirectorySeparatorChar);
                        if (nodeRoot.Equals(drive, StringComparison.OrdinalIgnoreCase))
                        {
                            driveNode = t;
                            break;
                        }
                    }
                }

                if (driveNode == null) return;

                // ドライブノードを展開
                driveNode.IsExpanded = true;

                // パスの各部分を分割
                string[] pathParts = path.Substring(drive.Length).Trim(System.IO.Path.DirectorySeparatorChar).Split(System.IO.Path.DirectorySeparatorChar);

                // ツリーを辿りながら各ノードを展開
                TreeViewItem currentNode = driveNode;
                string currentPath = drive;

                foreach (var part in pathParts)
                {
                    if (string.IsNullOrEmpty(part)) continue;

                    currentPath = System.IO.Path.Combine(currentPath, part);

                    // 子ノードを展開
                    if (currentNode.Items.Count == 1 && currentNode.Items[0] == null)
                    {
                        currentNode.Items.Clear();
                        try
                        {
                            var pathTag = currentNode.Tag as string;
                            foreach (var sub in Directory.GetDirectories(pathTag))
                            {
                                var child = new TreeViewItem { Header = Path.GetFileName(sub), Tag = sub };
                                child.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#e0e0e0");
                                child.Items.Add(null);
                                child.Expanded += Folder_Expanded;
                                currentNode.Items.Add(child);
                            }
                        }
                        catch { }
                    }

                    // 次のノードを探す
                    TreeViewItem nextNode = null;
                    foreach (TreeViewItem child in currentNode.Items.OfType<TreeViewItem>())
                    {
                        string childPath = (child.Tag as string) ?? "";
                        if (childPath.Equals(currentPath, StringComparison.OrdinalIgnoreCase))
                        {
                            nextNode = child;
                            break;
                        }
                    }

                    if (nextNode == null) break;

                    currentNode = nextNode;
                    currentNode.IsExpanded = true;
                }

                // 最終ノードを選択
                currentNode.IsSelected = true;
                currentNode.BringIntoView();
            }
            catch { }
        }

        private void LoadImagesFromFolderWithFilter(string dir)
        {
            try
            {
                var allPng = Directory.GetFiles(dir, "*.png").OrderBy(f => f).ToArray();
                var matched = new List<string>(allPng);
                if (matched.Count == 0) { ShowToast("No images"); return; }
                pngFiles = matched.ToArray();
                currentIndex = 0;
                ShowImage(pngFiles[currentIndex]);
            }
            catch { }
        }
        private void TreeToggleButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // find main grid under DockPanel
                var dockPanel = (DockPanel)this.Content;
                Grid mainGrid = null;
                foreach (UIElement child in dockPanel.Children)
                {
                    if (child is Grid g && g.ColumnDefinitions.Count >= 5)
                    {
                        mainGrid = g;
                        break;
                    }
                }
                if (mainGrid == null) return;

                var colDefs = mainGrid.ColumnDefinitions;

                if (treeBorder == null) return;

                if (treeBorder.Visibility == Visibility.Visible)
                {
                    // hide
                    treeBorder.Visibility = Visibility.Collapsed;
                    colDefs[0].Width = new GridLength(0);
                    colDefs[1].Width = new GridLength(0);
                }
                else
                {
                    treeBorder.Visibility = Visibility.Visible;
                    colDefs[0].Width = new GridLength(260);
                    colDefs[1].Width = new GridLength(8);
                }
            }
            catch { }
        }

        private void FolderFilesListBox_PreviewKeyDown(object sender, WpfKeyEventArgs e)
        {
            // ファイルリストにフォーカスがある時、左右キーで画像切り替え
            if (e.Key == Key.Left || e.Key == Key.Right)
            {
                if (pngFiles == null || pngFiles.Length == 0) return;

                if (e.Key == Key.Right)
                {
                    if (currentIndex < pngFiles.Length - 1)
                    {
                        currentIndex++;
                        ShowImage(pngFiles[currentIndex]);
                    }
                    else if (folderFilesListBox.Tag is string currentDir)
                    {
                        try
                        {
                            string parentDir = Directory.GetParent(currentDir)?.FullName;
                            if (!string.IsNullOrEmpty(parentDir))
                            {
                                var subDirs = Directory.GetDirectories(parentDir).OrderBy(d => d).ToArray();
                                int idx = Array.IndexOf(subDirs, currentDir);
                                if (idx >= 0 && idx < subDirs.Length - 1)
                                {
                                    var files = Directory.GetFiles(subDirs[idx + 1], "*.png").OrderBy(x => x).ToArray();
                                    if (files.Length > 0)
                                    {
                                        pngFiles = files;
                                        folderFilesListBox.ItemsSource = files.Select(f => System.IO.Path.GetFileName(f)).ToList();
                                        folderFilesListBox.Tag = subDirs[idx + 1];
                                        currentIndex = 0;
                                        ShowImage(pngFiles[currentIndex]);
                                        isInitializing = true;
                                        SelectFolderInTree(subDirs[idx + 1]);
                                        isInitializing = false;
                                    }
                                }
                            }
                        }
                        catch { }
                    }
                    e.Handled = true;
                }
                else if (e.Key == Key.Left)
                {
                    if (currentIndex > 0)
                    {
                        currentIndex--;
                        ShowImage(pngFiles[currentIndex]);
                    }
                    else if (folderFilesListBox.Tag is string currentDir)
                    {
                        try
                        {
                            string parentDir = Directory.GetParent(currentDir)?.FullName;
                            if (!string.IsNullOrEmpty(parentDir))
                            {
                                var subDirs = Directory.GetDirectories(parentDir).OrderBy(d => d).ToArray();
                                int idx = Array.IndexOf(subDirs, currentDir);
                                if (idx > 0)
                                {
                                    var files = Directory.GetFiles(subDirs[idx - 1], "*.png").OrderBy(x => x).ToArray();
                                    if (files.Length > 0)
                                    {
                                        pngFiles = files;
                                        folderFilesListBox.ItemsSource = files.Select(f => System.IO.Path.GetFileName(f)).ToList();
                                        folderFilesListBox.Tag = subDirs[idx - 1];
                                        currentIndex = files.Length - 1;
                                        ShowImage(pngFiles[currentIndex]);
                                        isInitializing = true;
                                        SelectFolderInTree(subDirs[idx - 1]);
                                        isInitializing = false;
                                    }
                                }
                            }
                        }
                        catch { }
                    }
                    e.Handled = true;
                }
            }
        }

        private void FolderFilesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (folderFilesListBox.SelectedItem is string name && folderFilesListBox.Tag is string dir)
                {
                    var full = System.IO.Path.Combine(dir, name);
                    if (File.Exists(full))
                    {
                        // Show selected image and update pngFiles/currentIndex so navigation works
                        var bitmap = new BitmapImage(new Uri(full));
                        imageBox.Source = bitmap;
                        // pngFiles は既にフィルター状態を持っているので、そのまま使用
                        currentIndex = Array.IndexOf(pngFiles, full);
                        if (currentIndex < 0) currentIndex = 0;

                        // 最後に表示した画像を保存
                        SaveLastImage(full);

                        // Update title and text chunks
                        UpdateWindowTitle(full);
                        ExtractAndDisplayTextChunks(full);

                        // Update favorites indicator
                        UpdateFavoritesIndicator();
                    }
                }
            }
            catch
            {
                // ignore
            }
        }

        private void FilterButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (filterTextBox == null) return;
                
                string filterText = filterTextBox.Text.Trim().ToLower();
                
                if (string.IsNullOrEmpty(filterText))
                {
                    // フィルターなし：すべてのファイルを表示
                    if (allPngFilesInFolder != null)
                    {
                        pngFiles = allPngFilesInFolder;
                        var names = pngFiles.Select(f => System.IO.Path.GetFileName(f)).ToList();
                        folderFilesListBox.ItemsSource = names;
                        folderFilesListBox.SelectedIndex = 0;
                        if (pngFiles.Length > 0)
                        {
                            currentIndex = 0;
                            ShowImage(pngFiles[0]);
                        }
                        ShowToast("Filter cleared");
                    }
                    return;
                }

                // フィルター処理：ファイル一覧に表示されているファイルのみをフィルター対象とする
                if (allPngFilesInFolder == null || allPngFilesInFolder.Length == 0)
                {
                    ShowToast("No files to filter");
                    return;
                }

                var filtered = new List<string>();

                foreach (var f in allPngFilesInFolder)
                {
                    try
                    {
                        using var fs = new FileStream(f, FileMode.Open, FileAccess.Read);
                        using var br = new BinaryReader(fs);

                        // skip PNG signature
                        br.ReadBytes(8);

                        bool found = false;
                        while (fs.Position + 8 < fs.Length && !found)
                        {
                            var lenBytes = br.ReadBytes(4);
                            if (lenBytes.Length < 4) break;
                            int length = ReadInt32BigEndian(lenBytes);
                            var typeBytes = br.ReadBytes(4);
                            if (typeBytes.Length < 4) break;
                            string chunkType = Encoding.ASCII.GetString(typeBytes);
                            var data = br.ReadBytes(length);
                            br.ReadBytes(4); // CRC

                            if (chunkType == "tEXt")
                            {
                                string text = Encoding.ASCII.GetString(data);
                                int nullIndex = text.IndexOf('\0');
                                if (nullIndex >= 0)
                                {
                                    string key = text.Substring(0, nullIndex);
                                    string value = text.Substring(nullIndex + 1).ToLower();
                                    
                                    if (key.Equals("parameters", StringComparison.OrdinalIgnoreCase))
                                    {
                                        // Search only the parameters text up to 'Negative prompt:'
                                        string paramText = value;
                                        int negIndex = value.IndexOf("negative prompt:");
                                        if (negIndex >= 0)
                                        {
                                            paramText = value.Substring(0, negIndex);
                                        }
                                        
                                        // フィルターテキストが含まれているかチェック
                                        if (paramText.Contains(filterText))
                                        {
                                            filtered.Add(f);
                                            found = true;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch
                    {
                        // ignore read errors
                    }
                }

                if (filtered.Count == 0)
                {
                    ShowToast($"No files match '{filterText}'");
                    return;
                }

                pngFiles = filtered.OrderBy(f => f).ToArray();
                var fileNames = pngFiles.Select(f => System.IO.Path.GetFileName(f)).ToList();
                folderFilesListBox.ItemsSource = fileNames;
                folderFilesListBox.SelectedIndex = 0;
                
                // フィルター後、最初の画像を表示
                currentIndex = 0;
                ShowImage(pngFiles[0]);
                
                ShowToast($"Found {filtered.Count} file(s)");
            }
            catch (Exception ex)
            {
                ShowToast($"Filter error: {ex.Message}");
            }
        }

        private void ClearFilterButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (filterTextBox == null) return;
                filterTextBox.Clear();

                if (allPngFilesInFolder != null)
                {
                    // Remember currently displayed image (if any)
                    string currentImagePath = null;
                    if (imageBox?.Source is BitmapImage bm && bm.UriSource != null)
                    {
                        currentImagePath = bm.UriSource.LocalPath;
                    }

                    // Restore full file list
                    pngFiles = allPngFilesInFolder;
                    var names = pngFiles.Select(f => System.IO.Path.GetFileName(f)).ToList();
                    folderFilesListBox.ItemsSource = names;

                    // If the currently displayed image is in the restored list, keep it selected
                    if (!string.IsNullOrEmpty(currentImagePath))
                    {
                        int idx = Array.IndexOf(pngFiles, currentImagePath);
                        if (idx >= 0)
                        {
                            currentIndex = idx;
                            folderFilesListBox.SelectedIndex = idx;
                            folderFilesListBox.ScrollIntoView(folderFilesListBox.SelectedItem);
                            // ensure UI reflects any parsed text/chunks for the same image
                            ShowImage(pngFiles[currentIndex]);
                        }
                        else
                        {
                            // Currently displayed image is not part of this folder's files.
                            // Do not switch the displayed image to the folder's first image.
                            // Just leave currentIndex as 0 so navigation won't crash later.
                            currentIndex = 0;
                            folderFilesListBox.SelectedIndex = -1;
                        }
                    }
                    else
                    {
                        // No image currently displayed: do not force-show first image
                        folderFilesListBox.SelectedIndex = -1;
                    }
                }
                ShowToast("Filter cleared");
            }
            catch { }
        }

        private void OpenInExplorerButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (imageBox?.Source is BitmapImage bm && bm.UriSource != null)
                {
                    string imagePath = bm.UriSource.LocalPath;
                    // エクスプローラーでファイルを選択状態で開く
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{imagePath}\"") { UseShellExecute = true });
                    ShowToast("Opened in Explorer");
                }
                else
                {
                    ShowToast("No image open");
                }
            }
            catch
            {
                ShowToast("Failed to open Explorer");
            }
        }

        private string LoadLastImage()
        {
            try
            {
                var file = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), "StableSatoViewer", "lastimage.txt");
                if (File.Exists(file))
                {
                    var txt = File.ReadAllText(file, Encoding.UTF8).Trim();
                    if (!string.IsNullOrEmpty(txt) && File.Exists(txt))
                    {
                        return txt;
                    }
                }
            }
            catch { }
            return null;
        }

        private void SaveWindowState()
        {
            try
            {
                var state = new WindowStateData
                {
                    WindowWidth = this.Width,
                    WindowHeight = this.Height,
                    WindowLeft = this.Left,
                    WindowTop = this.Top,
                    IsMaximized = this.WindowState == WindowState.Maximized,
                    IsFullScreen = this.WindowStyle == WindowStyle.None,
                    LayoutMode = layoutMode,
                    TreeVisible = treeBorder?.Visibility == Visibility.Visible,
                    RightPanelVisible = true,
                    ParametersVisible = parametersGrid?.Visibility == Visibility.Visible,
                    NegativePromptVisible = negativePromptGrid?.Visibility == Visibility.Visible,
                    StepsVisible = stepsGrid?.Visibility == Visibility.Visible
                };

                // グリッドの列幅を取得
                if (this.Content is DockPanel dockPanel)
                {
                    var grid = dockPanel.Children.OfType<Grid>().FirstOrDefault();
                    if (grid != null && grid.ColumnDefinitions.Count >= 5)
                    {
                        state.TreeColumnWidth = grid.ColumnDefinitions[0].Width.Value;
                        state.RightPanelColumnWidth = grid.ColumnDefinitions[4].Width.Value;
                        state.RightPanelVisible = grid.ColumnDefinitions[4].Width.Value > 0;
                    }
                }

                // ツリーの高さを取得
                if (treeBorder != null && treeBorder.Child is Grid treeGrid && treeGrid.RowDefinitions.Count >= 3)
                {
                    state.TreeHeight = treeGrid.RowDefinitions[0].ActualHeight;
                }

                var dir = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), "StableSatoViewer");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                var json = System.Text.Json.JsonSerializer.Serialize(state, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(windowStateFilePath, json, Encoding.UTF8);
            }
            catch { }
        }

        private void RestoreWindowState()
        {
            try
            {
                if (!File.Exists(windowStateFilePath)) return;

                var json = File.ReadAllText(windowStateFilePath, Encoding.UTF8);
                var state = System.Text.Json.JsonSerializer.Deserialize<WindowStateData>(json);

                if (state != null)
                {
                    // 全画面状態を復元（サイズ・位置の前に設定）
                    if (state.IsFullScreen)
                    {
                        this.WindowStyle = WindowStyle.None;
                        this.WindowState = WindowState.Maximized;
                        if (fullScreenToggle != null)
                            fullScreenToggle.IsChecked = true;
                    }
                    else
                    {
                        // 通常モード：ウィンドウサイズと位置を復元
                        this.Width = state.WindowWidth;
                        this.Height = state.WindowHeight;
                        this.Left = state.WindowLeft;
                        this.Top = state.WindowTop;

                        if (state.IsMaximized)
                        {
                            this.WindowState = WindowState.Maximized;
                        }
                    }

                    // この段階では、レイアウトが確定していないため、
                    // Loaded イベント後に列幅やレイアウトモードを設定する
                    this.Loaded += (s, e) =>
                    {
                        // グリッドの列幅を復元
                        if (this.Content is DockPanel dockPanel)
                        {
                            var grid = dockPanel.Children.OfType<Grid>().FirstOrDefault();
                            if (grid != null && grid.ColumnDefinitions.Count >= 5)
                            {
                                grid.ColumnDefinitions[0].Width = new GridLength(state.TreeColumnWidth);
                                grid.ColumnDefinitions[4].Width = new GridLength(state.RightPanelColumnWidth);
                            }
                        }

                        // ツリーの高さを復元
                        if (treeBorder != null && treeBorder.Child is Grid treeGrid && treeGrid.RowDefinitions.Count >= 3 && !double.IsNaN(state.TreeHeight))
                        {
                            treeGrid.RowDefinitions[0].Height = new GridLength(state.TreeHeight);
                        }

                        // ツリーの表示状態を復元
                        if (treeBorder != null)
                            treeBorder.Visibility = state.TreeVisible ? Visibility.Visible : Visibility.Collapsed;

                        // LayoutMode を復元してUIを更新
                        layoutMode = state.LayoutMode;
                        ApplyLayoutMode();

                        // 起動時は常にグリッド表示にする
                        if (parametersGrid != null)
                            parametersGrid.Visibility = Visibility.Visible;
                        if (parametersTextBox != null)
                            parametersTextBox.Visibility = Visibility.Collapsed;
                        
                        if (negativePromptGrid != null)
                            negativePromptGrid.Visibility = Visibility.Visible;
                        if (negativePromptTextBox != null)
                            negativePromptTextBox.Visibility = Visibility.Collapsed;
                        
                        if (stepsGrid != null)
                            stepsGrid.Visibility = Visibility.Visible;
                        if (stepsTextBox != null)
                            stepsTextBox.Visibility = Visibility.Collapsed;
                    };
                }
            }
            catch { }
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

    public class WindowStateData
    {
        public double WindowWidth { get; set; }
        public double WindowHeight { get; set; }
        public double WindowLeft { get; set; }
        public double WindowTop { get; set; }
        public bool IsMaximized { get; set; }
        public bool IsFullScreen { get; set; }
        public int LayoutMode { get; set; }
        public bool TreeVisible { get; set; }
        public bool RightPanelVisible { get; set; }
        public bool ParametersVisible { get; set; }
        public bool NegativePromptVisible { get; set; }
        public bool StepsVisible { get; set; }
        public double TreeColumnWidth { get; set; }
        public double RightPanelColumnWidth { get; set; }
        public double TreeHeight { get; set; }
    }
}