# StudyHub release validation checklist

Use this checklist for a local Windows release candidate before publishing anything publicly.

- [ ] Start the app with an existing local database.
- [ ] Confirm local courses appear in the catalog.
- [ ] Open a local course and start a local video in the native player.
- [ ] Confirm lesson progress persists after closing and reopening the app.
- [ ] Confirm routine goals and calendar history render correctly.
- [ ] Pause, reactivate, and complete a course.
- [ ] Confirm the sidebar separates Active, Paused, and Completed courses.
- [ ] Edit a course name and description, then reopen the course.
- [ ] Confirm a 100% completed module shows green module progress text.
- [ ] Open Settings > Cursos e armazenamento and confirm local courses report their source status.
- [ ] Relocate a course to an equivalent root and confirm IDs, progress, current lesson, and playback state are preserved.
- [ ] Review a sync preview and confirm new content is not applied before explicit confirmation.
- [ ] Confirm missing content remains visible and unavailable without losing progress or history.
- [ ] Restore an item at the same relative path and confirm it becomes available with the same identity.
- [ ] Confirm the validation zip contains no user SQLite database, routine files, backups, local course folders, or extension files.

## StudyHub v1.2.0 publication validation

- [x] Clean staging matches the final Task 13 manifest.
- [x] Final ZIP extracts successfully and matches the staging contents.
- [x] GitHub Release v1.2.0 is published.
- [x] The public asset was downloaded again from the published release.
- [x] The public download SHA-256 matches `E3C7F6D1621B21F67D011BC781DE6F430B13D1D10D684AB6A5D44571F8A1399D`.
- [x] The public artifact started successfully from a fresh extraction.
