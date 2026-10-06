# Contributing

Thanks for your interest in Odysseum. It is a hobby project in early development, so formats and APIs still change often.

## Repositories

- [odysseum](https://github.com/odysseumapp/odysseum): the .NET server, plugin abstractions, desktop app and docs. Open issues here.
- [odysseum-web](https://github.com/odysseumapp/odysseum-web): the Nuxt frontend.

Clone both side by side as `odysseum-server` and `odysseum-web`. The build scripts and web tests look for each other there; otherwise pass `-WebRoot` to the build scripts or set `ODYSSEUM_SERVER_ROOT` for the web tests.

```sh
git clone https://github.com/odysseumapp/odysseum.git odysseum-server
git clone https://github.com/odysseumapp/odysseum-web.git
```

## Before you start

For anything larger than a small fix, open an issue first so we can agree on the approach.

## Pull requests

- Keep each pull request to one change.
- Say how you tested it. Add a screenshot for UI changes.
- If a change needs both repositories, open a pull request in each and link them.

## Docs

- [Project architecture](docs/project-architecture.md)
- [Project format](docs/project-format.md)
- [Server services](docs/server-services.md)

## License

By contributing, you agree that your contributions are licensed under the [GNU AGPL v3](LICENSE).
