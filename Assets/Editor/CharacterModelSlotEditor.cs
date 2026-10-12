using System;
using System.Collections.Generic;
using System.Linq;
using TA.AdaptiveAnimation;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Editor-only appearance replacement with validation and a single Undo transaction.</summary>
public static class CharacterModelReplacement
{
    sealed class Plan
    {
        public Transform sourceRoot;
        public Renderer[] renderers;
        public readonly Dictionary<Transform, Transform> bones = new();
    }

    static string Path(Transform child, Transform root) => AnimationUtility.CalculateTransformPath(child, root);

    static Plan Prepare(CharacterModelSlot slot)
    {
        if (Application.isPlaying) throw new InvalidOperationException("Ganti model di Edit Mode.");
        if (!slot.modelRoot || !slot.skeletonRoot || !slot.skeletonRoot.IsChildOf(slot.modelRoot) ||
            !slot.modelRoot.IsChildOf(slot.transform))
            throw new InvalidOperationException("Model Root dan Skeleton Root harus berada di dalam player ini.");
        if (!slot.replacementModel || !EditorUtility.IsPersistent(slot.replacementModel))
            throw new InvalidOperationException("Pilih asset FBX/prefab dari Project, bukan objek scene.");
        var root = slot.replacementModel.transform;
        var candidates = root.GetComponentsInChildren<Transform>(true).Where(t => t.name == slot.skeletonRoot.name).ToArray();
        if (candidates.Length != 1) throw new InvalidOperationException("Model harus memiliki tepat satu root tulang bernama " + slot.skeletonRoot.name);
        var plan = new Plan { sourceRoot = root };
        var sourceBone = candidates[0];
        // Map by full relative path, never by ambiguous leaf names.
        foreach (var bone in sourceBone.GetComponentsInChildren<Transform>(true))
        {
            string path = Path(bone, sourceBone);
            var target = path.Length == 0 ? slot.skeletonRoot : slot.skeletonRoot.Find(path);
            if (!target) throw new InvalidOperationException("Tulang tidak cocok: " + path);
            plan.bones.Add(bone, target);
        }
        // Ancestors determine mesh-space alignment; ignore the model asset's root transform.
        var a = sourceBone.parent; var b = slot.skeletonRoot.parent;
        while (a && a != root && b && b != slot.modelRoot)
        {
            if (Vector3.Distance(a.localPosition, b.localPosition) > .001f ||
                Quaternion.Angle(a.localRotation, b.localRotation) > .1f ||
                Vector3.Distance(a.localScale, b.localScale) > .001f)
                throw new InvalidOperationException("Transform armature berbeda. Samakan orientasi dan skala export model.");
            plan.bones.Add(a, b); a = a.parent; b = b.parent;
        }
        if (a != root || b != slot.modelRoot)
            throw new InvalidOperationException("Struktur armature berbeda dari rig player.");
        plan.renderers = root.GetComponentsInChildren<Renderer>(true)
            .Where(r => r is SkinnedMeshRenderer || r is MeshRenderer).ToArray();
        if (!plan.renderers.OfType<SkinnedMeshRenderer>().Any())
            throw new InvalidOperationException("Model tidak memiliki SkinnedMeshRenderer.");
        foreach (var skin in plan.renderers.OfType<SkinnedMeshRenderer>())
        {
            if (!skin.sharedMesh || skin.bones.Length != skin.sharedMesh.bindposes.Length || skin.bones.Length == 0)
                throw new InvalidOperationException("Mesh/bind pose tidak lengkap: " + skin.name);
            foreach (var bone in skin.bones)
                if (!bone || !plan.bones.ContainsKey(bone))
                    throw new InvalidOperationException("Bone mesh berada di luar skeleton: " + skin.name);
            if (skin.rootBone && !plan.bones.ContainsKey(skin.rootBone))
                throw new InvalidOperationException("Root bone mesh tidak cocok: " + skin.name);
        }
        // Compare to the original imported asset, not a potentially animated scene pose.
        var animator = slot.GetComponent<Animator>();
        var referenceModel = animator && animator.avatar
            ? AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GetAssetPath(animator.avatar)) : null;
        foreach (var pair in plan.bones)
        {
            var rest = referenceModel ? referenceModel.transform.Find(Path(pair.Value, slot.modelRoot)) : null;
            if (!rest) rest = PrefabUtility.GetCorrespondingObjectFromOriginalSource(pair.Value);
            if (!rest) rest = pair.Value;
            if (Vector3.Distance(pair.Key.localPosition, rest.localPosition) > .001f ||
                Quaternion.Angle(pair.Key.localRotation, rest.localRotation) > .1f ||
                Vector3.Distance(pair.Key.localScale, rest.localScale) > .001f)
                throw new InvalidOperationException("Rest pose/skala tulang berbeda: " + pair.Key.name + ". Gunakan rig export yang sama.");
        }
        return plan;
    }

    public static string Validate(CharacterModelSlot slot)
    {
        try { var plan = Prepare(slot); return "Cocok: " + plan.renderers.Length + " renderer siap dipasang."; }
        catch (InvalidOperationException ex) { return "Tidak cocok: " + ex.Message; }
    }

    public static void Apply(CharacterModelSlot slot)
    {
        var plan = Prepare(slot); // Validate everything before touching the current appearance.
        Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Replace character model");
        try
        {
            Undo.RecordObject(slot, "Replace character model");
            if (slot.generatedVisuals)
            {
                if (slot.boneAttachments != null)
                    foreach (var attachment in slot.boneAttachments) if (attachment) Undo.DestroyObjectImmediate(attachment);
                Undo.DestroyObjectImmediate(slot.generatedVisuals);
            }
            else
            {
                slot.originalRenderers = slot.modelRoot.GetComponentsInChildren<Renderer>(true)
                    .Where(r => r is SkinnedMeshRenderer || r is MeshRenderer).ToArray();
                slot.originalEnabled = slot.originalRenderers.Select(r => r.enabled).ToArray();
            }
            var visual = new GameObject("CharacterAppearance");
            Undo.RegisterCreatedObjectUndo(visual, "Create character appearance");
            visual.transform.SetParent(slot.modelRoot, false);
            slot.generatedVisuals = visual;
            var attachments = new List<GameObject>();
            var mappedBones = new HashSet<Transform>(plan.bones.Values);
            var transforms = new Dictionary<Transform, Transform>(plan.bones) { [plan.sourceRoot] = visual.transform };
            Transform CopyTransform(Transform source)
            {
                if (transforms.TryGetValue(source, out var found)) return found;
                var parent = CopyTransform(source.parent);
                var child = new GameObject(source.name);
                Undo.RegisterCreatedObjectUndo(child, "Create visual node");
                child.transform.SetParent(parent, false);
                if (mappedBones.Contains(parent)) attachments.Add(child);
                child.transform.localPosition = source.localPosition;
                child.transform.localRotation = source.localRotation;
                child.transform.localScale = source.localScale;
                child.layer = slot.modelRoot.gameObject.layer;
                child.SetActive(source.gameObject.activeSelf);
                transforms.Add(source, child.transform);
                return child.transform;
            }
            foreach (var source in plan.renderers)
            {
                var parent = CopyTransform(source.transform);
                // Use a dedicated child even when a renderer lives directly on a mapped bone.
                var go = new GameObject(source.name + "_Visual");
                Undo.RegisterCreatedObjectUndo(go, "Create mesh renderer");
                go.transform.SetParent(parent, false);
                if (mappedBones.Contains(parent)) attachments.Add(go);
                go.SetActive(source.gameObject.activeSelf);
                go.layer = slot.modelRoot.gameObject.layer;
                var renderer = (Renderer)Undo.AddComponent(go, source.GetType());
                EditorUtility.CopySerialized(source, renderer);
                renderer.probeAnchor = source.probeAnchor && transforms.TryGetValue(source.probeAnchor, out var anchor) ? anchor : null;
                renderer.lightProbeProxyVolumeOverride = null;
                if (source is SkinnedMeshRenderer skin)
                {
                    var copy = (SkinnedMeshRenderer)renderer;
                    copy.bones = skin.bones.Select(bone => plan.bones[bone]).ToArray();
                    copy.rootBone = skin.rootBone ? plan.bones[skin.rootBone] : null;
                }
                else
                {
                    var filter = source.GetComponent<MeshFilter>();
                    Undo.AddComponent<MeshFilter>(go).sharedMesh = filter ? filter.sharedMesh : null;
                }
            }
            foreach (var renderer in slot.originalRenderers)
                if (renderer) { Undo.RecordObject(renderer, "Hide original appearance"); renderer.enabled = false;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(renderer); }
            slot.appliedModel = slot.replacementModel;
            slot.boneAttachments = attachments.ToArray();
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
        Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Restore character model");
        Undo.RecordObject(slot, "Restore character model");
        if (slot.boneAttachments != null)
            foreach (var attachment in slot.boneAttachments) if (attachment) Undo.DestroyObjectImmediate(attachment);
        Undo.DestroyObjectImmediate(slot.generatedVisuals);
        for (int i = 0; i < slot.originalRenderers.Length; i++)
            if (slot.originalRenderers[i])
            {
                Undo.RecordObject(slot.originalRenderers[i], "Restore renderer");
                slot.originalRenderers[i].enabled = slot.originalEnabled[i];
                PrefabUtility.RecordPrefabInstancePropertyModifications(slot.originalRenderers[i]);
            }
        slot.generatedVisuals = null; slot.appliedModel = null;
        slot.boneAttachments = null;
        slot.originalRenderers = null; slot.originalEnabled = null;
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
        EditorGUILayout.HelpBox("Tarik FBX/prefab ke Replacement Model, lalu Validate dan Apply. Rig gameplay, Animator, dan IK tetap dipakai. Model harus memakai nama tulang, struktur, rest pose, dan skala export yang sama.", MessageType.Info);
        using (new EditorGUI.DisabledScope(Application.isPlaying))
        {
            if (GUILayout.Button("Validate Model")) message = CharacterModelReplacement.Validate(slot);
            if (GUILayout.Button("Apply Model"))
            {
                try { CharacterModelReplacement.Apply(slot); message = "Model berhasil dipasang. Simpan scene/prefab."; }
                catch (Exception ex) { message = ex.Message; }
            }
            using (new EditorGUI.DisabledScope(!slot.generatedVisuals))
                if (GUILayout.Button("Restore Original Model")) { CharacterModelReplacement.Restore(slot); message = "Model awal dipulihkan."; }
        }
        if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, MessageType.Info);
    }
}
