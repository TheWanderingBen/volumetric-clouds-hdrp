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
            
            GUILayoutUtility.GetRect( 100, 20, GUILayout.ExpandWidth( false ), GUILayout.ExpandHeight(false) );
            if(GUILayout.Button("Generate Noise"))
                noiseGenerator.GenerateNoise();
            
            if(GUILayout.Button("Save Noise"))
                noiseGenerator.SaveNoise();
            
            if(GUILayout.Button("Clear Noise"))
                noiseGenerator.ClearNoise();
        }

        void OnSceneViewGUI(SceneView sv)
        {
            NoiseGenerator noiseGenerator = (NoiseGenerator)target;
            if (noiseGenerator.CurrentTexture3D != null && noiseGenerator.ShowPreview)
            {
                Handles.matrix = noiseGenerator.transform.localToWorldMatrix;
                Handles.DrawTexture3DSlice(noiseGenerator.CurrentTexture3D, noiseGenerator.PreviewSlices);
            }
        }

        void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneViewGUI;
        }

        void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneViewGUI;
        }
    }
}
