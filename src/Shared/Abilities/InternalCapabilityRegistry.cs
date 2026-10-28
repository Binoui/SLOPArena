using SlopArena.Shared;

namespace SlopArena.Shared.Abilities;

public static class InternalCapabilityRegistry
{
    public static bool TryCreate(
        string capabilityId,
        string capabilityVersion,
        CookedCapabilityParameters parameters,
        out ServerAbility capability)
    {
        capability = null!;
        if (!CharacterPackageCompiler.IsRuntimeCapability(capabilityId, capabilityVersion) || parameters == null)
            return false;

        switch (capabilityId)
        {
            case CharacterPackageCompiler.AimedRecoveryCapabilityId
                when parameters is CookedAimedRecoveryCapabilityParameters recovery:
                capability = new AimedRecoveryAbility(recovery);
                return true;
            case CharacterPackageCompiler.ChargedProjectileCapabilityId
                when parameters is CookedChargedProjectileCapabilityParameters projectile:
                capability = new ChargedProjectileAbility(projectile);
                return true;
            case CharacterPackageCompiler.FingerGunsCapabilityId
                when parameters is CookedFingerGunsCapabilityParameters guns:
                capability = new FingerGunsAbility(guns);
                return true;
            case CharacterPackageCompiler.ChargedStreamCapabilityId
                when parameters is CookedChargedStreamCapabilityParameters stream:
                capability = new ChargedStreamAbility(stream);
                return true;
            case "slop.internal.fightguy.rising-dragon.v1" when parameters is CookedRisingDragonCapabilityParameters rising:
                capability = new FightGuyRisingKick(rising);
                return true;
            case "slop.internal.fightguy.cyclone-kick.v1" when parameters is CookedCycloneKickCapabilityParameters cyclone:
                capability = new FightGuyCycloneKick(cyclone);
                return true;

            case "slop.internal.wibou.rising-slash.v1" when parameters is CookedWibouRisingSlashCapabilityParameters rising:
                capability = new WibouRisingSlash(rising);
                return true;
            case CharacterPackageCompiler.BladeFlurryCapabilityId when parameters is CookedWibouBladeFlurryCapabilityParameters flurry:
                capability = new WibouUltFlurry(flurry);
                return true;
            case CharacterPackageCompiler.ChargedDirectionalDashCapabilityId
                when capabilityVersion == CharacterPackageCompiler.ChargedDirectionalDashCapabilityVersion &&
                     parameters is CookedChargedDirectionalDashCapabilityParameters dash:
                capability = new ChargedDirectionalDashAbility(dash);
                return true;
            case CharacterPackageCompiler.TargetedLeapCapabilityId when parameters is CookedTargetedLeapCapabilityParameters leap:
                capability = new TargetedLeapAbility(leap);
                return true;
            case CharacterPackageCompiler.SteeredChargeCapabilityId when parameters is CookedSteeredChargeCapabilityParameters charge:
                capability = new SteeredChargeAbility(charge);
                return true;
            case CharacterPackageCompiler.ThrustCapabilityId when parameters is CookedThrustCapabilityParameters thrust:
                capability = new ThrustAbility(thrust);
                return true;
            case "slop.internal.manki.round-bomb.v1" when parameters is CookedMankiRoundBombCapabilityParameters bomb:
                capability = new MankiRoundBomb(bomb);
                return true;
            case "slop.internal.manki.jetpack-boost.v1" when parameters is CookedMankiJetpackBoostCapabilityParameters jetpack:
                capability = new MankiJetpackBoost(jetpack);
                return true;
            case "slop.internal.manki.bazooka.v2" when parameters is CookedMankiBazookaCapabilityParameters bazooka:
                capability = new MankiBazooka(bazooka);
                return true;
            case "slop.internal.manki.aerosol-inferno.v1" when parameters is CookedMankiAerosolInfernoCapabilityParameters aerosol:
                capability = new MankiAerosolInferno(aerosol);
                return true;
            default:
                return false;
        }
    }
}
