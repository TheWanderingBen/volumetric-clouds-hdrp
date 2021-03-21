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
    float3 _LightPos;
    float3 _BoundsMin;
    float3 _BoundsMax;
    float3 _CloudOffset;
    float _CloudScale;
    float _DensityThreshold;
    float _DensityMultiplier;
    float _DarknessThreshold;
    float _StepSize;
    int _LightAbsorptionTowardSun;
    
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

    half4 DrawCloud(Varyings varyings) : SV_Target
    {       
        float depth = LoadCameraDepth(varyings.positionCS.xy);
        PositionInputs posInput = GetPositionInput(varyings.positionCS.xy, _ScreenSize.zw, depth, UNITY_MATRIX_I_VP, UNITY_MATRIX_V);
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
        float3 lightEnergy = 0;
        
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
        
        float4 color = float4(transmittance + lightEnergy, 1 - transmittance.x);
                
        return color;
    }

    ENDHLSL

    SubShader
    {        
        Pass
        {
            Name "Custom Pass"

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