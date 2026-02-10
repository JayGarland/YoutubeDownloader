using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gress;
using Gress.Completable;
using YoutubeDownloader.Core.Downloading;
using YoutubeDownloader.Core.Resolving;
using YoutubeDownloader.Core.Tagging;
using YoutubeDownloader.Framework;
using YoutubeDownloader.Services;
using YoutubeDownloader.Utils;
using YoutubeDownloader.Utils.Extensions;
using YoutubeExplode.Exceptions;

namespace YoutubeDownloader.ViewModels.Components;

public partial class DashboardViewModel : ViewModelBase
{
    private readonly ViewModelManager _viewModelManager;
    private readonly SnackbarManager _snackbarManager;
    private readonly DialogManager _dialogManager;
    private readonly SettingsService _settingsService;

    private readonly DisposableCollector _eventRoot = new();
    private readonly ResizableSemaphore _downloadSemaphore = new();
    private readonly AutoResetProgressMuxer _progressMuxer;

    public DashboardViewModel(
        ViewModelManager viewModelManager,
        SnackbarManager snackbarManager,
        DialogManager dialogManager,
        SettingsService settingsService
    )
    {
        _viewModelManager = viewModelManager;
        _snackbarManager = snackbarManager;
        _dialogManager = dialogManager;
        _settingsService = settingsService;

        _progressMuxer = Progress.CreateMuxer().WithAutoReset();

        _eventRoot.Add(
            _settingsService.WatchProperty(
                o => o.ParallelLimit,
                () => _downloadSemaphore.MaxCount = _settingsService.ParallelLimit,
                true
            )
        );

        _eventRoot.Add(
            Progress.WatchProperty(
                o => o.Current,
                () => OnPropertyChanged(nameof(IsProgressIndeterminate))
            )
        );
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProgressIndeterminate))]
    [NotifyCanExecuteChangedFor(nameof(ProcessQueryCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowAuthSetupCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowSettingsCommand))]
    public partial bool IsBusy { get; set; }

    public ProgressContainer<Percentage> Progress { get; } = new();

    public bool IsProgressIndeterminate => IsBusy && Progress.Current.Fraction is <= 0 or >= 1;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ProcessQueryCommand))]
    public partial string? Query { get; set; }

    public ObservableCollection<DownloadViewModel> Downloads { get; } = [];

    private bool CanShowAuthSetup() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanShowAuthSetup))]
    private async Task ShowAuthSetupAsync() =>
        await _dialogManager.ShowDialogAsync(_viewModelManager.CreateAuthSetupViewModel());

    private bool CanShowSettings() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanShowSettings))]
    private async Task ShowSettingsAsync() =>
        await _dialogManager.ShowDialogAsync(_viewModelManager.CreateSettingsViewModel());

    private async void EnqueueDownload(DownloadViewModel download, int position = 0)
    {
        Downloads.Insert(position, download);
        var progress = _progressMuxer.CreateInput();
        var useCompat = _settingsService.UseCompatibilityModeYtDlp;
        var ytDlpPath = _settingsService.YtDlpPath;

        try
        {
            using var access = await _downloadSemaphore.AcquireAsync(download.CancellationToken);

            download.Status = DownloadStatus.Started;

            if (useCompat)
            {
                var reservedFilePath = download.FilePath;
                var videoUrl = download.Video?.Url?.ToString();
                if (string.IsNullOrWhiteSpace(videoUrl))
                    videoUrl = $"https://www.youtube.com/watch?v={download.Video!.Id}";

                var outputDir = Path.GetDirectoryName(download.FilePath!) ?? ".";
                var mergedProgress = download.Progress.Merge(progress);
                var ytDlpProgress = new Progress<double>(fraction =>
                {
                    Dispatcher.UIThread.Post(() =>
                        mergedProgress.Report(Percentage.FromFraction(fraction))
                    );
                });
                var compatPlan = CompatibilityModeDownloadPlan.FromSelection(
                    download.DownloadOption,
                    download.DownloadPreference,
                    reservedFilePath
                );

                try
                {
                    var yt = new YtDlpDownloader(ytDlpPath);
                    var fallbackPath =
                        compatPlan.IsAudio
                            ? await yt.DownloadAudioAsync(
                                videoUrl,
                                outputDir,
                                download.CancellationToken,
                                ytDlpProgress,
                                compatPlan.AudioFormat
                            )
                        : TryResolveFfmpeg()
                            ? await yt.DownloadVideoAsync(
                                videoUrl,
                                outputDir,
                                mergeFormat: "mp4",
                                download.CancellationToken,
                                ytDlpProgress
                            )
                        : await yt.DownloadVideoNoMergeAsync(
                            videoUrl,
                            outputDir,
                            formatSelector: @"best[ext=mp4]/best",
                            download.CancellationToken,
                            ytDlpProgress
                        );

                    TryDeleteReservedPlaceholder(reservedFilePath, fallbackPath);
                    download.FilePath = fallbackPath;
                    download.ErrorMessage = null;
                    download.Status = DownloadStatus.Completed;
                    return;
                }
                catch (Exception ex)
                {
                    // Best-effort cleanup of setup placeholder on failure/cancel in compat mode.
                    TryDeleteReservedPlaceholder(reservedFilePath);
                    download.Status =
                        ex is OperationCanceledException
                            ? DownloadStatus.Canceled
                            : DownloadStatus.Failed;
                    download.ErrorMessage = $"yt-dlp compatibility mode failed: {ex.Message}";
                    return;
                }
            }

            using var downloader = new VideoDownloader(_settingsService.LastAuthCookies);
            var tagInjector = new MediaTagInjector();

            var downloadOption =
                download.DownloadOption
                ?? await downloader.GetBestDownloadOptionAsync(
                    download.Video!.Id,
                    download.DownloadPreference!,
                    _settingsService.ShouldInjectLanguageSpecificAudioStreams,
                    download.CancellationToken
                );

            await downloader.DownloadVideoAsync(
                download.FilePath!,
                download.Video!,
                downloadOption,
                _settingsService.ShouldInjectSubtitles,
                download.Progress.Merge(progress),
                download.CancellationToken
            );

            if (_settingsService.ShouldInjectTags)
            {
                try
                {
                    await tagInjector.InjectTagsAsync(
                        download.FilePath!,
                        download.Video!,
                        download.CancellationToken
                    );
                }
                catch
                {
                    // Media tagging is not critical
                }
            }

            download.Status = DownloadStatus.Completed;
        }
        catch (DownloadBlockedException ex)
        {
            // Try yt-dlp fallback if enabled
            if (useCompat)
            {
                try
                {
                    // Determine fallback mode: audio-only or full video
                    var videoUrl = $"https://www.youtube.com/watch?v={download.Video!.Id}";
                    var outputDir = Path.GetDirectoryName(download.FilePath!) ?? ".";
                    var compatPlan = CompatibilityModeDownloadPlan.FromSelection(
                        download.DownloadOption,
                        download.DownloadPreference,
                        download.FilePath
                    );

                    // Create progress reporter that updates UI on dispatcher thread
                    var ytDlpProgress = new Progress<double>(fraction =>
                    {
                        Dispatcher.UIThread.Post(() =>
                            download.Progress.Report(Percentage.FromFraction(fraction))
                        );
                    });

                    var ytDlpDownloader = new YtDlpDownloader(ytDlpPath);
                    string fallbackFilePath;

                    if (compatPlan.IsAudio)
                    {
                        // Audio-only fallback
                        fallbackFilePath = await ytDlpDownloader.DownloadAudioAsync(
                            videoUrl,
                            outputDir,
                            download.CancellationToken,
                            progress: ytDlpProgress,
                            audioFormat: compatPlan.AudioFormat
                        );
                    }
                    else
                    {
                        fallbackFilePath = TryResolveFfmpeg()
                            ? await ytDlpDownloader.DownloadVideoAsync(
                                videoUrl,
                                outputDir,
                                mergeFormat: "mp4",
                                download.CancellationToken,
                                progress: ytDlpProgress
                            )
                            : await ytDlpDownloader.DownloadVideoNoMergeAsync(
                                videoUrl,
                                outputDir,
                                formatSelector: @"best[ext=mp4]/best",
                                download.CancellationToken,
                                progress: ytDlpProgress
                            );
                    }

                    // Update file path to the downloaded file and mark as completed
                    download.FilePath = fallbackFilePath;
                    download.Status = DownloadStatus.Completed;
                    return; // Success - exit early
                }
                catch (Exception ytDlpEx)
                {
                    var compatPlan = CompatibilityModeDownloadPlan.FromSelection(
                        download.DownloadOption,
                        download.DownloadPreference,
                        download.FilePath
                    );

                    // yt-dlp fallback failed - clean up and show combined error
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(download.FilePath))
                            File.Delete(download.FilePath);
                    }
                    catch
                    {
                        // Ignore
                    }

                    var fallbackType = compatPlan.IsAudio ? "audio" : "video";
                    download.Status = DownloadStatus.Failed;
                    download.ErrorMessage =
                        $"{ex.Message}\n\nyt-dlp {fallbackType} fallback also failed: {ytDlpEx.Message}";
                    return;
                }
            }

            // Compatibility mode not enabled - show original error
            try
            {
                // Delete the incompletely downloaded file
                if (!string.IsNullOrWhiteSpace(download.FilePath))
                    File.Delete(download.FilePath);
            }
            catch
            {
                // Ignore
            }

            download.Status = DownloadStatus.Failed;
            download.ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            try
            {
                // Delete the incompletely downloaded file
                if (!string.IsNullOrWhiteSpace(download.FilePath))
                    File.Delete(download.FilePath);
            }
            catch
            {
                // Ignore
            }

            download.Status =
                ex is OperationCanceledException ? DownloadStatus.Canceled : DownloadStatus.Failed;

            // Short error message for YouTube-related errors, full for others
            download.ErrorMessage = ex is YoutubeExplodeException ? ex.Message : ex.ToString();
        }
        finally
        {
            progress.ReportCompletion();
            download.Dispose();
        }
    }

    private static bool TryResolveFfmpeg() => FFmpeg.IsAvailable();

    private static void TryDeleteReservedPlaceholder(
        string? reservedFilePath,
        string? actualOutputPath = null
    )
    {
        if (string.IsNullOrWhiteSpace(reservedFilePath) || !File.Exists(reservedFilePath))
            return;

        if (
            !string.IsNullOrWhiteSpace(actualOutputPath)
            && string.Equals(reservedFilePath, actualOutputPath, StringComparison.OrdinalIgnoreCase)
        )
        {
            return;
        }

        try
        {
            var fileInfo = new FileInfo(reservedFilePath);
            if (fileInfo.Length == 0)
                File.Delete(reservedFilePath);
        }
        catch
        {
            // Ignore cleanup failures.
        }
    }

    private bool CanProcessQuery() => !IsBusy && !string.IsNullOrWhiteSpace(Query);

    [RelayCommand(CanExecute = nameof(CanProcessQuery))]
    private async Task ProcessQueryAsync()
    {
        if (string.IsNullOrWhiteSpace(Query))
            return;

        IsBusy = true;

        // Small weight so as to not offset any existing download operations
        var progress = _progressMuxer.CreateInput(0.01);

        try
        {
            using var resolver = new QueryResolver(_settingsService.LastAuthCookies);

            // Split queries by newlines
            var queries = Query.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            );

            // Process individual queries
            var queryResults = new List<QueryResult>();
            foreach (var (i, query) in queries.Index())
            {
                try
                {
                    queryResults.Add(await resolver.ResolveAsync(query));
                }
                // If it's not the only query in the list, don't interrupt the process
                // and report the error via an async notification instead of a sync dialog.
                // https://github.com/Tyrrrz/YoutubeDownloader/issues/563
                catch (YoutubeExplodeException ex)
                    when (ex is VideoUnavailableException or PlaylistUnavailableException
                        && queries.Length > 1
                    )
                {
                    _snackbarManager.Notify(ex.Message);
                }

                progress.Report(Percentage.FromFraction((i + 1.0) / queries.Length));
            }

            // Aggregate results
            var queryResult = QueryResult.Aggregate(queryResults);

            // Single video result
            if (queryResult.Videos.Count == 1)
            {
                var video = queryResult.Videos.Single();

                if (_settingsService.UseCompatibilityModeYtDlp)
                {
                    // In compatibility mode, avoid probing stream manifests via YoutubeExplode.
                    var downloads = await _dialogManager.ShowDialogAsync(
                        _viewModelManager.CreateDownloadMultipleSetupViewModel(
                            video.Title,
                            [video],
                            preselectVideos: true
                        )
                    );

                    if (downloads is null)
                        return;

                    foreach (var download in downloads)
                        EnqueueDownload(download);
                }
                else
                {
                    using var downloader = new VideoDownloader(_settingsService.LastAuthCookies);

                    var downloadOptions = await downloader.GetDownloadOptionsAsync(
                        video.Id,
                        _settingsService.ShouldInjectLanguageSpecificAudioStreams
                    );

                    var download = await _dialogManager.ShowDialogAsync(
                        _viewModelManager.CreateDownloadSingleSetupViewModel(video, downloadOptions)
                    );

                    if (download is null)
                        return;

                    EnqueueDownload(download);
                }

                Query = "";
            }
            // Multiple videos
            else if (queryResult.Videos.Count > 1)
            {
                var downloads = await _dialogManager.ShowDialogAsync(
                    _viewModelManager.CreateDownloadMultipleSetupViewModel(
                        queryResult.Title,
                        queryResult.Videos,
                        // Pre-select videos if they come from a single query and not from search
                        queryResult.Kind
                            is not QueryResultKind.Search
                                and not QueryResultKind.Aggregate
                    )
                );

                if (downloads is null)
                    return;

                foreach (var download in downloads)
                    EnqueueDownload(download);

                Query = "";
            }
            // No videos found
            else
            {
                await _dialogManager.ShowDialogAsync(
                    _viewModelManager.CreateMessageBoxViewModel(
                        "Nothing found",
                        "Couldn't find any videos based on the query or URL you provided"
                    )
                );
            }
        }
        catch (Exception ex)
        {
            await _dialogManager.ShowDialogAsync(
                _viewModelManager.CreateMessageBoxViewModel(
                    "Error",
                    // Short error message for YouTube-related errors, full for others
                    ex is YoutubeExplodeException or DownloadBlockedException
                        ? ex.Message
                        : ex.ToString()
                )
            );
        }
        finally
        {
            progress.ReportCompletion();
            IsBusy = false;
        }
    }

    private void RemoveDownload(DownloadViewModel download)
    {
        Downloads.Remove(download);
        download.CancelCommand.Execute(null);
        download.Dispose();
    }

    [RelayCommand]
    private void RemoveSuccessfulDownloads()
    {
        foreach (var download in Downloads.ToArray())
        {
            if (download.Status == DownloadStatus.Completed)
                RemoveDownload(download);
        }
    }

    [RelayCommand]
    private void RemoveInactiveDownloads()
    {
        foreach (var download in Downloads.ToArray())
        {
            if (
                download.Status
                is DownloadStatus.Completed
                    or DownloadStatus.Failed
                    or DownloadStatus.Canceled
            )
                RemoveDownload(download);
        }
    }

    [RelayCommand]
    private void RestartDownload(DownloadViewModel download)
    {
        var position = Math.Max(0, Downloads.IndexOf(download));
        RemoveDownload(download);

        var newDownload = download.DownloadOption is not null
            ? _viewModelManager.CreateDownloadViewModel(
                download.Video!,
                download.DownloadOption,
                download.FilePath!
            )
            : _viewModelManager.CreateDownloadViewModel(
                download.Video!,
                download.DownloadPreference!,
                download.FilePath!
            );

        EnqueueDownload(newDownload, position);
    }

    [RelayCommand]
    private void RestartFailedDownloads()
    {
        foreach (var download in Downloads.ToArray())
        {
            if (download.Status == DownloadStatus.Failed)
                RestartDownload(download);
        }
    }

    [RelayCommand]
    private void CancelAllDownloads()
    {
        foreach (var download in Downloads)
            download.CancelCommand.Execute(null);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CancelAllDownloads();

            _eventRoot.Dispose();
            _downloadSemaphore.Dispose();
        }

        base.Dispose(disposing);
    }
}
