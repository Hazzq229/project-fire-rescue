using System;
using System.Collections.Generic;
using System.Linq;
using TA.AdaptiveAnimation;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Editor-only appearance replacement. The imported model keeps its own skeleton.</summary>
public static class CharacterModelReplacement
{
    sealed class Plan
    {
        public GameObject model;
        public Renderer[] renderers;
        public CharacterModelSlot.RigidHeadSetup rigidHead;
    }

    static Bounds CombinedBounds(IEnumerable<Renderer> renderers)
    {
        var items = renderers.Where(renderer => renderer).ToArray();
        if (items.Length == 0) throw new InvalidOperationException("Model tidak memiliki renderer.");
        var bounds = new Bounds();
        bool first = true;
        foreach (var renderer in items)
        {
            if (renderer is SkinnedMeshRenderer skin && skin.sharedMesh && skin.sharedMesh.vertexCount > 0)
            {
                var baked = new Mesh();
                try
                {
                    skin.BakeMesh(baked, true);
                    foreach (var vertex in baked.vertices)
                    {
                        Vector3 point = skin.transform.TransformPoint(vertex);
                        if (first) { bounds = new Bounds(point, Vector3.zero); first = false; }
                        else bounds.Encapsulate(point);
                    }
                }
                finally { Object.DestroyImmediate(baked); }
            }
            else if (renderer is MeshRenderer)
            {
                if (first) { bounds = renderer.bounds; first = false; }
                else bounds.Encapsulate(renderer.bounds);
            }
        }
        if (first) throw new InvalidOperationException("Model tidak memiliki geometri yang dapat diukur.");
        return bounds;
    }

    static Plan Prepare(CharacterModelSlot slot)
    {
        if (Application.isPlaying) throw new InvalidOperationException("Ganti model di Edit Mode.");
        if (!slot.modelRoot || !slot.skeletonRoot || !slot.modelRoot.IsChildOf(slot.transform))
            throw new InvalidOperationException("Model Root dan Skeleton Root harus diisi dari player ini.");
        if (!slot.replacementModel || !EditorUtility.IsPersistent(slot.replacementModel))
            throw new InvalidOperationException("Pilih asset FBX/prefab dari Project, bukan objek scene.");

        var root = slot.replacementModel.transform;
        if (root.GetComponentsInChildren<Transform>(true).Count(transform => transform.name == slot.skeletonRoot.name) != 1)
            throw new InvalidOperationException("Model harus memiliki tepat satu root tulang bernama " + slot.skeletonRoot.name + ".");
        var renderers = root.GetComponentsInChildren<Renderer>(true)
            .Where(renderer => renderer is SkinnedMeshRenderer || renderer is MeshRenderer).ToArray();
        if (!renderers.OfType<SkinnedMeshRenderer>().Any())
            throw new InvalidOperationException("Model tidak memiliki SkinnedMeshRenderer.");
        var rigidHead = slot.savedRigidHead;
        if (rigidHead != null && rigidHead.model == slot.replacementModel)
            ValidateRigidHead(slot, rigidHead);
        // Capture the currently calibrated setup before replacing its hierarchy.
        if (slot.generatedVisuals && slot.appliedModel == slot.replacementModel &&
            slot.generatedVisuals.GetComponent<CharacterRigidHeadFollower>())
            rigidHead = CaptureRigidHead(slot);
        if (rigidHead != null && rigidHead.model == slot.replacementModel)
            ValidateRigidHead(slot, rigidHead);
        else rigidHead = null;
        return new Plan { model = slot.replacementModel, renderers = renderers, rigidHead = rigidHead };
    }

    static CharacterModelSlot.RigidHeadSetup CaptureRigidHead(CharacterModelSlot slot)
    {
        var visual = slot.generatedVisuals.transform;
        var follower = visual.GetComponent<CharacterRigidHeadFollower>();
        if (!follower.SourceHead || !follower.RigidRoot || follower.RigidRoot.parent != visual)
            throw new InvalidOperationException("Rigid head harus memiliki sumber tulang dan root langsung di CharacterAppearance.");
        var imported = follower.SourceHead;
        while (imported.parent && imported.parent != visual) imported = imported.parent;
        if (imported.parent != visual) throw new InvalidOperationException("Sumber kepala bukan bagian dari model pengganti.");
        return new CharacterModelSlot.RigidHeadSetup
        {
            model = slot.replacementModel,
            modelDependencyHash = AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(slot.replacementModel)).ToString(),
            headPath = AnimationUtility.CalculateTransformPath(follower.SourceHead, imported),
            rootName = follower.RigidRoot.name, rootScale = follower.RigidRoot.localScale,
            appearancePosition = visual.localPosition, appearanceRotation = visual.localRotation,
            appearanceScale = visual.localScale,
            headgearName = slot.headgearMeshName, headgearScale = slot.headgearScale, headgearLift = slot.headgearLift,
            parts = imported.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(skin => skin.rootBone == follower.RigidRoot)
                .Select(skin => new CharacterModelSlot.RigidHeadPart {
                    rendererPath = AnimationUtility.CalculateTransformPath(skin.transform, imported),
                    mesh = skin.sharedMesh, localBounds = skin.localBounds
                }).ToArray()
        };
    }

    static void ValidateRigidHead(CharacterModelSlot slot, CharacterModelSlot.RigidHeadSetup setup)
    {
        if (setup.modelDependencyHash != AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(slot.replacementModel)).ToString())
            throw new InvalidOperationException("Asset model berubah. Bangun ulang mesh kepala tersimpan sebelum Apply Model.");
        if (setup.headgearName != slot.headgearMeshName || !Mathf.Approximately(setup.headgearScale, slot.headgearScale) ||
            !Mathf.Approximately(setup.headgearLift, slot.headgearLift))
            throw new InvalidOperationException("Kalibrasi topi berbeda dari mesh kepala tersimpan. Bangun ulang mesh untuk kalibrasi baru; Apply dibatalkan agar topi tidak di-fit dua kali.");
        if (!slot.replacementModel.transform.Find(setup.headPath) || setup.parts == null || setup.parts.Length == 0)
            throw new InvalidOperationException("Konfigurasi rigid head tidak lengkap.");
        foreach (var part in setup.parts)
        {
            var target = slot.replacementModel.transform.Find(part.rendererPath);
            var skin = target ? target.GetComponent<SkinnedMeshRenderer>() : null;
            if (!skin || !skin.sharedMesh || !part.mesh || !EditorUtility.IsPersistent(part.mesh) ||
                part.mesh.vertexCount != skin.sharedMesh.vertexCount || part.mesh.bindposes.Length != 1)
                throw new InvalidOperationException("Mesh rigid head tersimpan tidak cocok: " + part.rendererPath);
        }
    }

    static void RestoreRigidHead(GameObject visual, GameObject imported, CharacterModelSlot.RigidHeadSetup setup)
    {
        var head = imported.transform.Find(setup.headPath);
        var root = new GameObject(setup.rootName);
        Undo.RegisterCreatedObjectUndo(root, "Restore rigid head root");
        Undo.SetTransformParent(root.transform, visual.transform, "Parent rigid head root");
        Undo.RecordObject(root.transform, "Restore rigid head scale");
        root.transform.localScale = setup.rootScale;
        var follower = Undo.AddComponent<CharacterRigidHeadFollower>(visual);
        follower.Configure(head, root.transform);
        foreach (var part in setup.parts)
        {
            var skin = imported.transform.Find(part.rendererPath).GetComponent<SkinnedMeshRenderer>();
            Undo.RecordObject(skin, "Restore persistent head mesh");
            skin.sharedMesh = part.mesh;
            skin.bones = new[] { root.transform };
            skin.rootBone = root.transform;
            skin.localBounds = part.localBounds;
            PrefabUtility.RecordPrefabInstancePropertyModifications(skin);
        }
    }

    // The gameplay controller uses generic animation paths. Match the imported skeleton's
    // path to the existing visual path without editing the source FBX.
    static void MatchAnimationPath(Transform importedRoot, Transform sourceBone, CharacterModelSlot slot)
    {
        var sourcePath = AnimationUtility.CalculateTransformPath(sourceBone, importedRoot).Split('/');
        var targetPath = AnimationUtility.CalculateTransformPath(slot.skeletonRoot, slot.modelRoot).Split('/');
        if (sourcePath.Length != targetPath.Length) return;

        var current = importedRoot;
        for (var i = 0; i < sourcePath.Length; i++)
        {
            current = current.Find(sourcePath[i]);
            if (!current) return;
            current.name = targetPath[i];
        }
    }

    public static string Validate(CharacterModelSlot slot)
    {
        try { var plan = Prepare(slot); return "Cocok: " + plan.renderers.Length + " renderer siap dipasang dengan skeleton asli model."; }
        catch (InvalidOperationException exception) { return "Tidak cocok: " + exception.Message; }
    }

    public static void Apply(CharacterModelSlot slot)
    {
        var plan = Prepare(slot);
        Undo.FlushUndoRecordObjects();
        Undo.IncrementCurrentGroup();
        var group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Replace character model");
        try
        {
            Undo.RegisterCompleteObjectUndo(slot, "Replace character model");
            if (plan.rigidHead != null) slot.savedRigidHead = plan.rigidHead;
            if (slot.generatedVisuals)
            {
                if (slot.generatedMeshes != null)
                    foreach (var mesh in slot.generatedMeshes) if (mesh && !EditorUtility.IsPersistent(mesh)) Undo.DestroyObjectImmediate(mesh);
                Undo.DestroyObjectImmediate(slot.generatedVisuals);
            }
            else
            {
                slot.originalRenderers = slot.modelRoot.GetComponentsInChildren<Renderer>(true)
                    .Where(renderer => renderer is SkinnedMeshRenderer || renderer is MeshRenderer).ToArray();
                slot.originalEnabled = slot.originalRenderers.Select(renderer => renderer.enabled).ToArray();
            }

            var visual = new GameObject("CharacterAppearance");
            Undo.RegisterCreatedObjectUndo(visual, "Create character appearance");
            Undo.SetTransformParent(visual.transform, slot.transform, "Parent character appearance");
            Undo.RecordObject(visual.transform, "Align character appearance");
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one;

            var imported = (GameObject)PrefabUtility.InstantiatePrefab(plan.model);
            Undo.RegisterCreatedObjectUndo(imported, "Instantiate replacement model");
            Vector3 importedPosition = imported.transform.localPosition;
            Quaternion importedRotation = imported.transform.localRotation;
            Vector3 importedScale = imported.transform.localScale;
            Undo.SetTransformParent(imported.transform, visual.transform, "Parent replacement model");
            Undo.RecordObject(imported.transform, "Preserve imported model transform");
            imported.transform.localPosition = importedPosition;
            imported.transform.localRotation = importedRotation;
            imported.transform.localScale = importedScale;

            var sourceBone = imported.GetComponentsInChildren<Transform>(true)
                .Single(transform => transform.name == slot.skeletonRoot.name);

            foreach (var animator in imported.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
            var sourceAnimator = slot.GetComponent<Animator>();
            if (sourceAnimator)
            {
                var mirror = Undo.AddComponent<CharacterModelAnimatorMirror>(visual);
                mirror.Configure(sourceAnimator, slot.skeletonRoot, sourceBone);
            }

            if (plan.rigidHead != null)
            {
                visual.transform.localPosition = plan.rigidHead.appearancePosition;
                visual.transform.localRotation = plan.rigidHead.appearanceRotation;
                visual.transform.localScale = plan.rigidHead.appearanceScale;
                RestoreRigidHead(visual, imported, plan.rigidHead);
            }
            else
            {
                var referenceBounds = CombinedBounds(slot.originalRenderers);
                var importedBounds = CombinedBounds(imported.GetComponentsInChildren<Renderer>(true));
                float uniformScale = referenceBounds.size.y / Mathf.Max(.0001f, importedBounds.size.y);
                visual.transform.localScale = Vector3.one * uniformScale;

                importedBounds = CombinedBounds(imported.GetComponentsInChildren<Renderer>(true));
                Vector3 alignmentOffset = Vector3.zero;
                alignmentOffset.x = referenceBounds.center.x - importedBounds.center.x;
                alignmentOffset.y = referenceBounds.min.y - importedBounds.min.y;
                alignmentOffset.z = referenceBounds.center.z - importedBounds.center.z;
                visual.transform.position += alignmentOffset;
            }

            if (plan.rigidHead == null && (!Mathf.Approximately(slot.headgearScale, 1f) || !Mathf.Approximately(slot.headgearLift, 0f)))
            {
                var headgear = imported.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .FirstOrDefault(renderer => renderer.name == slot.headgearMeshName);
                if (headgear)
                {
                    var weights = headgear.sharedMesh.boneWeights;
                    int bone = weights[0].boneIndex0;
                    if (weights.Any(weight => weight.boneIndex0 != bone || weight.weight0 < .999f))
                        throw new InvalidOperationException("Headgear fit requires an accessory rigidly weighted to one bone.");
                    var fit = Undo.AddComponent<CharacterHeadgearFit>(visual);
                    fit.Configure(headgear, bone, slot.headgearScale, slot.transform.up * slot.headgearLift);
                }
            }

            foreach (var renderer in slot.originalRenderers)
                if (renderer)
                {
                    Undo.RecordObject(renderer, "Hide original appearance");
                    renderer.enabled = false;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                }
            slot.appliedModel = slot.replacementModel;
            slot.generatedVisuals = visual;
            slot.boneAttachments = null;
            slot.generatedMeshes = null;
            EditorUtility.SetDirty(slot);
            PrefabUtility.RecordPrefabInstancePropertyModifications(slot);
            Undo.FlushUndoRecordObjects();
            Undo.CollapseUndoOperations(group);
        }
        catch { Undo.RevertAllDownToGroup(group); throw; }
    }

    public static void Restore(CharacterModelSlot slot)
    {
        if (Application.isPlaying) throw new InvalidOperationException("Gunakan Edit Mode.");
        if (!slot.generatedVisuals) return;
        Undo.FlushUndoRecordObjects();
        Undo.IncrementCurrentGroup();
        var group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Restore character model");
        Undo.RegisterCompleteObjectUndo(slot, "Restore character model");
        if (slot.generatedMeshes != null)
            foreach (var mesh in slot.generatedMeshes) if (mesh && !EditorUtility.IsPersistent(mesh)) Undo.DestroyObjectImmediate(mesh);
        Undo.DestroyObjectImmediate(slot.generatedVisuals);
        for (var i = 0; i < slot.originalRenderers.Length; i++)
            if (slot.originalRenderers[i])
            {
                Undo.RecordObject(slot.originalRenderers[i], "Restore renderer");
                slot.originalRenderers[i].enabled = slot.originalEnabled[i];
                PrefabUtility.RecordPrefabInstancePropertyModifications(slot.originalRenderers[i]);
            }
        slot.generatedVisuals = null;
        slot.appliedModel = null;
        slot.boneAttachments = null;
        slot.generatedMeshes = null;
        slot.originalRenderers = null;
        slot.originalEnabled = null;
        EditorUtility.SetDirty(slot);
        PrefabUtility.RecordPrefabInstancePropertyModifications(slot);
        Undo.FlushUndoRecordObjects();
        Undo.CollapseUndoOperations(group);
    }
}

[CustomEditor(typeof(CharacterModelSlot))]
public sealed class CharacterModelSlotEditor : Editor
{
    string message;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var slot = (CharacterModelSlot)target;
        EditorGUILayout.HelpBox("Model baru memakai skeleton dan bind pose aslinya. Tingginya disamakan dengan visual player lama, sementara animator player disinkronkan tanpa mengubah rig gameplay atau IK.", MessageType.Info);
        if (slot.savedRigidHead != null && slot.savedRigidHead.model)
            EditorGUILayout.HelpBox("Konfigurasi kepala/topi tersimpan akan dipakai saat model yang sama dipasang ulang, termasuk setelah Restore. Perubahan FBX atau kalibrasi topi memerlukan mesh kepala yang dibangun ulang.", MessageType.Info);
        using (new EditorGUI.DisabledScope(Application.isPlaying))
        {
            if (GUILayout.Button("Validate Model")) message = CharacterModelReplacement.Validate(slot);
            if (GUILayout.Button("Apply Model"))
            {
                try { CharacterModelReplacement.Apply(slot); message = "Model berhasil dipasang. Simpan scene/prefab."; }
                catch (Exception exception) { message = exception.Message; }
            }
            using (new EditorGUI.DisabledScope(!slot.generatedVisuals))
                if (GUILayout.Button("Restore Original Model")) { CharacterModelReplacement.Restore(slot); message = "Model awal dipulihkan."; }
        }
        if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, MessageType.Info);
    }
}
