using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace YGOProSharp.Core
{
    public class Banlist
    {
        // O(1) 数量索引，避免卡组校验时对 4 个 List 做线性 Contains 扫描。
        private readonly Dictionary<int, int> _quantities = new();

        public IList<int> BannedIds { get; private set; }
        public IList<int> LimitedIds { get; private set; }
        public IList<int> SemiLimitedIds { get; private set; }
        public IList<int> UnlimitedIds { get; private set; }
        public bool WhitelistOnly { get; private set; }
        public uint Hash { get; private set; }
        public string Name { get; }

        public Banlist(string name = "")
        {
            Name = name;
            BannedIds = new List<int>();
            LimitedIds = new List<int>();
            SemiLimitedIds = new List<int>();
            UnlimitedIds = new List<int>();
            WhitelistOnly = false;
            Hash = 0x7dfcee6a;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetQuantity(int cardId)
        {
            if (_quantities.TryGetValue(cardId, out int quantity))
                return quantity;

            return WhitelistOnly ? 0 : 3;
        }

        public void EnableWhitelistMode()
        {
            if (WhitelistOnly)
                return;
            WhitelistOnly = true;
            Hash ^= 0x0f0f0f0f;
        }

        public void Add(int cardId, int quantity)
        {
            if (quantity < 0 || quantity > 3)
                return;
            switch (quantity)
            {
                case 0:
                    BannedIds.Add(cardId);
                    break;
                case 1:
                    LimitedIds.Add(cardId);
                    break;
                case 2:
                    SemiLimitedIds.Add(cardId);
                    break;
                case 3:
                    UnlimitedIds.Add(cardId);
                    break;
            }
            // 与原 GetQuantity 优先级一致（Banned < Limited < SemiLimited < Unlimited）。
            if (!_quantities.TryGetValue(cardId, out int existing) || quantity < existing)
                _quantities[cardId] = quantity;

            uint code = (uint)cardId;
            Hash = Hash ^ ((code << 18) | (code >> 14)) ^ ((code << (27 + quantity)) | (code >> (5 - quantity)));
        }
    }
}
