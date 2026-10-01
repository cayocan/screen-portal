// Cursor do PC desenhado no headset (spec 3B §4): textura BGRA com alfa, por cima do furo da tela (ADR 0010). A cor sai
// pré-multiplicada e o alfa do eye buffer vira o alfa do cursor (o compositor mostra o cursor por cima da layer).
// _ZTest: LEqual sobre a tela; Always no mundo (o cursor 3D nunca some atrás de nada).
// Fica em Resources/ para entrar no build e ser achado por Shader.Find (CursorView).
Shader "ScreenPortal/CursorSprite"
{
    Properties
    {
        _MainTex ("Cursor", 2D) = "white" {}
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
    }
    SubShader
    {
        Tags { "Queue"="Transparent+20" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off ZTest [_ZTest]
        Blend One OneMinusSrcAlpha, One OneMinusSrcAlpha

        Pass
        {
            Name "Cursor"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Tint;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * _Tint;
                return half4(c.rgb * c.a, c.a);
            }
            ENDHLSL
        }
    }
}
