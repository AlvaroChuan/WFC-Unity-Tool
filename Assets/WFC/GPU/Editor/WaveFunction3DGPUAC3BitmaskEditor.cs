using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(WaveFunction3DGPUAC3Bitmask))]
public class WaveFunction3DGPUAC3BitmaskEditor : Editor
{
    public override void OnInspectorGUI()
    {
        // Draw the default inspector options
        DrawDefaultInspector();

        WaveFunction3DGPUAC3Bitmask wfc = (WaveFunction3DGPUAC3Bitmask)target;

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
