using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ReCoupler
{
    internal static class ReCouplerSettings
    {
        public const float connectRadius_default = 0.1f;
        public const float connectAngle_default = 91;
        public const string configURL = "ReCoupler/ReCouplerSettings/ReCouplerSettings";

        public static float connectRadius = connectRadius_default;
        public static float connectAngle = connectAngle_default;
    }
}
