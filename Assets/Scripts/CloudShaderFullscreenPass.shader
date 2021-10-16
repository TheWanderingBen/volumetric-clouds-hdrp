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
    
    TEXTURE3D(_CloudNoise);
    TEXTURE2D_X(_Source);
    float3 _LightPos;
    float3 _BoundsMin;
    float3 _BoundsMax;
    float3 _CloudOffset;
    float _CloudScale;
    float _DensityThreshold;
    float _DensityMultiplier;
    float _DarknessThreshold;
    float _StepSize;
    float _BlurQuality;
    int _LightAbsorptionTowardSun;

    //blur values:
    TEXTURE2D_X(_ColorBufferCopy);
    half4 _ViewPortSize; // We need the viewport size because we have a non fullscreen render target (blur buffers are downsampled in half res)
    half _Radius;
    
    // Returns (dstToBox, dstInsideBox). If ray misses box, dstInsideBox will be zero)
    float2 rayBoxDst(float3 boundsMin, float3 boundsMax, float3 rayOrigin, float3 rayDir)
    {
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

    float sampleDensity(float3 position)
    {
        float3 uvw = position * _CloudScale * 0.001 + _CloudOffset * 0.01;
        float4 shape = SAMPLE_TEXTURE3D(_CloudNoise, s_linear_repeat_sampler, uvw);
        float density = max(0, shape.r - _DensityThreshold) * _DensityMultiplier;
        return density;
    }

    // Calculate proportion of light that reaches the given point from the lightsource
    float lightmarch(float3 position)
    {
        float3 dirToLight = _LightPos.xyz - position;
        float dstInsideBox = rayBoxDst(_BoundsMin, _BoundsMax, position, dirToLight).y;
        
        float stepSize = dstInsideBox/5;
        float totalDensity = 0;

        [loop] for (int step = 0; step < 5; ++step)
        {
            position += dirToLight * stepSize;
            totalDensity += max(0, sampleDensity(position) * stepSize);
        } 

        float transmittance = exp(-totalDensity * _LightAbsorptionTowardSun);
        return _DarknessThreshold + transmittance * (1-_DarknessThreshold);
    }

    float3 blurPixels(float3 taps[9])
    {
        return 0.27343750 * (taps[4]          )
             + 0.21875000 * (taps[3] + taps[5])
             + 0.10937500 * (taps[2] + taps[6])
             + 0.03125000 * (taps[1] + taps[7])
             + 0.00390625 * (taps[0] + taps[8]);
    }

    // We need to clamp the UVs to avoid bleeding from bigger render tragets (when we have multiple cameras)
    half2 clampUVs(float2 uv)
    {
        uv = clamp(uv, 0, _RTHandleScale - _ScreenSize.zw * 2); // clamp UV to 1 pixel to avoid bleeding
        return uv;
    }

    half4 DrawCloud(Varyings varyings) : SV_Target
    {       
        float depth = LoadCameraDepth(varyings.positionCS.xy / _BlurQuality);
        PositionInputs posInput = GetPositionInput(varyings.positionCS.xy, _ScreenSize.zw / _BlurQuality, depth, UNITY_MATRIX_I_VP, UNITY_MATRIX_V);
        float2 uv = posInput.positionNDC.xy * _RTHandleScale.xy;

        float3 rayPos = _WorldSpaceCameraPos;
        float3 rayDir = normalize(posInput.positionWS);

        float2 rayBoxInfo = rayBoxDst(_BoundsMin, _BoundsMax, rayPos, rayDir);
        float dstToBox = rayBoxInfo.x;
        float dstInsideBox = rayBoxInfo.y;

        float dstTraveled = 0;
        float stepSize = _StepSize;
        float dstLimit = min(posInput.linearDepth - dstToBox, dstInsideBox);
        
        // point of intersection with the cloud container
        float3 entryPoint = rayPos + rayDir * dstToBox;
        
        float transmittance = 1;
        float lightEnergy = 0;
        
        [loop] while (dstTraveled < dstLimit)
        {
            rayPos = entryPoint + rayDir * dstTraveled;
            float density = sampleDensity(rayPos);
            
            if (density > 0)
            {
                float lightTransmittance = lightmarch(rayPos);                
                lightEnergy += density * stepSize * transmittance * lightTransmittance;
                
                transmittance *= exp(-density * stepSize);
            
                // Exit early if T is close to zero as further samples won't affect the result much
                if (transmittance < 0.01)
                    break;
            }
            
            dstTraveled += stepSize;
        }
        
        float4 color = float4(transmittance + lightEnergy, 0, 1 - transmittance, 1);
                
        return color;
    }

    half4 HorizontalBlur(Varyings varyings) : SV_Target
    {        
        half depth = LoadCameraDepth(varyings.positionCS.xy);
        PositionInputs posInput = GetPositionInput(varyings.positionCS.xy, _ViewPortSize.zw / _BlurQuality, depth, UNITY_MATRIX_I_VP, UNITY_MATRIX_V);
            
        half2 texcoord = posInput.positionNDC.xy * _RTHandleScale.xy;
        half texOffsetX = 0;
        
        // Horizontal blur from the camera color buffer
        half2 offset = _ScreenSize.zw * _Radius; // We don't use _ViewPortSize here because we want the offset to be the same between all the blur passes.
        float3 taps[9];
        for (int i = -4; i <= 4; i++)
        {
            half2 uv = clampUVs(texcoord + half2(i, 0) * (offset + texOffsetX));
            taps[i + 4] = SAMPLE_TEXTURE2D_X_LOD(_Source, s_linear_clamp_sampler, uv, 0).rgb;
        }

        return half4(blurPixels(taps), 1);
    }
 
    half4 VerticalBlur(Varyings varyings) : SV_Target
    {        
        half depth = LoadCameraDepth(varyings.positionCS.xy);
        PositionInputs posInput = GetPositionInput(varyings.positionCS.xy, _ViewPortSize.zw / _BlurQuality, depth, UNITY_MATRIX_I_VP, UNITY_MATRIX_V);
            
        half2 texcoord = posInput.positionNDC.xy * _RTHandleScale.xy;
        half texOffsetY = 0;
        
        half2 offset = _ScreenSize.zw * _Radius;
        float3 taps[9];
        for (int i = -4; i <= 4; i++)
        {
            half2 uv = clampUVs(texcoord + half2(0, i) * (offset + texOffsetY));
            taps[i + 4] = SAMPLE_TEXTURE2D_X_LOD(_Source, s_linear_clamp_sampler, uv, 0).rgb;
        }

        return half4(blurPixels(taps), 1);
    }
    
    half4 Composite(Varyings varyings) : SV_Target
    {
        float depth = LoadCameraDepth(varyings.positionCS.xy);
        PositionInputs posInput = GetPositionInput(varyings.positionCS.xy, _ViewPortSize.zw, depth, UNITY_MATRIX_I_VP, UNITY_MATRIX_V);
        half2 uv = clampUVs(posInput.positionNDC.xy * _RTHandleScale.xy);

        half4 colorBuffer = SAMPLE_TEXTURE2D_X_LOD(_ColorBufferCopy, s_linear_clamp_sampler, uv, 0).rgba;        
        half4 blurredCloudBuffer = SAMPLE_TEXTURE2D_X_LOD(_Source, s_linear_clamp_sampler, uv, 0).rgba;
        
        return half4(lerp(colorBuffer.rgb, blurredCloudBuffer.rrr, min(blurredCloudBuffer.b, 1)), 1);
    }

    ENDHLSL

    SubShader
    {        
        Pass
        {
            Name "Draw Clouds"

            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off

            HLSLPROGRAM
                #pragma fragment DrawCloud
            ENDHLSL
        }
        
        Pass
        {
            // Horizontal Blur of clouds
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
            // Vertical Blur of clouds
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
            Name "Composite Clouds and Color"

            ZWrite Off
            ZTest Always
            Blend Off
            Cull Off

            HLSLPROGRAM
                #pragma fragment Composite
            ENDHLSL
        }
    }
    Fallback Off
}