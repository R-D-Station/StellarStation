using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace ServerTests.Shared
{
    /// <summary>Сторож инвариантов Shared/**: engine-agnostic и молчаливый. Ловит класс ошибок, который
    /// `dotnet build` пропускает: Server.csproj включает ImplicitUsings, Unity — нет, поэтому Shared-код
    /// с Console/BCL без явного using собирается на сервере и падает в Assembly-CSharp (CS0103).</summary>
    public class SharedPurityTests
    {
        // Пред-существующее нарушение: печать при загрузке конфига. Кандидат на отдельную чистку
        // (вероятно, загрузку/лог конфига стоит перенести в Server, а не глушить печать).
        private static readonly HashSet<string> ConsoleWhitelist = new(StringComparer.OrdinalIgnoreCase) { "SVars.cs" };

        [Fact]
        public void Shared_DoesNotPrint_ToConsole()
            => AssertNoMatches("Console.", ConsoleWhitelist,
                "Shared/** обязан молчать: диагностика — это ДАННЫЕ (счётчик/поле результата), печатает потребитель");

        [Fact]
        public void Shared_DoesNotReference_UnityEngine()
            => AssertNoMatches("UnityEngine", null,
                "Shared/** линк-компилируется в Server.csproj (net10.0) — UnityEngine там не существует");

        [Fact]
        public void Shared_DoesNotUse_UnityDebugLog()
            => AssertNoMatches("Debug.Log", null,
                "Shared/** обязан молчать и не зависеть от Unity-логгера");

        private static void AssertNoMatches(string needle, HashSet<string>? whitelist, string why)
        {
            var offenders = new List<string>();
            foreach (string file in Directory.EnumerateFiles(SharedRoot(), "*.cs", SearchOption.AllDirectories))
            {
                string name = Path.GetFileName(file);
                if (whitelist != null && whitelist.Contains(name))
                    continue;

                string[] lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    string code = StripComment(lines[i]); // иначе ловим доки вида «без UnityEngine»
                    if (code.Contains(needle, StringComparison.Ordinal))
                        offenders.Add($"{name}:{i + 1}: {lines[i].Trim()}");
                }
            }

            Assert.True(offenders.Count == 0,
                $"{why}. Нарушения ({needle}):{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
        }

        /// <summary>Отбрасывает строчный комментарий. Блочные /* */ не разбираем — в Shared их нет, а ложное
        /// срабатывание на доке («без UnityEngine») хуже пропуска экзотического случая.</summary>
        private static string StripComment(string line)
        {
            int i = line.IndexOf("//", StringComparison.Ordinal);
            return i < 0 ? line : line.Substring(0, i);
        }

        /// <summary>Ищет Client/Assets/Scripts/Shared, поднимаясь от каталога тестовой сборки (без абсолютных путей).</summary>
        private static string SharedRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "Client", "Assets", "Scripts", "Shared");
                if (Directory.Exists(candidate))
                    return candidate;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException(
                $"Не найден Client/Assets/Scripts/Shared вверх от {AppContext.BaseDirectory}");
        }
    }
}
