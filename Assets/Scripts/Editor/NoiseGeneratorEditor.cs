using UnityEditor;
using UnityEngine;

namespace Editor
{
    [CustomEditor(typeof(NoiseGenerator))]
    public class NoiseGeneratorEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            
            NoiseGenerator noiseGenerator = (NoiseGenerator)target;
            if (GUI.changed)
            {
                noiseGenerator.GenerateNoise();
            }
        }
    }
}
