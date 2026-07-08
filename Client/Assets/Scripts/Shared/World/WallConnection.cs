namespace Shared.World
{
    /// <summary>Чистое ядро автотайлинга стен: маска 4 прямых соседей → базовая форма + поворот по Y.</summary>
    public static class WallConnection
    {
        /// <summary>
        /// Возвращает базовую форму и число поворотов на 90° по часовой вокруг Unity-Y.
        /// Базовые ориентации мешей: End соединяется на North; Straight = N+S; Corner = N+E;
        /// T = N+E+S (проём на West); Single/Cross симметричны. Шаг +90° поворачивает
        /// мировой North (+Z в Unity) к мировому East (+X).
        /// </summary>
        public static (WallShape shape, int rotationSteps) Resolve(bool n, bool e, bool s, bool w)
        {
            int count = (n ? 1 : 0) + (e ? 1 : 0) + (s ? 1 : 0) + (w ? 1 : 0);

            switch (count)
            {
                case 0:
                    return (WallShape.Single, 0);

                case 1:
                    // Поворот по стороне единственного соседа: N=0, E=1, S=2, W=3.
                    if (n) return (WallShape.End, 0);
                    if (e) return (WallShape.End, 1);
                    if (s) return (WallShape.End, 2);
                    return (WallShape.End, 3);

                case 2:
                    if (n && s) return (WallShape.Straight, 0);
                    if (e && w) return (WallShape.Straight, 1);
                    // Смежная пара → Corner по квадранту: N+E=0, E+S=1, S+W=2, W+N=3.
                    if (n && e) return (WallShape.Corner, 0);
                    if (e && s) return (WallShape.Corner, 1);
                    if (s && w) return (WallShape.Corner, 2);
                    return (WallShape.Corner, 3); // w && n

                case 3:
                    // Поворот по ОТСУТСТВУЮЩЕЙ стороне: нет W=0, нет N=1, нет E=2, нет S=3.
                    if (!w) return (WallShape.T, 0);
                    if (!n) return (WallShape.T, 1);
                    if (!e) return (WallShape.T, 2);
                    return (WallShape.T, 3); // !s

                default:
                    return (WallShape.Cross, 0);
            }
        }
    }
}
