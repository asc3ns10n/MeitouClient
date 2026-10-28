using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay;
using Meitou.Data.Gameplay.Economy;
using Meitou.Data.World;
using Meitou.Simulation.Items;

namespace Meitou.Simulation.Trading;

/// <summary>
/// The building filler <c>FUN_14095b210</c> (docs/game/economy.md "Building shops", <b>Verified</b>): stocks the storage containers of a building from its squad template's vendor lists.
/// Engine choices: containers of equal lock level keep the layout's order, items of equal value the roll's, and the draws come from the world's seeded stream.
/// </summary>
static class BuildingFill
{
    /// <summary>A stock container: its fixture and grid, lock level, <c>itemtype limit</c> and <c>limit inventory</c>, and its score for the item being placed.</summary>
    sealed class Box(Locks.Fixture fixture, Inventory grid, int lockLevel, FcsRecordType typeLimit, HashSet<string> allowed, float stackBonusMult)
    {
        public Locks.Fixture Fixture { get; } = fixture;
        public Inventory Grid { get; } = grid;
        public int LockLevel { get; } = lockLevel;
        public FcsRecordType TypeLimit { get; } = typeLimit;
        public HashSet<string> Allowed { get; } = allowed;
        public float StackBonusMult { get; } = stackBonusMult;
        public float Score { get; set; }
    }

    /// <summary>
    /// Fills <paramref name="building"/>'s containers with <paramref name="units"/> (and <paramref name="specials"/> first) and returns the value of what went in; null when no container
    /// takes what <paramref name="template"/>'s lists sell (the containers are then only unlocked) or there is no stock (<paramref name="anyStock"/> false: the specials go in and nothing is
    /// relocked). <paramref name="value"/> is an item's <c>FUN_1407a8f90</c>.
    /// </summary>
    public static long? Fill(World world, GameDatabase db, TownBuilding building, SquadTemplate template, GameRecord? faction, IReadOnlySet<string>? forbidden,
        List<ItemInstance> specials, List<ItemInstance> units, bool anyStock, Func<ItemInstance, int> value, Func<ItemInstance, long> valueAll, ref RandomStream rng)
    {
        var boxes = new List<Box>();
        foreach (var f in building.Fixtures)
        {
            if (!f.HoldsShopStock || world.Fixtures.Find(f.Id) is not { Contents: { } grid } box) continue;
            if (box.Lock is { } l)
            {
                l.Locked = false;
                world.Fixtures.Touch(box);
            }
            var record = db.Find(f.RecordId);
            var allowed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var r in record?.GetReferences("limit inventory") ?? [])
                if (db.Find(r.TargetStringId) is { Type: FcsRecordType.ITEM }) allowed.Add(r.TargetStringId);
            boxes.Add(new Box(box, grid, box.Lock?.Level ?? 0, (FcsRecordType)(record?.GetInt("itemtype limit") ?? 0), allowed, record?.GetFloat("stackable bonus mult") ?? 0));
        }
        Keep(db, boxes, template, faction, forbidden);
        if (boxes.Count == 0) return null;
        boxes = [.. boxes.OrderByDescending(b => b.LockLevel)];
        foreach (var b in boxes) b.Grid.Items.RemoveAll(i => !i.Unique);

        foreach (var item in specials)
        {
            Shuffle(boxes, ref rng);
            foreach (var b in boxes)
                if (Put(db, b, item)) break;
        }
        if (!anyStock) return null;
        long total = 0;
        foreach (var item in Place(db, boxes, units, value, ref rng)) total += valueAll(item);
        foreach (var b in boxes)
            if (b.Grid.Items.Count > 0 && b.Fixture.Lock is { } l)
            {
                l.Locked = true;
                world.Fixtures.Touch(b.Fixture);
            }
        return total;
    }

    /// <summary>
    /// Drops the containers that cannot hold what the lists sell (<c>FUN_14095a5b0</c>): a container stays when its <c>itemtype limit</c> is ITEM or a kind the lists sell, and its
    /// <c>limit inventory</c> is empty or names something they sell or a money item (<c>FUN_140165c10</c>: the ITEMs of <c>item function</c> 14 with a <c>value</c>).
    /// </summary>
    static void Keep(GameDatabase db, List<Box> boxes, SquadTemplate template, GameRecord? faction, IReadOnlySet<string>? forbidden)
    {
        if (boxes.Count == 0) return;
        var types = new HashSet<FcsRecordType>();
        var items = new HashSet<string>(StringComparer.Ordinal);
        foreach (var link in ShopFiller.VendorLinks(template, faction))
            if (db.Find(link.Id) is { Type: FcsRecordType.VENDOR_LIST } list) VendorList.Sold(db, list, forbidden, types, items);
        foreach (var r in db.OfType(FcsRecordType.ITEM))
            if (r.GetInt("item function") == PlayerSystem.MoneyFunction && r.GetInt("value") > 0) items.Add(r.StringId);
        boxes.RemoveAll(b => b.TypeLimit != FcsRecordType.ITEM && !types.Contains(b.TypeLimit) || b.Allowed.Count > 0 && !b.Allowed.Overlaps(items));
    }

    /// <summary>
    /// Puts the items into the containers (<c>FUN_1409565e0</c>): by value, dearest first, the first half (at least one) each into the container of best score among those under half
    /// full, the rest each into the first container of a fresh shuffle that takes it. Returns the items that went in whole as items of their own; the rest are lost.
    /// </summary>
    static List<ItemInstance> Place(GameDatabase db, List<Box> boxes, List<ItemInstance> items, Func<ItemInstance, int> value, ref RandomStream rng)
    {
        var placed = new List<ItemInstance>();
        var sorted = items.Select(i => (Value: value(i), Item: i)).OrderByDescending(x => x.Value).ToList();
        var working = new List<Box>(boxes);
        int count = (int)(sorted.Count * 0.5f);
        int k = 0;
        for (; k < sorted.Count; k++)
        {
            var item = sorted[k].Item;
            working.RemoveAll(b => FillOf(b) >= 0.5f);
            foreach (var b in working) b.Score = (float)(Suit(b, item, sorted[k].Value) + rng.NextFloat() * 0.01);
            foreach (var b in working.OrderByDescending(b => b.Score))
                if (Put(db, b, item))
                {
                    if (item.Quantity > 0) placed.Add(item);   // one wholly stacked onto another is not in the container
                    break;
                }
            if (--count <= 0)
            {
                k++;
                break;
            }
        }
        for (; k < sorted.Count; k++)
        {
            var item = sorted[k].Item;
            Shuffle(boxes, ref rng);
            foreach (var b in boxes)
                if (Put(db, b, item))
                {
                    if (item.Quantity > 0) placed.Add(item);   // one wholly stacked onto another is not in the container
                    break;
                }
        }
        return placed;
    }

    /// <summary>
    /// How well a container suits an item (<c>FUN_140952df0</c>): 0 when it is locked at level L and the item (not money) is worth less than 10 L; else L + 1, x 1.5 when its
    /// <c>itemtype limit</c> is the item's kind (0 when it is another kind than ITEM), + 150 when its <c>limit inventory</c> names the item (0 when it names others), x 100 for money.
    /// </summary>
    static float Suit(Box b, ItemInstance item, int value)
    {
        bool money = item.ItemFunction == PlayerSystem.MoneyFunction;
        if (b.LockLevel > 0 && !money && value < 10 * b.LockLevel) return 0;
        float r = b.LockLevel + 1f;
        if (b.TypeLimit != FcsRecordType.ITEM) r = b.TypeLimit == item.Type ? r * 1.5f : 0;
        else if (b.Allowed.Count > 0) r = b.Allowed.Contains(item.Record) ? r + 150 : 0;
        if (money) r *= 100;
        return r;
    }

    /// <summary>
    /// How full a container is (<c>FUN_140746fe0</c>): with one <c>limit inventory</c> item and a <c>stackable bonus mult</c>, its first stack's units over that
    /// mult; else its occupied cells over all.
    /// </summary>
    static float FillOf(Box b)
    {
        var grid = b.Grid;
        if (grid.Items.Count == 0) return 0;
        if (b.Allowed.Count == 1 && b.StackBonusMult > 0) return grid.Items[0].Quantity / b.StackBonusMult;
        int occupied = 0;
        for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
                if (!grid.IsFree("main", x, y, 1, 1)) occupied++;
        return (float)occupied / (grid.Width * grid.Height);
    }

    /// <summary>
    /// Puts the item into the container (<c>FUN_140954480</c>) when the container takes it (<c>FUN_14074bc30</c>): only the items its <c>limit inventory</c>
    /// names; else with <c>itemtype limit</c> 3 only armour, with 2 only items of the weapon slot (<c>FUN_140554530</c>); true when it all went in.
    /// </summary>
    static bool Put(GameDatabase db, Box b, ItemInstance item)
    {
        if (b.Allowed.Count > 0 ? !b.Allowed.Contains(item.Record)
            : b.TypeLimit == FcsRecordType.ARMOUR && item.Type != FcsRecordType.ARMOUR || b.TypeLimit == FcsRecordType.WEAPON && SlotOf(db, item) != WeaponSlot) return false;
        return b.Grid.AddTo("main", item);
    }

    /// <summary>AttachSlot ATTACH_WEAPON and ATTACH_NONE.</summary>
    const int WeaponSlot = 0, NoSlot = 7;

    /// <summary>An item's attach slot: its record's <c>slot</c> (<c>FUN_14075faf0</c>), the weapon slot for a crossbow, none for a map.</summary>
    static int SlotOf(GameDatabase db, ItemInstance item) => item.Type switch
    {
        FcsRecordType.CROSSBOW => WeaponSlot,
        FcsRecordType.MAP_ITEM => NoSlot,
        _ => db.Find(item.Record)?.GetInt("slot") ?? 0,
    };

    /// <summary><c>std::random_shuffle</c> of the containers in place: each position from the second swaps with one drawn at or before it.</summary>
    static void Shuffle(List<Box> boxes, ref RandomStream rng)
    {
        for (int i = 1; i < boxes.Count; i++)
        {
            int j = rng.NextInt(i + 1);
            (boxes[i], boxes[j]) = (boxes[j], boxes[i]);
        }
    }
}
