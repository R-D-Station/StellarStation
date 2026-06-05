using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.ProjectWindowCallback;
using UnityEngine;

namespace EditorTools
{
    /// <summary>
    /// Создание C#-скрипта с автоматическим namespace.
    /// Namespace = Root Namespace ближайшего вверх .asmdef + путь папок
    /// от этого .asmdef до целевой папки.
    ///
    /// Пример: Scripts/Client/Net/Foo.cs при Client.asmdef (root=Client)
    ///         -> namespace Client.Net
    ///
    /// Скрипт редакторский — лежать должен в папке с именем Editor.
    /// </summary>
    public static class NamespacedScriptCreator
    {
        // Ctrl/Cmd + Alt + N — по желанию. Приоритет ставит пункт рядом с Create > C# Script.
        [MenuItem("Assets/Create/Scripting/# Script (Auto Namespace) %&n", false, 80)]
        public static void CreateScript()
        {
            string folder = GetSelectedFolder();
            string defaultName = "NewScript.cs";
            string path = Path.Combine(folder, defaultName);

            var endAction = ScriptableObject.CreateInstance<DoCreateNamespacedScript>();

            ProjectWindowUtil.StartNameEditingIfProjectWindowExists(
                0,
                endAction,
                path,
                EditorGUIUtility.IconContent("cs Script Icon").image as Texture2D,
                null);
        }

        /// <summary>Папка текущего выделения в Project-окне (или Assets по умолчанию).</summary>
        private static string GetSelectedFolder()
        {
            foreach (var obj in Selection.GetFiltered<Object>(SelectionMode.Assets))
            {
                string p = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(p)) continue;
                if (Directory.Exists(p)) return p;            // выбрана папка
                if (File.Exists(p)) return Path.GetDirectoryName(p); // выбран файл — берём его папку
            }
            return "Assets";
        }

        /// <summary>
        /// Вычисляет namespace для файла по пути targetFolder.
        /// Ищет ближайший .asmdef вверх, берёт его rootNamespace + хвост папок.
        /// </summary>
        public static string ResolveNamespace(string targetFolder)
        {
            targetFolder = targetFolder.Replace('\\', '/');

            string asmdefDir = FindAsmdefDir(targetFolder, out string rootNs);

            if (asmdefDir != null)
            {
                // путь от папки asmdef до целевой папки -> суффикс
                string suffix = targetFolder.Length > asmdefDir.Length
                    ? targetFolder.Substring(asmdefDir.Length).Trim('/')
                    : "";

                string ns = rootNs ?? "";
                if (!string.IsNullOrEmpty(suffix))
                {
                    string suffixNs = suffix.Replace('/', '.');
                    ns = string.IsNullOrEmpty(ns) ? suffixNs : ns + "." + suffixNs;
                }
                return SanitizeNamespace(ns);
            }

            // Fallback: нет asmdef — собираем из папок от Assets.
            string fromAssets = targetFolder.StartsWith("Assets")
                ? targetFolder.Substring("Assets".Length).Trim('/')
                : targetFolder;
            return SanitizeNamespace(fromAssets.Replace('/', '.'));
        }

        /// <summary>Поднимается вверх по дереву, ищет .asmdef. Возвращает папку asmdef и его rootNamespace.</summary>
        private static string FindAsmdefDir(string folder, out string rootNamespace)
        {
            rootNamespace = null;
            string dir = folder;

            while (!string.IsNullOrEmpty(dir) && dir.StartsWith("Assets"))
            {
                string[] files = Directory.GetFiles(dir, "*.asmdef", SearchOption.TopDirectoryOnly);
                if (files.Length > 0)
                {
                    rootNamespace = ReadRootNamespace(files[0]);
                    return dir.Replace('\\', '/');
                }

                int slash = dir.Replace('\\', '/').LastIndexOf('/');
                if (slash < 0) break;
                dir = dir.Substring(0, slash);
            }
            return null;
        }

        /// <summary>Читает поле rootNamespace из JSON .asmdef (без JsonUtility — поле может отсутствовать).</summary>
        private static string ReadRootNamespace(string asmdefPath)
        {
            try
            {
                string json = File.ReadAllText(asmdefPath);
                var m = Regex.Match(json, "\"rootNamespace\"\\s*:\\s*\"([^\"]*)\"");
                if (m.Success) return m.Groups[1].Value;
            }
            catch { /* ignore */ }
            return null;
        }

        private static string SanitizeNamespace(string ns)
        {
            if (string.IsNullOrEmpty(ns)) return "";
            // каждый сегмент: убрать недопустимые символы, не начинать с цифры
            var parts = ns.Split('.');
            for (int i = 0; i < parts.Length; i++)
            {
                string p = Regex.Replace(parts[i], "[^A-Za-z0-9_]", "");
                if (p.Length > 0 && char.IsDigit(p[0])) p = "_" + p;
                parts[i] = p;
            }
            return string.Join(".", System.Array.FindAll(parts, p => p.Length > 0));
        }
    }

    /// <summary>Действие по завершении ввода имени файла: пишет содержимое с namespace.</summary>
    public class DoCreateNamespacedScript : EndNameEditAction
    {
        public override void Action(int instanceId, string pathName, string resourceFile)
        {
            pathName = pathName.Replace('\\', '/');
            if (!pathName.EndsWith(".cs")) pathName += ".cs";

            string folder = Path.GetDirectoryName(pathName).Replace('\\', '/');
            string className = SanitizeClassName(Path.GetFileNameWithoutExtension(pathName));
            string ns = NamespacedScriptCreator.ResolveNamespace(folder);

            string content = BuildContent(ns, className);
            File.WriteAllText(pathName, content, new UTF8Encoding(false));

            AssetDatabase.ImportAsset(pathName);
            var asset = AssetDatabase.LoadAssetAtPath<Object>(pathName);
            ProjectWindowUtil.ShowCreatedAsset(asset);
        }

        private static string BuildContent(string ns, string className)
        {
            var sb = new StringBuilder();

            if (string.IsNullOrEmpty(ns))
            {
                sb.AppendLine($"public class {className}");
                sb.AppendLine("{");
                sb.AppendLine("}");
                return sb.ToString();
            }

            // Блочный namespace с табуляцией (как просили).
            sb.AppendLine($"namespace {ns}");
            sb.AppendLine("{");
            sb.AppendLine($"    public class {className}");
            sb.AppendLine("    {");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static string SanitizeClassName(string raw)
        {
            string c = Regex.Replace(raw, "[^A-Za-z0-9_]", "");
            if (string.IsNullOrEmpty(c)) c = "NewScript";
            if (char.IsDigit(c[0])) c = "_" + c;
            return c;
        }
    }
}