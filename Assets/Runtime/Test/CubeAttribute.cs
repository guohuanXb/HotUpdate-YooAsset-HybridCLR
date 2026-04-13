using UnityEngine;

namespace Runtime
{
    [CreateAssetMenu(fileName = "CubeAttribute", menuName = "Runtime/CubeAttribute", order = 1)]
    public class CubeAttribute :ScriptableObject
    {
        public string cubeName;
        public Material cubeMaterial;
        public float cubeSize;
    }
}
