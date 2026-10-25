#!/usr/bin/env bash
set -euo pipefail

usage() {
    cat <<'USAGE'
Usage: sudo bash server/oracle/install.sh --public-ip IPV4 --email EMAIL --agree-tos [--configure-firewall]
       bash server/oracle/install.sh --public-ip IPV4 --email EMAIL --plan

Installs ZoneZ on a dedicated Ubuntu 24.04 amd64/arm64 VM. Creates a non-login
zonez service user, installs current Certbot via the official Snap, requests a
public Let's Encrypt IP certificate, and enables certificate renewal.

--agree-tos explicitly accepts Let's Encrypt's current subscriber agreement.
--configure-firewall adds INPUT TCP80/7780 rules without resetting other rules.
--plan validates local input and prints the plan without changing the system.
OCI security rules (TCP80 and7780) must already allow public inbound traffic.
The standalone ACME listener needs port80 free. No Oracle resources are created.
USAGE
}

zonez_ip=''
zonez_email=''
zonez_agree=false
zonez_firewall=false
zonez_plan=false
while (($#)); do
    case "$1" in
        --public-ip) [[ $# -ge 2 ]] || { usage >&2; exit 2; }; zonez_ip=$2; shift 2 ;;
        --email) [[ $# -ge 2 ]] || { usage >&2; exit 2; }; zonez_email=$2; shift 2 ;;
        --agree-tos) zonez_agree=true; shift ;;
        --configure-firewall) zonez_firewall=true; shift ;;
        --plan) zonez_plan=true; shift ;;
        -h|--help) usage; exit 0 ;;
        *) printf 'Unknown option: %s\n' "$1" >&2; usage >&2; exit 2 ;;
    esac
done

zonez_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)
zonez_source="$zonez_dir/../lobby_server.py"
[[ -f "$zonez_source" ]] || { printf 'Missing ../lobby_server.py next to installer.\n' >&2; exit 1; }
command -v python3 >/dev/null || { printf 'Python3 is required by the Ubuntu installer.\n' >&2; exit 1; }
python3 - "$zonez_ip" "$zonez_email" <<'PY'
import ipaddress, re, sys
try:
    ip = ipaddress.IPv4Address(sys.argv[1])
    assert ip.is_global
    assert re.fullmatch(r"[^\s@]+@[^\s@]+\.[^\s@]+", sys.argv[2])
except (ValueError, AssertionError):
    raise SystemExit("Provide the VM's public IPv4 and a valid contact email.")
PY
if "$zonez_plan"; then
    printf 'Plan: Ubuntu24.04; publicIP=%s; TLS7780; standaloneACME80;64clients; non-root service;6h renewal timer.\n' "$zonez_ip"
    printf 'Add-only host firewall requested: %s\n' "$zonez_firewall"
    exit 0
fi
[[ $EUID -eq 0 ]] || { printf 'Run installation with sudo.\n' >&2; exit 1; }
"$zonez_agree" || { printf 'Read the subscriber agreement, then add --agree-tos: https://letsencrypt.org/repository/\n' >&2; exit 1; }
# shellcheck source=/dev/null
. /etc/os-release
[[ ${ID:-} == ubuntu && ${VERSION_ID:-} == 24.04 ]] || { printf 'Supported OS: Ubuntu24.04 only.\n' >&2; exit 1; }
case "$(dpkg --print-architecture)" in amd64|arm64) ;; *) printf 'Supported architectures: amd64,arm64.\n' >&2; exit 1 ;; esac
if dpkg-query -W -f='${Status}' certbot 2>/dev/null | grep -q '^install ok installed$'; then
    printf 'An apt Certbot already exists. Use a clean VM or migrate it to Snap before this installer.\n' >&2
    exit 1
fi
for zonez_path in /opt/zonez /opt/zonez/server /etc/zonez /etc/zonez/tls; do
    [[ ! -L "$zonez_path" ]] || { printf 'Refusing a symlink installation path: %s\n' "$zonez_path" >&2; exit 1; }
done
if [[ -f /etc/zonez/public-ip ]] && [[ $(cat /etc/zonez/public-ip) != "$zonez_ip" ]]; then
    printf 'The installed public IP differs. Migrate the existing certificate deliberately before changing the endpoint.\n' >&2
    exit 1
fi
if id zonez >/dev/null 2>&1; then
    [[ $(id -u zonez) -ne 0 && $(getent passwd zonez | cut -d: -f7) == /usr/sbin/nologin ]] || {
        printf 'Existing zonez account is not the expected non-login service user.\n' >&2; exit 1;
    }
fi

export DEBIAN_FRONTEND=noninteractive
apt-get update
apt-get install -y --no-install-recommends python3 ca-certificates snapd openssl iproute2
if [[ -n $(ss -H -ltn 'sport = :80') ]]; then
    printf 'Port80 is already in use; standalone ACME cannot run. Nothing has been stopped.\n' >&2
    exit 1
fi
systemctl enable --now snapd.socket
timeout 300 snap wait system seed.loaded
if snap list certbot >/dev/null 2>&1; then snap refresh certbot --channel=latest/stable; else snap install --classic certbot; fi
/snap/bin/certbot --version | python3 -c 'import re,sys; v=re.search(r"(\d+)\.(\d+)\.(\d+)",sys.stdin.read()); assert v and tuple(map(int,v.groups())) >= (5,4,0), "Certbot5.4+ is required for IP certificates"'
getent group zonez >/dev/null || groupadd --system zonez
id zonez >/dev/null 2>&1 || useradd --system --gid zonez --home-dir /nonexistent --no-create-home --shell /usr/sbin/nologin zonez
install -d -o root -g root -m 0755 /opt/zonez/server /usr/local/libexec
install -d -o root -g zonez -m 0750 /etc/zonez /etc/zonez/tls
printf '%s\n' "$zonez_ip" > /etc/zonez/public-ip
chown root:zonez /etc/zonez/public-ip
chmod 0640 /etc/zonez/public-ip
install -o root -g root -m 0644 "$zonez_source" /opt/zonez/server/lobby_server.py
install -o root -g root -m 0750 "$zonez_dir/install-certificate.py" /usr/local/libexec/zonez-install-certificate
install -o root -g root -m 0750 "$zonez_dir/renew-certificate.sh" /usr/local/libexec/zonez-renew-certificate
install -o root -g root -m 0644 "$zonez_dir/zonez-lobby.service" /etc/systemd/system/zonez-lobby.service
install -o root -g root -m 0644 "$zonez_dir/zonez-certbot-renew.service" /etc/systemd/system/zonez-certbot-renew.service
install -o root -g root -m 0644 "$zonez_dir/zonez-certbot-renew.timer" /etc/systemd/system/zonez-certbot-renew.timer
if "$zonez_firewall"; then
    apt-get install -y --no-install-recommends iptables
    install -o root -g root -m 0750 "$zonez_dir/firewall.sh" /usr/local/libexec/zonez-firewall
    install -o root -g root -m 0644 "$zonez_dir/zonez-firewall.service" /etc/systemd/system/zonez-firewall.service
fi
systemctl daemon-reload
systemd-analyze verify /etc/systemd/system/zonez-lobby.service /etc/systemd/system/zonez-certbot-renew.service /etc/systemd/system/zonez-certbot-renew.timer
if "$zonez_firewall"; then systemctl enable --now zonez-firewall.service; fi
/snap/bin/certbot certonly --standalone --non-interactive --agree-tos --email "$zonez_email" \
    --cert-name zonez-ip --ip-address "$zonez_ip" --preferred-profile shortlived --required-profile shortlived \
    --http-01-address 0.0.0.0 --preferred-challenges http-01 --key-type rsa --rsa-key-size 2048 \
    --deploy-hook /usr/local/libexec/zonez-install-certificate
/usr/local/libexec/zonez-install-certificate --lineage /etc/letsencrypt/live/zonez-ip
systemctl enable zonez-lobby.service
systemctl restart zonez-lobby.service
systemctl enable --now zonez-certbot-renew.timer
bash "$zonez_dir/verify.sh"
printf '\nReady locally: %s:7780 with TLS enabled. Verify this endpoint from a separate network.\n' "$zonez_ip"
