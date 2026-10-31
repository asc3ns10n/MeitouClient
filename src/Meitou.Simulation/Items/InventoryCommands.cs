using Meitou.Data.Fcs;

namespace Meitou.Simulation.Items;

/// <summary>Snapshot identity prevents a drag started before eating/looting from moving a different item.</summary>
public sealed record InventoryItemToken(int Index, string Record, string Section, int X, int Y, int Quantity, float Quality);
/// <summary>
/// An inventory window's ARRANGE (<c>FUN_14070f740</c>, <b>Verified</b>): its inventory arranges each of its sections but the equipment ones (<see cref="InventoryCommands.Arrange"/>), so a
/// character window its main grid, the bag window (<see cref="Bag"/>) the worn bag's.
/// </summary>
public sealed record ArrangeInventory(CharacterId Character) : SimCommand
{
    public bool Bag { get; init; }
}

public static class InventoryCommands
{
    public static bool IsEquipmentSection(string section) => section is "back" or "hip" or "shirt" or "head" or "legs" or "boots" or "armour" or "belt" or "backpack_attach";

    /// <summary>
    /// An equipment section disabled for <paramref name="wearer"/> (<c>CharacterHuman::validateInventorySections</c> <c>FUN_1406218f0</c>, <b>Observed</b>; drawn red, it takes nothing):
    /// <c>head</c> for a race with <c>no hats</c>, <c>shirt</c> with <c>no shirts</c>, <c>boots</c> with <c>no shoes</c> or while neither leg is the original (both lost or replaced).
    /// </summary>
    public static bool SectionClosed(CharacterCold? wearer, string section) => wearer?.Race is { } race && section switch
    {
        "head" => race.NoHats,
        "shirt" => race.NoShirts,
        "boots" => race.NoShoes || wearer.Medical is { } m && m.LimbPart(2) is { Limb: not Bodies.LimbState.Original } && m.LimbPart(3) is { Limb: not Bodies.LimbState.Original },
        _ => false,
    };
    /// <summary>
    /// <c>validateInventorySections</c> (<c>FUN_1406218f0</c>, character vtable slot 130; <b>Verified</b> body): a section <see cref="SectionClosed"/> for the character empties itself. The
    /// first worn boots of a character whose race may wear shoes are dropped in front of it (slot 53, <c>FUN_1405ca740</c>); every other item of a closed section is destroyed
    /// (<c>FUN_140749c20</c> with its delete flag). The original runs it when it builds a human's inventory and when a leg is severed (<c>FUN_14064edc0</c>, parts 2 and 3); Meitou calls it
    /// for the second (<see cref="PopulationSystem"/>'s limb queue), as a built inventory never has a worn item in a closed section.
    /// </summary>
    public static void ValidateSections(World world, int slot, CharacterCold cold)
    {
        if (cold.Inventory is not { } inventory) return;
        foreach (string section in ClosableSections)
        {
            if (!SectionClosed(cold, section)) continue;
            if (section == "boots" && cold.Race is { NoShoes: false } && inventory.Items.Find(i => i.Section == section) is { } boots)
            {
                inventory.Items.Remove(boots);
                PlayerSystem.DropAhead(world, slot, boots);
            }
            inventory.Items.RemoveAll(i => i.Section == section);
        }
    }

    static readonly string[] ClosableSections = ["shirt", "head", "boots"];

    public static bool IsMovableSection(string section) => section is "main" or "backpack_content" || IsEquipmentSection(section);
    /// <summary>A fitted prosthetic's section (<see cref="Meitou.Data.Gameplay.Bodies.LimbReplacement.SlotOf"/>): the limbs window's slots; not traded, not saved with the inventory.</summary>
    public static bool IsLimbSection(string section) => LimbSlotOf(section) >= 0;
    /// <summary>The limb slot (0 left arm, 1 right arm, 2 left leg, 3 right leg) of a prosthetic section; -1 for other sections.</summary>
    public static int LimbSlotOf(string section) => section switch { "left_arm_replace" => 0, "right_arm_replace" => 1, "left_leg_replace" => 2, "right_leg_replace" => 3, _ => -1 };

    // Engine slot capacities, in inventory cells. Clothing and backpack slots hold one item.
    public static (int Width, int Height) SectionSize(string section) => section switch
    {
        "back" => (10, 2), "hip" => (7, 1), "backpack_attach" => (5, 4),
        "shirt" or "boots" => (4, 2), "head" => (4, 3), "legs" => (4, 5),
        "armour" => (4, 6), "belt" => (2, 2), _ => (0, 0),
    };

    /// <summary>
    /// Puts one unit into the equipment slot that takes it when that is free (the trade window's right click, <c>FUN_140713d20</c>: sections that accept the item's slot type are tried before
    /// the general grids, in the character's section order, <c>CharacterHuman::setupInventorySections</c> <c>0x14062c4e0</c>, <b>Verified</b>): weapons the first free cell of <c>hip</c>
    /// (one item at most), then of <c>back</c> (two); armour its <see cref="ItemInstance.EquipmentSection"/> when nothing is worn there. Bags are not equipped here: the trade moves them
    /// whole, into a free <c>backpack_attach</c> first (engine choice, docs/simulation.md). False: no slot, the caller places it in a grid. Nothing is worn
    /// that the wearer (<paramref name="wearer"/>; null: no check) may not wear (its race: <see cref="ItemInstance.WearableBy"/>), nor in a section closed for it (<see cref="SectionClosed"/>).
    /// </summary>
    public static bool TryEquip(Inventory root, ItemInstance item, CharacterCold? wearer = null)
    {
        if (!Wears(wearer, item) || item.Quantity != 1 || item.Width <= 0 || item.Height <= 0) return false;
        string[] sections = item.Type is Meitou.Data.Fcs.FcsRecordType.WEAPON or Meitou.Data.Fcs.FcsRecordType.CROSSBOW ? ["hip", "back"]
            : item.Type is Meitou.Data.Fcs.FcsRecordType.ARMOUR or Meitou.Data.Fcs.FcsRecordType.ITEM && IsEquipmentSection(item.EquipmentSection) && item.EquipmentSection is not ("back" or "hip" or "backpack_attach") ? [item.EquipmentSection] : [];
        foreach (string section in sections)
        {
            if (SectionClosed(wearer, section)) continue;
            int x = 0, y = 0;
            if (section is "back" or "hip")
            {
                if (ItemsLimitReached(root, section)) continue;
                var size = SectionSize(section);
                var cell = Enumerable.Range(0, Math.Max(0, size.Height - item.Height + 1))
                    .SelectMany(cy => Enumerable.Range(0, Math.Max(0, size.Width - item.Width + 1)).Select(cx => (X: cx, Y: cy)))
                    .Cast<(int X, int Y)?>().FirstOrDefault(c => root.IsFree(section, c!.Value.X, c.Value.Y, item.Width, item.Height));
                if (cell is not { } free) continue;
                (x, y) = free;
            }
            else if (root.Items.Any(i => i.Section == section)) continue;
            item.Section = section;
            item.X = x;
            item.Y = y;
            root.Items.Add(item);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Whether a weapon slot holds as many items as it may (the section's items limit, <c>CharacterHuman::setupInventorySections</c>, <b>Verified</b>: <c>hip</c> 1, <c>back</c> 2; the other
    /// equipment slots hold one item whatever its footprint, <see cref="IsSingleSlot"/>).
    /// </summary>
    public static bool ItemsLimitReached(Inventory root, string section) => section switch
    {
        "hip" => root.Items.Count(i => i.Section == section) >= 1,
        "back" => root.Items.Count(i => i.Section == section) >= 2,
        _ => false,
    };

    /// <summary>
    /// Whether <paramref name="wearer"/> (null: no check) may put <paramref name="item"/> in an equipment section: <c>RaceLimiter::canEquip(item, who)</c> (<c>FUN_14075d720</c>, <b>Verified</b>),
    /// which passes whether it is an animal, so an item without a race list is for no animal (<see cref="ItemInstance.WearableBy"/>).
    /// </summary>
    static bool Wears(CharacterCold? wearer, ItemInstance item) => item.WearableBy(wearer?.Race?.StringId, wearer is { } w && SquadRegistry.IsAnimal(w));

    /// <summary>A character's sections of an item's slot type (<c>getAllSectionsOfType</c>, in the character's section order).</summary>
    public static string[] SlotSections(ItemInstance item) => item.Type is FcsRecordType.WEAPON or FcsRecordType.CROSSBOW ? ["hip", "back"]
        : item.Type is FcsRecordType.ARMOUR or FcsRecordType.ITEM && IsEquipmentSection(item.EquipmentSection) && item.EquipmentSection is not ("back" or "hip" or "backpack_attach") ? [item.EquipmentSection] : [];

    /// <summary>
    /// A character's auto-add (<c>Inventory::addItem</c> <c>0x140745c80</c>, <c>tryAddItem</c> <c>0x14074bd20</c>, <b>Verified</b>): the equipment sections that take it, in the character's
    /// section order (<see cref="TryEquip"/>, a bag the free <c>backpack_attach</c> when the wearer may wear it), then <c>main</c> (<see cref="Inventory.AddTo"/>); never the worn bag, which is an inventory of its own.
    /// </summary>
    public static bool TryAdd(Inventory root, ItemInstance item, CharacterCold? wearer)
    {
        if (TryEquip(root, item, wearer)) return true;
        if (item.Type == FcsRecordType.CONTAINER && item.Contents is not null && item.Quantity == 1 && Wears(wearer, item) && !root.Items.Exists(i => i.Section == "backpack_attach"))
        {
            (item.Section, item.X, item.Y) = ("backpack_attach", 0, 0);
            root.Items.Add(item);
            return true;
        }
        return root.AddTo("main", item);
    }

    public static bool Move(Inventory root, InventoryItemToken token, string section, int x, int y, CharacterCold? wearer = null) => Transfer(root, token, root, section, x, y, limbs: true, wearer);

    /// <summary>A right click's unit count for <paramref name="item"/>: money is never split (<c>FUN_140713d20</c> skips the one-unit split for item function 14), so 0, the whole stack, whatever Shift says.</summary>
    public static int ClickCount(ItemInstance item, int count) => item.ItemFunction == PlayerSystem.MoneyFunction ? 0 : count;

    /// <summary>
    /// Moves at most <paramref name="count"/> units (0: all) of <paramref name="item"/>, held directly by <paramref name="owner"/>, into <paramref name="into"/> where they first fit
    /// (<see cref="Inventory.Add"/>: stacks of it, then free cells); what does not fit stays where it was. Returns the units moved. A right click into a storage or container moves one unit this
    /// way, with Shift the stack (docs/game/ui-input.md, the input tables); into a character it is <see cref="MoveInto"/>.
    /// </summary>
    public static int MoveUnits(Inventory owner, ItemInstance item, int count, Inventory into)
    {
        if (count <= 0 || count >= item.Quantity)
        {
            int before = item.Quantity, at = owner.Items.IndexOf(item);
            if (at < 0) return 0;
            owner.Items.RemoveAt(at);
            bool death = item.Death;
            item.Death = false;   // leaving its owner, it is no longer a death item
            if (into.Add(item)) return before;
            item.Death = death;
            if (item.Quantity > 0) owner.Items.Insert(at, item);
            return before - item.Quantity;
        }
        var unit = item.WithQuantity(count);
        unit.Death = false;
        int moved = into.Add(unit) ? count : count - unit.Quantity;
        item.Quantity -= moved;
        return moved;
    }

    /// <summary>
    /// Moves the item <paramref name="token"/> names in <paramref name="from"/> to <paramref name="section"/> at (<paramref name="x"/>, <paramref name="y"/>) of <paramref name="into"/>: the same
    /// inventory (<see cref="Move"/>) or another character's (the loot window), by the same rules. Prosthetic sections only with <paramref name="limbs"/>. False, nothing moved: the token is
    /// stale or the place does not take the item (an equipment or prosthetic section also one whose race <paramref name="wearer"/> may not wear, or a section closed for it; null: no check).
    /// </summary>
    public static bool Transfer(Inventory from, InventoryItemToken token, Inventory into, string section, int x, int y, bool limbs = false, CharacterCold? wearer = null)
    {
        var item = from.All().ElementAtOrDefault(token.Index);
        bool Movable(string s) => IsMovableSection(s) || limbs && IsLimbSection(s);
        if (item is null || item.Record != token.Record || item.Section != token.Section || item.X != token.X || item.Y != token.Y || item.Quantity != token.Quantity || item.Quality != token.Quality ||
            !Movable(item.Section) || !Movable(section)) return false;
        if (item.LockedOn && (section != item.Section || into != from)) return false;   // worn locked shackles stay on
        var owner = from.Items.Contains(item) ? from : from.Backpack?.Contents;
        var target = section == "backpack_content" ? into.Backpack?.Contents : into;
        // A bag cannot contain itself or another container; moving it preserves all its contents.
        if (section == "backpack_content" && item.Contents is not null) return false;
        var size = section is "back" or "hip" ? SectionSize(section) : IsEquipmentSection(section) || IsLimbSection(section) ? (item.Width, item.Height) : (target?.Width ?? 0, target?.Height ?? 0);
        if (owner is null || target is null || !owner.Items.Contains(item) || x < 0 || y < 0 || item.Width <= 0 || item.Height <= 0 ||
            x > size.Item1 - item.Width || y > size.Item2 - item.Height || item.Quantity > target.LimitOf(item)) return false;
        if ((IsEquipmentSection(section) || IsLimbSection(section)) && !Wears(wearer, item)) return false;
        if (SectionClosed(wearer, section)) return false;
        if (IsEquipmentSection(section))
        {
            bool allowed = section switch
            {
                "back" or "hip" => item.Type is Meitou.Data.Fcs.FcsRecordType.WEAPON or Meitou.Data.Fcs.FcsRecordType.CROSSBOW,
                "backpack_attach" => item.Type == Meitou.Data.Fcs.FcsRecordType.CONTAINER && item.Contents is not null,
                _ => item.Type is Meitou.Data.Fcs.FcsRecordType.ARMOUR or Meitou.Data.Fcs.FcsRecordType.ITEM && item.EquipmentSection == section,
            };
            if (!allowed || item.Quantity != 1) return false;
            if (section is not ("back" or "hip") && (x != 0 || y != 0 || target.Items.Any(i => i != item && i.Section == section))) return false;
        }
        // A prosthetic slot holds one LIMB_REPLACEMENT; whether it fits the limb is the caller's check (PlayerState).
        if (IsLimbSection(section) && (item.Type != Meitou.Data.Fcs.FcsRecordType.LIMB_REPLACEMENT || item.Quantity != 1 || x != 0 || y != 0 || target.Items.Any(i => i != item && i.Section == section))) return false;
        foreach (var other in target.Items)
            if (other != item && other.Section == section && x < other.X + other.Width && other.X < x + item.Width && y < other.Y + other.Height && other.Y < y + item.Height) return false;
        if (owner != target) { owner.Items.Remove(item); target.Items.Add(item); }
        if (into != from) item.Death = false;
        item.Section = section; item.X = x; item.Y = y; return true;
    }
    /// <summary>
    /// ARRANGE of one section (<c>InventorySection::autoArrange</c>, <c>FUN_140749d30</c>, <b>Verified</b>): its items in position order (by x, then y), sorted by the original's sort
    /// (<see cref="ArrangeSort"/>) largest first on a key (<c>FUN_1407468a0</c>: the longer side of the footprint, on a second pass the area, on a third the width), each added again by
    /// <paramref name="add"/> (the section's auto-add, <see cref="Inventory.AddTo"/>); the first pass that adds them all is kept. After a pass that does not, the items it joined away
    /// are dropped and the pieces it split off lost, as the original's; after the third the items left go back to their old cells with what they hold then.
    /// </summary>
    public static void Arrange(Inventory grid, string section, Func<ItemInstance, bool>? add = null)
    {
        add ??= item => grid.AddTo(section, item);
        var original = grid.Items.Where(i => i.Section == section).OrderBy(i => i.X).ThenBy(i => i.Y).Select(i => (Item: i, i.X, i.Y)).ToList();
        Func<(ItemInstance Item, int X, int Y), (ItemInstance Item, int X, int Y), bool>[] keys =
        [
            (a, b) => Math.Max(a.Item.Width, a.Item.Height) > Math.Max(b.Item.Width, b.Item.Height),
            (a, b) => a.Item.Width * a.Item.Height > b.Item.Width * b.Item.Height,
            (a, b) => a.Item.Width > b.Item.Width,
        ];
        for (int mode = 0; mode < keys.Length; mode++)
        {
            grid.Items.RemoveAll(i => i.Section == section);
            var sorted = original.ToArray();
            ArrangeSort.Sort(sorted, keys[mode]);
            bool fits = true;
            foreach (var entry in sorted)
                if (!add(entry.Item)) fits = false;
            if (fits) return;
            if (mode < keys.Length - 1) original.RemoveAll(e => e.Item.Quantity == 0);
        }
        grid.Items.RemoveAll(i => i.Section == section);
        foreach (var (item, x, y) in original)
        {
            if (item.Quantity <= 0) continue;
            (item.Section, item.X, item.Y) = (section, x, y);
            grid.Items.Add(item);
        }
    }

    /// <summary>
    /// The types that never stack (<c>FUN_14075e090</c>): WEAPON, ARMOUR, CONTAINER, MAP_ITEM, CROSSBOW, LIMB_REPLACEMENT; the others stack up to their record's <c>stackable</c>.
    /// </summary>
    public static bool NeverStacks(FcsRecordType type) =>
        type is FcsRecordType.WEAPON or FcsRecordType.ARMOUR or FcsRecordType.CONTAINER or FcsRecordType.MAP_ITEM or FcsRecordType.CROSSBOW or FcsRecordType.LIMB_REPLACEMENT;

    /// <summary>
    /// Whether two items are of a kind that stacks together (<c>canStackWith</c>, <c>FUN_14075d470</c>, <b>Verified</b>): the same record, maker and material (<c>isSameAs</c>,
    /// <c>FUN_14075d510</c>), and when either is stolen the same owner (<see cref="ItemInstance.Mark"/>); quality, level, a death item's or an artifact's flag are not compared. How many a
    /// cell holds is the section's (<see cref="Inventory.LimitOf"/>). <paramref name="held"/>, one of the two, lies in an inventory held by <paramref name="holder"/>; the other is held by
    /// nobody (being added, or on the mouse), so it is stolen whenever it has a mark (<see cref="Stolen"/>).
    /// </summary>
    public static bool SameKind(ItemInstance a, ItemInstance b, InventoryHolder? holder = null, ItemInstance? held = null) =>
        a.Record == b.Record && a.CompanyId == b.CompanyId && a.MaterialId == b.MaterialId
        && (!Stolen(a, ReferenceEquals(a, held) ? holder : null) && !Stolen(b, ReferenceEquals(b, held) ? holder : null) || a.Mark == b.Mark);

    /// <summary>
    /// Whether a marked item counts as stolen where it lies (<c>InventoryItemBase::isStolen(true)</c>, <c>FUN_14079e550</c>, <b>Verified</b>): not when its holder is of the owner's
    /// faction or lawless (a bandit or not-real faction), nor when the owner is of the holder's squad (the handles' <c>container</c>, the platoon); held by nobody, it is.
    /// </summary>
    public static bool Stolen(ItemInstance item, InventoryHolder? holder) => item.Mark != StolenMark.None
        && !(holder is { } h && (h.Faction >= 0 && (h.Faction == item.Mark.Faction || h.Lawless) || h.Squad >= 0 && h.Squad == item.Mark.Squad));

    /// <summary>
    /// <paramref name="amount"/> units of <paramref name="from"/> join <paramref name="into"/> (<c>addQuantity</c>, <c>FUN_14075e220</c>, <b>Verified</b>), nothing unless they are of a kind
    /// (<see cref="SameKind"/>). Each is full units and one partly used one: the two partly used ones' charges add, and past a full unit they make one unit more with the excess as the
    /// partly used one, else one unit. Past <paramref name="limit"/>, <paramref name="into"/> is cut to it at a full charge and the units over, with the partly used charge, are
    /// <paramref name="from"/>'s. Returns the units left in <paramref name="from"/> (0: all joined). The caller moves the traded count (<see cref="ItemInstance.MoveTraded"/>).
    /// </summary>
    public static int Join(ItemInstance into, ItemInstance from, int amount, int limit, InventoryHolder? holder = null, ItemInstance? held = null)
    {
        if (!SameKind(into, from, holder, held)) return amount;
        float full = Full(into), charge = Charge(into) + Charge(from);
        int quantity = into.Quantity + amount - 1, left = 0;
        if (charge > full) { quantity++; charge -= full; }
        SetCharge(into, charge);
        SetCharge(from, 0);
        if (quantity > limit)
        {
            SetCharge(from, charge);
            SetCharge(into, full);
            left = quantity - limit;
            quantity = limit;
        }
        into.Quantity = quantity;
        if (!ReferenceEquals(into, from)) from.Quantity = left;
        return left;
    }

    // An item without charges counts as one full charge of 1.
    static float Full(ItemInstance i) => i.MaxCharges > 0 ? i.MaxCharges : 1;
    static float Charge(ItemInstance i) => i.MaxCharges > 0 ? i.Charges : 1;
    static void SetCharge(ItemInstance i, float charge) { if (i.MaxCharges > 0) i.Charges = charge; }

    /// <summary>
    /// Whether a section takes an item, the section half of <c>FUN_14074bc30</c>: the grids (<c>main</c>, <c>backpack_content</c>) anything, but a bag only empty into a bag; a weapon slot
    /// (<c>back</c>, <c>hip</c>) a WEAPON or CROSSBOW; <c>backpack_attach</c> a CONTAINER; a prosthetic slot a LIMB_REPLACEMENT; a clothing slot the ARMOUR or belt ITEM of that slot.
    /// Before the slot type, a section other than the grids checks the wearer's race (<paramref name="wearable"/> false: "Can't wear that item."; <c>RaceLimiter</c>). Null: taken; otherwise the refusal ("" silent).
    /// </summary>
    public static string? SlotRefusal(string section, FcsRecordType type, string equipmentSection, Inventory? contents, bool wearable = true) =>
        SlotRefusal(section, type, equipmentSection, contents?.Items.Count ?? -1, wearable);

    /// <summary>As <see cref="SlotRefusal(string, FcsRecordType, string, Inventory?, bool)"/> with the number of items in a bag (-1: the item is not a bag).</summary>
    public static string? SlotRefusal(string section, FcsRecordType type, string equipmentSection, int bagItems, bool wearable = true)
    {
        if (section == "backpack_content") return bagItems > 0 ? "Container cannot be placed here unless it is empty." : null;
        if (section == "main") return null;
        if (!wearable) return "Can't wear that item.";
        bool takes = section switch
        {
            "back" or "hip" => type is FcsRecordType.WEAPON or FcsRecordType.CROSSBOW,
            "backpack_attach" => type == FcsRecordType.CONTAINER && bagItems >= 0,
            _ when IsLimbSection(section) => type == FcsRecordType.LIMB_REPLACEMENT,
            _ when IsEquipmentSection(section) => type is FcsRecordType.ARMOUR or FcsRecordType.ITEM && equipmentSection == section,
            _ => false,
        };
        return takes ? null : "Invalid container type.";
    }

    /// <summary>A section holding one item whatever its footprint (clothing, the bag slot, the prosthetic slots); the others are grids.</summary>
    public static bool IsSingleSlot(string section) => IsLimbSection(section) || IsEquipmentSection(section) && section is not ("back" or "hip");

    /// <summary>
    /// The right click's move into another character (<c>FUN_140713d20</c>, its target <c>FUN_14070dcd0</c>): one unit at a time, at most <paramref name="count"/> (0: until one fails), each
    /// into the target's open bag window first (<paramref name="bagFirst"/>), then its character window: the slots of the item's type (<see cref="TryEquip"/>), then the main grid, stacks of it
    /// first (<see cref="Inventory.AddTo"/>). Leaving its owner a unit is no longer a death item. Returns the units moved; <paramref name="equipped"/>: one was worn.
    /// <paramref name="bagOnly"/>: only into the target's worn bag, nowhere else.
    /// </summary>
    public static int MoveInto(Inventory owner, ItemInstance item, int count, Inventory into, bool bagFirst, out bool equipped, CharacterCold? wearer = null, bool bagOnly = false)
    {
        equipped = false;
        int want = count <= 0 ? item.Quantity : Math.Min(count, item.Quantity), moved = 0;
        while (moved < want)
        {
            bool last = item.Quantity <= 1;
            int at = owner.Items.IndexOf(item);
            if (at < 0) break;
            var unit = last ? item : item.WithQuantity(1);
            bool death = unit.Death;
            if (last) owner.Items.RemoveAt(at);
            unit.Death = false;
            bool worn = false;
            bool placed = bagOnly ? into.Backpack?.Contents is { } only && only.AddTo("backpack_content", unit, into.Holder)
                : bagFirst && into.Backpack?.Contents is { } bag && bag.AddTo("backpack_content", unit, into.Holder) || (worn = TryEquip(into, unit, wearer)) || into.AddTo("main", unit);
            if (!placed)
            {
                unit.Death = death;
                if (last) owner.Items.Insert(at, item);
                break;
            }
            equipped |= worn;
            if (!last) item.Quantity--;
            moved++;
        }
        return moved;
    }
}
