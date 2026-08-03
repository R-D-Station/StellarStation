#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Client.Editor.Inspectors
{
    internal static class BoxVisibilityMask
    {
        public const int MaxBoxes = 64;

        private const string Prefix = "Station.BoxVis.";

        private static string Key(Object asset, string set)
        {
            string path = AssetDatabase.GetAssetPath(asset);
            string id = string.IsNullOrEmpty(path)
                ? asset.GetInstanceID().ToString()
                : AssetDatabase.AssetPathToGUID(path);
            return Prefix + id + "." + set;
        }

        public static ulong Load(Object asset, string set, int count)
        {
            if (asset == null)
                return 0UL;
            string key = Key(asset, set);
            if (EditorPrefs.GetInt(key + ".n", -1) != count)
            {
                EditorPrefs.SetInt(key + ".n", count);
                EditorPrefs.SetString(key, "0");
                return 0UL;
            }
            return ulong.TryParse(EditorPrefs.GetString(key, "0"), out ulong mask) ? mask : 0UL;
        }

        public static void Save(Object asset, string set, int count, ulong mask)
        {
            if (asset == null)
                return;
            string key = Key(asset, set);
            EditorPrefs.SetInt(key + ".n", count);
            EditorPrefs.SetString(key, mask.ToString());
        }

        public static bool IsHidden(ulong mask, int index)
            => index >= 0 && index < MaxBoxes && (mask & (1UL << index)) != 0UL;

        public static ulong Toggle(ulong mask, int index)
            => index < 0 || index >= MaxBoxes ? mask : mask ^ (1UL << index);

        public static int HiddenCount(ulong mask, int count)
        {
            int hidden = 0;
            for (int i = 0; i < count && i < MaxBoxes; i++)
                if ((mask & (1UL << i)) != 0UL)
                    hidden++;
            return hidden;
        }
    }
}
#endif
