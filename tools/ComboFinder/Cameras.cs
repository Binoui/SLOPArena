namespace SlopArena.ComboFinder;

static class Cameras
{
    internal static CameraPlan Plan(Content content, ComboFile combo, string orientation)
    {
        if (orientation is not ("portrait" or "landscape")) throw new ArgumentException("orientation must be portrait or landscape");
        var trace = Replay.Run(content, combo);
        var arena = content.Arena;
        int ko = trace.VictimKo;
        int s0 = combo.Timeline.String.From, s1 = combo.Timeline.String.To;
        var pa = trace.AttackerPos; var pv = trace.VictimPos;
        float ax = 0, az = 0, cx = 0, cy = 0, cz = 0; int n = 0;
        for (int t = Math.Max(1, s0 - 40); t <= s1; t++)
        {
            var (x1, y1, z1) = pa[t - 1]; var (x2, y2, z2) = pv[t - 1];
            float dx = x2 - x1, dz = z2 - z1, len = MathF.Max(0.3f, MathF.Sqrt(dx * dx + dz * dz));
            ax += dx / len; az += dz / len; cx += (x1 + x2) / 2; cy += (y1 + y2) / 2; cz += (z1 + z2) / 2; n++;
        }
        cx /= n; cy /= n; cz /= n;
        float alen = MathF.Max(1e-3f, MathF.Sqrt(ax * ax + az * az)); ax /= alen; az /= alen;
        const float Dist = 6.5f, Up = 1.4f;
        // Count sampled sight lines from each side that the floor or a crate blocks.
        int Blocked(float side)
        {
            float px = cx + az * side * Dist, pz = cz - ax * side * Dist, py = cy + Up;
            int blocked = 0;
            for (int t = Math.Max(1, s0 - 40); t <= s1; t += 4)
            {
                var (x1, y1, z1) = pa[t - 1]; var (x2, y2, z2) = pv[t - 1];
                foreach (var (tx, ty, tz) in new[] { (x1, y1, z1), (x2, y2, z2) })
                    for (float u = 0.1f; u < 0.95f; u += 0.05f)
                    {
                        float h = arena.Heightmap.Sample(px + (tx - px) * u, pz + (tz - pz) * u);
                        if (h != float.MinValue && h > py + (ty - py) * u - 0.2f) { blocked++; break; }
                    }
            }
            return blocked;
        }
        int left = Blocked(1f), right = Blocked(-1f);
        float sideSign = left <= right ? 1f : -1f;
        float camX = cx + az * sideSign * Dist, camZ = cz - ax * sideSign * Dist;
        var (kx, ky, kz) = pv[s1 - 1];

        int cut = Math.Max(s0, s1 - 12), from = Math.Max(1, s0 - 50), to = ko + 30;
        bool portrait = orientation == "portrait";
        float fov = portrait ? 58f : 40f;
        // Pull back per metre between the fighters so both fit across the frame: portrait shows about 0.62 m per
        // metre of distance horizontally at fov 58, landscape about 1.3 m at fov 40, plus a metre of margin each side.
        float spread = portrait ? 1.6f : 0.8f;
        PhotoCamera Camera(float x, float y, float z, float lx, float ly, float lz, object follow, float ease = 0.08f, float? lens = null, bool pull = true) =>
            new(new[] { x, y, z }, new[] { lx, ly, lz }, follow, new[] { 0f, 0.3f, 0f }, true, ease, lens ?? fov, 4000f, pull ? spread : null);
        var both = new[] { 1, 2 };
        return new(combo.Id, orientation, portrait ? 1080 : 1920, portrait ? 1920 : 1080, new(from, cut, ko - 1, ko), new[] {
        new CameraAngle("side", from,to, Camera(camX,cy+Up,camZ,cx,cy+0.3f,cz,both)),
        new CameraAngle("far-side", from,to, Camera(cx-az*sideSign*Dist,cy+Up,cz+ax*sideSign*Dist,cx,cy+0.3f,cz,both)),
        new CameraAngle("behind", from,to, Camera(cx-ax*7f,cy+2f,cz-az*7f,cx,cy+0.3f,cz,both)),
        new CameraAngle("wide", from,to, Camera(cx+az*sideSign*12f,cy+8f,cz-ax*sideSign*12f,cx,cy,cz,both)),
        new CameraAngle("kill", Math.Max(1,cut-30),to, Camera(kx+az*sideSign*8f,ky+2.5f,kz-ax*sideSign*8f,kx,ky,kz,2,0.18f,portrait?65.2f:45f,pull:false))
    });
    }
}
