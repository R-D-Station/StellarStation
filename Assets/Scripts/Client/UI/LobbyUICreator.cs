using UnityEngine;
using UnityEngine.UI;

namespace Client.UI
{
    public class LobbyUICreator : MonoBehaviour
    {
        private LobbyUI _ui;

        private void Awake()
        {
            _ui = GetComponent<LobbyUI>();
            if (_ui == null) _ui = gameObject.AddComponent<LobbyUI>();
            BuildUI();
            _ui.Initialize();
        }

        private void BuildUI()
        {
            Canvas canvas = GetCanvas();

            // === Главная панель ===
            var root = Panel(canvas.transform, "LobbyPanel", 0, 0, 1, 1, new Color(0.1f, 0.1f, 0.15f));

            // === Левая панель: игроки ===
            var left = Panel(root.transform, "LeftPanel", 0.02f, 0.02f, 0.28f, 0.96f, new Color(0.15f, 0.15f, 0.2f));
            Label(left.transform, "PlayersTitle", "ИГРОКИ", 0.05f, 0.88f, 0.95f, 0.98f, 18, FontStyle.Bold, TextAnchor.MiddleCenter);
            Text playersList = Label(left.transform, "PlayersList", "", 0.05f, 0.02f, 0.95f, 0.86f, 14, FontStyle.Normal, TextAnchor.UpperLeft);

            // === Правая панель: чат ===
            var right = Panel(root.transform, "RightPanel", 0.30f, 0.02f, 0.98f, 0.96f, new Color(0.15f, 0.15f, 0.2f));
            Label(right.transform, "ChatTitle", "ЧАТ", 0.05f, 0.88f, 0.95f, 0.98f, 18, FontStyle.Bold, TextAnchor.MiddleCenter);

            // История чата — простой текст с маской
            var chatArea = Panel(right.transform, "ChatArea", 0.02f, 0.14f, 0.98f, 0.86f, new Color(0.08f, 0.08f, 0.12f));
            Mask mask = chatArea.AddComponent<Mask>();
            mask.showMaskGraphic = false;
            Image maskImg = chatArea.GetComponent<Image>();
            maskImg.color = new Color(0.08f, 0.08f, 0.12f);

            Text chatHistory = Label(chatArea.transform, "ChatHistory", "", 0, 0, 1, 1, 14, FontStyle.Normal, TextAnchor.LowerLeft);
            chatHistory.horizontalOverflow = HorizontalWrapMode.Wrap;
            chatHistory.verticalOverflow = VerticalWrapMode.Overflow;

            // Поле ввода
            var inputArea = Panel(right.transform, "InputArea", 0.02f, 0.02f, 0.82f, 0.12f, Color.white);
            InputField chatInput = inputArea.AddComponent<InputField>();

            var inputTextGo = new GameObject("Text");
            inputTextGo.transform.SetParent(inputArea.transform, false);
            var itr = inputTextGo.AddComponent<RectTransform>();
            itr.anchorMin = Vector2.zero; itr.anchorMax = Vector2.one;
            itr.offsetMin = new Vector2(8, 4); itr.offsetMax = new Vector2(-8, -4);
            Text inputText = inputTextGo.AddComponent<Text>();
            inputText.font = Font; inputText.fontSize = 14; inputText.color = Color.black; inputText.alignment = TextAnchor.MiddleLeft;
            chatInput.textComponent = inputText;

            var phGo = new GameObject("Placeholder");
            phGo.transform.SetParent(inputArea.transform, false);
            var phr = phGo.AddComponent<RectTransform>();
            phr.anchorMin = Vector2.zero; phr.anchorMax = Vector2.one;
            phr.offsetMin = new Vector2(8, 4); phr.offsetMax = new Vector2(-8, -4);
            Text ph = phGo.AddComponent<Text>();
            ph.text = "Введите сообщение..."; ph.font = Font; ph.fontSize = 14; ph.fontStyle = FontStyle.Italic; ph.color = Color.gray; ph.alignment = TextAnchor.MiddleLeft;
            chatInput.placeholder = ph;

            // Кнопка
            var btnGo = Panel(right.transform, "SendBtn", 0.84f, 0.02f, 0.98f, 0.12f, new Color(0.25f, 0.45f, 0.75f));
            Button sendBtn = btnGo.AddComponent<Button>();
            Label(btnGo.transform, "BtnText", "Отпр.", 0, 0, 1, 1, 14, FontStyle.Normal, TextAnchor.MiddleCenter).color = Color.white;

            // Привязка
            _ui.LobbyPanel = root;
            _ui.PlayersListText = playersList;
            _ui.ChatInput = chatInput;
            _ui.SendButton = sendBtn;
            _ui.ChatHistoryText = chatHistory;

            root.SetActive(false);
        }

        // --- Хелперы ---
        private Canvas GetCanvas()
        {
            var c = FindFirstObjectByType<Canvas>();
            if (c) return c;
            var go = new GameObject("Canvas");
            c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            go.AddComponent<CanvasScaler>();
            go.AddComponent<GraphicRaycaster>();
            return c;
        }

        private static Font Font => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        private GameObject Panel(Transform parent, string name, float ax, float ay, float bx, float by, Color c)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(ax, ay); rt.anchorMax = new Vector2(bx, by); rt.sizeDelta = Vector2.zero;
            go.AddComponent<Image>().color = c;
            return go;
        }

        private Text Label(Transform parent, string name, string txt, float ax, float ay, float bx, float by, int size, FontStyle style, TextAnchor align)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(ax, ay); rt.anchorMax = new Vector2(bx, by);
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var t = go.AddComponent<Text>();
            t.text = txt; t.font = Font; t.fontSize = size; t.fontStyle = style; t.color = Color.white; t.alignment = align;
            return t;
        }
    }
}