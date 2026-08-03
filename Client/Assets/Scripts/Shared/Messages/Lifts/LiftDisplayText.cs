using System.Globalization;

namespace Shared.Messages.Lifts
{
    /// <summary>Номер этажа для TMP-панели дисплея — из ПРЕДПОСЧИТАННОЙ таблицы, без аллокаций.</summary>
    public static class LiftDisplayText
    {
        public const int MaxFloors = 32;

        private static readonly string[] Floors = Build();

        private static string[] Build()
        {
            var table = new string[MaxFloors];
            for (int i = 0; i < MaxFloors; i++)
                table[i] = (i + 1).ToString(CultureInfo.InvariantCulture);
            return table;
        }

        /// <summary>Подпись по ПОРЯДКОВОМУ номеру этажа из <see cref="LiftStopTable.DisplayIndex"/>
        /// (0-базовый на входе, с 1 на экране); вне таблицы — пустая строка.</summary>
        public static string Floor(int displayIndex)
            => (uint)displayIndex < MaxFloors ? Floors[displayIndex] : string.Empty;
    }
}
