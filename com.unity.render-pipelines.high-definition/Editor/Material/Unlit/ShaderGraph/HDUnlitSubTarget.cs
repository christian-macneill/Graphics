using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEditor.ShaderGraph;
using UnityEditor.ShaderGraph.Internal;
using UnityEditor.Graphing;
using UnityEditor.ShaderGraph.Legacy;
using UnityEditor.Rendering.HighDefinition.ShaderGraph.Legacy;
using static UnityEngine.Rendering.HighDefinition.HDMaterialProperties;
using static UnityEditor.Rendering.HighDefinition.HDShaderUtils;

namespace UnityEditor.Rendering.HighDefinition.ShaderGraph
{
    sealed partial class HDUnlitSubTarget : SurfaceSubTarget, ILegacyTarget, IRequiresData<HDUnlitData>
    {
        public HDUnlitSubTarget() => displayName = "Unlit";


        // TODO: remove this line
        public static string passTemplatePath => $"{HDUtils.GetHDRenderPipelinePath()}Editor/Material/Unlit/ShaderGraph/HDUnlitPass.template";

        // Templates
        // TODO: Why do the raytracing passes use the template for the pipeline agnostic Unlit master node?
        // TODO: This should be resolved so we can delete the second pass template
        static string passTemplatePath => $"{HDUtils.GetHDRenderPipelinePath()}Editor/Material/Unlit/ShaderGraph/HDUnlitPass.template";
        protected override string templatePath => $"{HDUtils.GetHDRenderPipelinePath()}Editor/Material/Unlit/ShaderGraph/HDUnlitPass.template";
        protected override ShaderID shaderID => HDShaderUtils.ShaderID.SG_Unlit;
        protected override string renderType => HDRenderTypeTags.HDUnlitShader.ToString();
        protected override string subTargetAssetGuid => "4516595d40fa52047a77940183dc8e74"; // HDUnlitSubTarget
        protected override string customInspector => "Rendering.HighDefinition.HDUnlitGUI";

        protected override bool supportDistortion => true;

        HDUnlitData m_UnlitData;

        HDUnlitData IRequiresData<HDUnlitData>.data
        {
            get => m_UnlitData;
            set => m_UnlitData = value;
        }

        public HDUnlitData unlitData
        {
            get => m_UnlitData;
            set => m_UnlitData = value;
        }

        protected override SubShaderDescriptor GetSubShaderDescriptor()
        {
            if (unlitData.distortionOnly)
            {
                return new SubShaderDescriptor
                {
                    generatesPreview = true,
                    passes = new PassCollection{ distortionPass }
                };
                // TODO
            }
            else
            {
                return base.GetSubShaderDescriptor();
            }
        }

        public override void GetFields(ref TargetFieldContext context)
        {
            base.GetFields(ref context);

            // Unlit specific properties
            context.AddField(HDFields.EnableShadowMatte,            unlitData.enableShadowMatte);
            context.AddField(HDFields.DoAlphaTest,                  systemData.alphaTest && context.pass.validPixelBlocks.Contains(BlockFields.SurfaceDescription.AlphaClipThreshold));
        }

        public override void GetActiveBlocks(ref TargetActiveBlockContext context)
        {
            base.GetActiveBlocks(ref context);

            // Unlit specific blocks
            context.AddBlock(HDBlockFields.SurfaceDescription.ShadowTint,       unlitData.enableShadowMatte);
        }

        protected override void AddInspectorPropertyBlocks(SubTargetPropertiesGUI blockList)
        {
            blockList.AddPropertyBlock(new HDUnlitSurfaceOptionPropertyBlock(SurfaceOptionPropertyBlock.Features.Unlit, unlitData));
            if (systemData.surfaceType == SurfaceType.Transparent)
                blockList.AddPropertyBlock(new DistortionPropertyBlock());
            blockList.AddPropertyBlock(new AdvancedOptionsPropertyBlock());
        }

        public override void CollectShaderProperties(PropertyCollector collector, GenerationMode generationMode)
        {
            base.CollectShaderProperties(collector, generationMode);
    
            if (unlitData.enableShadowMatte)
            {
                uint mantissa = ((uint)LightFeatureFlags.Punctual | (uint)LightFeatureFlags.Directional | (uint)LightFeatureFlags.Area) & 0x007FFFFFu;
                uint exponent = 0b10000000u; // 0 as exponent
                collector.AddShaderProperty(new Vector1ShaderProperty
                {
                    hidden = true,
                    value = HDShadowUtils.Asfloat((exponent << 23) | mantissa),
                    overrideReferenceName = HDMaterialProperties.kShadowMatteFilter
                });
            }

            // Stencil state for unlit:
            HDSubShaderUtilities.AddStencilShaderProperties(collector, systemData, null);
        }

#region SubShaders
        static class SubShaders
        {
            public static SubShaderDescriptor Unlit = new SubShaderDescriptor()
            {
                pipelineTag = HDRenderPipeline.k_ShaderTagName,
                generatesPreview = true,
                passes = new PassCollection
                {
                    { UnlitPasses.ShadowCaster },
                    { UnlitPasses.META },
                    { UnlitPasses.SceneSelection },
                    { UnlitPasses.DepthForwardOnly },
                    { UnlitPasses.MotionVectors },
                    // { UnlitPasses.Distortion, new FieldCondition(HDFields.TransparentDistortion, true) },
                    { UnlitPasses.ForwardOnly },
                },
            };

            public static SubShaderDescriptor UnlitRaytracing = new SubShaderDescriptor()
            {
                pipelineTag = HDRenderPipeline.k_ShaderTagName,
                generatesPreview = false,
                passes = new PassCollection
                {
                    { UnlitPasses.RaytracingIndirect, new FieldCondition(Fields.IsPreview, false) },
                    { UnlitPasses.RaytracingVisibility, new FieldCondition(Fields.IsPreview, false) },
                    { UnlitPasses.RaytracingForward, new FieldCondition(Fields.IsPreview, false) },
                    { UnlitPasses.RaytracingGBuffer, new FieldCondition(Fields.IsPreview, false) },
                    { UnlitPasses.RaytracingPathTracing, new FieldCondition(Fields.IsPreview, false) },
                },
            };
        }
#endregion

#region Passes
        static class UnlitPasses
        {
            public static PassDescriptor META = new PassDescriptor()
            {
                // Definition
                displayName = "META",
                referenceName = "SHADERPASS_LIGHT_TRANSPORT",
                lightMode = "META",
                useInPreview = false,

                // Template
                passTemplatePath = passTemplatePath,
                sharedTemplateDirectory = HDTarget.sharedTemplateDirectory,

                // Block Mask
                validPixelBlocks = UnlitBlockMasks.FragmentDefault,

                // Collections
                structs = CoreStructCollections.Default,
                requiredFields = new FieldCollection(){ CoreRequiredFields.Meta, HDFields.SubShader.Unlit },
                fieldDependencies = CoreFieldDependencies.Default,
                renderStates = CoreRenderStates.Meta,
                pragmas = CorePragmas.DotsInstancedInV2Only,
                keywords = CoreKeywords.HDBase,
                includes = UnlitIncludes.Meta,
            };

            public static PassDescriptor ShadowCaster = new PassDescriptor()
            {
                // Definition
                displayName = "ShadowCaster",
                referenceName = "SHADERPASS_SHADOWS",
                lightMode = "ShadowCaster",
                useInPreview = false,

                // Template
                passTemplatePath = passTemplatePath,
                sharedTemplateDirectory = HDTarget.sharedTemplateDirectory,

                // Block Mask
                validVertexBlocks = CoreBlockMasks.Vertex,
                validPixelBlocks = UnlitBlockMasks.FragmentOnlyAlpha,

                // Collections
                structs = CoreStructCollections.Default,
                requiredFields = new FieldCollection(){ HDFields.SubShader.Unlit },
                fieldDependencies = CoreFieldDependencies.Default,
                renderStates = CoreRenderStates.ShadowCaster,
                pragmas = CorePragmas.DotsInstancedInV2Only,
                keywords = CoreKeywords.HDBase,
                includes = UnlitIncludes.DepthOnly,
            };

            public static PassDescriptor SceneSelection = new PassDescriptor()
            {
                // Definition
                displayName = "SceneSelectionPass",
                referenceName = "SHADERPASS_DEPTH_ONLY",
                lightMode = "SceneSelectionPass",
                useInPreview = false,

                // Template
                passTemplatePath = passTemplatePath,
                sharedTemplateDirectory = HDTarget.sharedTemplateDirectory,

                // Block Mask
                validVertexBlocks = CoreBlockMasks.Vertex,
                validPixelBlocks = UnlitBlockMasks.FragmentOnlyAlpha,

                // Collections
                structs = CoreStructCollections.Default,
                requiredFields = new FieldCollection(){ HDFields.SubShader.Unlit },
                fieldDependencies = CoreFieldDependencies.Default,
                renderStates = UnlitRenderStates.SceneSelection,
                pragmas = CorePragmas.DotsInstancedInV2OnlyEditorSync,
                defines = CoreDefines.SceneSelection,
                keywords = CoreKeywords.HDBase,
                includes = UnlitIncludes.DepthOnly,
            };

            public static PassDescriptor DepthForwardOnly = new PassDescriptor()
            {
                // Definition
                displayName = "DepthForwardOnly",
                referenceName = "SHADERPASS_DEPTH_ONLY",
                lightMode = "DepthForwardOnly",
                useInPreview = true,

                // Template
                passTemplatePath = passTemplatePath,
                sharedTemplateDirectory = HDTarget.sharedTemplateDirectory,

                // Block Mask
                validVertexBlocks = CoreBlockMasks.Vertex,
                validPixelBlocks = UnlitBlockMasks.FragmentOnlyAlpha,

                // Collections
                structs = CoreStructCollections.Default,
                requiredFields = new FieldCollection(){ HDFields.SubShader.Unlit },
                fieldDependencies = CoreFieldDependencies.Default,
                renderStates = UnlitRenderStates.DepthForwardOnly,
                pragmas = CorePragmas.DotsInstancedInV2Only,
                keywords = UnlitKeywords.DepthMotionVectors,
                includes = UnlitIncludes.DepthOnly,
            };

            public static PassDescriptor MotionVectors = new PassDescriptor()
            {
                // Definition
                displayName = "MotionVectors",
                referenceName = "SHADERPASS_MOTION_VECTORS",
                lightMode = "MotionVectors",
                useInPreview = false,

                // Template
                passTemplatePath = passTemplatePath,
                sharedTemplateDirectory = HDTarget.sharedTemplateDirectory,

                // Block Mask
                validVertexBlocks = CoreBlockMasks.Vertex,
                validPixelBlocks = UnlitBlockMasks.FragmentOnlyAlpha,

                // Collections
                structs = CoreStructCollections.Default,
                requiredFields = new FieldCollection(){ CoreRequiredFields.PositionRWS, HDFields.SubShader.Unlit },
                fieldDependencies = CoreFieldDependencies.Default,
                renderStates = UnlitRenderStates.MotionVectors,
                pragmas = CorePragmas.DotsInstancedInV2Only,
                keywords = UnlitKeywords.DepthMotionVectors,
                includes = UnlitIncludes.MotionVectors,
            };

            public static PassDescriptor ForwardOnly = new PassDescriptor()
            {
                // Definition
                displayName = "ForwardOnly",
                referenceName = "SHADERPASS_FORWARD_UNLIT",
                lightMode = "ForwardOnly",
                useInPreview = true,

                // Template
                passTemplatePath = passTemplatePath,
                sharedTemplateDirectory = HDTarget.sharedTemplateDirectory,

                // Block Mask
                validVertexBlocks = CoreBlockMasks.Vertex,
                validPixelBlocks = UnlitBlockMasks.FragmentForward,

                // Collections
                structs = CoreStructCollections.Default,
                requiredFields = new FieldCollection(){ HDFields.SubShader.Unlit },
                fieldDependencies = CoreFieldDependencies.Default,
                renderStates = CoreRenderStates.Forward,
                pragmas = CorePragmas.DotsInstancedInV2Only,
                keywords = UnlitKeywords.Forward,
                includes = UnlitIncludes.ForwardOnly,

                virtualTextureFeedback = true,
            };

            public static PassDescriptor RaytracingIndirect = new PassDescriptor()
            {
                // Definition
                displayName = "IndirectDXR",
                referenceName = "SHADERPASS_RAYTRACING_INDIRECT",
                lightMode = "IndirectDXR",
                useInPreview = false,

                // Template
                passTemplatePath = passTemplatePath,
                sharedTemplateDirectory = HDTarget.sharedTemplateDirectory,

                // Block Mask
                validVertexBlocks = CoreBlockMasks.Vertex,
                validPixelBlocks = UnlitBlockMasks.FragmentDefault,

                // Collections
                structs = CoreStructCollections.Default,
                fieldDependencies = CoreFieldDependencies.Default,
                pragmas = CorePragmas.RaytracingBasic,
                keywords = CoreKeywords.HDBase,
                includes = CoreIncludes.Raytracing,
                requiredFields = new FieldCollection(){ HDFields.SubShader.Unlit, HDFields.ShaderPass.RaytracingIndirect },
            };

            public static PassDescriptor RaytracingVisibility = new PassDescriptor()
            {
                // Definition
                displayName = "VisibilityDXR",
                referenceName = "SHADERPASS_RAYTRACING_VISIBILITY",
                lightMode = "VisibilityDXR",
                useInPreview = false,

                // Template
                passTemplatePath = passTemplatePath,
                sharedTemplateDirectory = HDTarget.sharedTemplateDirectory,

                // Block Mask
                validVertexBlocks = CoreBlockMasks.Vertex,
                validPixelBlocks = UnlitBlockMasks.FragmentDefault,

                // Collections
                structs = CoreStructCollections.Default,
                fieldDependencies = CoreFieldDependencies.Default,
                pragmas = CorePragmas.RaytracingBasic,
                includes = CoreIncludes.Raytracing,
                keywords = CoreKeywords.RaytracingVisiblity,
                requiredFields = new FieldCollection(){ HDFields.SubShader.Unlit, HDFields.ShaderPass.RaytracingVisibility },
            };

            public static PassDescriptor RaytracingForward = new PassDescriptor()
            {
                // Definition
                displayName = "ForwardDXR",
                referenceName = "SHADERPASS_RAYTRACING_FORWARD",
                lightMode = "ForwardDXR",
                useInPreview = false,

                // Template
                passTemplatePath = passTemplatePath,
                sharedTemplateDirectory = HDTarget.sharedTemplateDirectory,

                // Block Mask
                validVertexBlocks = CoreBlockMasks.Vertex,
                validPixelBlocks = UnlitBlockMasks.FragmentDefault,

                // Collections
                structs = CoreStructCollections.Default,
                fieldDependencies = CoreFieldDependencies.Default,
                pragmas = CorePragmas.RaytracingBasic,
                keywords = CoreKeywords.HDBase,
                includes = CoreIncludes.Raytracing,
                requiredFields = new FieldCollection(){ HDFields.SubShader.Unlit, HDFields.ShaderPass.RaytracingForward },
            };

            public static PassDescriptor RaytracingGBuffer = new PassDescriptor()
            {
                // Definition
                displayName = "GBufferDXR",
                referenceName = "SHADERPASS_RAYTRACING_GBUFFER",
                lightMode = "GBufferDXR",
                useInPreview = false,

                // Template
                passTemplatePath = passTemplatePath,
                sharedTemplateDirectory = HDTarget.sharedTemplateDirectory,

                // Block Mask
                validVertexBlocks = CoreBlockMasks.Vertex,
                validPixelBlocks = UnlitBlockMasks.FragmentDefault,

                // Collections
                structs = CoreStructCollections.Default,
                fieldDependencies = CoreFieldDependencies.Default,
                pragmas = CorePragmas.RaytracingBasic,
                keywords = CoreKeywords.HDBase,
                includes = CoreIncludes.Raytracing,
                requiredFields = new FieldCollection(){ HDFields.SubShader.Unlit, HDFields.ShaderPass.RayTracingGBuffer },
            };

            public static PassDescriptor RaytracingPathTracing = new PassDescriptor()
            {
                //Definition
                displayName = "PathTracingDXR",
                referenceName = "SHADERPASS_PATH_TRACING",
                lightMode = "PathTracingDXR",
                useInPreview = false,

                // Template
                passTemplatePath = passTemplatePath,
                sharedTemplateDirectory = HDTarget.sharedTemplateDirectory,

                // Block Mask
                validVertexBlocks = CoreBlockMasks.Vertex,
                validPixelBlocks = UnlitBlockMasks.FragmentDefault,

                // Collections
                structs = CoreStructCollections.Default,
                fieldDependencies = CoreFieldDependencies.Default,
                pragmas = CorePragmas.RaytracingBasic,
                keywords = CoreKeywords.HDBaseNoCrossFade,
                includes = CoreIncludes.Raytracing,
                requiredFields = new FieldCollection(){ HDFields.SubShader.Unlit, HDFields.ShaderPass.RaytracingPathTracing },
            };
        }
#endregion

#region BlockMasks
        static class UnlitBlockMasks
        {
            public static BlockFieldDescriptor[] FragmentDefault = new BlockFieldDescriptor[]
            {
                BlockFields.SurfaceDescription.BaseColor,
                BlockFields.SurfaceDescription.Alpha,
                BlockFields.SurfaceDescription.AlphaClipThreshold,
                BlockFields.SurfaceDescription.Emission,
            };

            public static BlockFieldDescriptor[] FragmentOnlyAlpha = new BlockFieldDescriptor[]
            {
                BlockFields.SurfaceDescription.Alpha,
                BlockFields.SurfaceDescription.AlphaClipThreshold,
            };

            public static BlockFieldDescriptor[] FragmentDistortion = new BlockFieldDescriptor[]
            {
                BlockFields.SurfaceDescription.Alpha,
                BlockFields.SurfaceDescription.AlphaClipThreshold,
                HDBlockFields.SurfaceDescription.Distortion,
                HDBlockFields.SurfaceDescription.DistortionBlur,
            };

            public static BlockFieldDescriptor[] FragmentForward = new BlockFieldDescriptor[]
            {
                BlockFields.SurfaceDescription.BaseColor,
                BlockFields.SurfaceDescription.Alpha,
                BlockFields.SurfaceDescription.AlphaClipThreshold,
                BlockFields.SurfaceDescription.Emission,
                HDBlockFields.SurfaceDescription.ShadowTint,
            };
        }
#endregion

#region RenderStates
        static class UnlitRenderStates
        {
            public static RenderStateCollection SceneSelection = new RenderStateCollection
            {
                { RenderState.Cull(CoreRenderStates.Uniforms.cullMode) },
                { RenderState.ZWrite(ZWrite.On) },
                { RenderState.ColorMask("ColorMask 0") },
            };

            // Caution: When using MSAA we have normal and depth buffer bind.
            // Unlit objects need to NOT write in normal buffer (or write 0) - Disable color mask for this RT
            // Note: ShaderLab doesn't allow to have a variable on the second parameter of ColorMask
            // - When MSAA: disable target 1 (normal buffer)
            // - When no MSAA: disable target 0 (normal buffer) and 1 (unused)
            public static RenderStateCollection DepthForwardOnly = new RenderStateCollection
            {
                { RenderState.Cull(CoreRenderStates.Uniforms.cullMode) },
                { RenderState.ZWrite(ZWrite.On) },
                { RenderState.ColorMask("ColorMask [_ColorMaskNormal]") },
                { RenderState.ColorMask("ColorMask 0 1") },
                { RenderState.AlphaToMask(CoreRenderStates.Uniforms.alphaToMask), new FieldCondition(Fields.AlphaToMask, true) },
                { RenderState.Stencil(new StencilDescriptor()
                {
                    WriteMask = CoreRenderStates.Uniforms.stencilWriteMaskDepth,
                    Ref = CoreRenderStates.Uniforms.stencilRefDepth,
                    Comp = "Always",
                    Pass = "Replace",
                }) },
            };

            // Caution: When using MSAA we have motion vector, normal and depth buffer bind.
            // Mean unlit object need to not write in it (or write 0) - Disable color mask for this RT
            // This is not a problem in no MSAA mode as there is no buffer bind
            public static RenderStateCollection MotionVectors = new RenderStateCollection
            {
                { RenderState.Cull(CoreRenderStates.Uniforms.cullMode) },
                { RenderState.ZWrite(ZWrite.On) },
                { RenderState.ColorMask("ColorMask [_ColorMaskNormal] 1") },
                { RenderState.ColorMask("ColorMask 0 2") },
                { RenderState.AlphaToMask(CoreRenderStates.Uniforms.alphaToMask), new FieldCondition(Fields.AlphaToMask, true) },
                { RenderState.Stencil(new StencilDescriptor()
                {
                    WriteMask = CoreRenderStates.Uniforms.stencilWriteMaskMV,
                    Ref = CoreRenderStates.Uniforms.stencilRefMV,
                    Comp = "Always",
                    Pass = "Replace",
                }) },
            };
        }
        #endregion

#region Defines
        static class UnlitDefines
        {
            public static DefineCollection RaytracingForward = new DefineCollection
            {
                { RayTracingNode.GetRayTracingKeyword(), 0 },
            };

            public static DefineCollection RaytracingIndirect = new DefineCollection
            {
                { RayTracingNode.GetRayTracingKeyword(), 1 },
            };

            public static DefineCollection RaytracingVisibility = new DefineCollection
            {
                { RayTracingNode.GetRayTracingKeyword(), 1 },
            };

            public static DefineCollection RaytracingGBuffer = new DefineCollection
            {
                { RayTracingNode.GetRayTracingKeyword(), 1 },
            };
        }
#endregion

#region Keywords
        static class UnlitKeywords
        {
            public static KeywordCollection DepthMotionVectors = new KeywordCollection
            {
                { CoreKeywords.HDBase },
                { CoreKeywordDescriptors.WriteMsaaDepth },
                { CoreKeywordDescriptors.AlphaToMask, new FieldCondition(Fields.AlphaToMask, true) },
            };

            public static KeywordCollection Forward = new KeywordCollection
            {
                { CoreKeywords.HDBase },
                { CoreKeywordDescriptors.DebugDisplay },
                { CoreKeywordDescriptors.Shadow, new FieldCondition(HDFields.EnableShadowMatte, true) },
            };
        }
#endregion
        protected override string subShaderInclude => CoreIncludes.kUnlit;

#region Includes
        static class UnlitIncludes
        {
            const string kPassForwardUnlit = "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPassForwardUnlit.hlsl";
            
            public static IncludeCollection Meta = new IncludeCollection
            {
                { CoreIncludes.CorePregraph },
                { CoreIncludes.kUnlit, IncludeLocation.Pregraph },
                { CoreIncludes.CoreUtility },
                { CoreIncludes.kShaderGraphFunctions, IncludeLocation.Pregraph },
                { CoreIncludes.kPassLightTransport, IncludeLocation.Postgraph },
            };

            public static IncludeCollection DepthOnly = new IncludeCollection
            {
                { CoreIncludes.CorePregraph },
                { CoreIncludes.kUnlit, IncludeLocation.Pregraph },
                { CoreIncludes.CoreUtility },
                { CoreIncludes.kShaderGraphFunctions, IncludeLocation.Pregraph },
                { CoreIncludes.kPassDepthOnly, IncludeLocation.Postgraph },
            };

            public static IncludeCollection MotionVectors = new IncludeCollection
            {
                { CoreIncludes.CorePregraph },
                { CoreIncludes.kUnlit, IncludeLocation.Pregraph },
                { CoreIncludes.CoreUtility },
                { CoreIncludes.kShaderGraphFunctions, IncludeLocation.Pregraph },
                { CoreIncludes.kPassMotionVectors, IncludeLocation.Postgraph },
            };

            public static IncludeCollection Distortion = new IncludeCollection
            {
                { CoreIncludes.CorePregraph },
                { CoreIncludes.kUnlit, IncludeLocation.Pregraph },
                { CoreIncludes.CoreUtility },
                { CoreIncludes.kShaderGraphFunctions, IncludeLocation.Pregraph },
                { CoreIncludes.kDisortionVectors, IncludeLocation.Postgraph },
            };

            public static IncludeCollection ForwardOnly = new IncludeCollection
            {
                { CoreIncludes.CorePregraph },
                { CoreIncludes.kUnlit, IncludeLocation.Pregraph },
                { CoreIncludes.CoreUtility },
                { CoreIncludes.kShaderGraphFunctions, IncludeLocation.Pregraph },
                { CoreIncludes.kHDShadow, IncludeLocation.Pregraph, new FieldCondition(HDFields.EnableShadowMatte, true) },
                { CoreIncludes.kLightLoopDef, IncludeLocation.Pregraph, new FieldCondition(HDFields.EnableShadowMatte, true) },
                { CoreIncludes.kPunctualLightCommon, IncludeLocation.Pregraph, new FieldCondition(HDFields.EnableShadowMatte, true) },
                { CoreIncludes.kHDShadowLoop, IncludeLocation.Pregraph, new FieldCondition(HDFields.EnableShadowMatte, true) },
                { kPassForwardUnlit, IncludeLocation.Postgraph },
            };
        }
#endregion
    }
}
