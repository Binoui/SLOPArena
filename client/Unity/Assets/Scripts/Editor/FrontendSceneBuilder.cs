using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using SlopArena.Client.UI;

/// <summary>
/// One-shot builder for the single frontend scene (ADR-0032; issues #210,
/// #212, #219, #221). Creates Assets/Scenes/Frontend.unity with the stable
/// FrontendShell document and the seven page hosts. Every page is a fragment
/// source: the shell clones the page's fragment into its hosts on every
/// activation through a per-activation <see cref="FrontendPageContext"/>.
/// Run through the Unity CLI Pipeline, e.g.
/// `unity command --project-path client/Unity eval 'FrontendSceneBuilder.Build();'`.
/// </summary>
public static class FrontendSceneBuilder
{
    private const string ScenePath = "Assets/Scenes/Frontend.unity";
    private const string ShellUxmlPath = "Assets/UI/FrontendShell.uxml";
    private const string PanelSettingsGuid = "375219e0f9be73791af68905fab77544";

    private static readonly (string Name, string Uxml, System.Type Controller)[] Pages =
    {
        ("HomePage", "Assets/UI/MainMenu.uxml", typeof(MainMenuController)),
        ("FighterSelectPage", "Assets/UI/CharSelect.uxml", typeof(CharSelectController)),
        ("StageSelectPage", "Assets/UI/StageSelect.uxml", typeof(StageSelectController)),
        ("ResultsPage", "Assets/UI/Results.uxml", typeof(ResultsUI)),
        ("ServerBrowserPage", "Assets/UI/ServerBrowser.uxml", typeof(ServerBrowserUI)),
        ("LobbyRoomPage", "Assets/UI/LobbyRoom.uxml", typeof(LobbyRoomUI)),
        ("ArcadePage", "Assets/UI/Arcade.uxml", typeof(ArcadeSelectController)),
    };

    private static readonly string[] RemovedScenes =
    {
        "Assets/Scenes/MainMenu.unity",
        "Assets/Scenes/CharSelect.unity",
        "Assets/Scenes/StageSelect.unity",
        "Assets/Scenes/Results.unity",
        "Assets/Scenes/ServerBrowser.unity",
        "Assets/Scenes/LobbyRoom.unity",
    };

    public static void Build()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        var shellObject = new GameObject("Frontend");
        // The stable shell document first, so its visual tree exists when the
        // controller binds and activates the pending page in Start (issue #219).
        var shellDocument = shellObject.AddComponent<UIDocument>();
        var panelPath = AssetDatabase.GUIDToAssetPath(PanelSettingsGuid);
        shellDocument.panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(panelPath);
        shellDocument.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(ShellUxmlPath);

        var shellView = shellObject.AddComponent<FrontendShellView>();
        var shellViewSerialized = new SerializedObject(shellView);
        shellViewSerialized.FindProperty("_uiDocument").objectReferenceValue = shellDocument;
        shellViewSerialized.ApplyModifiedPropertiesWithoutUndo();

        // The builder serializes these shell owners explicitly.
        shellObject.AddComponent<FrontendShellIdentityView>();
        shellObject.AddComponent<FrontendFocusRouter>();

        var shell = shellObject.AddComponent<FrontendController>();

        // Every page is a fragment source since Pass 3 (issue #221): each page
        // GameObject carries only its controller; the shell clones the
        // fragment into its hosts on every activation and injects the
        // per-activation page context.
        var pageObjects = new GameObject[Pages.Length];
        for (int i = 0; i < Pages.Length; i++)
        {
            var (name, _, controllerType) = Pages[i];
            var page = new GameObject(name);
            page.transform.SetParent(shellObject.transform, false);
            page.AddComponent(controllerType);
            page.SetActive(false);
            pageObjects[i] = page;
        }

        var shellSerialized = new SerializedObject(shell);
        shellSerialized.FindProperty("_homePage").objectReferenceValue = pageObjects[0];
        shellSerialized.FindProperty("_fighterSelectPage").objectReferenceValue = pageObjects[1];
        shellSerialized.FindProperty("_stageSelectPage").objectReferenceValue = pageObjects[2];
        shellSerialized.FindProperty("_resultsPage").objectReferenceValue = pageObjects[3];
        shellSerialized.FindProperty("_serverBrowserPage").objectReferenceValue = pageObjects[4];
        shellSerialized.FindProperty("_lobbyRoomPage").objectReferenceValue = pageObjects[5];
        shellSerialized.FindProperty("_arcadePage").objectReferenceValue = pageObjects[6];
        shellSerialized.FindProperty("_shell").objectReferenceValue = shellView;
        for (int i = 0; i < Pages.Length; i++)
        {
            string fragmentField = i switch
            {
                0 => "_homeFragment",
                1 => "_fighterSelectFragment",
                2 => "_stageSelectFragment",
                3 => "_resultsFragment",
                4 => "_serverBrowserFragment",
                5 => "_lobbyRoomFragment",
                6 => "_arcadeFragment",
                _ => throw new System.InvalidOperationException($"Unexpected page index {i}.")
            };
            shellSerialized.FindProperty(fragmentField).objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(Pages[i].Uxml);
        }
        shellSerialized.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.SaveScene(scene, ScenePath);

        var buildScenes = EditorBuildSettings.scenes
            .Where(s => System.Array.IndexOf(RemovedScenes, s.path) < 0 && s.path != ScenePath)
            .ToList();
        buildScenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = buildScenes.ToArray();

        foreach (var removed in RemovedScenes)
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(removed) != null)
                AssetDatabase.DeleteAsset(removed);
        }
        AssetDatabase.SaveAssets();

        Debug.Log($"[FrontendSceneBuilder] Built {ScenePath} with the stable shell document, identity surface and focus router (#220), "
            + $"and {Pages.Length} fragment-mounted pages (#221).");
    }
}
