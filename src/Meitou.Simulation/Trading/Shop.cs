using Meitou.Data;
using Meitou.Data.Characters;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay;
using Meitou.Data.Gameplay.Economy;
using Meitou.Simulation.Items;

namespace Meitou.Simulation.Trading;

/// <summary>
/// A trader squad's shop: the stock it sells, its trade terms and when it restocks (docs/game/economy.md section 9). The purse is the squad's <see cref="Squad.Money"/>, the game's
/// <c>Platoon+0x88</c>. <b>Engine choice</b>: the stock is one inventory of <see cref="Width"/> x <see cref="Height"/> cells held by the squad, the size of the
/// building shops' storage (<c>storage size</c> 18 x 18 in <c>gamedata.base</c>, the width of the vanilla trader window): the trader window shows its trader's <c>backpack_content</c> section (<c>FUN_140150570</c>,
/// docs/game/ui-screens.md section 6). In the original a squad shop's stock goes into the first section of each member's worn backpack, pack animals' first (<c>FUN_14095ab10</c>), and a building shop's lies in the shop building's containers (here
/// a rectangle of the stock per container, <see cref="ForBuilding"/>).
/// </summary>
public sealed class Shop
{
    public const int Width = 18, Height = 18;
    /// <summary>The stock section's stack bonus (<c>ShopTraderInventory::vfunc_1</c> sets the section's minimum 1 and mult 50, <b>Observed</b> decompile): a cell holds 50 x the item's <c>stackable</c>.</summary>
    public const float StockStackMult = 50;

    public Inventory Stock { get; init; } = new(Width, Height) { StackBonusMult = StockStackMult };
    public required Trader Terms { get; init; }
    /// <summary>A building shop's building (<see cref="Meitou.Data.World.TownBuilding.Key"/>): its stock is the merged storage of the building's containers; null for a squad shop.</summary>
    public string? Building { get; init; }
    /// <summary>The world tick at which the stock is next replaced (<c>Platoon+0x1f8</c>, saved as <c>inventory refresh time</c> in game hours).</summary>
    public long NextRestockTick { get; set; }
    /// <summary>How many times the stock was filled: the key of the next fill's rolls.</summary>
    public int Fills { get; set; }

    List<GridRegion>? unused;

    /// <summary>The cells of <see cref="Stock"/> outside every container (<see cref="Unused"/>, made once: the layout never changes).</summary>
    public IReadOnlyList<GridRegion> UnusedCells => unused ??= Unused(Stock);

    /// <summary>The stock containers of a building shop (fixture ids), in the order of <see cref="Inventory.Regions"/> of <see cref="Stock"/>; empty for a squad shop.</summary>
    public IReadOnlyList<string> Containers { get; init; } = [];

    /// <summary>
    /// A building shop of <paramref name="building"/> (the shop wrapper <c>FUN_140953850</c>, <b>Observed</b>, merges its stock containers into one section): null when the building has no
    /// stock container. <b>Engine choice</b>: the stock is one inventory as wide as the widest container (at least 18) in which each container's grid is a rectangle of its own
    /// (<see cref="Inventory.Regions"/>), so each container holds its own items and the trade window shows them all (the cells outside every container greyed, <see cref="Unused"/>). The
    /// rectangles are packed (<see cref="Pack"/>) in a few orders (by height, area, width, longer side, largest first; ties in the building's order) and the lowest packing is kept. The
    /// original's merged section is ceil(cells / width) high; this is up to ~5 % more when the containers do not tile (Squin's shops: 3 to 13 rows, and 300 random orders found none lower).
    /// </summary>
    public static Shop? ForBuilding(Meitou.Data.World.TownBuilding building, Trader terms)
    {
        var boxes = building.Fixtures.Where(f => f.HoldsShopStock).ToList();
        if (boxes.Count == 0) return null;
        int width = Math.Max(Width, boxes.Max(f => f.StorageWidth));
        Func<Meitou.Data.World.TownFixture, int>[] orders =
            [f => f.StorageHeight, f => f.StorageWidth * f.StorageHeight, f => f.StorageWidth, f => Math.Max(f.StorageWidth, f.StorageHeight)];
        (List<Meitou.Data.World.TownFixture> Boxes, List<GridRegion> Regions, int Height)? best = null;
        foreach (var order in orders)
        {
            var sorted = boxes.OrderByDescending(order).ToList();
            var (regions, height) = Pack(sorted, width);
            if (best is null || height < best.Value.Height) best = (sorted, regions, height);
        }
        var (placed, layout, rows) = best!.Value;
        return new Shop
        {
            Terms = terms, Building = building.Key, Containers = [.. placed.Select(f => f.Id)],
            Stock = new Inventory(width, rows) { StackBonusMult = StockStackMult, Regions = layout },
        };
    }

    /// <summary>
    /// Bottom-left maximal-rectangles packing: the free space is kept as every largest free rectangle (overlapping), each box goes where its bottom is highest up (lowest y + height), then
    /// leftmost, so it also fills holes left beside and under taller boxes.
    /// </summary>
    static (List<GridRegion> Regions, int Height) Pack(List<Meitou.Data.World.TownFixture> boxes, int width)
    {
        var free = new List<GridRegion> { new(0, 0, width, boxes.Sum(f => f.StorageHeight)) };
        var regions = new List<GridRegion>(boxes.Count);
        int height = 0;
        foreach (var f in boxes)
        {
            GridRegion? at = null;
            foreach (var r in free)
                if (f.StorageWidth <= r.Width && f.StorageHeight <= r.Height
                    && (at is not { } a || r.Y + f.StorageHeight < a.Y + a.Height || r.Y + f.StorageHeight == a.Y + a.Height && r.X < a.X))
                    at = new GridRegion(r.X, r.Y, f.StorageWidth, f.StorageHeight);
            var p = at!.Value;   // the free space is as tall as all the boxes stacked, so every box fits
            regions.Add(p);
            height = Math.Max(height, p.Y + p.Height);
            var split = new List<GridRegion>();
            foreach (var r in free)
            {
                if (p.X >= r.X + r.Width || p.X + p.Width <= r.X || p.Y >= r.Y + r.Height || p.Y + p.Height <= r.Y) { split.Add(r); continue; }
                if (p.X > r.X) split.Add(r with { Width = p.X - r.X });
                if (p.X + p.Width < r.X + r.Width) split.Add(r with { X = p.X + p.Width, Width = r.X + r.Width - p.X - p.Width });
                if (p.Y > r.Y) split.Add(r with { Height = p.Y - r.Y });
                if (p.Y + p.Height < r.Y + r.Height) split.Add(r with { Y = p.Y + p.Height, Height = r.Y + r.Height - p.Y - p.Height });
            }
            free = [.. split.Where((r, i) => !split.Where((o, j) => j != i && o.Contains(r.X, r.Y, r.Width, r.Height) && (o != r || j < i)).Any())];
        }
        return (regions, height);
    }

    /// <summary>The cells of <paramref name="grid"/> outside its <see cref="Inventory.Regions"/> (none without them), as rectangles: runs of a row, joined down while they repeat.</summary>
    public static List<GridRegion> Unused(Inventory grid)
    {
        var result = new List<GridRegion>();
        if (grid.Regions is null) return result;
        var open = new Dictionary<(int X, int Width), int>();   // a run of the previous row -> the index of its rectangle
        for (int y = 0; y < grid.Height; y++)
        {
            var next = new Dictionary<(int X, int Width), int>();
            for (int x = 0; x < grid.Width;)
            {
                if (grid.InRegion(x, y, 1, 1)) { x++; continue; }
                int start = x;
                while (x < grid.Width && !grid.InRegion(x, y, 1, 1)) x++;
                var run = (start, x - start);
                if (open.TryGetValue(run, out int k)) result[k] = result[k] with { Height = result[k].Height + 1 };
                else { k = result.Count; result.Add(new GridRegion(start, y, x - start, 1)); }
                next[run] = k;
            }
            open = next;
        }
        return result;
    }

    /// <summary>The rectangle of <see cref="Stock"/> that is container <paramref name="fixture"/>'s grid; false when it is not one of this shop's stock containers.</summary>
    public bool RegionOf(string fixture, out GridRegion region)
    {
        for (int k = 0; k < Containers.Count; k++)
            if (Containers[k] == fixture)
            {
                region = Stock.Regions![k];
                return true;
            }
        region = default;
        return false;
    }

    internal void Hash(ref StateHasher h)
    {
        h.Add(NextRestockTick);
        h.Add(Fills);
        Stock.Hash(ref h);
    }
}

/// <summary>
/// Fills a squad shop as the squad filler <c>FUN_14095ab10</c> does, and rolls the stock of the building filler (<see cref="BuildingFill"/>; docs/game/economy.md
/// "Building shops", <b>Verified</b>):
/// <list type="number">
/// <item>n = round(U(0.8, 1.2) x <c>vendors fill total amount</c> x wealth), half away from zero; wealth is 1 (the game's faction wealth function is a stub returning 1). The building filler
/// makes at least one; a squad's n of 0, or no unit made, leaves its stock cleared and its purse as it was.</item>
/// <item>Each list's money roll (<c>money item prob</c> against U(0, 1), so 1 or more always pays) adds U[min, max] cats; the lists then share n by their weights (v0, 0 getting none),
/// round(n x w / sum) each, the last also taking a positive remainder (<c>FUN_140959800</c>, <c>FUN_140959a90</c>).</item>
/// <item>Each unit is a weighted pick from its list (<see cref="VendorList"/>, <c>FUN_1409555d0</c>): its <c>items</c> but those the faction's culture forbids, its weapons that a
/// manufacturer of the list makes (<see cref="VendorList.From"/>); a blueprint or map is drawn once. Armour gets a grade from the faction's armour chances and the grade's quality
/// (5 / 20 / 40 / 60 / 80 / 95); a weapon a manufacturer from the list and the item factory's model and level from it (<see cref="Maker"/>); a
/// robotic limb a level from <see cref="LimbGrade"/>, a crossbow from <see cref="CrossbowGrade"/> (no manufacturer). A unit joins an earlier one of the same record with room, a weapon never.</item>
/// <item>A squad shop's units go into the stock dearest first; what does not fit is destroyed. Its purse is trunc(clamp(0.25 x the value of what went in, 3000, 25000) x wealth).</item>
/// <item>The original makes the cats into money items among the stock (<c>FUN_1401641d0</c>), which its trade window hides and its purse counts. A squad shop here
/// counts them into the purse value without stocking them (<b>engine choice</b>: Meitou's stock is shown whole, so a shop would sell its own cats).</item>
/// </list>
/// </summary>
public static class ShopFiller
{
    /// <summary>The armour grade chances when a faction has none (the editor's defaults, economy.md 9.2).</summary>
    static readonly int[] DefaultGradeChances = [50, 100, 50, 2, 0, 0];

    /// <summary>
    /// The vendor lists a squad's stock rolls from (<c>FUN_140958560</c>, <b>Verified</b>): the template's <c>vendors</c>; when it has none, by its building designation the owner FACTION's
    /// 2 BARRACKS / 5 ARMOURY <c>item spawns armoury</c>, 3 BAR <c>item spawns bar</c>, 6 TREASURE <c>item spawns treasure</c>, 8 HQ <c>item spawns HQ</c>, 9 RESIDENTIAL
    /// <c>item spawns resident</c>, 11 RESIDENTIAL_SMALL <c>item spawns resident small</c>; other designations none.
    /// </summary>
    public static IReadOnlyList<RecordLink> VendorLinks(SquadTemplate template, GameRecord? faction)
    {
        if (template.Vendors.Count > 0 || faction is null) return template.Vendors;
        string? list = template.BuildingDesignation switch
        {
            2 or 5 => "item spawns armoury",
            3 => "item spawns bar",
            6 => "item spawns treasure",
            8 => "item spawns HQ",
            9 => "item spawns resident",
            11 => "item spawns resident small",
            _ => null,
        };
        return list is null ? [] : [.. faction.GetReferences(list).Select(r => new RecordLink(r.TargetStringId, r.Values.Value0, r.Values.Value1, r.Values.Value2))];
    }

    /// <summary>
    /// Replaces a squad shop's stock but its unique items (<c>FUN_14095ab10</c>) and returns the new purse; null when no unit is to be made or none was
    /// (the stock stays cleared, the purse as it was). Items are valued as <c>FUN_1407a8f90</c> for <paramref name="valuation"/>, the open trade window's trader or none.
    /// </summary>
    public static long? Fill(Shop shop, SquadTemplate template, Market market, PopulationData data, ulong seed, ulong key, Trader valuation)
    {
        var rng = new RandomStream(seed, Rng.Mix(key ^ ((ulong)shop.Fills << 40)), RngPurpose.Trade);
        shop.Fills++;
        // The fills clear everything but unique items (docs/game/economy.md "Artifacts").
        shop.Stock.Items.RemoveAll(i => !i.Unique);
        int n = Count(template, ref rng);
        if (n == 0) return null;
        var (units, cats) = Roll(template, shop.Terms, market, data, n, ref rng);
        if (units.Count == 0 && cats == 0) return null;
        // Dearest first, ties by record id so the order never depends on anything but the data.
        foreach (var item in units.OrderByDescending(u => market.UnitPrice(u, false, valuation, -1)).ThenBy(u => u.Record, StringComparer.Ordinal))
            shop.Stock.Add(item);
        // The purse counts what this fill made, the money rolls included, not the unique items it kept.
        long value = shop.Stock.All().Where(i => !i.Unique).Sum(i => market.StackPrice(i, false, valuation, -1)) + cats;
        return (long)Math.Clamp(0.25f * value, 3000, 25000);
    }

    /// <summary>The number of units a fill makes: round(U(0.8, 1.2) x <c>vendors fill total amount</c> x wealth); <paramref name="building"/>: the building filler's, at least one.</summary>
    public static int Count(SquadTemplate template, ref RandomStream rng, bool building = false)
    {
        int n = (int)MathF.Round((0.8f + 0.4f * rng.NextFloat()) * template.VendorsFillTotalAmount, MidpointRounding.AwayFromZero);
        return building ? Math.Max(n, 1) : n;
    }

    /// <summary>
    /// A building shop's purse (<c>FUN_14095b210</c>): <c>vendor money</c> + r, r an int in [-trunc(0.4 x it), trunc(0.4 x it)], else trunc(clamp(0.25 x the value placed, 3000, 25000)).
    /// The original's value includes the money stack placed among the stock; here <paramref name="cats"/>, not stocked (as a squad shop's), counts in it instead.
    /// </summary>
    public static long BuildingPurse(SquadTemplate template, long value, long cats, ref RandomStream rng)
    {
        int vm = template.VendorMoney;
        if (vm < 1) return (long)Math.Clamp(0.25f * (value + cats), 3000, 25000);
        int spread = (int)(vm * 0.4);
        return vm - spread + rng.NextInt(2 * spread + 1);
    }

    /// <summary>
    /// The <paramref name="n"/> units a fill rolls (<c>FUN_140959800</c> / <c>FUN_140959a90</c>, <b>Verified</b>), merged into stacks, and the cats of the lists' money rolls (<c>FUN_140956060</c>).
    /// </summary>
    public static (List<ItemInstance> Units, long Cats) Roll(SquadTemplate template, Trader terms, Market market, PopulationData data, int n, ref RandomStream rng)
    {
        var faction = (uint)terms.Faction < (uint)data.Factions.Count ? data.Db.Find(data.Factions[terms.Faction].Id) : null;
        var lists = new List<(VendorList List, float Weight)>();
        foreach (var link in VendorLinks(template, faction))
            if (data.Db.Find(link.Id) is { Type: FcsRecordType.VENDOR_LIST } record) lists.Add((VendorList.From(data.Db, record), link.V0));
        float totalWeight = lists.Sum(l => l.Weight);
        var own = market.FactionCulture(terms.Faction);

        var units = new List<ItemInstance>();
        long cats = 0;
        foreach (var (list, _) in lists)
            if (list.MoneyItemProb > 0 && rng.NextFloat() <= list.MoneyItemProb && list.MoneyItemMax >= list.MoneyItemMin)
                cats += list.MoneyItemMin + rng.NextInt(list.MoneyItemMax - list.MoneyItemMin + 1);
        int remaining = n;
        for (int l = 0; l < lists.Count && totalWeight > 0; l++)
        {
            var (list, weight) = lists[l];
            int count = (int)MathF.Round(weight / totalWeight * n, MidpointRounding.AwayFromZero);
            remaining -= count;
            if (l == lists.Count - 1 && remaining > 0) count += remaining;
            // The list sells its faction's allowed items (FUN_140956ba0); a blueprint or map is drawn once per fill.
            var entries = list.Entries.Where(e => !(e.Type == FcsRecordType.ITEM && own?.Forbidden.Contains(e.Id) == true)).ToList();
            for (int k = 0; k < count && entries.Count > 0; k++)
            {
                var entry = Pick(entries, ref rng);
                if (Make(entry, list, faction, template, market, data.Db, ref rng) is { } item) Merge(units, item);
                if (entry.Type is FcsRecordType.RESEARCH or FcsRecordType.MAP_ITEM) entries.Remove(entry);
            }
        }
        return (units, cats);
    }

    /// <summary>
    /// SQUAD_TEMPLATE <c>special items</c> (ITEM references), else <c>special map items</c> (MAP_ITEM), on a fill with specials (<c>FUN_140958740</c> from the shop refresh <c>FUN_14095b210</c>,
    /// <b>Verified</b>): each entry's v0 items, in stacks of at most the item's stack limit; they go in before the vendor items and what fits nowhere is lost. The original also
    /// marks them unique, which <see cref="BuildingFill"/> does and a squad shop does not.
    /// </summary>
    public static List<ItemInstance> Specials(string templateId, GameDatabase db, Market market)
    {
        var made = new List<ItemInstance>();
        if (db.Find(templateId) is not { } template) return made;
        var list = template.GetReferences("special items");
        if (list.Count == 0) list = template.GetReferences("special map items");
        foreach (var r in list)
            for (int left = r.Values.Value0; left > 0;)
            {
                if (market.Items.Create(r.TargetStringId, 1) is not { } probe) break;
                int stack = Math.Clamp(left, 1, Math.Max(probe.StackLimit, 1));
                if (market.Items.Create(r.TargetStringId, stack) is { } item) made.Add(item);
                left -= stack;
            }
        return made;
    }

    internal static VendorEntry Pick(IReadOnlyList<VendorEntry> entries, ref RandomStream rng)
    {
        long total = entries.Sum(e => (long)Math.Max(e.Weight, 0));
        if (total <= 0) return entries[0];
        long roll = (long)(rng.NextFloat() * total);
        foreach (var e in entries)
        {
            if (roll < e.Weight) return e;
            roll -= Math.Max(e.Weight, 0);
        }
        return entries[^1];
    }

    /// <summary>Puts one unit into the list: onto an earlier one of the same record with room (whatever its quality; a weapon never), else as a new item (<c>FUN_1409555d0</c>).</summary>
    static void Merge(List<ItemInstance> units, ItemInstance item)
    {
        if (item.Type != FcsRecordType.WEAPON)
            foreach (var u in units)
                if (u.Record == item.Record && u.Quantity < u.StackLimit)
                {
                    u.Quantity++;
                    return;
                }
        units.Add(item);
    }

    static ItemInstance? Make(VendorEntry entry, VendorList list, GameRecord? faction, SquadTemplate template, Market market, GameDatabase db, ref RandomStream rng)
    {
        float quality = entry.Type == FcsRecordType.ARMOUR ? CharacterGenerator.QualityOfGrade(Grade(faction, ref rng)) : 0;
        if (market.Items.Create(entry.Id, 1, quality) is not { } item) return null;
        if (entry.Type == FcsRecordType.LIMB_REPLACEMENT) item.Level = CharacterGenerator.QualityOfGrade(LimbGrade(template.RoboticsLevels, ref rng));
        if (entry.Type == FcsRecordType.CROSSBOW) item.Level = CharacterGenerator.QualityOfGrade(CrossbowGrade(template.CrossbowLevels, ref rng));
        if (entry.Type == FcsRecordType.WEAPON && Maker(list, db, ref rng) is { } made)
        {
            item.CompanyId = made.Maker.StringId;
            item.MaterialId = made.Model ?? "";
            item.Level = made.Level;
        }
        return item;
    }

    /// <summary>
    /// A shop limb's grade around the template's <c>robotics levels</c> g (<c>FUN_140955370</c>, <b>Verified</b>): g weighs 100, g - 2 22 when g > 1, g - 1 44 when g > 0,
    /// g + 1 44 when g &lt; 5, g + 2 22 when g &lt; 4; the roll is an int in [0, total) against the running sums in that order (engine reading of the roll's range).
    /// </summary>
    public static int LimbGrade(int g, ref RandomStream rng) =>
        Spread([(g, 100), (g - 2, g > 1 ? 22 : 0), (g - 1, g > 0 ? 44 : 0), (g + 1, g < 5 ? 44 : 0), (g + 2, g < 4 ? 22 : 0)], g, ref rng);

    /// <summary>
    /// A shop crossbow's grade around the template's <c>crossbow levels</c> g (<c>FUN_140955210</c>, <b>Verified</b>): g weighs 100; unless g is 5, g - 2 20 when g > 1, g - 1 33
    /// when g > 0, g + 1 33 when g &lt; 5, g + 2 5 when g &lt; 4. Rolled as <see cref="LimbGrade"/>.
    /// </summary>
    public static int CrossbowGrade(int g, ref RandomStream rng) => g == 5 ? g :
        Spread([(g, 100), (g - 2, g > 1 ? 20 : 0), (g - 1, g > 0 ? 33 : 0), (g + 1, g < 5 ? 33 : 0), (g + 2, g < 4 ? 5 : 0)], g, ref rng);

    static int Spread(ReadOnlySpan<(int Grade, int Weight)> w, int g, ref RandomStream rng)
    {
        int total = 0;
        foreach (var (_, weight) in w) total += weight;
        int roll = rng.NextInt(total);
        foreach (var (grade, weight) in w)
        {
            if (roll < weight) return grade;
            roll -= weight;
        }
        return g;
    }

    /// <summary>A grade 0..5 by the faction's <c>armors 0</c> .. <c>armors 5</c> chances (the field names as the doc gives them; the spelling in the data is <b>Unknown</b>).</summary>
    internal static int Grade(GameRecord? faction, ref RandomStream rng)
    {
        Span<int> w = stackalloc int[6];
        int total = 0;
        for (int g = 0; g < 6; g++) total += w[g] = Math.Max(faction?.GetInt($"armors {g}", DefaultGradeChances[g]) ?? DefaultGradeChances[g], 0);
        if (total <= 0) return 1;
        int roll = rng.NextInt(total);
        for (int g = 0; g < 6; g++)
        {
            if (roll < w[g]) return g;
            roll -= w[g];
        }
        return 5;
    }

    /// <summary>A manufacturer from the list (v0, 0 counting as 100), then a model from its <c>weapon models</c> (v1, 0 counting as 100) whose v0 is the level; none or level 0: the fixed fallback model.</summary>
    internal static (GameRecord Maker, string? Model, int Level)? Maker(VendorList list, GameDatabase db, ref RandomStream rng)
    {
        var makers = list.Manufacturers.Select(m => (Record: db.Find(m.Id), Weight: m.V0 == 0 ? 100 : m.V0)).Where(m => m.Record is not null && m.Weight > 0).ToList();
        if (makers.Count == 0) return null;
        int roll = rng.NextInt(makers.Sum(m => m.Weight));
        var maker = makers[^1].Record!;
        foreach (var m in makers)
        {
            if (roll < m.Weight) { maker = m.Record!; break; }
            roll -= m.Weight;
        }
        var models = maker.GetReferences("weapon models").Where(r => db.Find(r.TargetStringId) is not null).ToList();
        // No model or a model of level 0: the factory substitutes manufacturer 1057 and model 1058 (FUN_140580750, Verified).
        if (models.Count == 0 || Model(models, ref rng) is not { Values.Value0: not 0 } picked)
        {
            var (fallback, model, level) = CharacterGenerator.FallbackWeaponMaker(db, maker);
            return (fallback, model?.StringId, level);
        }
        return (maker, picked.TargetStringId, picked.Values.Value0);
    }

    static GameReference Model(List<GameReference> models, ref RandomStream rng)
    {
        int total = models.Sum(r => r.Values.Value1 == 0 ? 100 : Math.Max(r.Values.Value1, 0));
        int pick = rng.NextInt(Math.Max(total, 1));
        foreach (var r in models)
        {
            int w = r.Values.Value1 == 0 ? 100 : Math.Max(r.Values.Value1, 0);
            if (pick < w) return r;
            pick -= w;
        }
        return models[^1];
    }
}
