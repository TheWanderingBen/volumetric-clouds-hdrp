using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Tangier.Effects.CustomPassEffects 
{

class CloudCustomPass : CustomPass
{
    public LayerMask cloudLayer = 0;
    public Transform container;
    public Transform directionalLight;
    public NoiseGenerator noiseGenerator;
    public Vector3 cloudOffset;
    public float cloudScale = 1f;
    [Range(0, 1)] public float densityThreshold = 0.1f;
    public float densityMultiplier = 1f;
    [Range(0, 1)] public float darknessThreshold = 0.1f;
    [Range(0.01f, 10f)] public float stepSize = 0.1f;
    public int lightAbsorptionTowardSun = 20;
    public float blurRadius = 4;
    [Range(0.1f, 1f)] public float blurQuality = 1f;
    
    RTHandle cameraColorCopy;
    RTHandle downSampleCloudBuffer;
    RTHandle downSampleBlurBuffer;
    Material cloudMaterial;
    float oldBlurQuality;

    // It can be used to configure render targets and their clear state. Also to create temporary render target textures.
    // When empty this render pass will render to the active camera render target.
    // You should never call CommandBuffer.SetRenderTarget. Instead call <c>ConfigureTarget</c> and <c>ConfigureClear</c>.
    // The render pipeline will ensure target setup and clearing happens in an performance manner.
    protected override void Setup(ScriptableRenderContext renderContext, CommandBuffer cmd)
    {
        cloudMaterial = CoreUtils.CreateEngineMaterial(Shader.Find("Hidden/FullScreen/CloudShader"));
    }

    protected override void Execute(CustomPassContext ctx)
    {
        RTHandle source = targetColorBuffer == TargetBuffer.Camera ? ctx.cameraColorBuffer : ctx.customColorBuffer.Value;

        if (oldBlurQuality != blurQuality)
        {
            if (cameraColorCopy?.rt != null)
            {
                cameraColorCopy?.Release();
                downSampleCloudBuffer?.Release();
                downSampleBlurBuffer?.Release();
            }
            oldBlurQuality = blurQuality;
        }
        
        if (cameraColorCopy?.rt == null)
        {
            cameraColorCopy = RTHandles.Alloc(
                Vector2.one, TextureXR.slices, dimension: TextureXR.dimension,
                colorFormat: source.rt.graphicsFormat,
                useDynamicScale: true, name: "Camera Color Copy"
            );

            // Allocate the buffers used for the clouds in half resolution to save some memory
            downSampleCloudBuffer = RTHandles.Alloc(
                Vector2.one * blurQuality, TextureXR.slices, dimension: TextureXR.dimension,
                colorFormat: source.rt.graphicsFormat,
                useDynamicScale: true, name: "Down Sample Cloud Buffer"
            );

            // Allocate the buffers used for the clouds in half resolution to save some memory
            downSampleBlurBuffer = RTHandles.Alloc(
                Vector2.one * blurQuality, TextureXR.slices, dimension: TextureXR.dimension,
                colorFormat: source.rt.graphicsFormat,
                useDynamicScale: true, name: "Down Sample Cloud Buffer"
            );
        }
        
        ctx.cmd.CopyTexture(source, cameraColorCopy);

        using (new ProfilingScope(ctx.cmd, new ProfilingSampler("CloudCustomPass - Generate Clouds")))
        {
            ctx.propertyBlock.SetVector("_BoundsMin", container.position - container.localScale / 2f);
            ctx.propertyBlock.SetVector("_BoundsMax", container.position + container.localScale / 2f);
            ctx.propertyBlock.SetVector("_LightPos", directionalLight.position);
            ctx.propertyBlock.SetVector("_CloudOffset", cloudOffset);
            ctx.propertyBlock.SetFloat("_CloudScale", cloudScale);
            ctx.propertyBlock.SetFloat("_DensityThreshold", densityThreshold);
            ctx.propertyBlock.SetFloat("_DensityMultiplier", densityMultiplier);
            ctx.propertyBlock.SetFloat("_DarknessThreshold", darknessThreshold);
            ctx.propertyBlock.SetFloat("_StepSize", stepSize);
            ctx.propertyBlock.SetFloat("_BlurQuality", blurQuality);
            ctx.propertyBlock.SetInt("_LightAbsorptionTowardSun", lightAbsorptionTowardSun);
        
            if (noiseGenerator.CurrentRenderTexture == null)
                noiseGenerator.GenerateNoise();
        
            ctx.propertyBlock.SetTexture("_CloudNoise", noiseGenerator.CurrentRenderTexture);
        
            HDUtils.DrawFullScreen(ctx.cmd, cloudMaterial, downSampleCloudBuffer, ctx.propertyBlock, shaderPassId: 0);
        }

        if (blurQuality < 1.0f)
        {
            using (new ProfilingScope(ctx.cmd, new ProfilingSampler("CloudCustomPass - Horizontal Blur")))
            {
                Vector2Int scaledViewportSize = source.GetScaledSize(source.rtHandleProperties.currentViewportSize);
                ctx.propertyBlock.SetVector("_ViewPortSize",
                    new Vector4(scaledViewportSize.x, scaledViewportSize.y, 1.0f / scaledViewportSize.x,
                        1.0f / scaledViewportSize.y));

                ctx.propertyBlock.SetTexture("_Source", downSampleCloudBuffer);
                ctx.propertyBlock.SetFloat("_Radius", blurRadius * (blurQuality / 2.0f));

                HDUtils.DrawFullScreen(ctx.cmd, cloudMaterial, downSampleBlurBuffer, ctx.propertyBlock,
                    shaderPassId: 1);
            }

            using (new ProfilingScope(ctx.cmd, new ProfilingSampler("CloudCustomPass - Vertical Blur")))
            {
                Vector2Int scaledViewportSize = source.GetScaledSize(source.rtHandleProperties.currentViewportSize);
                ctx.propertyBlock.SetVector("_ViewPortSize",
                    new Vector4(scaledViewportSize.x, scaledViewportSize.y, 1.0f / scaledViewportSize.x,
                        1.0f / scaledViewportSize.y));

                ctx.propertyBlock.SetTexture("_Source", downSampleBlurBuffer);
                ctx.propertyBlock.SetFloat("_Radius", blurRadius * (blurQuality / 2.0f));

                HDUtils.DrawFullScreen(ctx.cmd, cloudMaterial, downSampleCloudBuffer, ctx.propertyBlock,
                    shaderPassId: 2);
            }
        }

        using (new ProfilingScope(ctx.cmd, new ProfilingSampler("CloudCustomPass - Composite")))
        {
            Vector2Int scaledViewportSize = source.GetScaledSize(source.rtHandleProperties.currentViewportSize);
            ctx.propertyBlock.SetVector("_ViewPortSize",
                new Vector4(scaledViewportSize.x, scaledViewportSize.y, 1.0f / scaledViewportSize.x,
                    1.0f / scaledViewportSize.y));
            
            ctx.propertyBlock.SetTexture("_ColorBufferCopy", cameraColorCopy);
            ctx.propertyBlock.SetTexture("_Source", downSampleCloudBuffer);
            
            HDUtils.DrawFullScreen(ctx.cmd, cloudMaterial, ctx.cameraColorBuffer, ctx.propertyBlock, shaderPassId: 3);
        }
    }
    
    // release all resources
    protected override void Cleanup()
    {
        CoreUtils.Destroy(cloudMaterial);
        cameraColorCopy?.Release();
        downSampleCloudBuffer?.Release();
        downSampleBlurBuffer?.Release();
    }
}
}