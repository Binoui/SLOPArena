import copy
import io
import json
import subprocess
import sys
import tempfile
import unittest
from datetime import datetime, timezone
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch

import ci_release
import release


def candidate():
    return {
        "release_id": "steam-playtest-20261006-1",
        "version": "0.2.0-playtest.6",
        "source_revisions": {"gameplay": "a" * 40, "master": "b" * 40},
        "images": {
            "gameplay": "ghcr.io/binoui/sloparena-gameserver@sha256:" + "1" * 64,
            "master": "ghcr.io/binoui/sloparena-masterserver@sha256:" + "2" * 64,
            "migration": "ghcr.io/binoui/sloparena-masterserver-migrations@sha256:" + "3" * 64,
        },
        "catalog_hash": "4" * 64,
        "schema": {
            "target_migration": "20260925000000_InitialSchema",
            "compatible_migrations": ["20260925000000_InitialSchema"],
            "upgrade_from": [],
        },
        "steam": {"app_id": 5325920, "depot_id": 5325921,
                  "build_id": 25702912, "manifest_id": "12345678901234567890"},
        "master_endpoint": "https://master-test.example.com",
    }


class CandidateValidationTests(unittest.TestCase):
    def test_accepts_only_the_bounded_public_candidate_shape(self):
        normalized = ci_release.validate_candidate(candidate())
        self.assertEqual(set(normalized), {
            "release_id", "version", "source_revisions", "images", "catalog_hash",
            "schema", "steam", "master_endpoint",
        })
        for mutation in (
            lambda value: value.update(runtime={"master_env_file": "/etc/passwd"}),
            lambda value: value.update(path="/tmp/candidate.json"),
            lambda value: value["images"].update(gameplay="ghcr.io/binoui/sloparena-gameserver:latest"),
            lambda value: value["schema"].update(upgrade_from=["../migration"]),
            lambda value: value["schema"].update(upgrade_from=[None]),
            lambda value: value["source_revisions"].update(gameplay="c" * 64),
            lambda value: value.update(master_endpoint="http://master-test.example.com"),
            lambda value: value["steam"].update(build_id=True),
            lambda value: value.update(version="../../unsafe"),
        ):
            value = candidate()
            mutation(value)
            with self.subTest(value=value), self.assertRaises(ci_release.CandidateError):
                ci_release.validate_candidate(value)

    def test_optional_admitted_arenas_roster_is_bounded_and_kept_in_order(self):
        supplied = candidate()
        supplied["admitted_arenas"] = ["table_manners", "splash_deck", "slop_pit"]
        normalized = ci_release.validate_candidate(supplied)
        self.assertEqual(normalized["admitted_arenas"], ["table_manners", "splash_deck", "slop_pit"])
        self.assertNotIn("admitted_arenas", ci_release.validate_candidate(candidate()))
        for arenas in ([], ["slop_pit", "slop_pit"], ["../etc"], ["Slop_Pit"], [None], "slop_pit",
                       ["a%d" % n for n in range(33)], ["x" * 65]):
            value = candidate()
            value["admitted_arenas"] = arenas
            with self.subTest(arenas=arenas), self.assertRaises(ci_release.CandidateError):
                ci_release.validate_candidate(value)

    def test_validate_only_cli_emits_normalized_public_candidate(self):
        script = Path(ci_release.__file__)
        supplied = candidate()
        result = subprocess.run(
            [sys.executable, "-I", str(script), "--validate-only"],
            input=json.dumps(supplied).encode(),
            stdout=subprocess.PIPE, stderr=subprocess.PIPE, check=False,
        )
        self.assertEqual(result.returncode, 0, result.stderr.decode())
        self.assertEqual(json.loads(result.stdout), ci_release.validate_candidate(supplied))

    def test_bounded_cli_rejects_unknown_runtime_field_without_host_side_effects(self):
        script = Path(ci_release.__file__)
        result = subprocess.run(
            [sys.executable, "-I", str(script)],
            input=b'{"runtime":{"master_env_file":"/etc/passwd"}}',
            stdout=subprocess.PIPE, stderr=subprocess.PIPE, check=False,
        )
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(result.stdout, b"")
        self.assertIn(b"candidate rejected", result.stderr)

    def test_json_duplicate_keys_and_oversized_input_are_rejected(self):
        with self.assertRaises(ci_release.CandidateError):
            ci_release.read_candidate(io.BytesIO(b'{"release_id":"a","release_id":"b"}'))
        with self.assertRaises(ci_release.CandidateError):
            ci_release.read_candidate(io.BytesIO(b" " * (ci_release.MAX_CANDIDATE_BYTES + 1)))


class CandidateStateTests(unittest.TestCase):
    def test_release_master_environment_changes_only_catalog_hash_and_preserves_metadata(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            source = root / "active-master.env"
            old_hash = b"a" * 64
            new_hash = b"b" * 64
            original = (b"Other__Value=a=b\r\nRoom__CatalogHash=" + old_hash +
                        b"\r\n# unchanged comment\r\n")
            source.write_bytes(original)
            source.chmod(0o400)
            before = source.stat()
            active = {"runtime": {"master_env_file": str(source)}}
            supplied = candidate()
            supplied["catalog_hash"] = new_hash.decode()

            output = ci_release.private_master_environment(active, root / "state", supplied)

            self.assertEqual(output.read_bytes(), original.replace(old_hash, new_hash))
            self.assertEqual(source.read_bytes(), original)
            after = output.stat()
            self.assertEqual((after.st_uid, after.st_gid, after.st_mode & 0o777),
                             (before.st_uid, before.st_gid, before.st_mode & 0o777))

    def test_release_master_environment_replaces_arena_admission_with_the_roster_in_place(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            source = root / "active-master.env"
            old_hash, new_hash = b"a" * 64, b"b" * 64
            original = (b"Other__Value=a=b\r\nRoom__CatalogHash=" + old_hash + b"\r\n"
                        b"Room__AdmittedArenas__0=slop_court\r\n# kept comment\r\n"
                        b"Room__AdmittedArenas__1=after_hours\r\nLast__Value=z\r\n")
            source.write_bytes(original)
            active = {"runtime": {"master_env_file": str(source)}}
            supplied = candidate()
            supplied["catalog_hash"] = new_hash.decode()
            supplied["admitted_arenas"] = ["table_manners", "slop_pit", "after_hours"]

            output = ci_release.private_master_environment(active, root / "state", supplied)

            self.assertEqual(output.read_bytes(),
                             b"Other__Value=a=b\r\nRoom__CatalogHash=" + new_hash + b"\r\n"
                             b"Room__AdmittedArenas__0=table_manners\r\nRoom__AdmittedArenas__1=slop_pit\r\n"
                             b"Room__AdmittedArenas__2=after_hours\r\n# kept comment\r\nLast__Value=z\r\n")
            self.assertEqual(source.read_bytes(), original)

    def test_release_master_environment_adds_arena_admission_after_the_catalog_hash(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            source = root / "active-master.env"
            source.write_bytes(b"Room__CatalogHash=" + b"a" * 64)
            supplied = candidate()
            supplied["admitted_arenas"] = ["slop_pit"]
            output = ci_release.private_master_environment({"runtime": {"master_env_file": str(source)}},
                                                           root / "state", supplied)
            self.assertEqual(output.read_bytes(),
                             b"Room__CatalogHash=" + supplied["catalog_hash"].encode() + b"\n"
                             b"Room__AdmittedArenas__0=slop_pit\n")

    def test_release_master_environment_refuses_unusual_arena_admission_lines(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            source = root / "active-master.env"
            source.write_bytes(b"Room__CatalogHash=" + b"a" * 64 + b"\nexport Room__AdmittedArenas__0=slop_pit\n")
            supplied = candidate()
            supplied["admitted_arenas"] = ["slop_pit"]
            with self.assertRaisesRegex(release.ReleaseError, "plain assignments"):
                ci_release.private_master_environment({"runtime": {"master_env_file": str(source)}},
                                                      root / "state", supplied)

    def test_active_profile_must_admit_exactly_the_release_roster(self):
        profile = {"Deployment__Profile": "vps", "Auth__Mode": "steam", "Steam__AppId": "5325920",
                   "Steam__Identity": "sloparena-playtest", "Room__CatalogHash": "4" * 64,
                   "Room__AdmittedArenas__0": "table_manners", "Room__AdmittedArenas__1": "slop_pit"}

        def check(environment, arenas):
            config = {"services": {"master": {"environment": environment}}}
            with patch.object(release, "image_config", return_value=config):
                ci_release.require_active_vps_profile({}, Path("/unused"), {}, {}, "4" * 64, arenas)

        check(profile, ["table_manners", "slop_pit"])
        check(profile, None)
        for environment, arenas in ((profile, ["table_manners"]), (profile, ["slop_pit", "table_manners"]),
                                    ({**profile, "Room__AdmittedArenas__2": "log_off"}, ["table_manners", "slop_pit"])):
            with self.subTest(arenas=arenas), self.assertRaises(release.ReleaseError):
                check(environment, arenas)

    def test_duplicate_candidate_id_accepts_identical_bytes_and_rejects_mismatch(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = ci_release.candidate_record(Path(tmp), candidate()["release_id"])
            accepted = ci_release.validate_candidate(candidate())
            contents = release.json_bytes(accepted)
            ci_release.save_immutable_record(path, contents)
            ci_release.save_immutable_record(path, contents)
            changed = copy.deepcopy(accepted)
            changed["steam"]["build_id"] += 1
            with self.assertRaisesRegex(release.ReleaseError, "different candidate contents"):
                ci_release.save_immutable_record(path, release.json_bytes(changed))
            self.assertEqual(path.read_bytes(), contents)

    def test_endpoint_must_match_active_runtime_before_config_preparation(self):
        supplied = ci_release.validate_candidate(candidate())
        ci_release.require_endpoint(supplied, {"runtime": {"master_test_host": "master-test.example.com"}})
        supplied["master_endpoint"] = "https://other.example.com"
        with self.assertRaisesRegex(release.ReleaseError, "does not match"):
            ci_release.require_endpoint(supplied, {"runtime": {"master_test_host": "master-test.example.com"}})


class FailureReceiptTests(unittest.TestCase):
    def test_receipt_reports_rollback_without_private_error_output(self):
        event = {"stage": "application_readiness", "schema_after": "20260925000000_InitialSchema",
                 "recovery": {"status": "restored", "release_id": "previous", "error": "private password"}}
        error = release.CommandFailure("probe", 1, "private password")
        with patch.object(release, "active_manifest", return_value={"release_id": "previous"}):
            receipt = ci_release.public_failure(candidate(), event, Path("/unused"), error)
        ci_release.validate_failure(receipt, candidate()["release_id"])
        self.assertEqual(receipt["reason_code"], "application_not_ready")
        self.assertEqual(receipt["active_release"], "previous")
        self.assertEqual(receipt["recovery"]["status"], "restored")
        self.assertNotIn("private password", json.dumps(receipt))

    def test_nonzero_receiver_exit_still_returns_sanitized_receipt(self):
        receipt = ci_release.public_failure(candidate(), {}, Path("/missing"), RuntimeError("secret"))
        with (patch.object(sys, "argv", ["ci_release.py"]),
              patch.object(ci_release, "read_candidate", return_value=candidate()),
              patch.object(ci_release, "deploy_candidate", side_effect=ci_release.DeploymentFailure(receipt)),
              patch("sys.stdout", new_callable=io.StringIO) as output,
              patch("sys.stderr", new_callable=io.StringIO)):
            code = ci_release.main()
        self.assertEqual(code, 1)
        self.assertEqual(json.loads(output.getvalue())["outcome"], "failure")
        self.assertNotIn("secret", output.getvalue())

    def test_failure_receipt_refuses_foreign_identity_and_private_fields(self):
        receipt = ci_release.public_failure(candidate(), {}, Path("/missing"), RuntimeError("secret"))
        with self.assertRaises(ci_release.CandidateError):
            ci_release.validate_failure(receipt, "another-release")
        receipt["private_log"] = "secret"
        with self.assertRaises(ci_release.CandidateError):
            ci_release.validate_failure(receipt, candidate()["release_id"])

    def test_unknown_stage_never_becomes_a_pre_replacement_proof(self):
        with patch.object(release, "active_manifest", return_value={"release_id": "previous"}):
            receipt = ci_release.public_failure(candidate(), {"stage": "private-unknown-stage"},
                                                Path("/unused"), RuntimeError("private detail"))
        self.assertEqual(receipt["failed_stage"], "unknown")
        self.assertNotIn("private", json.dumps(receipt))


class OffhostBackupTests(unittest.TestCase):
    def test_deployment_requires_fresh_success_for_current_active_release(self):
        with tempfile.TemporaryDirectory() as tmp:
            target = Path(tmp)
            started = datetime(2026, 10, 6, 12, 0, tzinfo=timezone.utc)
            active = {"release_id": "active-release", "images": {"master": "image"}}
            with self.assertRaises(release.ReleaseError):
                ci_release.require_fresh_backup(target, active, started)

            release.atomic_write(target / "last-backup-attempt.json", release.json_bytes({
                "timestamp": "2026-10-06T11:59:59+00:00", "result": "success",
            }))
            release.atomic_write(target / "last-offhost-backup.json", release.json_bytes({
                "timestamp": "2026-10-06T11:59:59+00:00", "release_id": "active-release",
                "images": active["images"], "key": "postgres/old.dump",
                "sha256": "a" * 64, "bytes": 20,
            }))
            with self.assertRaises(release.ReleaseError):
                ci_release.require_fresh_backup(target, active, started)

            now = "2026-10-06T12:00:01+00:00"
            release.atomic_write(target / "last-backup-attempt.json", release.json_bytes({
                "timestamp": now, "result": "success",
            }))
            release.atomic_write(target / "last-offhost-backup.json", release.json_bytes({
                "timestamp": now, "release_id": "active-release", "images": active["images"],
                "key": "postgres/fresh.dump", "sha256": "b" * 64, "bytes": 42,
            }))
            release.atomic_write(target / "last-backup-attempt.json", release.json_bytes({
                "timestamp": now, "result": "failed",
            }))
            with self.assertRaises(release.ReleaseError):
                ci_release.require_fresh_backup(target, active, started)
            release.atomic_write(target / "last-backup-attempt.json", release.json_bytes({
                "timestamp": now, "result": "success",
            }))
            result = ci_release.require_fresh_backup(target, active, started)
            self.assertEqual(result["key"], "postgres/fresh.dump")
            self.assertEqual(result["bytes"], 42)


class IdleReplacementGateTests(unittest.TestCase):
    def _attempt(self, operation, registration):
        manifest = json.loads(Path(__file__).with_name("release.example.json").read_text())
        manifest["release_id"] = "candidate-release"
        previous = copy.deepcopy(manifest)
        previous["release_id"] = "previous-release"
        with tempfile.TemporaryDirectory() as tmp:
            target = Path(tmp)
            target.mkdir(exist_ok=True)
            if operation == "rollback":
                releases = target / "releases"
                releases.mkdir()
                (releases / "candidate-release.json").write_text(json.dumps(manifest))
            args = SimpleNamespace(
                release_id="candidate-release", allow_disposable_host=False,
                compose_override=None, allow_local_image_digests=False,
            )
            event = {"event_id": "test-event", "steps": []}
            with (
                patch.object(release, "active_manifest", return_value=previous),
                patch.object(release, "ensure_postgres_password_path"),
                patch.object(release, "prepare_release"),
                patch.object(release, "save_manifest"),
                patch.object(release, "ensure_postgres"),
                patch.object(release, "current_migration", return_value=([], "20260925000000_InitialSchema")),
                patch.object(release, "migration_decision", return_value=False),
                patch.object(release, "schema_compatible", return_value=True),
                patch.object(release, "capture_logs"),
                patch.object(release, "registration_status", return_value=registration),
                patch.object(release, "validate_manifest", return_value=manifest),
                patch.object(release, "validate_dns"),
                patch.object(release, "compose_action") as compose_action,
            ):
                with self.assertRaises(release.ReleaseError):
                    if operation == "deploy":
                        release.deploy(event, args, target, manifest, {})
                    else:
                        release.rollback(event, args, target, {})
            self.assertFalse(any(call.args[5] == "stop_writers" for call in compose_action.call_args_list))

    def test_deploy_and_rollback_refuse_active_stale_and_unavailable_registration_before_stop(self):
        conditions = {
            "active": {"available": True, "registered": True, "active_matches": 1},
            "stale": {"available": True, "registered": False, "active_matches": 0},
            "unavailable": {"available": False},
        }
        for operation in ("deploy", "rollback"):
            for label, status in conditions.items():
                with self.subTest(operation=operation, status=label):
                    self._attempt(operation, status)

    def test_successful_rollback_selects_and_records_requested_release(self):
        manifest = json.loads(Path(__file__).with_name("release.example.json").read_text())
        manifest["release_id"] = "rollback-target"
        previous = copy.deepcopy(manifest)
        previous["release_id"] = "currently-active"
        with tempfile.TemporaryDirectory() as tmp:
            target = Path(tmp)
            (target / "release.env").write_bytes(b"old release environment\n")
            releases = target / "releases"
            releases.mkdir()
            (releases / "rollback-target.json").write_text(json.dumps(manifest))
            event = {"event_id": "rollback-event", "steps": []}
            args = SimpleNamespace(
                release_id="rollback-target", allow_disposable_host=False,
                compose_override=None, allow_local_image_digests=False,
            )
            selected_environment = []

            def compose_action(_event, current_target, _env_file, _override, _env, name, _command):
                if name == "start_rollback_services":
                    selected_environment.append((current_target / "release.env").read_bytes())

            with (
                patch.object(release, "active_manifest", return_value=previous),
                patch.object(release, "ensure_postgres_password_path"),
                patch.object(release, "validate_manifest", return_value=manifest),
                patch.object(release, "validate_dns"),
                patch.object(release, "prepare_release"),
                patch.object(release, "ensure_postgres"),
                patch.object(release, "current_migration", return_value=([], "20260925000000_InitialSchema")),
                patch.object(release, "schema_compatible", return_value=True),
                patch.object(release, "capture_logs"),
                patch.object(release, "registration_status", return_value={
                    "available": True, "registered": True, "active_matches": 0,
                }),
                patch.object(release, "compose_action", side_effect=compose_action),
                patch.object(release, "wait_ready"),
            ):
                release.rollback(event, args, target, {})

            expected = release.release_env(manifest)
            self.assertEqual(selected_environment, [expected])
            self.assertEqual((target / "release.env").read_bytes(), expected)
            self.assertEqual(release.parse_json(target / "active.json")["release_id"], "rollback-target")


    def test_failed_deploy_after_disruption_restores_previous_release(self):
        manifest = json.loads(Path(__file__).with_name("release.example.json").read_text())
        manifest["release_id"] = "candidate-release"
        previous = copy.deepcopy(manifest)
        previous["release_id"] = "previous-release"
        old_env = b"old private runtime selection\n"
        with tempfile.TemporaryDirectory() as tmp:
            target = Path(tmp)
            (target / "release.env").write_bytes(old_env)
            event = {"event_id": "failed-event", "steps": []}
            args = SimpleNamespace(
                allow_disposable_host=False, compose_override=None,
                allow_local_image_digests=False,
            )
            with (
                patch.object(release, "active_manifest", return_value=previous),
                patch.object(release, "ensure_postgres_password_path"),
                patch.object(release, "prepare_release"),
                patch.object(release, "save_manifest"),
                patch.object(release, "ensure_postgres"),
                patch.object(release, "current_migration", return_value=([], "20260925000000_InitialSchema")),
                patch.object(release, "migration_decision", return_value=False),
                patch.object(release, "schema_compatible", return_value=True),
                patch.object(release, "capture_logs"),
                patch.object(release, "registration_status", return_value={
                    "available": True, "registered": True, "active_matches": 0,
                }),
                patch.object(release, "compose_action"),
                patch.object(release, "wait_ready", side_effect=[RuntimeError("candidate failed"), None]),
            ):
                with self.assertRaisesRegex(RuntimeError, "candidate failed"):
                    release.deploy(event, args, target, manifest, {})
            self.assertEqual((target / "release.env").read_bytes(), old_env)
            self.assertEqual(event["recovery"]["status"], "restored")


if __name__ == "__main__":
    unittest.main()
