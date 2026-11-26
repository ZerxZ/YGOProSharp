using System.Text;
using Microsoft.Extensions.Logging;
using YGOProSharp.Abstractions.Logging;
using YGOProSharp.Abstractions.Ocg.Enums;
using YGOProSharp.Core;
using YGOProSharp.Core.Cards;
using YGOProSharp.Protocol;
using YGOProSharp.Protocol.Enums;
using YGOProSharp.Protocol.Utils;

namespace YGOProSharp.AgentBridge;

public sealed partial class AgentDuelSession
{
    private sealed class TrackedCard
    {
        public int Code { get; set; }
        public CardLocation Location { get; set; }
        public int Sequence { get; set; }
        public int Position { get; set; }
        public int? Attack { get; set; }
        public int? Defense { get; set; }
        public int? Level { get; set; }
        public List<int> Overlays { get; } = new();

        public void ReadQueryBlock(BinaryReader reader, Func<int, int> mapPlayer)
        {
            int flag = reader.ReadInt32();
            if ((flag & (int)Query.Code) != 0)
                Code = reader.ReadInt32();
            if ((flag & (int)Query.Position) != 0)
            {
                mapPlayer(reader.ReadByte());
                Location = (CardLocation)reader.ReadByte();
                Sequence = reader.ReadByte();
                Position = reader.ReadByte();
            }
            if ((flag & (int)Query.Alias) != 0) reader.ReadInt32();
            if ((flag & (int)Query.Type) != 0) reader.ReadInt32();
            if ((flag & (int)Query.Level) != 0) Level = reader.ReadInt32() & 0xff;
            if ((flag & (int)Query.Rank) != 0)
            {
                int r = reader.ReadInt32() & 0xff;
                if (r > 0) Level = r;
            }
            if ((flag & (int)Query.Attribute) != 0) reader.ReadInt32();
            if ((flag & (int)Query.Race) != 0) reader.ReadInt32();
            if ((flag & (int)Query.Attack) != 0) Attack = reader.ReadInt32();
            if ((flag & (int)Query.Defence) != 0) Defense = reader.ReadInt32();
            if ((flag & (int)Query.BaseAttack) != 0) reader.ReadInt32();
            if ((flag & (int)Query.BaseDefence) != 0) reader.ReadInt32();
            if ((flag & (int)Query.Reason) != 0) reader.ReadInt32();
            if ((flag & (int)Query.ReasonCard) != 0) reader.ReadInt32();
            if ((flag & (int)Query.EquipCard) != 0) reader.ReadInt32();
            if ((flag & (int)Query.TargetCard) != 0)
            {
                int tc = reader.ReadInt32();
                for (int i = 0; i < tc; i++) reader.ReadInt32();
            }
            if ((flag & (int)Query.OverlayCard) != 0)
            {
                Overlays.Clear();
                int oc = reader.ReadInt32();
                for (int i = 0; i < oc; i++) Overlays.Add(reader.ReadInt32());
            }
            if ((flag & (int)Query.Counters) != 0)
            {
                int cc = reader.ReadInt32();
                for (int i = 0; i < cc; i++) reader.ReadInt32();
            }
            if ((flag & (int)Query.Owner) != 0) reader.ReadInt32();
            if ((flag & (int)Query.Status) != 0) reader.ReadInt32();
            if ((flag & (int)Query.LScale) != 0) reader.ReadInt32();
            if ((flag & (int)Query.RScale) != 0) reader.ReadInt32();
            if ((flag & (int)Query.Link) != 0)
            {
                int lc = reader.ReadInt32();
                reader.ReadInt32();
                if (lc > 0) Level = lc;
            }
        }
    }

    private sealed class SideTracker
    {
        public int LifePoints { get; set; } = 8000;
        public List<TrackedCard> Deck { get; } = new();
        public List<TrackedCard> Hand { get; } = new();
        public List<TrackedCard> Grave { get; } = new();
        public List<TrackedCard> Banished { get; } = new();
        public List<TrackedCard> Extra { get; } = new();
        public TrackedCard?[] MonsterZone { get; } = new TrackedCard?[7];
        public TrackedCard?[] SpellZone { get; } = new TrackedCard?[8];

        public void Reset(int lp, int deckCount, int extraCount)
        {
            LifePoints = lp;
            Deck.Clear();
            Hand.Clear();
            Grave.Clear();
            Banished.Clear();
            Extra.Clear();
            Array.Clear(MonsterZone);
            Array.Clear(SpellZone);

            for (int i = 0; i < deckCount; i++)
                Deck.Add(new TrackedCard { Code = 0, Location = CardLocation.Deck, Sequence = i, Position = (int)CardPosition.FaceDownDefence });
            for (int i = 0; i < extraCount; i++)
                Extra.Add(new TrackedCard { Code = 0, Location = CardLocation.Extra, Sequence = i, Position = (int)CardPosition.FaceDownDefence });
        }

        public TrackedCard? GetCard(CardLocation loc, int seq)
        {
            return loc switch
            {
                CardLocation.MonsterZone => seq >= 0 && seq < MonsterZone.Length ? MonsterZone[seq] : null,
                CardLocation.SpellZone => seq >= 0 && seq < SpellZone.Length ? SpellZone[seq] : null,
                CardLocation.Hand => seq >= 0 && seq < Hand.Count ? Hand[seq] : null,
                CardLocation.Grave => seq >= 0 && seq < Grave.Count ? Grave[seq] : null,
                CardLocation.Removed => seq >= 0 && seq < Banished.Count ? Banished[seq] : null,
                CardLocation.Extra => seq >= 0 && seq < Extra.Count ? Extra[seq] : null,
                CardLocation.Deck => seq >= 0 && seq < Deck.Count ? Deck[seq] : null,
                _ => null
            };
        }

        public TrackedCard? RemoveCard(CardLocation loc, int seq)
        {
            switch (loc)
            {
                case CardLocation.MonsterZone when seq >= 0 && seq < MonsterZone.Length:
                {
                    TrackedCard? c = MonsterZone[seq];
                    MonsterZone[seq] = null;
                    return c;
                }
                case CardLocation.SpellZone when seq >= 0 && seq < SpellZone.Length:
                {
                    TrackedCard? c = SpellZone[seq];
                    SpellZone[seq] = null;
                    return c;
                }
                case CardLocation.Hand when seq >= 0 && seq < Hand.Count:
                {
                    TrackedCard c = Hand[seq];
                    Hand.RemoveAt(seq);
                    return c;
                }
                case CardLocation.Grave when seq >= 0 && seq < Grave.Count:
                {
                    TrackedCard c = Grave[seq];
                    Grave.RemoveAt(seq);
                    return c;
                }
                case CardLocation.Removed when seq >= 0 && seq < Banished.Count:
                {
                    TrackedCard c = Banished[seq];
                    Banished.RemoveAt(seq);
                    return c;
                }
                case CardLocation.Extra when seq >= 0 && seq < Extra.Count:
                {
                    TrackedCard c = Extra[seq];
                    Extra.RemoveAt(seq);
                    return c;
                }
                case CardLocation.Deck when Deck.Count > 0:
                {
                    int idx = seq >= 0 && seq < Deck.Count ? seq : Deck.Count - 1;
                    TrackedCard c = Deck[idx];
                    Deck.RemoveAt(idx);
                    return c;
                }
                default:
                    return null;
            }
        }

        public void AddCard(CardLocation loc, int seq, int pos, TrackedCard card)
        {
            card.Location = loc;
            card.Sequence = seq;
            card.Position = pos;
            switch (loc)
            {
                case CardLocation.MonsterZone when seq >= 0 && seq < MonsterZone.Length:
                    MonsterZone[seq] = card;
                    break;
                case CardLocation.SpellZone when seq >= 0 && seq < SpellZone.Length:
                    SpellZone[seq] = card;
                    break;
                case CardLocation.Hand:
                    Hand.Add(card);
                    break;
                case CardLocation.Grave:
                    Grave.Add(card);
                    break;
                case CardLocation.Removed:
                    Banished.Add(card);
                    break;
                case CardLocation.Extra:
                    Extra.Add(card);
                    break;
                case CardLocation.Deck:
                    Deck.Add(card);
                    break;
            }
        }

        public IList<TrackedCard>? GetZoneCollection(CardLocation loc) => loc switch
        {
            CardLocation.Hand => Hand,
            CardLocation.Grave => Grave,
            CardLocation.Removed => Banished,
            CardLocation.Extra => Extra,
            CardLocation.Deck => Deck,
            CardLocation.MonsterZone => MonsterZone.Where(static c => c is not null).Cast<TrackedCard>().ToList(),
            CardLocation.SpellZone => SpellZone.Where(static c => c is not null).Cast<TrackedCard>().ToList(),
            _ => null
        };

        public AgentFieldSideView BuildView(INamedCardRepository repo, AgentObservationDetail detail)
        {
            bool includeEffects = detail == AgentObservationDetail.Full;
            bool includeGraveList = detail != AgentObservationDetail.Minimal;
            int faceUpExtra = Extra.Count(static c => (c.Position & (int)CardPosition.FaceUp) != 0);

            IReadOnlyList<AgentCardView>? knownExtra = null;
            if (detail == AgentObservationDetail.Full)
            {
                var allKnown = Extra
                    .Select((c, idx) => (Card: c, Idx: idx))
                    .Where(static x => x.Card.Code > 0)
                    .Select(x => ToCardView(x.Card, x.Idx, "Extra", repo, includeEffects: true))
                    .ToArray();
                if (allKnown.Length > 0)
                    knownExtra = allKnown;
            }
            else if (faceUpExtra > 0)
            {
                knownExtra = Extra
                    .Select((c, idx) => (Card: c, Idx: idx))
                    .Where(static x => x.Card.Code > 0 && (x.Card.Position & (int)CardPosition.FaceUp) != 0)
                    .Select(x => ToCardView(x.Card, x.Idx, "Extra", repo, includeEffects: false))
                    .ToArray();
            }

            return new AgentFieldSideView(
                LifePoints: LifePoints,
                DeckCount: Deck.Count,
                HandCount: Hand.Count,
                ExtraCount: Extra.Count,
                FaceUpExtraCount: faceUpExtra,
                GraveCount: Grave.Count,
                BanishedCount: Banished.Count,
                Hand: Hand.Select((c, idx) => ToCardView(c, idx, "Hand", repo, includeEffects)).ToArray(),
                MonsterZone: MonsterZone
                    .Select((c, idx) => c is null ? null : ToCardView(c, idx, "MonsterZone", repo, includeEffects))
                    .Where(static x => x is not null)
                    .Cast<AgentCardView>()
                    .ToArray(),
                SpellZone: SpellZone
                    .Select((c, idx) => c is null ? null : ToCardView(c, idx, "SpellZone", repo, includeEffects))
                    .Where(static x => x is not null)
                    .Cast<AgentCardView>()
                    .ToArray(),
                Graveyard: includeGraveList ? Grave.Select((c, idx) => ToCardView(c, idx, "Grave", repo, includeEffects)).ToArray() : null,
                Banished: includeGraveList ? Banished.Select((c, idx) => ToCardView(c, idx, "Removed", repo, includeEffects)).ToArray() : null,
                KnownExtraDeck: knownExtra);
        }

        private static AgentCardView ToCardView(TrackedCard c, int index, string loc, INamedCardRepository repo, bool includeEffects)
        {
            bool hidden = c.Code <= 0;
            NamedCard? nc = !hidden && repo.TryGetCard(c.Code, out NamedCard? found) ? found : null;
            string? shortDesc = null;
            if (includeEffects && nc is not null)
            {
                shortDesc = nc.Description;
                if (shortDesc is { Length: > 160 })
                    shortDesc = shortDesc[..160] + "...";
            }

            return new AgentCardView(
                Index: index,
                Code: hidden ? 0 : c.Code,
                Name: hidden ? null : (nc?.Name ?? $"#{c.Code}"),
                Location: loc,
                Sequence: c.Sequence,
                Position: ((CardPosition)c.Position).ToString(),
                IsHidden: hidden,
                Attack: hidden ? null : (c.Attack ?? ((nc is not null && (nc.Type & (int)CardType.Monster) != 0) ? nc.Attack : null)),
                Defense: hidden ? null : (c.Defense ?? ((nc is not null && (nc.Type & (int)CardType.Monster) != 0) ? nc.Defense : null)),
                Level: hidden ? null : (c.Level ?? (nc is not null && (nc.Type & (int)CardType.Monster) != 0 ? (int)(nc.Level & 0xff) : null)),
                OverlayCount: c.Overlays.Count,
                ShortEffect: shortDesc);
        }
    }
}
