// Furo no eye buffer para uma tela em compositor layer Underlay (ADR 0010): escreve cor e alpha 0 e profundidade no
// lugar da tela. O compositor mostra a layer onde o alpha do eye buffer é 0; o que for desenhado depois e na frente
// (moldura, alças, laser, menu) cobre a tela, e o que estiver atrás fica escondido pela profundidade.
// Fica em Resources/ para entrar no build e ser achado por Shader.Find (VideoScreenView).
Shader "ScreenPortal/ScreenHole"
{
    SubShader
    {
        Tags { "Queue"="Geometry" "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZTest LEqual ZWrite On
        Blend Off

        Pass
        {
            Name "Hole"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return half4(0, 0, 0, 0);
            }
            ENDHLSL
        }
    }
}
