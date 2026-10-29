using System;
using SlopArena.Client.Entities;
using SlopArena.Shared;
using UnityEngine;

namespace SlopArena.Client.Combat;

public sealed class QuickDrawBracket : MonoBehaviour
{
    private Func<ulong, CharacterState> _getState;
    private PlayerRenderer[] _fighters;
    private PlayerRenderer _owner;
    private ArenaDefinition _arena;
    private LineRenderer[] _lines;
    private Material _material;

    public void Init(Func<ulong, CharacterState> getState, PlayerRenderer[] fighters, ulong localId, ArenaDefinition arena)
    {
        _getState = getState; _fighters = fighters; _arena = arena;
        _owner = Array.Find(fighters, x => x.EntityId == localId);
        _material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        _material.SetColor("_BaseColor", new Color(.2f, 1f, .3f));
        _lines = new LineRenderer[2];
        for (int i = 0; i < 2; i++)
        {
            var child = new GameObject("QuickDrawBracket");
            child.transform.SetParent(transform, false);
            var line = child.AddComponent<LineRenderer>();
            line.sharedMaterial = _material;
            line.positionCount = 4;
            line.widthMultiplier = .055f;
            line.useWorldSpace = true;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.enabled = false;
            _lines[i] = line;
        }
    }

    private void LateUpdate()
    {
        if (_lines == null) return;
        foreach (var line in _lines) line.enabled = false;
        if (_owner == null || _owner.CharacterDef == null) return;
        var owner = _getState(_owner.EntityId);
        if (owner.State is not (ActionState.Idle or ActionState.Run or ActionState.Crouching) ||
            owner.GetCooldown(AbilitySlots.R) != 0 || owner.AnimLockTicks != 0 || owner.HitstopTicks != 0 ||
            owner.HitstunTicks != 0 || owner.LandingLagTicks != 0 || owner.TargetEntityId == 0) return;
        var targetRenderer = Array.Find(_fighters, x => x != null && x.EntityId == owner.TargetEntityId);
        if (targetRenderer == null || targetRenderer.CharacterDef == null) return;
        var target = _getState(targetRenderer.EntityId);
        if (target.State == ActionState.Shielding || target.InvincibilityTicks != 0) return;
        var p = DashStrikeReach.Parameters(_owner.CharacterDef.GetCookedSlotAbility(AbilitySlots.R, !owner.IsGrounded));
        if (p == null || DashStrikeReach.Classify(p, in owner, in target, targetRenderer.CharacterDef) != DashStrikeContact.Tip ||
            !DashStrikeReach.CorridorClear(in owner, _owner.CharacterDef, in target, p, in _arena)) return;
        float floor = _arena.Heightmap.Sample(target.PX, target.PZ);
        if (!float.IsFinite(floor)) return;
        for (int i = 0; i < 2; i++)
        {
            float side = i == 0 ? -1 : 1;
            _lines[i].SetPositions(new[] {
                new Vector3(target.PX + side * .48f, floor + .1f, target.PZ - .64f),
                new Vector3(target.PX + side * .76f, floor + .1f, target.PZ - .64f),
                new Vector3(target.PX + side * .76f, floor + .1f, target.PZ + .64f),
                new Vector3(target.PX + side * .48f, floor + .1f, target.PZ + .64f) });
            _lines[i].enabled = true;
        }
    }
    private void OnDestroy() { if (_material != null) Destroy(_material); }
}
