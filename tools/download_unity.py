"""Resume the official Unity installer using verified, disjoint HTTP ranges."""
import concurrent.futures
import hashlib
import os
from pathlib import Path
import subprocess

URL = 'https://download.unity3d.com/download_unity/c1aa84e375f6/Windows64EditorInstaller/UnitySetup64-6000.3.15f1.exe'
TARGET = Path(r'D:\ZoneZTools\UnityDownloadCache\UnitySetup64-6000.3.15f1.exe')
SIZE = 4139644624
MD5 = 'f7688d76aa23343fa08821fe7ab0aea8'


def download(task):
    index, start, end = task
    part = TARGET.with_suffix(f'.range-{start}-{end}')
    expected = end - start + 1
    if part.exists() and part.stat().st_size == expected:
        return part
    headers = part.with_suffix(part.suffix + '.headers')
    subprocess.run(['curl.exe', '--fail', '--location', '--silent', '--show-error',
                    '--connect-timeout', '20', '--max-time', '600', '--retry', '2',
                    '--range', f'{start}-{end}', '--dump-header', str(headers),
                    '--output', str(part), URL], check=True)
    if f'content-range: bytes {start}-{end}/{SIZE}' not in headers.read_text().lower():
        raise RuntimeError('Server did not honor exact range')
    headers.unlink()
    if part.stat().st_size != expected:
        raise RuntimeError('Incomplete range')
    print(f'Range {index + 1} downloaded: {expected:,} bytes', flush=True)
    return part


def main():
    start = TARGET.stat().st_size if TARGET.exists() else 0
    if start > SIZE:
        raise RuntimeError('Existing file exceeds expected length')
    chunk = 8 * 1024 * 1024
    jobs = [(i, s, min(SIZE - 1, s + chunk - 1)) for i, s in enumerate(range(start, SIZE, chunk))] if chunk else []
    print(f'Keeping {start:,} existing bytes; fetching {SIZE-start:,} remaining bytes in {len(jobs)} ranges', flush=True)
    with concurrent.futures.ThreadPoolExecutor(max_workers=8) as pool:
        parts = list(pool.map(download, jobs))
    with TARGET.open('ab') as output:
        for part in parts:
            with part.open('rb') as source:
                while block := source.read(1024 * 1024):
                    output.write(block)
        output.flush()
        os.fsync(output.fileno())
    digest = hashlib.md5()
    with TARGET.open('rb') as source:
        while block := source.read(1024 * 1024):
            digest.update(block)
    if TARGET.stat().st_size != SIZE or digest.hexdigest() != MD5:
        raise RuntimeError('Official installer checksum mismatch; DO NOT RUN')
    print('UNITY_INSTALLER_CHECKSUM_OK ' + digest.hexdigest(), flush=True)
    for part in parts:
        part.unlink()


if __name__ == '__main__':
    main()
