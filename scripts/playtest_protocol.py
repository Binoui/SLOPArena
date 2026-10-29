"""Check Steam admission compatibility from the two immutable source revisions."""
from __future__ import annotations

import base64
import re
from urllib.parse import quote

GAME_FILES = ("src/Shared/SimulationProtocol.cs", "src/Shared/SteamMatchDescriptor.cs")
MASTER_FILE = "Lobbies/LobbyModels.cs"
GAME_REPOSITORY = "Binoui/SlopArena"
MASTER_REPOSITORY = "Binoui/SlopArena-MasterServer"
GAME_DECLARATION = r"\bpublic\s+const\s+byte\s+Version"
RELEASE_TAG_PREFIX = "playtest-client-"
# Any Shared change may alter the simulation; a needless bump only makes players update.
SIMULATION_PATHS = ("src/Shared",)


def protocol_value(text: str, declaration: str) -> int:
    text = re.sub(r"/\*.*?\*/|//[^\n]*", "", text, flags=re.DOTALL)
    values = re.findall(declaration + r"\s*=\s*(0x[0-9a-fA-F]+|[0-9]+)\s*;", text)
    if len(values) != 1:
        raise ValueError("Protocol declaration is missing, ambiguous or no longer a literal; update the release check")
    value = int(values[0], 16 if values[0].startswith("0x") else 10)
    if not 1 <= value <= 255:
        raise ValueError("Protocol must be a positive byte value")
    return value


def verify_sources(game: dict[str, str], master: str, game_revision: str, master_revision: str) -> dict:
    game_protocol = protocol_value(game[GAME_FILES[0]], GAME_DECLARATION)
    alias = re.sub(r"/\*.*?\*/|//[^\n]*", "", game[GAME_FILES[1]], flags=re.DOTALL)
    if len(re.findall(r"\bpublic\s+const\s+int\s+CurrentProtocolVersion\s*=\s*SimulationProtocol\.Version\s*;", alias)) != 1:
        raise ValueError("Steam admission no longer delegates to SimulationProtocol.Version; update the release check")
    master_protocol = protocol_value(master, r"\bpublic\s+const\s+int\s+Protocol")
    if game_protocol != master_protocol:
        raise ValueError(f"Incompatible pinned sources: game protocol {game_protocol}, Master protocol {master_protocol}")
    return {"source_revisions": {"gameplay": game_revision, "master": master_revision},
            "game_protocol": game_protocol, "master_protocol": master_protocol}


def verify_release_bump(previous_tag: str, previous_protocol: int, protocol: int, simulation_changed: bool) -> None:
    """The protocol moves once per deploy, and only forward. Development changes do not bump it."""
    if protocol < previous_protocol:
        raise ValueError(f"Protocol {protocol} is lower than {previous_tag}'s {previous_protocol}")
    if simulation_changed and protocol == previous_protocol:
        raise ValueError(f"src/Shared changed since {previous_tag} but the protocol is still {protocol}; "
                         f"bump SimulationProtocol.Version and the Master Protocol to {protocol + 1}")


def github_source(gh_json, repository: str, path: str, revision: str) -> str:
    response = gh_json(f"repos/{repository}/contents/{quote(path, safe='/')}?ref={revision}")
    if (response.get("type") != "file" or response.get("encoding") != "base64"
            or not isinstance(response.get("content"), str) or len(response["content"]) > 1_000_000):
        raise ValueError("Cannot inspect pinned protocol source")
    return base64.b64decode(response["content"].replace("\n", ""), validate=True).decode("utf-8")


def verify_github(gh_json, game_revision: str, master_revision: str) -> dict:
    game = {path: github_source(gh_json, GAME_REPOSITORY, path, game_revision) for path in GAME_FILES}
    master = github_source(gh_json, MASTER_REPOSITORY, MASTER_FILE, master_revision)
    return verify_sources(game, master, game_revision, master_revision)
