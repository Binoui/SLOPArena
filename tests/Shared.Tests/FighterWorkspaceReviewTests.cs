using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class FighterWorkspaceReviewTests : IDisposable
{
    readonly string folder = Path.Combine(Path.GetTempPath(), "workspace-review-" + Guid.NewGuid().ToString("N"));
    public FighterWorkspaceReviewTests()
    {
        Directory.CreateDirectory(folder);
        var files = FighterItemAdmissionTests.Item();
        foreach (string name in new[] { "package.json", "character.json", "card.json", "tumbleweed.glb" }) File.WriteAllBytes(Path.Combine(folder, name), files[name]);
        var bindings = JsonNode.Parse(files["client.bindings"])!;
        var animations = new JsonObject(); foreach (var a in bindings["animations"]!.AsArray()) animations[a!["semanticId"]!.GetValue<string>()] = "fixture";
        File.WriteAllText(Path.Combine(folder, "cook.json"), new JsonObject { ["model"] = "tumbleweed.glb", ["animations"] = animations, ["creator"] = "test", ["license"] = "CC0", ["attribution"] = "", ["aiAssisted"] = false }.ToJsonString());
        File.WriteAllBytes(Path.Combine(folder, "tumbleweed.glb"), FootMesh());
    }
    public void Dispose() => Directory.Delete(folder, true);
    internal static byte[] FootMesh() => TestGlb.Rewrite(FighterItemAdmissionTests.Glb(), (root, _) =>
    {
        int foot = root["nodes"]!.AsArray().Select((n,i)=>(n,i)).Single(p=>p.n!["name"]!.GetValue<string>() == TestGlb.P + "LeftFoot").i;
        var joints = root["skins"]![0]!["joints"]!.AsArray(); int at = joints.Select((n,i)=>(n,i)).Single(p=>p.n!.GetValue<int>()==foot).i;
        var old = joints[0]!.DeepClone(); joints[0] = foot; joints[at] = old;
    });
    void EditCook(Action<JsonNode> edit) { string path=Path.Combine(folder,"cook.json"); var j=JsonNode.Parse(File.ReadAllText(path))!; edit(j);File.WriteAllText(path,j.ToJsonString()); }
    [Theory] [InlineData("model")] [InlineData("creator")] [InlineData("license")] [InlineData("animations")] [InlineData("aiAssisted")] [InlineData("attribution")]
    public void NullFieldsHaveFileAndFieldDiagnostics(string field)
    {
        EditCook(j=>j[field]=null); var result=LocalFighterWorkspace.Read(folder).Cook();
        Assert.Null(result.Item); Assert.Contains(result.Diagnostics,d=>d.Path=="cook.json."+field);
    }
    [Fact] public void NullClipValueHasSemanticPath() { EditCook(j=>j["animations"]!["anim.idle"]=null); Assert.Contains(LocalFighterWorkspace.Read(folder).Cook().Diagnostics,d=>d.Path=="cook.json.animations.anim.idle"); }
    [Fact] public void UnusedImagesAreIgnoredIncludingTheirSize()
    {
        var before=LocalFighterWorkspace.Read(folder); using(var f=File.Create(Path.Combine(folder,"notes.jpg")))f.SetLength(70*1024*1024);
        var after=LocalFighterWorkspace.Read(folder); Assert.Equal(before.Stamp,after.Stamp); var result=after.Cook(); Assert.NotNull(result.Item); Assert.DoesNotContain("notes.jpg",result.Item!.Keys);
    }
    [Fact] public void TypoAndMissingAnimationsAreBothReported()
    {
        EditCook(j=>{j["animations"]!["anim.idel"]="fixture";j["animations"]!.AsObject().Remove("anim.idle");});
        var result=LocalFighterWorkspace.Read(folder).Cook(); Assert.Contains(result.Diagnostics,d=>d.Path=="cook.json.animations.anim.idel"); Assert.Contains(result.Diagnostics,d=>d.Path=="cook.json.animations.anim.idle");
    }
    [Fact] public void SyntaxPathsKeepTheirFile() { File.WriteAllText(Path.Combine(folder,"character.json"),"{"); var d=LocalFighterWorkspace.Read(folder).Cook().Diagnostics; Assert.Contains(d,x=>LocalFighterWorkspace.DiagnosticPath(x).StartsWith("character.json")); Assert.Equal("character.json",LocalFighterWorkspace.DiagnosticPath(new CharacterDiagnostic(CharacterDiagnosticSeverity.Error,"json","character.json","bad"))); }
    [Fact] public void FailedRevisionsStayRecordedAndRequestsDuringCookAreQueued()
    {
        var s=new FighterWorkspaceSchedule(); Assert.False(s.Observe("broken",0)); Assert.True(s.Observe("broken",1)); s.Begin("broken"); Assert.False(s.Observe("broken",2)); s.Request(); Assert.True(s.Observe("broken",3)); s.Begin("broken"); s.Request(); Assert.True(s.Observe("broken",4)); Assert.False(s.Observe("changed",5)); Assert.True(s.Observe("changed",6));
    }
    [Fact] public void PeriodicProbeNoticesChangesAndFolderReplacement()
    {
        string before=LocalFighterWorkspace.Probe(folder); File.AppendAllText(Path.Combine(folder,"character.json")," "); Assert.NotEqual(before,LocalFighterWorkspace.Probe(folder));
        string old=folder+"-old"; Directory.Move(folder,old);Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"cook.json"),"{}");Assert.NotEqual(before,LocalFighterWorkspace.Probe(folder));Directory.Delete(old,true);
    }
    [Fact] public void HumanoidLimitMatchesAdmission() { File.WriteAllBytes(Path.Combine(folder,"humanoid.json"),new byte[65537]); Assert.Equal(65536,LocalFighterWorkspace.HumanoidByteLimit); Assert.Contains(LocalFighterWorkspace.Read(folder).Cook().Diagnostics,d=>d.Path=="humanoid.json"); }
    [Fact] public void OffsetPatchPreservesEveryOtherByteAndRefusesAConcurrentEdit()
    {
        string path=Path.Combine(folder,"character.json"); var j=JsonNode.Parse(File.ReadAllText(path))!; j["displayName"]="Kōji <3 & +1";
        string text=j.ToJsonString(new System.Text.Json.JsonSerializerOptions{WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}).Replace("\n","\r\n");File.WriteAllText(path,text);
        var s=LocalFighterWorkspace.Read(folder); Assert.NotNull(s.Cook().Item); byte[] original=File.ReadAllBytes(path); var expected=LocalFighterWorkspace.ReplaceOffset(original,s.SuggestedModelYOffset!.Value);
        s.ApplyModelYOffset(folder); Assert.Equal(expected,File.ReadAllBytes(path)); Assert.Contains("Kōji <3 & +1",File.ReadAllText(path));
        s=LocalFighterWorkspace.Read(folder);s.Cook(); Assert.Throws<IOException>(()=>s.ApplyModelYOffset(folder,()=>File.AppendAllText(path," ")));Assert.EndsWith(" ",File.ReadAllText(path)); Assert.Empty(Directory.GetFiles(folder,".workspace-*"));
    }
    [Fact] public void AtomicCacheBoundsRevisionsAndKeepsTrainingPin()
    {
        string cache=Path.Combine(folder,"cache"); Directory.CreateDirectory(Path.Combine(cache,"legacy")); File.WriteAllText(Path.Combine(cache,"legacy/item.json"),"{}");
        var s=LocalFighterWorkspace.Read(folder); var a=FighterWorkspaceCache.Cook(cache,folder,s);Assert.NotNull(a.Admission?.Fighter); Assert.False(Directory.Exists(Path.Combine(cache,"legacy")));
        using(var pin=FighterWorkspaceCache.Pin(a.Folder!))
        {
            for(int i=0;i<4;i++) Assert.NotNull(FighterWorkspaceCache.Cook(cache,folder,s).Admission?.Fighter);
            Assert.True(Directory.Exists(a.Folder)); Assert.Equal(2,Directory.GetDirectories(Path.GetDirectoryName(a.Folder!)!).Length);
        }
        string parent=Path.GetDirectoryName(a.Folder!)!;
        Assert.Single(Directory.GetDirectories(parent));
        string orphan=Path.Combine(parent,".pending-abandoned");Directory.CreateDirectory(orphan);File.WriteAllText(orphan+".lock","");
        Assert.NotNull(FighterWorkspaceCache.Cook(cache,folder,s).Admission?.Fighter);Assert.False(Directory.Exists(orphan));
        string active=Path.Combine(parent,".pending-active");Directory.CreateDirectory(active);
        using(var owner=new FileStream(active+".lock",FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None))
        { Assert.NotNull(FighterWorkspaceCache.Cook(cache,folder,s).Admission?.Fighter);Assert.True(Directory.Exists(active)); }
        Assert.NotNull(FighterWorkspaceCache.Cook(cache,folder,s).Admission?.Fighter);Assert.False(Directory.Exists(active));
    }
    [Fact] public async Task ConcurrentCooksNeverShareTheirWriteDirectory()
    {
        string cache=Path.Combine(folder,"cache"); var s=LocalFighterWorkspace.Read(folder);
        var results=await Task.WhenAll(Enumerable.Range(0,4).Select(_=>Task.Run(()=>FighterWorkspaceCache.Cook(cache,folder,s))));
        Assert.All(results,r=>Assert.NotNull(r.Admission?.Fighter));Assert.Equal(4,results.Select(r=>r.Folder).Distinct().Count());
        Assert.Single(Directory.GetDirectories(Path.GetDirectoryName(results[0].Folder!)!));
    }
    [Fact] public void OutputFailureIsDiagnosticAndCancellationDoesNotPublish()
    {
        string cache=Path.Combine(folder,"not-directory");File.WriteAllText(cache,"x");var s=LocalFighterWorkspace.Read(folder);var r=FighterWorkspaceCache.Cook(cache,folder,s); Assert.Contains(r.Diagnostics,d=>d.Code=="workspace.output"&&d.Path.StartsWith(cache));
        var c=new CancellationTokenSource();c.Cancel();Assert.Throws<OperationCanceledException>(()=>FighterWorkspaceCache.Cook(cache,folder,s,c.Token));
    }
    [Fact] public void LooseTorsoGeometryIsCappedWithAWarning()
    {
        var points = Enumerable.Range(0, 100).Select(_=>new MeshPoint(5, .5, 0)).ToArray();
        MeshPoint? Bone(string id) => id switch { "bone.head" => new MeshPoint(0,1,0), "bone.neck" => new MeshPoint(0,.8,0), "bone.left-upper-arm"=>new MeshPoint(.2,.8,0), "bone.right-upper-arm"=>new MeshPoint(-.2,.8,0), _=>new MeshPoint(0,0,0) };
        var diagnostics=new List<CharacterDiagnostic>(); var fit=PortableMeshMeasurements.FitCapsules(points,points.Select(_=>"torso").ToArray(),Bone,diagnostics);
        Assert.Equal(.2f,fit[1].Radius);Assert.Contains(diagnostics,d=>d.Code=="mesh.fit.loose-torso");
    }
    [Fact] public void LowPolygonRigidHatIsNotDiscardedByTheSkinPercentile()
    {
        var points=Enumerable.Range(0,1000).Select(_=>new MeshPoint(.01,1,0)).Append(new MeshPoint(.3,1,0)).ToArray();
        var rigid=points.Select((p,i)=>i==1000).ToArray();
        var fit=PortableMeshMeasurements.FitCapsules(points,points.Select(_=>"head").ToArray(),id=>id=="bone.head"?new MeshPoint(0,1,0):new MeshPoint(0,0,0),new List<CharacterDiagnostic>(),rigid);
        Assert.Equal(.3f,fit[0].Radius);
    }
    [Fact] public void RigidShoeChangesTheMeasuredSole()
    {
        byte[] glb=TestGlb.Rewrite(FootMesh(),(j,append)=>
        {
            var nodes=j["nodes"]!.AsArray();int foot=nodes.Select((n,i)=>(n,i)).Single(p=>p.n!["name"]!.GetValue<string>()==TestGlb.P+"LeftFoot").i;
            int at=nodes.Count;nodes.Add(new JsonObject{["name"]="shoe",["mesh"]=0,["translation"]=new JsonArray(0,-.5,0)});nodes[foot]!["children"]!.AsArray().Add(at);
        });
        Assert.Equal(.5f, PortableMeshMeasurements.Measure(glb,null,"fixture",1).ModelYOffset-PortableMeshMeasurements.Measure(FootMesh(),null,"fixture",1).ModelYOffset,3);
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public void RigidPartsAreFittedOrDiagnosed(bool attached)
    {
        byte[] glb=TestGlb.Rewrite(FootMesh(),(j,append)=>
        {
            var nodes=j["nodes"]!.AsArray();int head=nodes.Select((n,i)=>(n,i)).Single(p=>p.n!["name"]!.GetValue<string>()==TestGlb.P+"Head").i;
            int at=nodes.Count;nodes.Add(new JsonObject{["name"]="rigid-hat",["mesh"]=0,["translation"]=new JsonArray(0,0.2,0)});
            if(attached) { var h=nodes[head]!.AsObject();if(h["children"]==null)h["children"]=new JsonArray();h["children"]!.AsArray().Add(at); }else j["scenes"]![0]!["nodes"]!.AsArray().Add(at);
        });
        var fit=PortableMeshMeasurements.Measure(glb,null,"fixture",1);var plain=PortableMeshMeasurements.Measure(FootMesh(),null,"fixture",1);
        if(attached) { Assert.DoesNotContain(fit.Diagnostics,d=>d.Code=="mesh.fit.detached");Assert.True(fit.Hurtboxes[0].Radius>plain.Hurtboxes[0].Radius); }
        else Assert.Contains(fit.Diagnostics,d=>d.Code=="mesh.fit.detached");
    }
}
