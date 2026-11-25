"""Line-delimited JSON protocol for the local desktop label translator."""
import json
import sys

from offline import Translator, default_model


def main():
    sys.stdin.reconfigure(encoding="utf-8-sig")
    sys.stdout.reconfigure(encoding="utf-8")
    translator = Translator(default_model())
    for line in sys.stdin:
        labels = json.loads(line)
        translations = translator.batch(labels)
        print(json.dumps(translations, ensure_ascii=True), flush=True)


if __name__ == "__main__":
    main()
