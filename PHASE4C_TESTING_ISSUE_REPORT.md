# Phase 4C Testing Issue Report

**Date:** February 10, 2026  
**Phase:** 4C - Full Video Fallback via yt-dlp  
**Status:** ⚠️ Implementation Complete, Testing Failed  

---

## Summary

Phase 4C implementation is complete with all code changes applied and tests passing (17/17). However, **manual end-to-end testing fails** - the yt-dlp fallback mechanism does not trigger when a 403 error occurs.

---

## Implementation Status

### ✅ Code Complete

**Files Created:**
- `YoutubeDownloader.Core/Downloading/YtDlpDownloader.cs` - Unified downloader with both audio and video support
- `YoutubeDownloader.Core.Tests/Downloading/YtDlpDownloaderTests.cs` - 10 unit tests (6 audio + 4 video)

**Files Modified:**
- `YoutubeDownloader/ViewModels/Components/DashboardViewModel.cs` - Added video fallback branch
- `YoutubeDownloader.Core/Downloading/VideoDownloader.cs` - Added 403 detection and DownloadBlockedException throwing

**Files Deleted:**
- `YtDlpAudioDownloader.cs` (renamed to YtDlpDownloader.cs)
- `YtDlpAudioDownloaderTests.cs` (renamed to YtDlpDownloaderTests.cs)

### ✅ Tests Pass

```
Test summary: total: 17, failed: 0, succeeded: 17, skipped: 0
Build succeeded in 3.2s
```

**Test Coverage:**
- 8 tests: VideoDownloader (Phase 3)
- 6 tests: YtDlpDownloader audio mode (Phase 4A)
- 3 tests: YtDlpDownloader video mode (Phase 4C - NEW)

### ✅ Build Successful

```
Build succeeded in 1.5s
```

No compile errors detected.

---

## The Problem

### Expected Behavior

When user downloads a video and encounters a 403 Forbidden error:

1. `VideoDownloader` should throw `DownloadBlockedException`
2. `DashboardViewModel` should catch it
3. If `UseCompatibilityModeYtDlp == true`, trigger yt-dlp fallback
4. If `CompatibilityModeAudioOnly == false`, download full video via yt-dlp
5. Update UI with completed download

### Actual Behavior

1. User sees 403 error message in UI: "YouTube refused the request (403 Forbidden)..."
2. **No console debug output appears** (despite extensive Console.WriteLine statements added)
3. Fallback mechanism does not trigger
4. Download fails with original error message

### Debug Output Added

Multiple debug statements were added but **none produce output**:

```csharp
// VideoDownloader.cs - Line 53
Console.WriteLine($"[DEBUG] 403 Forbidden error detected - throwing DownloadBlockedException");

// VideoDownloader.cs - Line 119
Console.WriteLine($"[DEBUG] 403 Forbidden during actual download - throwing DownloadBlockedException");

// DashboardViewModel.cs - Line 145
Console.WriteLine($"[DEBUG] DownloadBlockedException caught. UseCompatibilityModeYtDlp={_settingsService.UseCompatibilityModeYtDlp}...");

// DashboardViewModel.cs - Line 239
Console.WriteLine($"[DEBUG] General Exception handler caught: {ex.GetType().Name}");
Console.WriteLine($"[DEBUG] Is DownloadBlockedException? {ex is DownloadBlockedException}");
```

**Result:** Zero console output when 403 error occurs.

---

## Test Configuration Used

### Settings (via UI Settings Dialog)

```
✅ Use Compatibility Mode (yt-dlp): ON
❌ Compatibility Mode Audio Only: OFF (unchecked)
📁 yt-dlp Path: "yt-dlp" (default)
```

### Test Video URL

```
https://www.youtube.com/watch?v=v-18TJLBu4s
```

### Test Steps Executed

1. Run app: `dotnet run --project .\YoutubeDownloader\YoutubeDownloader.csproj`
2. Open Settings → Enable compatibility mode, disable audio-only → Close settings
3. Enter URL in query field
4. Click search → Select video → Click download
5. Watch terminal for console output
6. Observe UI error message

### Environment

```
OS: Windows
.NET: 10.0
yt-dlp: v2026.02.04 (verified installed via winget)
IDE: VS Code
```

---

## Diagnostic Analysis

### Hypothesis 1: Exception Not Being Thrown

**Evidence:**
- No console output from VideoDownloader catch blocks
- Suggests HttpRequestException with 403 status is not being caught

**Possible Causes:**
- HttpRequestException.StatusCode property may be null or different value
- Exception may occur in different part of call stack
- YoutubeExplode library may handle 403 differently

**Action Needed:**
- Add try/catch around ALL VideoDownloader calls
- Log the actual exception type and properties
- Check if HttpRequestException has StatusCode populated

### Hypothesis 2: Exception Caught by Wrong Handler

**Evidence:**
- General exception handler has no output either
- Suggests exception may be caught elsewhere before reaching DashboardViewModel

**Possible Causes:**
- Task exception handling (async/await)
- Dispatcher/UI thread exception handling
- Framework-level exception swallowing

**Action Needed:**
- Add global exception handler
- Check if exception occurs during async operation
- Verify exception propagation through async call stack

### Hypothesis 3: Settings Not Persisted

**Evidence:**
- User toggled settings but fallback still doesn't work
- No way to verify settings values without debug output

**Possible Causes:**
- Settings dialog may not call Save() on close
- Settings may revert on app restart
- Property change notifications may not work

**Action Needed:**
- Add debug output showing settings values on app startup
- Verify SettingsService.Save() is called
- Check Settings.dat file existence and content

### Hypothesis 4: Console Output Not Visible

**Evidence:**
- Multiple Console.WriteLine() statements produce no output

**Possible Causes:**
- Avalonia UI may not show console output
- Output may be redirected/buffered
- Running in a mode that suppresses console

**Action Needed:**
- Use file logging instead: `File.AppendAllText("debug.log", ...)`
- Use MessageBox dialogs for critical debug info
- Check if console is attached to process

---

## Code Verification

### Exception Throwing Logic (VideoDownloader.cs)

```csharp
catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
{
    Console.WriteLine($"[DEBUG] 403 Forbidden error detected - throwing DownloadBlockedException");
    throw new DownloadBlockedException(
        "YouTube refused the request (403 Forbidden)...",
        ex,
        HttpStatusCode.Forbidden
    );
}
```

**Status:** ✅ Code looks correct

### Exception Catching Logic (DashboardViewModel.cs)

```csharp
catch (DownloadBlockedException ex)
{
    Console.WriteLine($"[DEBUG] DownloadBlockedException caught. UseCompatibilityModeYtDlp={_settingsService.UseCompatibilityModeYtDlp}...");
    
    if (_settingsService.UseCompatibilityModeYtDlp)
    {
        // Fallback logic here
        if (_settingsService.CompatibilityModeAudioOnly)
        {
            // Audio fallback
        }
        else
        {
            // Video fallback (NEW)
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine($"[DEBUG] General Exception handler caught: {ex.GetType().Name}");
    // ...
}
```

**Status:** ✅ Code looks correct, follows C# exception hierarchy rules

### Settings Service

```csharp
[ObservableProperty]
public partial bool UseCompatibilityModeYtDlp { get; set; }

[ObservableProperty]
public partial bool CompatibilityModeAudioOnly { get; set; } = true;
```

**Status:** ✅ Properties defined correctly with MVVM observability

---

## Recommended Next Steps

### Priority 1: Verify Exception Flow

1. **Replace Console.WriteLine with File Logging:**
   ```csharp
   File.AppendAllText("F:\\debug.log", 
       $"[{DateTime.Now:HH:mm:ss}] 403 error detected\n");
   ```

2. **Add try/catch around initial download call:**
   ```csharp
   try
   {
       await downloader.DownloadVideoAsync(...);
   }
   catch (Exception ex)
   {
       File.AppendAllText("F:\\debug.log", 
           $"Exception: {ex.GetType().FullName}\n" +
           $"Message: {ex.Message}\n" +
           $"StatusCode: {(ex as HttpRequestException)?.StatusCode}\n");
       throw;
   }
   ```

3. **Check HttpRequestException structure:**
   - Verify StatusCode property exists and is populated
   - Log all exception properties to understand behavior

### Priority 2: Verify Settings Persistence

1. **Add debug output on app startup:**
   ```csharp
   File.AppendAllText("F:\\debug.log",
       $"[STARTUP] UseCompatibilityModeYtDlp={_settingsService.UseCompatibilityModeYtDlp}\n" +
       $"[STARTUP] CompatibilityModeAudioOnly={_settingsService.CompatibilityModeAudioOnly}\n");
   ```

2. **Check Settings.dat file:**
   ```powershell
   Get-Content "F:\GitHub\YoutubeDownloader\YoutubeDownloader\bin\Debug\net10.0\Settings.dat"
   ```

3. **Verify setting change triggers save:**
   - Add logging in SettingsService property setters

### Priority 3: Alternative Testing Approach

Since fallback testing failed, consider:

1. **Test yt-dlp directly:**
   ```powershell
   yt-dlp -f "bv*+ba/b" --merge-output-format mp4 "https://www.youtube.com/watch?v=v-18TJLBu4s"
   ```

2. **Test YtDlpDownloader.DownloadVideoAsync in isolation:**
   - Create simple console app
   - Call DownloadVideoAsync directly
   - Verify it works without UI layer

3. **Mock the 403 error:**
   - Modify VideoDownloader to always throw DownloadBlockedException
   - Verify fallback triggers even without real 403

---

## Unit Test Results

All unit tests pass, confirming core logic works:

```
✅ DownloadVideoAsync_ShouldCallProcessRunner_WithCorrectArguments
✅ DownloadVideoAsync_ShouldReportProgress_AndReturnFinalFilePath
✅ DownloadVideoAsync_WhenProcessFails_ShouldThrowInvalidOperationException
✅ DownloadVideoAsync_WhenCancelled_ShouldCleanupPartialFiles
```

This proves:
- yt-dlp command construction is correct
- Progress reporting works
- Error handling works
- Output file detection works

**Therefore:** The issue is NOT in YtDlpDownloader, but in the integration layer or exception flow.

---

## Conclusion

**Phase 4C Implementation:** ✅ **COMPLETE**
- Code structure correct
- Unit tests pass
- Build succeeds

**Phase 4C Testing:** ❌ **FAILED**
- Fallback does not trigger in real usage
- No diagnostic output visible
- Root cause unknown

**Impact on Development:**
- Cannot verify video fallback works end-to-end
- Cannot confirm Phase 4C success criteria
- Cannot proceed with confidence to Phase 4D or production

**Recommendation:**
- Escalate to higher-tier debugging with actual debugger attachment
- Use file-based logging instead of console output
- Consider alternative diagnostic approaches (breakpoints, trace logging)
- May need to use Visual Studio debugger instead of dotnet CLI

---

## Appendix: Complete File Changes

### YtDlpDownloader.cs Key Addition (Lines 146-276)

```csharp
public async Task<string> DownloadVideoAsync(
    string url,
    string outputDir,
    string mergeFormat = "mp4",
    CancellationToken cancellationToken = default,
    IProgress<double>? progress = null
)
{
    // ... implementation with:
    // - Command: yt-dlp --newline --progress -f "bv*+ba/b" --merge-output-format mp4
    // - Progress parsing from stdout
    // - Output path detection via --print after_move:filepath
    // - Cleanup on cancellation
}
```

### DashboardViewModel.cs Key Change (Lines 142-222)

```csharp
catch (DownloadBlockedException ex)
{
    if (_settingsService.UseCompatibilityModeYtDlp)
    {
        // CHANGED: Now branches on CompatibilityModeAudioOnly
        if (_settingsService.CompatibilityModeAudioOnly)
        {
            // Audio fallback (Phase 4A)
            fallbackFilePath = await ytDlpDownloader.DownloadAudioAsync(...);
        }
        else
        {
            // Video fallback (Phase 4C - NEW)
            fallbackFilePath = await ytDlpDownloader.DownloadVideoAsync(...);
        }
        
        download.FilePath = fallbackFilePath;
        download.Status = DownloadStatus.Completed;
        return;
    }
}
```

---

**End of Report**
