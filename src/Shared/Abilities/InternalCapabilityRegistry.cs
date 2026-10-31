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
            case CharacterPackageCompiler.FlashCutCapabilityId when parameters is CookedFlashCutCapabilityParameters flash:
                capability = new FlashCutAbility(flash);
                return true;
            case CharacterPackageCompiler.DashStrikeCapabilityId
                when capabilityVersion == CharacterPackageCompiler.DashStrikeCapabilityVersion &&
                     parameters is CookedDashStrikeCapabilityParameters dash:
                capability = new DashStrikeAbility(dash);
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
            case CharacterPackageCompiler.CannonballCapabilityId when parameters is CookedCannonballCapabilityParameters cannonball:
                capability = new CannonballAbility(cannonball);
                return true;
            case CharacterPackageCompiler.TetherCapabilityId when parameters is CookedTetherCapabilityParameters tether:
                capability = new TetherAbility(tether);
                return true;
            case CharacterPackageCompiler.CommandGrabCapabilityId when parameters is CookedCommandGrabCapabilityParameters grab:
                capability = new CommandGrabAbility(grab);
                return true;
            case "slop.internal.manki.proximity-mine.v1" when parameters is CookedMankiRoundBombCapabilityParameters bomb:
                capability = new MankiRoundBomb(bomb);
                return true;
            case "slop.internal.manki.jetpack-boost.v3" when parameters is CookedMankiJetpackBoostCapabilityParameters jetpack:
                capability = new MankiJetpackBoost(jetpack);
                return true;
            case "slop.internal.manki.kamikaze.v1" when parameters is CookedMankiKamikazeCapabilityParameters kamikaze:
                capability = new MankiKamikaze(kamikaze);
                return true;
            case "slop.internal.manki.bazooka.v3" when parameters is CookedMankiBazookaCapabilityParameters { ChargeTicks: > 0 } charged:
                capability = new MankiBazooka(charged);
                return true;
            case "slop.internal.manki.bazooka.v2" when parameters is CookedMankiBazookaCapabilityParameters bazooka:
                capability = new MankiBazooka(bazooka);
                return true;
            default:
                return false;
        }
    }
}
