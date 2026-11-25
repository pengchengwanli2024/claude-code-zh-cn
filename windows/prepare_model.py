"""Install the pinned Argos English–Chinese model in the user's local cache."""
import argparse
import hashlib
from pathlib import Path
import shutil
import tempfile
from urllib.request import urlopen
from zipfile import ZipFile

from offline import default_model

URL = "https://argos-net.com/v1/translate-en_zh-1_9.argosmodel"
DIGEST = "433e7c4f034d87fbe2353161e05f18646d7999452f801a4e1f0378522b9850ab"
PREFIX = "translate-en_zh-1_9/"
FILES = ("metadata.json", "README.md", "sentencepiece.model", "model/model.bin",
         "model/config.json", "model/shared_vocabulary.json")


def install(dest, archive=None):
    if all((dest / name).is_file() for name in FILES):
        return
    dest.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(dir=dest.parent) as temp:
        temp = Path(temp)
        if archive is None:
            archive = temp / "package.zip"
            with urlopen(URL, timeout=120) as source, archive.open("wb") as output:
                shutil.copyfileobj(source, output)
        with archive.open("rb") as stream:
            digest = hashlib.sha256()
            for chunk in iter(lambda: stream.read(1024 * 1024), b""):
                digest.update(chunk)
        if digest.hexdigest() != DIGEST:
            raise ValueError("模型文件 SHA-256 校验失败。")
        staging = temp / "ready"
        with ZipFile(archive) as package:
            for name in FILES:
                target = staging / name
                target.parent.mkdir(parents=True, exist_ok=True)
                with package.open(PREFIX + name) as source, target.open("wb") as output:
                    shutil.copyfileobj(source, output)
        try:
            staging.rename(dest)
        except OSError:
            if not all((dest / name).is_file() for name in FILES):
                raise


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="安装本地英译中模型（下载约 71 MB）")
    parser.add_argument("--archive", type=Path, help="使用已下载的 .argosmodel 文件")
    parser.add_argument("--dest", type=Path, default=default_model())
    args = parser.parse_args()
    install(args.dest, args.archive)
    print("模型已就绪", args.dest)
