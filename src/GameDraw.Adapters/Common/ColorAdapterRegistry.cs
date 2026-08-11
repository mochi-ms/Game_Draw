using GameDraw.Adapters.FixedPalette;
using GameDraw.Adapters.HexInput;
using GameDraw.Adapters.HsvPicker;
using GameDraw.Adapters.Manual;
using GameDraw.Core.Colors;
using GameDraw.Core.Models;

namespace GameDraw.Adapters.Common;

public sealed class ColorAdapterRegistry
{
    private readonly IReadOnlyDictionary<ColorAdapterKind, IColorAdapter> _adapters;

    public ColorAdapterRegistry()
    {
        var adapters = new IColorAdapter[]
        {
            new ManualColorAdapter(),
            new HexInputColorAdapter(),
            new FixedPaletteColorAdapter(),
            new HsvPickerColorAdapter()
        };
        _adapters = adapters.ToDictionary(adapter => adapter.Kind);
    }

    public IReadOnlyCollection<IColorAdapter> All => _adapters.Values.ToArray();

    public IColorAdapter Get(ColorAdapterKind kind) => _adapters.TryGetValue(kind, out var adapter)
        ? adapter
        : throw new KeyNotFoundException($"Color adapter '{kind}' is not registered.");
}
