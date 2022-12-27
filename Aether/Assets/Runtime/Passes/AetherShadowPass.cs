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
        public static RenderTexture shadowTexture;

        public RTHandle Target { get; set; }

        public void CreateTexture ()
        {
            shadowTexture = new(Target.rt.width, Target.rt.height, 0, RenderTextureFormat.R16);
            shadowTexture.Create();
        }

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            ConfigureTarget(Target);
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            UniversalRenderer universalRenderer = renderingData.cameraData.renderer as UniversalRenderer;

            MainLightShadowCasterPass mainLightPass = (MainLightShadowCasterPass)typeof(UniversalRenderer).GetField("m_MainLightShadowCasterPass", flags).GetValue(universalRenderer);

            Target = (RTHandle)typeof(MainLightShadowCasterPass).GetField("m_MainLightShadowmapTexture", flags).GetValue(mainLightPass);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if(shadowTexture == null) CreateTexture();

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, new ProfilingSampler("Aether Shadow Pass")))
            {
                cmd.CopyTexture(Target, shadowTexture);
                cmd.SetGlobalTexture("_MainShadow", shadowTexture);
            }
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            CommandBufferPool.Release(cmd);
        }
    }
}