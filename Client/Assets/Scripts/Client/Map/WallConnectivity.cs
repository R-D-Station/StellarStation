using Shared.World;

namespace Client.Map
{
    /// <summary>Единый источник логики соединения стен для рантайма (<see cref="Client.Net.View.MapRenderer"/>)
    /// и редактора (MapEditorWindow): какие соседи соединяются (взаимное согласие W3-2) и какая форма/поворот.
    /// Чистая логика без UnityEngine — карта и каталог приходят параметрами.</summary>
    public static class WallConnectivity
    {
        /// <summary>Считается ли тайл-сосед (x,y,z) соединяющим для стены self (по флагам A1/A2 на Connection).</summary>
        /// <remarks>Без аллокаций: GetTile/GetStructure без new, Array.IndexOf по белому списку.</remarks>
        public static bool Connects(TileCatalog catalog, GridMap map, StructureDefinition self, int x, int y, int z)
        {
            var nt = map.GetTile(x, y, z);           // вне чанка → Tile.Space, StructureType==0
            if (nt.StructureType == 0) return false;
            var ns = catalog.GetStructure(nt.StructureType);
            if (ns == null) return false;

            var c = self.Connection;
            switch (ns.Category)
            {
                case StructureCategory.Wall:
                    // Same-type симметрично by design (один SO). Разные типы — только при ВЗАИМНОМ согласии,
                    // иначе односторонний шов: одна стена тянет арку, другая рисует End (plan §8 W3-2).
                    if (nt.StructureType == self.Type) return c.ConnectsToSameType;
                    return AcceptsOtherWall(self, nt.StructureType) && AcceptsOtherWall(ns, self.Type);
                case StructureCategory.Window: return c.ConnectsToWindows;
                case StructureCategory.Door:
                case StructureCategory.Hatch: return c.ConnectsToDoorsHatches;
                default: return false;
            }
        }

        // Принимает ли стена `def` соседнюю стену типа `otherType`: флаг + опц. белый список. Без аллокаций.
        private static bool AcceptsOtherWall(StructureDefinition def, byte otherType)
        {
            var cc = def.Connection;
            if (!cc.ConnectsToOtherWalls) return false;
            return cc.ConnectOnlyToTypes == null || cc.ConnectOnlyToTypes.Length == 0
                   || System.Array.IndexOf(cc.ConnectOnlyToTypes, otherType) >= 0;
        }

        /// <summary>Форма соединения и число поворотов на 90° по Unity-Y для стены self в точке (x,y,z).</summary>
        public static (WallShape shape, int rotationSteps) ResolveAt(TileCatalog catalog, GridMap map, StructureDefinition self, int x, int y, int z)
        {
            bool n = Connects(catalog, map, self, x,     y + 1, z); // мир N = +Y
            bool e = Connects(catalog, map, self, x + 1, y,     z);
            bool s = Connects(catalog, map, self, x,     y - 1, z);
            bool w = Connects(catalog, map, self, x - 1, y,     z);
            return WallConnection.Resolve(n, e, s, w);
        }
    }
}
