import hashlib
import json
import tempfile
import unittest
from datetime import datetime as DateTime, timezone
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch

import recovery
import release


class ImageProvenanceTests(unittest.TestCase):
    reference = "ghcr.io/binoui/sloparena-gameserver@sha256:" + "a" * 64

    def image(self):
        return {
            "RepoDigests": [self.reference],
            "Config": {"Labels": {
                "org.opencontainers.image.revision": "revision",
                "org.opencontainers.image.version": "release",
            }},
        }

    def test_registry_refusal_blocks_even_matching_local_image_metadata(self):
        def registry_refusal(event, name, argv, **kwargs):
            if argv[:3] == ["docker", "image", "inspect"]:
                return json.dumps([self.image()])
            raise release.CommandFailure(name, 1, "manifest unknown")

        with patch.object(release, "step", side_effect=registry_refusal):
            with self.assertRaisesRegex(release.CommandFailure, "manifest unknown"):
                release.inspect_image({}, self.reference, "revision", "release", {})

    def test_pulled_image_must_match_digest_revision_and_release(self):
        for mismatch in ("digest", "revision", "release"):
            with self.subTest(mismatch=mismatch):
                image = self.image()
                if mismatch == "digest":
                    image["RepoDigests"] = [self.reference[:-1] + "b"]
                else:
                    label = "revision" if mismatch == "revision" else "version"
                    image["Config"]["Labels"]["org.opencontainers.image." + label] = "wrong"

                def pulled_image(event, name, argv, **kwargs):
                    return json.dumps([image]) if argv[:3] == ["docker", "image", "inspect"] else ""

                with patch.object(release, "step", side_effect=pulled_image):
                    with self.assertRaises(release.ReleaseError):
                        release.inspect_image({}, self.reference, "revision", "release", {})


class PublishedPortsTests(unittest.TestCase):
    @staticmethod
    def config():
        def port(host, published, target, protocol):
            return {
                "host_ip": host,
                "published": str(published),
                "target": target,
                "protocol": protocol,
            }

        return {
            "services": {
                "caddy": {"ports": [port("0.0.0.0", 80, 80, "tcp"), port("0.0.0.0", 443, 443, "tcp")]},
                "game": {},
                "master": {},
                "postgres": {},
                "migrate": {},
            }
        }

    def test_public_profile_admits_only_https_without_gameplay_ports(self):
        release.check_published_ports(self.config(), disposable=False)

    def test_raw_master_or_any_gameplay_port_blocks_release(self):
        config = self.config()
        config["services"]["master"]["ports"] = [{"published": "8080", "target": 8080, "protocol": "tcp"}]
        with self.assertRaises(release.ReleaseError):
            release.check_published_ports(config, disposable=False)

        config = self.config()
        config["services"]["game"]["ports"] = [
            {"host_ip": "0.0.0.0", "published": "7777", "target": 7777, "protocol": "udp"}
        ]
        with self.assertRaises(release.ReleaseError):
            release.check_published_ports(config, disposable=False)

    def test_disposable_profile_rejects_public_binding(self):
        config = self.config()
        for binding in config["services"]["caddy"]["ports"]:
            binding["host_ip"] = "127.0.0.1"
            binding["published"] = str(int(binding["published"]) + 18000)
        release.check_published_ports(config, disposable=True)
        config["services"]["caddy"]["ports"][1]["host_ip"] = "0.0.0.0"
        with self.assertRaises(release.ReleaseError):
            release.check_published_ports(config, disposable=True)


class SteamRuntimeManifestTests(unittest.TestCase):
    def test_runtime_checksum_and_private_publisher_key_gate_release(self):
        with tempfile.TemporaryDirectory() as tmp:
            private = Path(tmp)
            manifest = json.loads(Path(__file__).with_name("release.example.json").read_text())
            runtime = manifest["runtime"]
            for key in ("master_env_file", "migration_env_file", "game_config_file",
                        "postgres_password_file", "steamclient_file"):
                path = private / key
                path.write_bytes(b"test-runtime")
                runtime[key] = str(path)
            (private / "master_env_file").write_text("Steam__ApiKey=test-publisher-key\n")
            (private / "master_env_file").chmod(0o600)
            digest = hashlib.sha256(b"test-runtime").hexdigest()
            runtime["steamclient_sha256"] = digest
            self.assertEqual(digest, release.validate_manifest(manifest)["runtime"]["steamclient_sha256"])

            runtime["steamclient_sha256"] = "0" * 64
            with self.assertRaisesRegex(release.ReleaseError, "checksum"):
                release.validate_manifest(manifest)
            runtime["steamclient_sha256"] = digest
            (private / "master_env_file").write_text("Jwt__Secret=test-only\n")
            with self.assertRaisesRegex(release.ReleaseError, "Steam__ApiKey"):
                release.validate_manifest(manifest)


    def test_legacy_active_record_is_readable_but_cannot_be_new_release_or_rollback(self):
        with tempfile.TemporaryDirectory() as tmp:
            target = Path(tmp)
            manifest = json.loads(Path(__file__).with_name("release.example.json").read_text())
            manifest["release_id"] = "legacy-udp"
            manifest["runtime"].pop("steamclient_file")
            manifest["runtime"].pop("steamclient_sha256")
            for key in ("master_env_file", "migration_env_file", "game_config_file", "postgres_password_file"):
                path = target / key
                path.write_text("legacy-test\n")
                manifest["runtime"][key] = str(path)
            releases = target / "releases"
            releases.mkdir()
            (releases / "legacy-udp.json").write_text(json.dumps(manifest))
            (target / "active.json").write_text(json.dumps({"release_id": "legacy-udp"}))
            self.assertEqual("legacy-udp", release.active_manifest(target)["release_id"])
            with self.assertRaises(release.ReleaseError):
                release.validate_manifest(manifest)
            args = SimpleNamespace(release_id="legacy-udp", allow_disposable_host=False)
            with self.assertRaisesRegex(release.ReleaseError, "legacy raw-UDP release"):
                release.rollback({}, args, target, {})
            event = {}
            release.restore_previous(event, target, release.active_manifest(target), b"old-env",
                                     manifest["schema"]["target_migration"], None, {})
            self.assertIn("legacy guest/UDP", event["recovery"])
            self.assertFalse((target / "release.env").exists())


class BackupRetentionTests(unittest.TestCase):
    def test_backup_keeps_newest_three_daily_pairs_and_ignores_pre_migration_files(self):
        with tempfile.TemporaryDirectory() as tmp:
            target = Path(tmp)
            (target / "release.env").touch()
            backups = target / "backups"
            backups.mkdir(mode=0o700)
            legacy = backups / f"20250101T000000Z-{'0' * 32}.dump"
            legacy.write_bytes(b"old daily")
            legacy.with_name(legacy.name + ".json").write_text("{}")
            older_legacy = backups / f"20250102T000000Z-{'0' * 31}1.dump"
            older_legacy.write_bytes(b"old daily")
            older_legacy.with_name(older_legacy.name + ".json").write_text("{}")
            premigration = backups / "pg-before-migration-release-001.dump"
            premigration.write_bytes(b"pre-migration")
            premigration.with_name(premigration.name + ".json").write_text("{}")

            class Clock:
                current = 0

                @classmethod
                def now(cls, tz):
                    value = DateTime(2026, 1, 1, tzinfo=timezone.utc).replace(second=cls.current)
                    cls.current += 1
                    return value

            uploads = {}

            def command(argv, *, stdout=None, env=None):
                stdout.write(f"dump-{Clock.current}".encode())
                return b""

            def s3(method, key, credentials, storage, file=None, *, verify_upload=False):
                if method == "PUT":
                    uploads[key] = file.read_bytes()
                elif method == "GET":
                    file.write_bytes(uploads[key])
                return b""

            credentials = target / "credentials"
            credentials.touch()
            with (
                patch.object(release, "active_manifest", return_value={"release_id": "test", "images": {}}),
                patch.object(release, "query_migrations", return_value=[]),
                patch.object(recovery, "command", side_effect=command),
                patch.object(recovery, "s3", side_effect=s3),
                patch.object(recovery, "datetime", Clock),
                patch.object(recovery.uuid, "uuid4", side_effect=[
                    SimpleNamespace(hex=f"{index:032x}") for index in range(20)
                ]),
            ):
                manifests = [recovery.backup(target, credentials, {"bucket": "test", "endpoint": "https://storage.example", "region": "test"}) for _ in range(5)]

            self.assertEqual([manifest["key"] for manifest in manifests], [
                f"postgres/20260101T00000{second}Z-{second * 3:032x}.dump"
                for second in range(5)
            ])

            daily = backups / "daily"
            surviving = sorted(daily.glob("*.dump"))
            self.assertEqual([path.name for path in surviving], [
                f"20260101T00000{second}Z-{second * 3:032x}.dump"
                for second in (2, 3, 4)
            ])
            self.assertEqual(len(uploads), 10)
            self.assertEqual(sorted(path.name for path in daily.iterdir()),
                             sorted(name for path in surviving for name in (path.name, path.name + ".json")))
            self.assertTrue(premigration.is_file())
            self.assertTrue(premigration.with_name(premigration.name + ".json").is_file())
            for old in (legacy, older_legacy):
                self.assertFalse(old.exists())
                self.assertFalse(old.with_name(old.name + ".json").exists())

class StorageConfigurationTests(unittest.TestCase):
    def test_private_storage_config_selects_https_target_and_signing_region(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "storage.json"
            path.write_text(json.dumps({
                "bucket": "example-backups", "endpoint": "https://storage.example.test/",
                "region": "eu-test-1",
            }))
            path.chmod(0o600)
            config = recovery.load_storage(path)
            credentials = Path(tmp) / "curl.conf"
            credentials.write_text('user = "test:secret"')
            credentials.chmod(0o600)
            with patch.object(recovery, "command", return_value=b"") as command:
                recovery.s3("PUT", "postgres/test.dump", credentials, config, path)
            args = command.call_args.args[0]
            self.assertEqual("https://storage.example.test/example-backups/postgres/test.dump", args[-1])
            self.assertIn("aws:amz:eu-test-1:s3", args)

    def test_storage_config_rejects_public_file_or_non_https_endpoint(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "storage.json"
            path.write_text('{"bucket":"example-backups","endpoint":"https://storage.example.test","region":"eu-test-1"}')
            with self.assertRaises(release.ReleaseError):
                recovery.load_storage(path)
            path.chmod(0o600)
            path.write_text('{"bucket":"example-backups","endpoint":"http://storage.example.test","region":"eu-test-1"}')
            with self.assertRaises(release.ReleaseError):
                recovery.load_storage(path)


class RestoreSafetyTests(unittest.TestCase):
    def test_restore_rejects_archive_keys_that_escape_its_isolated_target(self):
        from pathlib import Path
        for key in ("postgres/..dump", "postgres/.dump", "postgres/../live.dump",
                    "postgres/../../live.dump", "other/live.dump"):
            with self.subTest(key=key), self.assertRaises(release.ReleaseError):
                recovery.restore(Path("/no-live-state"), Path("/no-credentials"), {}, key)


class LogPrivacyTests(unittest.TestCase):
    def test_proxy_outage_preserves_error_without_token_url_or_authorization(self):
        raw = (b'caddy-1  | {"logger":"http.log.error","msg":"dial tcp: connection refused",'
               b'"request":{"uri":"/lobby?access_token=secret-jwt",'
               b'"headers":{"Authorization":["Bearer secret-jwt"]}},"status":502}\n')
        safe = release.safe_log_line(raw)
        self.assertIn(b"connection refused", safe)
        self.assertIn(b'"status":502', safe)
        self.assertNotIn(b"secret-jwt", safe)
        self.assertNotIn(b'"request"', safe)

    def test_prior_master_request_and_token_lines_are_not_archived(self):
        self.assertEqual(b"", release.safe_log_line(
            b"master-1  | Request starting HTTP/1.1 GET https://example/lobby?access_token=secret\n"))
        self.assertEqual(b"", release.safe_log_line(
            b"master-1  | Authorization: Bearer secret\n"))
        self.assertIn(b"Registered", release.safe_log_line(b"game-1  | [Registration] Registered\n"))


if __name__ == "__main__":
    unittest.main()


class ReplayVolumeTests(unittest.TestCase):
    def test_installed_compose_and_named_volume_survive_release(self):
        compose = Path(release.COMPOSE).read_text()
        game = compose.split('\n  game:\n', 1)[1].split('\nsecrets:', 1)[0]
        self.assertIn('    read_only: true', game)
        self.assertIn('      - game_replays:/replays', game)
        self.assertIn('      SLOPARENA_REPLAY_DIR: /replays', game)
        self.assertIn('\n  game_replays:\n', compose.split('\nvolumes:', 1)[1])
        dockerfile = Path(release.COMPOSE).parents[1].parent / 'Dockerfile.gameserver'
        image = dockerfile.read_text()
        self.assertLess(image.index('RUN mkdir /replays && chown 1654:1654 /replays'), image.index('USER 1654:1654'))
        command = release.compose_argv(Path('/var/lib/sloparena'), Path('/private/release.env'), None,
                                       ['up', '-d', 'game'])
        self.assertEqual(command[command.index('-f') + 1], str(release.COMPOSE))
        self.assertNotIn('down', command)
