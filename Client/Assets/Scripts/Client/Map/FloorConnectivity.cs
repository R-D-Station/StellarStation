using Shared.World;

namespace Client.Map
{
    /// <summary>Единый источник логики соединения пола для рантайма (<see cref="Client.Net.View.MapRenderer"/>)
    /// и редактора: какие соседи-полы соединяются (взаимное согласие, как у стен) и какая форма/поворот.
    /// Чистая логика без UnityEngine — карта и каталог приходят параметрами. Зеркало WallConnectivity.</summary>
    public static class FloorConnectivity
    {
        /// <summary>Считается ли тайл-сосед (x,y,z) соединяющим для пола self (по флагам на Connection).</summary>
        /// <remarks>Без аллокаций: GetTile/GetFloor без new, Array.IndexOf по белому списку.</remarks>
        public static bool Connects(TileCatalog catalog, GridMap map, FloorDefinition self, int x, int y, int z)
        {
            var nt = map.GetTile(x, y, z);           // вне чанка → Tile.Space, FloorType==0
            if (nt.FloorType == 0) return false;
            // Структура-NoVisibleFloor на соседе → пол «отсутствует» для автотайлинга (GetStructure(0)→null → безопасно).
            if (catalog.GetStructure(nt.StructureType)?.NoVisibleFloor == true) return false;
            var nf = catalog.GetFloor(nt.FloorType);
            if (nf == null) return false;

            var c = self.Connection;
            // Same-type симметрично by design (один SO). Разные типы — только при ВЗАИМНОМ согласии (как стены).
            if (nt.FloorType == self.Type) return c.ConnectsToSameType;
            return Accepts(self, nt.FloorType) && Accepts(nf, self.Type);
        }

        // Принимает ли пол `def` соседний пол типа `otherType`: флаг + опц. белый список. Без аллокаций.
        private static bool Accepts(FloorDefinition def, byte otherType)
        {
            var cc = def.Connection;
            if (!cc.ConnectsToOtherFloors) return false;
            return cc.ConnectOnlyToTypes == null || cc.ConnectOnlyToTypes.Length == 0
                   || System.Array.IndexOf(cc.ConnectOnlyToTypes, otherType) >= 0;
        }

        /// <summary>Форма соединения и число поворотов на 90° по Unity-Y для пола self в точке (x,y,z).</summary>
        public static (WallShape shape, int rotationSteps) ResolveAt(TileCatalog catalog, GridMap map, FloorDefinition self, int x, int y, int z)
        {
            bool n = Connects(catalog, map, self, x,     y + 1, z); // мир N = +Y
            bool e = Connects(catalog, map, self, x + 1, y,     z);
            bool s = Connects(catalog, map, self, x,     y - 1, z);
            bool w = Connects(catalog, map, self, x - 1, y,     z);
            return WallConnection.Resolve(n, e, s, w);
        }

        /// <summary>Маска углов (внутренних вырезов) для пола self в (x,y,z): бит есть, когда ОБА смежных кардинальных соседа —
        /// соединённые полы, а диагональ между ними — НЕ соединённый пол. Биты: NW=1, NE=2, SE=4, SW=8 (по часовой от NW).
        /// Мир N=+Y; диагонали NW=(x-1,y+1), NE=(x+1,y+1), SE=(x+1,y-1), SW=(x-1,y-1). Без аллокаций. Зеркало WallConnectivity.</summary>
        public static byte ResolveCornersAt(TileCatalog catalog, GridMap map, FloorDefinition self, int x, int y, int z)
        {
            bool n = Connects(catalog, map, self, x,     y + 1, z);
            bool e = Connects(catalog, map, self, x + 1, y,     z);
            bool s = Connects(catalog, map, self, x,     y - 1, z);
            bool w = Connects(catalog, map, self, x - 1, y,     z);
            bool nw = Connects(catalog, map, self, x - 1, y + 1, z);
            bool ne = Connects(catalog, map, self, x + 1, y + 1, z);
            bool se = Connects(catalog, map, self, x + 1, y - 1, z);
            bool sw = Connects(catalog, map, self, x - 1, y - 1, z);
            byte mask = 0;
            if (n && w && !nw) mask |= 1; // NW
            if (n && e && !ne) mask |= 2; // NE
            if (s && e && !se) mask |= 4; // SE
            if (s && w && !sw) mask |= 8; // SW
            return mask;
        }
    }
}
