namespace Hermes.App.Controls;

/// <summary>What a child had set before a <see cref="DropPriorityPanel"/> dropped it, put back when it is shown again.</summary>
internal sealed record DroppedChildInputState(bool IsTabStop, bool IsHitTestVisible);
