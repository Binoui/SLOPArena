const string Path = "Assets/CharacterPackages/nyx/Presentation/NyxRig.prefab";
var root = PrefabUtility.LoadPrefabContents(Path);
try
{
    var dress = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(s => s.name == "Nyx_Real_Dress");
    const string DressMaterialPath = "Assets/Art/Characters/nyx/Materials/Nyx_Dress.mat";
    var dressMaterial = AssetDatabase.LoadAssetAtPath<Material>(DressMaterialPath);
    if (dressMaterial == null)
    {
        dressMaterial = new Material(dress.sharedMaterial) { name = "Nyx_Dress" };
        AssetDatabase.CreateAsset(dressMaterial, DressMaterialPath);
    }
    dressMaterial.SetFloat("_Cull", 0);
    dressMaterial.doubleSidedGI = true;
    EditorUtility.SetDirty(dressMaterial);
    AssetDatabase.SaveAssetIfDirty(dressMaterial);
    dress.sharedMaterial = dressMaterial;
    var cloth = dress.GetComponent<Cloth>();
    cloth.bendingStiffness = 1;
    cloth.damping = 0.7f;
    cloth.selfCollisionDistance = 0.025f;
    cloth.selfCollisionStiffness = 1;
    cloth.SetSelfAndInterCollisionIndices(Enumerable.Range(0, cloth.vertices.Length).Select(i => (uint)i).ToList());
    float rootY = dress.rootBone.position.y;
    var coeff = cloth.coefficients;
    for (int i = 0; i < coeff.Length; i++)
    {
        float y = cloth.vertices[i].y + rootY;
        float t = Mathf.SmoothStep(0, 1, Mathf.Clamp01((1.05f - y) / 0.65f));
        coeff[i].maxDistance = 0.65f * t;
    }
    cloth.coefficients = coeff;
    PrefabUtility.SaveAsPrefabAsset(root, Path);
    return new { clothVertices = cloth.vertices.Length, selfCollision = cloth.selfCollisionDistance };
}
finally { PrefabUtility.UnloadPrefabContents(root); }
