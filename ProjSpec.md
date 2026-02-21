# MeetingScribe — Complete Technical Specification
## Windows Portable Desktop Application
## Ready for Codex Implementation

---

# 1. Product Overview

**MeetingScribe** is a portable Windows desktop application that:

1. Records a Webex meeting using two synchronized audio streams:
   - Microphone (local user)
   - System audio loopback (remote participants)
2. Performs **offline transcription** using `whisper.cpp`
3. Uses `whisper.cpp` native **VAD (Voice Activity Detection)**
4. Generates structured meeting minutes using **LM Studio local API**
5. Exports a Markdown note compatible with **Obsidian**
6. Requires **no admin privileges**
7. Works fully offline except for LM Studio local inference

This specification defines the complete architecture and behavior for deterministic implementation.

---

# 2. Technical Stack (MANDATORY)

The application MUST be implemented using:

- **.NET 8**
- **WPF**
- **MVVM pattern**
- **C# 12**
- **NAudio** for audio capture
- `System.Text.Json` for JSON handling
- `HttpClient` for LM Studio
- `Process` API for whisper.cpp CLI invocation

Target:
- Windows 10/11 x64
- Self-contained publish
- Portable folder distribution

---

# 3. Solution Structure

```

MeetingScribe.sln

src/
MeetingScribe.App/
WPF UI
ViewModels/
Views/
Commands/
Converters/

MeetingScribe.Core/
Pipeline/
Audio/
Whisper/
Transcript/
Llm/
Export/
Settings/
Logging/
Models/

MeetingScribe.Tests/

````

Separation rule:
- App layer = UI only
- Core layer = all business logic
- Tests = test Core only

---

# 4. Architecture

## 4.1 Pattern

MVVM with:

- MainViewModel
- SettingsViewModel
- RelayCommand
- PipelineOrchestrator (Core)

No business logic inside code-behind.

---

## 4.2 Application State Machine

States:

- Idle
- Recording
- Processing
  - TranscribingMic
  - TranscribingLoopback
  - Merging
  - Summarizing
  - Exporting
- Cancelling
- Done
- Error

State must be observable via INotifyPropertyChanged.

---

# 5. Application Lifecycle

1. Startup
   - Load settings
   - Cleanup old temp runs
   - Enumerate audio devices
2. Recording
   - Capture mic + loopback
3. Stop
   - Show confirmation dialog:
     - Start processing
     - Discard
     - Cancel
4. Processing pipeline runs automatically
5. Completion
   - Export Markdown
   - Optionally open vault folder

---

# 6. Audio Subsystem

## 6.1 Recording

Use NAudio:

- Mic: `WasapiCapture`
- Loopback: `WasapiLoopbackCapture`

Write raw WAV:
- mic_raw.wav
- loopback_raw.wav

## 6.2 Resampling

Convert raw WAV → 16 kHz mono PCM16 via `MediaFoundationResampler`.

Output:
- mic.wav
- loopback.wav

---

# 7. Synchronization and Offset Correction

## 7.1 Capture metadata

At first buffer arrival:

```csharp
RecordingSyncMetadata {
    DateTime RecordingStartUtc;
    long MicFirstSampleTicks;
    long LoopbackFirstSampleTicks;
    double InitialOffsetMs;
}
````

InitialOffsetMs =

```
(LoopbackFirst - MicFirst) / Stopwatch.Frequency * 1000
```

## 7.2 Merge offset correction

If InitialOffsetMs > 0:

* shift loopback timestamps backward

Else:

* shift mic timestamps backward

Clamp to >= 0.

Note:
This corrects startup skew only (no long-term drift correction in v1).

---

# 8. whisper.cpp Integration

## 8.1 Pinned Build

Distributed:

```
whisper/whisper-cli.exe
whisper/whisper-cli.VERSION.txt
```

VERSION file must include:

* commit hash
* build flags
* VAD support confirmation

---

## 8.2 VAD Model

Required file:

```
models/vad/ggml-silero-v*.bin
```

If `useVad=true` and model missing:

* show error
* allow disable VAD and continue

---

## 8.3 Command Construction

Base:

```
whisper-cli.exe
  -m "<model>"
  -f "<wav>"
  -l <language>
  --output-json
  --output-file "<outBase>"
```

If VAD enabled:

```
--vad
--vad-model "<vadModel>"
--vad-threshold <vadThreshold>
--vad-min-speech-duration-ms <vadMinSpeechMs>
--vad-min-silence-duration-ms <vadMinSilenceMs>
--vad-max-speech-duration-s <vadMaxSpeechSec>
--vad-speech-pad-ms <vadSpeechPadMs>
--vad-samples-overlap <vadSamplesOverlap>
```

Use a `WhisperCommandBuilder` class.

---

## 8.4 Progress Parsing

Parse stdout for percentage pattern.
If unavailable → indeterminate progress bar.

Never block pipeline if parsing fails.

---

# 9. Transcript Parsing

Expected JSON:

```json
{
  "segments": [
    { "start": 0.00, "end": 3.20, "text": " hello" }
  ]
}
```

Parser must:

* Support `start/end`
* Support fallback `t0/t1` (centiseconds)
* Trim whitespace
* Normalize spacing

---

# 10. Transcript Merge Engine

Algorithm:

1. Label:

   * mic → "You"
   * loopback → "Others"
2. Apply offset correction
3. Combine lists
4. Sort by StartSec
5. Detect overlap:

   * mark IsOverlap=true

Do NOT drop segments in v1.

---

# 11. LM Studio Integration

## 11.1 Endpoint

POST `{baseUrl}/chat/completions`

---

## 11.2 Retry Strategy

For attempt in 1..startupRetryCount:

* try request
* if fail → wait startupRetryDelaySec

Differentiate:

* connection failure
* 503 / model loading
* timeout

---

## 11.3 Always Save Raw Output

Always write:

```
llm_raw_output.txt
```

If JSON invalid after repair:

* export transcript-only note
* include raw output appendix

---

# 12. Prompt Templates (Exact)

## summary_system.txt

```
You are a meeting summarization assistant.
Return ONLY valid JSON.
Do not hallucinate information.
Use null if unknown.

Schema:
{
  "Title": string,
  "DateIso": string,
  "SummaryBullets": string[],
  "Decisions": string[],
  "Actions": [
    {
      "Text": string,
      "Owner": string|null,
      "DueDateIso": string|null,
      "Priority": "Low"|"Medium"|"High",
      "Evidence": string
    }
  ],
  "OpenQuestions": string[],
  "Risks": string[]
}
```

## summary_user.txt

```
Meeting Title: {{TITLE}}
Date: {{DATE_ISO}}

Transcript:
{{TRANSCRIPT}}

Return JSON only.
```

## summary_repair_system.txt

```
Your previous output was invalid JSON.
Return ONLY valid JSON matching the schema.
No commentary.
```

---

# 13. Chunking Algorithm

If transcript length > maxCharsPerChunk:

* Split into chunks
* Overlap chunkOverlapChars
* Summarize each chunk
* Merge results:

Merge rules:

* Concat and dedup bullets
* Dedup actions by normalized Text
* Choose highest priority
* Merge evidence ranges

---

# 14. Markdown Export

Template placeholders:

* {{TITLE}}
* {{DATE_ISO}}
* {{SUMMARY_BULLETS}}
* {{DECISIONS}}
* {{ACTIONS}}
* {{TRANSCRIPT}}

Use `Path.Combine` for file creation.

Filename:

```
YYYY-MM-DD - Title.md
```

Sanitize invalid characters.

---

# 15. Settings Handling

Primary:

```
<AppDir>/config/settings.json
```

Fallback:

```
%APPDATA%/MeetingScribe/settings.json
```

Include `"schemaVersion"`.

Migration required if version mismatch.

---

# 16. Temporary Files

Run folder:

```
%TEMP%/MeetingScribe/<timestamp>/
```

Startup:

* delete folders older than tempRetentionDays

UI:

* Clean temp files button

---

# 17. Logging

Run logs:

```
run.log
```

Persistent logs:

* AppDir/logs if writable
* else %APPDATA%/MeetingScribe/logs

---

# 18. Cancellation

All async operations accept CancellationToken.

Cancel behavior:

* Stop recording
* Kill whisper process tree
* Cancel HTTP request
* Keep run folder unless configured otherwise

---

# 19. UX Requirements

* Warn: loopback captures ALL system audio
* Stop confirmation dialog required
* Progress stage display required
* Cancel button during processing required

---

# 20. Build & Distribution

* dotnet publish
* Runtime: win-x64
* SelfContained: true
* Portable folder zip
* Include whisper folder + models

No installer.

---

# 21. Testing Strategy

Must include:

* Whisper JSON parser test
* Merge offset correction test
* Chunk splitting test
* Chunk merge logic test
* Markdown generation test
* Settings migration test
* Temp cleanup test

---

# 22. Acceptance Criteria

Application is complete when:

1. Runs without admin
2. Records two tracks
3. Applies offset correction
4. whisper.cpp runs with VAD
5. LM Studio produces structured JSON
6. Raw LLM output preserved
7. Cancel works at any stage
8. Markdown exported correctly
9. Temp cleanup works
10. UI never blocks or freezes

---

# 23. Known Limitations

* No drift correction
* No multi-speaker diarization
* Loopback captures all audio
* VAD behavior depends on whisper model

---

END OF SPECIFICATION

```

This version is now:

- Fully stack-defined (.NET 8, WPF, MVVM)
- Architecturally complete
- Pipeline deterministic
- VAD-integrated
- Offset-aware
- LM robust
- UX-safe
- Portable-defined
- Testable

Ready for Codex implementation.
