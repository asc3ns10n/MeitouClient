using System.Numerics;
using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Data.World;
using Meitou.Simulation;
using Meitou.Simulation.Locks;
using SimWorld = Meitou.Simulation.World;

namespace Meitou.Tests.Simulation;

/// <summary>The building filler (docs/game/economy.md "Building shops") on the synthetic town's one building, whose resident template has no characters.</summary>
public class BuildingFillTests
{
    static readonly Vector3 StoreAt = SyntheticTown.Centre + new Vector3(0, 0, 100);
    static readonly ZoneCoordinate StoreZone = WorldLayout.ZoneOf(StoreAt.X, StoreAt.Z);
    static readonly string StoreKey = BuildingKey.Of(StoreZone, "store");

    /// <summary>
    /// Testville with a store whose template has no members and sells gems (5000) and junk (5) from two lists, into a strongly locked chest, a weakly locked one and a weapon rack.
    /// <paramref name="specialsOnly"/>: the template sells nothing and has one idol among its <c>special items</c>. <paramref name="keepers"/>: the template makes a squad of three (no
    /// trader) with a <c>vendors refresh time</c> of 0.01 h.
    /// </summary>
    static PopulationData Data(bool specialsOnly = false, bool keepers = false)
    {
        FcsRecord Chest(string id, int bonus, int limit)
        {
            var c = SyntheticTown.Rec(id, FcsRecordType.BUILDING, id);
            c.Bools["has lock"] = true;
            c.Bools["has inventory"] = true;
            c.Ints["lock level bonus"] = bonus;
            c.Ints["itemtype limit"] = limit;
            return c;
        }
        var house = SyntheticTown.Rec("house-t", FcsRecordType.BUILDING, "House");
        var gem = SyntheticTown.Rec("gem-t", FcsRecordType.ITEM, "Gem");
        gem.Ints["value"] = 5000;
        gem.Ints["stackable"] = 10;
        var junk = SyntheticTown.Rec("junk-t", FcsRecordType.ITEM, "Junk");
        junk.Ints["value"] = 5;
        junk.Ints["stackable"] = 10;
        var gems = SyntheticTown.Rec("vg-t", FcsRecordType.VENDOR_LIST, "Gems");
        gems.References["items"] = [SyntheticTown.Ref("gem-t", 100)];
        var junks = SyntheticTown.Rec("vj-t", FcsRecordType.VENDOR_LIST, "Junk");
        junks.References["items"] = [SyntheticTown.Ref("junk-t", 100)];
        var store = SyntheticTown.Rec("store-t", FcsRecordType.SQUAD_TEMPLATE, "Store");
        if (specialsOnly) store.References["special items"] = [SyntheticTown.Ref("idol-t", 1)];
        else store.References["vendors"] = [SyntheticTown.Ref("vg-t", 100), SyntheticTown.Ref("vj-t", 100)];
        store.Ints["vendors fill total amount"] = 4;
        if (keepers)
        {
            store.References["leader"] = [SyntheticTown.Ref("2-t", 1)];
            store.References["squad"] = [SyntheticTown.Ref("2-t", 2)];
            store.Floats["vendors refresh time"] = 0.01f;
        }
        var idol = SyntheticTown.Rec("idol-t", FcsRecordType.ITEM, "Idol");
        idol.Ints["value"] = 100;
        var db = SyntheticTown.Database(extra: [Chest("strong-t", 50, 4), Chest("weak-t", -60, 4), Chest("rack-t", 0, 2), house, gem, junk, gems, junks, store, idol],
            changeTown: t => t.References["residents"] = [SyntheticTown.Ref("store-t", 1)]);
        TownFixture Box(int i, string record) => new($"{StoreKey}/{i}", record, false, StoreAt, 0) { Function = TownBuildings.GeneralStorageFunction, StorageWidth = 4, StorageHeight = 4 };
        var home = new TownBuilding("store", "house-t", "Store", StoreAt, 0, true, false, [])
        {
            Zone = StoreZone, CanHouse = true, Furnished = true, InteriorLayout = "Store", ResidentsTemplate = "store-t",
            Fixtures = [Box(0, "weak-t"), Box(1, "strong-t"), Box(2, "rack-t")],
        };
        return PopulationData.Create(db, [new TownPlacement("inst", "30-t", SyntheticTown.Centre, null)], buildings: new Dictionary<string, List<TownBuilding>> { ["inst"] = [home] });
    }

    static SimWorld World(PopulationData data)
    {
        var walk = new OpenGroundWalkability(SyntheticTown.Ground);
        var world = new SimWorld(new WorldSettings { Seed = 7, PublishSnapshots = false }, walk,
            SyntheticTown.Standard(data, walk, StandardParts.Population | StandardParts.Movement | StandardParts.Trade, new PopulationSettings { CheckEveryTicks = 1, UnloadGraceSeconds = 1 }).Systems);
        world.Player.Faction = data.PlayerFaction;
        return world;
    }

    static void Visit(SimWorld world, int ticks)
    {
        world.Commands.Enqueue(new FocusCommand(SyntheticTown.Centre) { Tick = world.Tick });
        world.RunTicks(ticks);
    }

    static Fixture Box(SimWorld world, int i) => world.Fixtures.Find($"{StoreKey}/{i}")!;

    [Fact]
    public void A_building_with_no_residents_is_stocked_once_into_the_containers_that_suit_it_and_relocked()
    {
        using var world = World(Data());
        Visit(world, 2);

        Assert.Empty(world.Squads.All);
        var (weak, strong, rack) = (Box(world, 0), Box(world, 1), Box(world, 2));
        // The gems are the dearer half: they go to the chest whose lock they are worth; the rack takes weapons, which the lists do not sell.
        Assert.Contains(strong.Contents!.Items, i => i.Record == "gem-t");
        Assert.DoesNotContain(weak.Contents!.Items, i => i.Record == "gem-t");
        Assert.Empty(rack.Contents!.Items);
        Assert.Contains(weak.Contents.Items.Concat(strong.Contents.Items), i => i.Record == "junk-t");
        Assert.All(new[] { weak, strong }.Where(b => b.Contents!.Items.Count > 0), b => Assert.True(b.Lock!.Locked));
        Assert.False(rack.Lock!.Locked);

        // Emptied, then made again (no squad holds the town): its zone was populated, so nothing comes back.
        weak.Contents.Items.Clear();
        strong.Contents.Items.Clear();
        world.Commands.Enqueue(new FocusCommand(new Vector3(-90000, 0, -90000)) { Tick = world.Tick });
        world.RunTicks(300);
        Visit(world, 60);
        Assert.Empty(weak.Contents.Items);
        Assert.Empty(strong.Contents.Items);
    }

    [Fact]
    public void A_fill_with_special_items_and_no_stock_puts_them_in_and_relocks_nothing()
    {
        using var world = World(Data(specialsOnly: true));
        Visit(world, 2);

        var chests = new[] { Box(world, 0), Box(world, 1) };
        var idol = Assert.Single(chests.SelectMany(c => c.Contents!.Items));
        Assert.Equal(("idol-t", true), (idol.Record, idol.Unique));
        // The filler stops once the specials are in when it rolled no stock (FUN_14095b210): the containers stay unlocked.
        Assert.All(chests, c => Assert.False(c.Lock!.Locked));
    }

    [Fact]
    public void A_home_of_a_squad_that_is_no_trader_is_stocked_when_the_squad_is_made_and_not_on_a_timer()
    {
        using var world = World(Data(keepers: true));
        Visit(world, 30);
        var squad = world.Squads.All.Single(s => s.TemplateId == "store-t");
        var chests = new[] { Box(world, 0), Box(world, 1) };
        Assert.NotEmpty(chests.SelectMany(c => c.Contents!.Items));

        // Emptied, it stays empty long past its refresh time: the timed restock is a trader's (FUN_1405003c0).
        foreach (var c in chests) c.Contents!.Items.Clear();
        world.RunTicks(300);
        Assert.Empty(chests.SelectMany(c => c.Contents!.Items));

        // Two of its three die; the town unloads, and the squad is made afresh at the next visit, which stocks its home again.
        foreach (var m in squad.Members.Where(m => m != squad.Leader)) world.Characters.Cold(m.Slot)!.DiedTick = world.Tick;
        world.Commands.Enqueue(new FocusCommand(new Vector3(-90000, 0, -90000)) { Tick = world.Tick });
        world.RunTicks(300);
        Visit(world, 60);
        Assert.NotEqual(squad.Id, world.Squads.All.Single(s => s.TemplateId == "store-t").Id);
        Assert.NotEmpty(chests.SelectMany(c => c.Contents!.Items));
    }
}
