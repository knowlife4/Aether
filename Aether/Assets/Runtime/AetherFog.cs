using System.Collections;
using System.Collections.Generic;
using UnityEngine;
namespace Aether
{
    public class AetherFog : MonoBehaviour
    {
        public enum FogType
        {
            Local,
            Global
        }

        public FogType Type;
        public Color Color;
        [Range(0, 1)] public float Density;
        [Range(0, 1)] public float ScatterCoefficient = .9f;


        public void OnDrawGizmos()
        {
            if(Type == FogType.Global) return;

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(transform.position, transform.localScale);
        }
    }
}