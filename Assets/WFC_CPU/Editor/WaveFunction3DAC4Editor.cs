using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(WaveFunction3DAC4))]
public class WaveFunction3DAC4Editor : Editor
{
    public override void OnInspectorGUI()
    {
        // Draw the default inspector options
        DrawDefaultInspector();

        WaveFunction3DAC4 wfc = (WaveFunction3DAC4)target;

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
