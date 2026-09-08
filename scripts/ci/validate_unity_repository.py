#!/usr/bin/env python3
"""Validate that a Unity checkout is complete and reproducible from Git."""

from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
from collections import defaultdict
from pathlib import Path


RELEASE_ROOTS = (
    "Assets/",
    "Packages/",
    "ProjectSettings/",
    ".github/",
    "docs/",
    "scripts/",
)
GUID_PATTERN = re.compile(r"^guid:\s*([0-9a-fA-F]{32})\s*$", re.MULTILINE)


def run_git(root: Path, *args: str, check: bool = True) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        ["git", *args],
        cwd=root,
        check=check,
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
    )


def normalize(path: str | Path) -> str:
    normalized = str(path).replace("\\", "/")
    while normalized.startswith("./"):
        normalized = normalized[2:]
    return normalized.lstrip("/")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--require-clean",
        action="store_true",
        help="Also reject every staged, modified, deleted, or untracked file.",
    )
    parser.add_argument(
        "--skip-lfs-fsck",
        action="store_true",
        help="Skip the local Git LFS object integrity check.",
    )
    return parser.parse_args()


def ignored_paths(root: Path, paths: list[str]) -> set[str]:
    if not paths:
        return set()
    process = subprocess.run(
        ["git", "check-ignore", "--stdin"],
        cwd=root,
        input="\n".join(paths) + "\n",
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
    )
    if process.returncode not in (0, 1):
        raise RuntimeError(process.stderr.strip() or "git check-ignore failed")
    return {normalize(line) for line in process.stdout.splitlines() if line.strip()}


def lfs_attributes(root: Path, paths: list[str]) -> dict[str, str]:
    if not paths:
        return {}
    process = subprocess.run(
        ["git", "check-attr", "--stdin", "filter"],
        cwd=root,
        input="\n".join(paths) + "\n",
        check=True,
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
    )
    attributes: dict[str, str] = {}
    for line in process.stdout.splitlines():
        parts = line.rsplit(": ", 2)
        if len(parts) == 3:
            attributes[normalize(parts[0])] = parts[2].strip()
    return attributes


def main() -> int:
    args = parse_args()
    root_result = run_git(Path.cwd(), "rev-parse", "--show-toplevel")
    root = Path(root_result.stdout.strip()).resolve()
    errors: list[str] = []
    warnings: list[str] = []

    required = (
        "Assets",
        "Packages/manifest.json",
        "Packages/packages-lock.json",
        "ProjectSettings/ProjectVersion.txt",
        "ProjectSettings/EditorBuildSettings.asset",
        ".gitattributes",
        ".gitignore",
    )
    for relative in required:
        if not (root / relative).exists():
            errors.append(f"필수 Unity/Git 파일 없음: {relative}")

    tracked = {
        normalize(path)
        for path in run_git(
            root, "-c", "core.quotepath=false", "ls-files"
        ).stdout.splitlines()
        if path.strip()
    }
    status_lines = [
        line for line in run_git(
            root,
            "-c",
            "core.quotepath=false",
            "status",
            "--porcelain=v1",
            "--untracked-files=all",
        ).stdout.splitlines()
        if line
    ]
    untracked = [normalize(line[3:]) for line in status_lines if line.startswith("?? ")]
    release_untracked = [
        path for path in untracked if path.startswith(RELEASE_ROOTS)
    ]
    for path in release_untracked:
        errors.append(f"Git에 추가되지 않은 프로젝트 파일: {path}")
    if args.require_clean and status_lines:
        errors.append(
            f"작업 폴더가 깨끗하지 않음: staged/modified/deleted/untracked {len(status_lines)}건"
        )

    assets_root = root / "Assets"
    disk_entries: list[str] = []
    if assets_root.exists():
        for entry in assets_root.rglob("*"):
            disk_entries.append(normalize(entry.relative_to(root)))
    ignored = ignored_paths(root, disk_entries)

    asset_files: list[Path] = []
    asset_directories: list[Path] = []
    for relative in disk_entries:
        if relative in ignored:
            continue
        absolute = root / relative
        if absolute.is_dir():
            asset_directories.append(absolute)
        elif absolute.is_file():
            asset_files.append(absolute)

    for directory in asset_directories:
        relative = normalize(directory.relative_to(root))
        meta = Path(str(directory) + ".meta")
        if not meta.is_file():
            errors.append(f"폴더 .meta 누락: {relative}.meta")

    for asset in asset_files:
        relative = normalize(asset.relative_to(root))
        if relative.endswith(".meta") or asset.name == ".gitkeep":
            continue
        meta = Path(str(asset) + ".meta")
        if not meta.is_file():
            errors.append(f"에셋 .meta 누락: {relative}.meta")

    for meta in (path for path in asset_files if path.suffix == ".meta"):
        source = Path(str(meta)[:-5])
        if not source.exists():
            errors.append(f"원본 없는 .meta: {normalize(meta.relative_to(root))}")

    guid_owners: dict[str, list[str]] = defaultdict(list)
    for meta in (path for path in asset_files if path.suffix == ".meta"):
        text = meta.read_text(encoding="utf-8", errors="replace")
        match = GUID_PATTERN.search(text)
        if match:
            guid_owners[match.group(1).lower()].append(normalize(meta.relative_to(root)))
        else:
            errors.append(f"GUID 없는 .meta: {normalize(meta.relative_to(root))}")
    for guid, owners in guid_owners.items():
        if len(owners) > 1:
            errors.append(f"중복 Unity GUID {guid}: {', '.join(sorted(owners))}")

    case_owners: dict[str, list[str]] = defaultdict(list)
    for path in tracked:
        case_owners[path.casefold()].append(path)
    for owners in case_owners.values():
        if len(owners) > 1:
            errors.append(f"대소문자 충돌 경로: {', '.join(sorted(owners))}")

    for relative in required:
        if relative != "Assets" and relative not in tracked:
            errors.append(f"필수 파일이 Git 추적 대상이 아님: {relative}")

    manifest_path = root / "Packages/manifest.json"
    if manifest_path.is_file():
        try:
            manifest = json.loads(manifest_path.read_text(encoding="utf-8-sig"))
            for package, value in manifest.get("dependencies", {}).items():
                if isinstance(value, str) and value.lower().startswith("file:"):
                    errors.append(f"다른 PC에서 깨지는 로컬 Package 참조: {package}={value}")
        except (OSError, json.JSONDecodeError) as exc:
            errors.append(f"Packages/manifest.json 파싱 실패: {exc}")

    lock_path = root / "Packages/packages-lock.json"
    if lock_path.is_file():
        try:
            package_lock = json.loads(lock_path.read_text(encoding="utf-8-sig"))
            for package, data in package_lock.get("dependencies", {}).items():
                if isinstance(data, dict) and data.get("source") == "local":
                    errors.append(f"다른 PC에서 깨지는 local Package lock: {package}")
        except (OSError, json.JSONDecodeError) as exc:
            errors.append(f"Packages/packages-lock.json 파싱 실패: {exc}")

    build_settings = root / "ProjectSettings/EditorBuildSettings.asset"
    if build_settings.is_file():
        text = build_settings.read_text(encoding="utf-8", errors="replace")
        scene_paths = re.findall(r"^\s*path:\s*(Assets/.+?\.unity)\s*$", text, re.MULTILINE)
        if not scene_paths:
            errors.append("Build Settings에서 Scene을 찾지 못함")
        for scene in scene_paths:
            if not (root / scene).is_file():
                errors.append(f"Build Settings Scene 파일 없음: {scene}")
            if scene not in tracked:
                errors.append(f"Build Settings Scene이 Git에 없음: {scene}")
            if f"{scene}.meta" not in tracked:
                errors.append(f"Build Settings Scene .meta가 Git에 없음: {scene}.meta")

    tracked_files = [path for path in sorted(tracked) if (root / path).is_file()]
    attributes = lfs_attributes(root, tracked_files)
    for path in tracked_files:
        size = (root / path).stat().st_size
        if size > 50 * 1024 * 1024 and attributes.get(path) != "lfs":
            errors.append(f"50MB 초과인데 Git LFS가 아닌 파일: {path} ({size} bytes)")

    if not args.skip_lfs_fsck:
        lfs_version = run_git(root, "lfs", "version", check=False)
        if lfs_version.returncode != 0:
            errors.append("Git LFS가 설치되어 있지 않음")
        else:
            lfs_fsck = run_git(root, "lfs", "fsck", check=False)
            if lfs_fsck.returncode != 0:
                errors.append("Git LFS 무결성 검사 실패: " + lfs_fsck.stdout.strip())

    version_file = root / "ProjectSettings/ProjectVersion.txt"
    if version_file.is_file():
        version_match = re.search(
            r"^m_EditorVersion:\s*(.+?)\s*$",
            version_file.read_text(encoding="utf-8", errors="replace"),
            re.MULTILINE,
        )
        if version_match:
            print(f"Unity version: {version_match.group(1)}")
        else:
            warnings.append("ProjectVersion.txt에서 Unity 버전을 읽지 못함")

    for warning in warnings:
        print(f"::warning::{warning}")
    if errors:
        for error in errors:
            print(f"::error::{error}")
        print(f"Repository integrity failed: {len(errors)} issue(s)")
        return 1

    print(
        "Repository integrity passed: "
        f"{len(tracked)} tracked paths, {len(guid_owners)} Unity GUIDs, "
        f"{len(tracked_files)} checked files"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
