Shader "Hidden/FullScreen/Blur"
{
    HLSLINCLUDE

    #pragma vertex Vert

    #pragma target 4.5
    #pragma only_renderers d3d11 ps4 xboxone vulkan metal switch
    #pragma enable_d3d11_debug_symbols

    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/RenderPass/CustomPass/CustomPassCommon.hlsl"

    // The PositionInputs struct allow you to retrieve a lot of useful information for your fullScreenShader:
    // struct PositionInputs
    // {
    //     float3 positionWS;  // World space position (could be camera-relative)
    //     float2 positionNDC; // Normalized screen coordinates within the viewport    : [0, 1) (with the half-pixel offset)
    //     uint2  positionSS;  // Screen space pixel coordinates                       : [0, NumPixels)
    //     uint2  tileCoord;   // Screen tile coordinates                              : [0, NumTiles)
    //     float  deviceDepth; // Depth from the depth buffer                          : [0, 1] (typically reversed)
    //     float  linearDepth; // View space Z coordinate                              : [Near, Far]
    // };

    // To sample custom buffers, you have access to these functions:
    // But be careful, on most platforms you can't sample to the bound color buffer. It means that you
    // can't use the SampleCustomColor when the pass color buffer is set to custom (and same for camera the buffer).
    // float3 SampleCustomColor(float2 uv);
    // float3 LoadCustomColor(uint2 pixelCoords);
    // float LoadCustomDepth(uint2 pixelCoords);
    // float SampleCustomDepth(float2 uv);

    // There are also a lot of utility function you can use inside Common.hlsl and Color.hlsl,
    // you can check them out in the source code of the core SRP package.

    int _UseTexture;
    Texture2D _Texture;
    half4 _ColorAdd;
    half4 _ColorMult;
    half4 _BackingColor;
    int _MaskOnTextureStrength;
    half _MinimumLightness;

    TEXTURE2D_X(_Source);
    TEXTURE2D_X(_ColorBufferCopy);
    TEXTURE2D_X_HALF(_Mask);
    half _Radius;
    half _TextureWeight;
    half2 _TextureSize;
    half _InvertMask;
    half4 _ViewPortSize; // We need the viewport size because we have a non fullscreen render target (blur buffers are downsampled in half res)

    #pragma enable_d3d11_debug_symbols

    float3 BlurPixels(float3 taps[9])
    {
        return 0.27343750 * (taps[4]          )
             + 0.21875000 * (taps[3] + taps[5])
             + 0.10937500 * (taps[2] + taps[6])
             + 0.03125000 * (taps[1] + taps[7])
             + 0.00390625 * (taps[0] + taps[8]);
    }

    // We need to clamp the UVs to avoid bleeding from bigger render tragets (when we have multiple cameras)
    half2 ClampUVs(float2 uv)
    {
        uv = clamp(uv, 0, _RTHandleScale - _ScreenSize.zw * 2); // clamp UV to 1 pixel to avoid bleeding
        return uv;
    }

    half4 HorizontalBlur(Varyings varyings) : SV_Target
    {
        
        half depth = LoadCameraDepth(varyings.positionCS.xy);
        PositionInputs posInput = GetPositionInput(varyings.positionCS.xy, _ViewPortSize.zw, depth, UNITY_MATRIX_I_VP, UNITY_MATRIX_V);
            
        half2 texcoord = posInput.positionNDC.xy * _RTHandleScale.xy;
        half texOffsetX = 0;
        if (_UseTexture == 1)
        {
            half2 texoffsetCoord = posInput.positionSS / _TextureSize;
            texOffsetX = (SAMPLE_TEXTURE2D( _Texture, s_trilinear_repeat_sampler, texoffsetCoord).r + SAMPLE_TEXTURE2D( _Texture, s_trilinear_repeat_sampler, texoffsetCoord).g) * _TextureWeight;
            if (_MaskOnTextureStrength > 0)
            {
                half4 mask = SAMPLE_TEXTURE2D_X_LOD(_Mask, s_linear_clamp_sampler, texcoord, 0);
                texOffsetX *= pow(mask.r, _MaskOnTextureStrength);
            }
        }

        // Horizontal blur from the camera color buffer
        half2 offset = _ScreenSize.zw * _Radius; // We don't use _ViewPortSize here because we want the offset to be the same between all the blur passes.
        float3 taps[9];
        for (int i = -4; i <= 4; i++)
        {
            half2 uv = ClampUVs(texcoord + half2(i, 0) * (offset + texOffsetX));
            taps[i + 4] = SAMPLE_TEXTURE2D_X_LOD(_Source, s_linear_clamp_sampler, uv, 0).rgb;
        }

        return half4(BlurPixels(taps), 1);
    }
 
    half4 VerticalBlur(Varyings varyings) : SV_Target
    {        
        half depth = LoadCameraDepth(varyings.positionCS.xy);
        PositionInputs posInput = GetPositionInput(varyings.positionCS.xy, _ViewPortSize.zw, depth, UNITY_MATRIX_I_VP, UNITY_MATRIX_V);
            
        half2 texcoord = posInput.positionNDC.xy * _RTHandleScale.xy;
        half texOffsetY = 0;
        if (_UseTexture == 1)
        {
            half2 texoffsetCoord = posInput.positionSS / _TextureSize;
            texOffsetY = (SAMPLE_TEXTURE2D( _Texture, s_trilinear_repeat_sampler, texoffsetCoord).b + SAMPLE_TEXTURE2D( _Texture, s_trilinear_repeat_sampler, texoffsetCoord).g) * _TextureWeight;
            if (_MaskOnTextureStrength > 0)
            {
                half4 mask = SAMPLE_TEXTURE2D_X_LOD(_Mask, s_linear_clamp_sampler, texcoord, 0);
                texOffsetY *= pow(mask.r, _MaskOnTextureStrength);
            }
        }
        
        half2 offset = _ScreenSize.zw * _Radius;
        float3 taps[9];
        for (int i = -4; i <= 4; i++)
        {
            half2 uv = ClampUVs(texcoord + half2(0, i) * (offset + texOffsetY));
            taps[i + 4] = SAMPLE_TEXTURE2D_X_LOD(_Source, s_linear_clamp_sampler, uv, 0).rgb;
        }

        return half4(BlurPixels(taps), 1);
    }

    half4 CompositeMaskedBlur(Varyings varyings) : SV_Target
    {
        float depth = LoadCameraDepth(varyings.positionCS.xy);
        PositionInputs posInput = GetPositionInput(varyings.positionCS.xy, _ViewPortSize.zw, depth, UNITY_MATRIX_I_VP, UNITY_MATRIX_V);
        half2 uv = ClampUVs(posInput.positionNDC.xy * _RTHandleScale.xy);
        half2 texoffsetCoord = posInput.positionSS / _TextureSize;

        half4 colorBuffer = SAMPLE_TEXTURE2D_X_LOD(_ColorBufferCopy, s_linear_clamp_sampler, uv, 0).rgba;
        half4 mask = SAMPLE_TEXTURE2D_X_LOD(_Mask, s_linear_clamp_sampler, uv, 0);
        float maskValue = 0;

        maskValue = mask.b;

        if (_InvertMask > 0.5)
            maskValue = 1.0 - maskValue;
            
        if (maskValue < 1.0)
        {            
            half4 blurredBuffer = SAMPLE_TEXTURE2D_X_LOD(_Source, s_linear_clamp_sampler, uv, 0).rgba;
            half texColor = _UseTexture == 1 ? SAMPLE_TEXTURE2D( _Texture, s_trilinear_repeat_sampler, texoffsetCoord).g : 1;
            half maskOnTextureStrength = _MaskOnTextureStrength > 0 ? pow(mask.r, _MaskOnTextureStrength) : 1;
    
            half3 blurColor = (_ColorAdd.rgb * _ColorAdd.a * maskOnTextureStrength * texColor + blurredBuffer + _BackingColor.rgb * _BackingColor.a).rgb;
            half lightness = ( blurColor.r + blurColor.b + blurColor.g ) / 3.0; 
            
            if (lightness < _MinimumLightness)
            {
                half boostTotalAmount = _MinimumLightness - lightness;
                half3 boostColors;
                
                boostColors.r = min(blurColor.r + boostTotalAmount / 3.0, 1.0);
                boostTotalAmount -= (boostColors.r - blurColor.r);
                
                boostColors.b = min(blurColor.b + boostTotalAmount / 2.0, 1.0);
                boostTotalAmount -= (boostColors.b - blurColor.b);
                            
                boostColors.g = min(blurColor.g + boostTotalAmount, 1.0);
                
                blurColor = boostColors;
            }
            
            return half4(lerp(blurColor, colorBuffer.rgb, maskValue) * lerp(_ColorMult.rgb, 1.0, maskValue),  colorBuffer.a);
        }
        
        return colorBuffer;
    }

    ENDHLSL

    SubShader
    {
        Pass
        {
            // Horizontal Blur from the camera color LOD
            Name "Horizontal Blur"

            ZWrite Off
            ZTest Always
            Blend Off
            Cull Off

            HLSLPROGRAM
                #pragma fragment HorizontalBlur
            ENDHLSL
        }

        Pass
        {
            // Vertical Blur from the blur buffer back to camera color
            Name "Vertical Blur"

            ZWrite Off
            ZTest Always
            Blend Off
            Cull Off

            HLSLPROGRAM
                #pragma fragment VerticalBlur
            ENDHLSL
        }

        Pass
        {
            // Vertical Blur from the blur buffer back to camera color
            Name "Composite Blur and Color using a mask"

            ZWrite Off
            ZTest Always
            Blend Off
            Cull Off

            HLSLPROGRAM
                #pragma fragment CompositeMaskedBlur
            ENDHLSL
        }
    }
    Fallback Off
}