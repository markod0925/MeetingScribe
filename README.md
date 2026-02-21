# MeetingScribe

Applicazione desktop **Windows portable** (WPF/.NET 8) per:
- registrazione dual-track (microfono + loopback sistema)
- trascrizione offline con `whisper.cpp`
- sintesi meeting via LM Studio locale
- export in Markdown compatibile Obsidian

## Requisiti software

Installa sul PC Windows 10/11 x64:

1. **.NET 8 SDK** (per compilare) oppure Runtime incluso se usi publish self-contained.
2. **Visual Studio 2022** (opzionale ma consigliato) con workload `.NET desktop development`.
3. **LM Studio** avviato in locale con API compatibile OpenAI (`/v1/chat/completions`).
4. **whisper.cpp** build con `whisper-cli.exe` e supporto VAD.
5. Modelli:
   - modello whisper (es. `models/ggml-base.bin`)
   - modello VAD silero (`models/vad/ggml-silero-v*.bin`)

## Struttura attesa runtime

Posiziona nella cartella dell'app:

- `whisper/whisper-cli.exe`
- `whisper/whisper-cli.VERSION.txt`
- `models/...`
- `config/settings.json` (opzionale, viene creato/aggiornato)

## Compilazione

Da terminale nella root del repository:

```bash
dotnet restore MeetingScribe.sln
dotnet build MeetingScribe.sln -c Release
```

## Test

```bash
dotnet test src/MeetingScribe.Tests/MeetingScribe.Tests.csproj -c Release
```

## Publish portable (consigliato)

```bash
dotnet publish src/MeetingScribe.App/MeetingScribe.App.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=false
```

L'output sarà in:

`src/MeetingScribe.App/bin/Release/net8.0-windows/win-x64/publish/`

Copia in questa cartella anche `whisper/` e `models/`.

## Come avviarlo ("play")

1. Avvia **LM Studio** e carica un modello chat.
2. Verifica che endpoint locale sia raggiungibile (default: `http://127.0.0.1:1234/v1`).
3. Avvia `MeetingScribe.App.exe`.
4. Premi **Start** per iniziare la registrazione.
5. Partecipa alla riunione (Webex o altra):
   - microfono = "You"
   - loopback sistema = "Others"
6. Premi **Stop** a fine meeting e conferma l'elaborazione.
7. Attendi pipeline: trascrizione → merge → summary → export.
8. Apri il file markdown generato nella cartella run temp (o output configurato) e importalo in Obsidian.

## Note operative

- Il loopback cattura **tutto** l'audio del sistema.
- Se modello VAD manca e VAD è attivo, la pipeline segnala errore: disabilita VAD o configura percorso corretto.
- Se la risposta LLM non è JSON valido, viene salvato sempre `llm_raw_output.txt` e viene esportata almeno la trascrizione.
