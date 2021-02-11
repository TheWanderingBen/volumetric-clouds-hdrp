using System;
using System.IO;
using UnityEngine;

public class NoiseGenerator : MonoBehaviour
{
    [SerializeField] ComputeShader computeShader;

    public Texture2D CurrentTexture { get { return currentTexture; } }
    Texture2D currentTexture;

    public void GenerateNoise()
    {
        int textureSize = 256;
        
        int kernelHandle = computeShader.FindKernel("CSMain");

        RenderTexture renderTexture = new RenderTexture(textureSize, textureSize, 24);
        renderTexture.enableRandomWrite = true;
        renderTexture.Create();
        
        computeShader.SetTexture(kernelHandle, "Result", renderTexture);
        computeShader.SetFloat("Time", DateTime.Now.Millisecond);
        computeShader.Dispatch(kernelHandle, textureSize/8, textureSize/8, 1);
        RenderTexture.active = renderTexture;

        Texture2D texture2D = new Texture2D(textureSize, textureSize);
        texture2D.ReadPixels(new Rect(0, 0, textureSize, textureSize), 0, 0);

        RenderTexture.active = null;

        currentTexture = texture2D;
    }

    public void SaveNoise()
    {
        if (currentTexture != null)
        {
            string saveDirectory = $"{Application.persistentDataPath}/CloudNoise";

            //write to directory
            byte[] textureBytes = currentTexture.EncodeToPNG();
            string filepath = $"{saveDirectory}/CloudNoise_{DateTime.Now:yyyy'-'MM'-'dd'_'HH'_'mm'_'ss}.png";

            try
            {
                if (!Directory.Exists(saveDirectory))
                    Directory.CreateDirectory(saveDirectory);

                File.WriteAllBytes(filepath, textureBytes);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }
    }

    public void ClearNoise()
    {
        currentTexture = null;
    }
}
