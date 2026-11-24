import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createHash } from 'node:crypto';
import { inflateSync, deflateSync } from 'node:zlib';

const codes = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const images = path.join(codes, 'BCCL/Images');
const work = path.resolve(codes, '../Temp/MCTier-embedded');
const executable = path.join(work, 'target/release/mctier.exe');
const stampPath = path.join(work, 'embedded-build.json');
const sha256 = bytes => createHash('sha256').update(bytes).digest('hex').toUpperCase();

// Losslessly re-encode the generated 64px PNG. This keeps the embedded
// resource within the original asset slot; no PE sections or code are moved.
const input = fs.readFileSync(path.join(images, 'icon-rounded-64.png'));
const chunks = [];
for (let offset = 8; offset < input.length;) {
  const length = input.readUInt32BE(offset);
  chunks.push({ type: input.toString('ascii', offset + 4, offset + 8), data: input.subarray(offset + 8, offset + 8 + length) });
  offset += length + 12;
}
const header = chunks.find(c => c.type === 'IHDR').data;
const width = header.readUInt32BE(0), height = header.readUInt32BE(4);
if (width !== 64 || height !== 64 || header[8] !== 8 || header[9] !== 6 || header[12] !== 0)
  throw new Error('Expected a generated non-interlaced 64px RGBA PNG.');
const encoded = inflateSync(Buffer.concat(chunks.filter(c => c.type === 'IDAT').map(c => c.data)));
const stride = width * 4;
if (encoded.length !== (stride + 1) * height) throw new Error('Unexpected PNG row data.');
const rgba = Buffer.alloc(stride * height);
function paeth(a, b, c) {
  const p = a + b - c, pa = Math.abs(p - a), pb = Math.abs(p - b), pc = Math.abs(p - c);
  return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
}
for (let y = 0; y < height; y++) {
  const filter = encoded[y * (stride + 1)];
  if (filter > 4) throw new Error('Invalid PNG filter.');
  for (let x = 0; x < stride; x++) {
    const i = y * stride + x, a = x >= 4 ? rgba[i - 4] : 0, b = y ? rgba[i - stride] : 0;
    const c = y && x >= 4 ? rgba[i - stride - 4] : 0;
    const predictor = [0, a, b, Math.floor((a + b) / 2), paeth(a, b, c)][filter];
    rgba[i] = (encoded[y * (stride + 1) + 1 + x] + predictor) & 255;
  }
}
const palette = [], colorIndex = new Map(), indices = Buffer.alloc(width * height);
for (let i = 0; i < indices.length; i++) {
  const color = rgba.readUInt32BE(i * 4);
  if (!colorIndex.has(color)) { colorIndex.set(color, palette.length); palette.push([...rgba.subarray(i * 4, i * 4 + 4)]); }
  indices[i] = colorIndex.get(color);
}
const indexed = palette.length <= 256;
const pixels = indexed ? indices : rgba, bpp = indexed ? 1 : 4, rowBytes = width * bpp;
const strategies = Array.from({ length: 6 }, () => Buffer.alloc((rowBytes + 1) * height));
for (let y = 0; y < height; y++) {
  let bestScore = Infinity, bestFilter = 0;
  for (let filter = 0; filter <= 4; filter++) {
    const rowStart = y * (rowBytes + 1);
    strategies[filter][rowStart] = filter;
    let score = 0;
    for (let x = 0; x < rowBytes; x++) {
      const i = y * rowBytes + x, a = x >= bpp ? pixels[i - bpp] : 0, b = y ? pixels[i - rowBytes] : 0;
      const c = y && x >= bpp ? pixels[i - rowBytes - bpp] : 0;
      const predictor = [0, a, b, Math.floor((a + b) / 2), paeth(a, b, c)][filter];
      const residual = (pixels[i] - predictor) & 255;
      strategies[filter][rowStart + 1 + x] = residual;
      score += Math.abs(residual < 128 ? residual : residual - 256);
    }
    if (score < bestScore) { bestScore = score; bestFilter = filter; }
  }
  const rowStart = y * (rowBytes + 1);
  strategies[bestFilter].copy(strategies[5], rowStart, rowStart, rowStart + rowBytes + 1);
}
const compressed = strategies.flatMap(rows => [0, 1, 2, 3, 4].flatMap(strategy =>
  [8, 9].map(memLevel => deflateSync(rows, { level: 9, strategy, memLevel }))
)).sort((a, b) => a.length - b.length)[0];
const crcTable = Array.from({ length: 256 }, (_, n) => {
  let c = n;
  for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
  return c >>> 0;
});
function chunk(type, data) {
  const body = Buffer.concat([Buffer.from(type), data]);
  let crc = 0xffffffff;
  for (const b of body) crc = crcTable[(crc ^ b) & 255] ^ (crc >>> 8);
  const result = Buffer.alloc(data.length + 12);
  result.writeUInt32BE(data.length, 0); body.copy(result, 4); result.writeUInt32BE((crc ^ 0xffffffff) >>> 0, result.length - 4);
  return result;
}
const indexedHeader = Buffer.from(header); indexedHeader[9] = indexed ? 3 : 6;
const logo = Buffer.concat([
  input.subarray(0, 8), chunk('IHDR', indexedHeader),
  ...(indexed ? [chunk('PLTE', Buffer.from(palette.flatMap(c => c.slice(0, 3)))), chunk('tRNS', Buffer.from(palette.map(c => c[3])))] : []),
  chunk('IDAT', compressed), chunk('IEND', Buffer.alloc(0)),
]);
const stamp = JSON.parse(fs.readFileSync(stampPath, 'utf8').replace(/^\uFEFF/, ''));
const binary = fs.readFileSync(executable);
if (sha256(binary) !== stamp.sha256.toUpperCase()) throw new Error('MCTier build checksum mismatch.');
const original = fs.readFileSync(path.join(images, 'icon.png'));
const offset = binary.indexOf(original);
if (offset >= 0) {
  if (binary.indexOf(original, offset + 1) >= 0) throw new Error('Ambiguous embedded logo asset.');
  if (logo.length > original.length) throw new Error('Rounded logo does not fit the original PNG slot.');
  binary.fill(0, offset, offset + original.length);
  logo.copy(binary, offset);
  // PNG decoders stop at IEND; zero padding preserves the asset slice length.
  fs.writeFileSync(executable, binary);
} else if (binary.indexOf(logo) < 0) {
  throw new Error('Unknown embedded branding. Rebuild MCTier from the current frontend before publishing.');
}
fs.writeFileSync(path.join(codes, 'ThirdParty/MCTier/public/BCCLIcon.png'), logo);
const distIcon = path.join(codes, 'ThirdParty/MCTier/dist/BCCLIcon.png');
if (fs.existsSync(distIcon)) fs.writeFileSync(distIcon, logo);
stamp.sha256 = sha256(binary);
stamp.brandingAssetSha256 = sha256(logo);
fs.writeFileSync(stampPath, JSON.stringify(stamp, null, 2) + '\n');
console.log(`Verified rounded multiplayer logo: ${logo.length} bytes, ${palette.length} colors; native runtime and resource offsets preserved.`);
