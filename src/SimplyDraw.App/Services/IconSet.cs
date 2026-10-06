namespace SimplyDraw.Services;

public static class IconSet
{
    private static readonly Dictionary<string, Geometry> Cache = new(StringComparer.Ordinal)
    {
        ["pen"] = Geo("M5,19 L13,11 M13,11 L18,6 M16.4,4.4 L19.8,7.8"),
        ["eraser"] = Geo("M8,17 L14,7 L19,11 L13,21 Z M8,17 L6,19"),
        ["select"] = Geo("M6,4 L6,18 L10,14 L13,20 L15.2,19 L12.2,13 L18,13"),
        ["hand"] = Geo("M8,12 V7 M12,12 V6 M16,12 V8 M8,12 C6,12 5,14 5,16 V18 H18 V14 C18,12.5 17,12 16,12"),
        ["line"] = Geo("M5,19 L19,5"),
        ["rect"] = Geo("M6,6 H18 V18 H6 Z"),
        ["ellipse"] = Geo("M19,12 A7,7 0 1 0 5,12 A7,7 0 1 0 19,12"),
        ["arrow"] = Geo("M5,17 L18,6 M18,6 L12,6 M18,6 L18,12"),
        ["text"] = Geo("M6,6 H18 M12,6 V19"),
        ["pick"] = Geo("M9,16 L15,7 L18,10 L12,19 Z"),
        ["undo"] = Geo("M8,8 H14 A5,5 0 1 1 14,18 H11 M8,8 L11,5 M8,8 L11,11"),
        ["redo"] = Geo("M16,8 H10 A5,5 0 1 0 10,18 H13 M16,8 L13,5 M16,8 L13,11"),
        ["new"] = Geo("M7,4 H14 L18,8 V20 H7 Z M14,4 V8 H18"),
        ["open"] = Geo("M4,8 H10 L12,6 H20 V18 H4 Z"),
        ["save"] = Geo("M5,4 H16 L20,8 V20 H5 Z M8,4 V9 H15 V4 M8,20 V14 H16 V20"),
        ["export"] = Geo("M12,4 V14 M8,10 L12,14 L16,10 M5,18 H19"),
        ["image"] = Geo("M4,6 H20 V18 H4 Z M4,15 L9,11 L13,14 L16,12 L20,15"),
        ["plus"] = Geo("M12,5 V19 M5,12 H19"),
        ["trash"] = Geo("M6,8 H18 M9,8 V6 H15 V8 M8,8 L9,19 H15 L16,8"),
        ["eye"] = Geo("M3,12 C6,7 18,7 21,12 C18,17 6,17 3,12 M12,12 A2,2 0 1 0 12.1,12"),
        ["lock"] = Geo("M8,11 V8 A4,4 0 0 1 16,8 V11 M7,11 H17 V19 H7 Z"),
        ["up"] = Geo("M6,14 L12,8 L18,14"),
        ["down"] = Geo("M6,10 L12,16 L18,10"),
        ["copy"] = Geo("M8,8 H18 V18 H8 Z M6,16 V6 H16"),
        ["fit"] = Geo("M8,4 H4 V8 M16,4 H20 V8 M8,20 H4 V16 M16,20 H20 V16"),
        ["grid"] = Geo("M5,5 H19 V19 H5 Z M5,10 H19 M5,15 H19 M10,5 V19 M15,5 V19"),
        ["close"] = Geo("M7,7 L17,17 M17,7 L7,17")
    };

    public static Geometry Get(string key) => Cache.TryGetValue(key, out var geometry) ? geometry : Cache["pen"];

    private static Geometry Geo(string data)
    {
        var geometry = Geometry.Parse(data);
        geometry.Freeze();
        return geometry;
    }
}
