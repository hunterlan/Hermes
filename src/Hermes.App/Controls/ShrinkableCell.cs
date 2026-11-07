using Avalonia;
using Avalonia.Controls;

namespace Hermes.App.Controls;

/// <summary>
/// Hosts the content of a table cell in a star column without letting it widen the column. A star column grows to
/// its content's desired width when the content is wider than the column's share, so a long tag next to a key would
/// push the other columns of that row out of line. This host reports no width of its own, and the content is
/// arranged in whatever width the column has (it should trim or clip itself).
/// </summary>
public class ShrinkableCell : Decorator
{
    public ShrinkableCell() => ClipToBounds = true;

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Child is null)
            return default;
        Child.Measure(availableSize);
        return double.IsInfinity(availableSize.Width)
            ? Child.DesiredSize
            : new Size(0, Child.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Child?.Arrange(new Rect(finalSize));
        return finalSize;
    }
}
