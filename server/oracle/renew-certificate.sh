#!/usr/bin/env bash
set -euo pipefail
# Certbot decides whether renewal is due. No --force-renewal or ad-hoc cadence.
/snap/bin/certbot renew --cert-name zonez-ip --quiet
# Retry certificate deployment even if a previous deploy-hook reload failed
# after successful issuance; unchanged certificates do not trigger another HUP.
/usr/local/libexec/zonez-install-certificate --lineage /etc/letsencrypt/live/zonez-ip
