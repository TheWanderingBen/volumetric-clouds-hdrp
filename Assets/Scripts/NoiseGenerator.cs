using System;
using UnityEngine;
using UnityEngine.Rendering;

public class NoiseGenerator : MonoBehaviour
{
    [SerializeField] ComputeShader computeShader;
    [SerializeField] int divisions0 = 8;
    [SerializeField] int divisions1 = 8;
    [SerializeField] int divisions2 = 8;
    [SerializeField] bool generateCenters;
    [SerializeField] bool showPreview;
    [SerializeField] Vector3 previewSlices;
    
    public Texture3D CurrentTexture3D { get { return currentTexture3D; } }
    Texture3D currentTexture3D;
    
    public bool ShowPreview { get { return showPreview; } }
    public Vector3 PreviewSlices { get { return previewSlices; } }

    int textureSize = 256;
    
    public void GenerateNoise()
    {
        int kernelHandle = computeShader.FindKernel("CSMain");
        
        RenderTexture renderTexture = new RenderTexture(textureSize, textureSize, 0, RenderTextureFormat.ARGB32);
        renderTexture.volumeDepth = textureSize;
        renderTexture.dimension = TextureDimension.Tex3D;
        renderTexture.enableRandomWrite = true;
        renderTexture.Create();
        
        computeShader.SetTexture(kernelHandle, "Result", renderTexture);
        computeShader.SetFloat("Time", DateTime.Now.Millisecond);
        computeShader.SetBool("GenerateCenters", generateCenters);
        computeShader.SetInt("Divisions0", divisions0);
        computeShader.SetInt("Divisions1", divisions1);
        computeShader.SetInt("Divisions2", divisions2);
        computeShader.SetInt("Size", textureSize);
        computeShader.Dispatch(kernelHandle, textureSize/8, textureSize/8, textureSize/8);
        
        Texture3D texture3D = new Texture3D(textureSize, textureSize, textureSize, TextureFormat.ARGB32, false);
        Graphics.CopyTexture(renderTexture, texture3D);

        currentTexture3D = texture3D;
    }

#if UNITY_EDITOR
    public void SaveNoise()
    {
        if (currentTexture3D != null)
            UnityEditor.AssetDatabase.CreateAsset(currentTexture3D, "Assets/Worley3DTexture.asset");
    }
#endif

    public void ClearNoise()
    {
        currentTexture3D = null;
    }
}
