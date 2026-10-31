using System.Collections.Concurrent;
using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay;

namespace Meitou.Simulation.Items;

/// <summary>
/// One item a character carries (an INVENTORY_ITEM_STATE of a save). The fields keep the save's names (docs/formats/save.md): <c>base data sid</c> = <see cref="Record"/>,
/// <c>material sid</c>, <c>company sid</c>, <c>section</c>, <c>inventory x</c> / <c>inventory y</c>, <c>quantity</c>, <c>level</c>, <c>item function</c>, <c>quality</c>, <c>charges</c>.
/// </summary>
public sealed class ItemInstance
{
    public required string Record { get; init; }
    public string Name { get; init; } = "";
    public FcsRecordType Type { get; init; }
    /// <summary>The armour section allowed by the record's slot; empty for other items.</summary>
    public string EquipmentSection { get; init; } = "";
    /// <summary>ARMOUR <c>is locked</c> (the original makes it a <c>LockedArmour</c>: shackles); worn, it shows the portrait's <c>Slave</c> overlay (docs/game/ui-screens.md "Portrait cells").</summary>
    public bool IsLocked { get; internal set; }
    /// <summary>The record's race limits (<see cref="ItemInfo.WornOnlyBy"/>, <see cref="ItemInfo.WornNeverBy"/>): who may put it in an equipment section.</summary>
    public string[] WornOnlyBy { get; init; } = [];
    public string[] WornNeverBy { get; init; } = [];
    /// <summary>Whether a character of the RACE <paramref name="race"/> may wear it (null: no wearer, no limit).</summary>
    public bool WearableBy(string? race, bool animal = false) => Wearable(WornOnlyBy, WornNeverBy, race, animal);
    internal static bool Wearable(string[] only, string[] never, string? race, bool animal = false) => race is null || (only.Length == 0 ? !animal && !never.Contains(race) : only.Contains(race));
    /// <summary>Shackles' lock (level = ARMOUR <c>lock level</c> clamped 0..100, its <c>hardness</c>, locked when made: <c>FUN_14089a440</c>); null for other items.</summary>
    public Locks.LockState? Lock { get; set; }
    /// <summary>Worn and effectively locked: the item cannot be taken off, sold or moved (LockedArmour's tooltip: "locked in place and cannot be removed").</summary>
    public bool LockedOn => Lock is { EffectivelyLocked: true } && InventoryCommands.IsEquipmentSection(Section);
    public string MaterialId { get; set; } = "";
    public string CompanyId { get; set; } = "";
    /// <summary>Where it is: <c>main</c>, <c>shirt</c>, <c>head</c>, <c>boots</c>, <c>armour</c>, <c>legs</c>, <c>hip</c>, <c>back</c>, <c>backpack_attach</c> (a worn backpack), <c>backpack_content</c> (inside it).</summary>
    public string Section { get; set; } = "main";
    public int X { get; set; }
    public int Y { get; set; }
    /// <summary>The units of the stack.</summary>
    public int Quantity { get; set; } = 1;
    // The open trade's buyback table entry for this item: the trade, and the count it holds.
    TradeSession? tradedIn;
    int traded;
    /// <summary>
    /// The item's count in the open trade's buyback table (item -> units traded in it, <c>FUN_14070e3b0</c>, <b>Verified</b>): those units are priced at the opposite direction's value.
    /// Only the table's own moves change it (<see cref="ToggleTraded"/>, <see cref="MoveTraded"/>), never a change of the stack's quantity, so it may be above the quantity, or below 0
    /// after a toggle of such a stack, as the original's. 0 once the trade closed.
    /// </summary>
    internal int Traded => tradedIn is { Active: true } ? traded : 0;
    /// <summary>A trade of this item, whole (<c>FUN_14070efb0</c>): no count, its quantity now is; a count, the quantity less it (none left: out of the table).</summary>
    internal void ToggleTraded(TradeSession session)
    {
        int before = Traded;
        (tradedIn, traded) = (session, before == 0 ? Quantity : Quantity - before);
    }
    /// <summary>
    /// Moves <paramref name="units"/> of <paramref name="from"/>'s count to <paramref name="to"/>, or all of the count when it is less (<c>FUN_14070f0d0</c>, <b>Verified</b>): what a split
    /// or a join of units does; nothing for an item out of the table.
    /// </summary>
    internal static void MoveTraded(ItemInstance from, ItemInstance to, int units)
    {
        if (from.Traded == 0) return;
        int n = Math.Min(from.traded, units);
        (to.tradedIn, to.traded) = (from.tradedIn, to.Traded + n);
        from.traded -= n;
    }
    /// <summary>A split-off part back onto this stack, its count with it (<c>FUN_14070f0d0</c>).</summary>
    internal void TakeBack(ItemInstance part)
    {
        MoveTraded(part, this, part.Quantity);
        Quantity += part.Quantity;
    }
    public int Level { get; set; }
    /// <summary>The name of the smith who crafted the item at a bench (the save key <c>crafter</c>; empty: not crafted by the player's smith). The tooltip prints it with the player faction's name.</summary>
    public string Crafter { get; set; } = "";
    /// <summary>The faction whose uniform the piece is (the save key <c>uniform</c>, the faction record id; empty: none): armour with one sells and buys at 0.3 of its price (<c>Armour+0x1e8</c>, <c>FUN_1408a9c20</c>, <b>Verified</b>).</summary>
    public string Uniform { get; set; } = "";
    /// <summary>The COLOR_DATA it is dyed with (the save key <c>color sid</c>; empty: none): its record's <c>color</c> when made, else the scheme of the first wearer (<see cref="Meitou.Data.Gameplay.ItemColours"/>).</summary>
    public string ColourId { get; set; } = "";
    public int ItemFunction { get; set; }
    /// <summary>Quality 0 to 100 (clothing from the character's armour grade).</summary>
    public float Quality { get; set; }
    /// <summary>Charges left; for food the record's charges times CONSTANTS <c>food quality mult</c>, which is what is eaten (100 points are one hunger level).</summary>
    public float Charges { get; set; } = 1;
    /// <summary>The charges of a full unit (the record's, as <see cref="Charges"/> starts); a stack of ammunition refills <see cref="Charges"/> from it when one unit is used up. 0 when not known (an item made elsewhere).</summary>
    public float MaxCharges { get; set; }
    /// <summary>A prosthetic's condition while it is not fitted, the save's <c>dam</c>, <c>wear</c> and <c>stun</c> (<see cref="Bodies.MedicalState.FitReplacement"/>); 0 for other items.</summary>
    public float LimbDamage { get; set; }
    public float LimbWear { get; set; }
    public float LimbStun { get; set; }
    public int Width { get; init; } = 1;
    public int Height { get; init; } = 1;
    /// <summary>Weight of one unit in kg as it counts in the inventory total (weapons already scaled by <c>weapon inventory weight mult</c>).</summary>
    public float UnitWeight { get => DynamicWeight is null ? baseWeight : WorkedWeight(); init => baseWeight = value; }
    float baseWeight;
    /// <summary><see cref="DynamicWeight"/>'s answer for the inputs it was asked with (the inventory total reads it every tick); one object, so a reader on another thread sees a whole one.</summary>
    sealed record WeightMemo(int Level, string Material, string Company, float Weight);
    WeightMemo? weightMemo;
    float WorkedWeight()
    {
        if (weightMemo is { } m && m.Level == Level && m.Material == MaterialId && m.Company == CompanyId) return m.Weight;
        float weight = DynamicWeight!(this);
        weightMemo = new(Level, MaterialId, CompanyId, weight);
        return weight;
    }
    /// <summary>The record weight (<see cref="UnitWeight"/> when nothing replaces it).</summary>
    public float RecordWeight => baseWeight;
    /// <summary>
    /// What replaces <see cref="RecordWeight"/> for items whose weight is worked out from the item: an armour piece's by its level and part coverage
    /// (<see cref="Meitou.Data.Gameplay.Combat.ArmourData.WeightAt"/>), a melee weapon's by its level, manufacturer and material (<c>WeaponStats</c>); null for anything else.
    /// </summary>
    public Func<ItemInstance, float>? DynamicWeight { get; init; }
    /// <summary>How many units one cell holds (<see cref="ItemInfo.StackLimit"/>; 1: it does not stack). Inside a backpack the bag's bonus applies (<see cref="Inventory.LimitOf"/>).</summary>
    public int StackLimit { get; init; } = 1;
    public bool IsFood { get; init; }
    /// <summary>
    /// The faction the item was stolen from (-1: none), the faction of the item's "taken from" owner (docs/game/carry-loot.md 4.4): set when the player's looting is a theft
    /// (<see cref="Carrying.CarrySystem"/>). The item counts as stolen while its holder is of another faction (<see cref="IsStolenFor"/>). Setting it alone leaves no owner
    /// character (a container's or the ground's mark); <see cref="Mark"/> sets both.
    /// </summary>
    public int StolenFrom { get => Mark.Faction; set => Mark = StolenMark.OfFaction(value); }
    /// <summary>
    /// The whole stolen mark: <see cref="StolenFrom"/> with the robbed character and its squad (the original's owner handle <c>ownedby</c>, item +0x140, whose container ids name the
    /// character's platoon). The fence rules test the owner's squad (docs/game/economy.md "Fencing, stealing and smuggling").
    /// </summary>
    public StolenMark Mark { get; set; } = StolenMark.None;
    /// <summary>The character the item was stolen from (<see cref="CharacterId.None"/>: none known).</summary>
    public CharacterId Owner => Mark.Owner;
    /// <summary>The squad <see cref="Owner"/> was in when robbed (-1: none known).</summary>
    public int OwnerSquad => Mark.Squad;
    /// <summary>The <c>ownedby</c> of <see cref="Saved"/> became <see cref="Owner"/> at load, so a save writes it from the mark (the null handle once the mark is cleared). Not hashed.</summary>
    internal bool OwnerFromSave { get; set; }
    /// <summary>
    /// One of its owner's CHARACTER / ANIMAL_CHARACTER <c>death items</c> (the save's <c>death</c>, docs/game/animals.md "Death items"): a live owner's own food search skips it, taking
    /// it from a live owner that is down butchers the owner, and it stops being one when it leaves the owner's inventory. Not compared when stacks join (<c>canStackWith</c>), so ordinary units joining its stack become death items too.
    /// </summary>
    public bool Death { get; set; }
    /// <summary>
    /// The original's <c>Item::isUnique</c> (the save's <c>unique</c>), set on a town's artifacts (docs/game/economy.md "Artifacts"): a shop's fills and restocks leave it in place. Not compared when
    /// stacks join (<c>canStackWith</c>).
    /// </summary>
    public bool Unique { get; set; }
    /// <summary>A worn backpack's contents (null for other items).</summary>
    public Inventory? Contents { get => contents; init => contents = value; }
    Inventory? contents;
    /// <summary>The factor of the contents' weight (a bag's <c>encumbrance effect</c>).</summary>
    public float ContentsFactor { get; init; } = 1;
    /// <summary>The INVENTORY_ITEM_STATE a loaded item was read from; saving copies it so the keys the engine does not model survive (null for an item made in play). Not hashed.</summary>
    public FcsRecord? Saved { get; set; }

    /// <summary>
    /// A new item like this one (the original's <c>RootObjectFactory::copyItem</c>, <c>FUN_14057c030</c>, <b>Verified</b>): the same record, maker, material, level, uniform, quality,
    /// charges, owner and unique flag, nothing of this instance's own: no lock (shackles copy as plain armour), no prosthetic damage, no crafter, not a death item, no saved state, and a
    /// bag an empty contents grid of the same size. Its colour is the caller's (a new item takes its record's). A shop's stock item as the trade window shows it (<see cref="Trading.TradeSystem"/>).
    /// </summary>
    internal ItemInstance Clone()
    {
        var copy = (ItemInstance)MemberwiseClone();
        (copy.Lock, copy.IsLocked) = (null, false);
        (copy.LimbDamage, copy.LimbWear, copy.LimbStun) = (0, 0, 0);
        (copy.Crafter, copy.Death, copy.Saved, copy.OwnerFromSave, copy.tradedIn, copy.traded) = ("", false, null, false, null, 0);
        if (contents is { } bag) copy.contents = new Inventory(bag.Width, bag.Height) { StackBonusMinimum = bag.StackBonusMinimum, StackBonusMult = bag.StackBonusMult };
        return copy;
    }

    /// <summary>This item moving whole to another inventory, as the same item: every field, its lock included (the old instance is removed).</summary>
    internal ItemInstance Moved() => (ItemInstance)MemberwiseClone();

    /// <summary>A copy of this item with another quantity (a stack split off; never used for a backpack, whose contents are not copied). It is out of the buyback table: a split moves its count (<see cref="MoveTraded"/>).</summary>
    public ItemInstance WithQuantity(int quantity) => new()
    {
        Record = Record, Name = Name, Type = Type, EquipmentSection = EquipmentSection, IsLocked = IsLocked, WornOnlyBy = WornOnlyBy, WornNeverBy = WornNeverBy, MaterialId = MaterialId, CompanyId = CompanyId, Section = Section, X = X, Y = Y, Quantity = quantity, Level = Level, Crafter = Crafter, Uniform = Uniform, ColourId = ColourId,
        ItemFunction = ItemFunction, Quality = Quality, Charges = Charges, MaxCharges = MaxCharges, Width = Width, Height = Height, UnitWeight = baseWeight, DynamicWeight = DynamicWeight, StackLimit = StackLimit,
        IsFood = IsFood, ContentsFactor = ContentsFactor, Mark = Mark, Death = Death, Unique = Unique,
    };

    /// <summary>
    /// Whether the item is stolen while held by a character of <paramref name="holderFaction"/> (<c>FUN_14079e550</c>, <b>Verified</b>: the from-owner is set and of another faction, and the holder's faction is not
    /// lawless: <c>not real</c> or BANDIT). <paramref name="data"/> null: no lawless test.
    /// </summary>
    public bool IsStolenFor(int holderFaction, PopulationData? data) => StolenFrom >= 0 && StolenFrom != holderFaction && data?.IsLawless(holderFaction) != true;

    /// <summary>The weight this item and what is in it add to the load.</summary>
    public float Weight => UnitWeight * Quantity + (Contents?.ContentWeight() ?? 0) * ContentsFactor;

    /// <summary>
    /// How full the item (a stack's partly used unit) is, 0 to 1, for the inventory cell's charge bar, whose width is charges / maximum (<c>FUN_140711200</c>, <b>Observed</b>); null when the
    /// maximum is 1 or less (no bar: gear, plain items).
    /// </summary>
    public float? ChargeFraction => MaxCharges > 1 ? Math.Clamp(Charges / MaxCharges, 0f, 1f) : null;

    /// <summary>The hunger levels eating the item gives: <see cref="Charges"/> / 100.</summary>
    public float Nutrition => IsFood ? Charges / 100f : 0;
}

/// <summary>
/// An item's stolen mark (<see cref="ItemInstance.Mark"/>): the faction it was stolen from (-1: not stolen), and when known the robbed character and its squad (the save's <c>ownedby</c>
/// handle). Stacks of differing marks join only where neither is stolen (<see cref="InventoryCommands.SameKind"/>).
/// </summary>
public readonly record struct StolenMark(int Faction, CharacterId Owner, int Squad)
{
    public static readonly StolenMark None = new(-1, CharacterId.None, -1);
    /// <summary>A mark naming only a faction (-1: none).</summary>
    public static StolenMark OfFaction(int faction) => new(faction, CharacterId.None, -1);
    /// <summary>The mark of an item taken from <paramref name="victim"/> (id <paramref name="id"/>): its faction, itself and its squad.</summary>
    public static StolenMark Of(CharacterCold victim, CharacterId id) => new(victim.Faction, id, victim.SquadId);
}
