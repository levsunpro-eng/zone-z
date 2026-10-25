#!/usr/bin/python3
"""Certbot deploy hook: validate a pair, atomically publish it, then reload TLS.

No private-key contents or ACME credentials are logged. Old immutable bundles
are retained so a failed service reload can roll the pointer back safely.
"""
import argparse
import hashlib
import ipaddress
import os
from pathlib import Path
import shutil
import ssl
import subprocess
import tempfile
import uuid

LINEAGE = Path("/etc/letsencrypt/live/zonez-ip")
TLS_ROOT = Path("/etc/zonez/tls")
PUBLIC_IP_FILE = Path("/etc/zonez/public-ip")


def validate_pair(lineage, public_ip):
    ipaddress.IPv4Address(public_ip)
    fullchain, key = lineage / "fullchain.pem", lineage / "privkey.pem"
    if not 0 < fullchain.stat().st_size <= 131072 or not 0 < key.stat().st_size <= 32768:
        raise ValueError("Unexpected certificate or key size.")
    context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
    context.minimum_version = ssl.TLSVersion.TLSv1_2
    context.load_cert_chain(str(fullchain), str(key))
    for arguments in (("-checkend", "86400"), ("-checkip", public_ip)):
        result = subprocess.run(["/usr/bin/openssl", "x509", "-in", str(fullchain), "-noout", *arguments],
                                capture_output=True, text=True, check=False)
        if result.returncode:
            raise ValueError("Certificate must match the public IPv4 and remain valid for at least 24 hours.")


def secure(path, mode, gid):
    os.chown(path, 0, gid)
    os.chmod(path, mode)


def point_current(tls_root, target):
    temporary = tls_root / (".current-" + uuid.uuid4().hex)
    try:
        temporary.symlink_to(target, target_is_directory=True)
        os.replace(temporary, tls_root / "current")
    finally:
        if temporary.is_symlink():
            temporary.unlink()


def install_certificate(lineage, tls_root, public_ip, gid):
    validate_pair(lineage, public_ip)
    if tls_root.is_symlink() or not tls_root.is_dir():
        raise ValueError("TLS root must be an existing real directory.")
    digest = hashlib.sha256((lineage / "fullchain.pem").read_bytes()).hexdigest()[:24]
    bundle = tls_root / digest
    current = tls_root / "current"
    if current.exists() and not current.is_symlink():
        raise ValueError("Refusing to replace a non-symlink current path.")
    previous = os.readlink(current) if current.is_symlink() else None
    if previous == digest:
        return False
    if bundle.is_symlink():
        raise ValueError("Certificate bundle cannot be a symlink.")
    if not bundle.exists():
        staging = Path(tempfile.mkdtemp(prefix=".certificate-", dir=str(tls_root)))
        try:
            for filename in ("fullchain.pem", "privkey.pem"):
                target = staging / filename
                target.write_bytes((lineage / filename).read_bytes())
                secure(target, 0o640, gid)
            validate_pair(staging, public_ip)
            secure(staging, 0o750, gid)
            os.replace(staging, bundle)
        finally:
            if staging.exists() and staging.parent.resolve() == tls_root.resolve():
                shutil.rmtree(staging)
    # A single symlink swap makes the full chain and matching key visible
    # together. Established game connections keep their existing TLS session.
    point_current(tls_root, digest)
    try:
        active = subprocess.run(["/bin/systemctl", "is-active", "--quiet", "zonez-lobby.service"], check=False)
        if active.returncode == 0:
            subprocess.run(["/bin/systemctl", "reload", "zonez-lobby.service"], check=True)
    except (OSError, subprocess.CalledProcessError):
        if previous is not None:
            point_current(tls_root, previous)
        elif current.is_symlink():
            current.unlink()
        raise
    return True


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--lineage", default=os.environ.get("RENEWED_LINEAGE", str(LINEAGE)))
    args = parser.parse_args()
    if os.geteuid() != 0:
        parser.error("Run the deploy hook as root.")
    lineage = Path(args.lineage)
    if lineage != LINEAGE:
        # The hook may be invoked by a system-wide renewal containing other
        # certificates. Never deploy one belonging to another service.
        print("ZoneZ: skipping another certificate lineage.")
        return
    import grp
    gid = grp.getgrnam("zonez").gr_gid
    import fcntl
    with open("/run/lock/zonez-certificate.lock", "a", encoding="ascii") as lock:
        fcntl.flock(lock, fcntl.LOCK_EX)
        changed = install_certificate(lineage, TLS_ROOT, PUBLIC_IP_FILE.read_text(encoding="ascii").strip(), gid)
    print("ZoneZ: certificate installed; active TLS reloaded." if changed else "ZoneZ: certificate is already current.")


if __name__ == "__main__":
    main()
