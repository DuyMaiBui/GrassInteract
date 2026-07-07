---

origin: theonekit-core
repository: The1Studio/theonekit-core
module: t1k-extended
protected: true
---
# Docker Volume Discipline — Named Volumes for State, Audit Before Prune

## Rule

1. **Stateful compose services MUST use named volumes.** Any docker-compose service with a persistent data directory (postgres, mysql, mongo, redis, rabbitmq, any DB/queue) MUST mount a **named volume** (declared under top-level `volumes:`) or an explicit bind mount for that directory. NEVER rely on the image's implicit `VOLUME` — that silently creates an anonymous volume, which is indistinguishable from prune-able waste.
2. **NEVER blanket-prune anonymous volumes.** Before removing dangling anonymous volumes (`docker volume prune`, `docker system prune --volumes`), audit each one for database fingerprints — `PG_VERSION` (postgres), `ibdata1` (mysql), `WiredTiger` (mongo), `dump.rdb` / `appendonly.aof` (redis) — and EXCLUDE matches. Delete via an explicit `docker volume rm <list>` of verified-safe volumes, never a blanket prune.
3. **Compose fixes land in the owning repo.** Converting a service to named volumes is a change to the owning project's compose file — branch + PR in that repo. NEVER edit compose files in place on a host machine.

## Why

Anonymous volumes hide real data behind hex names. A 2026-07-02 cleanup found 12 anonymous volumes holding live postgres/redis data (one written the previous day) among 234 prune candidates — a blanket `docker volume prune` would have destroyed them.

## How to apply

```yaml
services:
  db:
    image: postgres:16
    volumes:
      - db_data:/var/lib/postgresql/data   # named, survives prune
volumes:
  db_data:
```

Audit before prune (mount docker's data-root, check `docker info --format '{{.DockerRootDir}}'` — it may not be `/var/lib/docker`):

```bash
docker run --rm -v <docker-root>/volumes:/vols:ro alpine sh -c \
  'for v in /vols/*/_data; do [ -f "$v/PG_VERSION" ] && echo "DB DATA: $v"; done'
```

When converting a running service: stop it, copy data into the new named volume (`docker run --rm -v <anon>:/from -v <named>:/to alpine cp -a /from/. /to/`), then start with the updated compose file.

## Related

- `rules/preview-first-batch.md` — smoke-test + confirm gate before bulk deletion
- `rules/kit-wide-fix-discipline.md` — fixes land at the source repo, not locally
