#!/usr/bin/env bash
set -euo pipefail

# ── Linux user + app dir ──────────────────────────────────────────────────────
sudo useradd --system --home /opt/ryveswift --shell /usr/sbin/nologin ryveswift 2>/dev/null || true
sudo install -d -m 755 -o ryveswift -g ryveswift /opt/ryveswift

# ── Env file ──────────────────────────────────────────────────────────────────
sudo install -d -m 750 -o root -g root /etc/ryveswift

sudo tee /etc/ryveswift/env > /dev/null << 'ENVEOF'
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://127.0.0.1:5191

App__Name=RyveSwift
App__PublicBaseUrl=https://swift.ryvepos.com
RyveSwift__PublicBaseUrl=https://swift.ryvepos.com

ConnectionStrings__Postgres=Host=127.0.0.1;Port=5432;Database=ryveswift;Username=ryveswift_app;Password=DbqyExcizm0gxIUsnf6zMMB5eNgrnD7vrkwtiTakQpJKvMbj;Include Error Detail=false;Maximum Pool Size=20;Minimum Pool Size=0

Auth__Jwt__Issuer=RyveSwift
Auth__Jwt__Audience=RyveSwift
Auth__Jwt__SigningKey=Wmkr1nVjgKImpKqNlO+Uip91tE0HVJo8VZlk4oVt/LV4cCKmKqULsD+iPaHrAOx1
Auth__Jwt__AccessTokenMinutes=60
Auth__Jwt__RefreshTokenDays=30

Secrets__EncryptionKey=m8O4pYiHCtTcWUPNwSZlVEhg7K1RuGQM
ENVEOF

sudo chown root:root /etc/ryveswift/env
sudo chmod 600 /etc/ryveswift/env

echo "[1/4] env file written"

# ── PostgreSQL ────────────────────────────────────────────────────────────────
sudo -u postgres psql -v ON_ERROR_STOP=1 << 'SQLEOF'
DO $$
BEGIN
  IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'ryveswift_app') THEN
    CREATE ROLE ryveswift_app LOGIN PASSWORD 'DbqyExcizm0gxIUsnf6zMMB5eNgrnD7vrkwtiTakQpJKvMbj';
  ELSE
    ALTER ROLE ryveswift_app WITH PASSWORD 'DbqyExcizm0gxIUsnf6zMMB5eNgrnD7vrkwtiTakQpJKvMbj';
  END IF;
END $$;
SQLEOF

sudo -u postgres psql -tc "SELECT 1 FROM pg_database WHERE datname='ryveswift'" | grep -q 1 \
  || sudo -u postgres createdb -O ryveswift_app ryveswift

sudo -u postgres psql -c "GRANT ALL PRIVILEGES ON DATABASE ryveswift TO ryveswift_app;"
sudo -u postgres psql -d ryveswift -c "GRANT ALL ON SCHEMA public TO ryveswift_app;" || true

echo "[2/4] postgres ready"

# ── systemd service ───────────────────────────────────────────────────────────
sudo tee /etc/systemd/system/ryveswift.service > /dev/null << 'SVCEOF'
[Unit]
Description=RyveSwift API
After=network.target postgresql.service

[Service]
Type=exec
User=ryveswift
Group=ryveswift
WorkingDirectory=/opt/ryveswift
ExecStart=/usr/bin/dotnet /opt/ryveswift/RyveSwift.Api.dll
EnvironmentFile=/etc/ryveswift/env
Restart=always
RestartSec=10
KillSignal=SIGINT
SyslogIdentifier=ryveswift
TimeoutStopSec=30

NoNewPrivileges=true
ProtectSystem=strict
ProtectHome=true
PrivateTmp=true

[Install]
WantedBy=multi-user.target
SVCEOF

sudo systemctl daemon-reload
sudo systemctl enable ryveswift

echo "[3/4] systemd service installed and enabled"

# ── Cloudflare tunnel ingress ─────────────────────────────────────────────────
TUNNEL_CFG=/etc/cloudflared/config.yml
if [ -f "$TUNNEL_CFG" ]; then
  if ! grep -q 'swift.ryvepos.com' "$TUNNEL_CFG"; then
    sudo sed -i 's|  - service: http_status:404|  - hostname: swift.ryvepos.com\n    service: http://127.0.0.1:5191\n  - service: http_status:404|' "$TUNNEL_CFG"
    echo "[4/4] cloudflared ingress rule added"
  else
    echo "[4/4] cloudflared ingress rule already present"
  fi
else
  echo "[4/4] WARNING: $TUNNEL_CFG not found — add ingress rule manually"
fi
