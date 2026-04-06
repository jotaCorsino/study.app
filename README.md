# StudyHub

StudyHub is a Windows-first desktop study application built with .NET MAUI + Blazor Hybrid.

The project turns local course folders and curated online learning paths into a single personal study environment with persistent catalog, lesson navigation, course progress, roadmap generation, complementary materials, and per-course study routine tracking.

## What the app does today

StudyHub currently supports two real course modes:

- `LocalFolder`: import a local course folder, preserve the raw filesystem structure, and enrich it with AI-generated presentation, roadmap, and complementary materials.
- `OnlineCurated`: generate a real course from a learning intent using Gemini + YouTube, persist the course structure, and consume lessons inside the app.

The current build already includes:

- persistent course catalog with SQLite;
- course pages, lesson pages, roadmap, materials, and routine tracking;
- per-course progress, continue-studying logic, and lesson continuity;
- real local video playback on Windows through the native MAUI media pipeline;
- real YouTube external playback inside the app with controlled fallback when embed is blocked;
- per-course roadmap generation and complementary material generation;
- maintenance, backup, restore, reset, and startup recovery infrastructure.

## Main features

- Import local courses from disk without copying the user's course files into the app.
- Create curated online courses from free YouTube content.
- Track progress, last lesson, completion state, and continue-studying behavior per course.
- Generate roadmap and complementary materials per course.
- Enrich course/module/lesson titles and descriptions with Gemini in batches.
- Track per-course study routine, credited minutes, and streak based on real study records.
- Back up and restore StudyHub app data without touching the user's physical course folders.

## Stack and architecture

### Stack

- .NET MAUI
- Blazor Hybrid
- C#
- SQLite
- Entity Framework Core
- CommunityToolkit.Maui.MediaElement
- SecureStorage
- Gemini API
- YouTube Data API

### Solution layout

- `app_build/src/studyhub.app`: MAUI + Blazor Hybrid app shell and pages
- `app_build/src/studyhub.domain`: domain entities and core contracts
- `app_build/src/studyhub.application`: application contracts and interfaces
- `app_build/src/studyhub.infrastructure`: persistence, orchestration, providers, and maintenance services
- `app_build/src/studyhub.shared`: shared abstractions/utilities

The app follows a layered architecture where UI stays thin and real logic lives in services, orchestration, persistence, and provider integrations.

## Running locally

### Requirements

- Windows
- .NET SDK 10
- MAUI workload installed
- WebView2 runtime available

### Build

```powershell
dotnet restore .\app_build\src\studyhub.app\studyhub.app.csproj -p:TargetFramework=net10.0-windows10.0.19041.0
dotnet build .\app_build\src\studyhub.infrastructure\studyhub.infrastructure.csproj --no-restore -v minimal
dotnet build .\app_build\src\studyhub.app\studyhub.app.csproj -f net10.0-windows10.0.19041.0 --no-restore -v minimal
```

### Run

Open the solution or run the Windows target from Visual Studio / the MAUI toolchain on Windows.

## Publishing for Windows

Validated publish command:

```powershell
dotnet publish .\app_build\src\studyhub.app\studyhub.app.csproj -f net10.0-windows10.0.19041.0 -c Release --self-contained false -v minimal
```

Validated publish output:

```text
app_build\src\studyhub.app\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish\
```

Clean distribution script:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\publish-windows-clean.ps1
```

Clean distribution output:

```text
dist\windows\studyhub-windows-x64\
  abrir-studyhub.cmd
  como-abrir.txt
  runtime\
```

### Recommended distribution flow

1. Run the clean distribution script.
2. Zip the folder `dist\windows\studyhub-windows-x64\`.
3. Share that zipped folder.
4. The receiving user extracts it and runs `abrir-studyhub.cmd`.

### What must stay out of the zip

Do not include:

- your local course folders;
- your personal `studyhub.db`;
- SQLite sidecars such as `studyhub.db-wal`, `studyhub.db-shm`, `studyhub.db-journal`;
- routine JSON files from your machine;
- local backup folders created by the app;
- anything from your `AppData` / `LocalApplicationData` StudyHub storage;
- the repository root itself.

The published app is expected to start clean on another machine and create its own local data on first use.

## Downloading the Windows package

The recommended way to distribute StudyHub is through GitHub Releases, not by committing the zip file into the repository tree.

Recommended release flow:

1. Push the repository changes to GitHub.
2. Create a new Release in the repository.
3. Upload the Windows package zip as a release asset.
4. Share the Release page link.

Expected asset name:

```text
studyhub-windows-x64.zip
```

Repository:

- [jotaCorsino/study.app](https://github.com/jotaCorsino/study.app)

Recommended Releases page:

- [StudyHub Releases](https://github.com/jotaCorsino/study.app/releases)

For the end user:

1. Open the Releases page.
2. Download `studyhub-windows-x64.zip` from the latest release.
3. Extract the zip.
4. Run `abrir-studyhub.cmd`.

## Local storage and user data

StudyHub stores runtime data outside the publish folder.

Current local data roots:

- SQLite database: `FileSystem.AppDataDirectory\studyhub.db`
- database sidecars: same folder as the database
- backups: `FileSystem.AppDataDirectory\backups\`
- routine JSON files: `%LOCALAPPDATA%\StudyHub\Routine\`

This means the publish folder is distribution-friendly by default, as long as you zip only the publish output and not your local app-data folders.
The recommended shared package is the cleaner `dist\windows\studyhub-windows-x64\` wrapper generated by the script above.

## Important limitations and notes

- Windows is the primary supported target in the current real-use version.
- `OnlineCurated` currently supports YouTube as the external lesson runtime.
- YouTube resume-by-position is intentionally disabled for stability; course continuity still persists last lesson and progress.
- Online course generation depends on valid Gemini and YouTube API keys.
- Local course files are never copied into the app database; StudyHub references the folders selected by the user.

## Honest disclaimer about how this project was built

StudyHub was created as an experiment.

This codebase was not manually supervised line by line during implementation. The project was built 100% with the help of AI agents, specifically Codex and Antigravity, using Gemini and Claude during the process.

Even with that origin, the project evolved into a functional desktop application that can be used for real day-to-day study workflows on Windows.
