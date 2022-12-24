#if !SHADERGRAPH_PREVIEW
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#endif

#ifndef CUSTOM_DEPTH
#define CUSTOM_DEPTH

void Depth_float (float2 UV, out float Depth)
{
    #if SHADERGRAPH_PREVIEW
        Depth = 0;
    #else
        Depth = Linear01Depth(SampleSceneDepth(UV), _ZBufferParams);
    #endif
}

#endif