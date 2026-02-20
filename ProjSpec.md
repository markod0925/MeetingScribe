# MeetingScribe (Windows Portable) — Technical Specification for Codex

## 0. Purpose

Build a **portable Windows desktop application** that:
1. Captures a **Webex meeting audio** using **two separate tracks**:
   - **Microphone** (local user)
   - **System audio loopback** (remote participants via speakers/headset output)
2. Performs **offline transcription** using **whisper.cpp** (no dependency on LM Studio for ASR).
3. Calls **LM Studio (local HTTP API)** to generate:
   - Meeting summary
   - Decisions
   - Action items (with optional owners/due dates)
4. Exports a single **Obsidian-ready Markdown note** (no Obsidian plugin required).

Constraints:
- **Windows only**
- **Post-meeting processing** (no live transcription requirement)
- **Portable**: everything required to run is in the app folder, except **temporary files** (WAV/JSON/logs if needed), which go to `%TEMP%` by default.
- Must run without admin privileges.

Non-goals (v1):
- True diarization (speaker identification beyond mic-vs-loopback).
- Webex API integration or automatic meeting metadata extraction from Webex.
- Cloud services.

---

## 1. High-Level User Flow

1. User selects:
   - Microphone input device
   - System audio output device (for loopback capture) OR default system render device
   - Output Obsidian vault folder (and subfolder)
   - Whisper model (local path)
   - LM Studio endpoint/model
2. User clicks **Start Recording**.
3. App records:
   - `mic.wav`
   - `loopback.wav`
4. User clicks **Stop Recording**.
5. App runs transcription offline:
   - `mic.json`
   - `loopback.json`
6. App merges transcripts into a single timeline:
   - `merged_transcript.json`
   - (optional) `merged_transcript.md`
7. App calls LM Studio to produce structured output:
   - `llm_summary.json`
8. App renders final Markdown note:
   - `YYYY-MM-DD - <Title>.md` into the chosen vault folder
9. App displays success + link to the generated note.

---

## 2. Repository and Project Structure

Use .NET 8, WPF.

```

MeetingScribe/
MeetingScribe.sln
src/
MeetingScribe.App/                 # WPF UI (MVVM)
MeetingScribe.App.csproj
App.xaml
MainWindow.xaml
ViewModels/
Views/
Services/
Resources/
MeetingScribe.Core/                # Domain + pipeline orchestration
MeetingScribe.Core.csproj
Audio/
Transcription/
Llm/
Export/
Models/
Utils/
MeetingScribe.Tests/               # Unit tests
MeetingScribe.Tests.csproj

tools/
whisper/
whisper-cli.exe                  # shipped with app
models/
ggml-small.bin                   # optional: shipped or user-provided
templates/
obsidian_note.md                 # note template
prompts/
summary_system.txt
summary_user.txt

build/
publish.ps1
README.md
LICENSE

```

Distribution artifact (portable zip):
```

dist/MeetingScribe/
MeetingScribe.exe
whisper/whisper-cli.exe
models/...
templates/...
prompts/...
config/settings.json                 # created on first run

````

Notes:
- Prefer distributing as a **folder** (zip). Avoid embedding whisper/model into a single-file exe because whisper-cli and model must remain as files on disk.
- If app folder is not writable, store `settings.json` in `%TEMP%` and warn user (still runs).

---

## 3. Technology Choices

- Language: **C#**
- UI: **WPF**
- Audio: **NAudio** (WASAPI capture)
- Offline ASR: **whisper.cpp** via `whisper-cli.exe` (spawned process)
- LLM: **LM Studio** local server via HTTP (OpenAI-compatible endpoints)
- Serialization: `System.Text.Json`
- Logging: lightweight file logger (custom) or `Microsoft.Extensions.Logging` + simple file sink

---

## 4. Core Requirements

### 4.1 Audio Capture
- Record two tracks simultaneously:
  - Microphone: WASAPI capture of selected input device
  - Loopback: WASAPI loopback capture of selected output/render device (default render device accepted)
- Format:
  - Sample rate: **16 kHz** preferred (Whisper-friendly) OR record at device rate and resample to 16 kHz
  - Channels: mono (mic), stereo/mono (loopback) -> downmix to mono for Whisper
  - Bit depth: 16-bit PCM WAV
- Start/Stop must be robust and never hang.
- Handle device loss gracefully (stop recording with error message).

### 4.2 Temporary Files
- Default temp folder:
  - `%TEMP%\MeetingScribe\<yyyy-MM-dd_HH-mm-ss>\`
- Files created:
  - `mic.wav`, `loopback.wav`
  - `mic.json`, `loopback.json`
  - `merged_transcript.json`
  - `llm_summary.json`
  - optionally: debug logs

### 4.3 Whisper Transcription (Offline)
- Must not depend on LM Studio.
- Call `whisper-cli.exe` for each track.
- Output must include timestamps.

### 4.4 Transcript Merge
- Produce a single merged chronological transcript with speaker labels:
  - Mic track labeled `"You"` (or configurable)
  - Loopback labeled `"Others"`
- Merge based on segment start times.

### 4.5 LLM Summarization via LM Studio
- Use local endpoint (default `http://localhost:1234/v1/`).
- Generate:
  - Summary (bullets + short paragraph)
  - Decisions
  - Action items (checkbox list; include evidence timestamps)
  - Open questions / risks (optional)
- Output must be structured JSON to reduce formatting errors.

### 4.6 Obsidian Markdown Export
- Single Markdown note created in vault folder.
- Use a template with placeholders.
- Include:
  - YAML frontmatter
  - Summary/Decisions/Actions sections
  - Transcript section (collapsible via `details` HTML blocks if desired)

---

## 5. Configuration

### 5.1 settings.json (portable)
Location:
- Prefer: `<AppDir>\config\settings.json`
- If `<AppDir>` not writable: `%TEMP%\MeetingScribe\config\settings.json` and warn.

Schema (`config/settings.json`):
```json
{
  "audio": {
    "micDeviceId": "",
    "loopbackDeviceId": "",
    "targetSampleRateHz": 16000,
    "wavBitDepth": 16,
    "downmixToMono": true
  },
  "paths": {
    "whisperExe": "whisper/whisper-cli.exe",
    "whisperModel": "models/ggml-small.bin",
    "obsidianVaultPath": "",
    "obsidianMeetingsSubfolder": "Meetings",
    "templatePath": "templates/obsidian_note.md",
    "promptsDir": "prompts"
  },
  "whisper": {
    "language": "en",
    "threads": 8,
    "maxSegmentLengthSec": 30
  },
  "lmStudio": {
    "baseUrl": "http://localhost:1234/v1",
    "model": "local-model-name",
    "temperature": 0.2,
    "maxTokens": 1200,
    "timeoutSec": 120
  },
  "ui": {
    "defaultMeetingTitle": "Webex Meeting",
    "autoOpenOutputFolder": true
  }
}
````

Paths are **relative to AppDir** unless absolute.

---

## 6. Detailed Implementation Plan (Milestones)

### Milestone 1 — Project Skeleton + Packaging

* Create solution with three projects: App, Core, Tests.
* Implement settings load/save with portable fallback.
* Implement basic WPF UI:

  * Devices dropdown (mic, loopback)
  * Start/Stop recording
  * Status/log panel
  * Settings panel (paths, LM Studio baseUrl/model)
* Implement publish script to produce portable folder.

Deliverable: portable folder runs, UI shows devices, saves settings.

### Milestone 2 — Dual-track Recording (NAudio)

* Implement `AudioRecorder` that records mic and loopback simultaneously.
* Write WAV files to temp run folder.
* Ensure clean stop and file flush.

Deliverable: `mic.wav` and `loopback.wav` generated and playable.

### Milestone 3 — Whisper Transcription Integration (Process Runner)

* Implement `WhisperRunner` that calls whisper-cli twice.
* Parse whisper JSON output into internal model.
* Show progress + capture stdout/stderr to logs.

Deliverable: `mic.json`, `loopback.json` created.

### Milestone 4 — Merge Transcript

* Implement `TranscriptMerger` producing `merged_transcript.json`.
* Provide a preview view in UI.

Deliverable: merged transcript timeline.

### Milestone 5 — LM Studio Summarization

* Implement OpenAI-compatible client for LM Studio `/chat/completions`.
* Prompt + schema validation; retry on invalid JSON.
* Generate `llm_summary.json`.

Deliverable: summary/actions produced reliably.

### Milestone 6 — Obsidian Markdown Export

* Implement template-based Markdown rendering.
* Save note to vault path.
* Include transcript + evidence timestamps.

Deliverable: note created in vault and viewable in Obsidian.

### Milestone 7 — Hardening + Tests

* Unit tests for:

  * JSON parsing
  * transcript merging
  * markdown rendering
* Error handling: device missing, whisper missing, model missing, LM Studio unreachable.
* UX polishing.

---

## 7. Software Architecture

### 7.1 Core Data Models

#### Transcript segment (internal)

```csharp
public sealed record TranscriptSegment(
  string Track,          // "mic" | "loopback"
  string Speaker,        // "You" | "Others"
  double StartSec,
  double EndSec,
  string Text
);
```

#### Full transcript (internal)

```csharp
public sealed record TranscriptDocument(
  string MeetingId,
  DateTime StartedAt,
  IReadOnlyList<TranscriptSegment> Segments
);
```

#### LLM Summary model (structured JSON)

```csharp
public sealed record LlmMeetingSummary(
  string Title,
  string DateIso,
  List<string> SummaryBullets,
  List<string> Decisions,
  List<ActionItem> Actions,
  List<string> OpenQuestions,
  List<string> Risks
);

public sealed record ActionItem(
  string Text,
  string? Owner,
  string? DueDateIso,
  string Priority,           // "Low"|"Medium"|"High"
  string Evidence            // e.g. "00:12:34-00:13:10"
);
```

### 7.2 Core Services

* `SettingsService`

  * Load/save settings with writable fallback.
* `TempRunService`

  * Creates per-run temp folder
  * Tracks and cleans temp files
* `AudioDeviceService`

  * Enumerates mic and render devices
* `AudioRecorder`

  * `Start(runDir, micDeviceId, loopbackDeviceId)`
  * `Stop()`
* `WhisperRunner`

  * `Transcribe(wavPath, outJsonPath, modelPath, language, threads)`
* `WhisperJsonParser`

  * Parses whisper output JSON to `TranscriptSegment[]`
* `TranscriptMerger`

  * Merges mic + loopback segments
* `LmStudioClient`

  * Calls `/v1/chat/completions`
* `SummarizationService`

  * Builds prompts, validates JSON
* `MarkdownExporter`

  * Applies template placeholders, writes final `.md`

### 7.3 UI (MVVM)

* `MainViewModel`

  * Commands: Start, Stop, Transcribe, Summarize, Export
  * State machine: Idle → Recording → Processing → Done/Error
* `SettingsViewModel`

  * Paths, LM Studio, Whisper settings
* Views:

  * `MainView.xaml`
  * `SettingsView.xaml`

---

## 8. Audio Capture Details (NAudio)

### 8.1 Device Enumeration

Use NAudio’s MMDevice enumerator:

* Mic: `DataFlow.Capture`
* Loopback: `DataFlow.Render` (then loopback capture on selected render device)

### 8.2 Recording Implementation Requirements

* Record mic and loopback simultaneously:

  * Mic: `WasapiCapture` (selected capture device)
  * Loopback: `WasapiLoopbackCapture` (selected render device)
* Write WAV:

  * Use `WaveFileWriter`
* Downmix to mono + resample to 16k:

  * Preferred: record native -> post-process to 16k mono WAV before whisper.
  * Acceptable: record directly at 16k if device supports (not always).
* Post-process approach:

  * Use NAudio resampler: `MediaFoundationResampler` (works on Windows)
  * Ensure output is PCM 16-bit mono 16k.

### 8.3 File Naming

In runDir:

* `mic_raw.wav`, `loopback_raw.wav`
* `mic.wav`, `loopback.wav` (post-processed 16k mono)

---

## 9. whisper.cpp Integration

### 9.1 Files shipped

* `tools/whisper/whisper-cli.exe` copied to distribution as `whisper/whisper-cli.exe`
* Whisper model file in `models/` (or user supplies path)

### 9.2 Invocation

Run per track:

Example command (adapt flags if your whisper-cli build differs):

```
whisper-cli.exe -m "<modelPath>" -f "<inputWav>" -l en --output-json --output-file "<outBase>"
```

Output expectation:

* `"<outBase>.json"` exists after completion.

Implementation notes:

* Use `ProcessStartInfo` with:

  * `RedirectStandardOutput = true`
  * `RedirectStandardError = true`
  * `CreateNoWindow = true`
* Capture logs to a run log file.

### 9.3 Whisper JSON Parsing

Expected JSON includes segments with start/end and text.
Parse into:

* `TranscriptSegment` with `StartSec`, `EndSec`, `Text`
* Add `Track` and `Speaker` based on source.

If whisper output format differs, implement a tolerant parser:

* Find `segments` array
* Each segment has `t0`/`t1` or `start`/`end` (handle both)

---

## 10. Transcript Merge Rules

Inputs:

* `micSegments[]` labeled Speaker="You"
* `loopSegments[]` labeled Speaker="Others"

Merge algorithm:

1. Concatenate both arrays.
2. Sort by `StartSec` ascending.
3. Optional: if two segments overlap heavily, keep both but ensure stable ordering:

   * Tie-breaker: mic first if start times equal.
4. Optional text cleanup:

   * Trim
   * Collapse whitespace

Output:

* `merged_transcript.json` with full list
* Optional `merged_transcript.md` preview:

  ```
  [00:12:34] You: ...
  [00:12:40] Others: ...
  ```

Time formatting:

* `hh:mm:ss` from seconds (floor)

---

## 11. LM Studio Integration (OpenAI-Compatible)

### 11.1 Endpoint

Default:

* `POST {baseUrl}/chat/completions`
  Example baseUrl: `http://localhost:1234/v1`

### 11.2 Request

Use JSON:

```json
{
  "model": "<settings.lmStudio.model>",
  "temperature": 0.2,
  "max_tokens": 1200,
  "messages": [
    {"role": "system", "content": "<systemPrompt>"},
    {"role": "user", "content": "<userPrompt>"}    
  ]
}
```

### 11.3 Output format requirement

The model must return **valid JSON only**, matching `LlmMeetingSummary`.

Reliability strategy:

* Attempt 1: strict JSON-only instruction.
* Validate JSON parse; if fails:

  * Attempt 2: send a repair prompt: “Return ONLY valid JSON; do not include markdown or commentary.”
* Max 2 retries.

### 11.4 Prompting (files in prompts/)

* `summary_system.txt` — policy + JSON schema requirements
* `summary_user.txt` — includes transcript content and meeting title/date

Prompt requirements:

* Do not invent facts.
* For each action item include `Evidence` timestamp range.
* If owner/due date unknown, leave null.

Transcript inclusion:

* Include merged transcript as text with timestamps and speakers.
* For long meetings: chunking:

  * Split transcript into N chunks (by time or token estimate).
  * Summarize each chunk to intermediate JSON (mini summaries).
  * Merge into final summary JSON.

v1 simplification:

* If transcript length > threshold, do hierarchical summarization.

---

## 12. Obsidian Markdown Export

### 12.1 Template

`templates/obsidian_note.md` with placeholders:

```md
---
title: "{{TITLE}}"
date: "{{DATE_ISO}}"
source: "Webex"
tags: [meeting]
---

# Summary
{{SUMMARY_BULLETS}}

# Decisions
{{DECISIONS}}

# Action Items
{{ACTIONS}}

<details>
<summary>Transcript</summary>

{{TRANSCRIPT}}

</details>
```

Rendering rules:

* `{{SUMMARY_BULLETS}}` -> markdown list `- ...`
* `{{DECISIONS}}` -> markdown list
* `{{ACTIONS}}` -> checklist:

  * `- [ ] **Action**: ...  \n  **Owner**: ...  **Due**: ...  **Priority**: ...  \n  **Evidence**: 00:12:34-00:13:10`
* `{{TRANSCRIPT}}` -> lines:

  * `[00:12:34] You: text`
  * `[00:12:40] Others: text`

### 12.2 Output path

`<vaultPath>\<subfolder>\YYYY-MM-DD - <TitleSanitized>.md`

Sanitization:

* Remove invalid filename chars: `<>:"/\|?*`
* Collapse whitespace
* Max length 120 chars

---

## 13. Error Handling and UX Requirements

### 13.1 Pre-flight checks

Before recording:

* Verify whisper exe exists
* Verify model exists
* Verify vault path exists (or allow user to select)
* Verify LM Studio reachable (optional; only needed at summarization stage)

### 13.2 Common errors

* Device not available:

  * Show message; keep app responsive.
* whisper process fails:

  * Show stderr tail; store full log in runDir.
* LM Studio unreachable:

  * Allow exporting transcript-only note.
* App folder not writable:

  * Store settings/logs in `%TEMP%` and show banner.

### 13.3 State Machine

* Idle
* Recording
* Processing: TranscribingMic
* Processing: TranscribingLoopback
* Processing: Merging
* Processing: Summarizing
* Processing: Exporting
* Done / Error

UI must show:

* Current state
* Progress (indeterminate acceptable)
* Run folder path for debugging

---

## 14. Build and Publish

### 14.1 Publish target

* Windows x64
* self-contained

Recommended `dotnet publish` flags (folder distribution):

* Configuration: Release
* Runtime: win-x64
* SelfContained: true

Do **not** force single-file if it complicates shipping `whisper-cli.exe` and models.

Provide `build/publish.ps1`:

* publishes exe to `dist/MeetingScribe/`
* copies:

  * `tools/whisper/*` -> `dist/MeetingScribe/whisper/`
  * `tools/models/*` -> `dist/MeetingScribe/models/` (optional)
  * `tools/templates/*` -> `dist/MeetingScribe/templates/`
  * `tools/prompts/*` -> `dist/MeetingScribe/prompts/`

---

## 15. Code-Level Guidance (Key APIs and Patterns)

### 15.1 Process runner (whisper)

Implement a reusable helper:

* Start process
* Stream stdout/stderr to log
* Await exit with timeout
* Return exit code + captured stderr tail

### 15.2 JSON validation for LLM output

* Parse to `LlmMeetingSummary`
* Validate required fields non-null:

  * `Title`, `DateIso`, `SummaryBullets`, `Actions`
* If invalid:

  * retry with repair prompt

### 15.3 Unit Tests (minimum)

* `TranscriptMergerTests`

  * ensures stable ordering and correct speaker labels
* `MarkdownExporterTests`

  * placeholders replaced correctly
* `FilenameSanitizerTests`

---

## 16. Acceptance Criteria (v1)

1. Portable folder runs on a standard Windows user account (no admin).
2. Records and produces two WAV files:

   * `mic.wav` and `loopback.wav` (16k mono)
3. Offline transcription produces JSON for both tracks.
4. Merged transcript is chronological and labeled You/Others.
5. LM Studio summarization produces valid JSON and renders correct Markdown note.
6. Note is saved in chosen Obsidian vault folder and opens correctly in Obsidian.
7. If LM Studio is unavailable, app can still export transcript-only note.

---

## 17. Implementation Notes / Defaults

* Default language: `en` (configurable)
* Default whisper model: `ggml-small.bin` (balance quality/speed)
* Default LM Studio baseUrl: `http://localhost:1234/v1`
* Default meeting title: “Webex Meeting” with user editable field

---

## 18. Future Enhancements (post-v1)

* Optional diarization (pyannote via separate optional module) or heuristic speaker turns
* Auto-detect meeting title from active window name
* Store meeting index note and backlinks in Obsidian
* Chunk-level hierarchical summarization improvements
* Optional audio trimming via VAD before whisper to speed up transcription
