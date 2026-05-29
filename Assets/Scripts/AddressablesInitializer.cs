using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using TMPro;

/// <summary>
/// Attach to a persistent GameObject in the first scene (e.g. AR Session Origin).
/// On startup it:
///   1. Initialises the Addressables system
///   2. Checks the remote host for a newer catalog (.hash file comparison)
///   3. Downloads and applies any updated catalog
/// This satisfies "App should fetch catalog remotely" requirement.
/// </summary>
public class AddressablesInitializer : MonoBehaviour
{
    [Header("UI Feedback (optional)")]
    [Tooltip("Assign a TMP label to show download status to the user.")]
    public TMP_Text statusText;

    [Header("Settings")]
    [Tooltip("If true, the scene continues even when catalog update fails (uses cached catalog).")]
    public bool continueOnError = true;

    // Set to true once catalog is ready; other scripts can poll this.
    public static bool IsReady { get; private set; } = false;

    void Start()
    {
        IsReady = false;
        StartCoroutine(InitAndUpdateCatalog());
    }

    IEnumerator InitAndUpdateCatalog()
    {
        SetStatus("Initialising...");
        Debug.Log("[ADDR] Initialising Addressables...");

        // Step 1 — initialise the Addressables runtime
        var initHandle = Addressables.InitializeAsync();
        yield return initHandle;

        if (initHandle.Status != AsyncOperationStatus.Succeeded)
        {
            Debug.LogError($"[ADDR] Initialisation failed: {initHandle.OperationException}");
            if (!continueOnError) yield break;
        }
        else
        {
            Debug.Log("[ADDR] Initialised OK.");
        }
        // Release the init handle and allow one frame for the ResourceManager to settle.
        Addressables.Release(initHandle);
        yield return null;

        // Step 2 — check remote host for a newer catalog
        SetStatus("Checking for updates...");
        Debug.Log("[ADDR] Checking remote catalog for updates...");

        var checkHandle = Addressables.CheckForCatalogUpdates(autoReleaseHandle: false);
        yield return checkHandle;

        // Guard: if the returned handle is not valid, avoid accessing .Status (which throws).
        if (!checkHandle.IsValid())
        {
            Debug.LogWarning("[ADDR] CheckForCatalogUpdates returned an invalid handle.");
            if (!continueOnError) yield break;

            SetStatus("Using cached catalog.");
            IsReady = true;
            yield break;
        }

        if (checkHandle.Status != AsyncOperationStatus.Succeeded)
        {
            Debug.LogWarning($"[ADDR] Catalog check failed (offline?): {checkHandle.OperationException}");
            if (checkHandle.IsValid()) Addressables.Release(checkHandle);

            if (!continueOnError) yield break;

            SetStatus("Using cached catalog.");
            IsReady = true;
            yield break;
        }

        List<string> catalogsToUpdate = checkHandle.Result;
        if (checkHandle.IsValid()) Addressables.Release(checkHandle);

        if (catalogsToUpdate == null || catalogsToUpdate.Count == 0)
        {
            Debug.Log("[ADDR] Catalog is up to date — no download needed.");
            SetStatus("Content up to date.");
            IsReady = true;
            yield break;
        }

        // Step 3 — download updated catalogs
        Debug.Log($"[ADDR] {catalogsToUpdate.Count} catalog(s) to update. Downloading...");
        SetStatus("Downloading catalog...");

        var updateHandle = Addressables.UpdateCatalogs(catalogsToUpdate, autoReleaseHandle: false);
        yield return updateHandle;

        if (!updateHandle.IsValid())
        {
            Debug.LogError("[ADDR] UpdateCatalogs returned an invalid handle.");
            if (!continueOnError) yield break;

            SetStatus("Using cached catalog.");
            IsReady = true;
            yield break;
        }

        if (updateHandle.Status == AsyncOperationStatus.Succeeded)
        {
            Debug.Log("[ADDR] Catalog updated successfully.");
            SetStatus("Ready.");
        }
        else
        {
            Debug.LogError($"[ADDR] Catalog update failed: {updateHandle.OperationException}");
            SetStatus("Update failed — using cached.");
        }

        if (updateHandle.IsValid()) Addressables.Release(updateHandle);
        IsReady = true;
    }

    void SetStatus(string msg)
    {
        if (statusText != null)
            statusText.text = msg;
    }
}
