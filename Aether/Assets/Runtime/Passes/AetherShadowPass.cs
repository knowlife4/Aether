using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.Universal.Internal;

namespace Aether
{
    [System.Serializable]
    public class AetherShadowPass : ScriptableRenderPass
    {
        public static RenderTexture MainShadowTexture;
        public static RenderTexture AdditionalShadowTexture;

        public RTHandle MainShadowTarget { get; set; }
        public RTHandle AdditionalShadowTarget { get; set; }

        public void CreateTexture ()
        {
            var mainDesc = MainShadowTarget.rt.descriptor;
            mainDesc.colorFormat = RenderTextureFormat.R16;
            mainDesc.depthBufferBits = 0;

            MainShadowTexture = new(mainDesc);
            MainShadowTexture.Create();

            var additionalDesc = AdditionalShadowTarget.rt.descriptor;
            additionalDesc.colorFormat = RenderTextureFormat.R16;
            additionalDesc.depthBufferBits = 0;

            AdditionalShadowTexture = new(additionalDesc);
            AdditionalShadowTexture.Create();
        }

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            ConfigureTarget(MainShadowTarget);
            ConfigureTarget(AdditionalShadowTarget);
        }

        //* I LOVE URP!!! 😃😃😃😃😃😃
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            UniversalRenderer universalRenderer = renderingData.cameraData.renderer as UniversalRenderer;

            MainLightShadowCasterPass mainLightPass = (MainLightShadowCasterPass)typeof(UniversalRenderer).GetField("m_MainLightShadowCasterPass", flags).GetValue(universalRenderer);
            AdditionalLightsShadowCasterPass additionalLightPass = (AdditionalLightsShadowCasterPass)typeof(UniversalRenderer).GetField("m_AdditionalLightsShadowCasterPass", flags).GetValue(universalRenderer);

            MainShadowTarget = (RTHandle)typeof(MainLightShadowCasterPass).GetField("m_MainLightShadowmapTexture", flags).GetValue(mainLightPass);
            AdditionalShadowTarget = (RTHandle)typeof(AdditionalLightsShadowCasterPass).GetField("m_AdditionalLightsShadowmapHandle", flags).GetValue(additionalLightPass);
        }

        public bool CompareDesc (RenderTextureDescriptor a, RenderTextureDescriptor b)
        {
            return a.height == b.height && a.width == b.width;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if(!CompareDesc(MainShadowTarget.rt.descriptor, AdditionalShadowTarget.rt.descriptor)) CreateTexture();

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, new ProfilingSampler("Aether Shadow Pass")))
            {
                cmd.CopyTexture(MainShadowTarget, MainShadowTexture);
                cmd.SetGlobalTexture("_MainShadowTexture", MainShadowTexture);

                cmd.CopyTexture(AdditionalShadowTarget, AdditionalShadowTexture);
                cmd.SetGlobalTexture("_AdditionalShadowTexture", AdditionalShadowTexture);
            }
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            CommandBufferPool.Release(cmd);
        }
    }
}