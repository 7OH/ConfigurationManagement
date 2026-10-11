#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""РџСЂРѕРІРµСЂРєР° .deb 0.3.12.5: Р°СЂ-С‡Р»РµРЅС‹, РІРµСЂСЃРёСЏ РІ control, СЃРѕРѕС‚РІРµС‚СЃС‚РІРёРµ md5sums С„Р°Р№Р»Р°Рј data.tar.gz."""
import gzip
import hashlib
import io
import os
import tarfile

DEB = os.path.join("package", "linux", "deb", "out", "configuration-management_0.3.12.5_amd64.deb")
EXPECTED_VERSION = "0.3.12.5"


def parse_ar(data: bytes) -> dict:
    assert data[:8] == b"!<arch>\n", "РЅРµРІРµСЂРЅС‹Р№ ar-РјР°РіРёРє"
    i = 8
    members = {}
    while i + 60 <= len(data):
        name = data[i:i + 16].decode("ascii").strip()
        size = int(data[i + 48:i + 58].decode("ascii"))
        body = data[i + 60:i + 60 + size]
        members[name] = body
        i += 60 + size + (size % 2)
    return members


def main() -> None:
    with open(DEB, "rb") as fh:
        data = fh.read()
    members = parse_ar(data)
    print("ar-members:", list(members))

    # control.tar.gz -> control
    with tarfile.open(fileobj=io.BytesIO(gzip.decompress(members["control.tar.gz"]))) as tar:
        names = sorted(tar.getnames())
        print("control.tar.gz:", names)
        control = tar.extractfile("./control").read().decode("utf-8")
        version_line = next((ln for ln in control.splitlines() if ln.startswith("Version:")), None)
        print("control Version:", version_line)
        assert version_line == f"Version: {EXPECTED_VERSION}", "РІРµСЂСЃРёСЏ РЅРµ СЃРѕРІРїР°РґР°РµС‚!"
        md5sums = tar.extractfile("./md5sums").read().decode("utf-8")

    # data.tar.gz -> md5 РїРѕ СЃРѕРґРµСЂР¶РёРјРѕРјСѓ
    with tarfile.open(fileobj=io.BytesIO(gzip.decompress(members["data.tar.gz"]))) as tar:
        data_names = sorted(tar.getnames())
        print("data.tar.gz:", data_names)
        expected = {}
        for m in tar.getmembers():
            if not m.isfile():
                continue
            f = tar.extractfile(m)
            if f is None:
                continue
            path = m.name[2:] if m.name.startswith("./") else m.name
            payload = f.read()
            expected[path] = hashlib.md5(payload).hexdigest()
            if path == "usr/bin/ConfigurationManagement":
                print("binary mode in tar:", oct(m.mode))
                print("binary size:", len(payload))
                print("binary ELF magic:", payload[:4].hex(" "))
                assert payload[:4] == b"\x7fELF", "Р±РёРЅР°СЂСЊ РІ .deb РЅРµ ELF!"

    # md5sums-СЃС‚СЂРѕРєРё: "<md5>  <path>"
    actual = {}
    for line in md5sums.strip().splitlines():
        digest, path = line.split("  ", 1)
        actual[path] = digest
    print("md5sums entries:", len(actual), "data files:", len(expected))
    assert actual == expected, "md5sums РЅРµ СЃРѕРІРїР°РґР°СЋС‚ СЃ СЃРѕРґРµСЂР¶РёРјС‹Рј data.tar.gz!"
    print("OK: РєРѕРЅС‚СЂРѕР»СЊРЅР°СЏ СЃСѓРјРјР° Рё СЃРѕСЃС‚Р°РІ data.tar.gz СЃРѕРІРїР°РґР°СЋС‚ СЃ DEBIAN/md5sums")
    print("OK: СЃС‚СЂСѓРєС‚СѓСЂР° .deb РєРѕСЂСЂРµРєС‚РЅР°")


if __name__ == "__main__":
    main()
