import importlib.util
from pathlib import Path
import sys
import tempfile
import types
import unittest
from unittest import mock


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
        wav_file.writeframes(b"\0\0")


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
            self.assertTrue(play_wav.call_args.args[0].startswith(b"RIFF"))


if __name__ == "__main__":
    unittest.main()
