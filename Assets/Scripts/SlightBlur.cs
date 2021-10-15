using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Tangier.Effects.CustomPassEffects
{

class SlightBlur : CustomPass
{
    [SerializeField] float radius = 4;
    [SerializeField] bool useMask = false;
    [SerializeField] LayerMask maskLayer = 0;
    [SerializeField] bool invertMask = false;
    [SerializeField] bool useTexture;
    [SerializeField] Texture2D texture2D;
    [SerializeField] float textureWeight = 1;
    [SerializeField] Vector2 textureSize = new Vector2(512, 512);
    [SerializeField] Color colorAdd = Color.clear;
    [SerializeField] Color colorMultiply = Color.white;
    [SerializeField] Color backingColor = Color.clear;
    [SerializeField, Range(0f, 1f)] float minimumLightness = 0f;
    [SerializeField, Range(0, 10)] int maskOnTextureStrength = 1;
    [SerializeField, Range(0.1f, 1f)] float blurQuality = 1f;

    public Color ColorAdd { get { return colorAdd; } set { colorAdd = value; } }
    public Color ColorMultiply { get { return colorMultiply; } set { colorMultiply = value; } }
    public Color BackingColor { get { return backingColor; } set { backingColor = value; } }
    public float MinimumLightness { get { return minimumLightness; } set { minimumLightness = value; } }

    Material        blurMaterial;
    RTHandle        downSampleBuffer;
    RTHandle        blurBuffer;
    RTHandle        maskBuffer;
    RTHandle        colorCopy;
    ShaderTagId[]   shaderTags;

    static class ShaderID
    {
        public static readonly int _Radius = Shader.PropertyToID("_Radius");
        public static readonly int _UseTexture = Shader.PropertyToID("_UseTexture");
        public static readonly int _Texture = Shader.PropertyToID("_Texture");
        public static readonly int _TextureWeight = Shader.PropertyToID("_TextureWeight");
        public static readonly int _TextureSize = Shader.PropertyToID("_TextureSize");
        public static readonly int _ColorAdd = Shader.PropertyToID("_ColorAdd");
        public static readonly int _ColorMult = Shader.PropertyToID("_ColorMult");
        public static readonly int _BackingColor = Shader.PropertyToID("_BackingColor");
        public static readonly int _MinimumLightness = Shader.PropertyToID("_MinimumLightness");
        public static readonly int _Source = Shader.PropertyToID("_Source");
        public static readonly int _ColorBufferCopy = Shader.PropertyToID("_ColorBufferCopy");
        public static readonly int _Mask = Shader.PropertyToID("_Mask");
        public static readonly int _MaskOnTextureStrength = Shader.PropertyToID("_MaskOnTextureStrength");
        public static readonly int _InvertMask = Shader.PropertyToID("_InvertMask");
        public static readonly int _ViewPortSize = Shader.PropertyToID("_ViewPortSize");
    }

    // It can be used to configure render targets and their clear state. Also to create temporary render target textures.
    // When empty this render pass will render to the active camera render target.
    // You should never call CommandBuffer.SetRenderTarget. Instead call <c>ConfigureTarget</c> and <c>ConfigureClear</c>.
    // The render pipeline will ensure target setup and clearing happens in an performance manner.
    protected override void Setup(ScriptableRenderContext renderContext, CommandBuffer cmd)
    {
        blurMaterial = CoreUtils.CreateEngineMaterial(Shader.Find("Hidden/FullScreen/Blur"));

        // Allocate the buffers used for the blur in half resolution to save some memory
        downSampleBuffer = RTHandles.Alloc(
                                           Vector2.one * blurQuality, TextureXR.slices, dimension: TextureXR.dimension,
                                           colorFormat: GraphicsFormat.B10G11R11_UFloatPack32, // We don't need alpha in the blur
                                           useDynamicScale: true, name: "DownSampleBuffer"
                                          );

        blurBuffer = RTHandles.Alloc(
                                     Vector2.one * blurQuality, TextureXR.slices, dimension: TextureXR.dimension,
                                     colorFormat: GraphicsFormat.B10G11R11_UFloatPack32, // We don't need alpha in the blur
                                     useDynamicScale: true, name: "BlurBuffer"
                                    );

        shaderTags = new ShaderTagId[4]
                     {
                         new ShaderTagId("Forward"),
                         new ShaderTagId("ForwardOnly"),
                         new ShaderTagId("SRPDefaultUnlit"),
                         new ShaderTagId("FirstPass"),
                     };
    }

    void AllocateMaskBuffersIfNeeded()
    {
        if (useMask)
        {
            if (colorCopy?.rt == null)
            {
                var hdrpAsset         = (GraphicsSettings.renderPipelineAsset as HDRenderPipelineAsset);
                var colorBufferFormat = hdrpAsset.currentPlatformRenderPipelineSettings.colorBufferFormat;

                colorCopy = RTHandles.Alloc(
                                            Vector2.one, TextureXR.slices, dimension: TextureXR.dimension,
                                            colorFormat: (GraphicsFormat)colorBufferFormat,
                                            useDynamicScale: true, name: "Color Copy"
                                           );
            }
            if (maskBuffer?.rt == null)
            {
                var hdrpAsset = (GraphicsSettings.renderPipelineAsset as HDRenderPipelineAsset);
                var colorBufferFormat = hdrpAsset.currentPlatformRenderPipelineSettings.colorBufferFormat;
                maskBuffer = RTHandles.Alloc(
                                             Vector2.one, TextureXR.slices, dimension: TextureXR.dimension,
                                             colorFormat: (GraphicsFormat)colorBufferFormat, // We only need a 1 channel mask to composite the blur and color buffer copy
                                             useDynamicScale: true, name: "Blur Mask"
                                            );
            }
        }
    }

    protected override void Execute(ScriptableRenderContext renderContext, CommandBuffer cmd, HDCamera hdCamera, CullingResults cullingResult)
    {
        AllocateMaskBuffersIfNeeded();

        if (blurMaterial != null && radius > 0)
        {
            if (useMask)
            {
                DrawMaskObjects(renderContext, cmd, hdCamera, cullingResult);
            }

            GenerateGaussianMips(cmd, hdCamera);
        }
    }

    protected override void AggregateCullingParameters(ref ScriptableCullingParameters cullingParameters, HDCamera hdCamera)
        => cullingParameters.cullingMask |= (uint)maskLayer.value;

    void DrawMaskObjects(ScriptableRenderContext renderContext, CommandBuffer cmd, HDCamera hdCamera, CullingResults cullingResult)
    {
        // Render the objects in the layer blur mask into a mask buffer with their materials so we keep the alpha-clip and transparency if there is any.
        var result = new RendererListDesc(shaderTags, cullingResult, hdCamera.camera)
                     {
                         rendererConfiguration      = PerObjectData.None,
                         renderQueueRange           = RenderQueueRange.all,
                         sortingCriteria            = SortingCriteria.BackToFront,
                         excludeObjectMotionVectors = false,
                         layerMask                  = maskLayer,
                         stateBlock                 = new RenderStateBlock(RenderStateMask.Depth){ depthState = new DepthState(true, CompareFunction.LessEqual)},
                     };

        CoreUtils.SetRenderTarget(cmd, maskBuffer, ClearFlag.All);
        HDUtils.DrawRendererList(renderContext, cmd, RendererList.Create(result));
    }

    // We need the viewport size in our shader because we're using half resolution render targets (and so the _ScreenSize
    // variable in the shader does not match the viewport).
    void SetViewPortSize(CommandBuffer cmd, MaterialPropertyBlock block, RTHandle target)
    {
        Vector2Int scaledViewportSize = target.GetScaledSize(target.rtHandleProperties.currentViewportSize);
        block.SetVector(ShaderID._ViewPortSize, new Vector4(scaledViewportSize.x, scaledViewportSize.y, 1.0f / scaledViewportSize.x, 1.0f / scaledViewportSize.y));
    }

    void GenerateGaussianMips(CommandBuffer cmd, HDCamera hdCam)
    {
        RTHandle source;

        // Retrieve the target buffer of the blur from the UI:
        if (targetColorBuffer == TargetBuffer.Camera)
            GetCameraBuffers(out source, out _);
        else
            GetCustomBuffers(out source, out _);

        // Save the non blurred color into a copy if the mask is enabled:
        if (useMask)
            cmd.CopyTexture(source, colorCopy);

        // Downsample
        using (new ProfilingScope(cmd, new ProfilingSampler("SlightBlur - Downsample")))
        {
            // This Blit will automatically downsample the color because our target buffer have been allocated in half resolution
            HDUtils.BlitCameraTexture(cmd, source, downSampleBuffer, 0);
        }

        // Horizontal Blur
        using (new ProfilingScope(cmd, new ProfilingSampler("SlightBlur - H Blur")))
        {
            var hBlurProperties = new MaterialPropertyBlock();
            if (useTexture)
            {
                hBlurProperties.SetTexture(ShaderID._Texture, texture2D);
                hBlurProperties.SetFloat(ShaderID._TextureWeight, textureWeight);
                hBlurProperties.SetVector(ShaderID._TextureSize, textureSize);
                if (maskOnTextureStrength > 0)
                {
                    hBlurProperties.SetTexture(ShaderID._Mask, maskBuffer);
                    hBlurProperties.SetInt(ShaderID._MaskOnTextureStrength, maskOnTextureStrength);
                }
            }
            hBlurProperties.SetInt(ShaderID._UseTexture, useTexture ? 1 : 0);
            hBlurProperties.SetFloat(ShaderID._Radius, radius / 4.0f);      // The blur is 4 pixel wide in the shader
            hBlurProperties.SetTexture(ShaderID._Source, downSampleBuffer); // The blur is 4 pixel wide in the shader
            SetViewPortSize(cmd, hBlurProperties, blurBuffer);
            HDUtils.DrawFullScreen(cmd, blurMaterial, blurBuffer, hBlurProperties, shaderPassId: 0); // Do not forget the shaderPassId: ! or it won't work
        }

        // Copy back the result in the color buffer while doing a vertical blur
        using (new ProfilingScope(cmd, new ProfilingSampler("SlightBlur - V Blur + Copy Back")))
        {
            var vBlurProperties = new MaterialPropertyBlock();
            // When we use a mask, we do the vertical blur into the downsampling buffer instead of the camera buffer
            // We need that because we're going to write to the color buffer and read from this blured buffer which we can't do
            // if they are in the same buffer
            if (useTexture)
            {
                vBlurProperties.SetTexture(ShaderID._Texture, texture2D);
                vBlurProperties.SetFloat(ShaderID._TextureWeight, textureWeight);
                vBlurProperties.SetVector(ShaderID._TextureSize, textureSize);
                if (maskOnTextureStrength > 0)
                {
                    vBlurProperties.SetTexture(ShaderID._Mask, maskBuffer);
                    vBlurProperties.SetInt(ShaderID._MaskOnTextureStrength, maskOnTextureStrength);
                }
            }
            vBlurProperties.SetInt(ShaderID._UseTexture, useTexture ? 1 : 0);
            vBlurProperties.SetFloat(ShaderID._Radius, radius / 4.0f); // The blur is 4 pixel wide in the shader
            vBlurProperties.SetTexture(ShaderID._Source, blurBuffer);
            var targetBuffer = (useMask) ? downSampleBuffer : source;
            SetViewPortSize(cmd, vBlurProperties, targetBuffer);
            HDUtils.DrawFullScreen(cmd, blurMaterial, targetBuffer, vBlurProperties, shaderPassId: 1); // Do not forget the shaderPassId: ! or it won't work
        }

        if (useMask)
        {
            // Merge the non blur copy and the blurred version using the mask buffers
            using (new ProfilingScope(cmd, new ProfilingSampler("SlightBlur - Compose Mask Blur")))
            {
                var compositingProperties = new MaterialPropertyBlock();

                compositingProperties.SetFloat(ShaderID._Radius, radius / 4.0f); // The blur is 4 pixel wide in the shader
                compositingProperties.SetTexture(ShaderID._Source, downSampleBuffer);
                compositingProperties.SetTexture(ShaderID._ColorBufferCopy, colorCopy);
                compositingProperties.SetTexture(ShaderID._Mask, maskBuffer);
                compositingProperties.SetFloat(ShaderID._InvertMask, invertMask ? 1 : 0);
                if (useTexture)
                {
                    compositingProperties.SetTexture(ShaderID._Texture, texture2D);
                    compositingProperties.SetFloat(ShaderID._TextureWeight, textureWeight);
                    compositingProperties.SetVector(ShaderID._TextureSize, textureSize);
                    compositingProperties.SetInt(ShaderID._MaskOnTextureStrength, maskOnTextureStrength);
                }
                compositingProperties.SetInt(ShaderID._UseTexture, useTexture ? 1 : 0);
                compositingProperties.SetColor(ShaderID._ColorAdd, colorAdd);
                compositingProperties.SetColor(ShaderID._ColorMult, colorMultiply);
                compositingProperties.SetColor(ShaderID._BackingColor, backingColor);
                compositingProperties.SetFloat(ShaderID._MinimumLightness, minimumLightness);
                SetViewPortSize(cmd, compositingProperties, source);
                HDUtils.DrawFullScreen(cmd, blurMaterial, source, compositingProperties, shaderPassId: 2); // Do not forget the shaderPassId: ! or it won't work
            }
        }
    }

    // release all resources
    protected override void Cleanup()
    {
        CoreUtils.Destroy(blurMaterial);
        downSampleBuffer.Release();
        blurBuffer.Release();
        maskBuffer?.Release();
        colorCopy?.Release();
    }
}
}