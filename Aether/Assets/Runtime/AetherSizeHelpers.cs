namespace Aether
{
    public static class AetherSizeHelpers
    {
        public const int INT_SIZE = sizeof(int);
        public const int FLOAT_SIZE = sizeof(float);
        public const int VECTOR3_SIZE = FLOAT_SIZE * 3;
        public const int MATRIX4X4_SIZE = FLOAT_SIZE * 16;
    }
}