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
        var (kill, end, respawn) = KillCamera(content, pv, s1, ko, ax, az, sideSign, portrait);
        return new(combo.Id, orientation, portrait ? 1080 : 1920, portrait ? 1920 : 1080, new(from, cut, end, ko), new[] {
        new CameraAngle("side", from,to, Camera(camX,cy+Up,camZ,cx,cy+0.3f,cz,both)),
        new CameraAngle("far-side", from,to, Camera(cx-az*sideSign*Dist,cy+Up,cz+ax*sideSign*Dist,cx,cy+0.3f,cz,both)),
        new CameraAngle("behind", from,to, Camera(cx-ax*7f,cy+2f,cz-az*7f,cx,cy+0.3f,cz,both)),
        new CameraAngle("wide", from,to, Camera(cx+az*sideSign*12f,cy+8f,cz-ax*sideSign*12f,cx,cy,cz,both)),
        new CameraAngle("kill", Math.Max(1,cut-30),to, kill, respawn)
    });
    }

    /// <summary>
    /// A fixed camera behind the last hit, aimed between the hit and the point where the victim leaves the stage, so
    /// the victim flies away from it and the KO burst shows. The victim respawns on floor out of view so the cut can
    /// run past the KO; without such a point the cut ends just before it.
    /// </summary>
    static (PhotoCamera camera, int end, float[]? respawn) KillCamera(Content content, List<Position> pv, int s1, int ko,
        float ax, float az, float sideSign, bool portrait)
    {
        var hit = pv[s1 - 1]; var gone = pv[ko - 2];
        float dx = gone.x - hit.x, dz = gone.z - hit.z, dlen = MathF.Sqrt(dx * dx + dz * dz);
        if (dlen < 0.5f) { dx = ax; dz = az; dlen = 1f; }
        dx /= dlen; dz /= dlen;
        float[] cam = { hit.x - dx * 7f + dz * sideSign * 2f, hit.y + 3f, hit.z - dz * 7f - dx * sideSign * 2f };
        float[] Unit(float x, float y, float z) { float l = MathF.Sqrt(x * x + y * y + z * z); return new[] { x / l, y / l, z / l }; }
        var toHit = Unit(hit.x - cam[0], hit.y - cam[1], hit.z - cam[2]);
        var toGone = Unit(gone.x - cam[0], gone.y - cam[1], gone.z - cam[2]);
        var forward = Unit(toHit[0] + toGone[0], toHit[1] + toGone[1], toHit[2] + toGone[2]);
        float Angle(float[] a, float[] b) => MathF.Acos(Math.Clamp(a[0] * b[0] + a[1] * b[1] + a[2] * b[2], -1f, 1f)) * 180f / MathF.PI;
        // Both points must fit across the narrower side of the frame, with a margin.
        float aspect = portrait ? 9f / 16f : 16f / 9f;
        float across = Angle(toHit, toGone) * 1.3f + 12f;
        float Vertical(float horizontal) => 2f * MathF.Atan(MathF.Tan(horizontal * MathF.PI / 360f) / aspect) * 180f / MathF.PI;
        float fov = Math.Clamp(Math.Max(portrait ? Vertical(across) : across, portrait ? 50f : 38f), 35f, 95f);
        float[] look = { cam[0] + forward[0] * 10f, cam[1] + forward[1] * 10f, cam[2] + forward[2] * 10f };
        float half = Math.Min(fov, portrait ? 2f * MathF.Atan(MathF.Tan(fov * MathF.PI / 360f) * aspect) * 180f / MathF.PI : fov) / 2f;
        var arena = content.Arena;
        float[]? respawn = null; float widest = half * 1.3f;
        for (float x = arena.MinX; x <= arena.MaxX; x += 1f)
            for (float z = arena.MinZ; z <= arena.MaxZ; z += 1f)
            {
                float floor = arena.Heightmap.Sample(x, z);
                if (floor == float.MinValue) continue;
                float off = Angle(forward, Unit(x - cam[0], floor + 1f - cam[1], z - cam[2]));
                if (off > widest) { widest = off; respawn = new[] { x, z }; }
            }
        int end = respawn != null ? ko + 24 : ko - 1;
        return (new PhotoCamera(cam, look, null, new[] { 0f, 0f, 0f }, false, 1f, fov), end, respawn);
    }
}
