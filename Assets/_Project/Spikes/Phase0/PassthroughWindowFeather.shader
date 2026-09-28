// Passthrough Window (brief §15.3) com borda suave (feather) via SDF de retângulo arredondado.
// Blend Zero SrcAlpha: destino *= alpha. Onde alpha → 0 o eye buffer fica transparente e o
// passthrough (underlay) aparece. Cada face do cubo usa o próprio UV 0..1 para o SDF.
Shader "ScreenPortal/PassthroughWindowFeather"
{
    Properties
    {
        _FaceSizeMeters ("Face size (m) — preenchido pelo script", Vector) = (0.5, 0.3, 0, 0)
        _FeatherMeters ("Feather (m)", Range(0, 0.08)) = 0.02
        _CornerRadiusMeters ("Corner radius (m)", Range(0, 0.1)) = 0.02
        _Opacity ("Opacity (passthrough strength)", Range(0.3, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+2000" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            ZWrite Off
            ZTest LEqual
            Cull Back
            BlendOp Add
            Blend Zero SrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _FaceSizeMeters;
                float _FeatherMeters;
                float _CornerRadiusMeters;
                float _Opacity;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            // Distância assinada ao retângulo arredondado (negativa dentro).
            float sdRoundedBox(float2 p, float2 halfSize, float r)
            {
                float2 q = abs(p) - halfSize + r;
                return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 size = _FaceSizeMeters.xy;
                float2 p = (i.uv - 0.5) * size;
                float d = sdRoundedBox(p, size * 0.5, _CornerRadiusMeters);
                // inside = 1 no miolo, cai para 0 na borda ao longo de _FeatherMeters.
                float inside = saturate(-d / max(_FeatherMeters, 1e-4));
                float passthrough = inside * _Opacity;
                return half4(0, 0, 0, 1.0 - passthrough);
            }
            ENDHLSL
        }
    }
}
