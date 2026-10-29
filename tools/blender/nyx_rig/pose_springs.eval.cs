if (EditorApplication.isPlaying) throw new InvalidOperationException("Run the controlled skin pose check with the Editor stopped.");
string output = "/home/binoui/Documents/projects/SlopArena/tmp/nyx-model/spring-dress-v3/review/unity-poses";
System.IO.Directory.CreateDirectory(output);
var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
var created = new List<GameObject>();
var evidence = new List<object>();
var bakedMeshes = new List<Mesh>();
try
{
    var root = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/CharacterPackages/nyx/Presentation/NyxRig.prefab"));
    created.Add(root); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
    var animator = root.GetComponentInChildren<Animator>(); animator.enabled = false;
    foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) renderer.gameObject.SetActive(renderer.name.StartsWith("Nyx_Real"));
    var bones = root.GetComponentsInChildren<Transform>(true).Where(t=>t.name.StartsWith("mixamorig:") || t.name.StartsWith("Nyx_Skirt_")).ToDictionary(t=>t.name);
    var rotations = bones.ToDictionary(p=>p.Key,p=>p.Value.localRotation);
    var positions = bones.ToDictionary(p=>p.Key,p=>p.Value.localPosition);
    Transform Bone(string name) => bones["mixamorig:"+name];
    void Rotate(string name, Vector3 axis, float degrees) { var b = Bone(name); b.rotation = Quaternion.AngleAxis(degrees,axis)*b.rotation; }
    void Down() { Rotate("LeftArm",Vector3.forward,75); Rotate("RightArm",Vector3.forward,-75); }
    var spring = root.GetComponentInChildren<SlopArena.Client.Entities.SkirtBoneSpring>();
    spring.Step(1f/60f);
    var baked = new Dictionary<SkinnedMeshRenderer,Mesh>();
    foreach(var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r=>r.gameObject.activeInHierarchy))
    {
        var go=new GameObject(skin.name+"_PoseBake");go.transform.SetParent(root.transform,false);go.transform.SetPositionAndRotation(skin.transform.position,skin.transform.rotation);go.transform.localScale=Vector3.one;
        var mesh=new Mesh();baked[skin]=mesh;bakedMeshes.Add(mesh);go.AddComponent<MeshFilter>().sharedMesh=mesh;
        go.AddComponent<MeshRenderer>().sharedMaterials=skin.sharedMaterials;skin.enabled=false;
    }
    var cameraObject = new GameObject("Nyx pose camera"); created.Add(cameraObject);
    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject,scene);
    var camera = cameraObject.AddComponent<Camera>(); camera.scene = scene; camera.enabled=false;
    camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.32f,.35f,.39f);
    camera.orthographic=true; camera.orthographicSize=1.12f; camera.nearClipPlane=.01f; camera.farClipPlane=30;
    foreach(var setting in new[] { (new Vector3(-3,4,4), 1.8f), (new Vector3(3,2,-2),1.2f) })
    {
        var go=new GameObject("Nyx pose light");created.Add(go);UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,scene);
        go.transform.position=setting.Item1;go.transform.LookAt(new Vector3(0,.9f,0));
        var light=go.AddComponent<Light>();light.type=LightType.Directional;light.intensity=setting.Item2;
    }
    void Capture(string name, float yaw, bool hand = false)
    {
        foreach(var pair in baked) pair.Key.BakeMesh(pair.Value);
        var center=hand?(Bone("RightHand").position+Bone("RightHandMiddle3").position)*.5f:new Vector3(0,.85f,0);
        camera.orthographicSize=hand?.18f:1.12f;
        camera.transform.position=center+Quaternion.AngleAxis(yaw,Vector3.up)*new Vector3(0,0,5);
        camera.transform.LookAt(center);
        var target=RenderTexture.GetTemporary(700,850,24);
        var active=RenderTexture.active;
        var image=new Texture2D(700,850,TextureFormat.RGB24,false);
        try { camera.targetTexture=target;camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,700,850),0,0);image.Apply();System.IO.File.WriteAllBytes(System.IO.Path.Combine(output,name+".png"),image.EncodeToPNG()); }
        finally { camera.targetTexture=null;RenderTexture.active=active;RenderTexture.ReleaseTemporary(target);UnityEngine.Object.DestroyImmediate(image); }
    }
    foreach(string pose in new[] {"arms-raised","arms-forward","wide-stance","deep-squat","high-front-kick","run-stride","fist","open-hand"})
    {
        foreach(var pair in bones) {pair.Value.localPosition=positions[pair.Key];pair.Value.localRotation=rotations[pair.Key];}
        if(pose=="arms-raised") {Rotate("LeftArm",Vector3.forward,-65);Rotate("RightArm",Vector3.forward,65);}
        else if(pose=="arms-forward") {Rotate("LeftArm",Vector3.up,85);Rotate("RightArm",Vector3.up,-85);}
        else Down();
        if(pose=="wide-stance") {Rotate("LeftUpLeg",Vector3.forward,-32);Rotate("RightUpLeg",Vector3.forward,32);}
        if(pose=="deep-squat") {
            Bone("Hips").position+=Vector3.down*.34f;
            foreach(string side in new[]{"Left","Right"}) {Rotate(side+"UpLeg",Vector3.right,-65);Rotate(side+"Leg",Vector3.right,115);Rotate(side+"Foot",Vector3.right,-50);}
        }
        if(pose=="high-front-kick") {Rotate("LeftUpLeg",Vector3.right,-90);Rotate("LeftLeg",Vector3.right,12);}
        if(pose=="run-stride") {Rotate("LeftUpLeg",Vector3.right,-55);Rotate("LeftLeg",Vector3.right,55);Rotate("RightUpLeg",Vector3.right,30);Rotate("RightLeg",Vector3.right,55);Rotate("LeftArm",Vector3.right,30);Rotate("RightArm",Vector3.right,-40);}
        if(pose=="fist") foreach(string side in new[]{"Left","Right"}) {
            float sign=side=="Left"?1:-1;
            foreach(string finger in new[]{"Index","Middle","Ring","Pinky"}) for(int j=1;j<=3;j++) Rotate(side+"Hand"+finger+j,Vector3.forward,sign*65);
            for(int j=1;j<=3;j++) Rotate(side+"HandThumb"+j,Vector3.forward,sign*25);
        }
        spring.ResetSimulation();for(int frame=0;frame<60;frame++) spring.Step(1f/60f);
        Capture(pose,pose=="high-front-kick"?75:pose=="arms-forward"||pose=="run-stride"?30:0);
        if(pose=="fist"||pose=="open-hand") Capture(pose+"-hand",35,true);
        var dress=root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(r=>r.name=="Nyx_Real_Dress");
        var collisionMesh=new Mesh();dress.BakeMesh(collisionMesh,true);
        var world=collisionMesh.vertices.Select(v=>dress.transform.TransformPoint(v)).ToArray();UnityEngine.Object.DestroyImmediate(collisionMesh);
        var springSettings=new SerializedObject(spring);var capsules=springSettings.FindProperty("_capsules");
        var contacts=new List<object>();
        for(int i=0;i<capsules.arraySize;i++) {
            var capsule=capsules.GetArrayElementAtIndex(i);
            var start=(Transform)capsule.FindPropertyRelative("Start").objectReferenceValue;
            var end=(Transform)capsule.FindPropertyRelative("End").objectReferenceValue;
            float radius=capsule.FindPropertyRelative("Radius").floatValue*root.transform.lossyScale.x;
            Vector3 axis=end.position-start.position;int inside=0;float depth=0;
            foreach(var point in world) {
                var closest=start.position+axis*Mathf.Clamp01(Vector3.Dot(point-start.position,axis)/axis.sqrMagnitude);
                float penetration=radius-Vector3.Distance(point,closest);
                if(penetration>.001f) inside++;depth=Mathf.Max(depth,penetration);
            }
            contacts.Add(new {bone=start.name,insideVertices=inside,maxPenetrationMetres=depth});
        }
        evidence.Add(new {pose,files=new[]{System.IO.Path.Combine(output,pose+".png")},settledFrames=60,capsules=contacts});
    }
    var contactPath=System.IO.Path.Combine(output,"pose-contact-report.json");
    System.IO.File.WriteAllText(contactPath,Newtonsoft.Json.JsonConvert.SerializeObject(evidence,Newtonsoft.Json.Formatting.Indented));
    return new {output,poses=evidence,contactPath};
}
finally { foreach(var mesh in bakedMeshes) if(mesh!=null) UnityEngine.Object.DestroyImmediate(mesh); foreach(var go in created) if(go!=null) UnityEngine.Object.DestroyImmediate(go);UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene); }
