using UnityEngine;
using UnityEditor;

public class SetupArenaSkybox : EditorWindow
{
    [MenuItem("Tools/SLOPArena/Prototypes/Setup Arena Skybox")]
    public static void Execute()
    {
        string exrPath = "Assets/Scenes/Arena_Offline/ReflectionProbe-0.exr";
        var cubemap = AssetDatabase.LoadAssetAtPath<Texture>(exrPath);
        if (cubemap == null)
        {
            Debug.LogError("Could not load " + exrPath);
            return;
        }

        string matPath = "Assets/Art/Materials/Skybox_Arena.mat";
        var existingMat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        Material mat;
        if (existingMat != null)
        {
            mat = existingMat;
        }
        else
        {
            var shader = Shader.Find("Skybox/Cubemap");
            if (shader == null)
            {
                Debug.LogError("Skybox/Cubemap shader not found");
                return;
            }
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, matPath);
            AssetDatabase.SaveAssets();
        }

        mat.SetTexture("_Tex", cubemap);
        mat.SetFloat("_Exposure", 1.0f);
        mat.SetFloat("_Rotation", 0f);
        RenderSettings.skybox = mat;
        DynamicGI.UpdateEnvironment();

        // Switch camera to Skybox clear
        var cams = FindObjectsByType<UnityEngine.Camera>(FindObjectsSortMode.None);
        if (cams.Length > 0)
        {
            Undo.RecordObject(cams[0], "Set Skybox Clear");
            cams[0].clearFlags = CameraClearFlags.Skybox;
        }

        Debug.Log("[Arena Skybox] Applied from " + exrPath + " -> " + matPath + " (clearFlags=Skybox)");
    }
}
