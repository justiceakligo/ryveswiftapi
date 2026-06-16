# RyveSend Frontend Deployment Handoff

This document explains how to deploy the RyveSend frontend web project to the existing RyveServe production droplet and expose it at:

```text
https://ryvesend.com
https://www.ryvesend.com
```

The intended frontend project name is `RyveSend`.

## Executive Summary

Host RyveSend as a static production build on the existing DigitalOcean droplet `ryveserve-prod`.

Recommended deployment shape:

- Build the frontend into static assets.
- Copy only the build output to the server.
- Serve the files with the existing Caddy service.
- Route `ryvesend.com` and `www.ryvesend.com` through the existing Cloudflare Tunnel to Caddy.
- Keep the API on `https://swift.ryvepos.com` unless the team intentionally adds same-origin `/api/*` proxying later.

Do not run a permanent Node.js process for the public frontend unless the app requires SSR. A static build is the right fit for the current server capacity and architecture.

## Current Production Server

Server checked on 2026-06-15.

| Item | Value |
|------|-------|
| DigitalOcean droplet | `ryveserve-prod` |
| Public IP | `138.197.132.118` |
| Region | `tor1` |
| Size | `s-2vcpu-2gb` |
| OS | Ubuntu 24.04 LTS |
| Disk | 60 GB |
| Existing web server | Caddy |
| Existing edge tunnel | Cloudflare Tunnel |
| Existing API for RyveSend/RyveSwift | `https://swift.ryvepos.com` |
| Existing API local port | `127.0.0.1:5191` |

Live capacity check:

| Metric | Observed |
|--------|----------|
| Memory | 1.9 GiB total, about 944 MiB available |
| Swap | 2.0 GiB total, about 15 MiB used |
| Disk | 58 GB root filesystem, 8.8 GB used, 49 GB available |
| CPU | 98-99% idle during sample |
| API health | `https://swift.ryvepos.com/health` returned HTTP 200 |

Conclusion: the droplet has enough headroom for another static frontend. Static Caddy hosting should add minimal memory and CPU load.

## Current Server Layout

Active services currently include:

```text
caddy.service
cloudflared.service
postgresql@16-main.service
ryveswift.service
ryvepay.service
ryveserve.service
```

Current local listeners include:

```text
127.0.0.1:5188  RyveServe API
127.0.0.1:5190  RyvePay API
127.0.0.1:5191  RyveSwift/RyveSend API
*:5173          Caddy static RyvePOS public web
*:5174          Caddy static RyvePOS admin web
```

Use `5175` for the new RyveSend frontend to avoid colliding with existing services.

Proposed new layout:

```text
/opt/ryvesend-web/current
/opt/ryvesend-web/releases/<timestamp>
```

Caddy should serve:

```text
127.0.0.1:5175 -> /opt/ryvesend-web/current
```

Cloudflared should route:

```text
ryvesend.com      -> http://127.0.0.1:5175
www.ryvesend.com  -> http://127.0.0.1:5175
```

## Domain Status

At the time this handoff was written, `https://ryvesend.com` did not respond on port 443 from the local check. Treat DNS/Cloudflare routing as a launch prerequisite.

The team must configure Cloudflare DNS and Tunnel public hostnames before the domain will work.

## Frontend Build Requirements

The frontend should be deployed as static files.

Expected examples:

| Framework | Build command | Static output |
|-----------|---------------|---------------|
| Vite/React | `npm run build` | `dist/` |
| Next.js static export | `next build && next export` or configured static output | `out/` |
| CRA | `npm run build` | `build/` |

If RyveSend is a Vite app, use:

```text
VITE_API_BASE_URL=https://swift.ryvepos.com
VITE_APP_BASE_URL=https://ryvesend.com
VITE_STRIPE_PUBLISHABLE_KEY=<publishable key only>
```

If the frontend uses different environment variable names, map them to the same values. Do not expose any secret keys in the frontend build.

The frontend must not hardcode localhost, staging URLs, or private droplet IPs.

## API Configuration

The production API is currently:

```text
https://swift.ryvepos.com
```

The frontend should call API endpoints using that base URL, for example:

```text
GET  https://swift.ryvepos.com/health
POST https://swift.ryvepos.com/api/auth/login
POST https://swift.ryvepos.com/api/quotes
POST https://swift.ryvepos.com/api/bookings/confirm
```

The backend currently allows cross-origin requests, so hosting the frontend on `https://ryvesend.com` and the API on `https://swift.ryvepos.com` is acceptable.

Important backend config to update before launch:

```text
App:FrontendBaseUrl = https://ryvesend.com
```

This controls password reset links:

```text
https://ryvesend.com/reset-password?token=...
```

Keep this value as the API URL unless the backend is changed to use frontend-hosted unsubscribe pages:

```text
App:PublicBaseUrl = https://swift.ryvepos.com
```

`App:PublicBaseUrl` is used for API-hosted email preference links such as `/api/email/unsubscribe`.

## Required Frontend Routes

The static app should support these routes, either as real routes or SPA client routes:

```text
/
/login
/register
/forgot-password
/reset-password
/quotes
/shipments
/shipments/:id
/admin
```

At minimum, `reset-password` must exist before changing `App:FrontendBaseUrl` to `https://ryvesend.com`, because password reset emails will link there.

Caddy will fall back unknown paths to `index.html`, so client-side routing will work if the frontend app defines the routes.

## DHL Certification UI Requirements

The frontend must respect the DHL certification rules already enforced by the API.

Quote form:

- Show only `DHL Express Worldwide`.
- Do not expose DHL service selection to customers.
- Do not offer document shipments.
- Send `shipmentType: "parcel"` only.
- Block domestic shipments when origin and destination countries match.
- Handle `UNSUPPORTED_DHL_SERVICE` by showing the backend message.

Customs form:

- Require line-item customs details for international parcels.
- Require accurate item descriptions.
- Require real 6- to 10-digit HS codes.
- Reject placeholder HS codes such as `000000` and `999999`.
- Reject vague descriptions such as `Gift`, `Clothes`, `Sample`, `Goods`, `General Goods`, `Misc`, `Package`, and `Fashion item`.

Address forms:

- Do not insert `00000` as a postal-code fallback.
- For `addressLine3`, label it as `County / Suburb / District`.
- Do not label `addressLine3` as a normal street-address line.

Booking:

- Keep using `POST /api/bookings/confirm`.
- Poll `GET /api/shipments/{id}` after payment confirmation until the booking state moves past `paid`.
- Use the API base URL when opening document links returned as relative paths.

## Caddy Configuration

Add this block to `/etc/caddy/Caddyfile` on the droplet:

```caddyfile
127.0.0.1:5175 {
    root * /opt/ryvesend-web/current
    encode gzip zstd

    @sw path /sw.js /manifest.webmanifest
    header @sw Cache-Control "no-cache, no-store, must-revalidate"

    @assets path /assets/* *.png *.jpg *.jpeg *.webp *.svg *.woff *.woff2 *.ico *.css *.js
    handle @assets {
        header Cache-Control "public, max-age=31536000, immutable"
        file_server
    }

    handle {
        try_files {path} /index.html
        @index path /index.html
        header @index Cache-Control "no-cache, no-store, must-revalidate"
        file_server
    }
}
```

Validate and reload:

```bash
sudo caddy validate --config /etc/caddy/Caddyfile
sudo systemctl reload caddy
sudo systemctl status caddy --no-pager
```

Local server check:

```bash
curl -I http://127.0.0.1:5175
```

Expected result:

```text
HTTP/1.1 200 OK
```

## Cloudflare Tunnel Configuration

Edit `/etc/cloudflared/config.yml` and add the RyveSend routes before the final `http_status:404` rule:

```yaml
  - hostname: ryvesend.com
    service: http://127.0.0.1:5175
  - hostname: www.ryvesend.com
    service: http://127.0.0.1:5175
```

The final file should keep the catch-all rule last:

```yaml
ingress:
  - hostname: api.ryvepos.com
    service: http://127.0.0.1:5188
  - hostname: ryvepos.com
    service: http://127.0.0.1:5173
  - hostname: www.ryvepos.com
    service: http://127.0.0.1:5173
  - hostname: console.ryvepos.com
    service: http://127.0.0.1:5174
  - hostname: swift.ryvepos.com
    service: http://127.0.0.1:5191
  - hostname: pay.ryvepos.com
    service: http://127.0.0.1:5190
  - hostname: ryvesend.com
    service: http://127.0.0.1:5175
  - hostname: www.ryvesend.com
    service: http://127.0.0.1:5175
  - service: http_status:404
```

Validate and restart:

```bash
sudo cloudflared tunnel ingress validate --config /etc/cloudflared/config.yml
sudo systemctl restart cloudflared
sudo systemctl status cloudflared --no-pager
```

## Cloudflare DNS

Create DNS records for both hostnames.

If using Cloudflare Tunnel DNS routing, both records should point to the tunnel target:

```text
ryvesend.com      CNAME  <tunnel-id>.cfargotunnel.com
www.ryvesend.com  CNAME  <tunnel-id>.cfargotunnel.com
```

The current tunnel ID in the server config is:

```text
3bb0c7af-c9e3-473a-8740-eca8085166c7
```

That means the target should be:

```text
3bb0c7af-c9e3-473a-8740-eca8085166c7.cfargotunnel.com
```

Alternative, if the Cloudflare account has `cloudflared` authenticated on the droplet:

```bash
cloudflared tunnel route dns 3bb0c7af-c9e3-473a-8740-eca8085166c7 ryvesend.com
cloudflared tunnel route dns 3bb0c7af-c9e3-473a-8740-eca8085166c7 www.ryvesend.com
```

Do not point DNS directly at the droplet IP unless the team deliberately opens and secures public HTTP/HTTPS ports. The existing pattern uses Cloudflare Tunnel.

## First-Time Server Setup

Run these commands on the droplet before the first deploy:

```bash
sudo useradd --system --home /opt/ryvesend-web --shell /usr/sbin/nologin ryvesend-web 2>/dev/null || true
sudo install -d -m 755 -o ryvesend-web -g ryvesend-web /opt/ryvesend-web
sudo install -d -m 755 -o ryvesend-web -g ryvesend-web /opt/ryvesend-web/releases
```

Create the first empty `current` directory only if Caddy needs to validate before assets are deployed:

```bash
sudo install -d -m 755 -o ryvesend-web -g ryvesend-web /opt/ryvesend-web/releases/empty
sudo ln -sfn /opt/ryvesend-web/releases/empty /opt/ryvesend-web/current
```

## Deployment Procedure

These steps assume the frontend build output is `dist/`. Adjust if the project outputs `build/` or `out/`.

On the build machine:

```bash
npm ci
npm run build
tar -czf ryvesend-web.tar.gz -C dist .
```

Copy the artifact to the droplet:

```bash
scp ryvesend-web.tar.gz root@138.197.132.118:/tmp/ryvesend-web.tar.gz
```

On the droplet:

```bash
RELEASE="$(date -u +%Y%m%d%H%M%S)"
sudo install -d -m 755 -o ryvesend-web -g ryvesend-web "/opt/ryvesend-web/releases/$RELEASE"
sudo tar -xzf /tmp/ryvesend-web.tar.gz -C "/opt/ryvesend-web/releases/$RELEASE"
sudo chown -R ryvesend-web:ryvesend-web "/opt/ryvesend-web/releases/$RELEASE"
sudo ln -sfn "/opt/ryvesend-web/releases/$RELEASE" /opt/ryvesend-web/current
sudo systemctl reload caddy
```

Check locally:

```bash
curl -I http://127.0.0.1:5175
```

Check publicly after Cloudflare DNS and Tunnel routing are live:

```bash
curl -I https://ryvesend.com
curl -I https://www.ryvesend.com
```

## Rollback Procedure

List releases:

```bash
ls -lt /opt/ryvesend-web/releases
```

Point `current` back to the previous release:

```bash
sudo ln -sfn /opt/ryvesend-web/releases/<previous-release> /opt/ryvesend-web/current
sudo systemctl reload caddy
```

Verify:

```bash
curl -I http://127.0.0.1:5175
curl -I https://ryvesend.com
```

## Post-Deploy Smoke Test

Run these checks after deployment:

```bash
curl -I https://ryvesend.com
curl -s https://swift.ryvepos.com/health
curl -I https://ryvesend.com/reset-password
```

Manual browser checks:

- Home page loads at `https://ryvesend.com`.
- `https://www.ryvesend.com` either loads or redirects as intended.
- Hard refresh on a nested route works, for example `/reset-password`.
- Login form calls `https://swift.ryvepos.com/api/auth/login`.
- Quote form calls `https://swift.ryvepos.com/api/quotes`.
- Payment flow uses the publishable Stripe key only.
- No request goes to `localhost`, the droplet IP, or a staging API.
- Browser console has no CORS, mixed content, or missing asset errors.

## Backend Config Checklist

Before launch, confirm these backend values:

```text
App:FrontendBaseUrl = https://ryvesend.com
App:PublicBaseUrl   = https://swift.ryvepos.com
STRIPE_PUBLISHABLE_KEY is set and safe for frontend use
STRIPE_SECRET_KEY is set only in backend config
STRIPE_WEBHOOK_SECRET is set only in backend config
DHL_BASE_URL is the correct DHL environment
```

If the admin config API is used, update only non-secret public URLs through the admin UI/API. Secrets should be managed through the established production process.

If direct database access is used for the public frontend URL, run this on the droplet:

```bash
sudo -u postgres psql -d ryveswift -c "UPDATE \"AppConfigs\" SET \"Value\" = 'https://ryvesend.com', \"UpdatedAt\" = NOW() WHERE \"Key\" = 'App:FrontendBaseUrl';"
```

Then restart the API so the in-memory config is refreshed:

```bash
sudo systemctl restart ryveswift
sudo systemctl status ryveswift --no-pager
```

## Monitoring After Launch

Check server health immediately after launch:

```bash
free -h
df -h
vmstat 1 5
systemctl status caddy --no-pager
systemctl status cloudflared --no-pager
systemctl status ryveswift --no-pager
```

Expected after static frontend launch:

- Caddy remains low memory.
- CPU stays mostly idle under normal traffic.
- Swap does not grow materially.
- Disk remains well below 80% usage.

Investigate if:

- Available memory drops below 300 MiB for a sustained period.
- Swap grows rapidly.
- CPU load stays above the number of vCPUs.
- Caddy or cloudflared restarts repeatedly.
- API latency increases materially after frontend launch.

## Security Notes

- Do not deploy `.env` files to the public static directory.
- Do not expose backend secret keys, DHL credentials, Resend keys, or Stripe secret keys.
- The frontend may include only public browser-safe values such as Stripe publishable key and API base URL.
- Keep source maps disabled in production if they expose sensitive implementation details.
- Keep Cloudflare proxy/Tunnel enabled for `ryvesend.com`.
- Do not open direct public ports for Caddy unless the team intentionally changes the hosting model.

## Launch Checklist

- RyveSend frontend builds successfully.
- Production environment variables are set before build.
- Static artifact contains `index.html` and expected assets.
- `/opt/ryvesend-web/current` points to the latest release.
- Caddy has a `127.0.0.1:5175` site block.
- `caddy validate` passes.
- Cloudflared routes `ryvesend.com` and `www.ryvesend.com` to `127.0.0.1:5175`.
- Cloudflare DNS records exist for root and `www`.
- `https://ryvesend.com` returns HTTP 200.
- `https://www.ryvesend.com` returns HTTP 200 or redirects intentionally.
- `https://swift.ryvepos.com/health` returns HTTP 200.
- Password reset emails link to `https://ryvesend.com/reset-password`.
- DHL-certified frontend behavior is implemented.
- No browser console errors on core customer flows.
