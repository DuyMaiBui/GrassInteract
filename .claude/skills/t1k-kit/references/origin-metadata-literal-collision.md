---

origin: theonekit-core
repository: The1Studio/theonekit-core
module: t1k-maintainer
protected: true
---
# Origin Metadata Literal Collision

**Rule:** Never write the literal string `t1k-origin:` on a line of source code that is not a header comment.

## Why

CI's `inject-origin-metadata.cjs` injects a canonical `// t1k-origin: kit=... | repo=... | module=... | protected=...` header into every `.cjs/.js/.sh/.py/.yml` file and commits the result back to the repo (Git Is Truth). Before injecting, it removes any pre-existing origin header line.

Historically the filter used a substring test (`line.includes('t1k-origin:')`) — which ate ANY line mentioning the literal, not just the header. A regex or string constant referencing `t1k-origin:` for parsing purposes would survive the initial write but get stripped on the next release, leaving broken syntax like:

```js
const ORIGIN_COMMENT_RE =

let _agentCache = null;  // SyntaxError: Unexpected strict mode reserved word
```

## Fix in place

The filter is now a full-header-shape regex (see `inject-origin-metadata.cjs` → `ORIGIN_HEADER_RE`). Lines that merely mention the literal are preserved. CI gate `validate-post-inject-syntax.cjs` runs `node --check` on every hook file after injection — fails loudly if any file stops parsing.

## Defensive pattern (for code that MUST parse/match origin headers)

Split the literal so the header-shape regex in CI never matches your line:

```js
// Good — split prevents accidental match by any future filter
const _T1K_ORIGIN_TAG = 't1k-' + 'origin';
const ORIGIN_COMMENT_RE = new RegExp(
  `(?:^|\\n)\\s*(?:\\/\\/|#)\\s*${_T1K_ORIGIN_TAG}:\\s*kit=...`
);

// Bad — literal survives the regex but could be eaten by a naive filter
const ORIGIN_COMMENT_RE = /.*t1k-origin:\s*kit=.../;
```

## How to audit

Run before committing any file that processes origin metadata:

```bash
grep -n 't1k-origin:' your-file.cjs
```

- 0 matches → safe (file only uses encoded/split references)
- 1 match on a top-of-file comment line → the canonical header, safe
- Any other matches → risk. Split the literal or add a CI test asserting your file still parses post-inject.

## Related

- `rules/code-conventions.md` — Data-Driven Over Hardcoded
- `theonekit-release-action/scripts/inject-origin-metadata.cjs`
- `theonekit-release-action/scripts/validate-post-inject-syntax.cjs`
