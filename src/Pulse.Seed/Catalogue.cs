namespace Pulse.Seed;

public record Variant(string Sku, string Colour, decimal Price, int Stock)
{
    public string Gtin => Seed.Gtin.FromSku(Sku);
}

public record Product(string Handle, string Title, string Category, string Brand, string Material, Variant[] Variants);

// The one furniture catalogue both Shopify and AX are seeded from, so a SKU
// means the same item in both. Brands are made up.
public static class Catalogue
{
    // B2B-only items: never sold in the shop or on the webshop.
    public static readonly string[] B2BOnly = ["HOTEL90-HVI", "FLEX-SOR"];

    // Slow movers: plenty of stock, hardly any sales (for the slow-stock analysis).
    public static readonly string[] SlowMovers = ["GLOB-MES", "REOL80-HVI"];

    public static readonly Product[] Products =
    [
        new("sofa-oslo-3-pers", "Sofa Oslo 3-pers.", "Sofa", "Nordhus", "Bouclé",
            [new("OSLO3-NAT", "Natur", 12995m, 6), new("OSLO3-GRA", "Grå", 12995m, 4)]),
        new("sofa-oslo-2-pers", "Sofa Oslo 2-pers.", "Sofa", "Nordhus", "Bouclé",
            [new("OSLO2-NAT", "Natur", 9995m, 5), new("OSLO2-GRA", "Grå", 9995m, 3)]),
        new("hjoernesofa-bergen", "Hjørnesofa Bergen", "Sofa", "Fjord & Co", "Velour",
            [new("BERGEN-GRO", "Grøn", 18995m, 2), new("BERGEN-BLA", "Blå", 18995m, 3)]),
        new("kontinentalseng-aurora-160", "Kontinentalseng Aurora 160x200", "Seng", "Lund Design", "Stof og pocketfjedre",
            [new("AURORA160-GRA", "Grå", 14995m, 8), new("AURORA160-SAN", "Sand", 14995m, 5)]),
        new("kontinentalseng-aurora-180", "Kontinentalseng Aurora 180x200", "Seng", "Lund Design", "Stof og pocketfjedre",
            [new("AURORA180-GRA", "Grå", 16995m, 6), new("AURORA180-SAN", "Sand", 16995m, 4)]),
        new("boxmadras-hotel-90", "Boxmadras Hotel 90x200", "Seng", "Lund Design", "Stof og pocketfjedre",
            [new("HOTEL90-HVI", "Hvid", 4995m, 40)]),
        new("sengebord-aurora", "Sengebord Aurora", "Seng", "Lund Design", "Egfinér",
            [new("SENGEBORD-GRA", "Grå", 1495m, 20), new("SENGEBORD-SAN", "Sand", 1495m, 16)]),
        new("spisebord-eg-200", "Spisebord Eg 200 cm", "Bord", "Nordhus", "Massiv eg",
            [new("EG200-NAT", "Natur", 11995m, 4), new("EG200-SOR", "Sortbejdset", 11995m, 3)]),
        new("sofabord-rund-80", "Sofabord Rund Ø80", "Bord", "Fjord & Co", "Valnød",
            [new("RUND80-VAL", "Valnød", 3495m, 9)]),
        new("spisebordsstol-ella", "Spisebordsstol Ella", "Stol", "Nordhus", "Eg og papirsnor",
            [new("ELLA-NAT", "Natur", 1795m, 48), new("ELLA-SOR", "Sort", 1795m, 32)]),
        new("loungestol-boucle", "Loungestol Bouclé", "Stol", "Fjord & Co", "Bouclé",
            [new("LOUNGE-NAT", "Natur", 5995m, 3)]),
        new("kontorstol-flex", "Kontorstol Flex", "Stol", "Lund Design", "Mesh",
            [new("FLEX-SOR", "Sort", 3495m, 60)]),
        new("reol-modul-80", "Reol Modul 80 cm", "Opbevaring", "Lund Design", "Egfinér",
            [new("REOL80-EG", "Eg", 2995m, 14), new("REOL80-HVI", "Hvid", 2995m, 35)]),
        new("tv-bord-linje-180", "TV-bord Linje 180", "Opbevaring", "Lund Design", "Egfinér",
            [new("LINJE180-EG", "Eg", 3995m, 7)]),
        new("kommode-linje", "Kommode Linje", "Opbevaring", "Lund Design", "Egfinér",
            [new("KOMMODE-EG", "Eg", 4495m, 6), new("KOMMODE-HVI", "Hvid", 4495m, 5)]),
        new("gulvlampe-glob", "Gulvlampe Glob", "Belysning", "Fjord & Co", "Glas og messing",
            [new("GLOB-MES", "Messing", 2495m, 42)]),
        new("taeppe-uld-200x300", "Tæppe Uld 200x300", "Tekstil", "Nordhus", "Uld",
            [new("ULD200-NAT", "Natur", 4995m, 10), new("ULD200-GRA", "Grå", 4995m, 8)]),
        new("pyntepude-boucle", "Pyntepude Bouclé 50x50", "Tekstil", "Fjord & Co", "Bouclé",
            [new("PUDE-NAT", "Natur", 349m, 70), new("PUDE-GRA", "Grå", 349m, 55)]),
    ];

    public static IEnumerable<(Product Product, Variant Variant)> Items =>
        Products.SelectMany(p => p.Variants.Select(v => (p, v)));
}
