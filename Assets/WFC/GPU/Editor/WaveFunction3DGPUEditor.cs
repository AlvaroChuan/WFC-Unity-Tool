using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(WaveFunction3DGPU))]
public class WaveFunction3DGPUEditor : Editor
{
    public override void OnInspectorGUI()
    {
        // Draw the default inspector options
        DrawDefaultInspector();

        WaveFunction3DGPU wfc = (WaveFunction3DGPU)target;

        GUILayout.Space(10);

        if (GUILayout.Button("Start Generation"))
        {
            if (Application.isPlaying)
            {
                // Note: Regenerate() is not implemented in the base GPU script yet
                // but StartGeneration behaves safely due to ClearHierarchy
                wfc.StartGeneration();
            }
            else
            {
                wfc.StartGeneration();
            }
        }

        if (GUILayout.Button("Stop and Clear Generation"))
        {
            wfc.StopGeneration();
            wfc.ClearHierarchy();
        }
    }
}
