---

origin: theonekit-core
repository: The1Studio/theonekit-core
module: t1k-extended
protected: true
---
# ClickUp MCP — Validation Gotchas: Incident Detail

Historical/empirical context for the MCP validation and list-move gotchas documented
in SKILL.md "## Gotchas". The body carries the actionable rule + workaround; this file
records the incidents that confirmed them (progressive disclosure — loads on demand only).

## `clickup_get_custom_fields` `required: null` abort (confirmed 2026-05-20)

Confirmed empirically 2026-05-20: the validation failure reproduces identically with
`list_id`, `space_id`, AND `folder_id` scopes — erroring at
`["list_fields", N, "required"]`, `["space_fields", N, "required"]`, and
`["folder_fields", N, "required"]` respectively. Only `include_workspace: true` avoids it,
because workspace-scope fields typically have `required=false` explicitly set rather than
left null. Primary workaround (read any task's `custom_fields` via
`clickup_get_task(detail_level: "detailed")`) is more complete than the broken call.

## No list move/relocate — MEDDPICC restructure (confirmed 2026-05-20)

Confirmed empirically 2026-05-20 during the PlayableLabs Sales CRM MEDDPICC restructure:
lists cannot be relocated via the MCP. The create-new-list + move-tasks + deprecate-old
workaround chain in the body was derived from this restructure.

## `clickup_add_task_to_list` fallback — Moment Games + Whale Played (confirmed 2026-05-20)

Confirmed empirically 2026-05-20: Moment Games + Whale Played active accounts could not be
moved from the old `Accounts` list (template-defined `active`/`Closed` statuses, both
list-scoped) to the new Active Accounts list. `clickup_add_task_to_list` succeeded
immediately where 5+ `clickup_move_task` variants failed.

## Doc-mutation gaps — 26-doc consolidation (confirmed 2026-05-22)

Confirmed empirically 2026-05-22 during the PlayableLabs Sales CRM doc consolidation:
26 docs migrated 1:1, deprecation banners applied via `update_document_page`, and the
sidebar names stayed unchanged until the user performed a UI delete — confirming the
"banner changes content, not sidebar name" UX gotcha in the body.
