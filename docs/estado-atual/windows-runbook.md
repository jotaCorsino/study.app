# StudyHub Windows Runbook

## Local data paths

- Database: `FileSystem.AppDataDirectory\studyhub.db`
- Database sidecars: `studyhub.db-wal`, `studyhub.db-shm`, `studyhub.db-journal`
- Backups: `FileSystem.AppDataDirectory\backups\studyhub-backup-<timestamp>\`
- Routine JSON files: `%LOCALAPPDATA%\StudyHub\Routine\`

## Operational concepts

- Current scope:
  StudyHub runs as a local-course study app. Active flows are local folder import, local video playback, progress, routine, course lifecycle status, backups, restore, reset, and manual course title/description editing.
- Removed flows:
  AI integrations, API keys, roadmaps, supplementary materials, free external videos, online course creation/curation, external video playback, and YouTube/Gemini workflows are not active release features.
- Recovery:
  Startup-first attempt to recover the SQLite runtime without destroying data when the database still passes integrity checks.
- Reset:
  Clears only StudyHub app state, recreates a clean local database, and preserves user course files on disk.
- Restore:
  Replaces current StudyHub app state with a previously created backup.

## Backup contents

Each backup folder contains:

- `database\studyhub.db`
- database sidecars when present
- `routine\...` with all per-course routine JSON files
- `backup-manifest.json`

Backups are timestamped and never overwrite older backups.

## Current user-facing operations

Available local operations:

- import or resync a course from a local folder
- open local videos with the native player
- save lesson progress and resume playback
- set routine goals with historical validity periods
- pause, reactivate, and complete courses
- group courses in the sidebar by Active, Paused, and Completed status
- edit course title and description without changing `RawTitle`, `RawDescription`, folder path, modules, topics, lessons, progress, routine, or lifecycle status
- backup, restore, and reset StudyHub app data

These operations do not delete physical course files.

## Backup and restore flow

Recommended order:

1. Create a backup.
2. Perform maintenance or recovery work.
3. If needed, restore a known-good backup.
4. Restart the app after restore/reset so in-memory state is refreshed.

## Windows build

Infrastructure build:

```powershell
dotnet build .\src\studyhub-web\src\studyhub.infrastructure\studyhub.infrastructure.csproj --no-restore -v minimal
```

Windows app build:

```powershell
dotnet build .\src\studyhub-web\src\studyhub.app\studyhub.app.csproj -f net10.0-windows10.0.19041.0 --no-restore -v minimal
```

Windows-targeted restore before publish:

```powershell
dotnet restore .\src\studyhub-web\src\studyhub.app\studyhub.app.csproj -p:TargetFramework=net10.0-windows10.0.19041.0
```

Validated Windows publish:

```powershell
dotnet publish .\src\studyhub-web\src\studyhub.app\studyhub.app.csproj -f net10.0-windows10.0.19041.0 -c Release --self-contained false -v minimal
```

Validated publish output:

- `src\studyhub-web\src\studyhub.app\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish\`
- Clean distribution wrapper: `dist\windows\studyhub-windows-x64\`
- Distribution script: `powershell -ExecutionPolicy Bypass -File .\scripts\publish-windows-clean.ps1`

## Clean distribution package

Recommended packaging flow:

1. Run `powershell -ExecutionPolicy Bypass -File .\scripts\publish-windows-clean.ps1`.
2. Open `dist\windows\studyhub-windows-x64\`.
3. Zip that folder.
4. Share the zipped distribution folder.

The receiving user should:

1. Extract the folder anywhere on disk.
2. Run `abrir-studyhub.cmd`.
3. Import their own local course folders.

Do not include in the zip:

- any local course folders from your machine;
- `studyhub.db` or SQLite sidecars from your app-data directory;
- `%LOCALAPPDATA%\StudyHub\Routine\` JSON files;
- `FileSystem.AppDataDirectory\backups\` contents;
- any manually exported personal backup folder;
- extension files from `src\studyhub-extension\`;
- the repository root instead of the publish output.

The publish folder is intended to remain stateless relative to your personal data. StudyHub creates its own app-data structure on first run for each user.
The clean wrapper folder exists only to make navigation and sharing easier; the app still creates its real local state in the user's own app-data folders.

Packaging audit:

- `scripts\publish-windows-clean.ps1` publishes only `src\studyhub-web\src\studyhub.app\studyhub.app.csproj`.
- The script writes a clean wrapper under `dist\windows\studyhub-windows-x64\` with `runtime\`, `abrir-studyhub.cmd`, and `como-abrir.txt`.
- The script does not copy `%LOCALAPPDATA%\StudyHub`, SQLite user databases, routine JSON, backup folders, local course folders, or extension files.
- Zip only the clean wrapper folder, never the repository root.

## Recommended public distribution

Do not commit the Windows zip into the repository source tree.

Recommended flow:

1. Generate the clean distribution zip locally.
2. Keep that zip as a release artifact.
3. Push the repository source changes.
4. Create a GitHub Release.
5. Upload `studyhub-windows-x64.zip` as a Release asset.

For local release-candidate validation, stop at step 2 and do not upload the zip.

Recommended local staging path for the release zip:

- `production_artifacts\releases\studyhub-windows-x64.zip`

Recommended download page:

- `https://github.com/jotaCorsino/study.app/releases`

## Recovery notes

- If startup recovery fails, the database initializer backs up the incompatible database and recreates a clean one.
- Reset and restore operate only on StudyHub app data.
- Local course folders are never deleted by backup, restore, reset, or course maintenance services.
