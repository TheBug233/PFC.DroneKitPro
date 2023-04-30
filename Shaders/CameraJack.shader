Shader "Unlit/CameraJack"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        LOD 100

        Pass
        {
			//ZTest Always
			//Cull OFF
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // make fog work
            #pragma multi_compile_fog

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                UNITY_FOG_COORDS(1)
                float4 vertex : SV_POSITION;
                float distance : TEXCOORD1;
            };

            float _VRChatCameraMode;
            float _VRChatMirrorMode;

            sampler2D _MainTex;
            float4 _MainTex_ST;

			bool isVR() {
				// USING_STEREO_MATRICES
				#if UNITY_SINGLE_PASS_STEREO
					return true;
				#else
					return false;
				#endif
			}

			bool isDroneCamera() {
				return !isVR() && _ScreenParams.y == 1084;
			}

			bool IsInMirror(){
                return _VRChatMirrorMode != 0;
            }

            bool JackView(){
                return _VRChatCameraMode != 0;
            }

            v2f vert (appdata v)
            {
                v2f o; 
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.distance = distance(_WorldSpaceCameraPos, mul(unity_ObjectToWorld, float4(0,0,0,1)));
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                UNITY_TRANSFER_FOG(o,o.vertex);
                if (IsInMirror() || isDroneCamera()) { o.vertex=1; }
                else if (JackView() && o.distance < 1) {
                    o.vertex = float4(2*v.uv-1,1,1);
                    o.vertex.y *= -1;
                }
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // sample the texture
                fixed4 col = tex2D(_MainTex, i.uv);
                // apply fog
                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
    }
}
