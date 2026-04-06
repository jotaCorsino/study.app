# StudyHub Windows Runbook

## Local data paths

- Database: `FileSystem.AppDataDirectory\studyhub.db`
- Database sidecars: `studyhub.db-wal`, `studyhub.db-shm`, `studyhub.db-journal`
- Backups: `FileSystem.AppDataDirectory\backups\studyhub-backup-<timestamp>\`
- Routine JSON files: `%LOCALAPPDATA%\StudyHub\Routine\`

## Operational concepts

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

## Safe maintenance operations

Available service-level maintenance operations:

- regenerate course presentation
- regenerate text refinement
- regenerate supplementary materials
- revalidate online course
- resync local course
- clear broken operational state

These operations target only the requested `CourseId` and do not delete physical course files.

## Backup and restore flow

Recommended order:

1. Create a backup.
2. Perform maintenance or recovery work.
3. If needed, restore a known-good backup.
4. Restart the app after restore/reset so in-memory state is refreshed.

## Windows build

Infrastructure build:

```powershell
dotnet build .\app_build\src\studyhub.infrastructure\studyhub.infrastructure.csproj --no-restore -v minimal
```

Windows app build:

```powershell
dotnet build .\app_build\src\studyhub.app\studyhub.app.csproj -f net10.0-windows10.0.19041.0 --no-restore -v minimal
```

Windows-targeted restore before publish:

```powershell
dotnet restore .\app_build\src\studyhub.app\studyhub.app.csproj -p:TargetFramework=net10.0-windows10.0.19041.0
```

Validated Windows publish:

```powershell
dotnet publish .\app_build\src\studyhub.app\studyhub.app.csproj -f net10.0-windows10.0.19041.0 -c Release --self-contained false -v minimal
```

Validated publish output:

- `app_build\src\studyhub.app\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish\`
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
3. Configure their own API keys and import their own courses.

Do not include in the zip:

- any local course folders from your machine;
- `studyhub.db` or SQLite sidecars from your app-data directory;
- `%LOCALAPPDATA%\StudyHub\Routine\` JSON files;
- `FileSystem.AppDataDirectory\backups\` contents;
- any manually exported personal backup folder;
- the repository root instead of the publish output.

The publish folder is intended to remain stateless relative to your personal data. StudyHub creates its own app-data structure on first run for each user.
The clean wrapper folder exists only to make navigation and sharing easier; the app still creates its real local state in the user's own app-data folders.

## Recommended public distribution

Do not commit the Windows zip into the repository source tree.

Recommended flow:

1. Generate the clean distribution zip locally.
2. Keep that zip as a release artifact.
3. Push the repository source changes.
4. Create a GitHub Release.
5. Upload `studyhub-windows-x64.zip` as a Release asset.

Recommended local staging path for the release zip:

- `production_artifacts\releases\studyhub-windows-x64.zip`

Recommended download page:

- `https://github.com/jotaCorsino/study.app/releases`

## Recovery notes

- If startup recovery fails, the database initializer backs up the incompatible database and recreates a clean one.
- Reset and restore operate only on StudyHub app data.
- Local course folders are never deleted by backup, restore, reset, or course maintenance services.
