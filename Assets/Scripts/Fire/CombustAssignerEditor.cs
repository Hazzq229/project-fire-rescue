#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public class CombustAssignerEditor : EditorWindow
{
    [MenuItem("Tools/Fire System/Assign Config to All Prefabs")]
    public static void AssignConfigToPrefabs()
    {
        // Cari ScriptableObject config default yang ingin dipasang
        CombustibleConfig defaultConfig = AssetDatabase.LoadAssetAtPath<CombustibleConfig>("Assets/ScriptableObjects/DefaultCombustibleConfig.asset");

        if (defaultConfig == null)
        {
            Debug.LogError("[BatchAssigner] ScriptableObject Config tidak ditemukan di path!");
            return;
        }

        string[] guids = AssetDatabase.FindAssets("t:Prefab");
        int updatedCount = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            if (prefab != null && prefab.TryGetComponent(out CombustObject combust))
            {
                // Gunakan SerializedObject untuk memastikan perubahan tersimpan ke prefab
                SerializedObject serializedObj = new SerializedObject(combust);
                SerializedProperty configProperty = serializedObj.FindProperty("config");

                if (configProperty != null)
                {
                    configProperty.objectReferenceValue = defaultConfig;
                    serializedObj.ApplyModifiedProperties();
                    EditorUtility.SetDirty(prefab);
                    updatedCount++;
                }
            }
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"Berhasil mengupdate {updatedCount} Prefab secara otomatis!");
    }
}
#endif