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
    }
}
