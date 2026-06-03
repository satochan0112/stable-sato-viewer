using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace StableSatoSearcher;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".webp"
    };

    private readonly ObservableCollection<string> _managedFolders = [];
    private readonly ObservableCollection<ImageItem> _allImages = [];
    private readonly Random _random = new();

    private List<ImageItem> _slideShowItems = [];
    private int _slideShowIndex = -1;
    private bool _isSlideShowPlaying;
    private bool _isProgrammaticSelection;
    private bool _isRealtimeSearchEnabled;
    private bool _isFolderTreePaneVisible = true;
    private GridLength _folderTreeColumnWidth = new(280);
    private string _currentSearchKeyword = string.Empty;
    private string _sortBy = "フォルダ名";
    private bool _sortDescending;
    private FolderTreeNode? _selectedFolderNode;
    private ImageItem? _selectedImage;
    private SlideShowWindow? _slideShowWindow;

    public ObservableCollection<FolderTreeNode> FolderTreeRoots { get; } = [];

    public ObservableCollection<ImageItem> FilteredImages { get; } = [];

    public ObservableCollection<string> SearchHistories { get; } = [];

    public ImageItem? SelectedImage
    {
        get => _selectedImage;
        set
        {
            if (_selectedImage == value)
            {
                return;
            }

            _selectedImage = value;
            OnPropertyChanged();
        }
    }

    public bool IsRealtimeSearchEnabled
    {
        get => _isRealtimeSearchEnabled;
        set
        {
            if (_isRealtimeSearchEnabled == value)
            {
                return;
            }

            _isRealtimeSearchEnabled = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        SearchHistoryComboBox.ItemsSource = SearchHistories;
        UpdateFolderTreePaneVisibility();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        LoadSettings();
        RefreshFolderTree();
        RefreshImages();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        SaveSettings();
    }

    private void AddFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "画像フォルダを選択してください",
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var selectedPath = dialog.FolderName;
        if (_managedFolders.Any(f => string.Equals(f, selectedPath, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        _managedFolders.Add(selectedPath);
        RefreshFolderTree();
        RefreshImages();
    }

    private void RemoveFolderButton_Click(object sender, RoutedEventArgs e)
    {
        string? targetRoot = null;
        if (_selectedFolderNode is not null)
        {
            var current = _selectedFolderNode;
            while (current.Parent is not null)
            {
                current = current.Parent;
            }

            targetRoot = current.FullPath;
        }

        if (targetRoot is null && _managedFolders.Count > 0)
        {
            targetRoot = _managedFolders[0];
        }

        if (targetRoot is null)
        {
            return;
        }

        _managedFolders.Remove(targetRoot);
        _selectedFolderNode = null;
        RefreshFolderTree();
        RefreshImages();
    }

    private void ToggleSlideShowButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isSlideShowPlaying)
        {
            return;
        }

        StartSlideShow();
    }

    private void ToggleFolderTreePaneButton_Click(object sender, RoutedEventArgs e)
    {
        _isFolderTreePaneVisible = !_isFolderTreePaneVisible;
        UpdateFolderTreePaneVisibility();
    }


    private void RandomOrderCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isSlideShowPlaying)
        {
            BuildSlideShowItems();
        }
    }

    private void SearchHistoryComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SearchHistoryComboBox.SelectedItem is not string selected || string.IsNullOrWhiteSpace(selected))
        {
            return;
        }

        SearchTextBox.Text = selected;
        if (IsRealtimeSearchEnabled)
        {
            ExecuteSearch(addHistory: false);
        }
    }

    private void FolderTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        _selectedFolderNode = e.NewValue as FolderTreeNode;
        ApplyFilter();
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsRealtimeSearchEnabled)
        {
            return;
        }

        ExecuteSearch(addHistory: false);
    }

    private void SearchExecuteButton_Click(object sender, RoutedEventArgs e)
    {
        ExecuteSearch(addHistory: true);
    }

    private void RealtimeSearchCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        IsRealtimeSearchEnabled = RealtimeSearchCheckBox.IsChecked == true;
        if (IsRealtimeSearchEnabled)
        {
            ExecuteSearch(addHistory: false);
        }
    }

    private void ThumbnailListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThumbnailListBox.SelectedItem is not ImageItem selected)
        {
            return;
        }

        SelectedImage = selected;
    }

    private void ThumbnailListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ThumbnailListBox.SelectedItem is not ImageItem selected)
        {
            return;
        }

        OpenInExplorer(selected.FullPath);
    }

    private void PreviewGroupBox_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        PreviewGroupBox.Focus();
    }

    private void PropertyGroupBox_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        PropertyGroupBox.Focus();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Left && e.Key != Key.Right)
        {
            return;
        }

        var direction = e.Key == Key.Right ? 1 : -1;

        if (_isSlideShowPlaying)
        {
            MoveSlide(direction);
            e.Handled = true;
            return;
        }

        if (!IsKeyboardFocusWithin(PreviewGroupBox) && !IsKeyboardFocusWithin(PropertyGroupBox))
        {
            return;
        }

        MoveThumbnailSelection(direction);
        e.Handled = true;
    }

    private void SortOption_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        if (SortByComboBox.SelectedItem is ComboBoxItem sortByItem)
        {
            _sortBy = sortByItem.Content?.ToString() ?? "ファイル名";
        }

        if (SortDirectionComboBox.SelectedItem is ComboBoxItem directionItem)
        {
            _sortDescending = string.Equals(directionItem.Content?.ToString(), "降順", StringComparison.Ordinal);
        }

        ApplyFilter();
    }

    private void MoveThumbnailSelection(int direction)
    {
        if (FilteredImages.Count == 0)
        {
            return;
        }

        var currentIndex = SelectedImage is null ? -1 : FilteredImages.IndexOf(SelectedImage);
        if (currentIndex < 0)
        {
            currentIndex = direction > 0 ? 0 : FilteredImages.Count - 1;
        }
        else
        {
            currentIndex = (currentIndex + direction + FilteredImages.Count) % FilteredImages.Count;
        }

        SetSelectedImage(FilteredImages[currentIndex]);
    }

    private static bool IsKeyboardFocusWithin(DependencyObject container)
    {
        var focused = Keyboard.FocusedElement as DependencyObject;
        while (focused is not null)
        {
            if (ReferenceEquals(focused, container))
            {
                return true;
            }

            focused = VisualTreeHelper.GetParent(focused);
        }

        return false;
    }

    private void RefreshFolderTree()
    {
        FolderTreeRoots.Clear();

        foreach (var folder in _managedFolders.Where(Directory.Exists))
        {
            var root = BuildFolderNode(folder, null);
            FolderTreeRoots.Add(root);
        }
    }

    private FolderTreeNode BuildFolderNode(string folderPath, FolderTreeNode? parent)
    {
        var node = new FolderTreeNode
        {
            Name = Path.GetFileName(folderPath),
            FullPath = folderPath,
            Parent = parent
        };

        if (string.IsNullOrWhiteSpace(node.Name))
        {
            node.Name = folderPath;
        }

        foreach (var childDirectory in EnumerateDirectoriesSafe(folderPath).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            node.Children.Add(BuildFolderNode(childDirectory, node));
        }

        return node;
    }

    private void RefreshImages()
    {
        _allImages.Clear();

        foreach (var root in _managedFolders.Where(Directory.Exists))
        {
            foreach (var file in EnumerateFilesSafe(root))
            {
                if (!SupportedExtensions.Contains(Path.GetExtension(file)))
                {
                    continue;
                }

                var imageItem = BuildImageItem(file, root);
                if (imageItem is not null)
                {
                    _allImages.Add(imageItem);
                }
            }
        }

        ApplyFilter();
    }

    private ImageItem? BuildImageItem(string filePath, string rootFolder)
    {
        try
        {
            var info = new FileInfo(filePath);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 200;
            image.UriSource = new Uri(filePath, UriKind.Absolute);
            image.EndInit();
            image.Freeze();

            var preview = new BitmapImage();
            preview.BeginInit();
            preview.CacheOption = BitmapCacheOption.OnLoad;
            preview.UriSource = new Uri(filePath, UriKind.Absolute);
            preview.EndInit();
            preview.Freeze();

            return new ImageItem
            {
                FileName = info.Name,
                FullPath = info.FullName,
                DirectoryPath = info.DirectoryName ?? string.Empty,
                FolderName = info.Directory?.Name ?? string.Empty,
                RootFolder = rootFolder,
                Extension = info.Extension,
                FileSizeText = $"{info.Length / 1024d:N1} KB",
                LastWriteTimeText = info.LastWriteTime.ToString("yyyy/MM/dd HH:mm:ss"),
                DimensionText = $"{preview.PixelWidth} x {preview.PixelHeight}",
                Thumbnail = image,
                Preview = preview
            };
        }
        catch
        {
            return null;
        }
    }

    private void ExecuteSearch(bool addHistory)
    {
        _currentSearchKeyword = SearchTextBox?.Text?.Trim() ?? string.Empty;
        ApplyFilter(addHistory);
    }

    private void ApplyFilter(bool addHistory = false)
    {
        var keyword = _currentSearchKeyword;
        IEnumerable<ImageItem> query = _allImages;

        if (_selectedFolderNode is not null)
        {
            query = query.Where(i => i.DirectoryPath.StartsWith(_selectedFolderNode.FullPath, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query.Where(i =>
                i.FileName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                i.FullPath.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        var ordered = _sortBy == "フォルダ名"
            ? query.OrderBy(i => i.FolderName, StringComparer.OrdinalIgnoreCase).ThenBy(i => i.FileName, StringComparer.OrdinalIgnoreCase)
            : query.OrderBy(i => i.FileName, StringComparer.OrdinalIgnoreCase).ThenBy(i => i.FolderName, StringComparer.OrdinalIgnoreCase);

        if (_sortDescending)
        {
            ordered = _sortBy == "フォルダ名"
                ? query.OrderByDescending(i => i.FolderName, StringComparer.OrdinalIgnoreCase).ThenByDescending(i => i.FileName, StringComparer.OrdinalIgnoreCase)
                : query.OrderByDescending(i => i.FileName, StringComparer.OrdinalIgnoreCase).ThenByDescending(i => i.FolderName, StringComparer.OrdinalIgnoreCase);
        }

        var result = ordered.ToList();

        FilteredImages.Clear();
        foreach (var item in result)
        {
            FilteredImages.Add(item);
        }

        if (SelectedImage is null || !FilteredImages.Contains(SelectedImage))
        {
            SelectedImage = FilteredImages.FirstOrDefault();
        }

        _isProgrammaticSelection = true;
        ThumbnailListBox.SelectedItem = SelectedImage;
        _isProgrammaticSelection = false;

        if (addHistory)
        {
            AddSearchHistory(keyword);
        }

        if (_isSlideShowPlaying)
        {
            BuildSlideShowItems();
        }
    }

    private void AddSearchHistory(string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return;
        }

        var existing = SearchHistories.FirstOrDefault(h => string.Equals(h, keyword, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            SearchHistories.Remove(existing);
        }

        SearchHistories.Insert(0, keyword);
        while (SearchHistories.Count > 50)
        {
            SearchHistories.RemoveAt(SearchHistories.Count - 1);
        }
    }

    private void StartSlideShow()
    {
        if (FilteredImages.Count == 0)
        {
            return;
        }

        _isSlideShowPlaying = true;
        SlideShowToggleButton.IsEnabled = false;
        BuildSlideShowItems();

        if (_slideShowItems.Count == 0)
        {
            return;
        }

        if (SelectedImage is not null)
        {
            var index = _slideShowItems.FindIndex(i => i.FullPath == SelectedImage.FullPath);
            _slideShowIndex = index >= 0 ? index : 0;
        }
        else
        {
            _slideShowIndex = 0;
        }

        EnsureSlideShowWindowShown();
        SetSelectedImage(_slideShowItems[_slideShowIndex]);
    }

    private void StopSlideShow()
    {
        _isSlideShowPlaying = false;
        SlideShowToggleButton.IsEnabled = true;
        _slideShowItems = [];
        _slideShowIndex = -1;

        if (_slideShowWindow is not null)
        {
            _slideShowWindow.KeyDown -= SlideShowWindow_KeyDown;
            _slideShowWindow.Closed -= SlideShowWindow_Closed;
            var windowToClose = _slideShowWindow;
            _slideShowWindow = null;
            if (windowToClose.IsVisible)
            {
                windowToClose.Close();
            }
        }
    }

    private void BuildSlideShowItems()
    {
        _slideShowItems = FilteredImages.ToList();

        if (RandomOrderCheckBox.IsChecked == true)
        {
            _slideShowItems = _slideShowItems.OrderBy(_ => _random.Next()).ToList();
        }

        if (_slideShowItems.Count == 0)
        {
            StopSlideShow();
            return;
        }

        if (_slideShowIndex >= _slideShowItems.Count)
        {
            _slideShowIndex = 0;
        }
    }

    private void MoveSlide(int direction)
    {
        if (_slideShowItems.Count == 0)
        {
            BuildSlideShowItems();
            if (_slideShowItems.Count == 0)
            {
                return;
            }
        }

        if (_slideShowIndex < 0)
        {
            _slideShowIndex = 0;
        }
        else
        {
            _slideShowIndex = (_slideShowIndex + direction + _slideShowItems.Count) % _slideShowItems.Count;
        }

        SetSelectedImage(_slideShowItems[_slideShowIndex]);
    }

    private void SetSelectedImage(ImageItem image)
    {
        SelectedImage = image;
        _isProgrammaticSelection = true;
        ThumbnailListBox.SelectedItem = image;
        ThumbnailListBox.ScrollIntoView(image);
        _isProgrammaticSelection = false;

        UpdateSlideShowWindowImage();
    }

    private void EnsureSlideShowWindowShown()
    {
        if (_slideShowWindow is not null)
        {
            return;
        }

        _slideShowWindow = new SlideShowWindow
        {
            Owner = this
        };
        _slideShowWindow.KeyDown += SlideShowWindow_KeyDown;
        _slideShowWindow.Closed += SlideShowWindow_Closed;
        _slideShowWindow.Show();
    }

    private void UpdateSlideShowWindowImage()
    {
        if (!_isSlideShowPlaying || _slideShowWindow is null || SelectedImage is null)
        {
            return;
        }

        _slideShowWindow.SetImage(SelectedImage.Preview);
    }

    private void SlideShowWindow_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Left)
        {
            MoveSlide(-1);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Right)
        {
            MoveSlide(1);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            StopSlideShow();
            e.Handled = true;
        }
    }

    private void SlideShowWindow_Closed(object? sender, EventArgs e)
    {
        if (_slideShowWindow is not null)
        {
            _slideShowWindow.KeyDown -= SlideShowWindow_KeyDown;
            _slideShowWindow.Closed -= SlideShowWindow_Closed;
            _slideShowWindow = null;
        }

        if (_isSlideShowPlaying)
        {
            StopSlideShow();
        }
    }

    private static void OpenInExplorer(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{filePath}\"",
            UseShellExecute = true
        };
        Process.Start(startInfo);
    }

    private void LoadSettings()
    {
        try
        {
            var path = GetSettingsPath();
            if (!File.Exists(path))
            {
                return;
            }

            var json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<AppSettings>(json);
            if (settings is null)
            {
                return;
            }

            _managedFolders.Clear();
            foreach (var folder in settings.ManagedFolders.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                _managedFolders.Add(folder);
            }

            SearchHistories.Clear();
            foreach (var history in settings.SearchHistories.Where(h => !string.IsNullOrWhiteSpace(h)))
            {
                SearchHistories.Add(history);
            }

            RandomOrderCheckBox.IsChecked = settings.IsRandomSlideShow;
            IsRealtimeSearchEnabled = settings.IsRealtimeSearchEnabled;
            RealtimeSearchCheckBox.IsChecked = IsRealtimeSearchEnabled;
        }
        catch
        {
        }
    }

    private void SaveSettings()
    {
        try
        {
            var settings = new AppSettings
            {
                ManagedFolders = _managedFolders.ToList(),
                SearchHistories = SearchHistories.ToList(),
                IsRandomSlideShow = RandomOrderCheckBox.IsChecked == true,
                IsRealtimeSearchEnabled = IsRealtimeSearchEnabled
            };

            var path = GetSettingsPath();
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch
        {
        }
    }

    private static string GetSettingsPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "StableSatoSearcher", "settings.json");
    }

    private static IEnumerable<string> EnumerateFilesSafe(string rootPath)
    {
        var directories = new Stack<string>();
        directories.Push(rootPath);

        while (directories.Count > 0)
        {
            var current = directories.Pop();
            IEnumerable<string> files = [];
            IEnumerable<string> subDirs = [];

            try
            {
                files = Directory.EnumerateFiles(current);
            }
            catch
            {
            }

            foreach (var file in files)
            {
                yield return file;
            }

            try
            {
                subDirs = Directory.EnumerateDirectories(current);
            }
            catch
            {
            }

            foreach (var subDir in subDirs)
            {
                directories.Push(subDir);
            }
        }
    }

    private static IEnumerable<string> EnumerateDirectoriesSafe(string rootPath)
    {
        try
        {
            return Directory.EnumerateDirectories(rootPath).ToList();
        }
        catch
        {
            return [];
        }
    }

    private void UpdateFolderTreePaneVisibility()
    {
        if (_isFolderTreePaneVisible)
        {
            if (_folderTreeColumnWidth.Value <= 0)
            {
                _folderTreeColumnWidth = new GridLength(280);
            }

            FolderTreeColumn.Width = _folderTreeColumnWidth;
            FolderTreeColumn.MinWidth = 180;
            FolderTreeSplitterColumn.Width = new GridLength(5);
            FolderTreeGroupBox.Visibility = Visibility.Visible;
            FolderTreeGridSplitter.Visibility = Visibility.Visible;
            ToggleFolderTreePaneButton.Content = "フォルダツリー非表示";
            return;
        }

        if (FolderTreeColumn.Width.Value > 0)
        {
            _folderTreeColumnWidth = FolderTreeColumn.Width;
        }

        FolderTreeGroupBox.Visibility = Visibility.Collapsed;
        FolderTreeGridSplitter.Visibility = Visibility.Collapsed;
        FolderTreeColumn.MinWidth = 0;
        FolderTreeColumn.Width = new GridLength(0);
        FolderTreeSplitterColumn.Width = new GridLength(0);
        ToggleFolderTreePaneButton.Content = "フォルダツリー表示";
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class SlideShowWindow : Window
{
    private readonly Image _image;

    public SlideShowWindow()
    {
        WindowStyle = WindowStyle.None;
        WindowState = WindowState.Maximized;
        ResizeMode = ResizeMode.NoResize;
        Background = Brushes.Black;
        Topmost = true;
        Focusable = true;

        _image = new Image
        {
            Stretch = Stretch.Uniform
        };

        Content = _image;
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        Activate();
        Focus();
        Keyboard.Focus(this);
    }

    public void SetImage(BitmapSource? image)
    {
        _image.Source = image;
    }
}

public sealed class FolderTreeNode
{
    public string Name { get; set; } = string.Empty;

    public string FullPath { get; set; } = string.Empty;

    public FolderTreeNode? Parent { get; set; }

    public ObservableCollection<FolderTreeNode> Children { get; } = [];
}

public sealed class ImageItem
{
    public string FileName { get; set; } = string.Empty;

    public string FullPath { get; set; } = string.Empty;

    public string DirectoryPath { get; set; } = string.Empty;

    public string FolderName { get; set; } = string.Empty;

    public string RootFolder { get; set; } = string.Empty;

    public string FileSizeText { get; set; } = string.Empty;

    public string LastWriteTimeText { get; set; } = string.Empty;

    public string DimensionText { get; set; } = string.Empty;

    public string Extension { get; set; } = string.Empty;

    public BitmapImage? Thumbnail { get; set; }

    public BitmapImage? Preview { get; set; }
}

public sealed class AppSettings
{
    public List<string> ManagedFolders { get; set; } = [];

    public List<string> SearchHistories { get; set; } = [];

    public bool IsRandomSlideShow { get; set; }

    public bool IsRealtimeSearchEnabled { get; set; }
}
