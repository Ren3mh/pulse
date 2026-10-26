# pulse

A homemade product analytics pipeline, in the spirit of PostHog: a web app
sends user events to an API, which stores them for analysis.

A learning project for data engineering, DevOps and maintainable code.

## Run locally

```sh
docker compose up -d        # Postgres on localhost:5432
dotnet run --project src/Pulse.Api
dotnet test
```

## Docs

- [Event format](docs/event-format.md)
- [Prototype architecture](docs/architecture.md): diagrams of the Python prototype
