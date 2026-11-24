import { createReadStream, createWriteStream } from 'node:fs';
import { readFile, readdir } from 'node:fs/promises';
import { join } from 'node:path';
import { pipeline } from 'node:stream/promises';
import { constants, createBrotliCompress } from 'node:zlib';

const [runtimeDirectory, output] = process.argv.slice(2);
const entryPoint = join(runtimeDirectory, 'MCTier.exe');
const executable = await readFile(entryPoint);
const offsets = {};
const runtime = join(runtimeDirectory, 'runtime');

for (const file of await readdir(runtime, { withFileTypes: true })) {
  if (!file.isFile()) continue;
  const bytes = await readFile(join(runtime, file.name));
  if (bytes.length === 0) continue;

  const anchor = bytes.subarray(0, Math.min(bytes.length, 64));
  for (let offset = executable.indexOf(anchor); offset !== -1; offset = executable.indexOf(anchor, offset + 1)) {
    if (executable.subarray(offset, offset + bytes.length).equals(bytes)) {
      offsets[`runtime/${file.name}`] = offset;
      break;
    }
  }
}

await pipeline(createReadStream(entryPoint), createBrotliCompress({
  params: {
    [constants.BROTLI_PARAM_QUALITY]: 11,
    [constants.BROTLI_PARAM_LGWIN]: 24
  }
}), createWriteStream(output, { flags: 'wx' }));
console.log(JSON.stringify(offsets));
