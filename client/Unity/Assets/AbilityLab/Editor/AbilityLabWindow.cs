using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using SlopArena.Client.Tools;
using SlopArena.Client.Animation;
using SlopArena.Shared;
using SlopArena.Client.World;

namespace SlopArena.EditorTools;

public sealed partial class AbilityLabWindow : EditorWindow
{
    private sealed class PackageOption
    {
        public PackageOption(string packageId, string displayName, IReadOnlyList<CharacterDiagnostic> diagnostics)
        {
            PackageId = packageId;
            DisplayName = displayName;
            Diagnostics = diagnostics;
        }

        public string PackageId { get; }
        public string DisplayName { get; }
        public IReadOnlyList<CharacterDiagnostic> Diagnostics { get; }
    }

    private sealed class AnimationChoice
    {
        public AnimationChoice(string semanticId, string label)
        {
            SemanticId = semanticId;
            Label = label;
        }

        public string SemanticId { get; }
        public string Label { get; }
    }

    private AbilityLab? _lab;
    [SerializeField] private string _reloadSlot = "";
    [SerializeField] private string _reloadPackageId = "";
    [SerializeField] private int _reloadStage, _reloadTick;
    [SerializeField] private bool _reloadPlaying, _restoreReloadCursor;
    private bool _refreshAfterCompilation;
    [NonSerialized] private bool _waitingForAssemblyReload;
    private AbilityLabPackageWorkspace _workspace = new();
    private AbilityLabPackagePreviewResult? _preview;
    private readonly List<PackageOption> _packages = new();
    private readonly Dictionary<string, PackageOption> _packagesByDisplay = new(StringComparer.Ordinal);
    private VisualElement _root = null!;
    private DropdownField _packageSelector = null!;
    private string _activePage = "moves-page";
    private Button _packageStatusToggle = null!;
    private ScrollView _diagnosticsPanel = null!;
    private VisualElement _moveSelector = null!;
    private VisualElement _moveList = null!;
    private VisualElement _groundAirSelector = null!;
    private Button _groundMovesButton = null!;
    private Button _airMovesButton = null!;
    private VisualElement _inspector = null!;
    private VisualElement _moveTimeline = null!;
    private Label _timelineTick = null!;
    private Button _timelinePlay = null!;
    private SliderInt _timelineSlider = null!;
    private Label _timelineDuration = null!;
    private DropdownField _stageSelector = null!;
    private AbilityLabTimelineElement _timelineTrack = null!;
    private AbilityLabTimelineProjection? _timelineProjection;
    private Slider _timelineZoom = null!;
    private ScrollView _timelineScroll = null!;
    private string _timelineProjectionSlotId = "";
    private CharacterAuthoringDocument? _timelineProjectionDraft;
    private AbilityLabOperationProjection? _selectedOperation;
    private int _inspectedStageIndex;
    private int _selectedHitboxSourceIndex = -1;
    private string _timelineProjectionPackageRoot = "";
    private IntegerField _timelineSeek = null!;
    private Label _timelineNotice = null!;
    private VisualElement _fieldsInlineHost = null!;
    private Button _detachFields = null!;
    [SerializeField] private AbilityLabInspectorWindow? _fieldsWindow;
    private TwoPaneSplitView _movesSplit = null!;
    private bool _movesFieldsCollapsed;
    private bool _showSelectedEffectFields;
    private readonly Dictionary<string, VisualElement> _pages = new(StringComparer.Ordinal);
    private bool _airborneSelector;
    private bool _grabSelected;
    private bool _grabPriorShowHitboxes;
    private VisualElement _characterGeneral = null!;
    private VisualElement _characterMovement = null!;
    private VisualElement _movementGround = null!;
    private VisualElement _movementAir = null!;
    private VisualElement _movementJump = null!;
    private VisualElement _movementFalling = null!;
    private VisualElement _characterPresentation = null!;
    private VisualElement _characterHurtboxes = null!;
    private Foldout _characterHurtboxCapsules = null!;
    private Foldout _characterHurtboxBones = null!;
    private Label _characterUnavailable = null!;
    private VisualElement _assetsPage = null!;
    private ObjectField _assetsRigField = null!;
    private Label _assetsRigStatus = null!;
    private VisualElement _assetsSkeleton = null!;
    private VisualElement _assetsWeaponTrails = null!;
    private VisualElement _assetsLocomotionBindings = null!;
    private VisualElement _assetsHitReactionBindings = null!;
    private VisualElement _assetsMoveBindings = null!;
    private VisualElement _assetsPresentationBindings = null!;
    private TextField _presentationAssetId = null!;
    private ObjectField _presentationAssetPrefab = null!;
    private Button _presentationAssetAdd = null!;
    private Label _presentationAssetStatus = null!;
    private VisualElement _assetsValidation = null!;
    private VisualElement _advancedPackagePaths = null!;
    private Label _advancedSourcePath = null!;
    private Label _advancedCookedPath = null!;
    private VisualElement _advancedHashes = null!;
    private VisualElement _advancedRawIds = null!;
    private VisualElement _advancedDiagnostics = null!;
    private VisualElement _advancedProvenance = null!;
    private VisualElement _advancedSchemaProfile = null!;
    private TextField _advancedRenameOld = null!;
    private TextField _advancedRenameNew = null!;
    private Label _advancedRenameStatus = null!;
    private Button _advancedRenameConfirm = null!;
    private Button _advancedMigrateAuthoring = null!;
    private Button _advancedMigrateCatalog = null!;
    private Label _advancedMigrationStatus = null!;
    private bool _sceneRadiusEditing;
    private float _sceneRadiusPending;
    private int _sceneRadiusStageIndex = -1;
    private int _sceneRadiusOperationIndex = -1;
    private bool _scenePresentationEditing;
    private PresentationPlacement _scenePresentationPending = new();
    private int _scenePresentationStageIndex = -1;
    private int _scenePresentationOperationIndex = -1;
    private Label _scenarioAction = null!;
    private FloatField _scenarioDistance = null!;
    private FloatField _scenarioFacing = null!;
    private IntegerField _scenarioDamage = null!;
    private DropdownField _scenarioOpponent = null!;
    private IntegerField _scenarioHorizon = null!;
    private IntegerField _scenarioChargeTicks = null!;
    private Button _scenarioRun = null!;
    private Button _scenarioExit = null!;
    private Label _scenarioOutcomes = null!;
    private bool _refreshingScenarioControls;
    private bool _hadScenario;
    private string _displayedSharedAction = "";
    private CharacterPackageInspectionResult? _inspection;
    private bool _updatingControls;
    private bool _uiReady;
    private bool _suppressInitialPackage;
    private static bool _openingForCommand;

    internal AbilityLabPackageWorkspace CommandWorkspace => _workspace;
    internal AbilityLab? CommandLab => _lab;

    internal static AbilityLabWindow? FindExistingForCommand()
        => Resources.FindObjectsOfTypeAll<AbilityLabWindow>()
            .Where(window => window != null)
            .OrderByDescending(window => window.hasFocus)
            .FirstOrDefault();

    internal static AbilityLabWindow OpenForCommand()
    {
        _openingForCommand = true;
        try
        {
            return GetWindow<AbilityLabWindow>("Ability Lab");
        }
        finally
        {
            _openingForCommand = false;
        }
    }

    internal bool EnsureCommandUi()
    {
        if (!_uiReady || _root == null || !ReferenceEquals(_root, rootVisualElement))
            CreateGUI();
        return _uiReady;
    }

    internal bool TryBuildCommandTimeline(string slotId, out SlotAddress address, out AbilityLabTimelineProjection projection)
    {
        address = default;
        projection = null!;
        if (!CanonicalSlotProjection.TryGet(slotId, out address) ||
            !_workspace.TryResolveCanonicalSlot(slotId, out _, out var sourceSlot))
            return false;
        projection = AbilityLabTimelineProjection.Build(sourceSlot);
        return projection.Stages.Count > 0;
    }

    internal bool EnsureLabForCommand(out AbilityLab? lab, out bool created)
    {
        created = _lab == null;
        EnsureOwnedLab();
        if (created) RefreshAll();
        lab = _lab;
        return lab != null;
    }

    internal void DestroyTemporaryLabForCommand(AbilityLab lab, bool created)
    {
        if (created && _lab == lab)
            DestroyOwnedLab();
    }

    internal void ApplyCommandTimeline(string slotId, SlotAddress address, AbilityLabTimelineProjection projection, int cumulativeTick)
    {
        if (_lab == null) throw new InvalidOperationException("Ability Lab rig is unavailable.");
        bool grabbed = _grabSelected;
        _grabSelected = false;
        try
        {
            _lab.SetSlot(address);
            _timelineProjection = projection;
            _timelineProjectionSlotId = slotId;
            _timelineProjectionDraft = _workspace.Draft;
            ApplyCumulativeTick(cumulativeTick);
        }
        finally
        {
            _grabSelected = grabbed;
        }
    }

    internal void CompleteCommandPreview(SlotAddress address)
    {
        if (_grabSelected && _lab != null) _lab.ShowHitboxes = _grabPriorShowHitboxes;
        _grabSelected = false;
        _selectedOperation = null;
        _airborneSelector = address.IsAirborne;
        UpdateMoveModeButtons();
        UpdateMoveModeButtons();
        BuildMoveButtons(_airborneSelector);
        UpdateTimelineControls();
        RefreshInspector();
    }

    internal void RefreshCommandTimeline()
    {
        if (_uiReady) UpdateTimelineControls();
    }

    [MenuItem("Tools/SLOPArena/Ability Lab")]
    public static void Open() => GetWindow<AbilityLabWindow>("Ability Lab");

    private void OnEnable()
    {
        _suppressInitialPackage = _openingForCommand;
        InitializeRecoveryIdentity();
        _refreshAfterCompilation = true;
        _waitingForAssemblyReload = false;
        AssemblyReloadEvents.beforeAssemblyReload += ReleaseLabBeforeReload;
        UnityEditor.Compilation.CompilationPipeline.compilationStarted += ReleaseLabBeforeCompilation;
        EditorApplication.playModeStateChanged += OnPreviewPlayModeChanged;
        EditorApplication.quitting += DestroyOwnedLab;
    }
    private void OnDisable()
    {
        AssemblyReloadEvents.beforeAssemblyReload -= ReleaseLabBeforeReload;
        UnityEditor.Compilation.CompilationPipeline.compilationStarted -= ReleaseLabBeforeCompilation;
        EditorApplication.playModeStateChanged -= OnPreviewPlayModeChanged;
        EditorApplication.quitting -= DestroyOwnedLab;
        try
        {
            if (!_discardForClose && !_savedForClose) CheckpointWorkspace();
            ReleaseMovesViewport();
            DisposeAttachmentPreview();
            if (_grabSelected && _lab != null) _lab.ShowHitboxes = _grabPriorShowHitboxes;
            _workspace.StatusChanged -= OnWorkspaceStatusChanged;
            _fieldsWindow?.OwnerUnavailable(this);
        }
        finally { DestroyOwnedLab(); }
    }

    public void CreateGUI()
    {
        if (_uiReady && ReferenceEquals(_root, rootVisualElement)) return;
        _uiReady = false;
        string focusedName = CaptureFieldsFocus();
        _root = rootVisualElement;
        _root.Clear();
        var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/AbilityLab/Editor/AbilityLabWindow.uxml");
        if (tree == null)
        {
            _root.Add(new Label("Ability Lab UXML is missing."));
            return;
        }
        tree.CloneTree(_root);
        var stylesheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/AbilityLab/Editor/AbilityLabWindow.uss");
        if (stylesheet != null) _root.styleSheets.Add(stylesheet);
        foreach (var child in _root.Children()) child.style.flexGrow = 1;

        BindElements();
        DiscoverPackages();
        BindTabs();
        BindToolbar();
        BindMovesPage();
        BindMovesViewport();
        BindCharacterPage();
        BindAssetsPage();
        BindPhaseAuthoring();
        BindAdvancedPage();
        RestoreWindowRecovery();
        EnsureOwnedLab();
        string initialPackage = _packages.Any(option => option.PackageId == _reloadPackageId) ? _reloadPackageId : "fightguy";
        if (!_workspace.HasPackage && !_suppressInitialPackage && _packages.Any(option => option.PackageId == initialPackage))
            OpenPackage(initialPackage);
        else
            RefreshAll();
        _workspace.StatusChanged -= OnWorkspaceStatusChanged;
        _workspace.StatusChanged += OnWorkspaceStatusChanged;
        _fieldsWindow?.CreateGUI();
        _uiReady = true;
        RestoreFocus(focusedName);
    }

    private void Update()
    {
        if (_uiReady && _refreshAfterCompilation && !_waitingForAssemblyReload && !EditorApplication.isCompiling)
        {
            _refreshAfterCompilation = false;
            RefreshAll();
            if (_restoreReloadCursor && _lab?.IsPackagePreview == true &&
                CanonicalSlotProjection.TryGet(_reloadSlot, out var address))
            {
                _restoreReloadCursor = false;
                _lab.SetSlot(address);
                int stage = _reloadStage, tick = _reloadTick;
                if (_workspace.TryResolveCanonicalSlot(_reloadSlot, out _, out var sourceSlot) && sourceSlot.Timeline.Stages.Count > 0)
                {
                    stage = Mathf.Clamp(stage, 0, sourceSlot.Timeline.Stages.Count - 1);
                    tick = Mathf.Clamp(tick, 0, Mathf.Max(0, sourceSlot.Timeline.Stages[stage].DurationTicks - 1));
                }
                _lab.SetStage(stage);
                _lab.SetTick((ushort)tick);
                if (_recoveredPhaseActive)
                {
                    _lab.SetAuthoringPhase((AbilityLab.AuthoringPhase)_recoveredPhase);
                    _lab.SetPhasePreview(true);
                    _lab.SetPhaseTick(_recoveredPhaseTick);
                    _recoveredPhaseActive = false;
                }
                _lab.Playing = _reloadPlaying;
            }
            _lab?.RefreshPose();
            UpdateTimelineControls();
        }
        if (_uiReady && EditorApplication.timeSinceStartup >= _nextPersistedStatusCheck)
        {
            _nextPersistedStatusCheck = EditorApplication.timeSinceStartup + 1d;
            if (_workspace.RefreshPersistedStatus())
            {
                RefreshAll();
            }
        }
        if (_lab != null && _lab.Playing)
        {
            UpdateTimelineControls();
            Repaint();
        }
    }

    private double _nextPersistedStatusCheck;

    private void BindElements()
    {
        _packageSelector = Required<DropdownField>("package-selector");
        _packageStatusToggle = Required<Button>("package-status-toggle");
        _diagnosticsPanel = Required<ScrollView>("diagnostics-panel");
        _moveSelector = Required<VisualElement>("move-selector");
        _moveList = Required<VisualElement>("move-list");
        _groundAirSelector = Required<VisualElement>("ground-air-selector");
        _groundMovesButton = Required<Button>("ground-moves-button");
        _airMovesButton = Required<Button>("air-moves-button");
        _inspector = Required<VisualElement>("inspector");
        _moveTimeline = Required<VisualElement>("move-timeline");
        _phaseSelector = Required<DropdownField>("authoring-phase");
        _phaseClipLabel = Required<Label>("authoring-phase-clip");
        _timelineTick = Required<Label>("timeline-tick");
        _timelinePlay = Required<Button>("timeline-play");
        _timelineSlider = Required<SliderInt>("timeline-slider");
        _timelineDuration = Required<Label>("timeline-duration");
        _stageSelector = Required<DropdownField>("stage-selector");
        _timelineSeek = Required<IntegerField>("timeline-seek");
        _timelineNotice = Required<Label>("timeline-notice");
        _fieldsInlineHost = Required<VisualElement>("fields-inline-host");
        _detachFields = Required<Button>("detach-fields");
        _detachFields.clicked += DetachInspectorFields;
        _inspector.RegisterCallback<GeometryChangedEvent>(evt =>
            _inspector.EnableInClassList("fields-narrow", evt.newRect.width < 280));
        _timelineZoom = Required<Slider>("timeline-zoom");
        _timelineScroll = Required<ScrollView>("timeline-scroll");
        _scenarioAction = Required<Label>("scenario-action");
        _scenarioDistance = Required<FloatField>("scenario-distance");
        _scenarioFacing = Required<FloatField>("scenario-facing");
        _scenarioDamage = Required<IntegerField>("scenario-damage");
        _scenarioOpponent = Required<DropdownField>("scenario-opponent");
        _scenarioHorizon = Required<IntegerField>("scenario-horizon");
        _scenarioChargeTicks = Required<IntegerField>("scenario-charge-ticks");
        _scenarioRun = Required<Button>("scenario-run");
        _scenarioExit = Required<Button>("scenario-exit");
        _scenarioOutcomes = Required<Label>("scenario-outcomes");
        var timelinePlaceholder = Required<VisualElement>("timeline-track");
        _timelineTrack = new AbilityLabTimelineElement { name = "timeline-track" };
        _timelineTrack.AddToClassList("timeline-track");
        int timelineIndex = timelinePlaceholder.parent.IndexOf(timelinePlaceholder);
        timelinePlaceholder.parent.Insert(timelineIndex, _timelineTrack);
        timelinePlaceholder.RemoveFromHierarchy();
        _timelineTrack.OperationSelected += SelectOperation;
        _timelineTrack.DragCompleted += CompleteTimelineDrag;
        _timelineTrack.TickScrubbed += ApplyCumulativeTick;
        _root.focusable = true;
        _root.RegisterCallback<KeyDownEvent>(OnRootKeyDown, TrickleDown.TrickleDown);
        foreach (string page in new[] { "moves-page", "character-page", "assets-page", "advanced-page" })
            _pages[page] = Required<VisualElement>(page);
        _characterGeneral = Required<VisualElement>("character-general");
        _characterMovement = Required<VisualElement>("character-movement");
        _movementGround = Required<VisualElement>("movement-ground");
        _assetsPage = Required<VisualElement>("assets-page");
        _assetsRigField = Required<ObjectField>("assets-rig-field");
        _assetsRigStatus = Required<Label>("assets-rig-status");
        _assetsSkeleton = Required<VisualElement>("assets-skeleton");
        _assetsWeaponTrails = Required<VisualElement>("assets-weapon-trails");
        _assetsLocomotionBindings = Required<VisualElement>("assets-locomotion-bindings");
        _assetsHitReactionBindings = Required<VisualElement>("assets-hit-reaction-bindings");
        _assetsMoveBindings = Required<VisualElement>("assets-move-bindings");
        _assetsPresentationBindings = Required<VisualElement>("assets-presentation-bindings");
        _presentationAssetId = Required<TextField>("presentation-asset-id");
        _presentationAssetPrefab = Required<ObjectField>("presentation-asset-prefab");
        _presentationAssetAdd = Required<Button>("presentation-asset-add");
        _presentationAssetStatus = Required<Label>("presentation-asset-status");
        _assetsValidation = Required<VisualElement>("assets-validation");
        _advancedPackagePaths = Required<VisualElement>("advanced-package-paths");
        _advancedSourcePath = Required<Label>("advanced-source-path");
        _advancedCookedPath = Required<Label>("advanced-cooked-path");
        _advancedHashes = Required<VisualElement>("advanced-hashes");
        _advancedRawIds = Required<VisualElement>("advanced-raw-ids");
        _advancedDiagnostics = Required<VisualElement>("advanced-diagnostics");
        _advancedProvenance = Required<VisualElement>("advanced-provenance");
        _advancedSchemaProfile = Required<VisualElement>("advanced-schema-profile");
        _advancedRenameOld = Required<TextField>("advanced-rename-old");
        _advancedRenameNew = Required<TextField>("advanced-rename-new");
        _advancedRenameConfirm = Required<Button>("advanced-rename-confirm");
        _advancedRenameStatus = Required<Label>("advanced-rename-status");
        _advancedMigrateAuthoring = Required<Button>("advanced-migrate-authoring");
        _advancedMigrateCatalog = Required<Button>("advanced-migrate-catalog");
        _advancedMigrationStatus = Required<Label>("advanced-migration-status");
        _movementAir = Required<VisualElement>("movement-air");
        _movementJump = Required<VisualElement>("movement-jump");
        _movementFalling = Required<VisualElement>("movement-falling");
        _characterPresentation = Required<VisualElement>("character-presentation");
        _characterHurtboxes = Required<VisualElement>("character-hurtboxes");
        _characterHurtboxCapsules = Required<Foldout>("character-hurtbox-capsules");
        _characterHurtboxBones = Required<Foldout>("character-hurtbox-bones");
        _characterUnavailable = Required<Label>("character-unavailable");
    }

    private T Required<T>(string name) where T : VisualElement
        => _root.Q<T>(name) ?? throw new InvalidOperationException($"Ability Lab UXML control '{name}' is missing.");

    private void BindTabs()
    {
        BindTab("tab-moves", "moves-page");
        BindTab("tab-character", "character-page");
        BindTab("tab-assets", "assets-page");
        BindTab("tab-advanced", "advanced-page");
        SelectTab("moves-page");
    }

    private void BindTab(string tabName, string pageName)
        => Required<Button>(tabName).clicked += () => SelectTab(pageName);

    private void SelectTab(string pageName)
    {
        _activePage = pageName;
        foreach (var page in _pages)
            page.Value.style.display = page.Key == pageName ? DisplayStyle.Flex : DisplayStyle.None;
        foreach (string tab in new[] { "moves", "character", "assets", "advanced" })
            Required<Button>($"tab-{tab}").EnableInClassList("tab-button-selected", pageName == $"{tab}-page");
        _fieldsWindow?.RefreshAvailability();
    }

    internal bool MovesPageActive => _activePage == "moves-page";
    internal string InspectorContext => _lab == null ? "Moves fields" :
        $"{_workspace.PackageId} · {_lab.SelectedSlotId} · source stage {_inspectedStageIndex + 1}";
    internal bool HasDetachedFields(AbilityLabInspectorWindow window) => _fieldsWindow == window;
    internal void HandleFieldsKeyDown(KeyDownEvent evt) => OnRootKeyDown(evt);

    private void DetachInspectorFields()
    {
        var window = GetWindow<AbilityLabInspectorWindow>(false, "Ability Lab · Fields", false);
        window.BindOwner(this);
    }

    internal void AttachInspectorFields(AbilityLabInspectorWindow window, VisualElement host)
    {
        if (_inspector == null || _fieldsInlineHost == null) return;
        string focusedName = CaptureFieldsFocus();
        _fieldsWindow = window;
        host.Add(_inspector);
        _fieldsInlineHost.style.display = DisplayStyle.None;
        _detachFields.text = "Fields detached";
        UpdateMovesLayout();
        window.RefreshAvailability();
        RestoreFocus(focusedName);
    }

    internal void ReturnInspectorInline(AbilityLabInspectorWindow window)
    {
        if (_fieldsWindow != window) return;
        string focusedName = CaptureFieldsFocus();
        _fieldsWindow = null;
        if (_fieldsInlineHost != null && _inspector != null)
        {
            _fieldsInlineHost.Add(_inspector);
            _fieldsInlineHost.style.display = DisplayStyle.Flex;
            _detachFields.text = "Detach fields";
        }
        UpdateMovesLayout();
        window.RefreshAvailability();
        RestoreFocus(focusedName);
    }


    private void BindToolbar()
    {
        _packageSelector.RegisterValueChangedCallback(evt =>
        {
            if (_updatingControls || !_packagesByDisplay.TryGetValue(evt.newValue, out var option)) return;
            OpenPackage(option.PackageId);
        });
        _packageStatusToggle.clicked += ToggleDiagnostics;
        Required<Button>("diagnostics-toggle").clicked += ToggleDiagnostics;
        Required<Button>("toolbar-undo").clicked += () => { _workspace.Undo(); RefreshAll(); };
        Required<Button>("toolbar-redo").clicked += () => { _workspace.Redo(); RefreshAll(); };
        Required<Button>("toolbar-save").clicked += SaveAndCook;
    }

    private void ToggleDiagnostics()
    {
        if (_diagnosticsPanel.childCount == 0) return;
        _diagnosticsPanel.style.display = _diagnosticsPanel.style.display == DisplayStyle.None
            ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private void BindMovesPage()
    {
        _movesSplit = Required<TwoPaneSplitView>("moves-split");
        _movesFieldsCollapsed = false;
        Required<VisualElement>("ability-lab-root").RegisterCallback<GeometryChangedEvent>(_ => UpdateMovesLayout());
        var sceneColumn = Required<ScrollView>("move-scene-column");
        sceneColumn.RegisterCallback<GeometryChangedEvent>(evt =>
        {
            sceneColumn.EnableInClassList("scene-narrow", evt.newRect.width < 760);
            sceneColumn.contentContainer.style.minHeight = Mathf.Max(330f, evt.newRect.height);
        });
        UpdateMovesLayout();
        _scenarioOpponent.choices = new List<string> { "Idle", "Shield" };
        _scenarioOpponent.SetValueWithoutNotify("Idle");
        _scenarioRun.clicked += RunScenarioFromControls;
        _scenarioExit.clicked += ExitScenarioToAuthoring;
        _scenarioDistance.RegisterValueChangedCallback(_ => InvalidateScenarioControls());
        _scenarioFacing.RegisterValueChangedCallback(_ => InvalidateScenarioControls());
        _scenarioDamage.RegisterValueChangedCallback(_ => InvalidateScenarioControls());
        _scenarioHorizon.RegisterValueChangedCallback(_ => InvalidateScenarioControls());
        _scenarioOpponent.RegisterValueChangedCallback(_ => InvalidateScenarioControls());
        _scenarioChargeTicks.RegisterValueChangedCallback(_ => InvalidateScenarioControls());
        _groundMovesButton.clicked += () => SelectMoveMode(false);
        _airMovesButton.clicked += () => SelectMoveMode(true);
        _timelinePlay.clicked += () =>
        {
            if (_lab == null || (!_lab.IsScenarioPreview && !_lab.IsPackagePreview)) return;
            _lab.Playing = !_lab.Playing;
            UpdateTimelineControls();
            RefreshScenarioControls();
        };
        Required<Button>("timeline-step-back").clicked += () => SetTickDelta(-1);
        Required<Button>("timeline-step-forward").clicked += () => SetTickDelta(1);
        _timelineSlider.RegisterValueChangedCallback(evt =>
        {
            if (!_updatingControls) ApplyCumulativeTick(evt.newValue);
        });
        _timelineSeek.RegisterValueChangedCallback(evt =>
        {
            if (!_updatingControls) ApplyCumulativeTick(evt.newValue);
        });
        _timelineZoom.RegisterValueChangedCallback(evt => SetTimelineZoom(evt.newValue));
        SetTimelineZoom(_timelineZoom.value);
        _stageSelector.RegisterValueChangedCallback(evt =>
        {
            if (!_updatingControls && _lab?.IsScenarioPreview != true &&
                int.TryParse(evt.newValue.Replace("Stage ", ""), out var stage))
            {
                _inspectedStageIndex = stage - 1;
                _selectedOperation = null;
                _selectedHitboxSourceIndex = -1;
                _lab?.SelectHitbox(-1);
                UpdateTimelineControls();
                RefreshInspector();
            }
        });
    }

    private void UpdateMovesLayout()
    {
        if (_movesSplit == null) return;
        float width = _root.resolvedStyle.width;
        bool narrow = width > 0 && width < 760;
        Required<VisualElement>("ability-lab-root").EnableInClassList("workspace-narrow", narrow);
        var orientation = narrow ? TwoPaneSplitViewOrientation.Vertical : TwoPaneSplitViewOrientation.Horizontal;
        if (_movesSplit.orientation != orientation)
        {
            _movesSplit.orientation = orientation;
            _movesSplit.fixedPaneInitialDimension = narrow ? 300 : 340;
        }
        bool detached = _fieldsWindow != null;
        if (detached == _movesFieldsCollapsed) return;
        _movesFieldsCollapsed = detached;
        if (detached) _movesSplit.CollapseChild(1);
        else _movesSplit.UnCollapse();
    }
    private string CurrentSharedAction()
        => _lab == null ? "" : (_lab.Scenario?.Options.Action ?? (_grabSelected ? "grab" : _lab.SelectedAction));

    private void RunScenarioFromControls()
    {
        bool prepared = _lab != null && _workspace.PrepareScenarioPreview();
        if (!prepared) _lab?.MarkPackageDraftInvalid();
        if (_lab == null || !prepared || !_lab.CanRunScenario || string.IsNullOrEmpty(CurrentSharedAction()))
        {
            RefreshScenarioControls();
            _scenarioOutcomes.text = "Open a valid package preview before running a scenario.";
            return;
        }
        if (_scenarioDamage.value < 0 || _scenarioDamage.value > ushort.MaxValue)
        {
            _scenarioOutcomes.text = $"Starting damage must be in [0, {ushort.MaxValue}].";
            return;
        }
        try
        {
            var options = new AbilityLabScenarioOptions(
                CurrentSharedAction(),
                Mathf.Clamp(_scenarioHorizon.value, 0, AbilityLabScenarioOptions.MaxFrame),
                _scenarioDistance.value,
                _scenarioOpponent.value == "Shield" ? AbilityLabOpponentBehavior.Shield : AbilityLabOpponentBehavior.Idle,
                (ushort)_scenarioDamage.value,
                _scenarioFacing.value,
                Mathf.Clamp(_scenarioChargeTicks.value, 0, AbilityLabScenarioOptions.MaxChargeTicks));
            options.Validate();
            _lab.Playing = false;
            _lab.RunScenario(options);
            RefreshScenarioControls();
            SceneView.RepaintAll();
        }
        catch (Exception exception)
        {
            _scenarioOutcomes.text = $"Scenario failed: {exception.Message}";
        }
    }

    private void ExitScenarioToAuthoring()
    {
        if (_lab == null) return;
        bool exitingGrab = _lab.Scenario?.Options.Action == "grab";
        _hadScenario = false;
        if (exitingGrab && _grabSelected)
        {
            _lab.ShowHitboxes = _grabPriorShowHitboxes;
            _grabSelected = false;
            _airborneSelector = _lab.SelectedSlotId.StartsWith("air.", StringComparison.Ordinal);
            UpdateMoveModeButtons();
            BuildMoveButtons(_airborneSelector);
        }
        _lab.ExitScenario();
        RefreshScenarioControls();
        RefreshInspector();
        SceneView.RepaintAll();
    }

    private void InvalidateScenarioControls()
    {
        if (_refreshingScenarioControls || _lab?.Scenario == null) return;
        _hadScenario = false;
        _lab.ExitScenario();
        _scenarioOutcomes.text = "Scenario options changed; run again to record new results.";
        RefreshInspector();
        UpdateTimelineControls();
        SceneView.RepaintAll();
    }

    internal void RefreshScenarioControls()
    {
        if (!_uiReady && _scenarioAction == null) return;
        string action = CurrentSharedAction();
        _scenarioAction.text = string.IsNullOrEmpty(action) ? "Action: —" : $"Action: {action}";
        bool available = _lab != null && _lab.CanRunScenario;
        _scenarioRun.SetEnabled(available);
        bool hasScenario = _lab?.Scenario != null;
        bool scenarioEnded = _hadScenario && !hasScenario;
        bool scenarioStarted = hasScenario && !_hadScenario;
        if (hasScenario) _hadScenario = true;
        _scenarioExit.SetEnabled(hasScenario);
        bool selectionChanged = !string.Equals(action, _displayedSharedAction, StringComparison.Ordinal);
        if (selectionChanged)
        {
            bool grab = action == "grab";
            if (grab != _grabSelected)
            {
                if (grab)
                {
                    _grabPriorShowHitboxes = _lab?.ShowHitboxes ?? false;
                    if (_lab != null) _lab.ShowHitboxes = false;
                }
                else if (_grabSelected && _lab != null)
                {
                    _lab.ShowHitboxes = _grabPriorShowHitboxes;
                }
                _grabSelected = grab;
                _selectedOperation = null;
            }
            _airborneSelector = action.StartsWith("air.", StringComparison.Ordinal);
            _displayedSharedAction = action;
        }
        if (hasScenario && action != "grab" && _selectedOperation != null)
        {
            _selectedOperation = null;
            selectionChanged = true;
        }
        if (scenarioStarted && action != "grab")
        {
            _selectedOperation = null;
            selectionChanged = true;
        }
        _refreshingScenarioControls = true;
        if (hasScenario)
        {
            var result = _lab!.Scenario!;
            var options = result.Options;
            _scenarioDistance.SetValueWithoutNotify(options.Distance);
            _scenarioFacing.SetValueWithoutNotify(options.RelativeFacingDegrees);
            _scenarioDamage.SetValueWithoutNotify(options.OpponentDamage);
            _scenarioOpponent.SetValueWithoutNotify(options.OpponentBehavior == AbilityLabOpponentBehavior.Shield ? "Shield" : "Idle");
            _scenarioHorizon.SetValueWithoutNotify(options.LastFrame);
            _scenarioChargeTicks.SetValueWithoutNotify(options.ChargeTicks);
            var contacts = result.Contacts.Select(contact =>
                $"{(contact.Hit.Blocked ? "Block" : "Hit")} frame {contact.FrameIndex} · {contact.Hit.Damage:0.##}%");
            var interactions = result.Interactions.Select(interaction =>
                $"{interaction.Kind} frame {interaction.FrameIndex} (tick {interaction.MatchTick})");
            var outcomes = contacts.Concat(interactions).ToList();
            if (outcomes.Count == 0) outcomes.Add("No contact or grab interaction observed.");
            outcomes.Add($"{result.PresentationEvents.Count} presentation event(s) · {result.Deaths.Count} death(s)");
            _scenarioOutcomes.text = string.Join("\n", outcomes);
        }
        else if (_hadScenario)
        {
            _scenarioOutcomes.text = "Scenario invalidated by preview changes; run again.";
            _hadScenario = false;
        }
        else if (_lab?.IsScenarioPreview != true &&
                 !_scenarioOutcomes.text.StartsWith("Scenario options changed", StringComparison.Ordinal) &&
                 !_scenarioOutcomes.text.StartsWith("Scenario invalidated", StringComparison.Ordinal) &&
                 !_scenarioOutcomes.text.StartsWith("Scenario failed:", StringComparison.Ordinal))
        {
            _scenarioOutcomes.text = "No scenario run.";
        }
        _refreshingScenarioControls = false;
        if (selectionChanged)
        {
            UpdateMoveModeButtons();
            BuildMoveButtons(_airborneSelector);
            RefreshInspector();
        }
        if (scenarioEnded) RefreshInspector();
        UpdateTimelineControls();
    }
    private void SelectMoveMode(bool airborne)
    {
        if (_updatingControls || _lab == null) return;
        if (_grabSelected) _lab.ShowHitboxes = _grabPriorShowHitboxes;
        _airborneSelector = airborne;
        _grabSelected = false;
        if (CanonicalSlotProjection.TryGet(_lab.SelectedSlotId, out var current) &&
            CanonicalSlotProjection.TryGet(airborne, current.InputLabel, out var target))
            _lab.SetSlot(target);
        UpdateMoveModeButtons();
        UpdateTimelineControls();
        RefreshInspector();
        RefreshScenarioControls();
        BuildMoveButtons(airborne);
    }

    private void UpdateMoveModeButtons()
    {
        _groundMovesButton.EnableInClassList("ground-air-selected", !_airborneSelector);
        _airMovesButton.EnableInClassList("ground-air-selected", false);
        _airMovesButton.EnableInClassList("ground-air-air-selected", _airborneSelector);
    }
    private void BindCharacterPage()
    {
        BindDelayedText("character-display-name", value => CommitGeneral(current => current with { DisplayName = value }));
        BindDelayedFloat("character-weight", value => CommitGeneral(current => current with { Weight = value }));
        BindDelayedFloat("character-capsule-radius", value => CommitGeneral(current => current with { CapsuleRadius = value }));
        BindDelayedFloat("character-capsule-height", value => CommitGeneral(current => current with { CapsuleHeight = value }));
        BindDelayedFloat("character-hip-height", value => CommitGeneral(current => current with { HipHeight = value }));
        BindDelayedFloat("character-hurtbox-radius", value => CommitGeneral(current => current with { HurtboxRadius = value }));

        BindDelayedFloat("movement-run-speed", value => CommitMovement(current => current with { RunSpeed = value }));
        BindDelayedFloat("movement-run-acceleration-a", value => CommitMovement(current => current with { RunAccelerationA = value }));
        BindDelayedFloat("movement-run-acceleration-b", value => CommitMovement(current => current with { RunAccelerationB = value }));
        BindDelayedFloat("movement-dash-speed", value => CommitMovement(current => current with { DashSpeed = value }));
        BindDelayedFloat("movement-ground-friction", value => CommitMovement(current => current with { GroundFriction = value }));
        BindDelayedInteger("movement-dash-duration-ticks", value => CommitMovement(current => current with { DashDurationTicks = ClampUShort(value) }));
        BindDelayedInteger("movement-dash-cooldown-ticks", value => CommitMovement(current => current with { DashCooldownTicks = ClampUShort(value) }));
        BindDelayedInteger("movement-rush-ticks", value => CommitMovement(current => current with { RushTicks = ClampUShort(value) }));

        BindDelayedFloat("movement-air-speed-max", value => CommitMovement(current => current with { AirSpeedMax = value }));
        BindDelayedFloat("movement-air-acceleration-stick", value => CommitMovement(current => current with { AirAccelStick = value }));
        BindDelayedFloat("movement-air-acceleration-base", value => CommitMovement(current => current with { AirAccelBase = value }));
        BindDelayedFloat("movement-air-friction", value => CommitMovement(current => current with { AirFriction = value }));

        BindDelayedFloat("movement-jump-force", value => CommitMovement(current => current with { JumpForce = value }));
        BindDelayedFloat("movement-short-hop-force", value => CommitMovement(current => current with { ShortHopForce = value }));
        BindDelayedFloat("movement-air-jump-vertical-multiplier", value => CommitMovement(current => current with { AirJumpVMultiplier = value }));
        BindDelayedFloat("movement-air-jump-horizontal-multiplier", value => CommitMovement(current => current with { AirJumpHMultiplier = value }));
        BindDelayedInteger("movement-max-jumps", value => CommitMovement(current => current with { MaxJumps = ClampByte(value) }));
        BindDelayedInteger("movement-jump-squat-ticks", value => CommitMovement(current => current with { JumpSquatTicks = ClampUShort(value) }));

        BindDelayedFloat("movement-gravity", value => CommitMovement(current => current with { Gravity = value }));
        BindDelayedFloat("movement-air-float-gravity", value => CommitMovement(current => current with { AirFloatGravity = value }));
        BindDelayedFloat("movement-max-fall-speed", value => CommitMovement(current => current with { MaxFallSpeed = value }));
        BindDelayedFloat("movement-fast-fall-speed", value => CommitMovement(current => current with { FastFallSpeed = value }));
        BindDelayedInteger("movement-float-window-ticks", value => CommitMovement(current => current with { FloatWindowTicks = ClampUShort(value) }));

        BindPresentationSelector("presentation-idle", (current, value) => current with { Idle = value });
        BindPresentationSelector("presentation-run", (current, value) => current with { Run = value });
        BindPresentationSelector("presentation-dash", (current, value) => current with { Dash = value });
        BindPresentationSelector("presentation-jump", (current, value) => current with { Jump = value });
        BindPresentationSelector("presentation-fall", (current, value) => current with { Fall = value });
        BindPresentationSelector("presentation-tumble", (current, value) => current with { Tumble = value });
        BindPresentationSelector("presentation-hit-small", (current, value) => current with { HitSmall = value });
        BindPresentationSelector("presentation-hit-medium", (current, value) => current with { HitMedium = value });
        BindPresentationSelector("presentation-hit-hard", (current, value) => current with { HitHard = value });
        BindDelayedFloat("presentation-land-start-offset-seconds", value => CommitPresentation(current => current with { LandStartOffsetSeconds = value }));
        BindDelayedText("presentation-model-resource-path", value => CommitPresentation(current => current with { ModelResourcePath = value }));
        BindDelayedFloat("presentation-visual-scale", value => CommitPresentation(current => current with { VisualScale = value }));
        BindDelayedFloat("presentation-hurtbox-bone-scale", value => CommitPresentation(current => current with { HurtboxBoneScale = value }));
        BindDelayedFloat("presentation-model-y-offset", value => CommitPresentation(current => current with { ModelYOffset = value }));
        BindDelayedFloat("presentation-model-sole-offset", value => CommitPresentation(current => current with { ModelSoleOffset = value }));
        Required<Toggle>("presentation-auto-model-y-offset").RegisterValueChangedCallback(evt =>
        {
            if (!_updatingControls && _workspace.HasPackage)
                CommitPresentation(current => current with { AutoModelYOffset = evt.newValue });
        });
    }
    private void BindAssetsPage()
    {
        _assetsRigField.objectType = typeof(GameObject);
        _assetsRigField.allowSceneObjects = false;
        _assetsRigField.RegisterValueChangedCallback(evt =>
        {
            if (_updatingControls || !_workspace.HasPackage) return;
            _workspace.ReplaceCatalogRig(evt.newValue as GameObject);
            RefreshAll();
        });
        _presentationAssetPrefab.objectType = typeof(GameObject);
        _presentationAssetPrefab.allowSceneObjects = false;
        _presentationAssetAdd.clicked += () =>
        {
            if (_updatingControls || !_workspace.HasPackage) return;
            bool accepted = _workspace.AddPresentationAsset(
                _presentationAssetId.value,
                _presentationAssetPrefab.value as GameObject);
            _presentationAssetStatus.text = accepted
                ? "Added; save and cook to publish the presentation asset."
                : _workspace.Diagnostics.LastOrDefault()?.Message ?? "Presentation asset rejected; see diagnostics.";
            if (accepted)
            {
                _presentationAssetId.value = "";
                _presentationAssetPrefab.value = null;
            }
            RefreshAll();
        };
    }

    private void BindAdvancedPage()
    {
        _advancedRenameConfirm.clicked += () =>
            ConfirmAndRenameSemanticId(
                _advancedRenameOld.value,
                _advancedRenameNew.value,
                () => EditorUtility.DisplayDialog(
                    "Rename semantic ID",
                    $"Rename '{_advancedRenameOld.value}' to '{_advancedRenameNew.value}' across the source document and asset catalog?",
                    "Rename",
                    "Cancel"));
        _advancedMigrateAuthoring.clicked += () =>
            _advancedMigrationStatus.text = "Current; no migration required";
        _advancedMigrateCatalog.clicked += () =>
            _advancedMigrationStatus.text = "Current; no migration required";
    }

    internal bool ConfirmAndRenameSemanticId(string oldId, string newId, Func<bool> confirm)
    {
        if (_updatingControls || !_workspace.HasPackage || confirm == null || !confirm()) return false;
        bool renamed = _workspace.RenameSemanticId(oldId, newId);
        _advancedRenameStatus.text = renamed ? "Renamed; save and cook to publish the change." : "Rename rejected; see diagnostics.";
        RefreshAll();
        return renamed;
    }

    private void BindDelayedText(string name, Action<string> commit)
    {
        var field = Required<TextField>(name);
        field.isDelayed = true;
        field.RegisterValueChangedCallback(evt =>
        {
            if (!_updatingControls && _workspace.HasPackage) commit(evt.newValue);
        });
    }

    private void BindDelayedFloat(string name, Action<float> commit)
    {
        var field = Required<FloatField>(name);
        field.isDelayed = true;
        field.RegisterValueChangedCallback(evt =>
        {
            if (!_updatingControls && _workspace.HasPackage) commit(evt.newValue);
        });
    }

    private void BindDelayedInteger(string name, Action<int> commit)
    {
        var field = Required<IntegerField>(name);
        field.isDelayed = true;
        field.RegisterValueChangedCallback(evt =>
        {
            if (!_updatingControls && _workspace.HasPackage) commit(evt.newValue);
        });
    }

    private void BindPresentationSelector(string name,
        Func<CharacterPresentationSource, string, CharacterPresentationSource> write)
    {
        var field = Required<DropdownField>(name);
        field.RegisterValueChangedCallback(evt =>
        {
            if (_updatingControls || !_workspace.HasPackage) return;
            var choices = BuildAnimationChoices(PresentationSemanticIds());
            var choice = choices.FirstOrDefault(item => item.Label == evt.newValue);
            if (choice != null)
                CommitPresentation(current => write(current, choice.SemanticId));
        });
    }

    private IEnumerable<string> PresentationSemanticIds()
    {
        if (_preview?.AnimationCatalog?.Animations != null)
            foreach (var entry in _preview.AnimationCatalog.Animations)
                if (entry != null) yield return entry.SemanticId;
        if (_workspace.HasPackage)
        {
            var presentation = _workspace.Draft.Presentation;
            yield return presentation.Idle;
            yield return presentation.Run;
            yield return presentation.Dash;
            yield return presentation.Jump;
            yield return presentation.Fall;
            yield return presentation.Tumble;
            yield return presentation.HitSmall;
            yield return presentation.HitMedium;
            yield return presentation.HitHard;
        }
    }

    private void CommitGeneral(Func<CharacterAuthoringDocument, CharacterAuthoringDocument> edit)
    {
        if (_updatingControls || !_workspace.HasPackage) return;
        var current = edit(_workspace.Draft);
        if (_workspace.ReplaceGeneral(current.DisplayName, current.Weight, current.CapsuleRadius, current.CapsuleHeight, current.HipHeight, current.HurtboxRadius))
            RefreshCharacterPage();
    }

    private void CommitMovement(Func<CharacterMovementSource, CharacterMovementSource> edit)
    {
        if (_updatingControls || !_workspace.HasPackage) return;
        if (_workspace.ReplaceMovement(edit(_workspace.Draft.Movement)))
            RefreshCharacterPage();
    }

    private void CommitGrab(Func<CharacterCaptureGeometrySource, CharacterCaptureGeometrySource> edit)
    {
        if (_updatingControls || !_workspace.HasPackage) return;
        if (_workspace.ReplaceCaptureGeometry(edit(_workspace.Draft.CaptureGeometry)))
            SceneView.RepaintAll();
    }

    private void CommitPresentation(Func<CharacterPresentationSource, CharacterPresentationSource> edit)
    {
        if (_updatingControls || !_workspace.HasPackage) return;
        if (_workspace.ReplacePresentation(edit(_workspace.Draft.Presentation)))
            RefreshCharacterPage();
    }

    private void RefreshCharacterPage()
    {
        bool available = _workspace.HasPackage;
        _characterUnavailable.style.display = available ? DisplayStyle.None : DisplayStyle.Flex;
        _characterGeneral.SetEnabled(available);
        _characterMovement.SetEnabled(available);
        _characterPresentation.SetEnabled(available);
        _characterHurtboxes.SetEnabled(available);
        if (!available) return;

        var character = _workspace.Draft;
        SetText("character-display-name", character.DisplayName);
        SetFloat("character-weight", character.Weight);
        SetFloat("character-capsule-radius", character.CapsuleRadius);
        SetFloat("character-capsule-height", character.CapsuleHeight);
        SetFloat("character-hip-height", character.HipHeight);
        SetFloat("character-hurtbox-radius", character.HurtboxRadius);

        var movement = character.Movement;
        SetFloat("movement-run-speed", movement.RunSpeed);
        SetFloat("movement-run-acceleration-a", movement.RunAccelerationA);
        SetFloat("movement-run-acceleration-b", movement.RunAccelerationB);
        SetFloat("movement-dash-speed", movement.DashSpeed);
        SetFloat("movement-ground-friction", movement.GroundFriction);
        SetInteger("movement-dash-duration-ticks", movement.DashDurationTicks);
        SetInteger("movement-dash-cooldown-ticks", movement.DashCooldownTicks);
        SetInteger("movement-rush-ticks", movement.RushTicks);
        SetFloat("movement-air-speed-max", movement.AirSpeedMax);
        SetFloat("movement-air-acceleration-stick", movement.AirAccelStick);
        SetFloat("movement-air-acceleration-base", movement.AirAccelBase);
        SetFloat("movement-air-friction", movement.AirFriction);
        SetFloat("movement-jump-force", movement.JumpForce);
        SetFloat("movement-short-hop-force", movement.ShortHopForce);
        SetFloat("movement-air-jump-vertical-multiplier", movement.AirJumpVMultiplier);
        SetFloat("movement-air-jump-horizontal-multiplier", movement.AirJumpHMultiplier);
        SetInteger("movement-max-jumps", movement.MaxJumps);
        SetInteger("movement-jump-squat-ticks", movement.JumpSquatTicks);
        SetFloat("movement-gravity", movement.Gravity);
        SetFloat("movement-air-float-gravity", movement.AirFloatGravity);
        SetFloat("movement-max-fall-speed", movement.MaxFallSpeed);
        SetFloat("movement-fast-fall-speed", movement.FastFallSpeed);
        SetInteger("movement-float-window-ticks", movement.FloatWindowTicks);

        var presentation = character.Presentation;
        var animationChoices = BuildAnimationChoices(PresentationSemanticIds());
        SetAnimation("presentation-idle", presentation.Idle, animationChoices);
        SetAnimation("presentation-run", presentation.Run, animationChoices);
        SetAnimation("presentation-dash", presentation.Dash, animationChoices);
        SetAnimation("presentation-jump", presentation.Jump, animationChoices);
        SetAnimation("presentation-fall", presentation.Fall, animationChoices);
        SetAnimation("presentation-tumble", presentation.Tumble, animationChoices);
        SetAnimation("presentation-hit-small", presentation.HitSmall, animationChoices);
        SetAnimation("presentation-hit-medium", presentation.HitMedium, animationChoices);
        SetAnimation("presentation-hit-hard", presentation.HitHard, animationChoices);
        SetFloat("presentation-land-start-offset-seconds", presentation.LandStartOffsetSeconds);
        SetText("presentation-model-resource-path", presentation.ModelResourcePath);
        SetFloat("presentation-visual-scale", presentation.VisualScale);
        SetFloat("presentation-hurtbox-bone-scale", presentation.HurtboxBoneScale);
        SetFloat("presentation-model-y-offset", presentation.ModelYOffset);
        SetFloat("presentation-model-sole-offset", presentation.ModelSoleOffset);
        Required<Toggle>("presentation-auto-model-y-offset").SetValueWithoutNotify(presentation.AutoModelYOffset);
        RefreshHurtboxes(character);
    }

    private void RefreshHurtboxes(CharacterAuthoringDocument character)
    {
        _characterHurtboxCapsules.Clear();
        var capsules = character.HurtboxCapsules ?? Array.Empty<HurtboxCapsuleSource>();
        for (int index = 0; index < capsules.Count; index++)
        {
            var capsule = capsules[index];
            _characterHurtboxCapsules.Add(new Label(
                $"Capsule {index + 1} · Start ({Number(capsule.StartX)}, {Number(capsule.StartY)}, {Number(capsule.StartZ)}) · " +
                $"End ({Number(capsule.EndX)}, {Number(capsule.EndY)}, {Number(capsule.EndZ)}) · Radius {Number(capsule.Radius)}"));
        }
        if (capsules.Count == 0) _characterHurtboxCapsules.Add(new Label("No authored capsule hurtboxes."));

        _characterHurtboxBones.Clear();
        var bones = character.HurtboxBoneDefs ?? Array.Empty<HurtboxBoneSource>();
        for (int index = 0; index < bones.Count; index++)
        {
            var bone = bones[index];
            _characterHurtboxBones.Add(new Label(
                $"Bone {index + 1} · {bone.BoneId} · Offset ({Number(bone.OffsetX)}, {Number(bone.OffsetY)}, {Number(bone.OffsetZ)}) · Radius {Number(bone.Radius)}"));
        }
        if (bones.Count == 0) _characterHurtboxBones.Add(new Label("No authored bone hurtboxes."));
    }

    private void SetAnimation(string name, string semanticId, IReadOnlyList<AnimationChoice> choices)
    {
        var field = Required<DropdownField>(name);
        field.choices = choices.Select(choice => choice.Label).ToList();
        var selected = choices.FirstOrDefault(choice => choice.SemanticId == semanticId);
        field.SetValueWithoutNotify(selected?.Label ?? $"Unknown ({semanticId})");
    }

    private List<AnimationChoice> BuildAnimationChoices(IEnumerable<string> semanticIds)
    {
        var entries = (_preview?.AnimationCatalog?.Animations ?? Array.Empty<CharacterAnimationCatalog.AnimationEntry>())
            .Where(entry => entry != null && !string.IsNullOrEmpty(entry.SemanticId))
            .ToList();
        var ids = entries.Select(entry => entry.SemanticId)
            .Concat(semanticIds ?? Array.Empty<string>())
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var bases = ids.ToDictionary(id => id, FriendlyAnimationLabel, StringComparer.Ordinal);
        var duplicateBases = bases.Values.GroupBy(label => label, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<AnimationChoice>(ids.Count);
        foreach (string id in ids)
        {
            string label = duplicateBases.Contains(bases[id]) ? $"{bases[id]} ({id})" : bases[id];
            if (!used.Add(label))
            {
                string disambiguated = $"{label} ({id})";
                int suffix = 2;
                while (!used.Add(disambiguated))
                    disambiguated = $"{label} ({id}) #{suffix++}";
                label = disambiguated;
            }
            result.Add(new AnimationChoice(id, label));
        }
        return result;
    }

    private List<AnimationChoice> BuildPresentationChoices(IEnumerable<string> semanticIds)
    {
        var ids = (_workspace.Catalog?.Presentations ?? Array.Empty<CharacterAssetCatalog.PresentationBinding>())
            .Where(binding => binding != null && !string.IsNullOrEmpty(binding.SemanticId))
            .Select(binding => binding.SemanticId)
            .Concat(semanticIds ?? Array.Empty<string>())
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var choices = new List<AnimationChoice>(ids.Count + 1)
        {
            new AnimationChoice("", "None"),
        };
        choices.AddRange(ids.Select(id => new AnimationChoice(id, FriendlyPresentationLabel(id))));
        return choices;
    }

    private string FriendlyPresentationLabel(string semanticId)
    {
        var binding = (_workspace.Catalog?.Presentations ?? Array.Empty<CharacterAssetCatalog.PresentationBinding>())
            .FirstOrDefault(candidate => candidate != null && candidate.SemanticId == semanticId);
        string prefabName = binding?.Prefab != null && !string.IsNullOrEmpty(binding.Prefab.name)
            ? binding.Prefab.name
            : "Unknown";
        return $"{prefabName} ({semanticId})";
    }

    private string FriendlyAnimationLabel(string semanticId)
    {
        var binding = (_workspace.Catalog?.Bindings ?? Array.Empty<CharacterAssetCatalog.AnimationBinding>())
            .FirstOrDefault(candidate => candidate != null && candidate.SemanticId == semanticId);
        if (binding?.Clip != null && !string.IsNullOrEmpty(binding.Clip.name))
            return binding.Clip.name;
        if (_workspace.HasPackage)
        {
            var presentation = _workspace.Draft.Presentation;
            if (semanticId == presentation.Idle) return "Idle";
            if (semanticId == presentation.Run) return "Run";
            if (semanticId == presentation.Dash) return "Dash";
            if (semanticId == presentation.Jump) return "Jump";
            if (semanticId == presentation.Fall) return "Fall";
            if (semanticId == presentation.Tumble) return "Tumble";
            if (semanticId == presentation.HitSmall) return "HitSmall";
            if (semanticId == presentation.HitMedium) return "HitMedium";
            if (semanticId == presentation.HitHard) return "HitHard";
        }
        return $"Unknown ({semanticId})";
    }

    private void SetText(string name, string value) => Required<TextField>(name).SetValueWithoutNotify(value ?? "");
    private void SetFloat(string name, float value) => Required<FloatField>(name).SetValueWithoutNotify(value);
    private void SetInteger(string name, int value) => Required<IntegerField>(name).SetValueWithoutNotify(value);
    private static ushort ClampUShort(int value) => (ushort)Mathf.Clamp(value, 0, ushort.MaxValue);
    private static byte ClampByte(int value) => (byte)Mathf.Clamp(value, 0, byte.MaxValue);
    private static string Number(float value) => value.ToString("R", CultureInfo.InvariantCulture);


    private void DiscoverPackages()
    {
        _packages.Clear();
        _packagesByDisplay.Clear();
        string packageRoot = Path.Combine(UnityCharacterAssetCooker.ProjectRoot(), "Assets/CharacterPackages");
        if (!Directory.Exists(packageRoot))
        {
            _packages.Add(new PackageOption("", "No source packages", new[]
            {
                Diagnostic("package.discovery.missing", packageRoot, "Assets/CharacterPackages does not exist."),
            }));
        }
        else
        {
            foreach (string directory in Directory.GetDirectories(packageRoot).OrderBy(path => path, StringComparer.Ordinal))
            {
                string directoryName = Path.GetFileName(directory);
                string packagePath = Path.Combine(directory, "package.json");
                string characterPath = Path.Combine(directory, "character.json");
                var diagnostics = new List<CharacterDiagnostic>();
                string packageId = directoryName;
                string displayName = directoryName;
                try
                {
                    using var packageDocument = JsonDocument.Parse(File.ReadAllText(packagePath));
                    packageId = packageDocument.RootElement.GetProperty("packageId").GetString() ?? directoryName;
                    using var characterDocument = JsonDocument.Parse(File.ReadAllText(characterPath));
                    displayName = characterDocument.RootElement.GetProperty("displayName").GetString() ?? packageId;
                    if (!MatchContentCatalogBuilder.IsStablePackageId(packageId))
                        diagnostics.Add(Diagnostic("package.discovery.id-invalid", packagePath, "Source package ID is not stable."));
                }
                catch (Exception ex)
                {
                    diagnostics.Add(Diagnostic("package.discovery.invalid", directory, ex.Message));
                }
                var option = new PackageOption(packageId, displayName, diagnostics);
                _packages.Add(option);
                _packagesByDisplay[Display(option)] = option;
            }
        }
        _packageSelector.choices = _packages.Select(Display).ToList();
    }

    private static string Display(PackageOption option) => string.IsNullOrEmpty(option.PackageId)
        ? option.DisplayName
        : $"{option.DisplayName} ({option.PackageId})";

    private void OpenPackage(string packageId)
    {
        TryOpenPackage(packageId);
        if (_root != null && _packageSelector != null) RefreshAll();
    }

    internal bool OpenPackageForCommand(
        CharacterPackageInspectionResult inspection,
        out string errorCode,
        out string errorMessage)
    {
        errorCode = "";
        errorMessage = "";
        if (!CanCommandOpenPackage(_workspace.HasPackage, _workspace.PackageId, _workspace.HasUnsavedChanges, inspection.PackageId))
        {
            errorCode = "workspace.dirty";
            errorMessage = $"Cannot switch from dirty package '{_workspace.PackageId}' to '{inspection.PackageId}'.";
            return false;
        }
        if (_workspace.HasPackage && _workspace.PackageId == inspection.PackageId)
            return true;
        if (!TryOpenPackage(inspection.PackageId))
        {
            errorCode = "package.open.failed";
            errorMessage = !string.IsNullOrEmpty(RecoveryError) ? RecoveryError
                : string.Join("; ", _workspace.Diagnostics.Select(diagnostic => diagnostic.Message));
            return false;
        }
        return true;
    }

    private void RefreshAll()
    {
        string focusedName = CaptureFieldsFocus();
        bool retainCursor = _lab != null && _timelineProjection != null &&
            _timelineProjectionPackageRoot == _workspace.PackageRoot &&
            _timelineProjectionSlotId == _lab.SelectedSlotId &&
            !_lab.PhasePreviewActive && !_lab.IsScenarioPreview && !_grabSelected;
        int priorTick = retainCursor ? CumulativeTick(_timelineProjection!, _lab!.StageIndex, _lab.Tick) : 0;
        bool wasPlaying = retainCursor && _lab!.Playing;
        EnsureOwnedLab();
        _inspection = _workspace.HasPackage
            ? new CharacterPackageAuthoringService(UnityCharacterAssetCooker.ProjectRoot()).Inspect(_workspace.PackageRoot)
            : null;
        BindPackageSelection();
        RefreshWorkspaceControls();
        RefreshPreview();
        ApplyAttachmentPreview();
        RefreshCharacterPage();
        RefreshAssets();
        RefreshAdvanced();
        RefreshDiagnostics();
        UpdateTimelineControls();
        if (retainCursor && _lab?.IsPackagePreview == true && _timelineProjection != null)
        {
            int restoredTick = Mathf.Clamp(priorTick, 0, Mathf.Max(0, _timelineProjection.DurationTicks - 1));
            if (TryResolveCumulativeTick(_timelineProjection, restoredTick, out var cursorStage, out ushort cursorLocal, out _))
            {
                _lab.SetStage(cursorStage.SourceStageIndex);
                _lab.SetTick(cursorLocal);
            }
            _lab.Playing = wasPlaying;
            UpdateTimelineControls();
            _timelineNotice.text = restoredTick == priorTick ? "" : $"Cursor clamped from {priorTick} to {restoredTick}: move duration changed.";
        }
        RefreshScenarioControls();
        RefreshInspector();
        RefreshMovesViewport();
        RestoreFocus(focusedName);
    }

    internal string CaptureFieldsFocus()
    {
        var element = (_fieldsWindow?.rootVisualElement.panel?.focusController?.focusedElement ??
            _root?.panel?.focusController?.focusedElement) as VisualElement;
        while (element != null)
        {
            if (element.focusable && !string.IsNullOrEmpty(element.name) &&
                !element.name.StartsWith("unity-", StringComparison.Ordinal))
                return element.name;
            element = element.parent;
        }
        return "";
    }

    internal void RestoreFocus(string focusedName)
    {
        if (string.IsNullOrEmpty(focusedName)) return;
        var target = _inspector?.Q<VisualElement>(focusedName) ?? _root.Q<VisualElement>(focusedName);
        if (target != null && target.focusable) target.Focus();
    }


    private void BindPackageSelection()
    {
        string selected = _workspace.HasPackage ? _workspace.PackageId : _packages.FirstOrDefault()?.PackageId ?? "";
        string display = _packages.FirstOrDefault(option => option.PackageId == selected) is { } option ? Display(option) : _packageSelector.choices.FirstOrDefault() ?? "";
        _updatingControls = true;
        _packageSelector.SetValueWithoutNotify(display);
        _updatingControls = false;
    }
    private void RefreshWorkspaceControls()
    {
        RefreshUnsavedChanges();
        _packageStatusToggle.text = PackageStatus();
        Required<Button>("toolbar-undo").SetEnabled(_workspace.CanUndo);
        Required<Button>("toolbar-redo").SetEnabled(_workspace.CanRedo);
        Required<Button>("toolbar-save").SetEnabled(_workspace.HasPackage);
    }

    private string PackageStatus()
    {
        if (!_workspace.HasPackage) return "No package";
        if (_workspace.HasUnsavedChanges) return "Unsaved";
        if (_workspace.Status == "Cooking") return "Cooking…";
        if (_workspace.Status == "Failed" || _workspace.Preview == null || !_workspace.Preview.IsAvailable) return "Cook failed";
        if (_workspace.Status == "Stale") return "Stale";
        return "Cooked";
    }

    private void RefreshPreview()
    {
        bool prepared = _workspace.HasPackage && _workspace.PrepareScenarioPreview();
        _preview = _workspace.Preview;

        if (_workspace.HasPackage && !prepared)
        {
            _lab?.MarkPackageDraftInvalid();
            _moveList.Clear();
            return;
        }
        if (_workspace.LiveDraftPackage != null && _preview?.IsAvailable == true)
        {
            _lab?.ApplyPackageDraftPreview(_workspace.LiveDraftPackage, _preview);
            _lab?.SetPresentationBindings(_workspace.Catalog.Presentations);
            _airborneSelector = _lab?.SelectedSlotId.StartsWith("air.", StringComparison.Ordinal) ?? false;
            UpdateMoveModeButtons();
            BuildMoveButtons(_airborneSelector);
        }
        else if (_workspace.LiveDraftInvalid)
        {
            _lab?.MarkPackageDraftInvalid();
            _moveList.Clear();
        }
        else if (_preview != null && _preview.IsAvailable)
        {
            string priorSlot = _lab?.SelectedSlotId ?? CanonicalSlotProjection.All[0].Id;
            bool previewChanged = _lab != null &&
                (_lab.SelectedPackageId != _preview.Identity.PackageId ||
                 _lab.SelectedPackageHash != _preview.Identity.PackageHash || !_lab.AuthoritativePreview);
            if (_lab != null && previewChanged)
            {
                _lab.ApplyPackagePreview(_preview);
                _lab.SetSourceDocument(new CharacterPackageSource(_workspace.Manifest, _workspace.Draft));
                _lab.SetPresentationBindings(_workspace.Catalog.Presentations);
                if (CanonicalSlotProjection.TryGet(priorSlot, out var priorAddress))
                    _lab.SetSlot(priorAddress);
            }
            UpdateMoveModeButtons();
            BuildMoveButtons(_airborneSelector);
        }
        else
        {
            _moveList.Clear();
        }
        if (_grabSelected && _lab != null)
        {
            _lab.ShowHitboxes = false;
            _lab.Playing = false;
        }
    }

    private void BuildMoveButtons(bool airborne)
    {
        _moveList.Clear();
        foreach (var address in CanonicalSlotProjection.All.Where(slot => slot.IsAirborne == airborne))
        {
            string sourceName = _workspace.TryResolveCanonicalSlot(address.Id, out _, out var sourceSlot)
                ? FriendlyMoveLabel(address, sourceSlot)
                : $"Unknown ({address.Id})";
            var button = new Button(() =>
            {
                if (_grabSelected && _lab != null) _lab.ShowHitboxes = _grabPriorShowHitboxes;
                _grabSelected = false;
                _lab?.SetSlot(address);
                RefreshScenarioControls();
                UpdateTimelineControls();
                RefreshInspector();
                BuildMoveButtons(airborne);
                SceneView.RepaintAll();
            })
            {
                text = MoveButtonLabel(address),
                userData = address,
            };
            button.name = address.Id == "ground.1" ? "selected-ground-1" : address.Id;
            button.AddToClassList("move-slot");
            if (!_grabSelected && _lab?.SelectedSlotId == address.Id)
            {
                button.AddToClassList("move-slot-selected");
                if (airborne) button.AddToClassList("move-slot-air-selected");
            }
            _moveList.Add(button);
        }
        if (!airborne)
        {
            var grab = new Button(() =>
            {
                if (_lab != null)
                {
                    if (!_grabSelected) _grabPriorShowHitboxes = _lab.ShowHitboxes;
                    _lab.ShowHitboxes = false;
                    _lab.Playing = false;
                    if (_lab.CanPreviewGrab) _lab.SelectSharedAction("grab");
                }
                _grabSelected = true;
                _selectedOperation = null;
                RefreshScenarioControls();
                UpdateTimelineControls();
                RefreshInspector();
                BuildMoveButtons(false);
                SceneView.RepaintAll();
            })
            {
                name = "selected-grab",
                text = "Grab",
            };
            grab.AddToClassList("move-slot");
            grab.SetEnabled(_lab?.CanPreviewGrab == true);
            if (_grabSelected) grab.AddToClassList("move-slot-selected");
            _moveList.Add(grab);
        }
    }
    private static string MoveButtonLabel(SlotAddress address)
        => address.InputLabel == "A" ? "Q" : address.InputLabel;

    private void RefreshAssets()
    {
        bool available = _workspace.HasPackage && _workspace.Catalog != null;
        _assetsPage.SetEnabled(available);
        _assetsRigField.SetValueWithoutNotify(available ? _workspace.Catalog.Rig : null);
        _assetsRigStatus.text = !available
            ? "No package or catalog is open."
            : DescribeRig(_workspace.Catalog.Rig);
        _assetsSkeleton.Clear();
        if (available)
        {
            var bakedNames = _lab?.BakedBoneNames ?? Array.Empty<string>();
            var names = bakedNames.Length > 0
                ? bakedNames
                : DeterministicPoseTrackBaker.RequiredBones.Select(bone => bone.ToString()).ToArray();
            foreach (string name in names)
                _assetsSkeleton.Add(new Label(name));
        }

        _assetsWeaponTrails.Clear();
        var weaponConfig = available ? _workspace.Catalog.WeaponConfig : null;
        if (weaponConfig?.Entries != null)
        {
            var serialized = new SerializedObject(weaponConfig);
            for (int i = 0; i < weaponConfig.Entries.Length; i++)
            {
                var entry = weaponConfig.Entries[i];
                if (entry == null) continue;
                int entryIndex = i;
                var style = new ObjectField($"Trail style · {entry.BoneName}")
                {
                    name = $"trail-style-{i}",
                    objectType = typeof(GameObject),
                    allowSceneObjects = false,
                    value = entry.TrailStylePrefab,
                    tooltip = "Vendor sword trail prefab supplying material, dissolve, background and glints. Saves to the weapon asset.",
                };
                style.RegisterValueChangedCallback(evt =>
                {
                    if (_updatingControls) return;
                    if (!_workspace.ReplaceTrailStyle(entryIndex, evt.newValue as GameObject))
                    {
                        style.SetValueWithoutNotify(entry.TrailStylePrefab);
                        return;
                    }
                    _lab?.RefreshPose();
                    RefreshWorkspaceControls();
                    SceneView.RepaintAll();
                });
                style.TrackPropertyValue(
                    serialized.FindProperty($"Entries.Array.data[{i}].TrailStylePrefab"), property =>
                    {
                        style.SetValueWithoutNotify(property.objectReferenceValue);
                        _lab?.RefreshPose();
                        SceneView.RepaintAll();
                    });
                _assetsWeaponTrails.Add(style);
                var width = new Slider($"Blade width · {entry.BoneName}", 0.01f, 1f)
                {
                    name = $"trail-blade-width-{i}",
                    showInputField = true,
                    value = entry.TrailBladeWidth,
                    tooltip = "Fraction of blade length inward from the tip. Updates live and edits the weapon asset; no preview-only override.",
                };
                width.RegisterValueChangedCallback(evt =>
                {
                    if (_updatingControls || !_workspace.ReplaceTrailBladeWidth(entryIndex, evt.newValue)) return;
                    _lab?.RefreshPose();
                    RefreshWorkspaceControls();
                    SceneView.RepaintAll();
                });
                width.TrackPropertyValue(
                    serialized.FindProperty($"Entries.Array.data[{i}].TrailBladeWidth"), property =>
                    {
                        width.SetValueWithoutNotify(property.floatValue);
                        _lab?.RefreshPose();
                        SceneView.RepaintAll();
                    });
                _assetsWeaponTrails.Add(width);
            }
        }
        if (_assetsWeaponTrails.childCount == 0)
            _assetsWeaponTrails.Add(new Label("No weapon trail configured for this package."));

        _assetsLocomotionBindings.Clear();
        var presentation = available ? _workspace.Draft.Presentation : null;
        if (presentation != null)
        {
            AddBindingRow(_assetsLocomotionBindings, "Idle", presentation.Idle);
            AddBindingRow(_assetsLocomotionBindings, "Run", presentation.Run);
            AddBindingRow(_assetsLocomotionBindings, "Dash", presentation.Dash);
            AddBindingRow(_assetsLocomotionBindings, "Jump", presentation.Jump);
            AddBindingRow(_assetsLocomotionBindings, "Fall", presentation.Fall);
        }

        _assetsHitReactionBindings.Clear();
        if (presentation != null)
        {
            AddBindingRow(_assetsHitReactionBindings, "Tumble", presentation.Tumble);
            AddBindingRow(_assetsHitReactionBindings, "HitSmall", presentation.HitSmall);
            AddBindingRow(_assetsHitReactionBindings, "HitMedium", presentation.HitMedium);
            AddBindingRow(_assetsHitReactionBindings, "HitHard", presentation.HitHard);
        }

        _assetsMoveBindings.Clear();
        if (available)
        {
            foreach (var address in CanonicalSlotProjection.All)
            {
                if (!_workspace.TryResolveCanonicalSlot(address.Id, out _, out var sourceSlot))
                {
                    var unknown = BuildBindingRow(address.Id, $"{address.InputLabel} · Unknown ({address.Id})", null);
                    unknown.tooltip = address.Id;
                    _assetsMoveBindings.Add(unknown);
                    continue;
                }
                bool disambiguate = sourceSlot.Timeline.Stages.Count > 1 ||
                    sourceSlot.Timeline.Stages.Any(stage => (stage.AnimationIds?.Count ?? 0) > 1);
                for (int stageIndex = 0; stageIndex < sourceSlot.Timeline.Stages.Count; stageIndex++)
                {
                    var stage = sourceSlot.Timeline.Stages[stageIndex];
                    for (int animationIndex = 0; animationIndex < (stage.AnimationIds ?? Array.Empty<string>()).Count; animationIndex++)
                    {
                        string semanticId = stage.AnimationIds[animationIndex];
                        string label = $"{address.InputLabel} · {FriendlyMoveLabel(address, sourceSlot)}";
                        if (disambiguate)
                            label += $" · Stage {stageIndex + 1} · Animation {animationIndex + 1}";
                        var row = BuildBindingRow(semanticId, label, FindBinding(semanticId));
                        row.tooltip = $"{address.Id} · stage {stageIndex + 1} · animation {animationIndex + 1}";
                        _assetsMoveBindings.Add(row);
                    }
                }
            }
        }
        _assetsPresentationBindings.Clear();
        _presentationAssetStatus.text = "";
        if (available)
        {
            foreach (string semanticId in _workspace.Draft.PresentationIds ?? Array.Empty<string>())
                _assetsPresentationBindings.Add(BuildPresentationBindingRow(semanticId, FindPresentationBinding(semanticId)));
        }
        RefreshAssetsValidation();
    }

    private void AddBindingRow(VisualElement parent, string label, string semanticId)
        => parent.Add(BuildBindingRow(semanticId, label, FindBinding(semanticId)));

    private VisualElement BuildBindingRow(string semanticId, string label, CharacterAssetCatalog.AnimationBinding binding)
    {
        var row = new VisualElement { userData = semanticId ?? "" };
        row.AddToClassList("asset-binding-row");
        var name = new Label(label);
        name.AddToClassList("asset-binding-label");
        name.tooltip = semanticId ?? "";
        row.Add(name);
        var clip = new ObjectField
        {
            objectType = typeof(AnimationClip),
            allowSceneObjects = false,
            tooltip = semanticId ?? "",
        };
        clip.AddToClassList("asset-binding-clip");
        clip.SetValueWithoutNotify(binding?.Clip);
        clip.SetEnabled(binding != null);
        var extrapolation = new EnumField(binding?.Extrapolation ?? ExtrapolationMode.None)
        {
            tooltip = semanticId ?? "",
        };
        extrapolation.AddToClassList("asset-binding-extrapolation");
        extrapolation.SetEnabled(binding != null);
        clip.RegisterValueChangedCallback(evt =>
        {
            if (_updatingControls || binding == null) return;
            _workspace.ReplaceCatalogBinding((string)row.userData, evt.newValue as AnimationClip, (ExtrapolationMode)extrapolation.value);
            RefreshAll();
        });
        extrapolation.RegisterValueChangedCallback(evt =>
        {
            if (_updatingControls || binding == null) return;
            _workspace.ReplaceCatalogBinding((string)row.userData, clip.value as AnimationClip, (ExtrapolationMode)evt.newValue);
            RefreshAll();
        });
        row.Add(clip);
        row.Add(extrapolation);
        return row;
    }

    private VisualElement BuildPresentationBindingRow(
        string semanticId,
        CharacterAssetCatalog.PresentationBinding binding)
    {
        var row = new VisualElement { userData = semanticId ?? "" };
        row.AddToClassList("asset-binding-row");
        var name = new Label(semanticId ?? "");
        name.AddToClassList("asset-binding-label");
        name.tooltip = semanticId ?? "";
        row.Add(name);
        var prefab = new ObjectField
        {
            objectType = typeof(GameObject),
            allowSceneObjects = false,
            tooltip = semanticId ?? "",
        };
        prefab.AddToClassList("asset-binding-clip");
        prefab.SetValueWithoutNotify(binding?.Prefab);
        prefab.SetEnabled(binding != null);
        prefab.RegisterValueChangedCallback(evt =>
        {
            if (_updatingControls || binding == null) return;
            bool accepted = _workspace.ReplaceCatalogPresentation((string)row.userData, evt.newValue as GameObject);
            if (!accepted)
                _presentationAssetStatus.text = _workspace.Diagnostics.LastOrDefault()?.Message ?? "Presentation asset rejected; see diagnostics.";
            RefreshAll();
        });
        row.Add(prefab);
        return row;
    }

    private CharacterAssetCatalog.PresentationBinding FindPresentationBinding(string semanticId)
        => (_workspace.Catalog?.Presentations ?? Array.Empty<CharacterAssetCatalog.PresentationBinding>())
            .FirstOrDefault(binding => binding != null && binding.SemanticId == semanticId);

    private CharacterAssetCatalog.AnimationBinding FindBinding(string semanticId)
        => (_workspace.Catalog?.Bindings ?? Array.Empty<CharacterAssetCatalog.AnimationBinding>())
            .FirstOrDefault(binding => binding != null && binding.SemanticId == semanticId);

    private string DescribeRig(GameObject rig)
    {
        if (rig == null) return "Catalog rig missing; cooker will report asset-catalog.rig.missing.";
        var animator = rig.GetComponent<Animator>();
        if (animator == null) return $"{rig.name} · Animator missing";
        if (animator.avatar == null) return $"{rig.name} · Avatar missing";
        return $"{rig.name} · Avatar {(animator.avatar.isValid && animator.avatar.isHuman ? "valid Humanoid" : "invalid")}";
    }

    private void RefreshAssetsValidation()
    {
        _assetsValidation.Clear();
        if (!_workspace.HasPackage)
        {
            _assetsValidation.Add(new Label("No package selected."));
            return;
        }
        var diagnostics = _inspection?.RawDiagnostics ?? _workspace.Diagnostics;
        if (diagnostics.Count == 0)
        {
            _assetsValidation.Add(new Label("No diagnostics."));
            return;
        }
        foreach (var diagnostic in diagnostics)
            _assetsValidation.Add(new Label($"{diagnostic.Severity} · {diagnostic.Code} · {diagnostic.Path}\n{diagnostic.Message}"));
    }

    private string FriendlyMoveLabel(SlotAddress address, CharacterSlotSource sourceSlot)
        => !string.IsNullOrEmpty(sourceSlot?.Name) ? sourceSlot.Name : $"Unknown ({address.Id})";

    private void RefreshAdvanced()
    {
        bool available = _workspace.HasPackage && _inspection != null;
        _advancedPackagePaths.SetEnabled(available);
        _advancedSourcePath.text = available ? $"Source: {_inspection!.SourcePath}" : "Source: —";
        _advancedCookedPath.text = available ? $"Cooked: content-cooked/{_inspection!.PackageId}" : "Cooked: —";
        foreach (var group in new[] { _advancedHashes, _advancedRawIds, _advancedDiagnostics, _advancedProvenance, _advancedSchemaProfile })
            group.Clear();
        if (!available)
        {
            _advancedHashes.Add(new Label("No package selected."));
            _advancedMigrationStatus.text = "Current; no migration required";
            _advancedRenameConfirm.SetEnabled(false);
            _advancedMigrateAuthoring.SetEnabled(false);
            _advancedMigrateCatalog.SetEnabled(false);
            return;
        }

        var inspection = _inspection!;
        _advancedHashes.Add(new Label($"Source: {inspection.SourceHash}\nCooked source: {inspection.CookedSourceHash}\nCooked content: {inspection.CookedContentHash}\nPackage: {inspection.PackageHash}"));
        _advancedRawIds.Add(new Label($"Package ID: {inspection.PackageId}\nSemantic IDs: {string.Join(", ", _workspace.Catalog.Bindings.Where(x => x != null).Select(x => x.SemanticId))}\nPose IDs: {string.Join(", ", _workspace.Catalog.Bindings.Where(x => x != null).Select(x => x.PoseTrackId))}"));
        var diagnostics = new List<CharacterDiagnostic>(inspection.RawDiagnostics);
        if (inspection.Provenance != null)
            diagnostics.AddRange(inspection.Provenance.CookStatusDiagnostics.Select(x => new CharacterDiagnostic(
                x.Severity == "error" ? CharacterDiagnosticSeverity.Error : CharacterDiagnosticSeverity.Warning,
                x.Code, x.Path, x.Message)));
        if (diagnostics.Count == 0) _advancedDiagnostics.Add(new Label("No diagnostics."));
        foreach (var diagnostic in diagnostics)
            _advancedDiagnostics.Add(new Label($"{diagnostic.Severity} · {diagnostic.Code} · {diagnostic.Path}\n{diagnostic.Message}"));

        var provenance = inspection.Provenance;
        if (provenance == null)
        {
            _advancedProvenance.Add(new Label("No verified cooked manifest."));
            _advancedSchemaProfile.Add(new Label("No verified schema/profile metadata."));
        }
        else
        {
            _advancedProvenance.Add(new Label(
                $"Package: {provenance.PackageId} {provenance.Version}\nCreator: {provenance.Creator}\nLicense: {provenance.License}\nAttribution: {provenance.Attribution}\nCook status: {provenance.CookStatus}\nDependencies: {provenance.Dependencies.Count}\nUnity dependencies: {provenance.UnityDependencies.Count}\nPayloads: {string.Join(", ", provenance.Payloads.Select(x => $"{x.Path} [{x.Sha256}, {x.Size} bytes]"))}\nWarnings: {provenance.Warnings.Count}"));
            _advancedSchemaProfile.Add(new Label(
                $"Profile: {provenance.Profile}\nAuthoring schema: {provenance.AuthoringSchemaVersion}\nCooked schema: {provenance.CookedSchemaVersion}\nRuntime API: {provenance.RuntimeApiMin} – {provenance.RuntimeApiMax}\nCooker: {provenance.CookerVersion}\nUnity: {provenance.UnityVersion}\nBinding schema: {provenance.BindingSchemaVersion}\nPose: {provenance.PoseFormat} v{provenance.PoseVersion}\nSample rate: {provenance.SampleRate} Hz\nCapability requirements: {string.Join(", ", provenance.CapabilityRequirements.Select(x => $"{x.CapabilityId}@{x.CapabilityVersion}"))}"));
        }
        _advancedRenameConfirm.SetEnabled(true);
        _advancedMigrateAuthoring.SetEnabled(false);
        _advancedMigrateCatalog.SetEnabled(false);
        _advancedMigrationStatus.text = "Current; no migration required";
    }
    private void RefreshDiagnostics()
    {
        _diagnosticsPanel.Clear();
        var diagnostics = new List<CharacterDiagnostic>(_workspace.Diagnostics);
        if (_inspection != null) diagnostics.AddRange(_inspection.RawDiagnostics);
        if (_preview != null) diagnostics.AddRange(_preview.Diagnostics);
        if (!string.IsNullOrEmpty(RecoveryError)) diagnostics.Add(Diagnostic("workspace.recovery.failed", _workspace.PackageRoot, RecoveryError));
        foreach (var option in _packages.Where(option => option.Diagnostics.Count > 0)) diagnostics.AddRange(option.Diagnostics);
        var unique = diagnostics
            .GroupBy(x => $"{x.Severity}|{x.Code}|{x.Path}|{x.Message}", StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
        Required<Button>("diagnostics-toggle").SetEnabled(unique.Count > 0);
        foreach (var diagnostic in unique)
            _diagnosticsPanel.Add(new Label($"{diagnostic.Code} · {diagnostic.Path}\n{diagnostic.Message}"));
        _diagnosticsPanel.style.display = unique.Count > 0
            ? DisplayStyle.Flex
            : DisplayStyle.None;
    }

    private void UpdateTimelineControls()
    {
        var vfxStatus = Required<Label>("timeline-vfx-status");
        var vfxDiagnostics = _lab?.PresentationPreviewDiagnostics;
        vfxStatus.text = vfxDiagnostics == null ? "" : string.Join("\n", vfxDiagnostics);
        vfxStatus.style.display = string.IsNullOrEmpty(vfxStatus.text) ? DisplayStyle.None : DisplayStyle.Flex;
        RefreshMovesViewport();
        RefreshPhaseControls();
        if (UpdatePhaseTimeline())
        {
            SyncNumericSeek();
            return;
        }
        if (_lab?.IsScenarioPreview == true && _lab.Scenario != null)
        {
            var result = _lab.Scenario;
            int frame = Mathf.Clamp(_lab.ScenarioFrame, 0, result.Frames.Count - 1);
            var sample = result.Frames[frame];
            int lastFrame = result.Options.LastFrame;
            _timelineProjection = null;
            _moveTimeline.style.display = DisplayStyle.Flex;
            _updatingControls = true;
            SetTimelineRange(lastFrame, frame);
            _timelineSlider.SetEnabled(true);
            SyncNumericSeek();
            _timelinePlay.SetEnabled(true);
            _timelinePlay.text = _lab.Playing ? "Pause" : "Play";
            _timelineTick.text = $"Frame {sample.FrameIndex} · Match tick {sample.MatchTick}";
            _timelineDuration.text = $"Recorded {result.Frames.Count} frames · {result.Frames.Count / (float)AbilityLab.TickRate:0.00}s";
            _stageSelector.style.display = DisplayStyle.None;
            _stageSelector.SetEnabled(false);
            _timelineTrack.style.display = DisplayStyle.None;
            _timelineTrack.Projection = EmptyTimeline();
            _timelineTrack.SelectedOperation = _selectedOperation = null;
            _updatingControls = false;
            return;
        }
        _timelineTrack.style.display = DisplayStyle.Flex;
        if (_grabSelected)
        {
            _timelineProjection = null;
            _timelineSlider.SetEnabled(false);
            _timelineSeek.SetEnabled(false);
            _timelinePlay.SetEnabled(false);
            _stageSelector.style.display = DisplayStyle.None;
            _moveTimeline.style.display = DisplayStyle.None;
            return;
        }
        _moveTimeline.style.display = DisplayStyle.Flex;
        _timelineProjection = BuildTimelineProjection();
        if (_lab == null || !_lab.IsPackagePreview || _timelineProjection == null || _timelineProjection.Stages.Count == 0)
        {
            _timelineTick.text = "Tick 0";
            _timelineDuration.text = "Duration —";
            _timelineSlider.SetEnabled(false);
            _timelineSeek.SetEnabled(false);
            _timelinePlay.SetEnabled(false);
            _stageSelector.style.display = DisplayStyle.None;
            _stageSelector.SetEnabled(false);
            _timelineTrack.Projection = _timelineProjection ?? EmptyTimeline();
            _timelineTrack.CurrentTick = 0;
            _timelineTrack.SelectedOperation = _selectedOperation = null;
            return;
        }

        int cumulativeTick = CumulativeTick(_timelineProjection, _lab.StageIndex, _lab.Tick);
        int duration = _timelineProjection.DurationTicks;
        _updatingControls = true;
        SetTimelineRange(Mathf.Max(0, duration - 1), cumulativeTick);
        _timelineSlider.SetEnabled(true);
        SyncNumericSeek();
        _timelinePlay.SetEnabled(true);
        _timelinePlay.text = _lab.Playing ? "Pause" : "Play";
        _timelineTick.text = $"Tick {cumulativeTick}";
        _timelineDuration.text = $"Duration {duration} ticks · {duration / (float)AbilityLab.TickRate:0.00}s";
        if (!ReferenceEquals(_timelineTrack.Projection, _timelineProjection))
            _timelineTrack.Projection = _timelineProjection;
        _timelineTrack.CurrentTick = cumulativeTick;
        if (!ReferenceEquals(_timelineTrack.SelectedOperation, _selectedOperation))
            _timelineTrack.SelectedOperation = _selectedOperation;
        _updatingControls = false;
        UpdateStageSelector();
        SyncSelectedHitbox();
    }

    private void SetTimelineRange(int lastTick, int tick)
    {
        // Range setters otherwise enqueue a seek after the refresh guard is cleared.
        if (_timelineSlider.value > lastTick)
            _timelineSlider.SetValueWithoutNotify(lastTick);
        _timelineSlider.lowValue = 0;
        _timelineSlider.highValue = lastTick;
        _timelineSlider.SetValueWithoutNotify(tick);
    }

    private void SyncNumericSeek()
    {
        _timelineSeek.SetValueWithoutNotify(_timelineSlider.value);
        _timelineSeek.SetEnabled(_timelineSlider.enabledSelf);
    }

    private AbilityLabTimelineProjection? BuildTimelineProjection()
    {
        if (!_workspace.HasPackage || _lab == null || !_workspace.TryResolveCanonicalSlot(_lab.SelectedSlotId, out _, out var sourceSlot))
        {
            _timelineProjection = null;
            _timelineProjectionSlotId = "";
            _timelineProjectionDraft = null;
            _timelineProjectionPackageRoot = "";
            _selectedOperation = null;
            _selectedHitboxSourceIndex = -1;
            return null;
        }
        bool sameContext = _timelineProjectionPackageRoot == _workspace.PackageRoot &&
            _timelineProjectionSlotId == _lab.SelectedSlotId;
        if (sameContext && _timelineProjection != null && ReferenceEquals(_timelineProjectionDraft, _workspace.Draft))
            return _timelineProjection;
        if (!sameContext)
        {
            _selectedOperation = null;
            _selectedHitboxSourceIndex = -1;
            _inspectedStageIndex = Mathf.Max(0, _lab.StageIndex);
            _timelineNotice.text = "";
        }
        _timelineProjectionPackageRoot = _workspace.PackageRoot;
        _timelineProjectionSlotId = _lab.SelectedSlotId;
        _timelineProjectionDraft = _workspace.Draft;
        _timelineProjection = AbilityLabTimelineProjection.Build(sourceSlot);
        _inspectedStageIndex = Mathf.Clamp(_inspectedStageIndex, 0, Mathf.Max(0, _timelineProjection.Stages.Count - 1));
        if (_selectedOperation != null)
            _selectedOperation = FindProjectedOperation(_selectedOperation.SourceStageIndex, _selectedOperation.SourceOperationIndex);
        CacheSelectedHitbox();
        return _timelineProjection;
    }

    private static AbilityLabTimelineProjection EmptyTimeline()
        => AbilityLabTimelineProjection.Build(new CharacterSlotSource(
            "empty", "Empty", "", "", AuthoringAbilityBehavior.MeleeCombo, AuthoringAimMode.None,
            0, false, false, new CharacterTimelineSource(Array.Empty<CharacterStageSource>())));

    private static int CumulativeTick(AbilityLabTimelineProjection projection, int stageIndex, ushort localTick)
    {
        if (projection.Stages.Count == 0) return 0;
        var stage = projection.Stages[Mathf.Clamp(stageIndex, 0, projection.Stages.Count - 1)];
        return Mathf.Clamp(stage.StartTick + localTick, 0, projection.DurationTicks);
    }

    private void SetTimelineZoom(float value)
    {
        if (_timelineTrack == null) return;
        float zoom = Mathf.Clamp(value, 0.5f, 4f);
        _timelineTrack.style.width = new Length(zoom * 100f, LengthUnit.Percent);
        _timelineTrack.MarkDirtyRepaint();
    }

    internal static bool TryResolveCumulativeTick(
        AbilityLabTimelineProjection projection,
        int requestedTick,
        out AbilityLabStageProjection stage,
        out ushort localTick,
        out int appliedCumulativeTick)
    {
        stage = null!;
        localTick = 0;
        appliedCumulativeTick = 0;
        if (projection == null || projection.Stages.Count == 0) return false;
        int clamped = Mathf.Clamp(requestedTick, 0, projection.DurationTicks);
        stage = projection.Stages[^1];
        foreach (var candidate in projection.Stages)
            if (clamped < candidate.EndTick) { stage = candidate; break; }
        localTick = (ushort)Mathf.Clamp(clamped - stage.StartTick, 0, Mathf.Max(0, stage.DurationTicks - 1));
        appliedCumulativeTick = stage.StartTick + localTick;
        return true;
    }

    internal static bool CanCommandOpenPackage(bool hasPackage, string currentPackageId, bool dirty, string targetPackageId)
        => !hasPackage || !dirty || currentPackageId == targetPackageId;

    private void ApplyCumulativeTick(int cumulativeTick)
    {
        if (_updatingControls || _lab == null) return;
        if (_lab.PhasePreviewActive && !_lab.IsScenarioPreview)
        {
            _lab.SetPhaseTick(cumulativeTick);
            UpdateTimelineControls();
            SceneView.RepaintAll();
            return;
        }
        if (_lab.IsScenarioPreview && _lab.Scenario != null)
        {
            _lab.SeekScenario(Mathf.Clamp(cumulativeTick, 0, _lab.Scenario.Options.LastFrame));
            UpdateTimelineControls();
            SceneView.RepaintAll();
            return;
        }
        if (_timelineProjection == null ||
            !TryResolveCumulativeTick(_timelineProjection, cumulativeTick, out var stage, out ushort local, out _))
            return;
        _lab.SetStage(stage.SourceStageIndex);
        _lab.SetTick(local);
        _timelineNotice.text = cumulativeTick == CumulativeTick(_timelineProjection, stage.SourceStageIndex, local)
            ? "" : $"Cursor clamped to valid move tick {CumulativeTick(_timelineProjection, stage.SourceStageIndex, local)}.";
        UpdateTimelineControls();
    }


    private void UpdateStageSelector()
    {
        if (_timelineProjection == null || _timelineProjection.Stages.Count <= 1)
        {
            _stageSelector.style.display = DisplayStyle.None;
            _stageSelector.SetEnabled(false);
            if (_stageSelector.choices.Count != 0) _stageSelector.choices = new List<string>();
            _stageSelector.SetValueWithoutNotify("");
            return;
        }
        var choices = _stageSelector.choices;
        if (choices.Count != _timelineProjection.Stages.Count)
            choices = _timelineProjection.Stages.Select(stage => $"Stage {stage.SourceStageIndex + 1}").ToList();
        _stageSelector.style.display = DisplayStyle.Flex;
        _stageSelector.choices = choices;
        _stageSelector.SetValueWithoutNotify(choices[Mathf.Clamp(_inspectedStageIndex, 0, choices.Count - 1)]);
        _stageSelector.SetEnabled(true);
    }

    private void SetTickDelta(int delta)
    {
        if (_lab == null) return;
        if (_lab.PhasePreviewActive && !_lab.IsScenarioPreview)
        {
            ApplyCumulativeTick(_lab.PhaseTick + delta);
            return;
        }
        if (_lab.IsScenarioPreview && _lab.Scenario != null)
        {
            ApplyCumulativeTick(_lab.ScenarioFrame + delta);
            return;
        }
        if (_timelineProjection != null)
            ApplyCumulativeTick(CumulativeTick(_timelineProjection, _lab.StageIndex, _lab.Tick) + delta);
    }

    private void OnRootKeyDown(KeyDownEvent evt)
    {
        if (IsTextInput(evt.target as VisualElement)) return;
        bool packageMode = _workspace.HasPackage;
        if (evt.keyCode == KeyCode.Escape)
        {
            _timelineTrack.CancelDrag();
            _timelineTrack.Focus();
            evt.StopPropagation();
            return;
        }
        if (evt.ctrlKey && evt.shiftKey && evt.keyCode == KeyCode.Z && packageMode)
        {
            _workspace.Redo();
            RefreshAll();
            evt.StopPropagation();
            return;
        }
        if (evt.ctrlKey && evt.keyCode == KeyCode.Z && packageMode)
        {
            _workspace.Undo();
            RefreshAll();
            evt.StopPropagation();
            return;
        }
        if (evt.ctrlKey && evt.keyCode == KeyCode.S && packageMode)
        {
            SaveAndCook();
            evt.StopPropagation();
            return;
        }
        if (packageMode && _lab != null &&
            (_lab.IsPackagePreview || _lab.IsScenarioPreview) &&
            (evt.keyCode == KeyCode.LeftArrow || evt.keyCode == KeyCode.RightArrow))
        {
            SetTickDelta(evt.keyCode == KeyCode.LeftArrow ? -1 : 1);
            evt.StopPropagation();
        }
    }

    private static bool IsTextInput(VisualElement? target)
    {
        for (var current = target; current != null; current = current.parent)
            if (current is TextField || current is IntegerField || current is FloatField)
                return true;
        return false;
    }

    private void SelectOperation(AbilityLabOperationProjection operation)
    {
        if (_lab?.IsScenarioPreview == true || _lab == null) return;
        _selectedOperation = operation;
        _inspectedStageIndex = operation.SourceStageIndex;
        _showSelectedEffectFields = true;
        _sceneRadiusEditing = false;
        _scenePresentationEditing = false;
        CacheSelectedHitbox();
        SyncSelectedHitbox();
        UpdateTimelineControls();
        RefreshInspector();
    }

    private void CacheSelectedHitbox()
    {
        _selectedHitboxSourceIndex = -1;
        if (_selectedOperation?.Source is not SpawnHitboxOperationSource || _lab == null ||
            !_workspace.TryResolveCanonicalSlot(_lab.SelectedSlotId, out _, out var slot))
            return;
        var operations = slot.Timeline.Stages[_selectedOperation.SourceStageIndex].Operations;
        int index = 0;
        for (int i = 0; i < _selectedOperation.SourceOperationIndex; i++)
            if (operations[i] is SpawnHitboxOperationSource) index++;
        _selectedHitboxSourceIndex = index;
    }

    private void SyncSelectedHitbox()
    {
        if (_lab == null || _lab.IsScenarioPreview || _lab.PhasePreviewActive) return;
        int index = _selectedOperation != null && _selectedOperation.SourceStageIndex == _lab.StageIndex
            ? _selectedHitboxSourceIndex : -1;
        if (_lab.SelectedHitboxEventIndex != index) _lab.SelectHitbox(index);
    }
    private void CompleteTimelineDrag(AbilityLabTimelineDrag drag)
    {
        if (_lab?.IsScenarioPreview == true) return;
        if (_lab == null || !_lab.IsPackagePreview || !_workspace.HasPackage) return;
        bool accepted = drag.Mode switch
        {
            TimelineDragMode.Move => _workspace.ReplaceOperationTick(_lab.SelectedSlotId, drag.SourceStageIndex, drag.SourceOperationIndex, drag.Tick),
            TimelineDragMode.ResizeHitboxEnd => _workspace.ReplaceHitboxDuration(_lab.SelectedSlotId, drag.SourceStageIndex, drag.SourceOperationIndex, drag.DurationTicks),
            TimelineDragMode.ResizePresentationEnd => TryReplacePresentationDuration(drag),
            _ => false,
        };
        if (!accepted) return;

        _lab.SetStage(drag.SourceStageIndex);
        _lab.SetTick((ushort)Mathf.Clamp(drag.Tick, 0, ushort.MaxValue));
        _timelineProjection = BuildTimelineProjection();
        _selectedOperation = FindProjectedOperation(drag.SourceStageIndex, drag.SourceOperationIndex);
        _inspectedStageIndex = drag.SourceStageIndex;
        CacheSelectedHitbox();
        SyncSelectedHitbox();
        _timelineTrack.SelectedOperation = _selectedOperation;
        RefreshInspector();
        SceneView.RepaintAll();
    }

    private bool TryReplacePresentationDuration(AbilityLabTimelineDrag drag)
    {
        if (!_workspace.TryResolveCanonicalSlot(_lab!.SelectedSlotId, out _, out var slot) ||
            drag.SourceStageIndex < 0 || drag.SourceStageIndex >= slot.Timeline.Stages.Count)
            return false;
        var operations = slot.Timeline.Stages[drag.SourceStageIndex].Operations;
        if (drag.SourceOperationIndex < 0 || drag.SourceOperationIndex >= operations.Count ||
            operations[drag.SourceOperationIndex] is not EmitPresentationOperationSource presentation)
            return false;
        return _workspace.ReplacePresentationPlacement(_lab.SelectedSlotId, drag.SourceStageIndex, drag.SourceOperationIndex,
            presentation.Placement with { DurationTicks = (ushort)Mathf.Clamp(drag.DurationTicks, 1, ushort.MaxValue) });
    }

    private AbilityLabOperationProjection? FindProjectedOperation(int stageIndex, int operationIndex)
    {
        if (_timelineProjection == null || stageIndex < 0 || stageIndex >= _timelineProjection.Stages.Count) return null;
        foreach (var operation in _timelineProjection.Stages[stageIndex].Operations)
            if (operation.SourceOperationIndex == operationIndex) return operation;
        return null;
    }


    private void RefreshInspector()
    {
        string focusedName = CaptureFieldsFocus();
        bool attachmentExpanded = _inspector.Q<Foldout>("attachment-authoring")?.value ?? false;
        bool updating = _updatingControls;
        _updatingControls = true;
        try
        {
            RefreshInspectorContents();
            var attachment = _inspector.Q<Foldout>("attachment-authoring");
            if (attachment != null) attachment.SetValueWithoutNotify(attachmentExpanded || _workspace.AttachmentDraftDirty);
        }
        finally { _updatingControls = updating; }
        _fieldsWindow?.RefreshAvailability();
        RestoreFocus(focusedName);
    }

    private void RefreshInspectorContents()
    {
        _inspector.Clear();
        _moveTimeline.style.display = _grabSelected && _lab?.IsScenarioPreview != true
            ? DisplayStyle.None
            : DisplayStyle.Flex;
        if (_lab?.IsScenarioPreview == true && !_grabSelected)
        {
            _inspector.Add(new Label("Recorded Shared scenario preview · timeline and outcomes are read-only. Exit to authoring to edit move stages."));
            return;
        }
        if (_lab?.PhasePreviewActive == true && !_grabSelected)
        {
            AddAttachmentAuthoring();
            _inspector.Add(new Label("Presentation-only phase preview. Choose Timeline to edit gameplay operations."));
            return;
        }
        if (_grabSelected)
        {
            _stageSelector.style.display = DisplayStyle.None;
            _stageSelector.SetEnabled(false);
            if (!_workspace.HasPackage)
            {
                _inspector.Add(new Label("Open a cooked character package to edit Grab."));
                return;
            }

            var capture = _workspace.Draft.CaptureGeometry;
            _inspector.Add(new Label("Grounded Grab · local +Z is forward. Values are meters; dimensions must be positive."));
            var volume = new Foldout { text = "Forward capture volume", value = true };
            AddDelayedFloat(volume, "Reach", capture.Reach,
                value => CommitGrab(current => current with { Reach = value }));
            AddDelayedFloat(volume, "Width", capture.Width,
                value => CommitGrab(current => current with { Width = value }));
            AddDelayedFloat(volume, "Height", capture.Height,
                value => CommitGrab(current => current with { Height = value }));
            AddDelayedFloat(volume, "Vertical center", capture.OffsetY,
                value => CommitGrab(current => current with { OffsetY = value }));
            _inspector.Add(volume);

            var attacker = new Foldout { text = "Attacker restraint anchor", value = false };
            AddDelayedFloat(attacker, "X", capture.AttackerAnchor.X,
                value => CommitGrab(current => current with { AttackerAnchor = current.AttackerAnchor with { X = value } }));
            AddDelayedFloat(attacker, "Y", capture.AttackerAnchor.Y,
                value => CommitGrab(current => current with { AttackerAnchor = current.AttackerAnchor with { Y = value } }));
            AddDelayedFloat(attacker, "Z", capture.AttackerAnchor.Z,
                value => CommitGrab(current => current with { AttackerAnchor = current.AttackerAnchor with { Z = value } }));
            _inspector.Add(attacker);

            var victim = new Foldout { text = "Victim restraint anchor", value = false };
            AddDelayedFloat(victim, "X", capture.VictimAnchor.X,
                value => CommitGrab(current => current with { VictimAnchor = current.VictimAnchor with { X = value } }));
            AddDelayedFloat(victim, "Y", capture.VictimAnchor.Y,
                value => CommitGrab(current => current with { VictimAnchor = current.VictimAnchor with { Y = value } }));
            AddDelayedFloat(victim, "Z", capture.VictimAnchor.Z,
                value => CommitGrab(current => current with { VictimAnchor = current.VictimAnchor with { Z = value } }));
            _inspector.Add(victim);
            return;
        }
        if (_lab == null || !_workspace.HasPackage ||
            !_workspace.TryResolveCanonicalSlot(_lab.SelectedSlotId, out _, out var slot) ||
            slot.Timeline.Stages.Count == 0)
        {
            _stageSelector.style.display = DisplayStyle.None;
            _stageSelector.SetEnabled(false);
            _inspector.Add(new Label("Select a cooked package and move."));
            return;
        }
        BuildTimelineProjection();

        bool multiStage = slot.Timeline.Stages.Count > 1;
        _stageSelector.style.display = multiStage ? DisplayStyle.Flex : DisplayStyle.None;
        _stageSelector.SetEnabled(multiStage);
        if (multiStage)
            _inspector.Add(_stageSelector);

        int stageIndex = _inspectedStageIndex;
        var stage = slot.Timeline.Stages[stageIndex];
        bool effectMode = _showSelectedEffectFields && _selectedOperation != null;
        var header = new VisualElement();
        header.AddToClassList("fields-header");
        var title = new Label(slot.Name);
        title.AddToClassList("fields-title");
        header.Add(title);
        var context = new Label($"Stage {stageIndex + 1} · {stage.DurationTicks} ticks · IASA {stage.IasaTicks} · Landing lag {stage.LandingLagTicks}");
        context.AddToClassList("fields-description");
        header.Add(context);
        var modes = new VisualElement { name = "fields-mode-switch" };
        modes.AddToClassList("fields-mode-switch");
        var overviewMode = new Button(() => { _showSelectedEffectFields = false; RefreshInspector(); })
            { name = "fields-mode-overview", text = "Move overview" };
        var selectedMode = new Button(() => { _showSelectedEffectFields = true; RefreshInspector(); })
            { name = "fields-mode-effect", text = "Selected effect" };
        overviewMode.AddToClassList("fields-mode-button");
        selectedMode.AddToClassList("fields-mode-button");
        overviewMode.EnableInClassList("fields-mode-button-selected", !effectMode);
        selectedMode.EnableInClassList("fields-mode-button-selected", effectMode);
        selectedMode.SetEnabled(_selectedOperation != null);
        modes.Add(overviewMode);
        modes.Add(selectedMode);
        header.Add(modes);
        _inspector.Add(header);
        var moveGroup = new Foldout { name = "move-overview", text = "Timing & animation", value = true };
        moveGroup.AddToClassList("inspector-section");
        moveGroup.style.display = effectMode ? DisplayStyle.None : DisplayStyle.Flex;
        var durationField = new IntegerField("Duration ticks") { name = "move-duration", value = stage.DurationTicks, isDelayed = true };
        durationField.RegisterValueChangedCallback(evt =>
        {
            if (_updatingControls || _lab == null) return;
            int duration = Mathf.Clamp(evt.newValue, 1, ushort.MaxValue);
            CommitStage(current => current with { DurationTicks = (ushort)duration }, stageIndex);
        });
        moveGroup.Add(durationField);
        var iasaField = new IntegerField("IASA ticks") { name = "move-iasa", value = stage.IasaTicks, isDelayed = true };
        iasaField.RegisterValueChangedCallback(evt =>
        {
            if (_updatingControls || _lab == null) return;
            int iasa = Mathf.Clamp(evt.newValue, 0, stage.DurationTicks);
            CommitStage(current => current with { IasaTicks = (ushort)iasa }, stageIndex);
        });
        moveGroup.Add(iasaField);
        moveGroup.Add(new Label($"Auto-cancel before {stage.AutoCancelBeforeTicks} · after {stage.AutoCancelAfterTicks}"));
        var animationIds = (_preview?.AnimationCatalog?.Animations ?? Array.Empty<CharacterAnimationCatalog.AnimationEntry>())
            .Where(animation => animation != null && !string.IsNullOrEmpty(animation.SemanticId))
            .Select(animation => animation.SemanticId)
            .Concat(stage.AnimationIds ?? Array.Empty<string>())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var animationChoices = BuildAnimationChoices(animationIds);
        int animationIndex = 0;
        foreach (string animationId in stage.AnimationIds ?? Array.Empty<string>())
        {
            var selectedChoice = animationChoices.FirstOrDefault(choice => choice.SemanticId == animationId);
            var choices = animationChoices.Select(choice => choice.Label).ToList();
            var field = new PopupField<string>($"Animation · {selectedChoice?.Label ?? $"Unknown ({animationId})"}", choices,
                selectedChoice == null ? 0 : choices.IndexOf(selectedChoice.Label));
            field.name = $"move-animation-{animationIndex++}";
            field.RegisterValueChangedCallback(evt =>
            {
                var choice = animationChoices.FirstOrDefault(item => item.Label == evt.newValue);
                if (choice != null)
                    CommitStage(current => current with
                    {
                        AnimationIds = current.AnimationIds.Select(id => id == animationId ? choice.SemanticId : id).ToArray()
                    }, stageIndex);
            });
            moveGroup.Add(field);
            var animationEntry = _preview?.AnimationCatalog?.Animations
                ?.FirstOrDefault(entry => entry != null && entry.SemanticId == animationId);
            int frameCount = animationEntry?.FrameCount ?? 0;
            moveGroup.Add(new Label(frameCount > 0
                ? $"Animation frames {frameCount} · {frameCount / (float)Mathf.Max(1, stage.DurationTicks):0.00} frames/tick"
                : "Animation frames unavailable"));
        }
        var addHitbox = new Button(() =>
        {
            if (_lab == null || !_workspace.AddHitbox(_lab.SelectedSlotId, stageIndex)) return;
            UpdateTimelineControls();
            _selectedOperation = _timelineProjection?.Stages[stageIndex].Operations.LastOrDefault();
            CacheSelectedHitbox();
            SyncSelectedHitbox();
            _timelineTrack.SelectedOperation = _selectedOperation;
            RefreshInspector();
            SceneView.RepaintAll();
        })
        {
            text = "Add hitbox"
        };
        addHitbox.tooltip = "Add a default hitbox to this move stage.";
        moveGroup.Add(addHitbox);
        var addForwardLunge = new Button(() =>
        {
            if (_lab == null || !_workspace.AddForwardLunge(_lab.SelectedSlotId, stageIndex)) return;
            UpdateTimelineControls();
            _selectedOperation = _timelineProjection?.Stages[stageIndex].Operations.LastOrDefault();
            CacheSelectedHitbox();
            SyncSelectedHitbox();
            _timelineTrack.SelectedOperation = _selectedOperation;
            RefreshInspector();
            SceneView.RepaintAll();
        })
        {
            text = "Add forward lunge"
        };
        addForwardLunge.tooltip = "Move in the current facing direction at a fixed speed for a fixed duration.";
        moveGroup.Add(addForwardLunge);
        var addGravityWindow = new Button(() =>
        {
            if (_lab == null || !_workspace.AddGravityWindow(_lab.SelectedSlotId, stageIndex)) return;
            UpdateTimelineControls();
            _selectedOperation = _timelineProjection?.Stages[stageIndex].Operations.LastOrDefault();
            CacheSelectedHitbox();
            SyncSelectedHitbox();
            _timelineTrack.SelectedOperation = _selectedOperation;
            RefreshInspector();
            SceneView.RepaintAll();
        })
        {
            text = "Add gravity window"
        };
        addGravityWindow.tooltip = "Temporarily scales airborne gravity over a fixed timeline window.";
        moveGroup.Add(addGravityWindow);
        var addTargetedLeap = new Button(() =>
        {
            if (_lab == null || !_workspace.AddTargetedLeap(_lab.SelectedSlotId, stageIndex)) return;
            UpdateTimelineControls();
            _selectedOperation = _timelineProjection?.Stages[stageIndex].Operations.LastOrDefault();
            CacheSelectedHitbox();
            SyncSelectedHitbox();
            _timelineTrack.SelectedOperation = _selectedOperation;
            RefreshInspector();
            SceneView.RepaintAll();
        })
        {
            text = "Add targeted leap"
        };
        addTargetedLeap.tooltip = "Hold to aim, leap toward the target, then trigger a landing hitbox and recovery.";
        addTargetedLeap.SetEnabled(!slot.Timeline.Stages
            .SelectMany(phase => phase.Operations)
            .OfType<StartCapabilityOperationSource>()
            .Any(op => op.CapabilityId == CharacterPackageCompiler.TargetedLeapCapabilityId));
        moveGroup.Add(addTargetedLeap);
        AddChargedDirectionalDashButton(moveGroup, stageIndex, slot);


        var presentationIds = _workspace.Draft.PresentationIds ?? Array.Empty<string>();
        var addPresentation = new Button(() =>
        {
            if (_lab == null || presentationIds.Count == 0 ||
                !_workspace.TryResolveCanonicalSlot(_lab.SelectedSlotId, out int sourceSlotIndex, out _))
                return;
            string presentationId = presentationIds[0];
            if (!_workspace.AddOperation(
                    sourceSlotIndex,
                    stageIndex,
                    new EmitPresentationOperationSource(0, AuthoringUnit.Ticks, presentationId)))
                return;
            UpdateTimelineControls();
            _selectedOperation = _timelineProjection?.Stages[stageIndex].Operations.LastOrDefault();
            CacheSelectedHitbox();
            SyncSelectedHitbox();
            _timelineTrack.SelectedOperation = _selectedOperation;
            RefreshInspector();
            SceneView.RepaintAll();
        })
        {
            text = "Add VFX event"
        };
        addPresentation.tooltip = "Add a presentation event using the first declared presentation ID.";
        addPresentation.SetEnabled(presentationIds.Count > 0);
        moveGroup.Add(addPresentation);
        _inspector.Add(moveGroup);
        var inventory = new Foldout { name = "effect-inventory", text = "Effects · all source stages", value = true };
        inventory.AddToClassList("effect-inventory");
        inventory.tooltip = "Select source fields without seeking. Use the explicit seek action to move the cursor.";
        foreach (var sourceStage in _timelineProjection!.Stages)
        {
            var stageLabel = new Label($"Stage {sourceStage.SourceStageIndex + 1} · ticks {sourceStage.StartTick}–{sourceStage.EndTick}");
            stageLabel.AddToClassList("effect-stage-label");
            inventory.Add(stageLabel);
            foreach (var operation in sourceStage.Operations)
            {
                bool selected = _selectedOperation?.SourceStageIndex == operation.SourceStageIndex &&
                    _selectedOperation.SourceOperationIndex == operation.SourceOperationIndex;
                var button = new Button(() => SelectOperation(operation))
                {
                    name = $"effect-{operation.SourceStageIndex}-{operation.SourceOperationIndex}",
                    tooltip = $"{operation.Summary} · {EffectTiming(operation, sourceStage.StartTick)}. Select to inspect without seeking.",
                };
                button.AddToClassList("effect-inventory-row");
                button.EnableInClassList("effect-selected", selected);
                var name = new Label($"{(selected ? "● " : "")}{operation.Summary}");
                name.AddToClassList("effect-name");
                button.Add(name);
                var timing = new Label(operation.EndTick > operation.StartTick + 1
                    ? $"{operation.StartTick}–{operation.EndTick}" : $"{operation.StartTick}");
                timing.AddToClassList("effect-range");
                button.Add(timing);
                inventory.Add(button);
            }
        }
        _inspector.Add(inventory);
        if (_selectedOperation != null)
        {
            var seek = new Button(() => ApplyCumulativeTick(_selectedOperation!.StartTick))
            {
                name = "seek-selected-effect", text = "Seek to selected effect",
            };
            seek.style.display = effectMode ? DisplayStyle.Flex : DisplayStyle.None;
            _inspector.Add(seek);
            var detail = BuildOperationFoldout(_selectedOperation, true);
            detail.name = "selected-effect-fields";
            detail.AddToClassList("inspector-section");
            detail.style.display = effectMode ? DisplayStyle.Flex : DisplayStyle.None;
            _inspector.Add(detail);
        }
        else _inspector.Add(new Label("Select an effect for its fields."));
        AddAttachmentAuthoring();
    }

    private static string EffectTiming(AbilityLabOperationProjection operation, int stageStart)
    {
        int localStart = operation.StartTick - stageStart;
        if (operation.Source is StartCapabilityOperationSource)
            return $"trigger {localStart} (move {operation.StartTick}); capability-owned / conditional windows";
        if (operation.Source is EmitPresentationOperationSource presentation && presentation.Placement.DurationTicks == 0)
            return $"trigger {localStart} (move {operation.StartTick}); prefab-owned lifetime";
        return $"stage ticks [{localStart}, {operation.EndTick - stageStart}) · move ticks [{operation.StartTick}, {operation.EndTick})";
    }

    private Foldout BuildOperationFoldout(AbilityLabOperationProjection operation, bool expanded)
    {
        string range = $"[{operation.StartTick}, {operation.EndTick})";
        var group = new Foldout { name = "selected-effect-fields", text = $"{operation.Summary} · {range}", value = expanded };
        group.AddToClassList("timeline-operation");
        group.RegisterValueChangedCallback(evt =>
        {
            if (evt.newValue && !expanded)
                SelectOperation(operation);
        });

        if (operation.Source is SpawnHitboxOperationSource hitboxOperation)
        {
            AddHitboxTiming(group, operation.Source.Tick, hitboxOperation.Hitbox);
            AddHitboxCombat(group, hitboxOperation.Hitbox);
            AddHitboxShape(group, hitboxOperation.Hitbox);
            AddHitboxAttachment(group, hitboxOperation.Hitbox);
        }
        else if (operation.Source is SpawnProjectileOperationSource projectileOperation)
        {
            var timing = new Foldout { text = "Timing", value = true };
            AddDelayedInteger(timing, "Start tick", projectileOperation.Tick, value =>
            {
                int maxTick = Mathf.Min(ushort.MaxValue, Mathf.Max(0, CurrentStage(operation.SourceStageIndex).DurationTicks - 1));
                CommitProjectileOperation(operation, current => current with
                {
                    Tick = (ushort)Mathf.Clamp(value, 0, maxTick)
                });
            });
            group.Add(timing);

            var flight = new Foldout { text = "Flight", value = true };
            AddDelayedInteger(flight, "Flight lifetime ticks", projectileOperation.Projectile.MaxFlightTicks, value =>
                CommitProjectileOperation(operation, current => current with
                {
                    Projectile = current.Projectile with
                    {
                        MaxFlightTicks = (ushort)Mathf.Clamp(value, 0, ushort.MaxValue)
                    }
                }));
            AddDelayedFloat(flight, "Speed (m/s)", projectileOperation.Projectile.Speed, value =>
                CommitProjectileOperation(operation, current => current with
                {
                    Projectile = current.Projectile with { Speed = value }
                }));
            AddDelayedFloat(flight, "Gravity", projectileOperation.Projectile.Gravity, value =>
                CommitProjectileOperation(operation, current => current with
                {
                    Projectile = current.Projectile with { Gravity = value }
                }));
            AddDelayedFloat(flight, "Radius", projectileOperation.Projectile.Radius, value =>
                CommitProjectileOperation(operation, current => current with
                {
                    Projectile = current.Projectile with { Radius = value }
                }));
            group.Add(flight);

            var launch = new Foldout { text = "Launch", value = true };
            AddDelayedFloat(launch, "Offset X", projectileOperation.Projectile.LaunchOffsetX, value =>
                CommitProjectileOperation(operation, current => current with
                {
                    Projectile = current.Projectile with { LaunchOffsetX = value }
                }));
            AddDelayedFloat(launch, "Offset Y", projectileOperation.Projectile.LaunchOffsetY, value =>
                CommitProjectileOperation(operation, current => current with
                {
                    Projectile = current.Projectile with { LaunchOffsetY = value }
                }));
            AddDelayedFloat(launch, "Offset Z", projectileOperation.Projectile.LaunchOffsetZ, value =>
                CommitProjectileOperation(operation, current => current with
                {
                    Projectile = current.Projectile with { LaunchOffsetZ = value }
                }));
            AddDelayedFloat(launch, "Yaw spread offset (degrees)", projectileOperation.Projectile.YawOffsetDegrees, value =>
                CommitProjectileOperation(operation, current => current with
                {
                    Projectile = current.Projectile with { YawOffsetDegrees = value }
                }));
            group.Add(launch);

            var combat = new Foldout { text = "Combat", value = true };
            AddDelayedFloat(combat, "Damage", projectileOperation.Projectile.Damage, value =>
                CommitProjectileOperation(operation, current => current with
                {
                    Projectile = current.Projectile with { Damage = value }
                }));
            AddDelayedFloat(combat, "Knockback angle", projectileOperation.Projectile.Angle, value =>
                CommitProjectileOperation(operation, current => current with
                {
                    Projectile = current.Projectile with { Angle = value }
                }));
            AddDelayedFloat(combat, "Base knockback", projectileOperation.Projectile.BaseKnockback, value =>
                CommitProjectileOperation(operation, current => current with
                {
                    Projectile = current.Projectile with { BaseKnockback = value }
                }));
            AddDelayedFloat(combat, "Knockback growth", projectileOperation.Projectile.KnockbackGrowth, value =>
                CommitProjectileOperation(operation, current => current with
                {
                    Projectile = current.Projectile with { KnockbackGrowth = value }
                }));
            AddDelayedInteger(combat, "Stun ticks", projectileOperation.Projectile.StunTicks, value =>
                CommitProjectileOperation(operation, current => current with
                {
                    Projectile = current.Projectile with
                    {
                        StunTicks = (ushort)Mathf.Clamp(value, 0, ushort.MaxValue)
                    }
                }));
            group.Add(combat);
        }
        else if (operation.Source is StartupAimCorrectionOperationSource correction)
        {
            group.Add(new Label("Acquires a target at move startup and corrects facing within this window."));
            group.Add(new Label("Ticks are relative to this stage; end is exclusive and must precede contact or launch."));
            AddDelayedInteger(group, "Start tick", correction.Tick,
                value => CommitStartupAimCorrection(current => current with
                {
                    Tick = (ushort)Mathf.Clamp(value, 0, current.EndTick - 1)
                }));
            AddDelayedInteger(group, "End tick (exclusive)", correction.EndTick,
                value => CommitStartupAimCorrection(current => current with
                {
                    EndTick = (ushort)Mathf.Clamp(value, current.Tick + 1, CurrentStage().DurationTicks)
                }));
            AddDelayedFloat(group, "Acquisition range (m)", correction.AcquisitionRange,
                value => CommitStartupAimCorrection(current => current with { AcquisitionRange = value }));
            AddDelayedFloat(group, "Acquisition half-angle (degrees)", correction.AcquisitionHalfAngleDegrees,
                value => CommitStartupAimCorrection(current => current with { AcquisitionHalfAngleDegrees = value }));
            AddDelayedFloat(group, "Maximum yaw (degrees)", correction.MaxYawDegrees,
                value => CommitStartupAimCorrection(current => current with { MaxYawDegrees = value }));
            AddDelayedFloat(group, "Maximum pitch (degrees)", correction.MaxPitchDegrees,
                value => CommitStartupAimCorrection(current => current with { MaxPitchDegrees = value }));
            AddDelayedFloat(group, "Yaw speed (degrees/s)", correction.YawDegreesPerSecond,
                value => CommitStartupAimCorrection(current => current with { YawDegreesPerSecond = value }));
            AddDelayedFloat(group, "Pitch speed (degrees/s)", correction.PitchDegreesPerSecond,
                value => CommitStartupAimCorrection(current => current with { PitchDegreesPerSecond = value }));
        }
        else if (operation.Source is ForwardLungeOperationSource lunge)
        {
            group.Add(new Label("Direction is captured from facing when the lunge begins."));
            AddDelayedInteger(group, "Start tick", lunge.Tick, CommitForwardLungeStart);
            AddDelayedInteger(group, "Duration ticks", lunge.DurationTicks,
                value => CommitForwardLunge(current => current with
                {
                    DurationTicks = (ushort)Mathf.Clamp(value, 1,
                        Mathf.Max(1, CurrentStage().DurationTicks - current.Tick))
                }));
            AddDelayedFloat(group, "Speed (m/s)", lunge.Speed,
                value => CommitForwardLunge(current => current with { Speed = Mathf.Max(0.01f, value) }));
        }
        else if (operation.Source is GravityWindowOperationSource gravity)
        {
            group.Add(new Label("Scales the active airborne gravity; FastFall overrides the window."));
            AddDelayedInteger(group, "Start tick", gravity.Tick,
                value => CommitGravityWindow(current => current with
                {
                    Tick = (ushort)Mathf.Clamp(value, 0,
                        Mathf.Max(0, CurrentStage().DurationTicks - current.DurationTicks))
                }));
            AddDelayedInteger(group, "Duration ticks", gravity.DurationTicks,
                value => CommitGravityWindow(current => current with
                {
                    DurationTicks = (ushort)Mathf.Clamp(value, 1,
                        Mathf.Max(1, CurrentStage().DurationTicks - current.Tick))
                }));
            AddDelayedFloat(group, "Gravity scale", gravity.GravityScale,
                value => CommitGravityWindow(current => current with
                {
                    GravityScale = Mathf.Clamp01(value)
                }));
        }
        else if (operation.Source is ArmorWindowOperationSource armor)
        {
            group.Add(new Label("Takes damage without ordinary knockback or hitstun; grabs still work."));
            AddDelayedInteger(group, "Start tick", armor.Tick,
                value => CommitArmorWindow(current => current with
                {
                    Tick = (ushort)Mathf.Clamp(value, 0,
                        Mathf.Max(0, CurrentStage().DurationTicks - current.DurationTicks))
                }));
            AddDelayedInteger(group, "Duration ticks", armor.DurationTicks,
                value => CommitArmorWindow(current => current with
                {
                    DurationTicks = (ushort)Mathf.Clamp(value, 1,
                        Mathf.Max(1, CurrentStage().DurationTicks - current.Tick))
                }));
        }
        else if (operation.Source is StartCapabilityOperationSource targeted &&
            targeted.Parameters is TargetedLeapCapabilityParameters leap)
        {
            AddTargetedLeapInspector(group, operation, leap);
        }
        else if (operation.Source is StartCapabilityOperationSource capability &&
            capability.Parameters is ChargedDirectionalDashCapabilityParameters chargedDash)
        {
            AddChargedDashInspector(group, operation, capability, chargedDash);
        }
        else if (operation.Source is StartCapabilityOperationSource chargedProjectileSource &&
            chargedProjectileSource.Parameters is ChargedProjectileCapabilityParameters chargedProjectile)
        {
            AddChargedProjectileInspector(group, operation, chargedProjectile);
        }
        else if (operation.Source is StartCapabilityOperationSource otherCapability)
        {
            AddCapabilityPresentationSelector(group, operation, otherCapability);
        }
        else if (operation.Source is EmitPresentationOperationSource presentationOperation)
        {
            var ids = (_workspace.Draft.PresentationIds ?? Array.Empty<string>())
                .Concat(new[] { presentationOperation.PresentationId })
                .Where(id => !string.IsNullOrEmpty(id))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            var choices = BuildPresentationChoices(ids);
            var labels = choices.Select(choice => choice.Label).ToList();
            var selectedChoice = choices.FirstOrDefault(choice => choice.SemanticId == presentationOperation.PresentationId) ?? choices[0];
            var field = new PopupField<string>(
                "Presentation",
                labels,
                labels.IndexOf(selectedChoice.Label));
            field.name = FieldName(group, "Presentation");
            field.RegisterValueChangedCallback(evt =>
            {
                var choice = choices.FirstOrDefault(item => item.Label == evt.newValue);
                if (choice != null)
                    CommitPresentationOperationId(operation, choice.SemanticId);
            });
            group.Add(field);
            AddDelayedInteger(group, "Start tick", presentationOperation.Tick,
                value => CommitPresentationOperationStart(operation, value));
            AddPresentationPlacement(group, operation, presentationOperation.Placement);
        }
        else group.Add(new Label("Fields unavailable for this operation type. Source remains visible in the inventory."));


        return group;
    }

    private void AddPresentationPlacement(Foldout group, AbilityLabOperationProjection operation, PresentationPlacement placement)
    {
        var placementGroup = new Foldout { text = "Placement", value = true };
        var mode = new EnumField("Attachment", placement.AttachmentMode) { name = FieldName(placementGroup, "Attachment") };
        mode.RegisterValueChangedCallback(evt =>
            CommitPresentationPlacement(operation, current => current with
            {
                AttachmentMode = (AuthoringPresentationAttachmentMode)evt.newValue,
                BoneId = (AuthoringPresentationAttachmentMode)evt.newValue == AuthoringPresentationAttachmentMode.Bone
                    ? current.BoneId ?? AuthoringBoneChoices().FirstOrDefault(id => !string.IsNullOrEmpty(id))
                    : null,
            }));
        placementGroup.Add(mode);
        AddBonePopup(placementGroup, "Bone", placement.BoneId,
            boneId => CommitPresentationPlacement(operation, current => current with
            {
                AttachmentMode = string.IsNullOrEmpty(boneId) ? AuthoringPresentationAttachmentMode.World : AuthoringPresentationAttachmentMode.Bone,
                BoneId = boneId,
            }));
        AddDelayedFloat(placementGroup, "Local position X", placement.LocalPositionX,
            value => CommitPresentationPlacement(operation, current => current with { LocalPositionX = value }));
        AddDelayedFloat(placementGroup, "Local position Y", placement.LocalPositionY,
            value => CommitPresentationPlacement(operation, current => current with { LocalPositionY = value }));
        AddDelayedFloat(placementGroup, "Local position Z", placement.LocalPositionZ,
            value => CommitPresentationPlacement(operation, current => current with { LocalPositionZ = value }));
        AddDelayedFloat(placementGroup, "Local rotation X", placement.LocalRotationX,
            value => CommitPresentationPlacement(operation, current => current with { LocalRotationX = value }));
        AddDelayedFloat(placementGroup, "Local rotation Y", placement.LocalRotationY,
            value => CommitPresentationPlacement(operation, current => current with { LocalRotationY = value }));
        AddDelayedFloat(placementGroup, "Local rotation Z", placement.LocalRotationZ,
            value => CommitPresentationPlacement(operation, current => current with { LocalRotationZ = value }));
        AddDelayedFloat(placementGroup, "Local scale X", placement.LocalScaleX,
            value => CommitPresentationPlacement(operation, current => current with { LocalScaleX = Mathf.Max(0.0001f, value) }));
        AddDelayedFloat(placementGroup, "Local scale Y", placement.LocalScaleY,
            value => CommitPresentationPlacement(operation, current => current with { LocalScaleY = Mathf.Max(0.0001f, value) }));
        AddDelayedFloat(placementGroup, "Local scale Z", placement.LocalScaleZ,
            value => CommitPresentationPlacement(operation, current => current with { LocalScaleZ = Mathf.Max(0.0001f, value) }));
        AddDelayedInteger(placementGroup, "Duration ticks", placement.DurationTicks,
            value => CommitPresentationPlacement(operation, current => current with
            {
                DurationTicks = (ushort)Mathf.Clamp(value, 1,
                    Mathf.Max(1, CurrentStage().DurationTicks - operation.Source.Tick)),
            }));
        group.Add(placementGroup);
    }

    private void AddTargetedLeapInspector(
        Foldout group, AbilityLabOperationProjection operation, TargetedLeapCapabilityParameters leap)
    {
        group.Add(new Label("Hold to aim; release launches toward the selected distance. The impact occurs on landing, not at a fixed timeline tick."));
        AddDelayedInteger(group, "Start tick", operation.Source.Tick,
            value => CommitCapabilityStart(operation, value));
        AddDelayedInteger(group, "Aim limit ticks (0 = unlimited)", leap.MaxAimTicks,
            value => CommitTargetedLeap(p => p with { MaxAimTicks = (ushort)Mathf.Clamp(value, 0, ushort.MaxValue) }));
        AddDelayedInteger(group, "Flight limit ticks", leap.MaxFlightTicks,
            value => CommitTargetedLeap(p => p with { MaxFlightTicks = (ushort)Mathf.Clamp(value, 1, ushort.MaxValue) }));
        AddDelayedFloat(group, "Minimum target range (m)", leap.MinRange,
            value => CommitTargetedLeap(p => p with { MinRange = Mathf.Max(0f, value) }));
        AddDelayedFloat(group, "Maximum target range (m)", leap.MaxRange,
            value => CommitTargetedLeap(p => p with { MaxRange = Mathf.Max(0f, value) }));
        AddDelayedFloat(group, "Vertical launch speed (m/s)", leap.LaunchVerticalSpeed,
            value => CommitTargetedLeap(p => p with { LaunchVerticalSpeed = Mathf.Max(0.01f, value) }));
        AddDelayedInteger(group, "Landing animation seek tick", leap.LandingSeekTick,
            value => CommitTargetedLeap(p => p with { LandingSeekTick = (ushort)Mathf.Clamp(value, 0, ushort.MaxValue) }));
        AddDelayedInteger(group, "Landing recovery ticks", leap.RecoveryTicks,
            value => CommitTargetedLeap(p => p with { RecoveryTicks = (ushort)Mathf.Clamp(value, 1, ushort.MaxValue) }));

        var landing = new Foldout { text = "On landing · hitbox", value = false };
        AddDelayedInteger(landing, "Hitstun gate (0 = off)", leap.Hitbox.StunTicks,
            value => CommitHitbox(h => h with { StunTicks = (ushort)Mathf.Clamp(value, 0, ushort.MaxValue) }));
        AddDelayedInteger(landing, "Active duration ticks", leap.Hitbox.DurationTicks,
            value => CommitHitbox(h => h with { DurationTicks = (ushort)Mathf.Clamp(value, 1, ushort.MaxValue) }));
        AddToggle(landing, "Interruptible", leap.Hitbox.Interruptible,
            value => CommitHitbox(h => h with { Interruptible = value }));
        AddDelayedInteger(landing, "Hit group", leap.Hitbox.HitGroup,
            value => CommitHitbox(h => h with { HitGroup = (byte)Mathf.Clamp(value, 0, byte.MaxValue) }));
        var direction = new EnumField("Knockback direction", leap.Hitbox.KnockbackDirection) { name = FieldName(landing, "Knockback direction") };
        direction.RegisterValueChangedCallback(evt =>
            CommitHitbox(h => h with { KnockbackDirection = (AuthoringKnockbackDirection)evt.newValue }));
        landing.Add(direction);
        AddHitboxCombat(landing, leap.Hitbox);
        AddHitboxShape(landing, leap.Hitbox);
        AddHitboxAttachment(landing, leap.Hitbox);
        group.Add(landing);
    }

    private void CommitTargetedLeap(Func<TargetedLeapCapabilityParameters, TargetedLeapCapabilityParameters> edit)
    {
        if (_updatingControls || _lab == null ||
            _selectedOperation?.Source is not StartCapabilityOperationSource operation ||
            operation.Parameters is not TargetedLeapCapabilityParameters parameters ||
            !_workspace.TryResolveCanonicalSlot(_lab.SelectedSlotId, out int slotIndex, out _))
            return;
        int stageIndex = _selectedOperation.SourceStageIndex;
        int operationIndex = _selectedOperation.SourceOperationIndex;
        _updatingControls = true;
        bool accepted;
        try
        {
            accepted = _workspace.ReplaceOperation(slotIndex, stageIndex, operationIndex,
                operation with { Parameters = edit(parameters) });
        }
        finally { _updatingControls = false; }
        if (!accepted) return;
        UpdateTimelineControls();
        _selectedOperation = FindProjectedOperation(stageIndex, operationIndex);
        _timelineTrack.SelectedOperation = _selectedOperation;
        RefreshInspector();
        SceneView.RepaintAll();
    }

    private void AddCapabilityPresentationSelector(
        Foldout group,
        AbilityLabOperationProjection operation,
        StartCapabilityOperationSource capability)
    {
        group.Add(new Label($"Capability · {capability.CapabilityId}"));
        group.Add(new Label("Capability timing and remaining parameters are capability-owned; no complete typed editor is available here."));
        AddDelayedInteger(group, "Start tick", capability.Tick,
            value => CommitCapabilityStart(operation, value));
        if (capability.Parameters is not (MankiRoundBombCapabilityParameters or MankiJetpackBoostCapabilityParameters or MankiBazookaCapabilityParameters))
            return;
        string presentationId = capability.Parameters switch
        {
            MankiRoundBombCapabilityParameters parameters => parameters.ExplosionPresentationId,
            MankiJetpackBoostCapabilityParameters parameters => parameters.ExplosionPresentationId,
            MankiBazookaCapabilityParameters parameters => parameters.ExplosionPresentationId,
            _ => "",
        };
        var choices = BuildPresentationChoices(new[] { presentationId });
        var labels = choices.Select(choice => choice.Label).ToList();
        var selectedChoice = choices.FirstOrDefault(choice => choice.SemanticId == presentationId) ?? choices[0];
        var field = new PopupField<string>(
            "Explosion VFX",
            labels,
            labels.IndexOf(selectedChoice.Label))
        {
            name = FieldName(group, "Explosion VFX"),
            tooltip = "Package-owned explosion presentation emitted by this capability.",
        };
        field.RegisterValueChangedCallback(evt =>
        {
            var choice = choices.FirstOrDefault(item => item.Label == evt.newValue);
            if (choice != null)
                CommitCapabilityPresentationId(operation, choice.SemanticId);
        });
        group.Add(field);
    }


    private void CommitCapabilityPresentationId(AbilityLabOperationProjection selected, string semanticId)
    {
        if (_updatingControls || _lab == null ||
            selected.Source is not StartCapabilityOperationSource original ||
            !_workspace.TryResolveCanonicalSlot(_lab.SelectedSlotId, out int slotIndex, out _))
            return;

        var updatedParameters = original.Parameters switch
        {
            MankiRoundBombCapabilityParameters parameters => parameters with { ExplosionPresentationId = semanticId },
            MankiJetpackBoostCapabilityParameters parameters => parameters with { ExplosionPresentationId = semanticId },
            MankiBazookaCapabilityParameters parameters => parameters with { ExplosionPresentationId = semanticId },
            _ => original.Parameters,
        };
        if (ReferenceEquals(updatedParameters, original.Parameters)) return;

        _updatingControls = true;
        bool accepted;
        try
        {
            accepted = _workspace.ReplaceOperation(
                slotIndex,
                selected.SourceStageIndex,
                selected.SourceOperationIndex,
                original with { Parameters = updatedParameters });
        }
        finally { _updatingControls = false; }
        if (!accepted) return;
        UpdateTimelineControls();
        _selectedOperation = FindProjectedOperation(selected.SourceStageIndex, selected.SourceOperationIndex);
        _timelineTrack.SelectedOperation = _selectedOperation;
        RefreshInspector();
        SceneView.RepaintAll();
    }

    private void CommitCapabilityStart(AbilityLabOperationProjection selected, int startTick)
    {
        if (_updatingControls || _lab == null ||
            selected.Source is not StartCapabilityOperationSource)
            return;
        CharacterStageSource stage = CurrentStage();
        startTick = Mathf.Clamp(startTick, 0, Mathf.Max(0, stage.DurationTicks - 1));
        _updatingControls = true;
        bool accepted;
        try
        {
            accepted = _workspace.ReplaceOperationTick(
                _lab.SelectedSlotId,
                selected.SourceStageIndex,
                selected.SourceOperationIndex,
                startTick);
        }
        finally { _updatingControls = false; }
        if (!accepted) return;
        UpdateTimelineControls();
        _selectedOperation = FindProjectedOperation(selected.SourceStageIndex, selected.SourceOperationIndex);
        _timelineTrack.SelectedOperation = _selectedOperation;
        RefreshInspector();
        SceneView.RepaintAll();
    }

    private void CommitPresentationPlacement(
        AbilityLabOperationProjection selected,
        Func<PresentationPlacement, PresentationPlacement> edit)
    {
        if (_updatingControls || _lab == null ||
            selected.Source is not EmitPresentationOperationSource original)
            return;
        _updatingControls = true;
        bool accepted;
        try
        {
            accepted = _workspace.ReplacePresentationPlacement(
                _lab.SelectedSlotId,
                selected.SourceStageIndex,
                selected.SourceOperationIndex,
                edit(original.Placement));
        }
        finally { _updatingControls = false; }
        if (!accepted) return;
        UpdateTimelineControls();
        _selectedOperation = FindProjectedOperation(selected.SourceStageIndex, selected.SourceOperationIndex);
        _timelineTrack.SelectedOperation = _selectedOperation;
        RefreshInspector();
        SceneView.RepaintAll();
    }

    private void CommitPresentationOperationId(AbilityLabOperationProjection selected, string semanticId)
    {
        if (_updatingControls || _lab == null ||
            selected.Source is not EmitPresentationOperationSource original ||
            !_workspace.TryResolveCanonicalSlot(_lab.SelectedSlotId, out int slotIndex, out _))
            return;
        _updatingControls = true;
        bool accepted;
        try
        {
            accepted = _workspace.ReplaceOperation(
                slotIndex,
                selected.SourceStageIndex,
                selected.SourceOperationIndex,
                original with { PresentationId = semanticId });
        }
        finally { _updatingControls = false; }
        if (!accepted) return;
        UpdateTimelineControls();
        _selectedOperation = FindProjectedOperation(selected.SourceStageIndex, selected.SourceOperationIndex);
        _timelineTrack.SelectedOperation = _selectedOperation;
        RefreshInspector();
        SceneView.RepaintAll();
    }

    private void CommitPresentationOperationStart(AbilityLabOperationProjection selected, int startTick)
    {
        if (_updatingControls || _lab == null || selected.Source is not EmitPresentationOperationSource)
            return;
        CharacterStageSource stage = CurrentStage();
        startTick = Mathf.Clamp(startTick, 0, Mathf.Max(0, stage.DurationTicks - 1));
        _updatingControls = true;
        bool accepted;
        try
        {
            accepted = _workspace.ReplaceOperationTick(
                _lab.SelectedSlotId,
                selected.SourceStageIndex,
                selected.SourceOperationIndex,
                startTick);
        }
        finally { _updatingControls = false; }
        if (!accepted) return;
        UpdateTimelineControls();
        _selectedOperation = FindProjectedOperation(selected.SourceStageIndex, selected.SourceOperationIndex);
        _timelineTrack.SelectedOperation = _selectedOperation;
        RefreshInspector();
        SceneView.RepaintAll();
    }

    private void AddChargedProjectileInspector(Foldout group, AbilityLabOperationProjection operation,
        ChargedProjectileCapabilityParameters value)
    {
        group.Add(new Label("Hold to charge; release fires one projectile after startup. Charge caps without firing."));
        AddDelayedInteger(group, "Maximum charge ticks", value.MaxChargeTicks,
            v => CommitChargedProjectile(operation, p => p with { MaxChargeTicks = UShort(v) }));
        AddDelayedInteger(group, "Tier 2 threshold ticks", value.Tier2Ticks,
            v => CommitChargedProjectile(operation, p => p with { Tier2Ticks = UShort(v) }));
        AddDelayedInteger(group, "Tier 3 threshold ticks", value.Tier3Ticks,
            v => CommitChargedProjectile(operation, p => p with { Tier3Ticks = UShort(v) }));
        AddDelayedInteger(group, "Firing startup ticks", value.FireTriggerTick,
            v => CommitChargedProjectile(operation, p => p with { FireTriggerTick = UShort(v) }));
        AddDelayedFloat(group, "Tier 1 radius (m)", value.Projectile.Radius,
            v => CommitChargedProjectile(operation, p => p with { Projectile = p.Projectile with { Radius = v } }));
        AddDelayedFloat(group, "Tier 2 radius (m)", value.Tier2Radius,
            v => CommitChargedProjectile(operation, p => p with { Tier2Radius = v }));
        AddDelayedFloat(group, "Tier 3 radius (m)", value.Tier3Radius,
            v => CommitChargedProjectile(operation, p => p with { Tier3Radius = v }));
        AddDelayedFloat(group, "Speed (m/s)", value.Projectile.Speed,
            v => CommitChargedProjectile(operation, p => p with { Projectile = p.Projectile with { Speed = v } }));
        AddDelayedInteger(group, "Flight lifetime ticks", value.Projectile.MaxFlightTicks,
            v => CommitChargedProjectile(operation, p => p with { Projectile = p.Projectile with { MaxFlightTicks = UShort(v) } }));
        AddDelayedFloat(group, "Gravity", value.Projectile.Gravity,
            v => CommitChargedProjectile(operation, p => p with { Projectile = p.Projectile with { Gravity = v } }));
        AddDelayedFloat(group, "Damage", value.Projectile.Damage,
            v => CommitChargedProjectile(operation, p => p with { Projectile = p.Projectile with { Damage = v } }));
        AddDelayedFloat(group, "Knockback angle", value.Projectile.Angle,
            v => CommitChargedProjectile(operation, p => p with { Projectile = p.Projectile with { Angle = v } }));
        AddDelayedFloat(group, "Base knockback", value.Projectile.BaseKnockback,
            v => CommitChargedProjectile(operation, p => p with { Projectile = p.Projectile with { BaseKnockback = v } }));
        AddDelayedFloat(group, "Knockback growth", value.Projectile.KnockbackGrowth,
            v => CommitChargedProjectile(operation, p => p with { Projectile = p.Projectile with { KnockbackGrowth = v } }));
        AddDelayedInteger(group, "Stun ticks", value.Projectile.StunTicks,
            v => CommitChargedProjectile(operation, p => p with { Projectile = p.Projectile with { StunTicks = UShort(v) } }));
        AddDelayedFloat(group, "Launch offset X", value.Projectile.LaunchOffsetX,
            v => CommitChargedProjectile(operation, p => p with { Projectile = p.Projectile with { LaunchOffsetX = v } }));
        AddDelayedFloat(group, "Launch offset Y", value.Projectile.LaunchOffsetY,
            v => CommitChargedProjectile(operation, p => p with { Projectile = p.Projectile with { LaunchOffsetY = v } }));
        AddDelayedFloat(group, "Launch offset Z", value.Projectile.LaunchOffsetZ,
            v => CommitChargedProjectile(operation, p => p with { Projectile = p.Projectile with { LaunchOffsetZ = v } }));
    }

    private void CommitChargedProjectile(AbilityLabOperationProjection selected,
        Func<ChargedProjectileCapabilityParameters, ChargedProjectileCapabilityParameters> edit)
    {
        if (_updatingControls || _lab == null || selected.Source is not StartCapabilityOperationSource original ||
            original.Parameters is not ChargedProjectileCapabilityParameters parameters ||
            !_workspace.TryResolveCanonicalSlot(_lab.SelectedSlotId, out int slotIndex, out _)) return;
        _updatingControls = true;
        bool accepted;
        try
        {
            accepted = _workspace.ReplaceOperation(slotIndex, selected.SourceStageIndex, selected.SourceOperationIndex,
                original with { Parameters = edit(parameters) });
        }
        finally { _updatingControls = false; }
        if (!accepted) return;
        UpdateTimelineControls();
        _selectedOperation = FindProjectedOperation(selected.SourceStageIndex, selected.SourceOperationIndex);
        _timelineTrack.SelectedOperation = _selectedOperation;
        RefreshInspector();
        SceneView.RepaintAll();
    }

    private void CommitProjectileOperation(
        AbilityLabOperationProjection selected,
        Func<SpawnProjectileOperationSource, SpawnProjectileOperationSource> edit)
    {
        if (_updatingControls || _lab == null ||
            selected.Source is not SpawnProjectileOperationSource original ||
            !_workspace.TryResolveCanonicalSlot(_lab.SelectedSlotId, out int slotIndex, out _))
            return;
        int stageIndex = selected.SourceStageIndex;
        int operationIndex = selected.SourceOperationIndex;
        _updatingControls = true;
        bool accepted;
        try
        {
            accepted = _workspace.ReplaceOperation(
                slotIndex, stageIndex, operationIndex, edit(original));
        }
        finally { _updatingControls = false; }
        if (!accepted) return;
        UpdateTimelineControls();
        _selectedOperation = FindProjectedOperation(stageIndex, operationIndex);
        _timelineTrack.SelectedOperation = _selectedOperation;
        RefreshInspector();
        SceneView.RepaintAll();
    }

    private void CommitStage(Func<CharacterStageSource, CharacterStageSource> edit, int stageIndex)
    {
        if (_updatingControls || _lab == null) return;
        _updatingControls = true;
        try { _workspace.ReplaceStage(_lab.SelectedSlotId, stageIndex, edit(CurrentStage(stageIndex))); }
        finally { _updatingControls = false; }
        UpdateTimelineControls();
        RefreshInspector();
        SceneView.RepaintAll();
    }

    private CharacterStageSource CurrentStage(int stageIndex = -1)
    {
        if (_lab == null || !_workspace.TryResolveCanonicalSlot(_lab.SelectedSlotId, out _, out var slot))
            throw new InvalidOperationException("No selected source slot.");
        return slot.Timeline.Stages[stageIndex >= 0 ? stageIndex : _selectedOperation?.SourceStageIndex ?? _inspectedStageIndex];
    }

    private void CommitStartupAimCorrection(Func<StartupAimCorrectionOperationSource, StartupAimCorrectionOperationSource> edit)
    {
        if (_updatingControls || _lab == null ||
            _selectedOperation?.Source is not StartupAimCorrectionOperationSource original ||
            !_workspace.TryResolveCanonicalSlot(_lab.SelectedSlotId, out int slotIndex, out _))
            return;
        int stageIndex = _selectedOperation.SourceStageIndex;
        int operationIndex = _selectedOperation.SourceOperationIndex;
        _updatingControls = true;
        bool accepted;
        try
        {
            accepted = _workspace.ReplaceOperation(
                slotIndex, stageIndex, operationIndex, edit(original));
        }
        finally { _updatingControls = false; }
        if (!accepted) return;
        UpdateTimelineControls();
        _selectedOperation = FindProjectedOperation(stageIndex, operationIndex);
        _timelineTrack.SelectedOperation = _selectedOperation;
        RefreshInspector();
        SceneView.RepaintAll();
    }

    private void CommitForwardLunge(Func<ForwardLungeOperationSource, ForwardLungeOperationSource> edit)
    {
        if (_updatingControls || _lab == null ||
            _selectedOperation?.Source is not ForwardLungeOperationSource original ||
            !_workspace.TryResolveCanonicalSlot(_lab.SelectedSlotId, out int slotIndex, out _))
            return;
        int stageIndex = _selectedOperation.SourceStageIndex;
        int operationIndex = _selectedOperation.SourceOperationIndex;
        _updatingControls = true;
        bool accepted;
        try
        {
            accepted = _workspace.ReplaceOperation(
                slotIndex, stageIndex, operationIndex, edit(original));
        }
        finally { _updatingControls = false; }
        if (!accepted) return;
        UpdateTimelineControls();
        _selectedOperation = FindProjectedOperation(stageIndex, operationIndex);
        _timelineTrack.SelectedOperation = _selectedOperation;
        RefreshInspector();
        SceneView.RepaintAll();
    }

    private void CommitForwardLungeStart(int startTick)
    {
        if (_selectedOperation?.Source is not ForwardLungeOperationSource lunge) return;
        int maxStart = Mathf.Max(0, CurrentStage().DurationTicks - lunge.DurationTicks);
        CommitForwardLunge(current => current with
        {
            Tick = (ushort)Mathf.Clamp(startTick, 0, maxStart)
        });
    }

    private void CommitGravityWindow(Func<GravityWindowOperationSource, GravityWindowOperationSource> edit)
    {
        if (_updatingControls || _lab == null ||
            _selectedOperation?.Source is not GravityWindowOperationSource original ||
            !_workspace.TryResolveCanonicalSlot(_lab.SelectedSlotId, out int slotIndex, out _))
            return;
        int stageIndex = _selectedOperation.SourceStageIndex;
        int operationIndex = _selectedOperation.SourceOperationIndex;
        _updatingControls = true;
        bool accepted;
        try
        {
            accepted = _workspace.ReplaceOperation(
                slotIndex, stageIndex, operationIndex, edit(original));
        }
        finally { _updatingControls = false; }
        if (!accepted) return;
        UpdateTimelineControls();
        _selectedOperation = FindProjectedOperation(stageIndex, operationIndex);
        _timelineTrack.SelectedOperation = _selectedOperation;
        RefreshInspector();
        SceneView.RepaintAll();
    }

    private void CommitArmorWindow(Func<ArmorWindowOperationSource, ArmorWindowOperationSource> edit)
    {
        if (_updatingControls || _lab == null ||
            _selectedOperation?.Source is not ArmorWindowOperationSource original ||
            !_workspace.TryResolveCanonicalSlot(_lab.SelectedSlotId, out int slotIndex, out _))
            return;
        int stageIndex = _selectedOperation.SourceStageIndex;
        int operationIndex = _selectedOperation.SourceOperationIndex;
        _updatingControls = true;
        bool accepted;
        try
        {
            accepted = _workspace.ReplaceOperation(
                slotIndex, stageIndex, operationIndex, edit(original));
        }
        finally { _updatingControls = false; }
        if (!accepted) return;
        UpdateTimelineControls();
        _selectedOperation = FindProjectedOperation(stageIndex, operationIndex);
        _timelineTrack.SelectedOperation = _selectedOperation;
        RefreshInspector();
        SceneView.RepaintAll();
    }


    private void CommitHitbox(Func<HitboxSource, HitboxSource> edit)
    {
        if (_selectedOperation?.Source is StartCapabilityOperationSource capability &&
            capability.Parameters is TargetedLeapCapabilityParameters)
        {
            CommitTargetedLeap(p => p with { Hitbox = edit(p.Hitbox) });
            return;
        }
        if (_updatingControls || _lab == null || _selectedOperation?.Source is not SpawnHitboxOperationSource original) return;
        int stageIndex = _selectedOperation.SourceStageIndex;
        int operationIndex = _selectedOperation.SourceOperationIndex;
        _updatingControls = true;
        bool accepted;
        try
        {
            accepted = _workspace.ReplaceHitbox(_lab.SelectedSlotId, stageIndex, operationIndex, edit(original.Hitbox));
        }
        finally { _updatingControls = false; }
        if (!accepted) return;
        UpdateTimelineControls();
        _selectedOperation = FindProjectedOperation(stageIndex, operationIndex);
        _timelineTrack.SelectedOperation = _selectedOperation;
        RefreshInspector();
        SceneView.RepaintAll();
    }
    private void CommitHitboxStart(int startTick)
    {
        if (_updatingControls || _lab == null || _selectedOperation?.Source is not SpawnHitboxOperationSource original) return;
        int stageIndex = _selectedOperation.SourceStageIndex;
        int operationIndex = _selectedOperation.SourceOperationIndex;
        var stage = CurrentStage();
        startTick = Mathf.Clamp(startTick, 0, Mathf.Max(0, stage.DurationTicks - original.Hitbox.DurationTicks));
        _updatingControls = true;
        bool accepted;
        try
        {
            accepted = _workspace.ReplaceOperationTick(_lab.SelectedSlotId, stageIndex, operationIndex, startTick);
        }
        finally { _updatingControls = false; }
        if (!accepted) return;
        UpdateTimelineControls();
        _selectedOperation = FindProjectedOperation(stageIndex, operationIndex);
        _timelineTrack.SelectedOperation = _selectedOperation;
        RefreshInspector();
        SceneView.RepaintAll();
    }

    private void AddHitboxTiming(Foldout group, int startTick, HitboxSource value)
    {
        var timing = new Foldout { text = "Timing", value = true };
        AddDelayedInteger(timing, "Start tick", startTick, CommitHitboxStart);
        AddDelayedInteger(timing, "Hitstun gate (0 = off)", value.StunTicks, v => CommitHitbox(h => h with { StunTicks = (ushort)Mathf.Clamp(v, 0, ushort.MaxValue) }));
        AddDelayedInteger(timing, "Active duration ticks", value.DurationTicks, v => CommitHitbox(h => h with { DurationTicks = (ushort)Mathf.Clamp(v, 0, ushort.MaxValue) }));
        AddToggle(timing, "Interruptible", value.Interruptible, v => CommitHitbox(h => h with { Interruptible = v }));
        AddDelayedInteger(timing, "Hit group", value.HitGroup, v => CommitHitbox(h => h with { HitGroup = (byte)Mathf.Clamp(v, 0, byte.MaxValue) }));
        group.Add(timing);
    }

    private void AddHitboxCombat(Foldout group, HitboxSource value)
    {
        var combat = new Foldout { text = "Combat", value = true };
        AddDelayedFloat(combat, "Damage", value.Damage, v => CommitHitbox(h => h with { Damage = v }));
        AddDelayedFloat(combat, "Angle", value.Angle, v => CommitHitbox(h => h with { Angle = v }));
        AddDelayedFloat(combat, "Base knockback", value.BaseKnockback, v => CommitHitbox(h => h with { BaseKnockback = v }));
        AddDelayedFloat(combat, "Knockback growth", value.KnockbackGrowth, v => CommitHitbox(h => h with { KnockbackGrowth = v }));
        AddDelayedInteger(combat, "Fixed hitstun ticks (0 = automatic)", value.FixedHitstunTicks,
            v => CommitHitbox(h => h with { FixedHitstunTicks = (ushort)Mathf.Clamp(v, 0, 240) }));
        group.Add(combat);
    }

    private void AddHitboxShape(Foldout group, HitboxSource value)
    {
        var shape = new Foldout { text = "Shape", value = true };
        var enumField = new EnumField("Shape", value.Shape) { name = FieldName(shape, "Shape") };
        enumField.RegisterValueChangedCallback(evt => CommitHitbox(h => h with { Shape = (AuthoringHitboxShape)evt.newValue }));
        shape.Add(enumField);
        AddDelayedFloat(shape, "Radius", value.Radius, v => CommitHitbox(h => h with { Radius = v }));
        AddDelayedFloat(shape, "Offset X", value.OffsetX, v => CommitHitbox(h => h with { OffsetX = v }));
        AddDelayedFloat(shape, "Offset Y", value.OffsetY, v => CommitHitbox(h => h with { OffsetY = v }));
        AddDelayedFloat(shape, "Offset Z", value.OffsetZ, v => CommitHitbox(h => h with { OffsetZ = v }));
        AddDelayedFloat(shape, "End offset X", value.EndOffsetX, v => CommitHitbox(h => h with { EndOffsetX = v }));
        AddDelayedFloat(shape, "End offset Y", value.EndOffsetY, v => CommitHitbox(h => h with { EndOffsetY = v }));
        AddDelayedFloat(shape, "End offset Z", value.EndOffsetZ, v => CommitHitbox(h => h with { EndOffsetZ = v }));
        group.Add(shape);
    }

    private void AddHitboxAttachment(Foldout group, HitboxSource value)
    {
        var attachment = new Foldout { text = "Attachment", value = true };
        AddBonePopup(attachment, "Start bone", value.StartBoneId, id => CommitHitbox(h => h with { StartBoneId = id }));
        AddBonePopup(attachment, "End bone", value.EndBoneId, id => CommitHitbox(h => h with { EndBoneId = id }));
        group.Add(attachment);
    }

    private void AddBonePopup(Foldout group, string label, string? value, Action<string?> commit)
    {
        var choices = AuthoringBoneChoices();
        if (!string.IsNullOrEmpty(value) && !choices.Contains(value, StringComparer.Ordinal))
            choices.Add(value);
        var field = new PopupField<string>(label, choices, choices.IndexOf(value ?? "")) { name = FieldName(group, label) };
        field.RegisterValueChangedCallback(evt => commit(string.IsNullOrEmpty(evt.newValue) ? null : evt.newValue));
        group.Add(field);
    }

    private List<string> AuthoringBoneChoices()
    {
        var choices = new List<string> { "" };
        if (!_workspace.HasPackage) return choices;

        choices.AddRange((_workspace.Draft.HurtboxBoneDefs ?? Array.Empty<HurtboxBoneSource>())
            .Select(bone => bone.BoneId)
            .Where(id => !string.IsNullOrEmpty(id) && !choices.Contains(id, StringComparer.Ordinal)));
        choices.AddRange((_workspace.Draft.AttachmentBoneIds ?? Array.Empty<string>())
            .Where(id => !string.IsNullOrEmpty(id) && !choices.Contains(id, StringComparer.Ordinal)));
        return choices;
    }

    private void AddDelayedFloat(VisualElement parent, string label, float value, Action<float> commit)
    {
        var field = new FloatField(label) { name = FieldName(parent, label), value = value, isDelayed = true };
        field.RegisterValueChangedCallback(evt => commit(evt.newValue));
        parent.Add(field);
    }

    private void AddDelayedInteger(VisualElement parent, string label, int value, Action<int> commit)
    {
        var field = new IntegerField(label) { name = FieldName(parent, label), value = value, isDelayed = true };
        field.RegisterValueChangedCallback(evt => commit(evt.newValue));
        parent.Add(field);
    }

    private void AddToggle(VisualElement parent, string label, bool value, Action<bool> commit)
    {
        var field = new Toggle(label) { name = FieldName(parent, label), value = value };
        field.RegisterValueChangedCallback(evt => commit(evt.newValue));
        parent.Add(field);
    }

    private static string FieldName(VisualElement parent, string label) =>
        $"{(string.IsNullOrEmpty(parent.name) ? (parent as Foldout)?.text ?? "fields" : parent.name)}/{label}";

    private void ReleaseLabBeforeReload()
    {
        CheckpointWorkspace();
        _waitingForAssemblyReload = true;
        _reloadPackageId = _workspace.PackageId;
        _refreshAfterCompilation = true;
        DestroyOwnedLab();
    }

    private void ReleaseLabBeforeCompilation(object context) => ReleaseLabBeforeReload();

    private void DrawPreviewHandles()
    {
        if (_activePage != "moves-page" || _lab == null || !_lab.IsPackagePreview ||
            !_workspace.HasPackage || _preview == null || !_preview.IsAvailable || _lab.Playing)
            return;
        if (_grabSelected)
        {
            if (!_workspace.LiveDraftInvalid) DrawGrabPreview();
            return;
        }


        var hitboxes = _lab.ResolveHitboxes();
        if (hitboxes.Count == 0)
            _sceneRadiusEditing = false;
        DrawPresentationHandles();

        AbilityLabOperationProjection? selectedOperation = null;
        foreach (var hitbox in hitboxes)
        {
            var operation = FindSourceHitboxOperation(hitbox.index);
            float size = HandleUtility.GetHandleSize(hitbox.start) * 0.12f;
            if (Handles.Button(hitbox.start, Quaternion.identity, size, size, Handles.SphereHandleCap) && operation != null)
            {
                SelectOperation(operation);
                SceneView.RepaintAll();
            }
            if (hitbox.index == _lab.SelectedHitboxEventIndex && operation != null)
                selectedOperation = operation;
        }

        if (selectedOperation == null || _lab.SelectedHitboxEventIndex < 0) return;
        var selectedHitbox = hitboxes.FirstOrDefault(item => item.index == _lab.SelectedHitboxEventIndex);
        if (selectedHitbox.evt.DurationTicks == 0) return;
        float radius = _sceneRadiusEditing ? _sceneRadiusPending : selectedHitbox.evt.Radius;
        EditorGUI.BeginChangeCheck();
        float changedRadius = Handles.RadiusHandle(Quaternion.identity, selectedHitbox.start, radius);
        if (EditorGUI.EndChangeCheck())
        {
            _sceneRadiusPending = Mathf.Max(0.0001f, changedRadius);
            _sceneRadiusStageIndex = selectedOperation.SourceStageIndex;
            _sceneRadiusOperationIndex = selectedOperation.SourceOperationIndex;
            _sceneRadiusEditing = true;
        }
    }

    private void DrawGrabPreview()
    {
        var renderer = _lab?.Renderer;
        if (renderer == null) return;
        var capture = _workspace.Draft.CaptureGeometry;
        Vector3 center = renderer.transform.position - Vector3.up * renderer.ModelYOffset;
        Matrix4x4 previousMatrix = Handles.matrix;
        Color previousColor = Handles.color;
        Handles.matrix = Matrix4x4.TRS(center,
            Quaternion.Euler(0f, _lab!.FacingYaw * Mathf.Rad2Deg, 0f), Vector3.one);
        Handles.color = new Color(1f, 0.25f, 0.05f, 0.95f);
        Handles.DrawWireCube(new Vector3(0f, capture.OffsetY, capture.Reach * 0.5f),
            new Vector3(capture.Width, capture.Height, capture.Reach));
        Handles.color = Color.cyan;
        Handles.SphereHandleCap(0,
            new Vector3(capture.AttackerAnchor.X, capture.AttackerAnchor.Y, capture.AttackerAnchor.Z),
            Quaternion.identity, 0.08f, EventType.Repaint);
        Handles.color = Color.magenta;
        Handles.SphereHandleCap(0,
            new Vector3(capture.VictimAnchor.X, capture.VictimAnchor.Y, capture.VictimAnchor.Z),
            Quaternion.identity, 0.08f, EventType.Repaint);
        Handles.matrix = previousMatrix;
        Handles.color = previousColor;
    }

    private void DrawPresentationHandles()
    {
        if (_selectedOperation?.Source is not EmitPresentationOperationSource presentation ||
            _lab?.Renderer == null || _selectedOperation.SourceStageIndex != _lab.StageIndex)
            return;
        if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
        {
            _scenePresentationEditing = false;
            _scenePresentationStageIndex = -1;
            _scenePresentationOperationIndex = -1;
            Event.current.Use();
            SceneView.RepaintAll();
            return;
        }

        PresentationPlacement placement = _scenePresentationEditing
            ? _scenePresentationPending
            : presentation.Placement;
        Transform root = _lab.Renderer.transform;
        Vector3 eventPosition = root.position;
        Quaternion eventRotation = Quaternion.Euler(0f, _lab.FacingYaw * Mathf.Rad2Deg, 0f);
        if (!PresentationPlacementResolver.TryResolveWorldTransform(
                placement, _lab.Renderer, root, eventPosition, eventRotation,
                out Vector3 position, out Quaternion rotation, out Vector3 scale))
            return;

        Transform? parent = placement.AttachmentMode == AuthoringPresentationAttachmentMode.Bone
            ? _lab.Renderer.ResolvePresentationBone(placement.BoneId)
            : null;
        EditorGUI.BeginChangeCheck();
        Vector3 moved = Handles.PositionHandle(position, rotation);
        if (EditorGUI.EndChangeCheck())
        {
            Vector3 local = parent != null
                ? parent.InverseTransformPoint(moved)
                : Quaternion.Inverse(eventRotation) * (moved - eventPosition);
            placement = placement with
            {
                LocalPositionX = local.x,
                LocalPositionY = local.y,
                LocalPositionZ = local.z,
            };
            BeginPresentationSceneEdit(placement);
        }

        EditorGUI.BeginChangeCheck();
        Quaternion turned = Handles.RotationHandle(rotation, position);
        if (EditorGUI.EndChangeCheck())
        {
            Quaternion local = parent != null
                ? Quaternion.Inverse(parent.rotation) * turned
                : Quaternion.Inverse(eventRotation) * turned;
            Vector3 euler = local.eulerAngles;
            placement = placement with
            {
                LocalRotationX = euler.x,
                LocalRotationY = euler.y,
                LocalRotationZ = euler.z,
            };
            BeginPresentationSceneEdit(placement);
        }

        EditorGUI.BeginChangeCheck();
        Vector3 resized = Handles.ScaleHandle(scale, position, rotation);
        if (EditorGUI.EndChangeCheck())
        {
            Vector3 local = parent == null ? resized : new Vector3(
                resized.x / Mathf.Max(0.0001f, parent.lossyScale.x),
                resized.y / Mathf.Max(0.0001f, parent.lossyScale.y),
                resized.z / Mathf.Max(0.0001f, parent.lossyScale.z));
            placement = placement with
            {
                LocalScaleX = Mathf.Max(0.0001f, local.x),
                LocalScaleY = Mathf.Max(0.0001f, local.y),
                LocalScaleZ = Mathf.Max(0.0001f, local.z),
            };
            BeginPresentationSceneEdit(placement);
        }

    }
    private void BeginPresentationSceneEdit(PresentationPlacement placement)
    {
        if (!_scenePresentationEditing)
        {
            if (_selectedOperation?.Source is not EmitPresentationOperationSource ||
                _selectedOperation.SourceStageIndex < 0 || _selectedOperation.SourceOperationIndex < 0)
                return;
            _scenePresentationStageIndex = _selectedOperation.SourceStageIndex;
            _scenePresentationOperationIndex = _selectedOperation.SourceOperationIndex;
        }
        _scenePresentationPending = placement;
        _scenePresentationEditing = true;
    }

    private void CommitScenePresentation()
    {
        if (!_scenePresentationEditing || _lab == null || !_workspace.HasPackage ||
            _scenePresentationStageIndex < 0 || _scenePresentationOperationIndex < 0)
        {
            _scenePresentationEditing = false;
            return;
        }
        int stageIndex = _scenePresentationStageIndex;
        int operationIndex = _scenePresentationOperationIndex;
        _scenePresentationEditing = false;
        _scenePresentationStageIndex = -1;
        _scenePresentationOperationIndex = -1;
        bool accepted = _workspace.ReplacePresentationPlacement(
            _lab.SelectedSlotId, stageIndex, operationIndex, _scenePresentationPending);
        if (accepted)
        {
            UpdateTimelineControls();
            _selectedOperation = FindProjectedOperation(stageIndex, operationIndex);
            RefreshInspector();
            SceneView.RepaintAll();
        }
    }
    private AbilityLabOperationProjection? FindSourceHitboxOperation(int hitboxIndex)
    {

        if (_lab == null || _timelineProjection == null || _lab.StageIndex < 0 || _lab.StageIndex >= _timelineProjection.Stages.Count)
            return null;
        int index = 0;
        foreach (var operation in _timelineProjection.Stages[_lab.StageIndex].Operations)
            if (operation.Source is SpawnHitboxOperationSource)
            {
                if (index++ == hitboxIndex) return operation;
            }
        return null;
    }

    private void CommitSceneRadius()
    {
        if (_lab == null || !_workspace.HasPackage || _sceneRadiusStageIndex < 0 || _sceneRadiusOperationIndex < 0)
        {
            _sceneRadiusEditing = false;
            return;
        }
        if (!_workspace.TryResolveCanonicalSlot(_lab.SelectedSlotId, out _, out var slot) ||
            _sceneRadiusStageIndex >= slot.Timeline.Stages.Count)
        {
            _sceneRadiusEditing = false;
            return;
        }
        var operations = slot.Timeline.Stages[_sceneRadiusStageIndex].Operations;
        if (_sceneRadiusOperationIndex >= operations.Count || operations[_sceneRadiusOperationIndex] is not SpawnHitboxOperationSource hitbox)
        {
            _sceneRadiusEditing = false;
            return;
        }
        int stageIndex = _sceneRadiusStageIndex, operationIndex = _sceneRadiusOperationIndex;
        _sceneRadiusEditing = false;
        _sceneRadiusStageIndex = -1;
        _sceneRadiusOperationIndex = -1;
        bool accepted = _workspace.ReplaceHitbox(_lab.SelectedSlotId, stageIndex, operationIndex,
            hitbox.Hitbox with { Radius = _sceneRadiusPending });
        if (accepted) SceneView.RepaintAll();
    }


    private static CharacterDiagnostic Diagnostic(string code, string path, string message)
        => new(CharacterDiagnosticSeverity.Error, code, path, message);
}
