namespace Wrapper.UI;

internal static class PlatformSelector
{
    public static ComboBox Create(
        PlatformCatalogLoad catalog,
        string? selectedKey,
        bool includeDefault,
        string defaultPlatform,
        TrayPalette palette)
    {
        var normalizedSelected = ApiConfig.NormalizePlatformOverride(selectedKey);
        var items = new List<PlatformSelectionItem>();
        if (includeDefault)
        {
            var defaultName = catalog.Platforms
                .FirstOrDefault(platform => string.Equals(
                    platform.Key,
                    defaultPlatform,
                    StringComparison.OrdinalIgnoreCase))?.Name ?? defaultPlatform;
            items.Add(new PlatformSelectionItem(null, $"Use default ({defaultName})"));
        }

        items.AddRange(catalog.Platforms.Select(platform =>
            new PlatformSelectionItem(platform.Key, platform.Name)));

        if (normalizedSelected is not null && !items.Any(item => string.Equals(
                item.Key,
                normalizedSelected,
                StringComparison.OrdinalIgnoreCase)))
        {
            items.Add(new PlatformSelectionItem(
                normalizedSelected,
                $"{normalizedSelected} (unavailable)"));
        }

        if (!includeDefault && items.Count == 0)
        {
            var fallback = ApiConfig.NormalizePlatform(defaultPlatform);
            items.Add(new PlatformSelectionItem(fallback, $"{fallback} (unavailable)"));
        }

        var selector = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = palette.Surface,
            ForeColor = palette.Text,
            AccessibleName = includeDefault ? "Game play Platform" : "Default play Platform"
        };
        selector.Items.AddRange([.. items.Cast<object>()]);
        selector.SelectedItem = items.FirstOrDefault(item => string.Equals(
            item.Key,
            normalizedSelected,
            StringComparison.OrdinalIgnoreCase)) ?? items[0];
        return selector;
    }

    public static string? SelectedKey(ComboBox selector) =>
        selector.SelectedItem is PlatformSelectionItem item
            ? ApiConfig.NormalizePlatformOverride(item.Key)
            : null;

    private sealed record PlatformSelectionItem(string? Key, string Label)
    {
        public override string ToString() => Label;
    }
}
