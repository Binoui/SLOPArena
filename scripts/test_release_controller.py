"""Release interruption, receipt identity and manual acceptance boundaries."""
import base64
import copy
import importlib.util
import io
import json
from pathlib import Path
import tarfile
import tempfile
from types import SimpleNamespace
import unittest
from unittest import mock

SPEC = importlib.util.spec_from_file_location("playtest_controller", Path(__file__).with_name("release.py"))
controller = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(controller)


def state():
    config = {"version": "0.2.0-playtest.42", "gameRevision": "a" * 40, "masterRevision": "b" * 40,
              "endpoint": controller.ENDPOINT, "migrationFrom": "", "licensedInputsHash": controller.digest({})}
    return {"schemaVersion": 1, "configuration": config, "configurationHash": controller.digest(config),
            "compatibility": {"source_revisions": {"gameplay": "a" * 40, "master": "b" * 40},
                              "game_protocol": 8, "master_protocol": 8},
            "steps": {name: {"status": "pending"} for name in controller.STEPS},
            "playerAcceptance": {"status": "not-exercised", "reports": []}}


def candidate():
    return {"release_id": "steam-playtest-42", "version": "0.2.0-playtest.42",
            "source_revisions": {"gameplay": "a" * 40, "master": "b" * 40},
            "images": {"gameplay": "ghcr.io/binoui/sloparena-gameserver@sha256:" + "1" * 64,
                       "master": "ghcr.io/binoui/sloparena-masterserver@sha256:" + "2" * 64,
                       "migration": "ghcr.io/binoui/sloparena-masterserver-migrations@sha256:" + "3" * 64},
            "catalog_hash": "c" * 64, "master_endpoint": controller.ENDPOINT,
            "schema": {"target_migration": "20260927000001_HashGameServerApiTokens",
                       "compatible_migrations": ["20260927000001_HashGameServerApiTokens"], "upgrade_from": []},
            "steam": {"app_id": 5325920, "depot_id": 5325921, "build_id": 42, "manifest_id": "99"}}


def run():
    return {"id": 42, "head_sha": "a" * 40, "html_url": "https://github.com/Binoui/SlopArena/actions/runs/42",
            "status": "completed", "conclusion": "success", "run_attempt": 1}


def appinfo(build="42", manifest="99"):
    return ('"5325920" { "depots" { "branches" { "public" { "buildid" "' + build +
            '" } "test" { "buildid" "123" } } "5325921" { "manifests" { "public" { "gid" "' +
            manifest + '" } } } } }')


class PrepareTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        self.master = self.root / "Master"
        self.master.mkdir()
        self.project = self.root / "client/Unity"
        self.project.mkdir(parents=True)
        self.path = self.root / "0.2.0-playtest.42/candidate.json"
        self.path.parent.mkdir()
        self.args = SimpleNamespace(master_root=self.master, game_revision="a" * 40, master_revision="b" * 40,
                                    local_unity_project=self.project, unity_editor=self.root / "6000.0.78f1/Editor/Unity",
                                    version="0.2.0-playtest.42", endpoint=controller.ENDPOINT, migration_from="")
        self.master_protocol = 8
        self.contract = 1
        self.inputs = {"client/Unity/Assets/rig.fbx": "d" * 64}
        patches = [
            mock.patch.object(controller, "main_revision", side_effect=lambda root: "b" * 40 if root == self.master else "a" * 40),
            mock.patch.object(controller, "git", side_effect=self.git),
            mock.patch.object(controller.producer, "check_editor", return_value=self.args.unity_editor),
            mock.patch.object(controller.producer, "licensed_inputs", side_effect=lambda _project: (self.root, self.inputs))]
        self.patches = [patch.start() for patch in patches]
        for patch in patches:
            self.addCleanup(patch.stop)

    def git(self, root, *arguments):
        if arguments == ("rev-parse", "--show-toplevel"):
            return str(root)
        if arguments == ("remote", "get-url", "origin"):
            repository = controller.playtest_protocol.MASTER_REPOSITORY if root == self.master else controller.REPOSITORY
            return "git@github.com:" + repository + ".git"
        if arguments[0] in ("merge-base", "cat-file"):
            return ""
        if arguments[0] == "show":
            path = arguments[1].split(":", 1)[1]
            values = {
                controller.playtest_protocol.GAME_FILES[0]: "public const byte Version = 8;",
                controller.playtest_protocol.GAME_FILES[1]: "public const int CurrentProtocolVersion = SimulationProtocol.Version;",
                controller.playtest_protocol.MASTER_FILE: f"public const int Protocol = {self.master_protocol};",
                "scripts/playtest-release.py": f"CONTROLLER_CONTRACT_VERSION = {self.contract}\n",
                "client/Unity/ProjectSettings/ProjectVersion.txt": "m_EditorVersion: 6000.0.78f1",
                "content-cooked/roster/manifest.json": '{"entries":[{"packageId":"fightguy"}]}'}
            return values[path]
        raise AssertionError(arguments)

    def test_prepare_captures_exact_sources_and_inputs_without_build_or_publication(self):
        with mock.patch.object(controller, "command") as mutation:
            prepared = controller.prepare(self.args, self.root, self.path)
            mutation.assert_not_called()
        self.assertEqual(prepared["configuration"]["gameRevision"], "a" * 40)
        self.assertEqual(prepared["configuration"]["masterRevision"], "b" * 40)
        self.assertEqual(prepared["configuration"]["licensedInputsHash"], controller.digest(self.inputs))
        self.assertEqual(prepared["steps"]["build"]["status"], "pending")
        self.assertEqual(controller.read_state(self.path), prepared)

    def test_pair_mismatch_refuses_before_unity_or_licensed_input_work(self):
        self.master_protocol = 7
        with self.assertRaisesRegex(ValueError, "game protocol 8, Master protocol 7"):
            controller.prepare(self.args, self.root, self.path)
        self.patches[2].assert_not_called()
        self.patches[3].assert_not_called()
        self.assertFalse(self.path.exists())

    def test_old_pipeline_refuses_before_expensive_local_work(self):
        self.contract = 0
        with self.assertRaisesRegex(controller.ControllerError, "publish the tooling first"):
            controller.prepare(self.args, self.root, self.path)
        self.patches[2].assert_not_called()

    def test_changed_input_snapshot_does_not_overwrite_prepared_version(self):
        controller.prepare(self.args, self.root, self.path)
        before = self.path.read_bytes()
        self.inputs = {"client/Unity/Assets/rig.fbx": "e" * 64}
        with self.assertRaisesRegex(controller.ControllerError, "another snapshot"):
            controller.prepare(self.args, self.root, self.path)
        self.assertEqual(self.path.read_bytes(), before)

    def test_unpublished_game_snapshot_cannot_be_prepared(self):
        self.args.game_revision = "e" * 40
        with self.assertRaisesRegex(controller.ControllerError, "published main"):
            controller.prepare(self.args, self.root, self.path)
        self.assertFalse(self.path.exists())


class ControllerTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        self.path = self.root / "0.2.0-playtest.42/candidate.json"
        self.path.parent.mkdir()
        self.state = state()
        controller.save(self.path, self.state)

    def test_interrupted_record_write_preserves_previous_complete_record(self):
        before = self.path.read_bytes()
        with mock.patch.object(controller.os, "replace", side_effect=OSError("interrupted")):
            with self.assertRaises(OSError):
                controller.atomic_json(self.path, {"incomplete": True})
        self.assertEqual(self.path.read_bytes(), before)
        self.assertEqual(list(self.path.parent.glob(".candidate.json.*")), [])

    def test_configuration_changes_and_concurrent_controller_are_refused(self):
        changed = copy.deepcopy(self.state)
        changed["configuration"]["masterRevision"] = "e" * 40
        controller.atomic_json(self.path, changed)
        with self.assertRaisesRegex(controller.ControllerError, "configuration changed"):
            controller.read_state(self.path)
        with controller.locked(self.path):
            with self.assertRaisesRegex(controller.ControllerError, "running controller"):
                with controller.locked(self.path):
                    self.fail("Second controller acquired the same release")

    def test_completed_build_reuses_archive_and_refuses_modified_bytes(self):
        archive = self.path.parent / "SlopArena-0.2.0-playtest.42.tar.gz"
        archive.write_bytes(b"verified archive")
        self.state["steps"]["build"].update(status="completed", sha256=controller.coordinator.checksum(archive))
        with mock.patch.object(controller, "run_builder") as builder:
            controller.build(self.root, self.path, self.state)
            builder.assert_not_called()
            archive.write_bytes(b"other archive")
            with self.assertRaisesRegex(controller.ControllerError, "differs"):
                controller.build(self.root, self.path, self.state)
            builder.assert_not_called()

    def test_live_orphaned_builder_is_not_started_again(self):
        self.state["steps"]["build"].update(status="running", pid=123, processIdentity="same-process")
        with mock.patch.object(controller, "process_identity", return_value="same-process"), \
             mock.patch.object(controller, "run_builder") as builder:
            with self.assertRaisesRegex(controller.ControllerError, "still running"):
                controller.build(self.root, self.path, self.state)
            builder.assert_not_called()

    def test_missing_clean_checkout_documentation_blocks_before_unity_or_input_staging(self):
        with mock.patch.object(controller.subprocess, "run", return_value=SimpleNamespace(returncode=1)), \
             mock.patch.object(controller.subprocess, "Popen") as native, \
             mock.patch.object(controller.producer, "licensed_inputs") as inputs:
            with self.assertRaisesRegex(controller.ControllerError, "clean-checkout"):
                controller.run_builder(self.path, self.state, self.root)
        native.assert_not_called()
        inputs.assert_not_called()
        self.assertEqual(self.state["steps"]["build"]["reason"], "source_completeness_failed")

    def test_lost_publication_reply_is_adopted_without_creating_another_release(self):
        archive = self.path.parent / "SlopArena-0.2.0-playtest.42.tar.gz"
        archive.write_bytes(b"verified")
        sha = controller.coordinator.checksum(archive)
        self.state["steps"]["build"].update(archive=str(archive), sha256=sha)
        published = {"tag_name": "playtest-client-0.2.0-playtest.42", "draft": False, "prerelease": True,
                     "html_url": "https://github.com/example/release",
                     "assets": [{"name": archive.name, "digest": "sha256:" + sha}]}
        with mock.patch.object(controller, "api", side_effect=[published, {"sha": "a" * 40}]), \
             mock.patch.object(controller, "command") as create:
            controller.publish(self.root, self.path, self.state)
            create.assert_not_called()
        self.assertEqual(self.state["steps"]["publish"]["status"], "completed")

    def test_partial_publication_uploads_missing_own_asset_without_recreating_or_clobbering(self):
        archive = self.path.parent / "SlopArena-0.2.0-playtest.42.tar.gz"
        archive.write_bytes(b"verified")
        sha = controller.coordinator.checksum(archive)
        self.state["steps"]["build"].update(archive=str(archive), sha256=sha)
        partial = {"tag_name": "playtest-client-0.2.0-playtest.42", "draft": False, "prerelease": True,
                   "html_url": "https://github.com/example/release", "assets": []}
        complete = {**partial, "assets": [{"name": archive.name, "digest": "sha256:" + sha}]}
        with mock.patch.object(controller, "api", side_effect=[partial, {"sha": "a" * 40}, complete]), \
             mock.patch.object(controller, "command") as upload:
            controller.publish(self.root, self.path, self.state)
        self.assertEqual(upload.call_args.args[0][1:3], ["release", "upload"])
        self.assertNotIn("--clobber", upload.call_args.args[0])
        self.assertEqual(self.state["steps"]["publish"]["status"], "completed")

    def test_lost_dispatch_response_never_causes_blind_redispatch(self):
        with mock.patch.object(controller, "main_revision", return_value="a" * 40), \
             mock.patch.object(controller, "api", side_effect=controller.ControllerError("reply lost")):
            with self.assertRaises(controller.ControllerError):
                controller.dispatch(self.root, self.path, self.state)
        saved = controller.read_state(self.path)
        self.assertIn("dispatchStartedAtUtc", saved["steps"]["workflow"])
        with mock.patch.object(controller, "discover_run", return_value=None), \
             mock.patch.object(controller, "api") as dispatch:
            controller.dispatch(self.root, self.path, saved)
            dispatch.assert_not_called()
        self.assertEqual(saved["steps"]["workflow"]["status"], "dispatch-unconfirmed")

    def test_moved_main_refuses_before_dispatch(self):
        with mock.patch.object(controller, "main_revision", return_value="e" * 40), \
             mock.patch.object(controller, "api") as dispatch:
            with self.assertRaisesRegex(controller.ControllerError, "main moved"):
                controller.dispatch(self.root, self.path, self.state)
            dispatch.assert_not_called()

    def test_definite_dispatch_rejection_can_be_retried_after_credentials_are_fixed(self):
        with mock.patch.object(controller, "main_revision", return_value="a" * 40), \
             mock.patch.object(controller, "api", side_effect=controller.ControllerError("denied", 403)):
            with self.assertRaises(controller.ControllerError):
                controller.dispatch(self.root, self.path, self.state)
        self.assertNotIn("dispatchStartedAtUtc", self.state["steps"]["workflow"])
        self.assertEqual(self.state["steps"]["workflow"]["status"], "dispatch-rejected")

    def test_correlated_run_must_match_exact_source_and_be_unique(self):
        self.state["steps"]["workflow"]["requestId"] = "request"
        entry = {**run(), "display_title": "Playtest 0.2.0-playtest.42 (request)", "head_sha": "e" * 40}
        with mock.patch.object(controller, "api", return_value={"workflow_runs": [entry]}):
            with self.assertRaisesRegex(controller.ControllerError, "different game"):
                controller.discover_run(self.state)
        entry["head_sha"] = "a" * 40
        with mock.patch.object(controller, "api", return_value={"workflow_runs": [entry, entry]}):
            with self.assertRaisesRegex(controller.ControllerError, "More than one"):
                controller.discover_run(self.state)

    def test_candidate_changes_after_upload_are_refused(self):
        self.state["steps"]["build"]["catalogHash"] = "c" * 64
        self.state["steps"]["upload"]["candidate"] = candidate()
        changed = candidate()
        changed["steam"]["build_id"] += 1
        with self.assertRaisesRegex(controller.ControllerError, "identity changed"):
            controller.verify_candidate(changed, self.state, 42)
        changed = candidate()
        changed["source_revisions"]["master"] = "e" * 40
        with self.assertRaisesRegex(controller.ControllerError, "differs"):
            controller.verify_candidate(changed, self.state, 42)

    def write_receipts(self, outcome="success"):
        directory = self.path.parent / "fixture-ci"
        directory.mkdir()
        controller.atomic_json(directory / "candidate-public.json", candidate())
        controller.atomic_json(directory / "protocol.json", self.state["compatibility"])
        if outcome == "success":
            result = {**candidate(), "outcome": "success", "schema": candidate()["schema"]["target_migration"],
                      "services": {"master": {"readiness": True}, "game": {"readiness": True}},
                      "registration": {"available": True, "registered": True, "active_matches": 0,
                                       "hosts": [{"fresh": True, "active_matches": 0}]}}
        else:
            result = {"release_id": candidate()["release_id"], "outcome": "failure",
                      "failed_stage": "application_readiness", "reason_code": "application_not_ready",
                      "active_release": "previous", "recovery": {"status": "restored", "release_id": "previous"},
                      "failure_during_migration": False, "schema_after": candidate()["schema"]["target_migration"],
                      "observed_at": controller.now()}
        envelope = controller.coordinator.deployment_envelope(candidate(), result)
        envelope["publicReadinessVerified"] = outcome == "success"
        controller.atomic_json(directory / "release.json", envelope)
        self.state["steps"]["build"]["catalogHash"] = "c" * 64
        self.state["steps"]["workflow"]["runId"] = 42
        return directory

    def test_successful_ci_does_not_claim_steam_or_player_acceptance(self):
        directory = self.write_receipts()
        with mock.patch.object(controller, "api", return_value=run()), \
             mock.patch.object(controller, "receipts", return_value=[("deployment", directory)]):
            controller.reconcile(self.path, self.state)
        self.assertEqual(self.state["steps"]["deployment"]["status"], "completed")
        self.assertEqual(self.state["steps"]["steam"]["status"], "pending")
        self.assertEqual(self.state["playerAcceptance"]["status"], "not-exercised")

    def test_missing_protocol_proof_cannot_approve_protected_deployment(self):
        directory = self.write_receipts()
        (directory / "protocol.json").unlink()
        with mock.patch.object(controller, "api", return_value={**run(), "status": "waiting"}) as api, \
             mock.patch.object(controller, "receipts", return_value=[("upload", directory)]):
            with self.assertRaisesRegex(controller.ControllerError, "protocol proof"):
                controller.reconcile(self.path, self.state, approve=True)
        self.assertFalse(any("pending_deployments" in call.args[0] for call in api.call_args_list))

    def test_backend_receipt_with_failed_public_readiness_is_retained_without_steam_handoff(self):
        directory = self.write_receipts()
        receipt = controller.coordinator.read_json(directory / "release.json")
        receipt["publicReadinessVerified"] = False
        controller.atomic_json(directory / "release.json", receipt)
        with mock.patch.object(controller, "api", return_value=run()), \
             mock.patch.object(controller, "receipts", return_value=[("deployment", directory)]):
            controller.reconcile(self.path, self.state)
        self.assertEqual(self.state["steps"]["deployment"]["status"], "public-readiness-unverified")
        self.assertNotIn("Operator: activate", controller.summary(self.state))
        before = (directory / "release.json").read_bytes()
        response = mock.MagicMock()
        response.__enter__.return_value.status = 200
        with mock.patch.object(controller.urllib.request, "urlopen", return_value=response), \
             mock.patch.object(controller, "retry_deployment") as retry:
            controller.recheck_public_readiness(self.path, self.state)
            retry.assert_not_called()
        self.assertEqual(self.state["steps"]["deployment"]["status"], "completed")
        self.assertEqual((directory / "release.json").read_bytes(), before)

    def test_failed_ci_receipt_records_restoration_without_steam_activation(self):
        directory = self.write_receipts("failure")
        with mock.patch.object(controller, "api", return_value={**run(), "conclusion": "failure"}), \
             mock.patch.object(controller, "receipts", return_value=[("deployment", directory)]):
            controller.reconcile(self.path, self.state)
        self.assertEqual(self.state["steps"]["deployment"]["status"], "failed")
        self.assertEqual(self.state["steps"]["deployment"]["result"]["recovery"]["status"], "restored")
        self.assertNotIn("Operator: activate", controller.summary(self.state))

    def test_deploy_retry_preserves_upload_and_only_reruns_failed_job(self):
        self.state["steps"]["workflow"].update(runId=42, runAttempt=1, conclusion="failure")
        self.state["steps"]["upload"].update(status="completed", candidate=candidate())
        self.state["steps"]["deployment"]["result"] = {
            "outcome": "failure", "active_release": "previous", "failure_during_migration": False,
            "recovery": {"status": "restored"}, "failed_stage": "application_readiness"}
        before = copy.deepcopy(self.state["steps"]["upload"])
        with mock.patch.object(controller, "api", side_effect=[{"jobs": [
                {"id": 241, "name": "deploy", "conclusion": "failure"}]}, {}]) as api:
            controller.retry_deployment(self.path, self.state)
        self.assertEqual(api.call_args_list[-1].args[0], "repos/Binoui/SlopArena/actions/jobs/241/rerun")
        self.assertEqual(self.state["steps"]["upload"], before)
        with self.assertRaisesRegex(controller.ControllerError, "already recorded"):
            controller.retry_deployment(self.path, self.state)

    def test_old_ci_response_after_retry_does_not_report_the_old_failure_as_new_completion(self):
        self.state["steps"]["workflow"].update(runId=42, runAttempt=1, conclusion="failure",
                                               retryIntentAttempt=1, retryRequestedAtUtc=controller.now())
        self.state["steps"]["deployment"]["status"] = "retry-requested"
        with mock.patch.object(controller, "api", return_value={**run(), "conclusion": "failure"}), \
             mock.patch.object(controller, "receipts") as receipts:
            controller.reconcile(self.path, self.state)
        receipts.assert_not_called()
        self.assertEqual(self.state["steps"]["workflow"]["status"], "retry-requested")
        self.assertEqual(self.state["steps"]["deployment"]["status"], "retry-requested")

    def test_ambiguous_or_migration_failure_cannot_be_retried(self):
        self.state["steps"]["workflow"].update(runId=42, runAttempt=1, conclusion="failure")
        self.state["steps"]["upload"].update(status="completed", candidate=candidate())
        for result in ({"outcome": "unknown"}, {"outcome": "failure", "active_release": "previous",
                                             "failure_during_migration": True, "recovery": {"status": "restored"}},
                       {"outcome": "failure", "active_release": "previous", "failure_during_migration": False,
                        "recovery": {"status": "not_attempted"}, "failed_stage": "unknown"}):
            self.state["steps"]["deployment"]["result"] = result
            with mock.patch.object(controller, "api") as api, self.assertRaises(controller.ControllerError):
                controller.retry_deployment(self.path, self.state)
            api.assert_not_called()

    def test_steam_requires_both_public_build_and_correct_depot_manifest(self):
        self.assertEqual(controller.parse_appinfo(appinfo())["buildId"], 42)
        for malformed in (appinfo() + appinfo(), appinfo(manifest="0"), appinfo(build=str(2**32))):
            with self.assertRaises(controller.ControllerError):
                controller.parse_appinfo(malformed)
        self.state["steps"]["deployment"]["status"] = "completed"
        self.state["steps"]["upload"]["candidate"] = candidate()
        original = self.path.parent / "original-ci-receipt.json"
        original.write_text('{"steamDefaultActivated":false}\n')
        before = original.read_bytes()

        def steam(command, **kwargs):
            kwargs["stdout"].write(appinfo(manifest="100"))
            return SimpleNamespace(returncode=0)
        with mock.patch.object(controller.shutil, "which", return_value="/installed/steamcmd"), \
             mock.patch.object(controller.subprocess, "run", side_effect=steam):
            with self.assertRaisesRegex(controller.ControllerError, "exact BuildID and manifest"):
                controller.verify_live(SimpleNamespace(steamcmd="steamcmd"), self.path, self.state)
        self.assertEqual(self.state["steps"]["steam"]["status"], "awaiting-activation")
        self.assertEqual(original.read_bytes(), before)

    def test_player_acceptance_requires_live_build_and_completed_countdown_match_rematch(self):
        args = SimpleNamespace(outcome="passed", build_id=42, countdown_passed=True,
                               match_id="first", rematch_id="second", diagnostics=[], notes="two Steam accounts")
        self.state["steps"]["upload"]["candidate"] = candidate()
        with self.assertRaisesRegex(controller.ControllerError, "observed live"):
            controller.record_player_test(args, self.path, self.state)
        self.state["steps"]["steam"]["status"] = "completed"
        args.rematch_id = args.match_id
        with self.assertRaisesRegex(controller.ControllerError, "distinct"):
            controller.record_player_test(args, self.path, self.state)
        args.rematch_id = "second"
        controller.record_player_test(args, self.path, self.state)
        self.assertEqual(self.state["playerAcceptance"]["status"], "passed")
        self.assertIn("operator-reported", self.state["playerAcceptance"]["reports"][0]["source"])


if __name__ == "__main__":
    unittest.main()
