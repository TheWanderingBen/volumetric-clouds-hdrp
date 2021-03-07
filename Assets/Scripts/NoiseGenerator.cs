using System;
using UnityEngine;
using UnityEngine.Rendering;

public class NoiseGenerator : MonoBehaviour
{
    [SerializeField] ComputeShader computeShader;
    [SerializeField] Vector3 divisions = new Vector3(8, 8, 8);
    [SerializeField] Vector3 divisionsWeight = new Vector3(1f/3f, 1f/3f, 1f/3f);
    [SerializeField] bool generateCenters;
    [SerializeField] bool showPreview;
    [SerializeField] Vector3 previewSlices;
    
    public Texture3D CurrentTexture3D { get { return currentTexture3D; } }
    Texture3D currentTexture3D;
    
    public RenderTexture CurrentRenderTexture { get { return renderTexture; } }
    RenderTexture renderTexture;
    
    public bool ShowPreview { get { return showPreview; } }
    public Vector3 PreviewSlices { get { return previewSlices; } }

    int textureSize = 256;
    
    public void GenerateNoise()
    {
        float totalWeight = divisionsWeight.x + divisionsWeight.y + divisionsWeight.z;
        int kernelHandle = computeShader.FindKernel("CSMain");
        
        renderTexture = new RenderTexture(textureSize, textureSize, 0, RenderTextureFormat.ARGB32);
        renderTexture.volumeDepth = textureSize;
        renderTexture.dimension = TextureDimension.Tex3D;
        renderTexture.enableRandomWrite = true;
        renderTexture.Create();
        
        computeShader.SetTexture(kernelHandle, "Result", renderTexture);
        computeShader.SetFloat("Time", DateTime.Now.Millisecond);
        computeShader.SetBool("GenerateCenters", generateCenters);
        computeShader.SetInt("Divisions0", (int)divisions.x);
        computeShader.SetInt("Divisions1", (int)divisions.y);
        computeShader.SetInt("Divisions2", (int)divisions.z);
        computeShader.SetFloat("DivisionsWeight0", divisionsWeight.x / totalWeight);
        computeShader.SetFloat("DivisionsWeight1", divisionsWeight.y / totalWeight);
        computeShader.SetFloat("DivisionsWeight2", divisionsWeight.z / totalWeight);
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
