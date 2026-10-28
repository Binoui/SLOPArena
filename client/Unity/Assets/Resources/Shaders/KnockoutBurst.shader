Shader "SLOPArena/Knockout Burst"
{
    // Procedural print shapes for the KO flyer burst (KnockoutBurst builds the quads at the
    // blast-line crossing). Each camera places the burst itself: a crossing behind it is reflected
    // in front, the burst keeps a minimum on-screen size and is pinned inside the screen edge, and
    // the trail points at the middle of the screen, where the camera frames the fight. Quads turn
    // in the screen plane. Kind 0 is the starburst: an ink shadow, an out-of-register yellow copy,
    // the Orange star with an ink rim and a Paper core. Kind 1 is the halftone trail, kind 2 a
    // paper scrap with an ink rim and shadow. Shadows fall toward the bottom right of the screen.
    // Drawn over the world so a KO reads past stage geometry.
    Properties
    {
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+14" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            static const half3 Ink = half3(0.106, 0.102, 0.094);
            static const half3 Yellow = half3(1.0, 0.8, 0.133);
            static const half3 Paper = half3(0.871, 0.847, 0.788);
            static const int StarPoints = 11;
            // KnockoutBurst.BurstRadius (m), its on-screen radius limits as fractions of the screen
            // height, and its reach over that radius (peak pop 1.15 × longest spike 1.28).
            static const float BurstRadius = 1.5;
            static const float MinScreenRadius = 0.08, MaxScreenRadius = 0.13;
            static const float EdgeReach = 1.15 * 1.28;

            struct Attributes
            {
                float4 positionOS : POSITION;   // the blast-line crossing
                half4 color : COLOR;
                float2 uv : TEXCOORD0;          // the corner
                float4 shape : TEXCOORD1;       // half width, half height (m), screen rotation, kind
                float4 data : TEXCOORD2;        // burst: star angle, seed; trail: grow, fade
                float2 offset : TEXCOORD3;      // the quad's centre from the burst's (m, screen plane)
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 local : TEXCOORD0;       // metres from the quad's centre, in its own axes
                float4 shape : TEXCOORD1;
                float4 data : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 origin = TransformWorldToView(TransformObjectToWorld(input.positionOS.xyz));
                // View space looks down -z. Reflect a crossing behind the camera to the front.
                if (-origin.z < _ProjectionParams.y + 1.0)
                    origin.z = -max(abs(origin.z), 6.0);
                origin *= min(1.0, _ProjectionParams.z * 0.5 / -origin.z);
                float depth = -origin.z;
                // Render-texture projections may flip y; sizes use the magnitudes.
                float2 focal = abs(float2(UNITY_MATRIX_P[0][0], UNITY_MATRIX_P[1][1]));
                float screenHeight = 2.0 * depth / focal.y;
                float radius = clamp(BurstRadius, MinScreenRadius * screenHeight, MaxScreenRadius * screenHeight);
                float scale = radius / BurstRadius;
                float2 halfView = max(depth / focal - EdgeReach * radius, 0.0);
                origin.xy = clamp(origin.xy, -halfView, halfView);

                float2 offset = input.offset;
                float rotation = input.shape.z;
                if (abs(input.shape.w - 1.0) < 0.5)
                {
                    float2 toFight = dot(origin.xy, origin.xy) > 1e-4 ? normalize(-origin.xy) : float2(0.0, -1.0);
                    offset = toFight * input.shape.x;
                    rotation = atan2(toFight.y, toFight.x);
                }
                float2 local = (input.uv * 2.0 - 1.0) * input.shape.xy;
                float s, c;
                sincos(rotation, s, c);
                float2 screen = offset + float2(local.x * c - local.y * s, local.x * s + local.y * c);
                output.positionCS = TransformWViewToHClip(origin + float3(screen * scale, 0.0));
                output.color = input.color;
                output.local = local;
                output.shape = float4(input.shape.xy, rotation, input.shape.w);
                output.data = input.data;
                return output;
            }

            float Hash(float n) { return frac(sin(n) * 43758.5453); }

            float2 StarVertex(int i, float angle, float seed, float outer, float inner)
            {
                float a = angle + PI * i / StarPoints;
                float jitter = Hash(seed + i * 7.13) * 2.0 - 1.0;
                float r = (i & 1) == 0 ? outer * (1.0 + 0.28 * jitter) : inner * (1.0 + 0.15 * jitter);
                return r * float2(cos(a), sin(a));
            }

            // Signed distance to a jagged star polygon (negative inside).
            float StarDistance(float2 p, float angle, float seed, float outer, float inner)
            {
                float d = 1e5;
                float side = 1.0;
                float2 previous = StarVertex(2 * StarPoints - 1, angle, seed, outer, inner);
                for (int i = 0; i < 2 * StarPoints; i++)
                {
                    float2 current = StarVertex(i, angle, seed, outer, inner);
                    float2 e = previous - current;
                    float2 w = p - current;
                    float2 b = w - e * saturate(dot(w, e) / dot(e, e));
                    d = min(d, dot(b, b));
                    bool3 crossing = bool3(p.y >= current.y, p.y < previous.y, e.x * w.y > e.y * w.x);
                    if (all(crossing) || !any(crossing)) side = -side;
                    previous = current;
                }
                return side * sqrt(d);
            }

            float BoxDistance(float2 p, float2 size)
            {
                float2 q = abs(p) - size;
                return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0);
            }

            float Fill(float d)
            {
                float aa = max(fwidth(d), 1e-4);
                return 1.0 - smoothstep(-aa, aa, d);
            }

            void Over(inout half4 result, half3 color, float coverage)
            {
                float alpha = coverage + result.a * (1.0 - coverage);
                result.rgb = (color * coverage + result.rgb * result.a * (1.0 - coverage)) / max(alpha, 1e-4);
                result.a = alpha;
            }

            half4 Burst(Varyings input)
            {
                float radius = max(input.shape.y / 1.3, 1e-4);
                float2 p = input.local / radius;
                float angle = input.data.x, seed = input.data.y;
                float main = StarDistance(p, angle, seed, 1.0, 0.45);
                float core = StarDistance(p, angle + 0.35, seed + 17.0, 0.52, 0.26);
                half4 result = 0;
                Over(result, Ink, Fill(StarDistance(p - float2(0.11, -0.11), angle, seed, 1.0, 0.45)));
                Over(result, Yellow, Fill(StarDistance(p - float2(-0.07, 0.05), angle, seed, 1.0, 0.45)));
                Over(result, Ink, Fill(main));
                Over(result, input.color.rgb, Fill(main + 0.05));
                Over(result, Ink, Fill(core));
                Over(result, Paper, Fill(core + 0.04));
                return result;
            }

            half4 Trail(Varyings input)
            {
                // x runs from the burst (0) out along the trail; rows alternate ink and seat colour.
                float cell = input.shape.y * 0.36;
                float2 q = float2(input.local.x + input.shape.x, input.local.y) / cell;
                float row = floor(q.y + 0.5);
                float shift = abs(row) % 2.0 * 0.5;
                float2 centre = float2(floor(q.x - shift) + 0.5 + shift, row);
                float along = centre.x * cell / (2.0 * input.shape.x);
                float halfRows = lerp(0.2, 1.0, along) * input.shape.y / cell;
                float grow = input.data.x, fade = input.data.y;
                float dotRadius = 0.46 * (1.0 - 0.75 * along) * (1.0 - fade);
                float coverage = Fill((length(q - centre) - dotRadius) * cell);
                bool shown = abs(row) <= halfRows && along <= grow && along <= 1.0 && dotRadius > 0.0;
                half3 color = abs(row) % 2.0 < 0.5 ? Ink : input.color.rgb;
                return half4(color, shown ? coverage : 0.0);
            }

            half4 Scrap(Varyings input)
            {
                float2 size = input.shape.xy / 1.4;
                float rim = 0.3 * min(size.x, size.y);
                // The shadow falls toward the screen's bottom right, turned into the scrap's axes.
                float s, c;
                sincos(input.shape.z, s, c);
                float2 drop = float2(1.0, -1.0) * (0.45 * size.y);
                float2 shadow = float2(c * drop.x + s * drop.y, -s * drop.x + c * drop.y);
                float body = BoxDistance(input.local, size);
                half4 result = 0;
                Over(result, Ink, Fill(BoxDistance(input.local - shadow, size)));
                Over(result, Ink, Fill(body));
                Over(result, input.color.rgb, Fill(body + rim));
                return result;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float kind = input.shape.w;
                if (kind < 0.5) return Burst(input);
                if (kind < 1.5) return Trail(input);
                return Scrap(input);
            }
            ENDHLSL
        }
    }
}
