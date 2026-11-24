using System;
using System.IO;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace WindBot.Game.AI
{
    [DataContract]
    public class DialogsData
    {
        [DataMember]
        public string[] welcome { get; set; }
        [DataMember]
        public string[] deckerror { get; set; }
        [DataMember]
        public string[] duelstart { get; set; }
        [DataMember]
        public string[] newturn { get; set; }
        [DataMember]
        public string[] endturn { get; set; }
        [DataMember]
        public string[] directattack { get; set; }
        [DataMember]
        public string[] attack { get; set; }
        [DataMember]
        public string[] ondirectattack { get; set; }
        [DataMember]
        public string facedownmonstername { get; set; }
        [DataMember]
        public string[] activate { get; set; }
        [DataMember]
        public string[] summon { get; set; }
        [DataMember]
        public string[] setmonster { get; set; }
        [DataMember]
        public string[] chaining { get; set; }
        [DataMember]
        public string[] surrender { get; set; }
        [DataMember]
        public string[] custom { get; set; }
    }
    public class Dialogs
    {
        private GameClient _game;

        private string[] _welcome;
        private string[] _deckerror;
        private string[] _duelstart;
        private string[] _newturn;
        private string[] _endturn;
        private string[] _directattack;
        private string[] _attack;
        private string[] _ondirectattack;
        private string _facedownmonstername;
        private string[] _activate;
        private string[] _summon;
        private string[] _setmonster;
        private string[] _chaining;
        private string[] _surrender;
        private string[] _custom;

        public Dialogs(GameClient game)
        {
            _game = game;
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(DialogsData));
            string dialogfilename = string.IsNullOrWhiteSpace(game.Dialog) ? "default" : game.Dialog;
            try
            {
                using (FileStream fs = WindBotRuntime.ReadFile("Dialogs", dialogfilename, "json"))
                {
                    DialogsData data = (DialogsData)serializer.ReadObject(fs);
                    _welcome = data?.welcome ?? Array.Empty<string>();
                    _deckerror = data?.deckerror ?? Array.Empty<string>();
                    _duelstart = data?.duelstart ?? Array.Empty<string>();
                    _newturn = data?.newturn ?? Array.Empty<string>();
                    _endturn = data?.endturn ?? Array.Empty<string>();
                    _directattack = data?.directattack ?? Array.Empty<string>();
                    _attack = data?.attack ?? Array.Empty<string>();
                    _ondirectattack = data?.ondirectattack ?? Array.Empty<string>();
                    _facedownmonstername = string.IsNullOrEmpty(data?.facedownmonstername) ? "monster" : data.facedownmonstername;
                    _activate = data?.activate ?? Array.Empty<string>();
                    _summon = data?.summon ?? Array.Empty<string>();
                    _setmonster = data?.setmonster ?? Array.Empty<string>();
                    _chaining = data?.chaining ?? Array.Empty<string>();
                    _surrender = data?.surrender ?? Array.Empty<string>();
                    _custom = data?.custom ?? Array.Empty<string>();
                }
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                // 未提供 Dialogs 资源时退化为空对话集，避免阻塞无对话文件场景下的连接与对局。
                Logger.DebugWriteLine($"Dialog file '{dialogfilename}.json' not found, using empty dialogs.");
                _welcome = Array.Empty<string>();
                _deckerror = Array.Empty<string>();
                _duelstart = Array.Empty<string>();
                _newturn = Array.Empty<string>();
                _endturn = Array.Empty<string>();
                _directattack = Array.Empty<string>();
                _attack = Array.Empty<string>();
                _ondirectattack = Array.Empty<string>();
                _facedownmonstername = "monster";
                _activate = Array.Empty<string>();
                _summon = Array.Empty<string>();
                _setmonster = Array.Empty<string>();
                _chaining = Array.Empty<string>();
                _surrender = Array.Empty<string>();
                _custom = Array.Empty<string>();
            }
        }

        public void SendSorry()
        {
            InternalSendMessageForced(new[] { "Sorry, an error occurs." });
        }

        public void SendDeckSorry(string card)
        {
            if (card == "DECK")
                InternalSendMessageForced(new[] { "Deck illegal. Please check the database of your YGOPro and WindBot." });
            else
                InternalSendMessageForced(_deckerror, card);
        }

        public void SendWelcome()
        {
            InternalSendMessage(_welcome);
        }

        public void SendDuelStart()
        {
            InternalSendMessage(_duelstart);
        }

        public void SendNewTurn()
        {
            InternalSendMessage(_newturn);
        }

        public void SendEndTurn()
        {
            InternalSendMessage(_endturn);
        }

        public void SendDirectAttack(string attacker)
        {
            InternalSendMessage(_directattack, attacker);
        }

        public void SendAttack(string attacker, string defender)
        {
            if (defender=="monster")
            {
                defender = _facedownmonstername;
            }
            InternalSendMessage(_attack, attacker, defender);
        }

        public void SendOnDirectAttack(string attacker)
        {
            if (string.IsNullOrEmpty(attacker))
            {
                attacker = _facedownmonstername;
            }
            InternalSendMessage(_ondirectattack, attacker);
        }
        public void SendOnDirectAttack()
        {
            InternalSendMessage(_ondirectattack);
        }

        public void SendActivate(string spell)
        {
            InternalSendMessage(_activate, spell);
        }

        public void SendSummon(string monster)
        {
            InternalSendMessage(_summon, monster);
        }

        public void SendSetMonster()
        {
            InternalSendMessage(_setmonster);
        }

        public void SendChaining(string card)
        {
            InternalSendMessage(_chaining, card);
        }

        public void SendSurrender()
        {
            InternalSendMessage(_surrender);
        }

        private void InternalSendMessage(IList<string> array, params object[] opts)
        {
            if (!_game._chat)
                return;
            if (array == null || array.Count == 0)
                return;
            string message = string.Format(array[WindBotRuntime.Random.Next(array.Count)], opts);
            if (message != "")
                _game.Chat(message);
        }

        private void InternalSendMessageForced(IList<string> array, params object[] opts)
        {
            if (array == null || array.Count == 0)
                return;
            string message = string.Format(array[WindBotRuntime.Random.Next(array.Count)], opts);
            if (message != "")
                _game.Chat(message);
        }

        public void SendCustomChat(int index, params object[] opts)
        {
            if (!_game._chat || _custom == null || index < 0 || index >= _custom.Length)
                return;
            string message = string.Format(_custom[index], opts);
            if (message != "")
                _game.Chat(message);
        }
    }
}

