
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public class PhysicsDropAndSave : EditorWindow
{
    private float simulationSeconds = 8f;
    private float stepSize = 0.02f;
    private Vector3 gravity = new Vector3(0f, -9.81f, 0f);
    private bool freezeAfterSimulation = true;
    private bool includeInactive = false;
    private bool isRunning = false;

    private class BodyState
    {
        public Rigidbody body;
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 velocity;
        public Vector3 angularVelocity;
        public bool isKinematic;
        public bool useGravity;
    }

    [MenuItem("Tools/Physics Drop & Save")]
    private static void OpenWindow()
    {
        GetWindow<PhysicsDropAndSave>("Physics Drop & Save");
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField(
            "Physics Drop & Save",
            EditorStyles.boldLabel);

        EditorGUILayout.HelpBox(
            "Simulasikan semua Rigidbody di Scene aktif " +
            "tanpa masuk Play Mode, lalu simpan hasilnya.",
            MessageType.Info);

        simulationSeconds = EditorGUILayout.Slider(
            "Durasi Simulasi",
            simulationSeconds, 1f, 30f);

        stepSize = EditorGUILayout.Slider(
            "Physics Step",
            stepSize, 0.01f, 0.033333f);

        gravity = EditorGUILayout.Vector3Field(
            "Gravitasi", gravity);

        freezeAfterSimulation = EditorGUILayout.Toggle(
            "Freeze Setelah Simulasi",
            freezeAfterSimulation);

        includeInactive = EditorGUILayout.Toggle(
            "Sertakan Objek Inactive",
            includeInactive);

        EditorGUILayout.Space(8);

        using (new EditorGUI.DisabledScope(isRunning))
        {
            if (GUILayout.Button(
                "SIMULATE & SAVE",
                GUILayout.Height(38)))
            {
                SimulateAndSave();
            }
        }

        EditorGUILayout.HelpBox(
            "Pastikan Scene sudah disimpan sebagai cadangan. " +
            "Semua Rigidbody non-kinematic di Scene aktif " +
            "akan disimulasikan.",
            MessageType.Warning);
    }

    private void SimulateAndSave()
    {
        if (EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog(
                "Play Mode Aktif",
                "Keluar dari Play Mode terlebih dahulu.",
                "OK");
            return;
        }

        if (!EditorSceneManager
            .SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        Scene scene = SceneManager.GetActiveScene();

        if (!scene.IsValid() || !scene.isLoaded)
        {
            EditorUtility.DisplayDialog(
                "Scene Tidak Valid",
                "Tidak ada Scene aktif yang valid.",
                "OK");
            return;
        }

        List<Rigidbody> allBodies = new List<Rigidbody>();
        List<Rigidbody> targets = new List<Rigidbody>();

        foreach (Rigidbody rb in
            UnityEngine.Object.FindObjectsOfType<Rigidbody>(true))
        {
            if (rb == null || rb.gameObject.scene != scene)
                continue;

            allBodies.Add(rb);

            if ((includeInactive ||
                 rb.gameObject.activeInHierarchy) &&
                !rb.isKinematic)
            {
                targets.Add(rb);
            }
        }

        if (targets.Count == 0)
        {
            EditorUtility.DisplayDialog(
                "Tidak Ada Rigidbody",
                "Tidak ditemukan Rigidbody non-kinematic " +
                "di Scene aktif.",
                "OK");
            return;
        }

        if (!EditorUtility.DisplayDialog(
            "Konfirmasi Simulasi",
            "Jumlah Rigidbody yang akan disimulasikan: " +
            targets.Count +
            "\n\nPastikan Scene sudah dicadangkan.",
            "Mulai Simulasi",
            "Batal"))
        {
            return;
        }

        List<BodyState> states = new List<BodyState>();

        foreach (Rigidbody rb in allBodies)
        {
            states.Add(new BodyState
            {
                body = rb,
                position = rb.position,
                rotation = rb.rotation,
                velocity = rb.velocity,
                angularVelocity = rb.angularVelocity,
                isKinematic = rb.isKinematic,
                useGravity = rb.useGravity
            });
        }

        List<UnityEngine.Object> undoObjects =
            new List<UnityEngine.Object>();

        foreach (Rigidbody rb in targets)
        {
            undoObjects.Add(rb.transform);
            undoObjects.Add(rb);
        }

        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Physics Drop & Save");
        Undo.RecordObjects(
            undoObjects.ToArray(),
            "Physics Drop & Save");

        Vector3 oldGravity = Physics.gravity;
        bool success = false;
        isRunning = true;

        try
        {
            // Bekukan Rigidbody di luar target sementara.
            foreach (Rigidbody rb in allBodies)
            {
                if (!targets.Contains(rb))
                    rb.isKinematic = true;
            }

            Physics.gravity = gravity;
            Physics.SyncTransforms();

            PhysicsScene physicsScene = scene.GetPhysicsScene();

            if (!physicsScene.IsValid())
            {
                throw new InvalidOperationException(
                    "Physics Scene tidak valid.");
            }

            int steps = Mathf.CeilToInt(
                simulationSeconds / stepSize);

            for (int i = 0; i < steps; i++)
            {
                bool cancel =
                    EditorUtility.DisplayCancelableProgressBar(
                        "Physics Drop & Save",
                        "Simulasi " + (i + 1) +
                        " dari " + steps,
                        (float)(i + 1) / steps);

                if (cancel)
                    throw new OperationCanceledException();

                physicsScene.Simulate(stepSize);
            }

            // Hentikan gerakan dan simpan hasil akhir.
            foreach (Rigidbody rb in targets)
            {
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;

                if (freezeAfterSimulation)
                    rb.isKinematic = true;

                PrefabUtility
                    .RecordPrefabInstancePropertyModifications(
                        rb.transform);

                PrefabUtility
                    .RecordPrefabInstancePropertyModifications(rb);

                EditorUtility.SetDirty(rb.transform);
                EditorUtility.SetDirty(rb);
            }

            success = true;
        }
        catch (OperationCanceledException)
        {
            Debug.LogWarning(
                "Simulasi dibatalkan. Mengembalikan kondisi awal.");
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
        finally
        {
            // Pulihkan objek lain dan kondisi awal jika gagal.
            foreach (BodyState state in states)
            {
                Rigidbody rb = state.body;

                if (rb == null)
                    continue;

                bool isTarget = targets.Contains(rb);

                if (!success || !isTarget)
                {
                    rb.isKinematic = true;
                    rb.position = state.position;
                    rb.rotation = state.rotation;
                    rb.velocity = state.velocity;
                    rb.angularVelocity = state.angularVelocity;
                    rb.useGravity = state.useGravity;
                    rb.isKinematic = state.isKinematic;
                }
                else
                {
                    rb.useGravity = state.useGravity;

                    if (!freezeAfterSimulation)
                        rb.isKinematic = state.isKinematic;
                }
            }

            Physics.gravity = oldGravity;
            Physics.SyncTransforms();

            EditorUtility.ClearProgressBar();
            isRunning = false;
            Repaint();
        }

        if (success)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            Undo.CollapseUndoOperations(undoGroup);

            Debug.Log(
                "Physics Drop & Save selesai. " +
                targets.Count +
                " Rigidbody telah disimulasikan. " +
                "Tekan Ctrl+S untuk menyimpan Scene.");
        }
        else
        {
            Undo.RevertAllDownToGroup(undoGroup);
        }
    }
}
