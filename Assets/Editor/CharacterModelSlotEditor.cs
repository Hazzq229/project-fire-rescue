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
    }

    static Bounds CombinedBounds(IEnumerable<Renderer> renderers)
    {
        var items = renderers.Where(renderer => renderer).ToArray();
        if (items.Length == 0) throw new InvalidOperationException("Model tidak memiliki renderer.");
        var bounds = items[0].bounds;
        foreach (var renderer in items.Skip(1)) bounds.Encapsulate(renderer.bounds);
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
        return new Plan { model = slot.replacementModel, renderers = renderers };
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
        Undo.IncrementCurrentGroup();
        var group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Replace character model");
        try
        {
            Undo.RecordObject(slot, "Replace character model");
            if (slot.generatedVisuals)
            {
                if (slot.generatedMeshes != null)
                    foreach (var mesh in slot.generatedMeshes) if (mesh) Undo.DestroyObjectImmediate(mesh);
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
            visual.transform.SetParent(slot.transform, false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one;

            var imported = (GameObject)PrefabUtility.InstantiatePrefab(plan.model);
            Undo.RegisterCreatedObjectUndo(imported, "Instantiate replacement model");
            imported.transform.SetParent(visual.transform, false);

            var sourceBone = imported.GetComponentsInChildren<Transform>(true)
                .Single(transform => transform.name == slot.skeletonRoot.name);

            foreach (var animator in imported.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
            var sourceAnimator = slot.GetComponent<Animator>();
            if (sourceAnimator)
            {
                var mirror = Undo.AddComponent<CharacterModelAnimatorMirror>(visual);
                mirror.Configure(sourceAnimator, slot.skeletonRoot, sourceBone);
            }

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
            Undo.CollapseUndoOperations(group);
        }
        catch { Undo.RevertAllDownToGroup(group); throw; }
    }

    public static void Restore(CharacterModelSlot slot)
    {
        if (Application.isPlaying) throw new InvalidOperationException("Gunakan Edit Mode.");
        if (!slot.generatedVisuals) return;
        Undo.IncrementCurrentGroup();
        var group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Restore character model");
        Undo.RecordObject(slot, "Restore character model");
        if (slot.generatedMeshes != null)
            foreach (var mesh in slot.generatedMeshes) if (mesh) Undo.DestroyObjectImmediate(mesh);
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
