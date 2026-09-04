# Team test release checklist

The team-test source of truth is the pushed Git commit, not the Unity Editor cache
on the authoring PC. A release is ready only after the same commit passes a fresh,
cacheless checkout.

## Before creating the Pull Request

1. Use an `integration/<name>` branch. Do not push a dirty working tree directly to
   `main`.
2. Stage the intended paths explicitly. Confirm that new `.cs`, `.unity`, `.prefab`,
   `.asset`, audio, images, and every matching `.meta` are included.
3. Run:

   `python scripts/ci/validate_unity_repository.py --require-clean`

4. Create a separate checkout of that commit with no `Library`, `Temp`, `Obj`,
   `Logs`, or `UserSettings` folder. Open it with the exact Unity version in
   `ProjectSettings/ProjectVersion.txt` and run all Edit Mode tests.
5. Merge `integration/<name>` into `develop` only after the clean-checkout import and
   tests pass. Merge `develop` into `main` for the team-test release.

## On every tester PC

1. Install Git LFS once with `git lfs install`.
2. Clone with Git; do not use GitHub's source ZIP.
3. Run `git lfs pull` and verify `git lfs fsck` reports `Git LFS fsck OK`.
4. Use Unity `6000.4.8f1` unless `ProjectVersion.txt` changes in a reviewed commit.
5. If an older checkout was previously opened, delete only its generated `Library`,
   `Temp`, `Obj`, and `Logs` folders before opening the new commit.

The `Unity repository integrity` GitHub check rejects missing project files,
missing/orphaned `.meta` files, duplicate GUIDs, local Package references, missing
Build Settings scenes, case-colliding paths, broken LFS objects, and oversized files
that bypass Git LFS.
