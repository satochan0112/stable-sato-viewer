using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static System.Net.Mime.MediaTypeNames;
using WpfDataGrid = System.Windows.Controls.DataGrid;
using WpfDataGridCell = System.Windows.Controls.DataGridCell;
using WpfDragEventArgs = System.Windows.DragEventArgs;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace StableSatoViewer
{
    public partial class MainWindow : Window
    {
        // CA1861: Constant arrays extracted to static readonly fields
        private static readonly string[] LineBreakSeparators = ["\r\n", "\n"];
        
        // CA1869: Cache JsonSerializerOptions to avoid creating new instances
        private static readonly System.Text.Json.JsonSerializerOptions JsonSerializerOptions = new() { WriteIndented = true };

        private string[]? pngFiles;
        private int currentIndex = 0;
        private int layoutMode = 0; // 0: 通常（左画像+右パネル）, 1: フロート（画像最大化+プロンプトフロート）, 2: 非表示（画像のみ）
        private List<FavoriteItem> favorites = [];
        private List<HistoryItem> history = [];
        private Dictionary<string, System.Windows.Media.ImageSource?> thumbnailMemoryCache = [];
        private string[]? allPngFilesInFolder;
        private bool isInitializing = false; // 初期化中フラグ（フォルダ選択イベントを抑制）
        private static string WindowStateFilePath => Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), "StableSatoViewer", "windowstate.json");

        private static string FavoritesFilePath => Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), "StableSatoViewer", "favorites.txt");

        private static string HistoryFilePath => Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), "StableSatoViewer", "history.json");

        private static string ThumbnailCacheDirPath => Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), "StableSatoViewer", "ThumbnailCache");

        public MainWindow()
        {
            InitializeComponent();
            // 読み込んだお気に入りに基づき UI を更新
            LoadFavorites();
            UpdateFavoritesIndicator();

            // 履歴を読み込み
            LoadHistory();

            // ウィンドウの状態を復元
            RestoreWindowState();

            // ドライブとフォルダツリーを初期化し、最後のフォルダを復元します
            try
            {
                BuildFolderTree();
                
                // 前回表示していた画像を復元
                var lastImage = LoadLastImage();
                if (!string.IsNullOrEmpty(lastImage) && File.Exists(lastImage))
                {
                    string dir = Path.GetDirectoryName(lastImage)!;
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
            // もしコマンドライン引数で PNG ファイルが渡されていれば、最初に表示します
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
                            string dir = Path.GetDirectoryName(first)!;
                            pngFiles = Directory.GetFiles(dir, "*.png").OrderBy(f => f).ToArray();
                            currentIndex = Array.IndexOf(pngFiles, first);
                            if (currentIndex < 0) currentIndex = 0;
                            // ShowImage は UI 要素を更新します。InitializeComponent 後は安全に実行できます
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

        /// <summary>
        /// フィルターテキストボックスの KeyDown イベントハンドラー。Enter キー押下時にフィルター処理を実行します。
        /// </summary>
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

        /// <summary>
        /// データグリッドの KeyDown イベントハンドラー。左右矢印キーで画像を切り替えます。
        /// </summary>
        private void DataGrid_PreviewKeyDown(object sender, WpfKeyEventArgs e)
        {
            if (e.Key == Key.Left || e.Key == Key.Right)
            {
                MainWindow_KeyDown(this, e);
                e.Handled = true;
            }
        }

        /// <summary>
        /// データグリッドセルのマウスクリック時に、セルのテキストをクリップボードにコピーします。
        /// </summary>
        private void DataGrid_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // マウスの下にあるセルをヒットテストで探す
            var dep = (DependencyObject)e.OriginalSource;
            while (dep is not WpfDataGridCell)
            {
                dep = VisualTreeHelper.GetParent(dep);
                if (dep == null) return;
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

        /// <summary>
        /// テキストボックスの KeyDown イベントハンドラー。左右矢印キーで画像を切り替えます。
        /// </summary>
        private void TextBox_PreviewKeyDown(object sender, WpfKeyEventArgs e)
        {
            if (e.Key == Key.Left || e.Key == Key.Right)
            {
                MainWindow_KeyDown(this, e);
                e.Handled = true;
            }
        }

        /// <summary>
        /// ウィンドウのキーボード入力を処理します。矢印キー、Escape キー、Delete キーなどの機能を実装しています。
        /// </summary>
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
                    fullScreenToggle?.IsChecked = false;
                    e.Handled = true;
                    return;
                }
            }

            // Delete キーで画像をゴミ箱に移動
            if (e.Key == Key.Delete)
            {
                if (imageBox?.Source is BitmapImage bm && bm.UriSource != null)
                {
                    string filePath = bm.UriSource.LocalPath;
                    string fileName = System.IO.Path.GetFileName(filePath);
                    System.Windows.MessageBoxResult result = System.Windows.MessageBox.Show(
                        $"Are you sure you want to delete '{fileName}'?",
                        "Delete Image",
                        System.Windows.MessageBoxButton.YesNo,
                        System.Windows.MessageBoxImage.None
                    );

                    if (result == System.Windows.MessageBoxResult.Yes)
                    {
                        try
                        {
                            if (File.Exists(filePath))
                            {
                                // ビットマップの参照を解放
                                imageBox.Source = null;
                                
                                // ガベージコレクションを強制実行してメモリを解放
                                System.GC.Collect();
                                System.GC.WaitForPendingFinalizers();
                                System.GC.Collect();
                                
                                // 遅延実行でファイルを削除（UI系の参照がすべて解放されるのを待つ）
                                this.Dispatcher.InvokeAsync(async () =>
                                {
                                    try
                                    {
                                        // さらにメモリ解放
                                        await System.Threading.Tasks.Task.Delay(50);
                                        System.GC.Collect();
                                        System.GC.WaitForPendingFinalizers();
                                        
                                        if (File.Exists(filePath))
                                        {
                                            // ファイルをゴミ箱に移動
                                            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                                                filePath,
                                                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                                                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin
                                            );
                                            
                                            pngFiles = pngFiles.Where(f => f != filePath).ToArray();
                                            allPngFilesInFolder = allPngFilesInFolder?.Where(f => f != filePath).ToArray();

                                            if (pngFiles.Length == 0)
                                            {
                                                ShowToast("No more images");
                                                folderFilesListBox.ItemsSource = null;
                                                return;
                                            }

                                            if (currentIndex >= pngFiles.Length)
                                            {
                                                currentIndex = pngFiles.Length - 1;
                                            }

                                            ShowImage(pngFiles[currentIndex]);
                                            folderFilesListBox.ItemsSource = pngFiles.Select(f => System.IO.Path.GetFileName(f)).ToList();
                                            ShowToast($"Deleted '{fileName}'");
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        ShowToast($"Delete failed: {ex.Message}");
                                    }
                                }, System.Windows.Threading.DispatcherPriority.Background);
                            }
                        }
                        catch (Exception ex)
                        {
                            ShowToast($"Delete failed: {ex.Message}");
                        }
                    }
                }
                e.Handled = true;
                return;
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
                        string? parentDir = Directory.GetParent(currentDir)?.FullName;
                        if (!string.IsNullOrEmpty(parentDir))
                        {
                            var subDirs = Directory.GetDirectories(parentDir).OrderBy(d => d).ToArray();
                            int idx = Array.IndexOf(subDirs, currentDir);
                            if (idx >= 0 && idx < subDirs.Length - 1)
                            {
                                isInitializing = true;
                                SelectFolderInTree(subDirs[idx + 1]);
                                isInitializing = false;
                                ApplyFilterToNewFolder(subDirs[idx + 1]);
                                e.Handled = true;
                                return;
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
                        string? parentDir = Directory.GetParent(currentDir)?.FullName;
                        if (!string.IsNullOrEmpty(parentDir))
                        {
                            var subDirs = Directory.GetDirectories(parentDir).OrderBy(d => d).ToArray();
                            int idx = Array.IndexOf(subDirs, currentDir);
                            if (idx > 0)
                            {
                                isInitializing = true;
                                SelectFolderInTree(subDirs[idx - 1]);
                                isInitializing = false;
                                // フィルター適用後、最後のファイルを表示するためにコールバックを使用
                                this.Dispatcher.InvokeAsync(() =>
                                {
                                    if (pngFiles != null && pngFiles.Length > 0)
                                    {
                                        currentIndex = pngFiles.Length - 1;
                                        ShowImage(pngFiles[currentIndex]);
                                    }
                                });
                                ApplyFilterToNewFolder(subDirs[idx - 1]);
                                e.Handled = true;
                                return;
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

        /// <summary>
        /// マウスプレビュー MouseDown イベント。マウスブラウザボタン（戻る/進む）で画像を切り替えます。
        /// </summary>
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

        /// <summary>
        /// 指定されたパスの PNG 画像ファイルを表示し、メタデータを解析して UI に反映させます。
        /// </summary>
        private void ShowImage(string path)
        {
            var bitmap = new BitmapImage(new Uri(path))
            {
                CacheOption = BitmapCacheOption.OnLoad
            };
            bitmap.Freeze();
            imageBox.Source = bitmap;

            // 最後に表示した画像を保存
            SaveLastImage(path);

            // 背景アイコンを非表示
            if (bitmap != null)
            {
                // imageBorder 内のグリッドの子要素から背景画像を探す
                try
                {
                    if (imageBorder.Child is Grid innerGrid)
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

            // PNG の tEXt チャンクを読み取って表示
            ExtractAndDisplayTextChunks(path);

            // 画像が変更されたとき、お気に入り表示を更新
            UpdateFavoritesIndicator();

            // 履歴に記録
            AddToHistory(path);

            // 左パネルのファイル一覧を現在の画像のディレクトリで表示し、選択状態を反映する
            try
            {
                if (folderFilesListBox != null && pngFiles != null)
                {
                    var dir = System.IO.Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    {
                        // 現在の folderFilesListBox のディレクトリと異なる場合のみ更新
                        if (folderFilesListBox.Tag as string != dir)
                        {
                            // フィルター状態がない場合のみファイルリストを再取得
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

        /// <summary>
        /// ウィンドウタイトルを「ファイル名（現在のインデックス／全体） - [フォルダ名]」の形式で更新します。
        /// </summary>
        private void UpdateWindowTitle(string path)
        {
            try
            {
                string fileName = System.IO.Path.GetFileName(path);
                string folderName = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path) ?? "");
                int total = (pngFiles != null) ? pngFiles.Length : 0;
                int index = (pngFiles != null) ? (Array.IndexOf(pngFiles, path) + 1) : 0;
                
                if (total > 0 && index > 0)
                {
                    this.Title = $"{fileName} ({index}/{total}) - [{folderName}]";
                }
                else
                {
                    this.Title = $"{fileName} - [{folderName}]";
                }
                this.Title += " - StableSatoViewer";
            }
            catch
            {
                // タイトル更新エラーを無視
            }
        }

        /// <summary>
        /// PNG ファイルからメタデータチャンク（tEXt、iTXt）を抽出し、画面に表示します。
        /// </summary>
        private void ExtractAndDisplayTextChunks(string filePath)
        {
            Debug.WriteLine(filePath);
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            using var br = new BinaryReader(fs);
            
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

                if (chunkType == "tEXt" || chunkType == "iTXt")
                {
                    string text = Encoding.UTF8.GetString(data);

                    int nullIndex = text.IndexOf('\0');
                    if (nullIndex > 0)
                    {
                        string key = text[..nullIndex];
                        string value = text[(nullIndex + 1)..];

                        if (key.Equals("parameters", StringComparison.OrdinalIgnoreCase))
                        {
                            Debug.WriteLine(value);
                            int negPromptIndex = value.IndexOf("Negative prompt:");
                            int stepsIndex = value.IndexOf("Steps:");

                            if (negPromptIndex >= 0)
                            {
                                parameters = value[..negPromptIndex].Trim();

                                if (stepsIndex >= 0)
                                {
                                    negativePrompt = value[(negPromptIndex + "Negative prompt:".Length)..stepsIndex].Trim();
                                    steps = value[(stepsIndex + "Steps:".Length)..].Trim();
                                }
                                else
                                {
                                    negativePrompt = value[(negPromptIndex + "Negative prompt:".Length)..].Trim();
                                }
                            }
                            else if (stepsIndex >= 0)
                            {
                                parameters = value[..stepsIndex].Trim();
                                steps = value[stepsIndex..].Trim();
                            }
                            else
                            {
                                parameters = value.Trim();
                            }
                        }
                        else
                        {
                            Debug.WriteLine($"Unknown key: {key} text: {text}");
                        }
                    }
                    else
                    {
                        Debug.WriteLine($"Invalid chunk format (no null separator): {text}");
                    }
                }
                else
                {
                    Debug.WriteLine($"Skipped chunk: {chunkType}");
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

        /// <summary>
        /// テキストを行単位で分割し、データグリッドに表示します。
        /// </summary>
        private static void DisplayTextAsGrid(WpfDataGrid grid, string text)
        {
            var items = new ObservableCollection<SimpleItem>();

            if (!string.IsNullOrEmpty(text))
            {
                // 改行で分割
                string[] lines = text.Split(LineBreakSeparators, StringSplitOptions.None);
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

        /// <summary>
        /// Steps 情報をキー値ペアでグリッドに表示します。
        /// </summary>
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

        /// <summary>
        /// テキストをコロン記号で区切られたキー値ペアに解析します。
        /// </summary>
        private static List<(string Key, string Value)> ParseKeyValuePairs(string text)
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

        /// <summary>
        /// バイト配列をビッグエンディアン形式の 32 ビット整数に変換します。
        /// </summary>
        private static int ReadInt32BigEndian(byte[] bytes)
        {
            return (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];
        }

        /// <summary>
        /// レイアウトモード（通常、フロート、非表示）を循環切り替えします。
        /// </summary>
        private void ToggleButton_Click(object sender, RoutedEventArgs e)
        {
            // 3つのモードを順に切り替え
            layoutMode = (layoutMode + 1) % 3;
            ApplyLayoutMode();
        }

        /// <summary>
        /// 現在のレイアウトモードに基づいて UI の表示状態を切り替えます。
        /// </summary>
        private void ApplyLayoutMode()
        {
            // DockPanel 内のグリッドを取得
            if (this.Content is not DockPanel dockPanel) return;

            // DockPanel 内のすべての子要素からメイングリッドを探す
            Grid? mainGrid = null;
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
            
            // 右側のグリッド（Column=4）を探す
            Grid? rightGrid = null;
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
                    // 画像、右分割線、右列を復元
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

        /// <summary>
        /// フローティングプロンプトに現在のパラメータテキストを表示（グリッド形式）します。
        /// </summary>
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

        /// <summary>
        /// 全画面表示と通常表示を切り替えます。
        /// </summary>
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

        /// <summary>
        /// パラメータテキストの表示形式をグリッド表示と生テキスト表示で切り替えます。
        /// </summary>
        private void ParametersToggleButton_Click(object sender, RoutedEventArgs e)
        {
            if (parametersTextBox.Visibility == Visibility.Visible)
            {
                // グリッド表示に切り替え
                parametersTextBox.Visibility = Visibility.Collapsed;
                parametersGrid.Visibility = Visibility.Visible;

                // テキストからグリッドを更新
                var lines = parametersTextBox.Text.Split(LineBreakSeparators, StringSplitOptions.None);
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

        /// <summary>
        /// ネガティブプロンプトの表示形式をグリッド表示と生テキスト表示で切り替えます。
        /// </summary>
        private void NegativeToggleButton_Click(object sender, RoutedEventArgs e)
        {
            if (negativePromptTextBox.Visibility == Visibility.Visible)
            {
                // グリッド表示に切り替え
                negativePromptTextBox.Visibility = Visibility.Collapsed;
                negativePromptGrid.Visibility = Visibility.Visible;

                // テキストからグリッドを更新
                var lines = negativePromptTextBox.Text.Split(LineBreakSeparators, StringSplitOptions.None);
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
                // グリッドアイテムから生テキストを構築
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

        /// <summary>
        /// Steps 情報の表示形式をグリッド表示と生テキスト表示で切り替えます。
        /// </summary>
        private void StepsToggleButton_Click(object sender, RoutedEventArgs e)
        {
            if (stepsTextBox.Visibility == Visibility.Visible)
            {
                // グリッド表示に切り替え
                stepsTextBox.Visibility = Visibility.Collapsed;
                stepsGrid.Visibility = Visibility.Visible;

                // テキストからグリッドを更新 - key:value ラインを解析
                var lines = stepsTextBox.Text.Split(LineBreakSeparators, StringSplitOptions.RemoveEmptyEntries);
                var items = new ObservableCollection<StepsItem>();
                foreach (var line in lines)
                {
                    var idx = line.IndexOf(':');
                    if (idx > 0)
                    {
                        var key = line[..idx].Trim();
                        var value = line[(idx + 1)..].Trim();
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

        /// <summary>
        /// 前の画像を表示します。フォルダの最初に達したら前のフォルダの最後に移動します。
        /// </summary>
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
                    string? parentDir = Directory.GetParent(currentDir)?.FullName;
                    if (!string.IsNullOrEmpty(parentDir))
                    {
                        var subDirs = Directory.GetDirectories(parentDir).OrderBy(d => d).ToArray();
                        int idx = Array.IndexOf(subDirs, currentDir);
                        if (idx > 0)
                        {
                            isInitializing = true;
                            SelectFolderInTree(subDirs[idx - 1]);
                            isInitializing = false;
                            // フィルター適用後、最後のファイルを表示するためにコールバックを使用
                            this.Dispatcher.InvokeAsync(() =>
                            {
                                if (pngFiles != null && pngFiles.Length > 0)
                                {
                                    currentIndex = pngFiles.Length - 1;
                                    ShowImage(pngFiles[currentIndex]);
                                }
                            });
                            ApplyFilterToNewFolder(subDirs[idx - 1]);
                            return;
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

        /// <summary>
        /// 次の画像を表示します。フォルダの最後に達したら次のフォルダの最初に移動します。
        /// </summary>
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
                    string? parentDir = Directory.GetParent(currentDir)?.FullName;
                    if (!string.IsNullOrEmpty(parentDir))
                    {
                        var subDirs = Directory.GetDirectories(parentDir).OrderBy(d => d).ToArray();
                        int idx = Array.IndexOf(subDirs, currentDir);
                        if (idx >= 0 && idx < subDirs.Length - 1)
                        {
                            isInitializing = true;
                            SelectFolderInTree(subDirs[idx + 1]);
                            isInitializing = false;
                            ApplyFilterToNewFolder(subDirs[idx + 1]);
                            return;
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

        /// <summary>
        /// トースト通知を表示します。1.5 秒後に自動消去されます。
        /// </summary>
        private async void ShowToast(string message)
        {
            toastText.Text = message;
            toastBorder.Visibility = Visibility.Visible;
            await System.Threading.Tasks.Task.Delay(1500);
            toastBorder.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// お気に入りポップアップを表示します。
        /// </summary>
        private async void FavoritesButton_Click(object sender, RoutedEventArgs e)
        {
            LoadFavorites();
            favoritesListBox.ItemsSource = null;
            var collection = new System.Collections.ObjectModel.ObservableCollection<FavoriteItem>(favorites);
            favoritesListBox.ItemsSource = collection;
            favoritesPopup.IsOpen = true;

            await Task.Run(() =>
            {
                foreach (var item in collection)
                {
                    if (item.ThumbnailImage == null && !string.IsNullOrEmpty(item.FilePath))
                    {
                        if (!thumbnailMemoryCache.TryGetValue(item.FilePath, out var cached))
                        {
                            Dispatcher.Invoke(() =>
                            {
                                cached = GetOrGenerateThumbnail(item.FilePath);
                                thumbnailMemoryCache[item.FilePath] = cached;
                                item.ThumbnailImage = cached;
                            });
                        }
                        else
                        {
                            Dispatcher.Invoke(() =>
                            {
                                item.ThumbnailImage = cached;
                            });
                        }
                    }
                }
            });
        }

        /// <summary>
        /// 履歴ボタンクリック時のイベントハンドラ。履歴を読み込んでポップアップを表示します。
        /// </summary>
        private async void HistoryButton_Click(object sender, RoutedEventArgs e)
        {
            // 履歴を読み込み
            LoadHistory();

            // 履歴を新しい順にソート
            var sortedHistory = history.OrderByDescending(h => h.OpenedAt).ToList();

            // DataGrid に設定
            historyListBox.ItemsSource = null;
            historyListBox.ItemsSource = new System.Collections.ObjectModel.ObservableCollection<HistoryItem>(sortedHistory);
            historyPopup.IsOpen = true;

            await Task.Run(() =>
            {
                foreach (var item in sortedHistory)
                {
                    if (item.ThumbnailImage == null && !string.IsNullOrEmpty(item.FilePath))
                    {
                        if (!thumbnailMemoryCache.TryGetValue(item.FilePath, out var cached))
                        {
                            Dispatcher.Invoke(() =>
                            {
                                cached = GetOrGenerateThumbnail(item.FilePath);
                                thumbnailMemoryCache[item.FilePath] = cached;
                                item.ThumbnailImage = cached;
                            });
                        }
                        else
                        {
                            Dispatcher.Invoke(() =>
                            {
                                item.ThumbnailImage = cached;
                            });
                        }
                    }
                }
            });
        }

        /// <summary>
        /// 現在表示中の画像をお気に入りに追加します。
        /// </summary>
        private void AddFavoriteButton_Click(object sender, RoutedEventArgs e)
        {
            if (imageBox?.Source is BitmapImage bm && bm.UriSource != null)
            {
                var path = bm.UriSource.LocalPath;
                if (!favorites.Any(f => f.FilePath == path))
                {
                    var newFavorite = new FavoriteItem
                    {
                        FilePath = path,
                        FileName = System.IO.Path.GetFileName(path)
                    };
                    favorites.Add(newFavorite);
                    SaveFavorites();
                    favoritesListBox.ItemsSource = null;
                    favoritesListBox.ItemsSource = new System.Collections.ObjectModel.ObservableCollection<FavoriteItem>(favorites);
                    ShowToast("Added to favorites");
                    UpdateFavoritesIndicator();

                    Task.Run(() =>
                    {
                        if (!thumbnailMemoryCache.TryGetValue(path, out var cached))
                        {
                            Dispatcher.Invoke(() =>
                            {
                                cached = GetOrGenerateThumbnail(path);
                                thumbnailMemoryCache[path] = cached;
                                newFavorite.ThumbnailImage = cached;
                            });
                        }
                        else
                        {
                            Dispatcher.Invoke(() =>
                            {
                                newFavorite.ThumbnailImage = cached;
                            });
                        }
                    });
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

        /// <summary>
        /// すべてのお気に入りを削除します。
        /// </summary>
        private void RemoveAllFavoritesButton_Click(object sender, RoutedEventArgs e)
        {
            favorites.Clear();
            SaveFavorites();
            favoritesListBox.ItemsSource = null;
            ShowToast("All favorites removed");
            UpdateFavoritesIndicator();
        }

        private System.Windows.Media.ImageSource? GetOrGenerateThumbnail(string imagePath)
        {
            try
            {
                if (!File.Exists(imagePath))
                    return null;

                if (!Directory.Exists(ThumbnailCacheDirPath))
                    Directory.CreateDirectory(ThumbnailCacheDirPath);

                string cacheFileName = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(imagePath)).Aggregate("", (s, b) => s + b.ToString("x2")) + ".png";
                string cachePath = Path.Combine(ThumbnailCacheDirPath, cacheFileName);

                if (File.Exists(cachePath))
                {
                    return CreateBitmapImage(cachePath);
                }

                var originalBitmap = CreateBitmapImage(imagePath);
                if (originalBitmap == null)
                    return null;

                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create((BitmapSource)originalBitmap));

                using (var fileStream = new FileStream(cachePath, FileMode.Create))
                {
                    encoder.Save(fileStream);
                }

                return originalBitmap;
            }
            catch
            {
                return null;
            }
        }

        private System.Windows.Media.ImageSource? CreateBitmapImage(string imagePath)
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(imagePath);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = 64;
                bitmap.EndInit();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        private System.Windows.Media.ImageSource? GetCachedThumbnail(string imagePath)
        {
            if (thumbnailMemoryCache.TryGetValue(imagePath, out var cached))
            {
                return cached;
            }

            var thumbnail = GetOrGenerateThumbnail(imagePath);
            thumbnailMemoryCache[imagePath] = thumbnail;
            return thumbnail;
        }

        /// <summary>
        /// お気に入り設定ファイルをエクスプローラーで開きます。
        /// </summary>
        private void EditFavoritesButton_Click(object sender, RoutedEventArgs e)
        {
            // 簡単な編集: ファイルをエクスプローラーで開く
            var dir = Path.GetDirectoryName(FavoritesFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{FavoritesFilePath}\"") { UseShellExecute = true });
        }

        /// <summary>
        /// お気に入りリストの項目をダブルクリック時、その画像を表示します。
        /// </summary>
        private void FavoritesListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (favoritesListBox.SelectedItem is FavoriteItem item && !string.IsNullOrEmpty(item.FilePath))
            {
                var path = item.FilePath;
                if (File.Exists(path))
                {
                    string dir = Path.GetDirectoryName(path)!;
                    pngFiles = Directory.GetFiles(dir, "*.png").OrderBy(f => f).ToArray();
                    allPngFilesInFolder = pngFiles;
                    
                    // ファイルリストを更新（ShowImage前に更新する必要があります）
                    folderFilesListBox.ItemsSource = pngFiles.Select(f => System.IO.Path.GetFileName(f)).ToList();
                    folderFilesListBox.Tag = dir;
                    
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

        /// <summary>
        /// 履歴リストの項目をダブルクリックしたとき、その画像を表示します。
        /// </summary>
        private void HistoryListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (historyListBox.SelectedItem is HistoryItem historyItem)
            {
                if (File.Exists(historyItem.FilePath))
                {
                    string dir = Path.GetDirectoryName(historyItem.FilePath)!;
                    pngFiles = Directory.GetFiles(dir, "*.png").OrderBy(f => f).ToArray();
                    allPngFilesInFolder = pngFiles;

                    // ファイルリストを更新（ShowImage前に更新する必要があります）
                    folderFilesListBox.ItemsSource = pngFiles.Select(f => System.IO.Path.GetFileName(f)).ToList();
                    folderFilesListBox.Tag = dir;

                    currentIndex = Array.IndexOf(pngFiles, historyItem.FilePath);
                    if (currentIndex < 0) currentIndex = 0;
                    ShowImage(pngFiles[currentIndex]);
                    historyPopup.IsOpen = false;
                    // フォルダツリーで当該フォルダを選択
                    SelectFolderInTree(dir);
                }
                else
                {
                    ShowToast("History image not found");
                }
            }
        }

        /// <summary>
        /// お気に入りリストを設定ファイルに保存します。
        /// </summary>
        private void SaveFavorites()
        {
            try
            {
                var dir = Path.GetDirectoryName(FavoritesFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var json = System.Text.Json.JsonSerializer.Serialize(favorites, JsonSerializerOptions);
                File.WriteAllText(FavoritesFilePath, json, Encoding.UTF8);
            }
            catch
            {
            }
        }

        /// <summary>
        /// 設定ファイルからお気に入りリストを読み込みます。
        /// </summary>
        private void LoadFavorites()
        {
            try
            {
                if (File.Exists(FavoritesFilePath))
                {
                    var json = File.ReadAllText(FavoritesFilePath, Encoding.UTF8);
                    favorites = System.Text.Json.JsonSerializer.Deserialize<List<FavoriteItem>>(json, JsonSerializerOptions) ?? [];
                }
                else
                {
                    favorites = [];
                }
            }
            catch
            {
                favorites = [];
            }
        }

        /// <summary>
        /// すべての履歴をクリアします。
        /// </summary>
        private void ClearAllHistoryButton_Click(object sender, RoutedEventArgs e)
        {
            history.Clear();
            SaveHistory();
            historyListBox.ItemsSource = null;
            ShowToast("All history cleared");
        }

        /// <summary>
        /// 設定ファイルから履歴リストを読み込みます。
        /// </summary>
        private void LoadHistory()
        {
            try
            {
                if (File.Exists(HistoryFilePath))
                {
                    var json = File.ReadAllText(HistoryFilePath, Encoding.UTF8);
                    history = System.Text.Json.JsonSerializer.Deserialize<List<HistoryItem>>(json, JsonSerializerOptions) ?? [];
                }
                else
                {
                    history = [];
                }
            }
            catch
            {
                history = [];
            }
        }

        /// <summary>
        /// 履歴リストを設定ファイルに保存します。
        /// </summary>
        private void SaveHistory()
        {
            try
            {
                var dir = Path.GetDirectoryName(HistoryFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var json = System.Text.Json.JsonSerializer.Serialize(history, JsonSerializerOptions);
                File.WriteAllText(HistoryFilePath, json, Encoding.UTF8);
            }
            catch
            {
                // エラーは無視
            }
        }

        /// <summary>
        /// 画像を履歴に追加します。同じ画像でも毎回新しいエントリとして記録します。
        /// </summary>
        private void AddToHistory(string filePath)
        {
            try
            {
                LoadHistory();

                // 新しいエントリを追加（重複を許可）
                history.Add(new HistoryItem
                {
                    FilePath = filePath,
                    FileName = System.IO.Path.GetFileName(filePath),
                    OpenedAt = DateTime.Now
                });

                SaveHistory();

                Task.Run(() =>
                {
                    if (!thumbnailMemoryCache.TryGetValue(filePath, out var cached))
                    {
                        Dispatcher.Invoke(() =>
                        {
                            cached = GetOrGenerateThumbnail(filePath);
                            thumbnailMemoryCache[filePath] = cached;
                            if (history.FirstOrDefault(h => h.FilePath == filePath) is HistoryItem item)
                            {
                                item.ThumbnailImage = cached;
                            }
                        });
                    }
                    else
                    {
                        Dispatcher.Invoke(() =>
                        {
                            if (history.FirstOrDefault(h => h.FilePath == filePath) is HistoryItem item)
                            {
                                item.ThumbnailImage = cached;
                            }
                        });
                    }
                });
            }
            catch
            {
                // エラーは無視
            }
        }

        /// <summary>
        /// 現在表示中の画像がお気に入りかどうかを表示ボタンで視覚的に表示します。
        /// </summary>
        private void UpdateFavoritesIndicator()
        {
            try
            {
                if (favoritesButton == null) return;
                if (imageBox?.Source is BitmapImage bm && bm.UriSource != null)
                {
                    if (favorites.Any(f => f.FilePath == bm.UriSource.LocalPath))
                    {
                        favoritesButton.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#60a0ff")!;
                        favoritesButton.Foreground = System.Windows.Media.Brushes.White;
                        favoritesButton.ToolTip = "Favorited";
                        return;
                    }
                }
                favoritesButton.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#2d2d2d")!;
                favoritesButton.Foreground = System.Windows.Media.Brushes.White;
                favoritesButton.ToolTip = "Favorites";
            }
            catch
            {
            }
        }

        /// <summary>
        /// PNG ファイルのドラッグを受け付け、他のファイル型は却下します。
        /// </summary>
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

        /// <summary>
        /// ドラッグドロップされた PNG ファイルを開きます。
        /// </summary>
        private void ImageBorder_Drop(object sender, WpfDragEventArgs e)
        {
            if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)) return;
            var files = (string[]?)e.Data.GetData(System.Windows.DataFormats.FileDrop);
            if (files == null || files.Length == 0) return;

            // 最初のファイルが PNG なら読み込む
            var first = files[0];
            if (!File.Exists(first)) return;
            if (!string.Equals(Path.GetExtension(first), ".png", StringComparison.OrdinalIgnoreCase)) return;

            try
            {
                string dir = Path.GetDirectoryName(first)!;
                pngFiles = Directory.GetFiles(dir, "*.png").OrderBy(f => f).ToArray();
                currentIndex = Array.IndexOf(pngFiles, first);
                if (currentIndex < 0) currentIndex = 0;
                ShowImage(pngFiles[currentIndex]);
                // フォルダツリーでもこのフォルダを選択
                SelectFolderInTree(dir);
            }
            catch
            {
                // 無視
            }
        }

        /// <summary>
        /// ドライブを取得してフォルダツリーを初期化します。
        /// </summary>
        private void BuildFolderTree()
        {
            folderTreeView.Items.Clear();
            try
            {
                foreach (var d in DriveInfo.GetDrives().Where(d => d.IsReady))
                {
                    var ti = new TreeViewItem
                    {
                        Header = d.Name,
                        Tag = d.RootDirectory.FullName,
                        Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#e0e0e0")!
                    };
                    ti.Items.Add(null);
                    ti.Expanded += Folder_Expanded;
                    folderTreeView.Items.Add(ti);
                }
            }
            catch { }
        }

        /// <summary>
        /// フォルダツリーノードが展開される際、遅延ロードされたサブフォルダを読み込む
        /// </summary>
        private void Folder_Expanded(object sender, RoutedEventArgs e)
        {
            if (sender is TreeViewItem ti)
            {
                if (ti.Items.Count == 1 && ti.Items[0] == null)
                {
                    ti.Items.Clear();
                    try
                    {
                        if (ti.Tag is string path)
                        {
                            foreach (var sub in Directory.GetDirectories(path))
                            {
                                var child = new TreeViewItem
                                {
                                    Header = Path.GetFileName(sub),
                                    Tag = sub,
                                    Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#e0e0e0")!
                                };
                                child.Items.Add(null);
                                child.Expanded += Folder_Expanded;
                                ti.Items.Add(child);
                            }
                        }
                    }
                    catch { }
                }
            }
        }

        /// <summary>
        /// フォルダツリーでフォルダが選択された際、そのフォルダの PNG ファイル一覧を読み込み表示します。
        /// </summary>
        private void FolderTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            // 初期化中はイベントを無視
            if (isInitializing) return;

            if (folderTreeView.SelectedItem is TreeViewItem t && t.Tag is string p)
            {
                SaveLastFolder(p);
                LoadImagesFromFolderWithFilter(p);
                // ファイル一覧を読み込む
                try
                {
                    var files = Directory.GetFiles(p, "*.png").OrderBy(x => x).ToArray();
                    allPngFilesInFolder = files; // すべてのファイルを保存

                    // フィルターがアクティブな場合は、新しいフォルダに適用します。それ以外の場合はすべてを表示
                    if (filterTextBox != null && !string.IsNullOrWhiteSpace(filterTextBox.Text))
                    {
                        // FilterButton_Click は allPngFilesInFolder が設定されていることに依存しています
                        folderFilesListBox.Tag = p; // 現在のフォルダを保存
                        FilterButton_Click(filterButton, new RoutedEventArgs());
                    }
                    else
                    {
                        pngFiles = files; // フィルターをリセット
                        folderFilesListBox.ItemsSource = files.Select(f => System.IO.Path.GetFileName(f)).ToList();
                        folderFilesListBox.Tag = p; // 現在のフォルダを保存
                        // filterTextBox をクリアしない - ユーザーの入力を保持
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

        /// <summary>
        /// ファイルリストの項目をダブルクリック時、その画像を表示します。
        /// </summary>
        private void FolderFilesListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (folderFilesListBox.SelectedItem is string name && folderFilesListBox.Tag is string dir)
            {
                var full = System.IO.Path.Combine(dir, name);
                if (File.Exists(full))
                {
                    // ディレクトリのファイルを pngFiles に設定し、選択したファイルを表示
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

        /// <summary>
        /// 最後に選択されたフォルダパスを設定ファイルに保存します。
        /// </summary>
        private static void SaveLastFolder(string dir)
        {
            try
            {
                var fn = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StableSatoViewer");
                if (!Directory.Exists(fn)) Directory.CreateDirectory(fn);
                File.WriteAllText(Path.Combine(fn, "lastfolder.txt"), dir, Encoding.UTF8);
            }
            catch { }
        }

        /// <summary>
        /// 最後に表示された画像パスを設定ファイルに保存します。
        /// </summary>
        private static void SaveLastImage(string imagePath)
        {
            try
            {
                var fn = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StableSatoViewer");
                if (!Directory.Exists(fn)) Directory.CreateDirectory(fn);
                File.WriteAllText(Path.Combine(fn, "lastimage.txt"), imagePath, Encoding.UTF8);
            }
            catch { }
        }

        /// <summary>
        /// 指定パスをフォルダツリーで検索し、展開・選択します。
        /// </summary>
        private void SelectFolderInTree(string path)
        {
            try
            {
                // パスを正規化
                path = System.IO.Path.GetFullPath(path).TrimEnd(System.IO.Path.DirectorySeparatorChar);

                // ドライブを探す
                string drive = (System.IO.Path.GetPathRoot(path) ?? "").TrimEnd(System.IO.Path.DirectorySeparatorChar);
                
                TreeViewItem? driveNode = null;
                foreach (TreeViewItem t in folderTreeView.Items)
                {
                    string? nodeTag = t.Tag as string;
                    if (!string.IsNullOrEmpty(nodeTag))
                    {
                        string nodeRoot = (System.IO.Path.GetPathRoot(nodeTag) ?? "").TrimEnd(System.IO.Path.DirectorySeparatorChar);
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
                string[] pathParts = path[drive.Length..].Trim(System.IO.Path.DirectorySeparatorChar).Split(System.IO.Path.DirectorySeparatorChar);

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
                            if (currentNode.Tag is string pathTag)
                            {
                                foreach (var sub in Directory.GetDirectories(pathTag))
                                {
                                    var child = new TreeViewItem
                                    {
                                        Header = Path.GetFileName(sub),
                                        Tag = sub,
                                        Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#e0e0e0")!
                                    };
                                    child.Items.Add(null);
                                    child.Expanded += Folder_Expanded;
                                    currentNode.Items.Add(child);
                                }
                            }
                        }
                        catch { }
                    }

                    // 次のノードを探す
                    TreeViewItem? nextNode = null;
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

        /// <summary>
        /// 指定フォルダから PNG ファイルを読み込み、最初の画像を表示します。
        /// </summary>
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

        /// <summary>
        /// 新しいフォルダに現在のフィルターを適用して画像一覧を再構築します。
        /// </summary>
        private void ApplyFilterToNewFolder(string newFolderPath)
        {
            try
            {
                var files = Directory.GetFiles(newFolderPath, "*.png").OrderBy(x => x).ToArray();
                allPngFilesInFolder = files;
                folderFilesListBox.Tag = newFolderPath;

                if (filterTextBox != null && !string.IsNullOrWhiteSpace(filterTextBox.Text))
                {
                    FilterButton_Click(filterButton, new RoutedEventArgs());
                }
                else
                {
                    pngFiles = files;
                    folderFilesListBox.ItemsSource = files.Select(f => System.IO.Path.GetFileName(f)).ToList();
                    if (pngFiles.Length > 0)
                    {
                        currentIndex = 0;
                        ShowImage(pngFiles[currentIndex]);
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// フォルダツリーの表示非表示を切り替えます。
        /// </summary>
        private void TreeToggleButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // DockPanel 内のメイングリッドを探す
                var dockPanel = (DockPanel)this.Content;
                Grid? mainGrid = null;
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
                    // 非表示にする
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

        /// <summary>
        /// ファイルリストにフォーカスがある時、左右キーで画像を切り替えます。
        /// </summary>
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
                            string? parentDir = Directory.GetParent(currentDir)?.FullName;
                            if (!string.IsNullOrEmpty(parentDir))
                            {
                                var subDirs = Directory.GetDirectories(parentDir).OrderBy(d => d).ToArray();
                                int idx = Array.IndexOf(subDirs, currentDir);
                                if (idx >= 0 && idx < subDirs.Length - 1)
                                {
                                    isInitializing = true;
                                    SelectFolderInTree(subDirs[idx + 1]);
                                    isInitializing = false;
                                    ApplyFilterToNewFolder(subDirs[idx + 1]);
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
                            string? parentDir = Directory.GetParent(currentDir)?.FullName;
                            if (!string.IsNullOrEmpty(parentDir))
                            {
                                var subDirs = Directory.GetDirectories(parentDir).OrderBy(d => d).ToArray();
                                int idx = Array.IndexOf(subDirs, currentDir);
                                if (idx > 0)
                                {
                                    isInitializing = true;
                                    SelectFolderInTree(subDirs[idx - 1]);
                                    isInitializing = false;
                                    // フィルター適用後、最後のファイルを表示するためにコールバックを使用
                                    this.Dispatcher.InvokeAsync(() =>
                                    {
                                        if (pngFiles != null && pngFiles.Length > 0)
                                        {
                                            currentIndex = pngFiles.Length - 1;
                                            ShowImage(pngFiles[currentIndex]);
                                        }
                                    });
                                    ApplyFilterToNewFolder(subDirs[idx - 1]);
                                }
                            }
                        }
                        catch { }
                    }
                    e.Handled = true;
                }
            }
        }

        /// <summary>
        /// ファイルリストの選択が変更された際、選択された画像を表示します。
        /// </summary>
        private void FolderFilesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (folderFilesListBox.SelectedItem is string name && folderFilesListBox.Tag is string dir)
                {
                    var full = System.IO.Path.Combine(dir, name);
                    if (File.Exists(full))
                    {
                        // 選択された画像を表示し、pngFiles/currentIndex を更新してナビゲーションがクラッシュしないようにします
                        var bitmap = new BitmapImage(new Uri(full))
                        {
                            CacheOption = BitmapCacheOption.OnLoad
                        };
                        bitmap.Freeze();
                        imageBox.Source = bitmap;
                        // pngFiles は既にフィルター状態を持っているので、そのまま使用
                        currentIndex = pngFiles != null ? Array.IndexOf(pngFiles, full) : -1;
                        if (currentIndex < 0) currentIndex = 0;

                        // 最後に表示した画像を保存
                        SaveLastImage(full);

                        // タイトルとテキストチャンクを更新
                        UpdateWindowTitle(full);
                        ExtractAndDisplayTextChunks(full);

                        // お気に入り表示を更新
                        UpdateFavoritesIndicator();
                    }
                }
            }
            catch
            {
                // 無視
            }
        }

        /// <summary>
        /// フィルターテキストに基づいて PNG ファイルを検索し、結果を表示します。
        /// </summary>
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

                        // PNG シグネチャをスキップ
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

                            if (chunkType == "tEXt" || chunkType == "iTXt")
                            {
                                string text = Encoding.UTF8.GetString(data);
                                int nullIndex = text.IndexOf('\0');
                                if (nullIndex >= 0)
                                {
                                    string key = text[..nullIndex];
                                    string value = text[(nullIndex + 1)..].ToLower();

                                    if (key.Equals("parameters", StringComparison.OrdinalIgnoreCase))
                                    {
                                        // 「Negative prompt:」までのパラメータテキストのみを検索
                                        string paramText = value;
                                        int negIndex = value.IndexOf("negative prompt:");
                                        if (negIndex >= 0)
                                        {
                                            paramText = value[..negIndex];
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
                        // 読み込みエラーを無視
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

        /// <summary>
        /// フィルターをクリアして全ファイル一覧を復元します。
        /// </summary>
        private void ClearFilterButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (filterTextBox == null) return;
                filterTextBox.Clear();

                if (allPngFilesInFolder != null)
                {
                    // 現在表示されている画像を記憶（ある場合）
                    string? currentImagePath = null;
                    if (imageBox?.Source is BitmapImage bm && bm.UriSource != null)
                    {
                        currentImagePath = bm.UriSource.LocalPath;
                    }

                    // ファイル一覧全体を復元
                    pngFiles = allPngFilesInFolder;
                    var names = pngFiles.Select(f => System.IO.Path.GetFileName(f)).ToList();
                    folderFilesListBox.ItemsSource = names;

                    // 現在表示されている画像が復元されたリストにある場合、それを選択したままにします
                    if (!string.IsNullOrEmpty(currentImagePath))
                    {
                        int idx = Array.IndexOf(pngFiles, currentImagePath);
                        if (idx >= 0)
                        {
                            currentIndex = idx;
                            folderFilesListBox.SelectedIndex = idx;
                            folderFilesListBox.ScrollIntoView(folderFilesListBox.SelectedItem);
                            // UI が同じ画像の解析されたテキスト/チャンクを反映していることを確認
                            ShowImage(pngFiles[currentIndex]);
                        }
                        else
                        {
                            // 現在表示されている画像はこのフォルダのファイルの一部ではありません。
                            // 表示されている画像をフォルダの最初の画像に切り替えないでください。
                            // 後でナビゲーションがクラッシュしないよう currentIndex を 0 のままにしておきます。
                            currentIndex = 0;
                            folderFilesListBox.SelectedIndex = -1;
                        }
                    }
                    else
                    {
                        // 画像が現在表示されていない場合：最初の画像の表示を強制しない
                        folderFilesListBox.SelectedIndex = -1;
                    }
                }
                ShowToast("Filter cleared");
            }
            catch { }
        }

        /// <summary>
        /// 現在表示中の画像をエクスプローラーで選択状態で開きます。
        /// </summary>
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

        /// <summary>
        /// 設定ファイルから最後に表示した画像パスを読み込みます。
        /// </summary>
        private static string? LoadLastImage()
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

        /// <summary>
        /// ウィンドウの状態（サイズ、位置、レイアウトモードなど）を JSON ファイルに保存します。
        /// </summary>
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

                var json = System.Text.Json.JsonSerializer.Serialize(state, JsonSerializerOptions);
                File.WriteAllText(WindowStateFilePath, json, Encoding.UTF8);
            }
            catch { }
        }

        /// <summary>
        /// JSON ファイルからウィンドウの状態を復元します。
        /// </summary>
        private void RestoreWindowState()
        {
            try
            {
                if (!File.Exists(WindowStateFilePath)) return;

                var json = File.ReadAllText(WindowStateFilePath, Encoding.UTF8);
                var state = System.Text.Json.JsonSerializer.Deserialize<WindowStateData>(json);

                if (state != null)
                {
                    // 全画面状態を復元（サイズ・位置の前に設定）
                    if (state.IsFullScreen)
                    {
                        this.WindowStyle = WindowStyle.None;
                        this.WindowState = WindowState.Maximized;
                        fullScreenToggle?.IsChecked = true;
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
                        treeBorder?.Visibility = state.TreeVisible ? Visibility.Visible : Visibility.Collapsed;

                        // LayoutMode を復元して UI を更新
                        layoutMode = state.LayoutMode;
                        ApplyLayoutMode();

                        // 起動時は常にグリッド表示にする
                        parametersGrid?.Visibility = Visibility.Visible;
                        parametersTextBox?.Visibility = Visibility.Collapsed;
                        
                        negativePromptGrid?.Visibility = Visibility.Visible;
                        negativePromptTextBox?.Visibility = Visibility.Collapsed;
                        
                        stepsGrid?.Visibility = Visibility.Visible;
                        stepsTextBox?.Visibility = Visibility.Collapsed;
                    };
                }
            }
            catch { }
        }
    }

    /// <summary>
    /// Steps パラメータをグリッド形式で表示するためのデータクラス
    /// </summary>
    public class StepsItem
    {
        /// <summary>Steps のキー</summary>
        public string Key { get; set; } = "";
        /// <summary>Steps の値</summary>
        public string Value { get; set; } = "";
    }

    /// <summary>
    /// パラメータ情報を行単位で表示するためのシンプルなデータクラス
    /// </summary>
    public class SimpleItem
    {
        /// <summary>テキスト行の値</summary>
        public string Value { get; set; } = "";
    }

    /// <summary>
    /// 開いた画像の履歴を記録するためのデータクラス
    /// </summary>
    public class HistoryItem : System.ComponentModel.INotifyPropertyChanged
    {
        private System.Windows.Media.ImageSource? _thumbnailImage;

        /// <summary>画像ファイルのフルパス</summary>
        public string FilePath { get; set; } = "";
        /// <summary>ファイル名（表示用）</summary>
        public string FileName { get; set; } = "";
        /// <summary>開いた日時</summary>
        public DateTime OpenedAt { get; set; }
        /// <summary>開いた日時の文字列表現（表示用）</summary>
        public string OpenedAtString => OpenedAt.ToString("yyyy/MM/dd HH:mm:ss");
        /// <summary>サムネイル画像（バインド用）</summary>
        public System.Windows.Media.ImageSource? ThumbnailImage 
        { 
            get => _thumbnailImage;
            set
            {
                if (_thumbnailImage != value)
                {
                    _thumbnailImage = value;
                    PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(ThumbnailImage)));
                }
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }

    public class FavoriteItem : System.ComponentModel.INotifyPropertyChanged
    {
        private System.Windows.Media.ImageSource? _thumbnailImage;

        public string FilePath { get; set; } = "";
        public string FileName { get; set; } = "";
        public System.Windows.Media.ImageSource? ThumbnailImage 
        { 
            get => _thumbnailImage;
            set
            {
                if (_thumbnailImage != value)
                {
                    _thumbnailImage = value;
                    PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(ThumbnailImage)));
                }
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }


    /// <summary>
    /// ウィンドウの状態（サイズ、位置、レイアウト設定）を JSON で保存・復元するためのクラス
    /// </summary>
    public class WindowStateData
    {
        /// <summary>ウィンドウの幅</summary>
        public double WindowWidth { get; set; }
        /// <summary>ウィンドウの高さ</summary>
        public double WindowHeight { get; set; }
        /// <summary>ウィンドウの左端位置</summary>
        public double WindowLeft { get; set; }
        /// <summary>ウィンドウの上端位置</summary>
        public double WindowTop { get; set; }
        /// <summary>ウィンドウが最大化されているか</summary>
        public bool IsMaximized { get; set; }
        /// <summary>全画面表示中か</summary>
        public bool IsFullScreen { get; set; }
        /// <summary>現在のレイアウトモード（0:通常 1:フロート 2:非表示）</summary>
        public int LayoutMode { get; set; }
        /// <summary>フォルダツリーが表示されているか</summary>
        public bool TreeVisible { get; set; }
        /// <summary>右パネルが表示されているか</summary>
        public bool RightPanelVisible { get; set; }
        /// <summary>パラメータグリッドが表示されているか</summary>
        public bool ParametersVisible { get; set; }
        /// <summary>ネガティブプロンプトグリッドが表示されているか</summary>
        public bool NegativePromptVisible { get; set; }
        /// <summary>Steps グリッドが表示されているか</summary>
        public bool StepsVisible { get; set; }
        /// <summary>フォルダツリー列の幅</summary>
        public double TreeColumnWidth { get; set; }
        /// <summary>右パネル列の幅</summary>
        public double RightPanelColumnWidth { get; set; }
        /// <summary>フォルダツリーの高さ</summary>
        public double TreeHeight { get; set; }
    }
}