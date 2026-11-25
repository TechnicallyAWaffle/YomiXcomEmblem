// The tactical view (Alt in battle, TacticalView): terrain and models drawn see-through, their edges solid.
//   Faces: _GhostColor at its alpha, a little stronger toward the silhouette (_Rim), no depth writes, so everything
//          behind shows through.
//   Edges: the same shader on a line mesh of the model's hard edges (_Rim 0, alpha 1).
Shader "Apotheom/TacticalGhost"
{
    Properties
    {
        _GhostColor ("Colour (alpha: how solid)", Color) = (1,1,1,0.12)
        _Rim ("Silhouette strength", Range(0,1)) = 0.25
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest LEqual
        Cull Back

        Pass
        {
            Name "Ghost"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _GhostColor;
                half _Rim;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 viewWS : TEXCOORD1; };

            Varyings vert (Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(posWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.viewWS = GetWorldSpaceViewDir(posWS);
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                half facing = abs(dot(normalize(i.normalWS), normalize(i.viewWS)));
                half rim = pow(saturate(1.0h - facing), 3.0h) * _Rim;
                return half4(_GhostColor.rgb, saturate(_GhostColor.a + rim));
            }
            ENDHLSL
        }
    }
}
