#!/usr/bin/env bash
set -euo pipefail
# Add only the two inbound ports. Preserve existing SSH, OUTPUT and Oracle
# metadata/iSCSI protection rules; never flush chains or enable UFW.
for zonez_port in 80 7780; do
    if ! /usr/sbin/iptables -w 10 -C INPUT -p tcp --dport "$zonez_port" -m comment --comment zonez-lobby -j ACCEPT 2>/dev/null; then
        /usr/sbin/iptables -w 10 -I INPUT 1 -p tcp --dport "$zonez_port" -m comment --comment zonez-lobby -j ACCEPT
    fi
done
