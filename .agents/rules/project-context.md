# project context — studyhub

## product vision
studyhub is a personal, installable study application that starts on Windows but is built with .NET MAUI for future expansion.

It must transform locally organized video courses into a complete learning experience.

## core product pillars
1. local course library
2. internal course experience
3. progress persistence
4. AI roadmap per course
5. curated complementary materials per course

## expected course structure
- course root folder
  - module folders
    - topic folders
      - lesson video files

## main functional areas
- home/catalog page
- course page
- progress dashboard
- roadmap page
- complementary materials page
- settings page

## technical direction
- .NET MAUI
- Blazor Hybrid
- layered structure:
  - studyhub.app
  - studyhub.domain
  - studyhub.application
  - studyhub.infrastructure
  - studyhub.shared

## current state
- Phase 1 Block 1 (Scaffold & Design System): completed
- Phase 1 Block 2 (Shell & Navigation): completed
- Phase 1 Block 3 (Home / Catalog): completed
- Phase 1 Block 4 (Course Detail): completed
- Phase 1 Block 5 (Player Area): completed
- Phase 1 Block 6 (Dashboard): completed
- Phase 1 Block 7 (Roadmap): completed
- Phase 1 Block 8 (Materials): completed
- Phase 1 Block 9 (Refinement): completed
- Phase 4 (Local persistence with SQLite + EF Core): completed
- Architecture (app, domain, application, infrastructure, shared) wired with persistent services
- SQLite database initialization runs at app startup against the real local catalog only and no longer seeds mock courses on first launch
- Courses, lesson progress, roadmap checklist state, and complementary materials now persist between sessions
- Lateral menu course navigation now uses explicit routing with correct active-state handling across course, lesson, roadmap, and materials routes
- Course removal is available from the lateral menu with explicit confirmation and full deletion of persisted app data for that course
- Catalog and navigation refresh immediately after course deletion via shared app state notifications
- Authorized UI refinements applied: square add-course button in sidebar, roadmap action buttons aligned to app button system, catalog cards without cover area, and course performance panel using a single persisted progress bar
- Authorized UI polish round applied: roadmap header/actions/checklists refined, course CTA renamed to `Vídeos Gratuitos`, and material preview play icon visually centered
- First pass of the real local-course flow is now wired under the approved UI: Windows folder picker, deterministic filesystem scan, local-course import service, raw import snapshot persistence, and real video playback in the existing lesson screen
- Local lesson playback now persists current lesson, last playback position, completion state, and updated course progress in SQLite
- Critical local-flow correction round applied under the frozen UI: the local-course scanner now preserves a full recursive raw folder tree, imports sibling branches across the whole course, and updates an existing imported course without duplicating the main entity
- Hotfix round applied to the local import flow: folder selection, scan, persistence, state refresh, and post-import navigation are now wrapped with explicit status feedback and friendly failure handling on the `AddCourse` screen
- Local course reimport now persists through a transaction with a safe two-step replacement of the module tree, avoiding silent failures when deterministic child IDs are re-used for an existing imported course
- Local Windows lesson playback now uses a native `CommunityToolkit.Maui.MediaElement` host aligned over the same approved Blazor player area
- The lesson screen no longer uses HTML `<video>`, `video-playback.js`, `LocalVideoSourceResolver`, WebView transport fallbacks, `blob:` preloading, or direct `file://` playback for local lessons on Windows
- A native playback service now owns session state, viewport sync, MediaElement events, last-position persistence, completion, and course progress updates while `LessonPlayer` remains a state/orchestration screen
- Native player polish round applied: status overlays are now reserved for loading/error states only, and playback controls auto-hide again during normal video playback after a short period of inactivity
- Native player stability hotfix applied: automatic startup seek and automatic resume-position restoration are temporarily disabled on Windows local lessons, while position persistence remains active, to eliminate MediaElement COM crashes during lesson open, switch, and unload
- Native player state-change hotfix applied: `MainPage` no longer mutates `ShouldShowPlaybackControls` during `StateChanged`, native MediaElement events are attached/detached explicitly per loaded source, and pending event work is canceled during source transitions or page unload to avoid late COM crashes on Windows
- Course origin is now a first-class domain concept: courses persist `SourceType` (`LocalFolder` or `OnlineCurated`) plus structured `SourceMetadata`, and lessons persist explicit source semantics (`LessonSourceType`, local file path, external URL, provider)
- The real `LocalFolder` flow now builds its primary course structure through `LocalFolderCourseBuilder`, while supplementary materials are separated behind `ISupplementaryMaterialsService` / `SupplementaryMaterialsService`
- The architecture is now prepared for `OnlineCurated` as a primary course-creation path via `IOnlineCuratedCourseBuilder` and an `OnlineCuratedCourseBlueprint` that encodes discovery queries, multi-source curation, playlist breakdown, and learning-progression assembly rules without changing the approved UI
- SQLite initialization now treats the current schema as the source of truth: startup validates the local database, stamps `PRAGMA user_version`, and automatically renames incompatible databases to a timestamped backup before recreating a clean SQLite file so the app can still open
- The expanded module list on the course page now supports internal vertical scroll for large lesson trees without shifting the rest of the page layout
- DeepSeek has been removed from the active architecture and runtime; StudyHub now operates externally with only Gemini and the YouTube Data API
- StudyHub now persists only Gemini and YouTube API keys locally through `SecureStorage`, and the approved `Settings` screen validates both providers against their real remote APIs with only the necessary visual change to remove DeepSeek
- Real provider-backed orchestration is now enabled under the approved UI: `OnlineCurated` creation runs through separate steps for intent capture, Gemini pedagogical planning, YouTube discovery, source curation, Gemini text refinement, course assembly, roadmap generation, and supplementary-material generation, with per-course generation history persisted in SQLite
- The real `LocalFolder` path still owns the primary structure, and imported local courses now enter the app immediately with the raw filesystem tree while background enrichment runs asynchronously and never blocks import or usage
- Local course entities now preserve raw-vs-display presentation data across the domain and SQLite schema (`RawTitle` / `RawDescription` plus display `Title` / `Description`) so the filesystem-derived tree remains the source of truth while Gemini enrichments stay in a non-destructive presentation layer
- Local course reimport now merges existing valid presentation artifacts back onto the rebuilt raw structure, preserves roadmap/material/progress state where possible, and only forces regeneration when the underlying filesystem structure actually changed
- Background local enrichment now tracks separate per-course step states for presentation, text refinement, roadmap, and supplementary materials, allowing partial retries without duplicating successful artifacts
- Course persistence now stores richer orchestration artifacts per course through `SourceMetadata`, supplementary material URLs/video IDs, generation step history, and description fields on modules, topics, and lessons, while the approved Blazor pages remain visually unchanged
- `OnlineCurated` courses now have a real lesson-consumption runtime under the frozen UI: `LocalFile` lessons stay exclusively on the existing Windows-native `MediaElement` pipeline, while `ExternalVideo` lessons use a separate `WebView`-based YouTube runtime with its own service, session tokens, lifecycle, and failure handling
- The app remains the source of truth for online-course navigation and progress: YouTube playlists are flattened into individual persisted lessons, the app decides the next lesson, progress is stored per StudyHub lesson, and YouTube autoplay/playlist navigation is not used as course progression state
- Online external playback currently supports YouTube only; unsupported external providers fail in a controlled overlay state without crashing the lesson screen or affecting the local player runtime
- Continuity for `OnlineCurated` now persists last opened lesson, lesson status, course progress, and continue-studying behavior through the existing progress service; precise resume-by-position for YouTube remains intentionally disabled in this cut to prioritize runtime stability
- The YouTube external runtime now uses a dedicated local host page under a stable virtual HTTPS origin inside the Windows WebView, with `enablejsapi`, `origin`, `widget_referrer`, and a `CoreWebView2` message bridge instead of in-memory HTML injection, to address YouTube embed error 153 and keep the player isolated from the local `MediaElement` pipeline
- When a specific YouTube lesson still blocks embedded playback (for example 101/150/153), StudyHub now fails in a controlled state and launches the canonical YouTube URL in the external browser as a contained fallback instead of breaking the lesson screen
- Online course curation is now stronger and more pedagogical: YouTube discovery runs playlist-first plus embeddable-video search, playlists can act as reusable anchor sources across multiple modules, lesson selection is driven by token overlap/coverage instead of strict all-terms matching, and supplemental sources are chosen to fill coverage gaps instead of producing a loose random video list
- SQLite legacy-origin normalization no longer rewrites existing `OnlineCurated` records back to `LocalFolder` during startup backfill; source-type coexistence is now preserved in the database bootstrap path
- `OnlineCurated` courses now persist explicit operational state per stage through `course_generation_runs` plus a dedicated `external_lesson_runtime_states` table, allowing the app to track structure, presentation, text refinement, roadmap, supplementary materials, validation, and external playback with last success/failure timestamps and controlled per-lesson playback errors
- A new `IOnlineCourseOperationsService` now exposes per-course runtime-state aggregation and partial retry for online-course stages without changing the approved UI; retries can rerun source curation/structure, presentation, text refinement, roadmap, supplementary materials, and validation independently while keeping all data isolated by `CourseId`
- Online-course structure refreshes and operational retries now preserve useful course state whenever lesson IDs remain stable: progress, completion, last playback markers, and current lesson are merged back during `CoursePersistenceHelper.UpsertCourseAsync` instead of being destructively reset on every rebuild
- External YouTube lesson failures are now persisted per lesson and per course: opening a lesson marks external playback as running, successful player readiness marks the stage as succeeded, and embed/bridge failures are recorded as controlled failures without contaminating other lessons or the local `MediaElement` pipeline
- Online-course generation now pre-seeds every expected stage as `Pending` before execution, making it explicit in SQLite when a stage has not run yet versus when it later ran, failed, or was skipped
- `OnlineCurated` creation is now gated by a persisted-structure validation pass after `UpsertCourseAsync`: the orchestrator reloads the saved course, verifies origin, modules, lessons, lesson ordering, external URLs, and source metadata, records `online-course-validation`, and only then reports creation success
- If persisted validation fails, the inconsistent `OnlineCurated` course is removed before it can surface in the catalog/menu as a successfully created course; roadmap/material failures remain non-blocking and are now reported as partial post-creation issues instead of silently piggybacking on structure success
- Course routine tracking is now reconciled per lesson and per day instead of relying on blind timer increments in the Blazor player components: daily routine records keep per-lesson credited minutes, lesson completion tops up only the missing remainder to the lesson duration, and per-course streak is recalculated from real daily study records instead of a fake last-access heuristic
- Daily routine rollover hotfix applied: the routine pipeline now persists only the newly earned lesson-minute delta into the current day's record, and external-player heartbeats now reconcile by real playback position instead of coarse percentage buckets so a new `DailyStudyRecord` is created normally after midnight without leaking writes across days
- Routine adherence now treats non-planned study days as neutral across the whole course panel: unplanned days no longer color the daily/monthly grids, no longer enter the monthly adherence average, and no longer count toward the per-course streak even if the user studied on those days
- The `Materiais extras` page now triggers real complementary-material generation for the current `CourseId` through the existing `IMaterialService`, and supplementary generation now uses richer per-course context (source type, requested topic/objective, roadmap highlights, selected sources, lesson/module titles, prior queries) plus playlist-and-video YouTube discovery without recreating the main course
- Gemini text enrichment is now processed in batches instead of a single giant tree request: local post-import enrichment and online post-persistence refinement both run chunked module/lesson batches, persist per-batch generation history in `course_generation_runs`, skip already successful batches on retry, and preserve raw titles/descriptions as the immutable source layer
- StudyHub now has a dedicated operational-storage layer: managed paths for SQLite, routine JSON files, and timestamped backups are centralized behind `IStoragePathsService`, while `IAppBackupService` can create backups, restore a chosen backup, or reset app state without touching the user's physical course folders
- Backup, restore, reset, and startup recovery are now explicit and separate concepts: restore rehydrates a chosen backup, reset recreates a clean app state (optionally after a safety backup), and startup recovery first attempts a non-destructive integrity-checked reopen before falling back to backup-plus-reset
- Course and app maintenance services now exist under the frozen UI: `ICourseMaintenanceService` can regenerate presentation/text/materials, revalidate online courses, resync local courses, and clear broken per-course operational state; `IAppMaintenanceService` can normalize stale/orphan global operational state without deleting valid artifacts
- Critical runtime flows now log more explicitly at start/success/failure, including local import, local enrichment, online course creation, supplementary materials generation, provider validation, secure-storage settings persistence, and both local/external playback activation
- A Windows runbook now documents local data paths, backup/restore/reset semantics, safe maintenance expectations, and build/publish commands for real execution and recovery workflows
- Final product closeout removed residual mock-service files, removed artificial success-navigation delays from course creation, and replaced provisional runtime strings that were still visible in the real app flow
- The MAUI app now uses the official `img_ref/book-icon-3.svg` as its icon source of truth, mirrored into the MAUI-compatible asset `Resources/AppIcon/book_icon.svg` with the official symbol color `#6666ff`; older competing icon assets were removed from `Resources/AppIcon`, the app `bin`/`obj` caches were cleared to avoid stale icon reuse, and the validated framework-dependent Windows publish path produces the final assets under `bin\Release\net10.0-windows10.0.19041.0\win-x64\publish`
- Course continuation is now resolved by a dedicated per-course resume service instead of using a raw `LastLessonId` jump: StudyHub opens the in-progress/current lesson first, otherwise the next lesson after the last completed one, otherwise the last lesson at course end, otherwise the first lesson
- The lesson screen sidebar now anchors automatically to the active target lesson on load and re-entry using non-visual lesson-item anchors plus a small local JS scroll helper that centers the relevant module/item inside the existing lateral scroll areas
- Final distribution/readme closeout is now documented: the root `README.md` reflects the real final product state, Windows publish flow, local-data behavior, and the explicit AI-assisted origin disclaimer; the Windows runbook now explains the clean distribution path, which folder to zip, and which user-local files must stay out of a shared build
- Windows sharing now also has a cleaner packaging helper: `scripts\publish-windows-clean.ps1` publishes into `dist\windows\studyhub-windows-x64\runtime`, writes `abrir-studyhub.cmd` plus `como-abrir.txt` in the package root, and gives the repository a friendlier first folder to zip/share without changing the app's runtime storage model
- The official repository considered for final distribution/docs is `https://github.com/jotaCorsino/study.app.git`; local Windows release zips are staged at `production_artifacts\releases\studyhub-windows-x64.zip` before upload to GitHub Releases rather than being treated as tracked source files
- Course detail isolation hotfix applied: switching `CourseId` now resets the page state before reload, ignores stale async responses from a previous course, recreates keyed course/routine UI subtrees, and disposes old routine timers correctly so modules, progress, and routine data no longer bleed between courses

## future integrations
- SQLite for local persistence
- filesystem indexing for real course import
- local video player integration
- broader playback/runtime support for external online lessons
