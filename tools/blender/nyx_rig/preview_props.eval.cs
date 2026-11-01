if (EditorApplication.isPlaying) throw new InvalidOperationException("Run the prop event preview with the Editor stopped.");
string output = "/home/binoui/Documents/projects/SlopArena/tmp/nyx-model/props/unity-preview";
System.IO.Directory.CreateDirectory(output);
var previousScene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
var previousSelection = Selection.activeObject;
var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
var dispatcher = new SlopArena.Client.World.TimelinePresentationDispatcher();
var created = new List<UnityEngine.Object>();
var evidence = new List<object>();
var checks = new List<string>();
var eventHandoff = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(output), "events.json")));
ushort Duration(string form, string slot) => (ushort)eventHandoff["moves"].Single(m => (string)m["form"] == form && (string)m["slot"] == slot)["operationsToAdd"][0]["placement"]["durationTicks"];
ushort ground3Duration = Duration("fake", "ground.2"), air3Duration = Duration("fake", "air.2");
ushort liveDuration = Duration("real", "ground.F"), logoffDuration = Duration("fake", "ground.F");
try
{
    var host = new GameObject("Nyx prop event preview"); created.Add(host);
    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(host, scene);
    var root = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/CharacterPackages/nyx/Presentation/NyxRig.prefab"), host.transform);
    root.GetComponentInChildren<Animator>(true).enabled = false;
    var owner = host.AddComponent<SlopArena.Client.Entities.PlayerRenderer>();
    var forms = root.GetComponentInChildren<SlopArena.Client.Entities.FormPresentation>(true);
    var skins = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).ToDictionary(r => r.name);
    var bones = root.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("mixamorig:")).ToDictionary(t => t.name);
    var saved = AssetDatabase.LoadAssetAtPath<SlopArena.Client.Animation.CharacterAssetCatalog>("Assets/CharacterPackages/nyx/CharacterAssetCatalog.asset");
    var catalog = ScriptableObject.CreateInstance<SlopArena.Client.Animation.CharacterAnimationCatalog>(); created.Add(catalog);
    catalog.Presentations = saved.Presentations.Select(b => new SlopArena.Client.Animation.CharacterAnimationCatalog.PresentationEntry { SemanticId = b.SemanticId, Prefab = b.Prefab }).ToArray();
    dispatcher.Register(91, owner, catalog);
    var fieldFlags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
    void State(byte sequence, ushort tick, SlopArena.Shared.ActionState action = SlopArena.Shared.ActionState.Attacking)
    {
        var state = new SlopArena.Shared.CharacterState { State = action, AttackSequence = sequence, AttackElapsedTicks = tick };
        typeof(SlopArena.Client.Entities.PlayerRenderer).GetField("_lastState", fieldFlags).SetValue(owner, state);
        typeof(SlopArena.Client.Entities.PlayerRenderer).GetField("_lastAnimState", fieldFlags).SetValue(owner, action);
    }
    bool Visible(string name) => skins[name].gameObject.activeInHierarchy;
    void Check(bool value, string description) { if (!value) throw new InvalidOperationException(description); checks.Add(description); }
    SlopArena.Shared.TimelinePresentationEvent Event(byte sequence, string id, ushort duration)
        => new SlopArena.Shared.TimelinePresentationEvent((uint)(100+sequence), 91, 0, id, sequence, SlopArena.Shared.PresentationEventSource.Timeline, 0, 0, 0, 0)
        { Placement = new SlopArena.Shared.PresentationPlacement(AttachmentMode: SlopArena.Shared.AuthoringPresentationAttachmentMode.Bone, BoneId: "bone.hips", DurationTicks: duration) };
    var none = Array.Empty<SlopArena.Shared.TimelinePresentationEvent>();
    void Record(string name) => evidence.Add(new { name, form = forms.ShownForm, travelHidden = forms.TravelHidden, renderers = skins.Values.Select(r => new { name = r.name, active = r.gameObject.activeInHierarchy, enabled = r.enabled }).ToArray() });
    void Rotate(string name, Vector3 axis, float degrees) { var t = bones["mixamorig:"+name]; t.rotation = Quaternion.AngleAxis(degrees, axis)*t.rotation; }
    Rotate("LeftArm", Vector3.forward, 75); Rotate("RightArm", Vector3.forward, -75);
    var rest = bones.ToDictionary(p => p.Key, p => p.Value.localRotation);
    void Selfie()
    {
        foreach (var pair in rest) bones[pair.Key].localRotation = pair.Value;
        Rotate("RightArm", Vector3.forward, 75);
        Rotate("RightArm", Vector3.up, -90);
        Rotate("RightForeArm", Vector3.right, 85);
    }
    var cameraObject = new GameObject("Nyx prop camera"); created.Add(cameraObject);
    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject, scene);
    var camera = cameraObject.AddComponent<Camera>(); camera.scene = scene; camera.enabled = false;
    camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.32f,.35f,.39f);
    camera.orthographic = true; camera.nearClipPlane = .01f; camera.farClipPlane = 30;
    foreach (var setting in new[] { (new Vector3(-3,4,4), 1.6f), (new Vector3(3,2,-2), 1.1f) })
    {
        var lightObject = new GameObject("Nyx prop light"); created.Add(lightObject);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lightObject, scene);
        lightObject.transform.position = setting.Item1; lightObject.transform.LookAt(new Vector3(0,.9f,0));
        var light = lightObject.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = setting.Item2;
    }
    void Capture(string name, Vector3 center, float size, float yaw)
    {
        var copies = new List<GameObject>(); var meshes = new List<Mesh>(); var activeSkins = skins.Values.Where(r => r.gameObject.activeInHierarchy).ToArray();
        try
        {
            foreach (var skin in activeSkins)
            {
                var mesh = new Mesh(); meshes.Add(mesh); skin.BakeMesh(mesh, true);
                var copy = new GameObject(skin.name+" baked capture"); copies.Add(copy);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(copy, scene);
                copy.transform.SetPositionAndRotation(skin.transform.position, skin.transform.rotation);
                copy.transform.localScale = skin.transform.lossyScale;
                copy.AddComponent<MeshFilter>().sharedMesh = mesh;
                copy.AddComponent<MeshRenderer>().sharedMaterials = skin.sharedMaterials;
                skin.enabled = false;
            }
            camera.orthographicSize = size;
            camera.transform.position = center+Quaternion.AngleAxis(yaw,Vector3.up)*new Vector3(0,0,5);
            camera.transform.LookAt(center);
            var target = RenderTexture.GetTemporary(850,950,24); var active = RenderTexture.active;
            var image = new Texture2D(850,950,TextureFormat.RGB24,false);
            try { camera.targetTexture=target; camera.Render(); RenderTexture.active=target; image.ReadPixels(new Rect(0,0,850,950),0,0); image.Apply(); System.IO.File.WriteAllBytes(System.IO.Path.Combine(output,name+".png"),image.EncodeToPNG()); }
            finally { camera.targetTexture=null; RenderTexture.active=active; RenderTexture.ReleaseTemporary(target); UnityEngine.Object.DestroyImmediate(image); }
        }
        finally { foreach(var copy in copies) UnityEngine.Object.DestroyImmediate(copy); foreach(var mesh in meshes) UnityEngine.Object.DestroyImmediate(mesh); foreach(var skin in activeSkins) skin.enabled=true; }
    }
    forms.Show(0); State(1,0);
    Check(!Visible("Nyx_Phone") && !Visible("Nyx_PoleHeld") && Visible("Nyx_Real_Dress"), "Saved real defaults hide phone and held pole and keep spring dress"); Record("real-default");
    Check(forms.Skins.Length == 8, "All eight renderers participate in death and respawn presentation");
    forms.Show(1);
    Check(Visible("Nyx_Backpack") && Visible("Nyx_BackpackPole") && Visible("Nyx_Phone") && !Visible("Nyx_PoleHeld"), "Fake defaults show backpack pole and one right-hand phone"); Record("fake-default");
    Capture("fake-stowed",new Vector3(0,1.27f,0),.9f,155);
    var pole = Event(1,"presentation.nyx.props.pole-held",ground3Duration); dispatcher.Tick(new[]{pole});
    Check(Visible("Nyx_PoleHeld") && !Visible("Nyx_BackpackPole") && Visible("Nyx_Backpack"), "Ground 3 event shows held pole and hides only backpack pole"); Record("fake-ground3");
    Capture("fake-ground3",new Vector3(0,1.18f,0),1f,155);
    dispatcher.Tick(new[]{pole}); Check(host.GetComponentsInChildren<SlopArena.Client.World.PropVisibilityPresentation>(true).Length==1,"Duplicate event is applied once");
    State(1,1,SlopArena.Shared.ActionState.Idle); dispatcher.Tick(none);
    Check(!Visible("Nyx_PoleHeld") && Visible("Nyx_BackpackPole"), "Attack interruption restores stowed pole");
    State(2,0); var air = Event(2,"presentation.nyx.props.pole-held",air3Duration); dispatcher.Tick(new[]{air});
    Check(Visible("Nyx_PoleHeld") && !Visible("Nyx_BackpackPole"), "Air 3 event uses the same pole toggle");
    dispatcher.Retract(new[]{air}); Check(!Visible("Nyx_PoleHeld") && Visible("Nyx_BackpackPole"), "Rollback retraction restores default props");
    dispatcher.Tick(new[]{air}); Check(Visible("Nyx_PoleHeld"), "Retracted event can replay");
    State(2,air3Duration); dispatcher.Tick(none); Check(!Visible("Nyx_PoleHeld"), "Timeline duration expiry restores held pole default");
    forms.Show(0); State(3,0); Selfie(); var raise = Event(3,"presentation.nyx.props.phone-raise",liveDuration); dispatcher.Tick(new[]{raise});
    Check(Visible("Nyx_Phone") && Visible("Nyx_Real"), "Going Live event exposes phone on real form"); Record("going-live-tick0");
    var hand = bones["mixamorig:RightHand"].position; Capture("real-going-live-phone",hand+Vector3.up*.08f,.22f,-25);
    Capture("real-going-live",new Vector3(0,.95f,0),1.14f,25);
    for(int i=0;i<3;i++) dispatcher.Tick(none);
    Check(Visible("Nyx_Phone"), "Unchanged attack clock during hitstop retains prop");
    forms.Show(0,true); Check(skins.Values.All(r=>!r.gameObject.activeInHierarchy), "Travel hidden suppresses every prop");
    forms.Show(0); Check(Visible("Nyx_Phone"), "Travel visibility restoration keeps current event");
    forms.Show(1); forms.Show(0); Check(Visible("Nyx_Phone"), "Corrected form preserves the active event override");
    State(3,(ushort)(liveDuration-1)); dispatcher.Tick(none); Check(Visible("Nyx_Phone"), $"Going Live retains phone immediately before swap tick {liveDuration}");
    forms.Show(1); State(3,liveDuration); dispatcher.Tick(none);
    Check(Visible("Nyx_Phone") && !Visible("Nyx_Real"), $"Swap tick {liveDuration} switches to fake defaults with one phone"); Record($"going-live-swap-tick{liveDuration}");
    Capture("fake-phone",hand+Vector3.up*.08f,.22f,-25); Capture("fake-phone-full",new Vector3(0,.95f,0),1.14f,25);
    State(4,0); var drop = Event(4,"presentation.nyx.props.phone-drop",logoffDuration); dispatcher.Tick(new[]{drop});
    Check(!Visible("Nyx_Phone") && Visible("Nyx_Fake"), "Logging Off event hides phone on fake form"); Record("logging-off-tick0");
    State(4,(ushort)(logoffDuration-1)); dispatcher.Tick(none); Check(!Visible("Nyx_Phone"), $"Dropped phone stays hidden before swap tick {logoffDuration}");
    forms.Show(0); State(4,logoffDuration); dispatcher.Tick(none); Check(!Visible("Nyx_Phone") && Visible("Nyx_Real_Dress"), "Logging Off swap restores real defaults and spring dress"); Record($"logging-off-swap-tick{logoffDuration}");
    forms.Show(1); Check(Visible("Nyx_Phone"), "Returning to fake restores its default phone");
    State(5,0); dispatcher.Tick(new[]{Event(4,"presentation.nyx.props.pole-held",ground3Duration)}); Check(!Visible("Nyx_PoleHeld"), "Late event from old attack cannot toggle current props");
    dispatcher.Tick(new[]{Event(5,"presentation.nyx.props.pole-held",ground3Duration)}); owner.enabled=false; dispatcher.Tick(none);
    Check(!Visible("Nyx_PoleHeld") && Visible("Nyx_BackpackPole"), "Disabled owner releases prop override"); owner.enabled=true;
    State(6,0); dispatcher.Tick(new[]{Event(6,"presentation.nyx.props.pole-held",ground3Duration)}); dispatcher.ResetForFreshSimulation();
    Check(!Visible("Nyx_PoleHeld") && Visible("Nyx_BackpackPole"), "Fresh simulation clears prop overrides");
    string report = System.IO.Path.Combine(output,"toggle-proof.json");
    var result = new { success=true, checks, states=evidence, source="Actual TimelinePresentationDispatcher with manually fired TimelinePresentationEvent values and saved catalog prefabs", savedPrefab="Assets/CharacterPackages/nyx/Presentation/NyxRig.prefab", renderers=skins.Keys.ToArray(), previousScene=previousScene.path, previewSceneDisposable=true };
    System.IO.File.WriteAllText(report,Newtonsoft.Json.JsonConvert.SerializeObject(result,Newtonsoft.Json.Formatting.Indented));
    return new { success=true, checks=checks.Count, output, report };
}
finally
{
    dispatcher.Clear();
    foreach(var obj in created) if(obj!=null) UnityEngine.Object.DestroyImmediate(obj);
    UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
    Selection.activeObject=previousSelection;
}
