using Meitou.Data.Gameplay;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Simulation.Items;

namespace Meitou.Simulation.Trading;

/// <summary>The player opens a trade between one of their characters and a trader: the customer walks up to the trader and the trade window opens in reach.</summary>
public sealed record OpenTrade(CharacterId Customer, CharacterId Trader) : OrderCommand;
/// <summary>Closes the open trade.</summary>
public sealed record CloseTrade : SimCommand;
/// <summary>
/// The trade window's right click on a stock item (<c>RClickAutoTrade</c>, with Shift <c>RClickAutoTradeAll</c>): buys up to <see cref="Quantity"/> units of <see cref="Item"/> (its index in
/// the window's copy of the shop's stock, <see cref="TradeView.Stock"/>) a unit at a time into the customer's inventory, where they first fit. A drag is <see cref="LiftItem"/> and
/// <see cref="PlaceHeld"/>.
/// </summary>
public sealed record BuyItem(InventoryItemToken Item, int Quantity) : SimCommand
{
    /// <summary>One unit goes into the equipment slot that takes it when that is free, before the grids (<see cref="InventoryCommands.TryEquip"/>).</summary>
    public bool Equip { get; init; }
    /// <summary>
    /// The buyer's bag window is open: each unit goes into the worn bag first, then to the character (<c>rightClickAutoEquipping</c> trades into the bag window when it is open, into
    /// the character window on no room, <b>Verified</b>). A bag bought skips it, as Meitou keeps containers out of bags.
    /// </summary>
    public bool BagFirst { get; init; }
}

/// <summary>The right click on a customer's item: sells up to <see cref="Quantity"/> units of <see cref="Item"/> (its index in the customer's inventory) a unit at a time into the window, a copy into the stock.</summary>
public sealed record SellItem(InventoryItemToken Item, int Quantity) : SimCommand;

/// <summary>One item of a trade window, with what it costs (stock) or fetches (the customer's goods) as a whole stack.</summary>
public sealed record TradeItemView(int Index, string Record, string Name, string Section, int X, int Y, int Width, int Height, int Quantity, float Quality, long Price, long UnitPrice, bool Stolen)
{
    /// <summary>Units of it were traded in this trade window (<see cref="BuybackUnits"/> of them): those trade back at the opposite direction's value.</summary>
    public bool Buyback { get; init; }
    public int BuybackUnits { get; init; }
    /// <summary>A bag (a container with a contents grid): it trades whole, and may go into the backpack slot.</summary>
    public bool Container { get; init; }
    /// <summary>The item's material record ("" none), which picks its icon.</summary>
    public string Material { get; init; } = "";
    /// <summary>The COLOR_DATA it is dyed with (<see cref="Items.ItemInstance.ColourId"/>), which picks its icon.</summary>
    public string Colour { get; init; } = "";
    /// <summary>How full the item is (0 to 1); null: no charge bar.</summary>
    public float? Charge { get; init; }
    /// <summary>The item's manufacturer record (<c>company sid</c>; "" none), model level and charges, and the trader factor of its price (<c>FUN_14079e6d0</c>, 1: none).</summary>
    public string Company { get; init; } = "";
    public int Level { get; init; }
    public float Charges { get; init; }
    public float MaxCharges { get; init; }
    public float PriceFactor { get; init; } = 1;
    /// <summary>The trader's town market factor of the item (<see cref="Market.MarketFactor"/>, 1: none); with <see cref="PriceFactor"/> it makes a trade item's <c>Price mark-up</c>.</summary>
    public float MarketFactor { get; init; } = 1;
    /// <summary>The faction a stolen item was stolen from, -1 when it is not stolen.</summary>
    public int StolenFrom { get; init; } = -1;
    /// <summary>The item's <c>unique</c> flag (<see cref="Items.ItemInstance.Unique"/>): the tooltip's [Unique] row.</summary>
    public bool Unique { get; init; }
    /// <summary>The tooltip's two prices of one unit (<c>FUN_14079e7a0</c> calls slot 81, <b>Verified</b>): what the shop asks for it (Value) and what it pays (Sell value).</summary>
    public long Value { get; init; }
    public long SellValue { get; init; }
    /// <summary>The stack's weight in kg.</summary>
    public float Weight { get; init; }
    /// <summary>A stolen good of the customer's that the shop would roll on (<c>FUN_140792830</c>, whoever it was stolen from): the chance (0 to 1) a sale goes through unnoticed, <see cref="TradeSystem"/>'s fence odds, the hover box's number; null otherwise.</summary>
    public float? FenceChance { get; init; }
    /// <summary>
    /// A stolen good of the customer's that a shop which is no fence would buy only with a risk: the chance (0 to 1, below 1) a sale goes through unnoticed, whoever it was stolen from
    /// (<c>FUN_140715290</c> asks "Fence it anyway?" with the opposite in per cent); null when the sale is safe or the item is not stolen.
    /// </summary>
    public float? FenceOdds { get; init; }
    /// <summary>A good of the customer's that the culture of the trader's faction lists as illegal (the smuggling prompt, <c>FUN_140712920</c>).</summary>
    public bool Illegal { get; init; }
    public InventoryItemToken Token => new(Index, Record, Section, X, Y, Quantity, Quality);
}

/// <summary>Arranges the open trade's window (the trader window's ARRANGE button): the window's copy of the stock as <see cref="TradeSystem"/> arranges it; the stock itself stays as it is.</summary>
public sealed record ArrangeStock : SimCommand;

/// <summary>The open trade as the UI shows it (published with the snapshot).</summary>
public sealed record TradeView(CharacterId Customer, CharacterId Trader, string TraderName, long ShopMoney, long PlayerMoney, int StockWidth, int StockHeight,
    IReadOnlyList<TradeItemView> Stock, IReadOnlyList<TradeItemView> Goods, bool BuysStolen, bool BuysIllegal = false)
{
    /// <summary>The held item (<see cref="PlayerState.Held"/>) priced as stock (lifted from the stock) or as the customer's goods (lifted from the customer); null for none or another's.</summary>
    public TradeItemView? Held { get; init; }
}

/// <summary>A system that has an open trade to publish (the trade system).</summary>
public interface ITradeSource
{
    TradeView? PublishTrade();
}

public sealed class TradeSettings
{
    /// <summary>How close the customer must stand to the trader (the original's "Out of trading range." distance is <b>Unknown</b>; engine choice).</summary>
    public float Range { get; init; } = 30;
    /// <summary>Ticks between looks at the squads for new shops and due restocks (engine choice: half a game second).</summary>
    public int CheckEveryTicks { get; init; } = 15;
    /// <summary>Game seconds the customer has to reach the trader before the opening is dropped (engine choice).</summary>
    public float ApproachSeconds { get; init; } = 60;
}

/// <summary>
/// The economy in play (docs/simulation.md "Trade", docs/game/economy.md sections 8 and 9):
/// <list type="bullet">
/// <item><b>Shops</b>: a squad made from a template with <c>is trader</c> and vendor lists gets a <see cref="Shop"/> when it appears (the game's first fill at squad creation), filled by
/// <see cref="ShopFiller"/>, with the purse in <see cref="Squad.Money"/>; it restocks every <c>vendors refresh time</c> game hours (9.3). Squads of the player's faction never get one.
/// A housed one gets a building shop when its home has stock containers (<see cref="Shop.ForBuilding"/>).</item>
/// <item><b>Trading</b> (the trade window <c>FUN_140715560</c>, <b>Observed</b>): a purchase is priced by <see cref="Market.StackPrice"/> without the sell flag, a sale with it; when the payer
/// cannot afford the stack the number of units it can afford moves instead, none: "You can't afford that." (3) or "The shopkeeper can't afford that." (4). What does not fit: "No room for
/// that item." (2). The cats move between the player's purse and the shop's.</item>
/// <item><b>Fencing</b> (8.4, <b>Observed</b>): a stolen item sold to a shop that is not a knowing fence (<c>buys stolen</c>; a knowing fence only for goods stolen from its own squad) is caught unless a roll is below
/// lerp(thievery / 100, 0.4, 1.25), halved when the item was stolen from the shop's own faction and again from its own squad (never caught when it was stolen from a bandit or adventurer faction, or from one the shop's faction is
/// not allied to whose home town is not the trader's, <see cref="FenceOdds"/>); the seller trains thievery either way; caught: "Got caught selling stolen item." (9), the sale is refused and the seller gets a
/// pending Fencing crime against the shop's faction, booked at once with the shopkeeper as the witness, and the trade closes (<c>closeAllInventories</c>, a drop and a right click alike,
/// <b>Verified</b>). The prompt that shows the chance is the UI's. An illegal item (the culture of the town's faction) is refused by a trader that does not buy illegal goods:
/// "Attempted smuggling!" (<see cref="Smuggling"/>), which closes the trade too; the rest of the smuggling rules (the trader's answer for a smuggler, read by no caller seen) are not built.</item>
/// </list>
/// The shops' stock and restock times are in the state hash (through the squads). The open trade is not, but the buyback counts it leaves on items are, and they are not saved:
/// a save made mid-trade reloads with a different hash (docs/simulation.md, trading).
/// </summary>
public sealed class TradeSystem : ITickSystem, ITradeSource
{
    readonly PopulationData data;
    readonly CharacterAssembly assembly;
    readonly Dictionary<string, SquadTemplate?> templates = new(StringComparer.Ordinal), homeTemplates = new(StringComparer.Ordinal);
    Market? market;
    CharacterId customer = CharacterId.None, trader = CharacterId.None;
    bool open, traderWandered;
    TradeSession session = new();
    long approachUntil;
    TradeView? view;
    // The open trade's window of its shop's stock (DisplayOf), the shop it shows and that shop's fill count when it was made.
    Inventory? display;
    Shop? displayOf;
    int displayFills;
    // The window's sources besides the stock: the shop building's other containers (Sources).
    List<Inventory> others = [];

    public TradeSystem(PopulationData data, TradeSettings? settings = null)
    {
        this.data = data;
        assembly = new(data);
        Settings = settings ?? new TradeSettings();
    }

    public TradeSettings Settings { get; }
    /// <summary>The crime system a caught fence is reported to (null: no crimes).</summary>
    public Crime.CrimeSystem? Crimes { get; set; }
    /// <summary>The prices of this world (made at the first tick, from the world's seed).</summary>
    public Market Market(World world) => market ??= new Market(data, world.Seed);

    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        foreach (var command in commands)
        {
            switch (command)
            {
                case OpenTrade o:
                    Close(world);
                    customer = o.Customer;
                    trader = o.Trader;
                    approachUntil = world.Tick + (long)(Settings.ApproachSeconds / world.TickSeconds);
                    if (!Resolve(world, out int c, out int t, out _))
                    {
                        // A right click on an enemy is still an attack (the UI sends the trade for any trader; only the world knows who is hostile).
                        if (world.Characters.TryResolveNext(trader, out int who) && world.Characters.Cold(who) is { } tc && AggroSystem.IsEnemy(data.Relations, world.Player.Faction, tc.Faction))
                            world.Defer(new Combat.AttackOrder([customer], trader) { Tick = world.Tick + 1 });
                        Close(world);
                        break;
                    }
                    Hold(world, t);
                    if (!InReach(world, c, t)) world.Defer(new MoveOrder([customer], world.Characters.Next[t].Position) { Tick = world.Tick + 1 });
                    break;
                case CloseTrade:
                    Close(world);
                    break;
                case BuyItem buy when open:
                    Buy(world, buy);
                    break;
                case SellItem sell when open:
                    Sell(world, sell);
                    break;
                case ArrangeStock when open:
                    if (Resolve(world, out _, out _, out var arranged))
                    {
                        DisplayOf(world, arranged!);
                        Arrange();
                    }
                    break;
            }
        }
    }

    public void SlowWorld(World world)
    {
        if (world.Tick % Settings.CheckEveryTicks == 0) { Stock(world); PayWages(world); }
        if (!customer.IsNone)
        {
            if (!Resolve(world, out int c, out int t, out _)) Close(world);
            else if (InReach(world, c, t))
            {
                open = true;
                Hold(world, t);
            }
            else if (open || world.Tick >= approachUntil) Close(world);   // walked away, or never arrived
            else Hold(world, t);
        }
        RefreshView(world);
    }


    // CHARACTER `wages` (Character::giveBirth and Character::dailyUpdate, Verified): a character not of the player's faction with wages of 1 or more sets (not adds to) its squad's money
    // to them when it is made and every day; with several waged members the last one wins. Engine choices: the day pass runs when the game day changes, members in squad order; "when
    // made" is the first pass that sees the squad, except the squads already there at this system's first pass and a squad of characters back from a save or from out of the world
    // (Squad.Restored: giveBirth pays only a character made without saved state, so a loaded game keeps its saved purses until the next day).
    readonly Dictionary<string, int> wages = new(StringComparer.Ordinal);
    readonly HashSet<int> paidSquads = [];
    long wageDay = long.MinValue;

    void PayWages(World world)
    {
        long day = (long)Math.Floor(world.GameHours / 24);
        bool first = wageDay == long.MinValue, newDay = !first && day != wageDay;
        wageDay = day;
        foreach (var squad in world.Squads.All)
        {
            bool appeared = paidSquads.Add(squad.Id);
            if (first || !newDay && (!appeared || squad.Restored)) continue;
            foreach (var id in squad.Members)
            {
                if (!world.Characters.TryResolveNext(id, out int s) || world.Characters.Cold(s) is not { } c || c.IsPlayer || data.PlayerFaction >= 0 && c.Faction == data.PlayerFaction) continue;
                if (!wages.TryGetValue(c.RecordId, out int pay)) wages[c.RecordId] = pay = data.Db.Find(c.RecordId) is { Type: Meitou.Data.Fcs.FcsRecordType.CHARACTER } r ? r.GetInt("wages", 0) : 0;
                if (pay >= 1) squad.Money = pay;
            }
        }
    }

    internal void RefreshView(World world) =>
        view = open && Resolve(world, out int ci, out int ti, out var shop) ? Publish(world, ci, ti, shop!) : null;

    public TradeView? PublishTrade() => view;

    /// <summary>Keeps the trader standing while the customer comes and trades (residents wander; the original's shopkeepers stand at their node). Its wander resumes on close.</summary>
    void Hold(World world, int t)
    {
        ref var n = ref world.Characters.Next[t];
        world.Characters.Cold(t)!.Engaged = true;   // the AI leaves it alone until the trade closes
        if (n.Task == (byte)CharacterTask.Wander) traderWandered = true;
        if (n.Task is (byte)CharacterTask.Wander or (byte)CharacterTask.Idle) TaskStart.Stand(world.Characters, t);
    }

    void Close(World world)
    {
        session.Active = false;
        session = new();
        if (world.Characters.TryResolveNext(trader, out int held)) world.Characters.Cold(held)!.Engaged = false;
        if (traderWandered && world.Characters.TryResolveNext(trader, out int t) && world.Characters.Next[t].Task == (byte)CharacterTask.Idle)
            TaskStart.Become(world, t, CharacterTask.Wander, Meitou.Data.Gameplay.Ai.TaskType.WANDERER, TaskSource.Npc);
        customer = trader = CharacterId.None;
        open = traderWandered = false;
        view = null;
        // A copy held off the window was taken off the stock when lifted: it goes back into the stock.
        if (world.Player.Held is { From.Holder: ItemHolder.Stock } lifted && displayOf is { } shop)
        {
            Stocked(shop, lifted.Item, lifted.Item.Quantity);
            world.Player.Held = null;
        }
        (display, displayOf) = (null, null);
    }

    // ------------------------------------------------------------------ shops

    SquadTemplate? TemplateOf(string id)
    {
        if (!templates.TryGetValue(id, out var t))
            templates[id] = t = data.Db.Find(id) is { } r && SquadFactory.IsTemplate(r) && SquadTemplate.From(r) is { HasShop: true } shop ? shop : null;
        return t;
    }

    void Stock(World world)
    {
        foreach (var squad in world.Squads.All)
        {
            if (squad.Faction >= 0 && (squad.Faction == world.Player.Faction || squad.Faction == data.PlayerFaction)) continue;
            bool living = Living(world, squad);
            if (squad.Shop is { Building: not null } closing && !living)
            {
                // Nobody of the squad is left alive: its stock goes back into its containers, which keep it, as the original's containers hold the stock (artifacts included).
                GiveBack(world, closing, all: true);
                squad.Shop = null;
                continue;
            }
            if (squad.Shop is { } shop)
            {
                Hold(shop, squad);
                if (world.Tick < shop.NextRestockTick || TemplateOf(squad.TemplateId) is not { } again) continue;
                Refill(world, squad, shop, again);
            }
            else if (living && TemplateOf(squad.TemplateId) is { } template)
            {
                var terms = new Trader(squad.Town, squad.Faction, template.BuyMult, template.BuysStolen, template.BuysIllegal);
                // A housed trader keeps a building shop when its home has stock containers, whether or not it made prisoner or slave squads (FUN_140583a10 / FUN_1404ff5e0, Observed); without containers the squad shop stays (Meitou fallback, the original stocks nothing).
                squad.Shop = (squad.HomeBuilding is { } home ? Shop.ForBuilding(home, terms) : null)
                    ?? new Shop { Terms = terms };
                Hold(squad.Shop, squad);
                TakeContents(world, squad.Shop);
                Refill(world, squad, squad.Shop, template);
            }
            else if (squad.HomeBuilding is { } home && HomeTemplateOf(squad.TemplateId) is { } contents && ShopFiller.VendorLinks(contents, FactionRecord(squad.Faction)).Count > 0) FillHome(world, squad, contents, home);
        }
    }

    /// <summary>
    /// A new building shop's stock containers hand what they hold (a town's artifacts) to the stock, each into its own rectangle (in the original the stock is what they hold,
    /// docs/game/economy.md "Artifacts").
    /// </summary>
    static void TakeContents(World world, Shop shop)
    {
        for (int k = 0; k < shop.Containers.Count; k++)
        {
            if (world.Fixtures.Find(shop.Containers[k]) is not { Contents: { } grid } || grid.Items.Count == 0) continue;
            var region = shop.Stock.Regions![k];
            foreach (var item in grid.Items)
            {
                item.X += region.X;
                item.Y += region.Y;
                shop.Stock.Items.Add(item);
            }
            grid.Items.Clear();
        }
    }

    /// <summary>
    /// A shop's stock is held by the shop's faction (an item of it stolen from that faction is not stolen there, <see cref="InventoryCommands.Stolen"/>): a building's containers, of no
    /// squad; a squad shop's members' bags (<c>ShopTrader</c>), of its squad.
    /// </summary>
    void Hold(Shop shop, Squad squad) =>
        shop.Stock.Holder ??= () => new(shop.Terms.Faction, shop.Building is null ? squad.Id : -1, data.IsLawless(shop.Terms.Faction));

    /// <summary>Whether someone of the squad is alive.</summary>
    static bool Living(World world, Squad squad)
    {
        var table = world.Characters;
        foreach (var m in squad.Members)
            if (table.TryResolveNext(m, out int slot) && table.Cold(slot) is { DiedTick: < 0 }) return true;
        return false;
    }

    /// <summary>
    /// A building shop whose squad is dropped, made afresh or all dead hands its unique items (artifacts; with <paramref name="all"/>, its whole stock) back to their containers,
    /// which keep them for the next shop, as the original's containers keep them until taken (docs/game/economy.md "Artifacts").
    /// </summary>
    internal static void GiveBack(World world, Shop shop, bool all = false)
    {
        if (shop.Stock.Regions is not { } regions) return;
        for (int k = 0; k < shop.Containers.Count; k++)
        {
            if (world.Fixtures.Find(shop.Containers[k]) is not { Contents: { } grid }) continue;
            foreach (var item in shop.Stock.Items.Where(i => (all || i.Unique) && regions[k].Holds(i)).ToList())
            {
                shop.Stock.Items.Remove(item);
                (item.X, item.Y) = (item.X - regions[k].X, item.Y - regions[k].Y);
                if (grid.IsFree("main", item.X, item.Y, item.Width, item.Height)) grid.Items.Add(item);
                else grid.AddTo("main", item);
            }
        }
    }

    Meitou.Data.GameRecord? FactionRecord(int faction) => (uint)faction < (uint)data.Factions.Count ? data.Db.Find(data.Factions[faction].Id) : null;

    SquadTemplate? HomeTemplateOf(string id)
    {
        if (!homeTemplates.TryGetValue(id, out var t))
            homeTemplates[id] = t = data.Db.Find(id) is { } r && SquadFactory.IsTemplate(r) && SquadTemplate.From(r) is { IsTrader: false } fill && (fill.Vendors.Count > 0 || fill.BuildingDesignation is 2 or 3 or 5 or 6 or 8 or 9 or 11) ? fill : null;
        return t;
    }

    /// <summary>
    /// A housed squad with <c>vendors</c> that is no trader (a resident's <c>house contents</c>): the building filler fills its home's storage containers as a trader's (the squad factory
    /// <c>FUN_140583a10</c> tests <c>vendors</c>, not <c>is trader</c>, <b>Observed</b>), unlocking them; here each unit goes into the first container with room and what fits nowhere is
    /// lost. Engine choice: again every <c>vendors refresh time</c> hours, but only while the containers hold nothing but unique items (a loaded save keeps what they hold); the purse is not touched.
    /// </summary>
    void FillHome(World world, Squad squad, SquadTemplate template, Meitou.Data.World.TownBuilding home)
    {
        if (world.Tick < squad.NextHomeFillTick) return;
        bool first = squad.NextHomeFillTick == 0;
        squad.NextHomeFillTick = world.Tick + (long)(template.VendorsRefreshTime / BodySystem.HoursPerTick);
        var boxes = new List<Locks.Fixture>();
        foreach (var f in home.Fixtures)
            if (f.HoldsShopStock && world.Fixtures.Find(f.Id) is { Contents: not null } box) boxes.Add(box);
        if (boxes.Count == 0 || boxes.Any(b => b.Contents!.Items.Any(i => !i.Unique))) return;
        if (Shop.ForBuilding(home, new Trader(squad.Town, squad.Faction, template.BuyMult, template.BuysStolen, template.BuysIllegal)) is not { } merged) return;
        merged.Fills = (int)world.Tick;
        ShopFiller.Fill(merged, template, Market(world), data, world.Seed, (ulong)squad.Id, specials: first);
        foreach (var item in merged.Stock.Items)
            foreach (var box in boxes)
                if (box.Contents!.Add(item)) break;
        foreach (var box in boxes)
            if (box.Lock is { } l) l.Locked = false;
    }

    void Refill(World world, Squad squad, Shop shop, SquadTemplate template)
    {
        squad.Money = ShopFiller.Fill(shop, template, Market(world), data, world.Seed, (ulong)squad.Id, specials: shop.Fills == 0);
        // The building filler unlocks the stock containers it fills (FUN_14095b210).
        if (shop.Building is not null && squad.HomeBuilding is { } home)
            foreach (var f in home.Fixtures)
                if (f.HoldsShopStock && world.Fixtures.Find(f.Id)?.Lock is { } l) l.Locked = false;
        shop.NextRestockTick = world.Tick + (long)(template.VendorsRefreshTime / BodySystem.HoursPerTick);
    }

    /// <summary>The shop a character trades for: its squad's (null: not a trader).</summary>
    public static Shop? ShopOf(World world, CharacterCold cold) => cold.SquadId >= 0 ? world.Squads.Find(cold.SquadId)?.Shop : null;

    // ------------------------------------------------------------------ trading

    bool Resolve(World world, out int c, out int t, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Shop? shop)
    {
        var table = world.Characters;
        shop = null;
        t = -1;
        if (!table.TryResolveNext(customer, out c) || !table.TryResolveNext(trader, out t)) return false;
        var cc = table.Cold(c)!;
        var tc = table.Cold(t)!;
        if (!cc.IsPlayer || cc.Medical is { Incapacitated: true } || tc.Medical is { Incapacitated: true } || tc.InCombat || cc.Inventory is null) return false;
        if (AggroSystem.IsEnemy(data.Relations, cc.Faction, tc.Faction)) return false;
        shop = ShopOf(world, tc);
        return shop is not null;
    }

    bool InReach(World world, int c, int t)
    {
        var a = world.Characters.Next[c].Position;
        var b = world.Characters.Next[t].Position;
        float dx = a.X - b.X, dz = a.Z - b.Z;
        return dx * dx + dz * dz <= Settings.Range * Settings.Range;
    }

    static ItemInstance? Find(Inventory inventory, InventoryItemToken token)
    {
        var item = inventory.All().ElementAtOrDefault(token.Index);
        return item is not null && item.Record == token.Record && item.Section == token.Section && item.X == token.X && item.Y == token.Y && item.Quantity == token.Quantity
            && item.Quality == token.Quality && (item.Contents is null || item.Quantity == 1) ? item : null;
    }

    // ------------------------------------------------------------------ the trade window's stock

    /// <summary>
    /// The shop's stock as the trade window shows it (<c>FUN_140953850</c>, <c>FUN_1409528d0</c>, <b>Verified</b>): copies of its items but money in one grid as wide as the stock (the widest
    /// container, at least 18) and as tall as all its containers' cells take (18 x 18 for a squad shop), each added as the window adds one (<see cref="Shown"/>), then arranged
    /// (<see cref="Arrange"/>). Made when the trade opens, and made again in the same grid on a restock of the shop (<see cref="Shop.Fills"/>) when the trader shown is its squad's leader and
    /// the restock made stock (<c>FUN_14095ab10</c>, <c>FUN_14095b9d0</c>, <b>Verified</b>); other changes to the stock (a shopper, a container looted) do not show until then. An item held
    /// off the window stays on the mouse.
    /// </summary>
    Inventory DisplayOf(World world, Shop shop)
    {
        bool same = display is not null && ReferenceEquals(displayOf, shop);
        if (same && displayFills != shop.Fills && !ShowsRestock(world, shop)) displayFills = shop.Fills;
        if (!same || displayFills != shop.Fills)
        {
            (others, int width, int total) = Sources(world, shop);
            if (total == 0) total = width * width;
            int height = (total + width - 1) / width;
            // The window only ever grows during a trade (FUN_140954ba0): a remade one keeps its height.
            if (same) { display!.Items.Clear(); display.Height = Math.Max(height, display.Height); }
            // Held by the trader's ShopTrader: its faction, no squad (its handle's container is 0).
            else display = new Inventory(width, height) { StackBonusMult = Shop.StockStackMult, Holder = () => new(shop.Terms.Faction, -1, data.IsLawless(shop.Terms.Faction)) };
            foreach (var item in shop.Stock.Items.Concat(others.SelectMany(o => o.Items)))
                if (item.ItemFunction != PlayerSystem.MoneyFunction) Shown(Copy(item), item.Quantity, copy: true);
            Arrange();
            (displayOf, displayFills) = (shop, shop.Fills);
        }
        return display!;
    }

    /// <summary>
    /// The window's sources besides the stock, and its size (<c>FUN_140953850</c>, <b>Observed</b>): a building shop shows every building in its interior that has an inventory, whatever
    /// its function, as wide as the widest (at least 18) and as many cells as they all have. Engine choice: of those outside the stock, the lockable containers' contents are shown;
    /// one with no lock (a bench) keeps no inventory here, so it adds its cells and nothing else. A squad shop has only its stock.
    /// </summary>
    (List<Inventory> Others, int Width, int Cells) Sources(World world, Shop shop)
    {
        int cells = shop.Stock.Regions?.Sum(r => r.Width * r.Height) ?? 0;
        if (shop.Building is null || !world.Characters.TryResolveNext(trader, out int t) || world.Squads.Find(world.Characters.Cold(t)!.SquadId)?.HomeBuilding is not { } home
            || home.Key != shop.Building || home.Fixtures.Where(f => f.StorageWidth > 0 && f.StorageHeight > 0).ToList() is not { Count: > 0 } boxes)
            return ([], shop.Stock.Width, cells);
        var rest = boxes.Where(f => !shop.Containers.Contains(f.Id)).Select(f => world.Fixtures.Find(f.Id)?.Contents).OfType<Inventory>().ToList();
        return (rest, Math.Max(Shop.Width, boxes.Max(f => f.StorageWidth)), boxes.Sum(f => f.StorageWidth * f.StorageHeight));
    }

    /// <summary>The inventories a trade at <paramref name="shop"/> takes from and puts into: the stock first, then the window's other sources.</summary>
    IEnumerable<Inventory> SourcesOf(Shop shop) => ReferenceEquals(shop, displayOf) ? [shop.Stock, .. others] : [shop.Stock];

    /// <summary>
    /// Whether a restock remakes the open window: its trader is the shop squad's effective leader (<c>getSquadLeader</c>, <see cref="PopulationSystem.EffectiveLeader"/>), and the
    /// restock rolled stock (<see cref="Shop.Rolled"/>; a building shop's always remakes it).
    /// </summary>
    bool ShowsRestock(World world, Shop shop) =>
        world.Characters.TryResolveNext(trader, out int t) && world.Squads.Find(world.Characters.Cold(t)!.SquadId) is { } squad
        && (world.SystemOf<PopulationSystem>()?.EffectiveLeader(world, squad) ?? squad.Leader) == trader
        && (shop.Building is not null || shop.Rolled);

    /// <summary>
    /// A stock item as the window shows it (<c>FUN_1409528d0</c>, <b>Verified</b>): a new item (its own lock; a bag empty, <see cref="ItemInstance.Clone"/>), fully charged and with no
    /// owner, so not stolen (<c>FUN_140952730</c>, <c>FUN_14075d6b0</c>); a new item is in no trade's buyback table.
    /// </summary>
    ItemInstance Copy(ItemInstance item)
    {
        var copy = New(item);
        if (copy.MaxCharges > 0) copy.Charges = copy.MaxCharges;
        copy.Mark = StolenMark.None;
        return copy;
    }

    /// <summary>
    /// Adds an item to the window where it first fits (<c>FUN_140954ba0</c>): one that does not fit makes the window two of its heights taller (the same grid) and tries again. Returns the units placed.
    /// The retry passes the caller's count again, and the add places that count + what the item has left - 1 (<c>FUN_14074c8b0</c>, <b>Verified</b> by running both): exact for the
    /// adds of an item's own units, but a window copy (<paramref name="copy"/>: it holds 1 and is passed its source's count) places its count more, 2Q - 1 units in all, as the original.
    /// With <paramref name="stock"/> the window's callbacks follow each add (<c>_sectionAddItemCallback</c>, <c>_sectionUpdateItemCallback</c>, <b>Verified</b>): a stack of the window
    /// that grew puts a copy of itself, as many units as it grew by, into the stock, and an item new in it a copy of itself.
    /// </summary>
    int Shown(ItemInstance item, int units, int? x = null, int y = 0, bool copy = false, bool stock = false)
    {
        var before = stock ? display!.Items.ToDictionary(i => i, i => i.Quantity) : null;
        int placed = x is { } at ? PlaceAt(display!, item, units, at, y) : Place(display!, item, units);
        if (placed < units)
        {
            display!.Height += 2 * item.Height;
            if (copy) item.Quantity += units - 1;
            placed += Place(display, item, item.Quantity);
        }
        if (before is not null && displayOf is { } shop)
            foreach (var shown in display!.Items.ToList())
                if (!before.TryGetValue(shown, out int had)) Stocked(shop, shown, shown.Quantity);
                else if (shown.Quantity > had) Stocked(shop, shown, shown.Quantity - had);
        return placed;
    }

    /// <summary>
    /// A held copy lifted off the window put back (<c>returnItem</c>, <c>FUN_14070d860</c>): at its old cell when that is free, else where the window adds it (<see cref="Shown"/>), the
    /// stock following.
    /// </summary>
    internal void ShowAgain(Inventory window, ItemInstance item, int x, int y)
    {
        if (ReferenceEquals(window, display)) Shown(item, item.Quantity, x, y, stock: true);
    }

    /// <summary>
    /// Units of the window's item taken onto the mouse (or swapped onto it) leave the stock at once, and units added to the window from the mouse put a copy into it: the window's
    /// remove and add callbacks (<c>ShopTraderInventory::_sectionRemoveItemCallback</c>, <c>_sectionAddItemCallback</c>, called by the lift <c>FUN_1407139a0</c>, <b>Verified</b>).
    /// </summary>
    internal void FromStock(ItemInstance item, int units)
    {
        if (displayOf is { } shop) Unstocked(shop, item, units);
    }

    internal void ToStock(ItemInstance item, int units)
    {
        if (displayOf is { } shop) Stocked(shop, item, units);
    }

    /// <summary>
    /// The window's arranging (<c>FUN_140749d30</c>, <b>Verified</b>): its items from their position order (by x, then y), longer side first (<c>FUN_1407468a0</c>), each added again as
    /// <see cref="Shown"/> adds one; as that grows the window for any item, the other keys are never tried. The sort is the original's (<see cref="ArrangeSort"/>), which is not stable.
    /// </summary>
    void Arrange() => InventoryCommands.Arrange(display!, "main", item =>
    {
        // A stack that joins another whole has 0 left after the add: its units count, not what it holds after.
        int units = item.Quantity;
        return Shown(item, units) == units;
    });

    /// <summary>
    /// A sale into the window: a copy of the units goes into the stock (the original's into the first container that takes it, <c>FUN_1409529e0</c>); units that fit nowhere are lost to
    /// the stock, though the window shows them until it is made again, as in the original. Engine choice: the copy goes in as <see cref="Place"/> puts it, onto a stack in any container first.
    /// </summary>
    void Stocked(Shop shop, ItemInstance sold, int units)
    {
        var copy = New(sold);
        copy.Quantity = units;
        Owned(copy, shop);
        // Into the first source that takes it (FUN_1409529e0); engine choice: the stock first.
        int left = units;
        foreach (var source in SourcesOf(shop))
            if (left > 0) left -= Place(source, copy, left);
    }

    /// <summary>A new item like <paramref name="item"/> (<see cref="ItemInstance.Clone"/>) in its record's colour, as a new item takes it.</summary>
    ItemInstance New(ItemInstance item)
    {
        var copy = item.Clone();
        copy.ColourId = Meitou.Data.Gameplay.ItemColours.Of(data.Db, data.Db.Find(item.Record))?.StringId ?? "";
        return copy;
    }

    /// <summary>
    /// A buy from the window: the units come off the stock's items the same as the one shown, in the stock's order (<c>FUN_140952a70</c>, <b>Verified</b>: of the same record, maker and
    /// material, <c>FUN_14075d510</c>; a bag goes whole, its contents with it), the stock before the window's other sources (engine choice). A stack that keeps some units is full again
    /// (the removal <c>removeItemDontDestroy_returnsItem</c>, <c>FUN_14074a950</c>, <b>Verified</b>).
    /// </summary>
    void Unstocked(Shop shop, ItemInstance shown, int units)
    {
        foreach (var source in SourcesOf(shop))
            if (units > 0) units = Unstocked(source, shown, units);
    }

    int Unstocked(Inventory source, ItemInstance shown, int units)
    {
        var from = source.Items.Where(i => SameAs(i, shown)).ToList();
        foreach (var item in from)
        {
            if (units <= 0) break;
            int n = Math.Min(units, item.Quantity);
            if (n < item.Quantity) Refilled(item);
            Take(source, item, n);
            units -= n;
        }
        return units;
    }

    static bool SameAs(ItemInstance a, ItemInstance b) => a.Record == b.Record && a.CompanyId == b.CompanyId && a.MaterialId == b.MaterialId;

    // ------------------------------------------------------------------ the held item (PlayerSystem, Items/HeldItems.cs)

    /// <summary>The open trade's window (<see cref="DisplayOf"/>, the copy of its stock) and its customer while the trade's windows show; null when no trade is open.</summary>
    internal Inventory? OpenStock(World world, out CharacterId buyer)
    {
        buyer = CharacterId.None;
        if (!open || !Resolve(world, out _, out _, out var shop)) return null;
        buyer = customer;
        return DisplayOf(world, shop!);
    }

    /// <summary>
    /// How many units of a held item a drop takes at most for what the payer has (<c>getMaxAffordableNum</c>, <c>FUN_14075d5c0</c>, under <c>FUN_140715560</c>, <b>Verified</b>): the payer's
    /// money over one unit's plain value in the drop's direction (no buyback, no partly used unit), all of them when a unit is worth less than 1. The price of what is placed is checked
    /// after (<see cref="PayHeld"/>).
    /// </summary>
    internal int HeldCap(World world, ItemInstance item, bool sell)
    {
        if (!Resolve(world, out int c, out int t, out var shop)) return 0;
        var squad = world.Squads.Find(world.Characters.Cold(t)!.SquadId)!;
        long value = Market(world).UnitPrice(item, sell, shop!.Terms, sell ? world.Characters.Cold(c)!.Faction : squad.Faction);
        return value < 1 ? item.Quantity : (int)Math.Min(item.Quantity, (sell ? squad.Money : world.Player.Money) / value);
    }

    /// <summary>
    /// The checks of a sale to the shop before its price (<c>FUN_140715560</c>, <c>RClickAutoTrade</c>, <b>Verified</b>): a stolen item put on a shop that is no fence gets the fence roll (8.4;
    /// caught, "Got caught selling stolen item." and the pending Fencing crime), then the smuggling check; either refusal closes the trade (<c>closeAllInventories</c>). True when refused.
    /// <paramref name="unit"/> keys the roll of each unit of a right-click run.
    /// </summary>
    internal bool CaughtSelling(World world, ItemInstance item, int unit = 0)
    {
        if (!Resolve(world, out int c, out int t, out var shop)) return false;
        var squad = world.Squads.Find(world.Characters.Cold(t)!.SquadId)!;
        if (item.IsStolenFor(world.Characters.Cold(c)!.Faction, data) && Fences(item, squad, shop!.Terms) && Caught(world, c, t, item, squad, shop.Terms, unit))
        {
            world.Post("Got caught selling stolen item.");
            if (Crimes is { } crimes && crimes.SetPending(world, c, Crime.CrimeCode.Fencing, squad.Faction, trader, Crime.CrimeSystem.MinTimer, viaWrapper: true))
                crimes.Book(world, c, squad.Faction, trader);
        }
        else if (!Smuggling(world, c, item, shop!.Terms)) return false;
        Close(world);
        return true;
    }

    /// <summary>
    /// The smuggling check of a sale (<c>FUN_140794e80</c>, <b>Observed</b>, run after the fence check): an item the culture of the trader's town's faction lists as illegal is refused by a trader that
    /// does not buy illegal goods, "Attempted smuggling!" (code 0xf), and the trader says THIEF_CAUGHT_STEALING_FROM_ME (8) at the seller unless it buys stolen goods. True when refused.
    /// </summary>
    bool Smuggling(World world, int seller, ItemInstance item, Trader terms)
    {
        if (terms.BuysIllegal || (uint)terms.Site >= (uint)data.SiteFactions.Count || Market(world).FactionCulture(data.SiteFactions[terms.Site]) is not { } culture || !culture.Illegal.Contains(item.Record)) return false;
        world.Post("Attempted smuggling!");
        if (!terms.BuysStolen) world.Defer(new Dialogue.DialogueEvent(trader, world.Characters.IdOf(seller), 8) { Tick = world.Tick + 1 });
        return true;
    }

    /// <summary>
    /// Pays for the first <paramref name="units"/> of a held item that changed hands (priced before it moves, <see cref="Price"/>): the price goes from the buyer's purse to the seller's.
    /// A payer short of the price: "You can't afford that." or "The shopkeeper can't afford that." (<c>FUN_140715560</c>), false. The caller marks the moved units traded (<see cref="Traded"/>)
    /// and keeps the stock in step (<see cref="FromStock"/>, <see cref="ToStock"/>).
    /// </summary>
    internal bool PayHeld(World world, ItemInstance item, int units, bool sell)
    {
        if (units <= 0 || !Resolve(world, out int c, out int t, out var shop)) return false;
        var cold = world.Characters.Cold(c)!;
        var squad = world.Squads.Find(world.Characters.Cold(t)!.SquadId)!;
        var m = Market(world);
        int holder = sell ? cold.Faction : squad.Faction;
        long price = Price(m, item, units, sell, shop!.Terms, holder);
        if ((sell ? squad.Money : world.Player.Money) < price)
        {
            world.Post(sell ? "The shopkeeper can't afford that." : "You can't afford that.");
            return false;
        }
        if (sell) { squad.Money -= price; world.Player.Money += price; }
        else { world.Player.Money -= price; squad.Money += price; }
        world.Post($"{cold.Name} {(sell ? "sold" : "bought")} {Units(units, item.Name)} for c.{price}");
        return true;
    }

    /// <summary>Units that changed hands in this trade go into its buyback table (<see cref="ItemInstance.ToggleTraded"/>).</summary>
    internal void Traded(ItemInstance moved) => moved.ToggleTraded(session);

    /// <summary>
    /// A buy command: one unit, and a stack one unit at a time until one fails, each priced and placed on its own (<c>RClickAutoTrade</c>, <c>RClickAutoTradeAll</c>, <b>Verified</b>);
    /// only the first failure is told.
    /// </summary>
    void Buy(World world, BuyItem buy)
    {
        if (!Resolve(world, out int c, out int t, out var shop)) return;
        var cold = world.Characters.Cold(c)!;
        var squad = world.Squads.Find(world.Characters.Cold(t)!.SquadId)!;
        if (Find(DisplayOf(world, shop!), buy.Item) is not { } item) return;
        int want = Math.Clamp(buy.Quantity, 1, item.Quantity), moved = 0, bagged = 0;
        long paid = 0;
        for (int k = 0; k < want && display!.Items.Contains(item); k++)
        {
            var (price, refusal, inBag) = BuyUnit(buy, cold, squad, shop!, item, world);
            // With the bag window open a Shift run goes on into the character window and tells why that pass's first unit failed, so nothing when the bag and the character
            // window both took units (rightClickAutoEquipping, Verified).
            if (refusal is not null) { if (moved == 0 || buy.BagFirst && want > 1 && (bagged == 0 || bagged == moved)) world.Post(refusal); break; }
            (moved, paid) = (moved + 1, paid + price);
            if (inBag) bagged++;
        }
        if (moved > 0) world.Post($"{cold.Name} bought {Units(moved, item.Name)} for c.{paid}");
    }

    (long Price, string? Refusal, bool InBag) BuyUnit(BuyItem buy, CharacterCold cold, Squad squad, Shop shop, ItemInstance item, World world)
    {
        var shown = display!;
        bool bag = item.Contents is not null;
        var (bought, whole) = Split(item, 1);
        long price = Price(Market(world), bought, 1, false, shop.Terms, squad.Faction, mixed: false);
        var moving = bag ? item : bought;
        string? refusal = world.Player.Money < price ? "You can't afford that." : null;
        bool inBag = false;
        if (refusal is null)
        {
            moving.ToggleTraded(session);
            bool placed;
            if (bag) placed = TransferContainer(shown, buy.Item, cold.Inventory!, buy.Equip, cold);
            else if (buy.BagFirst && cold.Inventory!.Backpack?.Contents is { } worn && worn.AddTo("backpack_content", bought, cold.Inventory.Holder)) placed = inBag = true;
            else
            {
                // The sections of the item's slot type first (one the buyer's race may not wear: "Can't wear that item.", each), then the main grid, not the bag (RClickAutoTrade).
                if (buy.Equip && !bought.WearableBy(cold.Race?.StringId, SquadRegistry.IsAnimal(cold)))
                    foreach (var _ in InventoryCommands.SlotSections(bought)) world.Post("Can't wear that item.");
                placed = buy.Equip && InventoryCommands.TryEquip(cold.Inventory!, bought, cold) || cold.Inventory!.AddTo("main", bought);
            }
            if (!placed)
            {
                moving.ToggleTraded(session);
                refusal = "No room for that item.";
            }
        }
        if (refusal is not null)
        {
            // A whole item left the window and the stock at once, and back on its cell it puts a copy into the stock (_sectionAddItemCallback); a split-off unit is gone, its traded
            // count with it.
            if (whole) { Unstocked(shop, item, item.Quantity); Stocked(shop, item, item.Quantity); }
            else item.Quantity++;
            return (0, refusal, false);
        }
        if (whole && !bag) Take(shown, item, 1);
        if (!whole) Refilled(item);
        Unstocked(shop, item, 1);
        if (InventoryCommands.IsEquipmentSection((bag ? item : bought).Section)) assembly.RefreshEquipment(cold);
        world.Player.Money -= price;
        squad.Money += price;
        return (price, null, inBag);
    }

    /// <summary>
    /// A sell command: one unit, and a stack one unit at a time, each with its own fence and smuggling checks, as <see cref="Buy"/>. The checks come before the price, so a shop that
    /// can't pay still rolls (<c>RClickAutoTrade</c>, <b>Verified</b>).
    /// </summary>
    void Sell(World world, SellItem sell)
    {
        if (!Resolve(world, out int c, out int t, out var shop)) return;
        var cold = world.Characters.Cold(c)!;
        var squad = world.Squads.Find(world.Characters.Cold(t)!.SquadId)!;
        if (Find(cold.Inventory!, sell.Item) is not { } item || !InventoryCommands.IsMovableSection(item.Section)) return;
        if (item.LockedOn) { world.Post("This item is locked in place and cannot be removed."); return; }
        int want = Math.Clamp(sell.Quantity, 1, item.Quantity), moved = 0;
        long paid = 0;
        for (int k = 0; k < want && item.Quantity > 0 && cold.Inventory!.All().Contains(item); k++)
        {
            var (sold, price, refusal) = SellUnit(cold, squad, shop!, item, sell.Item, k, world);
            if (!sold) { if (moved == 0 && refusal is not null) world.Post(refusal); break; }
            (moved, paid) = (moved + 1, paid + price);
            if (item.Contents is not null) break;   // a bag goes whole
        }
        if (moved > 0) world.Post($"{cold.Name} sold {Units(moved, item.Name)} for c.{paid}");
    }

    /// <summary>One unit of a sale; not sold: the refusal to tell (null: told already).</summary>
    (bool Sold, long Price, string? Refusal) SellUnit(CharacterCold cold, Squad squad, Shop shop, ItemInstance item, InventoryItemToken token, int unit, World world)
    {
        if (CaughtSelling(world, item, unit)) return (false, 0, null);
        bool bag = item.Contents is not null, equipped = InventoryCommands.IsEquipmentSection(item.Section);
        var (sold, whole) = Split(item, 1);
        long price = Price(Market(world), sold, 1, true, shop.Terms, cold.Faction, mixed: false);
        var moving = bag ? item : sold;
        string? refusal = squad.Money < price ? "The shopkeeper can't afford that." : null;
        if (refusal is null)
        {
            moving.ToggleTraded(session);
            if (!bag) Owned(sold, shop);
            // Into the window, as it adds an item, growing it (FUN_140954ba0 under the auto-trade add, Observed; a bag moves itself), each add putting a copy into the stock.
            DisplayOf(world, shop);
            bool placed = !bag ? Shown(sold, 1, stock: true) == 1 : TransferContainer(cold.Inventory!, token, display!, false, null);
            if (!placed && bag)
            {
                display!.Height += 2 * item.Height;
                placed = TransferContainer(cold.Inventory!, token, display, false, null);
            }
            if (!placed)
            {
                moving.ToggleTraded(session);
                refusal = "No room for that item.";
            }
        }
        if (refusal is not null)
        {
            if (!whole) item.Quantity++;   // the split-off unit is gone, its traded count with it
            return (false, 0, refusal);
        }
        if (bag) { Owned(item, shop); Stocked(shop, item, 1); }
        else if (whole) Take(cold.Inventory!, item, 1);
        else Refilled(item);
        if (equipped) assembly.RefreshEquipment(cold);
        squad.Money -= price;
        world.Player.Money += price;
        return (true, price, null);
    }

    /// <summary>
    /// The chance a shop that is no fence lets a stolen item through (8.4, <c>FUN_140793490</c>): lerp(thievery / 100, 0.4, 1.25) of the seller, halved when the item was stolen from the
    /// shop's faction and halved again when it was stolen from the shop's own squad; 1 or more never gets caught. The roll is the seller's and the tick's.
    /// </summary>
    public static float FenceChance(float thievery, bool stolenFromShopFaction, bool stolenFromShopSquad = false)
    {
        float p = 0.4f + (1.25f - 0.4f) * (thievery / 100f);
        if (stolenFromShopFaction) p *= 0.5f;
        return stolenFromShopSquad ? p * 0.5f : p;
    }

    /// <summary>
    /// Whether the shop's squad asks about and rolls on a stolen good at all (<c>FUN_140792830</c>, <b>Observed</b>): always, unless it buys stolen goods; a squad that does only for goods
    /// stolen from one of its own members (the item's owner handle names the squad's platoon).
    /// </summary>
    static bool Fences(ItemInstance item, Squad shopSquad, Trader terms) => !terms.BuysStolen || item.OwnerSquad >= 0 && item.OwnerSquad == shopSquad.Id;

    /// <summary>The odds <c>FUN_140793490</c> answers for goods nobody looks for (<c>DAT_14169bb44</c>, <b>Verified</b>: 1.1).</summary>
    const float NeverCaught = 1.1f;

    /// <summary>The faction the shop's squad sits in regards another as its own (the game's <c>FUN_1406b2630</c>: the same faction, or a relation of 50 or more).</summary>
    bool Allied(int a, int b) => a == b || data.Relations.Contains(a) && data.Relations.Contains(b) && data.Relations.IsAlly(a, b);

    /// <summary>
    /// The chance the trader (slot <paramref name="trader"/>) lets the seller's stolen item through (<c>FUN_140793490</c>, <b>Observed</b>). With the robbed character known
    /// (<see cref="ItemInstance.Owner"/>): 1 when it is gone or of the seller's own squad, and its faction now is the one tested. Of another faction than the shop's: 1 for a shop that buys stolen
    /// goods (<see cref="Fences"/>) and for a bandit or adventurer faction; 1.1 (never caught) when the shop's faction is not allied to it and the owner's squad's home town is not the town the trader
    /// stands in (an item with no known owner has no home town). Else <see cref="FenceChance"/>, halved for the shop's faction and again for the shop's own squad.
    /// </summary>
    float FenceOdds(World world, float thievery, ItemInstance item, int seller, int trader, Squad shopSquad, Trader terms)
    {
        var table = world.Characters;
        int from = item.StolenFrom;
        CampaignTown? home = null;
        if (!item.Owner.IsNone)
        {
            if (!table.TryResolveNext(item.Owner, out int o) || table.Cold(o) is not { } owner) return 1;
            if (item.OwnerSquad == table.Cold(seller)!.SquadId) return 1;
            from = owner.Faction;
            home = world.Squads.Find(owner.SquadId) is { Town: >= 0 } squad ? CampaignTown.OfSite(squad.Town) : CampaignTown.None;
        }
        int shopFaction = shopSquad.Faction;
        if (from != shopFaction && from >= 0)
        {
            if (!Fences(item, shopSquad, terms) || data.Relations.Factions[from].FundamentalType is FundamentalType.Bandit or FundamentalType.Adventurer) return 1;
            if (!Allied(shopFaction, from) && home != Ai.AiSystem.TownAt(data, world, table.Next[trader].Position)) return NeverCaught;
        }
        return FenceChance(thievery, from == shopFaction, item.OwnerSquad >= 0 && item.OwnerSquad == shopSquad.Id);
    }

    /// <summary>The fence roll of one sale; <paramref name="unit"/> keys the roll of each unit of a stack sold a unit at a time within one tick.</summary>
    bool Caught(World world, int seller, int trader, ItemInstance item, Squad shopSquad, Trader terms, int unit = 0)
    {
        var cold = world.Characters.Cold(seller)!;
        float p = FenceOdds(world, cold.Stats?[StatsEnumerated.Thieving] ?? 0, item, seller, trader, shopSquad, terms);
        bool caught = p < 1 && Rng.Float(Rng.Hash(world.Seed, Rng.Key(world.Characters.IdOf(seller)), RngPurpose.Trade, (ulong)world.Tick ^ ((ulong)unit << 40))) >= p;
        // The seller trains thievery whenever the odds are below 0.98 (<c>FUN_140793770</c>): 5 x 2 for a fence that works, 20 x 2 for one that is caught.
        if (p < Crime.CrimeSystem.FenceXpBelow) Crimes?.TrainThievery(world, seller, caught ? Crime.CrimeSystem.FenceCaughtXp : Crime.CrimeSystem.FenceXp);
        return caught;
    }

    // Containers move whole: stack copies intentionally omit Contents. Transfer validates the destination before changing either inventory.
    static bool TransferContainer(Inventory from, InventoryItemToken token, Inventory into, bool equip, CharacterCold? wearer)
    {
        if (equip && InventoryCommands.Transfer(from, token, into, "backpack_attach", 0, 0, wearer: wearer)) return true;
        var item = Find(from, token);
        return item is not null && into.FindCell("main", item.Width, item.Height, out int cx, out int cy) && InventoryCommands.Transfer(from, token, into, "main", cx, cy);
    }

    /// <summary>
    /// The price of the first <paramref name="units"/> of <paramref name="item"/>, as split off it (their traded count: all of the item's for the whole of it, else at most
    /// <paramref name="units"/> of it, <see cref="ItemInstance.MoveTraded"/>): with no count, the stack's price; with one, the stack's price in the opposite direction, but
    /// (<paramref name="mixed"/>: the drop <c>FUN_140715560</c>, not the right click <c>RClickAutoTrade</c>, <b>Verified</b>) a count below the units prices the rest at this
    /// direction's unit value and the counted ones at the opposite's, less the opposite value's shortfall of the partly used unit.
    /// </summary>
    long Price(Market market, ItemInstance item, int units, bool sell, Trader terms, int holder, bool mixed = true)
    {
        bool whole = units >= item.Quantity;
        var stack = whole ? item : item.WithQuantity(units);
        int traded = whole ? item.Traded : Math.Min(item.Traded, units);
        if (traded == 0 || !mixed || traded >= units) return market.StackPrice(stack, traded == 0 ? sell : !sell, terms, holder);
        long normal = market.UnitPrice(stack, sell, terms, holder), opposite = market.UnitPrice(stack, !sell, terms, holder);
        // In the original's float order: multiplied by the charges first, divided by the full charge after.
        int shortfall = stack.MaxCharges > 0 ? (int)((float)opposite - (float)opposite * stack.Charges / stack.MaxCharges) : 0;
        return normal * (units - traded) + traded * opposite - shortfall;
    }

    /// <summary>Puts the units at a cell of the grid when it is free and inside it (the window's <c>_addItem</c> at an item's old cell), otherwise wherever they fit (<see cref="Place"/>).</summary>
    static int PlaceAt(Inventory grid, ItemInstance copy, int units, int x, int y)
    {
        if (x >= 0 && y >= 0 && x + copy.Width <= grid.Width && y + copy.Height <= grid.Height && grid.IsFree("main", x, y, copy.Width, copy.Height))
        {
            (copy.Section, copy.X, copy.Y) = ("main", x, y);
            grid.Items.Add(copy);
            return units;
        }
        return Place(grid, copy, units);
    }

    internal static int Place(Inventory into, ItemInstance copy, int units)
    {
        copy.Section = "main";
        return into.Add(copy) ? units : units - copy.Quantity;
    }

    /// <summary>Takes <paramref name="units"/> off an item's stack, removing it when none are left.</summary>
    internal static void Take(Inventory from, ItemInstance item, int units)
    {
        if (units >= item.Quantity) from.Remove(item);
        else item.Quantity -= units;
    }

    static string Units(int n, string name) => n == 1 ? name : $"{n} x {name}";

    /// <summary>
    /// The units a right-click trade moves (<c>RClickAutoTrade</c>, <c>FUN_140714a20</c>, <b>Verified</b>), taken before it is priced: the whole item moves itself, its lock and damage with
    /// it; part of a stack is a split-off unit (<c>copyItem</c>) that keeps the stack's partly used charge (<see cref="Refilled"/>) and takes its traded count first.
    /// </summary>
    static (ItemInstance Part, bool Whole) Split(ItemInstance item, int units)
    {
        if (units >= item.Quantity) return (item.Moved(), true);
        var part = item.WithQuantity(units);
        ItemInstance.MoveTraded(item, part, units);
        item.Quantity -= units;
        return (part, false);
    }

    /// <summary>The stack left behind by a right-click trade of part of it is full again (<c>Item::resetCharges</c>, <c>FUN_14075d570</c>, <b>Verified</b>).</summary>
    static void Refilled(ItemInstance rest)
    {
        if (rest.Quantity > 0 && rest.MaxCharges > 0) rest.Charges = rest.MaxCharges;
    }

    /// <summary>
    /// An item stolen from the shop's faction is the shop's own again where it goes in through an inventory's add, which clears an owner of the receiving inventory's faction
    /// (<c>InventorySection::addItem</c>, <c>FUN_14074c8b0</c>, <b>Verified</b>): a right-click sale's item in the window and every copy into the stock, not an item dropped on the
    /// window (<c>placeItemFromMouse</c> adds with <c>_addItem</c>, which keeps it).
    /// </summary>
    static void Owned(ItemInstance item, Shop shop)
    {
        if (item.StolenFrom >= 0 && item.StolenFrom == shop.Terms.Faction) item.Mark = StolenMark.None;
    }

    TradeView Publish(World world, int c, int t, Shop shop)
    {
        var m = Market(world);
        var cold = world.Characters.Cold(c)!;
        var squad = world.Squads.Find(world.Characters.Cold(t)!.SquadId)!;
        float thievery = cold.Stats?[StatsEnumerated.Thieving] ?? 0;
        TradeItemView View(ItemInstance item, int index, bool sell, int holder)
        {
            // The tooltip's Value and Sell value are one unit's plain prices, a buyback or not (FUN_14079e7a0).
            long value = m.UnitPrice(item, false, shop.Terms, holder), sellValue = m.UnitPrice(item, true, shop.Terms, holder);
            bool buyback = item.Traded != 0;
            bool stolen = item.IsStolenFor(holder, data);
            // The roll's own odds feed the hover box, the sale prompt and the roll alike; only where the shop would roll on the item at all.
            float? odds = sell && stolen && Fences(item, squad, shop.Terms) ? FenceOdds(world, thievery, item, c, t, squad, shop.Terms) : null;
            return new(index, item.Record, item.Name, item.Section, item.X, item.Y, item.Width, item.Height, item.Quantity,
                item.Quality, Price(m, item, item.Quantity, sell, shop.Terms, holder), Price(m, item, 1, sell, shop.Terms, holder), stolen)
            {
                Buyback = buyback, BuybackUnits = item.Traded, Container = item.Contents is not null,
                Material = item.MaterialId, Colour = item.ColourId, Charge = item.ChargeFraction, Value = value, SellValue = sellValue, Weight = item.Weight, PriceFactor = m.TraderFactor(item, shop.Terms, holder),
                MarketFactor = m.MarketFactor(shop.Terms.Site, item.Record), Company = item.CompanyId, Level = item.Level, Charges = item.Charges, MaxCharges = item.MaxCharges, StolenFrom = stolen ? item.StolenFrom : -1, Unique = item.Unique,
                FenceChance = odds is { } shown ? Math.Clamp(shown, 0, 1) : null,
                FenceOdds = odds is < 1 ? Math.Max(odds.Value, 0) : null,
                Illegal = sell && (uint)shop.Terms.Site < (uint)data.SiteFactions.Count && m.FactionCulture(data.SiteFactions[shop.Terms.Site]) is { } culture && culture.Illegal.Contains(item.Record),
            };
        }
        // Only the window's own items, at their All() indices (which tokens use): a bag's contents are not loose stock.
        var shown = DisplayOf(world, shop);
        var stock = new List<TradeItemView>(shown.Items.Count);
        int at = 0;
        foreach (var item in shown.Items)
        {
            stock.Add(View(item, at, false, squad.Faction));
            at += 1 + (item.Contents?.All().Count() ?? 0);
        }
        var goods = cold.Inventory!.All().Select((item, index) => (item, index)).Where(p => InventoryCommands.IsMovableSection(p.item.Section))
            .Select(p => View(p.item, p.index, true, cold.Faction)).ToArray();
        var held = world.Player.Held switch
        {
            { From.Holder: ItemHolder.Stock } h => View(h.Item, -1, false, squad.Faction),
            { From.Holder: ItemHolder.Character } h when h.From.Character == customer => View(h.Item, -1, true, cold.Faction),
            _ => null,
        };
        return new TradeView(customer, trader, world.Characters.Cold(t)!.Name, squad.Money, world.Player.Money, shown.Width, shown.Height, stock, goods, shop.Terms.BuysStolen, shop.Terms.BuysIllegal) { Held = held };
    }
}
