Shader "Hidden/FullScreen/CloudShader"
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
    #pragma enable_d3d11_debug_symbols
    
    TEXTURE2D_X(_CameraBuffer);
    float3 _BoundsMin;
    float3 _BoundsMax;
    
    // Returns (dstToBox, dstInsideBox). If ray misses box, dstInsideBox will be zero)
    float2 rayBoxDst(float3 boundsMin, float3 boundsMax, float3 rayOrigin, float3 rayDir) {
        // From http://jcgt.org/published/0007/03/04/
        // via https://medium.com/@bromanz/another-view-on-the-classic-ray-aabb-intersection-algorithm-for-bvh-traversal-41125138b525
        float3 t0 = (boundsMin - rayOrigin) / rayDir;
        float3 t1 = (boundsMax - rayOrigin) / rayDir;
        float3 tmin = min(t0, t1);
        float3 tmax = max(t0, t1);
        
        float dstA = max(max(tmin.x, tmin.y), tmin.z);
        float dstB = min(tmax.x, min(tmax.y, tmax.z));

        // CASE 1: ray intersects box from outside (0 <= dstA <= dstB)
        // dstA is dst to nearest intersection, dstB dst to far intersection

        // CASE 2: ray intersects box from inside (dstA < 0 < dstB)
        // dstA is the dst to intersection behind the ray, dstB is dst to forward intersection

        // CASE 3: ray misses box (dstA > dstB)

        float dstToBox = max(0, dstA);
        float dstInsideBox = max(0, dstB - dstToBox);
        return float2(dstToBox, dstInsideBox);
    }

    half4 StandardTexture(Varyings varyings) : SV_Target
    {       
        float depth = LoadCameraDepth(varyings.positionCS.xy);
        PositionInputs posInput = GetPositionInput(varyings.positionCS.xy, _ScreenSize.zw, depth, UNITY_MATRIX_I_VP, UNITY_MATRIX_V);
        
        float4 color = float4(CustomPassSampleCameraColor(posInput.positionNDC.xy, 0), 1);
                
        return float4(color.rgb, 1);
    }

    half4 DrawCloud(Varyings varyings) : SV_Target
    {       
        float depth = LoadCameraDepth(varyings.positionCS.xy);
        PositionInputs posInput = GetPositionInput(varyings.positionCS.xy, _ScreenSize.zw, depth, UNITY_MATRIX_I_VP, UNITY_MATRIX_V);
        float2 uv = posInput.positionNDC.xy * _RTHandleScale.xy;

        float3 rayOrigin = _WorldSpaceCameraPos + posInput.positionWS;
        float3 rayDir = normalize(GetViewForwardDir());

        float2 rayBoxInfo = rayBoxDst(_BoundsMin, _BoundsMax, rayOrigin, rayDir);
        float dstToBox = rayBoxInfo.x;
        float dstInsideBox = rayBoxInfo.y;

        float4 color;
        if (dstInsideBox > 0 && dstToBox < posInput.deviceDepth)
            color = float4(0,0,1,1);
        else
            color = float4(SAMPLE_TEXTURE2D_X_LOD(_CameraBuffer, s_linear_clamp_sampler, uv, 0).r, 0, 0, 1);
                
        return color;
    }

    ENDHLSL

    SubShader
    {
        Pass
        {
            Name "Custom Pass 0"

            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off

            HLSLPROGRAM
                #pragma fragment StandardTexture
            ENDHLSL
        }
        
        Pass
        {
            Name "Custom Pass 1"

            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off

            HLSLPROGRAM
                #pragma fragment DrawCloud
            ENDHLSL
        }
    }
    Fallback Off
}