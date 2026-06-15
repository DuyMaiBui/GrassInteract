// GPU-Driven Indirect Grass Shader — Phase 5
//
// Reads per-blade data from StructuredBuffers (_Blades, _VisibleIndices) to reconstruct
// each blade's world-space TRS, then applies wind sway + lean-away deform fully in the VS.
// Drawn via Graphics.RenderMeshIndirect: SV_InstanceID indexes _VisibleIndices[instanceID]
// to get the global blade index, then _Blades[bladeIdx] for the BladeInstance record.
//
// Packed format: packedYawScale high16 = yaw quantised 0..65535 over 0..360°
//                              low16  = scale quantised 0..65535 over 0..scaleMax2
//
// Wind: phase = (posWS.x*0.37 + posWS.z*0.21) * windNoiseScale * GRASS_TWO_PI
//        wave = sin(_GrassTime * windFrequency + phase) * windStrength
//        windTilt = windDir.xz * wave                      (XZ lean vector)
//
// Lean-away (interactor loop): for Phase 6 binding; count=0 makes loop a no-op.
//
// LeanRotation (mirrors GrassBendSimulator.LeanRotation exactly):
//   pitchDeg = lean.y * 55        (+Z lean → positive X-euler)
//   rollDeg  = -lean.x * 55       (+X lean → negative Z-euler)
//   clamp combined magnitude to 80°
//
// Billboard (_LOD2_BILLBOARD keyword on LOD2 material): rotate the quad to face the camera in VS.
//
// RENDER-PATH CONTRACT:
//   - Per-LOD buffer (_VisibleIndices) bound via material.SetBuffer (NOT MPB).
//   - Shared globals set via Shader.SetGlobalXxx.
//   - RenderParams.matProps NOT set.
//   - RenderParams.camera = null.
//   - RenderParams.worldBounds = non-zero-extent field AABB.
Shader "WorldPainter/IndirectGrass"
{
    Properties
    {
        _BaseColor ("Base Color",  Color)  = (0.20, 0.45, 0.12, 1)
        _TipColor  ("Tip Color",   Color)  = (0.55, 0.85, 0.30, 1)
        [NoScaleOffset] _BaseMap ("Base Map (optional)", 2D) = "white" {}
        _BaseMap_ST ("Base Map Tiling", Vector) = (1,1,0,0)
        [Toggle(_LOD2_BILLBOARD)] _Lod2Billboard ("LOD2 Billboard", Float) = 0
        // Per-flag deform gates. Default 1 = on (preserves existing always-on grass behavior).
        _WindEnabled        ("Wind Enabled",        Float) = 1
        _InteractorsEnabled ("Interactors Enabled", Float) = 1
        // Phase 2: PBR material properties (all default OFF/null — keyword toggles control activation).
        [Toggle(_NORMALMAP)]  _UseNormalMap  ("Use Normal Map",   Float) = 0
        [NoScaleOffset] _NormalMap ("Normal Map", 2D) = "bump" {}
        _NormalStrength ("Normal Strength", Float) = 1.0
        [Toggle(_PBR)]        _UsePbr        ("Use PBR Lighting", Float) = 0
        [Toggle(_MASKMAP)]    _UseMaskMap    ("Use Mask Map (R=Metallic G=AO A=Smoothness)", Float) = 0
        [NoScaleOffset] _MaskMap ("Mask Map", 2D) = "white" {}
        [Toggle(_EMISSION)]   _UseEmission   ("Use Emission",     Float) = 0
        [NoScaleOffset] _EmissionMap ("Emission Map", 2D) = "black" {}
        [HDR] _EmissionColor ("Emission Color", Color) = (0,0,0,1)
        // Phase 3: shadow properties (all default OFF/0 — keyword-off path == Phase 2 all-OFF baseline).
        [Toggle(_RECEIVE_SHADOWS)] _ReceiveShadows ("Receive Shadows", Float) = 0
        [Toggle(_SHADOW_TINT)]     _UseShadowTint  ("Shadow Tint",     Float) = 0
        _ShadowStrength ("Shadow Strength", Range(0,1)) = 1.0
        _ShadowTint     ("Shadow Tint Color", Color) = (1,1,1,1)
        [Toggle(_ALPHACLIP_SHADOWS)] _AlphaclipShadows ("Alpha-Clip Shadows", Float) = 0
        _Cutoff         ("Shadow Alpha Cutoff", Range(0,1)) = 0.5
        _ShadowDepthBias  ("Shadow Depth Bias",  Float) = 0
        _ShadowNormalBias ("Shadow Normal Bias", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue"          = "Geometry"
        }
        LOD 100
        Cull Off

        // ─────────────────────────────────────────────────────────────────────
        // Shared HLSL include block (inline — no .hlsl file dependency)
        // ─────────────────────────────────────────────────────────────────────

        // ── Pass 0 : UniversalForward ─────────────────────────────────────────
        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            // Keyword: enabled on the LOD2 material instance to billboard the quad toward the camera.
            // multi_compile_local (not shader_feature_local) so both variants are always compiled into
            // the player build — no on-disk material has this keyword set, so shader_feature strips it.
            #pragma multi_compile_local _ _LOD2_BILLBOARD
            // Wind model: _WIND_PERLIN swaps the directional sin() wind for a 2-octave Perlin gust+ripple.
            #pragma multi_compile_local _ _WIND_PERLIN
            // Phase 2: PBR material feature toggles (shader_feature_local — stripped from build when unused).
            // ALL DEFAULT OFF: with none of these defined, the original stylized path runs byte-for-byte unchanged.
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local _PBR
            #pragma shader_feature_local _MASKMAP
            #pragma shader_feature_local _EMISSION
            // Phase 3: shadow feature toggles (shader_feature_local, all default OFF).
            // _RECEIVE_SHADOWS: compute real URP shadow coord + receive shadows (only meaningful with _PBR).
            // _SHADOW_TINT:     modulate received shadow term by _ShadowStrength/_ShadowTint.
            // _ALPHACLIP_SHADOWS: unused in forward pass (applies in ShadowCaster). Declared for parity.
            #pragma shader_feature_local _RECEIVE_SHADOWS
            #pragma shader_feature_local _SHADOW_TINT
            #pragma shader_feature_local _ALPHACLIP_SHADOWS
            // Phase 3: URP shadow sampling keywords (multi_compile so shadow variants are always in build).
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _TipColor;
                float4 _BaseMap_ST;
                // Phase 4: _OrientMode=0 legacy (hash=decorrelation), 1=oriented (hash=octNormal+pitch+roll).
                float  _OrientMode;
                float4 _RotationOffsetEuler; // xyz=euler offset degrees (w unused). Identity when zero.
                // Independent deform gates. Default 1 = on (preserves existing always-on grass behavior).
                float _WindEnabled;
                float _InteractorsEnabled;
                // Phase 2: PBR material uniforms (only read under their respective keywords).
                float  _NormalStrength;
                float4 _EmissionColor;
                // Phase 3: shadow uniforms (only read under their respective keywords; defaults are inert).
                float  _ShadowStrength;   // only under _SHADOW_TINT
                float4 _ShadowTint;       // only under _SHADOW_TINT
                float  _ShadowDepthBias;  // ShadowCaster VS (0 = no-op)
                float  _ShadowNormalBias; // ShadowCaster VS (0 = no-op)
                float  _Cutoff;           // ShadowCaster alpha-clip threshold (0.5 default)
            CBUFFER_END

            TEXTURE2D(_BaseMap);    SAMPLER(sampler_BaseMap);
            TEXTURE2D(_NormalMap);  SAMPLER(sampler_NormalMap);
            // Mask map: R=Metallic, G=Occlusion, B=unused, A=Smoothness (URP Lit convention).
            TEXTURE2D(_MaskMap);    SAMPLER(sampler_MaskMap);
            TEXTURE2D(_EmissionMap); SAMPLER(sampler_EmissionMap);

            // BladeInstance struct — 20 B, must match ChunkedBladeBuffer BLADE_STRIDE.
            // hash: legacy=decorrelation hash; oriented=octNormal(hi16)|pitch8|roll8 (ChunkedBladeBuffer SSOT).
            struct BladeInstance
            {
                float3 posWS;
                uint   packedYawScale;
                uint   hash;
            };

            // Interactor record — 32 B. Phase 6 fills a real buffer; Phase 5 binds 1-element dummy, count=0.
            struct GrassInteractorGpu
            {
                float3 posWS;
                float  radius;
                float  strength;
                float  _pad0;
                float  _pad1;
                float  _pad2;
            };

            StructuredBuffer<BladeInstance>      _Blades;
            StructuredBuffer<uint>               _VisibleIndices; // per-LOD, bound via material.SetBuffer
            StructuredBuffer<GrassInteractorGpu> _Interactors;    // Phase 6; count=0 in Phase 5

            // TRAIL DEFORM BEGIN
            struct GrassTrailSegmentGpu {  // matches C# TrailSegmentGpu, 48 B
                float3 PosA;   float Radius;
                float3 PosB;   float Alpha;
                float  MaxBendRad;
                float  CenterPct;
                float  Strength;
                float  _Pad;
            };
            StructuredBuffer<GrassTrailSegmentGpu> _GrassTrailSegments;
            int                                     _GrassTrailSegmentCount;
            // TRAIL DEFORM END

            float  _ScaleMax2;
            float  _GrassTime;
            float2 _WindDir;
            float  _WindStrength;
            float  _WindFrequency;
            float  _WindNoiseScale;
            // Perlin-mode tunables (used only when _WIND_PERLIN is enabled).
            float  _WindGustScale;
            float  _WindRippleScale;
            float  _WindGustSpeed;
            float  _WindRippleSpeed;
            float  _WindRippleWeight;
            float  _BendStrength;
            float  _Flatten;
            int    _InteractorCount;
            float3 _CamPosWS;

            static const float GRASS_TWO_PI     = 6.2831853;
            static const float DEG_PER_METRE    = 55.0;
            static const float MAX_LEAN_DEGREES = 90.0; // TRAIL DEFORM: lifted from 80 to accommodate combined interactor+trail lean
            static const float DEG_TO_RAD       = 0.01745329;

            // 2D Perlin gradient noise — returns ~[-1, 1]. Hand-rolled because HLSL has no built-in.
            // Hash uses Inigo Quilez-style trig hash; gradients are unit vectors at hashed angles.
            float2 GRASS_PerlinGrad(float2 i)
            {
                float h = frac(sin(dot(i, float2(127.1, 311.7))) * 43758.5453) * GRASS_TWO_PI;
                return float2(cos(h), sin(h));
            }
            float GRASS_Perlin2(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = dot(GRASS_PerlinGrad(i),                f);
                float b = dot(GRASS_PerlinGrad(i + float2(1, 0)), f - float2(1, 0));
                float c = dot(GRASS_PerlinGrad(i + float2(0, 1)), f - float2(0, 1));
                float d = dot(GRASS_PerlinGrad(i + float2(1, 1)), f - float2(1, 1));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y) * 1.41;
            }

            // Build a 3x3 rotation matrix from Euler angles (degrees), ZXY intrinsic convention
            // matching Unity's Quaternion.Euler(pitch, yaw, roll): Ry * Rx * Rz.
            float3x3 EulerToMatrix(float pitchDeg, float yawDeg, float rollDeg)
            {
                float p = pitchDeg * DEG_TO_RAD;
                float y = yawDeg   * DEG_TO_RAD;
                float r = rollDeg  * DEG_TO_RAD;
                float sp = sin(p), cp = cos(p);
                float sy = sin(y), cy = cos(y);
                float sr = sin(r), cr = cos(r);
                // Ry * Rx * Rz
                float3x3 Ry = float3x3( cy, 0, sy,   0, 1,  0,  -sy, 0, cy);
                float3x3 Rx = float3x3(  1, 0,  0,   0, cp,-sp,    0,sp, cp);
                float3x3 Rz = float3x3( cr,-sr,  0,  sr, cr,  0,   0, 0,  1);
                return mul(Ry, mul(Rx, Rz));
            }

            // Decode octahedral normal (Y-up; mirrors ChunkedBladeBuffer.OctDecode in C#).
            float3 GRASS_OctDecode(float px, float py)
            {
                float pz = 1.0 - abs(px) - abs(py);
                if (pz < 0.0) {
                    float ox = px;
                    px = (1.0 - abs(py)) * (px >= 0.0 ? 1.0 : -1.0);
                    py = (1.0 - abs(ox))  * (py >= 0.0 ? 1.0 : -1.0);
                }
                return normalize(float3(px, pz, py)); // px→x, pz→y, py→z
            }

            // Build align rotation: FromToRotation(up, surfaceNormal).
            float3x3 GRASS_AlignMatrix(float3 n)
            {
                float3 up = float3(0,1,0);
                float3 ax = cross(up, n);
                float  s  = length(ax);
                float  c  = dot(up, n);
                if (s < 1e-6) return c > 0.0 ? (float3x3)1 : float3x3(1,0,0, 0,-1,0, 0,0,1);
                ax /= s; float t = 1.0 - c;
                return float3x3(
                    t*ax.x*ax.x+c,        t*ax.x*ax.y-s*ax.z, t*ax.x*ax.z+s*ax.y,
                    t*ax.x*ax.y+s*ax.z,   t*ax.y*ax.y+c,      t*ax.y*ax.z-s*ax.x,
                    t*ax.x*ax.z-s*ax.y,   t*ax.y*ax.z+s*ax.x, t*ax.z*ax.z+c);
            }

            // Build the base rotation from a BladeInstance:
            //   Legacy (_OrientMode < 0.5): yaw about Y + rotation offset.
            //   Oriented (_OrientMode >= 0.5): decode oct+pitch+roll from hash; align(normal)*Euler(p,y,r)*offset.
            // Wind lean is composed ON TOP of this base by the caller: leanRot * baseRot.
            float3x3 GRASS_BaseRotation(BladeInstance b, float yawDeg)
            {
                float3 oe = _RotationOffsetEuler.xyz;
                if (_OrientMode >= 0.5)
                {
                    uint s2 = b.hash;
                    float octX  = (float)((s2 >> 24) & 0xFFu) / 255.0 * 2.0 - 1.0;
                    float octY  = (float)((s2 >> 16) & 0xFFu) / 255.0 * 2.0 - 1.0;
                    float iP    = (float)((s2 >>  8) & 0xFFu) / 255.0 * 180.0 - 90.0;
                    float iR    = (float)( s2        & 0xFFu) / 255.0 * 180.0 - 90.0;
                    float3 sn   = GRASS_OctDecode(octX, octY);
                    return mul(GRASS_AlignMatrix(sn), mul(EulerToMatrix(iP, yawDeg, iR), EulerToMatrix(oe.x, oe.y, oe.z)));
                }
                // Legacy: yaw only + offset (identity when offset=0 → byte-identical to prior).
                return mul(EulerToMatrix(0.0, yawDeg, 0.0), EulerToMatrix(oe.x, oe.y, oe.z));
            }

            // Reconstruct world-space vertex position from a BladeInstance.
            // billboard=true rotates the blade's yaw to face the camera (LOD2 path).
            float3 ReconstructBladeVertexWS(BladeInstance b, float3 localPos, bool billboard)
            {
                // Unpack yaw + scale.
                uint  hi      = (b.packedYawScale >> 16) & 0xFFFFu;
                uint  lo      =  b.packedYawScale & 0xFFFFu;
                float yawDeg  = (float)hi / 65535.0 * 360.0;
                float scaleXZ = (float)lo / 65535.0 * _ScaleMax2;
                float scaleY  = scaleXZ;

                // Wind contribution (independent gate).
                float2 windXZ = float2(0, 0);
                if (_WindEnabled >= 0.5)
                {
                    #ifdef _WIND_PERLIN
                        float2 sampP  = b.posWS.xz;
                        float  gust   = GRASS_Perlin2(sampP * _WindGustScale   - _WindDir * _GrassTime * _WindGustSpeed);
                        float  ripple = GRASS_Perlin2(sampP * _WindRippleScale - _WindDir * _GrassTime * _WindRippleSpeed);
                        float  wave   = (gust + ripple * _WindRippleWeight) * _WindStrength;
                    #else
                        float phase = (b.posWS.x * 0.37 + b.posWS.z * 0.21) * _WindNoiseScale * GRASS_TWO_PI;
                        float wave  = sin(_GrassTime * _WindFrequency + phase) * _WindStrength;
                    #endif
                    windXZ = _WindDir * wave;
                }

                // Interactor lean-away contribution (independent gate).
                float2 bendXZ = float2(0, 0);
                if (_InteractorsEnabled >= 0.5)
                {
                    for (int i = 0; i < _InteractorCount; ++i)
                    {
                        GrassInteractorGpu ip = _Interactors[i];
                        float2 delta = b.posWS.xz - ip.posWS.xz;
                        float  d     = length(delta);
                        if (ip.radius <= 0.0 || d >= ip.radius) continue;
                        float  fall  = 1.0 - d / ip.radius;
                        float2 away  = (d > 1e-4) ? delta / d : float2(0, 0);
                        bendXZ += away * (fall * ip.strength * _BendStrength);
                    }
                }

                // TRAIL DEFORM BEGIN
                {
                    float2 bladeXZ = b.posWS.xz;
                    int n = _GrassTrailSegmentCount;
                    [loop]
                    for (int j = 0; j < n; ++j)
                    {
                        GrassTrailSegmentGpu s = _GrassTrailSegments[j];
                        float2 ab = s.PosB.xz - s.PosA.xz;
                        float  abLenSq = max(dot(ab, ab), 1e-6);
                        float  t  = saturate(dot(bladeXZ - s.PosA.xz, ab) / abLenSq);
                        float2 c  = s.PosA.xz + ab * t;
                        float2 r  = bladeXZ - c;
                        float  d  = length(r);
                        if (d > s.Radius) continue;

                        float dn      = d / s.Radius;
                        float plateau = (dn <= s.CenterPct) ? 1.0
                                       : 1.0 - smoothstep(s.CenterPct, 1.0, dn);

                        // Convert target bend radians → metres-of-push at root using DEG_PER_METRE
                        // (existing pipeline: bendXZ is in metres; pitch/roll = bendXZ.y/-bendXZ.x * DEG_PER_METRE).
                        // To target N degrees of lean, push N / DEG_PER_METRE metres.
                        float angleDeg = degrees(s.MaxBendRad) * plateau * s.Alpha * s.Strength;
                        float pushMetres = angleDeg / DEG_PER_METRE;

                        float2 dir2 = (d > 1e-4) ? (r / d) : float2(1, 0);   // outward from segment
                        bendXZ += dir2 * pushMetres;
                    }
                }
                // TRAIL DEFORM END

                // Flatten by bend magnitude only (not wind).
                float bendMag = length(bendXZ);
                if (_Flatten > 0.0 && _BendStrength > 1e-5)
                    scaleY = scaleXZ * (1.0 - _Flatten * saturate(bendMag / _BendStrength));

                // Lean rotation — mirrors GrassBendSimulator.LeanRotation.
                float2 lean   = windXZ + bendXZ;
                float leanPitch = lean.y * DEG_PER_METRE;
                float leanRoll  = -lean.x * DEG_PER_METRE;
                float magDeg    = sqrt(leanPitch * leanPitch + leanRoll * leanRoll);
                if (magDeg > MAX_LEAN_DEGREES)
                {
                    float s = MAX_LEAN_DEGREES / magDeg;
                    leanPitch *= s;
                    leanRoll  *= s;
                }

                // Billboard: override yaw to face camera (LOD2 only).
                if (billboard)
                {
                    float2 toCam = _CamPosWS.xz - b.posWS.xz;
                    yawDeg = atan2(toCam.x, toCam.y) * (180.0 / 3.14159265);
                }

                // Compose: leanRot * baseRot → scale → translate.
                // baseRot = oriented (align*Euler(p,y,r)*offset) or legacy (yaw+offset).
                // Wind lean is applied on top (leanRot * baseRot), matching CPU GrassBendSimulator:
                //   outSlab[k] = TRS(basePos, lean * yawSlab[k], scale)  where yawSlab[k] = m.rotation.
                float3x3 baseRot = GRASS_BaseRotation(b, yawDeg);
                float3x3 leanMat = EulerToMatrix(leanPitch, 0.0, leanRoll);
                float3x3 rot     = mul(leanMat, baseRot);
                float3 scaled    = float3(localPos.x * scaleXZ, localPos.y * scaleY, localPos.z * scaleXZ);
                return b.posWS + mul(rot, scaled);
            }

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float  heightT    : TEXCOORD1;
                // Phase 2: world-space position and TBN for PBR/normal-map.
                // Always interpolated; only consumed in the _PBR / _NORMALMAP branches.
                // Blade TBN: tangent = blade width axis (rot col 0), normal = blade face normal (rot col 2).
                float3 positionWS : TEXCOORD2;
                float3 normalWS   : TEXCOORD3;
                float3 tangentWS  : TEXCOORD4;
            };

            // Helper: reconstruct the blade's world-space face normal and width-tangent from the
            // same rotation matrix that places blade vertices.
            //
            // The blade quad mesh has local-space face normal = (0,0,1) and width tangent = (1,0,0).
            //   normalWS  = mul(rot, float3(0,0,1)) = third column of rot
            //   tangentWS = mul(rot, float3(1,0,0)) = first column of rot
            //
            // `rot` here is leanMat * baseRot (the same matrix used in ReconstructBladeVertexWS to
            // transform local positions). We rebuild it from the same yaw/orient/lean inputs so the
            // TBN is guaranteed to match the vertex positions.
            void GRASS_BladeWorldTBN(BladeInstance b, out float3 normalWS, out float3 tangentWS)
            {
                uint  hi      = (b.packedYawScale >> 16) & 0xFFFFu;
                uint  lo      =  b.packedYawScale & 0xFFFFu;
                float yawDeg  = (float)hi / 65535.0 * 360.0;

                // Wind/interactor lean is omitted for TBN: normals represent the blade's REST orientation.
                // Per-vertex lean already bends the geometry; recomputing lean per-fragment in the normal map
                // would require re-running the full deform loop in the FS — too expensive.
                // The normal map encodes detail relative to the blade face; lean is macro-scale and is already
                // captured by the geometry's silhouette. TBN from baseRot only is correct for this use case.
                float3x3 rot = GRASS_BaseRotation(b, yawDeg);

                // Blade quad mesh: local face normal = +Z, local width tangent = +X.
                // rot is row-major in HLSL: rot[row][col], so first column is (rot[0].x, rot[1].x, rot[2].x)
                // = mul(rot, float3(1,0,0)) = float3(rot._m00, rot._m10, rot._m20).
                tangentWS = normalize(float3(rot._m00, rot._m10, rot._m20));
                normalWS  = normalize(float3(rot._m02, rot._m12, rot._m22));
            }

            Varyings vert(float4 posOS : POSITION, float2 uv : TEXCOORD0, uint instanceID : SV_InstanceID)
            {
                Varyings o = (Varyings)0;
                uint bladeIdx   = _VisibleIndices[instanceID];
                BladeInstance b = _Blades[bladeIdx];

                #ifdef _LOD2_BILLBOARD
                    float3 posWS = ReconstructBladeVertexWS(b, posOS.xyz, true);
                #else
                    float3 posWS = ReconstructBladeVertexWS(b, posOS.xyz, false);
                #endif

                o.positionCS = TransformWorldToHClip(posWS);
                o.uv         = uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
                o.heightT    = saturate(uv.y);
                o.positionWS = posWS;

                float3 nWS, tWS;
                GRASS_BladeWorldTBN(b, nWS, tWS);
                o.normalWS  = nWS;
                o.tangentWS = tWS;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                half3 baseAlbedo = lerp(_BaseColor.rgb, _TipColor.rgb, i.heightT) * tex.rgb;

                // ── Normal map perturbation (runs before PBR or stylized branch) ──
                float3 normalWS = normalize(i.normalWS);
                #if defined(_NORMALMAP)
                {
                    float3 tangentWS   = normalize(i.tangentWS);
                    float3 bitangentWS = cross(normalWS, tangentWS); // left-hand cross gives correct bi-tangent for blade face
                    half4  nSample     = SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, i.uv);
                    float3 nTS         = UnpackNormal(nSample);
                    nTS.xy *= _NormalStrength;
                    normalWS = normalize(tangentWS * nTS.x + bitangentWS * nTS.y + normalWS * nTS.z);
                }
                #endif

                #if defined(_PBR)
                {
                    // ── URP PBR path ─────────────────────────────────────────────
                    SurfaceData surfaceData = (SurfaceData)0;
                    surfaceData.albedo      = baseAlbedo;
                    surfaceData.normalTS    = float3(0, 0, 1); // unused — we feed world-space normal directly
                    surfaceData.alpha       = 1.0;
                    #if defined(_MASKMAP)
                    {
                        half4 mask          = SAMPLE_TEXTURE2D(_MaskMap, sampler_MaskMap, i.uv);
                        surfaceData.metallic   = mask.r;
                        surfaceData.occlusion  = mask.g;
                        surfaceData.smoothness = mask.a;
                    }
                    #else
                    {
                        surfaceData.metallic   = 0.0;
                        surfaceData.occlusion  = 1.0;
                        surfaceData.smoothness = 0.5;
                    }
                    #endif
                    #if defined(_EMISSION)
                    {
                        half4 emitTex          = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, i.uv);
                        surfaceData.emission   = emitTex.rgb * _EmissionColor.rgb;
                    }
                    #else
                    {
                        surfaceData.emission   = 0.0;
                    }
                    #endif

                    InputData inputData = (InputData)0;
                    inputData.positionWS        = i.positionWS;
                    inputData.normalWS          = normalWS;
                    inputData.viewDirectionWS   = GetWorldSpaceNormalizeViewDir(i.positionWS);
                    // Phase 3: real shadow coord under _RECEIVE_SHADOWS; zero otherwise (no shadow sampling).
                    // OFF path is byte-identical to Phase 2 (shadowCoord = 0 → UniversalFragmentPBR skips shadow).
                    #if defined(_RECEIVE_SHADOWS)
                        inputData.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                    #else
                        inputData.shadowCoord = float4(0, 0, 0, 0);
                    #endif
                    inputData.fogCoord          = 0;
                    inputData.bakedGI           = SampleSH(normalWS);
                    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                    inputData.shadowMask        = half4(1, 1, 1, 1);

                    half4 pbr = UniversalFragmentPBR(inputData, surfaceData);

                    // Phase 3: _SHADOW_TINT — modulate received shadow by strength + tint color.
                    // Only active when _RECEIVE_SHADOWS is also on; adds a colored/attenuated shadow term.
                    #if defined(_SHADOW_TINT) && defined(_RECEIVE_SHADOWS)
                    {
                        Light mainLight = GetMainLight(inputData.shadowCoord);
                        float shadow    = mainLight.shadowAttenuation;
                        // Blend from full shadow tint (shadow=0) to neutral (shadow=1) by strength.
                        half3 tintedShadow = lerp(_ShadowTint.rgb, half3(1,1,1), shadow) * _ShadowStrength
                                            + (1.0 - _ShadowStrength);
                        pbr.rgb *= tintedShadow;
                    }
                    #endif

                    return pbr;
                }
                #else
                {
                    // ── Stylized lighting path ────────────────────────────────
                    // Byte-for-byte identical to pre-Phase-2 when _RECEIVE_SHADOWS is OFF.
                    // When _RECEIVE_SHADOWS is ON, also samples the main-light shadow and
                    // multiplies (or tints) the output — so grass/props darken in shadow on
                    // the cheap stylized path without requiring full PBR.
                    half3 col = baseAlbedo;
                    #if defined(_RECEIVE_SHADOWS)
                    {
                        float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                        Light mainLight    = GetMainLight(shadowCoord);
                        half  shadowAtten  = mainLight.shadowAttenuation;
                        #if defined(_SHADOW_TINT)
                        {
                            // Tint path: lerp toward _ShadowTint in shadow, then modulate by strength.
                            // Matches the PBR path's tint math for visual consistency.
                            half3 tintedShadow = lerp(_ShadowTint.rgb, half3(1,1,1), shadowAtten) * _ShadowStrength
                                                + (1.0 - _ShadowStrength);
                            col *= tintedShadow;
                        }
                        #else
                        {
                            // Simple multiply: darken in shadow proportionally.
                            col *= shadowAtten;
                        }
                        #endif
                    }
                    #endif
                    return half4(col, 1.0);
                }
                #endif
            }
            ENDHLSL
        }

        // ── Pass 1 : ShadowCaster ─────────────────────────────────────────────
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex shadowVert
            #pragma fragment shadowFrag
            #pragma multi_compile _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_local _ _LOD2_BILLBOARD
            #pragma multi_compile_local _ _WIND_PERLIN
            // Phase 3: alpha-clip shadow caster. OFF = solid-quad caster (Phase 2 behavior unchanged).
            #pragma shader_feature_local _ALPHACLIP_SHADOWS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            struct BladeInstance { float3 posWS; uint packedYawScale; uint hash; };
            struct GrassInteractorGpu { float3 posWS; float radius; float strength; float _p0; float _p1; float _p2; };

            // TRAIL DEFORM BEGIN
            struct GrassTrailSegmentGpu {  // matches C# TrailSegmentGpu, 48 B
                float3 PosA;   float Radius;
                float3 PosB;   float Alpha;
                float  MaxBendRad;
                float  CenterPct;
                float  Strength;
                float  _Pad;
            };
            StructuredBuffer<GrassTrailSegmentGpu> _GrassTrailSegments;
            int                                     _GrassTrailSegmentCount;
            // TRAIL DEFORM END

            StructuredBuffer<BladeInstance>      _Blades;
            StructuredBuffer<uint>               _VisibleIndices;
            StructuredBuffer<GrassInteractorGpu> _Interactors;

            float  _ScaleMax2; float _GrassTime; float2 _WindDir; float _WindStrength;
            float  _WindFrequency; float _WindNoiseScale; float _BendStrength; float _Flatten;
            float  _WindGustScale; float _WindRippleScale; float _WindGustSpeed; float _WindRippleSpeed; float _WindRippleWeight;
            int    _InteractorCount; float3 _CamPosWS;
            float  _OrientMode; float4 _RotationOffsetEuler;
            float  _WindEnabled; float _InteractorsEnabled;
            float3 _LightDirection; float3 _LightPosition;
            // Phase 3: shadow bias + alpha-clip uniforms (declared globally; 0 = no-op so today's caster is unchanged).
            float  _ShadowDepthBias;
            float  _ShadowNormalBias;
            float  _Cutoff; // alpha-clip threshold; only sampled under _ALPHACLIP_SHADOWS

            static const float GRASS_TWO_PI=6.2831853, DEG_PER_METRE=55.0, MAX_LEAN_DEGREES=90.0, DEG_TO_RAD=0.01745329; // TRAIL DEFORM: MAX_LEAN_DEGREES lifted 80→90

            float2 GS_PGrad(float2 i){ float h=frac(sin(dot(i,float2(127.1,311.7)))*43758.5453)*GRASS_TWO_PI; return float2(cos(h),sin(h)); }
            float  GS_Perlin2(float2 p){
                float2 i=floor(p),f=frac(p); float2 u=f*f*(3.0-2.0*f);
                float a=dot(GS_PGrad(i),f);
                float b=dot(GS_PGrad(i+float2(1,0)),f-float2(1,0));
                float c=dot(GS_PGrad(i+float2(0,1)),f-float2(0,1));
                float d=dot(GS_PGrad(i+float2(1,1)),f-float2(1,1));
                return lerp(lerp(a,b,u.x),lerp(c,d,u.x),u.y)*1.41;
            }

            float3x3 GS_EulerMat(float p,float y,float r)
            {
                p*=DEG_TO_RAD;y*=DEG_TO_RAD;r*=DEG_TO_RAD;
                float sp=sin(p),cp=cos(p),sy=sin(y),cy=cos(y),sr=sin(r),cr=cos(r);
                float3x3 Ry=float3x3(cy,0,sy,0,1,0,-sy,0,cy);
                float3x3 Rx=float3x3(1,0,0,0,cp,-sp,0,sp,cp);
                float3x3 Rz=float3x3(cr,-sr,0,sr,cr,0,0,0,1);
                return mul(Ry,mul(Rx,Rz));
            }
            float3 GS_OctDec(float px,float py)
            {
                float pz=1.0-abs(px)-abs(py);
                if(pz<0){float ox=px;px=(1-abs(py))*(px>=0?1:-1);py=(1-abs(ox))*(py>=0?1:-1);}
                return normalize(float3(px,pz,py));
            }
            float3x3 GS_AlignMat(float3 n)
            {
                float3 up=float3(0,1,0);float3 ax=cross(up,n);float s=length(ax),c=dot(up,n);
                if(s<1e-6)return c>0?(float3x3)1:float3x3(1,0,0,0,-1,0,0,0,1);
                ax/=s;float t=1-c;
                return float3x3(t*ax.x*ax.x+c,t*ax.x*ax.y-s*ax.z,t*ax.x*ax.z+s*ax.y,
                                t*ax.x*ax.y+s*ax.z,t*ax.y*ax.y+c,t*ax.y*ax.z-s*ax.x,
                                t*ax.x*ax.z-s*ax.y,t*ax.y*ax.z+s*ax.x,t*ax.z*ax.z+c);
            }
            float3x3 GS_BaseRot(BladeInstance b,float yaw)
            {
                float3 oe=_RotationOffsetEuler.xyz;
                if(_OrientMode>=0.5){
                    uint s2=b.hash;
                    float ox=(float)((s2>>24)&0xFFu)/255.0*2.0-1.0,oy=(float)((s2>>16)&0xFFu)/255.0*2.0-1.0;
                    float ip=(float)((s2>>8)&0xFFu)/255.0*180.0-90.0,ir=(float)(s2&0xFFu)/255.0*180.0-90.0;
                    return mul(GS_AlignMat(GS_OctDec(ox,oy)),mul(GS_EulerMat(ip,yaw,ir),GS_EulerMat(oe.x,oe.y,oe.z)));
                }
                return mul(GS_EulerMat(0,yaw,0),GS_EulerMat(oe.x,oe.y,oe.z));
            }

            float3 ReconstructWS(BladeInstance b, float3 lp, bool bb)
            {
                uint hi=(b.packedYawScale>>16)&0xFFFFu, lo=b.packedYawScale&0xFFFFu;
                float yaw=(float)hi/65535.0*360.0, sxz=(float)lo/65535.0*_ScaleMax2, sy2=sxz;
                float2 wt=float2(0,0);
                if(_WindEnabled>=0.5){
                    #ifdef _WIND_PERLIN
                        float2 sP=b.posWS.xz;
                        float gst=GS_Perlin2(sP*_WindGustScale  -_WindDir*_GrassTime*_WindGustSpeed);
                        float rip=GS_Perlin2(sP*_WindRippleScale-_WindDir*_GrassTime*_WindRippleSpeed);
                        wt=_WindDir*((gst+rip*_WindRippleWeight)*_WindStrength);
                    #else
                        float ph=(b.posWS.x*0.37+b.posWS.z*0.21)*_WindNoiseScale*GRASS_TWO_PI;
                        wt=_WindDir*sin(_GrassTime*_WindFrequency+ph)*_WindStrength;
                    #endif
                }
                float2 bx=float2(0,0);
                if(_InteractorsEnabled>=0.5){
                    for(int i=0;i<_InteractorCount;++i){
                        GrassInteractorGpu ip=_Interactors[i];
                        float2 d=b.posWS.xz-ip.posWS.xz; float dl=length(d);
                        if(ip.radius<=0||dl>=ip.radius)continue;
                        bx+=(dl>1e-4?d/dl:0)*(1-dl/ip.radius)*ip.strength*_BendStrength;
                    }
                }
                // TRAIL DEFORM BEGIN
                {
                    float2 bladeXZ=b.posWS.xz; int n=_GrassTrailSegmentCount;
                    [loop]
                    for(int j=0;j<n;++j){
                        GrassTrailSegmentGpu s=_GrassTrailSegments[j];
                        float2 ab=s.PosB.xz-s.PosA.xz; float abLenSq=max(dot(ab,ab),1e-6);
                        float t=saturate(dot(bladeXZ-s.PosA.xz,ab)/abLenSq);
                        float2 c=s.PosA.xz+ab*t; float2 r=bladeXZ-c; float dd=length(r);
                        if(dd>s.Radius)continue;
                        float dn=dd/s.Radius;
                        float plateau=(dn<=s.CenterPct)?1.0:1.0-smoothstep(s.CenterPct,1.0,dn);
                        float angleDeg=degrees(s.MaxBendRad)*plateau*s.Alpha*s.Strength;
                        float pushMetres=angleDeg/DEG_PER_METRE;
                        float2 dir2=(dd>1e-4)?(r/dd):float2(1,0);
                        bx+=dir2*pushMetres;
                    }
                }
                // TRAIL DEFORM END
                float bm=length(bx);
                if(_Flatten>0&&_BendStrength>1e-5) sy2=sxz*(1-_Flatten*saturate(bm/_BendStrength));
                float2 ln=wt+bx; float pt=ln.y*DEG_PER_METRE, rl=-ln.x*DEG_PER_METRE;
                float mg=sqrt(pt*pt+rl*rl); if(mg>MAX_LEAN_DEGREES){float s=MAX_LEAN_DEGREES/mg;pt*=s;rl*=s;}
                if(bb){float2 tc=_CamPosWS.xz-b.posWS.xz;yaw=atan2(tc.x,tc.y)*(180/3.14159265);}
                float3x3 rot=mul(GS_EulerMat(pt,0,rl),GS_BaseRot(b,yaw));
                return b.posWS+mul(rot,float3(lp.x*sxz,lp.y*sy2,lp.z*sxz));
            }

            // Phase 3: extend SV with uv when _ALPHACLIP_SHADOWS is active (alpha-clip in frag).
            struct SV
            {
                float4 positionCS : SV_POSITION;
                #if defined(_ALPHACLIP_SHADOWS)
                float2 uv : TEXCOORD0;
                #endif
            };

            SV shadowVert(float4 posOS : POSITION, float3 nrmOS : NORMAL, float2 uv : TEXCOORD0, uint iid : SV_InstanceID)
            {
                SV o=(SV)0;
                BladeInstance b=_Blades[_VisibleIndices[iid]];
                #ifdef _LOD2_BILLBOARD
                    float3 posWS=ReconstructWS(b,posOS.xyz,true);
                #else
                    float3 posWS=ReconstructWS(b,posOS.xyz,false);
                #endif
                float3 nWS=float3(0,1,0);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 ld=normalize(_LightPosition-posWS);
                #else
                    float3 ld=_LightDirection;
                #endif
                // Phase 3: apply per-layer shadow bias offsets (0 = no-op; default is unchanged caster).
                posWS += ld * _ShadowDepthBias;
                posWS += nWS * _ShadowNormalBias;
                float4 clip=TransformWorldToHClip(ApplyShadowBias(posWS,nWS,ld));
                #if UNITY_REVERSED_Z
                    clip.z=min(clip.z,UNITY_NEAR_CLIP_VALUE);
                #else
                    clip.z=max(clip.z,UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS=clip;
                #if defined(_ALPHACLIP_SHADOWS)
                o.uv = uv;
                #endif
                return o;
            }

            // Phase 3: _ALPHACLIP_SHADOWS clips the shadow caster by base-map alpha.
            // OFF path returns 0 immediately — byte-identical to the Phase 2 ShadowCaster.
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            float4 _BaseMap_ST;
            half4 shadowFrag(SV i):SV_Target
            {
                #if defined(_ALPHACLIP_SHADOWS)
                    half alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a;
                    clip(alpha - _Cutoff);
                #endif
                return 0;
            }
            ENDHLSL
        }

        // ── Pass 2 : DepthOnly ────────────────────────────────────────────────
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex depthVert
            #pragma fragment depthFrag
            #pragma multi_compile_local _ _LOD2_BILLBOARD
            #pragma multi_compile_local _ _WIND_PERLIN

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct BladeInstance { float3 posWS; uint packedYawScale; uint hash; };
            struct GrassInteractorGpu { float3 posWS; float radius; float strength; float _p0; float _p1; float _p2; };

            // TRAIL DEFORM BEGIN
            struct GrassTrailSegmentGpu {  // matches C# TrailSegmentGpu, 48 B
                float3 PosA;   float Radius;
                float3 PosB;   float Alpha;
                float  MaxBendRad;
                float  CenterPct;
                float  Strength;
                float  _Pad;
            };
            StructuredBuffer<GrassTrailSegmentGpu> _GrassTrailSegments;
            int                                     _GrassTrailSegmentCount;
            // TRAIL DEFORM END

            StructuredBuffer<BladeInstance>      _Blades;
            StructuredBuffer<uint>               _VisibleIndices;
            StructuredBuffer<GrassInteractorGpu> _Interactors;

            float  _ScaleMax2; float _GrassTime; float2 _WindDir; float _WindStrength;
            float  _WindFrequency; float _WindNoiseScale; float _BendStrength; float _Flatten;
            float  _WindGustScale; float _WindRippleScale; float _WindGustSpeed; float _WindRippleSpeed; float _WindRippleWeight;
            int    _InteractorCount; float3 _CamPosWS;
            float  _OrientMode; float4 _RotationOffsetEuler;
            float  _WindEnabled; float _InteractorsEnabled;

            static const float GRASS_TWO_PI=6.2831853, DEG_PER_METRE=55.0, MAX_LEAN_DEGREES=90.0, DEG_TO_RAD=0.01745329; // TRAIL DEFORM: MAX_LEAN_DEGREES lifted 80→90

            float2 GD_PGrad(float2 i){ float h=frac(sin(dot(i,float2(127.1,311.7)))*43758.5453)*GRASS_TWO_PI; return float2(cos(h),sin(h)); }
            float  GD_Perlin2(float2 p){
                float2 i=floor(p),f=frac(p); float2 u=f*f*(3.0-2.0*f);
                float a=dot(GD_PGrad(i),f);
                float b=dot(GD_PGrad(i+float2(1,0)),f-float2(1,0));
                float c=dot(GD_PGrad(i+float2(0,1)),f-float2(0,1));
                float d=dot(GD_PGrad(i+float2(1,1)),f-float2(1,1));
                return lerp(lerp(a,b,u.x),lerp(c,d,u.x),u.y)*1.41;
            }

            float3x3 GD_EulerMat(float p,float y,float r)
            {
                p*=DEG_TO_RAD;y*=DEG_TO_RAD;r*=DEG_TO_RAD;
                float sp=sin(p),cp=cos(p),sy=sin(y),cy=cos(y),sr=sin(r),cr=cos(r);
                float3x3 Ry=float3x3(cy,0,sy,0,1,0,-sy,0,cy),Rx=float3x3(1,0,0,0,cp,-sp,0,sp,cp),Rz=float3x3(cr,-sr,0,sr,cr,0,0,0,1);
                return mul(Ry,mul(Rx,Rz));
            }
            float3 GD_OctDec(float px,float py){float pz=1.0-abs(px)-abs(py);if(pz<0){float ox=px;px=(1-abs(py))*(px>=0?1:-1);py=(1-abs(ox))*(py>=0?1:-1);}return normalize(float3(px,pz,py));}
            float3x3 GD_AlignMat(float3 n){float3 up=float3(0,1,0);float3 ax=cross(up,n);float s=length(ax),c=dot(up,n);if(s<1e-6)return c>0?(float3x3)1:float3x3(1,0,0,0,-1,0,0,0,1);ax/=s;float t=1-c;return float3x3(t*ax.x*ax.x+c,t*ax.x*ax.y-s*ax.z,t*ax.x*ax.z+s*ax.y,t*ax.x*ax.y+s*ax.z,t*ax.y*ax.y+c,t*ax.y*ax.z-s*ax.x,t*ax.x*ax.z-s*ax.y,t*ax.y*ax.z+s*ax.x,t*ax.z*ax.z+c);}
            float3x3 GD_BaseRot(BladeInstance b,float yaw){float3 oe=_RotationOffsetEuler.xyz;if(_OrientMode>=0.5){uint s2=b.hash;float ox=(float)((s2>>24)&0xFFu)/255.0*2.0-1.0,oy=(float)((s2>>16)&0xFFu)/255.0*2.0-1.0,ip=(float)((s2>>8)&0xFFu)/255.0*180.0-90.0,ir=(float)(s2&0xFFu)/255.0*180.0-90.0;return mul(GD_AlignMat(GD_OctDec(ox,oy)),mul(GD_EulerMat(ip,yaw,ir),GD_EulerMat(oe.x,oe.y,oe.z)));}return mul(GD_EulerMat(0,yaw,0),GD_EulerMat(oe.x,oe.y,oe.z));}

            float3 ReconstructWS(BladeInstance b, float3 lp, bool bb)
            {
                uint hi=(b.packedYawScale>>16)&0xFFFFu, lo=b.packedYawScale&0xFFFFu;
                float yaw=(float)hi/65535.0*360.0, sxz=(float)lo/65535.0*_ScaleMax2, sy2=sxz;
                float2 wt=float2(0,0);
                if(_WindEnabled>=0.5){
                    #ifdef _WIND_PERLIN
                        float2 sP=b.posWS.xz;
                        float gst=GD_Perlin2(sP*_WindGustScale  -_WindDir*_GrassTime*_WindGustSpeed);
                        float rip=GD_Perlin2(sP*_WindRippleScale-_WindDir*_GrassTime*_WindRippleSpeed);
                        wt=_WindDir*((gst+rip*_WindRippleWeight)*_WindStrength);
                    #else
                        float ph=(b.posWS.x*0.37+b.posWS.z*0.21)*_WindNoiseScale*GRASS_TWO_PI;
                        wt=_WindDir*sin(_GrassTime*_WindFrequency+ph)*_WindStrength;
                    #endif
                }
                float2 bx=float2(0,0);
                if(_InteractorsEnabled>=0.5){
                    for(int i=0;i<_InteractorCount;++i){
                        GrassInteractorGpu ip=_Interactors[i];
                        float2 d=b.posWS.xz-ip.posWS.xz; float dl=length(d);
                        if(ip.radius<=0||dl>=ip.radius)continue;
                        bx+=(dl>1e-4?d/dl:0)*(1-dl/ip.radius)*ip.strength*_BendStrength;
                    }
                }
                // TRAIL DEFORM BEGIN
                {
                    float2 bladeXZ=b.posWS.xz; int n=_GrassTrailSegmentCount;
                    [loop]
                    for(int j=0;j<n;++j){
                        GrassTrailSegmentGpu s=_GrassTrailSegments[j];
                        float2 ab=s.PosB.xz-s.PosA.xz; float abLenSq=max(dot(ab,ab),1e-6);
                        float t=saturate(dot(bladeXZ-s.PosA.xz,ab)/abLenSq);
                        float2 c=s.PosA.xz+ab*t; float2 r=bladeXZ-c; float dd=length(r);
                        if(dd>s.Radius)continue;
                        float dn=dd/s.Radius;
                        float plateau=(dn<=s.CenterPct)?1.0:1.0-smoothstep(s.CenterPct,1.0,dn);
                        float angleDeg=degrees(s.MaxBendRad)*plateau*s.Alpha*s.Strength;
                        float pushMetres=angleDeg/DEG_PER_METRE;
                        float2 dir2=(dd>1e-4)?(r/dd):float2(1,0);
                        bx+=dir2*pushMetres;
                    }
                }
                // TRAIL DEFORM END
                float bm=length(bx);
                if(_Flatten>0&&_BendStrength>1e-5) sy2=sxz*(1-_Flatten*saturate(bm/_BendStrength));
                float2 ln=wt+bx; float pt=ln.y*DEG_PER_METRE, rl=-ln.x*DEG_PER_METRE;
                float mg=sqrt(pt*pt+rl*rl); if(mg>MAX_LEAN_DEGREES){float s=MAX_LEAN_DEGREES/mg;pt*=s;rl*=s;}
                if(bb){float2 tc=_CamPosWS.xz-b.posWS.xz;yaw=atan2(tc.x,tc.y)*(180/3.14159265);}
                float3x3 rot=mul(GD_EulerMat(pt,0,rl),GD_BaseRot(b,yaw));
                return b.posWS+mul(rot,float3(lp.x*sxz,lp.y*sy2,lp.z*sxz));
            }

            struct DV { float4 positionCS : SV_POSITION; };
            DV depthVert(float4 posOS : POSITION, float2 uv : TEXCOORD0, uint iid : SV_InstanceID)
            {
                DV o=(DV)0;
                BladeInstance b=_Blades[_VisibleIndices[iid]];
                #ifdef _LOD2_BILLBOARD
                    float3 posWS=ReconstructWS(b,posOS.xyz,true);
                #else
                    float3 posWS=ReconstructWS(b,posOS.xyz,false);
                #endif
                o.positionCS=TransformWorldToHClip(posWS);
                return o;
            }
            half4 depthFrag(DV i):SV_Target{return 0;}
            ENDHLSL
        }
    }

    Fallback Off
}
