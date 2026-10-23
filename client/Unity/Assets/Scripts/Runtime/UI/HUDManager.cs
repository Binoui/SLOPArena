using System;
using System.Collections.Generic;
using SlopArena.Shared;
using SlopArena.Client.Input;
using SlopArena.Client.Network;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace SlopArena.Client.UI
{
    /// <summary>
    /// Combat HUD, rebuilt on UI Toolkit.
    ///
    /// Player cards are built from the roster, so 1v1 and 2/3/4-player matches
    /// adapt without per-count UXML variants. Move keys pair each normal with the
    /// special on the same face button and use effective Input System bindings;
    /// cooldown data remains read-only.
    ///
    /// Fight cards, key presses/pops, and stock-loss broadcasts stay attached to
    /// their readouts rather than covering the fight.
    /// </summary>
    public class HUDManager : MonoBehaviour
    {
        [SerializeField] private UIDocument _uiDocument;
        [SerializeField] private MatchTextVFX _textVfx;

        /// <summary>The HUD's UI document — the explicit host for match-surface
        /// overlays (pause menu) so nothing searches for a first document.</summary>
        public UIDocument? Document => _uiDocument;
        /// <summary>Identity required by the tracked damage readout and broadcast card.</summary>
        public readonly struct HudPlayer
        {
            public readonly ulong EntityId;
            public readonly string Label;
            public readonly CharacterClass Class;
            public readonly bool IsLocal;
            /// <summary>Online player name for the fight card; null offline.</summary>
            public readonly string DisplayName;
            /// <summary>Chat identity of an online player, for speech slips; null offline.</summary>
            public readonly string ChatPlayerId;

            public HudPlayer(ulong entityId, string label, CharacterClass @class, bool isLocal,
                string displayName = null, string chatPlayerId = null)
            {
                EntityId = entityId;
                Label = label;
                Class = @class;
                IsLocal = isLocal;
                DisplayName = displayName;
                ChatPlayerId = chatPlayerId;
            }
        }

        private sealed class OverheadPanel
        {
            public VisualElement Root = null!;
            public Label Damage = null!;
            public VisualElement[] StockIcons = Array.Empty<VisualElement>();
            public Label StockCountLabel = null!;
            public VisualElement StockToast = null!;
            public Label StockToastTitle = null!;
            public Label StockToastDetail = null!;
            public float StockToastTimer;
            public float StockToastDuration;
            public Color TierColor = Color.white;
            public Color IdentityColor;
            public bool HasState;
            public ushort PrevDamage;
            public int PrevStocks = -1;
            public float HitFlashTimer;
        }

        private readonly struct SlotArtwork
        {
            public readonly Texture2D Texture;
            public readonly Sprite Sprite;
            public readonly Color Tint;

            public SlotArtwork(Texture2D texture, Sprite sprite, Color tint)
            {
                Texture = texture;
                Sprite = sprite;
                Tint = tint;
            }
        }

        private sealed class ActionSlot
        {
            public readonly VisualElement Root;
            public readonly VisualElement Cooldown;
            public readonly Label Timer;
            public readonly Label Key;
            public readonly Image Button;
            public readonly Label Ready;
            public readonly Label MoveName;
            public readonly Image Icon;
            public SlotArtwork Artwork;
            public SlotArtwork DisplayedIcon;
            public string DisplayName;
            public bool GroundAvailable;
            public bool AirAvailable;
            public ushort MaxCooldown;
            public ushort PrevCooldown;
            public int TimerValue = -1;
            public float PulseTimer;
            public bool Locked;

            public ActionSlot(VisualElement root, string cooldownName, string timerName, string keyName)
            {
                Root = root;
                Cooldown = root.Q<VisualElement>(cooldownName);
                Timer = root.Q<Label>(timerName);
                Timer.style.display = DisplayStyle.None;
                Key = root.Q<Label>(keyName);
                Button = root.Q<Image>(root.name + "-button");
                Button.scaleMode = ScaleMode.ScaleToFit;
                Ready = root.Q<Label>(root.name + "-ready")
                    ?? new Label("READY") { name = root.name + "-ready" };
                Ready.AddToClassList("slot-ready");
                if (Ready.parent == null) root.Add(Ready);
                MoveName = root.Q<Label>(root.name + "-name");
                Icon = root.Q<Image>(root.name + "-icon");
                Icon.scaleMode = ScaleMode.ScaleToFit;
            }
        }

        private readonly struct AbilitySlotDef
        {
            public readonly string Name;
            public readonly int SlotIndex; // zero-based wire index == cooldown index (0-10)
            public readonly string Action;

            public AbilitySlotDef(string name, int slotIndex, string action)
            {
                Name = name;
                SlotIndex = slotIndex;
                Action = action;
            }
        }

        /// <summary>
        /// Canonical projection order: normals 1–4 then specials A/E/R/F.
        /// UXML pairs 1/A, 2/R, 3/E, 4/F to match the existing input adapter.
        /// SlotIndex is the AbilitySlots/cooldown index (key "1" = 2 … key "A" = 10).
        /// LMB/RMB remain utility controls, and the former extra key position is outside the
        /// canonical action grid, so neither is shown as an ability slot.
        /// </summary>
        private static readonly AbilitySlotDef[] AbilitySlotDefs =
        {
            new("ab-1", 2, "Slot1"),
            new("ab-2", 6, "Slot2"),
            new("ab-3", 7, "Slot3"),
            new("ab-4", 8, "Slot4"),
            new("ab-a", 10, "SlotA"),
            new("ab-e", 3, "SlotE"),
            new("ab-r", 4, "SlotR"),
            new("ab-f", 5, "SlotF"),
        };

        // More than eight lives use the explicit count without overflowing the card.
        private const int MaxStockIcons = 8;

        private const float JuiceDuration = 0.15f;
        private const float ReadyDuration = 0.42f;

        private static readonly Color MoveInk = new Color32(27, 26, 24, 255);

        private static readonly Color[] BadgeColors;
        private static readonly Color OrangeDamage;
        private static readonly Color CrimsonDamage;

        static HUDManager()
        {
            BadgeColors = new Color[4];
            TryHex("#FBBF24", out BadgeColors[0]); // P1 gold
            TryHex("#EA580C", out BadgeColors[1]); // P2 orange-red
            TryHex("#3B82F6", out BadgeColors[2]); // P3 blue
            TryHex("#22C55E", out BadgeColors[3]); // P4 green
            TryHex("#F97316", out OrangeDamage);   // damage 40-89
            TryHex("#EF4444", out CrimsonDamage);  // damage 90+
        }

        private static bool TryHex(string hex, out Color color)
            => ColorUtility.TryParseHtmlString(hex, out color);

        private Func<ulong, CharacterState> _getState;
        private ulong _localEntityId;
        private int _maxStocks;
        private CharacterDefinition _charDef;
        private VisualElement _billboardLayer;
        private VisualElement _moveDeck;
        private VisualElement _movePairs;
        private Label _moveTooltip;
        private VisualElement _modifierPrompt;
        private Image _modifierButton;
        private Label _modifierKey;
        private bool _modifierHeld;
        private bool _moveDeckRightAligned;
        private float _moveDeckScale = -1f;
        public VisualElement TargetingRoot
            => _uiDocument != null
                ? _uiDocument.rootVisualElement.Q<VisualElement>("target-lock-indicator")
                : null;
        private Label _networkStatsLabel;
        private NetworkClient _networkClient;
        private SoloCountdownView _soloCountdown;
        private SoloEndCardView _soloEndCard;

        private readonly Dictionary<ulong, OverheadPanel> _billboardPanels = new();
        // Chat identity -> fight entity for online players; empty offline.
        private readonly Dictionary<string, ulong> _speakers = new(StringComparer.Ordinal);
        private ChatSpeechSlips _speech;
        private UnityEngine.Camera _camera;

        private ActionSlot[] _abilitySlots = Array.Empty<ActionSlot>();
        private InputPromptAtlas _promptAtlas;
        private int _seenBindingRevision = -1;
        private bool _showGamepadPrompts;

        /// <summary>
        /// Initialize the HUD.
        /// <paramref name="getState"/> is called each Refresh() per panel entity id —
        /// pass a method over whatever simulation source owns the states (local sim,
        /// network client, replay reader).
        /// </summary>
        /// <param name="players">Roster, one panel per entry. Local player must be marked.</param>
        /// <param name="maxStocks">Stocks per player; &lt;= 0 hides stock display (training).</param>
        public void Initialize(Func<ulong, CharacterState> getState, IReadOnlyList<HudPlayer> players, int maxStocks)
        {
            _getState = getState;
            _maxStocks = Mathf.Max(0, maxStocks);
            _localEntityId = 0;

            if (_uiDocument == null)
            {
                Debug.LogWarning("[HUD] No UIDocument assigned");
                return;
            }

            var root = _uiDocument.rootVisualElement;
            _soloCountdown = new SoloCountdownView(root);
            _soloEndCard = new SoloEndCardView(root);
            if (_textVfx == null)
                _textVfx = gameObject.AddComponent<MatchTextVFX>();
            var cam = UnityEngine.Camera.main ?? FindFirstObjectByType<UnityEngine.Camera>();
            if (cam != null)
                _textVfx.SetCamera(cam);
            _networkClient = FindFirstObjectByType<NetworkClient>();
            _networkStatsLabel = new Label();
            _networkStatsLabel.style.position = Position.Absolute;
            _networkStatsLabel.style.right = 18;
            _networkStatsLabel.style.top = 12;
            _networkStatsLabel.style.color = Color.white;
            _networkStatsLabel.style.unityTextAlign = TextAnchor.UpperRight;
            root.Add(_networkStatsLabel);
            _billboardLayer = root.Q<VisualElement>("player-billboard");
            // Preserve the named inspection anchor used by native UI commands.
            _moveDeck = root.Q<VisualElement>("kit-diamonds");

            // Rebuild player panels from the roster (badge color by roster position).
            _billboardLayer?.Clear();
            _billboardPanels.Clear();
            _speakers.Clear();
            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                if (p.IsLocal)
                    _localEntityId = p.EntityId;
                var billboard = BuildBillboardPanel(p, i);
                _billboardPanels[p.EntityId] = billboard;
                _billboardLayer?.Add(billboard.Root);
                if (!string.IsNullOrEmpty(p.ChatPlayerId))
                    _speakers[p.ChatPlayerId] = p.EntityId;
            }
            _speech?.Detach();
            _speech = new ChatSpeechSlips(root, _billboardLayer,
                id => _billboardPanels.TryGetValue(id, out var card) ? card.Root : null,
                () => TargetingRoot);

            SetupMoveDeck();

            if (_moveDeck != null)
                _moveDeck.style.display = _localEntityId != 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void SetupMoveDeck()
        {
            if (_uiDocument == null) return;
            var root = _uiDocument.rootVisualElement;
            _movePairs = root.Q<VisualElement>("move-pairs");
            _moveTooltip = root.Q<Label>("move-tooltip");
            _modifierPrompt = root.Q<VisualElement>("special-modifier-prompt");
            _modifierButton = root.Q<Image>("special-modifier-button");
            _modifierButton.scaleMode = ScaleMode.ScaleToFit;
            _modifierKey = root.Q<Label>("special-modifier-key");

            _abilitySlots = new ActionSlot[AbilitySlotDefs.Length];
            for (int i = 0; i < AbilitySlotDefs.Length; i++)
            {
                var d = AbilitySlotDefs[i];
                _abilitySlots[i] = new ActionSlot(
                    root.Q<VisualElement>(d.Name),
                    $"{d.Name}-cooldown", $"{d.Name}-timer", $"{d.Name}-key");
                var slot = _abilitySlots[i];
                slot.Root.userData = slot;
                slot.Root.UnregisterCallback<PointerEnterEvent>(OnMovePointerEnter);
                slot.Root.UnregisterCallback<PointerLeaveEvent>(OnMovePointerLeave);
                slot.Root.RegisterCallback<PointerEnterEvent>(OnMovePointerEnter);
                slot.Root.RegisterCallback<PointerLeaveEvent>(OnMovePointerLeave);
            }

            UpdateBindingPrompts();
        }

        private void OnMovePointerEnter(PointerEnterEvent evt)
        {
            if (_moveTooltip == null || evt.currentTarget is not VisualElement { userData: ActionSlot slot }) return;
            _moveTooltip.text = slot.DisplayName;
            float center = _movePairs.WorldToLocal(slot.Root.worldBound.center).x;
            _moveTooltip.style.left = Mathf.Clamp(center - 120f, 0f, Mathf.Max(0f, _movePairs.contentRect.width - 240f));
            _moveTooltip.style.display = DisplayStyle.Flex;
        }

        private void OnMovePointerLeave(PointerLeaveEvent evt)
        {
            if (_moveTooltip != null) _moveTooltip.style.display = DisplayStyle.None;
        }

        private void UpdateBindingPrompts()
        {
            _showGamepadPrompts = HumanInputActions.LastUsedGamepad && Gamepad.current != null;
            _seenBindingRevision = HumanInputActions.BindingRevision;
            _moveDeck.EnableInClassList("controller-prompts", _showGamepadPrompts);
            string group = _showGamepadPrompts ? HumanInputActions.GamepadGroup : HumanInputActions.KeyboardMouseGroup;

            for (int i = 0; i < _abilitySlots.Length; i++)
            {
                var slot = _abilitySlots[i];
                string action = _showGamepadPrompts
                    ? AbilitySlotDefs[i].Action switch
                    {
                        "SlotA" => "Slot1",
                        "SlotE" => "Slot3",
                        "SlotR" => "Slot2",
                        "SlotF" => "Slot4",
                        _ => AbilitySlotDefs[i].Action,
                    }
                    : AbilitySlotDefs[i].Action;
                string path = BindingPath(action, group);
                var buttonGlyph = default(InputPromptAtlas.Glyph);
                bool hasButton = _showGamepadPrompts &&
                    (_promptAtlas ??= new InputPromptAtlas()).TryXbox(path, out buttonGlyph);
                slot.Button.style.display = hasButton ? DisplayStyle.Flex : DisplayStyle.None;
                slot.Key.style.display = hasButton ? DisplayStyle.None : DisplayStyle.Flex;
                if (hasButton)
                    SetPromptImage(slot.Button, buttonGlyph);
                else
                {
                    string label = HumanInputActions.BindingLabel(action, group);
                    slot.Key.text = _showGamepadPrompts
                        ? label : InputPromptAtlas.KeyboardDisplayLabel(path, label);
                }
            }

            string modifierPath = BindingPath("SpecialModifier", HumanInputActions.GamepadGroup);
            string shoulderLabel = modifierPath?.EndsWith("/leftShoulder", StringComparison.OrdinalIgnoreCase) == true
                ? "LB" : modifierPath?.EndsWith("/rightShoulder", StringComparison.OrdinalIgnoreCase) == true
                    ? "RB" : null;
            var modifierGlyph = default(InputPromptAtlas.Glyph);
            bool hasModifier = _showGamepadPrompts &&
                (_promptAtlas ??= new InputPromptAtlas()).TryXbox(modifierPath, out modifierGlyph);
            bool showModifierGlyph = hasModifier && shoulderLabel == null;
            _modifierButton.style.display = showModifierGlyph ? DisplayStyle.Flex : DisplayStyle.None;
            _modifierKey.style.display = showModifierGlyph ? DisplayStyle.None : DisplayStyle.Flex;
            _modifierKey.text = _showGamepadPrompts
                ? shoulderLabel ?? HumanInputActions.BindingLabel("SpecialModifier", HumanInputActions.GamepadGroup)
                : "SPECIALS";
            if (hasModifier) SetPromptImage(_modifierButton, modifierGlyph);
            _modifierPrompt.EnableInClassList("keyboard-modifier", !_showGamepadPrompts);
        }

        private static string BindingPath(string action, string group)
        {
            int index = HumanInputActions.BindingIndex(action, group);
            return index < 0 ? null : HumanInputActions.Get(action).bindings[index].effectivePath;
        }

        private static void SetPromptImage(Image image, InputPromptAtlas.Glyph glyph)
        {
            image.image = glyph.Texture;
            image.uv = glyph.Uv;
        }


        private OverheadPanel BuildBillboardPanel(HudPlayer p, int colorIndex)
        {
            var identityColor = BadgeColors[colorIndex % BadgeColors.Length];
            var camp = MatchConfig.Format == MatchFormat.SlopballVersus && MatchConfig.Camps.TryGetValue(p.EntityId, out var selectedCamp)
                ? selectedCamp : SlopballCamp.None;
            if (camp != SlopballCamp.None) identityColor = camp == SlopballCamp.Teal
                ? new Color(.22f, .9f, .85f) : new Color(1f, .55f, .35f);
            if (CourseColours.ForEntity(p.EntityId) is { } courseColour) identityColor = courseColour;
            var root = new VisualElement { name = $"billboard-{p.EntityId}" };
            root.AddToClassList("billboard-card");
            root.AddToClassList("billboard-left");
            root.EnableInClassList("local-player", p.IsLocal);
            root.style.borderBottomColor = identityColor;

            var portraitFrame = new VisualElement();
            portraitFrame.AddToClassList("portrait-frame");
            portraitFrame.style.backgroundColor = identityColor;
            var portrait = new VisualElement();
            portrait.AddToClassList("fighter-portrait");
            var portraitTexture = Resources.Load<Texture2D>($"UI/Portraits/{p.Class}");
            if (portraitTexture != null)
                portrait.style.backgroundImage = new StyleBackground(portraitTexture);
            else
                portraitFrame.AddToClassList("portrait-missing");
            portraitFrame.Add(portrait);
            root.Add(portraitFrame);

            var copy = new VisualElement();
            copy.AddToClassList("fighter-copy");

            var details = new VisualElement();
            details.AddToClassList("fighter-details");

            var tape = new VisualElement();
            tape.AddToClassList("fighter-tape");
            var badge = new Label(p.Label);
            if (camp != SlopballCamp.None) badge.text += " · " + camp.ToString().ToUpperInvariant();
            badge.AddToClassList("badge");
            badge.style.backgroundColor = identityColor;
            tape.Add(badge);
            details.Add(tape);

            string character = p.Class.ToString().ToUpperInvariant();
            bool named = !string.IsNullOrEmpty(p.DisplayName);
            var fighterName = new Label(named ? p.DisplayName : character) { enableRichText = false };
            fighterName.AddToClassList("fighter-name");
            fighterName.EnableInClassList("fighter-name--player", named);
            details.Add(fighterName);
            if (named)
            {
                // Online cards say who is playing; the fighter stays as a quiet tag.
                var fighterTag = new Label(character);
                fighterTag.AddToClassList("fighter-character");
                details.Add(fighterTag);
            }
            copy.Add(details);

            var damage = new Label("0%");
            damage.AddToClassList("overhead-damage");
            copy.Add(damage);

            var panel = new OverheadPanel { Root = root, Damage = damage, IdentityColor = identityColor };
            AddStocks(panel, copy);
            root.Add(copy);

            var stockToast = new VisualElement();
            stockToast.AddToClassList("stock-toast-player");
            stockToast.style.display = DisplayStyle.None;
            stockToast.style.borderBottomColor = identityColor;
            var stockTitle = new Label("KO");
            stockTitle.AddToClassList("stock-toast-title");
            var stockDetail = new Label();
            stockDetail.AddToClassList("stock-toast-detail");
            stockToast.Add(stockTitle);
            stockToast.Add(stockDetail);
            root.Add(stockToast);
            panel.StockToast = stockToast;
            panel.StockToastTitle = stockTitle;
            panel.StockToastDetail = stockDetail;
            return panel;
        }

        private void AddStocks(OverheadPanel panel, VisualElement parent)
        {
            if (_maxStocks <= 0) return;

            var stockRow = new VisualElement();
            stockRow.AddToClassList("stock-row");
            var label = new Label("LIVES");
            label.AddToClassList("stock-label");
            stockRow.Add(label);

            var count = new Label($"×{_maxStocks}") { name = "stock-count" };
            count.AddToClassList("stock-count");
            stockRow.Add(count);
            panel.StockCountLabel = count;

            if (_maxStocks <= MaxStockIcons)
            {
                var tickets = new VisualElement { name = "stock-tickets" };
                tickets.AddToClassList("stock-tickets");
                tickets.EnableInClassList("many-stocks", _maxStocks > 4);
                panel.StockIcons = new VisualElement[_maxStocks];
                for (int i = 0; i < _maxStocks; i++)
                {
                    var icon = new VisualElement();
                    icon.AddToClassList("stock-icon");
                    var strike = new VisualElement { name = "stock-strike" };
                    strike.AddToClassList("stock-strike");
                    icon.Add(strike);
                    tickets.Add(icon);
                    panel.StockIcons[i] = icon;
                }
                stockRow.Add(tickets);
            }

            parent.Add(stockRow);
        }


        /// <summary>
        /// Provide the character definition: resolves ability icons, per-slot max
        /// cooldowns (max of grounded/airborne), and the locked (no-data) state.
        /// </summary>
        public void SetCharacterDefinition(CharacterDefinition def)
        {
            _charDef = def;
            if (_moveTooltip != null) _moveTooltip.style.display = DisplayStyle.None;
            bool isGrounded = _getState == null || _localEntityId == 0 || _getState(_localEntityId).IsGrounded;

            if (_abilitySlots == null || _abilitySlots.Length == 0) return;
            for (int i = 0; i < _abilitySlots.Length; i++)
            {
                var slot = _abilitySlots[i];
                var d = AbilitySlotDefs[i];
                var grounded = def.GetCookedSlotAbility((byte)((d.SlotIndex) + 1), airborne: false);
                var airborne = def.GetCookedSlotAbility((byte)((d.SlotIndex) + 1), airborne: true);

                ushort max = 0;
                if (grounded != null) max = grounded.CooldownTicks;
                if (airborne != null && airborne.CooldownTicks > max) max = airborne.CooldownTicks;
                slot.MaxCooldown = max;
                slot.Locked = grounded == null && airborne == null;
                slot.Root.EnableInClassList("locked", slot.Locked);

                bool normal = i < 4;
                slot.GroundAvailable = grounded != null;
                slot.AirAvailable = airborne != null;
                // The HUD is a stable control legend, even when the active move is aerial.
                var displayAbility = grounded ?? airborne;
                slot.DisplayName = MoveLabel(displayAbility);
                slot.Artwork = LoadSlotIcon(displayAbility, normal);
                SetSlotPresentation(slot, isGrounded);
            }

        }

        private static string MoveLabel(CookedSlotDefinition spec)
            => spec != null && !string.IsNullOrWhiteSpace(spec.Name)
                ? spec.Name.ToUpperInvariant() : "UNAVAILABLE";

        private SlotArtwork LoadSlotIcon(CookedSlotDefinition spec, bool normal)
        {
            if (spec == null || string.IsNullOrEmpty(spec.IconId)) return default;
            string path = $"Icons/{_charDef.Class}/";
            string cardPath = $"UI/AbilityCards/{_charDef.Class}/";
            var tint = normal ? MoveInk : Color.white;
            var cardIcons = Resources.LoadAll<Sprite>(cardPath + spec.IconId);
            if (cardIcons.Length > 0) return new SlotArtwork(null, cardIcons[0], Color.white);
            return new SlotArtwork(Resources.Load<Texture2D>(path + spec.IconId), null, tint);
        }

        private static void SetSlotPresentation(ActionSlot slot, bool grounded)
        {
            string name = slot.DisplayName;
            if (slot.MoveName.text != name)
            {
                slot.MoveName.text = name;
                slot.MoveName.EnableInClassList("long-move-name", name.Length > 18);
            }
            slot.Root.EnableInClassList("locked", grounded ? !slot.GroundAvailable : !slot.AirAvailable);
            var artwork = slot.Artwork;
            if (slot.DisplayedIcon.Texture == artwork.Texture &&
                slot.DisplayedIcon.Sprite == artwork.Sprite && slot.DisplayedIcon.Tint == artwork.Tint) return;
            slot.DisplayedIcon = artwork;
            if (artwork.Sprite != null) slot.Icon.sprite = artwork.Sprite;
            else slot.Icon.image = artwork.Texture;
            slot.Icon.tintColor = artwork.Tint;
        }

        /// <summary>Apply damage % + stocks to one scoreboard card.</summary>
        private void UpdatePanel(OverheadPanel panel, CharacterState state)
        {
            int dmg = (int)state.DamagePercent;
            panel.TierColor = DamageColor(dmg);
            if (dmg != panel.PrevDamage)
                panel.Damage.text = $"{dmg}%";
            if (panel.HasState && dmg > panel.PrevDamage && !ClientSettingsService.Instance.ReducedFlashing)
                panel.HitFlashTimer = JuiceDuration;
            panel.PrevDamage = (ushort)dmg;
            panel.HasState = true;

            if (_maxStocks > 0)
            {
                int left = Mathf.Clamp(_maxStocks - state.Deaths, 0, _maxStocks);
                if (left != panel.PrevStocks)
                {
                    panel.StockCountLabel.text = $"×{left}";
                    for (int i = 0; i < panel.StockIcons.Length; i++)
                        panel.StockIcons[i].EnableInClassList("lost", i >= left);
                    panel.Root.EnableInClassList("eliminated", left == 0);
                    panel.PrevStocks = left;
                }
            }
        }

        /// <summary>
        /// Shows the authored particle-text broadcast. Match callouts deliberately
        /// have no UI Toolkit text fallback so one message cannot render twice.
        /// </summary>
        public void ShowMatchCallout(string text, float seconds = 0.9f)
        {
            _textVfx?.Show(text, new Vector2(Screen.width * 0.5f, Screen.height * 0.5f),
                text == "SLOP IT OUT" ? 0.9f : 1f,
                Color.white, new Color(1f, 0.72f, 0.12f),
                seconds / 1.5f, centerInCamera: true);
        }

        public void ShowSoloCountdown(string text, float seconds)
            => _soloCountdown?.Show(text, seconds);

        /// <summary>A headline on the gold start card, without the countdown beat (Character Course ALL CLEAR!).</summary>
        public void ShowSoloStamp(string text, float seconds)
            => _soloCountdown?.Show(text, seconds, wide: true, sound: false);

        public void ShowSoloEnding(bool playerWon, bool sharedVictory, CharacterClass player, CharacterClass cpu)
        {
            _soloCountdown?.Clear();
            _soloEndCard?.Show(playerWon, sharedVictory, player, cpu);
        }

        public void ShowStockToast(ulong entityId, string text, float seconds = 1.15f)
        {
            if (!_billboardPanels.TryGetValue(entityId, out var panel) || panel.StockToast == null)
                return;

            bool eliminated = _maxStocks > 0 && _getState != null && _getState(entityId).Deaths >= _maxStocks;
            panel.StockToastTitle.text = eliminated ? "OUT" : "KO";
            panel.StockToastDetail.text = text;
            panel.StockToastTimer = panel.StockToastDuration = Mathf.Max(0.01f, seconds);
            panel.StockToast.EnableInClassList("stock-toast-player--out", eliminated);
            panel.StockToast.style.display = DisplayStyle.Flex;
            panel.StockToast.style.opacity = 1f;
            panel.StockToast.style.scale = new Scale(Vector2.one);
            panel.StockToast.style.borderBottomColor = panel.IdentityColor;
        }

        public void SetCamera(UnityEngine.Camera camera)
        {
            _camera = camera;
            if (_textVfx == null)
                _textVfx = gameObject.AddComponent<MatchTextVFX>();
            _textVfx.SetCamera(camera);
        }

        /// <summary>
        /// Print a Room chat line over the speaker's fighter, or beside their card
        /// when the fighter is not on screen. False when the sender is not playing
        /// in this match; the chat overlay then shows the line itself.
        /// </summary>
        public bool ShowChatSpeech(string senderPlayerId, string senderName, string text)
        {
            if (_speech == null || string.IsNullOrEmpty(senderPlayerId) ||
                !_speakers.TryGetValue(senderPlayerId, out ulong entityId) ||
                !_billboardPanels.TryGetValue(entityId, out var panel))
                return false;
            _speech.Show(entityId, senderName, panel.IdentityColor, text, onCard: entityId == _localEntityId);
            return true;
        }

        /// <summary>The fight-card identity color of a chat identity playing in this match.</summary>
        public bool TryGetSpeakerColor(string senderPlayerId, out Color color)
        {
            color = default;
            if (string.IsNullOrEmpty(senderPlayerId) || !_speakers.TryGetValue(senderPlayerId, out ulong entityId) ||
                !_billboardPanels.TryGetValue(entityId, out var panel))
                return false;
            color = panel.IdentityColor;
            return true;
        }

        /// <summary>
        /// Refresh all HUD data from the simulation. Called by the owning MatchBase
        /// each fixed tick. The player cards are updated from simulation state.
        /// </summary>
        public void Refresh()
        {
            if (_getState == null || _uiDocument == null) return;

            foreach (var kv in _billboardPanels)
                UpdatePanel(kv.Value, _getState(kv.Key));
            // Paired move deck — local player only; no ground/air selector.
            if (_localEntityId != 0)
            {
                var state = _getState(_localEntityId);


                for (int i = 0; i < _abilitySlots.Length; i++)
                {
                    var slot = _abilitySlots[i];
                    SetSlotPresentation(slot, state.IsGrounded);
                    // The key stays pressed while its move runs, including repeated
                    // casts, and while its returning projectile is out; the cooldown
                    // only starts when the move ends.
                    bool inUse = state.AttackSlot == AbilitySlotDefs[i].SlotIndex + 1
                        || state.ReturningProjectileSlot == AbilitySlotDefs[i].SlotIndex + 1;
                    slot.Root.EnableInClassList("in-use", inUse);
                    if (inUse && slot.PulseTimer > 0f)
                    {
                        slot.PulseTimer = 0f;
                        slot.Root.RemoveFromClassList("ready-pulse");
                    }
                    if (slot.Locked) continue;
                    UpdateCooldownSlot(slot, state.GetCooldown((byte)(AbilitySlotDefs[i].SlotIndex + 1)));
                }
            }
        }

        private void UpdateCooldownSlot(ActionSlot s, ushort cooldown)
        {
            bool onCooldown = cooldown > 0;

            s.Cooldown.style.display = onCooldown ? DisplayStyle.Flex : DisplayStyle.None;
            s.Timer.style.display = onCooldown ? DisplayStyle.Flex : DisplayStyle.None;
            s.Root.EnableInClassList("on-cooldown", onCooldown);
            if (onCooldown)
            {
                float remaining = s.MaxCooldown > 0 ? Mathf.Clamp01(cooldown / (float)s.MaxCooldown) : 1f;
                // The sunk key refills from the bottom as the cooldown elapses.
                s.Cooldown.style.height = Length.Percent((1f - remaining) * 100f);
                // Whole seconds from 3 s up, tenths below; text changes only with the shown value.
                bool wholeSeconds = cooldown >= 180;
                int shown = wholeSeconds ? 1000 + Mathf.CeilToInt(cooldown / 60f) : Mathf.CeilToInt(cooldown / 6f);
                if (shown != s.TimerValue)
                {
                    s.TimerValue = shown;
                    s.Timer.text = wholeSeconds ? (shown - 1000).ToString() : (shown / 10f).ToString("0.0");
                }
            }
            else
            {
                s.TimerValue = -1;
            }

            // One short pop when the key becomes ready again.
            if (s.PrevCooldown > 0 && cooldown == 0)
            {
                s.PulseTimer = ReadyDuration;
                s.Root.EnableInClassList("ready-pulse", true);
            }
            else if (onCooldown)
            {
                s.PulseTimer = 0f;
                s.Root.RemoveFromClassList("ready-pulse");
            }
            // Press feedback comes from the in-use class in Refresh: the cooldown only
            // starts at ability END, so it cannot signal the press itself.
            s.PrevCooldown = cooldown;
        }



        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            _soloCountdown?.Tick(Time.deltaTime);
            _soloEndCard?.Tick(dt);
            if (_abilitySlots.Length > 0 &&
                (_seenBindingRevision != HumanInputActions.BindingRevision ||
                 _showGamepadPrompts != (HumanInputActions.LastUsedGamepad && Gamepad.current != null)))
                UpdateBindingPrompts();
            UpdateNetworkStats();
            UpdateModifierFeedback();
            FitMoveDeck();

            for (int i = 0; i < _abilitySlots.Length; i++) TickSlotJuice(_abilitySlots[i], dt);

            foreach (var panel in _billboardPanels.Values)
                TickPanelJuice(panel, dt);
        }

        private void LateUpdate()
        {
            _speech?.LateTick(Time.unscaledDeltaTime,
                _camera != null ? _camera : UnityEngine.Camera.main,
                ClientSettingsService.Instance.ReducedFlashing);
        }

        private void OnDisable()
        {
            _soloCountdown?.Clear();
            _soloEndCard?.Clear();
            _speech?.Clear();
        }

        private void UpdateModifierFeedback()
        {
            if (_moveDeck == null) return;
            bool held = _showGamepadPrompts && HumanInputActions.Get("SpecialModifier").IsPressed() &&
                !ChatInputGate.SuppressGameplay && !HumanInputActions.IsCapturing &&
                !(SettingsOverlay.Active is { IsOpen: true }) &&
                !(MatchPauseMenu.Active is { IsPaused: true });
            if (held == _modifierHeld) return;
            _modifierHeld = held;
            _moveDeck.EnableInClassList("modifier-held", held);
        }

        private void FitMoveDeck()
        {
            if (_moveDeck == null || _movePairs == null || _moveDeck.panel == null) return;
            float width = _moveDeck.resolvedStyle.width;
            if (float.IsNaN(width) || width <= 0f) return;
            const float preferredWidth = 304f;
            bool rightAligned = _billboardLayer != null && _billboardLayer.childCount > 0 &&
                _moveDeck.worldBound.yMin < _billboardLayer.worldBound.yMax + 20f &&
                (width - preferredWidth) * 0.5f < _billboardLayer.worldBound.xMax + 20f;
            float availableWidth = rightAligned
                ? width - _billboardLayer.worldBound.xMax - 38f
                : width - 96f;
            float scale = Mathf.Clamp(availableWidth / preferredWidth, 0.5f, 1f);
            if (rightAligned != _moveDeckRightAligned)
            {
                _moveDeckRightAligned = rightAligned;
                _moveDeck.style.justifyContent = rightAligned ? Justify.FlexEnd : Justify.Center;
                _moveDeck.style.paddingRight = rightAligned ? 18f : 0f;
            }
            if (Mathf.Abs(scale - _moveDeckScale) > 0.001f)
            {
                _moveDeckScale = scale;
                _movePairs.style.scale = new Scale(Vector2.one * scale);
            }
        }

        private void UpdateNetworkStats()
        {
            if (_networkStatsLabel == null) return;
            int mode = ClientSettingsService.Instance.NetworkStats;
            _networkStatsLabel.style.display = mode == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            if (mode == 0) return;
            if (_networkClient == null)
            {
                _networkStatsLabel.text = "NETWORK: OFFLINE";
                return;
            }
            if (!_networkClient.IsServerConnected)
            {
                _networkStatsLabel.text = "NETWORK: DISCONNECTED";
                return;
            }
            float? ping = _networkClient.LastPingMilliseconds;
            _networkStatsLabel.text = !ping.HasValue
                ? "PING: MEASURING…"
                : mode == 1
                    ? $"PING: {ping.Value:F0} ms"
                    : $"PING: {ping.Value:F0} ms\nSERVER: {_networkClient.ServerEndpoint}\nTICK: {_networkClient.LastPingServerTick}";
        }

        private static void TickPanelJuice(OverheadPanel panel, float dt)
        {
            bool flashing = panel.HitFlashTimer > 0f && !ClientSettingsService.Instance.ReducedFlashing;
            if (panel.HitFlashTimer > 0f) panel.HitFlashTimer -= dt;
            panel.Damage.style.color = flashing ? Color.white : panel.TierColor;
            panel.Damage.EnableInClassList("damage-hit", flashing);

            if (panel.StockToastTimer > 0f)
            {
                panel.StockToastTimer -= dt;
                float elapsed = panel.StockToastDuration - panel.StockToastTimer;
                float scale = elapsed < 0.1f ? Mathf.Lerp(0.88f, 1.04f, elapsed / 0.1f)
                    : elapsed < 0.18f ? Mathf.Lerp(1.04f, 1f, (elapsed - 0.1f) / 0.08f) : 1f;
                panel.StockToast.style.scale = new Scale(Vector2.one *
                    (ClientSettingsService.Instance.ReducedFlashing ? 1f : scale));
                panel.StockToast.style.opacity = Mathf.Clamp01(panel.StockToastTimer / 0.18f);
                if (panel.StockToastTimer <= 0f)
                {
                    panel.StockToast.style.opacity = 0f;
                    panel.StockToast.style.display = DisplayStyle.None;
                }
            }
        }

        private static void TickSlotJuice(ActionSlot s, float dt)
        {
            s.Root.EnableInClassList("quiet-feedback", ClientSettingsService.Instance.ReducedFlashing);
            if (s.PulseTimer > 0f)
            {
                s.PulseTimer -= dt;
                s.Root.EnableInClassList("ready-pulse", s.PulseTimer > 0f);
            }
        }


        // ── Small helpers ───────────────────────────────────────────────────


        /// <summary>Damage-percent tier colors (spec §3.1): white &lt;40, orange 40-89, crimson 90+.</summary>
        internal static Color DamageColor(int percent)
            => percent < 40 ? Color.white : percent < 90 ? OrangeDamage : CrimsonDamage;
    }
}
