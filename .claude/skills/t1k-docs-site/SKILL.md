---
name: t1k:docs-site
description: "Maintain the TheOneKit docs site (t1k.the1studio.org). Use to understand the auto-update-on-release guarantee, debug the generator locally, or fix the SSOT no-hand-edit gate."
keywords: [docs, website, docs-site, generate, regenerate, dispatch, pagefind, starlight, ssot]
argument-hint: "[--local | --explain]"
effort: medium
version: 2.5.1
origin: theonekit-core
repository: The1Studio/theonekit-core
module: t1k-maintainer
protected: true
---

# TheOneKit Docs Site

Docs at `t1k.the1studio.org` (Cloudflare Pages + Access). Repo: `The1Studio/theonekit-docs`.

## Auto-Update Guarantee

**The site auto-regenerates and redeploys on EVERY kit OR module release. You never hand-update the website.**

This is a hard guarantee — any docs work that involves editing the generated pages directly is wrong by design.

## Dispatch Wiring

Each kit's release workflow emits `repository_dispatch event_type: kit-released` → `theonekit-docs` `rebuild-and-deploy.yml` → regenerate + redeploy to Cloudflare Pages. Gated on `did-release` output so module-only bumps (no kit-level tag) also fire the dispatch. Authentication uses the `t1k-ci-sync` GitHub App (App ID 3356613).

## Nightly Fallback

`cron: '0 7 * * *'` — safety-net rebuild that catches any dispatch that was dropped or missed. Site is guaranteed fresh within 24h even if dispatch fails.

## Local Debug (`--local`)

```bash
node scripts/generate-content.mjs --local
# reads local kit clones; no GitHub fetch
# one kit only:
node scripts/generate-content.mjs --local --kit theonekit-unity
```

Use `--local` to test generator changes or debug output without touching the live site. Kit registry: `scripts/kits.config.json`.

## SSOT No-Hand-Edit Gate

`check-generated-untouched` CI gate **fails any PR** that commits files under `src/content/docs/**`.

- Generated content under `src/content/docs/` is EPHEMERAL — rebuilt on every deploy, never committed.
- To fix a generated page: edit the **source** in the owning kit, release, let the site rebuild.
- Hand-written content goes ONLY in `src/content/handwritten/**` — this path is exempt from the gate.

## Pointers

| Item | Value |
|---|---|
| Repo | `The1Studio/theonekit-docs` |
| Hosting | `t1k.the1studio.org` (Cloudflare Pages + Access) |
| Generator | `scripts/generate-content.mjs` |
| Kit registry | `scripts/kits.config.json` |
| Deploy workflow | `.github/workflows/rebuild-and-deploy.yml` |
| GitHub App | `t1k-ci-sync` App ID 3356613 |

## Gotchas

- **Never commit to `src/content/docs/**`** — the gate treats any such commit as a violation. Revert and fix the source kit instead.
- **Dispatch requires a released tag** — the `did-release` gate prevents spurious rebuilds from non-release commits, but it also means draft commits don't trigger a rebuild. Use `--local` for development iteration.
- **Cloudflare Access** — `t1k.the1studio.org` requires org authentication. Unauthenticated users see a login wall, not the docs. This is intentional (internal-only scope per `docs/audience-scope.md`).
