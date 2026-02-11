# YoutubeDownloader

---

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

---

[![Status](https://img.shields.io/badge/status-maintenance-ffd700.svg)](https://github.com/Tyrrrz/.github/blob/master/docs/project-status.md)
[![Made in Ukraine](https://img.shields.io/badge/made_in-ukraine-ffd700.svg?labelColor=0057b7)](https://tyrrrz.me/ukraine)
[![Build](https://img.shields.io/github/actions/workflow/status/Tyrrrz/YoutubeDownloader/main.yml?branch=master)](https://github.com/Tyrrrz/YoutubeDownloader/actions)
[![Release](https://img.shields.io/github/release/Tyrrrz/YoutubeDownloader.svg)](https://github.com/Tyrrrz/YoutubeDownloader/releases)
[![Downloads](https://img.shields.io/github/downloads/Tyrrrz/YoutubeDownloader/total.svg)](https://github.com/Tyrrrz/YoutubeDownloader/releases)
[![Discord](https://img.shields.io/discord/869237470565392384?label=discord)](https://discord.gg/2SUWKFnHSm)

<table>
    <tr>
        <td width="99999" align="center">Development of this project is entirely funded by the community. <b><a href="https://tyrrrz.me/donate">Consider donating to support!</a></b></td>
    </tr>
</table>

<p align="center">
    <img src="favicon.png" alt="Icon" />
</p>

**YoutubeDownloader** is an application that lets you download videos from YouTube.
You can copy-paste URL of any video, playlist or channel and download it directly in a format of your choice.
It also supports searching by keywords, which is helpful if you want to quickly look up and download videos.

> [!NOTE]
> This application uses [**YoutubeExplode**](https://github.com/Tyrrrz/YoutubeExplode) under the hood to interact with YouTube.
> You can [read this article](https://tyrrrz.me/blog/reverse-engineering-youtube-revisited) to learn more about how it works.

## Terms of use<sup>[[?]](https://github.com/Tyrrrz/.github/blob/master/docs/why-so-political.md)</sup>

By using this project or its source code, for any purpose and in any shape or form, you grant your **implicit agreement** to all the following statements:

- You **support Ukraine's territorial integrity, including its claims over temporarily occupied territories of Crimea and Donbas**

To learn more about the war and how you can help, [click here](https://tyrrrz.me/ukraine). Glory to Ukraine! 🇺🇦

## Download

- 🟢 **[Stable release](https://github.com/Tyrrrz/YoutubeDownloader/releases/latest)**
- 🟠 [CI build](https://github.com/Tyrrrz/YoutubeDownloader/actions/workflows/main.yml)

> [!IMPORTANT]
> To launch the app on MacOS, you need to first remove the downloaded file from quarantine.
> You can do that by running the following command in the terminal: `xattr -rd com.apple.quarantine YoutubeDownloader.app`.

> [!NOTE]
> If you're unsure which build is right for your system, consult with [this page](https://useragent.cc) to determine your OS and CPU architecture.

> [!NOTE]
> **YoutubeDownloader** comes bundled with [FFmpeg](https://ffmpeg.org) which is used for processing videos.
> You can also download a version of **YoutubeDownloader** that doesn't include FFmpeg (`YoutubeDownloader.Bare.*` builds) if you prefer to use your own installation.

## Features

- Cross-platform graphical user interface
- Download videos by URL
- Download videos from playlists or channels
- Download videos by search query
- Selectable video quality and format
- Automatically embed audio tracks in alternative languages
- Automatically embed subtitles
- Automatically inject media tags
- Log in with a YouTube account to access private content

## Screenshots

![list](.assets/list.png)
![single](.assets/single.png)
![multiple](.assets/multiple.png)

