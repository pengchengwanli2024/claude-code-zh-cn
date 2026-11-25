"""Optional offline English-to-Chinese translation for previously unseen screen text."""
from pathlib import Path
import os


def default_model():
    base = Path(os.environ.get("LOCALAPPDATA", Path.home() / ".cache"))
    return base / "zh-ui" / "translate-en_zh-1_9"


class Translator:
    def __init__(self, directory):
        import ctranslate2
        import sentencepiece

        self.processor = sentencepiece.SentencePieceProcessor(
            model_file=str(directory / "sentencepiece.model"))
        self.engine = ctranslate2.Translator(str(directory / "model"), device="cpu",
                                             compute_type="int8", intra_threads=2)

    def batch(self, lines):
        tokens = self.processor.encode(list(lines), out_type=str)
        results = self.engine.translate_batch(tokens, beam_size=3, max_batch_size=16)
        return [self.processor.decode(row.hypotheses[0]).replace("▁", " ").strip() for row in results]
