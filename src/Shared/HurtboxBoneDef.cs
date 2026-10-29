namespace SlopArena.Shared
{
    public struct HurtboxBoneDef
    {
        public string BoneName;
        public float OffX, OffY, OffZ;
        public float Radius;
        public string? EndBoneName;
        public float EndOffX, EndOffY, EndOffZ;

        public HurtboxBoneDef(string bone, float ox, float oy, float oz, float r,
            string? endBone = null, float endOx = 0f, float endOy = 0f, float endOz = 0f)
        {
            BoneName = bone;
            OffX = ox; OffY = oy; OffZ = oz;
            Radius = r;
            EndBoneName = endBone;
            EndOffX = endOx; EndOffY = endOy; EndOffZ = endOz;
        }
    }
}
