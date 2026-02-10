## PHASE 5 — How to package this PR so it gets approved

### 1) PR title (choose one)

* **“Add yt-dlp compatibility mode (audio/video) + progress + tests for 403 blocks”**
* **“Graceful handling of YouTube 403: domain exception + yt-dlp fallback + progress UI”**

### 2) PR description (copy/paste template)

**Problem**

* Downloads fail with **403 Forbidden** from YoutubeExplode for some videos (YouTube-side blocking/changes).
* Current UX shows a stack trace or generic failure; no recovery path.

**Solution**

* Introduced a **domain-level exception**: `DownloadBlockedException` for controlled 403 handling.
* Added a **test seam** for manifests: `IStreamManifestProvider` (YoutubeExplode impl by default).
* Added **yt-dlp compatibility mode** (opt-in):

  * **Audio download**: `DownloadAudioAsync(... audioFormat)`
  * **Video download**: merged when FFmpeg available; **no-merge fallback** when FFmpeg missing.
  * **Progress reporting** via streaming process runner (`RunStreamingAsync`) + throttled parsing.
* Compatibility mode now follows the **user’s selected dropdown format** (audio vs video). `CompatibilityModeAudioOnly` is only a default preselect.

**UX / Behavior**

* Compatibility OFF: existing YoutubeExplode behavior remains.
* Compatibility ON: yt-dlp is used directly for downloads; format selection respected.
* Better error messages for blocked downloads.

**Testing**

* Added `YoutubeDownloader.Core.Tests` with deterministic fakes (no network / no real processes).
* Unit tests added/extended:

  * `VideoDownloaderTests`
  * `YtDlpDownloaderTests`
  * `CompatibilityModeDownloadPlanTests`
* Manual tests:

  * compat on/off
  * audio formats (mp3/m4a/ogg/opus)
  * video mp4 with/without ffmpeg
  * cancellation stops yt-dlp and cleans partial files
  * progress updates in UI

**Notes / Dependencies**

* Compatibility mode requires **yt-dlp** installed (path configurable).
* Full video merge may require **ffmpeg**; app falls back to a single-file mp4 strategy if not found.

**Risk & Mitigations**

* Risk: large change set.
  Mitigation: changes isolated behind interfaces + opt-in compatibility mode + expanded test coverage.
* Risk: external process behavior variability.
  Mitigation: process runner is abstracted; progress parsing throttled; output path uses `--print after_move:filepath`.