using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopArena.Shared;

public sealed record CharacterBonePoint(string Id, string HumanoidBone, string BakedName, bool Required = false);

public static class CharacterBoneRegistry
{
    public const string RigPrefix = "rig:";

    // Stable baked names preserve existing attack bindings even on rigs with custom deform names.
    public static IReadOnlyList<CharacterBonePoint> HumanoidPoints { get; } = Array.AsReadOnly(new[]
    {
        new CharacterBonePoint("bone.head", "Head", "mixamorig:Head", true),
        new CharacterBonePoint("bone.spine2", "UpperChest", "mixamorig:Spine2", true),
        new CharacterBonePoint("bone.hips", "Hips", "mixamorig:Hips", true),
        new CharacterBonePoint("bone.right-hand", "RightHand", "mixamorig:RightHand", true),
        new CharacterBonePoint("bone.left-hand", "LeftHand", "mixamorig:LeftHand", true),
        new CharacterBonePoint("bone.right-foot", "RightFoot", "mixamorig:RightFoot", true),
        new CharacterBonePoint("bone.left-foot", "LeftFoot", "mixamorig:LeftFoot", true),
        new CharacterBonePoint("bone.right-toes", "RightToes", "mixamorig:RightToeBase", true),
        new CharacterBonePoint("bone.left-toes", "LeftToes", "mixamorig:LeftToeBase", true),
        new CharacterBonePoint("bone.neck", "Neck", "mixamorig:Neck"),
        new CharacterBonePoint("bone.right-upper-arm", "RightUpperArm", "mixamorig:RightArm"),
        new CharacterBonePoint("bone.left-upper-arm", "LeftUpperArm", "mixamorig:LeftArm"),
        new CharacterBonePoint("bone.right-lower-arm", "RightLowerArm", "mixamorig:RightForeArm"),
        new CharacterBonePoint("bone.left-lower-arm", "LeftLowerArm", "mixamorig:LeftForeArm"),
        new CharacterBonePoint("bone.right-upper-leg", "RightUpperLeg", "mixamorig:RightUpLeg"),
        new CharacterBonePoint("bone.left-upper-leg", "LeftUpperLeg", "mixamorig:LeftUpLeg"),
        new CharacterBonePoint("bone.right-lower-leg", "RightLowerLeg", "mixamorig:RightLeg"),
        new CharacterBonePoint("bone.left-lower-leg", "LeftLowerLeg", "mixamorig:LeftLeg"),
        new CharacterBonePoint("bone.right-hand-end", "RightMiddleProximal", "mixamorig:RightHandMiddle1"),
        new CharacterBonePoint("bone.left-hand-end", "LeftMiddleProximal", "mixamorig:LeftHandMiddle1"),
    });

    public static CharacterBonePoint? Find(string id)
        => HumanoidPoints.FirstOrDefault(x => x.Id == (id == "bone.spine" ? "bone.spine2" : id));

    public static string? BakedName(string? id)
        => id == null ? null : Find(id)?.BakedName ?? id;

    public static bool IsRigId(string id)
        => id != null && id.StartsWith(RigPrefix, StringComparison.Ordinal) && id.Length > RigPrefix.Length
            && id.Length <= 128 && !id.Substring(RigPrefix.Length).Any(char.IsControl);
}
