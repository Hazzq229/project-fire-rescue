Shader "DisasterSquad/CharacterOutline"
{
    Properties
    {
        _OutlineColor("Outline Color", Color) = (1, 0.8, 0, 1)
        _OutlineWidth("Outline Width", Range(0.0001, 0.1)) = 0.005
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Opaque"
            "Queue"          = "Geometry+10"
            "RenderPipeline" = "UniversalPipeline"
        }

        // ── Outline pass (back-face extrusion) ───────────────────────
        Pass
        {
            Name "Outline"
            Cull Front
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _OutlineColor;
                float _OutlineWidth;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings o;

                // Transform normal to clip space for screen-size-consistent outline
                float3 posWS    = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

                // Extrude in world space, then transform to clip
                posWS += normalWS * _OutlineWidth;
                o.positionCS = TransformWorldToHClip(posWS);

                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                return _OutlineColor;
            }
            ENDHLSL
        }
    }
}

