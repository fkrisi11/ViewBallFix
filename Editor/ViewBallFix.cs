#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using VRC.SDK3.Avatars.Components;
using System.Collections.Generic;
using System.IO;
using System;
using UnityEngine.SceneManagement;

[Serializable]
public class CalibrationData
{
    public string avatarGuid;
    public string avatarName;
    public Vector3 originalViewPosition;
    public Vector3 calibratedViewPosition;

    public CalibrationData(string guid, string name, Vector3 original, Vector3 calibrated)
    {
        avatarGuid = guid;
        avatarName = name;
        originalViewPosition = original;
        calibratedViewPosition = calibrated;
    }
}

[Serializable]
public class CalibrationDatabase
{
    public List<CalibrationData> calibrations = new List<CalibrationData>();
}

public class ViewBallFix : EditorWindow
{
    private VRCAvatarDescriptor avatarDescriptor;
    private int descriptorInstanceID;
    private GameObject calibrationSphere;
    private static bool isCalibrating
    {
        get => EditorPrefs.GetBool("ViewBallFix_IsCalibrating", false);
        set => EditorPrefs.SetBool("ViewBallFix_IsCalibrating", value);
    }

    // Current calibration results
    private CalibrationData currentCalibration;
    private bool hasCalibrationResults = false;

    // Store avatar info for calibration
    private string currentAvatarGuid;
    private string currentAvatarName;

    // UI state
    private int selectedTab = 0;
    private string[] tabNames = { "Calibration", "Settings" };

    // Settings
    private bool debugMode = false;

    // File path for JSON storage
    private static string DataFilePath => Path.Combine(Application.persistentDataPath, "AvatarCalibrationData.json");

    [MenuItem("TohruTheDragon/View Ball Fix")]
    public static void ShowWindow()
    {
        GetWindow<ViewBallFix>("View Ball Fix");
    }

    void OnEnable()
    {
        SceneView.duringSceneGui += OnSceneGUI;
        CalibrationRunner.OnCalibrationComplete += OnCalibrationComplete;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

        // Try to restore current avatar's calibration data
        LoadCurrentAvatarData();
    }

    void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
        CalibrationRunner.OnCalibrationComplete -= OnCalibrationComplete;
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
    }

    void OnSceneGUI(SceneView sceneView)
    {
        // Check if we should show the overlay by looking for calibration sphere
        bool shouldShowOverlay = EditorApplication.isPlaying && isCalibrating;

        if (shouldShowOverlay)
        {
            Handles.BeginGUI();

            // Create a style for the red text that doesn't change on hover
            GUIStyle style = new GUIStyle();
            style.fontSize = 24;
            style.fontStyle = FontStyle.Bold;
            style.normal.textColor = Color.red;
            style.hover.textColor = Color.red;
            style.active.textColor = Color.red;
            style.focused.textColor = Color.red;
            style.alignment = TextAnchor.MiddleCenter;

            // Calculate position (center-top of scene view)
            Vector2 size = style.CalcSize(new GUIContent("Calibrating view position...\nMake sure Game View is visible!"));
            Rect rect = new Rect(
                (sceneView.position.width - size.x) / 2,
                50,
                size.x,
                size.y
            );

            // Draw the text (using a disabled GUI area to prevent interaction)
            bool originalEnabled = GUI.enabled;
            GUI.enabled = false;
            GUI.Label(rect, "Calibrating view position...\nMake sure Game View is visible!", style);
            GUI.enabled = originalEnabled;

            Handles.EndGUI();

            // Keep repainting to ensure text stays visible
            sceneView.Repaint();
        }
    }

    void OnCalibrationComplete(Vector3 original, Vector3 newPos)
    {
        // Hide the overlay immediately
        isCalibrating = false;
        SceneView.RepaintAll();

        // Use the saved avatar info instead of current avatarDescriptor
        if (!string.IsNullOrEmpty(currentAvatarGuid) && !string.IsNullOrEmpty(currentAvatarName))
        {
            currentCalibration = new CalibrationData(currentAvatarGuid, currentAvatarName, original, newPos);
            hasCalibrationResults = true;

            // Save to JSON
            SaveCalibrationData(currentCalibration);

            Debug.Log("<color=#00FF00>[View Ball Fix]</color> Calibration completed successfully!");
        }
        else
        {
            Debug.LogError("<color=#00FF00>[View Ball Fix]</color> Failed to save calibration data - avatar information was lost!");
        }

        // Ensure cleanup is complete
        CleanupExistingCalibration();

        // Force UI refresh
        Repaint();
    }

    void OnGUI()
    {
        GUILayout.Label("View Ball Fix", EditorStyles.boldLabel);
        GUILayout.Space(5);

        // Tab selection
        selectedTab = GUILayout.Toolbar(selectedTab, tabNames);
        GUILayout.Space(10);

        switch (selectedTab)
        {
            case 0:
                DrawCalibrationTab();
                break;
            case 1:
                DrawSettingsTab();
                break;
        }

        if (isCalibrating && !EditorApplication.isPlaying)
        {
            if (GUILayout.Button("Fix broken calibration"))
            {
                isCalibrating = false;
                CleanupExistingCalibration();
            }
        }

        // Auto-refresh when values change
        if (GUI.changed)
        {
            Repaint();
        }
    }

    void DrawCalibrationTab()
    {
        if (avatarDescriptor == null && descriptorInstanceID != 0)
        {
            avatarDescriptor = EditorUtility.InstanceIDToObject(descriptorInstanceID) as VRCAvatarDescriptor;
        }

        // Avatar descriptor field
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("Avatar Descriptor:", GUILayout.Width(120));
        VRCAvatarDescriptor newDescriptor = (VRCAvatarDescriptor)EditorGUILayout.ObjectField(
            avatarDescriptor,
            typeof(VRCAvatarDescriptor),
            true
        );

        // Load calibration data when descriptor changes
        if (newDescriptor != avatarDescriptor)
        {
            avatarDescriptor = newDescriptor;
            descriptorInstanceID = avatarDescriptor ? avatarDescriptor.GetInstanceID() : 0;
            LoadCurrentAvatarData();
        }

        EditorGUILayout.EndHorizontal();

        GUILayout.Space(10);

        if (avatarDescriptor == null)
        {
            EditorGUILayout.HelpBox("Please assign a VRC_AvatarDescriptor to view position information.", MessageType.Info);
            return;
        }

        // Display current eye position
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("Current Eye Position (Local Space)", EditorStyles.boldLabel);
        Vector3 viewPos = avatarDescriptor.ViewPosition;
        EditorGUILayout.LabelField($"X: {viewPos.x:F3}");
        EditorGUILayout.LabelField($"Y: {viewPos.y:F3}");
        EditorGUILayout.LabelField($"Z: {viewPos.z:F3}");
        EditorGUILayout.EndVertical();

        GUILayout.Space(10);

        // Check if calibration is running
        bool calibrationActive = isCalibrating;

        // Calibration button
        GUI.enabled = !calibrationActive && !EditorApplication.isPlaying;

        if (!hasCalibrationResults && !EditorApplication.isPlaying)
        {
            EditorGUILayout.HelpBox("Before starting the calibration, make sure Gesture Manager or Av3 Emulator are in the scene and enabled.", MessageType.Warning);
        }
        
        if (GUILayout.Button(calibrationActive ? "Calibrating..." : "Begin Calibration", GUILayout.Height(30)))
        {
            BeginCalibration();
        }
        GUI.enabled = true;

        if (calibrationActive)
        {
            EditorGUILayout.HelpBox("Calibration in progress. Please wait...", MessageType.Info);
        }

        // Show calibration results if available
        if (hasCalibrationResults && currentCalibration != null)
        {
            GUILayout.Space(10);
            EditorGUILayout.LabelField("Calibration Results", EditorStyles.boldLabel);

            // Original position
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Original Eye Position", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"X: {currentCalibration.originalViewPosition.x:F3}");
            EditorGUILayout.LabelField($"Y: {currentCalibration.originalViewPosition.y:F3}");
            EditorGUILayout.LabelField($"Z: {currentCalibration.originalViewPosition.z:F3}");
            EditorGUILayout.EndVertical();

            GUILayout.Space(5);

            // New position
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Calibrated Eye Position", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"X: {currentCalibration.calibratedViewPosition.x:F3}");
            EditorGUILayout.LabelField($"Y: {currentCalibration.calibratedViewPosition.y:F3}");
            EditorGUILayout.LabelField($"Z: {currentCalibration.calibratedViewPosition.z:F3}");
            EditorGUILayout.EndVertical();

            GUILayout.Space(5);

            // Difference
            Vector3 difference = currentCalibration.calibratedViewPosition - currentCalibration.originalViewPosition;
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Difference", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"X: {difference.x:F3}");
            EditorGUILayout.LabelField($"Y: {difference.y:F3}");
            EditorGUILayout.LabelField($"Z: {difference.z:F3}");
            EditorGUILayout.EndVertical();

            GUILayout.Space(10);

            // Apply buttons (always set X to 0)
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Apply Original Position"))
            {
                Vector3 pos = currentCalibration.originalViewPosition;
                pos.x = 0; // Always set X to 0
                ApplyViewPosition(pos);
            }
            if (GUILayout.Button("Apply Calibrated Position"))
            {
                Vector3 pos = currentCalibration.calibratedViewPosition;
                pos.x = 0; // Always set X to 0
                ApplyViewPosition(pos);
            }
            EditorGUILayout.EndHorizontal();

            // Debug buttons (include X axis)
            if (debugMode)
            {
                GUILayout.Space(5);
                EditorGUILayout.LabelField("Debug Mode (includes X axis):", EditorStyles.boldLabel);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Apply Original (with X)"))
                {
                    ApplyViewPosition(currentCalibration.originalViewPosition);
                }
                if (GUILayout.Button("Apply Calibrated (with X)"))
                {
                    ApplyViewPosition(currentCalibration.calibratedViewPosition);
                }
                EditorGUILayout.EndHorizontal();
            }

            GUILayout.Space(5);

            if (GUILayout.Button("Clear Current Results"))
            {
                ClearCurrentResults();
            }
        }
    }

    void DrawSettingsTab()
    {
        EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);
        GUILayout.Space(10);

        // Debug mode checkbox
        debugMode = EditorGUILayout.Toggle("Debug Mode", debugMode);
        EditorGUILayout.HelpBox("Debug mode shows additional buttons that apply view positions including the X axis. Normal buttons always set X to 0.", MessageType.Info);

        GUILayout.Space(20);

        // Data management
        EditorGUILayout.LabelField("Data Management", EditorStyles.boldLabel);
        GUILayout.Space(5);

        CalibrationDatabase db = LoadDatabase();
        EditorGUILayout.LabelField($"Stored calibrations: {db.calibrations.Count}");

        GUILayout.Space(10);

        if (GUILayout.Button("Clear All Calibration Data", GUILayout.Height(30)))
        {
            if (EditorUtility.DisplayDialog("Clear All Data",
                "Are you sure you want to clear all calibration data for all avatars? This action cannot be undone.",
                "Yes, Clear All", "Cancel"))
            {
                ClearAllData();
            }
        }

        GUILayout.Space(10);

        if (GUILayout.Button("Open Data Folder"))
        {
            EditorUtility.RevealInFinder(DataFilePath);
        }

        GUILayout.Space(5);
        EditorGUILayout.LabelField($"Data stored at: {DataFilePath}", EditorStyles.miniLabel);
    }

    void ApplyViewPosition(Vector3 position)
    {
        if (avatarDescriptor == null) return;

        Undo.RecordObject(avatarDescriptor, "Apply View Position");
        avatarDescriptor.ViewPosition = position;
        EditorUtility.SetDirty(avatarDescriptor);

        Repaint();
    }

    void BeginCalibration()
    {
        if (avatarDescriptor == null)
        {
            Debug.LogError("<color=#00FF00>[View Ball Fix]</color> No avatar descriptor assigned!");
            return;
        }

        Animator animator = avatarDescriptor.GetComponent<Animator>();
        if (animator == null)
        {
            Debug.LogError("<color=#00FF00>[View Ball Fix]</color> Avatar must have an Animator component!");
            return;
        }

        if (!animator.isHuman)
        {
            Debug.LogError("<color=#00FF00>[View Ball Fix]</color> Avatar must have a humanoid Animator component!");
            return;
        }

        Transform headBone = animator.GetBoneTransform(HumanBodyBones.Head);
        if (headBone == null)
        {
            Debug.LogError("<color=#00FF00>[View Ball Fix]</color> Could not find head bone in avatar!");
            return;
        }

        // Reset results
        hasCalibrationResults = false;
        currentCalibration = null;

        // Save avatar info before entering play mode (it will be lost during domain reload)
        currentAvatarGuid = GetAvatarGuid(avatarDescriptor);
        currentAvatarName = avatarDescriptor.name;

        Debug.Log("<color=#00FF00>[View Ball Fix]</color> Calibration started");

        // Clean up any existing calibration objects
        CleanupExistingCalibration();

        isCalibrating = true;

        // Create calibration sphere directly on the head bone
        calibrationSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        calibrationSphere.name = "CalibrationSphere";
        calibrationSphere.transform.localScale = Vector3.one * 0.1f;

        // Position sphere at eye position under head bone
        calibrationSphere.transform.SetParent(headBone);
        calibrationSphere.transform.localPosition = headBone.InverseTransformPoint(
            avatarDescriptor.transform.TransformPoint(avatarDescriptor.ViewPosition)
        );

        // Add calibration runner component
        CalibrationRunner runner = calibrationSphere.AddComponent<CalibrationRunner>();
        runner.Initialize(avatarDescriptor);

        // Enter play mode
        EditorApplication.isPlaying = true;
    }

    static void CleanupExistingCalibration()
    {
        // Single pass through all loaded scenes, including inactive objects
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (scene.isLoaded)
            {
                GameObject[] rootObjects = scene.GetRootGameObjects();
                foreach (GameObject rootObj in rootObjects)
                {
                    // Single recursive search through entire hierarchy (active + inactive)
                    FindAndDestroyCalibrationObjects(rootObj.transform);
                }
            }
        }
    }

    static void FindAndDestroyCalibrationObjects(Transform current)
    {
        // Check current object
        if (current.name == "CalibrationSphere")
        {
            DestroyImmediate(current.gameObject);
            return; // Don't process children since we're destroying the parent
        }

        // Check for CalibrationRunner component (more specific than name)
        CalibrationRunner runner = current.GetComponent<CalibrationRunner>();
        if (runner != null)
        {
            DestroyImmediate(current.gameObject);
            return;
        }

        // Process all children (includes inactive ones)
        for (int i = current.childCount - 1; i >= 0; i--)
        {
            FindAndDestroyCalibrationObjects(current.GetChild(i));
        }
    }

    static void OnPlayModeStateChanged(PlayModeStateChange state)
    { 
        if (state == PlayModeStateChange.ExitingPlayMode || state ==  PlayModeStateChange.EnteredEditMode)
        {
            isCalibrating = false;
            CleanupExistingCalibration();
        }
    }

    string GetAvatarGuid(VRCAvatarDescriptor avatar)
    {
        // Try to get the prefab GUID first
        string prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(avatar.gameObject);
        if (!string.IsNullOrEmpty(prefabPath))
        {
            return AssetDatabase.AssetPathToGUID(prefabPath);
        }

        // Fallback to instance ID if not a prefab
        return avatar.GetInstanceID().ToString();
    }

    void SaveCalibrationData(CalibrationData data)
    {
        CalibrationDatabase db = LoadDatabase();

        // Remove existing data for this avatar
        db.calibrations.RemoveAll(c => c.avatarGuid == data.avatarGuid && c.avatarName == data.avatarName);

        // Add new data
        db.calibrations.Add(data);

        // Ensure directory exists
        string directory = Path.GetDirectoryName(DataFilePath);
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Save to file
        try
        {
            string json = JsonUtility.ToJson(db, true);
            File.WriteAllText(DataFilePath, json);
        }
        catch (Exception e)
        {
            Debug.LogError($"<color=#00FF00>[View Ball Fix]</color> Failed to save calibration data: {e.Message}");
        }
    }

    CalibrationDatabase LoadDatabase()
    {
        if (File.Exists(DataFilePath))
        {
            try
            {
                string json = File.ReadAllText(DataFilePath);
                return JsonUtility.FromJson<CalibrationDatabase>(json);
            }
            catch (Exception e)
            {
                Debug.LogError($"<color=#00FF00>[View Ball Fix]</color> Failed to load calibration database: {e.Message}");
            }
        }

        return new CalibrationDatabase();
    }

    void LoadCurrentAvatarData()
    {
        if (avatarDescriptor == null)
        {
            hasCalibrationResults = false;
            currentCalibration = null;
            return;
        }

        string avatarGuid = GetAvatarGuid(avatarDescriptor);
        CalibrationDatabase db = LoadDatabase();

        currentCalibration = db.calibrations.Find(c => c.avatarGuid == avatarGuid && c.avatarName == avatarDescriptor.gameObject.name);
        hasCalibrationResults = currentCalibration != null;
    }

    void ClearCurrentResults()
    {
        if (currentCalibration == null) return;

        CalibrationDatabase db = LoadDatabase();
        db.calibrations.RemoveAll(c => c.avatarGuid == currentCalibration.avatarGuid && c.avatarName == avatarDescriptor.gameObject.name);

        try
        {
            string json = JsonUtility.ToJson(db, true);
            File.WriteAllText(DataFilePath, json);
        }
        catch (Exception e)
        {
            Debug.LogError($"<color=#00FF00>[View Ball Fix]</color> Failed to clear calibration data: {e.Message}");
        }

        hasCalibrationResults = false;
        currentCalibration = null;
        Repaint();
    }

    void ClearAllData()
    {
        try
        {
            if (File.Exists(DataFilePath))
            {
                File.Delete(DataFilePath);
            }

            hasCalibrationResults = false;
            currentCalibration = null;
            Repaint();
        }
        catch (Exception e)
        {
            Debug.LogError($"<color=#00FF00>[View Ball Fix]</color> Failed to clear all data: {e.Message}");
        }
    }

    void OnInspectorUpdate()
    {
        // Repaint the window to update values in real-time
        Repaint();

        // Additional check to ensure avatar descriptor field stays valid
        if (avatarDescriptor != null && !EditorApplication.isPlaying)
        {
            // Validate the object reference is still good
            if (avatarDescriptor.gameObject == null)
            {
                avatarDescriptor = null;
            }
        }
    }
}
#endif