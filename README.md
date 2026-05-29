# Digital Jalebi — AR Wall Video (Remote Addressables)

Unity AR app that detects **vertical walls**, places a transparent video quad, and streams **WebM videos** from **Firebase Hosting** via **Unity Addressables**. Users switch videos with **Next** / **Previous** UI buttons.

| Item | Value |
|------|--------|
| Unity | **2022.3.62f3** (LTS) |
| Main scene | `Assets/Scenes/SampleScene 1.unity` |
| Remote host | `https://digitaljalebiarworkv.web.app/AssetBundles/[BuildTarget]` |
| Android bundles path | `.../AssetBundles/Android/` |

---

## What each part does

| Component | Purpose |
|-----------|---------|
| **AR Session** | Starts and manages the AR tracking session on device. |
| **XR Origin (Mobile AR)** | Camera rig + AR Foundation managers (planes, raycasts, anchors). |
| **ARPlaneManager** (Vertical) | Detects wall planes only — not floor/ceiling. |
| **AR Feathered Plane** prefab | Visual feedback while scanning (from XR Interaction Toolkit samples). |
| **ARVerticalPlaneFilter** | Hides horizontal / invalid planes so users scan real walls. |
| **ARRaycastManager** | Screen-tap raycasts against detected planes. |
| **ARAnchorManager** | Locks the placed video to the wall in world space. |
| **ARManager** | Central object: placement, Addressables init, video switching. |
| **ARWallPlacement** | Preview quad, tap-to-place, validation, spawns `WallVideo` prefab. |
| **AddressablesInitializer** | Initializes Addressables and checks for remote catalog updates. |
| **RemoteVideoSwitcher** | Next/Previous — loads another Addressable video on the active wall. |
| **RemoteVideoCatalog** | Ordered list of videos (display names + AssetReferences). |
| **WallVideo prefab** | Quad + `VideoPlayerController` + transparent shader + optional pinch/rotate. |
| **Addressables (RemoteVideos)** | Packages videos into bundles; app downloads them at runtime from Firebase. |
| **Firebase Hosting** | Serves catalog + `.bundle` files over HTTPS (no videos in APK). |

---

## Setup steps (sequential)

Follow in order. Each step builds on the previous one.

### 1. Create the Unity project (AR template)

**Action:** Unity Hub → **New project** → template **Mobile AR** (or **AR**).

**Why:** Installs AR Foundation, XR Management, Input System, URP, and sample assets so you do not add packages manually.

**This repo:** Already configured; open the folder in Unity **2022.3.62f3**.

---

### 2. Add AR Session

**Action:** Hierarchy → right-click → **XR** → **AR Session** (or add `AR Session` component to an empty GameObject).

**Why:** Without it, AR tracking does not run on device.

**This repo:** Present in `SampleScene 1.unity`.

---

### 3. Configure XR Origin (Mobile AR)

**Action:** Use **XR Origin (Mobile AR)** from the template or samples. On the same GameObject, ensure these components exist:

| Component | Setting | Why |
|-----------|---------|-----|
| **AR Plane Manager** | **Detection Mode → Vertical** | Only wall planes are detected. |
| **AR Plane Manager** | **Plane Prefab → AR Feathered Plane** | Path: `Assets/Samples/XR Interaction Toolkit/3.1.2/AR Starter Assets/Prefabs/AR Feathered Plane.prefab` |
| **AR Raycast Manager** | (default) | Used for tap placement raycasts. |
| **AR Anchor Manager** | (default) | Anchors the video to the wall after placement. |
| **ARVerticalPlaneFilter** | Add script `ARVerticalPlaneFilter` | Disables non-vertical plane meshes during scanning. |

**Why:** Vertical detection + feathered planes give clear “scan the wall” feedback; raycast/anchor managers are required for stable placement.

**This repo:** `XR Origin (Mobile AR)` in scene with feathered plane prefab and `ARVerticalPlaneFilter` attached.

---

### 4. Create ARManager

**Action:** Hierarchy → Create Empty → rename **ARManager**. Add three scripts:

| Script | Role |
|--------|------|
| `ARWallPlacement` | Tap wall → spawn video prefab |
| `AddressablesInitializer` | Init Addressables before any video load |
| `RemoteVideoSwitcher` | Next/Previous video on placed wall |

**Inspector wiring (ARWallPlacement):**

| Field | Assign to |
|-------|-----------|
| Raycast Manager | XR Origin → AR Raycast Manager |
| Anchor Manager | XR Origin → AR Anchor Manager |
| Plane Manager | XR Origin → AR Plane Manager |
| Wall Prefab | `Assets/Prefabs/WallVideo.prefab` |
| Video Switcher | ARManager → RemoteVideoSwitcher |
| Instruction Text | UI Text (TMP) on Canvas |

**Inspector wiring (RemoteVideoSwitcher):**

| Field | Assign to |
|-------|-----------|
| Catalog | `Assets/Data/RemoteVideoCatalog.asset` |
| Wall Placement | ARManager → ARWallPlacement |

**Why:** One manager object keeps scene wiring obvious for grading and debugging.

---

### 5. UI Canvas (instructions + Next / Previous)

**Action:**

1. **UI → Canvas** (Screen Space – Overlay).
2. Add **TextMeshPro** text for instructions (e.g. “Move phone slowly along a plain wall, then tap once to place video”).
3. Add two **Buttons**: **Next** and **Previous**.

**Button wiring (Inspector → Button → On Click):**

| Button | Object | Method |
|--------|--------|--------|
| Next | ARManager | `RemoteVideoSwitcher.PlayNext` |
| Previous | ARManager | `RemoteVideoSwitcher.PlayPrevious` |

**Why:** Switching is done through public methods; no extra button fields on the script.

**Note:** Wait until Addressables reports ready (see log `[ADDR] Initialised OK`) before placing the wall. After placement, wait ~1–2 s for prepare; use cooldown (3 s) before re-tapping to reposition.

---

### 6. Install Addressables package

**Action:** **Window → Package Manager** → **Unity Registry** → install **Addressables** (this project: `1.22.3`).

**Why:** Required for remote video bundles and catalog.

**First-time Addressables setup:**

1. **Window → Asset Management → Addressables → Groups**
2. If prompted, **Create Addressables Settings**

---

### 7. Addressables — videos and RemoteVideos group

See [Addressables configuration](#addressables-configuration) below for full detail.

**Short version:**

1. Put `.webm` files in `Assets/Videos/`.
2. Create group **RemoteVideos** (remote build/load paths).
3. Mark each video **Addressable** in that group.
4. Build Addressables → output in `ServerData/Android/` (or iOS).
5. Deploy to Firebase (see [Firebase Hosting](#firebase-hosting)).

---

### 8. Create WallVideo prefab

**Action:** Create prefab `Assets/Prefabs/WallVideo.prefab`:

| Part | Details |
|------|---------|
| Mesh | Unity **Quad** |
| Material | Uses shader `Custom/TransparentVideo` (`Assets/Shaders/TransparentVideoShader.shader`) |
| **VideoPlayer** | Render Mode = **Render Texture** (configured in code) |
| **VideoPlayerController** | `addressableVideo` → first catalog entry (Earthquake GUID) |
| **ARWallInteraction** (optional) | Pinch to scale, twist to rotate |
| Transform | Rotation **(0, 0, 0)** — do not bake 180° Y on the prefab |

**Why:** `VideoPlayerController` creates a RenderTexture, loads the Addressable clip, scales the quad to video aspect ratio, and plays with alpha.

**At runtime:** `ARWallPlacement` also adds `ARWallLockRotation` so the video stays upright even if the AR plane normal is slightly tilted.

---

### 9. Remote video catalog asset

**Action:** **Assets → Create → Remote Video Catalog** (or use existing `Assets/Data/RemoteVideoCatalog.asset`).

Add one entry per Addressable video (display name + same `AssetReference` as in the Addressables group).

**Why:** `RemoteVideoSwitcher` cycles this list in order for Next/Previous.

---

### 10. Build and run on device

1. **File → Build Settings** → **Android** (or iOS) → enable **ARCore** / **ARKit** as needed.
2. Build Addressables for the **same** platform (see below).
3. Deploy bundles to Firebase.
4. Build & Run APK on a physical phone (AR does not work fully in Editor alone).

**Why:** Remote `LoadPath` uses `[BuildTarget]` — Android app must load `.../AssetBundles/Android/`, not Standalone paths.

---

## Addressables configuration

### Profile (Default)

**Window → Asset Management → Addressables → Profiles → Default**

| Variable | Value | Use |
|----------|--------|-----|
| **Remote.BuildPath** | `ServerData/[BuildTarget]` | Where Unity writes bundles on your PC after build |
| **Remote.LoadPath** | `https://digitaljalebiarworkv.web.app/AssetBundles/[BuildTarget]` | URL the **built app** uses to download bundles |

`[BuildTarget]` becomes `Android`, `iOS`, etc., automatically per platform build.

### Addressables settings

**Window → Asset Management → Addressables → Settings**

| Setting | Value | Use |
|---------|--------|-----|
| **Build Remote Catalog** | ✓ On | Publishes `catalog_*.json` for the app to find bundle locations |
| **Remote Catalog Build Path** | `Remote.BuildPath` | Catalog file written next to bundles |
| **Remote Catalog Load Path** | `Remote.LoadPath` | App fetches catalog from Firebase |

### RemoteVideos group

**Groups → RemoteVideos → Inspector (schemas)**

| Setting | Value |
|---------|--------|
| Build Path | `Remote.BuildPath` |
| Load Path | `Remote.LoadPath` |
| Bundle Mode | Pack Together (or per your choice) |

**Entries in this project:**

| Address | File |
|---------|------|
| `Assets/Videos/Tab 2 3-2 Earthquake.webm` | Earthquake (default on wall) |
| `Assets/Videos/Video2.webm` | Second video |

To add a video: import `.webm` → select asset → **Addressable** ✓ → Group **RemoteVideos** → add row to `RemoteVideoCatalog.asset` → rebuild & redeploy.

### Build Addressables (every time videos or group settings change)

1. Switch build target: **File → Build Settings → Android** (or iOS).
2. **Window → Asset Management → Addressables → Build → New Build → Default Build Script**
3. Confirm output folder exists: `ServerData/Android/` (contains `catalog_*.json`, `*.bundle`, etc.)

**Why:** The player does not use videos from `Assets/Videos/` at runtime on device — it downloads the built bundles from the remote URL.

---

## Firebase Hosting

Firebase serves the Addressables **catalog** and **asset bundles** over HTTPS. The Unity app stays small; videos update without reinstalling the APK (after catalog/bundle redeploy).

### One-time Firebase setup

1. Install [Node.js](https://nodejs.org/).
2. Install CLI: `npm install -g firebase-tools`
3. Login: `firebase login`
4. In project root, create/configure Firebase project (if not done):
   ```bash
   firebase init hosting
   ```
   - Public directory: **`public`**
   - Single-page app: **No** (not required for static bundles)
   - Link to project: **digitaljalebiarworkv** (or your project ID)

**Example `firebase.json` (project root):**

```json
{
  "hosting": {
    "public": "public",
    "ignore": ["firebase.json", "**/.*", "**/node_modules/**"]
  }
}
```

**Folder layout after deploy:**

```
public/
  AssetBundles/
    Android/
      catalog_1.0.0.json
      catalog_1.0.0.hash
      *.bundle
```

The app loads:  
`https://digitaljalebiarworkv.web.app/AssetBundles/Android/catalog_1.0.0.json`

### Deploy bundles (after each Addressables build)

**Option A — script (recommended):**

```powershell
.\deploy-addressables.ps1
```

This copies `ServerData/Android/*` → `public/AssetBundles/Android/` and runs `firebase deploy --only hosting`.

**Option B — manual:**

1. Copy `ServerData/Android/*` to `public/AssetBundles/Android/`
2. Run: `firebase deploy --only hosting`

### Verify hosting

Open in a browser (should return JSON, not 404):

```
https://digitaljalebiarworkv.web.app/AssetBundles/Android/catalog_1.0.0.json
```

**Why:** If this URL fails, the app cannot download videos (logcat will show catalog/bundle errors).

### Change hosting URL

If you use a different Firebase site:

1. Update **Addressables Profile → Remote.LoadPath**
2. Update `AddressablesInitializer.remoteLoadPathTemplate` on **ARManager** (for log diagnostics)
3. Rebuild Addressables and redeploy to the new `public/` path

---

## Scripts reference (`Assets/Scripts/`)

| Script | Responsibility |
|--------|----------------|
| `ARWallPlacement.cs` | Vertical plane raycast, preview, place/replace wall, hide plane meshes |
| `ARVerticalPlaneFilter.cs` | Filter plane visuals; shared wall validation helper |
| `ARWallLockRotation.cs` | Keep video upright relative to anchor |
| `ARWallInteraction.cs` | Optional pinch scale / twist rotate on prefab |
| `VideoPlayerController.cs` | RenderTexture, Addressable load, prepare/play, switch video |
| `AddressablesInitializer.cs` | `Addressables.InitializeAsync`, catalog update, `IsReady` flag |
| `RemoteVideoSwitcher.cs` | Next/Previous, syncs with catalog |
| `RemoteVideoCatalog.cs` | ScriptableObject list of `AssetReference` videos |

---

## Typical runtime flow

```
App start
  → AddressablesInitializer (wait until IsReady)
  → User scans vertical wall
  → Tap once → ARWallPlacement spawns WallVideo
  → VideoPlayerController loads default Addressable (Earthquake)
  → User taps Next/Previous → RemoteVideoSwitcher → SwitchToAddressable
```

---

## Troubleshooting

| Symptom | Check |
|---------|--------|
| Video never starts | Logcat for `[ADDR] Initialised OK`; verify catalog URL in browser |
| 404 on bundle URL | Ran `deploy-addressables.ps1`? Files under `public/AssetBundles/Android/`? |
| Placement on floor/window | Scan plain lit wall; vertical mode + `ARVerticalPlaneFilter` enabled |
| Video cancelled mid-load | Do not re-tap wall during load (3 s cooldown) |
| Wrong platform bundles | Rebuild Addressables with **Android** active, then redeploy |
| Black / no alpha | Material must use `Custom/TransparentVideo`; WebM with alpha channel |

---

## Project structure (key paths)

```
Assets/
  Scenes/SampleScene 1.unity      # Main scene
  Prefabs/WallVideo.prefab
  Videos/*.webm
  Data/RemoteVideoCatalog.asset
  Scripts/                        # AR + video + Addressables logic
  Shaders/TransparentVideoShader.shader
  AddressableAssetsData/          # Groups & profiles (versioned)
ServerData/Android/               # Local Addressables build output (gitignore recommended)
public/AssetBundles/Android/      # Copied bundles for Firebase deploy
deploy-addressables.ps1           # Copy + firebase deploy helper
```

---

## Quick checklist before demo / submission

- [ ] Addressables built for **Android**
- [ ] Bundles deployed to Firebase; catalog URL opens in browser
- [ ] `Remote.LoadPath` matches live Firebase URL
- [ ] `ARManager` references wired (managers, prefab, catalog, switcher)
- [ ] Next/Previous buttons call `PlayNext` / `PlayPrevious`
- [ ] Test on **physical device** with good lighting and a plain wall
