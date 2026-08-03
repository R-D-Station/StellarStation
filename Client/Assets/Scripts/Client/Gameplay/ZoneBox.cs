namespace Client.Gameplay
{
    public static class ZoneBox
    {
        public static bool ContainsLocal(float px, float py, float pz,
                                         float cx, float cy, float cz,
                                         float sx, float sy, float sz, float margin)
        {
            float hx = Half(sx) + margin, hy = Half(sy) + margin, hz = Half(sz) + margin;
            if (hx < 0f || hy < 0f || hz < 0f)
                return false;
            return px >= cx - hx && px <= cx + hx
                && py >= cy - hy && py <= cy + hy
                && pz >= cz - hz && pz <= cz + hz;
        }

        public static bool NextInside(bool wasInside,
                                      float px, float py, float pz,
                                      float cx, float cy, float cz,
                                      float sx, float sy, float sz, float exitMargin)
            => ContainsLocal(px, py, pz, cx, cy, cz, sx, sy, sz, wasInside ? exitMargin : 0f);

        public static float Volume(float sx, float sy, float sz) => Abs(sx) * Abs(sy) * Abs(sz);

        private static float Half(float size) => Abs(size) * 0.5f;

        private static float Abs(float v) => v < 0f ? -v : v;
    }
}
