using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.AsyncOperations;
using TMPro;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Initialises Addressables and optionally updates the remote catalog.
/// Do NOT release the InitializeAsync handle — that breaks later loads.
/// </summary>
public class AddressablesInitializer : MonoBehaviour
{
    [Header("UI Feedback (optional)")]
    public TMP_Text statusText;

    [Header("Settings")]
    public bool continueOnError = true;
    [Tooltip("Skip remote catalog check if init alone is enough for your test.")]
    public bool checkForCatalogUpdates = true;

    [Header("Diagnostics")]
    public string remoteLoadPathTemplate = "https://digitaljalebiarworkv.web.app/AssetBundles/[BuildTarget]";

    public static bool IsReady { get; private set; }
    public static string LastResolvedRemoteBaseUrl { get; private set; }
    public static string LastError { get; private set; }

    void Start()
    {
        IsReady = false;
        LastError = null;
        StartCoroutine(InitAndUpdateCatalog());
    }

    IEnumerator InitAndUpdateCatalog()
    {
        SetStatus("Initialising...");
        string buildTarget = GetBuildTargetName();
        LastResolvedRemoteBaseUrl = remoteLoadPathTemplate.Replace("[BuildTarget]", buildTarget);

        Debug.Log("[ADDR] ========== Addressables startup ==========");
        Debug.Log($"[ADDR] Build target: {buildTarget}");
        Debug.Log($"[ADDR] Remote URL: {LastResolvedRemoteBaseUrl}/");
        Debug.Log($"[ADDR] Catalog: {LastResolvedRemoteBaseUrl}/catalog_1.0.0.json");

        // Step 1 — NEVER call Addressables.Release on this handle (causes invalid handle crash).
        AsyncOperationHandle<IResourceLocator> initHandle = Addressables.InitializeAsync();
        yield return initHandle;

        if (!IsHandleSucceeded(initHandle, out string initError))
        {
            LastError = initError;
            Debug.LogError($"[ADDR] Initialisation FAILED: {initError}");
            if (continueOnError)
            {
                SetStatus("Init failed — retry placement.");
                IsReady = true;
            }
            yield break;
        }

        Debug.Log("[ADDR] Initialised OK.");
        LogLoadedCatalogs();
        IsReady = true;
        SetStatus("Ready.");

        if (!checkForCatalogUpdates)
            yield break;

        yield return null;

        // Step 2 — optional remote catalog update (auto-releases its own handle).
        SetStatus("Checking updates...");
        Debug.Log("[ADDR] Checking remote catalog...");

        AsyncOperationHandle<List<string>> checkHandle = Addressables.CheckForCatalogUpdates(false);
        yield return checkHandle;

        if (!IsHandleSucceeded(checkHandle, out string checkError))
        {
            Debug.LogWarning($"[ADDR] Catalog check skipped: {checkError}");
            SafeRelease(checkHandle);
            yield break;
        }

        List<string> catalogsToUpdate = checkHandle.Result;
        SafeRelease(checkHandle);

        if (catalogsToUpdate == null || catalogsToUpdate.Count == 0)
        {
            Debug.Log("[ADDR] Catalog up to date.");
            yield break;
        }

        Debug.Log($"[ADDR] Updating {catalogsToUpdate.Count} catalog(s)...");
        SetStatus("Updating catalog...");

        AsyncOperationHandle<List<IResourceLocator>> updateHandle =
            Addressables.UpdateCatalogs(catalogsToUpdate, false);
        yield return updateHandle;

        if (IsHandleSucceeded(updateHandle, out string updateError))
            Debug.Log("[ADDR] Catalog updated.");
        else
            Debug.LogWarning($"[ADDR] Catalog update failed: {updateError}");

        SafeRelease(updateHandle);
        LogLoadedCatalogs();
    }

    static bool IsHandleSucceeded<T>(AsyncOperationHandle<T> handle, out string error)
    {
        error = null;
        if (!handle.IsValid())
        {
            error = "AsyncOperationHandle is invalid (often caused by releasing InitializeAsync).";
            return false;
        }

        if (handle.Status == AsyncOperationStatus.Succeeded)
            return true;

        error = handle.OperationException != null
            ? handle.OperationException.ToString()
            : $"Status={handle.Status}";
        return false;
    }

    static void SafeRelease<T>(AsyncOperationHandle<T> handle)
    {
        if (handle.IsValid())
            Addressables.Release(handle);
    }

    void SetStatus(string msg)
    {
        if (statusText != null)
            statusText.text = msg;
    }

    static string GetBuildTargetName()
    {
#if UNITY_EDITOR
        return EditorUserBuildSettings.activeBuildTarget.ToString();
#else
        return Application.platform switch
        {
            RuntimePlatform.Android => "Android",
            RuntimePlatform.IPhonePlayer => "iOS",
            RuntimePlatform.WindowsPlayer or RuntimePlatform.WindowsEditor => "StandaloneWindows64",
            RuntimePlatform.OSXPlayer or RuntimePlatform.OSXEditor => "StandaloneOSX",
            _ => Application.platform.ToString()
        };
#endif
    }

    static void LogLoadedCatalogs()
    {
        var sb = new StringBuilder();
        sb.AppendLine("[ADDR] Loaded catalog locators:");
        int count = 0;
        foreach (IResourceLocator locator in Addressables.ResourceLocators)
        {
            count++;
            int keyCount = 0;
            foreach (object _ in locator.Keys)
                keyCount++;
            sb.AppendLine($"  - {locator.LocatorId} (keys: {keyCount})");
        }
        if (count == 0)
            sb.AppendLine("  (none)");
        Debug.Log(sb.ToString());
    }
}
