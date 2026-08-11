import importlib.util
import io
from pathlib import Path
import sys
import tempfile
import types
import unittest
from unittest import mock
import wave


HELPER_PATH = Path(__file__).with_name("drag-speech-helper.py")
SPEC = importlib.util.spec_from_file_location("drag_speech_helper", HELPER_PATH)
assert SPEC is not None and SPEC.loader is not None
HELPER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(HELPER)


class EspeakSpeechEngineTests(unittest.TestCase):
    def test_voice_and_text_are_passed_as_separate_arguments(self):
        with mock.patch.object(HELPER.shutil, "which", return_value="/usr/bin/espeak-ng"):
            engine = HELPER.EspeakSpeechEngine("espeak-ng")

        with mock.patch.object(HELPER.subprocess, "run") as run:
            engine.speak("Track ready", "en-us")

        self.assertEqual(
            ["/usr/bin/espeak-ng", "-v", "en-us", "Track ready"],
            run.call_args.args[0],
        )


class FakeSynthesisConfig:
    pass


class FakeVoice:
    loaded_models = []

    @classmethod
    def load(cls, model_path):
        cls.loaded_models.append(Path(model_path).name)
        return cls()

    def synthesize_wav(self, text, wav_file, syn_config):
        wav_file.setnchannels(1)
        wav_file.setsampwidth(2)
        wav_file.setframerate(22050)
        wav_file.writeframes(b"\x01\x02")


class PiperSpeechEngineTests(unittest.TestCase):
    def setUp(self):
        FakeVoice.loaded_models = []
        self.piper_module = types.ModuleType("piper")
        self.piper_module.PiperVoice = FakeVoice
        self.piper_module.SynthesisConfig = FakeSynthesisConfig
        self.module_patch = mock.patch.dict(sys.modules, {"piper": self.piper_module})
        self.module_patch.start()

    def tearDown(self):
        self.module_patch.stop()

    def test_first_model_is_preloaded_and_cached(self):
        with tempfile.TemporaryDirectory() as directory:
            voice_directory = Path(directory)
            for name in ("voice-b", "voice-a"):
                (voice_directory / f"{name}.onnx").touch()
                (voice_directory / f"{name}.onnx.json").touch()

            engine = HELPER.PiperSpeechEngine(directory)
            self.assertEqual(["voice-a", "voice-b"], engine.voices())
            self.assertEqual(["voice-a.onnx"], FakeVoice.loaded_models)

            engine.warm_up("voice-a")
            self.assertEqual(["voice-a.onnx"], FakeVoice.loaded_models)

            engine.warm_up("voice-b")
            self.assertEqual(
                ["voice-a.onnx", "voice-b.onnx"],
                FakeVoice.loaded_models,
            )

    def test_speak_reuses_loaded_model_and_produces_wav(self):
        with tempfile.TemporaryDirectory() as directory:
            voice_directory = Path(directory)
            (voice_directory / "voice-a.onnx").touch()
            (voice_directory / "voice-a.onnx.json").touch()
            engine = HELPER.PiperSpeechEngine(directory)

            with mock.patch.object(engine, "_play_wav") as play_wav:
                engine.speak("Lane one ready", "voice-a")

            self.assertEqual(["voice-a.onnx"], FakeVoice.loaded_models)
            audio = play_wav.call_args.args[0]
            self.assertTrue(audio.startswith(b"RIFF"))
            with wave.open(io.BytesIO(audio), "rb") as wav_file:
                frames = wav_file.readframes(wav_file.getnframes())
                expected_silence_bytes = round(
                    wav_file.getframerate() * HELPER.PIPER_LEADING_SILENCE_MS / 1000
                ) * wav_file.getnchannels() * wav_file.getsampwidth()
            self.assertEqual(bytes(expected_silence_bytes), frames[:expected_silence_bytes])
            self.assertEqual(b"\x01\x02", frames[expected_silence_bytes:])


if __name__ == "__main__":
    unittest.main()
