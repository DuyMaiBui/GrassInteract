---

origin: theonekit-unity
repository: The1Studio/theonekit-unity
module: editor
protected: false
---
# Milestone Gate Checklists

## Alpha Gate

Core loop playable. Not feature-complete. Crashes acceptable but tracked.

- [ ] Core gameplay loop implemented end-to-end (spawn → fight → resolve)
- [ ] All planned systems compile with 0 errors
- [ ] At least 1 demo runs to completion without crash in 5 min session
- [ ] Unit tests exist for all ISystem implementations (may have failures)
- [ ] docs/development-roadmap.md created with planned phases
- [ ] `gk:playtest --quick` passes checks 1-3 on primary demo
- [ ] **Restart-hygiene**: pressing Restart wipes all entities cleanly (no orphaned combat/spawn entities from previous run) — verified in Play mode
- [ ] **Naming-hygiene**: no production type names or namespace segments containing "Demo", "Test", "Temp", or "Placeholder" (CLAUDE.md convention; add CI grep hook if >5 violations found)

**Blocker to advance:** Any crash that prevents 5 min session.

---

## Beta Gate

Feature-complete. Balance pass done. No critical bugs. Perf baseline captured.

- [ ] All planned features implemented
- [ ] Balance audit passed (`gk:balance` — no flagged DPS/EHP outliers)
- [ ] 0 P0/P1 bugs open (crash, data loss, unwinnable state)
- [ ] Performance baseline captured and documented in `docs/project-changelog.md`
- [ ] `gk:playtest --full` passes all 8 checks on all demos
- [ ] All EditMode tests pass (0 failures)
- [ ] docs/wiki/ pages exist for all demos with diagrams
- [ ] `docs/system-architecture.md` up to date
- [ ] **Restart-hygiene**: entity-wipe traced for all demos — each demo's run-end path verified clean (no orphaned entities after restart or scene reload)
- [ ] **Naming-hygiene**: CI grep confirms 0 production types/namespaces contain "Demo"/"Test"/"Temp"/"Placeholder" — gate blocks release if violations found

**Blocker to advance:** Any open P0/P1 bug or failing playtest check.

---

## Release Candidate Gate

Zero bugs. Perf targets met. Final art. Platform tested.

- [ ] 0 open bugs of any severity
- [ ] All perf targets met (see `t1k-profile` skill `optimization-targets.md`)
- [ ] Final art assets in place (no placeholder sprites/materials in shipping demos)
- [ ] Target platform tested (Android IL2CPP if mobile, standalone if desktop)
- [ ] `docs/project-changelog.md` complete and reviewed
- [ ] CLAUDE.md and all skills updated to reflect final implementation
- [ ] `gk:playtest --full` passes on final build (not Editor play mode)

**Blocker to advance:** Any perf target miss or placeholder art remaining.

---

## Gold / Ship Gate

Ready for release or handoff.

- [ ] All tests pass (EditMode + PlayMode)
- [ ] Clean `git log` — no WIP commits on main
- [ ] Release notes written (`docs/project-changelog.md` → extract latest section)
- [ ] Platform submission checklist completed (icons, splash, store metadata)
- [ ] Build artifacts archived
- [ ] Post-mortem or lessons-learned note added to project memory

**Blocker to advance:** Any failing test or missing submission asset.
