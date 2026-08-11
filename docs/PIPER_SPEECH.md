# Piper Speech

DragWin can use Piper for higher-quality local speech on native Windows or
while running under Wine. Piper runs as a persistent Python helper and keeps
its active voice model loaded between announcements. DragWin communicates with
it only over IPv4 loopback at `127.0.0.1:38593`.

Piper is optional. Windows SAPI, the Linux eSpeak NG helper, controller
communication, race timing, results, and reports remain independent of it.

## Windows Setup

Install Python 3.10 or newer, then install Piper:

```powershell
python -m pip install piper-tts
```

Download a voice into Drag's standard voice directory:

```powershell
$voiceDir = "$env:LOCALAPPDATA\Drag\PiperVoices"
New-Item -ItemType Directory -Force $voiceDir | Out-Null
python -m piper.download_voices --download-dir $voiceDir en_US-lessac-medium
```

Start DragWin and select `Piper` on the Announcements tab in Race and Track
Settings. DragWin starts the packaged helper with the `python` command,
discovers models in the voice directory, and lists their model names in the
voice selector. Set `DRAG_PYTHON` before starting DragWin if Piper's Python
interpreter is installed under a different command or full path.

No separate helper terminal is required on native Windows. DragWin stops the
helper process during a normal application exit. It also loads the selected
model silently on its background speech thread during startup and after speech
settings change, avoiding a delayed first announcement.

## Linux And Wine Setup

Install Piper in the native Linux environment, not inside Wine. A dedicated
virtual environment works on distributions that reject `pip --user`:

```bash
python3 -m venv "$HOME/.local/share/Drag/piper-venv"
"$HOME/.local/share/Drag/piper-venv/bin/pip" install piper-tts
voice_dir="${XDG_DATA_HOME:-$HOME/.local/share}/Drag/PiperVoices"
mkdir -p "$voice_dir"
"$HOME/.local/share/Drag/piper-venv/bin/python" -m piper.download_voices \
  --download-dir "$voice_dir" en_US-lessac-medium
"$HOME/.local/share/Drag/piper-venv/bin/python" \
  Linux/drag-speech-helper.py --engine piper --port 38593 --data-dir "$voice_dir"
```

If Piper was installed with `pipx install piper-tts`, run the helper with the
interpreter inside pipx's isolated environment:

```bash
PIPX_VENVS="$(pipx environment --value PIPX_LOCAL_VENVS)"
"$PIPX_VENVS/piper-tts/bin/python" Linux/drag-speech-helper.py \
  --engine piper \
  --port 38593 \
  --data-dir "${XDG_DATA_HOME:-$HOME/.local/share}/Drag/PiperVoices"
```

The system `python3` cannot import Piper after a pipx installation because
pipx intentionally isolates each package.

Leave the helper running, start DragWin under Wine, and select `Piper`. The
helper loads its first available model before reporting that it is listening.
Wait for that listening message before starting DragWin so the first
announcement cannot be consumed by ONNX model startup.

For unattended startup, create a systemd user service using the resolved
virtual-environment Python path and the same helper arguments. Run the service
as the desktop user so generated audio reaches that user's audio session.

## Voice Directories

The defaults are:

```text
Windows: %LOCALAPPDATA%\Drag\PiperVoices
Linux:   ${XDG_DATA_HOME:-$HOME/.local/share}/Drag/PiperVoices
```

Set `DRAG_PIPER_VOICE_DIR` before starting DragWin or the helper to override
the default. Each voice requires both its `.onnx` model and adjacent
`.onnx.json` configuration file.

Piper's engine is GPL-3.0-or-later and remains a separately installed program;
it is not incorporated into the MIT-licensed DragWin executable. Voice models
have individual licenses. Review a model's `MODEL_CARD` before redistributing
it. Drag does not package or redistribute Piper or voice models.

## Engine Selection

- `Automatic` prefers a usable Windows SAPI voice, then Piper, then eSpeak NG.
- `Windows SAPI` uses only SAPI.
- `Piper` uses only the Piper helper on port `38593`.
- `eSpeak NG helper` uses only Drag's helper on port `38594`.
- `None` disables speech.

If no Piper voices appear, verify the model directory contains both required
files, switch away from `Piper` and back to refresh discovery, and test the
helper manually. Under Wine, also verify that the native helper reports it is
listening on port `38593`.

## Optional Live Test

After installing Piper and a voice, run Drag's normal protocol test project
with the live test enabled:

```powershell
$env:DRAG_TEST_PIPER = "1"
$env:DRAG_TEST_PIPER_VOICE = "en_US-lessac-medium" # optional
dotnet run --project dragWin\dragWin.ProtocolTests\dragWin.ProtocolTests.csproj -c Release
```

The test discovers the helper, warms the selected model, and speaks one short
phrase. It is opt-in so normal builds do not require Piper or audio hardware.
