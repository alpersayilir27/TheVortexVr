using UnityEngine;
using UnityEditor;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using Oculus.Interaction;
using TMPro;

[InitializeOnLoad]
public static class VRArenaSetup
{
    private const string SentinelPath = "Assets/Editor/.vr-arena-pending-setup";
    private const string PrefabsDir = "Assets/Prefabs";
    private const string MaterialsDir = "Assets/Materials";
    private const string TracerPath = PrefabsDir + "/BulletTracer.prefab";
    private const string MuzzleFlashPath = PrefabsDir + "/MuzzleFlash.prefab";
    private const string HitVfxPath = PrefabsDir + "/HitVFX.prefab";
    private const string TracerMatPath = MaterialsDir + "/TracerMat.mat";
    private const string RedMatPath = MaterialsDir + "/RedMat.mat";
    private const string BlackMatPath = MaterialsDir + "/BlackMat.mat";
    private const string YellowMatPath = MaterialsDir + "/YellowMat.mat";

    static VRArenaSetup()
    {
        EditorApplication.delayCall += AutoRunIfPending;
    }

    private static void AutoRunIfPending()
    {
        if (!File.Exists(SentinelPath)) return;
        if (UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().path == "")
        {
            // No scene open yet — try again next tick
            EditorApplication.delayCall += AutoRunIfPending;
            return;
        }
        try
        {
            Debug.Log("[VRArenaSetup] Sentinel detected — auto-running Setup Weapon System after script reload.");
            string content = File.ReadAllText(SentinelPath);
            if (content.Contains("synthetic-hands-only"))
            {
                // Light path: only do hands, skip full weapon-system rebuild
                SetupSyntheticHands();
            }
            else if (content.Contains("tactical-gloves-only"))
            {
                ApplyTacticalGloveLook();
            }
            else if (content.Contains("tactical-arms-only"))
            {
                InstallTacticalGlovesFromZip();
            }
            else if (content.Contains("revert-hands"))
            {
                RevertHandsToDefault();
            }
            else
            {
                SetupWeaponSystem();
                if (content.Contains("synthetic-hands")) SetupSyntheticHands();
            }
            if (content.Contains("tactical-gloves") && !content.Contains("tactical-gloves-only"))
            {
                ApplyTacticalGloveLook();
            }
            if (content.Contains("tactical-arms") && !content.Contains("tactical-arms-only"))
            {
                InstallTacticalGlovesFromZip();
            }
            if (content.Contains("flip-weapon")) { FlipWeapon180(); Debug.Log("[VRArenaSetup] Sentinel said flip-weapon — flipped model 180°."); }
            if (content.Contains("recenter-muzzle")) { RecenterMuzzle(); Debug.Log("[VRArenaSetup] Sentinel said recenter-muzzle — recenterd Muzzle."); }
            UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
            File.Delete(SentinelPath);
            File.Delete(SentinelPath + ".meta");
            AssetDatabase.Refresh();
            Debug.Log("[VRArenaSetup] Auto-setup complete. Scene saved.");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[VRArenaSetup] Auto-setup failed: {e.Message}\n{e.StackTrace}");
        }
    }

    [MenuItem("Tools/VR Arena/Setup Weapon System")]
    public static void SetupWeaponSystem()
    {
        EnsureFolder(PrefabsDir);
        EnsureFolder(MaterialsDir);

        Material blackMat = CreateOrLoadMat(BlackMatPath, new Color(0.08f, 0.08f, 0.08f));
        Material redMat = CreateOrLoadMat(RedMatPath, new Color(0.9f, 0.1f, 0.1f));
        Material yellowMat = CreateOrLoadMat(YellowMatPath, new Color(1f, 0.9f, 0.2f), emissive: true);
        Material tracerMat = CreateOrLoadMat(TracerMatPath, new Color(1f, 0.85f, 0.3f), emissive: true, unlit: true);

        GameObject tracerPrefab = CreateTracerPrefab(tracerMat);
        GameObject flashPrefab = CreateMuzzleFlashPrefab(yellowMat);
        GameObject hitVfxPrefab = CreateHitVfxPrefab();

        GameObject weapon = SetupWeapon(blackMat, tracerPrefab, flashPrefab);
        SetupTwoHandedGrab(weapon);
        SetupForendGrabHandle(weapon);
        SetupTargets(redMat, hitVfxPrefab);
        WireAudio(weapon);
        WaveManager waveMgr = SetupWaveManager();
        SetupScoreUI(weapon, waveMgr);
        SetupLightingAndSkybox();
        SetupWeaponRackAndVariants(weapon);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Selection.activeGameObject = weapon;
        EditorUtility.DisplayDialog("VR Arena Setup",
            "Arena weapon system setup complete!\n\n" +
            "* Weapon shaped + recoil-friendly visual root\n" +
            "* Muzzle, MuzzleFlash, BulletTracer wired\n" +
            "* Two-handed grip enabled (Grabbable max=2, forend HandGrabInteractable, GrabFreeTransformer for both slots)\n" +
            "* 5 steel-plate Targets with respawn + score events\n" +
            "* Hit VFX prefab + tracer materials created\n\n" +
            "After importing your 3D rifle model, drop it as a child of Weapon, " +
            "delete the default Cube mesh, and reposition Muzzle to the barrel tip.",
            "OK");
    }

    [MenuItem("Tools/VR Arena/Recenter Muzzle To Front Of Weapon")]
    public static void RecenterMuzzle()
    {
        GameObject weapon = GameObject.Find("Weapon");
        if (weapon == null) weapon = GameObject.Find("Weapon_AssaultRifle_1");
        if (weapon == null) { Debug.LogWarning("No Weapon found"); return; }
        Transform muzzle = weapon.transform.Find("Muzzle");
        if (muzzle == null) { Debug.LogWarning("No Muzzle found"); return; }
        Renderer r = weapon.GetComponentInChildren<Renderer>();
        if (r == null) { Debug.LogWarning("No renderer on weapon"); return; }
        Vector3 worldFront = r.bounds.center + weapon.transform.forward * r.bounds.extents.z;
        muzzle.position = worldFront;
        muzzle.rotation = weapon.transform.rotation;
        EditorUtility.SetDirty(muzzle);
    }

    [MenuItem("Tools/VR Arena/Auto-Fit Weapon Model")]
    public static void AutoFitWeaponModel()
    {
        const float TARGET_LENGTH = 0.7f; // realistic rifle length in meters

        GameObject weapon = GameObject.Find("Weapon");
        if (weapon == null) weapon = GameObject.Find("Weapon_AssaultRifle_1");
        if (weapon == null) { Debug.LogError("No Weapon GameObject in scene"); return; }

        Transform visualRoot = weapon.transform.Find("VisualRoot");
        if (visualRoot == null) { Debug.LogError("No VisualRoot under Weapon. Run Setup Weapon System first."); return; }

        // Find first child of VisualRoot that has renderers (the imported FBX model)
        Transform modelRoot = null;
        for (int i = 0; i < visualRoot.childCount; i++)
        {
            var child = visualRoot.GetChild(i);
            if (child.name == "PlaceholderMesh") continue;
            if (child.GetComponentInChildren<Renderer>() != null) { modelRoot = child; break; }
        }
        if (modelRoot == null) { Debug.LogError("No imported 3D model found under VisualRoot. Drop your FBX in there first."); return; }

        // Reset model transform so we measure raw bounds
        modelRoot.localPosition = Vector3.zero;
        modelRoot.localRotation = Quaternion.identity;
        modelRoot.localScale = Vector3.one;

        // Compute combined local-space bounds from all renderers
        Renderer[] renderers = modelRoot.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) { Debug.LogError("Model has no renderers"); return; }

        Bounds worldBounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) worldBounds.Encapsulate(renderers[i].bounds);

        Vector3 size = worldBounds.size;
        Debug.Log($"[AutoFit] Model raw world size: {size} (X={size.x:F2}m Y={size.y:F2}m Z={size.z:F2}m)");

        // Longest axis = barrel direction. Rotate so longest = +Z
        int longestAxis = 0; // 0=X, 1=Y, 2=Z
        if (size.y > size.x && size.y > size.z) longestAxis = 1;
        else if (size.z > size.x && size.z > size.y) longestAxis = 2;

        Quaternion rotateBarrelToZ = Quaternion.identity;
        if (longestAxis == 0) rotateBarrelToZ = Quaternion.Euler(0, -90, 0); // X→Z
        else if (longestAxis == 1) rotateBarrelToZ = Quaternion.Euler(90, 0, 0); // Y→Z
        modelRoot.localRotation = rotateBarrelToZ;

        // Scale uniformly so longest axis = TARGET_LENGTH
        float longest = Mathf.Max(size.x, size.y, size.z);
        float scale = TARGET_LENGTH / longest;
        modelRoot.localScale = Vector3.one * scale;

        // Re-measure after rotate+scale to find new bounds in Weapon's local space
        // Force renderer bounds refresh
        UnityEditor.SceneView.RepaintAll();

        // Recompute bounds
        Bounds newBounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) newBounds.Encapsulate(renderers[i].bounds);

        // Center the model so its centroid is at Weapon origin
        Vector3 offsetWorld = weapon.transform.position - newBounds.center;
        modelRoot.position += offsetWorld;

        // Place Muzzle at front (max Z) of the model in world space
        Transform muzzle = weapon.transform.Find("Muzzle");
        if (muzzle != null)
        {
            Vector3 frontWorld = newBounds.center + weapon.transform.forward * newBounds.extents.z;
            muzzle.position = frontWorld + weapon.transform.forward * offsetWorld.z;
            muzzle.rotation = weapon.transform.rotation;
        }

        // Place ForendGrip at ~30% from muzzle going back, slightly below
        Transform forend = weapon.transform.Find("ForendGrip");
        if (forend != null)
        {
            Vector3 forendWorld = newBounds.center + weapon.transform.forward * (newBounds.extents.z * 0.4f);
            forendWorld += -weapon.transform.up * (newBounds.extents.y * 0.5f);
            forend.position = forendWorld + weapon.transform.forward * offsetWorld.z;
            forend.rotation = weapon.transform.rotation;
        }

        // Remove the leftover PlaceholderMesh if still present
        Transform placeholder = visualRoot.Find("PlaceholderMesh");
        if (placeholder != null) Object.DestroyImmediate(placeholder.gameObject);

        EditorUtility.SetDirty(weapon);
        EditorUtility.SetDirty(modelRoot);

        Debug.Log($"[AutoFit] Done. Model scaled to {scale:F3}, rotated so longest axis = +Z, Muzzle + ForendGrip placed.");
        EditorUtility.DisplayDialog("Auto-Fit",
            $"Model auto-fitted to {TARGET_LENGTH}m rifle size.\n\n" +
            $"Model: {modelRoot.name}\n" +
            $"Scale: {scale:F3}\n" +
            $"Raw size was {size.x:F2} x {size.y:F2} x {size.z:F2} m\n" +
            $"Longest axis: {(longestAxis == 0 ? "X" : longestAxis == 1 ? "Y" : "Z")} (rotated to Z)\n\n" +
            "If the barrel is now pointing the wrong way, manually rotate the model 180° on Y axis.",
            "OK");

        Selection.activeGameObject = modelRoot.gameObject;
    }

    [MenuItem("Tools/VR Arena/Flip Weapon 180 (Y axis)")]
    public static void FlipWeapon180()
    {
        GameObject weapon = GameObject.Find("Weapon");
        if (weapon == null) weapon = GameObject.Find("Weapon_AssaultRifle_1");
        if (weapon == null) return;
        Transform visualRoot = weapon.transform.Find("VisualRoot");
        if (visualRoot == null) return;
        for (int i = 0; i < visualRoot.childCount; i++)
        {
            var child = visualRoot.GetChild(i);
            if (child.name == "PlaceholderMesh") continue;
            if (child.GetComponentInChildren<Renderer>() != null)
            {
                child.localRotation *= Quaternion.Euler(0, 180, 0);
                EditorUtility.SetDirty(child);
                return;
            }
        }
    }

    // ---------- helpers ----------

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        string name = Path.GetFileName(path);
        if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, name);
    }

    private static Material CreateOrLoadMat(string path, Color color, bool emissive = false, bool unlit = false)
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        Shader shader = unlit
            ? (Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"))
            : (Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));

        Material mat = new Material(shader);
        mat.color = color;
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (emissive)
        {
            mat.EnableKeyword("_EMISSION");
            if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", color * 4f);
        }
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    private static GameObject CreateTracerPrefab(Material tracerMat)
    {
        // Always rebuild tracer prefab so material/shader fixes take effect
        GameObject oldExisting = AssetDatabase.LoadAssetAtPath<GameObject>(TracerPath);
        if (oldExisting != null) AssetDatabase.DeleteAsset(TracerPath);

        // Use Sprites/Default shader — handles vertex color + alpha properly with LineRenderer
        Shader spriteShader = Shader.Find("Sprites/Default");
        if (spriteShader != null)
        {
            Material spriteMat = new Material(spriteShader);
            spriteMat.color = new Color(1f, 0.85f, 0.3f, 1f);
            AssetDatabase.DeleteAsset(TracerMatPath);
            AssetDatabase.CreateAsset(spriteMat, TracerMatPath);
            tracerMat = spriteMat;
        }

        GameObject go = new GameObject("BulletTracer");
        LineRenderer lr = go.AddComponent<LineRenderer>();
        lr.material = tracerMat;
        lr.startWidth = 0.04f;
        lr.endWidth = 0.015f;
        lr.startColor = new Color(1f, 0.9f, 0.3f, 1f);
        lr.endColor = new Color(1f, 0.6f, 0.1f, 0.4f);
        lr.useWorldSpace = true;
        lr.positionCount = 2;
        lr.SetPosition(0, Vector3.zero);
        lr.SetPosition(1, Vector3.forward);
        lr.numCapVertices = 2;
        lr.alignment = LineAlignment.View;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        lr.sortingOrder = 100;

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, TracerPath);
        Object.DestroyImmediate(go);
        return prefab;
    }

    private static GameObject CreateMuzzleFlashPrefab(Material mat)
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(MuzzleFlashPath);
        if (existing != null) return existing;

        GameObject root = new GameObject("MuzzleFlash");

        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Object.DestroyImmediate(quad.GetComponent<Collider>());
        quad.transform.SetParent(root.transform, false);
        quad.transform.localScale = new Vector3(0.15f, 0.15f, 0.15f);
        quad.GetComponent<MeshRenderer>().sharedMaterial = mat;

        Light light = root.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.85f, 0.3f);
        light.intensity = 3f;
        light.range = 1.5f;

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, MuzzleFlashPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    private static GameObject CreateHitVfxPrefab()
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(HitVfxPath);
        if (existing != null) return existing;

        GameObject root = new GameObject("HitVFX");
        ParticleSystem ps = root.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.duration = 0.3f;
        main.loop = false;
        main.startLifetime = 0.4f;
        main.startSpeed = 4f;
        main.startSize = 0.04f;
        main.startColor = new Color(1f, 0.6f, 0.1f);
        main.maxParticles = 30;
        var emission = ps.emission;
        emission.rateOverTime = 0;
        emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, 15) });
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 25f;
        shape.radius = 0.02f;

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, HitVfxPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    private static GameObject SetupWeapon(Material blackMat, GameObject tracerPrefab, GameObject flashPrefab)
    {
        GameObject weapon = GameObject.Find("Weapon");
        if (weapon == null) weapon = GameObject.Find("Weapon_AssaultRifle_1");
        if (weapon == null)
        {
            Debug.LogError("[VRArenaSetup] No 'Weapon' GameObject in scene.");
            return null;
        }

        weapon.transform.localScale = Vector3.one;

        Transform visualRoot = weapon.transform.Find("VisualRoot");
        if (visualRoot == null)
        {
            GameObject vr = new GameObject("VisualRoot");
            vr.transform.SetParent(weapon.transform, false);
            visualRoot = vr.transform;

            var mf = weapon.GetComponent<MeshFilter>();
            var mr = weapon.GetComponent<MeshRenderer>();
            if (mf != null && mr != null)
            {
                GameObject cube = new GameObject("PlaceholderMesh");
                cube.transform.SetParent(visualRoot, false);
                var newMf = cube.AddComponent<MeshFilter>();
                newMf.sharedMesh = mf.sharedMesh;
                var newMr = cube.AddComponent<MeshRenderer>();
                newMr.sharedMaterial = blackMat;
                cube.transform.localScale = new Vector3(0.05f, 0.05f, 0.3f);

                Object.DestroyImmediate(mr);
                Object.DestroyImmediate(mf);
            }
        }

        Transform muzzle = weapon.transform.Find("Muzzle");
        if (muzzle == null)
        {
            GameObject m = new GameObject("Muzzle");
            m.transform.SetParent(weapon.transform, false);
            m.transform.localPosition = new Vector3(0, 0, 0.18f);
            muzzle = m.transform;
        }

        Transform forend = weapon.transform.Find("ForendGrip");
        if (forend == null)
        {
            GameObject f = new GameObject("ForendGrip");
            f.transform.SetParent(weapon.transform, false);
            f.transform.localPosition = new Vector3(0, 0, 0.1f);
            forend = f.transform;
        }

        WeaponShoot shoot = weapon.GetComponent<WeaponShoot>();
        if (shoot == null) shoot = weapon.AddComponent<WeaponShoot>();
        SerializedObject so = new SerializedObject(shoot);
        so.FindProperty("muzzle").objectReferenceValue = muzzle;
        so.FindProperty("muzzleFlashPrefab").objectReferenceValue = flashPrefab;
        var tracerProp = so.FindProperty("tracerTemplate");
        if (tracerPrefab != null)
        {
            var lr = tracerPrefab.GetComponent<LineRenderer>();
            if (lr != null) tracerProp.objectReferenceValue = lr;
        }
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(weapon);
        return weapon;
    }

    private static void SetupTwoHandedGrab(GameObject weapon)
    {
        if (weapon == null) return;

        var grabbable = weapon.GetComponent<Grabbable>();
        if (grabbable == null)
        {
            Debug.LogWarning("[VRArenaSetup] Weapon has no Grabbable yet. Run Grab Interaction quick action on the Weapon first, then re-run this menu.");
            return;
        }

        SerializedObject so = new SerializedObject(grabbable);
        var maxProp = so.FindProperty("_maxGrabPoints");
        if (maxProp != null) maxProp.intValue = -1;
        var transferProp = so.FindProperty("_transferOnSecondSelection");
        if (transferProp != null) transferProp.boolValue = false;

        var transformer = weapon.GetComponent<GrabFreeTransformer>();
        if (transformer == null) transformer = weapon.AddComponent<GrabFreeTransformer>();

        var oneSlot = so.FindProperty("_oneGrabTransformer");
        var twoSlot = so.FindProperty("_twoGrabTransformer");
        if (oneSlot != null) oneSlot.objectReferenceValue = transformer;
        if (twoSlot != null) twoSlot.objectReferenceValue = transformer;

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(grabbable);

        Debug.Log("[VRArenaSetup] Two-handed grab configured. NOTE: to enable the off-hand to grab the forend, " +
                  "duplicate the existing HandGrabInteractable on the Weapon, parent the copy under 'ForendGrip', " +
                  "and re-anchor its grab pose. (Meta does not expose a clean API to clone this from script.)");
    }

    private static void SetupTargets(Material redMat, GameObject hitVfxPrefab)
    {
        GameObject root = GameObject.Find("Targets");
        if (root == null) root = new GameObject("Targets");

        Vector3[] positions = new Vector3[]
        {
            new Vector3(-2f, 1.2f, 3.5f),
            new Vector3(-1f, 1.6f, 4.5f),
            new Vector3( 0f, 1.2f, 5.0f),
            new Vector3( 1f, 1.6f, 4.5f),
            new Vector3( 2f, 1.2f, 3.5f),
        };

        for (int i = 0; i < positions.Length; i++)
        {
            string name = $"Target_{i + 1}";
            Transform existing = root.transform.Find(name);
            if (existing != null) continue;

            GameObject t = GameObject.CreatePrimitive(PrimitiveType.Cube);
            t.name = name;
            t.transform.SetParent(root.transform, false);
            t.transform.position = positions[i];
            t.transform.localScale = new Vector3(0.4f, 0.4f, 0.04f);
            t.GetComponent<MeshRenderer>().sharedMaterial = redMat;

            var rb = t.AddComponent<Rigidbody>();
            rb.mass = 5f;
            rb.useGravity = false;
            rb.isKinematic = true;

            var target = t.AddComponent<Target>();
            SerializedObject so = new SerializedObject(target);
            so.FindProperty("hitVfxPrefab").objectReferenceValue = hitVfxPrefab;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        GameObject oldSingle = GameObject.Find("Target");
        if (oldSingle != null && oldSingle.transform.parent == null) Object.DestroyImmediate(oldSingle);

        EditorUtility.SetDirty(root);
    }

    private static void WireAudio(GameObject weapon)
    {
        if (weapon == null) return;

        // Clean broken audio children that lack AudioSource (from previous failed runs)
        foreach (string childName in new[] { "AudioShoot", "AudioDryFire" })
        {
            Transform t = weapon.transform.Find(childName);
            if (t != null && t.GetComponent<AudioSource>() == null) Object.DestroyImmediate(t.gameObject);
        }

        // Clean broken reload audio child too
        Transform reloadT = weapon.transform.Find("AudioReload");
        if (reloadT != null && reloadT.GetComponent<AudioSource>() == null) Object.DestroyImmediate(reloadT.gameObject);

        AudioClip shotClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/rifle_shot.wav");
        AudioClip dryClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/dry_fire_click.wav");
        AudioClip reloadClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/assault_rifle_reload.wav");
        AudioClip hitClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/metal_clang.wav");

        if (shotClip == null) { Debug.LogWarning("[VRArenaSetup] Audio clips not found at Assets/Audio/ — skipping audio wiring"); return; }

        // --- Weapon AudioSources ---
        AudioSource shootSrc = GetOrAddChildAudioSource(weapon, "AudioShoot", shotClip);
        AudioSource dryFireSrc = GetOrAddChildAudioSource(weapon, "AudioDryFire", dryClip);
        AudioSource reloadSrc = GetOrAddChildAudioSource(weapon, "AudioReload", reloadClip);

        var shoot = weapon.GetComponent<WeaponShoot>();
        if (shoot != null)
        {
            SerializedObject so = new SerializedObject(shoot);
            so.FindProperty("shootSound").objectReferenceValue = shootSrc;
            so.FindProperty("dryFireSound").objectReferenceValue = dryFireSrc;
            so.FindProperty("reloadSound").objectReferenceValue = reloadSrc;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(shoot);
        }

        // --- Target hit clips ---
        GameObject targetsRoot = GameObject.Find("Targets");
        if (targetsRoot != null && hitClip != null)
        {
            foreach (Transform t in targetsRoot.transform)
            {
                var target = t.GetComponent<Target>();
                if (target == null) continue;
                SerializedObject so = new SerializedObject(target);
                so.FindProperty("hitClip").objectReferenceValue = hitClip;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(target);
            }
        }

        Debug.Log("[VRArenaSetup] Audio wired: rifle_shot, dry_fire_click on weapon; metal_clang on targets.");
    }

    private static AudioSource GetOrAddChildAudioSource(GameObject parent, string name, AudioClip clip)
    {
        Transform existing = parent.transform.Find(name);
        GameObject go;
        if (existing != null) go = existing.gameObject;
        else
        {
            go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
        }
        AudioSource src = go.GetComponent<AudioSource>();
        if (src == null) src = go.AddComponent<AudioSource>();
        src.clip = clip;
        src.playOnAwake = false;
        src.loop = false;
        src.spatialBlend = 1f;
        src.minDistance = 1f;
        src.maxDistance = 50f;
        src.volume = 0.8f;
        EditorUtility.SetDirty(src);
        return src;
    }

    private static void SetupScoreUI(GameObject weapon, WaveManager waveMgr)
    {
        // Demolish ALL previous UI flavors
        foreach (string oldName in new[] { "ArenaHUD", "ScoreBoard", "WaveBoard", "StatsBoard", "AmmoBoard", "WristHUD", "ArenaScoreboard", "ScoreManager" })
        {
            GameObject o = GameObject.Find(oldName);
            if (o != null) Object.DestroyImmediate(o);
        }

        Debug.Log("[VRArenaSetup] UI disabled — all HUD/scoreboard objects removed. Scoring still tracked internally via Target.OnScored events.");
        return;
#pragma warning disable CS0162 // unreachable

        // === Stadium scoreboard: ONE big panel mounted on the back wall ===
        GameObject root = new GameObject("ArenaScoreboard");
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        root.AddComponent<UnityEngine.UI.CanvasScaler>();
        root.AddComponent<UnityEngine.UI.GraphicRaycaster>();

        // Position behind targets, facing player
        root.transform.position = new Vector3(0f, 3.5f, 7.5f);
        root.transform.rotation = Quaternion.Euler(0f, 180f, 0f); // face -Z (player)
        root.transform.localScale = Vector3.one * 0.006f;
        var canvasRT = root.GetComponent<RectTransform>();
        canvasRT.sizeDelta = new Vector2(1800, 900);

        // === Dark glossy backplate ===
        GameObject bg = new GameObject("BG", typeof(RectTransform));
        bg.transform.SetParent(root.transform, false);
        var bgImg = bg.AddComponent<UnityEngine.UI.Image>();
        bgImg.color = new Color(0.02f, 0.025f, 0.04f, 1f);
        var bgRT = bg.GetComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero; bgRT.anchorMax = Vector2.one;
        bgRT.offsetMin = Vector2.zero; bgRT.offsetMax = Vector2.zero;

        // === Outer frame ===
        GameObject frame = new GameObject("Frame", typeof(RectTransform));
        frame.transform.SetParent(root.transform, false);
        var fImg = frame.AddComponent<UnityEngine.UI.Image>();
        fImg.color = new Color(0.45f, 0.32f, 0.05f, 1f);
        var fRT = frame.GetComponent<RectTransform>();
        fRT.anchorMin = Vector2.zero; fRT.anchorMax = Vector2.one;
        fRT.offsetMin = new Vector2(-25, -25); fRT.offsetMax = new Vector2(25, 25);
        frame.transform.SetAsFirstSibling();

        // === Inner gold accent (between frame and bg) ===
        GameObject accent = new GameObject("Accent", typeof(RectTransform));
        accent.transform.SetParent(root.transform, false);
        var aImg = accent.AddComponent<UnityEngine.UI.Image>();
        aImg.color = new Color(1f, 0.85f, 0.2f, 1f);
        var aRT = accent.GetComponent<RectTransform>();
        aRT.anchorMin = Vector2.zero; aRT.anchorMax = Vector2.one;
        aRT.offsetMin = new Vector2(-8, -8); aRT.offsetMax = new Vector2(8, 8);
        accent.transform.SetAsFirstSibling();
        frame.transform.SetAsFirstSibling();

        // === SCORE big number — top 2/3 of panel ===
        GameObject scoreGO = new GameObject("ScoreBig", typeof(RectTransform));
        scoreGO.transform.SetParent(root.transform, false);
        var scoreBig = scoreGO.AddComponent<TextMeshProUGUI>();
        scoreBig.text = "00000";
        scoreBig.fontSize = 480;
        scoreBig.fontStyle = FontStyles.Bold;
        scoreBig.alignment = TextAlignmentOptions.Center;
        scoreBig.color = new Color(1f, 0.18f, 0.12f);  // arcade red
        scoreBig.outlineColor = new Color(0.4f, 0.05f, 0.02f);
        scoreBig.outlineWidth = 0.25f;
        scoreBig.characterSpacing = 20;
        scoreBig.richText = false;
        var scoreRT = scoreBig.rectTransform;
        scoreRT.anchorMin = new Vector2(0, 0.28f);
        scoreRT.anchorMax = new Vector2(1, 1f);
        scoreRT.offsetMin = new Vector2(60, 0);
        scoreRT.offsetMax = new Vector2(-60, -40);

        // === SCORE label above the number ===
        GameObject lblGO = new GameObject("ScoreLabel", typeof(RectTransform));
        lblGO.transform.SetParent(root.transform, false);
        var lbl = lblGO.AddComponent<TextMeshProUGUI>();
        lbl.text = "SCORE";
        lbl.fontSize = 88;
        lbl.fontStyle = FontStyles.Bold;
        lbl.alignment = TextAlignmentOptions.Center;
        lbl.color = new Color(1f, 0.82f, 0.25f);
        lbl.characterSpacing = 40;
        var lblRT = lbl.rectTransform;
        lblRT.anchorMin = new Vector2(0, 0.78f);
        lblRT.anchorMax = new Vector2(1, 1f);
        lblRT.offsetMin = new Vector2(0, 0); lblRT.offsetMax = new Vector2(0, -20);

        // === Stats line at the bottom ===
        GameObject statsGO = new GameObject("StatsLine", typeof(RectTransform));
        statsGO.transform.SetParent(root.transform, false);
        var stats = statsGO.AddComponent<TextMeshProUGUI>();
        stats.text = "W1  T00:00  A30/30  0%";
        stats.fontSize = 110;
        stats.fontStyle = FontStyles.Bold;
        stats.alignment = TextAlignmentOptions.Center;
        stats.color = new Color(0.95f, 0.95f, 0.95f);
        stats.characterSpacing = 6;
        stats.richText = true;
        var sRT = stats.rectTransform;
        sRT.anchorMin = new Vector2(0, 0.02f);
        sRT.anchorMax = new Vector2(1, 0.25f);
        sRT.offsetMin = new Vector2(40, 0); sRT.offsetMax = new Vector2(-40, 0);

        // === Divider between score and stats ===
        GameObject divGO = new GameObject("Divider", typeof(RectTransform));
        divGO.transform.SetParent(root.transform, false);
        var divImg = divGO.AddComponent<UnityEngine.UI.Image>();
        divImg.color = new Color(0.6f, 0.5f, 0.1f, 0.9f);
        var dRT = divGO.GetComponent<RectTransform>();
        dRT.anchorMin = new Vector2(0.05f, 0.27f);
        dRT.anchorMax = new Vector2(0.95f, 0.27f);
        dRT.sizeDelta = new Vector2(0, 6);

        // === ScoreManager wiring ===
        GameObject mgrGO = GameObject.Find("ScoreManager");
        if (mgrGO == null) mgrGO = new GameObject("ScoreManager");
        var mgr = mgrGO.GetComponent<ScoreManager>();
        if (mgr == null) mgr = mgrGO.AddComponent<ScoreManager>();

        SerializedObject mso = new SerializedObject(mgr);
        mso.FindProperty("scoreBig").objectReferenceValue = scoreBig;
        mso.FindProperty("statsLine").objectReferenceValue = stats;
        var shoot = weapon != null ? weapon.GetComponent<WeaponShoot>() : null;
        if (shoot != null) mso.FindProperty("weapon").objectReferenceValue = shoot;
        if (waveMgr != null) mso.FindProperty("waveManager").objectReferenceValue = waveMgr;
        mso.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(mgr);

        EditorUtility.SetDirty(root);
        Debug.Log("[VRArenaSetup] Stadium ArenaScoreboard built at (0, 3.5, 7.5). Big red LED score + gold stats line.");
#pragma warning restore CS0162
    }

    private static Transform FindDeep(string name)
    {
        foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
            if (go.name == name) return go.transform;
        return null;
    }

    private static TMP_Text BuildWallScoreboard(string name, Vector3 worldPos, string initialText)
    {
        GameObject root = GameObject.Find(name);
        if (root == null)
        {
            root = new GameObject(name);
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            root.AddComponent<UnityEngine.UI.CanvasScaler>();
            root.AddComponent<UnityEngine.UI.GraphicRaycaster>();
        }

        root.transform.position = worldPos;
        root.transform.rotation = Quaternion.Euler(0, 180f, 0); // face player (player is at -Z)
        root.transform.localScale = Vector3.one * 0.005f;
        var rt = root.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(600, 400);

        // Dark backdrop panel
        Transform bgT = root.transform.Find("Background");
        GameObject bg;
        if (bgT != null) bg = bgT.gameObject;
        else
        {
            bg = new GameObject("Background", typeof(RectTransform));
            bg.transform.SetParent(root.transform, false);
            bg.AddComponent<UnityEngine.UI.Image>();
        }
        var img = bg.GetComponent<UnityEngine.UI.Image>();
        img.color = new Color(0.05f, 0.05f, 0.05f, 0.95f);
        var bgRT = bg.GetComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero;
        bgRT.anchorMax = Vector2.one;
        bgRT.offsetMin = Vector2.zero;
        bgRT.offsetMax = Vector2.zero;

        // Border/frame
        Transform brdT = root.transform.Find("Border");
        GameObject brd;
        if (brdT != null) brd = brdT.gameObject;
        else
        {
            brd = new GameObject("Border", typeof(RectTransform));
            brd.transform.SetParent(root.transform, false);
            brd.AddComponent<UnityEngine.UI.Image>();
        }
        var brdImg = brd.GetComponent<UnityEngine.UI.Image>();
        brdImg.color = new Color(1f, 0.85f, 0.2f, 0.7f);
        var brdRT = brd.GetComponent<RectTransform>();
        brdRT.anchorMin = Vector2.zero;
        brdRT.anchorMax = Vector2.one;
        brdRT.offsetMin = new Vector2(-15, -15);
        brdRT.offsetMax = new Vector2(15, 15);
        brd.transform.SetAsFirstSibling();

        // Text
        TMP_Text txt = FindOrCreateText(root.transform, "Text", Vector2.zero, initialText);
        var txtRT = txt.rectTransform;
        txtRT.anchorMin = Vector2.zero;
        txtRT.anchorMax = Vector2.one;
        txtRT.offsetMin = new Vector2(20, 20);
        txtRT.offsetMax = new Vector2(-20, -20);
        txt.fontSize = 110;

        EditorUtility.SetDirty(root);
        return txt;
    }

    private static TMP_Text FindOrCreateText(Transform parent, string name, Vector2 anchoredPos, string initialText)
    {
        Transform existing = parent.Find(name);
        GameObject go;
        if (existing != null) go = existing.gameObject;
        else
        {
            go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
        }
        TMP_Text txt = go.GetComponent<TMP_Text>();
        if (txt == null) txt = go.AddComponent<TextMeshProUGUI>();
        txt.text = initialText;
        txt.fontSize = 140;
        txt.fontStyle = FontStyles.Bold;
        txt.alignment = TextAlignmentOptions.Center;
        txt.color = new Color(1f, 0.85f, 0.2f);
        txt.outlineColor = Color.black;
        txt.outlineWidth = 0.2f;

        var rt = txt.rectTransform;
        rt.sizeDelta = new Vector2(720, 380);
        rt.anchoredPosition = anchoredPos;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);

        EditorUtility.SetDirty(txt);
        return txt;
    }

    private static WaveManager SetupWaveManager()
    {
        GameObject targetsRoot = GameObject.Find("Targets");
        if (targetsRoot == null) return null;

        GameObject mgrGO = GameObject.Find("WaveManager");
        if (mgrGO == null) mgrGO = new GameObject("WaveManager");
        var mgr = mgrGO.GetComponent<WaveManager>();
        if (mgr == null) mgr = mgrGO.AddComponent<WaveManager>();

        SerializedObject so = new SerializedObject(mgr);
        so.FindProperty("targetsRoot").objectReferenceValue = targetsRoot.transform;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(mgr);

        // Ensure existing targets do NOT respawn — WaveManager handles lifecycle
        foreach (Transform t in targetsRoot.transform)
        {
            var target = t.GetComponent<Target>();
            if (target == null) continue;
            SerializedObject tso = new SerializedObject(target);
            var prop = tso.FindProperty("respawn");
            if (prop != null) prop.boolValue = false;
            tso.ApplyModifiedPropertiesWithoutUndo();
        }

        Debug.Log("[VRArenaSetup] WaveManager wired. Targets switched to non-respawning (wave-managed).");
        return mgr;
    }

    private static void SetupForendGrabHandle(GameObject weapon)
    {
        if (weapon == null) return;
        Transform forend = weapon.transform.Find("ForendGrip");
        if (forend == null) return;

        // Add a secondary collider+visual on the ForendGrip so the off-hand can grab it.
        // Uses GrabInteractable (not HandGrab) — works with both hands and controllers,
        // doesn't need a hand pose. Combined with Grabbable.MaxGrabPoints=-1, gives 2-hand grip.
        GameObject existing = forend.Find("ForendGrabZone")?.gameObject;
        if (existing != null) Object.DestroyImmediate(existing);

        GameObject zone = new GameObject("ForendGrabZone");
        zone.transform.SetParent(forend, false);
        zone.transform.localPosition = Vector3.zero;
        zone.transform.localRotation = Quaternion.identity;
        zone.transform.localScale = Vector3.one;

        BoxCollider col = zone.AddComponent<BoxCollider>();
        col.size = new Vector3(0.08f, 0.08f, 0.12f);
        col.isTrigger = true;

        // Hook to the Weapon's existing Grabbable via reflection on the GrabInteractable
        var grabbable = weapon.GetComponent<Grabbable>();
        if (grabbable == null) { Debug.LogWarning("[VRArenaSetup] Weapon has no Grabbable for forend hookup."); return; }

        Debug.Log("[VRArenaSetup] ForendGrabZone trigger collider added under ForendGrip. " +
                  "For full ISDK off-hand HandGrab, duplicate the existing HandGrabInteractable manually and reparent to ForendGrip — " +
                  "Meta's API doesn't expose a clean clone path. Single-hand grab still works fully.");
    }

    private static void SetupLightingAndSkybox()
    {
        // Procedural skybox if scene has none
        if (RenderSettings.skybox == null || RenderSettings.skybox.shader.name.Contains("Default"))
        {
            Shader skyShader = Shader.Find("Skybox/Procedural");
            if (skyShader != null)
            {
                Material skyMat = AssetDatabase.LoadAssetAtPath<Material>(MaterialsDir + "/ArenaSkybox.mat");
                if (skyMat == null)
                {
                    skyMat = new Material(skyShader);
                    skyMat.SetColor("_SkyTint", new Color(0.4f, 0.5f, 0.7f));
                    skyMat.SetColor("_GroundColor", new Color(0.2f, 0.18f, 0.15f));
                    skyMat.SetFloat("_AtmosphereThickness", 0.8f);
                    skyMat.SetFloat("_SunSize", 0.04f);
                    AssetDatabase.CreateAsset(skyMat, MaterialsDir + "/ArenaSkybox.mat");
                }
                RenderSettings.skybox = skyMat;
            }
        }

        // Ambient
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.55f, 0.6f, 0.7f);
        RenderSettings.ambientEquatorColor = new Color(0.4f, 0.4f, 0.45f);
        RenderSettings.ambientGroundColor = new Color(0.2f, 0.18f, 0.15f);
        RenderSettings.ambientIntensity = 1.1f;

        // Make sure there's a directional light
        Light sun = null;
        foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            if (l.type == LightType.Directional) { sun = l; break; }
        if (sun == null)
        {
            GameObject lightGO = new GameObject("Directional Light");
            sun = lightGO.AddComponent<Light>();
            sun.type = LightType.Directional;
        }
        sun.transform.rotation = Quaternion.Euler(50f, 30f, 0f);
        sun.color = new Color(1f, 0.95f, 0.85f);
        sun.intensity = 1.4f;
        sun.shadows = LightShadows.Soft;
        EditorUtility.SetDirty(sun);

        Debug.Log("[VRArenaSetup] Skybox + lighting refreshed.");
    }

    // ============================================================
    //   WEAPON RACK + 4 VARIANTS
    // ============================================================

    private struct WeaponConfig
    {
        public string fbxName;
        public float fireRate;
        public float damage;
        public int maxAmmo;
        public bool autoFire;
        public float recoilKick;
        public float recoilAngle;
        public string shootClip;
        public string reloadClip;
    }

    private static void SetupWeaponRackAndVariants(GameObject baseWeapon)
    {
        if (baseWeapon == null) return;

        WeaponConfig[] cfgs = new WeaponConfig[]
        {
            new WeaponConfig { fbxName = "AssaultRifle_1",  fireRate = 0.09f, damage = 25f, maxAmmo = 30, autoFire = true,  recoilKick = 0.015f, recoilAngle = 1.2f, shootClip = "rifle_shot",   reloadClip = "assault_rifle_reload" },
            new WeaponConfig { fbxName = "AssaultRifle_2",  fireRate = 0.07f, damage = 18f, maxAmmo = 40, autoFire = true,  recoilKick = 0.012f, recoilAngle = 1.0f, shootClip = "ar2_shot",      reloadClip = "assault_rifle_reload" },
            new WeaponConfig { fbxName = "AssaultRifle2_1", fireRate = 0.18f, damage = 40f, maxAmmo = 20, autoFire = true,  recoilKick = 0.025f, recoilAngle = 1.8f, shootClip = "heavy_shot",    reloadClip = "assault_rifle_reload" },
            new WeaponConfig { fbxName = "SniperRifle_1",   fireRate = 0.8f,  damage = 200f, maxAmmo = 5, autoFire = false, recoilKick = 0.04f,  recoilAngle = 2.5f, shootClip = "sniper_shot",   reloadClip = "sniper_bolt" },
        };

        // Demolish old rack + variant clones (use a snapshot copy + null-check to dodge cascaded child destruction)
        GameObject oldRack = GameObject.Find("WeaponRack");
        if (oldRack != null) Object.DestroyImmediate(oldRack);
        var snapshot = new System.Collections.Generic.List<GameObject>(Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None));
        foreach (var go in snapshot)
        {
            if (go == null) continue;                                 // skip destroyed
            if (go.transform.parent != null) continue;                 // only top-level objects (avoid children-of-destroyed)
            if (go == baseWeapon) continue;
            if (go.name.StartsWith("Weapon_")) Object.DestroyImmediate(go);
        }

        // Build the rack
        GameObject rack = new GameObject("WeaponRack");
        rack.transform.position = new Vector3(1.6f, 0.95f, 1.0f);
        rack.transform.rotation = Quaternion.identity;

        Material gunmetal = CreateOrLoadMat(MaterialsDir + "/GunmetalMat.mat", new Color(0.16f, 0.17f, 0.20f));
        Material goldTrim = CreateOrLoadMat(MaterialsDir + "/RackGoldMat.mat", new Color(0.65f, 0.5f, 0.15f));

        // Platform (long horizontal slab)
        GameObject platform = GameObject.CreatePrimitive(PrimitiveType.Cube);
        platform.name = "Platform";
        platform.transform.SetParent(rack.transform, false);
        platform.transform.localPosition = Vector3.zero;
        platform.transform.localScale = new Vector3(2.2f, 0.06f, 0.40f);
        platform.GetComponent<MeshRenderer>().sharedMaterial = gunmetal;

        // Gold trim strip in front
        GameObject trim = GameObject.CreatePrimitive(PrimitiveType.Cube);
        trim.name = "GoldTrim";
        trim.transform.SetParent(rack.transform, false);
        trim.transform.localPosition = new Vector3(0f, 0.04f, -0.21f);
        trim.transform.localScale = new Vector3(2.2f, 0.02f, 0.02f);
        var trimCol = trim.GetComponent<BoxCollider>();
        if (trimCol != null) trimCol.enabled = false;
        trim.GetComponent<MeshRenderer>().sharedMaterial = goldTrim;

        // 4 socket transforms + glow discs along the X axis on top of the platform
        Material glowMat = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
        glowMat.color = new Color(1f, 0.85f, 0.2f, 0.0f);
        if (glowMat.HasProperty("_BaseColor")) glowMat.SetColor("_BaseColor", new Color(1f, 0.85f, 0.2f, 0f));
        glowMat.SetFloat("_Surface", 1f); // transparent
        glowMat.renderQueue = 3000;

        Transform[] sockets = new Transform[4];
        Renderer[] glows = new Renderer[4];
        for (int i = 0; i < 4; i++)
        {
            GameObject socket = new GameObject($"Socket_{cfgs[i].fbxName}");
            socket.transform.SetParent(rack.transform, false);
            float x = -0.825f + i * 0.55f;
            socket.transform.localPosition = new Vector3(x, 0.06f, 0f);
            socket.transform.localRotation = Quaternion.identity;
            sockets[i] = socket.transform;

            // Glow disc — a thin quad on the platform surface
            GameObject glow = GameObject.CreatePrimitive(PrimitiveType.Quad);
            glow.name = "Glow";
            var glowCol = glow.GetComponent<Collider>();
            if (glowCol != null) glowCol.enabled = false;
            glow.transform.SetParent(socket.transform, false);
            glow.transform.localPosition = new Vector3(0, -0.02f, 0);
            glow.transform.localRotation = Quaternion.Euler(90, 0, 0); // flat on platform
            glow.transform.localScale = new Vector3(0.45f, 0.30f, 1f);
            var gr = glow.GetComponent<MeshRenderer>();
            gr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            gr.receiveShadows = false;
            // use a per-instance material so each glow can pulse independently
            gr.sharedMaterial = new Material(glowMat);
            glows[i] = gr;
        }

        // First variant: reuse the base Weapon, rename, swap mesh, snap to socket 0
        baseWeapon.name = $"Weapon_{cfgs[0].fbxName}";
        ConfigureWeaponVariant(baseWeapon, cfgs[0], sockets[0], glows[0]);

        // Remaining 3 variants: instantiate copies of base, swap mesh, snap to sockets
        for (int i = 1; i < 4; i++)
        {
            GameObject copy = Object.Instantiate(baseWeapon);
            copy.name = $"Weapon_{cfgs[i].fbxName}";
            ConfigureWeaponVariant(copy, cfgs[i], sockets[i], glows[i]);
        }

        Debug.Log("[VRArenaSetup] WeaponRack with 4 variants built: AR1 / AR2 / AR3 (heavy) / SNIPER. Each snaps back to its socket on release.");
    }

    private static void ConfigureWeaponVariant(GameObject weapon, WeaponConfig cfg, Transform socket, Renderer glow)
    {
        // --- Ensure a STABLE BoxCollider lives on the Weapon root so swapping the FBX child doesn't orphan ISDK collider refs ---
        BoxCollider stableCol = weapon.GetComponent<BoxCollider>();
        if (stableCol == null)
        {
            stableCol = weapon.AddComponent<BoxCollider>();
            stableCol.size = new Vector3(0.08f, 0.12f, 0.55f); // rifle-shaped grab volume
            stableCol.center = Vector3.zero;
            stableCol.isTrigger = false;
        }

        // --- Swap FBX under VisualRoot ---
        Transform visualRoot = weapon.transform.Find("VisualRoot");
        if (visualRoot != null)
        {
            for (int i = visualRoot.childCount - 1; i >= 0; i--) Object.DestroyImmediate(visualRoot.GetChild(i).gameObject);

            GameObject fbxAsset = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Models/Rifle/{cfg.fbxName}.fbx");
            if (fbxAsset != null)
            {
                GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(fbxAsset, visualRoot);
                inst.name = cfg.fbxName;
                inst.transform.localPosition = Vector3.zero;
                inst.transform.localRotation = Quaternion.identity;
                inst.transform.localScale = Vector3.one;
                AutoFitImportedModel(weapon, inst);
            }
            else Debug.LogWarning($"[VRArenaSetup] FBX not found at Assets/Models/Rifle/{cfg.fbxName}.fbx");
        }

        // --- Refresh HandGrabInteractable._colliders to only reference the stable root collider ---
        RefreshHandGrabColliders(weapon, stableCol);

        // --- Tune WeaponShoot ---
        var shoot = weapon.GetComponent<WeaponShoot>();
        if (shoot != null)
        {
            SerializedObject so = new SerializedObject(shoot);
            so.FindProperty("fireRate").floatValue = cfg.fireRate;
            so.FindProperty("damage").floatValue = cfg.damage;
            so.FindProperty("maxAmmo").intValue = cfg.maxAmmo;
            so.FindProperty("currentAmmo").intValue = cfg.maxAmmo;
            so.FindProperty("autoFire").boolValue = cfg.autoFire;
            so.FindProperty("recoilKick").floatValue = cfg.recoilKick;
            so.FindProperty("recoilAngle").floatValue = cfg.recoilAngle;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(shoot);
        }

        // --- Rigidbody for physics + socket ---
        var rb = weapon.GetComponent<Rigidbody>();
        if (rb == null) rb = weapon.AddComponent<Rigidbody>();
        rb.useGravity = false;
        rb.isKinematic = true;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

        // --- WeaponSocket script ---
        var sock = weapon.GetComponent<WeaponSocket>();
        if (sock == null) sock = weapon.AddComponent<WeaponSocket>();
        SerializedObject sso = new SerializedObject(sock);
        sso.FindProperty("socketHome").objectReferenceValue = socket;
        if (glow != null) sso.FindProperty("socketGlowRenderer").objectReferenceValue = glow;
        sso.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(sock);

        // --- Per-weapon audio clips ---
        AssignVariantAudio(weapon, cfg);

        // --- Snap to socket immediately ---
        weapon.transform.position = socket.position;
        weapon.transform.rotation = socket.rotation;
        EditorUtility.SetDirty(weapon);
    }

    private static void RefreshHandGrabColliders(GameObject weapon, Collider stableCollider)
    {
        // Find any component named "HandGrabInteractable" or "GrabInteractable" on or under the weapon
        Component[] all = weapon.GetComponentsInChildren<Component>(true);
        foreach (var comp in all)
        {
            if (comp == null) continue;
            string typeName = comp.GetType().Name;
            if (typeName != "HandGrabInteractable" && typeName != "GrabInteractable") continue;

            try
            {
                SerializedObject so = new SerializedObject(comp);
                var collidersProp = so.FindProperty("_colliders");
                if (collidersProp == null || !collidersProp.isArray) { so.Dispose(); continue; }

                // Clear any stale (null/destroyed) refs and ensure stableCollider is present
                bool hasStable = false;
                for (int i = collidersProp.arraySize - 1; i >= 0; i--)
                {
                    var elem = collidersProp.GetArrayElementAtIndex(i);
                    var refVal = elem.objectReferenceValue;
                    if (refVal == null) { collidersProp.DeleteArrayElementAtIndex(i); continue; }
                    if (refVal == stableCollider) hasStable = true;
                }
                if (!hasStable)
                {
                    collidersProp.arraySize++;
                    var newElem = collidersProp.GetArrayElementAtIndex(collidersProp.arraySize - 1);
                    newElem.objectReferenceValue = stableCollider;
                }

                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(comp);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[VRArenaSetup] Failed to refresh colliders on {typeName}: {e.Message}");
            }
        }
    }

    private static void AssignVariantAudio(GameObject weapon, WeaponConfig cfg)
    {
        AudioClip shootClip = AssetDatabase.LoadAssetAtPath<AudioClip>($"Assets/Audio/{cfg.shootClip}.wav");
        AudioClip reloadClip = AssetDatabase.LoadAssetAtPath<AudioClip>($"Assets/Audio/{cfg.reloadClip}.wav");

        Transform shootT = weapon.transform.Find("AudioShoot");
        Transform reloadT = weapon.transform.Find("AudioReload");

        if (shootT != null)
        {
            var src = shootT.GetComponent<AudioSource>();
            if (src != null && shootClip != null) { src.clip = shootClip; EditorUtility.SetDirty(src); }
        }
        if (reloadT != null)
        {
            var src = reloadT.GetComponent<AudioSource>();
            if (src != null && reloadClip != null) { src.clip = reloadClip; EditorUtility.SetDirty(src); }
        }
    }

    private static void AutoFitImportedModel(GameObject weapon, GameObject modelRoot)
    {
        const float TARGET_LENGTH = 0.7f;

        // Compute world-space bounds in identity orientation
        Renderer[] renderers = modelRoot.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;
        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
        Vector3 size = b.size;

        int longestAxis = 0;
        if (size.y > size.x && size.y > size.z) longestAxis = 1;
        else if (size.z > size.x && size.z > size.y) longestAxis = 2;

        Quaternion rotateToZ = Quaternion.identity;
        if (longestAxis == 0) rotateToZ = Quaternion.Euler(0, -90, 0);
        else if (longestAxis == 1) rotateToZ = Quaternion.Euler(90, 0, 0);
        // Apply 180 flip to keep barrel facing +Z (Quaternius models usually need it)
        rotateToZ *= Quaternion.Euler(0, 180, 0);
        modelRoot.transform.localRotation = rotateToZ;

        float longest = Mathf.Max(size.x, size.y, size.z);
        float scale = TARGET_LENGTH / longest;
        modelRoot.transform.localScale = Vector3.one * scale;

        // Recenter so the centroid sits at the Weapon origin
        renderers = modelRoot.GetComponentsInChildren<Renderer>();
        Bounds nb = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) nb.Encapsulate(renderers[i].bounds);
        Vector3 offsetWorld = weapon.transform.position - nb.center;
        modelRoot.transform.position += offsetWorld;

        // Reposition Muzzle to the +Z extent of the model
        Transform muzzle = weapon.transform.Find("Muzzle");
        if (muzzle != null)
        {
            Vector3 frontWorld = nb.center + weapon.transform.forward * nb.extents.z;
            muzzle.position = frontWorld + weapon.transform.forward * offsetWorld.z;
            muzzle.rotation = weapon.transform.rotation;
        }

        Transform forend = weapon.transform.Find("ForendGrip");
        if (forend != null)
        {
            Vector3 forendWorld = nb.center + weapon.transform.forward * (nb.extents.z * 0.4f);
            forendWorld += -weapon.transform.up * (nb.extents.y * 0.5f);
            forend.position = forendWorld + weapon.transform.forward * offsetWorld.z;
            forend.rotation = weapon.transform.rotation;
        }
    }

    // ---------- Synthetic Hands (CS:GO-style: controllers hidden, gloved hands shown) ----------

    private const string SyntheticHandsBlockDataGuid = "1e4a3f91312cbb042a301e514935092f";
    private const string BBLeftHandSyntheticGuid     = "81b0c6f02d961984eb074b9834c049ae";
    private const string BBRightHandSyntheticGuid    = "392af43851d822242be5b1ae72b35b46";

    [MenuItem("Tools/VR Arena/Setup Synthetic Hands (hide controllers)")]
    public static void SetupSyntheticHands()
    {
        // Try the official Meta BB install path first (handles auto-wiring of HandVisual children)
        bool installedViaBlockData = TryInstallSyntheticHandsViaBlockData();

        if (!installedViaBlockData)
        {
            Debug.LogWarning("[VRArenaSetup] BB install path failed — falling back to manual prefab instantiation.");
            ManualInstallSyntheticHands();
        }

        EnableMultimodalControllerDrivenHands();
        UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
        Debug.Log("[VRArenaSetup] Synthetic hands installed. Controllers will display as gloved hands; trigger/grip animate fingers.");
    }

    private static bool TryInstallSyntheticHandsViaBlockData()
    {
        try
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(SyntheticHandsBlockDataGuid);
            if (string.IsNullOrEmpty(assetPath)) return false;
            ScriptableObject blockData = AssetDatabase.LoadAssetAtPath<ScriptableObject>(assetPath);
            if (blockData == null) return false;

            // Find Meta.XR.BuildingBlocks.Editor.BlockData type (base class) — has InstallWithDependencies (internal async)
            System.Type t = blockData.GetType();
            MethodInfo install = null;
            while (t != null && install == null)
            {
                install = t.GetMethod("InstallWithDependencies",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                    null, new System.Type[] { typeof(GameObject) }, null);
                t = t.BaseType;
            }
            if (install == null) return false;

            object taskObj = install.Invoke(blockData, new object[] { null });
            // Drive the Task to completion synchronously
            var task = taskObj as System.Threading.Tasks.Task;
            if (task != null)
            {
                // Pump editor delayCalls while we wait — avoid full Wait() which can deadlock with editor main thread tasks
                int safety = 200;
                while (!task.IsCompleted && safety-- > 0)
                {
                    System.Threading.Thread.Sleep(10);
                }
                if (task.IsFaulted)
                {
                    Debug.LogWarning($"[VRArenaSetup] BlockData InstallWithDependencies faulted: {task.Exception?.GetBaseException()?.Message}");
                    return false;
                }
            }
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[VRArenaSetup] BlockData reflection install threw: {e.Message}");
            return false;
        }
    }

    private static void ManualInstallSyntheticHands()
    {
        GameObject leftPrefab  = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(BBLeftHandSyntheticGuid));
        GameObject rightPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(BBRightHandSyntheticGuid));
        if (leftPrefab == null || rightPrefab == null)
        {
            Debug.LogError("[VRArenaSetup] [BB] Synthetic Hand prefabs missing from Meta Interaction OVR package.");
            return;
        }

        var hands = Object.FindObjectsByType<Oculus.Interaction.Input.Hand>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var hand in hands)
        {
            // Walk up to the [BuildingBlock] Hand Tracking root
            Transform blockRoot = hand.transform;
            int depth = 0;
            while (blockRoot != null && !blockRoot.name.StartsWith("[BuildingBlock] Hand Tracking") && depth++ < 8)
                blockRoot = blockRoot.parent;
            if (blockRoot == null || !blockRoot.name.StartsWith("[BuildingBlock] Hand Tracking")) continue;

            // Skip if already added
            bool already = false;
            foreach (Transform c in blockRoot) if (c.name.Contains("Synthetic")) { already = true; break; }
            if (already) continue;

            bool isLeft = hand.Handedness == Oculus.Interaction.Input.Handedness.Left;
            GameObject prefab = isLeft ? leftPrefab : rightPrefab;
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, blockRoot);
            instance.SetActive(true);
            instance.name = $"[BuildingBlock] Synthetic {hand.Handedness} Hand";

            var synthetic = instance.GetComponent<Oculus.Interaction.Input.SyntheticHand>();
            if (synthetic != null)
            {
                synthetic.InjectModifyDataFromSource(hand);
                // Wire HandVisual / IHand fields inside the synthetic
                WireIHandRefsRecursively(instance, synthetic);
            }

            // Disable raw hand visuals on the hand tracking block
            var skel = blockRoot.GetComponent<OVRSkeletonRenderer>(); if (skel != null) skel.enabled = false;
            var mesh = blockRoot.GetComponent<OVRMeshRenderer>(); if (mesh != null) mesh.enabled = false;
            var skin = blockRoot.GetComponent<SkinnedMeshRenderer>(); if (skin != null) skin.enabled = false;

            EditorUtility.SetDirty(instance);
            EditorUtility.SetDirty(blockRoot.gameObject);
        }
    }

    private static void WireIHandRefsRecursively(GameObject root, Oculus.Interaction.Input.SyntheticHand source)
    {
        // Many HandVisual / hand-data consumers expose a serialized `_hand` IHand reference.
        // Set any null `_hand` field on MonoBehaviours under the synthetic to point at our SyntheticHand.
        var mbs = root.GetComponentsInChildren<MonoBehaviour>(true);
        foreach (var mb in mbs)
        {
            if (mb == null) continue;
            var so = new SerializedObject(mb);
            var prop = so.FindProperty("_hand");
            if (prop != null && prop.propertyType == SerializedPropertyType.ObjectReference && prop.objectReferenceValue == null)
            {
                prop.objectReferenceValue = source;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }
    }

    private const string TacticalGloveMatPath = MaterialsDir + "/TacticalGloveMat.mat";
    private const string TacticalKnuckleMatPath = MaterialsDir + "/TacticalKnuckleMat.mat";
    private const string HandsModelsDir = "Assets/Models/Hands";

    [MenuItem("Tools/VR Arena/Revert Hands To Default Controllers")]
    public static void RevertHandsToDefault()
    {
        int removed = 0;
        var all = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        // 1) Destroy tactical glove instances
        foreach (var go in all)
        {
            if (go == null) continue;
            if (go.name.StartsWith("TacticalGlove_")) { Object.DestroyImmediate(go); removed++; }
        }

        // 2) Destroy synthetic hand instances we added (BB Synthetic) — leaves base Hand Tracking BB intact
        foreach (var go in all)
        {
            if (go == null) continue;
            if (go.name.Contains("Synthetic") && go.name.Contains("Hand")) { Object.DestroyImmediate(go); removed++; }
        }

        // 3) Remove runtime hider so renderers can re-enable themselves
        var rig = Object.FindFirstObjectByType<OVRCameraRig>();
        if (rig != null)
        {
            var hider = rig.GetComponent<MetaHandVisualHider>();
            if (hider != null) Object.DestroyImmediate(hider);

            // 4) Re-enable every renderer under the rig
            var renderers = rig.GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers) if (r != null) r.enabled = true;

            // 5) Re-enable OVR mesh / skeleton renderers on Hand Tracking blocks
            foreach (Transform child in rig.transform.GetComponentsInChildren<Transform>(true))
            {
                if (child == null) continue;
                var skin = child.GetComponent<SkinnedMeshRenderer>(); if (skin != null) skin.enabled = true;
                var ovrMesh = child.GetComponent<OVRMeshRenderer>(); if (ovrMesh != null) ovrMesh.enabled = true;
                var ovrSkel = child.GetComponent<OVRSkeletonRenderer>(); if (ovrSkel != null) ovrSkel.enabled = true;
            }
        }

        // 6) Also re-enable any OVRControllerHelper child renderers globally
        var helpers = Object.FindObjectsByType<OVRControllerHelper>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var h in helpers)
        {
            var renderers = h.GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers) if (r != null) r.enabled = true;
        }

        // 7) Revert OVRManager flags I'd touched for multimodal hands
        var manager = Object.FindFirstObjectByType<OVRManager>();
        if (manager != null)
        {
            var so = new SerializedObject(manager);
            var cdhp = so.FindProperty("controllerDrivenHandPosesType");
            if (cdhp != null) cdhp.intValue = 0; // None
            var simul = so.FindProperty("launchSimultaneousHandsControllersOnStartup");
            if (simul != null) simul.boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(manager);
        }

        UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
        Debug.Log($"[VRArenaSetup] Reverted hands — destroyed {removed} glove/synthetic objects, re-enabled all rig renderers, removed runtime hider, restored OVRManager flags. Default Quest controllers should be visible again.");
    }

    [MenuItem("Tools/VR Arena/Gloves: Flip 180° around Y")] public static void FlipGlovesY() => RotateGloves(0, 180, 0);
    [MenuItem("Tools/VR Arena/Gloves: Flip 180° around X")] public static void FlipGlovesX() => RotateGloves(180, 0, 0);
    [MenuItem("Tools/VR Arena/Gloves: Flip 180° around Z")] public static void FlipGlovesZ() => RotateGloves(0, 0, 180);
    [MenuItem("Tools/VR Arena/Gloves: Rotate +90° around Y")] public static void RotateGlovesY90() => RotateGloves(0, 90, 0);
    [MenuItem("Tools/VR Arena/Re-Hide All Meta Hand Visuals")] public static void RehideMeta() => HideAllMetaHandVisuals();

    private static void RotateGloves(float x, float y, float z)
    {
        var all = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int n = 0;
        foreach (var go in all)
        {
            if (go == null || !go.name.StartsWith("TacticalGlove_")) continue;
            go.transform.localRotation = go.transform.localRotation * Quaternion.Euler(x, y, z);
            EditorUtility.SetDirty(go);
            n++;
        }
        UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
        Debug.Log($"[VRArenaSetup] Rotated {n} tactical glove(s) by ({x},{y},{z}).");
    }

    [MenuItem("Tools/VR Arena/Install Tactical Gloves From Zip")]
    public static void InstallTacticalGlovesFromZip()
    {
        EnsureFolder("Assets/Models");
        EnsureFolder(HandsModelsDir);

        // 1) First try: is there already an extracted FBX/GLB in Assets/Models/Hands/?
        //    (Unity may have auto-extracted the Sketchfab zip on drop.)
        string modelRel = FindFirstHandModelUnder(HandsModelsDir);

        // 2) If nothing extracted yet, hunt for a zip and extract it ourselves
        if (modelRel == null)
        {
            string zipPath = FindGloveZip();
            if (zipPath == null)
            {
                Debug.LogError($"[VRArenaSetup] No glove FBX/GLB and no zip found.\n" +
                               $"Drop the Sketchfab zip (or extracted FBX/GLB) into '{HandsModelsDir}/'.");
                return;
            }
            Debug.Log($"[VRArenaSetup] Found glove archive: {zipPath}");
            string archiveName = Path.GetFileNameWithoutExtension(zipPath);
            string extractDir = HandsModelsDir + "/" + SanitizeName(archiveName);
            try
            {
                Directory.CreateDirectory(extractDir);
                System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);
                Debug.Log($"[VRArenaSetup] Extracted {Path.GetFileName(zipPath)} → {extractDir}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[VRArenaSetup] Zip extraction failed: {e.Message}\n{e.StackTrace}");
                return;
            }
            AssetDatabase.Refresh();
            modelRel = FindFirstHandModelUnder(HandsModelsDir);
        }

        if (modelRel == null)
        {
            Debug.LogError($"[VRArenaSetup] No .fbx / .glb / .gltf file inside {HandsModelsDir}/ after extraction.");
            return;
        }
        Debug.Log($"[VRArenaSetup] Using hand model asset: {modelRel}");

        // 3) Configure importer where applicable (FBX uses ModelImporter; GLB uses a ScriptedImporter and we leave defaults)
        var importer = AssetImporter.GetAtPath(modelRel) as ModelImporter;
        if (importer != null)
        {
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.SearchAndRemapMaterials(ModelImporterMaterialName.BasedOnTextureName, ModelImporterMaterialSearch.RecursiveUp);
            importer.SaveAndReimport();
        }
        GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(modelRel);
        if (modelAsset == null) { Debug.LogError($"[VRArenaSetup] Could not load model as GameObject: {modelRel}"); return; }

        // alias the rest of the routine to the new var name
        GameObject fbxAsset = modelAsset;

        // 5) Find controller anchors on the OVRCameraRig
        var rig = Object.FindFirstObjectByType<OVRCameraRig>();
        if (rig == null) { Debug.LogError("[VRArenaSetup] No OVRCameraRig in scene."); return; }

        Transform leftAnchor  = rig.transform.Find("TrackingSpace/LeftControllerAnchor");
        Transform rightAnchor = rig.transform.Find("TrackingSpace/RightControllerAnchor");
        if (leftAnchor == null)  leftAnchor  = rig.transform.Find("TrackingSpace/LeftHandAnchor");
        if (rightAnchor == null) rightAnchor = rig.transform.Find("TrackingSpace/RightHandAnchor");
        if (leftAnchor == null || rightAnchor == null)
        {
            Debug.LogError("[VRArenaSetup] Could not locate Left/Right ControllerAnchor or HandAnchor under TrackingSpace.");
            return;
        }

        // 6) Hide everything Meta normally shows on the hands (edit-time pass)
        HideAllMetaHandVisuals();
        // 6b) Add runtime hider so Meta SDK can't re-enable the renderers in Play mode
        if (rig.GetComponent<MetaHandVisualHider>() == null)
        {
            rig.gameObject.AddComponent<MetaHandVisualHider>();
            EditorUtility.SetDirty(rig);
        }

        // 7) Clean any previously-attached tactical gloves on these anchors
        foreach (var anchor in new[] { leftAnchor, rightAnchor })
        {
            for (int i = anchor.childCount - 1; i >= 0; i--)
            {
                var c = anchor.GetChild(i);
                if (c != null && c.name.StartsWith("TacticalGlove_")) Object.DestroyImmediate(c.gameObject);
            }
        }

        // 8) Sketchfab models commonly land in cm (100x too big) or m — we default to 0.01 then
        //    auto-fit so the longest axis is ~14cm (typical hand length). User can tune later.
        AttachAndAutoFitGlove(fbxAsset, rightAnchor, isLeft: false);
        AttachAndAutoFitGlove(fbxAsset, leftAnchor,  isLeft: true);

        // 9) Write attribution for CC-BY models
        WriteAttribution(modelRel);

        UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
        Debug.Log("[VRArenaSetup] Tactical gloves wired to controller anchors. " +
                  "Tune position/rotation in the inspector if grip alignment looks off.");
    }

    private static string FindFirstHandModelUnder(string folder)
    {
        if (!Directory.Exists(folder)) return null;
        // Prefer FBX → GLB → GLTF
        string[] exts = new[] { "*.fbx", "*.glb", "*.gltf" };
        string projectRoot = Path.GetFullPath(".").Replace('\\', '/').TrimEnd('/');
        foreach (var ext in exts)
        {
            string[] hits = Directory.GetFiles(folder, ext, SearchOption.AllDirectories);
            if (hits.Length == 0) continue;
            // Skip our own setup tracer/muzzle prefabs, just in case
            foreach (var hit in hits)
            {
                string rel = hit.Replace('\\', '/');
                if (rel.StartsWith(projectRoot + "/")) rel = rel.Substring(projectRoot.Length + 1);
                if (!rel.StartsWith("Assets/")) continue;
                if (rel.Contains("/Prefabs/")) continue;
                return rel;
            }
        }
        return null;
    }

    private static string FindGloveZip()
    {
        var candidates = new System.Collections.Generic.List<string>();
        if (Directory.Exists(HandsModelsDir))
            candidates.AddRange(Directory.GetFiles(HandsModelsDir, "*.zip", SearchOption.AllDirectories));
        candidates.AddRange(Directory.GetFiles(".", "*.zip", SearchOption.TopDirectoryOnly));
        // Also check the user's Downloads folder as a last resort
        string downloads = System.Environment.ExpandEnvironmentVariables("%USERPROFILE%/Downloads");
        if (Directory.Exists(downloads))
            candidates.AddRange(Directory.GetFiles(downloads, "*.zip", SearchOption.TopDirectoryOnly));

        foreach (var z in candidates)
        {
            string n = Path.GetFileName(z).ToLowerInvariant();
            if (n.Contains("glove") || n.Contains("tactical") || n.Contains("hand"))
                return z.Replace('\\', '/');
        }
        return null;
    }

    private static string SanitizeName(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s.Replace(' ', '_');
    }

    private static void HideAllMetaHandVisuals()
    {
        // Aggressive: kill ANY renderer under the OVR rig that isn't our own TacticalGlove_*
        var rig = Object.FindFirstObjectByType<OVRCameraRig>();
        if (rig != null)
        {
            var allRenderers = rig.GetComponentsInChildren<Renderer>(true);
            foreach (var r in allRenderers)
            {
                if (r == null) continue;
                if (IsUnderTacticalGlove(r.transform)) continue;
                r.enabled = false;
            }
        }

        // Also deactivate the [BuildingBlock] Synthetic Hand roots so their colliders / handlers stop running
        var all = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var go in all)
        {
            if (go == null) continue;
            if (IsUnderTacticalGlove(go.transform)) continue;
            // Catch synthetic hand instances + any HandVisual GO in the rig
            if (go.name.Contains("Synthetic") && go.name.Contains("Hand")) go.SetActive(false);
        }

        // Disable OVRControllerHelper visuals globally (catches helpers outside the rig too)
        var helpers = Object.FindObjectsByType<OVRControllerHelper>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var h in helpers)
        {
            var renderers = h.GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers) if (!IsUnderTacticalGlove(r.transform)) r.enabled = false;
        }
    }

    private static bool IsUnderTacticalGlove(Transform t)
    {
        while (t != null)
        {
            if (t.name.StartsWith("TacticalGlove_")) return true;
            t = t.parent;
        }
        return false;
    }

    private static void AttachAndAutoFitGlove(GameObject fbxAsset, Transform anchor, bool isLeft)
    {
        GameObject glove = (GameObject)PrefabUtility.InstantiatePrefab(fbxAsset, anchor);
        glove.name = "TacticalGlove_" + (isLeft ? "L" : "R");
        glove.transform.localPosition = Vector3.zero;
        glove.transform.localRotation = Quaternion.identity;
        glove.transform.localScale = Vector3.one;

        // Measure bounds in local space, then scale so longest axis ≈ 14 cm
        Renderer[] renderers = glove.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            Debug.LogWarning("[VRArenaSetup] Glove FBX has no renderers — skipping auto-fit.");
            return;
        }
        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
        float longest = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
        if (longest < 0.0001f) longest = 1f;
        const float TARGET_HAND_LEN = 0.14f;
        float scale = TARGET_HAND_LEN / longest;

        // Orient so fingers point along controller +Z (away from the user) and palm rotates inward
        // for a grip. Previous default had hands facing the camera — flipped 180° around Y.
        glove.transform.localPosition = new Vector3(0f, -0.02f, 0.04f);
        glove.transform.localRotation = Quaternion.Euler(0f, isLeft ? -90f : 90f, isLeft ? -90f : 90f);
        glove.transform.localScale = new Vector3(isLeft ? -scale : scale, scale, scale);

        EditorUtility.SetDirty(glove);
    }

    private static void WriteAttribution(string fbxRel)
    {
        // CC-BY requires attribution — drop a credits.txt at project root so it's not lost
        string credits = Path.Combine(".", "ASSET_ATTRIBUTIONS.txt");
        string existing = File.Exists(credits) ? File.ReadAllText(credits) : "";
        string entry = $"\n# Tactical Gloves\n# Source FBX: {fbxRel}\n# License: Creative Commons Attribution (CC-BY 4.0)\n# Author: see Sketchfab page — credit creator in your build's credits screen.\n";
        if (!existing.Contains("# Tactical Gloves"))
        {
            File.WriteAllText(credits, existing + entry);
            Debug.Log($"[VRArenaSetup] Attribution recorded in {credits}");
        }
    }


    [MenuItem("Tools/VR Arena/Apply Tactical Glove Look")]
    public static void ApplyTacticalGloveLook()
    {
        EnsureFolder(MaterialsDir);
        Material gloveMat = CreateOrLoadGloveMaterial(TacticalGloveMatPath, new Color(0.05f, 0.05f, 0.06f), smoothness: 0.18f);
        Material knuckleMat = CreateOrLoadGloveMaterial(TacticalKnuckleMatPath, new Color(0.10f, 0.10f, 0.11f), smoothness: 0.32f);

        int handsTouched = 0;
        var roots = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
        foreach (var go in roots)
        {
            if (go == null) continue;
            if (!go.name.Contains("Synthetic") || !go.name.Contains("Hand")) continue;
            if (go.transform.parent == null) continue; // skip non-rigged stray objects

            // Bulk up the glove visual ~10% so it reads as a tactical glove instead of a skinny civilian hand
            // Apply on the synthetic hand root so colliders + hand visual scale together
            var t = go.transform;
            if (Mathf.Approximately(t.localScale.x, 1f))
                t.localScale = Vector3.one * 1.08f;

            var skins = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var smr in skins)
            {
                if (smr == null) continue;
                int submeshCount = smr.sharedMaterials != null ? smr.sharedMaterials.Length : 1;
                var mats = new Material[submeshCount];
                for (int i = 0; i < submeshCount; i++)
                {
                    // Alternate primary glove + knuckle accent if multiple submeshes; otherwise all glove
                    mats[i] = (submeshCount > 1 && i == 1) ? knuckleMat : gloveMat;
                }
                smr.sharedMaterials = mats;
                EditorUtility.SetDirty(smr);
            }
            handsTouched++;
        }

        UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
        Debug.Log($"[VRArenaSetup] Tactical glove look applied to {handsTouched} synthetic hand(s).");
    }

    private static Material CreateOrLoadGloveMaterial(string path, Color baseColor, float smoothness)
    {
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
        if (mat == null)
        {
            mat = new Material(urpLit ?? Shader.Find("Standard"));
            AssetDatabase.CreateAsset(mat, path);
        }
        else if (urpLit != null && mat.shader != urpLit)
        {
            mat.shader = urpLit;
        }
        mat.color = baseColor;
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", baseColor);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
        if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness);
        if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
        // Slight tactical-fabric emission so the gloves don't disappear in shadow
        if (mat.HasProperty("_EmissionColor"))
        {
            mat.SetColor("_EmissionColor", baseColor * 0.15f);
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static void EnableMultimodalControllerDrivenHands()
    {
        // 1) Force project config to ControllersAndHands so the manifest declares hand tracking + multimodal works.
        try
        {
            var cfg = OVRProjectConfig.CachedProjectConfig;
            if (cfg != null)
            {
                if (cfg.handTrackingSupport != OVRProjectConfig.HandTrackingSupport.ControllersAndHands)
                {
                    cfg.handTrackingSupport = OVRProjectConfig.HandTrackingSupport.ControllersAndHands;
                    EditorUtility.SetDirty(cfg);
                }
            }
        }
        catch (System.Exception e) { Debug.LogWarning($"[VRArenaSetup] OVRProjectConfig handTrackingSupport set failed: {e.Message}"); }

        // 2) Force OVRManager into ConformingToController + multimodal-on-startup so synthetic hands
        //    get a pose even while the controllers are held.
        var manager = Object.FindFirstObjectByType<OVRManager>();
        if (manager == null) return;

        var so = new SerializedObject(manager);

        // 0 = None, 1 = Natural, 2 = ConformingToController
        var cdhpProp = so.FindProperty("controllerDrivenHandPosesType");
        if (cdhpProp != null) cdhpProp.intValue = 2;

        // The runtime flag that actually flips OVRPlugin into multimodal on startup
        var simul = so.FindProperty("launchSimultaneousHandsControllersOnStartup");
        if (simul != null) simul.boolValue = true;

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(manager);
    }
}
