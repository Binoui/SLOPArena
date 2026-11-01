using System;
using System.Reflection;
using System.Text.Json;
using SlopArena.Client.Camera;
using SlopArena.Client.Animation;
using SlopArena.Client.Combat;
using SlopArena.Client.Entities;
using SlopArena.Client.Input;
using SlopArena.Client.Network;
using SlopArena.Client.Simulation;
using SlopArena.Client.UI;
using SlopArena.Client.World;
using SlopArena.Shared;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using InputState = SlopArena.Shared.InputState;
using MouseButton = UnityEngine.InputSystem.LowLevel.MouseButton;
using Object = UnityEngine.Object;

namespace SlopArena.EditorTools
{
    public static class UnityAuditRuntimeSelfTest
    {
        private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        [MenuItem("Tools/SLOPArena/Tests/Unity Audit Runtime/All")]
        public static void RunAll()
        {
            RunMatchAbortParser();
            RunSwordSounds();
            RunTrainingFailure();
            RunSoloCountdown();
            RunDirectConnectModal();
            Debug.Log("[UnityAuditRuntimeSelfTest] All runtime regressions passed.");
        }

        [MenuItem("Tools/SLOPArena/Tests/Unity Audit Runtime/Match Abort Parser")]
        public static void RunMatchAbortParser()
        {
            RequireEditMode();
            var id = Guid.Parse("a813ec4b-f28d-41d7-b6c0-9d668006173e");
            foreach (string reason in new[]
            {
                "unfilled", "absent", "host_restart", "host_shutdown", "content_unavailable",
                "host_unavailable", "match_failed", "leader_returned_to_lobby",
            })
            {
                var parsed = ParseAbort("{\"matchId\":\"" + id + "\",\"reason\":\"" + reason + "\"}");
                Require(parsed != null && parsed.MatchId == id && parsed.Reason == reason,
                    "The MatchAborted boundary dropped or changed a supported cancellation: " + reason);
            }
            foreach (string malformed in new[]
            {
                "null", "[]", "17", "{}", "{\"matchId\":null,\"reason\":\"unfilled\"}",
                "{\"matchId\":42,\"reason\":\"unfilled\"}",
                "{\"matchId\":\"not-a-guid\",\"reason\":\"leader_returned_to_lobby\"}",
                "{\"matchId\":\"00000000-0000-0000-0000-000000000000\",\"reason\":\"unfilled\"}",
                "{\"matchId\":\"" + id + "\"}",
                "{\"matchId\":\"" + id + "\",\"reason\":null}",
                "{\"matchId\":\"" + id + "\",\"reason\":[]}",
                "{\"matchId\":\"" + id + "\",\"reason\":\"unknown_reason\"}",
            })
                Require(ParseAbort(malformed) == null, "Malformed cancellation was admitted: " + malformed);
            Debug.Log("[UnityAuditRuntimeSelfTest] Match abort allowlist and malformed payload rejection passed.");
        }

        [MenuItem("Tools/SLOPArena/Tests/Unity Audit Runtime/Sword Sounds")]
        public static void RunSwordSounds()
        {
            RequireEditMode();
            using var fixture = new PreviewFixture("Sword sounds");
            foreach (bool airborne in new[] { false, true })
            {
                var renderer = fixture.Root.AddComponent<PlayerRenderer>();
                var stage = PresentationSelfTestContent.Stage(20, Array.Empty<string>(),
                    new HitboxEvent { TriggerTick = 0 }, new HitboxEvent { TriggerTick = 5 },
                    new HitboxEvent { TriggerTick = 5 }, new HitboxEvent { TriggerTick = 9 });
                var slots = new CookedSlotDefinition[CanonicalSlotProjection.All.Count];
                int offset = airborne ? CanonicalSlotProjection.VariantSlotCount : 0;
                slots[offset] = PresentationSelfTestContent.Move(offset, stage, stage);
                slots[offset + 1] = PresentationSelfTestContent.Move(offset + 1, stage, stage);
                var catalog = SoundCatalog();
                try
                {
                Require(renderer.Configure(new CharacterDefinition { Class = CharacterClass.None, CookedSlots = slots },
                    null, catalog, null, out var error), error);
                var state = new CharacterState
                {
                    State = ActionState.Attacking, AttackSlot = AbilitySlots.Normal1,
                    AttackSequence = 1, IsGrounded = !airborne,
                };
                void Present(ushort elapsed, int expected, string why)
                {
                    state.AttackElapsedTicks = elapsed;
                    PresentSound(renderer, state);
                    Require((int)Get(renderer, "_swingSequence") == expected, (airborne ? "air" : "ground") + ": " + why);
                }
                Present(0, 1, "A first activation must emit its tick-zero trigger.");
                Present(0, 1, "A repeated tick-zero snapshot replayed a sound.");
                Present(4, 1, "A hitbox emitted before its trigger.");
                Present(5, 2, "Simultaneous hitboxes must emit one swoosh.");
                Present(6, 2, "The tick after a trigger emitted a duplicate swoosh.");
                Present(12, 3, "A skipped tick lost a crossed trigger.");
                Present(4, 3, "A rewind replayed a sound.");
                Present(12, 3, "Re-crossing an already presented trigger replayed a sound.");
                state.AttackSequence++;
                Present(0, 4, "A same-slot activation lost its tick-zero sound.");
                Present(12, 6, "Skipping multiple distinct triggers lost a sound.");
                state.ComboStage = 1;
                Present(0, 7, "A new combo stage lost its tick-zero sound.");
                Present(6, 8, "A new combo stage lost its later trigger.");
                state.ComboStage = 0;
                Present(12, 8, "Rewinding to a previous combo stage replayed its triggers.");
                state.ComboStage = 1;
                Present(6, 8, "Re-entering a previously presented stage replayed its triggers.");
                state.AttackSlot = AbilitySlots.Normal2;
                state.ComboStage = 0;
                Present(0, 9, "Changing slots retained the previous attack's sounds.");
                state.State = ActionState.Idle;
                Present(0, 9, "An idle state emitted a sound.");
                state.State = ActionState.Aiming;
                Present(0, 9, "Rewinding through idle replayed the same activation's trigger.");
                state.AttackSequence++;
                Present(0, 10, "A new activation after idle lost its trigger.");
                state.State = ActionState.Attacking;
                Present(0, 10, "Releasing aim replayed an already presented tick-zero trigger.");
                state.AttackSequence = byte.MaxValue;
                Present(0, 11, "A changed activation sequence lost its trigger.");
                state.AttackSequence = 0;
                Present(0, 12, "Activation sequence wrap lost its trigger.");
                }
                finally { Object.DestroyImmediate(catalog); }
            }
            CheckDashStrikeSword(fixture.Root);
            Debug.Log("[UnityAuditRuntimeSelfTest] Package swing triggers passed for None-class ground/air catalogs and Quick Draw tips; silent cycle positions retain dedupe/cursors.");
        }

        [MenuItem("Tools/SLOPArena/Tests/Unity Audit Runtime/Training Failure")]
        public static void RunTrainingFailure()
        {
            RequireEditMode();
            using var fixture = new PreviewFixture("Training failure");
            var match = fixture.Root.AddComponent<TrainingMatch>();
            string missingArena = "unity_audit_missing_" + Guid.NewGuid().ToString("N");
            Set(match, "_arenaNameOverride", missingArena);
            int diagnostics = 0;
            void Observe(string message, string stack, LogType type)
            {
                if (type == LogType.Error && message.Contains(missingArena)) diagnostics++;
            }
            Application.logMessageReceived += Observe;
            try { Invoke(match, "OnMatchStart"); }
            finally { Application.logMessageReceived -= Observe; }
            Require(diagnostics == 1, "Missing-arena admission must retain its useful diagnostic.");
            Require(!match.enabled && Get(match, "_bridge") == null && !match.CaptureSequenceReady,
                "Missing-arena admission left the match enabled or capture-ready.");
            Invoke(match, "Update");
            Invoke(match, "OnMatchFixedUpdate");

            // Later admission failures retain a bridge and a serialized renderer.
            // Null input is intentional: any polling of this failed match must fail the check.
            var bridge = new LocalSimulationBridge(new ArenaDefinition(), NoWinMatchRule.Instance);
            Set(match, "_bridge", bridge);
            Set(match, "_playerRenderer", fixture.Root.AddComponent<PlayerRenderer>());
            Invoke(match, "Update");
            Invoke(match, "OnMatchFixedUpdate");
            Require(bridge.InternalSim.SimulationTick == 0 && match.CaptureTick == 0 && !match.CaptureSequenceReady,
                "A failed match advanced a partially initialized simulation.");
            Debug.Log("[UnityAuditRuntimeSelfTest] Failed Training admission blocked polling and ticking before and after partial bridge setup. One missing-arena error above is expected.");
        }

        [MenuItem("Tools/SLOPArena/Tests/Unity Audit Runtime/Solo Countdown")]
        public static void RunSoloCountdown()
        {
            RequireEditMode();
            Require(!HumanInputActions.IsCapturing && !ChatInputGate.SuppressGameplay
                && SettingsOverlay.Active?.IsOpen != true && MatchPauseMenu.Active?.IsPaused != true,
                "Close input captures, chat, settings and pause before running the countdown check.");
            string previousOverrides = HumanInputActions.SaveOverrides();
            bool previousEnabled = HumanInputActions.Get("Normal1").actionMap.enabled;
            bool previousGamepad = HumanInputActions.LastUsedGamepad;
            var previousKeyboard = Keyboard.current;
            var previousMouse = Mouse.current;
            var actionMap = HumanInputActions.Get("Normal1").actionMap;
            var previousDevices = actionMap.devices;
            var previousSettings = InputSystem.settings;
            var testSettings = Object.Instantiate(previousSettings);
            var singleton = typeof(ClientSettingsService).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic);
            object previousService = singleton.GetValue(null);
            var previousMode = MatchConfig.Mode;
            using var fixture = new PreviewFixture("Solo countdown");
            var keyboard = InputSystem.AddDevice<Keyboard>("Unity audit countdown");
            var mouse = InputSystem.AddDevice<Mouse>("Unity audit countdown mouse");
            try
            {
                InputSystem.settings = testSettings;
                testSettings.updateMode = InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
                testSettings.SetInternalFeatureFlag("RUN_PLAYER_UPDATES_IN_EDIT_MODE", true);
                HumanInputActions.Initialize("");
                HumanInputActions.ResetOverrides();
                actionMap.devices = new InputDevice[] { keyboard, mouse };
                singleton.SetValue(null, fixture.Root.AddComponent<ClientSettingsService>());
                var input = fixture.Root.AddComponent<InputController>();
                var match = fixture.Root.AddComponent<TrainingMatch>();
                var bridge = new LocalSimulationBridge(new ArenaDefinition(), NoWinMatchRule.Instance);
                Set(match, "_bridge", bridge);
                Set(match, "_inputController", input);
                Set(match, "_playerRenderer", fixture.Root.AddComponent<PlayerRenderer>());
                Set(match, "_soloCountdownTicks", (ushort)300);
                MatchConfig.Mode = GameMode.Solo;

                void Send(params Key[] keys)
                {
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
                    InputSystem.QueueStateEvent(mouse, keys.Length == 0 ? new MouseState()
                        : new MouseState().WithButton(MouseButton.Left).WithButton(MouseButton.Right));
                    InputSystem.Update();
                    Invoke(match, "Update");
                }
                InputState Build() => input.BuildInputState(null, 0f, input.ConsumePendingSlotPress(), AimContext.None, null).input;
                Key[] presses = { Key.Space, Key.X, Key.C, Key.LeftShift, Key.Digit1, Key.Tab };
                for (int remaining = 300; remaining > 0; remaining--)
                {
                    if (remaining == 300 || remaining == 150 || remaining == 1) Send(presses);
                    else Send();
                    Invoke(match, "OnMatchFixedUpdate");
                }
                Send();
                var afterCountdown = Build();
                Require(!afterCountdown.Jump && !afterCountdown.DownPressed && !afterCountdown.ShieldPressed
                    && !afterCountdown.GrabPressed && !afterCountdown.RetargetPressed && !afterCountdown.FaceToCamera
                    && !afterCountdown.ToggleLock && afterCountdown.ActiveSlot == 0,
                    "Countdown presses leaked into the first live input.");
                Require((ushort)Get(match, "_soloCountdownTicks") == 0 && bridge.InternalSim.SimulationTick == 0,
                    "The countdown must consume 300 ticks without advancing combat.");
                Send(presses);
                var live = Build();
                Require(live.Jump && live.DownPressed && live.GrabPressed && live.RetargetPressed
                    && live.FaceToCamera && live.ToggleLock
                    && live.ActiveSlot == AbilitySlots.Normal1,
                    "Fresh next-live-tick presses were discarded after countdown completion.");
                Send();
                Build();
                Send(Key.LeftShift);
                Require(Build().ShieldPressed, "A fresh live Shield press was discarded.");
                input.RequireReleaseBeforeHumanInput();
                Send(Key.LeftShift, Key.Space);
                var blocked = Build();
                Require(!blocked.Jump && !blocked.ShieldHeld && !blocked.ShieldPressed,
                    "Post-pause release suppression admitted held input.");
                Send();
                Build();
                Send(Key.Space);
                Require(Build().Jump, "Input did not resume after releasing post-pause controls.");

                var definition = new CharacterDefinition
                {
                    CapsuleRadius = 0.4f, CapsuleHeight = 2f,
                    Movement = new MovementStats
                    {
                        MaxJumps = 2, JumpForce = 10f, AirJumpVMultiplier = 1f, JumpSquatTicks = 3,
                    },
                };
                bridge.RegisterEntity(MatchConfig.LocalEntityId, definition,
                    new CharacterState { PY = 2f, JumpsLeft = 2, IsGrounded = true });
                Set(match, "_playerDef", definition);
                Set(match, "_combatFeedback", fixture.Root.AddComponent<CombatFeedback>());
                Set(match, "_soloResultsShown", true);
                Send();
                Set(match, "_soloCountdownTicks", (ushort)1);
                Send(Key.Space);
                Invoke(match, "OnMatchFixedUpdate");
                Send();
                Invoke(match, "OnMatchFixedUpdate");
                Require(match.CaptureTick == 1 && bridge.GetState(MatchConfig.LocalEntityId).JumpsLeft == 2,
                    "A final-countdown press jumped on the first live simulation tick.");
                Send(Key.Space);
                Invoke(match, "OnMatchFixedUpdate");
                Require(match.CaptureTick == 2 && bridge.GetState(MatchConfig.LocalEntityId).JumpsLeft == 1,
                    "A fresh live press did not reach the Training simulation.");
                Debug.Log("[UnityAuditRuntimeSelfTest] All 300 Solo countdown ticks, the terminal transition into live simulation, fresh input and release suppression passed.");
            }
            finally
            {
                MatchConfig.Mode = previousMode;
                singleton.SetValue(null, previousService);
                HumanInputActions.ApplyOverrides(previousOverrides);
                actionMap.devices = previousDevices;
                if (!previousEnabled) HumanInputActions.Get("Normal1").actionMap.Disable();
                InputSystem.RemoveDevice(keyboard);
                InputSystem.RemoveDevice(mouse);
                InputSystem.settings = previousSettings;
                Object.DestroyImmediate(testSettings);
                if (previousKeyboard != null && previousKeyboard.added) previousKeyboard.MakeCurrent();
                if (previousMouse != null && previousMouse.added) previousMouse.MakeCurrent();
                typeof(HumanInputActions).GetProperty(nameof(HumanInputActions.LastUsedGamepad)).SetValue(null, previousGamepad);
            }
        }

        [MenuItem("Tools/SLOPArena/Tests/Unity Audit Runtime/Direct Connect Modal")]
        public static void RunDirectConnectModal()
        {
            RequireEditMode();
            Require(!UiModalState.Presented, "Close existing shell modals before running the direct-connect check.");
            using var fixture = new PreviewFixture("Direct connect modal");
            string previousRoute = Environment.GetEnvironmentVariable("SLOPARENA_DEV_UDP");
            var depth = typeof(UiModalState).GetField("_depth", BindingFlags.Static | BindingFlags.NonPublic);
            object previousDepth = depth.GetValue(null);
            try
            {
                Environment.SetEnvironmentVariable("SLOPARENA_DEV_UDP", "1");
                var context = (FrontendPageContext)Activator.CreateInstance(typeof(FrontendPageContext),
                    InstanceFlags, null, new object[] { FrontendPage.ServerBrowser, null! }, null);
                var page = new VisualElement();
                var modal = new VisualElement();
                var join = new Button();
                modal.Add(join);
                Invoke(context, "AttachRoot", page);
                Invoke(context, "AttachRoot", modal);
                var browser = fixture.Root.AddComponent<ServerBrowserUI>();
                browser.InjectPageContext(context);
                Set(browser, "_alive", true);
                Set(browser, "_directConnectModal", modal);
                Set(browser, "_modalJoin", join);
                Invoke(browser, "OpenDirectConnect");
                Require(modal.style.display.value == DisplayStyle.Flex && !page.enabledSelf
                    && context.ModalForwardButton == join, "Direct connect did not retain its page-modal routing.");
                UiModalState.Push();
                Invoke(context, "InvokeModalAction");
                Require(UiModalState.Presented, "Closing direct connect popped another owner's shell modal.");
                Require(modal.style.display.value == DisplayStyle.None && page.enabledSelf
                    && context.ModalForwardButton == null, "Closing direct connect did not clear its page-modal controls.");
                UiModalState.Pop();
                Invoke(browser, "OpenDirectConnect");
                UiModalState.Push();
                Invoke(browser, "OnDisable");
                Require(UiModalState.Presented, "Disabling the browser popped another owner's shell modal.");
                Debug.Log("[UnityAuditRuntimeSelfTest] Direct-connect close and disable preserved shell-modal ownership and page-modal routing.");
            }
            finally
            {
                depth.SetValue(null, previousDepth);
                Environment.SetEnvironmentVariable("SLOPARENA_DEV_UDP", previousRoute);
            }
        }

        private static void CheckDashStrikeSword(GameObject root)
        {
            var renderer = root.AddComponent<PlayerRenderer>();
            var blade = new CookedHitbox(AuthoringHitboxShape.Capsule, 0.1f, 0f, 0f, 0f, 0f, 0f, 1f,
                "_weapon_hilt", "_weapon_tip", 1f, 0f, 0f, 0f, 1, 1, true, 0);
            var charge = new CookedDashStrikeCapabilityParameters(3.5f, 28f, 8, 10, blade, blade, "presentation.fixture.tip");
            var timeline = new CookedTimeline(new[]
            {
                new CookedStage(20, 0, 0, 0, 0, Array.Empty<string>(), new CookedTimelineOperation[]
                {
                    new CookedStartCapabilityOperation(0, AuthoringUnit.Ticks, "fixture.charge", "1", charge),
                    new CookedSpawnHitboxOperation(8, AuthoringUnit.Meters, blade),
                    new CookedSpawnHitboxOperation(14, AuthoringUnit.Meters, blade),
                }),
            });
            var slots = new CookedSlotDefinition[CanonicalSlotProjection.All.Count];
            slots[0] = new CookedSlotDefinition(0, "ground.1", false, "Charge", "", "",
                AuthoringAbilityBehavior.DirectionalDash, AuthoringAimMode.None, 0, false, false, timeline);
            var catalog = SoundCatalog();
            try
            {
            Require(renderer.Configure(new CharacterDefinition
            {
                Class = CharacterClass.None, CookedSlots = slots,
            }, null, catalog, null, out var error), error);
            var state = new CharacterState
            {
                State = ActionState.Aiming, AttackSlot = AbilitySlots.Normal1, AttackSequence = 1,
                IsGrounded = true, AttackElapsedTicks = 4,
            };
            PresentSound(renderer, state);
            Require((int)Get(renderer, "_swingSequence") == 0, "A Quick Draw tip emitted while aiming.");
            state.State = ActionState.Attacking;
            foreach (ushort elapsed in new ushort[] { 7, 8, 9, 7, 8 })
            {
                state.AttackElapsedTicks = elapsed;
                PresentSound(renderer, state);
            }
            Require((int)Get(renderer, "_swingSequence") == 1,
                "A Quick Draw tip was lost or replayed across its seek tick or a rewind.");
            state.AttackSequence++;
            state.AttackElapsedTicks = 12;
            PresentSound(renderer, state);
            Require((int)Get(renderer, "_swingSequence") == 2,
                "A new Quick Draw activation skipped past the finisher without emitting.");
            state.AttackElapsedTicks = 14;
            PresentSound(renderer, state);
            PresentSound(renderer, state);
            Require((int)Get(renderer, "_swingSequence") == 3,
                "A Quick Draw tip suppressed or duplicated a later ordinary hitbox sound.");
            }
            finally { Object.DestroyImmediate(catalog); }
        }

        private static CharacterAnimationCatalog SoundCatalog()
        {
            var catalog = ScriptableObject.CreateInstance<CharacterAnimationCatalog>();
            var silentCycle = new AudioPool(AudioPoolSelection.Cycle, new AudioClip[1]);
            catalog.SetPresentationProfile(new CharacterPresentationProfile(Color.white, silentCycle,
                silentCycle, silentCycle, silentCycle, false, Array.Empty<ProjectilePresentation>()));
            return catalog;
        }

        private static MatchAbortedNotification ParseAbort(string json)
        {
            using var document = JsonDocument.Parse(json);
            return (MatchAbortedNotification)typeof(LobbyClient)
                .GetMethod("TryParseMatchAborted", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { document.RootElement });
        }

        private static void PresentSound(PlayerRenderer renderer, CharacterState state)
        {
            // Match ApplyServerState's sound-before-state-commit order without starting models or audio.
            state.AnimIndex = state.ComboStage;
            Set(renderer, "_activeMove", renderer.CharacterDef?.GetCookedSlotAbility(state.AttackSlot, !state.IsGrounded));
            Invoke(renderer, "EmitSwordHitboxSfx", state);
            Set(renderer, "_lastState", state);
            Set(renderer, "_hasAppliedState", true);
        }

        private static object Get(object target, string name) => FindField(target.GetType(), name).GetValue(target);
        private static void Set(object target, string name, object value) => FindField(target.GetType(), name).SetValue(target, value);

        private static FieldInfo FindField(Type type, string name)
        {
            for (; type != null; type = type.BaseType)
            {
                var field = type.GetField(name, InstanceFlags | BindingFlags.DeclaredOnly);
                if (field != null) return field;
            }
            throw new MissingFieldException(name);
        }

        private static void Invoke(object target, string method, params object[] args)
        {
            try { target.GetType().GetMethod(method, InstanceFlags).Invoke(target, args); }
            catch (TargetInvocationException error) when (error.InnerException != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();
                throw;
            }
        }

        private static void RequireEditMode() => Require(!Application.isPlaying, "Run the Unity audit regressions in Edit Mode.");
        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private sealed class PreviewFixture : IDisposable
        {
            private readonly Scene _scene;
            public GameObject Root { get; }

            public PreviewFixture(string name)
            {
                _scene = EditorSceneManager.NewPreviewScene();
                Root = new GameObject("Unity audit " + name) { hideFlags = HideFlags.HideAndDontSave };
                Root.SetActive(false);
                SceneManager.MoveGameObjectToScene(Root, _scene);
            }

            public void Dispose()
            {
                Object.DestroyImmediate(Root);
                EditorSceneManager.ClosePreviewScene(_scene);
            }
        }
    }
}
