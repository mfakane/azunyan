using Xunit;

namespace Azunote.Tests.Shell;

public sealed class WindowRegistryTests
{
    [Fact]
    public void Cycle_uses_creation_order_and_wraps_in_both_directions()
    {
        var registry = new WindowRegistry<WindowToken>();
        var first = new WindowToken();
        var second = new WindowToken();
        var third = new WindowToken();
        registry.Register(first);
        registry.Register(second);
        registry.Register(third);

        Assert.Same(second, registry.CycleFrom(first, direction: 1));
        Assert.Same(third, registry.CycleFrom(first, direction: -1));
        Assert.Same(first, registry.CycleFrom(third, direction: 1));
        Assert.Same(third, registry.CycleFrom(first, direction: -1));
    }

    [Fact]
    public void Unregistering_active_window_selects_the_next_surviving_window()
    {
        var registry = new WindowRegistry<WindowToken>();
        var first = new WindowToken();
        var second = new WindowToken();
        var third = new WindowToken();
        registry.Register(first);
        registry.Register(second);
        registry.Register(third);
        registry.MarkActive(second);

        Assert.True(registry.Unregister(second));
        Assert.Equal(new[] { first, third }, registry.Windows);
        Assert.Same(third, registry.Active);

        Assert.True(registry.Unregister(third));
        Assert.Same(first, registry.Active);
    }

    [Fact]
    public void Registering_the_same_window_twice_is_rejected()
    {
        var registry = new WindowRegistry<WindowToken>();
        var window = new WindowToken();
        registry.Register(window);

        Assert.Throws<InvalidOperationException>(() => registry.Register(window));
    }

    private sealed class WindowToken
    {
    }
}
