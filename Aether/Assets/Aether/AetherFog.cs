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

        public FogType type;
        public FogData data;

        public void OnDrawGizmos()
        {
            if(type == FogType.Global) return;

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(transform.position, transform.localScale);
        }
    }
}