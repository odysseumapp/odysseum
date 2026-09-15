# Odysseum

Self-hosted novel and book writing software.

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
docker compose up --build -d
```

Open **http://localhost:5080**. The container uses two directories:

| Container path | Host default |  |
|---|---|---|
| `/projects` | `./projects` | your projects |
| `/data` | `./data` | `server-settings.json`, authentication keys |

Set `ODYSSEUM_DEMO` to `false` to start with an empty workspace.

To access Odysseum remotely, a reverse proxy is recommended. Configure `ODYSSEUM_PASSWORD` in a `.env` file.

## Run locally on Windows

Requires the .NET 10 SDK, Node.js 20.19+ or 22.12+, and npm.

```powershell
cd web
npm ci
cd ..
./scripts/build.ps1
./scripts/start.ps1
```

Open **http://localhost:5080**. To open a different directory:

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
Odysseum's API is described at **http://localhost:5080/scalar**.

## License
[GNU AGPL v3](LICENSE)
