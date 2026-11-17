namespace SpawnDev.MultiMedia
{
    /// <summary>
    /// BT.601 limited ("video") range YUV &lt;-&gt; RGB, the range cameras, Media Foundation and H.264 decoders use: Y 16..235, U / V 16..240
    /// around 128. Float math, rounded to the nearest integer and clamped to 0..255; one implementation for the CPU converter and the ILGPU
    /// kernels, so both give the same bytes. U and V arguments are already centred (value - 128).
    /// </summary>
    public static class Bt601
    {
        /// <summary>Round to the nearest integer and clamp to 0..255.</summary>
        public static int ToByte(float x) => x <= 0f ? 0 : x >= 255f ? 255 : (int)(x + 0.5f);

        public static int R(int y, int v) => ToByte(1.164383f * (y - 16) + 1.596027f * v);
        public static int G(int y, int u, int v) => ToByte(1.164383f * (y - 16) - 0.391762f * u - 0.812968f * v);
        public static int B(int y, int u) => ToByte(1.164383f * (y - 16) + 2.017232f * u);

        public static int Y(float r, float g, float b) => ToByte(16f + 0.256788f * r + 0.504129f * g + 0.097906f * b);
        public static int U(float r, float g, float b) => ToByte(128f - 0.148223f * r - 0.290993f * g + 0.439216f * b);
        public static int V(float r, float g, float b) => ToByte(128f + 0.439216f * r - 0.367788f * g - 0.071427f * b);
    }
}
