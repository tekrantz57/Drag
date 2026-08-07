# Linux Speech Under Wine

DragWin can use native Linux text-to-speech while the unchanged Windows
application runs under Wine. DragWin connects to a small helper on TCP
`127.0.0.1:38592`; the helper invokes `espeak-ng` and acknowledges each
announcement after playback finishes.

The helper listens only on IPv4 loopback. It does not accept remote network
connections, construct shell commands, or require root access. Drag uses a
different port from YATSS, so both helpers may run at the same time.

## Speech Engine Selection

The Announcements tab in Race and Track Settings offers these engines:

- `Automatic` uses Windows SAPI when at least one SAPI voice is installed. If
  SAPI has no voices, it tries the Linux helper.
- `Windows SAPI` uses only the existing Windows COM speech implementation.
- `Linux helper` uses only the loopback helper.
- `None` disables speech.

The separate `Enabled` checkbox remains the quick global on/off control.
`Automatic` is the default engine. Speech is optional and disabled by default.

## Fedora Setup

The release publish directory includes the helper at
`Linux/drag-speech-helper.py`.

1. Install Python and eSpeak NG:

   ```bash
   sudo dnf install python3 espeak-ng
   ```

2. Confirm that native speech reaches the desired audio output:

   ```bash
   espeak-ng "Drag strip speech test"
   ```

3. From the extracted DragWin publish directory, start the helper as the same
   desktop user who runs Wine:

   ```bash
   python3 Linux/drag-speech-helper.py
   ```

4. Start DragWin under Wine. Open Race and Track Settings, enable voice
   announcements, and leave the engine on `Automatic` or select
   `Linux helper`. The voice list should contain eSpeak language codes such as
   `en`, `en-gb`, and `en-us`.

Keep the helper terminal open while DragWin is running. Stop it with `Ctrl+C`.

## Optional User Service

After verifying manual operation, create
`~/.config/systemd/user/drag-speech-helper.service` and replace the example
script path with the absolute path to the extracted helper:

```ini
[Unit]
Description=DragWin Linux speech helper

[Service]
ExecStart=/usr/bin/python3 /absolute/path/to/DragWin/Linux/drag-speech-helper.py
Restart=on-failure

[Install]
WantedBy=default.target
```

Enable and start it:

```bash
systemctl --user daemon-reload
systemctl --user enable --now drag-speech-helper.service
systemctl --user status drag-speech-helper.service
```

## Failure Behavior

DragWin serializes speech requests on its existing background announcer thread.
If the selected engine or helper fails, DragWin continues silently. Controller
communication, race timing, results, and reports do not depend on speech.

If no Linux voices appear:

1. Verify that the helper says it is listening on `127.0.0.1:38592`.
2. Run `espeak-ng --voices` and confirm that it returns voices.
3. Change the selected engine away from `Linux helper` and back to refresh it.
4. Check the helper terminal for an eSpeak or audio-system error.

Only one Drag speech helper may listen on port `38592` at a time.
