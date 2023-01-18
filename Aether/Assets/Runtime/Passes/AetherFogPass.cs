using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Aether
{
    [System.Serializable]
    public class AetherFogPass : ScriptableRenderPass
    {
        const string FOG_SHADER_NAME = "ComputeFog";
        const string RAYMARCH_SHADER_NAME = "RaymarchFog";
        const string BLIT_SHADER_NAME = "Aether/FogApply";
        const string BLUE_NOISE_NAME = "BlueNoise";

        public AetherFogPass (AetherFogPassSettings settings)
        {
            Settings = settings;
            FogCompute = (ComputeShader)Resources.Load(FOG_SHADER_NAME);
            RaymarchCompute = (ComputeShader)Resources.Load(RAYMARCH_SHADER_NAME);
            blueNoise = (Texture)Resources.Load(BLUE_NOISE_NAME);

            SceneManager.sceneLoaded += OnSceneLoad;
        }

        public AetherFogPassSettings Settings { get; }
        public ComputeShader FogCompute { get; }
        public ComputeShader RaymarchCompute { get; }

        public RTHandle Target { get; set; }

        [SerializeField] RenderTexture previousFogTexture, fogTexture;

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

        Texture blueNoise;

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
            if(!UpdateTextures()) return;
            if(!UpdateCamera()) return;
            if(!UpdateLights()) return;
            if(!UpdateFogVolumes()) return;
            if(!UpdateFogCompute(context)) return;
            if(!UpdateRaymarchCompute(context)) return;
            if(!UpdateMaterial()) return;

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

        public void OnSceneLoad (Scene scene, LoadSceneMode mode)
        {
            Dispose();
            SetupCamera();
            SetupLights();
            SetupFogVolumes();
        }

        public void Dispose ()
        {
            DisposeTextures();
            DisposeCamera();
            DisposeLights();
            DisposeFogVolumes();

            Debug.Log("Disposing!");

            SceneManager.sceneLoaded -= OnSceneLoad;
        }

        //* TEXTURES
        public bool UpdateTextures ()
        {
            if(fogTexture == null || previousFogTexture == null) SetupTextures();
            return true;
        }
        public void SetupTextures ()
        {
            RenderTextureDescriptor desc = new(Settings.VolumeResolution.x, Settings.VolumeResolution.y, RenderTextureFormat.ARGBHalf)
            {
                volumeDepth = Settings.VolumeResolution.z,
                dimension = TextureDimension.Tex3D,
                enableRandomWrite = true,
            };
            
            DisposeTextures();

            previousFogTexture = new(desc);
            previousFogTexture.Create();
            previousFogTexture.name = "previousFogTexture";

            fogTexture = new(desc);
            fogTexture.Create();
            fogTexture.name = "fogTexture";
        }
        public void DisposeTextures ()
        {
            
            if(previousFogTexture != null)
            {
                previousFogTexture.Release();
                Object.DestroyImmediate(previousFogTexture);
            }
            
            if(fogTexture != null)
            {
                fogTexture.Release();
                Object.DestroyImmediate(fogTexture);
            }
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
        public void SetupCamera ()
        {
            DisposeCamera();
            cameraDataBuffer = new(cameraData.Length, CameraData.SIZE);
        }
        public void DisposeCamera() => cameraDataBuffer?.Release();

        //* Lights
        public bool UpdateLights ()
        {
            if(lightDataBuffer == null || lightData == null) SetupLights();
            if(lightDataBuffer == null) return false;
    
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

            DisposeLights();
            
            lightDataBuffer = new(lightData.Length, LightData.SIZE);
        }
        public void DisposeLights() => lightDataBuffer?.Release();

        //* Fog Volumes
        public bool UpdateFogVolumes ()
        {
            if(fogDataBuffer == null || fogData == null) SetupFogVolumes();
            if(fogDataBuffer == null) return false;

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

            DisposeFogVolumes();
            
            fogDataBuffer = new(fogData.Length, FogData.SIZE);
        }
        public void DisposeFogVolumes() => fogDataBuffer?.Release();

        //* Fog Compute
        public bool UpdateFogCompute (ScriptableRenderContext context)
        {
            var kernel = FogCompute.FindKernel("ComputeFog");

            FogCompute.SetBool("useMainShadowTexture", AetherShadowPass.UseMainShadowTexture);

            int3 dispatchSize = GetDispatchSize(FogCompute, kernel, Settings.VolumeResolution);

            CommandBuffer cmd = CommandBufferPool.Get();

            cmd.SetExecutionFlags(CommandBufferExecutionFlags.AsyncCompute);

            using (new ProfilingScope(cmd, new ProfilingSampler("Aether Fog Compute")))
            {
                cmd.SetComputeTextureParam(FogCompute, kernel, "previousFogTexture", previousFogTexture);
                cmd.SetComputeTextureParam(FogCompute, kernel, "fogTexture", fogTexture);

                cmd.SetComputeVectorParam(FogCompute, "fogTextureResolution", new(Settings.VolumeResolution.x, Settings.VolumeResolution.y, Settings.VolumeResolution.z));

                cmd.SetComputeBufferParam(FogCompute, kernel, "lightData", lightDataBuffer);
                cmd.SetComputeIntParam(FogCompute, "lightDataLength", lightData.Length);

                cmd.SetComputeBufferParam(FogCompute, kernel, "fogData", fogDataBuffer);
                cmd.SetComputeIntParam(FogCompute, "fogDataLength", fogData.Length);

                cmd.SetComputeBufferParam(FogCompute, kernel, "cameraData", cameraDataBuffer);

                cmd.SetComputeFloatParam(FogCompute, "time", Time.unscaledTime);
                cmd.SetComputeFloatParam(FogCompute, "jitterDistance", Settings.JitterDistance);
                cmd.SetComputeFloatParam(FogCompute, "jitterScale", Settings.JitterScale);
                cmd.SetComputeFloatParam(FogCompute, "temporalStrength", Settings.TemporalStrength);

                cmd.SetComputeTextureParam(FogCompute, kernel, "mainShadowTexture", (Texture)AetherShadowPass.MainShadowTexture ?? Texture2D.whiteTexture);

                cmd.SetComputeTextureParam(FogCompute, kernel, "blueNoise", blueNoise);
                cmd.SetComputeFloatParam(FogCompute, "blueNoiseSize", blueNoise.width);

                cmd.DispatchCompute(FogCompute, kernel, dispatchSize.x, dispatchSize.y, dispatchSize.z);
            }

            context.ExecuteCommandBufferAsync(cmd, ComputeQueueType.Background);
            cmd.Clear();

            CommandBufferPool.Release(cmd);

            return true;
        }

        //* Raymarch Compute
        public bool UpdateRaymarchCompute (ScriptableRenderContext context)
        {
            var kernel = RaymarchCompute.FindKernel("RaymarchFog");

            int3 dispatchSize = GetDispatchSize(RaymarchCompute, kernel, Settings.VolumeResolution);

            CommandBuffer cmd = CommandBufferPool.Get();

            cmd.SetExecutionFlags(CommandBufferExecutionFlags.AsyncCompute);

            using (new ProfilingScope(cmd, new ProfilingSampler("Aether Raymarch Compute")))
            {
                
                cmd.SetComputeTextureParam(RaymarchCompute, kernel, "raymarchTexture", fogTexture);
                cmd.SetComputeIntParam(RaymarchCompute, "depthResolution", Settings.VolumeResolution.z);

                cmd.DispatchCompute(RaymarchCompute, kernel, dispatchSize.x, dispatchSize.y, 1);
            }

            context.ExecuteCommandBufferAsync(cmd, ComputeQueueType.Background);
            cmd.Clear();

            CommandBufferPool.Release(cmd);

            return true;
        }

        //* Update Material
        public bool UpdateMaterial ()
        {
            if(blitMaterial == null) SetupMaterial();

            blitMaterial.SetTexture("_Volume", fogTexture);
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
        public float ViewDistance = 70;
        public float JitterDistance = 2;
        public float JitterScale = 3;
        [Range(0, 1)] public float TemporalStrength = .75f;
    }
}