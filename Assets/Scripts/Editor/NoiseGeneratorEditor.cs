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
            EditorGUI.PrefixLabel(new Rect(25, 45, 100, 15), 0, new GUIContent("Preview:"));

            Rect reservedRect = GUILayoutUtility.GetRect( 256, 256, GUILayout.ExpandWidth( false ), GUILayout.ExpandHeight(false) );
            if (noiseGenerator.CurrentTexture != null)
            {
                //I have no idea why I need to load these byte into a buffer texture, but whatever??
                byte[] textureBytes = noiseGenerator.CurrentTexture.GetRawTextureData();
                Texture2D bufferTexture = new Texture2D(256, 256);
                bufferTexture.LoadRawTextureData(textureBytes);
                bufferTexture.Apply();
                EditorGUI.DrawPreviewTexture(new Rect(30, reservedRect.y, reservedRect.width, reservedRect.height), bufferTexture);
            }

            if(GUILayout.Button("Generate Noise"))
                noiseGenerator.GenerateNoise();
            
            if(GUILayout.Button("Save Noise"))
                noiseGenerator.SaveNoise();
            
            if(GUILayout.Button("Clear Noise"))
                noiseGenerator.ClearNoise();
        }
    }
}
