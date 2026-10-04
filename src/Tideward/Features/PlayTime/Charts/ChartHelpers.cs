using Microsoft.UI.Xaml;
using System.Collections.Generic;

namespace Tideward.Features.PlayTime;

internal static class ChartHelpers
{

        public static T GetResource<T>(string key) where T : class
    {
        if (Application.Current.Resources.TryGetValue(key, out var value) && value is T t)
        {
            return t;
        }
        throw new KeyNotFoundException($"Theme resource '{key}' not found.");
    }

}
