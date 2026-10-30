using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices.JavaScript;
using System.Text;
using SlopArena.Shared;

public static partial class Program
{
    public static void Main() { }
}

// Browser bridge over the real Shared simulation. JS owns rendering and raw input;
// everything gameplay goes through ServerSimulation exactly as Unity's local matches do.
public static partial class Demo
{
    static BuiltInRosterManifest? _roster;
    static readonly Dictionary<string, CookedCharacterPackageLoadResult> _packages = new(StringComparer.Ordinal);
    static readonly Dictionary<string, MatchContentPackageRequirement> _ownIdentity = new(StringComparer.Ordinal);
    static MatchContentCatalog? _catalog;
    static ArenaDefinition _arena;
    static MatchMode? _mode;
    static readonly Dictionary<ulong, InputState> _inputs = new();
    static uint _tick;

    static readonly MethodInfo ResolveBoneAnimFrame = typeof(ServerSimulation).GetMethod("ResolveBoneAnimFrame",
        BindingFlags.NonPublic | BindingFlags.Instance)!;

    [JSExport] public static int ProtocolVersion() => SimulationProtocol.Version;

    [JSExport]
    public static void SetRoster(string json)
    {
        _roster = BuiltInRosterManifestCodec.ParseCooked(json);
        _packages.Clear();
        _ownIdentity.Clear();
        _catalog = null;
    }

    [JSExport]
    public static string AddPackage(string packageId, byte[] manifest, byte[] runtime, byte[] poses, byte[] bindings)
    {
        var entry = _roster!.ResolvePackage(packageId);
        if (entry == null) return "not in roster";
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [CharacterPackageAssembler.ManifestPath] = manifest,
            [CharacterPackageAssembler.RuntimePath] = runtime,
            [CharacterPackageAssembler.PosePath] = poses,
            [CharacterPackageAssembler.BindingPath] = bindings,
        };
        var result = CookedCharacterPackageLoader.LoadFiles(files, entry.Requirement);
        // A shared working tree can hold a fresh cook whose roster pin lags a few seconds behind. Load the package
        // by its own identity so local play keeps working; scripts/package.mjs refuses to publish such a set.
        string warning = "";
        if (!result.IsValid && result.Diagnostics.Any(d => d.Code == "package.identity.mismatch"))
        {
            using var doc = System.Text.Json.JsonDocument.Parse(manifest);
            var m = doc.RootElement;
            var own = new MatchContentPackageRequirement(m.GetProperty("packageId").GetString()!, m.GetProperty("version").GetString()!,
                m.GetProperty("cookedContentHash").GetString()!, m.GetProperty("packageHash").GetString()!);
            result = CookedCharacterPackageLoader.LoadFiles(files, own);
            _ownIdentity[packageId] = own;
            warning = "stale roster pin, loaded by the package's own identity";
        }
        _packages[packageId] = result;
        return result.IsValid ? warning : string.Join("; ", result.Diagnostics);
    }

    // Packages that failed to load are dropped so one stale cook doesn't block the demo.
    [JSExport]
    public static string BuildCatalog()
    {
        var valid = _roster!.Entries.Where(e => _packages.TryGetValue(e.PackageId, out var p) && p.IsValid)
            .Select(e => _ownIdentity.TryGetValue(e.PackageId, out var own) ? new BuiltInRosterEntry(e.Selector, e.PackageId, own) : e)
            .ToList();
        var manifest = new BuiltInRosterManifest(_roster.SchemaVersion, valid);
        var packages = valid.ToDictionary(e => e.PackageId, e => _packages[e.PackageId], StringComparer.Ordinal);
        var result = new MatchContentCatalogBuilder().Build(manifest, packages);
        if (!result.IsValid || result.Catalog == null) return "{\"error\":" + Str(string.Join("; ", result.Diagnostics)) + "}";
        _catalog = result.Catalog;
        var sb = new StringBuilder("{\"fighters\":[");
        bool first = true;
        foreach (var e in _catalog.Entries)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append("{\"id\":").Append(Str(e.Identity.PackageId)).Append(",\"name\":").Append(Str(e.DisplayName))
              .Append(",\"half\":").Append(F(e.Definition.CapsuleHeight * 0.5f))
              .Append(",\"shieldRadius\":").Append(F(e.Definition.ShieldRadius))
              .Append(",\"visualScale\":").Append(F(e.Definition.VisualScale)).Append(",\"modelYOffset\":").Append(F(e.Definition.ModelYOffset)).Append(",\"bones\":[");
            var names = e.BakedAnimation?.BoneNames ?? Array.Empty<string>();
            for (int i = 0; i < names.Count; i++) { if (i > 0) sb.Append(','); sb.Append(Str(names[i])); }
            sb.Append("]}");
        }
        return sb.Append("]}").ToString();
    }

    [JSExport]
    public static string LoadArena(byte[] bytes)
    {
        var arena = ArenaBinaryFormat.Deserialize(bytes);
        if (arena is not ArenaDefinition a) return "{\"error\":\"bad arena\"}";
        _arena = a;
        var sb = new StringBuilder("{");
        sb.Append("\"name\":").Append(Str(a.DisplayName ?? a.Name ?? "")).Append(',');
        sb.Append("\"color\":").Append(Str(a.PreviewColor ?? "")).Append(',');
        sb.Append("\"killHeight\":").Append(F(a.KillHeight)).Append(',');
        sb.Append("\"bounds\":[").Append(F(a.MinX)).Append(',').Append(F(a.MaxX)).Append(',')
          .Append(F(a.MinZ)).Append(',').Append(F(a.MaxZ)).Append("],");
        sb.Append("\"spawns\":").Append(a.SpawnPoints?.Length ?? 0).Append(',');
        sb.Append("\"pvp\":").Append(a.Purpose == ArenaPurpose.PvP ? "true" : "false");
        return sb.Append('}').ToString();
    }

    [JSExport]
    public static double[] ArenaTriangles()
    {
        var tris = _arena.CollisionTriangles ?? Array.Empty<CollisionTriangle>();
        var data = new double[tris.Length * 9];
        for (int i = 0; i < tris.Length; i++)
        {
            var t = tris[i];
            int o = i * 9;
            data[o] = t.AX; data[o + 1] = t.AY; data[o + 2] = t.AZ;
            data[o + 3] = t.BX; data[o + 4] = t.BY; data[o + 5] = t.BZ;
            data[o + 6] = t.CX; data[o + 7] = t.CY; data[o + 8] = t.CZ;
        }
        return data;
    }

    // mode: brawl or slopball. packages: comma list, first is the human, the rest are CPUs.
    // Returns {"error":...} or {"court":{...}} with the mode's static layout.
    [JSExport]
    public static string Start(string mode, string packages, int stocks, int difficulty, int seed)
    {
        MatchMode m = mode switch
        {
            "slopball" => new SlopballMode(),
            _ => new BrawlMode(stocks),
        };
        m.Arena = _arena;
        m.Sim = new ServerSimulation(_arena, m.Rule);
        var error = m.Begin(packages.Split(',', StringSplitOptions.RemoveEmptyEntries), _catalog!, difficulty, seed);
        if (error != null) return "{\"error\":" + Str(error) + "}";
        _mode = m;
        _tick = 0;
        var sb = new StringBuilder("{\"court\":");
        m.WriteCourt(sb);
        return sb.Append('}').ToString();
    }

    // input: the 22-byte InputState wire format built by JS, or empty to let a CPU drive P1.
    [JSExport]
    public static string Step(byte[] input)
    {
        var m = _mode!;
        _inputs.Clear();
        foreach (var f in m.Fighters)
            _inputs[f.Id] = !f.Cpu && input.Length == InputState.Size
                ? m.HumanInput(f, InputState.Deserialize(input))
                : m.CpuInput(f, _tick);
        m.Sim.Tick(_inputs);
        _tick++;
        m.AfterTick();
        return Frame();
    }

    static string Frame()
    {
        var m = _mode!;
        var sim = m.Sim;
        var sb = new StringBuilder(2048);
        sb.Append("{\"tick\":").Append(_tick).Append(",\"fighters\":[");
        for (int i = 0; i < m.Fighters.Count; i++)
        {
            var f = m.Fighters[i];
            var s = sim.GetState(f.Id);
            var def = sim.GetDefinition(f.Id);
            if (i > 0) sb.Append(',');
            sb.Append("{\"id\":").Append(f.Id).Append(",\"pkg\":").Append(Str(f.Entry.Identity.PackageId))
              .Append(",\"p\":[").Append(F(s.PX)).Append(',').Append(F(s.PY)).Append(',').Append(F(s.PZ)).Append(']')
              .Append(",\"yaw\":").Append(F(s.FacingYaw))
              .Append(",\"dmg\":").Append(F(s.DamagePercent))
              .Append(",\"deaths\":").Append(s.Deaths)
              .Append(",\"state\":").Append(Str(s.State.ToString()))
              .Append(",\"slot\":").Append(s.AttackSlot)
              .Append(",\"sequence\":").Append(s.AttackSequence)
              .Append(",\"attackTick\":").Append(s.AttackElapsedTicks)
              .Append(",\"grounded\":").Append(s.IsGrounded ? 1 : 0)
              .Append(",\"inv\":").Append(s.InvincibilityTicks)
              .Append(",\"stop\":").Append(s.HitstopTicks)
              .Append(",\"target\":").Append(s.TargetEntityId)
              .Append(",\"form\":").Append(s.Form.Active)
              .Append(",\"anim\":");
            var args = new object?[] { f.Id, s, def, null, null, 0f, false };
            bool posed = (bool)ResolveBoneAnimFrame.Invoke(sim, args)!;
            if (posed && args[3] is BakedAnimationData baked && args[4] is string anim)
            {
                float frame = (float)args[5]!;
                int animIdx = baked.FindAnimIndex(anim);
                sb.Append(Str(anim)).Append(",\"frame\":").Append(F(frame)).Append(",\"pts\":[");
                for (int b = 0; b < baked.BoneNames.Count; b++)
                {
                    if (b > 0) sb.Append(',');
                    if (HitboxGeometry.BakedPointToWorld(in s, def, baked, animIdx, frame, b, def.HurtboxBoneScale,
                        out float x, out float y, out float z))
                    {
                        HitboxGeometry.ApplyAttackPosePitch(in s, def, ref x, ref y, ref z);
                        sb.Append(F(x)).Append(',').Append(F(y)).Append(',').Append(F(z));
                    }
                    else sb.Append("null,null,null");
                }
                sb.Append(']');
            }
            else sb.Append("null,\"frame\":0,\"pts\":[]");
            WritePresentationState(sb, sim, f.Id, in s);
            sb.Append('}');
        }
        sb.Append("],\"hitboxes\":[");
        bool first = true;
        foreach (var h in sim.Resolver.GetActiveHitboxes())
        {
            if (!first) sb.Append(',');
            first = false;
            bool capsule = h.Shape == HitboxShape.Capsule;
            sb.Append("{\"a\":[").Append(F(h.X)).Append(',').Append(F(h.Y)).Append(',').Append(F(h.Z))
              .Append("],\"b\":[").Append(F(capsule ? h.EndX : h.X)).Append(',').Append(F(capsule ? h.EndY : h.Y)).Append(',')
              .Append(F(capsule ? h.EndZ : h.Z)).Append("],\"r\":").Append(F(h.Radius))
              .Append(",\"shape\":").Append((int)h.Shape).Append(",\"owner\":").Append(h.OwnerId)
              .Append(",\"slot\":").Append(h.AttackSlot).Append(",\"air\":").Append(h.ActivationAirborne ? 1 : 0)
              .Append(",\"projectile\":").Append(h.StickyFuseTicks > 0 || h.ContactExplosion && h.AttackSlot == AbilitySlots.A || h.Perch.HasValue ? 1 : 0)
              .Append(",\"activation\":").Append(h.ActivationId).Append(",\"operation\":").Append(h.VisualOperationIndex)
              .Append(",\"attached\":").Append(h.AttachedEntityId).Append(",\"perch\":").Append(h.Perch.HasValue ? 1 : 0)
              .Append(",\"visualScale\":").Append(F(ProjectileScale(sim, in h)))
              .Append(",\"emit\":").Append(h.EmissionId).Append(",\"age\":").Append(h.AgeTicks)
              .Append(",\"v\":[").Append(F(h.VX)).Append(',').Append(F(h.VY)).Append(',').Append(F(h.VZ)).Append("]}");
        }
        sb.Append("],\"hits\":[");
        first = true;
        foreach (var hit in sim.LastTickHits)
        {
            if (!first) sb.Append(',');
            first = false;
            if (!m.TryGetPoint(hit.TargetEntityId, out float tx, out float ty, out float tz))
            {
                var t = sim.GetState(hit.TargetEntityId);
                (tx, ty, tz) = (t.PX, t.PY + 1f, t.PZ);
            }
            sb.Append('[').Append(hit.TargetEntityId).Append(',').Append(hit.OwnerEntityId).Append(',').Append(F(hit.Damage))
              .Append(',').Append(hit.Blocked ? 1 : 0).Append(',').Append(F(tx)).Append(',').Append(F(ty))
              .Append(',').Append(F(tz)).Append(',').Append(hit.HitstopTicks).Append(',').Append(hit.AttackSlot);
            var slot = hit.AttackSlot > 0
                ? sim.GetDefinition(hit.OwnerEntityId)?.ForForm(hit.Form).GetCookedSlotAbility(hit.AttackSlot, hit.Airborne)
                : null;
            sb.Append(',').Append(hit.StunTicks).Append(',').Append(string.IsNullOrEmpty(slot?.HitPresentationId) ? 0 : 1)
              .Append(',').Append(F(hit.ImpactForce)).Append(',').Append(F(hit.DirX)).Append(',').Append(F(hit.DirZ)).Append(',').Append(hit.KnockbackAngle)
              .Append(',').Append(F(hit.HitX)).Append(',').Append(F(hit.HitY)).Append(',').Append(F(hit.HitZ)).Append(']');
        }
        sb.Append("],\"deaths\":[");
        first = true;
        foreach (var d in sim.LastTickDeaths)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append('[').Append(d.EntityId).Append(',').Append(F(d.State.PX)).Append(',').Append(F(d.State.PY))
              .Append(',').Append(F(d.State.PZ)).Append(']');
        }
        sb.Append("],\"events\":[");
        first = true;
        foreach (var e in sim.GetPresentationEvents(clear: true))
        {
            if (!first) sb.Append(',');
            first = false;
            var pl = e.Placement;
            sb.Append("{\"id\":").Append(Str(e.PresentationId)).Append(",\"entity\":").Append(e.EntityId)
              .Append(",\"source\":").Append(Str(e.Source.ToString()))
              .Append(",\"p\":[").Append(F(e.WorldX)).Append(',').Append(F(e.WorldY)).Append(',').Append(F(e.WorldZ))
              .Append("],\"yaw\":").Append(F(e.WorldYaw))
              .Append(",\"bone\":").Append(pl.AttachmentMode == AuthoringPresentationAttachmentMode.Bone && pl.BoneId != null ? Str(pl.BoneId) : "null")
              .Append(",\"local\":[").Append(F(pl.LocalPositionX)).Append(',').Append(F(pl.LocalPositionY)).Append(',').Append(F(pl.LocalPositionZ))
              .Append("],\"scale\":[").Append(F(pl.LocalScaleX)).Append(',').Append(F(pl.LocalScaleY)).Append(',').Append(F(pl.LocalScaleZ))
              .Append("],\"rotation\":[").Append(F(pl.LocalRotationX)).Append(',').Append(F(pl.LocalRotationY)).Append(',').Append(F(pl.LocalRotationZ))
              .Append("],\"sequence\":").Append(e.AttackSequence)
              .Append(",\"ticks\":").Append(pl.DurationTicks).Append('}');
        }
        sb.Append("],\"mode\":");
        m.WriteFrame(sb);
        sb.Append(",\"ended\":").Append(m.Ended ? "true" : "false");
        return sb.Append('}').ToString();
    }

    static IEnumerable<CookedCapabilityParameters> Capabilities(CookedSlotDefinition? slot)
    {
        if (slot == null) yield break;
        foreach (var stage in slot.Timeline.Stages)
        foreach (var operation in stage.Operations)
            if (operation is CookedStartCapabilityOperation start) yield return start.Parameters;
    }

    static float ProjectileScale(ServerSimulation sim, in Hitbox hitbox)
    {
        var slot = sim.GetDefinition(hitbox.OwnerId)?.ForForm(hitbox.Form).GetCookedSlotAbility(hitbox.AttackSlot, hitbox.ActivationAirborne);
        foreach (var capability in Capabilities(slot))
            if (capability is CookedChargedProjectileCapabilityParameters charge)
                return hitbox.Radius / charge.Projectile.Radius;
        return 1f;
    }

    static void WritePresentationState(StringBuilder sb, ServerSimulation sim, ulong id, in CharacterState state)
    {
        int tier = -1;
        float progress = 0;
        var slot = sim.GetActiveMove(id)?.Slot;
        foreach (var capability in Capabilities(slot))
        {
            if (capability is CookedChargedProjectileCapabilityParameters charge)
            {
                tier = charge.GetChargeTier(state.ChargeTicks);
                progress = Math.Min(1f, state.ChargeTicks / (float)charge.MaxChargeTicks);
            }
            if (capability is CookedChargedStreamCapabilityParameters stream) progress = stream.GetCharge(state.ChargeTicks);
            if (capability is CookedFlashCutCapabilityParameters flash)
                sb.Append(",\"flash\":{\"stanceTicks\":").Append(flash.StanceTicks)
                  .Append(",\"legTicks\":").Append(flash.LegTicks).Append(",\"legAnimationId\":").Append(Str(flash.LegAnimationId)).Append('}');
        }
        sb.Append(",\"chargeTier\":").Append(tier).Append(",\"chargeProgress\":").Append(F(progress))
          .Append(",\"partner\":").Append(state.InteractionPartnerId);
    }

    internal static string F(float v) => float.IsFinite(v) ? v.ToString("0.####", CultureInfo.InvariantCulture) : "0";

    internal static string Str(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (char c in s)
        {
            if (c == '"' || c == '\\') sb.Append('\\').Append(c);
            else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
            else sb.Append(c);
        }
        return sb.Append('"').ToString();
    }
}
