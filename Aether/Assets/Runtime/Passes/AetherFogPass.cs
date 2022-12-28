using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Aether
{
    [System.Serializable]
    public class AetherFogPass : ScriptableRenderPass
    {
        const string FOG_SHADER_NAME = "ComputeFog";
        const string RAYMARCH_SHADER_NAME = "RaymarchFog";
        const string BLIT_SHADER_NAME = "Aether/FogApply";

        public AetherFogPass (AetherFogPassSettings settings)
        {
            Settings = settings;
            FogCompute = (ComputeShader)Resources.Load(FOG_SHADER_NAME);
            RaymarchCompute = (ComputeShader)Resources.Load(RAYMARCH_SHADER_NAME);
        }

        public AetherFogPassSettings Settings { get; }
        public ComputeShader FogCompute { get; }
        public ComputeShader RaymarchCompute { get; }

        public RTHandle Target { get; set; }

        [SerializeField] RenderTexture fogTexture, raymarchTexture;

        //Camera
        Camera camera;
        CameraData[] cameraData = new CameraData[1];
        ComputeBuffer cameraDataBuffer;

        AetherLight[] lights;
        LightData[] lightData;
        ComputeBuffer lightDataBuffer;

        AetherFog[] fogVolumes;
        FogData[] fogData;
        ComputeBuffer fogDataBuffer;

        Material blitMaterial;

        public static int3 GetDispatchSize (ComputeShader shader, int kernel, int3 desiredThreads)
        {
            uint3 threadGroups;
            shader.GetKernelThreadGroupSizes(kernel, out threadGroups.x, out threadGroups.y, out threadGroups.z);

            return (int3)math.ceil((float3)desiredThreads / (float3)threadGroups);
        }

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            ConfigureTarget(Target);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if(!UpdateTextures()) throw new("Failed To Update Textures!");
            if(!UpdateCamera()) throw new("Failed To Update Camera!");
            if(!UpdateLights()) throw new("Failed To Update Lights!");
            if(!UpdateFogVolumes()) throw new("Failed To Update Volumes!");
            if(!UpdateFogCompute(context)) throw new("Failed To Update Fog Compute!");
            if(!UpdateRaymarchCompute(context)) throw new("Failed To Update Raymarch Compute!");
            if(!UpdateMaterial()) throw new("Failed To Update Material!");

            Blit(context);
        }

        public void Blit (ScriptableRenderContext context)
        {
            CommandBuffer cmd = CommandBufferPool.Get();

            using (new ProfilingScope(cmd, new ProfilingSampler("Aether Blit")))
            {
                //cmd.Blit(Target, Target, blitMaterial);
                Blitter.BlitCameraTexture(cmd, Target, Target, blitMaterial, 0);
            }
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            CommandBufferPool.Release(cmd);
        }

        public void Dispose ()
        {
            cameraDataBuffer?.Dispose();
            lightDataBuffer?.Dispose();
            fogDataBuffer?.Dispose();
        }

        //* TEXTURES
        public bool UpdateTextures ()
        {
            if(fogTexture == null || raymarchTexture == null) SetupTextures();
            return true;
        }
        public void SetupTextures ()
        {
            RenderTexture reference = new (Settings.VolumeResolution.x, Settings.VolumeResolution.y, 0, RenderTextureFormat.ARGBHalf)
            {
                volumeDepth = Settings.VolumeResolution.z,
                dimension = TextureDimension.Tex3D,
                enableRandomWrite = true,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            fogTexture = reference;
            fogTexture.Create();

            raymarchTexture = reference;
            raymarchTexture.Create();
        }

        //* Camera
        public bool UpdateCamera ()
        {
            if(cameraDataBuffer == null) SetupCamera();
        
            camera = Camera.current ?? Camera.main;
            if(camera == null) return false;
            
            cameraData[0].Update(camera, Settings.ViewDistance);

            cameraDataBuffer.SetData(cameraData);

            return true;
        }
        public void SetupCamera () => cameraDataBuffer = new(cameraData.Length, CameraData.SIZE);

        //* Lights
        public bool UpdateLights ()
        {
            if(lightDataBuffer == null || lightData == null) SetupLights();
            if(lights == null) return false;
    
            for (int i = 0; i < lights.Length; i++)
            {
                if(lights[i] == null) SetupLights();

                lightData[i].Update(lights[i]);
            }

            lightDataBuffer.SetData(lightData);

            return true;
        }
        public void SetupLights ()
        {
            lights = Object.FindObjectsOfType<AetherLight>();
            lightData = new LightData[lights.Length];
            if(lightData.Length == 0) return;

            lightDataBuffer = new(lightData.Length, LightData.SIZE);
        }

        //* Fog Volumes
        public bool UpdateFogVolumes ()
        {
            if(fogDataBuffer == null || fogData == null) SetupFogVolumes();
            if(fogVolumes == null) return false;

            for (int i = 0; i < fogVolumes.Length; i++)
            {
                if(fogVolumes[i] == null) SetupFogVolumes();

                fogData[i].Update(fogVolumes[i]);
            }

            fogDataBuffer.SetData(fogData);

            return true;
        }
        public void SetupFogVolumes ()
        {
            fogVolumes = Object.FindObjectsOfType<AetherFog>();
            fogData = new FogData[fogVolumes.Length];
            if(fogData.Length == 0) return;

            fogDataBuffer = new(fogData.Length, FogData.SIZE);
        }

        //* Fog Compute
        public bool UpdateFogCompute (ScriptableRenderContext context)
        {
            var kernel = FogCompute.FindKernel("ComputeFog");

            FogCompute.SetTexture(kernel, "fogTexture", fogTexture);
            FogCompute.SetVector("fogTextureResolution", new(Settings.VolumeResolution.x, Settings.VolumeResolution.y, Settings.VolumeResolution.z));

            FogCompute.SetBuffer(kernel, "lightData", lightDataBuffer);
            FogCompute.SetInt("lightDataLength", lightData.Length);

            FogCompute.SetBuffer(kernel, "fogData", fogDataBuffer);
            FogCompute.SetInt("fogDataLength", fogData.Length);

            FogCompute.SetBuffer(kernel, "cameraData", cameraDataBuffer);

            FogCompute.SetFloat("time", Time.unscaledTime);

            //FogCompute.SetInt("sampleCount", Settings.SampleCount);

            FogCompute.SetTexture(kernel, "mainShadowTexture", AetherShadowPass.MainShadowTexture);

            FogCompute.SetTexture(kernel, "additionalShadowTexture", AetherShadowPass.AdditionalShadowTexture);

            int3 dispatchSize = GetDispatchSize(FogCompute, kernel, Settings.VolumeResolution);

            CommandBuffer cmd = CommandBufferPool.Get();

            using (new ProfilingScope(cmd, new ProfilingSampler("Aether Fog Compute")))
            {
                cmd.DispatchCompute(FogCompute, kernel, dispatchSize.x, dispatchSize.y, dispatchSize.z);
            }

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            CommandBufferPool.Release(cmd);

            return true;
        }

        //* Raymarch Compute
        public bool UpdateRaymarchCompute (ScriptableRenderContext context)
        {
            var kernel = RaymarchCompute.FindKernel("RaymarchFog");

            RaymarchCompute.SetTexture(kernel, "raymarchTexture", raymarchTexture);
            RaymarchCompute.SetTexture(kernel, "fogTexture", fogTexture);

            RaymarchCompute.SetInt("depthResolution", Settings.VolumeResolution.z);

            int3 dispatchSize = GetDispatchSize(RaymarchCompute, kernel, Settings.VolumeResolution);

            CommandBuffer cmd = CommandBufferPool.Get();

            using (new ProfilingScope(cmd, new ProfilingSampler("Aether Raymarch Compute")))
            {
                cmd.DispatchCompute(RaymarchCompute, kernel, dispatchSize.x, dispatchSize.y, 1);
            }

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            CommandBufferPool.Release(cmd);

            return true;
        }

        //* Update Material
        public bool UpdateMaterial ()
        {
            if(blitMaterial == null) SetupMaterial();

            blitMaterial.SetTexture("_Volume", raymarchTexture);
            blitMaterial.SetFloat("_fogFar", Settings.ViewDistance);
            blitMaterial.SetFloat("_cameraFar", camera.farClipPlane);

            return true;
        }
        public void SetupMaterial ()
        {
            blitMaterial = new(Shader.Find(BLIT_SHADER_NAME));
        }
    }

    [System.Serializable]
    public class AetherFogPassSettings
    {
        public int3 VolumeResolution = new(160, 90, 128);
        //public int SampleCount = 5;
        public float ViewDistance = 70;
    }
}