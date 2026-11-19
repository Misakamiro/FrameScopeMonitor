"""Restore a verified dependency archive into new, isolated build caches."""
import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import subprocess
import xml.etree.ElementTree as ET
import zipfile


def file_hash(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def relative_path(name):
    if (not isinstance(name, str) or not name or "\\" in name or ":" in name
            or any(ord(c) < 32 for c in name)):
        raise ValueError("Invalid dependency path")
    parts = name.split("/")
    if any(p in ("", ".", "..") or p.endswith((" ", ".")) or
           re.fullmatch(r"(?i)(con|prn|aux|nul|com[1-9]|lpt[1-9])(?:\..*)?", p) for p in parts):
        raise ValueError("Invalid dependency path: " + name)
    return PurePosixPath(name)


def restore(args):
    root, destination = args.root.resolve(), args.destination.absolute()
    if destination.exists() and not args.resume:
        raise ValueError("Dependency destination must be new")
    for parent in (destination, *destination.parents):
        if parent.exists() and (parent.is_symlink() or parent.is_junction()):
            raise ValueError("Dependency destination contains a reparse point")
    if file_hash(args.archive).lower() != args.sha256.lower():
        raise ValueError("Dependency archive SHA-256 mismatch")
    with zipfile.ZipFile(args.archive) as archive:
        manifest_bytes = archive.read("DEPENDENCY_MANIFEST.json")
        manifest = json.loads(manifest_bytes)
        if destination.exists() and (destination / "DEPENDENCY_MANIFEST.json").read_bytes() != manifest_bytes:
            raise ValueError("Resume destination has a different dependency manifest")
        if manifest.get("schema") != 1 or manifest.get("platform") != "windows-x64" or not manifest.get("complete"):
            raise ValueError("Unsupported or incomplete dependency manifest")
        for lock in manifest["locks"]:
            name = relative_path(lock["path"])
            if file_hash(root / name) != lock["sha256"]:
                raise ValueError("Source lock differs from dependency archive: " + str(name))
        entries = {entry["path"]: entry for entry in manifest["files"]}
        if len(entries) != len(manifest["files"]):
            raise ValueError("Duplicate dependency manifest entry")
        expected = set(entries) | {"DEPENDENCY_MANIFEST.json"}
        actual = archive.namelist()
        if set(actual) != expected or len(actual) != len(expected) or len({n.casefold() for n in actual}) != len(actual):
            raise ValueError("Archive and manifest file sets differ")
        for name, entry in entries.items():
            relative_path(name)
            if not name.startswith(("cargo/registry/", "npm/", "nuget-feed/")):
                raise ValueError("Unexpected dependency kind: " + name)
            if archive.getinfo(name).file_size != entry["length"]:
                raise ValueError("Dependency length mismatch: " + name)
        destination.mkdir(parents=True, exist_ok=args.resume)
        for name in actual:
            path = destination / relative_path(name)
            path.parent.mkdir(parents=True, exist_ok=True)
            if not path.exists():
                with archive.open(name) as source, path.open("xb") as target:
                    shutil.copyfileobj(source, target, 1024 * 1024)
            elif path.is_symlink() or path.is_junction():
                raise ValueError("Resume dependency is a reparse point: " + name)
            if name in entries and file_hash(path) != entries[name]["sha256"]:
                raise ValueError("Dependency SHA-256 mismatch: " + name)

    npm_module = args.npm_module
    if npm_module is None:
        npm = shutil.which("npm.cmd" if os.name == "nt" else "npm")
        if npm is None:
            raise ValueError("npm is required to restore its cache")
        npm_module = (Path(npm).parent / "node_modules/npm" if os.name == "nt"
                      else Path(npm).resolve().parents[1])
    restored = subprocess.run(
        [args.node, str(Path(__file__).with_name("offline-npm-cache.cjs")),
         "restore", str(npm_module), str(destination / "npm-cache/_cacache"), str(destination)],
        input=json.dumps(manifest["npmPackages"]), text=True, capture_output=True)
    if restored.returncode:
        raise RuntimeError("npm cache restore failed: " + restored.stderr.strip())
    (destination / "cargo/config.toml").write_text(
        '[net]\noffline = true\n\n[registries.crates-io]\nprotocol = "sparse"\n', encoding="utf-8")
    configuration = ET.Element("configuration")
    sources = ET.SubElement(configuration, "packageSources")
    ET.SubElement(sources, "clear")
    ET.SubElement(sources, "add", key="FrameScopeOffline", value=str(destination / "nuget-feed"))
    ET.SubElement(ET.SubElement(configuration, "fallbackPackageFolders"), "clear")
    config = ET.SubElement(configuration, "config")
    ET.SubElement(config, "add", key="globalPackagesFolder", value=str(destination / "nuget-packages"))
    ET.ElementTree(configuration).write(destination / "NuGet.Config", encoding="utf-8", xml_declaration=True)
    environment = {
        "CARGO_HOME": str(destination / "cargo"),
        "CARGO_NET_OFFLINE": "true",
        "npm_config_cache": str(destination / "npm-cache"),
        "npm_config_offline": "true",
        "npm_config_audit": "false",
        "npm_config_fund": "false",
        "NUGET_PACKAGES": str(destination / "nuget-packages"),
        "NUGET_HTTP_CACHE_PATH": str(destination / "nuget-http"),
        "NUGET_PLUGINS_CACHE_PATH": str(destination / "nuget-plugins"),
        "RestoreSources": str(destination / "nuget-feed"),
        "NuGetAudit": "false",
        "FRAMESCOPE_NUGET_SOURCE": str(destination / "nuget-feed"),
        "FRAMESCOPE_AMD_AUTOMATION": "1",
    }
    (destination / "environment.ps1").write_text(
        "\n".join("$env:" + name + "='" + value.replace("'", "''") + "'" for name, value in environment.items()) + "\n",
        encoding="utf-8")
    result = {
        "schema": 1, "archiveSha256": args.sha256.lower(), "filesVerified": len(entries),
        "lockFilesVerified": len(manifest["locks"]), "npmPackagesRestored": len(manifest["npmPackages"]),
        "destination": str(destination), "independentBuildVerified": False,
    }
    (destination / "restore-result.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    return result


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--archive", type=Path, required=True)
    parser.add_argument("--sha256", required=True)
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--destination", type=Path, required=True)
    parser.add_argument("--node", default="node")
    parser.add_argument("--npm-module", type=Path)
    parser.add_argument("--resume", action="store_true")
    print(json.dumps(restore(parser.parse_args())))
