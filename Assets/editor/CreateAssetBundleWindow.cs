using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public class CreateAssetBundleWindow : EditorWindow
{
    private Vector2 _scroll;
    private List<GameObject> _sceneObjects;
    private List<string> _customScripts;

    [MenuItem("Bundler/Create Asset Bundle")]
    public static void ShowWindow() =>
        GetWindow<CreateAssetBundleWindow>("Create Asset Bundle");

    private void OnEnable() =>
        RefreshSceneContents();

    private void RefreshSceneContents()
    {
        _sceneObjects = new List<GameObject>();
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (root.hideFlags != HideFlags.None) continue;
            if (root.CompareTag("EditorOnly")) continue;
            AddRecursive(root);
        }

        _customScripts = new List<string>();
        foreach (var go in _sceneObjects)
            foreach (var mb in go.GetComponents<MonoBehaviour>())
                if (mb != null && mb.GetType().Assembly.GetName().Name == "Assembly-CSharp")
                    if (!_customScripts.Contains(mb.GetType().FullName))
                        _customScripts.Add(mb.GetType().FullName);
    }

    private void AddRecursive(GameObject go)
    {
        _sceneObjects.Add(go);
        foreach (Transform child in go.transform)
            AddRecursive(child.gameObject);
    }

    private void OnGUI()
    {
        if (_sceneObjects == null) RefreshSceneContents();

        _sceneObjects.RemoveAll(go => go == null);

        EditorGUILayout.LabelField("Scene Contents:", EditorStyles.boldLabel);
        _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.Height(200));
        foreach (var go in _sceneObjects)
            if (go != null)
                EditorGUILayout.LabelField("• " + go.name);
        EditorGUILayout.EndScrollView();

        if (_customScripts.Count > 0)
            EditorGUILayout.HelpBox(
                "Warning: custom scripts Viewer won’t have:\n" +
                string.Join("\n", _customScripts),
                MessageType.Warning
            );

        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Create Asset Bundle (Prefab)", GUILayout.Height(30)))
            PickAndBuildBundle();
    }

    private void PickAndBuildBundle()
    {
        var scene = SceneManager.GetActiveScene();
        string defaultName = scene.isLoaded ? scene.name : "Bundle";

        string bundlePath = EditorUtility.SaveFilePanel(
            "Save Asset Bundle",
            "",
            defaultName,
            "unity3d"
        );
        if (string.IsNullOrEmpty(bundlePath)) return;

        BuildPrefabBundle(bundlePath);
        RefreshSceneContents();
    }

    private void BuildPrefabBundle(string outputPath)
    {
        string bundleName = Path.GetFileNameWithoutExtension(outputPath);
        string outDir = Path.GetDirectoryName(outputPath);

        // 1) Prepare temp folder
        const string tempFolder = "Assets/TempBundles";
        if (!AssetDatabase.IsValidFolder(tempFolder))
            AssetDatabase.CreateFolder("Assets", "TempBundles");

        // 2) Parent all true roots under a container
        var scene = SceneManager.GetActiveScene();
        var roots = scene.GetRootGameObjects();
        var container = new GameObject(bundleName);
        foreach (var go in roots)
        {
            if (go.hideFlags != HideFlags.None) continue;
            if (go.CompareTag("EditorOnly")) continue;
            go.transform.SetParent(container.transform, true);
        }

        // 3) Save prefab
        string prefabPath = AssetDatabase.GenerateUniqueAssetPath(
            Path.Combine(tempFolder, bundleName + ".prefab")
        );
        PrefabUtility.SaveAsPrefabAsset(container, prefabPath);

        // 4) Restore scene — safe reparenting
        var children = new List<Transform>();
        for (int i = 0; i < container.transform.childCount; i++)
            children.Add(container.transform.GetChild(i));
        foreach (var child in children)
            child.SetParent(null, true);
        DestroyImmediate(container);

        // 5) Mark prefab for bundling
        var importer = AssetImporter.GetAtPath(prefabPath);
        importer.SetAssetBundleNameAndVariant(bundleName, "");

        // 6) Build that one bundle
        var buildMap = new AssetBundleBuild[1];
        buildMap[0].assetBundleName = bundleName;
        buildMap[0].assetNames = new[] { prefabPath };

        BuildPipeline.BuildAssetBundles(
            outDir,
            buildMap,
            BuildAssetBundleOptions.None,
            EditorUserBuildSettings.activeBuildTarget
        );

        // 7) Copy chosen .unity3d over
        string intermediate = Path.Combine(outDir, bundleName);
        if (File.Exists(intermediate))
            FileUtil.CopyFileOrDirectory(intermediate, outputPath);

        // 8) Clean up all intermediate bundles & manifests
        FileUtil.DeleteFileOrDirectory(intermediate);
        FileUtil.DeleteFileOrDirectory(intermediate + ".manifest");
        string dirName = new DirectoryInfo(outDir).Name;
        FileUtil.DeleteFileOrDirectory(Path.Combine(outDir, dirName));
        FileUtil.DeleteFileOrDirectory(Path.Combine(outDir, dirName + ".manifest"));

        // 9) Tear down
        importer.SetAssetBundleNameAndVariant("", "");
        AssetDatabase.RemoveUnusedAssetBundleNames();
        AssetDatabase.DeleteAsset(prefabPath);
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog(
            "Done",
            $"Asset Bundle (prefab) saved to:\n{outputPath}",
            "OK"
        );
    }
}
