# Dan.Plugin.Bits

Data.altinn.no plugin that serves bank control information (Kontrollinformasjon, KontrollinformasjonUtvidet,
Begrensninger) and PantUtlegg endpoints. The source data is a set of CSV and JSON files maintained in a separate
source repository.

## Cache flow

All datasets are served from an in-memory snapshot per Function instance. A client request never waits for the
source except on a cold instance that has not finished warming up.

| Trigger | Where | What it does |
|---|---|---|
| Worker start-up (deploy, restart, scale-out) | `CacheWarmupService`, every instance | Builds both caches from one download |
| Change to the control CSV or any limitation file (workflow in the source repository) | `OppdaterKontrollinformasjon` | Re-reads the CSV and every limitation file, swaps the snapshot atomically |
| Change to the PantUtlegg CSV | `OppdaterPantUtlegg` | Re-reads the PantUtlegg CSV |
| Timer, top of every hour | `OppdaterCachePlanlagt`, one instance | Safety net: refreshes both caches |
| Request finds data older than `CacheSoftTtlMinutes` | any dataset function | Answers from the current data and starts one background rebuild |
| Request on a cold instance | any dataset function | Builds the snapshot once; concurrent requests wait for that one build |

The soft time-to-live exists because the webhook and the timer each reach only one instance. With it, every instance
bounds its own staleness to roughly the TTL plus one request interval, without a rebuild on the request path.

A refresh that fails (source unavailable, missing file, control CSV without rows) keeps the previous snapshot and
returns 500 to the caller, so a failed workflow run in the source repository is visible there. A header-only
PantUtlegg CSV is valid and gives an empty list, which is the current state in prod.

Rebuilding is a single request: the source repository is downloaded as one archive (a few hundred KB, under a
second), so the CSVs and every limitation file come from the same commit. Every rebuild logs the commit it came
from and how the data changed compared with the previous snapshot.

## Settings

| Setting | Example | Purpose |
|---|---|---|
| `EndpointsResourceFile` | `prod/Banker_Kontroll_prod.csv` | Control endpoints CSV, path inside the source repository |
| `PantUtleggResourceFile` | `prod/Banker_Pantutlegg_prod.csv` | PantUtlegg CSV |
| `LimitationsResourceFile` | `prod/limitations/` | Folder with one `{orgNo}.json` per bank |
| `UseTestEndpoints` | `false` | Sets `env` on returned endpoints to `test` or `prod` |
| `CacheSoftTtlMinutes` | `60` | Age after which a request triggers a background rebuild; `0` disables |

Access to the source repository is configured through app settings; see `src/Dan.Plugin.Bits/local.settings.json.template`
for the keys. Copy the template to `local.settings.json` to run locally.
