"""Export GameContent.Devices from current MRes files without starting Unity.

Reads package metadata and game/device.json only; Unity remains responsible for
full resource/shader/Lua validation. Keep the projection aligned with
DeviceDefinition and DeviceResourceCatalog (verified against Unity's snapshot).
"""
from pathlib import Path
import argparse
import json
import math
import struct

ASSETS = Path(__file__).resolve().parents[2]
RARITIES = {"common": 0, "rare": 1, "epic": 2, "legendary": 3}
# Serialized defaults in Scripts/Marbles/Rules/DeviceDefinition.cs.
DEFAULTS = dict(Id=0, Name=None, Price=0, SellPrice=0, Rarity=0, Durability=20,
                ScoreMultiplier=1.0, RushChanceAdd=0.0, Radius=0.18,
                StartingCount=0, PurchaseQuantity=1, RushCategory=False,
                LinkGroup="", GroupClearance=0.01, Kind=0, SourceId="",
                Description=None, Cooldown=0.1, MaxTriggersPerBall=0,
                Range=0.2, Strength=2.0, Value=1.0)
FLOAT_FIELDS = ("Radius", "GroupClearance", "Cooldown", "Range", "Strength")


def float32(value):
    if isinstance(value, bool) or not isinstance(value, (int, float)) or not math.isfinite(value):
        raise ValueError("Expected a finite number")
    return struct.unpack("<f", struct.pack("<f", value))[0]


def package_entries(path):
    if path.stat().st_size > 256 * 1024 * 1024:
        raise ValueError(f"{path}: package exceeds 256 MiB")
    data = path.read_bytes()
    if len(data) < 28:
        raise ValueError(f"{path}: truncated MRes header")
    magic, version, reserved, count, toc, size = struct.unpack_from("<4sHHIQQ", data)
    if (magic != b"MRES" or version != 1 or reserved != 0 or
            not 0 < count <= 65536 or toc < 28 or toc + size != len(data)):
        raise ValueError(f"{path}: invalid MRes header")
    cursor, entries, manifests, spans = toc, {}, [], []
    for _ in range(count):
        if cursor + 4 > len(data):
            raise ValueError(f"{path}: truncated TOC")
        length, = struct.unpack_from("<I", data, cursor)
        cursor += 4
        if not 0 < length <= 1024 or cursor + length + 20 > len(data):
            raise ValueError(f"{path}: invalid TOC path")
        name = data[cursor:cursor + length].decode("utf-8")
        cursor += length
        kind, compression, reserved, offset, length = struct.unpack_from("<BBHQQ", data, cursor)
        cursor += 20
        if compression or reserved or offset < 28 or offset + length > toc or name in entries:
            raise ValueError(f"{path}: invalid/duplicate entry {name}")
        entries[name] = (kind, data[offset:offset + length])
        if length:
            spans.append((offset, offset + length))
        if kind == 1:
            manifests.append(name)
    spans.sort()
    if (cursor != len(data) or len(manifests) != 1 or
            any(a[1] > b[0] for a, b in zip(spans, spans[1:]))):
        raise ValueError(f"{path}: invalid TOC/manifest")
    return entries, manifests[0]


def read_device(path):
    entries, manifest_path = package_entries(path)

    def entry(name, kind):
        actual_kind, data = entries[name]
        if actual_kind != kind:
            raise ValueError(f"{path}: wrong entry type for {name}")
        return data

    manifest = json.loads(entry(manifest_path, 1).decode("utf-8-sig"))
    if manifest["format"] != "mres" or manifest["version"] != 1:
        raise ValueError(f"{path}: invalid manifest")
    metadata = json.loads(entry(manifest["metadata"], 7).decode("utf-8-sig"))
    if metadata["type"] != "device" or not entry(manifest["lua"], 6).strip():
        raise ValueError(f"{path}: missing device metadata/Lua")
    raw = json.loads(entry("game/device.json", 255).decode("utf-8-sig"))
    device = {key: raw.get(key, value) for key, value in DEFAULTS.items()}
    device.update(Name=metadata["name"], Description=metadata["description"],
                  Price=float32(metadata.get("purchasePrice", 5)), SellPrice=float32(metadata["value"]),
                  Rarity=RARITIES[metadata.get("rarity", "common")],
                  Durability=metadata.get("durability", 20))
    for key, minimum, maximum in (("Id", 1, 2**32-1), ("Price", 1, 9_000_000_000_000_000),
                                   ("SellPrice", 0, 9_000_000_000_000_000),
                                   ("StartingCount", 0, 200), ("PurchaseQuantity", 1, 20)):
        value = device[key]
        if isinstance(value, bool) or not isinstance(value, (int, float)) or not math.isfinite(value) or value != int(value) or not minimum <= value <= maximum:
            raise ValueError(f"{path}: invalid {key}")
        device[key] = int(value)
    durability = device["Durability"]
    if isinstance(durability, bool) or not isinstance(durability, (int, float)) or not math.isfinite(durability) or durability != int(durability) or (durability != -1 and not 1 <= durability <= 10000):
        raise ValueError(f"{path}: invalid durability")
    device["Durability"] = int(durability)
    for key in ("LinkGroup", "SourceId"):
        if device[key] is None:
            device[key] = ""  # JsonUtility serializes null string fields as empty strings.
    if not isinstance(device["Name"], str) or not device["Name"].strip() or not isinstance(device["Description"], str) or not device["Description"].strip():
        raise ValueError(f"{path}: missing name/description")
    # Match JsonUtility's single-precision field values, avoiding repeated rewrites.
    for key in FLOAT_FIELDS:
        device[key] = float32(device[key])
    json.dumps(device, allow_nan=False)
    return device


def read_devices(folder):
    devices = {}
    for path in sorted(folder.rglob("*")):
        if not path.is_file() or path.suffix.lower() != ".mres":
            continue
        device = read_device(path)
        if device["Id"] in devices:
            raise ValueError(f"{path}: duplicate device ID {device['Id']}")
        devices[device["Id"]] = device
    if not devices:
        raise ValueError(f"{folder}: no device packages")
    return [devices[key] for key in sorted(devices)]


def synchronize(assets=ASSETS, check=False):
    path = assets / "Resources/GameContent.json"
    content = json.loads(path.read_text(encoding="utf-8-sig"))
    devices = read_devices(assets / "Resources/Devices")
    if content.get("Devices") == devices:
        return False
    if check:
        raise ValueError("GameContent.Devices is outdated; run sync_mres_devices.py or Tools/MRes/Rebuild Device Catalog")
    content["Devices"] = devices
    path.write_text(json.dumps(content, ensure_ascii=False, indent=4, allow_nan=False) + "\n", encoding="utf-8")
    return True


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="Check without modifying files")
    args = parser.parse_args()
    changed = synchronize(check=args.check)
    print("Synchronized Devices from current MRes files." if changed else "Devices already match current MRes files.")
