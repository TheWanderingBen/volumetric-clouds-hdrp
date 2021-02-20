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
    
    Material cloudMaterial;
    RTHandle cameraBuffer;

    // It can be used to configure render targets and their clear state. Also to create temporary render target textures.
    // When empty this render pass will render to the active camera render target.
    // You should never call CommandBuffer.SetRenderTarget. Instead call <c>ConfigureTarget</c> and <c>ConfigureClear</c>.
    // The render pipeline will ensure target setup and clearing happens in an performance manner.
    protected override void Setup(ScriptableRenderContext renderContext, CommandBuffer cmd)
    {
        cloudMaterial = CoreUtils.CreateEngineMaterial(Shader.Find("Hidden/FullScreen/CloudShader"));
        
        cameraBuffer = RTHandles.Alloc(
            Vector2.one, TextureXR.slices, dimension: TextureXR.dimension,
            colorFormat: GraphicsFormat.B10G11R11_UFloatPack32,
            useDynamicScale: true, name: "Camera Buffer"
        );
    }

    protected override void Execute(CustomPassContext ctx)
    {
        HDUtils.DrawFullScreen(ctx.cmd, cloudMaterial, cameraBuffer, ctx.propertyBlock, shaderPassId: 0);
        
        ctx.propertyBlock.SetVector("_BoundsMin", container.position - container.localScale/2f);
        ctx.propertyBlock.SetVector("_BoundsMax", container.position + container.localScale/2f);
        ctx.propertyBlock.SetTexture("_CameraBuffer", cameraBuffer);
        HDUtils.DrawFullScreen(ctx.cmd, cloudMaterial, ctx.cameraColorBuffer, ctx.propertyBlock, shaderPassId: 1);
    }
    
    // release all resources
    protected override void Cleanup()
    {
        CoreUtils.Destroy(cloudMaterial);
        cameraBuffer.Release();
    }
}
}