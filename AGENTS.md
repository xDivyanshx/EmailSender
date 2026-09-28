# Repository Agent Rules

## Milestone Handoff Documentation

After every meaningful milestone, update all documentation needed for a new coding session to resume accurately:

- `PROJECT_CONTEXT.md`: record the completed change, decisions, verification, blockers/risks, and exact next step.
- `README.md`: update user-facing workflow, setup, behavior, or testing instructions whenever they changed.
- `AGENTS.md`: change this file only when the repository's agent workflow or engineering rules change.

Do not leave documentation describing behavior that is more than one completed feature behind. Before finishing a milestone, run the relevant tests/build checks and record their result in `PROJECT_CONTEXT.md`. Never include secrets, tokens, passwords, full recipient lists, or other sensitive local data in documentation.

After the milestone is verified, stage every intended file required for that milestone, including documentation, source, tests, and generated production assets when the repository tracks them. Review `git status` and the staged diff before handoff. Do not create the commit or push unless the user explicitly asks; finish by asking the user to commit and push the staged milestone.

## Session Startup

Read `AGENTS.md`, `PROJECT_CONTEXT.md`, and `README.md` before implementing changes. Treat `PROJECT_CONTEXT.md` as the authoritative resumable checkpoint and preserve unrelated user work in the working tree.
