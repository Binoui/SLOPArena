#!/usr/bin/python3
"""Receive one pinned public Playtest candidate and deploy it on the VPS."""
from __future__ import annotations

import hashlib
import json
import os
import re
import stat
import subprocess
import sys
import time
import uuid
from datetime import datetime, timezone
from pathlib import Path
from types import SimpleNamespace
from urllib.parse import urlsplit
from typing import Any

sys.path.insert(0, str(Path(__file__).resolve().parent))
import recovery
import release

STATE_DIR = Path("/var/lib/sloparena")
BACKUP_SERVICE = "sloparena-backup.service"
MAX_CANDIDATE_BYTES = 65_536
BACKUP_TIMEOUT_SECONDS = 45 * 60
REGISTRATION_TIMEOUT_SECONDS = 30
IMAGE_REPOSITORIES = {
    "gameplay": "ghcr.io/binoui/sloparena-gameserver",
    "master": "ghcr.io/binoui/sloparena-masterserver",
    "migration": "ghcr.io/binoui/sloparena-masterserver-migrations",
}
VERSION = re.compile(r"^[0-9]+\.[0-9]+\.[0-9]+-playtest\.[0-9]+$")
RELEASE_ID = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._-]{0,99}$")
SHA256 = re.compile(r"^[a-f0-9]{64}$")
MANIFEST_ID = re.compile(r"^[1-9][0-9]{0,19}$")
# Stage keys Master admits online; the release copies them from the picker roster.
ARENA_KEY = re.compile(r"^[a-z][a-z0-9_]{0,63}$")
MAX_ADMITTED_ARENAS = 32


class CandidateError(release.ReleaseError):
    pass


class DeploymentFailure(release.ReleaseError):
    def __init__(self, receipt: dict[str, Any]) -> None:
        self.receipt = receipt
        super().__init__("deployment failed; sanitized receipt retained")


PUBLIC_STAGES = {
    "unknown",
    "receiver_preflight", "offhost_backup", "backup_freshness", "candidate_validation",
    "host_registration", "deployment_verification", "idle_registration", "docker_context",
    "compose_config", "pre_replacement_logs", "application_readiness", "pg_dump_backup",
    "schema_compatibility", "restore_previous_release", "stop_writers", "start_release_services",
    "run_schema_migration", "restore_previous_services", "firewall_preflight", "pull_images",
    "verify_images", "start_postgres", "postgres_readiness", "current_schema", "dns_preflight",
    "base_compose_config", "check_migration_history", "docker_context_inspect", "docker_context_show",
    "ensure_postgres", "read_migration_history", "start_rollback_services",
}

FAILURE_CODES = {"schema_migration_failed", "application_not_ready", "registration_unavailable",
                 "backup_failed", "command_failed", "deployment_refused"}


def validate_failure(receipt: Any, release_id: str) -> dict:
    fields = {"release_id", "outcome", "failed_stage", "reason_code", "active_release", "recovery",
              "failure_during_migration", "schema_after", "observed_at"}
    if not isinstance(receipt, dict) or set(receipt) != fields:
        raise CandidateError("failure receipt has unknown or missing fields")
    if (receipt["release_id"] != release_id or receipt["outcome"] != "failure"
            or receipt["failed_stage"] not in PUBLIC_STAGES or receipt["reason_code"] not in FAILURE_CODES
            or type(receipt["failure_during_migration"]) is not bool
            or not isinstance(receipt["observed_at"], str) or len(receipt["observed_at"]) > 64
            or _parse_time(receipt["observed_at"]) is None):
        raise CandidateError("failure receipt identity, stage or reason is invalid")
    recovery_state = receipt["recovery"]
    if (not isinstance(recovery_state, dict) or set(recovery_state) != {"status", "release_id"}
            or recovery_state["status"] not in {"restored", "failed", "not_attempted"}):
        raise CandidateError("failure receipt recovery is invalid")
    for identity in (receipt["active_release"], recovery_state["release_id"]):
        if identity is not None and (not isinstance(identity, str) or not RELEASE_ID.fullmatch(identity)):
            raise CandidateError("failure receipt contains an invalid release identity")
    schema = receipt["schema_after"]
    if schema is not None and (not isinstance(schema, str) or len(schema) > 128 or not release.MIGRATION_ID.fullmatch(schema)):
        raise CandidateError("failure receipt schema is invalid")
    return receipt


def public_failure(candidate: dict, event: dict, target: Path, error: Exception) -> dict:
    """Project only known identities/enums; exception text and commands stay private."""
    stage = event.get("failed_stage") or event.get("stage")
    stage = stage if stage in PUBLIC_STAGES else "unknown"
    code = ("schema_migration_failed" if event.get("failure_during_migration") is True else
            "application_not_ready" if stage == "application_readiness" else
            "registration_unavailable" if stage in ("idle_registration", "host_registration") else
            "backup_failed" if stage in ("offhost_backup", "backup_freshness") else
            "command_failed" if isinstance(error, release.CommandFailure) else "deployment_refused")
    active_id = None
    try:
        active = release.active_manifest(target)
        if active and isinstance(active.get("release_id"), str) and RELEASE_ID.fullmatch(active["release_id"]):
            active_id = active["release_id"]
    except Exception:
        pass
    recovery = event.get("recovery")
    status = recovery.get("status") if isinstance(recovery, dict) else None
    status = status if status in ("restored", "failed") else "not_attempted"
    recovered_id = recovery.get("release_id") if isinstance(recovery, dict) else None
    if not isinstance(recovered_id, str) or not RELEASE_ID.fullmatch(recovered_id):
        recovered_id = None
    schema = event.get("schema_after")
    if not isinstance(schema, str) or len(schema) > 128 or not release.MIGRATION_ID.fullmatch(schema):
        schema = None
    return {"release_id": candidate["release_id"], "outcome": "failure", "failed_stage": stage,
            "reason_code": code, "active_release": active_id,
            "recovery": {"status": status, "release_id": recovered_id},
            "failure_during_migration": event.get("failure_during_migration") is True,
            "schema_after": schema, "observed_at": release.now()}


def _unique_pairs(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise CandidateError("candidate JSON contains a duplicate key")
        result[key] = value
    return result


def _reject_constant(_value: str) -> None:
    raise CandidateError("candidate JSON contains an invalid number")


def read_candidate(stream) -> dict[str, Any]:
    try:
        raw = stream.read(MAX_CANDIDATE_BYTES + 1)
    except OSError as exc:
        raise CandidateError("candidate input could not be read") from exc
    if len(raw) > MAX_CANDIDATE_BYTES:
        raise CandidateError("candidate JSON exceeds the size limit")
    try:
        value = json.loads(raw.decode("utf-8"), object_pairs_hook=_unique_pairs,
                           parse_constant=_reject_constant)
    except CandidateError:
        raise
    except (ValueError, RecursionError) as exc:
        raise CandidateError("candidate JSON is malformed") from exc
    return validate_candidate(value)


def validate_candidate(payload: Any) -> dict[str, Any]:
    """Validate and normalize the public candidate without consulting host state."""
    keys = {"release_id", "version", "source_revisions", "images", "catalog_hash",
            "schema", "steam", "master_endpoint"}
    # admitted_arenas is optional so candidates from before roster admission still validate.
    if not isinstance(payload, dict) or not keys <= set(payload) <= keys | {"admitted_arenas"}:
        raise CandidateError("candidate must contain exactly the approved public fields")

    release_id = payload["release_id"]
    version = payload["version"]
    if not isinstance(release_id, str) or not RELEASE_ID.fullmatch(release_id):
        raise CandidateError("release_id is invalid")
    if not isinstance(version, str) or len(version) > 64 or not VERSION.fullmatch(version):
        raise CandidateError("version must be a bounded x.y.z-playtest.n value")

    revisions = payload["source_revisions"]
    if not isinstance(revisions, dict) or set(revisions) != {"gameplay", "master"}:
        raise CandidateError("source_revisions must contain exactly gameplay and master")
    if any(not isinstance(value, str) or not re.fullmatch(r"[a-f0-9]{40}", value)
           for value in revisions.values()):
        raise CandidateError("source revisions must be full lowercase 40-character commit hashes")

    images = payload["images"]
    if not isinstance(images, dict) or set(images) != set(IMAGE_REPOSITORIES):
        raise CandidateError("images must contain exactly gameplay, master and migration")
    normalized_images: dict[str, str] = {}
    for name, repository in IMAGE_REPOSITORIES.items():
        reference = images[name]
        if (not isinstance(reference, str) or
                not re.fullmatch(re.escape(repository) + r"@sha256:[a-f0-9]{64}", reference)):
            raise CandidateError(f"images.{name} must pin its approved GHCR repository digest")
        normalized_images[name] = reference

    catalog_hash = payload["catalog_hash"]
    if not isinstance(catalog_hash, str) or not SHA256.fullmatch(catalog_hash):
        raise CandidateError("catalog_hash must be a lowercase SHA-256 digest")

    schema = payload["schema"]
    if (not isinstance(schema, dict) or
            set(schema) != {"target_migration", "compatible_migrations", "upgrade_from"}):
        raise CandidateError("schema has unknown or missing fields")
    target = schema["target_migration"]
    compatible = schema["compatible_migrations"]
    upgrade_from = schema["upgrade_from"]
    if not isinstance(target, str) or not release.MIGRATION_ID.fullmatch(target):
        raise CandidateError("schema.target_migration is invalid")
    if (not isinstance(compatible, list) or not compatible or
            any(not isinstance(item, str) or not release.MIGRATION_ID.fullmatch(item)
                for item in compatible) or len(set(compatible)) != len(compatible) or
            target not in compatible):
        raise CandidateError("schema.compatible_migrations is invalid")
    if (not isinstance(upgrade_from, list) or
            any(not isinstance(item, str) or not release.MIGRATION_ID.fullmatch(item)
                for item in upgrade_from) or len(set(upgrade_from)) != len(upgrade_from) or
            target in upgrade_from):
        raise CandidateError("schema.upgrade_from must list only explicit unique migration IDs")

    steam = payload["steam"]
    if not isinstance(steam, dict) or set(steam) != {"app_id", "depot_id", "build_id", "manifest_id"}:
        raise CandidateError("steam has unknown or missing fields")
    if type(steam["app_id"]) is not int or steam["app_id"] != 5325920:
        raise CandidateError("steam.app_id must be the approved Playtest app")
    if type(steam["depot_id"]) is not int or steam["depot_id"] != 5325921:
        raise CandidateError("steam.depot_id must be the approved Playtest depot")
    if type(steam["build_id"]) is not int or not 1 <= steam["build_id"] <= 2**32 - 1:
        raise CandidateError("steam.build_id must be a positive 32-bit integer")
    manifest_id = steam["manifest_id"]
    if (not isinstance(manifest_id, str) or not MANIFEST_ID.fullmatch(manifest_id) or
            int(manifest_id) > 2**64 - 1):
        raise CandidateError("steam.manifest_id must be a canonical nonzero decimal string")

    endpoint = payload["master_endpoint"]
    if not isinstance(endpoint, str) or len(endpoint) > 261:
        raise CandidateError("master_endpoint must be an HTTPS origin")
    try:
        parsed = urlsplit(endpoint)
        host = parsed.hostname
    except ValueError as exc:
        raise CandidateError("master_endpoint must be an HTTPS origin") from exc
    if (parsed.scheme != "https" or not host or parsed.netloc != host or
            endpoint != f"https://{host}" or not release.HOSTNAME.fullmatch(host)):
        raise CandidateError("master_endpoint must be an HTTPS hostname without a path")

    arenas = payload.get("admitted_arenas")
    if "admitted_arenas" in payload and (
            not isinstance(arenas, list) or not 1 <= len(arenas) <= MAX_ADMITTED_ARENAS or
            any(not isinstance(name, str) or not ARENA_KEY.fullmatch(name) for name in arenas) or
            len(set(arenas)) != len(arenas)):
        raise CandidateError("admitted_arenas must list 1-32 unique lowercase stage keys")

    normalized = {
        "release_id": release_id,
        "version": version,
        "source_revisions": {name: revisions[name] for name in ("gameplay", "master")},
        "images": normalized_images,
        "catalog_hash": catalog_hash,
        "schema": {
            "target_migration": target,
            "compatible_migrations": list(compatible),
            "upgrade_from": list(upgrade_from),
        },
        "steam": {name: steam[name] for name in ("app_id", "depot_id", "build_id", "manifest_id")},
        "master_endpoint": endpoint,
    }
    if arenas is not None:
        normalized["admitted_arenas"] = list(arenas)
    return normalized


def require_endpoint(candidate: dict[str, Any], active: dict[str, Any]) -> None:
    expected = f"https://{active['runtime']['master_test_host']}"
    if candidate["master_endpoint"] != expected:
        raise release.ReleaseError("client endpoint does not match the active VPS Master host")


def release_manifest(candidate: dict[str, Any], runtime: dict[str, Any]) -> dict[str, Any]:
    return release.validate_manifest({
        "release_id": candidate["release_id"],
        "source_revisions": candidate["source_revisions"],
        "images": candidate["images"],
        "schema": candidate["schema"],
        "runtime": dict(runtime),
    })


def candidate_record(target: Path, release_id: str) -> Path:
    return target / "ci-candidates" / f"{release_id}.json"


def require_immutable_record(path: Path, content: bytes) -> None:
    if path.is_symlink():
        raise release.ReleaseError("immutable release record is not a regular file")
    if path.exists() and (not path.is_file() or path.read_bytes() != content):
        raise release.ReleaseError("release ID already exists with different candidate contents")


def save_immutable_record(path: Path, content: bytes) -> None:
    require_immutable_record(path, content)
    if not path.exists():
        release.private_dir(path.parent)
        release.atomic_write(path, content)


def private_master_environment(active: dict[str, Any], target: Path,
                               candidate: dict[str, Any]) -> Path:
    source = Path(active["runtime"]["master_env_file"])
    source_stat = source.stat()
    original = source.read_bytes()
    lines = original.splitlines(keepends=True)
    matching = [index for index, line in enumerate(lines)
                if re.match(rb"^\s*(?:export\s+)?Room__CatalogHash\s*=", line)]
    if len(matching) != 1:
        raise release.ReleaseError("active Master environment must define exactly one Room__CatalogHash")
    index = matching[0]
    match = re.fullmatch(rb"(Room__CatalogHash=)[a-f0-9]{64}(\r?\n)?", lines[index])
    if not match:
        raise release.ReleaseError("active Room__CatalogHash must be a plain SHA-256 assignment")
    ending = match.group(2) or b""
    lines[index] = match.group(1) + candidate["catalog_hash"].encode("ascii") + ending
    if "admitted_arenas" in candidate:
        # The roster replaces every Room__AdmittedArenas__N entry, in place of the first one.
        arena_lines = [i for i, line in enumerate(lines)
                       if re.match(rb"^\s*(?:export\s+)?Room__AdmittedArenas__", line)]
        for i in arena_lines:
            if not re.fullmatch(rb"Room__AdmittedArenas__[0-9]+=[a-z0-9_]*(\r?\n)?", lines[i]):
                raise release.ReleaseError("active Room__AdmittedArenas entries must be plain assignments")
        position = arena_lines[0] if arena_lines else index + 1
        line_ending = ending or b"\n"
        if not arena_lines and not lines[index].endswith(b"\n"):
            lines[index] += line_ending
        admitted = [b"Room__AdmittedArenas__%d=%s" % (n, name.encode("ascii")) + line_ending
                    for n, name in enumerate(candidate["admitted_arenas"])]
        kept = [line for i, line in enumerate(lines) if i not in set(arena_lines)]
        position -= sum(1 for i in arena_lines if i < position)
        lines = kept[:position] + admitted + kept[position:]
    content = b"".join(lines)
    path = target / "private" / f"master-{candidate['release_id']}.env"
    release.private_dir(path.parent)
    mode = stat.S_IMODE(source_stat.st_mode)
    if path.is_symlink():
        raise release.ReleaseError("release-specific Master environment is not a regular file")
    if path.exists():
        existing_stat = path.stat()
        if (not path.is_file() or path.read_bytes() != content or
                existing_stat.st_uid != source_stat.st_uid or
                existing_stat.st_gid != source_stat.st_gid or
                stat.S_IMODE(existing_stat.st_mode) != mode):
            raise release.ReleaseError("release-specific Master environment already differs")
        return path

    temporary = path.with_name(f".{path.name}.{uuid.uuid4().hex}.tmp")
    fd = os.open(temporary, os.O_WRONLY | os.O_CREAT | os.O_EXCL, mode)
    try:
        with os.fdopen(fd, "wb") as stream:
            os.fchown(stream.fileno(), source_stat.st_uid, source_stat.st_gid)
            os.fchmod(stream.fileno(), mode)
            stream.write(content)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, path)
        directory_fd = os.open(path.parent, os.O_RDONLY | getattr(os, "O_DIRECTORY", 0))
        try:
            os.fsync(directory_fd)
        finally:
            os.close(directory_fd)
    except BaseException:
        try:
            temporary.unlink(missing_ok=True)
        except OSError:
            pass
        raise
    return path


def require_active_vps_profile(event: dict[str, Any], target: Path, active: dict[str, Any],
                               env: dict[str, str], catalog_hash: str | None = None,
                               admitted_arenas: list[str] | None = None) -> None:
    config = release.image_config(event, target, target / "release.env", None, env, active)
    try:
        master_env = config["services"]["master"]["environment"]
    except (KeyError, TypeError) as exc:
        raise release.ReleaseError("active Master Compose profile is unavailable") from exc
    expected = {
        "Deployment__Profile": "vps",
        "Auth__Mode": "steam",
        "Steam__AppId": "5325920",
        "Steam__Identity": "sloparena-playtest",
    }
    if catalog_hash is not None:
        expected["Room__CatalogHash"] = catalog_hash
    if admitted_arenas is not None:
        expected.update({f"Room__AdmittedArenas__{n}": name for n, name in enumerate(admitted_arenas)})
    if not isinstance(master_env, dict) or any(master_env.get(key) != value for key, value in expected.items()):
        raise release.ReleaseError("active Master Compose profile is not the approved VPS Playtest profile")
    if admitted_arenas is not None and any(key.startswith("Room__AdmittedArenas__") and key not in expected
                                           for key in master_env):
        raise release.ReleaseError("active Master Compose profile admits arenas beyond the release roster")


def run_offhost_backup() -> datetime:
    started = datetime.now(timezone.utc).replace(microsecond=0)
    try:
        result = subprocess.run(
            ["/usr/bin/systemctl", "start", BACKUP_SERVICE],
            stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
            env={"PATH": "/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin",
                 "HOME": "/root", "LANG": "C.UTF-8"},
            timeout=BACKUP_TIMEOUT_SECONDS, check=False,
        )
    except (OSError, subprocess.TimeoutExpired) as exc:
        raise release.ReleaseError("fresh offhost backup service did not complete") from exc
    if result.returncode != 0:
        raise release.ReleaseError("fresh offhost backup service failed")
    return started


def _parse_time(value: Any) -> datetime | None:
    if not isinstance(value, str):
        return None
    try:
        parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError:
        return None
    return parsed.astimezone(timezone.utc) if parsed.tzinfo is not None else None


def require_fresh_backup(target: Path, active: dict[str, Any], started: datetime) -> dict[str, Any]:
    attempt = release.parse_json(target / "last-backup-attempt.json")
    backup = release.parse_json(target / "last-offhost-backup.json")
    attempt_time = _parse_time(attempt.get("timestamp") if isinstance(attempt, dict) else None)
    backup_time = _parse_time(backup.get("timestamp") if isinstance(backup, dict) else None)
    if (not isinstance(attempt, dict) or attempt.get("result") != "success" or
            attempt_time is None or attempt_time < started or backup_time is None or
            backup_time < started or backup.get("release_id") != active["release_id"] or
            backup.get("images") != active["images"] or
            not isinstance(backup.get("key"), str) or not recovery.KEY.fullmatch(backup["key"]) or
            not isinstance(backup.get("sha256"), str) or not SHA256.fullmatch(backup["sha256"]) or
            type(backup.get("bytes")) is not int or backup["bytes"] <= 0):
        raise release.ReleaseError("no fresh successful offhost backup matches the active release")
    return {"timestamp": backup["timestamp"], "key": backup["key"],
            "sha256": backup["sha256"], "bytes": backup["bytes"]}


def registration_until_fresh(target: Path, env_file: Path, env: dict[str, str]) -> dict[str, Any]:
    deadline = time.monotonic() + REGISTRATION_TIMEOUT_SECONDS
    while True:
        status = release.registration_status(target, env_file, None, env)
        if status.get("available") and status.get("registered") is True:
            return status
        if time.monotonic() >= deadline:
            raise release.ReleaseError("GameServer did not report a fresh registration after deployment")
        time.sleep(1)


def public_registration(status: dict[str, Any]) -> dict[str, Any]:
    return {
        "available": status["available"],
        "registered": status["registered"],
        "active_matches": status["active_matches"],
        "hosts": [
            {"fresh": host["fresh"], "active_matches": host["current_matches"]}
            for host in status["hosts"]
        ],
    }


def trusted_docker_env(target: Path) -> dict[str, str]:
    suffix = hashlib.sha256(str(target.resolve()).encode()).hexdigest()[:12]
    return {
        "PATH": "/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin",
        "HOME": "/root",
        "LANG": "C.UTF-8",
        "COMPOSE_PROJECT_NAME": f"sloparena-vps-{suffix}",
    }


def verify_ready(target: Path, env_file: Path, env: dict[str, str]) -> dict[str, dict[str, bool]]:
    services = {
        "master": {"readiness": release.service_probe(target, env_file, None, env, "master", 8080, "/ready")},
        "game": {"readiness": release.service_probe(target, env_file, None, env, "game", 7777, "/ready")},
    }
    if not all(item["readiness"] for item in services.values()):
        raise release.ReleaseError("deployed services did not pass their readiness probes")
    return services


def _ensure_root_state(target: Path) -> None:
    info = target.stat()
    if info.st_uid != 0 or info.st_mode & 0o077:
        raise release.ReleaseError("release state must be root-owned and private")


def deploy_candidate(candidate: dict[str, Any]) -> dict[str, Any]:
    event = {"event_id": uuid.uuid4().hex, "operation": "ci_deploy",
             "release_id": candidate["release_id"], "started_at": release.now(),
             "steps": [], "stage": "receiver_preflight"}
    try:
        return _deploy_candidate(candidate, event)
    except Exception as exc:
        raise DeploymentFailure(public_failure(candidate, event, STATE_DIR, exc)) from exc


def _deploy_candidate(candidate: dict[str, Any], event: dict) -> dict[str, Any]:
    if os.geteuid() != 0:
        raise release.ReleaseError("receiver must run as root")
    if STATE_DIR.is_symlink():
        raise release.ReleaseError("fixed release state directory may not be a symlink")
    target = release.target_path(str(STATE_DIR))
    if target != STATE_DIR:
        raise release.ReleaseError("release state path is not the fixed VPS directory")

    # The backup service uses the release lock itself, so start it outside the
    # receiver's lock and bind its receipt to the active release under lock below.
    with release.with_lock(target):
        _ensure_root_state(target)
        initial = release.active_manifest(target)
        if initial is None:
            raise release.ReleaseError("CI deployment cannot bootstrap a VPS without an active release")
        record_path = candidate_record(target, candidate["release_id"])
        record_data = release.json_bytes(candidate)
        require_immutable_record(record_path, record_data)
        require_endpoint(candidate, initial)
        initial_manifest = release_manifest(candidate, initial["runtime"])
        initial_manifest["runtime"]["master_env_file"] = str(
            target / "private" / f"master-{candidate['release_id']}.env"
        )
        require_immutable_record(
            target / "releases" / f"{candidate['release_id']}.json",
            release.json_bytes(initial_manifest),
        )
    event["stage"] = "offhost_backup"
    backup_started = run_offhost_backup()

    env = trusted_docker_env(target)
    args = SimpleNamespace(allow_disposable_host=False, compose_override=None,
                           firewall_helper=None, allow_local_image_digests=False)
    with release.with_lock(target):
        _ensure_root_state(target)
        active = release.active_manifest(target)
        if active is None:
            raise release.ReleaseError("active VPS release disappeared during backup")
        require_endpoint(candidate, active)
        event["stage"] = "backup_freshness"
        backup = require_fresh_backup(target, active, backup_started)
        try:
            release.ensure_local_docker(event, env)
            require_active_vps_profile(event, target, active, env)
            base_manifest = release_manifest(candidate, active["runtime"])
            release.validate_dns(event, base_manifest, False)
            new_env_path = target / "private" / f"master-{candidate['release_id']}.env"
            manifest = dict(base_manifest)
            manifest["runtime"] = dict(base_manifest["runtime"])
            manifest["runtime"]["master_env_file"] = str(new_env_path)
            manifest_data = release.json_bytes(manifest)
            saved_manifest = target / "releases" / f"{candidate['release_id']}.json"
            require_immutable_record(saved_manifest, manifest_data)

            record_path = candidate_record(target, candidate["release_id"])
            record_data = release.json_bytes(candidate)
            require_immutable_record(record_path, record_data)
            already_active = active["release_id"] == candidate["release_id"]
            if already_active:
                if not record_path.is_file() or not saved_manifest.is_file():
                    raise release.ReleaseError("active CI release lacks its immutable candidate record")
                environment_path = private_master_environment(active, target, candidate)
                manifest["runtime"]["master_env_file"] = str(environment_path)
                manifest = release.validate_manifest(manifest)
                release.prepare_release(event, target, manifest, target / "release.env", None, env)
            else:
                environment_path = private_master_environment(active, target, candidate)
                manifest["runtime"]["master_env_file"] = str(environment_path)
                manifest = release.validate_manifest(manifest)
                save_immutable_record(record_path, record_data)
                release.preflight(event, args, env)
                release.deploy(event, args, target, manifest, env)
            require_active_vps_profile(event, target, manifest, env, candidate["catalog_hash"],
                                       candidate.get("admitted_arenas"))

            event["stage"] = "deployment_verification"
            services = verify_ready(target, target / "release.env", env)
            _, current_schema = release.current_migration(
                event, target, target / "release.env", None, env
            )
            event["stage"] = "host_registration"
            registration = registration_until_fresh(target, target / "release.env", env)
            if not already_active:
                event["schema_after"] = current_schema
            release.record_release_event(target, event, None)
            return {
                "release_id": candidate["release_id"],
                "version": candidate["version"],
                "source_revisions": candidate["source_revisions"],
                "images": candidate["images"],
                "catalog_hash": candidate["catalog_hash"],
                "steam": candidate["steam"],
                "master_endpoint": candidate["master_endpoint"],
                "outcome": "success",
                "schema": current_schema,
                "services": services,
                "registration": public_registration(registration),
                "backup": backup,
                **({"admitted_arenas": candidate["admitted_arenas"]} if "admitted_arenas" in candidate else {}),
            }
        except Exception as exc:
            try:
                release.record_release_event(target, event, exc)
            except OSError:
                pass
            raise


def main() -> int:
    if sys.argv[1:] not in ([], ["--validate-only"]):
        print("usage: ci_release.py [--validate-only]", file=sys.stderr)
        return 2
    validate_only = sys.argv[1:] == ["--validate-only"]
    try:
        candidate = read_candidate(sys.stdin.buffer)
    except CandidateError as exc:
        print(f"candidate rejected: {exc}", file=sys.stderr)
        return 1
    if validate_only:
        print(json.dumps(candidate, indent=2, sort_keys=True))
        return 0
    try:
        receipt = deploy_candidate(candidate)
    except DeploymentFailure as exc:
        print(json.dumps(exc.receipt, indent=2, sort_keys=True))
        print("release request failed; sanitized failure receipt returned", file=sys.stderr)
        return 1
    except CandidateError as exc:
        print(f"candidate rejected: {exc}", file=sys.stderr)
        return 1
    except Exception as exc:
        if isinstance(exc, release.CommandFailure):
            message = f"deployment command failed during {exc.step}; private output withheld"
        else:
            message = "deployment precondition or operation failed; private details withheld"
        print(f"release request failed: {message}", file=sys.stderr)
        return 1
    print(json.dumps(receipt, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
