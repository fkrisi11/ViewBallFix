#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using VRC.SDK3.Avatars.Components;
using System.Collections;

// Runtime component that survives domain reload
public class CalibrationRunner : MonoBehaviour
{
    private VRCAvatarDescriptor avatarDescriptor;
    private Vector3 originalEyePosition;
    private float startTime;
    private bool hasStarted = false;
    private bool calibrationComplete = false;

    // Event for when calibration completes
    public static System.Action<Vector3, Vector3> OnCalibrationComplete;

    public void Initialize(VRCAvatarDescriptor avatar)
    {
        avatarDescriptor = avatar;
        originalEyePosition = avatar.ViewPosition;
    }

    void Start()
    {
        if (avatarDescriptor == null)
        {
            // Try to find avatar descriptor
            avatarDescriptor = FindObjectOfType<VRCAvatarDescriptor>();
            if (avatarDescriptor == null)
            {
                Debug.LogError("Avatar descriptor not found during domain reload!");
                Destroy(this);
                return;
            }
            originalEyePosition = avatarDescriptor.ViewPosition;
        }

        // Don't start timer yet - wait for play mode to fully load
        StartCoroutine(WaitForPlayModeLoad());
    }

    IEnumerator WaitForPlayModeLoad()
    {
        // Wait a bit for play mode to fully initialize
        yield return new WaitForSeconds(0.5f);

        // Additional check to ensure everything is ready
        yield return new WaitForEndOfFrame();

        // Now start the actual calibration timer
        startTime = Time.time;
        hasStarted = true;
    }

    void Update()
    {
        if (!hasStarted || calibrationComplete) return;

        float elapsed = Time.time - startTime;

        // Wait 2 seconds
        if (elapsed >= 2f)
        {
            calibrationComplete = true;

            // Get the world position of the sphere (this is our calibrated view position)
            Vector3 worldPosition = transform.position;
            Vector3 localPosition = avatarDescriptor.transform.InverseTransformPoint(worldPosition);

            // Exit play mode and schedule results display
            StartCoroutine(ExitAndShowResults(originalEyePosition, localPosition));
        }
    }

    IEnumerator ExitAndShowResults(Vector3 originalPos, Vector3 newPos)
    {
        // Exit play mode
        EditorApplication.isPlaying = false;

        // Schedule results to be shown after exiting play mode
        EditorApplication.delayCall += () => {
            EditorApplication.delayCall += () => {
                OnCalibrationComplete?.Invoke(originalPos, newPos);
            };
        };

        yield return null;
    }
}
#endif