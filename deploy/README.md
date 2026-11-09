# Deploying AjoCircle

One Linux server (or VM) with Docker runs everything: PostgreSQL, the API, the web app (nginx) and a nightly
backup job. Cloudflare can sit in front through a Cloudflare Tunnel, so the server needs no open inbound ports.

```
visitor ──https──▶ Cloudflare ──tunnel──▶ cloudflared ──▶ web (nginx :8080) ──/api──▶ api (:8080) ──▶ postgres
                                                           └── static React app
```

The web app and the API share one address. The session cookie is `SameSite=Strict` and the app makes no
cross-origin calls, so don't split them across two domains.

> **Before real users:** the items under "Still open" in the main README (SMS vendor, legal review of the
> privacy notice, lawful basis for identity data) are not solved by deploying. Without an SMS vendor nobody can
> verify a phone number, so nobody can create or join a group.

## 1. First deploy

```bash
git clone https://github.com/Mannyfa/Ajo-Circle.git && cd Ajo-Circle
cp deploy/.env.example deploy/.env      # then fill it in (see below)
docker compose -f deploy/docker-compose.prod.yml --env-file deploy/.env --profile tunnel up -d --build
docker compose -f deploy/docker-compose.prod.yml ps   # api should say (healthy)
```

Leave out `--profile tunnel` if you aren't using Cloudflare; the site is then on `http://<server>:WEB_PORT` and you
must put your own HTTPS in front of it (the session cookie is only sent over HTTPS outside development).

Database migrations run automatically when the API starts.

### Settings in `deploy/.env`

| Setting | What it's for |
|---|---|
| `POSTGRES_PASSWORD` | Long random password. Generate with `openssl rand -base64 32`. |
| `PUBLIC_BASE_URL` | The address people use, e.g. `https://app.example.com`. Used in email links. |
| `SMTP_*` | Your email provider. Until set, sign-up and password-reset codes are refused, not silently dropped. |
| `CLIENT_IP_HEADER` | `CF-Connecting-IP` behind Cloudflare; `X-Forwarded-For` otherwise. See "Rate limits" below. |
| `DATA_PROTECTION_CERT_*` | Optional but recommended. See "Encryption keys". |
| `CLOUDFLARE_TUNNEL_TOKEN` | From Cloudflare Zero Trust → Networks → Tunnels. |

## 2. Cloudflare

1. Add your domain to Cloudflare.
2. Zero Trust → Networks → Tunnels → **Create a tunnel** (cloudflared). Copy the token into `CLOUDFLARE_TUNNEL_TOKEN`.
3. Add a public hostname, e.g. `app.example.com`, with service **HTTP** `web:8080`.
4. Set `CLIENT_IP_HEADER=CF-Connecting-IP` and `PUBLIC_BASE_URL=https://app.example.com`, then re-run the
   `docker compose ... up -d` command.
5. SSL/TLS → Edge Certificates: turn on **Always Use HTTPS**.

With the tunnel there's no need to open ports 80/443 on the server; keep the firewall closed except SSH. If you
also publish `WEB_PORT` to the internet, someone could bypass Cloudflare and fake the `CF-Connecting-IP` header,
so either don't publish it or firewall it to Cloudflare's IP ranges.

## 3. Rate limits and the visitor's address

Sign-in, verification, invitation and dispute endpoints are rate-limited per visitor. Behind a proxy the API sees
the proxy's address, so it reads the visitor's address from `CLIENT_IP_HEADER`, and only when the request came from
the web container (`ReverseProxy:TrustedNetworks`). nginx overwrites `X-Forwarded-For`, so a visitor can't pick
their own address. If this is set wrong, everyone shares one limit and one person's failed logins lock out everybody.

## 4. Encryption keys (important)

Session cookies, two-step sign-in secrets, ID numbers, BVNs and uploaded ID documents are encrypted with keys that
are stored in the database (`data_protection_keys` table), so they survive restarts and redeploys and are included in
the backups. **If that table is lost, the encrypted data can never be read again.**

By default the keys are stored unencrypted inside the database (the API logs a warning saying so), which means a copy
of the database is enough to decrypt the data. To close that gap, give the keys their own certificate:

```bash
openssl req -x509 -newkey rsa:3072 -nodes -days 3650 -subj "/CN=AjoCircle Data Protection" \
  -keyout dp.key -out dp.crt
openssl pkcs12 -export -inkey dp.key -in dp.crt -out deploy/secrets/dataprotection.pfx   # choose a password
rm dp.key dp.crt
```

Then set `DATA_PROTECTION_CERT_PATH=/run/secrets/ajocircle/dataprotection.pfx` and the password. Back up the `.pfx`
and its password somewhere separate from the database backups (a password manager). Losing the certificate has the
same effect as losing the keys. Do this before any real data goes in; keys created earlier stay readable.

## 5. Backups

The `backup` container writes one compressed dump per day to `BACKUP_DIR` (default `deploy/backups/`) and deletes
dumps older than `BACKUP_KEEP_DAYS`. Dumps on the same server don't survive the server, so **copy them off the
machine** (e.g. a nightly `rclone copy deploy/backups r2:ajocircle-backups` to Cloudflare R2 or S3).

Check that backups restore, before you need them:

```bash
docker compose -f deploy/docker-compose.prod.yml exec -T postgres createdb -U ajocircle restore_check
docker compose -f deploy/docker-compose.prod.yml exec -T postgres pg_restore -U ajocircle -d restore_check < deploy/backups/<file>.dump
docker compose -f deploy/docker-compose.prod.yml exec -T postgres psql -U ajocircle -d restore_check -c "select count(*) from users"
docker compose -f deploy/docker-compose.prod.yml exec -T postgres dropdb -U ajocircle restore_check
```

## 6. Operations (staff) accounts

Staff access is never granted through the website. The person registers a normal account first, then on the server:

```bash
docker compose -f deploy/docker-compose.prod.yml exec api dotnet AjoCircle.Api.dll grant-operations person@example.com
docker compose -f deploy/docker-compose.prod.yml exec api dotnet AjoCircle.Api.dll revoke-operations person@example.com
```

Both are written to the audit log. The person must set up two-step sign-in before the operations workspace opens.

## 7. Updating

```bash
git pull
docker compose -f deploy/docker-compose.prod.yml --env-file deploy/.env --profile tunnel up -d --build
```

Take a backup first if the update includes a database migration (new files under
`src/AjoCircle.Infrastructure/Persistence/Migrations/`).

## 8. Monitoring

- `GET /api/v1/health`: the API process is up.
- `GET /api/v1/health/ready`: the API can also reach the database (503 if not). Point an uptime monitor at
  `https://app.example.com/api/v1/health/ready`.
- Logs: `docker compose -f deploy/docker-compose.prod.yml logs -f api`. They never contain codes, tokens or
  identity data.
