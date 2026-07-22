using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(WaveFunction3DBitmask))]
public class WaveFunction3DBitmaskEditor : Editor
{
    public override void OnInspectorGUI()
    {
        // Draw the default inspector options
        DrawDefaultInspector();

        WaveFunction3DBitmask wfc = (WaveFunction3DBitmask)target;

        GUILayout.Space(10);

        if (GUILayout.Button("Start Generation"))
        {
            if (Application.isPlaying)
            {
                wfc.Regenerate();
            }
            else
            {
                wfc.StartGeneration();
            }
        }

        if (GUILayout.Button("Stop and Clear Generation"))
        {
            wfc.ClearGeneration();
        }
    }
}
