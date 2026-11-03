# Jellystat API

> [!TIP]
> **Looking to install Jellystat?** Head to [JellyStat/Jellystat](https://github.com/JellyStat/Jellystat) for install instructions, and to report bugs or request features. This repo is for developing the backend.

The .NET backend for [Jellystat](https://github.com/JellyStat/Jellystat). It talks to Jellyfin and Emby, stores data in PostgreSQL, runs background tasks (library sync, backups, scheduled reports) and serves the [web UI](https://github.com/JellyStat/Jellystat-UI).

## Development

- [DEVELOPMENT_USER_SECRETS.md](DEVELOPMENT_USER_SECRETS.md): setting up local configuration.
- [EF_CORE_README.md](EF_CORE_README.md): database migrations with Entity Framework Core.
- Every environment variable is listed under [Environment Variables](https://github.com/JellyStat/Jellystat#environment-variables) in the main repo.

## Docker image

Each push to `main` builds `ghcr.io/jellystat/jellystat-api-core`. The build downloads the latest [Jellystat-UI](https://github.com/JellyStat/Jellystat-UI) release into `wwwroot`, so the image serves both the API and the UI.

## Contributing

See the [contributing guide](https://github.com/JellyStat/.github/blob/main/CONTRIBUTING.md).

## Licence

[GNU General Public License v3.0](LICENSE)
