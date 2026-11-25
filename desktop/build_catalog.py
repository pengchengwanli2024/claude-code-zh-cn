"""Build the desktop Chinese dictionary from a checked-out zh-ui-helper locale directory."""
import argparse
import gzip
import json
import re
from pathlib import Path


def build(locales):
    def read(name):
        return json.loads((locales / name).read_text(encoding="utf-8-sig"))

    source = read("source-fingerprints.json")
    if source["algorithm"] != "sha256" or source["schema"] != 1:
        raise ValueError("Unsupported source fingerprint format")
    result = {}
    for group, filename in [
        ("frontend", "frontend-zh-Hans.json"),
        ("dynamic", "dynamic-zh-Hans.json"),
        ("native", "native-additions-zh-Hans.json"),
    ]:
        for key, translation in read(filename).items():
            digest = source["catalogs"][group].get(key)
            if not digest or not re.fullmatch(r"[0-9a-f]{64}", digest):
                continue
            if not isinstance(translation, str) or not translation.strip():
                continue
            if any(char in translation for char in "{}<>\r\n"):
                continue
            result[digest] = translation
    return result


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("locales", type=Path)
    parser.add_argument("--output", type=Path, default=Path(__file__).with_name("labels.json.gz"))
    args = parser.parse_args()
    labels = build(args.locales)
    encoded = json.dumps(labels, ensure_ascii=False, sort_keys=True, separators=(",", ":")).encode()
    with args.output.open("wb") as dest:
        with gzip.GzipFile(filename="", mode="wb", fileobj=dest, mtime=0) as out:
            out.write(encoded)
    print(f"{len(labels)} labels")
