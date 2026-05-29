using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Client.Core;

namespace Client.UI
{
    public class LobbyUI : MonoBehaviour
    {
        public GameObject LobbyPanel;
        public Text PlayersListText;
        public InputField ChatInput;
        public Button SendButton;
        public Text ChatHistoryText;

        private List<string> _chatMessages = new();
        private const int MaxChatMessages = 15;

        public void Initialize()
        {
            if (SendButton != null)
            {
                SendButton.onClick.RemoveAllListeners();
                SendButton.onClick.AddListener(OnSendClicked);
            }
        }

        public void Show()
        {
            if (LobbyPanel == null) return;
            LobbyPanel.SetActive(true);
            if (ChatInput != null)
            {
                ChatInput.text = "";
                ChatInput.ActivateInputField();
            }
        }

        public void Hide()
        {
            if (LobbyPanel != null)
                LobbyPanel.SetActive(false);
        }

        public void UpdatePlayerList(string[] players)
        {
            if (PlayersListText == null) return;
            PlayersListText.text = "»гроки в лобби:\n";
            if (players == null) return;
            foreach (string p in players)
                PlayersListText.text += $"Х {p}\n";
        }

        public void AddChatMessage(string playerName, string message)
        {
            if (ChatHistoryText == null) return;

            string entry = string.IsNullOrEmpty(playerName)
                ? $"<color=grey>{message}</color>"
                : $"<b>{playerName}:</b> {message}";

            _chatMessages.Add(entry);
            while (_chatMessages.Count > MaxChatMessages)
                _chatMessages.RemoveAt(0);

            ChatHistoryText.text = string.Join("\n", _chatMessages);
        }

        private void OnSendClicked()
        {
            if (ChatInput == null) return;
            string text = ChatInput.text.Trim();
            if (string.IsNullOrEmpty(text)) return;
            ChatInput.text = "";
            ChatInput.ActivateInputField();
            GameManager.Instance.SendChatMessage(text);
        }
    }
}