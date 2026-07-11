namespace Client.Config
{
    /// <summary>Клиентские визуальные константы раскладки мира (общие для всех рендеров).</summary>
    public static class RenderConfig
    {
        /// <summary>Высота одного этажа по Unity-Y. Равна высоте стены — этажи стоят впритык.</summary>
        public const float FloorHeight = 2.1f;

        /// <summary>Горизонтальный размер тайла по Unity-X/Z (мир-ед); сейчас 1. Основа связи размеров %↔юниты + forward-compat к блок-миру (там = размер блока).</summary>
        public const float TileSize = 1f;

        /// <summary>Купол видимости сущностей (блок-мир): базовый радиус на одной высоте с игроком.</summary>
        public const float EntityViewRadius = 15f;

        /// <summary>Сужение радиуса за каждый блок разницы высот (вверх и вниз симметрично).</summary>
        public const float EntityViewLossPerBlock = 2.5f;
    }
}
