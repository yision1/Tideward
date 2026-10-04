using System;
using Vanara.PInvoke;

namespace Tideward.Helpers;

[Obsolete("不可用状态", true)]
public static class SystemUIHelper
{

        public static bool TransparencyEffectEnabled
    {
        get
        {
            User32.SystemParametersInfo(User32.SPI.SPI_GETDISABLEOVERLAPPEDCONTENT, out bool enabled);
            return enabled;
        }
        set => User32.SystemParametersInfo(User32.SPI.SPI_SETDISABLEOVERLAPPEDCONTENT, value);
    }

        public static bool AnimationEffectEnabled
    {
        get
        {
            User32.SystemParametersInfo(User32.SPI.SPI_GETCLIENTAREAANIMATION, out bool enabled);
            return enabled;
        }
        set => User32.SystemParametersInfo(User32.SPI.SPI_SETCLIENTAREAANIMATION, value);
    }

}
