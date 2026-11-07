using Hermes.App.ViewModels;
using Hermes.Core.Headers;

namespace Hermes.App.Tests;

public class SensitiveValueTests
{
    private const string Token = "Bearer eyJhbGciOiJIUzI1NiJ9.payload.signature";

    [Fact]
    public void A_sensitive_value_shows_dots_until_revealed()
    {
        var value = new SensitiveValueViewModel(Token, isSensitive: true, copyText: null);

        Assert.Equal(SensitiveHeaders.MaskText, value.DisplayValue);

        value.ToggleRevealCommand.Execute(null);

        Assert.Equal(Token, value.DisplayValue);
        Assert.Equal("Hide value", value.RevealToolTip);
    }

    [Fact]
    public void A_plain_value_is_never_masked()
    {
        var value = new SensitiveValueViewModel("application/json", isSensitive: false, copyText: null);

        Assert.Equal("application/json", value.DisplayValue);
        Assert.False(value.CanExpand);
    }

    [Fact]
    public void A_long_plain_value_can_be_expanded()
    {
        var value = new SensitiveValueViewModel(new string('a', 61), isSensitive: false, copyText: null);

        Assert.True(value.CanExpand);
    }

    [Fact]
    public void The_character_count_is_grouped()
    {
        var value = new SensitiveValueViewModel(new string('a', 1734), isSensitive: true, copyText: null);

        Assert.Equal("1,734 characters", value.CharacterCountText);
    }

    [Fact]
    public async Task Copy_puts_the_real_value_on_the_clipboard_while_masked_and_confirms_briefly()
    {
        string? copied = null;
        var value = new SensitiveValueViewModel(Token, isSensitive: true, text =>
        {
            copied = text;
            return Task.CompletedTask;
        })
        { CopiedDuration = TimeSpan.FromMilliseconds(50) };

        var copying = value.CopyCommand.ExecuteAsync(null);

        Assert.Equal(Token, copied);
        Assert.True(value.IsCopied);
        await copying;
        Assert.False(value.IsCopied);
        Assert.False(value.IsRevealed);
    }

    [Fact]
    public void Reveal_state_is_kept_for_a_rebuilt_row_until_cleared()
    {
        var state = new HeaderRevealState(copyText: null);
        state.CreateValue("own", "Authorization", Token, true).ToggleRevealCommand.Execute(null);

        var rebuilt = state.CreateValue("own", "authorization", Token, true);
        var otherSection = state.CreateValue("inherited", "Authorization", Token, true);

        Assert.True(rebuilt.IsRevealed);
        Assert.False(otherSection.IsRevealed);

        state.Clear();

        Assert.False(state.CreateValue("own", "Authorization", Token, true).IsRevealed);
    }

    [Fact]
    public void Hide_masks_the_value_and_closes_the_expanded_box()
    {
        var value = new SensitiveValueViewModel(Token, isSensitive: true, copyText: null) { IsRevealed = true, IsExpanded = true };

        value.HideValueCommand.Execute(null);

        Assert.True(value.ShowMask);
        Assert.False(value.IsExpanded);
    }
}
