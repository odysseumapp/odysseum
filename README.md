# Odysseum

Self-hosted novel and book writing software.

This repository contains the .NET API and file storage. The Vue 3 / Nuxt 4 interface lives in [odysseum-web](https://github.com/odysseumapp/odysseum-web).

## Development Note
Odysseum is in early development and breaking changes will be shipped regularly.

## Planned Features

- Multiple projects in one workspace
- Corkboard and outline
- Markdown editing
- Source Mode/Reading Mode/Focus Mode
- Word goals
- Offline support
- Exports
- And more

There is a demo manuscript included when `ODYSSEUM_DEMO=true`.

## AI Usage
AI coding tools are used in the development of Odysseum. The developer is a programmer by trade and all code is reviewed before merging. Odysseum is a hobby project and will be worked on as spare time allows.

## Workspace and project format

```text
workspace/                      ← ODYSSEUM_WORKSPACE
  The Cartographer's Daughter/  ← one project per folder
    Manuscript/
      Chapter 01/
        The letter arrives.md
    Notes/
      Characters.md
    .writer/
      project.json
      history/<document-id>/<timestamp>-<hash>.md
      instance.lock
  Short stories/
```
## Run with Docker

```sh
git clone https://github.com/odysseumapp/odysseum-web.git odysseum-web
docker compose up --build -d
```

Open **http://localhost:5080**. Compose builds the API and Nuxt frontend as separate containers. API documentation is at **http://localhost:5081/scalar**. Set `ODYSSEUM_WEB_PATH` if the frontend checkout is elsewhere, for example `../odysseum-web`.

The API container uses two directories:

| Container path | Host default |  |
|---|---|---|
| `/projects` | `./projects` | your projects |
| `/data` | `./data` | `server-settings.json`, authentication keys |

Set `ODYSSEUM_DEMO` to `false` to start with an empty workspace.

To access Odysseum remotely, a reverse proxy is recommended. Configure `ODYSSEUM_PASSWORD` in a `.env` file.

## Run locally on Windows

The API requires the .NET 10 SDK. The separate frontend requires Node.js 22.19+ or 24.11+ (24 LTS recommended) and npm.

```powershell
./scripts/build.ps1
./scripts/start.ps1
```

The API listens at **http://localhost:5080**. In a second terminal:

```powershell
git clone https://github.com/odysseumapp/odysseum-web.git odysseum-web
cd odysseum-web
npm ci
npm run dev
```

Open the Nuxt UI at **http://localhost:3000**. Its `NUXT_API_ORIGIN` defaults to `http://127.0.0.1:5080`; Nuxt proxies API requests and live events to that address. See the [frontend instructions](https://github.com/odysseumapp/odysseum-web#readme) for production builds, offline behavior and browser tests.

To open a different workspace directory:

```powershell
./scripts/start.ps1 -Workspace 'D:\Writing\My Novel'
```
## Configuration

| Variable | Default | Purpose |
|---|---|---|
| `ODYSSEUM_WORKSPACE` | `workspace` beside the repository's server directory (`/projects` in Docker) | Root directory holding one folder per project |
| `ODYSSEUM_DEMO` | `true` in development, otherwise `false` | Seed an empty workspace with the sample project |
| `ODYSSEUM_PASSWORD` | unset | Optional password for the single workspace |
| `ODYSSEUM_SCAN_SECONDS` | `3` | Full reconciliation interval, 1–300 seconds |
| `ODYSSEUM_KEYS` | ASP.NET Core default (`/data/keys` in Docker) | Persistent authentication key directory |
| `ODYSSEUM_SETTINGS` | `server-settings.json` beside the server (`/data/server-settings.json` in Docker) | Optional settings file |

## API
Odysseum's API is described at **http://localhost:5080/scalar** when running locally, or **http://localhost:5081/scalar** with Compose. Documentation is available in every environment. The API runs independently and does not serve the frontend.

## Tests

```sh
dotnet run --project tests/Odysseum.StorageChecks -c Release
```

Frontend and API integration tests live in `odysseum-web/tests` and exercise both services together.

## Working with both repositories

The `odysseum-web/` checkout is ignored here. Commit and push frontend changes from that directory; commit and push API changes from this repository. Each checkout has its own remote, so the two can be pushed independently.

## License
[GNU AGPL v3](LICENSE)
