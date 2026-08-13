using System.Globalization;
using GameDraw_App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.System;

namespace GameDraw_App;

public sealed partial class RecordingLibraryView : UserControl, IDisposable
{
    private readonly RecordingLibraryService _library;
    private readonly MediaPlayer _player = new();
    private IReadOnlyList<DrawingRecording> _recordings = Array.Empty<DrawingRecording>();
    private string? _playingId;
    private bool _disposed;

    public event EventHandler? BackRequested;

    public RecordingLibraryView()
    {
        InitializeComponent();
        _library = App.RecordingLibrary;
        PlayerElement.SetMediaPlayer(_player);
        Unloaded += (_, _) => Pause();
        ActualThemeChanged += async (_, _) => await RenderListAsync();
    }

    public async Task OpenAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await RefreshAsync();
    }

    public void Pause()
    {
        if (!_disposed)
        {
            _player.Pause();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _player.Pause();
        _player.Source = null;
        _player.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task RefreshAsync()
    {
        try
        {
            _recordings = await _library.GetRecordingsAsync();
            LibrarySummary.Text = _recordings.Count == 0
                ? "자동 그리기 과정과 완성 결과를 한곳에서 관리합니다."
                : $"{_recordings.Count:N0}개의 기록 · 최근 순서 · PC에만 저장";
            await RenderListAsync();
        }
        catch (Exception exception)
        {
            RecordingList.Children.Clear();
            RecordingList.Children.Add(CreateMessage($"기록을 불러오지 못했습니다.\n{exception.Message}"));
            EmptyLibraryState.Visibility = Visibility.Collapsed;
        }
    }

    private async Task RenderListAsync()
    {
        if (_disposed || RecordingList is null)
        {
            return;
        }

        var query = SearchBox?.Text?.Trim() ?? string.Empty;
        var visible = _recordings
            .Where(item => string.IsNullOrEmpty(query) ||
                item.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                item.Mode.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                item.SourceImageName.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .ToArray();
        RecordingCountBadge.Text = string.IsNullOrEmpty(query)
            ? $"{visible.Length:N0}개"
            : $"{visible.Length:N0}/{_recordings.Count:N0}개";
        RecordingList.Children.Clear();
        EmptyLibraryState.Visibility = visible.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var recording in visible)
        {
            RecordingList.Children.Add(await CreateRecordingCardAsync(recording));
        }
    }

    private async Task<UIElement> CreateRecordingCardAsync(DrawingRecording recording)
    {
        var card = new Border
        {
            Padding = new Thickness(10),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            BorderBrush = BrushResource("CardBorderBrush"),
            Background = string.Equals(_playingId, recording.Id, StringComparison.Ordinal)
                ? BrushResource("PrimarySoftBrush")
                : BrushResource("RecordRowBrush")
        };
        var layout = new Grid { ColumnSpacing = 11 };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
        layout.ColumnDefinitions.Add(new ColumnDefinition());

        var thumbnail = new Image { Width = 112, Height = 72, Stretch = Stretch.Uniform };
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(_library.GetThumbnailPath(recording));
            using var stream = await file.OpenReadAsync();
            var bitmap = new BitmapImage { DecodePixelWidth = 224 };
            await bitmap.SetSourceAsync(stream);
            thumbnail.Source = bitmap;
        }
        catch
        {
        }

        layout.Children.Add(new Border
        {
            Width = 112,
            Height = 72,
            CornerRadius = new CornerRadius(9),
            Background = BrushResource("PlayerSurfaceBrush"),
            Child = thumbnail
        });

        var details = new Grid { RowSpacing = 5 };
        details.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        details.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        details.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var name = new TextBlock
        {
            Text = recording.Name,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = BrushResource("StrongTextBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        details.Children.Add(name);
        var metadata = new TextBlock
        {
            Text = $"{recording.CreatedAt.LocalDateTime:MM.dd HH:mm} · {FormatDuration(recording.Duration)} · {recording.Width}×{recording.Height}",
            FontSize = 12,
            Foreground = BrushResource("MutedTextBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetRow(metadata, 1);
        details.Children.Add(metadata);
        var actions = new Grid { ColumnSpacing = 6 };
        actions.ColumnDefinitions.Add(new ColumnDefinition());
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var play = new Button
        {
            Content = recording.Completed ? "재생" : "중지 기록 재생",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(10, 5, 10, 5)
        };
        play.Click += async (_, _) => await PlayAsync(recording);
        actions.Children.Add(play);
        var rename = IconButton("\uE8AC", "이름 변경");
        rename.Click += async (_, _) => await RenameAsync(recording);
        Grid.SetColumn(rename, 1);
        actions.Children.Add(rename);
        var delete = IconButton("\uE74D", "삭제");
        delete.Click += async (_, _) => await DeleteAsync(recording);
        Grid.SetColumn(delete, 2);
        actions.Children.Add(delete);
        Grid.SetRow(actions, 2);
        details.Children.Add(actions);
        Grid.SetColumn(details, 1);
        layout.Children.Add(details);
        card.Child = layout;
        return card;
    }

    private async Task PlayAsync(DrawingRecording recording)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(_library.GetVideoPath(recording));
            _playingId = recording.Id;
            _player.Source = MediaSource.CreateFromStorageFile(file);
            ApplyPlaybackRate();
            NowPlayingTitle.Text = recording.Name;
            NowPlayingDetails.Text = $"{recording.Mode} · {FormatDuration(recording.Duration)} · {recording.Width}×{recording.Height}";
            SelectionStatus.Text = recording.Completed
                ? "100% 완료 시점의 캔버스가 썸네일로 저장된 기록입니다."
                : "중간에 중지된 기록입니다. 마지막 저장 프레임까지 재생합니다.";
            PlayerEmptyState.Visibility = Visibility.Collapsed;
            PlayerElement.Visibility = Visibility.Visible;
            _player.Play();
            await RenderListAsync();
        }
        catch (Exception exception)
        {
            await ShowErrorAsync("영상을 재생하지 못했습니다.", exception.Message);
        }
    }

    private async Task RenameAsync(DrawingRecording recording)
    {
        var input = new TextBox { Text = recording.Name, SelectionStart = recording.Name.Length, MaxLength = 80 };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "영상 이름 변경",
            Content = input,
            PrimaryButtonText = "저장",
            CloseButtonText = "취소",
            DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(input.Text))
        {
            return;
        }

        try
        {
            await _library.RenameAsync(recording.Id, input.Text);
            await RefreshAsync();
        }
        catch (Exception exception)
        {
            await ShowErrorAsync("이름을 변경하지 못했습니다.", exception.Message);
        }
    }

    private async Task DeleteAsync(DrawingRecording recording)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "영상 기록 삭제",
            Content = $"'{recording.Name}' 영상과 썸네일을 완전히 삭제할까요?",
            PrimaryButtonText = "삭제",
            CloseButtonText = "취소",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        if (string.Equals(_playingId, recording.Id, StringComparison.Ordinal))
        {
            _player.Pause();
            _player.Source = null;
            _playingId = null;
            PlayerElement.Visibility = Visibility.Collapsed;
            PlayerEmptyState.Visibility = Visibility.Visible;
            NowPlayingTitle.Text = "재생할 기록을 선택하세요";
            NowPlayingDetails.Text = "목록에서 재생을 누르면 영상이 여기에 표시됩니다.";
        }

        try
        {
            await Task.Delay(80);
            await _library.DeleteAsync(recording.Id);
            await RefreshAsync();
        }
        catch (Exception exception)
        {
            await ShowErrorAsync("영상 기록을 삭제하지 못했습니다.", exception.Message);
        }
    }

    private void PlaybackRateBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => ApplyPlaybackRate();

    private void ApplyPlaybackRate()
    {
        if (PlaybackRateBox?.SelectedItem is ComboBoxItem { Tag: string rateText } &&
            double.TryParse(rateText, NumberStyles.Float, CultureInfo.InvariantCulture, out var rate))
        {
            _player.PlaybackSession.PlaybackRate = rate;
        }
    }

    private async void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        => await RenderListAsync();

    private void Root_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var stacked = e.NewSize.Width < 920 || e.NewSize.Height < 610;
        LibraryBody.ColumnDefinitions[0].Width = stacked ? new GridLength(1, GridUnitType.Star) : new GridLength(1.55, GridUnitType.Star);
        LibraryBody.ColumnDefinitions[1].Width = stacked ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        LibraryBody.RowDefinitions[0].Height = stacked ? new GridLength(Math.Clamp(e.NewSize.Height * 0.48, 260, 390)) : new GridLength(1, GridUnitType.Star);
        LibraryBody.RowDefinitions[1].Height = stacked ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        Grid.SetColumn(ListCard, stacked ? 0 : 1);
        Grid.SetRow(ListCard, stacked ? 1 : 0);
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        Pause();
        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
        => await RefreshAsync();

    private async void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var folder = await StorageFolder.GetFolderFromPathAsync(_library.RootPath);
            _ = await Launcher.LaunchFolderAsync(folder);
        }
        catch (Exception exception)
        {
            await ShowErrorAsync("저장 폴더를 열지 못했습니다.", exception.Message);
        }
    }

    private async Task ShowErrorAsync(string title, string message)
    {
        await new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = message,
            CloseButtonText = "확인"
        }.ShowAsync();
    }

    private static Button IconButton(string glyph, string name)
    {
        var button = new Button
        {
            Content = glyph,
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            Padding = new Thickness(8, 5, 8, 5)
        };
        ToolTipService.SetToolTip(button, name);
        AutomationProperties.SetName(button, name);
        return button;
    }

    private static TextBlock CreateMessage(string text)
        => new()
        {
            Text = text,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(20, 36, 20, 20),
            Foreground = BrushResource("DangerTextBrush")
        };

    private static Brush BrushResource(string key)
        => (Brush)Application.Current.Resources[key];

    private static string FormatDuration(TimeSpan duration)
        => duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}"
            : $"{duration.Minutes:00}:{duration.Seconds:00}";
}
