#if UNITY_EDITOR
using UnityEngine;

namespace Client.Net.View
{
    /// <summary>Editor-only диагностика reveal: повесь на GameObject тайла (стену, что не рисуется) — компонент
    /// выводит ПРИЧИНУ, почему рендерер reveal скрыт (не кандидат / cheb&gt;radius / кластер далеко).
    /// ПКМ по компоненту → «Reveal: Dump в консоль»; или выдели объект в сцене — метка-сводка у тайла.
    /// Весь файл под UNITY_EDITOR — в билд не входит (только инструмент маппера).</summary>
    public class CeilingRevealProbe : MonoBehaviour
    {
        private MapRenderer _mr;
        private Renderer _renderer;

        private MapRenderer Mr => _mr != null ? _mr : (_mr = FindFirstObjectByType<MapRenderer>());
        private Renderer Rend => _renderer != null ? _renderer : (_renderer = GetComponentInChildren<Renderer>(true));

        [ContextMenu("Reveal: Dump в консоль")]
        private void Dump()
        {
            var mr = Mr;
            if (mr == null) { Debug.LogWarning("[CeilingRevealProbe] MapRenderer не найден в сцене."); return; }
            var rend = Rend;
            if (rend == null) { Debug.LogWarning("[CeilingRevealProbe] Renderer не найден на объекте/детях."); return; }
            Debug.Log(mr.DescribeReveal(rend), this);
        }

        // Живая метка у тайла при выделении: видно вердикт-причину прямо в сцене при движении игрока (Play Mode).
        private void OnDrawGizmosSelected()
        {
            var mr = Mr;
            var rend = Rend;
            if (mr == null || rend == null) return;
            UnityEditor.Handles.Label(transform.position + Vector3.up * 0.5f, Brief(mr.DescribeReveal(rend)));
        }

        // Короткая сводка для метки: только строки candidate/gate/ВЕРДИКТ (полный дамп — через ПКМ → Dump).
        private static string Brief(string full)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var line in full.Split('\n'))
                if (line.Contains("ВЕРДИКТ") || line.Contains("gate(live)") || line.Contains("candidate:"))
                    sb.AppendLine(line.Trim());
            return sb.Length > 0 ? sb.ToString() : full;
        }
    }
}
#endif
