#!/usr/bin/env python3
"""Build an isolated Win64 proof player and compare portable bytes with .NET 8."""
import argparse
import json
import os
from pathlib import Path
import subprocess


def run(args, cwd, log, env=None):
    with log.open("w") as output:
        subprocess.run(args, cwd=cwd, env=env, stdout=output, stderr=subprocess.STDOUT, check=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--version", required=True, help="Fresh X.Y.Z-playtest.N build version")
    parser.add_argument("--local-unity-project", required=True, type=Path, help="Read-only licensed input project")
    parser.add_argument("--endpoint", default="https://master-test.sloparena.barakaslurp.fr")
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[2]
    if subprocess.check_output(["git", "status", "--porcelain"], cwd=root).strip():
        raise RuntimeError("Commit the feature first; proof clones only committed bytes.")
    feature_revision = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=root, text=True).strip()
    evidence = root / "build" / "portable-proof" / args.version
    evidence.mkdir(parents=True, exist_ok=False)
    source = evidence / "source"
    item = evidence / "item"
    env = {**os.environ, "DISPLAY": os.environ.get("DISPLAY", ":0"), "SLOP_PORTABLE_PROOF": str(item)}
    run(["dotnet", "test", "tests/Shared.Tests/Shared.Tests.csproj", "-p:SkipUnityPluginCopy=true",
         "--filter", "FullyQualifiedName~TumbleweedAssemblesAndStopsAtPoseBakeGate"], root, evidence / "fixture.log", env)
    run(["dotnet", "build", "tools/PortableCookProof", "-p:SkipUnityPluginCopy=true"], root, evidence / "runner-build.log")
    runner = root / "tools/PortableCookProof/bin/Debug/netstandard2.1/PortableCookProof.dll"
    runner.with_suffix(".runtimeconfig.json").write_text(json.dumps({"runtimeOptions": {"tfm": "net8.0", "framework": {"name": "Microsoft.NETCore.App", "version": "8.0.0"}}}))
    run(["dotnet", str(runner), str(item)], root, evidence / "dotnet.log")
    run(["git", "clone", "--no-hardlinks", str(root), str(source)], root, evidence / "clone.log")
    run(["git", "checkout", "--detach", feature_revision], source, evidence / "checkout.log")
    run(["git", "switch", "-c", "build-only/portable-proof-" + args.version], source, evidence / "branch.log")
    # This is the same sole builder bypass as commit 9e5fb420. Cook checks remain enabled.
    patch = (root / "tools/PortableCookProof/origin-pin.patch").read_bytes()
    subprocess.run(["git", "apply", "-"], cwd=source, input=patch, check=True)
    program = (source / "tools/PortableCookProof/Program.cs").read_text().replace("internal static class Program", "internal static class PortableCookProof")
    hook = '''    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RunProof()
    {
        var args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "--portable-cook-proof");
        if (index < 0) return;
        try { Main(new[] { args[index + 1] }); File.WriteAllText(args[index + 2], "PASS"); UnityEngine.Application.Quit(0); }
        catch (Exception error) { File.WriteAllText(args[index + 2], error.ToString()); UnityEngine.Application.Quit(1); }
    }
'''
    program = program.replace("    public static int Main", hook + "    public static int Main")
    scripts = source / "client/Unity/Assets/Scripts"
    (scripts / "PortableCookProof.cs").write_text(program)
    (scripts / "PortableCookProof.cs.meta").write_text("fileFormatVersion: 2\nguid: e9a29d9780e54e20b22d75ab57f98b12\n")
    link = scripts / "PortableProof"
    link.mkdir()
    (link / "link.xml").write_text('<linker><assembly fullname="SlopArena.Shared"><type fullname="SlopArena.Shared.FighterItemAdmission" preserve="all"/></assembly></linker>')
    subprocess.run(["git", "add", "scripts/build-playtest-local.py", "client/Unity/Assets/Scripts/PortableCookProof.cs",
                    "client/Unity/Assets/Scripts/PortableCookProof.cs.meta", "client/Unity/Assets/Scripts/PortableProof/link.xml"], cwd=source, check=True)
    run(["git", "commit", "-m", "build-only: portable player proof with origin pin skip"], source, evidence / "commit.log")
    revision = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=source, text=True).strip()
    run(["python3", "scripts/build-playtest-local.py", "--version", args.version, "--source-revision", revision,
         "--endpoint", args.endpoint, "--local-unity-project", str(args.local_unity_project.resolve())], source, evidence / "build.log", env)
    receipt = json.loads((evidence / "build.log").read_text())
    player = Path(receipt["releaseOutput"]) / "SLOPArena.exe"
    def windows(path):
        return "Z:" + str(path.resolve()).replace("/", "\\")
    result = evidence / "mono-result.txt"
    run(["wine", str(player), "-batchmode", "-nographics", "-logFile", windows(evidence / "mono-player.log"),
         "--portable-cook-proof", windows(item), windows(result)], source, evidence / "wine.log", {**env, "WINEDEBUG": "-all"})
    if result.read_text() != "PASS":
        raise RuntimeError(result.read_text())
    summary = {"result": "PASS", "buildRevision": revision, "featureRevision": feature_revision,
               "hashes": (evidence / "dotnet.log").read_text().strip(), "build": receipt, "midpointBits": "3f800001", "negativeZeroBits": "00000000"}
    (evidence / "proof.json").write_text(json.dumps(summary, indent=2) + "\n")
    print(evidence / "proof.json")


if __name__ == "__main__":
    main()
