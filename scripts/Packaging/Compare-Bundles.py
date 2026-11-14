"""Compare every restored file in two .NET single-file bundles, without running either EXE.

The v6 manifest layout follows the installed SDK's Microsoft.NET.HostModel
Manifest.Write and FileEntry.Write. Compression must not remove or alter files.
"""
import argparse
import hashlib
import json
import mmap
import struct
import zlib
from pathlib import Path

SIGNATURE = bytes.fromhex('8b1202b96a612038727b930214d7a03213f5b9e6efae3318ee3b2dce24b36aae')


def read_string(stream):
    size = 0
    shift = 0
    for _ in range(5):
        value = stream.read(1)[0]
        size |= (value & 127) << shift
        if value < 128:
            if size > 1024 * 1024:
                raise ValueError('Invalid bundle string length')
            return stream.read(size).decode('utf-8')
        shift += 7
    raise ValueError('Invalid bundle string encoding')


def inspect(path):
    with path.open('rb') as stream:
        with mmap.mmap(stream.fileno(), 0, access=mmap.ACCESS_READ) as image:
            marker = image.find(SIGNATURE)
            if marker < 8:
                raise ValueError(f'Missing bundle signature: {path}')
            offset = struct.unpack_from('<q', image, marker - 8)[0]
        stream.seek(offset)
        major, minor, count = struct.unpack('<IIi', stream.read(12))
        if major != 6 or minor != 0 or not 0 < count < 10000:
            raise ValueError(f'Unsupported bundle manifest: {major}.{minor}, {count} files')
        bundle_id = read_string(stream)
        stream.read(40)  # deps/runtimeconfig locations and header flags
        entries = {}
        for _ in range(count):
            position, size, compressed = struct.unpack('<qqq', stream.read(24))
            kind = stream.read(1)[0]
            name = read_string(stream)
            if name in entries or min(position, size, compressed) < 0:
                raise ValueError('Invalid bundle file entry')
            entries[name] = {'offset': position, 'size': size, 'compressed': compressed, 'type': kind}
        for name, entry in entries.items():
            stream.seek(entry['offset'])
            remaining = entry['compressed'] or entry['size']
            digest = hashlib.sha256()
            restored_size = 0
            decoder = zlib.decompressobj(-15) if entry['compressed'] else None
            while remaining:
                chunk = stream.read(min(remaining, 256 * 1024))
                if not chunk:
                    raise ValueError(f'Truncated bundle entry: {name}')
                remaining -= len(chunk)
                output = decoder.decompress(chunk) if decoder else chunk
                digest.update(output)
                restored_size += len(output)
            if decoder:
                tail = decoder.flush()
                digest.update(tail)
                restored_size += len(tail)
                if not decoder.eof:
                    raise ValueError(f'Incomplete compressed entry: {name}')
            if restored_size != entry['size']:
                raise ValueError(f'Restored size mismatch: {name}')
            entry['sha256'] = digest.hexdigest()
        return bundle_id, entries


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('before', type=Path)
    parser.add_argument('after', type=Path)
    parser.add_argument('--report', type=Path, required=True)
    args = parser.parse_args()
    before_id, before = inspect(args.before)
    after_id, after = inspect(args.after)
    missing = sorted(before.keys() - after.keys())
    added = sorted(after.keys() - before.keys())
    changed = [name for name in before.keys() & after.keys()
               if any(before[name][field] != after[name][field] for field in ('size', 'type', 'sha256'))]
    result = {
        'beforeBytes': args.before.stat().st_size, 'afterBytes': args.after.stat().st_size,
        'beforeBundleId': before_id, 'afterBundleId': after_id,
        'filesBefore': len(before), 'filesAfter': len(after),
        'compressedFilesAfter': sum(bool(entry['compressed']) for entry in after.values()),
        'missingFiles': missing, 'addedFiles': added, 'changedRestoredFiles': changed,
        'allRestoredFilesByteIdentical': not (missing or added or changed), 'files': after,
    }
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps({key: value for key, value in result.items() if key != 'files'}, indent=2))
    if not result['allRestoredFilesByteIdentical']:
        raise SystemExit(1)


if __name__ == '__main__':
    main()
