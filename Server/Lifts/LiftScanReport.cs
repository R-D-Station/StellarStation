using System;

namespace Server.Lifts
{
    public static class LiftScanReport
    {
        public static void Print(LiftScanResult result)
        {
            if (result == null)
                return;

            Console.WriteLine($"[Lifts] шахт: {result.Shafts.Count}, остановок: {result.Stops}, " +
                              $"ошибок: {result.ErrorCount}, предупреждений: {result.WarningCount} " +
                              $"(модулей рельса: {result.RailModules}, кабин: {result.Cabins})");

            foreach (var issue in result.Issues)
                Console.WriteLine($"[Lifts] {Tag(issue.Severity)} {issue.Cell} {Describe(issue)}");

            foreach (var shaft in result.Shafts)
                Console.WriteLine($"[Lifts] шахта {shaft.LiftId}: рельс ({shaft.RailX},{shaft.BaseY},{shaft.RailZ}) " +
                                  $"facing={shaft.Facing}, этажей {shaft.FloorCount} шагом {shaft.FloorStep}, " +
                                  $"кабина y{shaft.CabinY} скорость {shaft.SpeedBlocksPerSec}/сек, остановок {shaft.Stops.Count}");
        }

        private static string Tag(LiftScanSeverity severity)
            => severity == LiftScanSeverity.Error ? "ОШИБКА" : "внимание";

        /// <summary>Текст диагностики, который увидит автор карты (публично — проверяется тестом).</summary>
        public static string Describe(in LiftScanIssue issue)
        {
            switch (issue.Kind)
            {
                case LiftScanIssueKind.RailStackGap:
                    return $"разрыв штабеля: шаг {issue.A} вместо {issue.B} — начата новая шахта";
                case LiftScanIssueKind.RailModuleMismatch:
                    return issue.A == issue.B
                        ? $"поворот модуля не совпал с низом штабеля (тип {issue.A}) — шахта отброшена"
                        : $"тип модуля не совпал с низом штабеля (было {issue.A}, стало {issue.B}) — шахта отброшена";
                case LiftScanIssueKind.TooManyFloors:
                    return $"модулей {issue.A} больше предела этажей {issue.B} — верхние отброшены";
                case LiftScanIssueKind.ShaftFootprintOverlap:
                    return $"план шахты перекрывается с шахтой у ({issue.A},{issue.B}) — обе отброшены";
                case LiftScanIssueKind.CabinFacingMismatch:
                    return $"поворот кабины (facing={issue.B}) не совпал с поворотом рельса (facing={issue.A}) — " +
                           "разверни кабину так же, как шахту";
                case LiftScanIssueKind.CabinWithoutRail:
                    return "кабина вне плана какой-либо шахты";
                case LiftScanIssueKind.CabinInBrokenShaft:
                    return $"кабина в шахте у ({issue.A},{issue.B}), которая уже отбракована — чинить причину выше";
                case LiftScanIssueKind.DuplicateCabin:
                    return $"вторая кабина в шахте у ({issue.A},{issue.B})";
                case LiftScanIssueKind.CabinModuleMismatch:
                    return $"модуль кабины {issue.A >> 8}×{issue.A & 255} не совпадает с модулем рельса " +
                           $"{issue.B >> 8}×{issue.B & 255}: план шахты строится по рельсу, а боксы берутся " +
                           "от кабины — визуал и коллизия разъедутся";
                case LiftScanIssueKind.CabinOffFloor:
                    return $"кабина не на этаже (низ {issue.A}, шаг {issue.B})";
                case LiftScanIssueKind.CabinBoxesEmpty:
                    return "у кабины пустой набор боксов";
                case LiftScanIssueKind.CabinBadSpeed:
                    return "скорость кабины не положительна";
                case LiftScanIssueKind.ShaftWithoutCabin:
                    return "шахта без кабины — лифт не создан";
                case LiftScanIssueKind.DoorOffFloor:
                    return $"дверь шахты не на уровне этажа (низ {issue.A}, шаг {issue.B})";
                case LiftScanIssueKind.OrphanShaftDoor:
                    return "дверь шахты ни к чему не примыкает";
                case LiftScanIssueKind.DoorNotRegistered:
                    return "двери нет в реестре дверей — командовать ей нечем";
                case LiftScanIssueKind.DoorSavedOpen:
                    return "дверь сохранена открытой — нормализована в закрытую";
                case LiftScanIssueKind.DoorClaimedByTwoShafts:
                    return "дверь примыкает к двум шахтам — не отдана ни одной";
                case LiftScanIssueKind.DuplicateFloorDoor:
                    return $"на этаже {issue.A} шахты уже есть дверь — эта не обслуживается лифтом " +
                           "(один этаж = одна дверь: и команда, и клетка для клиента берутся из одного слота)";
                case LiftScanIssueKind.ShaftWithoutStops:
                    return "у шахты нет ни одной остановки";
                default:
                    return issue.Kind.ToString();
            }
        }
    }
}
