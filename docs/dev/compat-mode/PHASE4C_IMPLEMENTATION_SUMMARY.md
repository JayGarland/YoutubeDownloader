# PHASE 4C IMPLEMENTATION SUMMARY: Full Video Compatibility Mode via yt-dlp

## Overview
Phase 4C is implemented with a compatibility-first strategy:

- When `UseCompatibilityModeYtDlp` is enabled, downloads use yt-dlp directly.
- YoutubeExplode is bypassed for the actual download path.
- Full video mode supports two runtime paths:
  - merged video+audio (`DownloadVideoAsync`) when FFmpeg is available
  - no-merge single-file fallback (`DownloadVideoNoMergeAsync`) when FFmpeg is unavailable

This resolves the previous dependency on reliably receiving a 403 before fallback.

---

## Objectives and Status

- ✅ Compatibility mode uses yt-dlp first (not 403-triggered fallback only)
- ✅ Audio-only and full-video modes both supported
- ✅ FFmpeg runtime detection added for video merge vs no-merge strategy
- ✅ Existing `catch (DownloadBlockedException)` path retained as safety net
- ✅ Temporary UI-visible debug marker added for manual verification in compat mode
- ✅ Console debug writes removed from active troubleshooting path
- ✅ Placeholder/empty pre-created file cleanup implemented for compatibility mode
- ✅ Core tests passing

---

## Key Behavior Change

## Before
1. YoutubeExplode download attempted first.
2. If blocked (403), throw `DownloadBlockedException`.
3. Then optionally fall back to yt-dlp.

## After
1. Read compatibility settings at the start of `EnqueueDownload`.
2. If compatibility mode is ON:
   - run yt-dlp directly
   - report progress through UI dispatcher path
   - complete/return early
3. If compatibility mode is OFF:
   - keep existing YoutubeExplode flow
4. If YoutubeExplode path throws `DownloadBlockedException`, existing fallback handler remains.

---

## Files Updated

## `YoutubeDownloader/ViewModels/Components/DashboardViewModel.cs`

- Added early compatibility branch in `EnqueueDownload(...)`.
- Reads settings once:
  - `useCompat`
  - `audioOnly`
  - `ytDlpPath`
- Compatibility execution path:
  - resolve URL from `download.Video?.Url` (fallback to canonical `watch?v=` URL)
  - compute output directory from existing target path
  - create yt-dlp progress reporter using `Dispatcher.UIThread.Post(...)`
  - set temporary debug text in `download.ErrorMessage`:
    - `DBG compat=... audioOnly=... yt=...`
  - choose mode:
    - audio: `DownloadAudioAsync(...)`
    - video + FFmpeg: `DownloadVideoAsync(..., mergeFormat: "mp4", ...)`
    - video without FFmpeg: `DownloadVideoNoMergeAsync(..., "best[ext=mp4]/best", ...)`
- Added `TryResolveFfmpeg()` helper (uses `FFmpeg.IsAvailable()`).
- Added cleanup helper for setup placeholder file:
  - `TryDeleteReservedPlaceholder(...)`
  - removes zero-byte reserved path when yt-dlp outputs a different real file.
- Updated single-video query flow:
  - when compatibility mode is ON, avoid `GetDownloadOptionsAsync(...)` probing and use preference-based setup flow to prevent pre-download 403.

## `YoutubeDownloader.Core/Downloading/YtDlpDownloader.cs`

- Added `DownloadVideoNoMergeAsync(...)` for FFmpeg-independent video downloads.
- Refactored video execution into shared internal method:
  - `DownloadVideoInternalAsync(...)`
- Made `--merge-output-format` conditional.
- Improved output path resolution:
  - capture `--print after_move:filepath` output
  - fallback parsing via merger/destination lines
  - fallback scan for known video extensions.

## `YoutubeDownloader.Core/Downloading/VideoDownloader.cs`

- Removed console debug prints.
- Retained/ensured 403 mapping to `DownloadBlockedException` in both stream manifest and actual download path.

## `YoutubeDownloader.Core.Tests/Downloading/YtDlpDownloaderTests.cs`

- Added no-merge command test:
  - `DownloadVideoNoMergeAsync_ShouldCallProcessRunner_WithNoMergeFormatSelector`
- Verifies:
  - no-merge format selector is used
  - `--merge-output-format` is not included.

---

## Validation

- ✅ `dotnet test` passing (`18/18`)
- ✅ Prior build/test runs successful after patching (when app process is not locking output executable)
- ✅ Manual test confirmed compatibility-mode download succeeds
- ✅ Empty duplicate file issue addressed by placeholder cleanup

---

## User-Visible Outcomes

1. Enabling compatibility mode now guarantees yt-dlp is used first for downloads.
2. Full video works even without FFmpeg (no-merge fallback).
3. Real-time progress continues through existing UI update path.
4. The previous duplicate-file symptom (second empty file) is fixed by cleaning the reserved placeholder when appropriate.

---

## Temporary Notes

- A temporary debug string is still written to `download.ErrorMessage` before yt-dlp launch in compatibility mode.
- TODO is in code to remove this after verification is complete.

---

## Conclusion

Phase 4C is implemented and operational with a more reliable architecture:

- Compatibility mode is now deterministic (yt-dlp-first),
- video mode gracefully handles FFmpeg presence/absence,
- and integration issues from the previous 403-dependent approach are resolved.
