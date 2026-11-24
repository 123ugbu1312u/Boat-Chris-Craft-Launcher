import { openSync, closeSync, copyFileSync, constants, createWriteStream, readFileSync, readSync } from 'node:fs';
import { once } from 'node:events';
import { availableParallelism } from 'node:os';
import { inflateRawSync } from 'node:zlib';
import { Worker, isMainThread, parentPort, workerData } from 'node:worker_threads';
import zopfli from '@gfx/zopfli';

const bundleSignature = Buffer.from('8b1202b96a612038727b930214d7a03213f5b9e6efae3318ee3b2dce24b36aae', 'hex');
const assemblyKind = 1;
const depsKind = 3;
const runtimeConfigKind = 4;

function readBundle(file) {
  const pe = file.readUInt32LE(0x3c);
  if (file.readUInt16LE(0) !== 0x5a4d || file.readUInt32LE(pe) !== 0x4550 ||
      file.readUInt16LE(pe + 4) !== 0x8664 || file.readUInt16LE(pe + 24) !== 0x20b) {
    throw new Error('Expected a Windows x64 apphost.');
  }
  if (file.readUInt32LE(pe + 24 + 112 + 4 * 8) !== 0) {
    throw new Error('Compress the bundle before signing it.');
  }

  // .NET 6+ HostModel apphost and manifest representation. Preserve the ID, file names,
  // kinds and flags and update only offsets and lengths for the unchanged payload.
  const marker = file.subarray(0, 16 * 1024 * 1024).indexOf(bundleSignature);
  if (marker < 8) throw new Error('Missing .NET bundle signature.');
  const headerOffset = Number(file.readBigUInt64LE(marker - 8));
  if (headerOffset <= marker || headerOffset >= file.length ||
      file.readUInt32LE(headerOffset) !== 6 || file.readUInt32LE(headerOffset + 4) !== 0) {
    throw new Error('Unsupported .NET bundle manifest.');
  }
  const count = file.readUInt32LE(headerOffset + 8);
  if (count === 0 || count > 10000) throw new Error('Invalid .NET bundle file count.');

  let cursor = headerOffset + 12;
  function readString() {
    let length = 0;
    for (let shift = 0; shift <= 21; shift += 7) {
      const octet = file.readUInt8(cursor++);
      length += (octet & 0x7f) * 2 ** shift;
      if (!(octet & 0x80)) {
        if (length === 0 || length > 65535 || cursor + length > file.length) {
          throw new Error('Invalid .NET bundle string length.');
        }
        const value = file.toString('utf8', cursor, cursor + length);
        cursor += length;
        return value;
      }
    }
    throw new Error('Invalid .NET bundle string encoding.');
  }

  readString(); // Extraction ID
  const locations = cursor - headerOffset;
  cursor += 40; // Deps and runtimeconfig locations, then bundle flags.
  const entries = [];
  const names = new Set();
  for (let index = 0; index < count; index++) {
    const record = cursor - headerOffset;
    const offset = Number(file.readBigUInt64LE(cursor));
    const size = Number(file.readBigUInt64LE(cursor + 8));
    const packed = Number(file.readBigUInt64LE(cursor + 16));
    const kind = file.readUInt8(cursor + 24);
    cursor += 25;
    const name = readString();
    if (names.has(name) || !Number.isSafeInteger(size) || !Number.isSafeInteger(packed) ||
        offset < marker + bundleSignature.length || offset + (packed || size) > headerOffset) {
      throw new Error(`Invalid .NET bundle entry: ${name}`);
    }
    names.add(name);
    entries.push({ name, index, kind, record, offset, size, packed });
  }
  if (cursor !== file.length) throw new Error('Unexpected data after the .NET bundle manifest.');
  entries.sort((left, right) => left.offset - right.offset || left.index - right.index);
  let end = entries[0].offset;
  for (const entry of entries) {
    if (entry.offset < end && (entry.packed || entry.size)) {
      throw new Error(`Overlapping .NET bundle entry: ${entry.name}`);
    }
    end = Math.max(end, entry.offset + (entry.packed || entry.size));
  }
  return { marker, headerOffset, locations, entries };
}

async function compressEntries({ path, entries }) {
  const source = openSync(path, 'r');
  try {
    for (const entry of entries) {
      const previous = Buffer.alloc(entry.packed);
      if (readSync(source, previous, 0, previous.length, entry.offset) !== previous.length) {
        throw new Error(`Unexpected end of .NET bundle: ${entry.name}`);
      }
      const bytes = inflateRawSync(previous, { maxOutputLength: entry.size });
      if (bytes.length !== entry.size) throw new Error(`Unexpected expanded length: ${entry.name}`);
      const replacement = await zopfli.deflateAsync(bytes, { numiterations: 3 });
      if (replacement.length >= previous.length) continue;
      if (!inflateRawSync(replacement, { maxOutputLength: entry.size }).equals(bytes)) {
        throw new Error(`Recompression changed file contents: ${entry.name}`);
      }
      parentPort.postMessage({ index: entry.index, replacement }, [replacement.buffer]);
    }
  } finally {
    closeSync(source);
  }
}

async function writeBundle(input, output, reference) {
  const file = readFileSync(input);
  const { marker, headerOffset, locations, entries } = readBundle(file);
  const replacements = new Map();
  let selected = entries.filter(entry => entry.packed >= 64 * 1024 &&
    entry.kind !== depsKind && entry.kind !== runtimeConfigKind);
  const entryBytes = (bytes, entry) => bytes.subarray(entry.offset, entry.offset + entry.packed);
  if (reference) {
    const prior = readFileSync(reference);
    const priorEntries = new Map(readBundle(prior).entries.map(entry => [entry.name, entry]));
    selected = selected.filter(entry => {
      const cached = priorEntries.get(entry.name);
      if (!cached?.packed || cached.size !== entry.size || cached.packed > entry.packed) return true;
      const compressed = entryBytes(prior, cached);
      if (!inflateRawSync(compressed, { maxOutputLength: entry.size })
        .equals(inflateRawSync(entryBytes(file, entry), { maxOutputLength: entry.size }))) return true;
      if (cached.packed < entry.packed) replacements.set(entry.index, compressed);
      return false;
    });
  }
  const work = Array.from({ length: Math.min(4, availableParallelism(), selected.length) }, () => []);
  selected.sort((left, right) => right.packed - left.packed)
    .forEach((entry, index) => work[index % work.length].push(entry));
  await Promise.all(work.map(group => new Promise((resolve, reject) => {
    const worker = new Worker(new URL(import.meta.url), { workerData: { path: input, entries: group } });
    worker.on('message', ({ index, replacement }) => replacements.set(index, Buffer.from(replacement)));
    worker.once('error', reject);
    worker.once('exit', code => code === 0 ? resolve() : reject(new Error(`Compression worker exited with ${code}`)));
  })));

  const apphost = Buffer.from(file.subarray(0, entries[0].offset));
  const manifest = Buffer.from(file.subarray(headerOffset));
  const regions = [apphost];
  let offset = apphost.length;
  for (const entry of entries) {
    const bytes = replacements.get(entry.index) ?? file.subarray(entry.offset, entry.offset + (entry.packed || entry.size));
    if (entry.kind === assemblyKind && !entry.packed) {
      const padding = (4096 - offset % 4096) % 4096;
      regions.push(Buffer.alloc(padding));
      offset += padding;
    }
    manifest.writeBigUInt64LE(BigInt(offset), entry.record);
    if (entry.packed) manifest.writeBigUInt64LE(BigInt(bytes.length), entry.record + 16);
    if (entry.kind === depsKind) manifest.writeBigUInt64LE(BigInt(offset), locations);
    if (entry.kind === runtimeConfigKind) manifest.writeBigUInt64LE(BigInt(offset), locations + 16);
    regions.push(bytes);
    offset += bytes.length;
  }
  apphost.writeBigUInt64LE(BigInt(offset), marker - 8);
  regions.push(manifest);

  const saved = file.length - offset - manifest.length;
  if (saved <= 0) {
    copyFileSync(input, output, constants.COPYFILE_EXCL);
    console.log('Kept the published bundle at its original size.');
    return;
  }
  const stream = createWriteStream(output, { flags: 'wx' });
  for (const region of regions) {
    if (!stream.write(region)) await once(stream, 'drain');
  }
  const finished = once(stream, 'finish');
  stream.end();
  await finished;
  console.log(`Verified ${replacements.size} recompressed .NET entries; saved ${saved} bytes.`);
}

if (isMainThread) {
  const [input, output, reference] = process.argv.slice(2);
  await writeBundle(input, output, reference);
} else {
  await compressEntries(workerData);
}
