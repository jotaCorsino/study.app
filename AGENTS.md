# studyhub — agent instructions

## objective
Build **studyhub**, a personal study application for Windows using **.NET MAUI + Blazor Hybrid**, designed to evolve later to other operating systems.

The app must transform local course folders into a complete learning experience with:
- persistent course catalog;
- internal course pages;
- progress tracking;
- roadmap by AI;
- curated complementary materials from YouTube and other free sources.

## mandatory rules
- Start with **frontend-first**.
- Do **not** implement real backend, real persistence, real filesystem integration, or real external API integrations before UI approval.
- Use mock/in-memory data first.
- Keep responses short and objective during implementation.
- Update `.agents/rules/project-context.md` whenever the project structure or implementation state changes.
- Prefer file and folder names in lowercase, except conventions that must remain uppercase, such as `AGENTS.md` and `README.md`.

## technical stack
- .NET MAUI
- Blazor Hybrid
- C#
- future local persistence: SQLite
- future ORM: Entity Framework Core
- future integrations: AI APIs and Google/YouTube APIs

## implementation sequence
Follow `.agents/rules/build-sequence.md`.

## current build mode
Follow `.agents/rules/approval-mode.md`.
