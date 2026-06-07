using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using System.IO;
using System.Collections.Generic;
using TMPro;
using Oculus.Interaction;

// Builds the "Antep franchise" arena scene, hands-off via a sentinel + [InitializeOnLoad].
//
// AntepArena.unity is a clone of MainScene (so it already has the VR rig, weapons,
// WeaponRack/Platform and all Meta ISDK grab wiring). This pass:
//   1) waits for glTFast to import Assets/Models/Zombie/Zombie.glb,
//   2) builds a looping-walk AnimatorController from the imported clips,
//   3) assembles Assets/Prefabs/Zombie.prefab (collider + Zombie AI + model),
//   4) opens AntepArena, lays in the to-scale floor / walls / staircase (from the DWG),
//   5) strips the old ground, boundary walls and plate-Targets,
//   6) drops a ZombieSpawner sized to the arena, and saves.
//
// Footprint comes straight from "vr arena antep franchise.dwg" (cm -> m): ~9.03 x 15.38 m.
[InitializeOnLoad]
public static class AntepArenaBuilder
{
    private const string SentinelPath = "Assets/Editor/.antep-arena-pending";
    private const string ScenePath = "Assets/Scenes/AntepArena.unity";
    private const string MaterialsDir = "Assets/Materials";
    private const string PrefabsDir = "Assets/Prefabs";
    private const string GlbPath = "Assets/Models/Zombie/Zombie.glb";
    private const string ZombieDir = "Assets/Models/Zombie";
    private const string ZombiePrefabPath = PrefabsDir + "/Zombie.prefab";
    private const string BloodVfxPath = PrefabsDir + "/ZombieHitVFX.prefab";
    private const string LoopWalkPath = ZombieDir + "/ZombieWalk_Loop.anim";
    private const string LoopBitePath = ZombieDir + "/ZombieBite_Loop.anim";
    private const string ControllerPath = ZombieDir + "/ZombieAC.controller";
    private const string VignetteTexPath = MaterialsDir + "/DamageVignette.asset";
    private const string VignetteMatPath = MaterialsDir + "/DamageVignetteMat.mat";

    private const float TargetZombieHeight = 1.7f;

    // ---- DWG data (centimeters); CCW outer perimeter A,B,C,D ----
    private static readonly Vector2[] OuterCm =
    {
        new Vector2(155.4f,   51.4f), new Vector2(1012.0f,  51.4f),
        new Vector2(1058.3f, 1589.4f), new Vector2(155.4f, 1589.4f),
    };
    private const float DoorJambStartCm = 621.4f, DoorJambEndCm = 655.4f;
    private const float StairXMinCm = 887.4f, StairXMaxCm = 987.4f;
    private const float StairFrontCm = 1407.4f, StairBackCm = 1589.4f;
    private const int StairSteps = 7;
    private const float StepDepthCm = 26f, StepRiseM = 0.17f;
    private const float WallHeight = 3.0f, WallThickness = 0.2f;

    // Structural columns from the DWG (layer 0): (centerX, centerY, widthX, depthY) in cm.
    // These exist physically in the room — they must appear full-height and exactly placed
    // so a headset wearer doesn't walk into them.
    private static readonly Vector4[] ColumnsCm =
    {
        new Vector4(659.9f,  686.9f, 59f, 31f),
        new Vector4(659.4f, 1262.4f, 58f, 30f),
    };

    private static int _retries;

    static AntepArenaBuilder()
    {
        EditorApplication.delayCall += AutoRunIfPending;
    }

    private static void AutoRunIfPending()
    {
        if (!File.Exists(SentinelPath)) return;

        if (!ZombieAssetsReady())
        {
            if (_retries++ % 60 == 0)
                Debug.Log("[AntepArenaBuilder] Waiting for glTFast to import Zombie.glb…");
            if (_retries > 4000)
            {
                Debug.LogError("[AntepArenaBuilder] Gave up waiting for Zombie.glb import. Is the glTFast package resolved?");
                return;
            }
            EditorApplication.delayCall += AutoRunIfPending;
            return;
        }

        try
        {
            Debug.Log("[AntepArenaBuilder] Building Antep arena (zombies edition).");
            GameObject zombiePrefab = BuildZombiePrefab();
            BuildSceneContent(zombiePrefab);
            File.Delete(SentinelPath);
            if (File.Exists(SentinelPath + ".meta")) File.Delete(SentinelPath + ".meta");
            AssetDatabase.Refresh();
            Debug.Log("[AntepArenaBuilder] Done. " + ScenePath + " ready with weapons + zombies.");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[AntepArenaBuilder] Build failed: {e.Message}\n{e.StackTrace}");
        }
    }

    private static bool ZombieAssetsReady()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(GlbPath) == null) return false;
        if (Clip("zombie_bite") == null || Clip("pickup_health") == null) return false; // wait for audio import too
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(GlbPath))
            if (o is AnimationClip) return true;
        return false;
    }

    [MenuItem("Tools/VR Arena/Build Antep Franchise Scene")]
    public static void BuildManually()
    {
        if (!ZombieAssetsReady()) { Debug.LogError("[AntepArenaBuilder] Zombie.glb not imported yet."); return; }
        BuildSceneContent(BuildZombiePrefab());
    }

    // ============================================================
    //  ZOMBIE PREFAB (model + looping walk controller + AI)
    // ============================================================

    private static GameObject BuildZombiePrefab()
    {
        EnsureFolder(PrefabsDir);

        // ---- gather imported clips + avatar ----
        AnimationClip walkSrc = null, biteSrc = null, firstClip = null;
        Avatar avatar = null;
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(GlbPath))
        {
            if (o is AnimationClip clip)
            {
                if (firstClip == null) firstClip = clip;
                if (clip.name.Contains("Walk")) walkSrc = clip;
                else if (clip.name.Contains("Bite")) biteSrc = clip;
            }
            else if (o is Avatar av) avatar = av;
        }
        if (walkSrc == null) walkSrc = firstClip;

        // ---- looping copies + Walk/Attack animator controller ----
        AnimationClip walkLoop = MakeLoopClip(walkSrc, LoopWalkPath, "ZombieWalk_Loop");
        AnimationClip biteLoop = biteSrc != null ? MakeLoopClip(biteSrc, LoopBitePath, "ZombieBite_Loop") : walkLoop;
        AnimatorController controller = BuildZombieController(walkLoop, biteLoop);

        // ---- assemble prefab root ----
        GameObject root = new GameObject("Zombie");

        GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(GlbPath);
        GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
        PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        model.transform.SetParent(root.transform, false);

        // scale model to a believable height and drop its feet to y=0
        Bounds b = ComputeBounds(model);
        float h = Mathf.Max(0.01f, b.size.y);
        float scale = TargetZombieHeight / h;
        model.transform.localScale = Vector3.one * scale;
        b = ComputeBounds(model);
        model.transform.localPosition = new Vector3(-b.center.x, -b.min.y, -b.center.z);

        // animator (glTFast places it on the model root, but be defensive)
        Animator animator = model.GetComponentInChildren<Animator>();
        if (animator == null) animator = model.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        if (avatar != null) animator.avatar = avatar;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        // collider sized to the scaled body (on the root, so raycasts find Zombie via GetComponentInParent)
        CapsuleCollider col = root.AddComponent<CapsuleCollider>();
        col.height = TargetZombieHeight;
        col.radius = 0.28f;
        col.center = new Vector3(0f, TargetZombieHeight * 0.5f, 0f);
        col.direction = 1; // Y

        Rigidbody rb = root.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        // AI + feedback wiring
        Zombie zb = root.AddComponent<Zombie>();
        GameObject bloodVfx = BuildZombieBloodVFX(); // URP-correct (default particle mat renders magenta in URP)
        SerializedObject so = new SerializedObject(zb);
        if (bloodVfx != null)
        {
            so.FindProperty("hitVfxPrefab").objectReferenceValue = bloodVfx;
            so.FindProperty("deathVfxPrefab").objectReferenceValue = bloodVfx;
        }
        AudioClip bite = Clip("zombie_bite"), zdeath = Clip("zombie_death");
        AudioClip g1 = Clip("zombie_groan_1"), g2 = Clip("zombie_groan_2");
        if (bite != null) so.FindProperty("biteClip").objectReferenceValue = bite;
        if (zdeath != null) so.FindProperty("deathClip").objectReferenceValue = zdeath;
        var groans = so.FindProperty("groanClips");
        var gl = new List<AudioClip>(); if (g1 != null) gl.Add(g1); if (g2 != null) gl.Add(g2);
        groans.arraySize = gl.Count;
        for (int i = 0; i < gl.Count; i++) groans.GetArrayElementAtIndex(i).objectReferenceValue = gl[i];
        so.ApplyModifiedPropertiesWithoutUndo();

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, ZombiePrefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        Debug.Log($"[AntepArenaBuilder] Zombie prefab built (scale {scale:F3}, walk '{(walkSrc != null ? walkSrc.name : "none")}', bite '{(biteSrc != null ? biteSrc.name : "none")}').");
        return prefab;
    }

    // Blood/impact burst with a URP-correct particle material. Unity's default particle
    // material renders solid magenta under URP — this fixes the "pink stuff" on hits.
    private static GameObject BuildZombieBloodVFX()
    {
        Color blood = new Color(0.6f, 0.03f, 0.03f);
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialsDir + "/ZombieBloodMat.mat");
        if (mat == null)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                     ?? Shader.Find("Universal Render Pipeline/Unlit")
                     ?? Shader.Find("Sprites/Default");
            mat = new Material(sh);
            mat.color = blood;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", blood);
            AssetDatabase.CreateAsset(mat, MaterialsDir + "/ZombieBloodMat.mat");
        }

        GameObject root = new GameObject("ZombieHitVFX");
        var ps = root.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.duration = 0.4f; main.loop = false; main.startLifetime = 0.5f;
        main.startSpeed = 3.5f; main.startSize = 0.06f; main.startColor = blood; main.maxParticles = 40;
        var emission = ps.emission;
        emission.rateOverTime = 0;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 22) });
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 35f; shape.radius = 0.03f;
        var psr = root.GetComponent<ParticleSystemRenderer>();
        psr.sharedMaterial = mat;
        psr.renderMode = ParticleSystemRenderMode.Billboard;
        psr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        psr.receiveShadows = false;

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, BloodVfxPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    private static Bounds ComputeBounds(GameObject go)
    {
        var rends = go.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return new Bounds(go.transform.position, Vector3.one);
        Bounds b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        return b;
    }

    private static AnimationClip MakeLoopClip(AnimationClip src, string path, string name)
    {
        AnimationClip existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (existing != null) return existing;
        if (src == null) return null;
        var clip = Object.Instantiate(src);
        clip.name = name;
        var s = AnimationUtility.GetAnimationClipSettings(clip);
        s.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, s);
        AssetDatabase.CreateAsset(clip, path);
        return clip;
    }

    // Two-state controller: Walk (default, loop) <-> Attack (bite), driven by bool "Attacking".
    private static AnimatorController BuildZombieController(AnimationClip walk, AnimationClip bite)
    {
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null)
            AssetDatabase.DeleteAsset(ControllerPath);

        var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        controller.AddParameter("Attacking", AnimatorControllerParameterType.Bool);
        var sm = controller.layers[0].stateMachine;

        var walkState = sm.AddState("Walk");
        walkState.motion = walk;
        sm.defaultState = walkState;

        var attackState = sm.AddState("Attack");
        attackState.motion = bite != null ? bite : walk;

        var toAttack = walkState.AddTransition(attackState);
        toAttack.hasExitTime = false; toAttack.duration = 0.15f;
        toAttack.AddCondition(AnimatorConditionMode.If, 0f, "Attacking");

        var toWalk = attackState.AddTransition(walkState);
        toWalk.hasExitTime = false; toWalk.duration = 0.15f;
        toWalk.AddCondition(AnimatorConditionMode.IfNot, 0f, "Attacking");

        return controller;
    }

    // ============================================================
    //  SCENE CONTENT
    // ============================================================

    private static void BuildSceneContent(GameObject zombiePrefab)
    {
        EnsureFolder(MaterialsDir);

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        // recentre footprint on origin
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        foreach (var p in OuterCm)
        {
            minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
            minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
        }
        Vector2 centerCm = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
        float footW = (maxX - minX) / 100f, footL = (maxY - minY) / 100f;

        StripOldEnvironment();

        Material floorMat = CreateOrLoadMat(MaterialsDir + "/ArenaFloorMat.mat", new Color(0.30f, 0.31f, 0.34f));
        Material wallMat = CreateOrLoadMat(MaterialsDir + "/ArenaWallMat.mat", new Color(0.60f, 0.58f, 0.53f));
        Material stairMat = CreateOrLoadMat(MaterialsDir + "/ArenaStairMat.mat", new Color(0.45f, 0.46f, 0.50f));
        Material columnMat = CreateHazardMaterial(MaterialsDir + "/ArenaColumnMat.mat", MaterialsDir + "/HazardStripes.asset");

        GameObject arena = GameObject.Find("AntepArena");
        if (arena != null) Object.DestroyImmediate(arena);
        arena = new GameObject("AntepArena");

        Vector3[] corners = new Vector3[OuterCm.Length];
        for (int i = 0; i < OuterCm.Length; i++) corners[i] = ToUnity(OuterCm[i], centerCm);

        BuildFloor(arena.transform, corners, floorMat);
        BuildPerimeterWalls(arena.transform, corners, centerCm, wallMat);
        BuildStairs(arena.transform, centerCm, stairMat);
        BuildColumns(arena.transform, centerCm, columnMat);

        // ---- zombie spawner sized to the arena (a little inside the walls) ----
        GameObject spawnerGO = GameObject.Find("ZombieSpawner");
        if (spawnerGO != null) Object.DestroyImmediate(spawnerGO);
        spawnerGO = new GameObject("ZombieSpawner");
        var spawner = spawnerGO.AddComponent<ZombieSpawner>();
        SerializedObject sso = new SerializedObject(spawner);
        sso.FindProperty("zombiePrefab").objectReferenceValue = zombiePrefab;
        sso.FindProperty("arenaCenter").vector2Value = Vector2.zero;
        sso.FindProperty("arenaSize").vector2Value = new Vector2(footW - 0.6f, footL - 0.6f);
        sso.FindProperty("floorY").floatValue = 0f;
        sso.ApplyModifiedPropertiesWithoutUndo();

        // give the arena weapons a finite ammo reserve so pickups matter (MainScene stays infinite)
        foreach (var w in Object.FindObjectsByType<WeaponShoot>(FindObjectsSortMode.None))
        {
            var wso = new SerializedObject(w);
            wso.FindProperty("reserveAmmo").intValue = 120;
            wso.ApplyModifiedPropertiesWithoutUndo();
        }

        ConfigureWeaponGrips();
        RemoveForegripGrab();
        AddWeaponMagazines();
        BuildPickups();
        BuildPlayerSystems();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log($"[AntepArenaBuilder] Arena {footW:F2} x {footL:F2} m built; spawner + player health + HUD wired.");
    }

    // PlayerHealth (singleton) + a camera-parented damage vignette + a wall HUD.
    private static void BuildPlayerSystems()
    {
        // --- PlayerHealth on a GameManager object ---
        GameObject gm = GameObject.Find("GameManager");
        if (gm != null) Object.DestroyImmediate(gm);
        gm = new GameObject("GameManager");
        var ph = gm.AddComponent<PlayerHealth>();

        // --- damage vignette parented to the eye + hurt/death audio ---
        Renderer vignette = BuildDamageVignette();
        SerializedObject pso = new SerializedObject(ph);
        if (vignette != null) pso.FindProperty("vignette").objectReferenceValue = vignette;
        AudioClip hurt = Clip("player_hurt"), pdeath = Clip("player_death");
        if (hurt != null) pso.FindProperty("hurtClip").objectReferenceValue = hurt;
        if (pdeath != null) pso.FindProperty("deathClip").objectReferenceValue = pdeath;
        pso.ApplyModifiedPropertiesWithoutUndo();

        // --- wall HUD ---
        BuildHUD();
    }

    private static Renderer BuildDamageVignette()
    {
        GameObject eye = GameObject.Find("CenterEyeAnchor");
        if (eye == null) { Debug.LogWarning("[AntepArenaBuilder] CenterEyeAnchor not found — skipping damage vignette."); return null; }

        Transform old = eye.transform.Find("DamageVignette");
        if (old != null) Object.DestroyImmediate(old.gameObject);

        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "DamageVignette";
        var qc = quad.GetComponent<Collider>(); if (qc != null) Object.DestroyImmediate(qc);
        quad.transform.SetParent(eye.transform, false);
        quad.transform.localPosition = new Vector3(0f, 0f, 0.4f);
        quad.transform.localRotation = Quaternion.identity;
        quad.transform.localScale = new Vector3(1.1f, 0.9f, 1f);

        var mr = quad.GetComponent<MeshRenderer>();
        mr.sharedMaterial = CreateVignetteMaterial();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        return mr;
    }

    private static Material CreateVignetteMaterial()
    {
        Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(VignetteTexPath);
        if (tex == null)
        {
            const int size = 256;
            tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color[size * size];
            Vector2 c = new Vector2(0.5f, 0.5f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x / (float)size, y / (float)size), c) / 0.5f;
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1.0f, d)); // clear centre, red rim
                    px[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            tex.SetPixels(px);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.Apply(false);
            AssetDatabase.CreateAsset(tex, VignetteTexPath);
        }

        Material mat = AssetDatabase.LoadAssetAtPath<Material>(VignetteMatPath);
        if (mat == null)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Transparent");
            mat = new Material(sh);
            AssetDatabase.CreateAsset(mat, VignetteMatPath);
        }
        if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);  // transparent
        if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);      // alpha
        if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);        // double-sided
        if (mat.HasProperty("_ZTest")) mat.SetFloat("_ZTest", 8f);      // always (draw over geometry)
        if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
        if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.renderQueue = 4000;
        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
        mat.mainTexture = tex;
        Color red = new Color(0.85f, 0.05f, 0.05f, 0f); // alpha driven at runtime by PlayerHealth
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", red);
        mat.color = red;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static void BuildHUD()
    {
        foreach (var n in new[] { "ArenaHUD", "Scoreboards" })
        {
            var oldGo = GameObject.Find(n);
            if (oldGo != null) Object.DestroyImmediate(oldGo);
        }

        // One fixed board, hung high on the back (north) wall, facing the arena.
        GameObject boards = new GameObject("Scoreboards");
        BuildScoreboard(boards.transform, "Board_N", new Vector3(0f, 2.7f, 7.55f), Vector3.forward);

        // VR-native: no head-locked flat panels. The world-space board shows everything
        // (health/wave/score/ammo + reload + game over). Remove any old camera-locked UI.
        CleanupHeadLockedUI();

        GameObject hudGo = new GameObject("ArenaHUD");
        hudGo.AddComponent<ArenaHUD>(); // finds the board(s) at runtime; overlays left null
    }

    // Strips earlier head-locked canvases (they made the UI feel like a flatscreen HUD in VR).
    private static void CleanupHeadLockedUI()
    {
        GameObject eye = GameObject.Find("CenterEyeAnchor");
        if (eye == null) return;
        foreach (var n in new[] { "GameOverOverlay", "ReloadWarning" })
        {
            Transform t = eye.transform.Find(n);
            if (t != null) Object.DestroyImmediate(t.gameObject);
        }
    }

    // One hanging scoreboard, oriented so its forward = outward (away from the player who
    // reads it). A ScoreboardFace component lets ArenaHUD push values to all boards at once.
    private static void BuildScoreboard(Transform parent, string name, Vector3 pos, Vector3 outward)
    {
        GameObject root = new GameObject(name);
        root.transform.SetParent(parent, false);
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        root.AddComponent<CanvasScaler>();
        root.AddComponent<GraphicRaycaster>();
        root.transform.position = pos;
        root.transform.rotation = Quaternion.LookRotation(outward, Vector3.up);
        root.transform.localScale = Vector3.one * 0.0045f;
        root.GetComponent<RectTransform>().sizeDelta = new Vector2(1500, 760);

        var frame = AddImage(root.transform, "Frame", new Color(0.5f, 0.4f, 0.08f, 1f), Vector2.zero, Vector2.one, new Vector2(-14, -14), new Vector2(14, 14));
        frame.transform.SetAsFirstSibling();
        AddImage(root.transform, "BG", new Color(0.03f, 0.04f, 0.05f, 0.92f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        var waveText = AddText(root.transform, "Wave", "DALGA 1", 120, new Vector2(0f, 0.72f), new Vector2(1f, 1f), new Color(1f, 0.85f, 0.25f));
        var scoreText = AddText(root.transform, "Score", "SKOR 0", 120, new Vector2(0f, 0.50f), new Vector2(1f, 0.72f), Color.white);
        var ammoText = AddText(root.transform, "Ammo", "CEPHANE 30 / 120", 100, new Vector2(0f, 0.30f), new Vector2(1f, 0.50f), new Color(0.7f, 0.85f, 1f));

        var barBg = AddImage(root.transform, "HealthBarBG", new Color(0.12f, 0.12f, 0.12f, 1f),
            new Vector2(0.05f, 0.05f), new Vector2(0.95f, 0.26f), Vector2.zero, Vector2.zero);
        var fillGO = AddImage(barBg.transform, "HealthFill", new Color(0.2f, 0.85f, 0.2f, 1f),
            Vector2.zero, Vector2.one, new Vector2(6, 6), new Vector2(-6, -6));
        fillGO.type = Image.Type.Filled;
        fillGO.fillMethod = Image.FillMethod.Horizontal;
        fillGO.fillOrigin = 0;
        fillGO.fillAmount = 1f;
        var healthText = AddText(barBg.transform, "HealthText", "100%", 90, Vector2.zero, Vector2.one, Color.white);

        GameObject go = new GameObject("GameOverPanel", typeof(RectTransform));
        go.transform.SetParent(root.transform, false);
        go.AddComponent<Image>().color = new Color(0.4f, 0f, 0f, 0.9f);
        StretchFull(go.GetComponent<RectTransform>());
        AddText(go.transform, "GOText", "ÖLDÜN\n<size=90>A / X ile yeniden başla</size>", 180, Vector2.zero, Vector2.one, Color.white);

        var face = root.AddComponent<ScoreboardFace>();
        SerializedObject so = new SerializedObject(face);
        so.FindProperty("healthFill").objectReferenceValue = fillGO;
        so.FindProperty("healthText").objectReferenceValue = healthText;
        so.FindProperty("waveText").objectReferenceValue = waveText;
        so.FindProperty("scoreText").objectReferenceValue = scoreText;
        so.FindProperty("ammoText").objectReferenceValue = ammoText;
        so.FindProperty("gameOverPanel").objectReferenceValue = go;
        so.ApplyModifiedPropertiesWithoutUndo();
    }


    private static Image AddImage(Transform parent, string name, Color color, Vector2 anchorMin, Vector2 anchorMax, Vector2 offMin, Vector2 offMax)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = color;
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
        rt.offsetMin = offMin; rt.offsetMax = offMax;
        return img;
    }

    private static TMP_Text AddText(Transform parent, string name, string text, float size, Vector2 anchorMin, Vector2 anchorMax, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.text = text;
        t.fontSize = size;
        t.fontStyle = FontStyles.Bold;
        t.alignment = TextAlignmentOptions.Center;
        t.color = color;
        var rt = t.rectTransform;
        rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        return t;
    }

    private static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    // Remove MainScene's old ground / boundary walls / plate targets so only the
    // new arena geometry remains. Weapons, rack, rig and managers are left intact.
    private static void StripOldEnvironment()
    {
        string[] kill = { "Plane", "Boundaries", "Targets", "WaveManager",
                          "East", "West", "North", "South", "ArenaScoreboard" };
        foreach (var name in kill)
        {
            // only top-level objects, and re-find each time (children may already be gone)
            GameObject go = GameObject.Find(name);
            if (go != null && go.transform.parent == null) Object.DestroyImmediate(go);
        }
    }

    private static Vector3 ToUnity(Vector2 cm, Vector2 centerCm)
        => new Vector3((cm.x - centerCm.x) / 100f, 0f, (cm.y - centerCm.y) / 100f);

    private static void BuildFloor(Transform parent, Vector3[] corners, Material mat)
    {
        GameObject floor = new GameObject("Floor");
        floor.transform.SetParent(parent, false);

        Mesh mesh = new Mesh { name = "ArenaFloorMesh" };
        mesh.vertices = corners;
        int[] tris = { 0, 1, 2, 0, 2, 3 };
        Vector3 n = Vector3.Cross(corners[1] - corners[0], corners[2] - corners[0]);
        if (n.y < 0f) tris = new[] { 0, 2, 1, 0, 3, 2 };
        mesh.triangles = tris;
        Vector2[] uv = new Vector2[corners.Length];
        for (int i = 0; i < corners.Length; i++) uv[i] = new Vector2(corners[i].x, corners[i].z);
        mesh.uv = uv;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        floor.AddComponent<MeshFilter>().sharedMesh = mesh;
        floor.AddComponent<MeshRenderer>().sharedMaterial = mat;
        floor.AddComponent<MeshCollider>().sharedMesh = mesh;
    }

    private static void BuildPerimeterWalls(Transform parent, Vector3[] corners, Vector2 centerCm, Material mat)
    {
        GameObject wallsRoot = new GameObject("Walls");
        wallsRoot.transform.SetParent(parent, false);
        int n = corners.Length;
        for (int i = 0; i < n; i++)
        {
            Vector3 a = corners[i], b = corners[(i + 1) % n];
            if (i == 0) // entrance wall -> door gap
            {
                Vector3 doorA = ToUnity(new Vector2(DoorJambStartCm, OuterCm[0].y), centerCm);
                Vector3 doorB = ToUnity(new Vector2(DoorJambEndCm, OuterCm[0].y), centerCm);
                MakeWall(wallsRoot.transform, a, doorA, mat, "Wall_S_a");
                MakeWall(wallsRoot.transform, doorB, b, mat, "Wall_S_b");
            }
            else MakeWall(wallsRoot.transform, a, b, mat, $"Wall_{i}");
        }
    }

    private static void MakeWall(Transform parent, Vector3 a, Vector3 b, Material mat, string name)
    {
        Vector3 dir = b - a;
        float length = dir.magnitude;
        if (length < 0.01f) return;
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = name;
        wall.transform.SetParent(parent, false);
        wall.transform.position = (a + b) * 0.5f + Vector3.up * (WallHeight * 0.5f);
        wall.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
        wall.transform.localScale = new Vector3(WallThickness, WallHeight, length);
        wall.GetComponent<MeshRenderer>().sharedMaterial = mat;
    }

    private static void BuildStairs(Transform parent, Vector2 centerCm, Material mat)
    {
        GameObject stairsRoot = new GameObject("Staircase");
        stairsRoot.transform.SetParent(parent, false);
        float widthM = (StairXMaxCm - StairXMinCm) / 100f;
        float centerXcm = (StairXMinCm + StairXMaxCm) * 0.5f;
        for (int i = 0; i < StairSteps; i++)
        {
            float frontCm = StairFrontCm + i * StepDepthCm;
            float depthM = (StairBackCm - frontCm) / 100f;
            float height = (i + 1) * StepRiseM;
            float stepCenterYcm = frontCm + (StairBackCm - frontCm) * 0.5f;
            Vector3 center = ToUnity(new Vector2(centerXcm, stepCenterYcm), centerCm);
            center.y = height * 0.5f;
            GameObject step = GameObject.CreatePrimitive(PrimitiveType.Cube);
            step.name = $"Step_{i + 1}";
            step.transform.SetParent(stairsRoot.transform, false);
            step.transform.position = center;
            step.transform.localScale = new Vector3(widthM, height, depthM);
            step.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }
    }

    private static void BuildColumns(Transform parent, Vector2 centerCm, Material mat)
    {
        GameObject root = new GameObject("Columns");
        root.transform.SetParent(parent, false);
        for (int i = 0; i < ColumnsCm.Length; i++)
        {
            Vector4 c = ColumnsCm[i];
            Vector3 pos = ToUnity(new Vector2(c.x, c.y), centerCm);
            pos.y = WallHeight * 0.5f;
            GameObject col = GameObject.CreatePrimitive(PrimitiveType.Cube);
            col.name = $"Column_{i + 1}";
            col.transform.SetParent(root.transform, false);
            col.transform.position = pos;
            col.transform.localScale = new Vector3(c.z / 100f, WallHeight, c.w / 100f);
            col.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }
    }

    // ============================================================
    //  WEAPON GRIP (stable two-handed hold)
    // ============================================================

    // Gives every arena weapon a OneGrabFreeTransformer (one hand) + a scale-locked
    // TwoGrabFreeTransformer (two hands). Locking scale stops the rifle from resizing when
    // both hands are on it, so the off-hand on the foregrip only steadies the aim — a stable,
    // fixed two-handed hold instead of a floaty free transform.
    private static void ConfigureWeaponGrips()
    {
        foreach (var ws in Object.FindObjectsByType<WeaponShoot>(FindObjectsSortMode.None))
        {
            GameObject w = ws.gameObject;
            var grabbable = w.GetComponent<Grabbable>();
            if (grabbable == null) continue;

            foreach (var oldT in w.GetComponents<GrabFreeTransformer>()) Object.DestroyImmediate(oldT);

            var oneGrab = w.GetComponent<OneGrabFreeTransformer>() ?? w.AddComponent<OneGrabFreeTransformer>();
            var twoGrab = w.GetComponent<TwoGrabFreeTransformer>() ?? w.AddComponent<TwoGrabFreeTransformer>();

            // lock scale on all axes so two-handed grip never resizes the weapon
            var tso = new SerializedObject(twoGrab);
            SetBoolP(tso, "_constraints.ConstraintsAreRelative", true);
            SetBoolP(tso, "_constraints.MinScale.Constrain", true);
            SetFloatP(tso, "_constraints.MinScale.Value", 1f);
            SetBoolP(tso, "_constraints.MaxScale.Constrain", true);
            SetFloatP(tso, "_constraints.MaxScale.Value", 1f);
            SetBoolP(tso, "_constraints.ConstrainXScale", true);
            SetBoolP(tso, "_constraints.ConstrainYScale", true);
            SetBoolP(tso, "_constraints.ConstrainZScale", true);
            tso.ApplyModifiedPropertiesWithoutUndo();

            var gso = new SerializedObject(grabbable);
            var maxP = gso.FindProperty("_maxGrabPoints"); if (maxP != null) maxP.intValue = -1;
            var oneSlot = gso.FindProperty("_oneGrabTransformer"); if (oneSlot != null) oneSlot.objectReferenceValue = oneGrab;
            var twoSlot = gso.FindProperty("_twoGrabTransformer"); if (twoSlot != null) twoSlot.objectReferenceValue = twoGrab;
            gso.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(grabbable);
        }
        Debug.Log("[AntepArenaBuilder] Weapon grips: OneGrabFree + scale-locked TwoGrabFree for a stable two-handed hold.");
    }

    private static void SetBoolP(SerializedObject so, string path, bool v) { var p = so.FindProperty(path); if (p != null) p.boolValue = v; }
    private static void SetFloatP(SerializedObject so, string path, float v) { var p = so.FindProperty(path); if (p != null) p.floatValue = v; }

    // Removes the foregrip second-grab point (cloned "ISDK_HandGrabInteraction_Forend") from
    // every weapon — back to a single-hand grab.
    private static void RemoveForegripGrab()
    {
        int removed = 0;
        foreach (var ws in Object.FindObjectsByType<WeaponShoot>(FindObjectsSortMode.None))
        {
            foreach (var t in ws.GetComponentsInChildren<Transform>(true))
            {
                if (t != null && t.name == "ISDK_HandGrabInteraction_Forend")
                {
                    Object.DestroyImmediate(t.gameObject);
                    removed++;
                }
            }
        }
        Debug.Log($"[AntepArenaBuilder] Foregrip grab removed from weapons ({removed} cleared).");
    }

    // Adds a small magazine box under each weapon's VisualRoot and wires it to WeaponShoot,
    // so the reload animation has a magazine to eject/insert.
    private static void AddWeaponMagazines()
    {
        Material magMat = CreateOrLoadMat(MaterialsDir + "/MagazineMat.mat", new Color(0.1f, 0.1f, 0.12f));
        foreach (var ws in Object.FindObjectsByType<WeaponShoot>(FindObjectsSortMode.None))
        {
            Transform anchor = ws.transform.Find("VisualRoot") ?? ws.transform;
            var prev = anchor.Find("Magazine");
            if (prev != null) Object.DestroyImmediate(prev.gameObject);

            GameObject mag = GameObject.CreatePrimitive(PrimitiveType.Cube);
            mag.name = "Magazine";
            var col = mag.GetComponent<Collider>(); if (col != null) Object.DestroyImmediate(col); // must not block bullets
            mag.transform.SetParent(anchor, false);
            mag.transform.localPosition = new Vector3(0f, -0.06f, 0.01f);
            mag.transform.localRotation = Quaternion.identity;
            mag.transform.localScale = new Vector3(0.03f, 0.10f, 0.05f);
            mag.GetComponent<MeshRenderer>().sharedMaterial = magMat;

            var so = new SerializedObject(ws);
            var p = so.FindProperty("magazine");
            if (p != null) p.objectReferenceValue = mag.transform;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        Debug.Log("[AntepArenaBuilder] Magazines added + wired for the reload animation.");
    }


    // ============================================================
    //  PICKUPS (health / ammo)
    // ============================================================

    private static void BuildPickups()
    {
        EnsureFolder(PrefabsDir);
        GameObject health = BuildPickupPrefab(PrefabsDir + "/HealthPickup.prefab", Pickup.Kind.Health, 35f,
            new Color(0.1f, 0.9f, 0.2f), Clip("pickup_health"), true);
        GameObject ammo = BuildPickupPrefab(PrefabsDir + "/AmmoPickup.prefab", Pickup.Kind.Ammo, 60f,
            new Color(1f, 0.8f, 0.1f), Clip("pickup_ammo"), false);

        GameObject root = GameObject.Find("Pickups");
        if (root != null) Object.DestroyImmediate(root);
        root = new GameObject("Pickups");

        PlacePickup(root.transform, health, new Vector3(-3f, 0f, -3f));
        PlacePickup(root.transform, health, new Vector3(3f, 0f, 5.5f));
        PlacePickup(root.transform, ammo, new Vector3(3f, 0f, -3f));
        PlacePickup(root.transform, ammo, new Vector3(-3f, 0f, 5.5f));
    }

    private static void PlacePickup(Transform parent, GameObject prefab, Vector3 pos)
    {
        if (prefab == null) return;
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
    }

    private static GameObject BuildPickupPrefab(string path, Pickup.Kind kind, float amount, Color color, AudioClip clip, bool cross)
    {
        GameObject root = new GameObject(kind + "Pickup");
        var p = root.AddComponent<Pickup>();

        GameObject vis = new GameObject("Visual");
        vis.transform.SetParent(root.transform, false);
        vis.transform.localPosition = new Vector3(0f, 0.5f, 0f);

        Material mat = CreateEmissiveMat(MaterialsDir + "/" + kind + "PickupMat.mat", color);
        MakeUncollidableCube(vis.transform, Vector3.zero, Vector3.one * 0.22f, mat);
        if (cross)
        {
            Material white = CreateEmissiveMat(MaterialsDir + "/PickupCrossMat.mat", Color.white);
            MakeUncollidableCube(vis.transform, new Vector3(0, 0, 0.12f), new Vector3(0.16f, 0.05f, 0.02f), white);
            MakeUncollidableCube(vis.transform, new Vector3(0, 0, 0.12f), new Vector3(0.05f, 0.16f, 0.02f), white);
        }

        SerializedObject so = new SerializedObject(p);
        so.FindProperty("kind").enumValueIndex = (int)kind;
        so.FindProperty("amount").floatValue = amount;
        so.FindProperty("visual").objectReferenceValue = vis.transform;
        if (clip != null) so.FindProperty("pickupClip").objectReferenceValue = clip;
        so.ApplyModifiedPropertiesWithoutUndo();

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return prefab;
    }

    private static GameObject MakeUncollidableCube(Transform parent, Vector3 localPos, Vector3 scale, Material mat)
    {
        GameObject c = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var col = c.GetComponent<Collider>(); if (col != null) Object.DestroyImmediate(col);
        c.transform.SetParent(parent, false);
        c.transform.localPosition = localPos;
        c.transform.localScale = scale;
        c.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return c;
    }

    private static AudioClip Clip(string name) => AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/" + name + ".wav");

    private static Material CreateEmissiveMat(string path, Color color)
    {
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            mat = new Material(sh);
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.color = color;
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        mat.EnableKeyword("_EMISSION");
        if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", color * 1.6f);
        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        EditorUtility.SetDirty(mat);
        return mat;
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

    // Procedural tileable 45° yellow/black hazard stripe texture (no external asset needed).
    private static Texture2D CreateHazardTexture(string path)
    {
        Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (existing != null) return existing;

        const int size = 256, period = 64; // 32px yellow + 32px black, tiles seamlessly (256 % 64 == 0)
        Color yellow = new Color(1f, 0.82f, 0f);
        Color black = new Color(0.06f, 0.06f, 0.06f);
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                px[y * size + x] = ((x + y) % period) < period / 2 ? yellow : black;
        tex.SetPixels(px);
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.filterMode = FilterMode.Bilinear;
        tex.Apply(true);
        AssetDatabase.CreateAsset(tex, path);
        return tex;
    }

    // URP Lit material wearing the hazard texture, tiled so stripes read as ~15 cm tape bands.
    private static Material CreateHazardMaterial(string matPath, string texPath)
    {
        Texture2D tex = CreateHazardTexture(texPath);
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, matPath);
        }
        Color white = Color.white;
        mat.color = white;
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", white);
        Vector2 tiling = new Vector2(2f, 10f); // x across the narrow faces, y up the 3 m column
        if (mat.HasProperty("_BaseMap")) { mat.SetTexture("_BaseMap", tex); mat.SetTextureScale("_BaseMap", tiling); }
        mat.mainTexture = tex;
        mat.mainTextureScale = tiling;
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.2f);
        // subtle glow so the yellow stays visible in dim arena lighting
        mat.EnableKeyword("_EMISSION");
        if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", new Color(0.25f, 0.2f, 0f));
        if (mat.HasProperty("_EmissionMap")) mat.SetTexture("_EmissionMap", tex);
        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static Material CreateOrLoadMat(string path, Color color)
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        Material mat = new Material(shader);
        mat.color = color;
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }
}
