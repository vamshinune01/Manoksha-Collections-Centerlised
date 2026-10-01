# Load test

`load-test.mjs` simulates shoppers browsing the storefront (categories → product list → product page, with think time) and, with
an admin token, the Owner's dashboard and Exception Center. No dependencies — Node 18+.

```bash
BASE_URL=https://<api-host> VUS=20 DURATION=60 node tests/load/load-test.mjs
```

| Variable | Default | Meaning |
|---|---|---|
| `VUS` / `DURATION` | 10 / 30 | Concurrent virtual users / seconds |
| `PROXY_KEY` | — | Spread requests over synthetic client IPs (needs the API's `Security:ProxyKey`; otherwise one machine hits the 300/min per-IP limit, which is correct behaviour for a single client) |
| `ACCESS_TOKEN` | — | Admin access token: adds dashboard + Exception Center reads |
| `THRESHOLD_P95_MS` / `MAX_ERROR_RATE` | 800 / 0.01 | Exit 1 when exceeded |

Writes (checkouts, POS sales) are covered by the concurrency suites in `manoksha-backend/tests/Manoksha.IntegrationTests`
(`ConcurrencyAndFailureTests`, `OnlineCheckoutTests`, `IdempotencyTests`) against a real PostgreSQL database instead of loading a
shared environment with fake orders.
