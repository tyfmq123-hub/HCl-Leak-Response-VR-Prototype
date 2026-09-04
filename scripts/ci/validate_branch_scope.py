#!/usr/bin/env python3
"""Reject Pull Requests that use the wrong branch or cross Stage ownership."""

from __future__ import annotations

import argparse
import subprocess
import sys
from dataclasses import dataclass


@dataclass(frozen=True)
class StageRule:
    branch: str
    roots: tuple[str, ...]
    exact_files: tuple[str, ...]
    handoff: str


STAGES = (
    StageRule(
        branch="feature/ppe",
        roots=("Assets/1.JSR/",),
        exact_files=(
            "Assets/Scenes/04_PPE.unity",
            "Assets/Scenes/04_PPE.unity.meta",
        ),
        handoff="docs/handoffs/ppe/LATEST.md",
    ),
    StageRule(
        branch="feature/preentry",
        roots=("Assets/2.YJH/",),
        exact_files=(
            "Assets/Scenes/05_PreEntry.unity",
            "Assets/Scenes/05_PreEntry.unity.meta",
        ),
        handoff="docs/handoffs/preentry/LATEST.md",
    ),
    StageRule(
        branch="feature/leakresponse",
        roots=("Assets/3.SSH/",),
        exact_files=(
            "Assets/Scenes/06_LeakResponse.unity",
            "Assets/Scenes/06_LeakResponse.unity.meta",
        ),
        handoff="docs/handoffs/leakresponse/LATEST.md",
    ),
)

PROTECTED_ROOTS = (
    "Assets/Scripts/App/",
    "Assets/Resources/App/",
    "Assets/Common/XR/",
    "Assets/Common/InteractionGhosts/",
    "Assets/Editor/AppIntegrationBuilder.cs",
    "Assets/Editor/AppPlayModeSmokeRunner.cs",
    "Assets/Editor/IntegrationSceneValidator.cs",
    "Assets/VRHandPoseToolOutput/",
    "ProjectSettings/",
    "Packages/",
    ".github/",
    "scripts/ci/",
)

PROTECTED_EXACT_FILES = (
    "Assets/Scenes/01_App.unity",
    "Assets/Scenes/01_App.unity.meta",
    "Assets/Scenes/02_Title.unity",
    "Assets/Scenes/02_Title.unity.meta",
    "Assets/Scenes/03_Start.unity",
    "Assets/Scenes/03_Start.unity.meta",
    "Assets/Scenes/07_Complete.unity",
    "Assets/Scenes/07_Complete.unity.meta",
)

PROTECTED_SUFFIXES = (
    ".asmdef",
    ".asmdef.meta",
    ".asmref",
    ".asmref.meta",
)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--base", required=True)
    parser.add_argument("--head", required=True)
    parser.add_argument("--base-sha")
    parser.add_argument("--head-sha")
    parser.add_argument(
        "--changed-file",
        action="append",
        default=[],
        help="Testing override. May be supplied more than once.",
    )
    return parser.parse_args()


def changed_files(args: argparse.Namespace) -> list[str]:
    if args.changed_file:
        return sorted({normalize(path) for path in args.changed_file if path.strip()})
    if not args.base_sha or not args.head_sha:
        raise ValueError("--base-sha and --head-sha are required without --changed-file")

    result = subprocess.run(
        ["git", "diff", "--name-only", "--diff-filter=ACMRD", args.base_sha, args.head_sha],
        check=True,
        capture_output=True,
        text=True,
    )
    return sorted({normalize(path) for path in result.stdout.splitlines() if path.strip()})


def normalize(path: str) -> str:
    normalized = path.strip().replace("\\", "/")
    while normalized.startswith("./"):
        normalized = normalized[2:]
    return normalized.lstrip("/")


def is_protected(path: str) -> bool:
    if path == "AGENTS.md" or path.endswith("/AGENTS.md"):
        return True
    if path.endswith(PROTECTED_SUFFIXES):
        return True
    if path in PROTECTED_EXACT_FILES:
        return True
    return path.startswith(PROTECTED_ROOTS)


def validate(base: str, head: str, files: list[str]) -> list[str]:
    errors: list[str] = []

    if base == "main":
        if head != "develop":
            errors.append("main에는 develop 브랜치만 Pull Request할 수 있습니다.")
        return errors

    if base != "develop":
        return [f"허용되지 않은 base 브랜치입니다: {base}. base는 develop 또는 main이어야 합니다."]

    if head == "integration":
        return ["기존 integration 브랜치는 폐기되었습니다. integration/<작업명>을 사용하세요."]

    if head.startswith("integration/") or head.startswith("codex/integration-"):
        return errors

    stage = next((rule for rule in STAGES if rule.branch == head), None)
    if stage is None:
        allowed = ", ".join(rule.branch for rule in STAGES)
        errors.append(
            f"develop PR의 head '{head}'는 허용되지 않습니다. "
            f"Stage 브랜치는 {allowed}, 통합 작업은 integration/<작업명>만 사용하세요."
        )
        return errors

    if stage.handoff not in files:
        errors.append(f"필수 인수인계 문서를 갱신해야 합니다: {stage.handoff}")

    allowed_exact = set(stage.exact_files) | {stage.handoff}
    for path in files:
        if is_protected(path):
            errors.append(f"Integration 보호 파일 수정 금지: {path}")
            continue
        if path in allowed_exact or path.startswith(stage.roots):
            continue
        errors.append(f"{head} 담당 범위 밖 파일: {path}")

    return errors


def main() -> int:
    args = parse_args()
    try:
        files = changed_files(args)
    except (ValueError, subprocess.CalledProcessError) as exc:
        print(f"::error::변경 파일을 확인하지 못했습니다: {exc}")
        return 2

    errors = validate(args.base, args.head, files)
    if errors:
        for error in errors:
            print(f"::error::{error}")
        print("\nChanged files:")
        print("\n".join(files) if files else "(none)")
        return 1

    print(f"Branch/scope guard passed: {args.head} -> {args.base} ({len(files)} files)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
