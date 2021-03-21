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
    [Range(0.01f, 1f)] public float stepSize = 0.1f;
    public int lightAbsorptionTowardSun = 20;
    
    Material cloudMaterial;

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
        ctx.propertyBlock.SetVector("_BoundsMin", container.position - container.localScale/2f);
        ctx.propertyBlock.SetVector("_BoundsMax", container.position + container.localScale/2f);
        ctx.propertyBlock.SetVector("_LightPos", directionalLight.position);
        ctx.propertyBlock.SetVector("_CloudOffset", cloudOffset);
        ctx.propertyBlock.SetFloat("_CloudScale", cloudScale);
        ctx.propertyBlock.SetFloat("_DensityThreshold", densityThreshold);
        ctx.propertyBlock.SetFloat("_DensityMultiplier", densityMultiplier);
        ctx.propertyBlock.SetFloat("_DarknessThreshold", darknessThreshold);
        ctx.propertyBlock.SetFloat("_StepSize", stepSize);
        ctx.propertyBlock.SetInt("_LightAbsorptionTowardSun", lightAbsorptionTowardSun);
        
        if (noiseGenerator.CurrentRenderTexture == null)
            noiseGenerator.GenerateNoise();
        
        ctx.propertyBlock.SetTexture("_CloudNoise", noiseGenerator.CurrentRenderTexture);
        
        HDUtils.DrawFullScreen(ctx.cmd, cloudMaterial, ctx.cameraColorBuffer, ctx.propertyBlock, shaderPassId: 1);
    }
    
    // release all resources
    protected override void Cleanup()
    {
        CoreUtils.Destroy(cloudMaterial);
    }
}
}