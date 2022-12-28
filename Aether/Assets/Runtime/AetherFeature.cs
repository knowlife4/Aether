using UnityEngine;
using UnityEngine.Rendering.Universal;
using static Aether.AetherSizeHelpers;

namespace Aether
{
    public class AetherFeature : ScriptableRendererFeature
    {
        [SerializeField] AetherFogPassSettings settings = new();

        [SerializeField] AetherShadowPass shadowPass = null;
        [SerializeField] AetherFogPass fogPass = null;

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            renderer.EnqueuePass(shadowPass);
            renderer.EnqueuePass(fogPass);
        }

        public override void Create()
        {
            shadowPass = new()
            {
                renderPassEvent = RenderPassEvent.AfterRenderingShadows
            };

            fogPass = new(settings)
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing
            };
        }

        public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
        {
            fogPass.ConfigureInput(ScriptableRenderPassInput.Color);
            fogPass.ConfigureInput(ScriptableRenderPassInput.Depth);
            fogPass.Target = renderer.cameraColorTargetHandle;
        }

        protected override void Dispose(bool disposing)
        {
            fogPass?.Dispose();
            base.Dispose(disposing);
        }
    }

    public struct LightData
    {
        public Matrix4x4 mat;
        public Vector3 position;
        public Vector3 direction;
        public Vector3 color;
        public float intensity;
        public float angle;
        public int shadowSliceIndex;
        public int type;

        public const int SIZE = MATRIX4X4_SIZE + VECTOR3_SIZE + VECTOR3_SIZE + VECTOR3_SIZE + FLOAT_SIZE + FLOAT_SIZE + INT_SIZE + INT_SIZE;

        public void Update (AetherLight aetherLight)
        {
            Light light = aetherLight.Light;
            
            Matrix4x4 v = aetherLight.transform.worldToLocalMatrix;
			Matrix4x4 p = GL.GetGPUProjectionMatrix(Matrix4x4.Perspective(light.spotAngle, 1.0f, light.shadowNearPlane, light.range), true);

			p *= Matrix4x4.Scale(new Vector3(1.0f, 1.0f, -1.0f));
			mat = p * v;

            position = aetherLight.transform.position;
            direction = aetherLight.transform.forward;
            color = new Vector3(light.color.r, light.color.g, light.color.b);
            intensity = light.intensity;
            angle = light.spotAngle;
            type = (int)light.type;
        }
    }

    [System.Serializable]
    public struct FogData
    {
        public Matrix4x4 trs;
        public Vector3 minimum, maximum;
        public Vector3 color;
        public float density;
        public float scatterCoefficient;
        public int type;

        public const int SIZE = MATRIX4X4_SIZE + (VECTOR3_SIZE * 2) + VECTOR3_SIZE + FLOAT_SIZE + FLOAT_SIZE + INT_SIZE;

        public void Update (AetherFog volume)
        {
            trs = Matrix4x4.TRS(volume.transform.position, volume.transform.rotation, volume.transform.localScale);
            minimum = volume.transform.position - (volume.transform.localScale/2);
            maximum = volume.transform.position + (volume.transform.localScale/2);
            color = new(volume.Color.r, volume.Color.g, volume.Color.b);
            density = volume.Density;
            scatterCoefficient = volume.ScatterCoefficient;
            type = (int)volume.Type;
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