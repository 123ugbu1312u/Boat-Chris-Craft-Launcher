"""Prepare build tools inside Temp; never modify machine-wide PATH or installations."""
import concurrent.futures
import hashlib
import json
from pathlib import Path
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parents[3]
WORK = ROOT / 'Temp/MCTier-embedded'
TOOLS = WORK / 'Tools'
CACHE = TOOLS / 'downloads'
CACHE.mkdir(parents=True, exist_ok=True)

def fetch(url, destination, checksum=None):
    if not destination.exists() or (checksum and hashlib.sha256(destination.read_bytes()).hexdigest().lower() != checksum.lower()):
        print('Downloading', destination.name, flush=True)
        pending = destination.with_suffix(destination.suffix + '.partial')
        urllib.request.urlretrieve(url, pending)
        if checksum and hashlib.sha256(pending.read_bytes()).hexdigest().lower() != checksum.lower():
            raise RuntimeError('Checksum mismatch: ' + destination.name)
        pending.replace(destination)
    return destination

def compiler(item):
    if '.spectre' in item['Id']:
        return
    archive = fetch(item['Url'], CACHE / item['Name'], item['Sha256'])
    with zipfile.ZipFile(archive) as z:
        for member in z.infolist():
            name = member.filename.replace('\\', '/')
            if not name.startswith('Contents/') or member.is_dir():
                continue
            relative = Path(name.removeprefix('Contents/'))
            if '..' in relative.parts:
                raise RuntimeError('Unsafe archive path')
            target = TOOLS / 'msvc' / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(z.read(member))
    print('Prepared', item['Id'], flush=True)

def sdk(package):
    version = '10.0.26100.3916'
    url = f'https://api.nuget.org/v3-flatcontainer/{package}/{version}/{package}.{version}.nupkg'
    archive = fetch(url, CACHE / (package + '.nupkg'))
    with zipfile.ZipFile(archive) as z:
        z.extractall(TOOLS / 'sdk' / package)
    print('Prepared', package, flush=True)

if __name__ == '__main__':
    packages = json.loads((Path(__file__).with_name('toolchain-packages.json')).read_text(encoding='utf-8-sig'))
    with concurrent.futures.ThreadPoolExecutor(max_workers=5) as pool:
        tasks = [pool.submit(compiler, item) for item in packages]
        tasks += [pool.submit(sdk, package) for package in ['microsoft.windows.sdk.cpp', 'microsoft.windows.sdk.cpp.x64']]
        for task in concurrent.futures.as_completed(tasks):
            task.result()
