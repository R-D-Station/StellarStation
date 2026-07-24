using Shared.World.Blocks;

namespace Server.Services
{
    /// <summary>Источник блок-мира сервера: карта v10 по пути (редакторная) либо DevBlockWorld (код-полигон).</summary>
    public static class BlockWorldSource
    {
        /// <summary>Загрузить v10 по path (пробуется и Map/&lt;path&gt;); нет файла/не v10 → дев-полигон.
        /// fromFile=true ⇒ формы блоков — каталог; false ⇒ формы DevBlockWorld.</summary>
        public static (BlockGrid grid, bool fromFile) Load(string? path)
        {
            string? resolved = Resolve(path);
            if (resolved != null)
            {
                try
                {
                    var grid = BlockMapSerializer.LoadFromFile(resolved);
                    Console.WriteLine($"[Map] Blocks: loaded v10 '{resolved}' ({grid.Sections.Count} sections)");
                    return (grid, true);
                }
                catch (Exception e)
                {
                    Console.WriteLine($"[Map] Blocks: '{resolved}' не блочная карта ({e.Message}) — дев-полигон");
                }
            }

            var dev = new BlockGrid();
            DevBlockWorld.Build(dev);
            return (dev, false);
        }

        private static string? Resolve(string? path)
        {
            if (string.IsNullOrEmpty(path))
                return null;
            if (File.Exists(path))
                return path;
            string inMap = Path.Combine("Map", path);
            return File.Exists(inMap) ? inMap : null;
        }
    }
}
