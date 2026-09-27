# Hacker News best stories API

ASP.NET Core API that returns the best `n` Hacker News stories ordered by score, without the traffic it
receives ever reaching Hacker News.

Coding test by Daniel Rodrigo Rodrigo, September 2026.

## Run it

Requires the .NET 10 SDK.

```
dotnet run --project src/DanielRodrigo.PruebaNET.Api
```

- API: http://localhost:5080/api/stories/best?n=10 (ready a few seconds after start-up)
- Interactive reference: http://localhost:5080/scalar
- Postman: import the files in [postman/](postman/) and run the collection (10 requests, 28 checks)
- Tests: `dotnet test`
- Docker: `docker build -t pruebanet-api .` then `docker run --rm -p 8080:8080 pruebanet-api`

## The endpoint

`GET /api/stories/best?n={1..500}` returns the stories ordered by score, highest first:

```json
[
  {
    "title": "A uBlock Origin update was rejected from the Chrome Web Store",
    "uri": "https://github.com/uBlockOrigin/uBlock-issues/issues/745",
    "postedBy": "ismaildonmez",
    "time": "2019-10-12T13:43:01+00:00",
    "score": 1716,
    "commentCount": 572
  }
]
```

| Status | When |
| --- | --- |
| 200 | Stories, with `Cache-Control: max-age` set to the seconds left until the next refresh. |
| 400 | `n` missing, not an integer or outside 1..500 (problem details). |
| 503 | No stories loaded yet, with `Retry-After`. |

Health: `/health/live` and `/health/ready` (ready once the first snapshot is loaded).

## How it works

Hacker News only offers a list of ids plus one request per story, so ranking by score means fetching every
story. Doing that per request would make Hacker News pay for our traffic, so the service uses a
**refresh-ahead cache**:

1. A background service loads the ids and every story (16 requests at a time) at start-up and every 5 minutes.
2. It sorts them by score and publishes an immutable snapshot in memory, swapping a single reference.
3. Requests only read and slice that snapshot: no lock, no network.

Upstream load is therefore fixed at about 1 + N requests every 5 minutes (N ≤ 500), whatever the traffic.
Locally, 414,000 requests in 10 seconds caused zero calls to Hacker News.

Edge cases:
- **Start-up:** requests that arrive before the first snapshot wait for the load in progress (single-flight);
  they never start another one.
- **Hacker News down:** the previous snapshot keeps being served. The HTTP client has timeouts, retries and a
  circuit breaker. If too many stories fail in a refresh, that refresh is discarded.

## Project layout

| Project | Contents |
| --- | --- |
| `src/...Application` | Story model, ports, loader, snapshot cache. No ASP.NET dependency. |
| `src/...Infrastructure` | Hacker News HTTP client, resilience, background refresh service. |
| `src/...Api` | Minimal API endpoint, problem details, health checks, OpenAPI. |
| `tests/...UnitTests` | NUnit and Moq, with a fake gateway and a fake clock. |
| `tests/...IntegrationTests` | The real app in memory with a fake Hacker News that counts requests. |
| `tests/...AcceptanceTests` | Reqnroll (Gherkin) scenarios for the brief's rules. |

Settings (refresh interval, concurrency, timeouts) are in `appsettings.json`. CI builds, runs the tests and
checks the Docker image against the real Hacker News.

## Assumptions

- "Best" means Hacker News' best stories list, ordered here by score.
- Only live stories are returned: no jobs, polls, comments, deleted or dead items.
- `uri` is `null` when a story has no link; `commentCount` is `descendants`.
- Data can be up to 5 minutes old.
- If `n` is larger than the number of stories available, all of them are returned.

## With more time

- A single refresher shared by all instances (Redis or a message bus), so upstream load does not grow with
  the instance count.
- OpenTelemetry metrics: refresh duration, failures, snapshot age, latency.
- Cheaper refreshes: fetch only new ids and refresh scores less often.
