"""The selected pair must agree, independently of each repository's own tests."""
import unittest
import playtest_protocol as protocol


class ProtocolTests(unittest.TestCase):
    def sources(self, version=8):
        return {protocol.GAME_FILES[0]: f"public const byte Version = {version};",
                protocol.GAME_FILES[1]: "public const int CurrentProtocolVersion = SimulationProtocol.Version;"}

    def test_rejects_protocol_pair_that_passed_independent_repository_tests(self):
        with self.assertRaisesRegex(ValueError, "game protocol 8, Master protocol 7"):
            protocol.verify_sources(self.sources(), "public const int Protocol = 7;", "a" * 40, "b" * 40)

    def test_accepts_pinned_pair_and_ignores_retired_comments(self):
        result = protocol.verify_sources(self.sources(), "// public const int Protocol = 7;\npublic const int Protocol = 8;",
                                         "a" * 40, "b" * 40)
        self.assertEqual(result["source_revisions"]["master"], "b" * 40)
        self.assertEqual(result["game_protocol"], result["master_protocol"])

    def test_changed_alias_or_ambiguous_constant_fails_closed(self):
        game = self.sources()
        game[protocol.GAME_FILES[1]] = "public const int CurrentProtocolVersion = 7;"
        with self.assertRaises(ValueError):
            protocol.verify_sources(game, "public const int Protocol = 8;", "a" * 40, "b" * 40)
        for source in ("public const byte Version = Build.Version;", "public const byte Version = 8; public const byte Version = 7;"):
            with self.assertRaises(ValueError):
                protocol.protocol_value(source, r"\bpublic\s+const\s+byte\s+Version")

    def test_release_bump_is_required_only_when_shared_changed(self):
        protocol.verify_release_bump("playtest-client-1", 8, 8, simulation_changed=False)
        protocol.verify_release_bump("playtest-client-1", 8, 9, simulation_changed=True)
        with self.assertRaisesRegex(ValueError, "bump SimulationProtocol.Version and the Master Protocol to 9"):
            protocol.verify_release_bump("playtest-client-1", 8, 8, simulation_changed=True)
