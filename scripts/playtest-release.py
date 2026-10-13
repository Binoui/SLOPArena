#!/usr/bin/env python3
"""Prepare, upload and deploy a pinned Steam Playtest candidate; never activate Steam."""
from __future__ import annotations

import argparse
import base64
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import subprocess
import sys
import tarfile
import tempfile
import urllib.request
sys.path.insert(0, str(Path(__file__).resolve().parent))
import playtest_protocol

ROOT = Path(__file__).resolve().parents[1]
APP_ID = 5325920
DEPOT_ID = 5325921
SHA = re.compile(r"[a-f0-9]{40}")
VERSION = re.compile(r"[0-9]+\.[0-9]+\.[0-9]+-playtest\.[0-9]+")
PAYLOADS = ("manifest.json", "character.runtime.json", "poses.bin", "client.bindings")
MASTER_REPOSITORY = "Binoui/SlopArena-MasterServer"
GAME_REPOSITORY = "Binoui/SlopArena"
CONTROLLER_CONTRACT_VERSION = 1


class ReleaseError(RuntimeError):
    pass


def read_json(path: Path) -> dict:
    value = json.loads(path.read_text(), object_pairs_hook=_unique_pairs)
    if not isinstance(value, dict):
        raise ReleaseError("Release receipt must be a JSON object")
    return value


def write_json(path: Path, value: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2, sort_keys=True) + "\n")


def require(value, condition: bool, message: str):
    if not condition:
        raise ReleaseError(message)
    return value


def validate_version(value: str) -> str:
    return require(value, isinstance(value, str) and len(value) <= 64 and VERSION.fullmatch(value) is not None,
                   "Version must be a bounded X.Y.Z-playtest.N identifier")


def validate_revision(value: str) -> str:
    return require(value, isinstance(value, str) and SHA.fullmatch(value) is not None,
                   "Source revision must be a full lowercase commit SHA")


def gh_json(route: str) -> dict:
    result = subprocess.run(["gh", "api", route], stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, text=True)
    if result.returncode:
        raise ReleaseError("Cannot read the approved GitHub source; check the configured repository access token")
    return json.loads(result.stdout)


def require_environment_policy(name: str, environment: dict, branches: dict) -> None:
    policy = environment.get("deployment_branch_policy")
    require(policy, isinstance(policy, dict) and policy.get("protected_branches") is False
            and policy.get("custom_branch_policies") is True,
            f"Configure selected deployment branches for {name}, allowing only the main branch")
    rules = branches.get("branch_policies")
    require(rules, type(branches.get("total_count")) is int and branches["total_count"] == 1
            and isinstance(rules, list) and len(rules) == 1
            and rules[0].get("name") == "main" and rules[0].get("type") == "branch",
            f"{name} must allow exactly the main branch, not wildcard branches or tags")
    if name == "playtest-vps":
        protections = environment.get("protection_rules", [])
        require(protections, any(rule.get("type") == "required_reviewers" and rule.get("reviewers")
                                for rule in protections),
                "Configure at least one required reviewer for playtest-vps before deployment")


def check_environments(_args) -> None:
    for name in ("playtest-build", "playtest-vps"):
        route = f"repos/Binoui/SlopArena/environments/{name}"
        try:
            environment = gh_json(route)
            branches = gh_json(route + "/deployment-branch-policies?per_page=100")
        except ReleaseError as exc:
            raise ReleaseError(f"Create/configure {name} with main-only deployment branches before dispatch") from exc
        require_environment_policy(name, environment, branches)
    print("Main-only build/deploy environments and required VPS approval policy verified")


def resolve(args) -> None:
    validate_version(args.version)
    validate_revision(args.source_revision)
    if getattr(args, "expected_source_revision", ""):
        require(args, args.source_revision == validate_revision(args.expected_source_revision),
                "Game main moved after preparation; do not dispatch a different source snapshot")
    master_main = validate_revision(gh_json(f"repos/{MASTER_REPOSITORY}/git/ref/heads/main")["object"]["sha"])
    master = validate_revision(args.master_revision) if args.master_revision else master_main
    if master != master_main:
        comparison = gh_json(f"repos/{MASTER_REPOSITORY}/compare/{master}...{master_main}")
        require(master, comparison.get("status") in ("ahead", "identical"),
                "Master source must be an approved main ancestor")
    compatibility = playtest_protocol.verify_github(gh_json, args.source_revision, master)
    write_json(ROOT / "build/playtest/protocol.json", compatibility)
    release_id = f"steam-playtest-{args.run_id}"
    require(release_id, re.fullmatch(r"[1-9][0-9]{0,19}", args.run_id) is not None, "Invalid GitHub run identity")
    require(args.migration_from, not args.migration_from or re.fullmatch(r"[A-Za-z0-9_]+", args.migration_from),
            "Invalid approved migration source")
    output = {"version": args.version, "release_id": release_id, "game_revision": args.source_revision,
              "master_revision": master, "migration_from": args.migration_from}
    if os.environ.get("GITHUB_OUTPUT"):
        with open(os.environ["GITHUB_OUTPUT"], "a") as stream:
            for key, value in output.items():
                stream.write(f"{key}={value}\n")
    print(json.dumps(output, sort_keys=True))


def checksum(path: Path) -> str:
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def validate_client_inputs(version: str, source_revision: str, endpoint: str) -> None:
    validate_version(version)
    validate_revision(source_revision)
    require(endpoint, isinstance(endpoint, str) and len(endpoint) <= 256 and endpoint.startswith("https://")
            and not any(char.isspace() or ord(char) < 32 for char in endpoint),
            "Client Master endpoint must be a bounded HTTPS URL")


def client_paths(root: Path, version: str) -> tuple[Path, Path]:
    return root / "build/playtest/client.json", root / f"build/release/SlopArena-{version}"


def pack_client(args) -> None:
    validate_client_inputs(args.version, args.source_revision, args.endpoint)
    root = args.client.resolve()
    receipt, output = client_paths(root, args.version)
    verify_client(read_json(receipt), args.version, args.source_revision, args.endpoint, root)
    archive = args.output
    require(archive, not archive.exists(), "Client archive already exists; refusing to overwrite")
    archive.parent.mkdir(parents=True, exist_ok=True)
    created = False
    try:
        with archive.open("xb") as stream:
            created = True
            with tarfile.open(fileobj=stream, mode="w:gz") as tar:
                tar.add(receipt, arcname="build/playtest/client.json", recursive=False)
                tar.add(output, arcname=f"build/release/SlopArena-{args.version}")
    except Exception:
        if created:
            archive.unlink(missing_ok=True)
        raise
    print(f"Packed verified client artifact: {archive}")


def gh_release_json(tag: str) -> dict:
    result = subprocess.run(["gh", "api", f"repos/{GAME_REPOSITORY}/releases/tags/{tag}"],
                            stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, text=True)
    if result.returncode:
        raise ReleaseError("Cannot read the published client release with gh")
    return json.loads(result.stdout)


def safe_client_members(tar: tarfile.TarFile, version: str) -> list[tarfile.TarInfo]:
    receipt_name = "build/playtest/client.json"
    output_name = f"build/release/SlopArena-{version}"
    ancestors = {"build", "build/playtest", "build/release"}
    seen = set()
    members = []
    for member in tar.getmembers():
        name = member.name
        path_name = name.rstrip("/")
        path = PurePosixPath(path_name)
        require(name, name and path_name and not name.startswith("/") and "\\" not in name and
                not re.match(r"^[A-Za-z]:", name) and all(part not in ("", ".", "..") for part in path_name.split("/")),
                "Client archive contains an absolute or traversal path")
        normalized = path.as_posix()
        require(normalized, normalized not in seen, "Client archive contains duplicate members")
        seen.add(normalized)
        allowed = normalized in ancestors or normalized == receipt_name or normalized == output_name or normalized.startswith(output_name + "/")
        require(normalized, allowed, "Client archive contains an unexpected path")
        require(normalized, not (name.endswith("/") and not member.isdir()),
                "Client archive file has a directory path")
        require(normalized, member.type in (tarfile.DIRTYPE, tarfile.REGTYPE, tarfile.AREGTYPE),
                "Client archive contains a link or special member")
        require(normalized, not (member.isdir() and normalized == receipt_name) and
                not (member.isfile() and normalized in ancestors | {output_name}),
                "Client archive member has an unexpected type")
        members.append(member)
    require(members, receipt_name in seen and output_name in seen,
            "Client archive is missing its receipt or client directory")
    return members


def fetch_client(args) -> None:
    validate_client_inputs(args.version, args.source_revision, args.endpoint)
    tag = f"playtest-client-{args.version}"
    release = gh_release_json(tag)
    require(release, isinstance(release, dict) and release.get("tag_name") == tag
            and release.get("draft") is False and release.get("prerelease") is True,
            "Client release must be the published prerelease for the requested version")
    commit = gh_json(f"repos/{GAME_REPOSITORY}/commits/{tag}")
    require(commit, isinstance(commit, dict) and commit.get("sha") == args.source_revision,
            "Client release tag does not resolve to the requested source revision")
    filename = f"SlopArena-{args.version}.tar.gz"
    assets = release.get("assets")
    require(assets, isinstance(assets, list) and len(assets) == 1 and isinstance(assets[0], dict)
            and assets[0].get("name") == filename,
            "Client release must contain exactly the expected archive asset")
    digest = assets[0].get("digest")
    require(digest, isinstance(digest, str) and re.fullmatch(r"sha256:[a-f0-9]{64}", digest) is not None,
            "Client release archive has no valid SHA-256 digest")
    root = ROOT
    receipt_path, output_path = client_paths(root, args.version)
    require((receipt_path, output_path), not receipt_path.exists() and not receipt_path.is_symlink()
            and not output_path.exists() and not output_path.is_symlink(),
            "Client extraction destination already exists")
    for parent in (root / "build", receipt_path.parent, output_path.parent):
        require(parent, not parent.exists() or (parent.is_dir() and not parent.is_symlink()),
                "Client extraction parent is not a real directory")
    with tempfile.TemporaryDirectory(prefix="playtest-client-") as temporary:
        temporary_path = Path(temporary)
        downloaded = subprocess.run(
            ["gh", "release", "download", tag, "--repo", GAME_REPOSITORY, "--pattern", filename,
             "--dir", str(temporary_path)], stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, text=True)
        require(downloaded, downloaded.returncode == 0, "gh could not download the approved client archive")
        archive = temporary_path / filename
        require(archive, archive.is_file() and checksum(archive) == digest.removeprefix("sha256:"),
                "Downloaded client archive digest differs from the release asset")
        stage = temporary_path / "extracted"
        stage.mkdir()
        with tarfile.open(archive, "r:gz") as tar:
            members = safe_client_members(tar, args.version)
            receipt_member = next(member for member in members if member.name == "build/playtest/client.json")
            receipt_stream = tar.extractfile(receipt_member)
            require(receipt_stream, receipt_stream is not None, "Client receipt is not a regular file")
            receipt = json.loads(receipt_stream.read(), object_pairs_hook=lambda pairs: _unique_pairs(pairs))
            require(receipt, isinstance(receipt, dict) and receipt.get("version") == args.version
                    and receipt.get("sourceRevision") == args.source_revision
                    and receipt.get("masterEndpoint") == args.endpoint,
                    "Client receipt differs from the requested version, source, or endpoint")
            for member in members:
                destination = stage / PurePosixPath(member.name)
                if member.isdir():
                    destination.mkdir(parents=True, exist_ok=True)
                    continue
                destination.parent.mkdir(parents=True, exist_ok=True)
                with tar.extractfile(member) as source, destination.open("xb") as target:
                    shutil.copyfileobj(source, target)
        receipt_path.parent.mkdir(parents=True, exist_ok=True)
        output_path.parent.mkdir(parents=True, exist_ok=True)
        try:
            shutil.move(str(stage / "build/playtest/client.json"), receipt_path)
            shutil.move(str(stage / f"build/release/SlopArena-{args.version}"), output_path)
            verify_client(read_json(receipt_path), args.version, args.source_revision, args.endpoint, root)
        except Exception:
            receipt_path.unlink(missing_ok=True)
            shutil.rmtree(output_path, ignore_errors=True)
            raise
    print(f"Fetched and verified client artifact for {tag}")


def _unique_pairs(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ReleaseError(f"Duplicate JSON field: {key}")
        result[key] = value
    return result


def verify_client(receipt: dict, version: str, source_revision: str, endpoint: str, root: Path = ROOT) -> Path:
    public_fields = {"version", "sourceRevision", "catalogHash", "masterEndpoint", "outputRelativePath",
                     "packageIdentities", "sharedSha256", "steamApi64Sha256", "buildResult", "errors", "warnings"}
    require(receipt, isinstance(receipt, dict) and set(receipt) == public_fields,
            "Client receipt must contain exactly the approved public fields")
    require(receipt, type(receipt["warnings"]) is int and receipt["warnings"] >= 0
            and isinstance(receipt["packageIdentities"], list), "Malformed client build evidence")
    validate_version(version)
    validate_revision(source_revision)
    require(receipt, receipt.get("version") == version and receipt.get("sourceRevision") == source_revision,
            "Client version/source differs from the approved release")
    require(receipt, receipt.get("masterEndpoint") == endpoint and endpoint.startswith("https://"),
            "Client Master endpoint differs from the approved HTTPS host")
    require(receipt, receipt.get("buildResult") == "Succeeded" and type(receipt.get("errors")) is int and receipt["errors"] == 0,
            "Client build did not succeed without errors")
    require(receipt, isinstance(receipt.get("catalogHash"), str) and re.fullmatch(r"[a-f0-9]{64}", receipt["catalogHash"]) is not None,
            "Client canonical catalog identity is missing")
    relative = f"build/release/SlopArena-{version}"
    require(receipt, receipt.get("outputRelativePath") == relative, "Unexpected client output path")
    output = root / relative
    require(output, output.is_dir() and not output.is_symlink(), "Client artifact directory is missing or symlinked")
    for path in output.rglob("*"):
        require(path, not path.is_symlink(), "Client artifact contains a symlink")
        if path.is_file():
            forbidden = path.suffix.lower() in (".env", ".pem", ".key", ".pfx", ".pdb") or path.name.lower() in (
                "game.json", "server.json", "master.env", "migration.env", "postgres-password")
            forbidden |= "skeleton" in path.name.lower() or any("DoNotShip" in part or "BackUpThisFolder" in part for part in path.parts)
            require(path, not forbidden, "Client artifact contains private or developer-only files")
    require(output, (output / "SlopArena.exe").is_file(), "Windows player is missing")
    data = output / "SlopArena_Data"
    managed = data / "Managed"
    require(receipt, checksum(managed / "SlopArena.Shared.dll") == receipt.get("sharedSha256"), "Shipped Shared hash differs")
    require(receipt, checksum(data / "Plugins/x86_64/steam_api64.dll") == receipt.get("steamApi64Sha256"), "Shipped Steam native hash differs")
    require(receipt, endpoint.encode("utf-16le") in (managed / "Assembly-CSharp.dll").read_bytes(), "Compiled Master endpoint missing")
    require(receipt, version.encode() in (data / "globalgamemanagers").read_bytes(), "Packaged application version differs")
    staged = data / "StreamingAssets"
    require(staged, {p.name for p in staged.iterdir()} == {"arenas", "content-cooked"}, "Unexpected runtime staging content")
    cooked = root / "content-cooked"
    roster = read_json(cooked / "roster/manifest.json")
    paths = ["roster/manifest.json"]
    expected_identities = []
    for entry in roster["entries"]:
        package_id = entry["packageId"]
        require(package_id, re.fullmatch(r"[a-z][a-z0-9.-]*", package_id) is not None, "Unsafe roster package identity")
        package = staged / "content-cooked" / package_id
        require(package, {p.name for p in package.iterdir()} == set(PAYLOADS), "Unexpected packaged character files")
        paths.extend(f"{package_id}/{name}" for name in PAYLOADS)
        manifest = read_json(cooked / package_id / "manifest.json")
        expected_identities.append({"selector": entry["selector"], **{
            name: manifest[name] for name in ("packageId", "version", "sourceHash", "cookedContentHash", "packageHash")}})
    require(roster, {p.name for p in (staged / "content-cooked").iterdir()} == {"roster", *(e["packageId"] for e in roster["entries"])},
            "Packaged cooked root contains an unselected package or authoring input")
    require(roster, {p.name for p in (staged / "content-cooked/roster").iterdir()} == {"manifest.json"},
            "Unexpected roster payload")
    require(receipt, receipt["packageIdentities"] == sorted(expected_identities, key=lambda identity: identity["packageId"]),
            "Client receipt identities differ from the roster-selected shipped packages")
    for relative_path in paths:
        require(relative_path, (cooked / relative_path).read_bytes() == (staged / "content-cooked" / relative_path).read_bytes(),
                "Shipped cooked payload differs from the approved source")
    source_arenas = {p.name: p for p in (root / "data/arenas").glob("*.arena")}
    require(source_arenas, source_arenas and {p.name for p in (staged / "arenas").iterdir()} == set(source_arenas),
            "Packaged arena set differs")
    for name, original in source_arenas.items():
        require(name, original.read_bytes() == (staged / "arenas" / name).read_bytes(), "Packaged arena bytes differ")
    for source, destination in [(root / "CREDITS.md", output / "CREDITS.txt"), (root / "LICENSE", output / "LICENSE.txt")]:
        require(source, source.read_bytes() == destination.read_bytes(), "Release attribution missing or changed")
    for name in ("ArchivoBlack-OFL.txt", "SpaceMono-OFL.txt"):
        require(name, (root / "client/Unity/Assets/Fonts" / name).read_bytes() == (output / "LICENSES" / name).read_bytes(),
                "Font license missing or changed")
    return output


def parse_steam_receipt(app_log: str, depot_log: str, depot_vdf: str) -> dict:
    builds = re.findall(r"Successfully finished AppID 5325920 build \(BuildID ([1-9][0-9]*)\)", app_log)
    manifests = re.findall(r"Success! New manifestID ([1-9][0-9]*) created", depot_log)
    saved = re.findall(r'^\s*"manifest"\s*"([1-9][0-9]*)"\s*$', depot_vdf, re.MULTILINE)
    require(depot_vdf, re.findall(r'^\s*"appid"\s*"([0-9]+)"\s*$', depot_vdf, re.MULTILINE) == [str(APP_ID)]
            and re.findall(r'^\s*"depotid"\s*"([0-9]+)"\s*$', depot_vdf, re.MULTILINE) == [str(DEPOT_ID)],
            "Steam depot metadata belongs to a different application or depot")
    require(builds, len(builds) == 1 and len(manifests) == 1 and saved == manifests,
            "Steam did not report one matching successful app build and depot manifest")
    require(builds, int(builds[0]) <= 2**32 - 1 and int(manifests[0]) <= 2**64 - 1, "Steam identity out of range")
    return {"app_id": APP_ID, "depot_id": DEPOT_ID, "build_id": int(builds[0]), "manifest_id": manifests[0]}


def steam_logs(directory: Path) -> dict:
    return parse_steam_receipt((directory / f"app_build_{APP_ID}.log").read_text(),
                              (directory / f"depot_build_{DEPOT_ID}.log").read_text(),
                              (directory / f"depot_build_{DEPOT_ID}.vdf").read_text())


def upload(args) -> None:
    verify_client(read_json(args.client), args.version, args.source_revision, args.endpoint)
    require(None, os.environ.get("GITHUB_ACTIONS") == "true", "Upload helper is restricted to the isolated Actions checkout")
    username = os.environ.get("STEAM_BUILD_USER", "")
    encoded = os.environ.get("STEAM_CONFIG_VDF", "")
    require(username, re.fullmatch(r"[A-Za-z0-9_]+", username) is not None, "Set STEAM_BUILD_USER to the dedicated builder account")
    require(encoded, bool(encoded) and len(encoded) <= 2_000_000, "STEAM_CONFIG_VDF is missing or oversized")
    try:
        config = base64.b64decode("".join(encoded.split()), validate=True)
    except ValueError as exc:
        raise ReleaseError("STEAM_CONFIG_VDF must be base64-encoded authorized config.vdf") from exc
    require(config, bool(config), "STEAM_CONFIG_VDF decodes to an empty session")
    logs = ROOT / "build/steampipe/output"
    require(logs, not logs.exists(), "Steam output must be fresh; do not reuse another upload's logs")
    with tempfile.TemporaryDirectory(prefix="playtest-steam-", dir=os.environ.get("RUNNER_TEMP")) as temporary:
        home = Path(temporary)
        install = home / "Steam"
        install.mkdir()
        archive = home / "steamcmd.tar.gz"
        with urllib.request.urlopen("https://steamcdn-a.akamaihd.net/client/installer/steamcmd_linux.tar.gz", timeout=60) as response:
            with archive.open("wb") as stream:
                while chunk := response.read(1024 * 1024):
                    stream.write(chunk)
        with tarfile.open(archive) as stream:
            stream.extractall(install, filter="data")
        (install / "config").mkdir(exist_ok=True)
        session = install / "config/config.vdf"
        session.write_bytes(config)
        session.chmod(0o600)
        environment = os.environ.copy()
        environment.pop("STEAM_CONFIG_VDF", None)
        environment.update(HOME=str(home), STEAM_HOME=str(install), STEAMCMD=str(install / "steamcmd.sh"), STEAM_BUILD_USER=username)
        with (home / "private-upload.log").open("wb") as private_log:
            result = subprocess.run(["bash", str(ROOT / "scripts/steam-playtest.sh"), args.version, "--upload"],
                                    cwd=ROOT, env=environment, stdin=subprocess.DEVNULL, stdout=private_log,
                                    stderr=subprocess.STDOUT, timeout=1800)
        require(result, result.returncode == 0, "Steam upload failed; reauthorize the builder config.vdf locally and renew its secret")
        receipt = steam_logs(logs)
    write_json(args.output, receipt)
    print(json.dumps(receipt, sort_keys=True))


def validated_candidate(value: dict) -> dict:
    sys.path.insert(0, str(ROOT / "deploy/vps"))
    from ci_release import validate_candidate
    return validate_candidate(value)


def assemble(args) -> None:
    client = read_json(args.client)
    verify_client(client, args.version, args.source_revision, args.endpoint)
    steam = read_json(args.steam)
    candidate = {"release_id": args.release_id, "version": args.version,
                 "source_revisions": {"gameplay": args.source_revision, "master": args.master_revision},
                 "images": {"gameplay": args.game_image, "master": args.master_image, "migration": args.migration_image},
                 "catalog_hash": client["catalogHash"], "master_endpoint": client["masterEndpoint"],
                 "schema": {"target_migration": args.target_migration, "compatible_migrations": [args.target_migration],
                            "upgrade_from": [args.migration_from] if args.migration_from else []}, "steam": steam}
    write_json(args.output, validated_candidate(candidate))
    print(f"Candidate {args.release_id}: Steam BuildID {steam['build_id']}; manual default promotion required")


def verify_deployment(candidate: dict, result: dict) -> None:
    require(result, isinstance(result, dict), "VPS receipt must be an object")
    require(result, result.get("outcome") == "success", "VPS did not report successful deployment")
    for key in ("release_id", "source_revisions", "images", "catalog_hash", "master_endpoint", "steam"):
        require(result, result.get(key) == candidate[key], f"VPS {key} differs from the uploaded candidate")
    require(result, result.get("schema") in candidate["schema"]["compatible_migrations"], "VPS schema differs")
    services = result.get("services", {})
    require(services, isinstance(services, dict) and all(
        isinstance(services.get(service), dict) and services[service].get("readiness") is True
        for service in ("master", "game")), "Master/GameHost readiness is not verified")
    registration = result.get("registration", {})
    require(registration, isinstance(registration, dict), "VPS registration receipt must be an object")
    hosts = registration.get("hosts")
    require(registration, registration.get("available") is True and registration.get("registered") is True
            and isinstance(hosts, list) and hosts and all(isinstance(host, dict) for host in hosts)
            and any(host.get("fresh") is True for host in hosts),
            "VPS registration is not fresh")


def deploy(args) -> None:
    candidate = validated_candidate(read_json(args.candidate))
    key = os.environ.get("PLAYTEST_SSH_PRIVATE_KEY", "")
    known_hosts = os.environ.get("PLAYTEST_SSH_KNOWN_HOSTS", "")
    host = os.environ.get("PLAYTEST_SSH_HOST", "")
    user = os.environ.get("PLAYTEST_SSH_USER", "")
    port = os.environ.get("PLAYTEST_SSH_PORT", "22")
    require(key, bool(key) and bool(known_hosts), "Deployment key and pinned SSH known-hosts secret are required")
    require(host, re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9.-]*", host) is not None, "Invalid approved SSH host")
    require(user, re.fullmatch(r"[a-z_][a-z0-9_-]*", user) is not None, "Invalid restricted deployment account")
    require(port, port.isdecimal() and 1 <= int(port) <= 65535, "Invalid SSH port")
    with tempfile.TemporaryDirectory(prefix="playtest-ssh-", dir=os.environ.get("RUNNER_TEMP")) as temporary:
        directory = Path(temporary)
        identity = directory / "identity"
        identity.write_text(key + "\n")
        identity.chmod(0o600)
        hosts = directory / "known_hosts"
        hosts.write_text(known_hosts + "\n")
        hosts.chmod(0o600)
        command = ["ssh", "-T", "-i", str(identity), "-p", port, "-o", "BatchMode=yes", "-o", "IdentitiesOnly=yes",
                   "-o", "StrictHostKeyChecking=yes", "-o", f"UserKnownHostsFile={hosts}", "-o", "ConnectTimeout=15",
                   f"{user}@{host}", "sloparena-release"]
        environment = {k: v for k, v in os.environ.items() if k not in ("PLAYTEST_SSH_PRIVATE_KEY", "PLAYTEST_SSH_KNOWN_HOSTS")}
        try:
            result = subprocess.run(command, input=json.dumps(candidate).encode(), stdout=subprocess.PIPE,
                                    stderr=subprocess.DEVNULL, env=environment, timeout=5400)
        except (OSError, subprocess.TimeoutExpired):
            write_json(args.output, deployment_envelope(candidate, ambiguous_failure(candidate)))
            raise ReleaseError("VPS response is unavailable; ambiguous failure receipt retained; reconcile before retrying")
        if result.returncode:
            failure = ambiguous_failure(candidate)
            if len(result.stdout) <= 131072:
                try:
                    from ci_release import validate_failure
                    failure = validate_failure(json.loads(result.stdout, object_pairs_hook=_unique_pairs), candidate["release_id"])
                except (ValueError, TypeError, KeyError, RuntimeError):
                    pass
            write_json(args.output, deployment_envelope(candidate, failure))
            raise ReleaseError(f"VPS {failure['failed_stage']}: {failure['reason_code']}; recovery {failure['recovery']['status']}; receipt: {args.output}")
    try:
        require(result.stdout, len(result.stdout) <= 131072, "Oversized VPS response")
        receipt = json.loads(result.stdout, object_pairs_hook=_unique_pairs)
        verify_deployment(candidate, receipt)
    except (ValueError, TypeError, KeyError, RuntimeError):
        write_json(args.output, deployment_envelope(candidate, ambiguous_failure(candidate)))
        raise ReleaseError("VPS response was malformed or mismatched; ambiguous receipt retained")
    envelope = deployment_envelope(candidate, receipt)
    envelope["publicReadinessVerified"] = False
    write_json(args.output, envelope)
    try:
        with urllib.request.urlopen(candidate["master_endpoint"] + "/ready", timeout=20) as response:
            require(response, response.status == 200, "Public Master readiness failed")
    except (OSError, RuntimeError):
        raise ReleaseError("Backend receipt retained, but public HTTPS readiness is unverified; do not activate Steam")
    envelope["publicReadinessVerified"] = True
    write_json(args.output, envelope)
    print(f"VPS {candidate['release_id']} verified. Set Steam BuildID {candidate['steam']['build_id']} live manually.")


def deployment_envelope(candidate: dict, result: dict) -> dict:
    return {"candidate": candidate, "deployment": result, "steamDefaultActivated": False,
            "manualPromotionUrl": f"https://partner.steamgames.com/apps/builds/{APP_ID}"}


def ambiguous_failure(candidate: dict) -> dict:
    from datetime import datetime, timezone
    return {"release_id": candidate["release_id"], "outcome": "unknown", "failed_stage": "transport",
            "reason_code": "response_unavailable", "active_release": None,
            "recovery": {"status": "unknown", "release_id": None},
            "failure_during_migration": False, "schema_after": None,
            "observed_at": datetime.now(timezone.utc).isoformat(timespec="seconds")}


def parser() -> argparse.ArgumentParser:
    result = argparse.ArgumentParser(description=__doc__)
    commands = result.add_subparsers(dest="command", required=True)
    commands.add_parser("check-environments").set_defaults(function=check_environments)
    source = commands.add_parser("resolve")
    source.add_argument("--version", required=True)
    source.add_argument("--source-revision", required=True)
    source.add_argument("--master-revision", default="")
    source.add_argument("--migration-from", default="")
    source.add_argument("--run-id", required=True)
    source.add_argument("--expected-source-revision", default="")
    source.set_defaults(function=resolve)
    for name in ("verify-client", "upload", "assemble"):
        command = commands.add_parser(name)
        command.add_argument("--client", required=True, type=Path)
        command.add_argument("--version", required=True)
        command.add_argument("--source-revision", required=True)
        command.add_argument("--endpoint", required=True)
        if name != "verify-client":
            command.add_argument("--output", required=True, type=Path)
        if name == "assemble":
            for argument in ("release-id", "master-revision", "game-image", "master-image", "migration-image", "target-migration"):
                command.add_argument("--" + argument, required=True)
            command.add_argument("--steam", required=True, type=Path)
            command.add_argument("--migration-from", default="")
        command.set_defaults(function={"verify-client": lambda a: print(verify_client(read_json(a.client), a.version, a.source_revision, a.endpoint)),
                                       "upload": upload, "assemble": assemble}[name])
    pack = commands.add_parser("pack-client")
    pack.add_argument("--client", required=True, type=Path)
    pack.add_argument("--version", required=True)
    pack.add_argument("--source-revision", required=True)
    pack.add_argument("--endpoint", required=True)
    pack.add_argument("--output", required=True, type=Path)
    pack.set_defaults(function=pack_client)
    fetch = commands.add_parser("fetch-client")
    fetch.add_argument("--version", required=True)
    fetch.add_argument("--source-revision", required=True)
    fetch.add_argument("--endpoint", required=True)
    fetch.set_defaults(function=fetch_client)
    deployed = commands.add_parser("deploy")
    deployed.add_argument("--candidate", required=True, type=Path)
    deployed.add_argument("--output", required=True, type=Path)
    deployed.set_defaults(function=deploy)
    return result


def main() -> int:
    try:
        args = parser().parse_args()
        args.function(args)
        return 0
    except (RuntimeError, ValueError, TypeError, KeyError, OSError, EOFError,
            tarfile.TarError, subprocess.TimeoutExpired) as exc:
        if isinstance(exc, ReleaseError):
            print(f"Release refused: {exc}", file=sys.stderr)
        else:
            print("Release failed: missing/malformed artifact or failed external operation; no deployment success recorded", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
