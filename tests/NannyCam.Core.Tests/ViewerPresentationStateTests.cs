using Xunit;

namespace NannyCam.Core.Tests;

public sealed class ViewerPresentationStateTests
{
    [Fact]
    public void DefaultState_HasNeitherPresentationOptionEnabled()
    {
        ViewerPresentationState state = ViewerPresentationState.Default;

        Assert.False(state.IsAlwaysOnTop);
        Assert.False(state.IsCameraOnly);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void PresentationOptions_AcceptEveryIndependentCombination(bool isAlwaysOnTop, bool isCameraOnly)
    {
        ViewerPresentationState state = ViewerPresentationState.Default
            .WithAlwaysOnTop(isAlwaysOnTop)
            .WithCameraOnly(isCameraOnly);

        Assert.Equal(isAlwaysOnTop, state.IsAlwaysOnTop);
        Assert.Equal(isCameraOnly, state.IsCameraOnly);
    }

    [Fact]
    public void ChangingOneOption_PreservesTheOther()
    {
        ViewerPresentationState state = new ViewerPresentationState(true, false);

        ViewerPresentationState cameraOnly = state.WithCameraOnly(true);
        ViewerPresentationState normal = cameraOnly.WithCameraOnly(false);

        Assert.True(cameraOnly.IsAlwaysOnTop);
        Assert.True(cameraOnly.IsCameraOnly);
        Assert.True(normal.IsAlwaysOnTop);
        Assert.False(normal.IsCameraOnly);
    }
}
