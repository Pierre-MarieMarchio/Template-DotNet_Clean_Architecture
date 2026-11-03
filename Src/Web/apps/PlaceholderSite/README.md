# PlaceholderSite

A static page standing where a web front end goes, served by nginx, which relays `/api/` and
`/health` to the API so the browser talks to a single origin.

| File | What it is |
|---|---|
| `public/` | the files nginx serves; replace them with the front end's build output |
| `nginx.conf` | the server: page security headers, caching, the `index.html` fallback, the relay |
| `Dockerfile` | the image, built from `Src/Web` |
| `compose.yml` | the `web-placeholder-site` service, started with the rest of the stack |

## Run it

From the project root, with the stack's `.env` in place:

```bash
docker compose up --build --wait
```

The page answers on <http://localhost:8081>, or on the port `WEB_PLACEHOLDER_SITE_HOST_PORT` sets in
`.env`. The relay is the API itself: <http://localhost:8081/health> answers from it.

The image alone, from the project root:

```bash
docker build -f Src/Web/apps/PlaceholderSite/Dockerfile -t placeholder-site Src/Web
```

`API_UPSTREAM` (default `http://api:8080`) tells the container where the API answers.

## Join the stack

`docker compose up` starts the app once `docker-compose.yml` includes it and `.env.example` names
its port, the two lines listed under "Joining a project generated without one" below.

Removing the app is deleting this folder and those two lines.

## Joining a project generated without one

A project whose workspace holds other apps takes this one later, at the project root:

```bash
dotnet new cleanarch-webapp -n PlaceholderSite --framework placeholder
dotnet run Tools/Tasks.cs web-join PlaceholderSite --web placeholder
```

`Src/Web/README.md` gives the first app's command, with the workspace. The lines `--web placeholder`
writes for this app in files the item template cannot edit are listed below, each placed by the
lines it goes beside. `web-join` makes them all, leaves those in place already, or makes none when
one of those lines is not found exactly once, then records the app in `cleanarch.json`:

- in `docker-compose.yml`, after the line:

  ```yaml
  include:
  ```

  add:

  ```yaml
    - path: Src/Web/apps/PlaceholderSite/compose.yml
  ```

- in `.env.example`, after the line:

  ```bash
  # Web apps, loopback only: one port per app, named after its folder.
  ```

  add:

  ```bash
  WEB_PLACEHOLDER_SITE_HOST_PORT=8081
  ```
