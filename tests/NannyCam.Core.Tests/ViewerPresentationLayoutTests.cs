using Xunit;

namespace NannyCam.Core.Tests;

public sealed class ViewerPresentationLayoutTests
{
    [Fact]
    public void CameraOnly_UsesTheCompactMinimumSize()
    {
        ViewerWindowSize minimum = ViewerPresentationLayout.GetMinimum(new ViewerPresentationState(false, true));

        Assert.Equal(new ViewerWindowSize(320, 180), minimum);
    }

    [Fact]
    public void NormalMode_UsesTheNormalMinimumSize()
    {
        ViewerWindowSize minimum = ViewerPresentationLayout.GetMinimum(ViewerPresentationState.Default);

        Assert.Equal(new ViewerWindowSize(800, 500), minimum);
    }

    [Fact]
    public void RestoreNormalMinimum_ExpandsASmallCompactWindow()
    {
        ViewerWindowSize restored = ViewerPresentationLayout.RestoreNormalMinimum(400, 225);

        Assert.Equal(new ViewerWindowSize(800, 500), restored);
    }

    [Fact]
    public void RestoreNormalMinimum_PreservesALargerWindow()
    {
        ViewerWindowSize restored = ViewerPresentationLayout.RestoreNormalMinimum(1100, 700);

        Assert.Equal(new ViewerWindowSize(1100, 700), restored);
    }
}
