#!/usr/bin/env python3
"""Deploy and roll back pinned SLOPArena VPS releases using a local Docker daemon."""
from __future__ import annotations

import argparse
import contextlib
import fcntl
import hashlib
import ipaddress
import json
import os
import platform
import re
import socket
import subprocess
import sys
import time
import uuid
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parents[2]
COMPOSE = Path(__file__).with_name("compose.yaml")
FIREWALL_HELPER = Path("/usr/local/sbin/sloparena-vps-docker-firewall")
SERVICES = ("caddy", "postgres", "migrate", "master", "game")
APP_IMAGES = {"master": "master", "game": "gameplay", "migrate": "master"}
IMAGE_REF = re.compile(r"^([a-z0-9.-]+(?::[0-9]+)?(?:/[a-z0-9]+(?:[._-][a-z0-9]+)*)*)(?::[a-zA-Z0-9._-]+)?@sha256:([a-f0-9]{64})$")
REVISION = re.compile(r"^(?:[a-f0-9]{40}|[a-f0-9]{64})$")
TOKEN = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._+-]{0,127}$")
MIGRATION_ID = re.compile(r"^[A-Za-z0-9_]+$")
HOSTNAME = re.compile(r"^(?=.{1,253}$)(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}$")
MAX_LOG_BYTES = 1_048_576
MAX_PRE_REPLACEMENT_LOG_LINES = 200
HEALTH_TIMEOUT_SECONDS = 180


class ReleaseError(RuntimeError):
    pass


class CommandFailure(ReleaseError):
    def __init__(self, step: str, code: int, output: str = "") -> None:
        self.step = step
        self.code = code
        self.output = output
        message = f"{step} failed with exit code {code}"
        if output.strip():
            message += f": {output.strip()[-4000:]}"
        super().__init__(message)


def now() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="seconds")


def json_bytes(value: Any) -> bytes:
    return (json.dumps(value, indent=2, sort_keys=True) + "\n").encode()


def private_dir(path: Path) -> None:
    path.mkdir(parents=True, exist_ok=True, mode=0o700)
    if path.stat().st_mode & 0o077:
        raise ReleaseError(f"state directory must not be accessible to group or others: {path}")

def atomic_write(path: Path, data: bytes, mode: int = 0o600) -> None:
    private_dir(path.parent)
    temp = path.with_name(f".{path.name}.{uuid.uuid4().hex}.tmp")
    fd = os.open(temp, os.O_WRONLY | os.O_CREAT | os.O_EXCL, mode)
    try:
        with os.fdopen(fd, "wb") as stream:
            stream.write(data)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temp, path)
        dir_fd = os.open(path.parent, os.O_RDONLY | getattr(os, "O_DIRECTORY", 0))
        try:
            os.fsync(dir_fd)
        finally:
            os.close(dir_fd)
    except BaseException:
        with contextlib.suppress(FileNotFoundError):
            temp.unlink()
        raise


def parse_json(path: Path) -> Any:
    def unique_pairs(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
        result: dict[str, Any] = {}
        for key, value in pairs:
            if key in result:
                raise ReleaseError(f"duplicate JSON key {key!r} in {path}")
            result[key] = value
        return result

    try:
        return json.loads(path.read_text(encoding="utf-8"), object_pairs_hook=unique_pairs)
    except (OSError, UnicodeError, json.JSONDecodeError) as exc:
        raise ReleaseError(f"cannot read JSON config {path}: {exc}") from exc


def is_inside(path: Path, directory: Path) -> bool:
    try:
        path.relative_to(directory)
        return True
    except ValueError:
        return False


def external_file(value: Any, name: str) -> str:
    if not isinstance(value, str) or not value or "\n" in value or "\r" in value:
        raise ReleaseError(f"runtime.{name} must be an absolute file path")
    path = Path(value)
    if not path.is_absolute():
        raise ReleaseError(f"runtime.{name} must be an absolute file path")
    try:
        resolved = path.resolve(strict=True)
    except OSError as exc:
        raise ReleaseError(f"runtime.{name} does not exist: {path}") from exc
    if not resolved.is_file():
        raise ReleaseError(f"runtime.{name} is not a regular file: {resolved}")
    if is_inside(resolved, ROOT):
        raise ReleaseError(f"runtime.{name} must stay outside the repository: {resolved}")
    return str(resolved)


def validate_manifest(value: Any, allow_localhost: bool = False, allow_legacy_runtime: bool = False) -> dict[str, Any]:
    if not isinstance(value, dict) or set(value) != {"release_id", "source_revisions", "images", "schema", "runtime"}:
        raise ReleaseError("release JSON must contain exactly release_id, source_revisions, images, schema, runtime")

    release_id = value["release_id"]
    if not isinstance(release_id, str) or not TOKEN.fullmatch(release_id):
        raise ReleaseError("release_id must be a safe non-empty token")

    revisions = value["source_revisions"]
    if not isinstance(revisions, dict) or set(revisions) != {"master", "gameplay"}:
        raise ReleaseError("source_revisions must contain exactly master and gameplay")
    for name, revision in revisions.items():
        if not isinstance(revision, str) or not REVISION.fullmatch(revision):
            raise ReleaseError(f"source_revisions.{name} must be a full 40- or 64-character commit hash")

    images = value["images"]
    if not isinstance(images, dict) or set(images) != {"master", "gameplay", "migration"}:
        raise ReleaseError("images must contain exactly master, gameplay and migration")
    for name, reference in images.items():
        if not isinstance(reference, str) or not IMAGE_REF.fullmatch(reference) or (not allow_localhost and "/" not in reference.split("@", 1)[0]):
            raise ReleaseError(f"images.{name} must be an exact repository@sha256:<64 lowercase hex> digest reference")

    schema = value["schema"]
    if not isinstance(schema, dict) or set(schema) != {"target_migration", "compatible_migrations", "upgrade_from"}:
        raise ReleaseError("schema must contain exactly target_migration, compatible_migrations and upgrade_from")
    target = schema["target_migration"]
    if not isinstance(target, str) or not MIGRATION_ID.fullmatch(target):
        raise ReleaseError("schema.target_migration must be an explicit EF migration ID")
    compatible = schema["compatible_migrations"]
    upgrade_from = schema["upgrade_from"]
    if not isinstance(compatible, list) or not compatible or any(not isinstance(item, str) or not MIGRATION_ID.fullmatch(item) for item in compatible):
        raise ReleaseError("schema.compatible_migrations must be a non-empty list of EF migration IDs")
    if len(set(compatible)) != len(compatible) or target not in compatible:
        raise ReleaseError("schema.compatible_migrations must be unique and include target_migration")
    if not isinstance(upgrade_from, list) or any(item is not None and (not isinstance(item, str) or not MIGRATION_ID.fullmatch(item)) for item in upgrade_from):
        raise ReleaseError("schema.upgrade_from must list EF migration IDs and may include null for a new database")
    if len(set(upgrade_from)) != len(upgrade_from) or target in upgrade_from:
        raise ReleaseError("schema.upgrade_from must be unique and exclude target_migration")

    runtime = value["runtime"]
    required_runtime = {
        "public_ipv4", "public_ipv4_is_provider_nat", "master_test_host", "gameplay_test_host",
        "master_env_file", "migration_env_file", "game_config_file", "postgres_password_file",
        "steamclient_file", "steamclient_sha256",
    }
    legacy_runtime = required_runtime - {"steamclient_file", "steamclient_sha256"}
    if not isinstance(runtime, dict) or (
        set(runtime) != required_runtime and not (allow_legacy_runtime and set(runtime) == legacy_runtime)
    ):
        raise ReleaseError("runtime must specify VPS hosts, private files and a pinned Steam game-server runtime")
    try:
        runtime["public_ipv4"] = str(ipaddress.IPv4Address(runtime["public_ipv4"]))
    except (ipaddress.AddressValueError, TypeError) as exc:
        raise ReleaseError("runtime.public_ipv4 must be the explicit VPS IPv4 address") from exc
    if not isinstance(runtime["public_ipv4_is_provider_nat"], bool):
        raise ReleaseError("runtime.public_ipv4_is_provider_nat must be a boolean")
    for key in ("master_test_host", "gameplay_test_host"):
        host = runtime[key]
        if not isinstance(host, str) or not HOSTNAME.fullmatch(host) or (host.endswith(".localhost") and not allow_localhost):
            raise ReleaseError(f"runtime.{key} must be a distinct public DNS hostname")
    if runtime["master_test_host"] == runtime["gameplay_test_host"]:
        raise ReleaseError("Master and gameplay test hostnames must be distinct")
    for key in ("master_env_file", "migration_env_file", "game_config_file",
                "postgres_password_file"):
        runtime[key] = external_file(runtime[key], key)
    if set(runtime) == required_runtime:
        runtime["steamclient_file"] = external_file(runtime["steamclient_file"], "steamclient_file")
        if Path(runtime["master_env_file"]).stat().st_mode & 0o077:
            raise ReleaseError("private Master environment file must not be group/world-accessible")
        if not isinstance(runtime["steamclient_sha256"], str) or not re.fullmatch(r"[a-f0-9]{64}", runtime["steamclient_sha256"]):
            raise ReleaseError("runtime.steamclient_sha256 must be a lowercase SHA-256 digest")
        digest = hashlib.sha256()
        with Path(runtime["steamclient_file"]).open("rb") as stream:
            for block in iter(lambda: stream.read(1024 * 1024), b""):
                digest.update(block)
        if digest.hexdigest() != runtime["steamclient_sha256"]:
            raise ReleaseError("pinned Steam game-server runtime checksum does not match")
        master_env = Path(runtime["master_env_file"]).read_text(encoding="utf-8")
        publisher_keys = [line.partition("=")[2].strip().strip('\"')
                          for line in master_env.splitlines() if line.startswith("Steam__ApiKey=")]
        if len(publisher_keys) != 1 or not publisher_keys[0]:
            raise ReleaseError("private Master environment must configure Steam__ApiKey")

    # EF migration bundles for Master are built from the same source revision.
    return value


def load_manifest(path: Path, allow_localhost: bool = False) -> dict[str, Any]:
    value = parse_json(path)
    return validate_manifest(value, allow_localhost)




def append_event(target: Path, event: dict[str, Any]) -> None:
    history = target / "history.jsonl"
    private_dir(history.parent)
    line = json.dumps(event, sort_keys=True, separators=(",", ":")).encode() + b"\n"
    fd = os.open(history, os.O_WRONLY | os.O_CREAT | os.O_APPEND, 0o600)
    try:
        os.write(fd, line)
        os.fsync(fd)
    finally:
        os.close(fd)


def step(event: dict[str, Any], name: str, argv: list[str], *, env: dict[str, str] | None = None, max_output: int = 8192) -> str:
    event["stage"] = name
    started = time.monotonic()
    proc = subprocess.run(argv, env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, check=False)
    output = proc.stdout.decode("utf-8", errors="replace")
    entry = {
        "name": name,
        "command": argv,
        "exit_code": proc.returncode,
        "duration_seconds": round(time.monotonic() - started, 3),
    }
    if proc.returncode != 0:
        entry["output_tail"] = output[-max_output:]
        entry["status"] = "failed"
        event["steps"].append(entry)
        raise CommandFailure(name, proc.returncode, output[-max_output:])
    entry["status"] = "ok"
    event["steps"].append(entry)
    return output


def docker_env(target: Path) -> dict[str, str]:
    env = os.environ.copy()
    project_suffix = hashlib.sha256(str(target.resolve()).encode()).hexdigest()[:12]
    env["COMPOSE_PROJECT_NAME"] = f"sloparena-vps-{project_suffix}"
    return env


def ensure_local_docker(event: dict[str, Any], env: dict[str, str]) -> None:
    event["stage"] = "docker_context"
    if env.get("DOCKER_HOST"):
        endpoint = env["DOCKER_HOST"]
        details = "DOCKER_HOST"
    else:
        context = step(event, "docker_context_show", ["docker", "context", "show"], env=env).strip()
        endpoints = step(event, "docker_context_inspect", ["docker", "context", "inspect", context, "--format", "{{json .Endpoints}}"], env=env)
        try:
            endpoint = json.loads(endpoints)["docker"]["Host"]
        except (json.JSONDecodeError, KeyError, TypeError) as exc:
            raise ReleaseError(f"cannot determine Docker daemon endpoint for context {context!r}") from exc
        details = f"context {context}"
    if not isinstance(endpoint, str) or not endpoint.startswith("unix://"):
        raise ReleaseError(f"refusing non-local Docker endpoint from {details}: {endpoint!r}")
    event["docker_endpoint"] = endpoint


def check_host_mode(disposable: bool, override: Path | None, helper_override: Path | None, allow_local_image_ids: bool) -> None:
    if disposable:
        if override is None or helper_override is None:
            raise ReleaseError("--allow-disposable-host requires both --compose-override and an explicit --firewall-helper fixture")
        return
    if override is not None:
        raise ReleaseError("--compose-override is allowed only with explicit --allow-disposable-host")
    if allow_local_image_ids:
        raise ReleaseError("--allow-local-image-digests is restricted to explicit disposable mode")
    try:
        release = platform.freedesktop_os_release()
    except (OSError, AttributeError) as exc:
        raise ReleaseError("cannot verify host OS; release operations require Ubuntu 24.04 amd64") from exc
    if release.get("ID") != "ubuntu" or release.get("VERSION_ID") != "24.04" or platform.machine() not in {"x86_64", "amd64"}:
        raise ReleaseError("release operations require Ubuntu 24.04 amd64; use --allow-disposable-host only with a loopback Compose override and explicit firewall fixture")


def check_test_scope(args: argparse.Namespace) -> None:
    if args.allow_disposable_host and (args.compose_override is None or args.firewall_helper is None):
        raise ReleaseError("--allow-disposable-host requires both --compose-override and an explicit --firewall-helper fixture")
    if args.compose_override is not None and not args.allow_disposable_host:
        raise ReleaseError("--compose-override is allowed only with explicit --allow-disposable-host")
    if args.allow_local_image_digests and not args.allow_disposable_host:
        raise ReleaseError("--allow-local-image-digests is restricted to explicit disposable mode")


def preflight(event: dict[str, Any], args: argparse.Namespace, env: dict[str, str]) -> None:
    check_test_scope(args)
    check_host_mode(args.allow_disposable_host, args.compose_override, args.firewall_helper, args.allow_local_image_digests)
    ensure_local_docker(event, env)
    helper = args.firewall_helper or FIREWALL_HELPER
    if not helper.is_absolute() or not helper.is_file() or not os.access(helper, os.X_OK):
        raise ReleaseError(f"required firewall preflight helper is missing or not executable: {helper}")
    step(event, "firewall_preflight", [str(helper)], env=env)
    event["host_mode"] = "disposable" if args.allow_disposable_host else "ubuntu-24.04-amd64"
    event["firewall_helper"] = str(helper)



def validate_dns(event: dict[str, Any], manifest: dict[str, Any], disposable: bool) -> None:
    if disposable:
        event["dns_check"] = "skipped for explicit disposable target"
        return
    expected = ipaddress.IPv4Address(manifest["runtime"]["public_ipv4"])
    if not expected.is_global:
        raise ReleaseError("runtime.public_ipv4 must be a public global IPv4 address")
    if not manifest["runtime"]["public_ipv4_is_provider_nat"]:
        try:
            with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as probe:
                probe.bind((str(expected), 0))
        except OSError as exc:
            raise ReleaseError(f"VPS IPv4 {expected} is not assigned locally; set public_ipv4_is_provider_nat only for explicit provider 1:1 NAT") from exc
    observed: dict[str, list[str]] = {}
    for key in ("master_test_host", "gameplay_test_host"):
        host = manifest["runtime"][key]
        try:
            addresses = sorted({item[4][0] for item in socket.getaddrinfo(host, None, socket.AF_INET, socket.SOCK_STREAM)})
        except socket.gaierror as exc:
            raise ReleaseError(f"DNS A lookup failed for {host}: {exc}") from exc
        if addresses != [str(expected)]:
            raise ReleaseError(f"{host} A records must resolve only to configured VPS IPv4 {expected}; got {addresses}")
        try:
            ipv6 = socket.getaddrinfo(host, None, socket.AF_INET6, socket.SOCK_STREAM)
        except socket.gaierror as exc:
            if exc.errno not in {socket.EAI_NONAME, getattr(socket, "EAI_NODATA", socket.EAI_NONAME)}:
                raise ReleaseError(f"DNS AAAA lookup failed for {host}: {exc}") from exc
            ipv6 = []
        if ipv6:
            raise ReleaseError(f"{host} has AAAA records but this release requires IPv6 DNS to be absent")
        observed[host] = addresses
    event["dns_check"] = {
        "public_ipv4": str(expected),
        "public_ipv4_is_provider_nat": manifest["runtime"]["public_ipv4_is_provider_nat"],
        "A_records": observed,
        "AAAA_records": "none",
    }
def compose_argv(target: Path, env_file: Path, override: Path | None, command: list[str]) -> list[str]:
    suffix = hashlib.sha256(str(target.resolve()).encode()).hexdigest()[:12]
    project = f"sloparena-vps-{suffix}"
    argv = ["docker", "compose", "--project-name", project, "--env-file", str(env_file), "-f", str(COMPOSE)]
    if override is not None:
        argv += ["-f", str(override)]
    return argv + command


def env_value(value: str) -> str:
    if any(char in value for char in "\x00\n\r"):
        raise ReleaseError("Compose environment values may not contain NUL or newline")
    return '"' + value.replace("\\", "\\\\").replace('"', '\\"').replace("$", "$$") + '"'


def release_env(manifest: dict[str, Any]) -> bytes:
    values = {
        "MASTER_IMAGE": manifest["images"]["master"],
        "GAMESERVER_IMAGE": manifest["images"]["gameplay"],
        "MASTER_MIGRATION_IMAGE": manifest["images"]["migration"],
        "MASTER_TEST_HOST": manifest["runtime"]["master_test_host"],
        "GAMEPLAY_TEST_HOST": manifest["runtime"]["gameplay_test_host"],
        "MASTER_ENV_FILE": manifest["runtime"]["master_env_file"],
        "MIGRATION_ENV_FILE": manifest["runtime"]["migration_env_file"],
        "GAME_CONFIG_FILE": manifest["runtime"]["game_config_file"],
        "POSTGRES_PASSWORD_FILE": manifest["runtime"]["postgres_password_file"],
        "STEAMCLIENT_FILE": manifest["runtime"]["steamclient_file"],
    }
    return "".join(f"{name}={env_value(value)}\n" for name, value in values.items()).encode()


def image_config(event: dict[str, Any], target: Path, env_file: Path, override: Path | None, env: dict[str, str], manifest: dict[str, Any]) -> dict[str, Any]:
    event["stage"] = "compose_config"
    config_text = step(event, "compose_config", compose_argv(target, env_file, override, ["--profile", "migration", "config", "--format", "json"]), env=env)
    try:
        config = json.loads(config_text)
    except json.JSONDecodeError as exc:
        raise ReleaseError(f"docker compose config did not return valid JSON: {exc}") from exc
    services = config.get("services", {})
    if not isinstance(services, dict) or not set(SERVICES).issubset(services):
        raise ReleaseError("Compose config is missing a required VPS service")
    expected_images = {
        "master": manifest["images"]["master"],
        "game": manifest["images"]["gameplay"],
        "migrate": manifest["images"]["migration"],
    }
    for service, reference in expected_images.items():
        if services[service].get("image") != reference:
            raise ReleaseError(f"Compose {service} image does not match the release manifest")
    all_images: dict[str, str] = {}
    for service_name, service in services.items():
        reference = service.get("image")
        if not isinstance(reference, str) or not IMAGE_REF.fullmatch(reference):
            raise ReleaseError(f"Compose service {service_name} is not pinned to a repository digest")
        all_images[service_name] = reference
    check_published_ports(config, override is not None)
    if override is not None:
        base_text = step(event, "base_compose_config", compose_argv(target, env_file, None, ["--profile", "migration", "config", "--format", "json"]), env=env)
        try:
            base = json.loads(base_text)
        except json.JSONDecodeError as exc:
            raise ReleaseError(f"base docker compose config did not return valid JSON: {exc}") from exc
        if without_ports(base) != without_ports(config):
            raise ReleaseError("disposable Compose override may change only caddy/game port bindings")
    event["compose_images"] = all_images
    return config


def without_ports(config: dict[str, Any]) -> dict[str, Any]:
    copied = json.loads(json.dumps(config))
    for name in ("caddy", "game"):
        if name in copied.get("services", {}):
            copied["services"][name].pop("ports", None)
    return copied


def port_span(value: Any) -> tuple[int, int] | None:
    text = str(value)
    match = re.fullmatch(r"(\d+)(?:-(\d+))?", text)
    if not match:
        return None
    first, last = int(match.group(1)), int(match.group(2) or match.group(1))
    return first, last


def normalized_ports(service: dict[str, Any]) -> list[tuple[str, tuple[int, int] | None, tuple[int, int] | None, str]]:
    result = []
    for port in service.get("ports", []) or []:
        if not isinstance(port, dict):
            raise ReleaseError("Compose port config is not normalized")
        result.append((str(port.get("host_ip") or "0.0.0.0"), port_span(port.get("published")), port_span(port.get("target")), str(port.get("protocol", "tcp"))))
    return result


def check_published_ports(config: dict[str, Any], disposable: bool) -> None:
    services = config["services"]
    if services["master"].get("ports") or services["postgres"].get("ports"):
        raise ReleaseError("Compose must not publish Master, PostgreSQL or private control ports")
    bind = "127.0.0.1" if disposable else "0.0.0.0"
    web = sorted(normalized_ports(services["caddy"]))
    game = sorted(normalized_ports(services["game"]))
    expected_web = sorted([(bind, (18080, 18080), (80, 80), "tcp"), (bind, (18443, 18443), (443, 443), "tcp")]) if disposable else sorted([(bind, (80, 80), (80, 80), "tcp"), (bind, (443, 443), (443, 443), "tcp")])
    if web != expected_web or game:
        target = "loopback HTTPS-only ports" if disposable else "published 80/443 TCP with no gameplay ports"
        raise ReleaseError(f"Compose must expose only the exact {target}")
    unexpected = [name for name, service in services.items() if name not in {"caddy", "game", "master", "postgres"} and service.get("ports")]
    if unexpected:
        raise ReleaseError(f"Compose unexpectedly publishes ports for: {', '.join(unexpected)}")


def inspect_image(event: dict[str, Any], reference: str, expected_revision: str | None, expected_release: str | None, env: dict[str, str], allow_local_image_id: bool = False) -> None:
    if not allow_local_image_id:
        # Engine pull verifies registry bytes for the exact pin, including OCI indexes.
        # Local-only RepoDigest aliases cannot satisfy a registry-backed pull.
        step(event, f"pull_image_{hashlib.sha256(reference.encode()).hexdigest()[:10]}",
             ["docker", "pull", reference], env=env)
    output = step(event, f"inspect_image_{hashlib.sha256(reference.encode()).hexdigest()[:10]}", ["docker", "image", "inspect", reference], env=env)
    try:
        image = json.loads(output)[0]
    except (json.JSONDecodeError, IndexError, TypeError) as exc:
        raise ReleaseError(f"docker image inspect returned invalid data for {reference}") from exc
    repo_digests = image.get("RepoDigests") or []
    match = IMAGE_REF.fullmatch(reference)
    digest = match.group(2)  # validated by Compose and manifest before inspection
    canonical_reference = f"{match.group(1)}@sha256:{digest}"
    if canonical_reference not in repo_digests:
        raise ReleaseError(f"image {reference} is not available under that exact RepoDigest")
    labels = (image.get("Config") or {}).get("Labels") or {}
    if expected_revision is not None and labels.get("org.opencontainers.image.revision") != expected_revision:
        raise ReleaseError(f"image {reference} revision label does not match source_revisions")
    if expected_release is not None and labels.get("org.opencontainers.image.version") != expected_release:
        raise ReleaseError(f"image {reference} version label does not match release_id")


def prepare_release(event: dict[str, Any], target: Path, manifest: dict[str, Any], env_file: Path, override: Path | None, env: dict[str, str], allow_local_image_ids: bool = False) -> dict[str, Any]:
    config = image_config(event, target, env_file, override, env, manifest)
    if allow_local_image_ids:
        event["image_identity_mode"] = "explicit disposable local-image-ID fixtures; not registry provenance"
    for service, reference in event["compose_images"].items():
        revision_key = APP_IMAGES.get(service)
        expected_revision = manifest["source_revisions"][revision_key] if revision_key else None
        expected_release = manifest["release_id"] if revision_key else None
        inspect_image(event, reference, expected_revision, expected_release, env,
                      allow_local_image_ids and service in APP_IMAGES)
    return config


def query_migrations(event: dict[str, Any], target: Path, env_file: Path, override: Path | None, env: dict[str, str]) -> list[str]:
    exists_sql = "SELECT to_regclass('public.\"__EFMigrationsHistory\"');"
    exists = step(event, "check_migration_history", compose_argv(target, env_file, override, ["exec", "-T", "postgres", "psql", "-U", "sloparena", "-d", "sloparena", "-A", "-t", "-v", "ON_ERROR_STOP=1", "-c", exists_sql]), env=env).strip()
    if not exists or exists == "\n" or exists == "null":
        return []
    sql = 'SELECT "MigrationId" FROM public."__EFMigrationsHistory" ORDER BY "MigrationId";'
    output = step(event, "read_migration_history", compose_argv(target, env_file, override, ["exec", "-T", "postgres", "psql", "-U", "sloparena", "-d", "sloparena", "-A", "-t", "-v", "ON_ERROR_STOP=1", "-c", sql]), env=env)
    migrations = [line.strip() for line in output.splitlines() if line.strip()]
    if any(not MIGRATION_ID.fullmatch(item) for item in migrations):
        raise ReleaseError("database returned an invalid EF migration ID")
    return migrations


def latest_migration(migrations: list[str]) -> str | None:
    return max(migrations) if migrations else None


def ensure_postgres(event: dict[str, Any], target: Path, env_file: Path, override: Path | None, env: dict[str, str]) -> None:
    step(event, "ensure_postgres", compose_argv(target, env_file, override, ["up", "-d", "postgres"]), env=env)
    deadline = time.monotonic() + HEALTH_TIMEOUT_SECONDS
    attempt = 0
    while time.monotonic() < deadline:
        attempt += 1
        proc = subprocess.run(compose_argv(target, env_file, override, ["exec", "-T", "postgres", "pg_isready", "-U", "sloparena", "-d", "sloparena"]), env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, check=False)
        if proc.returncode == 0:
            event["steps"].append({"name": "postgres_ready", "status": "ok", "attempts": attempt})
            return
        time.sleep(2)
    event["steps"].append({"name": "postgres_ready", "status": "failed", "attempts": attempt})
    raise ReleaseError("PostgreSQL did not become ready before the timeout")


def save_manifest(target: Path, manifest: dict[str, Any]) -> None:
    path = target / "releases" / f"{manifest['release_id']}.json"
    data = json_bytes(manifest)
    if path.exists():
        if path.read_bytes() != data:
            raise ReleaseError(f"release_id {manifest['release_id']} already exists with different contents")
        return
    atomic_write(path, data)


def active_manifest(target: Path, allow_localhost: bool = False) -> dict[str, Any] | None:
    active = target / "active.json"
    if not active.exists():
        return None
    data = parse_json(active)
    release = data.get("release_id") if isinstance(data, dict) else None
    if not isinstance(release, str) or not TOKEN.fullmatch(release):
        raise ReleaseError("active.json is invalid")
    path = target / "releases" / f"{release}.json"
    if not path.is_file():
        raise ReleaseError(f"active release record is missing: {path}")
    manifest = validate_manifest(parse_json(path), allow_localhost, allow_legacy_runtime=True)
    if manifest["release_id"] != release:
        raise ReleaseError("active.json and saved release manifest disagree")
    return manifest




def save_active(target: Path, manifest: dict[str, Any], current_schema: str | None) -> None:
    atomic_write(target / "active.json", json_bytes({
        "release_id": manifest["release_id"],
        "activated_at": now(),
        "schema_migration": current_schema,
    }))


def store_schema(target: Path, migrations: list[str]) -> None:
    atomic_write(target / "schema.json", json_bytes({"observed_at": now(), "migrations": migrations, "current": latest_migration(migrations)}))


def safe_log_line(line: bytes) -> bytes:
    prefix, separator, body = line.partition(b" | ")
    if separator and b"caddy-" in prefix and body.lstrip().startswith(b"{"):
        try:
            event = json.loads(body)
            if isinstance(event, dict):
                # Prior Caddy versions may have logged the complete token-bearing URI
                # and request headers before the Caddyfile log filter was deployed.
                event.pop("request", None)
                line = prefix + separator + json.dumps(event, separators=(",", ":")).encode() + b"\n"
        except (ValueError, UnicodeError):
            return b""  # Unknown proxy log format: do not archive raw request data.
    lower = line.lower()
    if (b"request starting " in lower or b"request finished " in lower
            or any(secret in lower for secret in (b"access_token", b"authorization:", b"bearer ",
                                                   b"password=", b"secret=", b"token=", b"cookie:"))
            or re.search(rb"https?://\S+\?", lower)
            or re.search(rb"\beyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\b", line)):
        return b""
    return line


def capture_logs(event: dict[str, Any], target: Path, env_file: Path, override: Path | None, env: dict[str, str]) -> None:
    event["stage"] = "pre_replacement_logs"
    path = target / "logs" / f"pre-replacement-{event['event_id']}.log"
    private_dir(path.parent)
    argv = compose_argv(target, env_file, override, ["logs", "--no-color", "--timestamps", f"--tail={MAX_PRE_REPLACEMENT_LOG_LINES}", "caddy", "master", "game"])
    started = time.monotonic()
    truncated = False
    total = 0
    proc = subprocess.Popen(argv, env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    with path.open("xb") as output:
        os.chmod(path, 0o600)
        assert proc.stdout is not None
        for raw_line in proc.stdout:
            line = safe_log_line(raw_line)
            if not line:
                continue
            if total + len(line) <= MAX_LOG_BYTES:
                output.write(line)
                total += len(line)
            else:
                truncated = True
        code = proc.wait()
        if truncated:
            output.write(b"\n[log capture truncated at 1 MiB]\n")
        output.flush()
        os.fsync(output.fileno())
    event["steps"].append({
        "name": "pre_replacement_logs", "command": argv, "exit_code": code,
        "status": "ok" if code == 0 else "failed", "bytes": total,
        "truncated": truncated, "path": str(path),
        "duration_seconds": round(time.monotonic() - started, 3),
    })
    event["pre_replacement_logs"] = str(path)
    if code != 0:
        raise CommandFailure("pre_replacement_logs", code, f"see {path}")


def wait_ready(event: dict[str, Any], target: Path, env_file: Path, override: Path | None, env: dict[str, str]) -> None:
    probes = {"master": "8080", "game": "7777"}
    event["stage"] = "application_readiness"
    deadline = time.monotonic() + HEALTH_TIMEOUT_SECONDS
    attempt = 0
    ready: dict[str, bool] = {}
    while time.monotonic() < deadline:
        attempt += 1
        for service, port in probes.items():
            script = f'exec 3<>/dev/tcp/127.0.0.1/{port}; printf "GET /ready HTTP/1.0\\r\\nHost: localhost\\r\\nConnection: close\\r\\n\\r\\n" >&3; IFS= read -r status <&3; [[ "$status" == *" 200 "* ]]'
            argv = compose_argv(target, env_file, override, ["exec", "-T", service, "bash", "-ec", script])
            proc = subprocess.run(argv, env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, check=False)
            ready[service] = proc.returncode == 0
        if all(ready.values()):
            event["steps"].append({"name": "application_readiness", "status": "ok", "services": ready, "attempts": attempt})
            return
        time.sleep(3)
    event["steps"].append({"name": "application_readiness", "status": "failed", "services": ready, "attempts": attempt})
    raise ReleaseError(f"Master and GameServer /ready did not both return HTTP 200 within {HEALTH_TIMEOUT_SECONDS}s: {ready}")


def backup_database(event: dict[str, Any], target: Path, env_file: Path, override: Path | None, env: dict[str, str]) -> Path:
    backup = target / "backups" / f"pg-before-migration-{event['event_id']}.dump"
    private_dir(backup.parent)
    event["stage"] = "pg_dump_backup"
    argv = compose_argv(target, env_file, override, ["exec", "-T", "postgres", "pg_dump", "-U", "sloparena", "-d", "sloparena", "--format=custom"])
    started = time.monotonic()
    try:
        with backup.open("xb") as output:
            os.chmod(backup, 0o600)
            proc = subprocess.run(argv, env=env, stdout=output, stderr=subprocess.PIPE, check=False)
            output.flush()
            os.fsync(output.fileno())
        stderr = proc.stderr.decode("utf-8", errors="replace")
        backup_size = backup.stat().st_size if backup.exists() else 0
        entry = {
            "name": "pg_dump_backup", "command": argv, "exit_code": proc.returncode,
            "duration_seconds": round(time.monotonic() - started, 3),
            "path": str(backup), "bytes": backup_size,
        }
        if proc.returncode != 0 or not backup.is_file() or backup.stat().st_size == 0:
            entry["status"] = "failed"
            entry["error_tail"] = stderr[-4000:]
            event["steps"].append(entry)
            with contextlib.suppress(FileNotFoundError):
                backup.unlink()
            raise CommandFailure("pg_dump_backup", proc.returncode or 1, stderr or "backup file was empty")
        entry["status"] = "ok"
        digest = hashlib.sha256()
        with backup.open("rb") as saved:
            for chunk in iter(lambda: saved.read(1024 * 1024), b""):
                digest.update(chunk)
        entry["sha256"] = digest.hexdigest()
        event["backup_sha256"] = digest.hexdigest()
        event["steps"].append(entry)
    except BaseException:
        with contextlib.suppress(FileNotFoundError):
            if backup.exists() and backup.stat().st_size == 0:
                backup.unlink()
        raise
    return backup


def compose_action(event: dict[str, Any], target: Path, env_file: Path, override: Path | None, env: dict[str, str], name: str, command: list[str]) -> None:
    step(event, name, compose_argv(target, env_file, override, command), env=env)


def current_migration(event: dict[str, Any], target: Path, env_file: Path, override: Path | None, env: dict[str, str]) -> tuple[list[str], str | None]:
    migrations = query_migrations(event, target, env_file, override, env)
    schema = latest_migration(migrations)
    event["schema_current"] = schema
    event["schema_migrations"] = migrations
    store_schema(target, migrations)
    return migrations, schema


def migration_decision(manifest: dict[str, Any], current: str | None) -> bool:
    schema = manifest["schema"]
    target = schema["target_migration"]
    if current == target:
        return False
    if current in schema["upgrade_from"]:
        if current is not None and current >= target:
            raise ReleaseError(f"cannot migrate backwards from schema {current} to {target}")
        return True
    if current in schema["compatible_migrations"]:
        return False
    raise ReleaseError(f"schema {current!r} is neither compatible nor an approved migration source for {manifest['release_id']}")


def schema_compatible(manifest: dict[str, Any], current: str | None) -> bool:
    return current in manifest["schema"]["compatible_migrations"]

def ensure_postgres_password_path(previous: dict[str, Any] | None, candidate: dict[str, Any]) -> None:
    if previous and previous["runtime"]["postgres_password_file"] != candidate["runtime"]["postgres_password_file"]:
        raise ReleaseError("POSTGRES_PASSWORD_FILE path is stable target state; rotate the database credential through a separate maintenance procedure")


def record_release_event(target: Path, event: dict[str, Any], error: Exception | None = None) -> None:
    event["finished_at"] = now()
    event["outcome"] = "failure" if error else "success"
    if error:
        event["error"] = str(error)
        event["failed_stage"] = event.get("stage")
    event.pop("stage", None)
    append_event(target, event)


def restore_previous(event: dict[str, Any], target: Path, previous: dict[str, Any] | None, previous_env: bytes | None, current_schema: str | None, override: Path | None, env: dict[str, str]) -> None:
    if previous is None or previous_env is None or not schema_compatible(previous, current_schema):
        event["recovery"] = "not attempted: no prior release or prior release is not compatible with the observed database schema"
        return
    if "steamclient_file" not in previous["runtime"]:
        event["recovery"] = "not attempted: legacy guest/UDP release cannot serve Steam-only Playtest clients"
        return
    event["stage"] = "restore_previous_release"
    atomic_write(target / "release.env", previous_env)
    compose_action(event, target, target / "release.env", override, env, "restore_previous_services", ["up", "-d", "caddy", "master", "game"])
    wait_ready(event, target, target / "release.env", override, env)
    event["recovery"] = {"status": "restored", "release_id": previous["release_id"]}


def deploy(event: dict[str, Any], args: argparse.Namespace, target: Path, manifest: dict[str, Any], env: dict[str, str]) -> None:
    override = args.compose_override
    previous = active_manifest(target, args.allow_disposable_host)
    ensure_postgres_password_path(previous, manifest)
    previous_env_path = target / "release.env"
    previous_env = previous_env_path.read_bytes() if previous_env_path.is_file() else None
    pending = target / f".release.env.{event['event_id']}"
    atomic_write(pending, release_env(manifest))
    disrupted = False
    migration_attempted = False
    migration_verified = False
    current_schema: str | None = None
    try:
        prepare_release(event, target, manifest, pending, override, env, args.allow_local_image_digests)
        save_manifest(target, manifest)
        # Use the currently selected env for the DB if it exists; the candidate is used on a fresh target.
        db_env = previous_env_path if previous_env is not None else pending
        ensure_postgres(event, target, db_env, override, env)
        _, current_schema = current_migration(event, target, db_env, override, env)
        event["stage"] = "schema_compatibility"
        needs_migration = migration_decision(manifest, current_schema)
        if not needs_migration and not schema_compatible(manifest, current_schema):
            raise ReleaseError(f"release {manifest['release_id']} does not support current schema {current_schema!r}")
        capture_logs(event, target, db_env, override, env)
        if previous is not None:
            event["stage"] = "idle_registration"
            require_idle_registration(target, db_env, override, env)
        disrupted = True
        compose_action(event, target, db_env, override, env, "stop_writers", ["stop", "-t", "30", "master", "game"])
        if needs_migration:
            backup = backup_database(event, target, db_env, override, env)
            event["backup"] = str(backup)
            migration_attempted = True
            os.replace(pending, target / "release.env")
            compose_action(event, target, target / "release.env", override, env, "run_schema_migration", ["--profile", "migration", "run", "--rm", "--no-deps", "migrate", manifest["schema"]["target_migration"]])
            _, current_schema = current_migration(event, target, target / "release.env", override, env)
            if current_schema != manifest["schema"]["target_migration"]:
                raise ReleaseError(f"migration completed but database is at {current_schema!r}, expected {manifest['schema']['target_migration']!r}")
            migration_verified = True
        else:
            os.replace(pending, target / "release.env")
        event["schema_after"] = current_schema
        compose_action(event, target, target / "release.env", override, env, "start_release_services", ["up", "-d", "caddy", "master", "game"])
        wait_ready(event, target, target / "release.env", override, env)
        save_active(target, manifest, current_schema)
    except Exception as exc:
        failed_stage = event.get("stage")
        event["failure_during_migration"] = migration_attempted and not migration_verified
        if migration_attempted and not migration_verified and (target / "release.env").is_file():
            try:
                _, current_schema = current_migration(event, target, target / "release.env", override, env)
                event["schema_after"] = current_schema
            except Exception as schema_exc:
                event["schema_observation_error"] = str(schema_exc)
        if disrupted and (not migration_attempted or migration_verified):
            try:
                restore_previous(event, target, previous, previous_env, current_schema, override, env)
            except Exception as recovery_exc:
                event["recovery"] = {"status": "failed", "error": str(recovery_exc)}
        event["stage"] = failed_stage
        raise
    finally:
        with contextlib.suppress(FileNotFoundError):
            pending.unlink()


def rollback(event: dict[str, Any], args: argparse.Namespace, target: Path, env: dict[str, str]) -> None:
    release = args.release_id
    if not isinstance(release, str) or not TOKEN.fullmatch(release):
        raise ReleaseError("rollback requires a valid --release-id")
    path = target / "releases" / f"{release}.json"
    if not path.is_file():
        raise ReleaseError(f"no recorded release {release!r} exists in {target / 'releases'}")
    recorded = parse_json(path)
    if "steamclient_file" not in recorded.get("runtime", {}):
        raise ReleaseError("Steam-only Compose cannot roll back to a legacy raw-UDP release; restore the database through an explicit recovery procedure")
    manifest = validate_manifest(recorded, args.allow_disposable_host)
    validate_dns(event, manifest, args.allow_disposable_host)
    previous = active_manifest(target, args.allow_disposable_host)
    ensure_postgres_password_path(previous, manifest)
    if previous and previous["release_id"] == release:
        raise ReleaseError(f"release {release!r} is already active")
    override = args.compose_override
    candidate = target / f".release.env.{event['event_id']}"
    atomic_write(candidate, release_env(manifest))
    active_env = target / "release.env"
    previous_env = active_env.read_bytes() if active_env.is_file() else None
    disrupted = False
    current_schema: str | None = None
    try:
        prepare_release(event, target, manifest, candidate, override, env, args.allow_local_image_digests)
        db_env = active_env if previous_env is not None else candidate
        ensure_postgres(event, target, db_env, override, env)
        _, current_schema = current_migration(event, target, db_env, override, env)
        event["stage"] = "schema_compatibility"
        if not schema_compatible(manifest, current_schema):
            raise ReleaseError(f"rollback to {release} is incompatible with current schema {current_schema!r}; restore the database separately")
        capture_logs(event, target, db_env, override, env)
        if previous is not None:
            require_idle_registration(target, db_env, override, env)
        disrupted = True
        compose_action(event, target, db_env, override, env, "stop_writers", ["stop", "-t", "30", "master", "game"])
        os.replace(candidate, active_env)
        compose_action(event, target, active_env, override, env, "start_rollback_services", ["up", "-d", "caddy", "master", "game"])
        wait_ready(event, target, active_env, override, env)
        save_active(target, manifest, current_schema)
        event["schema_after"] = current_schema
    except Exception:
        failed_stage = event.get("stage")
        if disrupted:
            try:
                restore_previous(event, target, previous, previous_env, current_schema, override, env)
            except Exception as recovery_exc:
                event["recovery"] = {"status": "failed", "error": str(recovery_exc)}
        event["stage"] = failed_stage
        raise
    finally:
        with contextlib.suppress(FileNotFoundError):
            candidate.unlink()


def with_lock(target: Path):
    private_dir(target)
    lock = (target / ".release.lock").open("a")
    try:
        fcntl.flock(lock.fileno(), fcntl.LOCK_EX)
    except BaseException:
        lock.close()
        raise
    return lock


def target_path(raw: str) -> Path:
    path = Path(raw)
    if not path.is_absolute():
        raise ReleaseError("--target-dir must be an explicit absolute path")
    resolved = path.resolve()
    if is_inside(resolved, ROOT):
        raise ReleaseError(f"target state must stay outside the repository: {resolved}")
    return resolved


def configure_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    deploy_parser = commands.add_parser("deploy", help="validate and deploy a pinned release")
    deploy_parser.add_argument("--target-dir", required=True, help="absolute, local state/backup directory outside this repository")
    deploy_parser.add_argument("--config", required=True, type=Path, help="release JSON manifest")
    rollback_parser = commands.add_parser("rollback", help="restore a recorded schema-compatible application pair")
    rollback_parser.add_argument("--target-dir", required=True, help="absolute, local state directory")
    rollback_parser.add_argument("--release-id", required=True)
    status_parser = commands.add_parser("status", help="show recorded release and local Compose status")
    status_parser.add_argument("--target-dir", required=True)
    logs_parser = commands.add_parser("logs", help="show a bounded tail of a selected service")
    logs_parser.add_argument("--target-dir", required=True)
    logs_parser.add_argument("--tail", type=int, default=100)
    logs_parser.add_argument("--service", choices=("caddy", "postgres", "master", "game"),
                             default="master", help="Compose service whose recent logs to view")
    for sub in (deploy_parser, rollback_parser, status_parser, logs_parser):
        sub.add_argument("--compose-override", type=Path, help="disposable-only Compose ports override")
        sub.add_argument("--allow-disposable-host", action="store_true", help="explicit isolated smoke mode; requires loopback override and firewall fixture")
        sub.add_argument("--firewall-helper", type=Path, help="explicit firewall preflight binary (fixtures only outside production)")
        sub.add_argument("--allow-local-image-digests", action="store_true", help="accept local image-ID pseudo digests only in disposable fixture mode")
    return parser


def disk_usage(path: Path) -> dict[str, int | str]:
    filesystem = os.statvfs(path)
    block = filesystem.f_frsize
    return {
        "path": str(path),
        "total_bytes": filesystem.f_blocks * block,
        "used_bytes": (filesystem.f_blocks - filesystem.f_bfree) * block,
        "available_bytes": filesystem.f_bavail * block,
    }


def service_probe(target: Path, env_file: Path, override: Path | None, env: dict[str, str],
                  service: str, port: int, route: str) -> bool:
    script = (
        f'exec 3<>/dev/tcp/127.0.0.1/{port}; '
        f'printf "GET {route} HTTP/1.0\\r\\nHost: localhost\\r\\nConnection: close\\r\\n\\r\\n" >&3; '
        'IFS= read -r status <&3; [[ "$status" == *" 200 "* ]]'
    )
    try:
        proc = subprocess.run(compose_argv(target, env_file, override, ["exec", "-T", service, "bash", "-ec", script]),
                              env=env, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=5)
        return proc.returncode == 0
    except subprocess.TimeoutExpired:
        return False


def registration_status(target: Path, env_file: Path, override: Path | None, env: dict[str, str]) -> dict[str, Any]:
    sql = (
        'SELECT COALESCE(json_agg(json_build_object('
        '\'id\', "Id", \'current_matches\', "CurrentMatches", '
        '\'last_heartbeat\', "LastHeartbeat", '
        '\'fresh\', "LastHeartbeat" > now() - interval \'15 seconds\')), \'[]\'::json) '
        'FROM "GameServers";'
    )
    argv = compose_argv(target, env_file, override,
                        ["exec", "-T", "postgres", "psql", "-U", "sloparena", "-d", "sloparena",
                         "-A", "-t", "-v", "ON_ERROR_STOP=1", "-c", sql])
    try:
        proc = subprocess.run(argv, env=env, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, timeout=5)
        if proc.returncode:
            return {"available": False}
        rows = json.loads(proc.stdout.decode().strip())
        return {"available": True, "hosts": rows,
                "registered": any(host["fresh"] for host in rows),
                "active_matches": sum(host["current_matches"] for host in rows if host["fresh"])}
    except (subprocess.TimeoutExpired, UnicodeError, ValueError, KeyError, TypeError):
        return {"available": False}

def require_idle_registration(target: Path, env_file: Path, override: Path | None, env: dict[str, str]) -> dict[str, Any]:
    status = registration_status(target, env_file, override, env)
    if not status.get("available"):
        raise ReleaseError("cannot verify fresh GameServer registration before replacement")
    if status.get("registered") is not True:
        raise ReleaseError("replacement requires a fresh GameServer registration")
    active_matches = status.get("active_matches")
    if type(active_matches) is not int or active_matches < 0:
        raise ReleaseError("cannot verify active match count before replacement")
    if active_matches != 0:
        raise ReleaseError("replacement requires zero active matches")
    return status

def status_or_logs(args: argparse.Namespace, target: Path, env: dict[str, str]) -> None:
    active = active_manifest(target, args.allow_disposable_host)
    env_file = target / "release.env"
    if args.command == "status":
        result: dict[str, Any] = {
            "active_release": active["release_id"] if active else None,
            "images": active["images"] if active else None,
            "source_revisions": active["source_revisions"] if active else None,
            "disk": {"root": disk_usage(Path("/")), "state": disk_usage(target)},
        }
        docker_root = Path(step_for_readonly(["docker", "info", "--format", "{{.DockerRootDir}}"], env).strip())
        result["disk"]["docker_data"] = disk_usage(docker_root)
        schema_path = target / "schema.json"
        if schema_path.exists():
            result["schema"] = parse_json(schema_path)
        latest = target / "last-offhost-backup.json"
        result["last_offhost_backup"] = parse_json(latest) if latest.exists() else None
        attempt = target / "last-backup-attempt.json"
        result["last_backup_attempt"] = parse_json(attempt) if attempt.exists() else None
        if env_file.is_file():
            output = step_for_readonly(compose_argv(target, env_file, args.compose_override, ["ps", "--all", "--format", "json"]), env)
            result["compose"] = [
                {key: row.get(key) for key in ("Service", "Image", "State", "Health", "Ports")}
                for row in (json.loads(line) for line in output.splitlines() if line.strip())
            ]
            result["services"] = {
                service: {
                    "liveness": service_probe(target, env_file, args.compose_override, env, service, port, "/health"),
                    "readiness": service_probe(target, env_file, args.compose_override, env, service, port, "/ready"),
                }
                for service, port in (("master", 8080), ("game", 7777))
            }
            result["registration"] = registration_status(target, env_file, args.compose_override, env)
        print(json.dumps(result, indent=2, sort_keys=True))
        return
    if not 1 <= args.tail <= MAX_PRE_REPLACEMENT_LOG_LINES:
        raise ReleaseError(f"--tail must be from 1 to {MAX_PRE_REPLACEMENT_LOG_LINES}")
    if not env_file.is_file():
        raise ReleaseError("no release.env exists in target state")
    command = compose_argv(target, env_file, args.compose_override, ["logs", "--no-color", "--tail", str(args.tail), args.service])
    proc = subprocess.run(command, env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, check=False)
    lines: list[bytes] = []
    size = 0
    truncated = False
    for raw_line in proc.stdout.splitlines(keepends=True):
        line = safe_log_line(raw_line)
        if size + len(line) <= MAX_LOG_BYTES:
            lines.append(line)
            size += len(line)
        else:
            truncated = True
    output = b"".join(lines).decode("utf-8", errors="replace")
    print(output, end="" if output.endswith("\n") or not output else "\n")
    if truncated:
        print("[output truncated at 1 MiB]")
    if proc.returncode != 0:
        raise CommandFailure("logs", proc.returncode, output[-4000:])


def step_for_readonly(argv: list[str], env: dict[str, str]) -> str:
    proc = subprocess.run(argv, env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, check=False)
    output = proc.stdout.decode("utf-8", errors="replace")
    if proc.returncode:
        raise CommandFailure("compose_status", proc.returncode, output[-4000:])
    return output


def main() -> int:
    args = configure_parser().parse_args()
    try:
        target = target_path(args.target_dir)
        if args.compose_override is not None:
            args.compose_override = args.compose_override.resolve(strict=True)
            if not args.compose_override.is_file():
                raise ReleaseError("--compose-override must be a file")
        if args.firewall_helper is not None:
            args.firewall_helper = args.firewall_helper.resolve(strict=True)
        env = docker_env(target)
        with with_lock(target):
            if args.command in {"status", "logs"}:
                check_test_scope(args)
                ensure_local_docker({"steps": []}, env)
                status_or_logs(args, target, env)
                return 0
            event: dict[str, Any] = {
                "event_id": uuid.uuid4().hex,
                "operation": args.command,
                "started_at": now(),
                "steps": [],
            }
            error: Exception | None = None
            try:
                preflight(event, args, env)
                if args.command == "deploy":
                    manifest = load_manifest(args.config.resolve(strict=True), args.allow_disposable_host)
                    event["release_id"] = manifest["release_id"]
                    validate_dns(event, manifest, args.allow_disposable_host)
                    deploy(event, args, target, manifest, env)
                else:
                    event["release_id"] = args.release_id
                    rollback(event, args, target, env)
                print(json.dumps({"outcome": "success", "release_id": event.get("release_id"), "event_id": event["event_id"], "schema": event.get("schema_after", event.get("schema_current"))}, indent=2))
            except Exception as exc:
                error = exc
                print(f"release {args.command} failed: {exc}", file=sys.stderr)
            try:
                record_release_event(target, event, error)
            except OSError as exc:
                print(f"failed to persist release history: {exc}", file=sys.stderr)
                return 1
            return 1 if error else 0
    except (ReleaseError, OSError, subprocess.SubprocessError) as exc:
        print(f"release {getattr(args, 'command', 'operation')} failed: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
