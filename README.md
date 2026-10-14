# Domain Dashboard for Traefik (.NET 10)

Shows every domain that Traefik routes on a single page and regularly checks
whether it is reachable: HTTP status, response time, certificate (validity, name,
expiry) and the router status reported by Traefik. New domains show up automatically,
whether they come from Docker labels, the file provider (VMs, LXCs) or anywhere else.

Blazor Web App (Interactive Server) with [MudBlazor](https://mudblazor.com) as the component library. The page
is updated live via SignalR as soon as a check finishes.

## Structure

```
src/DomainDashboard/
  Program.cs                      Request pipeline
  Api/
    DashboardEndpoints.cs         JSON API (/api/status, /api/refresh, /healthz)
  Services/
    ServiceCollectionExtensions.cs  DI registration (AddDashboard)
    DashboardOptions.cs           Settings ("Dashboard" section)
    TraefikClient.cs              Reads the routers from the Traefik API
    RouterParser.cs               Turns host rules into domains
    DomainChecker.cs              HTTP/TLS check of a domain, status logic
    RefreshWorker.cs              BackgroundService: interval, parallel checks
    StatusStore.cs                Latest state, Changed event, "Check now" signal
    DomainState.cs, DomainStatus.cs, StatusSnapshot.cs   Models
  Components/
    Pages/Home.razor(.cs)         Dashboard page (stats, filter, search, table)
    Dashboard/DomainRow.razor(.cs)   Cells of a table row
    Dashboard/CheckClock.razor(.cs)  "Checked at …, next check in … s"
```

Razor components keep their markup in `.razor` and their logic in a `.razor.cs` code-behind file.

Blazor Server keeps one WebSocket connection open per browser tab. Traefik passes
WebSockets through without any extra configuration.

## 1. Enable the Traefik API

In the static Traefik configuration (`traefik.yml`):

```yaml
api:
  dashboard: true
  insecure: true   # API on port 8080 (entrypoint "traefik")
```

`insecure` only means the API is reachable on port 8080 without a login. Do not
expose port 8080 to the outside. Test it: `curl http://<traefik>:8080/api/http/routers`

If the API is protected by a router with basic auth, set
`Dashboard__TraefikApiUser` and `Dashboard__TraefikApiPassword`.

## 2. Set the address

- Traefik as a Docker container in the same network: `http://traefik:8080`, and
  enter the network name under `networks`.
- Traefik in another VM/LXC: `http://192.168.x.y:8080`; remove the `networks` block
  if there is no shared Docker network.

## 3. Start

The compose file uses the prebuilt image `ghcr.io/ashryverelisa/domaindashboard:latest`
(linux/amd64 and linux/arm64), which GitHub Actions builds on every push to `main`.
Version tags such as `v1.2.0` additionally publish `1.2.0` and `1.2`.

```bash
docker compose pull
docker compose up -d
docker logs -f domain-dashboard
```

To build the image yourself, swap `image:` for `build: .` in the compose file and run
`docker compose up -d --build`.

Locally without Docker (requires SDK 10):

```bash
cd src/DomainDashboard
dotnet run
```

The values from `appsettings.json` apply; for development you can create an
`appsettings.Development.json`.

## Settings

All values live in the `Dashboard` section of `appsettings.json` and can be
overridden with the environment variable `Dashboard__<Name>`.

| Name | Default | Meaning |
|---|---|---|
| `TraefikApiUrl` | `http://traefik:8080` | Address of the Traefik API |
| `TraefikApiUser` / `TraefikApiPassword` | empty | Basic auth for the API |
| `TraefikApiVerifySsl` | `true` | Verify the API certificate (for https) |
| `RefreshInterval` | `60` | Seconds between checks (min. 10) |
| `CheckTimeout` | `5` | Timeout per domain in seconds |
| `CheckWorkers` | `16` | Parallel checks |
| `CheckViaIp` | empty | Send all checks to this IP (see below) |
| `HttpsPort` / `HttpPort` | `443` / `80` | Ports used for checks |
| `CaFile` | empty | Custom CA so internal certificates count as valid |
| `CertWarnDays` | `14` | When an expiring certificate turns yellow |
| `ExcludeDomains` | empty | Regex; matching domains are hidden |
| `ExcludeRouters` | empty | Regex on router names, e.g. `redirect` |
| `Title` | `Network Domains` | Page title |

## How the status is decided

- **Green:** Response with 2xx, 3xx, 401 or 403 (401/403 means the service is running, e.g. behind Authelia).
- **Yellow:** Reachable, but with a warning: another 4xx response, an invalid or soon-to-expire
  certificate, a name that does not match the certificate, or Traefik reports the router as `warning`/`disabled`.
- **Red:** No response, DNS error, connection refused or 5xx (e.g. 502 when the backend is gone).

## Common problems

**All domains red with "Name could not be resolved".** The container does not know your
local DNS (Pi-hole, AdGuard, router). Either set `dns: [192.168.x.y]` in the compose file,
or set `Dashboard__CheckViaIp` to the IP of Traefik. Every check then goes straight
to Traefik, with the correct Host header and SNI.

**Certificates are shown as untrusted.** This does not happen with Let's Encrypt
certificates. For a custom CA, mount the CA file and set `Dashboard__CaFile`.

## API

- `GET /api/status`: all results as JSON (snake_case), e.g. for homepage widgets or scripts
- `POST /api/refresh`: check again immediately
- `GET /healthz`: health check
