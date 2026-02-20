# MeetingScribe (Windows Portable) — Updated Technical Specification (VAD + Sync Enhancements)

This revision integrates:

- Native **whisper.cpp VAD support**
- Startup **track offset correction**
- Improved **LM Studio robustness**
- Better **UX safety around Stop**
- Deterministic **whisper CLI contract**

This document supersedes previous versions.

---

# 0. Purpose

Build a **portable Windows desktop application** that:

1. Captures a Webex meeting into **two separate tracks**:
   - Microphone (local user)
   - System loopback (remote participants)
2. Performs **offline transcription via whisper.cpp**
3. Uses **LM Studio (local API)** for structured meeting minutes
4. Exports an **Obsidian-ready Markdown note**

Constraints:

- Windows only
- Post-meeting processing
- Portable (no admin, no installer)
- Offline-first
- Deterministic and testable pipeline

---

# 1. High-Level User Flow (Revised UX)

## Recording Flow

1. User clicks **Start Recording**
2. App records mic + loopback
3. User clicks **Stop Recording**
4. App shows dialog:

   > Recording finished. What do you want to do?

   Options:

   - ✅ Start processing now (default)
   - 🗑 Discard recording
   - ❌ Cancel (return to recording)

5. If confirmed → full automatic pipeline runs

This prevents accidental long processing runs.

---

# 2. Repository Structure (Updated)

Add VAD model and fixtures.

```

tools/
whisper/
whisper-cli.exe
whisper-cli.VERSION.txt
fixtures/
whisper_sample_output_v1.json

models/
ggml-small.bin

```
vad/
  ggml-silero-v6.2.0.bin   # REQUIRED when VAD enabled
```

templates/
obsidian_note.md

prompts/
summary_system.txt
summary_user.txt
summary_repair_system.txt

````

---

# 3. Audio Capture and Synchronization

## 3.1 Startup Offset Problem

WASAPI mic and loopback start asynchronously.

### Requirement (NEW)

At recording start capture:

```csharp
record RecordingSyncMetadata(
    DateTime RecordingStartUtc,
    long MicFirstSampleStopwatchTicks,
    long LoopbackFirstSampleStopwatchTicks,
    double InitialOffsetMs
);
````

Where:

```
InitialOffsetMs =
    (LoopbackFirstSampleTicks - MicFirstSampleTicks)
    / Stopwatch.Frequency * 1000
```

## 3.2 Offset Correction in Merge (MANDATORY)

During transcript merge:

```
if InitialOffsetMs > 0:
    shift loopback timestamps backward
else:
    shift mic timestamps backward
```

Clamp to ≥ 0.

⚠️ NOTE: This corrects **startup skew only**, not long-term drift (explicit v1 limitation).

---

# 4. Temporary Files Policy (Unchanged but Explicit)

Temp root:

```
%TEMP%\MeetingScribe\<runId>\
```

Startup cleanup:

* delete runs older than `tempRetentionDays` (default 7)
* UI button: **Clean temp files**

---

# 5. whisper.cpp Integration (Pinned + VAD Enabled)

## 5.1 Pinned Build Requirement (CRITICAL)

The distributed build MUST support:

* `--output-json`
* `--vad`
* `--vad-model`
* progress output to stdout

Record in:

```
whisper/whisper-cli.VERSION.txt
```

Example:

```
whisper.cpp commit: <HASH>
vad support: enabled
json format: segments[].start/end (seconds)
```

---

## 5.2 Expected JSON Format (UNCHANGED)

Parser must expect:

```json
{
  "segments": [
    {
      "start": 0.00,
      "end": 4.32,
      "text": " hello"
    }
  ]
}
```

Backward tolerance:

* support `t0` / `t1` in centiseconds

Primary path remains pinned format.

---

# 6. Native VAD Support (NEW MAJOR FEATURE)

## 6.1 Design Decision

**Use whisper.cpp built-in VAD**

DO NOT implement external VAD in .NET.

Rationale:

* faster pipeline
* less duplicated logic
* better segmentation quality
* lower CPU time on silence

---

## 6.2 settings.json — Whisper Section (UPDATED)

```json
"whisper": {
  "language": "en",
  "threads": 8,

  "useVad": true,
  "vadModelPath": "models/vad/ggml-silero-v6.2.0.bin",
  "vadThreshold": 0.5,
  "vadMinSpeechMs": 250,
  "vadMinSilenceMs": 100,
  "vadMaxSpeechSec": 30,
  "vadSpeechPadMs": 100,
  "vadSamplesOverlapSec": 0.10,

  "extraArgs": ""
}
```

---

## 6.3 Whisper Command Construction (REQUIRED)

Base command:

```
whisper-cli.exe
  -m "<model>"
  -f "<wav>"
  -l en
  --output-json
  --output-file "<outBase>"
```

When `useVad = true`, append:

```
--vad
--vad-model "<vadModelPath>"
--vad-thold <vadThreshold>
--vad-min-speech-duration-ms <vadMinSpeechMs>
--vad-min-silence-duration-ms <vadMinSilenceMs>
--vad-max-speech-duration-s <vadMaxSpeechSec>
--vad-speech-pad-ms <vadSpeechPadMs>
--vad-samples-overlap <vadSamplesOverlapSec>
```

⚠️ IMPORTANT

Exact flag names MUST match the pinned build.

Codex must implement a **WhisperCommandBuilder** that:

* builds arguments deterministically
* only adds VAD flags when enabled
* logs full command line for debugging

---

## 6.4 VAD Model Validation (NEW)

Before transcription:

* verify `vadModelPath` exists when `useVad=true`
* if missing:

  * show error
  * offer “disable VAD and continue”

---

# 7. Whisper Progress Reporting (NEW UX)

whisper.cpp prints progress to stdout.

## Requirement

Implement best-effort progress parsing.

Pattern (example — adjust to pinned build):

```
progress = XX%
```

Behavior:

* if parsing succeeds → update ProgressBar
* else → show indeterminate progress

Must never block pipeline.

---

# 8. Transcript Merge Improvements

## 8.1 Overlap Detection (NEW)

During merge compute:

```csharp
bool IsOverlap
```

Definition:

Segments overlap if time ranges intersect.

Store in output JSON.

## 8.2 Optional Debounce (Disabled by Default)

Future-ready setting:

```
"merge": {
  "dropShortOverlaps": false,
  "overlapDropThresholdSec": 0.6
}
```

v1 behavior:

* DO NOT drop segments
* only mark overlaps

---

# 9. LM Studio Robustness (Enhanced)

## 9.1 Startup Retry (REQUIRED)

Distinguish:

* connection failure
* HTTP 503/500
* timeout

Retry policy:

```
for attempt in 1..startupRetryCount:
    try request
    if success → break
    else wait startupRetryDelaySec
```

---

## 9.2 Raw Output Preservation (NEW — MUST)

Always save:

```
<llm_run_dir>/llm_raw_output.txt
```

Even on success.

If JSON validation fails after retries:

* export transcript-only note
* include raw output in appendix
* log warning

---

## 9.3 JSON Validation (REQUIRED)

After LLM response:

1. Parse JSON
2. Validate required fields
3. If invalid → repair prompt
4. If still invalid → fallback mode

Unit tests required.

---

# 10. Chunking Rules (UNCHANGED BUT RECONFIRMED)

Threshold:

```
maxInputCharsPerChunk = 12000
overlap = 500
```

Hierarchical merge rules remain deterministic.

---

# 11. Cancellation Support (REQUIRED)

All long operations accept `CancellationToken`.

Cancel behavior:

### During recording

* stop capture
* finalize WAV

### During whisper

* kill process tree

### During LM Studio

* cancel HTTP request

State machine includes:

* Idle
* Recording
* Processing
* Cancelling
* Done
* Error

---

# 12. User Experience Improvements

## 12.1 Loopback Warning (NEW)

In Settings UI show:

> Loopback capture records ALL system audio from the selected playback device.

---

## 12.2 Stop Confirmation (NEW — REQUIRED)

After Stop show confirmation dialog (see Section 1).

---

## 12.3 Progress UI

Pipeline stage labels:

* Transcribing (Mic)
* Transcribing (Loopback)
* Merging transcript
* Generating summary
* Exporting note

Each stage visible in UI.

---

# 13. Build and Distribution (Unchanged)

* Windows x64
* self-contained
* portable folder zip
* no installer

---

# 14. Updated Acceptance Criteria

The application is accepted when:

1. Portable run without admin.
2. Startup offset between tracks is measured and corrected.
3. whisper.cpp runs with native VAD when enabled.
4. Missing VAD model is detected gracefully.
5. Progress bar updates when whisper prints progress.
6. LM Studio failures never lose data (raw output saved).
7. Cancel works during any processing stage.
8. Stop confirmation prevents accidental long processing.
9. Temp folders auto-clean after retention period.
10. Overlapping speech segments are detected and flagged.

---

# 15. Explicit v1 Limitations

Document clearly:

* No long-term drift correction (only startup offset)
* No multi-speaker diarization
* Loopback captures all system audio
* VAD quality depends on whisper.cpp model

---

# 16. Future Enhancements (v1.5+)

* true drift correction
* better overlap heuristics
* optional stereo capture path
* GPU structured configuration
* advanced diarization
