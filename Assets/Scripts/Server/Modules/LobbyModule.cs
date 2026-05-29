using System.Collections.Generic;
using Server.Network;

namespace Server.Modules
{
    public class LobbyModule
    {
        private List<ServerClient> _players = new List<ServerClient>();

        public void AddPlayer(ServerClient client)
        {
            _players.Add(client);
        }

        public void RemovePlayer(ServerClient client)
        {
            _players.Remove(client);
        }

        public string[] GetPlayerList()
        {
            var names = new string[_players.Count];
            for (int i = 0; i < _players.Count; i++)
                names[i] = _players[i].PlayerName;
            return names;
        }
    }
}