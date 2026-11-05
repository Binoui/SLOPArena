#if UNITY_EDITOR
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace SlopArena.EditorTools
{
    /// <summary>
    /// Import settings for the Splash Deck kraken (tools/blender/splash_deck/kraken_build.py). The FBX
    /// holds every clip on one take; kraken.json beside it lists each clip's frames, which become the
    /// clip splits here. Generic rig, no root motion node (the clips move the Root bone and the
    /// presenter places the object), no materials (the presenter assigns them), and no resampling or
    /// compression: the build already reduced the keys against the exact poses, and Unity's default
    /// 0.5 degree reduction would add up along 33-bone arms. Regenerating is: copy kraken.fbx and
    /// kraken.json into the folder, import both (json first).
    /// </summary>
    public sealed class KrakenHazardImport : AssetPostprocessor
    {
        public const string Folder = "Assets/Stages/splash_deck/Hazards/Resources/StageHazards/Kraken";
        public const string ModelPath = Folder + "/kraken.fbx";
        private const string DataPath = Folder + "/kraken.json";

        public override uint GetVersion() => 1;

        private void OnPreprocessModel()
        {
            // First import only: later changes made in the importer (the compression chosen against the
            // build's checkpoints, see kraken.json) stay in the .meta.
            if (assetPath != ModelPath || !assetImporter.importSettingsMissing) return;
            var model = (ModelImporter)assetImporter;
            model.globalScale = 1f;
            model.useFileScale = true;
            model.bakeAxisConversion = false;
            model.importCameras = false;
            model.importLights = false;
            model.importVisibility = false;
            model.importBlendShapes = false;
            model.isReadable = false;
            model.meshCompression = ModelImporterMeshCompression.Off;
            model.importNormals = ModelImporterNormals.Import;
            model.materialImportMode = ModelImporterMaterialImportMode.None;
            model.animationType = ModelImporterAnimationType.Generic;
            model.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            model.motionNodeName = "";
            model.optimizeGameObjects = false;
            model.importAnimation = true;
            model.resampleCurves = false;
            model.animationCompression = ModelImporterAnimationCompression.Off;
            model.importAnimatedCustomProperties = false;
            model.importConstraints = false;
        }

        private void OnPreprocessAnimation()
        {
            if (assetPath != ModelPath) return;
            var model = (ModelImporter)assetImporter;
            string json = Path.Combine(Path.GetDirectoryName(Application.dataPath)!, DataPath);
            if (!File.Exists(json))
            {
                Debug.LogError($"[KrakenHazardImport] {DataPath} is missing; import it before the FBX.");
                return;
            }
            var data = JObject.Parse(File.ReadAllText(json));
            string? take = model.defaultClipAnimations.FirstOrDefault()?.takeName;
            if (take == null)
            {
                Debug.LogError("[KrakenHazardImport] kraken.fbx has no take.");
                return;
            }
            var loops = data["loops"]!.Select(t => (string)t).ToHashSet();
            model.clipAnimations = ((JObject)data["takes"]!["clips"]!).Properties().Select(p => new ModelImporterClipAnimation
            {
                name = p.Name,
                takeName = take,
                firstFrame = (float)p.Value[0]!,
                lastFrame = (float)p.Value[1]!,
                loopTime = loops.Contains(p.Name.Split('.').Last()),
                loopPose = false,
                lockRootRotation = false,
                lockRootHeightY = false,
                lockRootPositionXZ = false,
                keepOriginalOrientation = true,
                keepOriginalPositionY = true,
                keepOriginalPositionXZ = true,
                wrapMode = WrapMode.Default,
            }).ToArray();
        }
    }
}
#endif
