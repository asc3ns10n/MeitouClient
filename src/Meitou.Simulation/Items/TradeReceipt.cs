namespace Meitou.Simulation.Items;

/// <summary>An open trade, the lifetime of its buyback table (<see cref="ItemInstance.Traded"/>): closing it voids every count, moved items' too. Never saved.</summary>
internal sealed class TradeSession
{
    public bool Active { get; set; } = true;
}
