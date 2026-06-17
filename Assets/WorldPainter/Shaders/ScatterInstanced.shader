// ScatterInstanced.shader — GPU-instanced mesh props with optional wind+interactor deform (Phase 3+4+5).
//
// Deform path (_InteractsWithDeform >= 0.5):
//   Reads wind globals (_GrassTime/_WindDir/_WindStrength/_WindFrequency/_WindNoiseScale)
//   and interactor globals (_Interactors/_InteractorCount/_BendStrength/_Flatten/_CamPosWS)
//   set by GrassGpuEngine or MeshScatterEngine each frame. Wind phase is reconstructed from
//   posWS.xz (PHASE_FREQ=0.37,0.21, same as GrassInteractIndirect). Interactor lean-away loop
//   is identical to GrassInteractIndirect (same lean math, same DEG_PER_METRE=55, same clamp=80°).
//   Deform is composed AFTER base orientation: leanMat * baseRot (orientation first, then bend on top).
//
// Static path (_InteractsWithDeform < 0.5):
//   EXACTLY the current static path — byte-identical to the Phase 3+4 behaviour. No deform.
//
// _InteractsWithDeform is a per-material float set at Build time by MeshScatterEngine.
// Using a float uniform (not a keyword) avoids variant stripping issues in player builds.
//
// VS reads _Instances (global StructuredBuffer<InstanceData>) indexed via
// _VisibleIndices[SV_InstanceID], unpacks yaw+scale from packedYawScale,
// builds a world TRS and transforms the LOD mesh vertex.
//
// InstanceData layout (20 B, byte-identical to BladeInstance in GrassCull.compute):
//   float3 posWS          (12 B)  — base world position
//   uint   packedYawScale  (4 B)  — hi16=yaw/65535*360, lo16=scale/65535*_ScaleMax2
//   uint   lodHash         (4 B)  — legacy: decorrelation hash; oriented: octNormal16|pitch8|roll8
//
// Phase 4 — Orient mode (_OrientMode uniform):
//   0.0 (legacy/default): slot2 = hash. Rotation = yaw about Y + _RotationOffsetEuler applied uniformly.
//   1.0 (oriented):       slot2 = octNormal(hi16) | pitch8(b8) | roll8(b0).
//                         Rotation = alignFromNormal * Euler(pitch,yaw,roll) * _RotationOffsetEuler.
//   _OrientMode is a float uniform (not a keyword) to avoid variant stripping in player builds.
//
// Slot2 oriented decode (HLSL SSOT — must match ChunkedBladeBuffer.PackOrientedSlot2):
//   octX  = ((slot2 >> 24) & 0xFF) / 255.0 * 2.0 - 1.0
//   octY  = ((slot2 >> 16) & 0xFF) / 255.0 * 2.0 - 1.0
//   normal = OctDecode(octX, octY)
//   pitch  = ((slot2 >>  8) & 0xFF) / 255.0 * 180.0 - 90.0   [degrees, range -90..90]
//   roll   = ( slot2        & 0xFF) / 255.0 * 180.0 - 90.0   [degrees, range -90..90]
//
// Buffer binding contract:
//   _Instances           — global, set via Shader.SetGlobalBuffer (MeshScatterEngine).
//   _VisibleIndices      — per-LOD, bound via material.SetBuffer (NOT MPB).
//   _ScaleMax2           — global float.
//   _OrientMode          — per-material float, set at Build time. 0=legacy, 1=oriented.
//   _RotationOffsetEuler — per-material float3 (degrees), applied as uniform offset.
//   _InteractsWithDeform — per-material float, 0=static (default), 1=deform. Set by MeshScatterEngine.
//   _GrassTime/_WindDir/_WindStrength/_WindFrequency/_WindNoiseScale/_BendStrength/_Flatten/_CamPosWS
//                        — global floats shared with GrassInteractIndirect (set by GrassGpuEngine or
//                          MeshScatterEngine when InteractsWithDeform=true).
//   _Interactors/_InteractorCount — global buffer+count shared with GrassInteractIndirect.
//
// RENDER-PATH CONTRACT:
//   RenderParams built with new RenderParams(material) CTOR (NOT object-initializer).
//   multi_compile_local for runtime-toggled keywords.
//   SCATTER_ prefix on all custom HLSL constants (URP Macros.hlsl pre-defines TWO_PI/PI/HALF_PI).
Shader "WorldPainter/ScatterInstanced"
{
    Properties
    {
        _BaseColor ("Base Color",  Color)  = (0.45, 0.35, 0.22, 1)
        _TipColor  ("Tip Color",   Color)  = (0.65, 0.55, 0.38, 1)
        [NoScaleOffset] _BaseMap ("Base Map (optional)", 2D) = "white" {}
        _BaseMap_ST ("Base Map Tiling", Vector) = (1, 1, 0, 0)
        // Per-flag deform gates. Set by MeshScatterEngine at Build time. Default 0 = static.
        _WindEnabled        ("Wind Enabled",        Float) = 0
        _InteractorsEnabled ("Interactors Enabled", Float) = 0
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

        // ── Pass 0: UniversalForward ──────────────────────────────────────────
        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            // multi_compile_local so both variants survive player build (shader_feature_local strips unused).
            #pragma multi_compile_local _ SCATTER_ALIGN_TO_NORMAL
            // Phase 2: PBR material feature toggles (shader_feature_local — stripped from build when unused).
            // ALL DEFAULT OFF: with none of these defined, the original stylized path runs unchanged.
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local _PBR
            #pragma shader_feature_local _MASKMAP
            #pragma shader_feature_local _EMISSION
            // Phase 3: shadow feature toggles (shader_feature_local, all default OFF).
            #pragma shader_feature_local _RECEIVE_SHADOWS
            #pragma shader_feature_local _SHADOW_TINT
            #pragma shader_feature_local _ALPHACLIP_SHADOWS
            // Phase 3: URP shadow sampling keywords (multi_compile so shadow variants are always in build).
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile _ _WP_ROOT_TRANSFORM

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "WorldRootTransform.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _TipColor;
                float4 _BaseMap_ST;
                // Phase 4: _OrientMode=0 legacy (hash slot2), =1 oriented (octNormal+pitch+roll slot2).
                // Float uniform avoids keyword stripping in player builds.
                float  _OrientMode;
                float4 _RotationOffsetEuler; // xyz = euler degrees uniform offset (w unused)
                // Phase 5: independent deform gates. 0=off (default), 1=on.
                float _WindEnabled;
                float _InteractorsEnabled;
                // Phase B: rigid whole-instance tilt gate. 0=off, 1=apply _InstanceTilt.
                float _TiltEnabled;
                // Per-layer deform sampling anchor (local space). Wind phase + interactor lean sample from
                // posWS + baseRot*(_AnchorOffset.xyz * scale) instead of the pivot. (0,0,0) = pivot (legacy).
                float4 _AnchorOffset;
                // Phase 2: PBR material uniforms.
                float  _NormalStrength;
                float4 _EmissionColor;
                // Phase 3: shadow uniforms (only read under their respective keywords; defaults are inert).
                float  _ShadowStrength;   // only under _SHADOW_TINT
                float4 _ShadowTint;       // only under _SHADOW_TINT
                float  _ShadowDepthBias;  // ShadowCaster VS (0 = no-op)
                float  _ShadowNormalBias; // ShadowCaster VS (0 = no-op)
                float  _Cutoff;           // ShadowCaster alpha-clip threshold (0.5 default)
            CBUFFER_END

            TEXTURE2D(_BaseMap);     SAMPLER(sampler_BaseMap);
            TEXTURE2D(_NormalMap);   SAMPLER(sampler_NormalMap);
            // Mask map: R=Metallic, G=Occlusion, B=unused, A=Smoothness (URP Lit convention).
            TEXTURE2D(_MaskMap);     SAMPLER(sampler_MaskMap);
            TEXTURE2D(_EmissionMap); SAMPLER(sampler_EmissionMap);

            // InstanceData struct — 20 B, byte-identical to BladeInstance in GrassCull.compute.
            // posWS MUST be first so the cull kernel reads posWS at the correct offset.
            struct InstanceData
            {
                float3 posWS;
                uint   packedYawScale;
                uint   lodHash; // legacy: hash; oriented: octNormal(hi16)|pitch8|roll8
            };

            // Interactor record — 32 B, byte-identical to GrassInteractorGpu in GrassInteractIndirect.shader.
            struct SCATTER_InteractorGpu
            {
                float3 posWS;
                float  radius;
                float  strength;
                float  _pad0;
                float  _pad1;
                float  _pad2;
            };

            StructuredBuffer<InstanceData>         _Instances;      // global, all prop instances
            StructuredBuffer<uint>                 _VisibleIndices; // per-LOD, material.SetBuffer
            StructuredBuffer<SCATTER_InteractorGpu> _Interactors;   // global, shared with GrassInteractIndirect
            StructuredBuffer<float4>                _InstanceTilt;  // global, per-instance rigid tilt quaternion (xyzw, baked-order indexed)

            float  _ScaleMax2;       // global float — scale decode upper bound
            float  _GrassTime;       // global — time accumulator (set by GrassGpuEngine or MeshScatterEngine)
            float2 _WindDir;         // global — wind direction (normalised XZ)
            float  _WindStrength;    // global — wind sway amplitude
            float  _WindFrequency;   // global — wind oscillation frequency
            float  _WindNoiseScale;  // global — per-instance phase noise scale
            float  _BendStrength;    // global — interactor bend multiplier
            float  _Flatten;         // global — vertical scale reduction under bend
            int    _InteractorCount; // global — number of active interactors
            float3 _CamPosWS;        // global — camera world position (unused for props, kept for completeness)

            // Custom constants prefixed SCATTER_ to avoid collisions with URP Macros.hlsl
            // (TWO_PI, PI, HALF_PI are already predefined there).
            static const float SCATTER_DEG_TO_RAD    = 0.01745329f;
            static const float SCATTER_TWO_PI        = 6.2831853f;
            static const float SCATTER_DEG_PER_METRE = 55.0f;
            static const float SCATTER_MAX_LEAN_DEG  = 80.0f;

            // Rotate a vector by a unit quaternion (q.xyz = imaginary, q.w = real) — Unity.Mathematics convention.
            float3 SCATTER_RotateByQuat(float3 v, float4 q)
            {
                float3 t = 2.0f * cross(q.xyz, v);
                return v + q.w * t + cross(q.xyz, t);
            }

            // Build a Y-axis rotation matrix from yaw (degrees).
            float3x3 SCATTER_YawMatrix(float yawDeg)
            {
                float a = yawDeg * SCATTER_DEG_TO_RAD;
                float sy = sin(a), cy = cos(a);
                return float3x3(cy, 0, sy,
                                 0, 1,  0,
                               -sy, 0, cy);
            }

            // Build a full Euler ZXY rotation matrix from pitch/yaw/roll (degrees).
            // Mirrors Unity Quaternion.Euler convention: Ry * Rx * Rz.
            float3x3 SCATTER_EulerMatrix(float pitchDeg, float yawDeg, float rollDeg)
            {
                float p = pitchDeg * SCATTER_DEG_TO_RAD;
                float y = yawDeg   * SCATTER_DEG_TO_RAD;
                float r = rollDeg  * SCATTER_DEG_TO_RAD;
                float sp=sin(p),cp=cos(p), sy2=sin(y),cy2=cos(y), sr=sin(r),cr=cos(r);
                float3x3 Ry = float3x3(cy2,0,sy2,  0,1,0,  -sy2,0,cy2);
                float3x3 Rx = float3x3(1,0,0,  0,cp,-sp,  0,sp,cp);
                float3x3 Rz = float3x3(cr,-sr,0,  sr,cr,0,  0,0,1);
                return mul(Ry, mul(Rx, Rz));
            }

            // Decode octahedral normal from two snorm values (C# SSOT: ChunkedBladeBuffer.OctDecode).
            // oct.x maps to world X, oct.y maps to world Z, derived pz maps to world Y (Y-up convention).
            float3 SCATTER_OctDecode(float px, float py)
            {
                float pz = 1.0f - abs(px) - abs(py);
                if (pz < 0.0f)
                {
                    float oldPx = px;
                    px = (1.0f - abs(py)) * (px >= 0.0f ? 1.0f : -1.0f);
                    py = (1.0f - abs(oldPx)) * (py >= 0.0f ? 1.0f : -1.0f);
                }
                return normalize(float3(px, pz, py)); // px→x, pz→y, py→z (mirrors C# OctDecode)
            }

            // Build align rotation: FromToRotation(up, surfaceNormal).
            float3x3 SCATTER_AlignMatrix(float3 surfNorm)
            {
                float3 up = float3(0,1,0);
                float3 axis = cross(up, surfNorm);
                float  sinA = length(axis);
                float  cosA = dot(up, surfNorm);
                if (sinA < 1e-6f) return (cosA > 0.0f) ? (float3x3)1 : float3x3(1,0,0, 0,-1,0, 0,0,1);
                axis = axis / sinA;
                float t = 1.0f - cosA;
                return float3x3(
                    t*axis.x*axis.x + cosA,        t*axis.x*axis.y - sinA*axis.z, t*axis.x*axis.z + sinA*axis.y,
                    t*axis.x*axis.y + sinA*axis.z,  t*axis.y*axis.y + cosA,        t*axis.y*axis.z - sinA*axis.x,
                    t*axis.x*axis.z - sinA*axis.y,  t*axis.y*axis.z + sinA*axis.x, t*axis.z*axis.z + cosA);
            }

            // Build the base orientation matrix for an InstanceData record.
            // Legacy (_OrientMode < 0.5): yaw about Y + rotation offset.
            // Oriented (_OrientMode >= 0.5): align(surfNorm) * Euler(pitch,yaw,roll) * offset.
            float3x3 SCATTER_BaseRot(InstanceData inst, float yawDeg)
            {
                float3 offsetE = _RotationOffsetEuler.xyz;
                if (_OrientMode >= 0.5f)
                {
                    uint slot2 = inst.lodHash;
                    float octX     = (float)((slot2 >> 24) & 0xFFu) / 255.0f * 2.0f - 1.0f;
                    float octY     = (float)((slot2 >> 16) & 0xFFu) / 255.0f * 2.0f - 1.0f;
                    float pitchDeg = (float)((slot2 >>  8) & 0xFFu) / 255.0f * 180.0f - 90.0f;
                    float rollDeg  = (float)( slot2        & 0xFFu) / 255.0f * 180.0f - 90.0f;
                    float3 surfNorm   = SCATTER_OctDecode(octX, octY);
                    float3x3 align    = SCATTER_AlignMatrix(surfNorm);
                    float3x3 base     = SCATTER_EulerMatrix(pitchDeg, yawDeg, rollDeg);
                    float3x3 offsetMat = SCATTER_EulerMatrix(offsetE.x, offsetE.y, offsetE.z);
                    return mul(align, mul(base, offsetMat));
                }
                // Legacy: yaw + offset.
                return mul(SCATTER_YawMatrix(yawDeg), SCATTER_EulerMatrix(offsetE.x, offsetE.y, offsetE.z));
            }

            // Reconstruct world-space vertex position and normal from an InstanceData record.
            // Independent gates: _WindEnabled and _InteractorsEnabled may be on/off separately.
            void TransformInstance(InstanceData inst, float4 tiltQ, float3 localPos, float3 localNormal,
                out float3 posWS, out float3 normalWS)
            {
                // Unpack yaw and scale (common to both modes).
                uint  hi     = (inst.packedYawScale >> 16) & 0xFFFFu;
                uint  lo     =  inst.packedYawScale & 0xFFFFu;
                float yawDeg = (float)hi / 65535.0f * 360.0f;
                float scale  = (float)lo / 65535.0f * _ScaleMax2;

                float3x3 baseRot = SCATTER_BaseRot(inst, yawDeg);

                // Deform sampling anchor: pivot + baseRot*(localOffset * scale). Wind phase and interactor
                // proximity are evaluated HERE (not at the pivot) so the offset rides the instance's
                // orientation and scales with it. Deform is still applied about the pivot below.
                float3 anchorWS = inst.posWS + mul(baseRot, _AnchorOffset.xyz * scale);

                // Wind contribution (independent gate).
                float2 windXZ = float2(0, 0);
                if (_WindEnabled >= 0.5f)
                {
                    float phase = (anchorWS.x * 0.37f + anchorWS.z * 0.21f) * _WindNoiseScale * SCATTER_TWO_PI;
                    float wave  = sin(_GrassTime * _WindFrequency + phase) * _WindStrength;
                    windXZ      = _WindDir * wave;
                }

                // Interactor lean-away contribution (independent gate).
                float2 bendXZ = float2(0, 0);
                if (_InteractorsEnabled >= 0.5f)
                {
                    for (int i = 0; i < _InteractorCount; ++i)
                    {
                        SCATTER_InteractorGpu ip = _Interactors[i];
                        float2 delta = anchorWS.xz - ip.posWS.xz;
                        float  d     = length(delta);
                        if (ip.radius <= 0.0f || d >= ip.radius) continue;
                        float  fall  = 1.0f - d / ip.radius;
                        float2 away  = (d > 1e-4f) ? delta / d : float2(0, 0);
                        bendXZ += away * (fall * ip.strength * _BendStrength);
                    }
                }

                // Lean rotation (identity when no deform). Kept SEPARATE from baseRot so it can pivot
                // about the ANCHOR — composing it into baseRot would pivot the bend about the mesh origin.
                float3x3 leanMat = float3x3(1,0,0, 0,1,0, 0,0,1);
                if (_WindEnabled >= 0.5f || _InteractorsEnabled >= 0.5f)
                {
                    float2 lean     = windXZ + bendXZ;
                    float leanPitch = lean.y * SCATTER_DEG_PER_METRE;
                    float leanRoll  = -lean.x * SCATTER_DEG_PER_METRE;
                    float magDeg    = sqrt(leanPitch * leanPitch + leanRoll * leanRoll);
                    if (magDeg > SCATTER_MAX_LEAN_DEG)
                    {
                        float s = SCATTER_MAX_LEAN_DEG / magDeg;
                        leanPitch *= s;
                        leanRoll  *= s;
                    }
                    leanMat = SCATTER_EulerMatrix(leanPitch, 0.0f, leanRoll);
                }

                float3 baseV = mul(baseRot, localPos * scale);
                if (_WindEnabled < 0.5f && _InteractorsEnabled < 0.5f && _TiltEnabled < 0.5f)
                {
                    // Pure static path — byte-identical to Phase 3+4 (no anchor round-trip / FP drift).
                    posWS    = inst.posWS + baseV;
                    normalWS = mul(baseRot, localNormal);
                }
                else
                {
                    // Deform pivots ABOUT THE ANCHOR: rotate the vertex (wind/interactor lean, then rigid
                    // tilt) AROUND the anchor point so wind + interactors bend the instance about its anchor,
                    // not its mesh origin. anchorRel = anchorWS - pivot.
                    float3 anchorRel = anchorWS - inst.posWS;
                    posWS    = inst.posWS + anchorRel + SCATTER_RotateByQuat(mul(leanMat, baseV - anchorRel), tiltQ);
                    // Normals are directions — rotate by lean then tilt, no pivot translation.
                    normalWS = SCATTER_RotateByQuat(mul(leanMat, mul(baseRot, localNormal)), tiltQ);
                }
            }

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float2 uv         : TEXCOORD1;
                // Phase 2: world-space position and tangent for PBR/normal-map.
                float3 positionWS : TEXCOORD2;
                float3 tangentWS  : TEXCOORD3;
            };

            // Compute world-space tangent using the SAME full transform chain as TransformInstance
            // (baseRot → leanMat → tiltQ) so tangent and normal always share a basis.
            // Omitting leanMat was the FIX3 bug: under wind/deform the tangent and normal diverged,
            // producing a non-orthonormal TBN that skewed normal-map lighting on swaying props.
            float3 SCATTER_TangentWS(InstanceData inst, float4 tiltQ, float3 tangentOS)
            {
                uint  hi     = (inst.packedYawScale >> 16) & 0xFFFFu;
                uint  lo     =  inst.packedYawScale & 0xFFFFu;
                float yawDeg = (float)hi / 65535.0f * 360.0f;
                float scale  = (float)lo / 65535.0f * _ScaleMax2;

                float3x3 baseRot  = SCATTER_BaseRot(inst, yawDeg);
                float3   anchorWS = inst.posWS + mul(baseRot, _AnchorOffset.xyz * scale);

                // Rebuild the same leanMat as TransformInstance so tangent follows the deformed basis.
                float2 windXZ = float2(0, 0);
                if (_WindEnabled >= 0.5f)
                {
                    float phase = (anchorWS.x * 0.37f + anchorWS.z * 0.21f) * _WindNoiseScale * SCATTER_TWO_PI;
                    windXZ      = _WindDir * sin(_GrassTime * _WindFrequency + phase) * _WindStrength;
                }
                float2 bendXZ = float2(0, 0);
                if (_InteractorsEnabled >= 0.5f)
                {
                    for (int i = 0; i < _InteractorCount; ++i)
                    {
                        SCATTER_InteractorGpu ip = _Interactors[i];
                        float2 delta = anchorWS.xz - ip.posWS.xz;
                        float  d     = length(delta);
                        if (ip.radius <= 0.0f || d >= ip.radius) continue;
                        float  fall  = 1.0f - d / ip.radius;
                        float2 away  = (d > 1e-4f) ? delta / d : float2(0, 0);
                        bendXZ += away * (fall * ip.strength * _BendStrength);
                    }
                }
                float3x3 leanMat = float3x3(1,0,0, 0,1,0, 0,0,1);
                if (_WindEnabled >= 0.5f || _InteractorsEnabled >= 0.5f)
                {
                    float2 lean     = windXZ + bendXZ;
                    float leanPitch = lean.y * SCATTER_DEG_PER_METRE;
                    float leanRoll  = -lean.x * SCATTER_DEG_PER_METRE;
                    float magDeg    = sqrt(leanPitch * leanPitch + leanRoll * leanRoll);
                    if (magDeg > SCATTER_MAX_LEAN_DEG) { float s = SCATTER_MAX_LEAN_DEG / magDeg; leanPitch *= s; leanRoll *= s; }
                    leanMat = SCATTER_EulerMatrix(leanPitch, 0.0f, leanRoll);
                }
                // Apply baseRot → leanMat → tiltQ (same chain as normalWS in TransformInstance).
                return SCATTER_RotateByQuat(mul(leanMat, mul(baseRot, tangentOS)), tiltQ);
            }

            Varyings vert(
                float4 posOS      : POSITION,
                float3 normalOS   : NORMAL,
                float4 tangentOS  : TANGENT,
                float2 uv         : TEXCOORD0,
                uint   instanceID : SV_InstanceID)
            {
                Varyings o = (Varyings)0;

                uint         instIdx = _VisibleIndices[instanceID];
                InstanceData inst    = _Instances[instIdx];
                float4       tiltQ   = (_TiltEnabled >= 0.5f) ? _InstanceTilt[instIdx] : float4(0, 0, 0, 1);

                float3 posWS, normalWS;
                TransformInstance(inst, tiltQ, posOS.xyz, normalOS, posWS, normalWS);

                // FIX3: tangent now uses the FULL transform chain (baseRot + leanMat + tiltQ),
                // matching normalWS so TBN stays orthonormal under wind/interactor deform.
                float3 tangentWS = SCATTER_TangentWS(inst, tiltQ, tangentOS.xyz);

                // P2-Root: map painting-space posWS/normalWS to world before clip.
                float3 posWSWorld = WP_PaintingToWorldPos(posWS);
                o.positionCS = TransformWorldToHClip(posWSWorld);
                o.normalWS   = WP_PaintingToWorldNormal(normalWS);
                o.uv         = uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
                o.positionWS = posWSWorld;
                o.tangentWS  = WP_PaintingToWorldDir(tangentWS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 normalWS = normalize(i.normalWS);

                // ── Normal map perturbation (runs before PBR or stylized branch) ──
                #if defined(_NORMALMAP)
                {
                    float3 tangentWS = normalize(i.tangentWS);
                    // Re-orthonormalize: remove any residual dot(tangent, normal) component that
                    // may accumulate through the lean+tilt rotation chain (FIX3 safety measure).
                    tangentWS = normalize(tangentWS - dot(tangentWS, normalWS) * normalWS);
                    float3 bitangentWS = cross(normalWS, tangentWS);
                    half4  nSample     = SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, i.uv);
                    float3 nTS         = UnpackNormal(nSample);
                    nTS.xy *= _NormalStrength;
                    normalWS = normalize(tangentWS * nTS.x + bitangentWS * nTS.y + normalWS * nTS.z);
                }
                #endif

                // Simple gradient: base→tip driven by world-space up-dot (approximates top color).
                float upDot      = saturate(dot(normalWS, float3(0, 1, 0)));
                half4 tex        = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                half3 baseAlbedo = lerp(_BaseColor.rgb, _TipColor.rgb, upDot) * tex.rgb;

                #if defined(_PBR)
                {
                    // ── URP PBR path ─────────────────────────────────────────────
                    SurfaceData surfaceData = (SurfaceData)0;
                    surfaceData.albedo      = baseAlbedo;
                    surfaceData.normalTS    = float3(0, 0, 1); // unused — world-space normal fed via inputData
                    surfaceData.alpha       = 1.0f;
                    #if defined(_MASKMAP)
                    {
                        half4 mask             = SAMPLE_TEXTURE2D(_MaskMap, sampler_MaskMap, i.uv);
                        surfaceData.metallic   = mask.r;
                        surfaceData.occlusion  = mask.g;
                        surfaceData.smoothness = mask.a;
                    }
                    #else
                    {
                        surfaceData.metallic   = 0.0f;
                        surfaceData.occlusion  = 1.0f;
                        surfaceData.smoothness = 0.5f;
                    }
                    #endif
                    #if defined(_EMISSION)
                    {
                        half4 emitTex          = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, i.uv);
                        surfaceData.emission   = emitTex.rgb * _EmissionColor.rgb;
                    }
                    #else
                    {
                        surfaceData.emission   = 0.0f;
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

                    // Phase 3: _SHADOW_TINT — modulate received shadow term by strength + tint color.
                    #if defined(_SHADOW_TINT) && defined(_RECEIVE_SHADOWS)
                    {
                        Light mainLight = GetMainLight(inputData.shadowCoord);
                        float shadow    = mainLight.shadowAttenuation;
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
                    // multiplies (or tints) the output — so props darken in shadow on the
                    // cheap stylized path without requiring full PBR.
                    half3 col = baseAlbedo;

                    // Simple ambient + main light diffuse (single forward light).
                    #if defined(_RECEIVE_SHADOWS)
                    {
                        float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                        Light mainLight    = GetMainLight(shadowCoord);
                        float NdotL        = saturate(dot(normalWS, mainLight.direction));
                        col *= mainLight.color * (0.5f + 0.5f * NdotL);
                        col += half3(0.05f, 0.05f, 0.05f); // ambient lift
                        half shadowAtten   = mainLight.shadowAttenuation;
                        #if defined(_SHADOW_TINT)
                        {
                            half3 tintedShadow = lerp(_ShadowTint.rgb, half3(1,1,1), shadowAtten) * _ShadowStrength
                                                + (1.0 - _ShadowStrength);
                            col *= tintedShadow;
                        }
                        #else
                        {
                            col *= shadowAtten;
                        }
                        #endif
                    }
                    #else
                    {
                        // No shadow sampling — original GetMainLight() (no coord) path, unchanged.
                        Light mainLight = GetMainLight();
                        float NdotL = saturate(dot(normalWS, mainLight.direction));
                        col *= mainLight.color * (0.5f + 0.5f * NdotL);
                        col += half3(0.05f, 0.05f, 0.05f); // ambient lift so props aren't pure black in shadow
                    }
                    #endif

                    return half4(col, 1.0f);
                }
                #endif
            }
            ENDHLSL
        }

        // ── Pass 1: ShadowCaster ──────────────────────────────────────────────
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex shadowVert
            #pragma fragment shadowFrag
            #pragma multi_compile _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_local _ SCATTER_ALIGN_TO_NORMAL
            // Phase 3: alpha-clip shadow caster. OFF = solid caster (Phase 2 behavior unchanged).
            #pragma shader_feature_local _ALPHACLIP_SHADOWS
            #pragma multi_compile _ _WP_ROOT_TRANSFORM

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #include "WorldRootTransform.hlsl"

            struct InstanceData { float3 posWS; uint packedYawScale; uint lodHash; };
            struct SC_InteractorGpu { float3 posWS; float radius; float strength; float _p0; float _p1; float _p2; };

            StructuredBuffer<InstanceData>   _Instances;
            StructuredBuffer<uint>           _VisibleIndices;
            StructuredBuffer<SC_InteractorGpu> _Interactors;
            StructuredBuffer<float4>         _InstanceTilt;

            float _ScaleMax2;
            float _OrientMode;
            float4 _RotationOffsetEuler;
            float _WindEnabled;
            float _InteractorsEnabled;
            float _TiltEnabled;
            float4 _AnchorOffset;
            float _GrassTime; float2 _WindDir; float _WindStrength; float _WindFrequency;
            float _WindNoiseScale; float _BendStrength; float _Flatten; int _InteractorCount;
            // Explicitly declare shadow-direction uniforms (mirrors GrassInteractIndirect.shader pattern).
            float3 _LightDirection;
            float3 _LightPosition;
            // Phase 3: shadow bias + alpha-clip uniforms (0 = no-op so today's caster is unchanged).
            float  _ShadowDepthBias;
            float  _ShadowNormalBias;
            float  _Cutoff;

            static const float SC_DEG_TO_RAD=0.01745329f,SC_TWO_PI=6.2831853f,SC_DPM=55.0f,SC_MAX_LEAN=80.0f;
            float3 SC_RotateByQuat(float3 v,float4 q){float3 t=2.0f*cross(q.xyz,v);return v+q.w*t+cross(q.xyz,t);}

            float3x3 SC_YawMat(float y){float a=y*SC_DEG_TO_RAD,sy=sin(a),cy=cos(a);return float3x3(cy,0,sy,0,1,0,-sy,0,cy);}
            float3x3 SC_EulMat(float p,float y,float r){p*=SC_DEG_TO_RAD;y*=SC_DEG_TO_RAD;r*=SC_DEG_TO_RAD;float sp=sin(p),cp=cos(p),sy=sin(y),cy=cos(y),sr=sin(r),cr=cos(r);return mul(float3x3(cy,0,sy,0,1,0,-sy,0,cy),mul(float3x3(1,0,0,0,cp,-sp,0,sp,cp),float3x3(cr,-sr,0,sr,cr,0,0,0,1)));}
            float3 SC_OctDec(float px,float py){float pz=1.0f-abs(px)-abs(py);if(pz<0){float ox=px;px=(1-abs(py))*(px>=0?1:-1);py=(1-abs(ox))*(py>=0?1:-1);}return normalize(float3(px,pz,py));}
            float3x3 SC_AlignMat(float3 n){float3 up=float3(0,1,0);float3 ax=cross(up,n);float s=length(ax),c=dot(up,n);if(s<1e-6)return c>0?(float3x3)1:float3x3(1,0,0,0,-1,0,0,0,1);ax/=s;float t=1-c;return float3x3(t*ax.x*ax.x+c,t*ax.x*ax.y-s*ax.z,t*ax.x*ax.z+s*ax.y,t*ax.x*ax.y+s*ax.z,t*ax.y*ax.y+c,t*ax.y*ax.z-s*ax.x,t*ax.x*ax.z-s*ax.y,t*ax.y*ax.z+s*ax.x,t*ax.z*ax.z+c);}
            float3x3 SC_BaseRot2(InstanceData inst,float yaw){float3 oe=_RotationOffsetEuler.xyz;if(_OrientMode>=0.5f){uint s2=inst.lodHash;float ox=(float)((s2>>24)&0xFFu)/255.0f*2.0f-1.0f,oy=(float)((s2>>16)&0xFFu)/255.0f*2.0f-1.0f,pd=(float)((s2>>8)&0xFFu)/255.0f*180.0f-90.0f,rd=(float)(s2&0xFFu)/255.0f*180.0f-90.0f;return mul(SC_AlignMat(SC_OctDec(ox,oy)),mul(SC_EulMat(pd,yaw,rd),SC_EulMat(oe.x,oe.y,oe.z)));}return mul(SC_YawMat(yaw),SC_EulMat(oe.x,oe.y,oe.z));}

            void TransformInstance2(InstanceData inst, float4 tiltQ, float3 lp, float3 ln, out float3 wp, out float3 wn)
            {
                uint hi=(inst.packedYawScale>>16)&0xFFFFu,lo=inst.packedYawScale&0xFFFFu;
                float yaw=(float)hi/65535.0f*360.0f, scale=(float)lo/65535.0f*_ScaleMax2;
                float3x3 baseRot=SC_BaseRot2(inst,yaw);
                float3 anchorWS=inst.posWS+mul(baseRot,_AnchorOffset.xyz*scale);
                float2 windXZ=float2(0,0);
                if(_WindEnabled>=0.5f){float ph=(anchorWS.x*0.37f+anchorWS.z*0.21f)*_WindNoiseScale*SC_TWO_PI;windXZ=_WindDir*sin(_GrassTime*_WindFrequency+ph)*_WindStrength;}
                float2 bxz=float2(0,0);
                if(_InteractorsEnabled>=0.5f){for(int i=0;i<_InteractorCount;++i){SC_InteractorGpu ip=_Interactors[i];float2 d=anchorWS.xz-ip.posWS.xz;float dl=length(d);if(ip.radius<=0||dl>=ip.radius)continue;bxz+=(dl>1e-4f?d/dl:0)*(1-dl/ip.radius)*ip.strength*_BendStrength;}}
                float3x3 leanMat=float3x3(1,0,0,0,1,0,0,0,1);
                if(_WindEnabled>=0.5f||_InteractorsEnabled>=0.5f){
                    float2 lean=windXZ+bxz; float pt=lean.y*SC_DPM,rl=-lean.x*SC_DPM,mg=sqrt(pt*pt+rl*rl);
                    if(mg>SC_MAX_LEAN){float s=SC_MAX_LEAN/mg;pt*=s;rl*=s;}
                    leanMat=SC_EulMat(pt,0,rl);
                }
                float3 baseV=mul(baseRot,lp*scale);
                if(_WindEnabled<0.5f&&_InteractorsEnabled<0.5f&&_TiltEnabled<0.5f){ wp=inst.posWS+baseV; wn=mul(baseRot,ln); }
                else {
                    // Deform pivots about the anchor (see Forward pass TransformInstance for the rationale).
                    float3 anchorRel=anchorWS-inst.posWS;
                    wp=inst.posWS+anchorRel+SC_RotateByQuat(mul(leanMat,baseV-anchorRel),tiltQ);
                    wn=SC_RotateByQuat(mul(leanMat,mul(baseRot,ln)),tiltQ);
                }
            }

            // Phase 3: extend SV with uv when _ALPHACLIP_SHADOWS is active (alpha-clip in frag).
            struct SV
            {
                float4 positionCS : SV_POSITION;
                #if defined(_ALPHACLIP_SHADOWS)
                float2 uv : TEXCOORD0;
                #endif
            };

            SV shadowVert(
                float4 posOS    : POSITION,
                float3 normalOS : NORMAL,
                float2 uv       : TEXCOORD0,
                uint   iid      : SV_InstanceID)
            {
                SV o = (SV)0;
                uint sIdx = _VisibleIndices[iid];
                InstanceData inst = _Instances[sIdx];
                float4 tiltQ = (_TiltEnabled >= 0.5f) ? _InstanceTilt[sIdx] : float4(0,0,0,1);
                float3 posWS, nrmWS;
                TransformInstance2(inst, tiltQ, posOS.xyz, normalOS, posWS, nrmWS);

                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDir = normalize(_LightPosition - posWS);
                #else
                    float3 lightDir = _LightDirection;
                #endif

                // Phase 3: apply per-layer shadow bias offsets (0 = no-op; default is unchanged caster).
                posWS += lightDir * _ShadowDepthBias;
                posWS += nrmWS   * _ShadowNormalBias;

                // P2-Root: map painting-space posWS/nrmWS to world before shadow clip.
                float3 posWSWorld = WP_PaintingToWorldPos(posWS);
                float3 nrmWSWorld = WP_PaintingToWorldNormal(nrmWS);

                float4 clip = TransformWorldToHClip(ApplyShadowBias(posWSWorld, nrmWSWorld, lightDir));
                #if UNITY_REVERSED_Z
                    clip.z = min(clip.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    clip.z = max(clip.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = clip;
                #if defined(_ALPHACLIP_SHADOWS)
                o.uv = uv;
                #endif
                return o;
            }

            // Phase 3: _ALPHACLIP_SHADOWS clips the shadow caster by base-map alpha.
            // OFF path returns 0 immediately — byte-identical to the Phase 2 ShadowCaster.
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            float4 _BaseMap_ST;
            half4 shadowFrag(SV i) : SV_Target
            {
                #if defined(_ALPHACLIP_SHADOWS)
                    half alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a;
                    clip(alpha - _Cutoff);
                #endif
                return 0;
            }
            ENDHLSL
        }

        // ── Pass 2: DepthOnly ─────────────────────────────────────────────────
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex depthVert
            #pragma fragment depthFrag
            #pragma multi_compile_local _ SCATTER_ALIGN_TO_NORMAL
            #pragma multi_compile _ _WP_ROOT_TRANSFORM

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "WorldRootTransform.hlsl"

            struct InstanceData { float3 posWS; uint packedYawScale; uint lodHash; };
            struct SD_InteractorGpu { float3 posWS; float radius; float strength; float _p0; float _p1; float _p2; };

            StructuredBuffer<InstanceData>   _Instances;
            StructuredBuffer<uint>           _VisibleIndices;
            StructuredBuffer<SD_InteractorGpu> _Interactors;
            StructuredBuffer<float4>         _InstanceTilt;

            float _ScaleMax2;
            float _OrientMode;
            float4 _RotationOffsetEuler;
            float _WindEnabled;
            float _InteractorsEnabled;
            float _TiltEnabled;
            float4 _AnchorOffset;
            float _GrassTime; float2 _WindDir; float _WindStrength; float _WindFrequency;
            float _WindNoiseScale; float _BendStrength; int _InteractorCount;

            static const float SD_DEG_TO_RAD=0.01745329f,SD_TWO_PI=6.2831853f,SD_DPM=55.0f,SD_MAX_LEAN=80.0f;
            float3 SD_RotateByQuat(float3 v,float4 q){float3 t=2.0f*cross(q.xyz,v);return v+q.w*t+cross(q.xyz,t);}

            float3x3 SD_YawMat(float y){float a=y*SD_DEG_TO_RAD,sy=sin(a),cy=cos(a);return float3x3(cy,0,sy,0,1,0,-sy,0,cy);}
            float3x3 SD_EulMat(float p,float y,float r){p*=SD_DEG_TO_RAD;y*=SD_DEG_TO_RAD;r*=SD_DEG_TO_RAD;float sp=sin(p),cp=cos(p),sy=sin(y),cy=cos(y),sr=sin(r),cr=cos(r);return mul(float3x3(cy,0,sy,0,1,0,-sy,0,cy),mul(float3x3(1,0,0,0,cp,-sp,0,sp,cp),float3x3(cr,-sr,0,sr,cr,0,0,0,1)));}
            float3 SD_OctDec(float px,float py){float pz=1.0f-abs(px)-abs(py);if(pz<0){float ox=px;px=(1-abs(py))*(px>=0?1:-1);py=(1-abs(ox))*(py>=0?1:-1);}return normalize(float3(px,pz,py));}
            float3x3 SD_AlignMat(float3 n){float3 up=float3(0,1,0);float3 ax=cross(up,n);float s=length(ax),c=dot(up,n);if(s<1e-6)return c>0?(float3x3)1:float3x3(1,0,0,0,-1,0,0,0,1);ax/=s;float t=1-c;return float3x3(t*ax.x*ax.x+c,t*ax.x*ax.y-s*ax.z,t*ax.x*ax.z+s*ax.y,t*ax.x*ax.y+s*ax.z,t*ax.y*ax.y+c,t*ax.y*ax.z-s*ax.x,t*ax.x*ax.z-s*ax.y,t*ax.y*ax.z+s*ax.x,t*ax.z*ax.z+c);}
            float3x3 SD_BaseRot3(InstanceData inst,float yaw){float3 oe=_RotationOffsetEuler.xyz;if(_OrientMode>=0.5f){uint s2=inst.lodHash;float ox=(float)((s2>>24)&0xFFu)/255.0f*2.0f-1.0f,oy=(float)((s2>>16)&0xFFu)/255.0f*2.0f-1.0f,pd=(float)((s2>>8)&0xFFu)/255.0f*180.0f-90.0f,rd=(float)(s2&0xFFu)/255.0f*180.0f-90.0f;return mul(SD_AlignMat(SD_OctDec(ox,oy)),mul(SD_EulMat(pd,yaw,rd),SD_EulMat(oe.x,oe.y,oe.z)));}return mul(SD_YawMat(yaw),SD_EulMat(oe.x,oe.y,oe.z));}

            float3 TransformInstancePos3(InstanceData inst, float4 tiltQ, float3 lp)
            {
                uint hi=(inst.packedYawScale>>16)&0xFFFFu,lo=inst.packedYawScale&0xFFFFu;
                float yaw=(float)hi/65535.0f*360.0f, scale=(float)lo/65535.0f*_ScaleMax2;
                float3x3 baseRot=SD_BaseRot3(inst,yaw);
                float3 anchorWS=inst.posWS+mul(baseRot,_AnchorOffset.xyz*scale);
                float2 windXZ=float2(0,0);
                if(_WindEnabled>=0.5f){float ph=(anchorWS.x*0.37f+anchorWS.z*0.21f)*_WindNoiseScale*SD_TWO_PI;windXZ=_WindDir*sin(_GrassTime*_WindFrequency+ph)*_WindStrength;}
                float2 bxz=float2(0,0);
                if(_InteractorsEnabled>=0.5f){for(int i=0;i<_InteractorCount;++i){SD_InteractorGpu ip=_Interactors[i];float2 d=anchorWS.xz-ip.posWS.xz;float dl=length(d);if(ip.radius<=0||dl>=ip.radius)continue;bxz+=(dl>1e-4f?d/dl:0)*(1-dl/ip.radius)*ip.strength*_BendStrength;}}
                float3x3 leanMat=float3x3(1,0,0,0,1,0,0,0,1);
                if(_WindEnabled>=0.5f||_InteractorsEnabled>=0.5f){
                    float2 lean=windXZ+bxz; float pt=lean.y*SD_DPM,rl=-lean.x*SD_DPM,mg=sqrt(pt*pt+rl*rl);
                    if(mg>SD_MAX_LEAN){float s=SD_MAX_LEAN/mg;pt*=s;rl*=s;}
                    leanMat=SD_EulMat(pt,0,rl);
                }
                float3 baseV=mul(baseRot,lp*scale);
                if(_WindEnabled<0.5f&&_InteractorsEnabled<0.5f&&_TiltEnabled<0.5f) return inst.posWS+baseV;
                // Deform pivots about the anchor (see Forward pass TransformInstance for the rationale).
                float3 anchorRel=anchorWS-inst.posWS;
                return inst.posWS+anchorRel+SD_RotateByQuat(mul(leanMat,baseV-anchorRel),tiltQ);
            }

            struct DV { float4 positionCS : SV_POSITION; };

            DV depthVert(float4 posOS : POSITION, uint iid : SV_InstanceID)
            {
                DV o = (DV)0;
                uint dIdx = _VisibleIndices[iid];
                InstanceData inst = _Instances[dIdx];
                float4 tiltQ = (_TiltEnabled >= 0.5f) ? _InstanceTilt[dIdx] : float4(0,0,0,1);
                // P2-Root: map painting-space pos to world before clip.
                o.positionCS = TransformWorldToHClip(WP_PaintingToWorldPos(TransformInstancePos3(inst, tiltQ, posOS.xyz)));
                return o;
            }

            half4 depthFrag(DV i) : SV_Target { return 0; }
            ENDHLSL
        }
    }

    Fallback Off
}
