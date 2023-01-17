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
        //private static RenderTexture additionalShadowTexture;

        public static bool UseMainShadowTexture { get; private set; }
        public static RenderTexture MainShadowTexture { get; private set; }
        //public static Texture AdditionalShadowTexture { get => additionalShadowTexture; private set => additionalShadowTexture = value; }

        public RTHandle MainShadowTarget { get; set; }
        //public RTHandle AdditionalShadowTarget { get; set; }

        public void CreateTexture()
        {
            if (MainShadowTexture != null) MainShadowTexture.Release();
            //if (AdditionalShadowTexture != null) AdditionalShadowTexture.Release();

            var mainDesc = MainShadowTarget.rt.descriptor;

            MainShadowTexture = new(mainDesc.width, mainDesc.height, 0, RenderTextureFormat.R16);
            MainShadowTexture.Create();

            // var additionalDesc = AdditionalShadowTarget.rt.descriptor;
            // additionalDesc.colorFormat = RenderTextureFormat.R16;
            // additionalDesc.depthBufferBits = 0;

            // AdditionalShadowTexture = new(additionalDesc);
            // AdditionalShadowTexture.Create();
        }

        //* I LOVE URP!!! 😃😃😃😃😃😃
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            UniversalRenderer universalRenderer = renderingData.cameraData.renderer as UniversalRenderer;

            MainLightShadowCasterPass mainLightPass = (MainLightShadowCasterPass)typeof(UniversalRenderer).GetField("m_MainLightShadowCasterPass", flags).GetValue(universalRenderer);
            // AdditionalLightsShadowCasterPass additionalLightPass = (AdditionalLightsShadowCasterPass)typeof(UniversalRenderer).GetField("m_AdditionalLightsShadowCasterPass", flags).GetValue(universalRenderer);

            MainShadowTarget = (RTHandle)typeof(MainLightShadowCasterPass).GetField("m_MainLightShadowmapTexture", flags).GetValue(mainLightPass);
            // AdditionalShadowTarget = (RTHandle)typeof(AdditionalLightsShadowCasterPass).GetField("m_AdditionalLightsShadowmapHandle", flags).GetValue(additionalLightPass);

            ConfigureTarget(MainShadowTarget);
        }

        public bool CompareRT(Texture a, Texture b)
        {
            return a != null && b != null && a.height == b.height && a.width == b.width;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            UseMainShadowTexture = renderingData.shadowData.supportsMainLightShadows;

            if (!UseMainShadowTexture) return;

            if (!CompareRT(MainShadowTarget.rt, MainShadowTexture)) CreateTexture();

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, new ProfilingSampler("Aether Shadow Pass")))
            {
                cmd.CopyTexture(MainShadowTarget.rt, MainShadowTexture);
                cmd.SetGlobalTexture("_MainShadowTexture", MainShadowTexture);
            }
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            CommandBufferPool.Release(cmd);
        }
    }
}