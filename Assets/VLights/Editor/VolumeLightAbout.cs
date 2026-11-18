using UnityEditor;
using UnityEngine;


/*
 * VLight
 * Copyright Brian Su 2011-2019
*/
public class VolumeLightAbout : EditorWindow
{
    private Texture _logoImage;

    [MenuItem("Help/About V-Lights...", false, 1010)]
    private static void Init()
    {
        VolumeLightAbout window = EditorWindow.CreateInstance<VolumeLightAbout>();
        window.ShowUtility();

        window.titleContent = new GUIContent("About V-Lights");
    }

    private void OnGUI()
    {
        GUILayout.BeginHorizontal();
        GUILayout.Space(20);

        GUILayout.BeginVertical();
        if (_logoImage == null)
        {
            _logoImage = Resources.Load("VLights/Logo") as Texture; 
        }
        GUI.DrawTexture(new Rect(-60, -64, 256, 256), _logoImage);
        GUILayout.Space(64);
        GUILayout.FlexibleSpace();
        GUILayout.Label("Version 1.3");
        GUILayout.FlexibleSpace();
        GUILayout.Label("(c) 2011-2019 Brian Su");
        GUILayout.EndVertical();
        GUILayout.EndHorizontal();
    }
}
