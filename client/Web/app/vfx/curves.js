const N = 64;
const clamp = t => Math.max(0, Math.min(1, t));
const mix = (a, b, t) => a + (b - a) * t;
const tables = new WeakMap();
function hermite(keys, t) {
  if (!keys?.length) return 0;
  if (t <= keys[0].time) return keys[0].value;
  for (let i = 1; i < keys.length; i++) {
    const a = keys[i - 1], b = keys[i];
    if (t > b.time) continue;
    const dt = b.time - a.time, x = dt ? (t - a.time) / dt : 1;
    if (!Number.isFinite(Number(a.outSlope)) || !Number.isFinite(Number(b.inSlope))) return a.value;
    const x2 = x * x, x3 = x2 * x;
    return (2 * x3 - 3 * x2 + 1) * a.value + (x3 - 2 * x2 + x) * dt * a.outSlope
      + (-2 * x3 + 3 * x2) * b.value + (x3 - x2) * dt * b.inSlope;
  }
  return keys.at(-1).value;
}
function channel(keys, t, index, fixed) {
  if (t <= keys[0][0]) return keys[0][index];
  for (let i = 1; i < keys.length; i++) {
    if (t > keys[i][0]) continue;
    const a = keys[i - 1], b = keys[i];
    return fixed ? a[index] : mix(a[index], b[index], (t - a[0]) / (b[0] - a[0] || 1));
  }
  return keys.at(-1)[index];
}
function bake(c) {
  if (tables.has(c)) return tables.get(c);
  const stride = c.type === 'gradient' ? 4 : 1;
  const result = {};
  for (const side of ['min', 'max']) {
    const keys = c[side + (stride === 4 ? 'Gradient' : 'Curve')];
    if (!keys) continue;
    const data = new Float32Array((N + 1) * stride);
    for (let i = 0; i <= N; i++) for (let k = 0; k < stride; k++) {
      data[i * stride + k] = stride === 1 ? hermite(keys, i / N)
        : channel(k === 3 ? keys.alphas : keys.colors, i / N, k === 3 ? 1 : k + 1, keys.mode === 1);
    }
    result[side] = data;
  }
  tables.set(c, result);
  return result;
}
function sample(table, t, k = 0, stride = 1) {
  const f = clamp(t) * N, i = Math.min(N - 1, Math.floor(f));
  return mix(table[i * stride + k], table[(i + 1) * stride + k], f - i);
}
export function value(c, t = 0, random = .5) {
  if (!c) return 0;
  if (typeof c === 'number') return c;
  if (c.mode === 0) return c.scalar;
  if (c.mode === 3) return mix(c.minScalar, c.scalar, random);
  const lut = bake(c);
  const max = sample(lut.max, t);
  return (c.mode === 2 ? mix(sample(lut.min, t), max, random) : max) * c.scalar;
}
export function color(c, t = 0, random = .5, out = [1, 1, 1, 1]) {
  if (!c) return out.fill(1);
  if (c.mode === 0) { for (let k = 0; k < 4; k++) out[k] = c.maxColor[k]; return out; }
  if (c.mode === 2) { for (let k = 0; k < 4; k++) out[k] = mix(c.minColor[k], c.maxColor[k], random); return out; }
  const lut = bake(c);
  for (let k = 0; k < 4; k++) {
    const max = sample(lut.max, c.mode === 4 ? random : t, k, 4);
    out[k] = c.mode === 3 ? mix(sample(lut.min, t, k, 4), max, random) : max;
  }
  return out;
}
export function bakeEffect(effect) {
  const visit = o => {
    if (!o || typeof o !== 'object') return;
    if (o.type === 'curve' || o.type === 'gradient') bake(o);
    else for (const v of Object.values(o)) visit(v);
  };
  visit(effect);
}
