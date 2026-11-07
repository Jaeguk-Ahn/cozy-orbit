namespace CozyOrbit.Core.Game;

/// <summary>스킨 상점: 별사탕으로 사고, 가진 것만 장착한다. 무료(기본) 스킨은 처음부터 가진 것으로 친다.</summary>
public static class Shop
{
    public static bool Owns(GameState s, string skinId) =>
        Skins.Find(skinId) is { } d && (d.IsFree || s.OwnedSkins.Contains(skinId));

    /// <summary>자리에 장착된 스킨 id. 저장된 값이 모르는 id·다른 자리·안 가진 것이면 기본값.</summary>
    public static string Equipped(GameState s, SkinSlot slot) =>
        s.Equipped.TryGetValue(slot, out var id) && Skins.Find(id) is { } d && d.Slot == slot && Owns(s, id)
            ? id
            : Skins.DefaultFor(slot);

    public static SkinDef EquippedSkin(GameState s, SkinSlot slot) => Skins.Get(Equipped(s, slot));

    public static bool CanBuy(GameState s, SkinDef d) => !Owns(s, d.Id) && s.Candy.Balance >= d.Price;

    /// <summary>사면 바로 장착한다</summary>
    public static bool TryBuy(GameState s, string skinId)
    {
        var d = Skins.Get(skinId);
        if (!CanBuy(s, d)) return false;
        s.Candy.Balance -= d.Price;
        s.OwnedSkins.Add(skinId);
        s.Equipped[d.Slot] = skinId;
        return true;
    }

    public static bool Equip(GameState s, string skinId)
    {
        if (!Owns(s, skinId)) return false;
        s.Equipped[Skins.Get(skinId).Slot] = skinId;
        return true;
    }
}
