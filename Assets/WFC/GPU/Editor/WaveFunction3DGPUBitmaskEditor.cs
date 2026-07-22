using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(WaveFunction3DGPUBitmask))]
public class WaveFunction3DGPUBitmaskEditor : Editor
{
    public override void OnInspectorGUI()
    {
        // Draw the default inspector options
        DrawDefaultInspector();

        WaveFunction3DGPUBitmask wfc = (WaveFunction3DGPUBitmask)target;

        GUILayout.Space(10);

        if (GUILayout.Button("Start Generation"))
        {
            if (Application.isPlaying)
            {
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
