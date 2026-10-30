export function floorSampler(triangles) {
  return (x, z, ceiling = Infinity) => {
    let highest = null;
    for (let i = 0; i < triangles.length; i += 9) {
      const ax = triangles[i], az = triangles[i + 2], bx = triangles[i + 3], bz = triangles[i + 5], cx = triangles[i + 6], cz = triangles[i + 8];
      const d = (bz - cz) * (ax - cx) + (cx - bx) * (az - cz);
      if (Math.abs(d) < 1e-8) continue;
      const a = ((bz - cz) * (x - cx) + (cx - bx) * (z - cz)) / d;
      const b = ((cz - az) * (x - cx) + (ax - cx) * (z - cz)) / d;
      if (a < -1e-5 || b < -1e-5 || a + b > 1.00001) continue;
      const y = a * triangles[i + 1] + b * triangles[i + 4] + (1 - a - b) * triangles[i + 7];
      if (y <= ceiling + .5 && (highest == null || y > highest)) highest = y;
    }
    return highest;
  };
}
