using UnityEngine;
using UnityEngine.UI;
using Client.Core;

namespace Client.UI
{
    public class LoginUI : MonoBehaviour
    {
        private InputField _ipInputField;
        private InputField _loginInputField;
        private Button _connectButton;
        private Text _errorText;

        private void Awake()
        {
            CreateUI();
        }

        private void CreateUI()
        {
            Canvas canvas = FindFirstObjectByType<Canvas>();
            if (canvas == null)
            {
                GameObject canvasObj = new GameObject("Canvas");
                canvas = canvasObj.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvasObj.AddComponent<CanvasScaler>();
                canvasObj.AddComponent<GraphicRaycaster>();
            }

            GameObject panelObj = new GameObject("LoginPanel");
            panelObj.transform.SetParent(canvas.transform, false);
            RectTransform panelRect = panelObj.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(400, 300);
            panelRect.anchoredPosition = Vector2.zero;
            Image panelImage = panelObj.AddComponent<Image>();
            panelImage.color = new Color(0.2f, 0.2f, 0.2f, 0.9f);

            _ipInputField = CreateInputField(panelRect, "IPInput", "127.0.0.1", new Vector2(0, 80));
            _loginInputField = CreateInputField(panelRect, "LoginInput", "Имя игрока", new Vector2(0, 20));
            _connectButton = CreateButton(panelRect, "ConnectButton", "Подключиться", new Vector2(0, -50));
            _connectButton.onClick.AddListener(OnConnectClicked);

            _errorText = CreateText(panelRect, "ErrorText", "", new Vector2(0, -110));
            _errorText.color = Color.red;
            _errorText.alignment = TextAnchor.MiddleCenter;

            _ipInputField.text = PlayerPrefs.GetString("LastIP", "127.0.0.1");
            _loginInputField.text = PlayerPrefs.GetString("LastLogin", "Player");
        }

        private InputField CreateInputField(RectTransform parent, string name, string placeholder, Vector2 position)
        {
            GameObject inputObj = new GameObject(name);
            inputObj.transform.SetParent(parent, false);
            RectTransform rect = inputObj.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(300, 40);
            rect.anchoredPosition = position;

            Image bg = inputObj.AddComponent<Image>();
            bg.color = Color.white;

            InputField inputField = inputObj.AddComponent<InputField>();

            GameObject textArea = new GameObject("Text");
            textArea.transform.SetParent(inputObj.transform, false);
            RectTransform textAreaRect = textArea.AddComponent<RectTransform>();
            textAreaRect.anchorMin = Vector2.zero;
            textAreaRect.anchorMax = Vector2.one;
            textAreaRect.sizeDelta = new Vector2(-10, -6);
            textAreaRect.anchoredPosition = new Vector2(5, -3);

            Text text = textArea.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 18;
            text.color = Color.black;
            text.alignment = TextAnchor.MiddleLeft;
            inputField.textComponent = text;

            GameObject placeholderObj = new GameObject("Placeholder");
            placeholderObj.transform.SetParent(inputObj.transform, false);
            RectTransform placeholderRect = placeholderObj.AddComponent<RectTransform>();
            placeholderRect.anchorMin = Vector2.zero;
            placeholderRect.anchorMax = Vector2.one;
            placeholderRect.sizeDelta = new Vector2(-10, -6);
            placeholderRect.anchoredPosition = new Vector2(5, -3);

            Text placeholderText = placeholderObj.AddComponent<Text>();
            placeholderText.text = placeholder;
            placeholderText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            placeholderText.fontSize = 18;
            placeholderText.fontStyle = FontStyle.Italic;
            placeholderText.color = Color.gray;
            placeholderText.alignment = TextAnchor.MiddleLeft;
            inputField.placeholder = placeholderText;

            return inputField;
        }

        private Button CreateButton(RectTransform parent, string name, string text, Vector2 position)
        {
            GameObject buttonObj = new GameObject(name);
            buttonObj.transform.SetParent(parent, false);
            RectTransform rect = buttonObj.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(200, 40);
            rect.anchoredPosition = position;

            Image image = buttonObj.AddComponent<Image>();
            image.color = new Color(0.3f, 0.5f, 0.8f);

            Button button = buttonObj.AddComponent<Button>();

            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(buttonObj.transform, false);
            RectTransform textRect = textObj.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;

            Text label = textObj.AddComponent<Text>();
            label.text = text;
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 20;
            label.color = Color.white;
            label.alignment = TextAnchor.MiddleCenter;

            return button;
        }

        private Text CreateText(RectTransform parent, string name, string text, Vector2 position)
        {
            GameObject textObj = new GameObject(name);
            textObj.transform.SetParent(parent, false);
            RectTransform rect = textObj.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(300, 30);
            rect.anchoredPosition = position;

            Text textComponent = textObj.AddComponent<Text>();
            textComponent.text = text;
            textComponent.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            textComponent.fontSize = 16;
            textComponent.color = Color.white;

            return textComponent;
        }

        private void OnConnectClicked()
        {
            if (string.IsNullOrEmpty(_ipInputField.text) || string.IsNullOrEmpty(_loginInputField.text))
            {
                ShowError("Заполните все поля!");
                return;
            }

            PlayerPrefs.SetString("LastIP", _ipInputField.text);
            PlayerPrefs.SetString("LastLogin", _loginInputField.text);
            PlayerPrefs.Save();

            GameManager.Instance.ConnectToServer(_ipInputField.text, _loginInputField.text);
        }

        public void ShowError(string message) => _errorText.text = message;
        public void ShowConnecting() => _errorText.text = "Подключение...";
    }
}