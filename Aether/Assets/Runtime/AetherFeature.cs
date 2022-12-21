using System.Linq;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using static Aether.AetherSizeHelpers;

namespace Aether
{
    public class AetherFeature : ScriptableRendererFeature
    {
        [SerializeField] AetherSettings settings = new();

        [SerializeField] AetherFeaturePass pass = null;

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            renderer.EnqueuePass(pass);
        }

        public override void Create()
        {
            ComputeShader fogCompute = (ComputeShader)Resources.Load("ComputeFog");
            ComputeShader raymarchCompute = (ComputeShader)Resources.Load("RaymarchFog");

            settings.blitMaterial = new(Shader.Find("Aether/FogApply"));

            pass = new(settings, fogCompute, raymarchCompute)
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing
            };
        }

        public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
        {
            pass.ConfigureInput(ScriptableRenderPassInput.Color);
            pass.ConfigureInput(ScriptableRenderPassInput.Depth);
            pass.SetTarget(renderer.cameraColorTargetHandle);
        }
    }

    [System.Serializable]
    public class AetherFeaturePass : ScriptableRenderPass
    {
        public AetherFeaturePass (AetherSettings settings, ComputeShader fogCompute, ComputeShader raymarchCompute)
        {
            Settings = settings;
            FogCompute = fogCompute;
            RaymarchCompute = raymarchCompute;
            Setup();
        }

        public AetherSettings Settings { get; }
        public ComputeShader FogCompute { get; }
        public ComputeShader RaymarchCompute { get; }
    

        Light[] lights;
        LightData[] lightData;
        ComputeBuffer lightDataBuffer;

        AetherFog[] fogVolumes;
        FogData[] fogData;
        ComputeBuffer fogDataBuffer;

        Camera camera;
        CameraData[] cameraData = new CameraData[1];
        ComputeBuffer cameraDataBuffer;
        
        RTHandle target;


        [SerializeField] RenderTexture fogTexture, previousFogTexture, raymarchTexture;

        public void SetTarget(RTHandle targetHandle) => target = targetHandle;

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            ConfigureTarget(target);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if(RaymarchCompute == null || FogCompute == null || fogTexture == null || raymarchTexture == null || Settings.blitMaterial == null)
            {
                Debug.LogError("Missing Compute/Texture!");
                Setup();
                return;
            }

            UpdateCamera();
            UpdateLights();
            UpdateFogVolumes();
            UpdateFog();
            UpdateRaymarch();
            UpdateMaterial();

            var cameraData = renderingData.cameraData;

            if (Settings.blitMaterial == null) return;
            
            CommandBuffer cmd = CommandBufferPool.Get();

            using (new ProfilingScope(cmd, new ProfilingSampler("AetherBlit")))
            {
                Blitter.BlitCameraTexture(cmd, target, target, Settings.blitMaterial, 0);
            }
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            CommandBufferPool.Release(cmd);
        }

        public void Setup ()
        {
            SetupTexture();
            SetupLights();
            SetupFogVolumes();
            SetupCamera();
        }

        public void SetupTexture ()
        {
            raymarchTexture = new RenderTexture(Settings.volumeResolution.x, Settings.volumeResolution.y, 0, RenderTextureFormat.ARGBHalf)
            {
                volumeDepth = Settings.volumeResolution.z,
                dimension = TextureDimension.Tex3D,
                enableRandomWrite = true,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            raymarchTexture.Create();

            fogTexture = new RenderTexture(Settings.volumeResolution.x, Settings.volumeResolution.y, 0, RenderTextureFormat.ARGBHalf)
            {
                volumeDepth = Settings.volumeResolution.z,
                dimension = TextureDimension.Tex3D,
                enableRandomWrite = true,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear
            };

            fogTexture.Create();
        }

        public void SetupLights ()
        {
            lights = Object.FindObjectsOfType<Light>();
            lightData = new LightData[lights.Length];
            if(lightData.Length == 0) return;

            lightDataBuffer ??= new(lightData.Length, LightData.SIZE);
        }

        public void SetupFogVolumes ()
        {
            fogVolumes = Object.FindObjectsOfType<AetherFog>();
            fogData = new FogData[fogVolumes.Length];
            if(fogData.Length == 0) return;

            fogDataBuffer ??= new(fogData.Length, FogData.SIZE);
        }

        public void SetupCamera ()
        {
            cameraDataBuffer ??= new(cameraData.Length, CameraData.SIZE);
        }

        public void UpdateCamera ()
        {
            camera = Camera.current ?? Camera.main;
            cameraData[0].Update(camera, Settings.viewDistance);

            cameraDataBuffer.SetData(cameraData);
        }

        public void UpdateLights ()
        {
            for (int i = 0; i < lights.Length; i++)
            {
                if(lights[i] == null) SetupLights();

                lightData[i].Update(lights[i]);
            }

            lightDataBuffer.SetData(lightData);
        }

        public void UpdateFogVolumes()
        {
            if(fogDataBuffer == null || fogData == null) SetupFogVolumes();

            for (int i = 0; i < fogVolumes.Length; i++)
            {
                if(fogVolumes[i] == null) SetupFogVolumes();

                fogData[i].Update(fogVolumes[i]);
            }

            fogDataBuffer.SetData(fogData);
        }

        public void UpdateMaterial ()
        {
            Settings.blitMaterial.SetTexture("_Volume", raymarchTexture);
            Settings.blitMaterial.SetFloat("_fogFar", Settings.viewDistance);
            Settings.blitMaterial.SetFloat("_cameraFar", camera.farClipPlane);
        }

        public void UpdateFog ()
        {
            var kernel = FogCompute.FindKernel("ComputeFog");

            FogCompute.SetTexture(kernel, "fogTexture", fogTexture);

            FogCompute.SetBuffer(kernel, "lightData", lightDataBuffer);
            FogCompute.SetInt("lightDataLength", lightData.Length);

            FogCompute.SetBuffer(kernel, "fogData", fogDataBuffer);
            FogCompute.SetInt("fogDataLength", fogData.Length);

            FogCompute.SetBuffer(kernel, "cameraData", cameraDataBuffer);

            FogCompute.SetVector("fogTextureResolution", new(Settings.volumeResolution.x, Settings.volumeResolution.y, Settings.volumeResolution.z));

            FogCompute.SetFloat("time", Time.unscaledTime);

            FogCompute.Dispatch(kernel, 20, 24, 32);
        }

        public void UpdateRaymarch ()
        {
            var kernel = RaymarchCompute.FindKernel("RaymarchFog");

            RaymarchCompute.SetTexture(kernel, "raymarchTexture", raymarchTexture);
            RaymarchCompute.SetTexture(kernel, "fogTexture", fogTexture);
            RaymarchCompute.Dispatch(kernel, 10, 10, 1);
        }
    }

    [System.Serializable]
    public class AetherSettings
    {
        public int3 volumeResolution = new(160, 90, 128);
        public float viewDistance;
        public Material blitMaterial;
        public Texture blueNoise;
    }

    public struct LightData
    {
        public Vector3 position;
        public Vector3 direction;
        public Vector3 color;
        public float intensity;
        public float angle;
        public int type;

        public const int SIZE = VECTOR3_SIZE + VECTOR3_SIZE + VECTOR3_SIZE + FLOAT_SIZE + FLOAT_SIZE + INT_SIZE;

        public void Update (Light light)
        {
            position = light.transform.position;
            direction = light.transform.forward;
            color = new Vector3(light.color.r, light.color.g, light.color.b);
            intensity = light.intensity;
            angle = light.spotAngle;
            type = (int)light.type;
        }
    }

    [System.Serializable]
    public struct FogData
    {
        [HideInInspector] public Matrix4x4 trs;
        public float density;
        public float scattering;
        public float absorption;
        public float phaseFactor;
        [HideInInspector] public int type;

        public const int SIZE = MATRIX4X4_SIZE + FLOAT_SIZE + FLOAT_SIZE + FLOAT_SIZE + FLOAT_SIZE + INT_SIZE;

        public void Update (AetherFog volume)
        {
            this = volume.data;
            trs = Matrix4x4.TRS(volume.transform.position, volume.transform.rotation, volume.transform.localScale);
            type = (int)volume.type;
        }
    }

    public struct CameraData
    {
        public Vector3 position;
        public Matrix4x4 inverseViewMatrix;
        public float aspect;
        public float fov;
        public float near;
        public float far;

        public const int SIZE = VECTOR3_SIZE + MATRIX4X4_SIZE + FLOAT_SIZE + FLOAT_SIZE + FLOAT_SIZE + FLOAT_SIZE;

        public void Update (Camera camera, float viewDistance)
        {
            position = camera.transform.position;
            inverseViewMatrix = camera.cameraToWorldMatrix;
            aspect = camera.aspect;
            fov = camera.fieldOfView;
            near = camera.nearClipPlane;
            far = viewDistance;
        }
    }
}