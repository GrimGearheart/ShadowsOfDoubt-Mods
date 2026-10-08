using System;
using System.Collections.Generic;
using System.IO;

namespace CityPlanner;

internal class PlannedDistrict
{
    public char Key;
    public string Name;
    public string Type;
    public BuildingPreset.LandValue LandValue;
    public BuildingPreset.Density Density;
}

/// <summary>What a painted gap between two tiles becomes. Higher ranks win where streets cross.</summary>
internal enum StreetKind
{
    Alley = 1,
    Back = 2,
    Main = 3,
}

/// <summary>
/// A gap between two neighbouring city tiles. Vertical gaps lie between tile (X, Y) and its east neighbour (the road
/// runs north–south); horizontal ones between tile (X, Y) and its north neighbour (the road runs east–west).
/// </summary>
internal readonly record struct Gap(bool Vertical, int X, int Y);

internal class PlannedTile
{
    public PlannedDistrict District;
    /// <summary>The game's building preset name, or null to let the game choose.</summary>
    public string Building;
    /// <summary>Which way the building faces (its front), or null to let the game choose.</summary>
    public NewBuilding.Direction? Facing;
}

/// <summary>The city plan, read from BepInEx/config/cityplan.txt (see that file for the format).</summary>
internal class Plan
{
    public int Width, Height;
    public float Population = 1f;
    public readonly Dictionary<char, PlannedDistrict> Districts = new();

    /// <summary>Interior tiles by city coordinate (x 1..Width-2 west to east, y 1..Height-2 south to north).</summary>
    public readonly Dictionary<(int x, int y), PlannedTile> Tiles = new();

    /// <summary>Painted gaps from the [Streets] section. Empty = the game lays out the streets itself.</summary>
    public readonly Dictionary<Gap, StreetKind> Streets = new();

    public static Plan Load(string path, out string error)
    {
        error = null;
        if (!File.Exists(path))
        {
            error = "no plan file at " + path;
            return null;
        }
        return Parse(File.ReadAllLines(path), out error);
    }

    /// <summary>Reads a plan from its text (a file, or one sent from the planner page). Null with an error if it's broken.</summary>
    public static Plan Parse(IEnumerable<string> lines, out string error)
    {
        error = null;
        var plan = new Plan();
        var codes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<string[]>();
        var streetLines = new List<string>();
        var section = "";
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;
            if (line.StartsWith("[") && line.EndsWith("]"))
            {
                section = line[1..^1].Trim().ToLowerInvariant();
                continue;
            }
            switch (section)
            {
                case "city":
                {
                    var (k, v) = KeyValue(line);
                    if (k.Equals("Size", StringComparison.OrdinalIgnoreCase))
                    {
                        var p = v.ToLowerInvariant().Split('x');
                        int.TryParse(p[0], out plan.Width);
                        if (p.Length > 1) int.TryParse(p[1], out plan.Height);
                    }
                    else if (k.Equals("Population", StringComparison.OrdinalIgnoreCase))
                    {
                        float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out plan.Population);
                    }
                    break;
                }
                case "districts":
                {
                    var (k, v) = KeyValue(line);
                    var p = v.Split('|');
                    if (k.Length != 1 || p.Length < 4) throw new FormatException("bad district line: " + line);
                    plan.Districts[k[0]] = new PlannedDistrict
                    {
                        Key = k[0],
                        Name = p[0].Trim(),
                        Type = p[1].Trim(),
                        LandValue = Enum.Parse<BuildingPreset.LandValue>(p[2].Trim(), true),
                        Density = Enum.Parse<BuildingPreset.Density>(p[3].Trim(), true),
                    };
                    break;
                }
                case "buildings":
                {
                    var (k, v) = KeyValue(line);
                    codes[k] = v;
                    break;
                }
                case "map":
                    rows.Add(line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries));
                    break;
                case "streets":
                    streetLines.Add(line);
                    break;
            }
        }

        if (plan.Width < 5 || plan.Height < 5)
        {
            error = "missing or bad Size";
            return null;
        }
        if (rows.Count != plan.Height - 2)
        {
            error = $"map has {rows.Count} rows, a {plan.Width}x{plan.Height} city needs {plan.Height - 2}";
            return null;
        }
        for (var r = 0; r < rows.Count; r++)
        {
            if (rows[r].Length != plan.Width - 2)
            {
                error = $"map row {r + 1} has {rows[r].Length} cells, needs {plan.Width - 2}";
                return null;
            }
            for (var c = 0; c < rows[r].Length; c++)
            {
                var cell = rows[r][c].Split(':');
                if (cell.Length != 2 || cell[0].Length != 1 || !plan.Districts.TryGetValue(cell[0][0], out var district))
                {
                    error = $"map row {r + 1}, column {c + 1}: '{rows[r][c]}' isn't District:Building with a known district";
                    return null;
                }
                // Optional facing: Code/N, /E, /S or /W
                var code = cell[1];
                NewBuilding.Direction? facing = null;
                var slash = code.IndexOf('/');
                if (slash >= 0)
                {
                    // The plan names the side the front door is on. The game's Direction names the opposite side
                    // (its own facing code gives a building with its busiest street to the west "East").
                    facing = code[(slash + 1)..].ToUpperInvariant() switch
                    {
                        "N" => NewBuilding.Direction.South,
                        "E" => NewBuilding.Direction.West,
                        "S" => NewBuilding.Direction.North,
                        "W" => NewBuilding.Direction.East,
                        _ => null,
                    };
                    if (facing == null)
                    {
                        error = $"map row {r + 1}, column {c + 1}: facing must be /N, /E, /S or /W in '{rows[r][c]}'";
                        return null;
                    }
                    code = code[..slash];
                }
                // "?" leaves the building to the game, which picks one that suits the district.
                string building = null;
                if (code != "?" && !codes.TryGetValue(code, out building))
                {
                    error = $"map row {r + 1}, column {c + 1}: unknown building code '{code}'";
                    return null;
                }
                // Row 0 is the north edge: the highest y.
                plan.Tiles[(c + 1, plan.Height - 2 - r)] = new PlannedTile { District = district, Building = building, Facing = facing };
            }
        }
        // Later lines win, so a plan can paint a whole block and then change one gap.
        foreach (var line in streetLines)
        {
            if (!plan.ParseStreetLine(line, out error))
            {
                error = $"[Streets] '{line}': {error}";
                return null;
            }
        }
        return plan;
    }

    /// <summary>
    /// A [Streets] line: rows, columns and which gaps = type. Rows and columns count from 1 at the top left of the
    /// map, like the [Map] section. Examples: "R3 C1-C7 S = main" (the south side of those cells),
    /// "R5-R7 C1-C3 edge = main" (all the way round that block), "R5-R7 C1-C3 inside = back" (the lanes inside it).
    /// </summary>
    private bool ParseStreetLine(string line, out string error)
    {
        error = null;
        var (selector, value) = KeyValue(line);
        StreetKind kind;
        switch (value.ToLowerInvariant())
        {
            case "main": case "street": case "road": kind = StreetKind.Main; break;
            case "back": case "backstreet": case "back street": kind = StreetKind.Back; break;
            case "alley": kind = StreetKind.Alley; break;
            default:
                error = "the type must be main, back or alley";
                return false;
        }
        (int, int)? rowRange = null, colRange = null;
        string side = null;
        foreach (var token in selector.Split(new[] { ' ', '	' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var t = token.ToUpperInvariant();
            if (t.StartsWith("R") && TryRange(t, 'R', out var rr)) rowRange = rr;
            else if (t.StartsWith("C") && TryRange(t, 'C', out var cc)) colRange = cc;
            else if (t is "N" or "E" or "S" or "W" or "EDGE" or "INSIDE") side = t;
            else
            {
                error = $"don't understand '{token}' (use R<row>, C<column> and N, E, S, W, edge or inside)";
                return false;
            }
        }
        if (rowRange == null || colRange == null || side == null)
        {
            error = "needs rows (R3 or R3-R5), columns (C1 or C1-C4) and a side (N, E, S, W, edge or inside)";
            return false;
        }
        var (r1, r2) = rowRange.Value;
        var (c1, c2) = colRange.Value;
        if (r1 < 1 || r2 > Height - 2 || c1 < 1 || c2 > Width - 2)
        {
            error = $"outside the map (rows 1-{Height - 2}, columns 1-{Width - 2})";
            return false;
        }
        for (var r = r1; r <= r2; r++)
        for (var c = c1; c <= c2; c++)
        {
            var sides = side switch
            {
                "EDGE" => new[] { r == r1 ? "N" : null, r == r2 ? "S" : null, c == c1 ? "W" : null, c == c2 ? "E" : null },
                "INSIDE" => new[] { r < r2 ? "S" : null, c < c2 ? "E" : null },
                _ => new[] { side },
            };
            foreach (var s in sides)
                if (s != null) Streets[GapBeside(r, c, s)] = kind;
        }
        return true;
    }

    /// <summary>The gap on one side of a map cell (row and column from 1, top left).</summary>
    public Gap GapBeside(int row, int col, string side)
    {
        int x = col, y = Height - 1 - row;
        return side switch
        {
            "N" => new Gap(false, x, y),
            "S" => new Gap(false, x, y - 1),
            "E" => new Gap(true, x, y),
            _ => new Gap(true, x - 1, y),
        };
    }

    /// <summary>"R3" or "R3-R5" or "R3-5" (rows), likewise for columns.</summary>
    private static bool TryRange(string token, char letter, out (int, int) range)
    {
        range = default;
        var parts = token.Split('-');
        if (parts.Length > 2) return false;
        if (!int.TryParse(parts[0].TrimStart(letter), out var a)) return false;
        var b = a;
        if (parts.Length == 2 && !int.TryParse(parts[1].TrimStart(letter), out b)) return false;
        range = (Math.Min(a, b), Math.Max(a, b));
        return true;
    }

    private static (string, string) KeyValue(string line)
    {
        var i = line.IndexOf('=');
        return i < 0 ? (line.Trim(), "") : (line[..i].Trim(), line[(i + 1)..].Trim());
    }

    /// <summary>The planned tile nearest to a coordinate (used to give coastline tiles a district).</summary>
    public PlannedTile Nearest(int x, int y) =>
        Tiles[(Math.Clamp(x, 1, Width - 2), Math.Clamp(y, 1, Height - 2))];
}
