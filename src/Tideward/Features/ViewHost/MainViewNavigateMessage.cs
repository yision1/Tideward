using System;

namespace Tideward.Features.ViewHost;

internal class MainViewNavigateMessage
{

    public Type Page { get; set; }

    public MainViewNavigateMessage(Type page)
    {
        Page = page;
    }

}
