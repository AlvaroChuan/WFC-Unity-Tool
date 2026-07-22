using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(WaveFunction3DGPUWaveFrontAC3))]
public class WaveFunction3DGPUWaveFrontAC3Editor : Editor
{
    public override void OnInspectorGUI()
    {
        // Draw the default inspector options
        DrawDefaultInspector();

        WaveFunction3DGPUWaveFrontAC3 wfc = (WaveFunction3DGPUWaveFrontAC3)target;

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
