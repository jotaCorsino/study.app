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
