"""Build a deterministic UnityPackage from the reviewable optional sample source."""
import gzip
import hashlib
import io
from pathlib import Path
import re
import tarfile

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "src/AIDrivenFramework/Samples~/AISetup"
OUTPUT = ROOT / "src/AIDrivenFramework/Editor/Packages/AIDriven_AISetup.unitypackage"


def build():
    entries = []
    for asset in sorted(SOURCE.rglob("*")):
        if asset.suffix == ".meta" or asset.name == "README.md":
            continue
        meta = Path(str(asset) + ".meta")
        if not meta.exists():
            raise RuntimeError(f"Missing Unity metadata: {asset.relative_to(SOURCE)}")
        guid = re.search(r"^guid: ([0-9a-f]{32})$", meta.read_text(), re.M).group(1)
        path = "Assets/AIDrivenFW/AISetup/" + asset.relative_to(SOURCE).as_posix()
        entries.append((guid + "/pathname", path.encode()))
        entries.append((guid + "/asset.meta", meta.read_bytes()))
        if asset.is_file():
            entries.append((guid + "/asset", asset.read_bytes()))
    buffer = io.BytesIO()
    with tarfile.open(fileobj=buffer, mode="w", format=tarfile.USTAR_FORMAT) as archive:
        for name, data in sorted(entries):
            item = tarfile.TarInfo(name)
            item.size = len(data)
            item.mode = 0o644
            item.mtime = 0
            archive.addfile(item, io.BytesIO(data))
    with OUTPUT.open("wb") as output:
        with gzip.GzipFile(filename="", fileobj=output, mode="wb", mtime=0) as compressed:
            compressed.write(buffer.getvalue())
    print(f"{len(entries)} entries; sha256 {hashlib.sha256(OUTPUT.read_bytes()).hexdigest()}")


if __name__ == "__main__":
    build()
