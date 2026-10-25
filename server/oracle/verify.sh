#!/usr/bin/env bash
set -euo pipefail
[[ $EUID -eq 0 ]] || { printf 'Run with sudo to inspect the protected certificate directory.\n' >&2; exit 1; }
systemctl is-active --quiet zonez-lobby.service
systemctl is-enabled --quiet zonez-certbot-renew.timer
systemctl is-active --quiet zonez-certbot-renew.timer
[[ $(systemctl show zonez-lobby.service --property=User --value) == zonez ]]
[[ $(stat -L -c '%U:%G:%a' /etc/zonez/tls/current/privkey.pem) == root:zonez:640 ]]
openssl x509 -in /etc/zonez/tls/current/fullchain.pem -noout -dates -ext subjectAltName
python3 - <<'PY'
import json, socket, ssl, struct
from pathlib import Path
address = Path('/etc/zonez/public-ip').read_text(encoding='ascii').strip()
def read_message(connection):
    def exact(size):
        result = b''
        while len(result) < size:
            data = connection.recv(size - len(result))
            if not data: raise RuntimeError('Connection ended before the complete frame.')
            result += data
        return result
    size = struct.unpack('<I', exact(4))[0]
    if not 0 < size <= 512 * 1024: raise RuntimeError('Invalid frame size.')
    return json.loads(exact(size))
with socket.create_connection(('127.0.0.1', 7780), timeout=10) as raw:
    with ssl.create_default_context().wrap_socket(raw, server_hostname=address) as connection:
        assert read_message(connection)['service'] == 'welcome'
        body = b'{"service":"ping"}'
        connection.sendall(struct.pack('<I', len(body)) + body)
        assert read_message(connection)['service'] == 'pong'
        print('PASS: non-root service, protected key, renewal timer, trusted IP TLS, framed welcome/pong.')
PY
systemctl list-timers --no-pager zonez-certbot-renew.timer
