if (UnityEditor.EditorApplication.isPlaying) throw new System.Exception("VFX mesh snapshot requires Edit Mode");
var paths = new[] {
    "Assets/CharacterPackages/bonk/Presentation/BonkPotLidProjectile.prefab",
    "Assets/CharacterPackages/bashie/Presentation/BashieRing.prefab",
    "Assets/CharacterPackages/wibou/Presentation/WibouKunaiProjectile.prefab",
};
float[] V3(UnityEngine.Vector3 v) => new[] { v.x, v.y, v.z };
var result = new System.Collections.Generic.SortedDictionary<string, object>();
foreach (var path in paths) {
    var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(path);
    if (prefab == null) throw new System.Exception(path);
    var transforms = prefab.GetComponentsInChildren<UnityEngine.Transform>(true);
    var nodes = new System.Collections.Generic.List<object>();
    foreach (var transform in transforms) {
        var filter = transform.GetComponent<UnityEngine.MeshFilter>();
        var renderer = transform.GetComponent<UnityEngine.MeshRenderer>();
        var parts = new System.Collections.Generic.List<object>();
        if (filter != null && renderer != null && filter.sharedMesh != null) {
            var mesh = filter.sharedMesh;
            var materials = renderer.sharedMaterials;
            for (int sub = 0; sub < mesh.subMeshCount; sub++) {
                var material = materials[System.Math.Min(sub, materials.Length - 1)];
                parts.Add(new {
                    id = UnityEditor.AssetDatabase.AssetPathToGUID(UnityEditor.AssetDatabase.GetAssetPath(mesh)) + ":" + mesh.name + ":" + sub,
                    position = System.Array.ConvertAll(mesh.vertices, V3),
                    normal = System.Array.ConvertAll(mesh.normals, V3),
                    uv = System.Array.ConvertAll(mesh.uv, v => new[] { v.x, v.y }),
                    index = mesh.GetTriangles(sub),
                    material = UnityEditor.AssetDatabase.AssetPathToGUID(UnityEditor.AssetDatabase.GetAssetPath(material)),
                    materialData = new {name=material.name, color=material.HasProperty("_BaseColor") ? new[]{material.GetColor("_BaseColor").r,material.GetColor("_BaseColor").g,material.GetColor("_BaseColor").b,material.GetColor("_BaseColor").a}:new[]{material.color.r,material.color.g,material.color.b,material.color.a},
                        texture=material.mainTexture != null ? UnityEditor.AssetDatabase.AssetPathToGUID(UnityEditor.AssetDatabase.GetAssetPath(material.mainTexture)):null},
                });
            }
        }
        nodes.Add(new { name = transform.name, parent = System.Array.IndexOf(transforms, transform.parent),
            active = transform.gameObject.activeSelf,
            p = V3(transform.localPosition), q = new[] {transform.localRotation.x,transform.localRotation.y,transform.localRotation.z,transform.localRotation.w},
            s = V3(transform.localScale), parts = parts });
    }
    result.Add("client/Unity/" + path, nodes);
}
var output = System.IO.Path.GetFullPath("../../tmp/web-vfx/projectile-mesh-snapshot.json");
System.IO.File.WriteAllText(output, Newtonsoft.Json.JsonConvert.SerializeObject(result));
return new {output, prefabs = result.Count};
