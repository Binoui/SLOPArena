#!/usr/bin/env python3
"""Prepare, run and resume a pinned Playtest release without conversational state."""
from __future__ import annotations

import argparse
import contextlib
from datetime import datetime, timezone
import fcntl
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import signal
import subprocess
import sys
import tarfile
import tempfile
import time
import uuid
import urllib.request

sys.path.insert(0, str(Path(__file__).resolve().parent))
import playtest_protocol

ROOT = Path(__file__).resolve().parents[1]
REPOSITORY = playtest_protocol.GAME_REPOSITORY
ENDPOINT = "https://master-test.sloparena.barakaslurp.fr"
STEPS = ("build", "publish", "workflow", "upload", "deployment", "steam")


class ControllerError(RuntimeError):
    def __init__(self, message, http_status=None):
        self.http_status = http_status
        super().__init__(message)


def module(name, filename):
    spec = importlib.util.spec_from_file_location(name, Path(__file__).with_name(filename))
    value = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(value)
    return value


producer = module("playtest_local_producer", "build-playtest-local.py")
coordinator = module("playtest_coordinator", "playtest-release.py")


def now():
    return datetime.now(timezone.utc).isoformat(timespec="seconds")


def digest(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(",", ":")).encode()).hexdigest()


def command(argv, cwd=None, *, input=None, missing=False):
    result = subprocess.run(argv, cwd=cwd, input=input, stdin=subprocess.DEVNULL if input is None else None,
                            stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True,
                            env={**os.environ, "GIT_TERMINAL_PROMPT": "0"})
    if result.returncode:
        if missing and "(HTTP 404)" in result.stderr:
            return None
        status = re.search(r"HTTP[ :]+([0-9]{3})", result.stderr)
        raise ControllerError(f"{argv[0]} operation failed; no success was recorded",
                              int(status.group(1)) if status else None)
    return result.stdout


def api(route, payload=None, *, missing=False):
    argv = ["gh", "api", route]
    if payload is not None:
        argv += ["--method", "POST", "--input", "-"]
    output = command(argv, input=json.dumps(payload) if payload is not None else None, missing=missing)
    return None if output is None else json.loads(output) if output.strip() else {}


def git(root, *arguments):
    return command(["git", *arguments], root).strip()


def atomic_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True, mode=0o700)
    if path.is_symlink():
        raise ControllerError("Release record may not be a symlink")
    temporary = path.with_name("." + path.name + "." + uuid.uuid4().hex)
    try:
        with os.fdopen(os.open(temporary, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600), "w") as stream:
            json.dump(value, stream, indent=2, sort_keys=True)
            stream.write("\n")
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, path)
        fd = os.open(path.parent, os.O_RDONLY | os.O_DIRECTORY)
        try:
            os.fsync(fd)
        finally:
            os.close(fd)
    finally:
        temporary.unlink(missing_ok=True)


def record_path(root, version):
    coordinator.validate_version(version)
    path = root / "build/playtest" / version
    producer.make_dirs_beneath(root, path)
    return path / "candidate.json"


@contextlib.contextmanager
def locked(path):
    fd = os.open(path.parent / ".controller.lock", os.O_CREAT | os.O_RDWR | os.O_NOFOLLOW, 0o600)
    try:
        try:
            fcntl.flock(fd, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError as exc:
            raise ControllerError("This release already has a running controller; use status") from exc
        yield
    finally:
        os.close(fd)


def read_state(path):
    if path.is_symlink():
        raise ControllerError("Release record may not be a symlink")
    value = coordinator.read_json(path)
    if value.get("schemaVersion") != 1:
        raise ControllerError("This is a historical release record; retain it and prepare a new version")
    if value.get("configurationHash") != digest(value.get("configuration")):
        raise ControllerError("Prepared source/target configuration changed; prepare a new version")
    if value["configuration"]["version"] != path.parent.name:
        raise ControllerError("Release directory and prepared version differ")
    return value


def save(path, state):
    state["updatedAtUtc"] = now()
    atomic_json(path, state)


def mark(path, state, step, status, **evidence):
    state["steps"][step].update(status=status, observedAtUtc=now(), **evidence)
    save(path, state)


def main_revision(root):
    lines = git(root, "ls-remote", "--exit-code", "origin", "refs/heads/main").splitlines()
    if len(lines) != 1 or lines[0].split()[-1] != "refs/heads/main":
        raise ControllerError("Cannot identify published main")
    return coordinator.validate_revision(lines[0].split()[0])


def require_repository(root, expected):
    remote = git(root, "remote", "get-url", "origin").rstrip("/")
    match = re.fullmatch(r"(?:https://github\.com/|git@github\.com:|ssh://git@github\.com/)([^/]+/[^/]+?)(?:\.git)?", remote)
    if not match or match.group(1).lower() != expected.lower():
        raise ControllerError("Selected checkout origin differs from the approved release repository")


def ensure_revision(root, revision):
    try:
        git(root, "cat-file", "-e", revision + "^{commit}")
    except ControllerError:
        # Fetch objects only, without changing branches, origin/main or FETCH_HEAD.
        git(root, "fetch", "--no-tags", "--no-write-fetch-head", "origin", revision)
        git(root, "cat-file", "-e", revision + "^{commit}")


def source_pair(game_root, game_revision, master_root, master_revision):
    coordinator.validate_revision(game_revision)
    coordinator.validate_revision(master_revision)
    game = {path: git(game_root, "show", f"{game_revision}:{path}") for path in playtest_protocol.GAME_FILES}
    master = git(master_root, "show", f"{master_revision}:{playtest_protocol.MASTER_FILE}")
    return playtest_protocol.verify_sources(game, master, game_revision, master_revision)


def prepare(args, root, path):
    master_root = args.master_root.expanduser().resolve(strict=True)
    if Path(git(master_root, "rev-parse", "--show-toplevel")).resolve() != master_root:
        raise ControllerError("Master root must identify its selected checkout")
    require_repository(root, REPOSITORY)
    require_repository(master_root, playtest_protocol.MASTER_REPOSITORY)
    published_game = main_revision(root)
    game_revision = coordinator.validate_revision(args.game_revision or published_game)
    if published_game != game_revision:
        raise ControllerError("Selected game source must equal published main")
    published_master = main_revision(master_root)
    master_revision = coordinator.validate_revision(args.master_revision or published_master)
    ensure_revision(root, game_revision)
    ensure_revision(master_root, published_master)
    ensure_revision(master_root, master_revision)
    git(master_root, "merge-base", "--is-ancestor", master_revision, published_master)
    compatibility = source_pair(root, game_revision, master_root, master_revision)
    tooling = git(root, "show", f"{game_revision}:scripts/playtest-release.py")
    if not re.search(r"^CONTROLLER_CONTRACT_VERSION = 1$", tooling, re.MULTILINE):
        raise ControllerError("Selected source lacks the controller-compatible pipeline; publish the tooling first")
    local_project = (args.local_unity_project or root / "client/Unity").expanduser().resolve(strict=True)
    editor = producer.check_editor(args.unity_editor)
    version_text = git(root, "show", f"{game_revision}:client/Unity/ProjectSettings/ProjectVersion.txt")
    match = re.search(r"m_EditorVersion:\s*(\S+)", version_text)
    if not match or editor.parent.parent.name != match.group(1):
        raise ControllerError("Installed Unity must match the version in the pinned source")
    _, inputs = producer.licensed_inputs(local_project)
    configuration = {"version": args.version, "gameRevision": game_revision, "masterRevision": master_revision,
                     "masterRoot": str(master_root), "endpoint": producer.endpoint_arg(args.endpoint),
                     "localUnityProject": str(local_project), "unityEditor": str(editor),
                     "unityVersion": match.group(1), "licensedInputsHash": digest(inputs),
                     "migrationFrom": args.migration_from}
    if args.migration_from and not re.fullmatch(r"[A-Za-z0-9_]+", args.migration_from):
        raise ControllerError("Migration source must be an explicit migration ID")
    if path.exists():
        state = read_state(path)
        if state["configuration"] != configuration:
            raise ControllerError("This version already pins another snapshot; choose a new version")
        return state
    if any(path.parent.glob("SlopArena-*.tar.gz")):
        raise ControllerError("This version already contains an immutable client; choose a new version")
    atomic_json(path.parent / "environment/licensed-inputs.json", inputs)
    roster = json.loads(git(root, "show", f"{game_revision}:content-cooked/roster/manifest.json"))
    if not isinstance(roster.get("entries"), list) or not roster["entries"]:
        raise ControllerError("Prepared source has no admitted roster entries")
    for entry in roster["entries"]:
        package = entry["packageId"]
        if not re.fullmatch(r"[a-z][a-z0-9.-]*", package):
            raise ControllerError("Unsafe roster package identity")
        baseline = local_project / "Library/SlopArena/CharacterCook" / package / "cook-status.json"
        if baseline.is_file() and not baseline.is_symlink():
            atomic_json(path.parent / "environment/accepted-cook-status" / (package + ".json"),
                        coordinator.read_json(baseline))
    state = {"schemaVersion": 1, "configuration": configuration, "configurationHash": digest(configuration),
             "compatibility": compatibility, "createdAtUtc": now(), "updatedAtUtc": now(),
             "steps": {name: {"status": "pending"} for name in STEPS},
             "playerAcceptance": {"status": "not-exercised", "reports": []}}
    save(path, state)
    return state


def ensure_checkout(root, path, state, *, cook=False):
    config = state["configuration"]
    base = root / "build" / (".playtest-controller-" + config["version"] + ("-cook-" + uuid.uuid4().hex[:8] if cook else ""))
    producer.make_dirs_beneath(root, base)
    marker = base / ".owner"
    if marker.is_symlink():
        raise ControllerError("Release ownership marker may not be a symlink")
    identity = state["configurationHash"]
    if marker.exists() and marker.read_text().strip() != identity:
        raise ControllerError("Release checkout belongs to another prepared snapshot")
    marker.write_text(identity + "\n")
    checkout = base / "source"
    if not checkout.exists():
        git(root, "worktree", "add", "--detach", str(checkout), config["gameRevision"])
    if (checkout.is_symlink() or git(checkout, "rev-parse", "HEAD") != config["gameRevision"]
            or git(checkout, "status", "--porcelain=v1", "--untracked-files=all")):
        raise ControllerError("Task-owned build checkout no longer matches clean prepared source")
    return checkout


def process_identity(pid):
    try:
        text = Path(f"/proc/{pid}/stat").read_text()
        # The command name may contain spaces; fields after its final ')' start at field 3.
        fields = text.rsplit(")", 1)[1].split()
        return None if fields[0] == "Z" else fields[19]
    except (OSError, IndexError):
        return None


def retain_attempts(source, destination_parent):
    for attempt in source.glob("20*") if source.exists() else ():
        destination = destination_parent / attempt.name
        if destination.exists():
            continue
        with tempfile.TemporaryDirectory(prefix=".retain-", dir=destination_parent) as directory:
            completed = Path(directory) / attempt.name
            shutil.copytree(attempt, completed)
            os.replace(completed, destination)


def run_builder(path, state, checkout, *, cook=False):
    config = state["configuration"]
    if not state["steps"]["build"].get("sourceChecksPassed"):
        check_log = path.parent / ("source-checks-" + uuid.uuid4().hex[:8] + ".log")
        with os.fdopen(os.open(check_log, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600), "w") as log:
            result = subprocess.run([sys.executable, "tools/check_docs.py"], cwd=checkout,
                                    stdin=subprocess.DEVNULL, stdout=log, stderr=subprocess.STDOUT)
        if result.returncode:
            mark(path, state, "build", "failed", reason="source_completeness_failed", log=str(check_log))
            raise ControllerError(f"Pinned clean-checkout documentation/source check failed; see {check_log}")
        mark(path, state, "build", state["steps"]["build"]["status"], sourceChecksPassed=True)
    _, inputs = producer.licensed_inputs(Path(config["localUnityProject"]))
    if digest(inputs) != config["licensedInputsHash"]:
        raise ControllerError("Licensed build inputs changed; prepare a new version")
    argv = [sys.executable, str(checkout / "scripts/build-playtest-local.py"),
            "--version", config["version"], "--source-revision", config["gameRevision"],
            "--endpoint", config["endpoint"], "--local-unity-project", config["localUnityProject"],
            "--unity-editor", config["unityEditor"],
            "--cook-baseline", str(path.parent / "environment/accepted-cook-status")]
    if cook:
        argv.append("--diagnose-cook")
    log_path = path.parent / (("cook-" if cook else "build-") + uuid.uuid4().hex[:8] + ".log")
    with os.fdopen(os.open(log_path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600), "w") as log:
        process = subprocess.Popen(argv, cwd=checkout, stdin=subprocess.DEVNULL,
                                   stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
        operation = {"pid": process.pid, "processIdentity": process_identity(process.pid),
                     "checkout": str(checkout), "log": str(log_path)}
        if cook:
            state["cookOperation"] = operation
            save(path, state)
        else:
            mark(path, state, "build", "running", **operation)
        try:
            code = process.wait()
        except BaseException:
            with contextlib.suppress(ProcessLookupError):
                os.killpg(process.pid, signal.SIGTERM)
            try:
                process.wait(timeout=30)
            except subprocess.TimeoutExpired:
                os.killpg(process.pid, signal.SIGKILL)
                process.wait()
            if not cook:
                mark(path, state, "build", "interrupted")
            raise
    # Always retain settled attempt logs, including failed or interrupted builds.
    source = checkout / "build/playtest" / config["version"]
    retain_attempts(source, path.parent)
    if code:
        if not cook:
            mark(path, state, "build", "failed", reason="local_build_failed")
        raise ControllerError(f"Local {'cook' if cook else 'build'} failed; inspect {log_path}")
    return source


def verify_archive(root, archive, state):
    """Recheck the exact bytes against a checkout of the prepared commit."""
    config = state["configuration"]
    checkout = ensure_checkout(root, archive.parent / "candidate.json", state)
    with tempfile.TemporaryDirectory(prefix="playtest-verify-") as directory:
        temporary = Path(directory)
        for name in ("content-cooked", "data/arenas", "client/Unity/Assets/Fonts"):
            shutil.copytree(checkout / name, temporary / name)
        for name in ("CREDITS.md", "LICENSE"):
            shutil.copy2(checkout / name, temporary / name)
        with tarfile.open(archive, "r:gz") as tar:
            members = coordinator.safe_client_members(tar, config["version"])
            tar.extractall(temporary, members=members, filter="data")
        receipt = coordinator.read_json(temporary / "build/playtest/client.json")
        coordinator.verify_client(receipt, config["version"], config["gameRevision"], config["endpoint"], temporary)
    return receipt


def build(root, path, state):
    config = state["configuration"]
    step = state["steps"]["build"]
    archive = path.parent / ("SlopArena-" + config["version"] + ".tar.gz")
    if step["status"] == "completed":
        if not archive.is_file() or coordinator.checksum(archive) != step["sha256"]:
            raise ControllerError("Retained client archive differs from the verified build")
        return
    pid = step.get("pid")
    identity = process_identity(pid) if pid else None
    if identity is not None and (step.get("processIdentity") is None or identity == step["processIdentity"]):
        raise ControllerError("The recorded local builder is still running; inspect status instead of starting another")
    checkout = ensure_checkout(root, path, state)
    source = checkout / "build/playtest" / config["version"]
    produced = source / archive.name
    if not archive.exists() and not produced.exists():
        source = run_builder(path, state, checkout)
    # A completed archive is recoverable even if the previous process stopped before saving state.
    attempt_inputs = list(source.glob("20*/local-dependencies.json"))
    if not attempt_inputs or digest(coordinator.read_json(max(attempt_inputs))) != config["licensedInputsHash"]:
        raise ControllerError("Actual staged licensed inputs differ from the prepared fingerprint")
    retain_attempts(source, path.parent)
    if not archive.exists():
        shutil.copy2(produced, archive)
    receipt = verify_archive(root, archive, state)
    atomic_json(path.parent / "client.json", receipt)
    mark(path, state, "build", "completed", archive=str(archive), sha256=coordinator.checksum(archive),
         clientReceipt=str(path.parent / "client.json"), catalogHash=receipt["catalogHash"])


def publish(root, path, state):
    config = state["configuration"]
    archive = Path(state["steps"]["build"]["archive"])
    if coordinator.checksum(archive) != state["steps"]["build"]["sha256"]:
        raise ControllerError("Verified archive changed before publication")
    tag = "playtest-client-" + config["version"]
    route = f"repos/{REPOSITORY}/releases/tags/{tag}"
    existing = api(route, missing=True)
    if existing is None:
        mark(path, state, "publish", "running")
        command(["gh", "release", "create", tag, str(archive), "--repo", REPOSITORY,
                 "--target", config["gameRevision"], "--prerelease", "--latest=false",
                 "--title", "Local Playtest client " + config["version"],
                 "--notes", "Verified Windows client from game commit " + config["gameRevision"] +
                 ". Steam activation follows compatible VPS deployment."], root)
        existing = api(route)
    actual_sha = api(f"repos/{REPOSITORY}/commits/{tag}")["sha"]
    assets = [asset for asset in existing.get("assets", []) if asset["name"] == archive.name]
    matching_tag = (not existing.get("draft") and existing.get("prerelease") is True
                    and existing.get("tag_name") == tag and actual_sha == config["gameRevision"])
    if matching_tag and not assets:
        # gh release create can finish the tag/release before its asset upload fails.
        command(["gh", "release", "upload", tag, str(archive), "--repo", REPOSITORY], root)
        existing = api(route)
        assets = [asset for asset in existing.get("assets", []) if asset["name"] == archive.name]
    if (existing.get("draft") or existing.get("prerelease") is not True or existing.get("tag_name") != tag or actual_sha != config["gameRevision"]
            or len(assets) != 1 or assets[0].get("digest") != "sha256:" + state["steps"]["build"]["sha256"]):
        raise ControllerError("Published tag or asset differs from the exact prepared client")
    mark(path, state, "publish", "completed", tag=tag, url=existing["html_url"])


def discover_run(state):
    config = state["configuration"]
    step = state["steps"]["workflow"]
    title = f"Playtest {config['version']} ({step['requestId']})"
    response = api(f"repos/{REPOSITORY}/actions/workflows/release-playtest.yml/runs?event=workflow_dispatch&per_page=100")
    matches = [run for run in response["workflow_runs"] if run.get("display_title") == title]
    if len(matches) > 1:
        raise ControllerError("More than one run matches this request; reconcile instead of choosing the newest")
    if not matches:
        return None
    if matches[0]["head_sha"] != config["gameRevision"]:
        raise ControllerError("Correlated workflow ran from a different game commit; prepare a new version")
    return matches[0]


def dispatch(root, path, state):
    step = state["steps"]["workflow"]
    if step.get("runId"):
        return
    config = state["configuration"]
    if not step.get("requestId"):
        mark(path, state, "workflow", "pending", requestId=uuid.uuid4().hex)
    if step.get("dispatchStartedAtUtc"):
        run = discover_run(state)
        if run:
            mark(path, state, "workflow", "running", runId=run["id"], url=run["html_url"])
        else:
            mark(path, state, "workflow", "dispatch-unconfirmed")
        return
    if main_revision(root) != config["gameRevision"]:
        raise ControllerError("Game main moved after preparation; use a new version/snapshot")
    mark(path, state, "workflow", "dispatching", dispatchStartedAtUtc=now())
    # Intent is durable before the non-idempotent API call. A lost reply is reconciled by requestId.
    try:
        api(f"repos/{REPOSITORY}/actions/workflows/release-playtest.yml/dispatches", {
            "ref": "main", "inputs": {"version": config["version"], "master_revision": config["masterRevision"],
                                     "expected_game_revision": config["gameRevision"],
                                     "migration_from": config["migrationFrom"], "request_id": step["requestId"]}})
    except ControllerError as exc:
        if exc.http_status in (401, 403, 404, 422):
            step.pop("dispatchStartedAtUtc", None)
            mark(path, state, "workflow", "dispatch-rejected", rejectedHttpStatus=exc.http_status)
        raise
    run = discover_run(state)
    if run:
        mark(path, state, "workflow", "running", runId=run["id"], url=run["html_url"])
    else:
        mark(path, state, "workflow", "dispatch-unconfirmed")


def receipts(path, run):
    base = path.parent / "ci" / str(run["id"]) / ("attempt-" + str(run.get("run_attempt", 1)))
    artifacts = api(f"repos/{REPOSITORY}/actions/runs/{run['id']}/artifacts?per_page=100")["artifacts"]
    for name, leaf in (("playtest-upload-receipts", "upload"), ("playtest-release-receipt", "deployment")):
        matches = [artifact for artifact in artifacts if artifact["name"] == name and not artifact.get("expired")]
        if not matches:
            continue
        if len(matches) != 1:
            raise ControllerError("Ambiguous CI receipt artifact")
        destination = base / leaf / str(matches[0]["id"])
        if not destination.exists():
            destination.parent.mkdir(parents=True, exist_ok=True)
            with tempfile.TemporaryDirectory(prefix="receipt-", dir=destination.parent) as directory:
                command(["gh", "run", "download", str(run["id"]), "--repo", REPOSITORY,
                         "--name", name, "--dir", directory])
                os.replace(directory, destination)
        yield leaf, destination


def verify_candidate(candidate, state, run_id):
    candidate = coordinator.validated_candidate(candidate)
    config = state["configuration"]
    expected_sources = {"gameplay": config["gameRevision"], "master": config["masterRevision"]}
    if (candidate["release_id"] != f"steam-playtest-{run_id}" or candidate["version"] != config["version"]
            or candidate["source_revisions"] != expected_sources or candidate["master_endpoint"] != config["endpoint"]
            or candidate["catalog_hash"] != state["steps"]["build"]["catalogHash"]
            or candidate["schema"]["upgrade_from"] != ([config["migrationFrom"]] if config["migrationFrom"] else [])):
        raise ControllerError("CI candidate differs from prepared source, content, endpoint or schema selection")
    previous = state["steps"]["upload"].get("candidate")
    if previous is not None and previous != candidate:
        raise ControllerError("Existing release identity changed its candidate; do not overwrite it")
    return candidate


def reconcile(path, state, *, approve=False):
    step = state["steps"]["workflow"]
    if not step.get("runId"):
        run = discover_run(state) if step.get("dispatchStartedAtUtc") else None
        if run is None:
            return
        mark(path, state, "workflow", "running", runId=run["id"], url=run["html_url"])
    run = api(f"repos/{REPOSITORY}/actions/runs/{step['runId']}")
    if run["head_sha"] != state["configuration"]["gameRevision"]:
        raise ControllerError("Workflow source no longer matches the prepared game")
    if step.get("retryIntentAttempt") == run.get("run_attempt", 1):
        age = (datetime.now(timezone.utc) - datetime.fromisoformat(step["retryRequestedAtUtc"])).total_seconds()
        mark(path, state, "workflow", "retry-requested" if age < 60 else "retry-unconfirmed")
        return
    mark(path, state, "workflow", run["status"], conclusion=run.get("conclusion"),
         runAttempt=run.get("run_attempt", 1), url=run["html_url"])
    for leaf, directory in receipts(path, run):
        candidate_path = directory / "candidate-public.json"
        if not candidate_path.is_file():
            continue
        candidate = verify_candidate(coordinator.read_json(candidate_path), state, run["id"])
        protocol_path = directory / "protocol.json"
        if not protocol_path.is_file() or coordinator.read_json(protocol_path) != state["compatibility"]:
            raise ControllerError("CI protocol proof does not match the prepared source pair")
        mark(path, state, "upload", "completed", candidate=candidate, receiptDirectory=str(directory))
        result_path = directory / "release.json"
        if not result_path.exists() or run["status"] != "completed":
            continue
        envelope = coordinator.read_json(result_path)
        if envelope.get("candidate") != candidate:
            raise ControllerError("Deployment receipt belongs to another candidate")
        result = envelope.get("deployment", {})
        if result.get("outcome") == "success":
            coordinator.verify_deployment(candidate, result)
            observed_ready = state["steps"]["deployment"].get("publicReadinessObservation", {})
            mark(path, state, "deployment", "completed" if envelope.get("publicReadinessVerified") is True
                 or observed_ready.get("candidateHash") == digest(candidate) and observed_ready.get("status") == 200
                 else "public-readiness-unverified", receipt=str(result_path), result=result)
        elif result.get("outcome") == "failure":
            from ci_release import validate_failure
            validate_failure(result, candidate["release_id"])
            mark(path, state, "deployment", "failed", receipt=str(result_path), result=result)
        else:
            mark(path, state, "deployment", "ambiguous", receipt=str(result_path),
                 reason="Deployment response unavailable; reconcile private host events before retrying")
    if run["status"] == "completed" and state["steps"]["deployment"]["status"] not in (
            "completed", "failed", "ambiguous", "public-readiness-unverified"):
        mark(path, state, "deployment", "ambiguous" if state["steps"]["upload"]["status"] == "completed" else "not-started",
             reason="Workflow ended without a verified deployment receipt")
    if approve and run["status"] != "completed" and state["steps"]["upload"]["status"] == "completed":
        pending = api(f"repos/{REPOSITORY}/actions/runs/{run['id']}/pending_deployments")
        for deployment in pending:
            if deployment["environment"]["name"] != "playtest-vps":
                continue
            if not deployment.get("current_user_can_approve"):
                mark(path, state, "deployment", "waiting-for-reviewer")
                return
            api(f"repos/{REPOSITORY}/actions/runs/{run['id']}/pending_deployments", {
                "environment_ids": [deployment["environment"]["id"]], "state": "approved",
                "comment": "Approved pinned candidate " + state["configuration"]["version"]})
            mark(path, state, "deployment", "running", approvedAtUtc=now())


def retry_deployment(path, state):
    workflow = state["steps"]["workflow"]
    result = state["steps"]["deployment"].get("result", {})
    recovery = result.get("recovery", {})
    safe_before_replacement = {"receiver_preflight", "offhost_backup", "backup_freshness", "idle_registration"}
    known_safe = (result.get("outcome") == "failure" and result.get("active_release")
                  and result["active_release"] != state["steps"]["upload"]["candidate"]["release_id"]
                  and result.get("failure_during_migration") is False
                  and (recovery.get("status") == "restored"
                       or recovery.get("status") == "not_attempted" and result.get("failed_stage") in safe_before_replacement))
    if not known_safe or workflow.get("conclusion") != "failure":
        raise ControllerError("Deploy retry needs a failed exact run and proven unchanged/restored host state")
    if workflow.get("retryIntentAttempt") == workflow["runAttempt"]:
        raise ControllerError("A retry request is already recorded; reconcile its outcome before another retry")
    jobs = api(f"repos/{REPOSITORY}/actions/runs/{workflow['runId']}/jobs?filter=latest&per_page=100")["jobs"]
    failed = [job for job in jobs if job["name"] == "deploy" and job.get("conclusion") == "failure"]
    if len(failed) != 1:
        raise ControllerError("Cannot identify one failed deploy job; do not rerun the entire release")
    mark(path, state, "workflow", "retry-requested", retryIntentAttempt=workflow["runAttempt"],
         retryJobId=failed[0]["id"], retryRequestedAtUtc=now())
    mark(path, state, "deployment", "retry-requested")
    try:
        api(f"repos/{REPOSITORY}/actions/jobs/{failed[0]['id']}/rerun", {})
    except ControllerError as exc:
        if exc.http_status in (401, 403, 404, 422):
            workflow.pop("retryIntentAttempt", None)
            mark(path, state, "workflow", "completed", rejectedHttpStatus=exc.http_status)
            mark(path, state, "deployment", "failed")
        raise


def recheck_public_readiness(path, state):
    if state["steps"]["deployment"]["status"] != "public-readiness-unverified":
        return
    candidate = state["steps"]["upload"]["candidate"]
    coordinator.verify_deployment(candidate, state["steps"]["deployment"]["result"])
    try:
        with urllib.request.urlopen(candidate["master_endpoint"] + "/ready", timeout=20) as response:
            ready = response.status == 200
    except OSError:
        ready = False
    if ready:
        observation = {"candidateHash": digest(candidate), "status": 200, "observedAtUtc": now()}
        atomic_json(path.parent / ("public-readiness-" + uuid.uuid4().hex[:8] + ".json"), observation)
        mark(path, state, "deployment", "completed", publicReadinessObservation=observation)


def execute(args, root, path, state):
    build(root, path, state)
    publish(root, path, state)
    dispatch(root, path, state)
    if args.retry_deploy:
        reconcile(path, state)
        retry_deployment(path, state)
    deadline = time.monotonic() + args.wait_timeout
    while True:
        reconcile(path, state, approve=True)
        recheck_public_readiness(path, state)
        workflow = state["steps"]["workflow"]
        deployment = state["steps"]["deployment"]
        if (args.no_wait or workflow["status"] in ("completed", "dispatch-unconfirmed", "retry-unconfirmed")
                or deployment["status"] == "waiting-for-reviewer" or time.monotonic() >= deadline):
            break
        time.sleep(15)


def parse_appinfo(text):
    if len(text) > 5_000_000:
        raise ControllerError("Steam metadata response is too large")
    tokens = [quoted if brace == "" else brace
              for quoted, brace in re.findall(r'"((?:\\.|[^"\\])*)"|([{}])', text)]
    starts = [index for index in range(len(tokens) - 1)
              if tokens[index] == str(coordinator.APP_ID) and tokens[index + 1] == "{"]
    if len(starts) != 1:
        raise ControllerError("Steam metadata lacks one unambiguous Playtest app")

    def object_at(index, depth=0):
        if depth > 64:
            raise ControllerError("Steam metadata nesting is too deep")
        result = {}
        while index < len(tokens) and tokens[index] != "}":
            key = tokens[index]
            index += 1
            if key in ("{", "}") or key in result or index >= len(tokens):
                raise ControllerError("Malformed or duplicate Steam metadata field")
            if tokens[index] == "{":
                value, index = object_at(index + 1, depth + 1)
            else:
                value = tokens[index]
                if value == "}":
                    raise ControllerError("Steam metadata value is missing")
                index += 1
            result[key] = value
        if index >= len(tokens):
            raise ControllerError("Unterminated Steam metadata object")
        return result, index + 1

    app, _ = object_at(starts[0] + 2)
    try:
        depots = app["depots"]
        build_id = depots["branches"]["public"]["buildid"]
        manifest_id = depots[str(coordinator.DEPOT_ID)]["manifests"]["public"]["gid"]
        if not re.fullmatch(r"[1-9][0-9]*", build_id) or not re.fullmatch(r"[1-9][0-9]*", manifest_id):
            raise ValueError
        if int(build_id) > 2**32 - 1 or int(manifest_id) > 2**64 - 1:
            raise ValueError
    except (KeyError, TypeError, ValueError):
        raise ControllerError("Steam public branch BuildID or depot manifest is missing/invalid")
    return {"appId": coordinator.APP_ID, "depotId": coordinator.DEPOT_ID,
            "buildId": int(build_id), "manifestId": manifest_id,
            "steamworksBranch": "default", "appInfoBranch": "public"}


def verify_live(args, path, state):
    if state["steps"]["deployment"]["status"] != "completed":
        raise ControllerError("Require a successful compatible backend receipt before Steam activation verification")
    steamcmd = shutil.which(args.steamcmd)
    if not steamcmd:
        raise ControllerError("Installed SteamCMD is required for fresh anonymous app info")
    mark(path, state, "steam", "verifying")
    log_path = path.parent / ("steam-appinfo-" + uuid.uuid4().hex[:8] + ".log")
    with os.fdopen(os.open(log_path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600), "w") as log:
        result = subprocess.run([steamcmd, "+login", "anonymous", "+app_info_update", "1",
                                 "+app_info_print", str(coordinator.APP_ID), "+quit"],
                                stdin=subprocess.DEVNULL, stdout=log, stderr=subprocess.STDOUT, timeout=120)
    if result.returncode:
        mark(path, state, "steam", "unverified", reason="steam_metadata_failed", log=str(log_path))
        raise ControllerError("Anonymous Steam metadata failed; no live activation was recorded")
    observation = parse_appinfo(log_path.read_text(errors="replace"))
    observation.update(observedAtUtc=now(), source="fresh anonymous SteamCMD app info")
    atomic_json(path.parent / ("steam-observation-" + uuid.uuid4().hex[:8] + ".json"), observation)
    expected = state["steps"]["upload"]["candidate"]["steam"]
    matched = observation["buildId"] == expected["build_id"] and observation["manifestId"] == expected["manifest_id"]
    mark(path, state, "steam", "completed" if matched else "awaiting-activation", observation=observation)
    if not matched:
        raise ControllerError("Steam default does not yet show this candidate's exact BuildID and manifest")


def diagnose_cook(root, path, state):
    operation = state.get("cookOperation", {})
    identity = process_identity(operation.get("pid")) if operation.get("pid") else None
    if identity is not None and (operation.get("processIdentity") is None or identity == operation["processIdentity"]):
        raise ControllerError("The recorded diagnostic cook is still running; do not start another")
    checkout = ensure_checkout(root, path, state, cook=True)
    source = run_builder(path, state, checkout, cook=True)
    handoffs = [value for value in source.glob("20*/handoff.json")
                if coordinator.read_json(value).get("result") == "diagnostic-cook"]
    if len(handoffs) != 1:
        raise ControllerError("Diagnostic cook did not produce one settled report")
    retained = path.parent / handoffs[0].parent.name
    state["cookDiagnostic"] = {"report": str(retained / "cook-diagnostics/cook.json"),
                              "correctionPatch": str(retained / "cook-correction.patch"),
                              "observedAtUtc": now(), "published": False}
    save(path, state)


def record_player_test(args, path, state):
    if state["steps"]["steam"]["status"] != "completed":
        raise ControllerError("Player acceptance requires an observed live candidate")
    expected = state["steps"]["upload"]["candidate"]["steam"]["build_id"]
    if args.build_id != expected:
        raise ControllerError("Player test BuildID differs from this candidate")
    if args.outcome == "passed" and (not args.countdown_passed or not args.match_id or not args.rematch_id
                                      or args.match_id == args.rematch_id):
        raise ControllerError("Passing acceptance requires countdown and distinct completed match/rematch IDs")
    evidence = []
    for supplied in args.diagnostics:
        supplied = supplied.expanduser().resolve(strict=True)
        coordinator.read_json(supplied)  # Require an actual exported JSON document.
        destination = path.parent / "player-tests" / (uuid.uuid4().hex + ".json")
        destination.parent.mkdir(parents=True, exist_ok=True, mode=0o700)
        with os.fdopen(os.open(destination, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600), "wb") as output:
            output.write(supplied.read_bytes())
        evidence.append({"path": str(destination), "sha256": coordinator.checksum(destination)})
    report = {"outcome": args.outcome, "buildId": expected, "countdownPassed": args.countdown_passed,
              "matchId": args.match_id, "rematchId": args.rematch_id, "diagnostics": evidence,
              "notes": args.notes, "observedAtUtc": now(), "source": "operator-reported packaged two-account test"}
    state["playerAcceptance"]["reports"].append(report)
    state["playerAcceptance"]["status"] = args.outcome
    save(path, state)


def summary(state):
    config = state["configuration"]
    lines = [config["version"], f"Game: {config['gameRevision']}", f"Master: {config['masterRevision']}",
             f"Protocol: {state['compatibility']['game_protocol']} (both pinned sources)"]
    lines += [f"{name}: {state['steps'][name]['status']}" for name in STEPS]
    if state["steps"]["build"].get("log") and state["steps"]["build"]["status"] in ("failed", "interrupted"):
        lines.append("Build evidence: " + state["steps"]["build"]["log"])
    lines.append("player acceptance: " + state["playerAcceptance"]["status"])
    workflow = state["steps"]["workflow"]
    if workflow.get("url"):
        lines.append("CI: " + workflow["url"])
    deployment = state["steps"]["deployment"]
    if deployment["status"] == "completed" and state["steps"]["steam"]["status"] != "completed":
        steam = state["steps"]["upload"]["candidate"]["steam"]
        lines += [f"Operator: activate BuildID {steam['build_id']} on default.",
                  f"Depot {steam['depot_id']} / manifest {steam['manifest_id']}.",
                  f"https://partner.steamgames.com/apps/builds/{steam['app_id']}",
                  "Preview Change -> Set Build Live Now -> accept browser/mobile confirmations; then verify-live."]
    elif deployment["status"] == "public-readiness-unverified":
        lines.append("Backend deployment receipt is verified; public HTTPS readiness is unverified.")
        lines.append("Resume can repeat the public readiness observation without deploying or uploading again.")
    elif deployment["status"] in ("failed", "ambiguous"):
        result = deployment.get("result", {})
        lines.append("Failure: " + result.get("failed_stage", deployment.get("reason", "unknown")))
        lines.append("Recovery: " + result.get("recovery", {}).get("status", "unknown"))
        lines.append("Resume only after the failure cause is resolved; retain the existing Steam upload.")
    if state["steps"]["steam"]["status"] == "completed" and state["playerAcceptance"]["status"] != "passed":
        lines.append("Player checklist: two updated Steam accounts -> countdown -> match -> rematch; capture diagnostics.")
    if workflow["status"] == "dispatch-unconfirmed":
        lines.append("Dispatch is unconfirmed. Resume reconciles this request; it never sends it again blindly.")
    if workflow["status"] == "retry-unconfirmed":
        lines.append("Deploy retry is unconfirmed. Resume waits for its new attempt; it never submits another blindly.")
    if state.get("cookDiagnostic"):
        lines.append("Cook report: " + state["cookDiagnostic"]["report"])
        lines.append("Reviewable cook correction: " + state["cookDiagnostic"]["correctionPatch"])
    return "\n".join(lines)


def parser():
    result = argparse.ArgumentParser(description=__doc__)
    result.add_argument("--root", type=Path, default=ROOT, help="Explicit selected game checkout")
    commands = result.add_subparsers(dest="action", required=True)
    for name in ("prepare", "run", "resume", "status", "verify-live", "cook", "record-player-test"):
        action = commands.add_parser(name)
        action.add_argument("--version", required=True, type=producer.version_arg)
        action.add_argument("--json", action="store_true", help="Print the durable record")
        if name == "prepare":
            action.add_argument("--master-root", type=Path, required=True)
            action.add_argument("--game-revision", type=producer.revision_arg)
            action.add_argument("--master-revision", type=producer.revision_arg)
            action.add_argument("--endpoint", type=producer.endpoint_arg, default=ENDPOINT)
            action.add_argument("--local-unity-project", type=Path)
            action.add_argument("--unity-editor", type=producer.unity_path_arg, default=producer.DEFAULT_UNITY)
            action.add_argument("--migration-from", default="")
        elif name in ("run", "resume"):
            action.add_argument("--no-wait", action="store_true", help="Return after dispatch/reconciliation")
            action.add_argument("--wait-timeout", type=int, default=3600)
            action.add_argument("--retry-deploy", action="store_true", help="Retry only a proven safe failed deploy job")
        elif name == "status":
            action.add_argument("--refresh", action="store_true", help="Read the exact CI run and download its receipts")
        elif name == "verify-live":
            action.add_argument("--steamcmd", default="steamcmd")
        elif name == "record-player-test":
            action.add_argument("--outcome", choices=("passed", "failed"), required=True)
            action.add_argument("--build-id", type=int, required=True)
            action.add_argument("--countdown-passed", action="store_true")
            action.add_argument("--match-id")
            action.add_argument("--rematch-id")
            action.add_argument("--diagnostics", action="append", type=Path, default=[])
            action.add_argument("--notes", default="")
    return result


def main():
    args = parser().parse_args()
    try:
        root = args.root.expanduser().resolve(strict=True)
        if (Path(git(root, "rev-parse", "--show-toplevel")).resolve() != root
                or not (root / "src/Shared/SlopArena.Shared.csproj").is_file()
                or not (root / "tests/Shared.Tests/Shared.Tests.csproj").is_file()):
            raise ControllerError("Explicit root is not the selected game checkout")
        path = record_path(root, args.version)
        if args.action == "status" and not args.refresh:
            state = read_state(path)
        else:
            with locked(path):
                state = prepare(args, root, path) if args.action == "prepare" else read_state(path)
                if args.action in ("run", "resume"):
                    if not 0 <= args.wait_timeout <= 7200:
                        raise ControllerError("Wait timeout must be 0–7200 seconds")
                    execute(args, root, path, state)
                elif args.action == "status":
                    reconcile(path, state)
                elif args.action == "verify-live":
                    verify_live(args, path, state)
                elif args.action == "cook":
                    diagnose_cook(root, path, state)
                elif args.action == "record-player-test":
                    record_player_test(args, path, state)
        print(json.dumps(state, indent=2) if args.json else summary(state))
        return 1 if args.action in ("run", "resume") and (
            state["steps"]["deployment"]["status"] in ("failed", "ambiguous", "not-started", "public-readiness-unverified")
            or state["steps"]["workflow"]["status"] == "completed"
            and state["steps"]["workflow"].get("conclusion") in ("failure", "cancelled", "timed_out")
            and state["steps"]["deployment"]["status"] != "completed") else 0
    except KeyboardInterrupt:
        print("Interrupted. Retained status and receipts can be resumed.", file=sys.stderr)
        return 130
    except (OSError, RuntimeError, ValueError, TypeError, KeyError, tarfile.TarError, subprocess.TimeoutExpired) as exc:
        # External operations and raw logs can contain credentials; only controlled failures are displayed.
        print(str(exc) if isinstance(exc, ControllerError) else
              "Release operation refused or failed; retained evidence is unchanged. Inspect status and private logs.",
              file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
